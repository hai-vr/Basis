using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisHipHingeSweepPage : BasisIKSweepPage
    {
        public override string Group => "Spine & Torso";
        public override string Title => "Hip Hinge Sweep";
        public override int Order => 60;
        public override string Description =>
            "Sweeps forward lean against the pelvis hinge. Verifies it engages only past the onset, " +
            "caps at the max add, rotates by exactly the reported amount about a horizontal axis, and " +
            "grows monotonically with lean. Same math as the live ApplyHipHinge.";

        BasisHipHingeSweepConfig _cfg = BasisHipHingeSweepConfig.Default();
        string _path;
        BasisHipHingeSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisHipHingeSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Configuration");
            _cfg.HeadToHips = EditorGUILayout.FloatField("Head→Hips Lever (m)", _cfg.HeadToHips);
            _cfg.MaxLeanDeg = EditorGUILayout.Slider("Max Lean (deg)", _cfg.MaxLeanDeg, 10f, 90f);
            _cfg.LeanSteps = EditorGUILayout.IntSlider("Lean Steps", _cfg.LeanSteps, 5, 91);
            _cfg.AzimuthSteps = EditorGUILayout.IntSlider("Azimuth Steps", _cfg.AzimuthSteps, 1, 16);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Hip Hinge Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisHipHingeSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[HipHingeSweep] {_last.Cases} cases -> {_last.Path}");
                else Debug.LogError($"[HipHingeSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateHipHinge(_last);
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"cases={_last.Cases} engaged={_last.EngagedCases} nan={_last.NaNCount} fails={_last.Failures}\n" +
                        $"angleMatch={_last.MaxAngleMatchErrDeg:F3}° over={_last.MaxOverAddDeg:F3}° axisUp={_last.MaxAxisDotUp:F4} " +
                        $"mono={_last.MonotonicViolations} disabledMoves={_last.DisabledMoves}\n" + _last.Path);
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
