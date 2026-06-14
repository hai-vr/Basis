using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Run All Sweeps. One click runs every IK sweep (and the trajectory scans
    // that exist) with default configs, writing each CSV to persistentDataPath, then evaluates the
    // quality gates in BasisIKTestGates -- a one-click regression test over all the IK math. PASS/FAIL
    // reflects the gate thresholds, not just whether the sweep ran. Each run is isolated.
    public class BasisIKSweepRunnerWindow : EditorWindow
    {
        struct Row { public string Name; public bool Ok; public string Detail; public string Path; }

        readonly List<Row> _rows = new List<Row>();
        bool _hasRun;
        bool _includeTraj = true;
        float _trajNoise = 0.003f;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Run All Sweeps")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisIKSweepRunnerWindow>("Run All IK Tests");
            w.minSize = new Vector2(460, 380);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Runs every IK sweep (grid) and the available trajectory scans with default configs, " +
                "writing each CSV to persistentDataPath, then checks the BasisIKTestGates quality gates. " +
                "PASS/FAIL = the IK math passed its thresholds (not just 'it ran'). Tune thresholds in BasisIKTestGates.",
                MessageType.Info);

            _includeTraj = EditorGUILayout.Toggle("Include Trajectory Scans", _includeTraj);
            using (new EditorGUI.DisabledScope(!_includeTraj))
            {
                _trajNoise = EditorGUILayout.Slider("Trajectory Noise (m)", _trajNoise, 0f, 0.01f);
            }

            if (GUILayout.Button("Run All IK Tests", GUILayout.Height(32)))
            {
                RunAll();
            }

            EditorGUILayout.HelpBox(
                "Live jitter capture: ENTER PLAY, click below, then move/hold the arm for ~10s. Writes the " +
                "solved shoulder/elbow/hand + IK inputs each frame to persistentDataPath/ArmIKRuntime so we " +
                "can see which one jitters (shoulder vs filtered target/hint vs the elbow itself).",
                MessageType.None);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Capture Live Arm Jitter (10s)"))
                {
                    Basis.Scripts.Drivers.BasisArmIKRuntimeRecorder.RequestCapture(900);
                }
            }

            if (_hasRun)
            {
                EditorGUILayout.Space();
                int ok = 0;
                for (int i = 0; i < _rows.Count; i++) if (_rows[i].Ok) ok++;
                EditorGUILayout.LabelField($"Results: {ok}/{_rows.Count} passed", EditorStyles.boldLabel);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                foreach (var r in _rows)
                {
                    EditorGUILayout.BeginHorizontal();
                    var prev = GUI.color;
                    GUI.color = r.Ok ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.5f);
                    EditorGUILayout.LabelField(r.Ok ? "PASS" : "FAIL", GUILayout.Width(42));
                    GUI.color = prev;
                    EditorGUILayout.LabelField(r.Name, GUILayout.Width(150));
                    EditorGUILayout.LabelField(r.Detail);
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(r.Path)))
                    {
                        if (GUILayout.Button("Reveal", GUILayout.Width(60)) && !string.IsNullOrEmpty(r.Path))
                        {
                            EditorUtility.RevealInFinder(r.Path);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("Reveal Output Folder"))
                {
                    EditorUtility.RevealInFinder(Application.persistentDataPath);
                }
            }
        }

        void RunAll()
        {
            _rows.Clear();
            _hasRun = true;

            try { string p = BasisArmIKSweep.DefaultPath(); var s = BasisArmIKSweep.Run(BasisArmIKSweepConfig.Default(), p); var g = BasisIKTestGates.GateArm(s); Record("Arm IK", g.pass, g.reason, p); }
            catch (System.Exception e) { Record("Arm IK", false, e.Message, null); }

            try { string p = BasisShoulderSweep.DefaultPath(); var s = BasisShoulderSweep.Run(BasisShoulderSweepConfig.Default(), p); var g = BasisIKTestGates.GateShoulder(s); Record("Shoulder", g.pass, g.reason, p); }
            catch (System.Exception e) { Record("Shoulder", false, e.Message, null); }

            try { string p = BasisLegIKSweep.DefaultPath(); var s = BasisLegIKSweep.Run(BasisLegIKSweepConfig.Default(), p); var g = BasisIKTestGates.GateLeg(s); Record("Leg IK", g.pass, g.reason, p); }
            catch (System.Exception e) { Record("Leg IK", false, e.Message, null); }

            try { string p = BasisHeadSweep.DefaultPath(); var s = BasisHeadSweep.Run(BasisHeadSweepConfig.Default(), p); var g = BasisIKTestGates.GateHead(s); Record("Head", g.pass, g.reason, p); }
            catch (System.Exception e) { Record("Head", false, e.Message, null); }

            try { string p = BasisElbowProtectSweep.DefaultPath(); var s = BasisElbowProtectSweep.Run(BasisElbowProtectSweepConfig.Default(), p); var g = BasisIKTestGates.GateElbow(s); Record("Elbow Protect", g.pass, g.reason, p); }
            catch (System.Exception e) { Record("Elbow Protect", false, e.Message, null); }

            if (_includeTraj)
            {
                try
                {
                    string p = TrajPath("BasisArmIKTrajectory.csv");
                    var s = BasisArmIKSweep.RunTrajectories(BasisArmIKSweepConfig.Default(), _trajNoise, p);
                    var g = BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg);
                    Record("Arm IK · traj", g.pass, g.reason, p);
                }
                catch (System.Exception e) { Record("Arm IK · traj", false, e.Message, null); }

                try
                {
                    // Per-frame feedback, no input noise: isolates solve + rate-limiter (vs the stateless scan).
                    string p = TrajPath("BasisArmIKTemporal.csv");
                    var s = BasisArmIKSweep.RunTemporal(BasisArmIKSweepConfig.Default(), 0f, 0f, 1f / 90f, p);
                    var g = BasisIKTestGates.GateTemporal(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg);
                    Record("Arm IK · temporal", g.pass, g.reason, p);
                }
                catch (System.Exception e) { Record("Arm IK · temporal", false, e.Message, null); }

                try
                {
                    // Per-frame feedback + HAND-TARGET noise: the NO-TRACKER (lookup) case -- controller noise
                    // moves the lookup hint AND the IK target. elbowJitter (mm) = how far it throws the elbow.
                    string p = TrajPath("BasisArmIKTemporalHand.csv");
                    var s = BasisArmIKSweep.RunTemporal(BasisArmIKSweepConfig.Default(), 0f, _trajNoise, 1f / 90f, p);
                    Record("Arm IK · temporal+handnoise", s.Ok, $"elbowJitter={s.WorstElbowJitterM * 1000f:F0}mm pop={s.WorstPopDeg:F0} (hand noise {_trajNoise * 1000f:F0}mm)", p);
                }
                catch (System.Exception e) { Record("Arm IK · temporal+handnoise", false, e.Message, null); }

                try
                {
                    // Per-frame feedback + independent hint noise: simulates an elbow tracker's jitter.
                    string p = TrajPath("BasisArmIKTemporalTracker.csv");
                    var s = BasisArmIKSweep.RunTemporal(BasisArmIKSweepConfig.Default(), _trajNoise, 0f, 1f / 90f, p);
                    Record("Arm IK · temporal+tracker", s.Ok, $"elbowJitter={s.WorstElbowJitterM * 1000f:F0}mm glideJitter={s.WorstRoughDeg:F2} (hint noise {_trajNoise * 1000f:F0}mm)", p);
                }
                catch (System.Exception e) { Record("Arm IK · temporal+tracker", false, e.Message, null); }

                try
                {
                    string p = TrajPath("BasisElbowProtectTrajectory.csv");
                    var s = BasisElbowProtectSweep.RunTrajectories(BasisElbowProtectSweepConfig.Default(), _trajNoise, p);
                    var g = BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg);
                    Record("Elbow Protect · traj", g.pass, g.reason, p);
                }
                catch (System.Exception e) { Record("Elbow Protect · traj", false, e.Message, null); }

                try
                {
                    // Knee = the other hint role. Catches knee pole flips (pop) -- expected clean, the leg
                    // is flip-safe by design (hint-first axis + xyz-scaled hint commits at 180).
                    string p = TrajPath("BasisLegIKTrajectory.csv");
                    var s = BasisLegIKSweep.RunTrajectories(BasisLegIKSweepConfig.Default(), _trajNoise, 1f / 90f, false, p);
                    var g = BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg);
                    Record("Leg IK · traj", g.pass, g.reason, p);
                }
                catch (System.Exception e) { Record("Leg IK · traj", false, e.Message, null); }

                try
                {
                    // Feedback + foot-target noise: does the knee amplify input noise the way the elbow did?
                    // Expected small -- the knee pole is the stable bend-normal, not a moving target.
                    string p = TrajPath("BasisLegIKTemporal.csv");
                    var s = BasisLegIKSweep.RunTrajectories(BasisLegIKSweepConfig.Default(), _trajNoise, 1f / 90f, true, p);
                    Record("Leg IK · temporal+footnoise", s.Ok, $"kneeJitter={s.WorstKneeJitterM * 1000f:F0}mm pop={s.WorstPopDeg:F0} (foot noise {_trajNoise * 1000f:F0}mm)", p);
                }
                catch (System.Exception e) { Record("Leg IK · temporal+footnoise", false, e.Message, null); }

                try
                {
                    // Head pitch-noise is in DEGREES, so it uses a fixed ~0.3 deg HMD-noise rather than the metres slider.
                    string p = TrajPath("BasisHeadTrajectory.csv");
                    var s = BasisHeadSweep.RunTrajectories(BasisHeadSweepConfig.Default(), 0.3f, p);
                    var g = BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg);
                    Record("Head · traj", g.pass, g.reason, p);
                }
                catch (System.Exception e) { Record("Head · traj", false, e.Message, null); }
            }
        }

        static string TrajPath(string fname)
        {
            return System.IO.Path.Combine(Application.persistentDataPath, fname);
        }

        void Record(string name, bool ok, string detail, string path)
        {
            _rows.Add(new Row { Name = name, Ok = ok, Detail = detail, Path = path });
            if (ok) Debug.Log($"[IKTests] {name} PASS: {detail} -> {path}");
            else Debug.LogError($"[IKTests] {name} FAIL: {detail}");
        }
    }
}
