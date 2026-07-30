using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisSpineTwistSweepPage : BasisIKSweepPage
    {
        public override string Group => "Spine & Torso";
        public override string Title => "Spine Twist Sweep";
        public override int Order => 40;
        public override string Description =>
            "Exercises BasisTwistSolveCore.ShapeReachStep and the graded, orientation-independent spine CCD. " +
            "Pass A: the swing/twist split is exact. Pass B: a forward-pitched spine reaches a head swept " +
            "laterally across center, run UPRIGHT and LYING DOWN -- keep=1 corkscrews; the graded shape " +
            "(rigid lumbar -> free neck) bends instead, stiffens the lumbar, still reaches, and matches in " +
            "both orientations (body hips-up axis). Pass C: world-up axis while prone leaves the corkscrew " +
            "(negative control). Edit mode, pure math.";

        BasisSpineTwistSweepConfig _cfg = BasisSpineTwistSweepConfig.Default();
        string _path;
        BasisSpineTwistSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisSpineTwistSweep.DefaultPath();
        }

        public override void Draw()
        {
            _cfg.InvariantCases = EditorGUILayout.IntSlider("Invariant Cases", _cfg.InvariantCases, 200, 20000);
            _cfg.LeanSteps = EditorGUILayout.IntSlider("Lean Steps", _cfg.LeanSteps, 21, 641);
            _cfg.LumbarKeep = EditorGUILayout.Slider("Lumbar Keep", _cfg.LumbarKeep, 0f, 1f);
            _cfg.CervicalKeep = EditorGUILayout.Slider("Cervical (Neck) Keep", _cfg.CervicalKeep, 0f, 1f);

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 30f))
            {
                _last = BasisSpineTwistSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[SpineTwistSweep] {_last.Cases} cases -> {_last.Path}");
                else Debug.LogError($"[SpineTwistSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var gate = BasisIKTestGates.GateSpineTwist(_last);
                    BasisEditorUI.PassFail(gate.pass, gate.reason);

                    BasisEditorUI.Readout(
                        $"{_last.Cases} cases (lumbar/neck keep {_last.TestedLumbarKeep:F2}/{_last.TestedCervicalKeep:F2}).\n" +
                        $"ShapeReachStep: identity {_last.MaxIdentityErrDeg:F3}°, resid twist {_last.MaxResidualTwistDeg:F3}°, resid swing {_last.MaxResidualSwingDeg:F3}°, blend {_last.MaxBlendErrDeg:F3}°\n" +
                        $"Peak twist: {_last.ReproMaxTwistDeg:F1}° (keep=1) -> {_last.GradedMaxTwistDeg:F1}° (graded)\n" +
                        $"Lumbar: {_last.ReproLumbarTwistDeg:F1}° -> {_last.GradedLumbarTwistDeg:F1}°   Neck (graded): {_last.GradedCervicalTwistDeg:F1}°\n" +
                        $"Across-center jump: {_last.GradedMaxJumpDeg:F1}°   Reach gap: {_last.GradedMaxReachErrM * 100f:F1} cm\n" +
                        $"Upright vs lying-down spread: {_last.OrientationSpreadDeg:F2}°   World-up-prone twist (neg ctrl): {_last.WorldUpSupineTwistDeg:F1}°\n" +
                        _last.Path);
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
