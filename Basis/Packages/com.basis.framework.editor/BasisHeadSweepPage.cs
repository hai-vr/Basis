using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisHeadSweepPage : BasisIKSweepPage
    {
        public override string Group => "Spine & Torso";
        public override string Title => "Head Sweep";
        public override int Order => 90;
        public override string Description =>
            "Sweeps head gaze pitch and records the cervical-lordosis response (neck/upper-chest " +
            "bend, extreme-region hips/chest offsets, head-pitch clamp). Same math as the live rig. " +
            "Defaults match the production FBIK settings.";

        BasisHeadSweepConfig _cfg = BasisHeadSweepConfig.Default();
        string _path;
        BasisHeadSweepSummary _last;
        bool _hasResult;
        float _trajNoise = 0.3f;
        BasisHeadSweep.BasisHeadTrajectorySummary _traj;
        bool _hasTraj;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisHeadSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Sweep");
            _cfg.PitchMin = EditorGUILayout.FloatField("Pitch Min (deg, +=down)", _cfg.PitchMin);
            _cfg.PitchMax = EditorGUILayout.FloatField("Pitch Max (deg)", _cfg.PitchMax);
            _cfg.PitchSteps = EditorGUILayout.IntField("Pitch Steps", _cfg.PitchSteps);
            _cfg.Yaw = EditorGUILayout.FloatField("Yaw (deg)", _cfg.Yaw);
            _cfg.HasUpperChest = EditorGUILayout.Toggle("Has UpperChest", _cfg.HasUpperChest);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Lordosis Params (FBIK defaults)");
            _cfg.BaseDeg = EditorGUILayout.FloatField("Base Deg", _cfg.BaseDeg);
            _cfg.PitchGainDeg = EditorGUILayout.FloatField("Pitch Gain Deg", _cfg.PitchGainDeg);
            _cfg.NeckShare = EditorGUILayout.Slider("Neck Share", _cfg.NeckShare, 0f, 1f);
            _cfg.MaxHeadPitchDeg = EditorGUILayout.FloatField("Max Head Pitch Deg", _cfg.MaxHeadPitchDeg);
            _cfg.ExtremeStartDeg = EditorGUILayout.FloatField("Extreme Start Deg", _cfg.ExtremeStartDeg);
            _cfg.ExtremeFullDeg = EditorGUILayout.FloatField("Extreme Full Deg", _cfg.ExtremeFullDeg);
            _cfg.ExtremeRollForwardMaxDeg = EditorGUILayout.FloatField("Extreme Roll Fwd Max", _cfg.ExtremeRollForwardMaxDeg);
            _cfg.ExtremeRollBackwardMaxDeg = EditorGUILayout.FloatField("Extreme Roll Back Max", _cfg.ExtremeRollBackwardMaxDeg);
            _cfg.ExtremeHipsHorizontalMax = EditorGUILayout.FloatField("Extreme Hips Horiz Max", _cfg.ExtremeHipsHorizontalMax);
            _cfg.ExtremeChestHorizontalMax = EditorGUILayout.FloatField("Extreme Chest Horiz Max", _cfg.ExtremeChestHorizontalMax);
            _cfg.ExtremeHipsDownMax = EditorGUILayout.FloatField("Extreme Hips Down Max", _cfg.ExtremeHipsDownMax);
            _cfg.ExtremeChestDownMax = EditorGUILayout.FloatField("Extreme Chest Down Max", _cfg.ExtremeChestDownMax);
            _cfg.ExtremeHipsDownLookUp = EditorGUILayout.FloatField("Extreme Hips Down LookUp", _cfg.ExtremeHipsDownLookUp);
            _cfg.ExtremeChestDownLookUp = EditorGUILayout.FloatField("Extreme Chest Down LookUp", _cfg.ExtremeChestDownLookUp);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Head Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisHeadSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[HeadSweep] {_last.Rows} rows -> {_last.Path}");
                else Debug.LogError($"[HeadSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    EditorGUILayout.HelpBox(
                        $"Wrote {_last.Rows} rows.\n" +
                        $"max neck bend {_last.MaxNeckDeg:F1}°; extreme region onsets at |pitch| {_last.ExtremeOnsetPitch:F0}°; " +
                        $"head clamp engages at |pitch| {_last.ClampOnsetPitch:F0}°\n{_last.Path}", MessageType.None);
                    if (BasisEditorUI.SecondaryButton("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
                }
                else
                {
                    BasisEditorUI.Help("Sweep failed: " + _last.Error, MessageType.Error);
                }
            }

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Trajectory Scan (per-frame, between data)");
            EditorGUILayout.LabelField("nods pitch continuously; pops = kinks in the neck/chest bend, rough/zigzag = jitter under head-pitch noise", EditorStyles.miniLabel);
            _trajNoise = EditorGUILayout.Slider("Pitch Noise (deg)", _trajNoise, 0f, 2f);
            if (BasisEditorUI.PrimaryButton("Run Trajectory Scan", 26f))
            {
                string tp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_path), "BasisHeadTrajectory.csv");
                _traj = BasisHeadSweep.RunTrajectories(_cfg, _trajNoise, tp);
                _hasTraj = true;
                if (_traj.Ok) Debug.Log($"[HeadTraj] worst pop {_traj.WorstPopDeg:F1} deg, worst rough {_traj.WorstRoughDeg:F2} -> {_traj.Path}");
                else Debug.LogError($"[HeadTraj] failed: {_traj.Error}");
            }
            if (_hasTraj)
            {
                if (_traj.Ok && _traj.Results != null)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("output:  clean max-jump deg / pops (worst pitch)  |  noisy rough deg / zigzag");
                    foreach (var r in _traj.Results)
                    {
                        sb.AppendLine($"{r.Name}:  {r.CleanMaxJumpDeg:F2} / {r.Pops} (@ {r.WorstJumpTarget.x:F0}deg)  |  {r.NoisyRoughDeg:F3} / {r.Zigzags}");
                    }
                    sb.Append(_traj.Path);
                    BasisEditorUI.Readout(sb.ToString());
                    if (GUILayout.Button("Reveal Trajectory CSV")) EditorUtility.RevealInFinder(_traj.Path);
                }
                else
                {
                    BasisEditorUI.Help("Trajectory scan failed: " + _traj.Error, MessageType.Error);
                }
            }

        }
    }
}
