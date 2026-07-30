using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisRemoteBoneSweepPage : BasisIKSweepPage
    {
        public override string Group => "Tracking & Calibration";
        public override string Title => "Remote Bone Sweep";
        public override int Order => 50;
        public override string Description =>
            "Exercises the remote-avatar head-chain forward kinematics (BasisRemoteBoneMath, the real " +
            "BasisRemoteBoneJob math): neck→chest→spine + derived eye/mouth off the networked head pose. " +
            "Asserts each child composes as parent + headRot*offset, segment lengths are rotation-preserved " +
            "and scale linearly, and nothing NaNs. Edit mode, pure math.\n\n" +
            "Note: remote HAND/FOOT drift (the known sliding) is a play-mode measurement, not covered here.";

        BasisRemoteBoneSweepConfig _cfg = BasisRemoteBoneSweepConfig.Default();
        string _path;
        BasisRemoteBoneSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisRemoteBoneSweep.DefaultPath();
        }

        public override void Draw()
        {
            _cfg.Cases = EditorGUILayout.IntSlider("Cases", _cfg.Cases, 100, 50000);

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 30f))
            {
                _last = BasisRemoteBoneSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[RemoteBoneSweep] {_last.Cases} cases -> {_last.Path}");
                else Debug.LogError($"[RemoteBoneSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var gate = BasisIKTestGates.GateRemoteBone(_last);
                    BasisEditorUI.PassFail(gate.pass, gate.reason);

                    BasisEditorUI.Readout(
                        $"{_last.Cases} cases.\n" +
                        $"Composition err: {_last.MaxCompErr:E2} m\n" +
                        $"Segment-length err: {_last.MaxSegLenErr:E2} m\n" +
                        $"Scale-linearity err: {_last.MaxScaleErr:E2} m\n" +
                        $"Non-finite outputs: {_last.NaNCount}\n" +
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
