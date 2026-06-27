using System.Collections.Generic;
using Basis.Scripts.BasisSdk.Interactions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisSeatSync))]
[CanEditMultipleObjects]
public class BasisSeatSyncInspector : BasisDocInspector_UI
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();

        root.Add(BasisSyncInspectorUI.Header(
            "Basis Seat Sync",
            "Reliable seat-occupancy sync. Sitting broadcasts the occupant and drives that remote player into the seat; only the occupant can stand. Discrete + reliable — separate from the interpolated value-sync system."));

        root.Add(BasisSyncInspectorUI.ValidationContainer(ValidateSeat));

        VisualElement seat = BasisSyncInspectorUI.Card("Seat");
        seat.Add(BasisSyncInspectorUI.Described(new PropertyField(serializedObject.FindProperty("Seat")),
            "The BasisSeat whose occupancy is synced. Auto-found on this GameObject if left empty."));
        root.Add(seat);

        root.Bind(serializedObject);

        root.Add(BasisSyncInspectorUI.NetworkInfo(serializedObject.targetObject));

        var api = CreateApiReferenceFoldout();
        if (api != null) root.Add(api);
        return root;
    }

    private List<BasisSyncIssue> ValidateSeat()
    {
        var issues = new List<BasisSyncIssue>();
        var s = target as BasisSeatSync;
        if (s == null) return issues;
        if (s.Seat == null && s.GetComponent<BasisSeat>() == null)
            issues.Add(BasisSyncIssue.Error("No BasisSeat assigned or found on this GameObject."));
        return issues;
    }
}
