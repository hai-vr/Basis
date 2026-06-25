using System.Collections.Generic;
using Basis.Scripts.Networking.Sync;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisSyncedTransform))]
[CanEditMultipleObjects]
public class BasisSyncedTransformInspector : BasisDocInspector_UI
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();

        root.Add(BasisSyncInspectorUI.Header(
            "Basis Synced Transform",
            "The owner streams the enabled axes; every other client's copy is interpolated and composed. Call TakeOwnership() (e.g. on grab) to become the authority."));

        root.Add(BasisSyncInspectorUI.ValidationContainer(Validate));

        VisualElement target = BasisSyncInspectorUI.Card("Target");
        target.Add(new PropertyField(serializedObject.FindProperty("Target")));
        root.Add(target);

        root.Add(AxisGroup("Position", "SyncPosition", "PositionX", "PositionY", "PositionZ"));
        root.Add(AxisGroup("Rotation", "SyncRotation", "RotationX", "RotationY", "RotationZ"));
        root.Add(AxisGroup("Scale", "SyncScale", "ScaleX", "ScaleY", "ScaleZ"));

        VisualElement space = BasisSyncInspectorUI.Card("Space & Precision");
        space.Add(new PropertyField(serializedObject.FindProperty("WorldSpace")));
        space.Add(new PropertyField(serializedObject.FindProperty("HalfPrecision")));
        root.Add(space);

        root.Add(BasisSyncInspectorUI.SmoothingCard(serializedObject));
        root.Add(BasisSyncInspectorUI.NetworkingCard(serializedObject));

        root.Bind(serializedObject);

        var api = CreateApiReferenceFoldout();
        if (api != null) root.Add(api);
        return root;
    }

    protected virtual List<BasisSyncIssue> Validate()
    {
        var issues = new List<BasisSyncIssue>();
        var t = target as BasisSyncedTransform;
        if (t == null) return issues;

        if (t.Target == null)
            issues.Add(BasisSyncIssue.Error("Target is not assigned — nothing will be synced."));

        bool posAxes = t.PositionX || t.PositionY || t.PositionZ;
        bool rotAxes = t.RotationX || t.RotationY || t.RotationZ;
        bool scaleAxes = t.ScaleX || t.ScaleY || t.ScaleZ;

        if (t.SyncPosition && !posAxes)
            issues.Add(BasisSyncIssue.Warning("Sync Position is on but all X/Y/Z axes are off — position won't sync."));
        if (t.SyncRotation && !rotAxes)
            issues.Add(BasisSyncIssue.Warning("Sync Rotation is on but all X/Y/Z axes are off — rotation won't sync."));
        if (t.SyncScale && !scaleAxes)
            issues.Add(BasisSyncIssue.Warning("Sync Scale is on but all X/Y/Z axes are off — scale won't sync."));

        if (!((t.SyncPosition && posAxes) || (t.SyncRotation && rotAxes) || (t.SyncScale && scaleAxes)))
            issues.Add(BasisSyncIssue.Warning("Nothing is being synced — enable Position, Rotation, or Scale with at least one axis."));

        return issues;
    }

    private VisualElement AxisGroup(string title, string syncProp, string x, string y, string z)
    {
        VisualElement card = BasisSyncInspectorUI.Card(title);

        var sync = new Toggle("Sync " + title) { bindingPath = syncProp };
        VisualElement row = AxisRow(x, y, z);
        row.SetEnabled(serializedObject.FindProperty(syncProp).boolValue);
        sync.RegisterValueChangedCallback(evt => row.SetEnabled(evt.newValue));

        card.Add(sync);
        card.Add(row);
        return card;
    }

    private VisualElement AxisRow(string x, string y, string z)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.marginTop = 2;
        row.style.marginLeft = 4;
        row.Add(AxisToggle("X", x));
        row.Add(AxisToggle("Y", y));
        row.Add(AxisToggle("Z", z));
        return row;
    }

    private static VisualElement AxisToggle(string label, string bindingPath)
    {
        var toggle = new Toggle(label) { bindingPath = bindingPath };
        toggle.style.marginRight = 14;
        var labelEl = toggle.Q<Label>();
        if (labelEl != null)
        {
            labelEl.style.minWidth = 0;
            labelEl.style.marginRight = 4;
        }
        return toggle;
    }
}
