using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisCapsuleCollisionSweepPage : BasisIKSweepPage
    {
        public override string Group => "Spine & Torso";
        public override string Title => "Capsule Collision Sweep";
        public override int Order => 100;
        public override string Description =>
            "Invariant sweep of the body self-collision geometry (segment/capsule closest points, " +
            "penetration resolve, push-out) used by SolveHand and the elbow protect. Verifies optimality " +
            "certificates, symmetry and the penetration round-trip. Same static math the live job runs.";

        BasisCapsuleCollisionSweepConfig _cfg = BasisCapsuleCollisionSweepConfig.Default();
        string _path;
        BasisCapsuleCollisionSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisCapsuleCollisionSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Configuration");
            _cfg.OffsetSteps = EditorGUILayout.IntSlider("Offset Steps (per axis)", _cfg.OffsetSteps, 3, 15);
            _cfg.OffsetRange = EditorGUILayout.FloatField("Offset Range (m)", _cfg.OffsetRange);
            long cases = 4L * 6L * 3L * 3L * _cfg.OffsetSteps * _cfg.OffsetSteps * _cfg.OffsetSteps;
            EditorGUILayout.LabelField("Cases", cases.ToString("n0"));

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
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
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
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
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"cases={_last.Cases} overlap={_last.OverlapCases} nan={_last.NaNCount} fails={_last.Failures}\n" +
                        $"param={_last.MaxClosestParamErr:F5} kkt={_last.MaxKktResidual:F5} sym={_last.MaxSymmetryErr:F5} " +
                        $"depth={_last.MaxPenetrationDepthErr:F5} resid={_last.MaxResidualPenMm:F2}mm pushout={_last.MaxPushOutSurfaceErrMm:F2}mm\n" +
                        $"deep/crossing (reported): {_last.DeepOverlapCases} cases, worst residual {_last.MaxDeepResidualPenMm:F0}mm\n" +
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
