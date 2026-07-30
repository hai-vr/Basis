using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisSwivelFilterSweepPage : BasisIKSweepPage
    {
        public override string Group => "Arm & Elbow";
        public override string Title => "Swivel Filter Sweep";
        public override int Order => 50;
        public override string Description =>
            "Temporal sweep of the elbow-swivel One-Euro filter: step (no overshoot, converges), ramp " +
            "(bounded lag), noise hold (smoother than input), and smooth sinusoid (no added stepping). " +
            "Same filter the live SmoothElbowSwivel runs.";

        BasisSwivelFilterSweepConfig _cfg = BasisSwivelFilterSweepConfig.Default();
        string _path;
        BasisSwivelFilterSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisSwivelFilterSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Configuration");
            _cfg.Fps = EditorGUILayout.Slider("FPS", _cfg.Fps, 15f, 144f);
            _cfg.Seconds = EditorGUILayout.Slider("Seconds", _cfg.Seconds, 1f, 10f);
            _cfg.NoiseDeg = EditorGUILayout.Slider("Noise (deg)", _cfg.NoiseDeg, 0f, 15f);
            _cfg.RampDegPerSec = EditorGUILayout.Slider("Ramp (deg/s)", _cfg.RampDegPerSec, 10f, 360f);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Swivel Filter Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisSwivelFilterSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[SwivelFilterSweep] {_last.Steps} steps -> {_last.Path}");
                else Debug.LogError($"[SwivelFilterSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateSwivelFilter(_last);
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"steps={_last.Steps} nan={_last.NaNCount}\n" +
                        $"stepOvershoot={_last.MaxStepOvershootDeg:F2}° finalErr={_last.StepFinalErrDeg:F2}° " +
                        $"rampLag={_last.MaxRampLagDeg:F1}° noiseReject={_last.NoiseRejectRatio:F2} glide={_last.MaxGlideJitterDeg:F3}°\n" +
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
