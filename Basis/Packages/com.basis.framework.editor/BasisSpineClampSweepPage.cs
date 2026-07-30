using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisSpineClampSweepPage : BasisIKSweepPage
    {
        public override string Group => "Spine & Torso";
        public override string Title => "Spine Clamp Sweep";
        public override int Order => 30;
        public override string Description =>
            "Invariant sweep of the FBIK spine safety clamps (hips distance, bend limit, anti-contortion, " +
            "buckling, rotation clamp). Verifies each limit holds, is idempotent, and leaves valid poses " +
            "untouched. Same static math the live SolveSpine runs. (Virtual-spine synthesis is the Spine sweep.)";

        BasisSpineClampSweepConfig _cfg = BasisSpineClampSweepConfig.Default();
        string _path;
        BasisSpineClampSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisSpineClampSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Spine");
            _cfg.RestDistance = EditorGUILayout.FloatField("Rest Head→Hips (m)", _cfg.RestDistance);
            _cfg.MinFactor = EditorGUILayout.FloatField("Min Factor", _cfg.MinFactor);
            _cfg.MaxFactor = EditorGUILayout.FloatField("Max Factor", _cfg.MaxFactor);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Pose Grid");
            _cfg.VerticalSteps = EditorGUILayout.IntSlider("Vertical Steps", _cfg.VerticalSteps, 2, 31);
            _cfg.LateralSteps = EditorGUILayout.IntSlider("Lateral Steps", _cfg.LateralSteps, 1, 31);
            long cases = (long)_cfg.VerticalSteps * Mathf.Max(1, _cfg.LateralSteps) * 6L;
            EditorGUILayout.LabelField("Cases", cases.ToString("n0"));

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Spine Clamp Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisSpineClampSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[SpineClampSweep] {_last.Cases} cases -> {_last.Path}");
                else Debug.LogError($"[SpineClampSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateSpineClamp(_last);
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"cases={_last.Cases} nan={_last.NaNCount} fails={_last.Failures}\n" +
                        $"hipsOver={_last.MaxHipsClampOverErrM:F5} bendOver={_last.MaxBendOverDeg:F3}° " +
                        $"antiDef={_last.MaxAntiContortDeficitM:F5} buck={_last.MaxBucklingPushErrM:F5} rotOver={_last.MaxClampRotOverDeg:F3}°\n" +
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
