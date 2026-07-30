using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisOneEuroSweepPage : BasisIKSweepPage
    {
        public override string Group => "Eyes & Filters";
        public override string Title => "One-Euro Filter Sweep";
        public override int Order => 30;
        public override string Description =>
            "Temporal sweep of the general-purpose One-Euro filter (Vector3 + Quaternion) that smooths " +
            "tracker/target inputs before IK: step (no overshoot, converges), ramp (bounded lag), noise " +
            "hold (smoother than input), smooth sinusoid (no added stepping). Sweep the three tuning knobs " +
            "to explore the latency/jitter tradeoff.";

        BasisOneEuroSweepConfig _cfg = BasisOneEuroSweepConfig.Default();
        string _path;
        BasisOneEuroSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisOneEuroSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Configuration");
            _cfg.Fps = EditorGUILayout.Slider("FPS", _cfg.Fps, 15f, 144f);
            _cfg.Seconds = EditorGUILayout.Slider("Seconds", _cfg.Seconds, 1f, 10f);
            _cfg.MinCutoff = EditorGUILayout.Slider("Min Cutoff (Hz)", _cfg.MinCutoff, 0.01f, 10f);
            _cfg.Beta = EditorGUILayout.Slider("Beta", _cfg.Beta, 0f, 10f);
            _cfg.DCutoff = EditorGUILayout.Slider("Derivative Cutoff (Hz)", _cfg.DCutoff, 0.01f, 10f);
            _cfg.NoiseMeters = EditorGUILayout.Slider("Noise (m)", _cfg.NoiseMeters, 0f, 0.05f);
            _cfg.NoiseDeg = EditorGUILayout.Slider("Noise (deg)", _cfg.NoiseDeg, 0f, 15f);
            _cfg.RampMetersPerSec = EditorGUILayout.Slider("Ramp (m/s)", _cfg.RampMetersPerSec, 0.1f, 5f);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("One-Euro Filter Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisOneEuroSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[OneEuroSweep] {_last.Steps} steps -> {_last.Path}");
                else Debug.LogError($"[OneEuroSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateOneEuro(_last);
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"steps={_last.Steps} nan={_last.NaNCount}\n" +
                        $"stepOvershoot={_last.MaxStepOvershootM * 1000f:F1}mm finalErr={_last.StepFinalErrM * 1000f:F1}mm " +
                        $"rampLag={_last.MaxRampLagM * 1000f:F0}mm\n" +
                        $"noiseReject={_last.NoiseRejectRatio:F2} glide={_last.MaxGlideJitterM * 1000f:F2}mm\n" +
                        $"quatNoiseReject={_last.QuatNoiseRejectRatio:F2} quatGlide={_last.QuatMaxGlideJitterDeg:F3}° nonUnit={_last.QuatMaxNonUnit:F4}\n" +
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
