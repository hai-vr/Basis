using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Hip Hinge Sweep.
    public class BasisHipHingeSweepWindow : EditorWindow
    {
        BasisHipHingeSweepConfig _cfg = BasisHipHingeSweepConfig.Default();
        string _path;
        BasisHipHingeSweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Hip Hinge Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisHipHingeSweepWindow>("Hip Hinge Sweep");
            w.minSize = new Vector2(360, 400);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisHipHingeSweep.DefaultPath();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Sweeps forward lean against the pelvis hinge. Verifies it engages only past the onset, " +
                "caps at the max add, rotates by exactly the reported amount about a horizontal axis, and " +
                "grows monotonically with lean. Same math as the live ApplyHipHinge.",
                MessageType.Info);

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            _cfg.HeadToHips = EditorGUILayout.FloatField("Head→Hips Lever (m)", _cfg.HeadToHips);
            _cfg.MaxLeanDeg = EditorGUILayout.Slider("Max Lean (deg)", _cfg.MaxLeanDeg, 10f, 90f);
            _cfg.LeanSteps = EditorGUILayout.IntSlider("Lean Steps", _cfg.LeanSteps, 5, 91);
            _cfg.AzimuthSteps = EditorGUILayout.IntSlider("Azimuth Steps", _cfg.AzimuthSteps, 1, 16);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
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
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
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
                    EditorGUILayout.HelpBox(
                        (g.pass ? "PASS  " : "FAIL  ") + g.reason + "\n" +
                        $"cases={_last.Cases} engaged={_last.EngagedCases} nan={_last.NaNCount} fails={_last.Failures}\n" +
                        $"angleMatch={_last.MaxAngleMatchErrDeg:F3}° over={_last.MaxOverAddDeg:F3}° axisUp={_last.MaxAxisDotUp:F4} " +
                        $"mono={_last.MonotonicViolations} disabledMoves={_last.DisabledMoves}\n" + _last.Path,
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
