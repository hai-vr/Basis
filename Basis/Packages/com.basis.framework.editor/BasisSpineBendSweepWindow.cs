using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Spine Bend Sweep.
    public class BasisSpineBendSweepWindow : EditorWindow
    {
        BasisSpineBendSweepConfig _cfg = BasisSpineBendSweepConfig.Default();
        string _path;
        BasisSpineBendSweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Spine Bend Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisSpineBendSweepWindow>("Spine Bend Sweep");
            w.minSize = new Vector2(360, 400);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisSpineBendSweep.DefaultPath();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Sweeps the per-axis spine/upperChest bend distribution. Pass A checks the asymmetric clamp, " +
                "rest deadband, squish range and the 25/75 yaw split; pass B scans head yaw across center with " +
                "a yawed hips bind to guard the twist branch-cut fix (no ±360 snap); pass D scans head pitch " +
                "through vertical to guard the look-down fade (chest must not snap sideways); pass E checks that " +
                "fade is twist-only (the forward bend must not drift as the gaze pitches). Same math as DistributeSpineBend.",
                MessageType.Info);

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            _cfg.HeadGridSteps = EditorGUILayout.IntSlider("Head Grid Steps (per axis)", _cfg.HeadGridSteps, 3, 13);
            _cfg.TwistYawSteps = EditorGUILayout.IntSlider("Twist Scan Steps", _cfg.TwistYawSteps, 41, 721);
            long gridCases = 4L * 4L * 3L * _cfg.HeadGridSteps * _cfg.HeadGridSteps * _cfg.HeadGridSteps;
            EditorGUILayout.LabelField("Grid cases", gridCases.ToString("n0"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Spine Bend Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _last = BasisSpineBendSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[SpineBendSweep] {_last.Cases} cases -> {_last.Path}");
                else Debug.LogError($"[SpineBendSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateSpineBend(_last);
                    EditorGUILayout.HelpBox(
                        (g.pass ? "PASS  " : "FAIL  ") + g.reason + "\n" +
                        $"cases={_last.Cases} nan={_last.NaNCount} fails={_last.Failures}\n" +
                        $"clampOver={_last.MaxClampOverDeg:F3}° deadband={_last.MaxDeadbandLeakDeg:F3}° " +
                        $"squishErr={_last.MaxSquishErr:F4} yawSplit={_last.MaxYawSplitErr:F4} twistJump={_last.TwistMaxJumpDeg:F2}° lookDownJump={_last.LookDownTwistMaxJumpDeg:F2}° bendDrift={_last.LookDownBendDriftDeg:F2}°\n" +
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
