using System;
using System.Collections.Generic;
using Basis.Scripts.Behaviour;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Partial extension of BasisAvatarSDKInspector that populates the
/// "Network Behaviours" UXML foldout with discovered BasisAvatarMonoBehaviour types.
/// Shows attached components with Select/Remove, and available types with Add buttons.
/// Adding a component calls OnEditorSetup for auto-configuration.
/// </summary>
public partial class BasisAvatarSDKInspector
{
    private VisualElement _attachedContainer;
    private VisualElement _availableContainer;

    /// <summary>
    /// Queries the UXML containers and populates them with discovered behaviours.
    /// Call from CreateInspectorGUI after the UXML tree is cloned.
    /// </summary>
    public void SetupNetworkBehaviours()
    {
        _attachedContainer = uiElementsRoot.Q<VisualElement>(BasisSDKConstants.NetworkBehavioursAttached);
        _availableContainer = uiElementsRoot.Q<VisualElement>(BasisSDKConstants.NetworkBehavioursAvailable);

        if (_attachedContainer == null || _availableContainer == null)
        {
            Debug.LogError("Network Behaviours UXML containers not found.");
            return;
        }

        RefreshNetworkBehaviours();
    }

    private void RefreshNetworkBehaviours()
    {
        _attachedContainer.Clear();
        _availableContainer.Clear();

        // ---- Discover all concrete subclasses ----
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

        // ---- Attached components ----
        var existing = Avatar.GetComponentsInChildren<BasisAvatarMonoBehaviour>(true);
        var existingTypes = new HashSet<Type>();

        foreach (var comp in existing)
        {
            existingTypes.Add(comp.GetType());

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
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
                RefreshNetworkBehaviours();
            });
            removeBtn.text = "Remove";
            removeBtn.style.width = 60;
            row.Add(removeBtn);

            _attachedContainer.Add(row);
        }

        if (existing.Length == 0)
        {
            var noneLabel = new Label("None attached");
            noneLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            _attachedContainer.Add(noneLabel);
        }

        // ---- Available to add ----
        foreach (var type in availableTypes)
        {
            if (existingTypes.Contains(type))
                continue;

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
                RefreshNetworkBehaviours();
            });
            addBtn.text = "Add " + type.Name;
            addBtn.style.marginBottom = 2;
            _availableContainer.Add(addBtn);
        }

        if (_availableContainer.childCount == 0)
        {
            var allLabel = new Label("All available behaviours are attached");
            allLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            _availableContainer.Add(allLabel);
        }
    }
}
