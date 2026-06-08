using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ Leg IK Sweep.
    public class BasisLegIKSweepWindow : EditorWindow
    {
        BasisLegIKSweepConfig _cfg = BasisLegIKSweepConfig.Default();
        string _path;
        BasisLegIKSweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Leg IK Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisLegIKSweepWindow>("Leg IK Sweep");
            w.minSize = new Vector2(380, 520);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path))
            {
                _path = BasisLegIKSweep.DefaultPath();
            }
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Sweeps the foot target over a 3D grid and solves BasisLegSolveCore at every " +
                "point WITH and WITHOUT a knee hint. One CSV row per (target, mode). Same math the live rig runs.",
                MessageType.Info);

            EditorGUILayout.LabelField("Leg Geometry", EditorStyles.boldLabel);
            _cfg.UpperLength = EditorGUILayout.FloatField("Upper Length (m)", _cfg.UpperLength);
            _cfg.LowerLength = EditorGUILayout.FloatField("Lower Length (m)", _cfg.LowerLength);
            _cfg.IsLeft = EditorGUILayout.Toggle("Left Leg (mirror X)", _cfg.IsLeft);
            _cfg.RestKneeDir = EditorGUILayout.Vector3Field("Rest Knee Dir", _cfg.RestKneeDir);
            _cfg.RestShinDir = EditorGUILayout.Vector3Field("Rest Shin Dir", _cfg.RestShinDir);
            _cfg.BendNormal = EditorGUILayout.Vector3Field("Bend Normal (hips right)", _cfg.BendNormal);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Target Grid (fractions of leg length)", EditorStyles.boldLabel);
            _cfg.MinFrac = EditorGUILayout.Vector3Field("Min Frac", _cfg.MinFrac);
            _cfg.MaxFrac = EditorGUILayout.Vector3Field("Max Frac", _cfg.MaxFrac);
            _cfg.Steps = EditorGUILayout.Vector3IntField("Steps (X,Y,Z)", _cfg.Steps);
            int pts = Mathf.Max(1, _cfg.Steps.x) * Mathf.Max(1, _cfg.Steps.y) * Mathf.Max(1, _cfg.Steps.z);
            EditorGUILayout.LabelField("Points", pts + "  (" + (pts * 2) + " rows)");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Knee Hint (pole)", EditorStyles.boldLabel);
            _cfg.HintDir = EditorGUILayout.Vector3Field("Hint Dir", _cfg.HintDir);
            _cfg.HintDistanceFrac = EditorGUILayout.Slider("Hint Distance Frac", _cfg.HintDistanceFrac, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Leg IK Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Reset to persistentDataPath"))
            {
                _path = BasisLegIKSweep.DefaultPath();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _last = BasisLegIKSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[LegIKSweep] {_last.Rows} rows -> {_last.Path}");
                else Debug.LogError($"[LegIKSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    EditorGUILayout.HelpBox(
                        $"Wrote {_last.Rows} rows ({_last.Points} points, {_last.ReachablePoints} reachable).\n" +
                        $"Mean knee swivel shift hint vs no-hint: {_last.MeanSwivelShiftDeg:F1}°  (max {_last.MaxSwivelShiftDeg:F1}°)\n" +
                        _last.Path, MessageType.None);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Reveal CSV"))
                    {
                        EditorUtility.RevealInFinder(_last.Path);
                    }
                    if (GUILayout.Button("Copy Path"))
                    {
                        EditorGUIUtility.systemCopyBuffer = _last.Path;
                    }
                    EditorGUILayout.EndHorizontal();
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
