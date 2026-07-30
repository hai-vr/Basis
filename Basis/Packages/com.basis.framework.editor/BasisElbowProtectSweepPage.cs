using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisElbowProtectSweepPage : BasisIKSweepPage
    {
        public override string Group => "Arm & Elbow";
        public override string Title => "Elbow Protect Sweep";
        public override int Order => 30;
        public override string Description =>
            "Sweeps the hand target over a 3D grid against a fixed torso and runs the SAME " +
            "BasisElbowProtectCore the live rig runs after the arm solve. Reports how hard the " +
            "push swings the elbow (chicken-wing), how often it flips across the body (snap), and " +
            "how much the final elbow swivel moves per cm of hand motion (twitch).";

        BasisElbowProtectSweepConfig _cfg = BasisElbowProtectSweepConfig.Default();
        string _path;
        BasisElbowProtectSweepSummary _last;
        bool _hasResult;
        float _trajNoise = 0.003f;
        BasisElbowProtectSweep.BasisElbowProtectTrajectorySummary _traj;
        bool _hasTraj;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path))
            {
                _path = BasisElbowProtectSweep.DefaultPath();
            }
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Arm Geometry");
            _cfg.UpperLength = EditorGUILayout.FloatField("Upper Length (m)", _cfg.UpperLength);
            _cfg.LowerLength = EditorGUILayout.FloatField("Lower Length (m)", _cfg.LowerLength);
            _cfg.IsLeft = EditorGUILayout.Toggle("Left Arm (mirror X)", _cfg.IsLeft);
            _cfg.RestElbowDir = EditorGUILayout.Vector3Field("Rest Elbow Dir", _cfg.RestElbowDir);
            _cfg.RestForearmDir = EditorGUILayout.Vector3Field("Rest Forearm Dir", _cfg.RestForearmDir);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Torso (capsule heights on +Y, radii from chest base)");
            _cfg.HipsHeight = EditorGUILayout.FloatField("Hips Height", _cfg.HipsHeight);
            _cfg.SpineHeight = EditorGUILayout.FloatField("Spine Height", _cfg.SpineHeight);
            _cfg.ChestHeight = EditorGUILayout.FloatField("Chest Height", _cfg.ChestHeight);
            _cfg.NeckHeight = EditorGUILayout.FloatField("Neck Height", _cfg.NeckHeight);
            _cfg.ChestRadiusBase = EditorGUILayout.FloatField("Chest Radius Base", _cfg.ChestRadiusBase);
            _cfg.CollisionSkin = EditorGUILayout.FloatField("Collision Skin", _cfg.CollisionSkin);
            _cfg.HandRadius = EditorGUILayout.FloatField("Hand Radius", _cfg.HandRadius);
            _cfg.HandSkin = EditorGUILayout.FloatField("Hand Skin", _cfg.HandSkin);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Shoulder Placement (right side; mirrored for left)");
            _cfg.ShoulderSide = EditorGUILayout.FloatField("Shoulder Side (x)", _cfg.ShoulderSide);
            _cfg.ShoulderHeight = EditorGUILayout.FloatField("Shoulder Height (y)", _cfg.ShoulderHeight);
            _cfg.ShoulderForward = EditorGUILayout.FloatField("Shoulder Forward (z)", _cfg.ShoulderForward);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Target Grid (fractions of arm length)");
            _cfg.MinFrac = EditorGUILayout.Vector3Field("Min Frac", _cfg.MinFrac);
            _cfg.MaxFrac = EditorGUILayout.Vector3Field("Max Frac", _cfg.MaxFrac);
            _cfg.Steps = EditorGUILayout.Vector3IntField("Steps (X,Y,Z)", _cfg.Steps);
            int pts = Mathf.Max(1, _cfg.Steps.x) * Mathf.Max(1, _cfg.Steps.y) * Mathf.Max(1, _cfg.Steps.z);
            EditorGUILayout.LabelField("Points", pts.ToString());

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Elbow Protect Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();
            if (BasisEditorUI.SecondaryButton("Reset to persistentDataPath"))
            {
                _path = BasisElbowProtectSweep.DefaultPath();
            }

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisElbowProtectSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[ElbowProtectSweep] {_last.Rows} rows -> {_last.Path}");
                else Debug.LogError($"[ElbowProtectSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    EditorGUILayout.HelpBox(
                        $"Wrote {_last.Rows} rows ({_last.Points} points, {_last.ReachablePoints} reachable, {_last.EngagedPoints} engaged).\n" +
                        $"CLEARANCE: cleared {_last.ClearedPoints}/{_last.EngagedPoints}; mean residual {_last.MeanResidualPenMm:F0}mm, max {_last.MaxResidualPenMm:F0}mm; could-not-clear {_last.WrongSideFlipCount}\n" +
                        $"SWING: mean {_last.MeanSwingDeg:F1}°, max {_last.MaxSwingDeg:F0}°; pushed-up {_last.ElbowUpCount}; mean shift {_last.MeanProtectShiftDeg:F1}°\n" +
                        $"TWITCH: mean {_last.MeanSensDegPerCm:F1}°/cm, max {_last.MaxSensDegPerCm:F0}°/cm; jittery (>20°/cm) at {_last.JitteryCount}\n" +
                        _last.Path, MessageType.None);
                    EditorGUILayout.BeginHorizontal();
                    if (BasisEditorUI.SecondaryButton("Reveal CSV"))
                    {
                        EditorUtility.RevealInFinder(_last.Path);
                    }
                    if (BasisEditorUI.SecondaryButton("Copy Path"))
                    {
                        EditorGUIUtility.systemCopyBuffer = _last.Path;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    BasisEditorUI.Help("Sweep failed: " + _last.Error, MessageType.Error);
                }
            }

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Trajectory Scan (per-frame, between data)");
            EditorGUILayout.LabelField("sweeps the hand along continuous paths; pops = discontinuities crossed, rough/zigzag = jitter under noise", EditorStyles.miniLabel);
            _trajNoise = EditorGUILayout.Slider("Tracking Noise (m)", _trajNoise, 0f, 0.01f);
            if (BasisEditorUI.PrimaryButton("Run Trajectory Scan", 26f))
            {
                string tp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_path), "BasisElbowProtectTrajectory.csv");
                _traj = BasisElbowProtectSweep.RunTrajectories(_cfg, _trajNoise, tp);
                _hasTraj = true;
                if (_traj.Ok) Debug.Log($"[ElbowProtectTraj] worst pop {_traj.WorstPopDeg:F0} deg, worst rough {_traj.WorstRoughDeg:F2} -> {_traj.Path}");
                else Debug.LogError($"[ElbowProtectTraj] failed: {_traj.Error}");
            }
            if (_hasTraj)
            {
                if (_traj.Ok && _traj.Results != null)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("path:  clean max-jump deg / pops  |  noisy rough deg / zigzag");
                    foreach (var r in _traj.Results)
                    {
                        sb.AppendLine($"{r.Name}:  {r.CleanMaxJumpDeg:F1} / {r.Pops}  |  {r.NoisyRoughDeg:F2} / {r.Zigzags}");
                    }
                    sb.Append(_traj.Path);
                    BasisEditorUI.Readout(sb.ToString());
                    if (GUILayout.Button("Reveal Trajectory CSV"))
                    {
                        EditorUtility.RevealInFinder(_traj.Path);
                    }
                }
                else
                {
                    BasisEditorUI.Help("Trajectory scan failed: " + _traj.Error, MessageType.Error);
                }
            }

        }
    }
}
