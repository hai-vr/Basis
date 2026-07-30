using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisCalibrationMathSweepPage : BasisIKSweepPage
    {
        public override string Group => "Tracking & Calibration";
        public override string Title => "Calibration Math Sweep";
        public override int Order => 30;
        public override string Description =>
            "Exercises the real calibration math the live FullBodyCalibration runs: tracker→bone " +
            "inverse-offset capture/apply (BasisCalibrationMath), device scaling, per-effector " +
            "rotation calibration (BasisAnimationRiggingHelper — the #531 no-orientation-leak), " +
            "pitch-calibrated eye height, and the avatar scale modifier. Asserts round-trip / no-leak " +
            "invariants over many synthetic poses. Edit mode, no avatar.";

        BasisCalibrationMathSweepConfig _cfg = BasisCalibrationMathSweepConfig.Default();
        string _path;
        BasisCalibrationMathSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path))
            {
                _path = BasisCalibrationMathSweep.DefaultPath();
            }
        }

        public override void Draw()
        {
            EditorGUILayout.Space();
            _cfg.CasesPerSection = EditorGUILayout.IntSlider("Cases per Section", _cfg.CasesPerSection, 100, 20000);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Calibration Math Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();
            if (BasisEditorUI.SecondaryButton("Reset to persistentDataPath"))
            {
                _path = BasisCalibrationMathSweep.DefaultPath();
            }

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisCalibrationMathSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[CalibrationMathSweep] {_last.Cases} cases, {_last.Failures} fails -> {_last.Path}");
                else Debug.LogError($"[CalibrationMathSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var gate = BasisIKTestGates.GateCalibrationMath(_last);
                    BasisEditorUI.PassFail(gate.pass, gate.reason);

                    BasisEditorUI.Readout(
                        $"{_last.Cases} cases, {_last.Failures} failures.\n" +
                        $"Inverse offset: pos err {_last.MaxOffsetPosErr:E2} m, rot err {_last.MaxOffsetRotErrDeg:F3}°, rigid-follow {_last.MaxRigidFollowErr:E2} m\n" +
                        $"Device scale round-trip: {_last.MaxScalePosErr:E2} m\n" +
                        $"Rotation calibration (no-leak): {_last.MaxRotCalErrDeg:F3}°\n" +
                        $"Scale modifier mismatches: {_last.ScaleModifierMismatches}\n" +
                        $"Feel height: viewpoint err {_last.MaxFeelHeightErr:E2} m, too-tall ratio err {_last.MaxFeelFactorErr:E2}\n" +
                        _last.Path);

                    EditorGUILayout.BeginHorizontal();
                    if (BasisEditorUI.SecondaryButton("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
                    if (BasisEditorUI.SecondaryButton("Copy Path")) EditorGUIUtility.systemCopyBuffer = _last.Path;
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    BasisEditorUI.Help("Sweep failed: " + _last.Error, MessageType.Error);
                }
            }

        }
    }
}
