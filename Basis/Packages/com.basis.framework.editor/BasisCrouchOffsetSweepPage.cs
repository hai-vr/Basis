using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisCrouchOffsetSweepPage : BasisIKSweepPage
    {
        public override string Group => "Spine & Torso";
        public override string Title => "Crouch Offset Sweep";
        public override int Order => 80;
        public override string Description =>
            "Sweeps crouch depth against the sit-back. Verifies the hips slide back by exactly the " +
            "corpus curve, land on the rest-length sphere once engaged, leak nothing sideways, grow " +
            "monotonically, and never move while standing, below the deadzone, or disabled. Same math " +
            "as the live ApplyCrouchBodyOffset.";

        BasisCrouchOffsetSweepConfig _cfg = BasisCrouchOffsetSweepConfig.Default();
        string _path;
        BasisCrouchOffsetSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisCrouchOffsetSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Configuration");
            _cfg.DepthSteps = EditorGUILayout.IntSlider("Depth Steps", _cfg.DepthSteps, 5, 91);
            _cfg.YawSteps = EditorGUILayout.IntSlider("Yaw Steps", _cfg.YawSteps, 1, 16);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Crouch Offset Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisCrouchOffsetSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[CrouchOffsetSweep] {_last.Cases} cases -> {_last.Path}");
                else Debug.LogError($"[CrouchOffsetSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateCrouchOffset(_last);
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"cases={_last.Cases} applied={_last.AppliedCases} nan={_last.NaNCount} fails={_last.Failures}\n" +
                        $"magErr={_last.MaxMagErrM:F5}m sphere={_last.MaxSphereErrM:F5}m lat={_last.MaxLateralLeakM:F5}m " +
                        $"standingMoves={_last.StandingMoves} mono={_last.MonotonicViolations}\n" + _last.Path);
                    if (BasisEditorUI.SecondaryButton("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
                }
                else
                {
                    BasisEditorUI.Help("Sweep failed: " + _last.Error, MessageType.Error);
                }
            }

        }
    }
}
