using System.Collections.Generic;
using Basis.Network.Vehicles;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisNetworkedVehicle))]
[CanEditMultipleObjects]
public class BasisNetworkedVehicleInspector : BasisDocInspector_UI
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();

        root.Add(BasisSyncInspectorUI.Header(
            "Basis Networked Vehicle",
            "Drivable vehicle built on BasisSyncedTransform. The pilot takes ownership on sit and streams the body transform plus per-wheel spin, steer angle and engine revs; every other client interpolates. 'Static' locks it for everyone."));

        root.Add(BasisSyncInspectorUI.ValidationContainer(Validate));

        VisualElement core = BasisSyncInspectorUI.Card("Vehicle");
        core.Add(new PropertyField(serializedObject.FindProperty("BasisVehicleBody")));
        core.Add(new PropertyField(serializedObject.FindProperty("Seat")));
        core.Add(new PropertyField(serializedObject.FindProperty("SeatSync")));
        core.Add(new PropertyField(serializedObject.FindProperty("Rigidbody")));
        core.Add(new PropertyField(serializedObject.FindProperty("EngineAudio")));
        core.Add(new PropertyField(serializedObject.FindProperty("SteeringWheel")));
        root.Add(core);

        VisualElement wheels = BasisSyncInspectorUI.Card("Wheels & Visuals");
        wheels.Add(new PropertyField(serializedObject.FindProperty("Colliders")));
        wheels.Add(new PropertyField(serializedObject.FindProperty("Wheels")));
        wheels.Add(new PropertyField(serializedObject.FindProperty("SpinVisuals")));
        wheels.Add(new PropertyField(serializedObject.FindProperty("SteerVisuals")));
        wheels.Add(new PropertyField(serializedObject.FindProperty("SpinAxisLocal")));
        wheels.Add(new PropertyField(serializedObject.FindProperty("SteerAxisLocal")));
        wheels.Add(new PropertyField(serializedObject.FindProperty("SteerRangeDeg")));
        wheels.Add(new PropertyField(serializedObject.FindProperty("HalfPrecisionWheels")));
        root.Add(wheels);

        root.Add(BasisSyncInspectorUI.NetworkingCard(serializedObject));
        root.Add(BasisSyncInspectorUI.SmoothingCard(serializedObject));

        root.Bind(serializedObject);

        var api = CreateApiReferenceFoldout();
        if (api != null) root.Add(api);
        return root;
    }

    private List<BasisSyncIssue> Validate()
    {
        var issues = new List<BasisSyncIssue>();
        var v = target as BasisNetworkedVehicle;
        if (v == null) return issues;

        if (v.BasisVehicleBody == null)
            issues.Add(BasisSyncIssue.Error("No BasisVehicleBody assigned — physics and the static / lock state won't drive."));
        if (v.Seat == null)
            issues.Add(BasisSyncIssue.Error("No pilot Seat assigned — nobody can take ownership and drive."));
        if (v.Rigidbody == null)
            issues.Add(BasisSyncIssue.Warning("No Rigidbody assigned."));
        if (v.SeatSync == null)
            issues.Add(BasisSyncIssue.Warning("No BasisSeatSync assigned — ownership won't follow the pilot into the seat."));
        if (v.Wheels == null || v.Wheels.Length == 0)
            issues.Add(BasisSyncIssue.Warning("No Wheels assigned."));
        if (v.SpinVisuals == null || v.SpinVisuals.Length == 0)
            issues.Add(BasisSyncIssue.Warning("No SpinVisuals assigned — remote clients won't see the wheels spin."));
        return issues;
    }
}
