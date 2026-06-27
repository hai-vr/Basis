using System.Collections.Generic;
using Basis.Scripts.Networking.Sync;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisSyncedParameters))]
[CanEditMultipleObjects]
public class BasisSyncedParametersInspector : BasisDocInspector_UI
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();

        root.Add(BasisSyncInspectorUI.Header(
            "Basis Synced Parameters",
            "Build a list of named parameters, each with per-axis compression. On the owner call Set*(name, value); on every other client Get*(name) returns the interpolated value. Owner-authoritative (TakeOwnership)."));

        root.Add(BasisSyncInspectorUI.ValidationContainer(Validate));

        VisualElement card = BasisSyncInspectorUI.Card("Parameters");
        var list = new VisualElement();
        card.Add(list);

        var add = new Button(() =>
        {
            SerializedProperty prop = serializedObject.FindProperty("Parameters");
            serializedObject.Update();
            prop.arraySize++;
            SerializedProperty added = prop.GetArrayElementAtIndex(prop.arraySize - 1);
            added.FindPropertyRelative("Name").stringValue = "Parameter" + prop.arraySize;
            serializedObject.ApplyModifiedProperties();
            RebuildList(list);
        })
        { text = "+ Add Parameter", tooltip = "Add a new named, synced value. Reference it by name from Set*/Get* in code." };
        add.style.marginTop = 4;
        card.Add(add);
        root.Add(card);

        RebuildList(list);

        root.Add(BasisSyncInspectorUI.NetworkingCard(serializedObject));
        root.Add(BasisSyncInspectorUI.SmoothingCard(serializedObject));

        root.Bind(serializedObject);

        root.Add(BasisSyncInspectorUI.NetworkInfo(serializedObject.targetObject));

        var api = CreateApiReferenceFoldout();
        if (api != null) root.Add(api);
        return root;
    }

    private void RebuildList(VisualElement list)
    {
        list.Clear();
        SerializedProperty prop = serializedObject.FindProperty("Parameters");
        if (prop == null) return;

        for (int i = 0; i < prop.arraySize; i++)
        {
            list.Add(BuildElement(prop, i, list));
        }

        if (prop.arraySize == 0)
        {
            var empty = new Label("No parameters yet. Use “+ Add Parameter”.");
            empty.style.unityFontStyleAndWeight = FontStyle.Italic;
            empty.style.opacity = 0.7f;
            list.Add(empty);
        }
    }

    private VisualElement BuildElement(SerializedProperty listProp, int index, VisualElement list)
    {
        SerializedProperty element = listProp.GetArrayElementAtIndex(index);
        SerializedProperty nameProp = element.FindPropertyRelative("Name");
        SerializedProperty typeProp = element.FindPropertyRelative("Type");
        SerializedProperty interpProp = element.FindPropertyRelative("Interpolate");

        var foldout = new Foldout { value = true };
        foldout.style.marginBottom = 6;
        foldout.style.borderLeftWidth = 2;
        foldout.style.borderLeftColor = new StyleColor(BasisSyncInspectorUI.Accent);
        foldout.style.paddingLeft = 4;

        foldout.Add(BasisSyncInspectorUI.Described(new PropertyField(nameProp),
            "Identifier you pass to Set*/Get* in code. Must be unique within this component."));
        foldout.Add(BasisSyncInspectorUI.Described(new PropertyField(typeProp),
            "Value type — sets how many components are sent and whether it interpolates (continuous) or snaps (discrete)."));
        var interpField = BasisSyncInspectorUI.Described(new PropertyField(interpProp),
            "Smooth this parameter between received updates (continuous types only).");
        foldout.Add(interpField);

        var axisCard = BasisSyncInspectorUI.Card("Compression (per axis)", false);
        VisualElement[] axes =
        {
            BasisSyncInspectorUI.CompressionAxisRow(serializedObject, element.FindPropertyRelative("AxisX"), "X"),
            BasisSyncInspectorUI.CompressionAxisRow(serializedObject, element.FindPropertyRelative("AxisY"), "Y"),
            BasisSyncInspectorUI.CompressionAxisRow(serializedObject, element.FindPropertyRelative("AxisZ"), "Z"),
            BasisSyncInspectorUI.CompressionAxisRow(serializedObject, element.FindPropertyRelative("AxisW"), "W"),
        };
        foreach (VisualElement a in axes) axisCard.Add(a);
        foldout.Add(axisCard);

        var remove = new Button(() =>
        {
            serializedObject.Update();
            listProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RebuildList(list);
        })
        { text = "Remove", tooltip = "Delete this parameter." };
        remove.style.alignSelf = Align.FlexEnd;
        foldout.Add(remove);

        foldout.Bind(serializedObject);

        void Refresh()
        {
            if (typeProp == null) return;
            serializedObject.Update();
            var type = (BasisSyncedParameterType)typeProp.enumValueIndex;
            int comps = BasisSyncedParameters.ComponentCount(type);
            bool discrete = typeProp.enumValueIndex >= (int)BasisSyncedParameterType.Int;

            string name = string.IsNullOrEmpty(nameProp.stringValue) ? "(unnamed)" : nameProp.stringValue;
            foldout.text = $"{name}  ·  {type}";

            interpField.style.display = discrete ? DisplayStyle.None : DisplayStyle.Flex;
            axisCard.style.display = comps > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            for (int a = 0; a < axes.Length; a++)
                axes[a].style.display = a < comps ? DisplayStyle.Flex : DisplayStyle.None;
        }

        Refresh();
        foldout.schedule.Execute(Refresh).Every(300);
        return foldout;
    }

    private List<BasisSyncIssue> Validate()
    {
        var issues = new List<BasisSyncIssue>();
        var c = target as BasisSyncedParameters;
        if (c == null || c.Parameters == null) return issues;

        var seen = new HashSet<string>();
        int empty = 0;
        foreach (BasisSyncedParameter p in c.Parameters)
        {
            if (p == null) continue;
            if (string.IsNullOrWhiteSpace(p.Name)) { empty++; continue; }
            if (!seen.Add(p.Name))
                issues.Add(BasisSyncIssue.Error($"Duplicate parameter name '{p.Name}' — only the first will be registered."));
        }
        if (empty > 0) issues.Add(BasisSyncIssue.Warning($"{empty} parameter(s) have no name and will be skipped."));
        if (c.Parameters.Count == 0) issues.Add(BasisSyncIssue.Warning("No parameters defined yet — add one below."));
        return issues;
    }
}
