using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ Shoulder Sweep.
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
            w.minSize = new Vector2(360, 430);
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
                "Sweeps the hand target around the shoulder and records the shoulder engagement curve " +
                "(reach/elevation/protraction) and applied angle. Same math as the live SolveShoulder.",
                MessageType.Info);

            EditorGUILayout.LabelField("Shoulder", EditorStyles.boldLabel);
            _cfg.TposeArmLength = EditorGUILayout.FloatField("T-pose Arm Length (m)", _cfg.TposeArmLength);
            _cfg.ElevationFactor = EditorGUILayout.FloatField("Elevation Factor", _cfg.ElevationFactor);
            _cfg.ProtractionFactor = EditorGUILayout.FloatField("Protraction Factor", _cfg.ProtractionFactor);
            _cfg.IsLeft = EditorGUILayout.Toggle("Left Shoulder (mirror X)", _cfg.IsLeft);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Target Grid (fractions of arm length)", EditorStyles.boldLabel);
            _cfg.MinFrac = EditorGUILayout.Vector3Field("Min Frac", _cfg.MinFrac);
            _cfg.MaxFrac = EditorGUILayout.Vector3Field("Max Frac", _cfg.MaxFrac);
            _cfg.Steps = EditorGUILayout.Vector3IntField("Steps (X,Y,Z)", _cfg.Steps);
            int pts = Mathf.Max(1, _cfg.Steps.x) * Mathf.Max(1, _cfg.Steps.y) * Mathf.Max(1, _cfg.Steps.z);
            EditorGUILayout.LabelField("Points", pts.ToString());

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
                    EditorGUILayout.HelpBox(
                        $"Wrote {_last.Rows} rows. Engaged (reachRatio>0): {_last.Engaged}. " +
                        $"Max shoulder angle: {_last.MaxShoulderAngleDeg:F1}°\n{_last.Path}", MessageType.None);
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
