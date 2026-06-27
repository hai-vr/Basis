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
            "Networked grabbable. Grabbing takes ownership; the owner streams the transform and every other client interpolates. 'Static' freezes it for everyone (no grab, kinematic). 'Attach To Hand While Held' streams the hand-relative grab offset instead of the world position while grabbed, so the prop stays glued to the holder's hand bone on every client."));

        VisualElement pickup = BasisSyncInspectorUI.Card("Pickup");
        pickup.Add(BasisSyncInspectorUI.Described(new PropertyField(serializedObject.FindProperty("BasisPickupInteractable")),
            "The grabbable this networks. Auto-found in children if left empty."));
        pickup.Add(BasisSyncInspectorUI.Described(new PropertyField(serializedObject.FindProperty("CanNetworkSteal")),
            "Allow another player to grab this away from the current owner."));
        pickup.Add(BasisSyncInspectorUI.Described(new PropertyField(serializedObject.FindProperty("IsStatic")),
            "Server-locked: nobody can grab it and it's frozen kinematic in place."));
        pickup.Add(BasisSyncInspectorUI.Described(new PropertyField(serializedObject.FindProperty("RemoteDeadReckon"), "Remote Dead-Reckon (velocity)"),
            "Stream velocity and let free-flying remote copies simulate from it (prediction) with the synced pose correcting — instead of being kinematic and pose-driven."));
        pickup.Add(BasisSyncInspectorUI.Described(new PropertyField(serializedObject.FindProperty("AttachToHandOnGrab"), "Attach To Hand While Held"),
            "While held, stream which hand + the hand-relative grab offset instead of the world position, so the prop stays glued to the holder's hand bone on every client with no interpolation lag. Snaps back to position sync on release."));
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
