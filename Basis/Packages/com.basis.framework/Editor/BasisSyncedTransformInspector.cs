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
        target.Add(BasisSyncInspectorUI.Described(new PropertyField(serializedObject.FindProperty("Target")),
            "Transform that is synced. Defaults to this GameObject."));
        root.Add(target);

        root.Add(AxisGroup("Position", "SyncPosition", "InterpolatePosition", "PositionX", "PositionY", "PositionZ"));
        root.Add(RotationGroup());
        root.Add(ScaleGroup());

        VisualElement space = BasisSyncInspectorUI.Card("Space", false);
        space.Add(BasisSyncInspectorUI.Described(new PropertyField(serializedObject.FindProperty("WorldSpace")),
            "Sync world-space pose instead of local (relative to the parent)."));
        space.Add(BasisSyncInspectorUI.Described(new PropertyField(serializedObject.FindProperty("RelativeTo")),
            "Encode position in this transform's space so a tight Ranged window can cover a large world. Both clients need the same reference."));
        var relHint = new Label("RelativeTo: encode position in that transform's space so a tight Ranged window covers a large world (both clients need the same reference).");
        relHint.style.whiteSpace = WhiteSpace.Normal;
        relHint.style.fontSize = 10;
        relHint.style.opacity = 0.8f;
        space.Add(relHint);
        root.Add(space);

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
        var t = target as BasisSyncedTransform;
        if (t == null) return issues;

        if (t.Target == null)
            issues.Add(BasisSyncIssue.Error("Target is not assigned — nothing will be synced."));

        bool posAxes = t.PositionX || t.PositionY || t.PositionZ;
        bool rotAxes = t.RotationX || t.RotationY || t.RotationZ;
        bool scaleAxes = t.ScaleX || t.ScaleY || t.ScaleZ;
        bool rotEuler = t.RotationMode == BasisRotationSyncMode.Euler;

        bool posActive = t.SyncPosition && posAxes;
        bool rotActive = t.SyncRotation && (!rotEuler || rotAxes);
        bool scaleActive = t.SyncScale && (t.ScaleUniform || scaleAxes);

        if (t.SyncPosition && !posAxes)
            issues.Add(BasisSyncIssue.Warning("Sync Position is on but all X/Y/Z axes are off — position won't sync."));
        if (t.SyncRotation && rotEuler && !rotAxes)
            issues.Add(BasisSyncIssue.Warning("Sync Rotation (Euler) is on but all X/Y/Z axes are off — rotation won't sync."));
        if (t.SyncScale && !t.ScaleUniform && !scaleAxes)
            issues.Add(BasisSyncIssue.Warning("Sync Scale is on but all X/Y/Z axes are off — scale won't sync."));

        if (!(posActive || rotActive || scaleActive))
            issues.Add(BasisSyncIssue.Warning("Nothing is being synced — enable Position, Rotation, or Scale with at least one axis."));

        return issues;
    }

    private VisualElement AxisGroup(string title, string syncProp, string interpProp, string x, string y, string z)
    {
        VisualElement card = BasisSyncInspectorUI.Card(title);

        var sync = new Toggle("Sync " + title) { bindingPath = syncProp, tooltip = $"Stream {title.ToLower()} from the owner to every other client." };
        VisualElement row = AxisRow(x, y, z);
        row.tooltip = "Per-axis include toggles — turn off axes that never change to shrink the packet.";
        row.SetEnabled(serializedObject.FindProperty(syncProp).boolValue);
        sync.RegisterValueChangedCallback(evt => row.SetEnabled(evt.newValue));

        card.Add(sync);
        card.Add(row);
        card.Add(new Toggle("Interpolate") { bindingPath = interpProp, tooltip = "Smooth this channel between received updates instead of snapping to each packet." });
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

    private VisualElement RotationGroup()
    {
        VisualElement card = BasisSyncInspectorUI.Card("Rotation");

        var sync = new Toggle("Sync Rotation") { bindingPath = "SyncRotation", tooltip = "Stream rotation from the owner to every other client." };
        var mode = new PropertyField(serializedObject.FindProperty("RotationMode"), "Mode");
        mode.tooltip = "Orientation encoding. Smallest Three = compact full rotation; Quaternion = raw/half; Euler = per-axis (lets you sync just one axis, e.g. a hinged door).";
        var bits = new SliderInt("Bits / component", 6, 16) { showInputField = true, bindingPath = "SmallestThreeBits", tooltip = "Precision of the Smallest-Three encoding. More bits = finer orientation, larger packets." };
        VisualElement row = AxisRow("RotationX", "RotationY", "RotationZ");
        row.tooltip = "Euler mode only — include this rotation axis.";

        var hint = new Label("XYZ apply to Euler mode only — Smallest Three / Quaternion sync the whole orientation.");
        hint.style.whiteSpace = WhiteSpace.Normal;
        hint.style.fontSize = 10;
        hint.style.opacity = 0.8f;
        hint.style.marginTop = 2;

        void Apply(bool on, int modeIdx)
        {
            mode.SetEnabled(on);
            row.SetEnabled(on && modeIdx == (int)BasisRotationSyncMode.Euler);
            bits.style.display = (on && modeIdx == (int)BasisRotationSyncMode.SmallestThree) ? DisplayStyle.Flex : DisplayStyle.None;
        }
        Apply(serializedObject.FindProperty("SyncRotation").boolValue, serializedObject.FindProperty("RotationMode").enumValueIndex);
        sync.RegisterValueChangedCallback(evt => Apply(evt.newValue, serializedObject.FindProperty("RotationMode").enumValueIndex));
        mode.RegisterValueChangeCallback(evt => Apply(sync.value, serializedObject.FindProperty("RotationMode").enumValueIndex));

        card.Add(sync);
        card.Add(mode);
        card.Add(bits);
        card.Add(row);
        card.Add(hint);
        card.Add(new Toggle("Interpolate") { bindingPath = "InterpolateRotation", tooltip = "Smooth rotation between received updates instead of snapping to each packet." });
        return card;
    }

    private VisualElement ScaleGroup()
    {
        VisualElement card = BasisSyncInspectorUI.Card("Scale");

        var sync = new Toggle("Sync Scale") { bindingPath = "SyncScale", tooltip = "Stream scale from the owner to every other client." };
        var uniform = new Toggle("Uniform (one value → XYZ)") { bindingPath = "ScaleUniform", tooltip = "Stream a single value applied to all three scale axes (smaller than three separate axes)." };
        VisualElement row = AxisRow("ScaleX", "ScaleY", "ScaleZ");
        row.tooltip = "Per-axis include toggles — turn off axes that never change to shrink the packet.";

        void Apply(bool on, bool uni)
        {
            uniform.SetEnabled(on);
            row.SetEnabled(on && !uni);
        }
        Apply(serializedObject.FindProperty("SyncScale").boolValue, serializedObject.FindProperty("ScaleUniform").boolValue);
        sync.RegisterValueChangedCallback(evt => Apply(evt.newValue, uniform.value));
        uniform.RegisterValueChangedCallback(evt => Apply(sync.value, evt.newValue));

        card.Add(sync);
        card.Add(uniform);
        card.Add(row);
        card.Add(new Toggle("Interpolate") { bindingPath = "InterpolateScale", tooltip = "Smooth scale between received updates instead of snapping to each packet." });
        return card;
    }

    private VisualElement BuildCompression()
    {
        VisualElement foldout = BasisSyncInspectorUI.Card("Compression (per axis)", false);

        var note = new Label("Inherit / default = Raw (32-bit). Set Half to halve the cost, or Ranged for min/max + bit level — fewer bits within a known range = smaller packets.");
        note.style.whiteSpace = WhiteSpace.Normal;
        note.style.fontSize = 10;
        note.style.opacity = 0.8f;
        note.style.marginBottom = 4;
        foldout.Add(note);

        foldout.Add(GroupLabel("Position"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("PosCompX"), "X"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("PosCompY"), "Y"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("PosCompZ"), "Z"));

        foldout.Add(GroupLabel("Rotation (Euler mode only — Smallest Three / Quaternion ignore these)"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("RotCompX"), "X"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("RotCompY"), "Y"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("RotCompZ"), "Z"));

        foldout.Add(GroupLabel("Scale"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("ScaleCompX"), "X"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("ScaleCompY"), "Y"));
        foldout.Add(BasisSyncInspectorUI.CompressionAxisRow(serializedObject, serializedObject.FindProperty("ScaleCompZ"), "Z"));

        return foldout;
    }

    private static Label GroupLabel(string text)
    {
        var l = new Label(text);
        l.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
        l.style.whiteSpace = WhiteSpace.Normal;
        l.style.marginTop = 6;
        l.style.marginBottom = 2;
        return l;
    }

    private void FillWireSize(BasisWireSizeModel model)
    {
        var t = target as BasisSyncedTransform;
        if (t == null) return;
        model.Checksum = t.UseChecksum;
        model.SendIntervalSeconds = t.SendIntervalSeconds;
        model.KeyframeIntervalSeconds = t.KeyframeIntervalSeconds;

        int posBits = 0, posFields = 0;
        if (t.SyncPosition)
        {
            if (t.PositionX) { posBits += t.PosCompX.ToSpec().BitCount; posFields++; }
            if (t.PositionY) { posBits += t.PosCompY.ToSpec().BitCount; posFields++; }
            if (t.PositionZ) { posBits += t.PosCompZ.ToSpec().BitCount; posFields++; }
        }
        model.Add("Position", posBits, posFields, posFields == 1 ? "1 axis" : posFields + " axes");

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
                    if (t.RotationX) { rotBits += t.RotCompX.ToSpec().BitCount; rotFields++; }
                    if (t.RotationY) { rotBits += t.RotCompY.ToSpec().BitCount; rotFields++; }
                    if (t.RotationZ) { rotBits += t.RotCompZ.ToSpec().BitCount; rotFields++; }
                    rotDetail = rotFields == 1 ? "1 axis (Euler)" : rotFields + " axes (Euler)";
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
