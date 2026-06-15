using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ Arm IK Sweep.
    public class BasisArmIKSweepWindow : EditorWindow
    {
        BasisArmIKSweepConfig _cfg = BasisArmIKSweepConfig.Default();
        string _path;
        BasisArmIKSweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;
        float _trajNoise = 0.003f;
        BasisArmIKSweep.BasisArmIKTrajectorySummary _traj;
        bool _hasTraj;

        [MenuItem("Basis/Debug/IK/Arm IK Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisArmIKSweepWindow>("Arm IK Sweep");
            w.minSize = new Vector2(380, 520);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path))
            {
                _path = BasisArmIKSweep.DefaultPath();
            }
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Sweeps the hand target over a 3D grid and solves BasisArmSolveCore at every " +
                "point WITH and WITHOUT an elbow hint tracker. One CSV row per (target, mode). " +
                "Same math the live rig runs.", MessageType.Info);

            EditorGUILayout.LabelField("Arm Geometry", EditorStyles.boldLabel);
            _cfg.UpperLength = EditorGUILayout.FloatField("Upper Length (m)", _cfg.UpperLength);
            _cfg.LowerLength = EditorGUILayout.FloatField("Lower Length (m)", _cfg.LowerLength);
            _cfg.IsLeft = EditorGUILayout.Toggle("Left Arm (mirror X)", _cfg.IsLeft);
            _cfg.RestElbowDir = EditorGUILayout.Vector3Field("Rest Elbow Dir", _cfg.RestElbowDir);
            _cfg.RestForearmDir = EditorGUILayout.Vector3Field("Rest Forearm Dir", _cfg.RestForearmDir);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Target Grid (fractions of arm length)", EditorStyles.boldLabel);
            _cfg.MinFrac = EditorGUILayout.Vector3Field("Min Frac", _cfg.MinFrac);
            _cfg.MaxFrac = EditorGUILayout.Vector3Field("Max Frac", _cfg.MaxFrac);
            _cfg.Steps = EditorGUILayout.Vector3IntField("Steps (X,Y,Z)", _cfg.Steps);
            int pts = Mathf.Max(1, _cfg.Steps.x) * Mathf.Max(1, _cfg.Steps.y) * Mathf.Max(1, _cfg.Steps.z);
            EditorGUILayout.LabelField("Points", pts + "  (" + (pts * 3) + " rows: nohint/hint/lookup)");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Elbow Tracker (hint pole)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("modes: nohint=raw, hint=WITH tracker, lookup=WITHOUT (production)", EditorStyles.miniLabel);
            _cfg.HintDir = EditorGUILayout.Vector3Field("Tracker Pole Dir", _cfg.HintDir);
            _cfg.HintDistanceFrac = EditorGUILayout.Slider("Tracker Distance Frac", _cfg.HintDistanceFrac, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Arm IK Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Reset to persistentDataPath"))
            {
                _path = BasisArmIKSweep.DefaultPath();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _last = BasisArmIKSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[ArmIKSweep] {_last.Rows} rows -> {_last.Path}");
                else Debug.LogError($"[ArmIKSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    EditorGUILayout.HelpBox(
                        $"Wrote {_last.Rows} rows ({_last.Points} points, {_last.ReachablePoints} reachable).\n" +
                        $"LOOKUP (no tracker): mean |swivel| {_last.LookupMeanAbsSwivelDeg:F1}°, elbow-up (chicken-wing) at {_last.LookupElbowUpCount} targets\n" +
                        $"TRACKER jitter: mean {_last.TrackerMeanSensDegPerCm:F1}°/cm, max {_last.TrackerMaxSensDegPerCm:F0}°/cm; jittery (>20°/cm) at {_last.TrackerJitteryCount}\n" +
                        $"TRACKER follow: mean align err {_last.TrackerMeanAlignErrDeg:F1}° (0=perfect), max {_last.TrackerMaxAlignErrDeg:F0}°; faded-out (reach>0.9) at {_last.TrackerFadedCount}\n" +
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

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Trajectory Scan (per-frame, between data)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("production lookup path swept along continuous hand motion; pops = pole-flip/discontinuity, rough/zigzag = jitter", EditorStyles.miniLabel);
            _trajNoise = EditorGUILayout.Slider("Tracking Noise (m)", _trajNoise, 0f, 0.01f);
            if (GUILayout.Button("Run Trajectory Scan", GUILayout.Height(26)))
            {
                string tp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_path), "BasisArmIKTrajectory.csv");
                _traj = BasisArmIKSweep.RunTrajectories(_cfg, _trajNoise, tp);
                _hasTraj = true;
                if (_traj.Ok) Debug.Log($"[ArmIKTraj] worst pop {_traj.WorstPopDeg:F0} deg, worst rough {_traj.WorstRoughDeg:F2} -> {_traj.Path}");
                else Debug.LogError($"[ArmIKTraj] failed: {_traj.Error}");
            }
            if (_hasTraj)
            {
                if (_traj.Ok && _traj.Results != null)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("path:  clean max-jump deg / pops  |  noisy rough deg / zigzag  |  singular jump deg");
                    foreach (var r in _traj.Results)
                    {
                        sb.AppendLine($"{r.Name}:  {r.CleanMaxJumpDeg:F1} / {r.Pops}  |  {r.NoisyRoughDeg:F2} / {r.Zigzags}  |  {r.SingularMaxJumpDeg:F0}");
                    }
                    sb.Append(_traj.Path);
                    EditorGUILayout.HelpBox(sb.ToString(), MessageType.None);
                    if (GUILayout.Button("Reveal Trajectory CSV"))
                    {
                        EditorUtility.RevealInFinder(_traj.Path);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Trajectory scan failed: " + _traj.Error, MessageType.Error);
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
