using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisVirtualSpineTemporalSweepPage : BasisIKSweepPage
    {
        public override string Group => "Spine & Torso";
        public override string Title => "Virtual Spine Temporal Sweep";
        public override int Order => 50;
        public override string Description =>
            "Drives the REAL virtual-spine solve job frame-by-frame (the static spine sweeps can't see the " +
            "per-frame yaw deadzone / catch-up dynamics). A yaw ramp must break the deadzone and relock " +
            "without snapping; a smooth yaw sinusoid must stay step-free. Catches relock pops, stepping, the " +
            "hips-above-head flip, and a torso that never catches the head.";

        BasisVirtualSpineTemporalSweepConfig _cfg = BasisVirtualSpineTemporalSweepConfig.Default();
        string _path;
        BasisVirtualSpineTemporalSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisVirtualSpineTemporalSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Drive");
            _cfg.Fps = EditorGUILayout.Slider("FPS", _cfg.Fps, 30f, 144f);
            _cfg.Seconds = EditorGUILayout.Slider("Seconds", _cfg.Seconds, 2f, 12f);
            _cfg.RampYawDeg = EditorGUILayout.Slider("Ramp Yaw (deg)", _cfg.RampYawDeg, 30f, 170f);
            _cfg.SineYawDeg = EditorGUILayout.Slider("Sine Yaw (deg)", _cfg.SineYawDeg, 10f, 90f);
            _cfg.SineHz = EditorGUILayout.Slider("Sine (Hz)", _cfg.SineHz, 0.1f, 1.5f);

            BasisEditorUI.SectionTitle("Solver Tunables");
            _cfg.TorsoYawDeadzoneDeg = EditorGUILayout.Slider("Yaw Deadzone (deg)", _cfg.TorsoYawDeadzoneDeg, 0f, 60f);
            _cfg.TorsoYawBlendSpeed = EditorGUILayout.Slider("Yaw Blend Speed", _cfg.TorsoYawBlendSpeed, 1f, 20f);
            _cfg.NeckRotationSpeed = EditorGUILayout.Slider("Neck Rot Speed", _cfg.NeckRotationSpeed, 5f, 90f);
            _cfg.ChestRotationSpeed = EditorGUILayout.Slider("Chest Rot Speed", _cfg.ChestRotationSpeed, 5f, 90f);
            _cfg.SpineRotationSpeed = EditorGUILayout.Slider("Spine Rot Speed", _cfg.SpineRotationSpeed, 5f, 90f);
            _cfg.HipsRotationSpeed = EditorGUILayout.Slider("Hips Rot Speed", _cfg.HipsRotationSpeed, 5f, 90f);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Virtual Spine Temporal Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisVirtualSpineTemporalSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[VirtualSpineTemporalSweep] {_last.Steps} steps -> {_last.Path}");
                else Debug.LogError($"[VirtualSpineTemporalSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateVirtualSpineTemporal(_last);
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"steps={_last.Steps} nan={_last.NaNCount} follow={_last.MaxTorsoFollow:F2}\n" +
                        $"pop={_last.MaxOutputPopDeg:F1}° hipsStep={_last.MaxHipsYawStepDeg:F2}° chestStep={_last.MaxChestYawStepDeg:F2}°\n" +
                        $"aboveHead={_last.MaxHipsAboveHeadM * 100f:F1}cm catchUp={_last.FinalCatchupErrDeg:F1}°\n" +
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
