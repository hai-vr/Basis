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
            string trackerPlacementPath = BasisTrackerPlacementSweep.DefaultPath();
            string multiTrackerRotPath = BasisMultiTrackerRotationSweep.DefaultPath();
            string multiTrackerTemporalPath = BasisMultiTrackerRotationSweep.DefaultTemporalPath();
            string calibMathPath = BasisCalibrationMathSweep.DefaultPath();
            string twistPath = BasisTwistSweep.DefaultPath();
            string spinePath = BasisSpineSweep.DefaultPath();
            string remoteBonePath = BasisRemoteBoneSweep.DefaultPath();
            string capsulePath = BasisCapsuleCollisionSweep.DefaultPath();
            string spineClampPath = BasisSpineClampSweep.DefaultPath();
            string hipHingePath = BasisHipHingeSweep.DefaultPath();
            string chestSpringPath = BasisChestSpringSweep.DefaultPath();
            string crouchPath = BasisCrouchOffsetSweep.DefaultPath();
            string spineBendPath = BasisSpineBendSweep.DefaultPath();
            string swivelFilterPath = BasisSwivelFilterSweep.DefaultPath();
            string swingContinuityPath = BasisSwingContinuitySweep.DefaultPath();
            string footPath = BasisFootIKSweep.DefaultPath();
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

            var jobs = new System.Collections.Generic.List<System.Func<Row[]>>();

            // Side-specific grid sweeps run for BOTH sides (RIGHT then LEFT, mirrored) so a left/right
            // asymmetry trips a gate. Each side writes its OWN csv (_L/_R) -- concurrent writes to one
            // file would corrupt it. Head has no side. The jobs all fan out in parallel below.
            foreach (bool isLeft in new[] { false, true })
            {
                bool L = isLeft;
                string side = L ? "L" : "R";
                string ap = SidePath(armPath, L), shp = SidePath(shoulderPath, L), lp = SidePath(legPath, L), pp = SidePath(protectPath, L);

                jobs.Add(() =>
                {
                    try
                    {
                        var cfg = BasisArmIKSweepConfig.Default();
                        cfg.Steps = new Vector3Int(armSteps, armSteps, armSteps);
                        cfg.IsLeft = L;
                        var s = BasisArmIKSweep.Run(cfg, ap);
                        var g = BasisIKTestGates.GateArm(s);
                        var ge = BasisIKTestGates.GateArmElbowDirection(s);
                        return new[]
                        {
                            new Row { Name = $"Arm IK ({side})", Ok = g.pass, Detail = g.reason, Path = ap },
                            new Row { Name = $"Arm IK · elbow dir ({side})", Ok = ge.pass, Detail = ge.reason, Path = ap },
                        };
                    }
                    catch (System.Exception e) { return new[] { new Row { Name = $"Arm IK ({side})", Ok = false, Detail = e.Message, Path = null } }; }
                });
                jobs.Add(() => Job($"Shoulder ({side})", shp, () => { var cfg = BasisShoulderSweepConfig.Default(); cfg.IsLeft = L; var s = BasisShoulderSweep.Run(cfg, shp); return BasisIKTestGates.GateShoulder(s); }));
                jobs.Add(() => Job($"Leg IK ({side})", lp, () => { var cfg = BasisLegIKSweepConfig.Default(); cfg.IsLeft = L; var s = BasisLegIKSweep.Run(cfg, lp); return BasisIKTestGates.GateLeg(s); }));
                jobs.Add(() => Job($"Elbow Protect ({side})", pp, () => { var cfg = BasisElbowProtectSweepConfig.Default(); cfg.IsLeft = L; var s = BasisElbowProtectSweep.Run(cfg, pp); return BasisIKTestGates.GateElbow(s); }));
            }
            // Head and Leg Inversion have no per-side config (symmetric) -- run once.
            jobs.Add(() => Job("Head", headPath, () => { var s = BasisHeadSweep.Run(BasisHeadSweepConfig.Default(), headPath); return BasisIKTestGates.GateHead(s); }));
            jobs.Add(() => Job("Leg Inversion", legInvPath, () => { var cfg = BasisLegInversionConfig.Default(); cfg.SafeConeDeg = BasisIKTestGates.LegInvertHintSafeConeDeg; var s = BasisLegInversionSweep.Run(cfg, legInvPath); return BasisIKTestGates.GateLegInversion(s); }));
            jobs.Add(() => Job("Tracker Placement", trackerPlacementPath, () => { var s = BasisTrackerPlacementSweep.Run(BasisTrackerPlacementSweepConfig.Default(), trackerPlacementPath); return BasisIKTestGates.GateTrackerPlacement(s); }));
            jobs.Add(() => Job("Multi-Tracker Rotation", multiTrackerRotPath, () => { var s = BasisMultiTrackerRotationSweep.Run(BasisMultiTrackerRotationConfig.Default(), multiTrackerRotPath); return BasisIKTestGates.GateMultiTrackerRotation(s); }));
            jobs.Add(() => Job("Multi-Tracker Rotation · temporal", multiTrackerTemporalPath, () => { var s = BasisMultiTrackerRotationSweep.RunTemporal(BasisMultiTrackerRotationConfig.Default(), Basis.Scripts.Device_Management.Devices.Pairing.BasisMidpointFusionTunables.Default(), 1f / 90f, 0f, multiTrackerTemporalPath); return BasisIKTestGates.GateMultiTrackerRotationTemporal(s); }));
            jobs.Add(() => Job("Calibration Math", calibMathPath, () => { var s = BasisCalibrationMathSweep.Run(BasisCalibrationMathSweepConfig.Default(), calibMathPath); return BasisIKTestGates.GateCalibrationMath(s); }));
            jobs.Add(() => Job("Twist", twistPath, () => { var s = BasisTwistSweep.Run(BasisTwistSweepConfig.Default(), twistPath); return BasisIKTestGates.GateTwist(s); }));
            jobs.Add(() => Job("Spine", spinePath, () => { var s = BasisSpineSweep.Run(BasisSpineSweepConfig.Default(), spinePath); return BasisIKTestGates.GateSpine(s); }));
            jobs.Add(() => Job("Remote Bone", remoteBonePath, () => { var s = BasisRemoteBoneSweep.Run(BasisRemoteBoneSweepConfig.Default(), remoteBonePath); return BasisIKTestGates.GateRemoteBone(s); }));
            jobs.Add(() => Job("Capsule Collision", capsulePath, () => { var s = BasisCapsuleCollisionSweep.Run(BasisCapsuleCollisionSweepConfig.Default(), capsulePath); return BasisIKTestGates.GateCapsuleCollision(s); }));
            jobs.Add(() => Job("Spine Clamp", spineClampPath, () => { var s = BasisSpineClampSweep.Run(BasisSpineClampSweepConfig.Default(), spineClampPath); return BasisIKTestGates.GateSpineClamp(s); }));
            jobs.Add(() => Job("Hip Hinge", hipHingePath, () => { var s = BasisHipHingeSweep.Run(BasisHipHingeSweepConfig.Default(), hipHingePath); return BasisIKTestGates.GateHipHinge(s); }));
            jobs.Add(() => Job("Chest Spring", chestSpringPath, () => { var s = BasisChestSpringSweep.Run(BasisChestSpringSweepConfig.Default(), chestSpringPath); return BasisIKTestGates.GateChestSpring(s); }));
            jobs.Add(() => Job("Crouch Offset", crouchPath, () => { var s = BasisCrouchOffsetSweep.Run(BasisCrouchOffsetSweepConfig.Default(), crouchPath); return BasisIKTestGates.GateCrouchOffset(s); }));
            jobs.Add(() => Job("Spine Bend", spineBendPath, () => { var s = BasisSpineBendSweep.Run(BasisSpineBendSweepConfig.Default(), spineBendPath); return BasisIKTestGates.GateSpineBend(s); }));
            jobs.Add(() => Job("Swivel Filter", swivelFilterPath, () => { var s = BasisSwivelFilterSweep.Run(BasisSwivelFilterSweepConfig.Default(), swivelFilterPath); return BasisIKTestGates.GateSwivelFilter(s); }));
            jobs.Add(() => Job("Swing Continuity", swingContinuityPath, () => { var s = BasisSwingContinuitySweep.Run(BasisSwingContinuitySweepConfig.Default(), swingContinuityPath); return BasisIKTestGates.GateSwingContinuity(s); }));

            if (includeTraj)
            {
                foreach (bool isLeft in new[] { false, true })
                {
                    bool L = isLeft;
                    string side = L ? "L" : "R";
                    string atp = SidePath(armTrajPath, L), atmp = SidePath(armTempPath, L), athp = SidePath(armTempHandPath, L), attp = SidePath(armTempTrackPath, L);
                    string ptp = SidePath(protectTrajPath, L), ltp = SidePath(legTrajPath, L), ltmp = SidePath(legTempPath, L);

                    jobs.Add(() => Job($"Arm IK · traj ({side})", atp, () => { var c = BasisArmIKSweepConfig.Default(); c.IsLeft = L; var s = BasisArmIKSweep.RunTrajectories(c, trajNoise, atp); return BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg); }));
                    jobs.Add(() => Job($"Arm IK · temporal ({side})", atmp, () => { var c = BasisArmIKSweepConfig.Default(); c.IsLeft = L; var s = BasisArmIKSweep.RunTemporal(c, 0f, 0f, 1f / 90f, atmp); return BasisIKTestGates.GateTemporal(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg); }));
                    jobs.Add(() => Job($"Arm IK · temporal+handnoise ({side})", athp, () => { var c = BasisArmIKSweepConfig.Default(); c.IsLeft = L; var s = BasisArmIKSweep.RunTemporal(c, 0f, trajNoise, 1f / 90f, athp); return (s.Ok, $"elbowJitter={s.WorstElbowJitterM * 1000f:F0}mm pop={s.WorstPopDeg:F0} (hand noise {trajNoise * 1000f:F0}mm)"); }));
                    jobs.Add(() => Job($"Arm IK · temporal+tracker ({side})", attp, () => { var c = BasisArmIKSweepConfig.Default(); c.IsLeft = L; var s = BasisArmIKSweep.RunTemporal(c, trajNoise, 0f, 1f / 90f, attp); return (s.Ok, $"elbowJitter={s.WorstElbowJitterM * 1000f:F0}mm glideJitter={s.WorstRoughDeg:F2} (hint noise {trajNoise * 1000f:F0}mm)"); }));
                    jobs.Add(() => Job($"Elbow Protect · traj ({side})", ptp, () => { var c = BasisElbowProtectSweepConfig.Default(); c.IsLeft = L; var s = BasisElbowProtectSweep.RunTrajectories(c, trajNoise, ptp); return BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg); }));
                    jobs.Add(() => Job($"Leg IK · traj ({side})", ltp, () => { var c = BasisLegIKSweepConfig.Default(); c.IsLeft = L; var s = BasisLegIKSweep.RunTrajectories(c, trajNoise, 1f / 90f, false, ltp); return BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg); }));
                    jobs.Add(() => Job($"Leg IK · temporal+footnoise ({side})", ltmp, () => { var c = BasisLegIKSweepConfig.Default(); c.IsLeft = L; var s = BasisLegIKSweep.RunTrajectories(c, trajNoise, 1f / 90f, true, ltmp); return (s.Ok, $"kneeJitter={s.WorstKneeJitterM * 1000f:F0}mm pop={s.WorstPopDeg:F0} (foot noise {trajNoise * 1000f:F0}mm)"); }));
                }
                jobs.Add(() => Job("Head · traj", headTrajPath, () => { var s = BasisHeadSweep.RunTrajectories(BasisHeadSweepConfig.Default(), 0.3f, headTrajPath); return BasisIKTestGates.GateTrajectory(s.Ok, s.Error, s.WorstPopDeg, s.WorstRoughDeg); }));
                jobs.Add(() => Job("Leg Inversion · temporal", legInvTempPath, () => { var c = BasisLegInversionConfig.Default(); c.SafeConeDeg = BasisIKTestGates.LegInvertHintSafeConeDeg; var s = BasisLegInversionSweep.RunTemporal(c, trajNoise, legInvTempPath); return BasisIKTestGates.GateLegInversionTemporal(s); }));
            }

            // Fan out: one worker thread per sweep (the .NET thread pool caps concurrency to the core count).
            var running = new System.Threading.Tasks.Task<Row[]>[jobs.Count];
            for (int i = 0; i < jobs.Count; i++) { var job = jobs[i]; running[i] = System.Threading.Tasks.Task.Run(job); }
            try { System.Threading.Tasks.Task.WaitAll(running); }
            catch (System.AggregateException) { } // every job already captures its own exception into a FAIL row

            foreach (var t in running)
                foreach (var row in t.Result)
                    Record(row.Name, row.Ok, row.Detail, row.Path);

            // Foot placement runs on the MAIN THREAD (not in the parallel fan-out above): it ticks the real
            // Burst foot job over NativeArrays, which the editor's job-safety system won't let us allocate on
            // a worker thread. It is a short temporal sim, so running it serially here costs almost nothing.
            try
            {
                var fs = BasisFootIKSweep.Run(BasisFootIKSweepConfig.Default(), footPath);
                var fg = BasisIKTestGates.GateFoot(fs);
                Record("Foot Placement", fg.pass, fg.reason, footPath);
            }
            catch (System.Exception e) { Record("Foot Placement", false, e.Message, null); }
        }

        // One sweep job for the parallel Run All: runs body on a worker thread, turns its (pass, reason) into
        // a Row, and captures any exception (incl. a sweep that touches a main-thread-only API) into a FAIL
        // row so one bad sweep can't sink the rest.
        static Row[] Job(string name, string path, System.Func<(bool pass, string reason)> body)
        {
            try { var (pass, reason) = body(); return new[] { new Row { Name = name, Ok = pass, Detail = reason, Path = path } }; }
            catch (System.Exception e) { return new[] { new Row { Name = name, Ok = false, Detail = e.Message, Path = null } }; }
        }

        // Inserts _L / _R before the extension so the left and right sweeps write distinct CSVs (two
        // sides writing the same file in parallel would corrupt it). Pure string op -- safe off-thread.
        static string SidePath(string basePath, bool isLeft)
        {
            string dir = System.IO.Path.GetDirectoryName(basePath);
            string name = System.IO.Path.GetFileNameWithoutExtension(basePath);
            string ext = System.IO.Path.GetExtension(basePath);
            return System.IO.Path.Combine(dir, name + (isLeft ? "_L" : "_R") + ext);
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
