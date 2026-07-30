using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisSpineSweepPage : BasisIKSweepPage
    {
        public override string Group => "Spine & Torso";
        public override string Title => "Spine Sweep";
        public override int Order => 10;
        public override string Description =>
            "Exercises the virtual-spine solve helpers (BasisVirtualSpineCore chain placement, " +
            "hips position, yaw extraction). Asserts chest/spine sit on the neck→hips segment at the " +
            "right fractions (no inversion), hips drop the spine length below the neck, and yaw " +
            "extraction strips pitch/roll. Edit mode, pure math.";

        BasisSpineSweepConfig _cfg = BasisSpineSweepConfig.Default();
        string _path;
        BasisSpineSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisSpineSweep.DefaultPath();
        }

        public override void Draw()
        {
            _cfg.Cases = EditorGUILayout.IntSlider("Cases", _cfg.Cases, 100, 50000);

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 30f))
            {
                _last = BasisSpineSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[SpineSweep] {_last.Cases} cases -> {_last.Path}");
                else Debug.LogError($"[SpineSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var gate = BasisIKTestGates.GateSpine(_last);
                    BasisEditorUI.PassFail(gate.pass, gate.reason);

                    EditorGUILayout.HelpBox(
                        $"{_last.Cases} cases.\n" +
                        $"Chain fraction err: {_last.MaxChainFracErr:E2}; monotonic fails: {_last.ChainMonotonicFails}\n" +
                        $"Hips: Y err {_last.MaxHipsYErr:E2} m, XZ err {_last.MaxHipsXZErr:E2} m, freeze err {_last.MaxHipsFreezeErr:E2} m\n" +
                        $"Yaw: flatness {_last.MaxYawFlatErr:E2}, idempotence {_last.MaxYawIdempotentErr:F3}°, deg recovery {_last.MaxYawDegErr:F3}°\n" +
                        _last.Path, MessageType.None);
                    if (BasisEditorUI.SecondaryButton("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
                }
                else
                {
                    BasisEditorUI.Help("Sweep failed: " + _last.Error, MessageType.Error);
                }
            }
        }
    }
}
