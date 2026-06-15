using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Remote Bone Sweep.
    public class BasisRemoteBoneSweepWindow : EditorWindow
    {
        BasisRemoteBoneSweepConfig _cfg = BasisRemoteBoneSweepConfig.Default();
        string _path;
        BasisRemoteBoneSummary _last;
        bool _hasResult;

        [MenuItem("Basis/Debug/IK/Remote Bone Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisRemoteBoneSweepWindow>("Remote Bone Sweep");
            w.minSize = new Vector2(400, 320);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisRemoteBoneSweep.DefaultPath();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Exercises the remote-avatar head-chain forward kinematics (BasisRemoteBoneMath, the real " +
                "BasisRemoteBoneJob math): neck→chest→spine + derived eye/mouth off the networked head pose. " +
                "Asserts each child composes as parent + headRot*offset, segment lengths are rotation-preserved " +
                "and scale linearly, and nothing NaNs. Edit mode, pure math.\n\n" +
                "Note: remote HAND/FOOT drift (the known sliding) is a play-mode measurement, not covered here.",
                MessageType.Info);

            _cfg.Cases = EditorGUILayout.IntSlider("Cases", _cfg.Cases, 100, 50000);

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(30)))
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
                    var prev = GUI.color;
                    GUI.color = gate.pass ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
                    EditorGUILayout.LabelField(gate.pass ? "GATE: PASS" : "GATE: FAIL", EditorStyles.boldLabel);
                    GUI.color = prev;
                    EditorGUILayout.HelpBox(gate.reason, gate.pass ? MessageType.Info : MessageType.Warning);

                    EditorGUILayout.HelpBox(
                        $"{_last.Cases} cases.\n" +
                        $"Composition err: {_last.MaxCompErr:E2} m\n" +
                        $"Segment-length err: {_last.MaxSegLenErr:E2} m\n" +
                        $"Scale-linearity err: {_last.MaxScaleErr:E2} m\n" +
                        $"Non-finite outputs: {_last.NaNCount}\n" +
                        _last.Path, MessageType.None);
                    if (GUILayout.Button("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
                }
                else
                {
                    EditorGUILayout.HelpBox("Sweep failed: " + _last.Error, MessageType.Error);
                }
            }
        }
    }
}
