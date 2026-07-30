using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisLegTwistSweepPage : BasisIKSweepPage
    {
        public override string Group => "Leg & Foot";
        public override string Title => "Leg Twist Sweep";
        public override int Order => 30;
        public override string Description =>
            "Standing leg twist: drives the leg solver near full extension with a hips-yaw-jittering bend " +
            "frame and runs the same One-Euro knee-swivel smoothing the live job applies. The smoothed " +
            "knee swivel must collapse the standing twist yet still track a real turn.";

        BasisLegTwistSweepConfig _cfg = BasisLegTwistSweepConfig.Default();
        string _path;
        BasisLegTwistSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisLegTwistSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Configuration");
            _cfg.JitterAmpDeg = EditorGUILayout.Slider("Standing Yaw Jitter (deg)", _cfg.JitterAmpDeg, 0.5f, 15f);
            _cfg.TurnRateDeg = EditorGUILayout.Slider("Real Turn Rate (deg/s)", _cfg.TurnRateDeg, 10f, 180f);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Leg Twist Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisLegTwistSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[LegTwistSweep] {_last.Rows} rows -> {_last.Path}");
                else Debug.LogError($"[LegTwistSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateLegTwist(_last);
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"worst raw {_last.WorstRawP2PDeg:F1}° -> smoothed {_last.WorstSmoothedP2PDeg:F1}° @ ext {_last.WorstExt:F2} ({_last.WorstReductionFrac:P0})\n" +
                        $"turn tracks {_last.TurnSmoothChangeDeg:F0}/{_last.TurnRawChangeDeg:F0}° lag {_last.TurnMaxLagDeg:F1}°\n" +
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
