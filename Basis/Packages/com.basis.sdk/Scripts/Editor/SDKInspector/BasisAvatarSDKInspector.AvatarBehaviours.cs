using System;
using System.Collections.Generic;
using Basis.Scripts.Behaviour;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Partial extension of BasisAvatarSDKInspector that adds a section for discovering,
/// adding, and pre-configuring BasisAvatarMonoBehaviour components on the avatar.
/// </summary>
public partial class BasisAvatarSDKInspector
{
    private VisualElement _behavioursContainer;

    /// <summary>
    /// Call from CreateInspectorGUI after the main inspector is built.
    /// Adds the "Avatar Network Behaviours" foldout to the root.
    /// </summary>
    public void BuildAvatarBehavioursSection()
    {
        _behavioursContainer = new VisualElement();
        rootElement.Add(_behavioursContainer);
        RefreshBehavioursContent();
    }

    private void RefreshBehavioursContent()
    {
        _behavioursContainer.Clear();

        var foldout = new Foldout
        {
            text = "Avatar Network Behaviours",
            value = true
        };
        foldout.style.marginTop = 10;

        // ---- Discover all concrete subclasses of BasisAvatarMonoBehaviour ----
        var availableTypes = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!type.IsAbstract && type.IsSubclassOf(typeof(BasisAvatarMonoBehaviour)))
                    availableTypes.Add(type);
            }
        }

        // ---- Show components already attached ----
        var existing = Avatar.GetComponentsInChildren<BasisAvatarMonoBehaviour>(true);
        var existingTypes = new HashSet<Type>();

        if (existing.Length > 0)
        {
            var attachedHeader = new Label("Attached");
            attachedHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            attachedHeader.style.marginTop = 4;
            attachedHeader.style.marginBottom = 2;
            foldout.Add(attachedHeader);

            foreach (var comp in existing)
            {
                existingTypes.Add(comp.GetType());

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginLeft = 4;
                row.style.marginBottom = 2;

                var label = new Label(comp.GetType().Name);
                label.style.flexGrow = 1;
                row.Add(label);

                var selectBtn = new Button(() =>
                {
                    Selection.activeGameObject = comp.gameObject;
                    EditorGUIUtility.PingObject(comp);
                });
                selectBtn.text = "Select";
                selectBtn.style.width = 50;
                row.Add(selectBtn);

                var capturedComp = comp;
                var removeBtn = new Button(() =>
                {
                    Undo.DestroyObjectImmediate(capturedComp);
                    EditorUtility.SetDirty(Avatar);
                    RefreshBehavioursContent();
                });
                removeBtn.text = "Remove";
                removeBtn.style.width = 60;
                row.Add(removeBtn);

                foldout.Add(row);
            }
        }

        // ---- Show types available to add ----
        var addableTypes = new List<Type>();
        foreach (var type in availableTypes)
        {
            if (!existingTypes.Contains(type))
                addableTypes.Add(type);
        }

        if (addableTypes.Count > 0 || existing.Length == 0)
        {
            var addHeader = new Label(existing.Length > 0 ? "Available" : "No behaviours attached. Available");
            addHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            addHeader.style.marginTop = existing.Length > 0 ? 8 : 4;
            addHeader.style.marginBottom = 2;
            foldout.Add(addHeader);
        }

        foreach (var type in addableTypes)
        {
            var capturedType = type;
            var addBtn = new Button(() =>
            {
                var comp = Undo.AddComponent(Avatar.gameObject, capturedType);

#if UNITY_EDITOR
                if (comp is BasisAvatarMonoBehaviour basisComp)
                {
                    basisComp.OnEditorSetup(Avatar.gameObject);
                }
#endif

                EditorUtility.SetDirty(Avatar);
                RefreshBehavioursContent();
            });
            addBtn.text = $"Add {type.Name}";
            addBtn.style.marginLeft = 4;
            addBtn.style.marginBottom = 2;
            foldout.Add(addBtn);
        }

        // ---- Info label if nothing available ----
        if (addableTypes.Count == 0 && existing.Length > 0)
        {
            var allAttachedLabel = new Label("All available behaviours are attached.");
            allAttachedLabel.style.marginLeft = 4;
            allAttachedLabel.style.marginTop = 4;
            allAttachedLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            foldout.Add(allAttachedLabel);
        }

        _behavioursContainer.Add(foldout);
    }
}
