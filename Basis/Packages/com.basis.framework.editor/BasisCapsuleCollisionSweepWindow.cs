using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Capsule Collision Sweep.
    public class BasisCapsuleCollisionSweepWindow : EditorWindow
    {
        BasisCapsuleCollisionSweepConfig _cfg = BasisCapsuleCollisionSweepConfig.Default();
        string _path;
        BasisCapsuleCollisionSweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Capsule Collision Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisCapsuleCollisionSweepWindow>("Capsule Collision Sweep");
            w.minSize = new Vector2(360, 430);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisCapsuleCollisionSweep.DefaultPath();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Invariant sweep of the body self-collision geometry (segment/capsule closest points, " +
                "penetration resolve, push-out) used by SolveHand and the elbow protect. Verifies optimality " +
                "certificates, symmetry and the penetration round-trip. Same static math the live job runs.",
                MessageType.Info);

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            _cfg.OffsetSteps = EditorGUILayout.IntSlider("Offset Steps (per axis)", _cfg.OffsetSteps, 3, 15);
            _cfg.OffsetRange = EditorGUILayout.FloatField("Offset Range (m)", _cfg.OffsetRange);
            long cases = 4L * 6L * 3L * 3L * _cfg.OffsetSteps * _cfg.OffsetSteps * _cfg.OffsetSteps;
            EditorGUILayout.LabelField("Cases", cases.ToString("n0"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Capsule Collision Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _last = BasisCapsuleCollisionSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[CapsuleCollisionSweep] {_last.Cases} cases -> {_last.Path}");
                else Debug.LogError($"[CapsuleCollisionSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateCapsuleCollision(_last);
                    EditorGUILayout.HelpBox(
                        (g.pass ? "PASS  " : "FAIL  ") + g.reason + "\n" +
                        $"cases={_last.Cases} overlap={_last.OverlapCases} nan={_last.NaNCount} fails={_last.Failures}\n" +
                        $"param={_last.MaxClosestParamErr:F5} kkt={_last.MaxKktResidual:F5} sym={_last.MaxSymmetryErr:F5} " +
                        $"depth={_last.MaxPenetrationDepthErr:F5} resid={_last.MaxResidualPenMm:F2}mm pushout={_last.MaxPushOutSurfaceErrMm:F2}mm\n" +
                        $"deep/crossing (reported): {_last.DeepOverlapCases} cases, worst residual {_last.MaxDeepResidualPenMm:F0}mm\n" +
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
