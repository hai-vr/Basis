using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Tracker Placement Sweep.
    public class BasisTrackerPlacementSweepWindow : EditorWindow
    {
        BasisTrackerPlacementSweepConfig _cfg = BasisTrackerPlacementSweepConfig.Default();
        string _path;
        BasisTrackerPlacementSummary _last;
        bool _hasResult;
        Vector2 _scroll;
        float _maxJitterCm = 4f;
        float _measureErrorCm = 0f;

        [MenuItem("Basis/Debug/IK/Tracker Placement Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisTrackerPlacementSweepWindow>("Tracker Placement Sweep");
            w.minSize = new Vector2(420, 560);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path))
            {
                _path = BasisTrackerPlacementSweep.DefaultPath();
            }
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Generates synthetic tracker constellations for many body archetypes (heights, limb " +
                "proportions, stance, mount variants) x tracker-set presets x EYE HEIGHTS x placement noise (cm), then runs " +
                "the REAL discovery classifier (BasisConstellationClassifier, shared with live " +
                "FullBodyCalibration) and checks each tracker landed on the role it was placed for. " +
                "One CSV row per (scenario, tracker). Edit mode, no avatar.", MessageType.Info);

            EditorGUILayout.LabelField($"{BasisTrackerPlacementSweep.Archetypes.Length} archetypes x {BasisTrackerPlacementSweep.Presets.Length} tracker sets", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
            _cfg.Tolerance = EditorGUILayout.Slider("Calibration Tolerance", _cfg.Tolerance, 1f, 3f);
            _cfg.HandsPresent = EditorGUILayout.Toggle("Hands Present (elbow recenter)", _cfg.HandsPresent);
            _maxJitterCm = EditorGUILayout.Slider("Max Placement Noise (cm)", _maxJitterCm, 0f, 10f);
            _measureErrorCm = EditorGUILayout.Slider("Eye-Height Measure Error (cm)", _measureErrorCm, 0f, 10f);
            _cfg.SeedsPerJitter = EditorGUILayout.IntSlider("Seeds per Noise Level", _cfg.SeedsPerJitter, 1, 32);
            _cfg.DumpFullScoreTable = EditorGUILayout.Toggle("Dump Full Score Table", _cfg.DumpFullScoreTable);

            EditorGUILayout.LabelField("eye heights swept: 0.9 / 1.2 / 1.5 / 1.8 / 2.1 m   noise: 0, " + (_maxJitterCm * 0.5f).ToString("0.#") + ", " + _maxJitterCm.ToString("0.#") + " cm", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("(cm noise becomes a larger ratio error for short users; measure-error skews short bodies more)", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Tracker Placement Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Reset to persistentDataPath"))
            {
                _path = BasisTrackerPlacementSweep.DefaultPath();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _cfg.JitterCm = _maxJitterCm <= 0f ? new[] { 0f } : new[] { 0f, _maxJitterCm * 0.5f, _maxJitterCm };
                _cfg.EyeHeightMeasureErrorCm = _measureErrorCm;
                _last = BasisTrackerPlacementSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[TrackerPlacementSweep] {_last.Trackers} trackers / {_last.Scenarios} scenarios -> {_last.Path}");
                else Debug.LogError($"[TrackerPlacementSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var gate = BasisIKTestGates.GateTrackerPlacement(_last);
                    var prev = GUI.color;
                    GUI.color = gate.pass ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
                    EditorGUILayout.LabelField(gate.pass ? "GATE: PASS" : "GATE: FAIL", EditorStyles.boldLabel);
                    GUI.color = prev;
                    EditorGUILayout.HelpBox(gate.reason, gate.pass ? MessageType.Info : MessageType.Warning);

                    EditorGUILayout.HelpBox(
                        $"{_last.Correct}/{_last.Trackers} trackers correctly classified across {_last.Scenarios} scenarios.\n" +
                        $"CLEAN (no jitter): {_last.CleanCorrect}/{_last.CleanTrackers} ({_last.CleanCorrectFraction:P1}); core archetypes {_last.CoreCleanCorrect}/{_last.CoreCleanTrackers} ({_last.CoreCleanFraction:P1})\n" +
                        $"Misassigned {_last.Misassigned}, unassigned {_last.Unassigned}; cross-side leaks {_last.CrossSideLeaks} (clean {_last.CleanCrossSideLeaks}); near-origin leaks {_last.NearOriginLeaks}\n" +
                        $"Min winning margin (correct binds): {_last.MinCorrectMargin:F2}\n" +
                        $"By eye height (noisy rows): {_last.PerHeightAccuracy}\n" +
                        $"Height bias: {_last.HeightBiasPts:F1} pts (short {_last.ShortAccuracy:F1}% vs tall {_last.TallAccuracy:F1}%); measure-error {_last.MeasureErrorCm:F1}cm\n" +
                        $"Top confusions: {_last.TopConfusions}\n" +
                        _last.Path, MessageType.None);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Reveal CSV"))
                    {
                        EditorUtility.RevealInFinder(_last.Path);
                    }
                    if (GUILayout.Button("Copy Path"))
                    {
                        EditorGUIUtility.systemCopyBuffer = _last.Path;
                    }
                    EditorGUILayout.EndHorizontal();
                    if (!string.IsNullOrEmpty(_last.FullTablePath) && GUILayout.Button("Reveal Score Table CSV"))
                    {
                        EditorUtility.RevealInFinder(_last.FullTablePath);
                    }
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
