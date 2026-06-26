using System.Collections.Generic;
using Basis.Scripts.Networking.Sync;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[CustomEditor(typeof(BasisSyncedRigidbody))]
[CanEditMultipleObjects]
public class BasisSyncedRigidbodyInspector : BasisDocInspector_UI
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();

        root.Add(BasisSyncInspectorUI.Header(
            "Basis Synced Rigidbody",
            "The owner streams the body's world pose plus linear / angular velocity; every other client drives its copy. Remote bodies are made kinematic by default so physics never fights the synced pose. Call TakeOwnership() (e.g. on grab) to become the authority."));

        root.Add(BasisSyncInspectorUI.ValidationContainer(Validate));

        VisualElement body = BasisSyncInspectorUI.Card("Body");
        body.Add(new PropertyField(serializedObject.FindProperty("Body")));
        body.Add(new PropertyField(serializedObject.FindProperty("RelativeTo")));
        var relHint = new Label("RelativeTo: encode position in that transform's space so a tight Ranged window can cover a large world (both clients need the same reference).");
        relHint.style.whiteSpace = WhiteSpace.Normal;
        relHint.style.fontSize = 10;
        relHint.style.opacity = 0.8f;
        body.Add(relHint);
        root.Add(body);

        VisualElement pos = BasisSyncInspectorUI.Card("Position");
        pos.Add(new Toggle("Sync Position") { bindingPath = "SyncPosition" });
        pos.Add(new Toggle("Interpolate") { bindingPath = "InterpolatePosition" });
        root.Add(pos);

        root.Add(RotationGroup());

        root.Add(ScaleGroup());

        VisualElement vel = BasisSyncInspectorUI.Card("Velocity");
        vel.Add(new Toggle("Sync Linear Velocity") { bindingPath = "SyncLinearVelocity" });
        vel.Add(new Toggle("Sync Angular Velocity") { bindingPath = "SyncAngularVelocity" });
        root.Add(vel);

        VisualElement remote = BasisSyncInspectorUI.Card("Remote");
        remote.Add(new Toggle("Drive Remote Kinematic") { bindingPath = "DriveRemoteKinematic" });
        var remoteHint = new Label("On = remote bodies are kinematic, driven straight to the synced pose. Off = velocity-assisted extrapolation — the synced velocity carries a dynamic remote between updates, with a soft correction toward the synced pose.");
        remoteHint.style.whiteSpace = WhiteSpace.Normal;
        remoteHint.style.fontSize = 10;
        remoteHint.style.opacity = 0.8f;
        remote.Add(remoteHint);
        root.Add(remote);

        root.Add(BuildCompression());

        root.Add(BasisSyncInspectorUI.SmoothingCard(serializedObject));
        root.Add(BasisSyncInspectorUI.NetworkingCard(serializedObject));

        root.Add(BasisSyncInspectorUI.WireSizeCard(FillWireSize));

        root.Bind(serializedObject);

        root.Add(BasisSyncInspectorUI.NetworkInfo(serializedObject.targetObject));

        var api = CreateApiReferenceFoldout();
        if (api != null) root.Add(api);
        return root;
    }

    protected virtual List<BasisSyncIssue> Validate()
    {
        var issues = new List<BasisSyncIssue>();
        var t = target as BasisSyncedRigidbody;
        if (t == null) return issues;

        if (t.Body == null && t.GetComponent<UnityEngine.Rigidbody>() == null)
            issues.Add(BasisSyncIssue.Error("No Rigidbody assigned or on this object — nothing will be synced."));

        if (!(t.SyncPosition || t.SyncRotation || t.SyncScale || t.SyncLinearVelocity || t.SyncAngularVelocity))
            issues.Add(BasisSyncIssue.Warning("Nothing is being synced — enable Position, Rotation, Scale, or Velocity."));

        return issues;
    }

    private VisualElement RotationGroup()
    {
        VisualElement card = BasisSyncInspectorUI.Card("Rotation");

        var sync = new Toggle("Sync Rotation") { bindingPath = "SyncRotation" };
        var mode = new PropertyField(serializedObject.FindProperty("RotationMode"), "Mode");
        var bits = new SliderInt("Bits / component", 6, 16) { showInputField = true, bindingPath = "SmallestThreeBits" };
        var interp = new Toggle("Interpolate") { bindingPath = "InterpolateRotation" };

        void UpdateEnabled()
        {
            bool on = serializedObject.FindProperty("SyncRotation").boolValue;
            int modeIdx = serializedObject.FindProperty("RotationMode").enumValueIndex;
            mode.SetEnabled(on);
            bits.style.display = (on && modeIdx == (int)BasisRotationSyncMode.SmallestThree) ? DisplayStyle.Flex : DisplayStyle.None;
        }
        UpdateEnabled();
        sync.RegisterValueChangedCallback(evt => UpdateEnabled());
        mode.RegisterValueChangeCallback(evt => UpdateEnabled());

        card.Add(sync);
        card.Add(mode);
        card.Add(bits);
        card.Add(interp);
        return card;
    }

    private VisualElement ScaleGroup()
    {
        VisualElement card = BasisSyncInspectorUI.Card("Scale");

        var sync = new Toggle("Sync Scale") { bindingPath = "SyncScale" };
        var uniform = new Toggle("Uniform (one value → XYZ)") { bindingPath = "ScaleUniform" };
        VisualElement row = AxisRow("ScaleX", "ScaleY", "ScaleZ");

        void UpdateEnabled()
        {
            bool on = serializedObject.FindProperty("SyncScale").boolValue;
            bool uni = serializedObject.FindProperty("ScaleUniform").boolValue;
            uniform.SetEnabled(on);
            row.SetEnabled(on && !uni);
        }
        UpdateEnabled();
        sync.RegisterValueChangedCallback(evt => UpdateEnabled());
        uniform.RegisterValueChangedCallback(evt => UpdateEnabled());

        card.Add(sync);
        card.Add(uniform);
        card.Add(row);
        card.Add(new Toggle("Interpolate") { bindingPath = "InterpolateScale" });
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

    private VisualElement BuildCompression()
    {
        var foldout = new Foldout { text = "Compression (per axis)", value = false };
        foldout.style.marginBottom = 8;
        var lbl = foldout.Q<Label>();
        if (lbl != null)
        {
            lbl.style.color = new UnityEngine.UIElements.StyleColor(BasisSyncInspectorUI.Accent);
            lbl.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
        }

        var note = new Label("Position is per-axis; velocity uses one spec across X/Y/Z. Half (16-bit) is a good velocity default.");
        note.style.whiteSpace = WhiteSpace.Normal;
        note.style.fontSize = 10;
        note.style.opacity = 0.8f;
        note.style.marginBottom = 4;
        foldout.Add(note);

        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("PosCompX"), "Pos X"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("PosCompY"), "Pos Y"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("PosCompZ"), "Pos Z"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("LinearVelocityComp"), "Lin Vel"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("AngularVelocityComp"), "Ang Vel"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("ScaleCompX"), "Scale X"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("ScaleCompY"), "Scale Y"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("ScaleCompZ"), "Scale Z"));
        return foldout;
    }

    private void FillWireSize(BasisWireSizeModel model)
    {
        var t = target as BasisSyncedRigidbody;
        if (t == null) return;
        model.Checksum = t.UseChecksum;
        model.SendIntervalSeconds = t.SendIntervalSeconds;
        model.KeyframeIntervalSeconds = t.KeyframeIntervalSeconds;

        int posBits = 0, posFields = 0;
        if (t.SyncPosition)
        {
            posBits = t.PosCompX.ToSpec().BitCount + t.PosCompY.ToSpec().BitCount + t.PosCompZ.ToSpec().BitCount;
            posFields = 3;
        }
        model.Add("Position", posBits, posFields, "3 axes");

        int rotBits = 0, rotFields = 0;
        string rotDetail = "";
        if (t.SyncRotation)
        {
            switch (t.RotationMode)
            {
                case BasisRotationSyncMode.QuaternionHalf:
                    rotBits = 64; rotFields = 4; rotDetail = "quaternion ×4 half";
                    break;
                case BasisRotationSyncMode.QuaternionRaw:
                    rotBits = 128; rotFields = 4; rotDetail = "quaternion ×4 raw";
                    break;
                case BasisRotationSyncMode.Euler:
                    rotBits = 96; rotFields = 3; rotDetail = "3 axes (Euler raw)";
                    break;
                default:
                {
                    int magBits = UnityEngine.Mathf.Clamp(t.SmallestThreeBits, 2, 18);
                    rotBits = 2 + 3 * (1 + magBits);
                    rotFields = 1;
                    rotDetail = $"smallest-three {magBits}b";
                    break;
                }
            }
        }
        model.Add("Rotation", rotBits, rotFields, rotDetail);

        if (t.SyncLinearVelocity)
        {
            int b = t.LinearVelocityComp.ToSpec().BitCount;
            model.Add("Linear Vel", b * 3, 3, "3 axes");
        }
        else model.Add("Linear Vel", 0, 0, "");

        if (t.SyncAngularVelocity)
        {
            int b = t.AngularVelocityComp.ToSpec().BitCount;
            model.Add("Angular Vel", b * 3, 3, "3 axes");
        }
        else model.Add("Angular Vel", 0, 0, "");

        int scaleBits = 0, scaleFields = 0;
        string scaleDetail = "";
        if (t.SyncScale)
        {
            if (t.ScaleUniform)
            {
                scaleBits = t.ScaleCompX.ToSpec().BitCount;
                scaleFields = 1;
                scaleDetail = "uniform";
            }
            else
            {
                if (t.ScaleX) { scaleBits += t.ScaleCompX.ToSpec().BitCount; scaleFields++; }
                if (t.ScaleY) { scaleBits += t.ScaleCompY.ToSpec().BitCount; scaleFields++; }
                if (t.ScaleZ) { scaleBits += t.ScaleCompZ.ToSpec().BitCount; scaleFields++; }
                scaleDetail = scaleFields == 1 ? "1 axis" : scaleFields + " axes";
            }
        }
        model.Add("Scale", scaleBits, scaleFields, scaleDetail);
    }
}
