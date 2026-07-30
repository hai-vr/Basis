using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisFootIKSweepPage : BasisIKSweepPage
    {
        public override string Group => "Leg & Foot";
        public override string Title => "Foot IK Sweep";
        public override int Order => 40;
        public override string Description =>
            "Ticks the REAL foot-placement job (BasisFootSimulateJob + the driver's FinalizeStep) through " +
            "a battery of scripted locomotion scenarios on synthetic ground, and measures the foot-IK " +
            "failure modes per frame: skating, crossover, over-extension, ground penetration, tilt/yaw " +
            "clamp, stuck/overdue steps, both-feet-airborne, knee-behind. One CSV row per (scenario, tick).";

        BasisFootIKSweepConfig _cfg = BasisFootIKSweepConfig.Default();
        string _path;
        BasisFootIKSweepSummary _last;
        bool _hasResult;
        string _report;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisFootIKSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Avatar (T-pose measurements)");
            _cfg.StanceWidth = EditorGUILayout.FloatField("Stance Width (m)", _cfg.StanceWidth);
            _cfg.HipToFoot = EditorGUILayout.FloatField("Hip → Foot (m)", _cfg.HipToFoot);
            _cfg.ThighLen = EditorGUILayout.FloatField("Thigh Length (m)", _cfg.ThighLen);
            _cfg.ShinLen = EditorGUILayout.FloatField("Shin Length (m)", _cfg.ShinLen);
            _cfg.FootLength = EditorGUILayout.FloatField("Foot Length (m)", _cfg.FootLength);
            _cfg.AnkleHeight = EditorGUILayout.FloatField("Ankle Height (m)", _cfg.AnkleHeight);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Key Tunables");
            _cfg.MaxFootTiltDegrees = EditorGUILayout.Slider("Max Foot Tilt (deg)", _cfg.MaxFootTiltDegrees, 0f, 60f);
            _cfg.MaxFootYawDegrees = EditorGUILayout.Slider("Max Foot Yaw (deg)", _cfg.MaxFootYawDegrees, 0f, 45f);
            _cfg.PredictionFactor = EditorGUILayout.Slider("Prediction Factor", _cfg.PredictionFactor, 0.3f, 2f);
            _cfg.StepTriggerMul = EditorGUILayout.Slider("Step Trigger ×leg", _cfg.StepTriggerMul, 0.02f, 0.2f);
            _cfg.StrideScaleMul = EditorGUILayout.Slider("Stride Scale ×leg", _cfg.StrideScaleMul, 0.02f, 0.25f);
            _cfg.FootSideEnforceFraction = EditorGUILayout.Slider("Foot Side Enforce ×½stance", _cfg.FootSideEnforceFraction, 0.05f, 0.5f);
            _cfg.MaxVerticalDriftFraction = EditorGUILayout.Slider("Max Vert Drift ×hipToFoot", _cfg.MaxVerticalDriftFraction, 0.1f, 0.5f);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Run");
            float hz = _cfg.Dt > 0f ? 1f / _cfg.Dt : 0f;
            float newHz = EditorGUILayout.Slider("Tick Rate (Hz)", hz, 30f, 144f);
            _cfg.Dt = 1f / Mathf.Max(1f, newHz);
            _cfg.SlowSpeed = EditorGUILayout.Slider("Slow Speed (m/s)", _cfg.SlowSpeed, 0.1f, 1.5f);
            _cfg.NormalSpeed = EditorGUILayout.Slider("Normal Speed (m/s)", _cfg.NormalSpeed, 0.5f, 3f);
            _cfg.FastSpeed = EditorGUILayout.Slider("Fast Speed (m/s)", _cfg.FastSpeed, 1f, 4f);
            _cfg.Duration = EditorGUILayout.Slider("Duration / scenario (s)", _cfg.Duration, 2f, 20f);
            _cfg.TrackerNoise = EditorGUILayout.Slider("Head Noise (m)", _cfg.TrackerNoise, 0f, 0.02f);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Foot IK Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();
            if (BasisEditorUI.SecondaryButton("Reset to persistentDataPath")) _path = BasisFootIKSweep.DefaultPath();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Advanced Simulation", 32f))
            {
                _last = BasisFootIKSweep.Run(_cfg, _path);
                _hasResult = true;
                _report = _last.Ok ? BasisFootIKSweep.JoinReports(_last.Results) : "";
                if (_last.Ok) Debug.Log($"[FootIKSweep] {_last.Rows} rows, {_last.Scenarios} scenarios -> {_last.Path}\n{_report}");
                else Debug.LogError($"[FootIKSweep] failed: {_last.Error}");
            }

            if (_hasResult) DrawResults();

        }

        void DrawResults()
        {
            EditorGUILayout.Space();
            if (!_last.Ok)
            {
                BasisEditorUI.Help("Sweep failed: " + _last.Error, MessageType.Error);
                return;
            }

            var gate = BasisIKTestGates.GateFoot(_last);
            var prev = GUI.color;
            GUI.color = gate.pass ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
            EditorGUILayout.LabelField(gate.pass ? "GATE PASS" : "GATE FAIL", EditorStyles.boldLabel);
            GUI.color = prev;
            BasisEditorUI.Help(gate.reason, gate.pass ? MessageType.Info : MessageType.Error);

            EditorGUILayout.LabelField(
                $"Worst: slide {_last.WorstPlantedSlideMm:F1}mm · ext {_last.WorstExtensionRatio:F2} · pen {_last.WorstPenetrationMm:F1}mm · " +
                $"hover {_last.WorstPlantedHoverMm:F1}mm · tilt {_last.WorstTiltDeg:F0}° · yaw {_last.WorstYawDeg:F0}° · drift {_last.WorstPlantToIdealM * 100f:F1}cm",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"crossover {_last.Crossovers} · bothStepping {_last.BothSteppingTicks} (worst {_last.BothSteppingWorstScenario} {_last.BothSteppingWorstTicks}) · kneeBehind {_last.WorstKneeBackwardM * 100f:F1}cm · steps {_last.TotalSteps} · NaN {_last.HadNaN}",
                EditorStyles.miniLabel);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle($"Per-scenario report ({_last.Scenarios} scenarios) — full detail in {System.IO.Path.GetFileName(_last.ReportPath)}");
            if (!string.IsNullOrEmpty(_report))
            {
                int lines = 1;
                for (int i = 0; i < _report.Length; i++) if (_report[i] == '\n') lines++;
                EditorGUILayout.SelectableLabel(_report, EditorStyles.textArea, GUILayout.Height(Mathf.Clamp(lines, 6, 240) * 13f + 8f));
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (BasisEditorUI.SecondaryButton("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_last.ReportPath)))
                if (BasisEditorUI.SecondaryButton("Reveal Report")) EditorUtility.RevealInFinder(_last.ReportPath);
            if (GUILayout.Button("Copy CSV Path")) EditorGUIUtility.systemCopyBuffer = _last.Path;
            EditorGUILayout.EndHorizontal();
        }
    }
}
