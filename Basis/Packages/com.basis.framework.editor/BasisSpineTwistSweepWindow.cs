using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Spine Twist Sweep.
    public class BasisSpineTwistSweepWindow : EditorWindow
    {
        BasisSpineTwistSweepConfig _cfg = BasisSpineTwistSweepConfig.Default();
        string _path;
        BasisSpineTwistSummary _last;
        bool _hasResult;

        [MenuItem("Basis/Debug/IK/Spine Twist Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisSpineTwistSweepWindow>("Spine Twist Sweep");
            w.minSize = new Vector2(460, 400);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisSpineTwistSweep.DefaultPath();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Exercises BasisTwistSolveCore.ShapeReachStep and the graded, orientation-independent spine CCD. " +
                "Pass A: the swing/twist split is exact. Pass B: a forward-pitched spine reaches a head swept " +
                "laterally across center, run UPRIGHT and LYING DOWN -- keep=1 corkscrews; the graded shape " +
                "(rigid lumbar -> free neck) bends instead, stiffens the lumbar, still reaches, and matches in " +
                "both orientations (body hips-up axis). Pass C: world-up axis while prone leaves the corkscrew " +
                "(negative control). Edit mode, pure math.", MessageType.Info);

            _cfg.InvariantCases = EditorGUILayout.IntSlider("Invariant Cases", _cfg.InvariantCases, 200, 20000);
            _cfg.LeanSteps = EditorGUILayout.IntSlider("Lean Steps", _cfg.LeanSteps, 21, 641);
            _cfg.LumbarKeep = EditorGUILayout.Slider("Lumbar Keep", _cfg.LumbarKeep, 0f, 1f);
            _cfg.CervicalKeep = EditorGUILayout.Slider("Cervical (Neck) Keep", _cfg.CervicalKeep, 0f, 1f);

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(30)))
            {
                _last = BasisSpineTwistSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[SpineTwistSweep] {_last.Cases} cases -> {_last.Path}");
                else Debug.LogError($"[SpineTwistSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var gate = BasisIKTestGates.GateSpineTwist(_last);
                    var prev = GUI.color;
                    GUI.color = gate.pass ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
                    EditorGUILayout.LabelField(gate.pass ? "GATE: PASS" : "GATE: FAIL", EditorStyles.boldLabel);
                    GUI.color = prev;
                    EditorGUILayout.HelpBox(gate.reason, gate.pass ? MessageType.Info : MessageType.Warning);

                    EditorGUILayout.HelpBox(
                        $"{_last.Cases} cases (lumbar/neck keep {_last.TestedLumbarKeep:F2}/{_last.TestedCervicalKeep:F2}).\n" +
                        $"ShapeReachStep: identity {_last.MaxIdentityErrDeg:F3}°, resid twist {_last.MaxResidualTwistDeg:F3}°, resid swing {_last.MaxResidualSwingDeg:F3}°, blend {_last.MaxBlendErrDeg:F3}°\n" +
                        $"Peak twist: {_last.ReproMaxTwistDeg:F1}° (keep=1) -> {_last.GradedMaxTwistDeg:F1}° (graded)\n" +
                        $"Lumbar: {_last.ReproLumbarTwistDeg:F1}° -> {_last.GradedLumbarTwistDeg:F1}°   Neck (graded): {_last.GradedCervicalTwistDeg:F1}°\n" +
                        $"Across-center jump: {_last.GradedMaxJumpDeg:F1}°   Reach gap: {_last.GradedMaxReachErrM * 100f:F1} cm\n" +
                        $"Upright vs lying-down spread: {_last.OrientationSpreadDeg:F2}°   World-up-prone twist (neg ctrl): {_last.WorldUpSupineTwistDeg:F1}°\n" +
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
