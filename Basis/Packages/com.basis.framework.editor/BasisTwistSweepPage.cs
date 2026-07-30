using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisTwistSweepPage : BasisIKSweepPage
    {
        public override string Group => "Arm & Elbow";
        public override string Title => "Twist Sweep";
        public override int Order => 60;
        public override string Description =>
            "Exercises BasisTwistSolveCore (swing-twist decomposition for forearm/arm twist): a pure " +
            "twist about the bone axis must be recovered exactly, the Fraction blends it linearly, a " +
            "perpendicular swing must not tilt the extracted twist axis, and singular inputs no-op. " +
            "Edit mode, pure math.";

        BasisTwistSweepConfig _cfg = BasisTwistSweepConfig.Default();
        string _path;
        BasisTwistSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisTwistSweep.DefaultPath();
        }

        public override void Draw()
        {
            _cfg.Cases = EditorGUILayout.IntSlider("Cases", _cfg.Cases, 100, 50000);

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 30f))
            {
                _last = BasisTwistSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[TwistSweep] {_last.Cases} cases -> {_last.Path}");
                else Debug.LogError($"[TwistSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var gate = BasisIKTestGates.GateTwist(_last);
                    BasisEditorUI.PassFail(gate.pass, gate.reason);

                    BasisEditorUI.Readout(
                        $"{_last.Cases} cases.\n" +
                        $"Pure-twist recovery err: {_last.MaxPureTwistErrDeg:F3}°\n" +
                        $"Fraction blend err: {_last.MaxFractionErrDeg:F3}°\n" +
                        $"Axis misalign under swing: {_last.MaxAxisMisalignDeg:F2}°\n" +
                        $"Singularity failures: {_last.SingularityFailures}\n" +
                        _last.Path);
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
