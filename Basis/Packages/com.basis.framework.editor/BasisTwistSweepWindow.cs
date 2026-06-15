using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Twist Sweep.
    public class BasisTwistSweepWindow : EditorWindow
    {
        BasisTwistSweepConfig _cfg = BasisTwistSweepConfig.Default();
        string _path;
        BasisTwistSummary _last;
        bool _hasResult;

        [MenuItem("Basis/Debug/IK/Twist Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisTwistSweepWindow>("Twist Sweep");
            w.minSize = new Vector2(400, 320);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisTwistSweep.DefaultPath();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Exercises BasisTwistSolveCore (swing-twist decomposition for forearm/arm twist): a pure " +
                "twist about the bone axis must be recovered exactly, the Fraction blends it linearly, a " +
                "perpendicular swing must not tilt the extracted twist axis, and singular inputs no-op. " +
                "Edit mode, pure math.", MessageType.Info);

            _cfg.Cases = EditorGUILayout.IntSlider("Cases", _cfg.Cases, 100, 50000);

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(30)))
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
                    var prev = GUI.color;
                    GUI.color = gate.pass ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
                    EditorGUILayout.LabelField(gate.pass ? "GATE: PASS" : "GATE: FAIL", EditorStyles.boldLabel);
                    GUI.color = prev;
                    EditorGUILayout.HelpBox(gate.reason, gate.pass ? MessageType.Info : MessageType.Warning);

                    EditorGUILayout.HelpBox(
                        $"{_last.Cases} cases.\n" +
                        $"Pure-twist recovery err: {_last.MaxPureTwistErrDeg:F3}°\n" +
                        $"Fraction blend err: {_last.MaxFractionErrDeg:F3}°\n" +
                        $"Axis misalign under swing: {_last.MaxAxisMisalignDeg:F2}°\n" +
                        $"Singularity failures: {_last.SingularityFailures}\n" +
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
