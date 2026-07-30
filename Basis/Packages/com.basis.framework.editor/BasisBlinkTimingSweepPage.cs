using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisBlinkTimingSweepPage : BasisIKSweepPage
    {
        public override string Group => "Eyes & Filters";
        public override string Title => "Blink Timing Sweep";
        public override int Order => 20;
        public override string Description =>
            "Temporal sweep of BasisLocalFacialBlinkDriver's procedural blink scheduler over a long " +
            "virtual run: every inter-blink interval lands in [min,max], each blink's close duration " +
            "matches blinkDuration, the eyelid weight stays in [0,100] and returns to rest between " +
            "blinks, blinks never overlap, no NaN. The driver's scheduler uses UnityEngine.Random and " +
            "writes a mesh, so the timing arithmetic is faithfully re-implemented against a fixed seed.";

        BasisBlinkTimingSweepConfig _cfg = BasisBlinkTimingSweepConfig.Default();
        string _path;
        BasisBlinkTimingSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisBlinkTimingSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Configuration");
            _cfg.Fps = EditorGUILayout.Slider("FPS", _cfg.Fps, 15f, 144f);
            _cfg.Seconds = EditorGUILayout.Slider("Seconds", _cfg.Seconds, 10f, 600f);
            _cfg.MinBlinkInterval = EditorGUILayout.Slider("Min Interval (s)", _cfg.MinBlinkInterval, 0.5f, 30f);
            _cfg.MaxBlinkInterval = EditorGUILayout.Slider("Max Interval (s)", _cfg.MaxBlinkInterval, 0.5f, 60f);
            _cfg.BlinkDuration = EditorGUILayout.Slider("Blink Duration (s)", _cfg.BlinkDuration, 0.02f, 1f);
            _cfg.VisemeTransitionDuration = EditorGUILayout.Slider("Open Transition (s)", _cfg.VisemeTransitionDuration, 0.02f, 1f);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Blink Timing Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisBlinkTimingSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[BlinkTimingSweep] {_last.Steps} steps -> {_last.Path}");
                else Debug.LogError($"[BlinkTimingSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateBlinkTiming(_last);
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"steps={_last.Steps} nan={_last.NaNCount} blinks={_last.Blinks}\n" +
                        $"interval={_last.MinIntervalS:F1}..{_last.MaxIntervalS:F1}s underMin={_last.IntervalUnderMinS * 1000f:F0}ms overMax={_last.IntervalOverMaxS * 1000f:F0}ms\n" +
                        $"closeErr={_last.MaxCloseDurationErrS * 1000f:F1}ms weight={_last.MinWeight:F1}..{_last.MaxWeight:F1} rest={_last.MaxRestWeight:F2}\n" +
                        $"overlaps={_last.OverlapCount}\n" +
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
