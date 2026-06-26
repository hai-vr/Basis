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

        root.Add(AxisGroup("Position", "SyncPosition", "InterpolatePosition", "PositionX", "PositionY", "PositionZ"));
        root.Add(RotationGroup());
        root.Add(ScaleGroup());

        VisualElement space = BasisSyncInspectorUI.Card("Space");
        space.Add(new PropertyField(serializedObject.FindProperty("WorldSpace")));
        root.Add(space);

        root.Add(BuildCompression());

        root.Add(BasisSyncInspectorUI.SmoothingCard(serializedObject));
        root.Add(BasisSyncInspectorUI.NetworkingCard(serializedObject));

        root.Add(BuildWireSize());

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

        var sync = new Toggle("Sync " + title) { bindingPath = syncProp };
        VisualElement row = AxisRow(x, y, z);
        row.SetEnabled(serializedObject.FindProperty(syncProp).boolValue);
        sync.RegisterValueChangedCallback(evt => row.SetEnabled(evt.newValue));

        card.Add(sync);
        card.Add(row);
        card.Add(new Toggle("Interpolate") { bindingPath = interpProp });
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

        var sync = new Toggle("Sync Rotation") { bindingPath = "SyncRotation" };
        var mode = new PropertyField(serializedObject.FindProperty("RotationMode"), "Mode");
        var bits = new SliderInt("Bits / component", 6, 16) { showInputField = true, bindingPath = "SmallestThreeBits" };
        VisualElement row = AxisRow("RotationX", "RotationY", "RotationZ");

        var hint = new Label("XYZ apply to Euler mode only — Smallest Three / Quaternion sync the whole orientation.");
        hint.style.whiteSpace = WhiteSpace.Normal;
        hint.style.fontSize = 10;
        hint.style.opacity = 0.8f;
        hint.style.marginTop = 2;

        void UpdateEnabled()
        {
            bool on = serializedObject.FindProperty("SyncRotation").boolValue;
            int modeIdx = serializedObject.FindProperty("RotationMode").enumValueIndex;
            mode.SetEnabled(on);
            row.SetEnabled(on && modeIdx == (int)BasisRotationSyncMode.Euler);
            bits.style.display = (on && modeIdx == (int)BasisRotationSyncMode.SmallestThree) ? DisplayStyle.Flex : DisplayStyle.None;
        }
        UpdateEnabled();
        sync.RegisterValueChangedCallback(evt => UpdateEnabled());
        mode.RegisterValueChangeCallback(evt => UpdateEnabled());

        card.Add(sync);
        card.Add(mode);
        card.Add(bits);
        card.Add(row);
        card.Add(hint);
        card.Add(new Toggle("Interpolate") { bindingPath = "InterpolateRotation" });
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

    private VisualElement BuildWireSize()
    {
        VisualElement card = BasisSyncInspectorUI.Card("Wire Size");

        var note = new Label("Bytes per send for the current axes + compression. Per-axis payloads are bit-packed into one contiguous stream, so only the totals round up to whole bytes. Full-quaternion rotation is a fixed 32-bit pack; partial (Euler) rotation uses the per-axis compression above.");
        note.style.whiteSpace = WhiteSpace.Normal;
        note.style.fontSize = 10;
        note.style.opacity = 0.8f;
        note.style.marginBottom = 4;
        card.Add(note);

        Label pos = SizeRow(card, "Position", false);
        Label rot = SizeRow(card, "Rotation", false);
        Label scale = SizeRow(card, "Scale", false);

        var divider = new VisualElement();
        divider.style.height = 1;
        divider.style.backgroundColor = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(1f, 1f, 1f, 0.15f));
        divider.style.marginTop = 4;
        divider.style.marginBottom = 4;
        card.Add(divider);

        Label overhead = SizeRow(card, "Overhead", false);
        Label keyframe = SizeRow(card, "Keyframe total", true);
        Label delta = SizeRow(card, "Delta total (all axes)", true);
        Label bandwidth = SizeRow(card, "Bandwidth", true);

        void Refresh()
        {
            var t = target as BasisSyncedTransform;
            if (t == null) return;

            int posBits = 0, posFields = 0;
            if (t.SyncPosition)
            {
                if (t.PositionX) { posBits += t.PosCompX.ToSpec().BitCount; posFields++; }
                if (t.PositionY) { posBits += t.PosCompY.ToSpec().BitCount; posFields++; }
                if (t.PositionZ) { posBits += t.PosCompZ.ToSpec().BitCount; posFields++; }
            }

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

            int fields = posFields + rotFields + scaleFields;
            int payloadBits = posBits + rotBits + scaleBits;
            int payloadBytes = (payloadBits + 7) >> 3;
            int maskBytes = (fields + 7) >> 3;
            int header = BasisSyncCodec.HeaderSize;
            int checksum = t.UseChecksum ? BasisSyncCodec.ChecksumSize : 0;

            pos.text = SectionText(posFields, posBits, posFields == 1 ? "1 axis" : posFields + " axes");
            rot.text = SectionText(rotFields, rotBits, rotDetail);
            scale.text = SectionText(scaleFields, scaleBits, scaleDetail);

            overhead.text = checksum > 0
                ? $"header {header} B  +  mask {maskBytes} B  +  checksum {checksum} B"
                : $"header {header} B  +  mask {maskBytes} B";

            int keyframeBytes = header + payloadBytes + checksum;
            int deltaBytes = header + maskBytes + payloadBytes + checksum;
            keyframe.text = $"{keyframeBytes} B   ({payloadBits} b payload → {payloadBytes} B)";
            delta.text = $"{deltaBytes} B";

            float sendHz = t.SendIntervalSeconds > 0f ? 1f / t.SendIntervalSeconds : 0f;
            float kfHz = t.KeyframeIntervalSeconds > 0f ? 1f / t.KeyframeIntervalSeconds : 0f;
            float bps = deltaBytes * sendHz + UnityEngine.Mathf.Max(0, keyframeBytes - deltaBytes) * kfHz;
            bandwidth.text = $"≈ {FormatRate(bps)}  @ {sendHz:0} Hz";
            bandwidth.style.color = new UnityEngine.UIElements.StyleColor(bps > 2000f ? new UnityEngine.Color(0.95f, 0.75f, 0.2f) : BasisSyncInspectorUI.Accent);
        }

        Refresh();
        card.schedule.Execute(Refresh).Every(400);
        return card;
    }

    private static string SectionText(int fields, int bits, string detail)
    {
        if (fields == 0) return "off";
        return $"{bits} b  •  {(bits / 8f):0.##} B   ({detail})";
    }

    private static string FormatRate(float bytesPerSec) =>
        bytesPerSec >= 1024f ? $"{bytesPerSec / 1024f:0.0} KB/s" : $"{bytesPerSec:0} B/s";

    private static Label SizeRow(VisualElement parent, string key, bool strong)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.paddingTop = 1;
        row.style.paddingBottom = 1;

        var k = new Label(key);
        k.style.flexShrink = 0;
        k.style.whiteSpace = WhiteSpace.NoWrap;
        if (strong)
        {
            k.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            k.style.color = new UnityEngine.UIElements.StyleColor(BasisSyncInspectorUI.Accent);
        }

        var v = new Label("—");
        v.style.flexGrow = 1;
        v.style.whiteSpace = WhiteSpace.Normal;
        v.style.unityTextAlign = UnityEngine.TextAnchor.MiddleRight;
        if (strong) v.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
        else v.style.color = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(0.78f, 0.78f, 0.78f, 1f));

        row.Add(k);
        row.Add(v);
        parent.Add(row);
        return v;
    }
}
