using System.Collections.Generic;
using Basis.Scripts.BasisSdk.Interactions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisPickupSyncNetworking))]
[CanEditMultipleObjects]
public class BasisPickupSyncNetworkingInspector : BasisSyncedTransformInspector
{
    public override VisualElement CreateInspectorGUI()
    {
        // Reuse the full BasisSyncedTransform UI (axis toggles, precision, smoothing, networking).
        VisualElement root = base.CreateInspectorGUI();

        // Swap the generic header for a pickup-specific one.
        if (root.childCount > 0) root.RemoveAt(0);
        root.Insert(0, BasisSyncInspectorUI.Header(
            "Basis Pickup Sync",
            "Networked grabbable. Grabbing takes ownership; the owner streams the transform and every other client interpolates. 'Static' freezes it for everyone (no grab, kinematic)."));

        VisualElement pickup = BasisSyncInspectorUI.Card("Pickup");
        pickup.Add(new PropertyField(serializedObject.FindProperty("BasisPickupInteractable")));
        pickup.Add(new PropertyField(serializedObject.FindProperty("CanNetworkSteal")));
        pickup.Add(new PropertyField(serializedObject.FindProperty("IsStatic")));
        root.Insert(1, pickup);
        pickup.Bind(serializedObject);

        return root;
    }

    protected override List<BasisSyncIssue> Validate()
    {
        var issues = base.Validate();
        var p = target as BasisPickupSyncNetworking;
        if (p != null && p.BasisPickupInteractable == null
            && p.GetComponentInChildren<BasisPickupInteractable>(true) == null)
        {
            issues.Add(BasisSyncIssue.Warning("No BasisPickupInteractable found in children — this prop can't be grabbed."));
        }
        return issues;
    }
}
