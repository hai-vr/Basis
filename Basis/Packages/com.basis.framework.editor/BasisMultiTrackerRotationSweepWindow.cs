using Basis.Scripts.Device_Management.Devices.Pairing;
using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Multi-Tracker Rotation Sweep. Scores the double-hip fusion's rotation against the
    // body's rotation since calibration, for several fusion conventions, so the data names the robust one.
    public class BasisMultiTrackerRotationSweepWindow : EditorWindow
    {
        BasisMultiTrackerRotationConfig _cfg = BasisMultiTrackerRotationConfig.Default();
        string _path;
        BasisMultiTrackerRotationSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        BasisMidpointFusionTunables _tun = BasisMidpointFusionTunables.Default();
        float _temporalFlex = 0f;
        string _temporalPath;
        BasisMultiTrackerRotationTemporalSummary _lastTemporal;
        bool _hasTemporal;

        [MenuItem("Basis/Debug/IK/Multi-Tracker Rotation Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisMultiTrackerRotationSweepWindow>("Multi-Tracker Rotation Sweep");
            w.minSize = new Vector2(460, 560);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisMultiTrackerRotationSweep.DefaultPath();
            if (string.IsNullOrEmpty(_temporalPath)) _temporalPath = BasisMultiTrackerRotationSweep.DefaultTemporalPath();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Two trackers strapped to the pelvis, fused into one virtual hip by BasisVirtualMidpointInput. " +
                "Scores Angle(Fuse(now)*Inverse(Fuse(cal)), bodyRotationSinceCal) over a body-orientation grid x blend ratios. " +
                "A correct fusion reads 0 (the calibration offset cancels). The 'current, re-prime' convention models a " +
                "device-churn recreate re-priming the half-rest at a flexed pose after calibration -- the suspected break. " +
                "Same BasisMidpointRotationFusion the live pair runs.",
                MessageType.Info);

            EditorGUILayout.LabelField("Body Orientation Grid", EditorStyles.boldLabel);
            _cfg.YawSteps = EditorGUILayout.IntSlider("Yaw Steps", _cfg.YawSteps, 4, 144);
            _cfg.PitchSteps = EditorGUILayout.IntSlider("Pitch Steps", _cfg.PitchSteps, 1, 21);
            _cfg.RollSteps = EditorGUILayout.IntSlider("Roll Steps", _cfg.RollSteps, 1, 21);
            _cfg.PitchMaxDeg = EditorGUILayout.Slider("Pitch Max (deg)", _cfg.PitchMaxDeg, 0f, 89f);
            _cfg.RollMaxDeg = EditorGUILayout.Slider("Roll Max (deg)", _cfg.RollMaxDeg, 0f, 89f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Mounting & Flex", EditorStyles.boldLabel);
            _cfg.MountYawDeg = EditorGUILayout.Slider("Mount Yaw Spread (deg)", _cfg.MountYawDeg, 0f, 90f);
            _cfg.MountRollDeg = EditorGUILayout.Slider("Mount Roll Spread (deg)", _cfg.MountRollDeg, 0f, 90f);
            _cfg.FlexDeg = EditorGUILayout.Slider("Strap Flex (deg)", _cfg.FlexDeg, 0f, 30f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Calibration vs Re-Prime Pose", EditorStyles.boldLabel);
            _cfg.CalEuler = EditorGUILayout.Vector3Field("Calibration Euler", _cfg.CalEuler);
            _cfg.RePrimeEuler = EditorGUILayout.Vector3Field("Re-Prime Euler", _cfg.RePrimeEuler);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Blend", EditorStyles.boldLabel);
            _cfg.TSteps = EditorGUILayout.IntSlider("Blend Ratio Steps", _cfg.TSteps, 1, 11);
            _cfg.TCap = EditorGUILayout.Slider("Blend At Calibration", _cfg.TCap, 0f, 1f);
            _cfg.OnsetThresholdDeg = EditorGUILayout.Slider("Onset Threshold (deg)", _cfg.OnsetThresholdDeg, 0.5f, 30f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Multi-Tracker Rotation Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Reset to persistentDataPath")) _path = BasisMultiTrackerRotationSweep.DefaultPath();

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _last = BasisMultiTrackerRotationSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[MultiTrackerRotationSweep] {_last.Rows} rows -> {_last.Path}");
                else Debug.LogError($"[MultiTrackerRotationSweep] failed: {_last.Error}");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Temporal Sim (no play mode — simulates motion frame-by-frame)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drives synthetic turns/leans through the REAL stateful fusion (BasisMidpointFusionCore.Step) frame-by-frame -- " +
                "the dynamic lag/snap the static sweep above can't see, with NO play mode. A single tracker tracks the body at " +
                "~0 error, so whatever shows here is what the pairing low-pass + confidence blend ADD. PairingRotationHalfLife " +
                "(the prime suspect) is exposed below -- set it to 0 to A/B the redundant low-pass.",
                MessageType.Info);
            _tun.RotationHalfLife = EditorGUILayout.Slider("Rotation Half-Life (s)", _tun.RotationHalfLife, 0f, 0.3f);
            _temporalFlex = EditorGUILayout.Slider("Sim Strap Flex (deg)", _temporalFlex, 0f, 30f);
            if (GUILayout.Button("Run Temporal Sim", GUILayout.Height(28)))
            {
                _lastTemporal = BasisMultiTrackerRotationSweep.RunTemporal(_cfg, _tun, 1f / 90f, _temporalFlex, _temporalPath);
                _hasTemporal = true;
                if (_lastTemporal.Ok) Debug.Log($"[MultiTrackerRotationTemporal] {_lastTemporal.Frames} frames -> {_lastTemporal.Path}");
                else Debug.LogError($"[MultiTrackerRotationTemporal] failed: {_lastTemporal.Error}");
            }
            if (_hasTemporal && _lastTemporal.Ok)
            {
                var gt = BasisIKTestGates.GateMultiTrackerRotationTemporal(_lastTemporal);
                var pt = GUI.color;
                GUI.color = gt.pass ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
                EditorGUILayout.LabelField(gt.pass ? "PASS" : "FAIL", EditorStyles.boldLabel);
                GUI.color = pt;
                EditorGUILayout.HelpBox(gt.reason, MessageType.None);
                EditorGUILayout.LabelField("Per-trajectory (deg): trackErr / lag / overshoot / snap / blendDev / settleFrames", EditorStyles.miniBoldLabel);
                for (int i = 0; i < _lastTemporal.TrajNames.Length; i++)
                {
                    EditorGUILayout.LabelField(
                        $"{_lastTemporal.TrajNames[i]}: {_lastTemporal.MaxTrackErrDeg[i]:F1} / {_lastTemporal.MaxLagDeg[i]:F1} / {_lastTemporal.MaxOvershootDeg[i]:F1} / {_lastTemporal.Max2ndDiffDeg[i]:F1} / {_lastTemporal.MaxBlendDevFromHalf[i]:F2} / {_lastTemporal.SettleFrames[i]:F0}");
                }
                if (GUILayout.Button("Reveal Temporal CSV")) EditorUtility.RevealInFinder(_lastTemporal.Path);
            }
            else if (_hasTemporal)
            {
                EditorGUILayout.HelpBox("Temporal sim failed: " + _lastTemporal.Error, MessageType.Error);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Live capture (a reality check against real hardware): ENTER PLAY with the linked pair on, " +
                "click below, then turn/lean for ~10s. Writes per-frame partner vs fused vs hip-bone rotation deltas " +
                "(+ confidence weights/blend) to persistentDataPath/PairingRotation. Reading it: ratio_fused_a~2 = the " +
                "fusion doubles; ratio_bone_a~2 with ratio_fused_a~1 = a downstream (offset/calibration) double; a step in " +
                "ang_fused while wA/wB/t jump = a confidence-blend snap on the turn.",
                MessageType.None);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Capture Live Pairing Rotation (10s)"))
                {
                    Basis.Scripts.Drivers.BasisPairingRotationRecorder.RequestCapture(900);
                }
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateMultiTrackerRotation(_last);
                    var prev = GUI.color;
                    GUI.color = g.pass ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
                    EditorGUILayout.LabelField(g.pass ? "PASS" : "FAIL", EditorStyles.boldLabel);
                    GUI.color = prev;
                    EditorGUILayout.HelpBox(g.reason, MessageType.None);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Per-convention (deg): rigidWorst / flexWorst / mean / t-spread / onset", EditorStyles.miniBoldLabel);
                    for (int c = 0; c < _last.ConvNames.Length; c++)
                    {
                        string onset = float.IsNaN(_last.OnsetYawDeg[c]) ? "never" : $"{_last.OnsetYawDeg[c]:F0}°yaw";
                        string tag = c == _last.BestConvIndex ? "  ← best" : (c == _last.CurrentConvIndex ? "  (current)" : "");
                        var p2 = GUI.color;
                        if (c == _last.BestConvIndex) GUI.color = new Color(0.6f, 1f, 0.6f);
                        else if (c == _last.CurrentConvIndex) GUI.color = new Color(1f, 0.8f, 0.5f);
                        EditorGUILayout.LabelField(
                            $"{_last.ConvNames[c]}: {_last.RigidWorstErrDeg[c]:F1} / {_last.FlexWorstErrDeg[c]:F1} / {_last.MeanErrDeg[c]:F1} / {_last.TSpreadDeg[c]:F1} / {onset}{tag}");
                        GUI.color = p2;
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
                    if (GUILayout.Button("Copy Path")) EditorGUIUtility.systemCopyBuffer = _last.Path;
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
