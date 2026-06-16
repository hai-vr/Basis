using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Shoulder Sweep.
    public class BasisShoulderSweepWindow : EditorWindow
    {
        BasisShoulderSweepConfig _cfg = BasisShoulderSweepConfig.Default();
        string _path;
        BasisShoulderSweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Shoulder Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisShoulderSweepWindow>("Shoulder Sweep");
            w.minSize = new Vector2(380, 520);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path))
            {
                _path = BasisShoulderSweep.DefaultPath();
            }
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Sweeps the upper-arm direction around the shoulder (both elbow-driven and hand-driven) and " +
                "records the scapulohumeral engagement curve and applied girdle rotation. Same math as the live " +
                "SolveShoulder; checks the BasisIKTestGates.GateShoulder invariants.",
                MessageType.Info);

            EditorGUILayout.LabelField("Shoulder", EditorStyles.boldLabel);
            _cfg.ArmLength = EditorGUILayout.FloatField("Arm Length (m)", _cfg.ArmLength);
            _cfg.ElevationFactor = EditorGUILayout.Slider("Elevation Factor", _cfg.ElevationFactor, 0f, 1f);
            _cfg.ProtractionFactor = EditorGUILayout.Slider("Protraction Factor", _cfg.ProtractionFactor, 0f, 1f);
            _cfg.CoupleRatio = EditorGUILayout.Slider("Couple Ratio", _cfg.CoupleRatio, 0f, 1f);
            _cfg.MaxShoulderDeg = EditorGUILayout.FloatField("Max Girdle (deg)", _cfg.MaxShoulderDeg);
            _cfg.IsLeft = EditorGUILayout.Toggle("Left Shoulder (mirror X)", _cfg.IsLeft);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Direction Grid (chest-local)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _cfg.AzMin = EditorGUILayout.FloatField("Azimuth", _cfg.AzMin);
            _cfg.AzMax = EditorGUILayout.FloatField("…", _cfg.AzMax);
            _cfg.AzSteps = EditorGUILayout.IntField(_cfg.AzSteps, GUILayout.Width(48));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _cfg.ElMin = EditorGUILayout.FloatField("Elevation", _cfg.ElMin);
            _cfg.ElMax = EditorGUILayout.FloatField("…", _cfg.ElMax);
            _cfg.ElSteps = EditorGUILayout.IntField(_cfg.ElSteps, GUILayout.Width(48));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _cfg.ReachMin = EditorGUILayout.FloatField("Reach", _cfg.ReachMin);
            _cfg.ReachMax = EditorGUILayout.FloatField("…", _cfg.ReachMax);
            _cfg.ReachSteps = EditorGUILayout.IntField(_cfg.ReachSteps, GUILayout.Width(48));
            EditorGUILayout.EndHorizontal();

            int pts = Mathf.Max(1, _cfg.AzSteps) * Mathf.Max(1, _cfg.ElSteps) * Mathf.Max(1, _cfg.ReachSteps) * 2;
            EditorGUILayout.LabelField("Grid points (×2 modes)", pts.ToString());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Shoulder Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _last = BasisShoulderSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[ShoulderSweep] {_last.Rows} rows -> {_last.Path}");
                else Debug.LogError($"[ShoulderSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var gate = BasisIKTestGates.GateShoulder(_last);
                    var prev = GUI.color;
                    GUI.color = gate.pass ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
                    EditorGUILayout.LabelField(gate.pass ? "GATE PASS" : "GATE FAIL", EditorStyles.boldLabel);
                    GUI.color = prev;
                    EditorGUILayout.HelpBox(gate.reason, gate.pass ? MessageType.Info : MessageType.Error);

                    EditorGUILayout.LabelField($"Rows {_last.Rows}   Engaged {_last.Engaged}");
                    EditorGUILayout.LabelField($"Max girdle {_last.MaxShoulderAngleDeg:F1}°   clamp overshoot {_last.ClampOvershootDeg:F2}°");
                    EditorGUILayout.LabelField($"Rest-pose move {_last.RestPoseMaxDeg:F2}°   twist leak {_last.MaxTwistLeakDeg:F2}°");
                    EditorGUILayout.LabelField($"Frame-rigidity err {_last.MaxFrameInvarErrDeg:F2}°   symmetry err {_last.SymmetryMaxErrDeg:F2}°");
                    EditorGUILayout.LabelField($"Monotonic violations {_last.MonotonicViolations}   smooth jump {_last.MaxAdjacentJumpDeg:F1}°");
                    EditorGUILayout.LabelField($"Noisy-elbow jitter {_last.NoisyMaxJitterDeg:F1}°");
                    EditorGUILayout.LabelField($"Bent-arm elevation: elbow {_last.BentArmElbowElevationDeg:F1}° vs hand {_last.BentArmHandElevationDeg:F1}°");

                    if (GUILayout.Button("Reveal CSV"))
                    {
                        EditorUtility.RevealInFinder(_last.Path);
                    }
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
