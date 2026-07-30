using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisEyeSweepPage : BasisIKSweepPage
    {
        public override string Group => "Eyes & Filters";
        public override string Title => "Eye Gaze Sweep";
        public override int Order => 10;
        public override string Description =>
            "Deterministic temporal sweep of the eye gaze/saccade/vergence math (BasisEyeState.Update): " +
            "a static target swept across the field of view (gaze never exceeds the maxAngle clamp; eyes " +
            "verge symmetrically), a fast target jump (no overshoot/NaN, eyes converge toward the target), " +
            "and a held target with personality-driven idle saccades (hold intervals land in the " +
            "Liveliness bounds). Calibration is forced to identity and RNG is seeded, so it's reproducible.";

        BasisEyeSweepConfig _cfg = BasisEyeSweepConfig.Default();
        string _path;
        BasisEyeSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisEyeSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Configuration");
            _cfg.Fps = EditorGUILayout.Slider("FPS", _cfg.Fps, 15f, 144f);
            _cfg.Seconds = EditorGUILayout.Slider("Seconds", _cfg.Seconds, 1f, 20f);
            _cfg.MaxAngleDeg = EditorGUILayout.Slider("Max Angle (deg)", _cfg.MaxAngleDeg, 1f, 30f);
            _cfg.SaccadeMin = EditorGUILayout.Slider("Saccade Min (s)", _cfg.SaccadeMin, 0.01f, 0.3f);
            _cfg.SaccadeMax = EditorGUILayout.Slider("Saccade Max (s)", _cfg.SaccadeMax, _cfg.SaccadeMin, 0.5f);
            _cfg.PerEyeVarianceDeg = EditorGUILayout.Slider("Per-Eye Variance (deg)", _cfg.PerEyeVarianceDeg, 0f, 2f);
            _cfg.Liveliness = EditorGUILayout.Slider("Liveliness", _cfg.Liveliness, 0f, 1f);
            _cfg.Attentiveness = EditorGUILayout.Slider("Attentiveness", _cfg.Attentiveness, 0f, 1f);
            _cfg.TargetYawDeg = EditorGUILayout.Slider("Target Yaw (deg)", _cfg.TargetYawDeg, 1f, 50f);
            _cfg.TargetPitchDeg = EditorGUILayout.Slider("Target Pitch (deg)", _cfg.TargetPitchDeg, -20f, 20f);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Eye Gaze Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisEyeSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[EyeSweep] {_last.Steps} steps -> {_last.Path}");
                else Debug.LogError($"[EyeSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateEye(_last);
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"steps={_last.Steps} nan={_last.NaNCount}\n" +
                        $"maxGaze={_last.MaxGazeAngleDeg:F1}° clamp={_last.ClampDeg:F0}° over={_last.ClampOvershootDeg:F2}°\n" +
                        $"vergenceWrong={_last.VergenceWrongSign}/{_last.VergenceSamples} jumpToward={(_last.JumpMovedTowardOk == 1 ? "ok" : "NO")} jumpOvershoot={_last.JumpMaxOvershootDeg:F2}°\n" +
                        $"saccadeHolds={_last.SaccadeIntervals} hold={_last.SaccadeMinHoldSec:F2}..{_last.SaccadeMaxHoldSec:F2}s bounds={_last.PersonalityHoldMinSec:F2}..{_last.PersonalityHoldMaxSec:F2}s oob={_last.SaccadeOutOfBounds}\n" +
                        $"symmetry={_last.MaxSymmetryDeg:F3}°\n" +
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
