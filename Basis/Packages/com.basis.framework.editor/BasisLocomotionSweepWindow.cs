using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Locomotion Sweep.
    public class BasisLocomotionSweepWindow : EditorWindow
    {
        BasisLocomotionSweepConfig _cfg = BasisLocomotionSweepConfig.Default();
        string _path;
        BasisLocomotionSweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Locomotion Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisLocomotionSweepWindow>("Locomotion Sweep");
            w.minSize = new Vector2(360, 440);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisLocomotionSweep.DefaultPath();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Pure-kinematic sweep of the locomotion vertical model (BasisLocalCharacterDriver / " +
                "BasisWalkMovementMode). Re-implements the constants-derived math (the live path is " +
                "CharacterController-coupled): ballistic jump apex must match jumpHeight, the coyote-time " +
                "window must equal its configured duration, and the landing-recovery dip must ease in then " +
                "recover monotonically to rest. No colliders / scene physics.",
                MessageType.Info);

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            _cfg.Fps = EditorGUILayout.Slider("Fixed FPS", _cfg.Fps, 20f, 120f);
            _cfg.JumpHeight = EditorGUILayout.Slider("Jump Height (m)", _cfg.JumpHeight, 0.1f, 3f);
            _cfg.Gravity = EditorGUILayout.Slider("Gravity (m/s²)", _cfg.Gravity, -30f, -1f);
            _cfg.CoyoteTimeDuration = EditorGUILayout.Slider("Coyote Time (s)", _cfg.CoyoteTimeDuration, 0f, 0.5f);
            _cfg.LandingDescentSpeed = EditorGUILayout.Slider("Landing Descent Speed", _cfg.LandingDescentSpeed, 1f, 30f);
            _cfg.LandingRecoverySpeed = EditorGUILayout.Slider("Landing Recovery Speed", _cfg.LandingRecoverySpeed, 1f, 20f);
            _cfg.LandingImpactScale = EditorGUILayout.Slider("Landing Impact Scale", _cfg.LandingImpactScale, 0f, 0.2f);
            _cfg.MaxLandingCrouch = EditorGUILayout.Slider("Max Landing Crouch (m)", _cfg.MaxLandingCrouch, 0.05f, 1f);
            _cfg.LandingFallSpeed = EditorGUILayout.Slider("Landing Fall Speed (m/s)", _cfg.LandingFallSpeed, 0f, 30f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Locomotion Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _last = BasisLocomotionSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[LocomotionSweep] {_last.Steps} steps -> {_last.Path}");
                else Debug.LogError($"[LocomotionSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateLocomotion(_last);
                    EditorGUILayout.HelpBox(
                        (g.pass ? "PASS  " : "FAIL  ") + g.reason + "\n" +
                        $"steps={_last.Steps} nan={_last.NaNCount}\n" +
                        $"jump apex={_last.SimApexHeightM:F3}m vs target={_last.ConfiguredJumpHeightM:F3}m (err {_last.ApexErrM * 1000f:F1}mm, v0={_last.LaunchVelocity:F2}m/s)\n" +
                        $"coyote measured={_last.MeasuredCoyoteS * 1000f:F0}ms vs {_last.ConfiguredCoyoteS * 1000f:F0}ms (err {_last.CoyoteErrS * 1000f:F0}ms)\n" +
                        $"landing dip={_last.LandingPeakDipM * 1000f:F0}mm target={_last.LandingPeakTargetM * 1000f:F0}mm reversal={_last.LandingRecoveryReversalM * 1000f:F2}mm settle={_last.LandingSettleTimeS:F2}s settled={_last.LandingSettled}\n" +
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
