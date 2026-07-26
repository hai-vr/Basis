using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Basis.IK;
using Basis.IK.Mocap;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    // ⚠ Alias inside the namespace -- com.basis.sdk declares an unrelated global BasisMotionClip.
    using BasisMotionClip = Basis.IK.Mocap.BasisMotionClip;
    public sealed class BasisArmWristAxialBoundTests
    {
        const float Envelope = BasisArmSolveCore.WristAxialHardDeg;   // 15

        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        struct Row
        {
            public float WristPost;      // hand vs solved forearm, AFTER the bound, through the stream
            public float WristPre;       // the same, with the RAW controller rotation
            public float Reported;       // r.WristAxialDeg -- the solver's own reading of WristPre
            public float Guard;          // r.WristAxialGuardDeg
            public float Fore, Hum, Flex, SupMax, ProMax;
            public float HandPoseErr, FingerErr, PalmErr;
            public bool Tracker;
            public float Reach, El, HandRoll, Aux;
            public Quaternion TipPost, TipRaw;
            public Vector3 ForeAxis;
            public Vector3 CoreElbow, CoreHand;     // the SOLVER's own joints
            public Vector3 StreamElbow, StreamHand; // what the runtime's composition actually produces
        }

        /// <summary>The envelope sweep, identical in shape to BasisArmInvariantNetRollEnvelopeTests' own so the
        /// two report the same quantity over the same poses. `feedLower` false is the CONTROL: the bound's
        /// documented off-switch, which restores the unbounded wrist the sweep used to measure.</summary>
        static List<Row> Sweep(bool feedLower, bool correlatedTracker = false)
        {
            var rows = new List<Row>();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (float el in new[] { -60f, -25f, 15f })
            foreach (float reach in new[] { 0.75f, 0.90f, 0.98f })
            foreach (bool tracker in new[] { false, true })
            for (int hr = 0; hr <= 360; hr += 5)
            for (int aux = 0; aux <= 300; aux += 60)
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(30f, el);
                s.Reach = reach;
                s.HandRollDeg = hr;
                s.HintMode = tracker ? BasisArmNet.HintTracker : BasisArmNet.HintModel;
                s.HintAzimuthDeg = aux;
                s.HintRhoMin = 0.015f;
                s.TrackerRollDeg = tracker ? (correlatedTracker ? hr : aux) : float.NaN;
                s.RefPerp = Vector3.up;
                s.FeedTwistBind = true;
                s.FeedLowerBind = feedLower;
                s.FeedTip = true;
                s.AnimForeRollDeg = 35f + aux;
                s.AnimHandRollDeg = -20f;
                s.WristBound = true;

                BasisArmSolveInput i = BasisArmNet.Build(s);
                BasisArmNet.Solve(i, out BasisArmSolveResult r);
                BasisArmNet.StreamCompose(i, r, out Vector3 elbow, out Vector3 hand, out Quaternion root, out Quaternion mid);

                Vector3 foreAxis = hand - elbow;
                if (foreAxis.sqrMagnitude < 1e-10f) continue;
                foreAxis = foreAxis.normalized;

                Quaternion tipRaw = i.TargetRotation * i.TargetOffset;

                Row row = default;
                row.TipPost = r.TipRotation; row.TipRaw = tipRaw; row.ForeAxis = foreAxis;
                row.CoreElbow = r.ElbowSolved; row.CoreHand = r.HandSolved;
                row.StreamElbow = elbow; row.StreamHand = hand;
                row.WristPost = BasisArmNet.TwistDeg(r.TipRotation * Quaternion.Inverse(mid), foreAxis);
                row.WristPre = BasisArmNet.TwistDeg(tipRaw * Quaternion.Inverse(mid), foreAxis);
                row.Reported = r.WristAxialDeg;
                row.Guard = r.WristAxialGuardDeg;

                Quaternion neutralFore = root * (Quaternion.Inverse(rig.BindHumerusRot) * rig.BindLowerArmRot);
                row.Fore = BasisArmNet.TwistDeg(mid * Quaternion.Inverse(neutralFore), foreAxis);
                row.Hum = r.HumeralTwistDeg;
                row.Flex = 180f - r.ElbowAngleDeg;
                row.SupMax = Mathf.Clamp(50f + 0.35f * row.Flex, 50f, 85f);
                row.ProMax = Mathf.Clamp(85f - 0.20f * row.Flex, 55f, 85f);

                row.HandPoseErr = BasisArmNet.PoseChangeDeg(tipRaw, r.TipRotation);
                row.FingerErr = Vector3.Angle(tipRaw * Vector3.right, r.TipRotation * Vector3.right);
                row.PalmErr = Vector3.Angle(tipRaw * Vector3.up, r.TipRotation * Vector3.up);

                row.Tracker = tracker; row.Reach = reach; row.El = el; row.HandRoll = hr; row.Aux = aux;
                rows.Add(row);
            }
            return rows;
        }

        // ============================================================================================
        // 1. THE BOUND ITSELF
        // ============================================================================================

        /// <summary>
        /// ⭐ THE HAND'S AXIAL ROLL AGAINST ITS OWN FOREARM, HELD TO THE GRIPPED-CARPUS ENVELOPE ON BOTH
        /// PATHS, WITH THE UNBOUNDED CONTROL ON THE SAME SWEEP.
        ///
        /// The control is the bound's own documented off-switch -- BindLowerArmRotation declined -- so a
        /// pass is this stage working and not the sweep having gone quiet. Before the bound landed this
        /// sweep measured 75.7 / 179.8 deg, which is what the control still measures.
        /// </summary>
        [Test]
        public void WristAxialRoll_StaysInsideTheGrippedCarpusEnvelope_OnBothPaths()
        {
            List<Row> guarded = Sweep(feedLower: true);
            List<Row> control = Sweep(feedLower: false);
            Assert.That(guarded.Count, Is.GreaterThan(1000), "the sweep is too small.");

            Worst(guarded, false, out float gm, out Row rm);
            Worst(guarded, true, out float gt, out Row rt);
            Worst(control, false, out float cm, out _);
            Worst(control, true, out float ct, out _);

            var log = new StringBuilder();
            log.AppendLine($"      path      worst |hand-vs-forearm axial roll|      control (bound declined)");
            log.AppendLine($"      model   {gm,14:F2}                        {cm,14:F1}");
            log.AppendLine($"      track   {gt,14:F2}                        {ct,14:F1}");
            log.AppendLine($"      envelope{Envelope,14:F0}   (soft {BasisArmSolveCore.WristAxialSoftDeg:F0})");
            log.AppendLine($"      worst model row: reach {rm.Reach:F2} el {rm.El:F0} roll {rm.HandRoll:F0} " +
                           $"demand {rm.WristPre:F1} -> {rm.WristPost:F2} (guard {rm.Guard:F1})");
            log.AppendLine($"      worst track row: reach {rt.Reach:F2} el {rt.El:F0} roll {rt.HandRoll:F0} " +
                           $"demand {rt.WristPre:F1} -> {rt.WristPost:F2} (guard {rt.Guard:F1})");
            TestContext.WriteLine("\n  wrist axial roll, BY PATH:\n" + log);

            BasisArmNet.Gate("hand-vs-forearm axial roll, NO-TRACKER path -- the radiocarpal joint has no axial " +
                             "degree of freedom, so this is the joint that cannot take what the absorbers above it drop",
                             gm, Envelope, cm, 60f);
            BasisArmNet.Gate("hand-vs-forearm axial roll, ELBOW-TRACKER path",
                             gt, Envelope, ct, 150f);
        }

        /// <summary>
        /// ⚠️ THE SOLVER'S OWN BOOKKEEPING, PINNED TO THE STREAM. A guard shipped in this file behind a gate
        /// that "proved" a 0.000 mm hand displacement by comparing two result fields the guard never wrote,
        /// and 205 mm went with it. So r.WristAxialDeg is not taken on trust: it is checked against the same
        /// quantity measured through the runtime's actual composition, which is the only thing the user sees.
        /// </summary>
        [Test]
        public void TheSolversWristReading_MatchesTheStreamComposition()
        {
            List<Row> rows = Sweep(feedLower: true);
            float worst = 0f; Row w = default;
            int engaged = 0;
            foreach (Row r in rows)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(r.Reported, r.WristPre));
                if (d > worst) { worst = d; w = r; }
                if (Mathf.Abs(r.Guard) > 1e-4f) engaged++;
            }
            TestContext.WriteLine($"\n  r.WristAxialDeg vs the stream-composed hand-vs-forearm roll: worst " +
                                  $"disagreement {worst:F5} deg over {rows.Count} poses ({engaged} engaged the bound).");

            Assert.That(engaged, Is.GreaterThan(rows.Count / 10),
                $"only {engaged} of {rows.Count} poses engaged the bound; this sweep is not exercising it.");
            Assert.That(worst, Is.LessThan(0.05f),
                $"the solver reports a hand-vs-forearm roll that the STREAM does not produce (worst {worst:F4} deg, " +
                $"at reach {w.Reach:F2} el {w.El:F0} roll {w.HandRoll:F0}). The solver's `midRot` is supposed to BE " +
                "the stream's final forearm -- MidPostRoll carries inverse(twistR) precisely so HintDelta's humeral " +
                "roll cancels out of the forearm -- and if that has stopped being true the bound is measuring one " +
                "forearm and the runtime is rendering another.");
        }

        // ============================================================================================
        // 2. ⭐ THE RESIDUAL CANNOT MOVE -- STRUCTURALLY, NOT BY OBSERVATION
        // ============================================================================================

        /// <summary>
        /// ⭐⭐ THE ASSERTION THAT MAKES "IT DID NOT RELOCATE THE DISCONTINUITY" A PROOF RATHER THAN A SURVEY.
        ///
        /// Capping the forearm alone once moved this file's tear into the twist bone (54.10 deg = 360*f);
        /// capping a tracker roll without wrapping left a 227.3 deg unbounded path. Both were found by
        /// looking. Looking does not scale, so this gate is arranged so that relocation is not possible:
        /// BindHandRotation is read by the wrist bound and by NOTHING ELSE in the solver, so rolling it
        /// changes the bound's correction and must change NOTHING else. Every other output field is
        /// compared BIT-FOR-BIT, not to a tolerance.
        ///
        /// ⚠️ AND IT IS NOT VACUOUS: TipRotation is required to actually differ, so a bound that had quietly
        /// stopped engaging would fail here rather than pass by doing nothing.
        /// </summary>
        [Test]
        public void TheBound_WritesNothingButTheHand()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            int compared = 0, differed = 0;

            foreach (float el in new[] { -60f, -25f, 15f })
            foreach (float reach in new[] { 0.75f, 0.90f, 0.98f })
            foreach (bool tracker in new[] { false, true })
            for (int hr = 0; hr <= 360; hr += 15)
            for (int aux = 0; aux <= 300; aux += 60)
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(30f, el); s.Reach = reach; s.HandRollDeg = hr;
                s.HintMode = tracker ? BasisArmNet.HintTracker : BasisArmNet.HintModel;
                s.HintAzimuthDeg = aux; s.HintRhoMin = 0.015f;
                s.TrackerRollDeg = tracker ? aux : float.NaN;
                s.RefPerp = Vector3.up;
                s.FeedTwistBind = true; s.FeedLowerBind = true; s.FeedTip = true;
                s.AnimForeRollDeg = 35f + aux; s.AnimHandRollDeg = -20f;
                s.WristBound = true;

                BasisArmSolveInput a = BasisArmNet.Build(s);
                BasisArmSolveInput b = a;
                // Two different bind HAND relations. Both are read ONLY by the wrist bound.
                a.BindHandRotation = rig.BindLowerArmRot;
                b.BindHandRotation = Quaternion.AngleAxis(70f, rig.BindHumerusDir) * rig.BindLowerArmRot;

                BasisArmNet.Solve(a, out BasisArmSolveResult ra);
                BasisArmNet.Solve(b, out BasisArmSolveResult rb);
                compared++;
                if (!BasisArmNet.Same(ra.TipRotation, rb.TipRotation)) differed++;

                Assert.That(BasisArmNet.Same(ra.MidDelta, rb.MidDelta)
                         && BasisArmNet.Same(ra.RootDelta, rb.RootDelta)
                         && BasisArmNet.Same(ra.HintDelta, rb.HintDelta)
                         && BasisArmNet.Same(ra.MidPostRoll, rb.MidPostRoll)
                         && BasisArmNet.Same(ra.ElbowSolved, rb.ElbowSolved)
                         && BasisArmNet.Same(ra.HandSolved, rb.HandSolved)
                         && BasisArmNet.Same(ra.RootRotationSolved, rb.RootRotationSolved)
                         && BasisArmNet.Same(ra.MidRotationSolved, rb.MidRotationSolved)
                         && ra.ForearmRollDeg == rb.ForearmRollDeg
                         && ra.WristReliefDeg == rb.WristReliefDeg
                         && ra.HumeralTwistDeg == rb.HumeralTwistDeg
                         && ra.HumeralTwistGuardDeg == rb.HumeralTwistGuardDeg
                         && ra.HandError == rb.HandError,
                    $"THE WRIST BOUND REACHED SOMETHING OTHER THAN THE HAND. Changing BindHandRotation -- which " +
                    $"nothing but the bound reads -- moved another output at reach {reach:F2} el {el:F0} roll {hr} " +
                    $"aux {aux} ({(tracker ? "tracker" : "model")}). The residual this stage drops is supposed to be " +
                    "unable to reach the forearm, the twist bones, the elbow or the humerus, and that is the whole " +
                    "reason it can be dropped safely. If this fails, it can.");
            }

            TestContext.WriteLine($"\n  {compared} pose pairs: every non-hand output bit-identical under a 70 deg " +
                                  $"change of the bind hand relation; TipRotation differed on {differed} of them.");
            Assert.That(differed, Is.GreaterThan(compared / 4),
                $"TipRotation differed on only {differed} of {compared} pose pairs, so this gate is mostly comparing " +
                "a bound that never engaged against itself. It would pass whatever the solver did.");
        }

        /// <summary>
        /// THE CORRECTION IS A PURE ROLL ABOUT THE SOLVED FOREARM'S OWN LONG AXIS -- the one axis on which
        /// it can bound the wrist without changing where the hand points relative to the arm.
        ///
        /// ⚠️ NOTE WHAT IS DELIBERATELY *NOT* ASSERTED HERE, because asserting it would be the vacuous-gate
        /// class again: "the hand does not move". TipRotation carries no position and the stream never reads
        /// it when placing a joint, so a hand-position check here would compare two numbers this stage
        /// cannot affect and would pass no matter what axis the correction used. The falsifiable statement
        /// is the AXIS, and it is the one that dies if the correction is ever applied about shoulder->hand
        /// or about the target direction instead.
        /// </summary>
        [Test]
        public void TheCorrection_IsAPureRollAboutTheSolvedForearm()
        {
            List<Row> rows = Sweep(feedLower: true);
            float worstSwing = 0f, worstAngle = 0f, worstDrift = 0f, worstElbow = 0f, worstHand = 0f;
            int engaged = 0;
            Row ws = default;

            foreach (Row r in rows)
            {
                // How far the CORE's own forearm has drifted from the one the stream composes. The bound is
                // built about the core's axis and judged about the stream's, so this is the reconciliation
                // that has to hold for the two to be the same statement at all. The POSITIONS are what the
                // house gate (BasisArmInvariantNetStreamTests, 1e-5 m) pins; the ANGLE is what this stage
                // actually consumes, and it is a difference of two nearly-equal vectors over a 0.26 m
                // forearm, so it amplifies those positions by ~2/0.26 = 7.7 per metre.
                worstElbow = Mathf.Max(worstElbow, Vector3.Distance(r.CoreElbow, r.StreamElbow));
                worstHand = Mathf.Max(worstHand, Vector3.Distance(r.CoreHand, r.StreamHand));
                Vector3 coreAxis = r.CoreHand - r.CoreElbow;
                if (coreAxis.sqrMagnitude > 1e-12f)
                {
                    // ⚠️ atan2, NOT Vector3.Angle. Vector3.Angle is acos, whose derivative is infinite at 1,
                    // and these two vectors are near-parallel by construction: measured with acos this
                    // reported 0.0198 deg between axes that agree to a ten-thousandth of a millimetre --
                    // the same 0.1 deg noise floor BasisArmNet.PoseChangeDeg exists to avoid, reached here
                    // by the same mistake. atan2(|cross|, dot) is well-conditioned at zero.
                    Vector3 ca = coreAxis.normalized;
                    float drift = Mathf.Atan2(Vector3.Cross(ca, r.ForeAxis).magnitude,
                                              Vector3.Dot(ca, r.ForeAxis)) * Mathf.Rad2Deg;
                    if (drift > worstDrift) worstDrift = drift;
                }

                if (Mathf.Abs(r.Guard) < 1e-3f) continue;
                engaged++;

                // ⚠️ THE SWING, NOT THE AXIS DIRECTION. A tiny correction has a quaternion vector part of
                // ~1e-5, so its DIRECTION is float noise and an axis-angle check on it reports up to 0.15 deg
                // on a correction that is provably exact. The physically meaningful quantity -- and the one
                // that is scale-free -- is how much SWING the correction adds: strip the twist about the
                // forearm and whatever is left is the hand being pushed off where the controller points.
                Quaternion delta = BasisArmNet.NormalizeQ(r.TipPost * Quaternion.Inverse(r.TipRaw));
                float applied = BasisArmNet.TwistDeg(delta, r.ForeAxis);
                Quaternion twistOnly = Quaternion.AngleAxis(applied, r.ForeAxis);
                float swing = BasisArmNet.PoseChangeDeg(delta, twistOnly);
                if (swing > worstSwing) { worstSwing = swing; ws = r; }

                float d = Mathf.Abs(Mathf.DeltaAngle(applied, r.Guard));
                if (d > worstAngle) worstAngle = d;
            }

            TestContext.WriteLine($"\n  {engaged} of {rows.Count} poses engaged the bound.\n" +
                                  $"    worst SWING added by the correction        {worstSwing:F5} deg\n" +
                                  $"    worst applied-roll vs r.WristAxialGuardDeg {worstAngle:F5} deg\n" +
                                  $"    core vs stream: elbow {worstElbow * 1000f:F6} mm, hand {worstHand * 1000f:F6} mm, " +
                                  $"forearm axis {worstDrift:F5} deg");

            Assert.That(engaged, Is.GreaterThan(500), $"only {engaged} poses engaged the bound.");
            Assert.That(worstElbow, Is.LessThan(1e-5f),
                $"the stream puts the elbow {worstElbow * 1000f:0.000000} mm from ElbowSolved.");
            Assert.That(worstHand, Is.LessThan(1e-5f),
                $"the stream puts the hand {worstHand * 1000f:0.000000} mm from HandSolved. The bound reads the " +
                "forearm from the solver's own joints, so if those are fiction the bound is guarding a forearm the " +
                "user never sees.");
            Assert.That(worstDrift, Is.LessThan(0.01f),
                $"the solver's forearm axis has drifted {worstDrift:F5} deg from the one the runtime composes. The " +
                "bound is measured and applied about the CORE's axis and rendered about the STREAM's, so if those " +
                "come apart the bound is guarding a forearm the user never sees.");
            Assert.That(worstSwing, Is.LessThan(0.05f),
                $"the wrist correction adds {worstSwing:F4} deg of SWING to the hand (worst at reach " +
                $"{ws.Reach:F2} el {ws.El:F0} roll {ws.HandRoll:F0}). About any axis but the forearm's own it is not " +
                "a wrist roll -- it swings the hand off where the controller points, which is a far more visible " +
                "error than the unfulfilled roll this stage exists to trade for.");
            Assert.That(worstAngle, Is.LessThan(0.05f),
                $"r.WristAxialGuardDeg disagrees with the roll actually applied to TipRotation by {worstAngle:F3} deg.");
        }

        // ============================================================================================
        // 3. THE SEAM
        // ============================================================================================

        /// <summary>
        /// ⭐ A FULL TURN OF THE CONTROLLER, IN 0.02 DEG STEPS, THROUGH THE +/-180 SEAM.
        ///
        /// Every principal-angle clamp in this file has had this defect and three of them shipped with it:
        /// a bare min/max onto an arc sends two hand poses 0.02 deg apart to two hands 2*bound apart.
        /// Measured amplifications when they were live: 1430x (humeral guard) and 3201x (no-tracker forearm).
        ///
        /// The wrist bound caps the BOUND by the distance to the seam rather than the PULL, which is the
        /// opposite of what its three siblings do -- capping the pull would leave the wrist AT 180 deg,
        /// which is the entire defect. It can afford to, and that is the whole reason this stage is the easy
        /// one: 15 deg has 165 deg of room to reach the seam in, so the return slope is 15/165 = 0.09.
        /// </summary>
        [Test]
        public void TheBound_IsContinuous_ThroughAFullTurnOfTheController()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (bool tracker in new[] { false, true })
            foreach (float reach in new[] { 0.75f, 0.90f, 0.98f })
            {
                var hand = new BasisArmNet.Channel($"{(tracker ? "track" : "model")} r{reach:F2} hand pose");
                var wristCh = new BasisArmNet.Channel($"{(tracker ? "track" : "model")} r{reach:F2} wrist angle");
                Quaternion prevTip = Quaternion.identity;
                float prevWrist = 0f;
                bool have = false;

                for (float hr = 0f; hr <= 360f; hr += 0.02f)
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(30f, -25f); s.Reach = reach; s.HandRollDeg = hr;
                    s.HintMode = tracker ? BasisArmNet.HintTracker : BasisArmNet.HintModel;
                    s.HintAzimuthDeg = 60f; s.HintRhoMin = 0.015f;
                    s.TrackerRollDeg = tracker ? 60f : float.NaN;
                    s.RefPerp = Vector3.up;
                    s.FeedTwistBind = true; s.FeedLowerBind = true; s.FeedTip = true;
                    s.AnimForeRollDeg = 95f; s.AnimHandRollDeg = -20f;
                    s.WristBound = true;

                    BasisArmSolveInput i = BasisArmNet.Build(s);
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    BasisArmNet.StreamCompose(i, r, out Vector3 e, out Vector3 h, out _, out Quaternion mid);
                    Vector3 fa = (h - e).normalized;
                    float wrist = BasisArmNet.TwistDeg(r.TipRotation * Quaternion.Inverse(mid), fa);

                    if (have)
                    {
                        hand.Add(BasisArmNet.PoseChangeDeg(prevTip, r.TipRotation), hr);
                        wristCh.Add(Mathf.Abs(Mathf.DeltaAngle(prevWrist, wrist)), hr);
                    }
                    prevTip = r.TipRotation; prevWrist = wrist; have = true;
                }

                string ctx = $"controller roll sweep ({(tracker ? "tracker" : "model")}, reach {reach:F2})";
                string f1 = BasisArmNet.GateSmooth(hand, 0.5f, 6f, ctx, log);
                string f2 = BasisArmNet.GateSmooth(wristCh, 0.5f, 6f, ctx, log);
                if (f1 != null) findings.Add(f1);
                if (f2 != null) findings.Add(f2);
            }

            BasisArmNet.Report(findings, log, "wrist bound continuity across the +/-180 seam");
        }

        // ============================================================================================
        // 4. THE ZERO-DEFAULT DECLINES
        // ============================================================================================

        /// <summary>
        /// THE OFF-SWITCH IS EXACT. A rig that has not baked BindLowerArmRotation gets the hand written
        /// straight from the controller, bit for bit, exactly as before this stage existed -- because
        /// without the rig's own bind there is no rig-defined zero for this angle and a bound against an
        /// invented one is worse than none.
        /// </summary>
        [Test]
        public void DecliningTheLowerArmBind_DeclinesTheBound_BitIdentical()
        {
            List<Row> rows = Sweep(feedLower: false);
            int n = 0;
            foreach (Row r in rows)
            {
                n++;
                Assert.That(BasisArmNet.Same(r.TipPost, r.TipRaw), Is.True,
                    $"with BindLowerArmRotation declined the hand must be the controller's rotation bit for bit, " +
                    $"but it differs at reach {r.Reach:F2} el {r.El:F0} roll {r.HandRoll:F0}.");
                Assert.That(r.Guard, Is.EqualTo(0f), "the declined bound must report exactly zero correction.");
                Assert.That(r.Reported, Is.EqualTo(0f), "the declined bound must report exactly zero demand.");
            }
            Assert.That(n, Is.GreaterThan(1000), "the decline sweep is too small to prove anything.");
            TestContext.WriteLine($"\n  {n} poses: TipRotation bit-identical to TargetRotation*TargetOffset with the " +
                                  "lower-arm bind declined.");
        }

        /// <summary>
        /// THE NEW INPUT'S ZERO DEFAULT IS THE ALIGNED RELATION, PROVEN BIT-FOR-BIT RATHER THAN ARGUED.
        /// A zero BindHandRotation must behave EXACTLY as feeding BindLowerArmRotation itself does -- that
        /// is what "the bind hand is axially aligned with the bind forearm" means as code, and it is the
        /// assumption the no-tracker forearm roll above has always made when it reads the hand's pronation
        /// demand against the zero-pronation forearm.
        /// </summary>
        [Test]
        public void ZeroBindHandRotation_IsTheAlignedRelation_BitIdentical()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            int n = 0, engaged = 0;

            foreach (float el in new[] { -60f, -25f, 15f })
            foreach (float reach in new[] { 0.75f, 0.90f, 0.98f })
            foreach (bool tracker in new[] { false, true })
            for (int hr = 0; hr <= 360; hr += 15)
            for (int aux = 0; aux <= 300; aux += 60)
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(30f, el); s.Reach = reach; s.HandRollDeg = hr;
                s.HintMode = tracker ? BasisArmNet.HintTracker : BasisArmNet.HintModel;
                s.HintAzimuthDeg = aux; s.HintRhoMin = 0.015f;
                s.TrackerRollDeg = tracker ? aux : float.NaN;
                s.RefPerp = Vector3.up;
                s.FeedTwistBind = true; s.FeedLowerBind = true; s.FeedTip = true;
                s.AnimForeRollDeg = 35f + aux; s.AnimHandRollDeg = -20f;
                s.WristBound = true;

                BasisArmSolveInput zero = BasisArmNet.Build(s);
                BasisArmSolveInput aligned = zero;
                aligned.BindHandRotation = zero.BindLowerArmRotation;

                BasisArmNet.Solve(zero, out BasisArmSolveResult rz);
                BasisArmNet.Solve(aligned, out BasisArmSolveResult ral);
                n++;
                if (Mathf.Abs(rz.WristAxialGuardDeg) > 1e-4f) engaged++;

                Assert.That(BasisArmNet.Same(rz.TipRotation, ral.TipRotation), Is.True,
                    $"a zero BindHandRotation is documented as the aligned relation, but it does not match feeding " +
                    $"BindLowerArmRotation itself at reach {reach:F2} el {el:F0} roll {hr}.");
                Assert.That(rz.WristAxialDeg, Is.EqualTo(ral.WristAxialDeg));
                Assert.That(rz.WristAxialGuardDeg, Is.EqualTo(ral.WristAxialGuardDeg));
            }

            Assert.That(engaged, Is.GreaterThan(n / 10),
                $"only {engaged} of {n} poses engaged the bound, so this equivalence is mostly comparing two " +
                "declines against each other.");
            TestContext.WriteLine($"\n  {n} poses, {engaged} with the bound engaged: a zero BindHandRotation is " +
                                  "bit-identical to the explicitly aligned relation.");
        }

        // ============================================================================================
        // 5. ⭐ WHY THE RESIDUAL IS DROPPED AND NOT RECRUITED
        // ============================================================================================

        /// <summary>
        /// ⭐ THE MEASUREMENT THAT CHOSE THE RESIDUAL POLICY, PINNED SO "JUST RECRUIT MORE FOREARM" IS
        /// REFUTED BY A NUMBER RATHER THAN RE-ARGUED EVERY TIME SOMEONE READS THIS FILE.
        ///
        /// The alternative to dropping is to make the forearm and the humerus absorb more before the wrist
        /// has to. That is only possible if they have room WHEN THE WRIST BREACHES, and they do not. The
        /// forearm's ceiling here is the researched flexion-dependent, sign-asymmetric one --
        /// sup_max = clamp(50 + 0.35*flex, 50, 85), pro_max = clamp(85 - 0.20*flex, 55, 85), which collapses
        /// toward 50 at a straight arm because the biceps cannot supinate with the elbow extended -- and the
        /// LOOSER of the two signs is used, so this is the most generous reading available.
        /// </summary>
        [Test]
        public void TheAbsorbersAreAlreadyOverCommitted_WhenTheWristBreaches()
        {
            List<Row> rows = Sweep(feedLower: true);
            var log = new StringBuilder();
            float[] fracForeOver = new float[2];
            int[] breaching = new int[2];

            for (int p = 0; p < 2; p++)
            {
                bool tracker = p == 1;
                int n = 0, foreOver = 0, humOver = 0, both = 0;
                float sumExcess = 0f, worstExcess = 0f;
                foreach (Row r in rows)
                {
                    if (r.Tracker != tracker) continue;
                    if (Mathf.Abs(r.WristPre) <= Envelope) continue;
                    n++;
                    float ceiling = Mathf.Max(r.SupMax, r.ProMax);
                    float excess = Mathf.Abs(r.Fore) - ceiling;
                    if (excess >= 0f) { foreOver++; sumExcess += excess; if (excess > worstExcess) worstExcess = excess; }
                    bool h = Mathf.Abs(r.Hum) >= BasisArmSolveCore.HumeralTwistSoftDeg;
                    if (h) humOver++;
                    if (excess >= 0f && h) both++;
                }
                breaching[p] = n;
                fracForeOver[p] = n > 0 ? (float)foreOver / n : 0f;
                log.AppendLine($"   {(tracker ? "ELBOW-TRACKER" : "NO-TRACKER   ")}: {n} poses breach the {Envelope:F0} deg wrist envelope");
                log.AppendLine($"        forearm ALREADY past its flexion-dependent human ceiling on {100f * foreOver / Mathf.Max(n, 1):F1}% " +
                               $"of them (mean {(foreOver > 0 ? sumExcess / foreOver : 0f):F1} deg past, worst {worstExcess:F1})");
                log.AppendLine($"        humerus already past its {BasisArmSolveCore.HumeralTwistSoftDeg:F0} deg soft limit on " +
                               $"{100f * humOver / Mathf.Max(n, 1):F1}%;  BOTH over on {100f * both / Mathf.Max(n, 1):F1}%");
            }

            TestContext.WriteLine("\n  IS THERE ANYTHING TO RECRUIT INTO WHEN THE WRIST BREACHES?\n" + log +
                "\n  There is not. Recruiting would buy a smaller wrist error by deepening a forearm breach, which is\n" +
                "  relocating the defect. So the residual is dropped, and what that costs is measured separately.\n");

            Assert.That(breaching[0], Is.GreaterThan(200), "the no-tracker path is not breaching enough to judge.");
            Assert.That(breaching[1], Is.GreaterThan(200), "the tracker path is not breaching enough to judge.");

            // ⛔ INVERTED ON PURPOSE, exactly as BasisHumeralTwistElevationCorpusTests pins its own refutation:
            // this records that recruitment WAS refuted here. If it trips, the forearm has stopped being
            // over-committed when the wrist breaches and the residual policy is worth re-deriving.
            Assert.That(fracForeOver[1], Is.GreaterThan(0.75f),
                $"the forearm is now past its human ceiling on only {fracForeOver[1]:P1} of breaching ELBOW-TRACKER " +
                "poses (it was 93.1% when the drop policy was chosen). Recruiting into the forearm was rejected " +
                "because there was nothing to recruit into; if that has changed, re-derive the policy rather than " +
                "assuming this file's answer still holds.");
        }

        /// <summary>
        /// WHAT DROPPING COSTS, IN THE UNITS A VIEWER ACTUALLY PERCEIVES. The correction is a pure roll about
        /// the forearm, so the hand's POSITION is untouched and its POINTING direction moves only as far as
        /// that roll carries it. What the user loses is palm ROLL.
        ///
        /// ⚠️ THE TRACKER PATH IS REPORTED TWICE ON PURPOSE. The envelope sweep rolls the elbow tracker and
        /// the controller INDEPENDENTLY, which manufactures poses no strapped puck can produce -- a forearm
        /// measured at +101.8 deg under a hand measured at -75.5. The bound has to survive those, so they are
        /// swept. But the cost a real user pays is the CORRELATED one, where the puck is on the arm holding
        /// the controller, and quoting only the decorrelated figure would overstate it by 4x.
        /// </summary>
        [Test]
        public void WhatTheDropCosts_IsMeasuredInWhatAViewerSees()
        {
            var log = new StringBuilder();
            log.AppendLine("      path                       hand pose err        finger direction         palm facing");
            float trackerCorrelatedFinger = float.NaN;

            foreach (bool corr in new[] { false, true })
            {
                List<Row> rows = Sweep(feedLower: true, correlatedTracker: corr);
                for (int p = 0; p < 2; p++)
                {
                    bool tracker = p == 1;
                    if (corr && !tracker) continue;   // the model path does not depend on the tracker's roll
                    var pose = new List<float>(); var fing = new List<float>(); var palm = new List<float>();
                    foreach (Row r in rows)
                    {
                        if (r.Tracker != tracker) continue;
                        pose.Add(r.HandPoseErr); fing.Add(r.FingerErr); palm.Add(r.PalmErr);
                    }
                    pose.Sort(); fing.Sort(); palm.Sort();
                    string name = !tracker ? "NO-TRACKER          "
                                           : (corr ? "TRACKER (correlated)" : "TRACKER (independent)");
                    log.AppendLine($"      {name}  p50 {P(pose,.5f),5:F1} max {P(pose,1f),5:F1}   " +
                                   $"p50 {P(fing,.5f),5:F2} max {P(fing,1f),5:F1}   p50 {P(palm,.5f),5:F1} max {P(palm,1f),5:F1}");
                    if (corr && tracker) trackerCorrelatedFinger = P(fing, .5f);
                }
            }

            TestContext.WriteLine("\n  THE PRICE OF THE DROP, in degrees of hand rotation the controller asked for and\n" +
                                  "  did not get. Hand POSITION is untouched -- TipRotation carries none.\n" + log);

            Assert.That(trackerCorrelatedFinger, Is.LessThan(15f),
                $"the median finger-direction error on a physically consistent tracker rig is now " +
                $"{trackerCorrelatedFinger:F2} deg. The whole case for dropping the residual rather than recruiting " +
                "it is that an unfulfilled hand ROLL is nearly invisible while the pose it buys back is a wrist that " +
                "has come off; if the drop has started swinging where the hand POINTS, that trade no longer holds.");
        }

        // ============================================================================================
        // 6. ⭐ THE CORPUS VETO -- AND WHAT THIS CORPUS CANNOT REFEREE
        // ============================================================================================

        static string CorpusRoot => Path.GetFullPath("Packages/com.basis.framework/Tests/MocapCorpus~");
        static readonly string[] k_TierDirs = { "", "posture", "dynamic", "slow" };

        /// <summary>Elbow flexion bands, 180 - the elbow's own angle. The forearm's pronation ceiling is
        /// flexion-dependent and sign-asymmetric, so a pooled figure would hide exactly the band-specific
        /// breach that pooling has already hidden twice in this repo.</summary>
        const int k_BandDeg = 30;

        /// <summary>
        /// ⭐⭐ THE SEGMENTED VETO -- AND ITS FIRST FINDING IS THAT THIS CORPUS CANNOT REFEREE THE QUANTITY
        /// THE BOUND CONSTRAINS. THAT IS MEASURED HERE, NOT ASSUMED, BECAUSE IT WOULD OTHERWISE LOOK LIKE
        /// THE BOUND CLIPS HALF OF ALL REAL MOTION.
        ///
        /// Read naively, the corpus says hand-vs-forearm axial roll has a median of 15.0 deg and exceeds
        /// 15 deg on 49.9% of frames -- which against a 15 deg bound would be a catastrophic veto. It is
        /// not, and the reason is one number:
        ///
        ///     |forearm vs humerus| axial roll, over 266,156 arm-frames:  p50 0.0   p99 0.0   MAX 0.1 deg
        ///
        /// THE CORPUS'S FOREARM CARRIES NO PRONATION AT ALL. The BVH skeleton has no axial channel between
        /// the elbow and the wrist, so every degree of the arm's pronation -- a FOREARM motion, the radius
        /// crossing the ulna -- is dumped into the HAND joint by the retarget. The corpus's
        /// "hand-vs-forearm" angle is therefore not radiocarpal roll mislabelled; it is FOREARM PRONATION
        /// mislabelled, and no split of it into forearm and wrist can be recovered from this data.
        /// Corroborating: that channel takes single-frame steps of up to 171.1 deg (25 of 265,938 frames)
        /// while the pronation channel takes ZERO steps over 45 deg, which is a branch artefact and not a
        /// wrist.
        ///
        /// ⭐ SO WHAT THIS CORPUS *CAN* REFEREE, AND DOES BELOW: THE TOTAL. Whatever the split, the sum --
        /// hand vs humerus about the forearm axis -- is real motion, and the solver has to be able to
        /// deliver it. The solver delivers it as forearm pronation (the 80 deg comfort band) plus whatever
        /// the wrist is allowed (15), so the veto is: per flexion band, the 97th percentile of the total
        /// axial roll real people use must fit inside 95 deg. The 97th percentile is this house's 3%
        /// firing precedent, applied per band because pooling has hidden a band-specific breach twice.
        /// </summary>
        [Test]
        public void TheCorpusVeto_IsSegmentedByElbowFlexion_AndDeclaresWhatItCannotJudge()
        {
            var wristSplit = new List<float>();
            var pronation = new List<float>();
            var totals = new List<float>[6];
            for (int k = 0; k < 6; k++) totals[k] = new List<float>();
            var buf = BasisBodyFrame.Allocate();
            int clips = 0;

            for (int t = 0; t < k_TierDirs.Length; t++)
            {
                string dir = t == 0 ? CorpusRoot : Path.Combine(CorpusRoot, k_TierDirs[t]);
                if (!Directory.Exists(dir)) continue;
                string[] files = Directory.GetFiles(dir, "*.bvh");
                Array.Sort(files);
                foreach (string f in files)
                {
                    if (!BasisBvhLoader.TryLoad(f, out BasisMotionClip c, out _)) continue;
                    clips++;
                    for (int fi = 0; fi < c.FrameCount; fi++)
                    {
                        BasisBodyFrame frame = BasisBodyFrame.FromClip(c, fi, buf);
                        for (int side = 0; side < 2; side++)
                        {
                            bool right = side == 1;
                            BasisMocapJoint sj = right ? BasisMocapJoint.RightUpperArm : BasisMocapJoint.LeftUpperArm;
                            BasisMocapJoint ej = right ? BasisMocapJoint.RightLowerArm : BasisMocapJoint.LeftLowerArm;
                            BasisMocapJoint hj = right ? BasisMocapJoint.RightHand : BasisMocapJoint.LeftHand;
                            if (!frame.Has(sj) || !frame.Has(ej) || !frame.Has(hj)) continue;

                            Quaternion hum = frame.Rot(sj), fore = frame.Rot(ej), hand = frame.Rot(hj);
                            if (!(Quaternion.Dot(hum, hum) > 0.5f && Quaternion.Dot(fore, fore) > 0.5f
                                  && Quaternion.Dot(hand, hand) > 0.5f)) continue;

                            Vector3 fv = frame.Pos(hj) - frame.Pos(ej);
                            Vector3 hv = frame.Pos(ej) - frame.Pos(sj);
                            if (!(fv.sqrMagnitude > 1e-8f) || !(hv.sqrMagnitude > 1e-8f)) continue;
                            Vector3 fa = fv.normalized;

                            wristSplit.Add(Mathf.Abs(BasisArmNet.TwistDeg(hand * Quaternion.Inverse(fore), fa)));
                            pronation.Add(Mathf.Abs(BasisArmNet.TwistDeg(fore * Quaternion.Inverse(hum), fa)));

                            float flex = 180f - Vector3.Angle(-hv, fv);
                            int b = Mathf.Clamp(Mathf.FloorToInt(flex / k_BandDeg), 0, 5);
                            totals[b].Add(Mathf.Abs(BasisArmNet.TwistDeg(hand * Quaternion.Inverse(hum), fa)));
                        }
                    }
                }
            }

            if (wristSplit.Count == 0) Assert.Ignore($"no corpus at {CorpusRoot}");
            wristSplit.Sort(); pronation.Sort();

            // The solver's in-band axial capacity: the forearm's comfort band plus what the wrist may keep.
            const float k_Capacity = BasisArmSolveCore.WristRollComfortDeg + BasisArmSolveCore.WristAxialHardDeg;

            var sb = new StringBuilder();
            sb.AppendLine($"{clips} clips, {wristSplit.Count} arm-frames.");
            sb.AppendLine();
            sb.AppendLine($"  WHY THE SPLIT CANNOT BE REFEREED HERE:");
            sb.AppendLine($"    |hand vs FOREARM| axial   p50 {P(wristSplit,.5f),6:F1}  p97 {P(wristSplit,.97f),6:F1}  max {P(wristSplit,1f),6:F1}");
            sb.AppendLine($"    |forearm vs HUMERUS| ax.  p50 {P(pronation,.5f),6:F1}  p97 {P(pronation,.97f),6:F1}  max {P(pronation,1f),6:F1}   <- the forearm carries NONE of it");
            sb.AppendLine();
            sb.AppendLine($"  WHAT IT CAN REFEREE -- the TOTAL, per elbow-flexion band, against the solver's {k_Capacity:F0} deg capacity:");
            sb.AppendLine($"    flexion band        n        p50      p90      p97      p99      max   VERDICT");

            var judged = new List<int>();
            for (int b = 0; b < 6; b++)
            {
                string band = $"[{b * k_BandDeg},{(b + 1) * k_BandDeg})";
                if (totals[b].Count < 2000)
                {
                    sb.AppendLine($"    {band,-12}{totals[b].Count,9}   -- too few frames: NOT JUDGED");
                    continue;
                }
                totals[b].Sort();
                judged.Add(b);
                float p97 = P(totals[b], .97f);
                sb.AppendLine($"    {band,-12}{totals[b].Count,9}{P(totals[b],.5f),9:F1}{P(totals[b],.9f),9:F1}{p97,9:F1}" +
                              $"{P(totals[b],.99f),9:F1}{P(totals[b],1f),9:F1}   {(p97 < k_Capacity ? "clears" : "BREACH")}");
            }

            TestContext.WriteLine("\n  SEGMENTED CORPUS VETO FOR THE WRIST BOUND\n\n" + sb);

            Assert.That(wristSplit.Count, Is.GreaterThan(100000),
                $"only {wristSplit.Count} arm-frames, so this is not measuring the corpus it claims to.");

            // ── THE BLIND SPOT, ASSERTED SO IT CANNOT BE FORGOTTEN. This is the finding, not a caveat.
            Assert.That(P(pronation, .999f), Is.LessThan(1f),
                $"the corpus's forearm now carries {P(pronation,.999f):F1} deg of pronation at p99.9, where it carried " +
                "0.0. This file's central claim -- that the corpus cannot referee the hand/forearm SPLIT because the " +
                "retarget puts all pronation in the hand joint -- rests on that being ~0. If the corpus, the loader or " +
                "the skeleton has changed, the split may now be judgeable and this veto should be rewritten to use it.");

            Assert.That(judged.Count, Is.GreaterThanOrEqualTo(4),
                $"only {judged.Count} flexion bands carry enough frames to judge; a segmented veto that judges one or " +
                "two bands is a pooled veto wearing a table.");

            foreach (int b in judged)
            {
                float p97 = P(totals[b], .97f);
                // ⭐ NON-VACUITY: the band must actually exercise more than the wrist bound alone, or "the total
                // fits" would be proving nothing about whether the forearm is doing the work.
                Assert.That(p97, Is.GreaterThan(Envelope),
                    $"elbow-flexion band [{b * k_BandDeg},{(b + 1) * k_BandDeg}) only reaches {p97:F1} deg of total axial roll " +
                    $"at p97, which is inside the wrist bound on its own. This band cannot referee whether the FOREARM " +
                    "has to carry the roll, so the veto below is vacuous there.\n" + sb);

                Assert.That(p97, Is.LessThan(k_Capacity),
                    $"REAL MOTION IS BEING CLIPPED. In elbow-flexion band [{b * k_BandDeg},{(b + 1) * k_BandDeg}) the 97th " +
                    $"percentile of total clavicle-to-hand axial roll is {p97:F1} deg, against the {k_Capacity:F0} deg the " +
                    $"solver can deliver in band ({BasisArmSolveCore.WristRollComfortDeg:F0} of forearm pronation plus " +
                    $"{Envelope:F0} of wrist). Past the house 3% firing precedent, so the wrist bound is not the thing to " +
                    "relax -- the forearm's comfort band is what is short.\n" + sb);
            }
        }

        // ------------------------------------------------------------------ helpers

        static void Worst(List<Row> rows, bool tracker, out float worst, out Row at)
        {
            worst = 0f; at = default;
            foreach (Row r in rows)
            {
                if (r.Tracker != tracker) continue;
                float v = Mathf.Abs(r.WristPost);
                if (v > worst) { worst = v; at = r; }
            }
        }

        static float P(List<float> sorted, float f)
        {
            if (sorted.Count == 0) return 0f;
            return sorted[Mathf.Clamp(Mathf.RoundToInt(f * (sorted.Count - 1)), 0, sorted.Count - 1)];
        }
    }
}
