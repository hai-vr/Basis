using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Bone Sim Stability Sweep.
    public class BasisBoneSimStabilitySweepWindow : EditorWindow
    {
        BasisBoneSimStabilitySweepConfig _cfg = BasisBoneSimStabilitySweepConfig.Default();
        string _path;
        BasisBoneSimStabilitySweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Bone Sim Stability Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisBoneSimStabilitySweepWindow>("Bone Sim Stability Sweep");
            w.minSize = new Vector2(380, 460);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisBoneSimStabilitySweep.DefaultPath();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Stability/correctness sweep of the secondary bone-chain sim (BasisBoneSimChainJob). Ticks the " +
                "real Burst job over scripted tracker poses -- yaw step (0->45°), position step (0->0.5m), and " +
                "sustained oscillation -- and checks the feedback recurrence stays sane: no NaN/Inf, quaternion " +
                "stays unit with no energy gain, bounded position lag, and it settles (no self-oscillation). " +
                "MAIN-THREAD ONLY (allocates/ticks/disposes Burst-job NativeArrays).",
                MessageType.Info);

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            _cfg.Fps = EditorGUILayout.Slider("FPS", _cfg.Fps, 15f, 144f);
            _cfg.Seconds = EditorGUILayout.Slider("Seconds", _cfg.Seconds, 2f, 12f);

            EditorGUILayout.LabelField("Smoothing tunables (BasisLocalBoneControl)", EditorStyles.miniBoldLabel);
            _cfg.TrackerSmooth = EditorGUILayout.Slider("Tracker Smooth", _cfg.TrackerSmooth, 0.05f, 25f);
            _cfg.PositionLerpAmount = EditorGUILayout.Slider("Position Lerp Rate", _cfg.PositionLerpAmount, 1f, 80f);
            _cfg.QuaternionLerp = EditorGUILayout.Slider("Quaternion Lerp", _cfg.QuaternionLerp, 1f, 80f);
            _cfg.QuaternionLerpFastMovement = EditorGUILayout.Slider("Quaternion Lerp Fast", _cfg.QuaternionLerpFastMovement, 1f, 120f);
            _cfg.AngleBeforeSpeedup = EditorGUILayout.Slider("Angle Before Speedup (deg)", _cfg.AngleBeforeSpeedup, 1f, 90f);

            EditorGUILayout.LabelField("Drive", EditorStyles.miniBoldLabel);
            _cfg.StepAngleDeg = EditorGUILayout.Slider("Step Angle (deg)", _cfg.StepAngleDeg, 5f, 170f);
            _cfg.StepPositionM = EditorGUILayout.Slider("Step Position (m)", _cfg.StepPositionM, 0.05f, 2f);
            _cfg.OscAngleDeg = EditorGUILayout.Slider("Osc Angle (deg)", _cfg.OscAngleDeg, 1f, 90f);
            _cfg.OscPositionM = EditorGUILayout.Slider("Osc Position (m)", _cfg.OscPositionM, 0.02f, 1f);
            _cfg.OscHz = EditorGUILayout.Slider("Osc Frequency (Hz)", _cfg.OscHz, 0.25f, 6f);
            _cfg.ChildOffsetM = EditorGUILayout.Slider("Child Offset (m)", _cfg.ChildOffsetM, 0.02f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Bone Sim Stability Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _last = BasisBoneSimStabilitySweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[BoneSimStabilitySweep] {_last.Steps} steps x {_last.Scenarios} scenarios -> {_last.Path}");
                else Debug.LogError($"[BoneSimStabilitySweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateBoneSim(_last);
                    EditorGUILayout.HelpBox(
                        (g.pass ? "PASS  " : "FAIL  ") + g.reason + "\n" +
                        $"steps={_last.Steps} scenarios={_last.Scenarios} nan={_last.NaNCount}\n" +
                        $"quatNonUnit={_last.MaxQuatNonUnit:F5} energyGain={_last.MaxQuatEnergyGain:F3}\n" +
                        $"rootLag={_last.MaxRootPosLagM * 1000f:F1}mm childLag={_last.MaxChildPosLagM * 1000f:F1}mm\n" +
                        $"stepSettle={_last.StepSettleErrDeg:F2}° / {_last.StepSettleErrM * 1000f:F1}mm\n" +
                        $"oscResidual={_last.OscResidualDeg:F2}° / {_last.OscResidualM * 1000f:F1}mm settleRatio={_last.SettleRatio:F2}\n" +
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
