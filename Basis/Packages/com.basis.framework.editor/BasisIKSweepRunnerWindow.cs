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
        [System.NonSerialized] int _armGridSteps = 75;   // per-axis reach-target density for the arm grid sweep. NonSerialized so this code default wins on every recompile (Unity otherwise persists the open window's slider value).
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

            _armGridSteps = EditorGUILayout.IntSlider("Arm Grid Steps (per axis)", _armGridSteps, 9, 99);
            long armPts = (long)_armGridSteps * _armGridSteps * _armGridSteps;
            EditorGUILayout.LabelField($"    arm grid = {armPts:n0} reach targets ({_armGridSteps}^3); higher = finer flip detection, slower", EditorStyles.miniLabel);

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

            // Capture everything that needs the main thread up front: Application.persistentDataPath (the
            // paths) and the window fields. The sweeps themselves are pure math + per-file CSV IO and share
            // no mutable state, so each runs on a worker thread and they all go in parallel. We block until
            // they finish (the editor is busy during the run, same as before -- just shorter), then log in
            // submission order so the console/list read the same as the serial version.
            int armSteps = _armGridSteps;
            float trajNoise = _trajNoise;
            bool includeTraj = _includeTraj;

            string armPath = BasisArmIKSweep.DefaultPath();
            string shoulderPath = BasisShoulderSweep.DefaultPath();
            string legPath = BasisLegIKSweep.DefaultPath();
            string legInvPath = BasisLegInversionSweep.DefaultPath();
            string headPath = BasisHeadSweep.DefaultPath();
            string protectPath = BasisElbowProtectSweep.DefaultPath();
            string armTrajPath = TrajPath("BasisArmIKTrajectory.csv");
            string armTempPath = TrajPath("BasisArmIKTemporal.csv");
            string armTempHandPath = TrajPath("BasisArmIKTemporalHand.csv");
            string armTempTrackPath = TrajPath("BasisArmIKTemporalTracker.csv");
            string protectTrajPath = TrajPath("BasisElbowProtectTrajectory.csv");
            string legTrajPath = TrajPath("BasisLegIKTrajectory.csv");
            string legTempPath = TrajPath("BasisLegIKTemporal.csv");
            string legInvTempPath = TrajPath("BasisLegInversionTemporal.csv");
            string headTrajPath = TrajPath("BasisHeadTrajectory.csv");

            var jobs = new System.Collections.Generic.List<System.Func<Row[]>>
            {
                () =>
                {
                    try
                    {
                        var cfg = BasisArmIKSweepConfig.Default();
                        cfg.Steps = new Vector3Int(armSteps, armSteps, armSteps);
                        var s = BasisArmIKSweep.Run(cfg, armPath);
                        var g = BasisIKTestGates.GateArm(s);
                        var ge = BasisIKTestGates.GateArmElbowDirection(s);
                        return new[]
                        {
                            new Row { Name = "Arm IK", Ok = g.pass, Detail = g.reason, Path = armPath },
                            new Row { Name = "Arm IK · elbow dir", Ok = ge.pass, Detail = ge.reason, Path = armPath },
                        };
                    }
                    catch (System.Exception e) { return new[] { new Row { Name = "Arm IK", Ok = false, Detail = e.Message, Path = null } }; }
                },
                () => Job("Shoulder", shoulderPath, () => { var s = BasisShoulderSweep.Run(BasisShoulderSweepConfig.Default(), shoulderPath); return BasisIKTestGates.GateShoulder(s); }),
                () => Job("Leg IK", legPath, () => { var s = BasisLegIKSweep.Run(BasisLegIKSweepConfig.Default(), legPath); return BasisIKTestGates.GateLeg(s); }),
                () => Job("Leg Inversion", legInvPath, () => { var cfg = BasisLegInversionConfig.Default(); cfg.SafeConeDeg = BasisIKTestGates.LegInvertHintSafeConeDeg; var s = BasisLegInversionSweep.Run(cfg, legInvPath); return BasisIKTestGates.GateLegInversion(s); }),
                () => Job("Head", headPath, () => { var s = BasisHeadSweep.Run(BasisHeadSweepConfig.Default(), headPath); return BasisIKTestGates.GateHead(s); }),
                () => Job("Elbow Protect", protectPath, () => { var s = BasisElbowProtectSweep.Run(BasisElbowProtectSweepConfig.Default(), protectPath); return BasisIKTestGates.GateElbow(s); }),
            };

            if (includeTraj)
            {
                jobs.Add(() => Job("Arm IK · traj", armTrajPath, () => { var s = BasisArmIKSweep.RunTrajectories(BasisArmIKSweepConfig.Default(), trajNoise, armTrajPath); return BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg); }));
                jobs.Add(() => Job("Arm IK · temporal", armTempPath, () => { var s = BasisArmIKSweep.RunTemporal(BasisArmIKSweepConfig.Default(), 0f, 0f, 1f / 90f, armTempPath); return BasisIKTestGates.GateTemporal(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg); }));
                jobs.Add(() => Job("Arm IK · temporal+handnoise", armTempHandPath, () => { var s = BasisArmIKSweep.RunTemporal(BasisArmIKSweepConfig.Default(), 0f, trajNoise, 1f / 90f, armTempHandPath); return (s.Ok, $"elbowJitter={s.WorstElbowJitterM * 1000f:F0}mm pop={s.WorstPopDeg:F0} (hand noise {trajNoise * 1000f:F0}mm)"); }));
                jobs.Add(() => Job("Arm IK · temporal+tracker", armTempTrackPath, () => { var s = BasisArmIKSweep.RunTemporal(BasisArmIKSweepConfig.Default(), trajNoise, 0f, 1f / 90f, armTempTrackPath); return (s.Ok, $"elbowJitter={s.WorstElbowJitterM * 1000f:F0}mm glideJitter={s.WorstRoughDeg:F2} (hint noise {trajNoise * 1000f:F0}mm)"); }));
                jobs.Add(() => Job("Elbow Protect · traj", protectTrajPath, () => { var s = BasisElbowProtectSweep.RunTrajectories(BasisElbowProtectSweepConfig.Default(), trajNoise, protectTrajPath); return BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg); }));
                jobs.Add(() => Job("Leg IK · traj", legTrajPath, () => { var s = BasisLegIKSweep.RunTrajectories(BasisLegIKSweepConfig.Default(), trajNoise, 1f / 90f, false, legTrajPath); return BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg); }));
                jobs.Add(() => Job("Leg IK · temporal+footnoise", legTempPath, () => { var s = BasisLegIKSweep.RunTrajectories(BasisLegIKSweepConfig.Default(), trajNoise, 1f / 90f, true, legTempPath); return (s.Ok, $"kneeJitter={s.WorstKneeJitterM * 1000f:F0}mm pop={s.WorstPopDeg:F0} (foot noise {trajNoise * 1000f:F0}mm)"); }));
                jobs.Add(() => Job("Leg Inversion · temporal", legInvTempPath, () => { var cfg = BasisLegInversionConfig.Default(); cfg.SafeConeDeg = BasisIKTestGates.LegInvertHintSafeConeDeg; var s = BasisLegInversionSweep.RunTemporal(cfg, trajNoise, legInvTempPath); return BasisIKTestGates.GateLegInversionTemporal(s); }));
                jobs.Add(() => Job("Head · traj", headTrajPath, () => { var s = BasisHeadSweep.RunTrajectories(BasisHeadSweepConfig.Default(), 0.3f, headTrajPath); return BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg); }));
            }

            // Fan out: one worker thread per sweep (the .NET thread pool caps concurrency to the core count).
            var running = new System.Threading.Tasks.Task<Row[]>[jobs.Count];
            for (int i = 0; i < jobs.Count; i++) { var job = jobs[i]; running[i] = System.Threading.Tasks.Task.Run(job); }
            try { System.Threading.Tasks.Task.WaitAll(running); }
            catch (System.AggregateException) { } // every job already captures its own exception into a FAIL row

            foreach (var t in running)
                foreach (var row in t.Result)
                    Record(row.Name, row.Ok, row.Detail, row.Path);
        }

        // One sweep job for the parallel Run All: runs body on a worker thread, turns its (pass, reason) into
        // a Row, and captures any exception (incl. a sweep that touches a main-thread-only API) into a FAIL
        // row so one bad sweep can't sink the rest.
        static Row[] Job(string name, string path, System.Func<(bool pass, string reason)> body)
        {
            try { var (pass, reason) = body(); return new[] { new Row { Name = name, Ok = pass, Detail = reason, Path = path } }; }
            catch (System.Exception e) { return new[] { new Row { Name = name, Ok = false, Detail = e.Message, Path = null } }; }
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
