using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Swing Continuity Sweep.
    public class BasisSwingContinuitySweepWindow : EditorWindow
    {
        BasisSwingContinuitySweepConfig _cfg = BasisSwingContinuitySweepConfig.Default();
        string _path;
        BasisSwingContinuitySweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Swing Continuity Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisSwingContinuitySweepWindow>("Swing Continuity Sweep");
            w.minSize = new Vector2(360, 380);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisSwingContinuitySweep.DefaultPath();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Temporal sweep of the collision-gated elbow swing rate limiter. Verifies the swing never " +
                "moves faster than rate*dt while easing a collision pop, converges to the target, follows " +
                "instantly in free air, and re-seeds on a target teleport. Same logic as ApplySwingContinuity.",
                MessageType.Info);

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            _cfg.Fps = EditorGUILayout.Slider("FPS", _cfg.Fps, 15f, 144f);
            _cfg.RateDegPerSec = EditorGUILayout.Slider("Swing Rate (deg/s)", _cfg.RateDegPerSec, 20f, 360f);
            _cfg.JumpDeg = EditorGUILayout.Slider("Collision Jump (deg)", _cfg.JumpDeg, 20f, 160f);
            _cfg.Frames = EditorGUILayout.IntSlider("Frames", _cfg.Frames, 60, 600);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Swing Continuity Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _last = BasisSwingContinuitySweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[SwingContinuitySweep] {_last.Steps} steps -> {_last.Path}");
                else Debug.LogError($"[SwingContinuitySweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateSwingContinuity(_last);
                    EditorGUILayout.HelpBox(
                        (g.pass ? "PASS  " : "FAIL  ") + g.reason + "\n" +
                        $"steps={_last.Steps} nan={_last.NaNCount} fails={_last.Failures}\n" +
                        $"easingStep={_last.MaxEasingStepDeg:F2}° viol={_last.RateLimitViolations} converged={_last.Converged}({_last.ConvergeFrames}f) " +
                        $"freeAirLag={_last.FreeAirMaxLagDeg:F2}° teleport={_last.TeleportAccepted}\n" +
                        _last.Path,
                        g.pass ? MessageType.Info : MessageType.Error);
                    if (GUILayout.Button("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
                }
                else
                {
                    EditorGUILayout.HelpBox("Sweep failed: " + _last.Error, MessageType.Error);
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
