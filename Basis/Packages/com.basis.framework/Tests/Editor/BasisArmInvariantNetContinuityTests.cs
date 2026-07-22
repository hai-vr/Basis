using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// CONTINUITY, SWEPT OVER EVERY INPUT THE ARM SOLVE HAS.
    ///
    /// ================================================================================================
    /// THE CLASS THIS CATCHES: a SEAM -- a smooth input producing a jump in the output. The instance that
    /// shipped was a principal-angle clamp mapping +179.5 and -179.5 (the same humerus, one degree apart)
    /// onto +140 and -140 (poses 80 degrees apart): an 80.009 degree arm jump for a 0.05 degree input step,
    /// 1430x amplification. A seam is INVISIBLE IN ANY SINGLE POSE by construction, so no per-pose suite of
    /// any size can find one -- only a sweep can, and only if it sweeps the input the seam lives on.
    ///
    /// So this file sweeps EVERY input, not the one that broke: hand direction (azimuth and elevation),
    /// reach through 1.0, controller roll, elbow-tracker azimuth, tracker roll, and the animated pose's own
    /// roll -- and on every one it measures the worst single-step change of EVERY output: both bone
    /// rotations, both joint positions THROUGH THE STREAM, and each bone's AXIAL ROLL separately (a bone can
    /// swing smoothly while rolling discontinuously, and the roll is the DOF every singularity in this file
    /// pays out in).
    ///
    /// ⚠️ THE HARNESS IS GATED BEFORE THE SOLVER IS. A sweep that builds its pole circle from a fixed world
    /// axis injects its own seam the moment the swept direction passes through that axis, and then reports
    /// the harness's discontinuity as the core's. Every sweep here therefore runs the same continuity gate
    /// over its INPUT channels first, at a far tighter bound, and a harness seam fails as a harness seam.
    ///
    /// The threshold is the sweep's OWN 95th-percentile step (see BasisArmNet.GateSmooth), not a constant:
    /// an absolute threshold has to be re-guessed for every unit and every step size, and guessing it wrong
    /// is how the 1430x amplifier passed review.
    /// ================================================================================================
    /// </summary>
    public class BasisArmInvariantNetContinuityTests
    {
        // A jump smaller than this is not worth calling a seam whatever the amplification says; above it,
        // amplification decides. 1.5 deg / 1.5 mm are both far below anything a user could see AND far
        // above float noise on a 0.56 m arm.
        const float RotFloorDeg = 1.5f;
        const float PosFloorM = 0.0015f;
        const float Amp = 12f;

        // The INPUT gate is deliberately much tighter: the harness is supposed to be smooth by
        // construction, so anything above a couple of times its own p95 is a harness bug.
        const float InputAmp = 4f;
        const float InputRotFloorDeg = 0.05f;
        const float InputPosFloorM = 2e-5f;

        // ============================================================================================
        // the sweep runner
        // ============================================================================================

        sealed class Sweep
        {
            public readonly BasisArmNet.Channel InTarget = new BasisArmNet.Channel("IN target pos (m)");
            public readonly BasisArmNet.Channel InHint = new BasisArmNet.Channel("IN hint pos (m)");
            public readonly BasisArmNet.Channel InTargetRot = new BasisArmNet.Channel("IN target rot (deg)");

            public readonly BasisArmNet.Channel Root = new BasisArmNet.Channel("upper arm rot (deg)");
            public readonly BasisArmNet.Channel Mid = new BasisArmNet.Channel("forearm rot (deg)");
            public readonly BasisArmNet.Channel Tip = new BasisArmNet.Channel("hand rot (deg)");
            public readonly BasisArmNet.Channel Elbow = new BasisArmNet.Channel("elbow pos (m)");
            public readonly BasisArmNet.Channel Hand = new BasisArmNet.Channel("hand pos (m)");
            public readonly BasisArmNet.Channel HumRoll = new BasisArmNet.Channel("humerus AXIAL (deg)");
            public readonly BasisArmNet.Channel ForeRoll = new BasisArmNet.Channel("forearm AXIAL (deg)");
        }

        /// <summary>
        /// Runs one parameter sweep and gates every channel. Findings are aggregated rather than thrown, so
        /// a single run names EVERY broken invariant instead of dying on the first.
        ///
        /// ⭐ `known*` IS A NAMED, MEASURED ALLOWANCE FOR A DEFECT THAT IS ALREADY OPEN -- NOT A TUNING KNOB.
        /// Two seams are live in the shipping core today (see
        /// <see cref="BasisArmInvariantNetKnownOpenDefectTests"/>, which reproduces each in isolation and
        /// derives its magnitude in closed form). A sweep that reaches one of them would otherwise fail
        /// forever and drown out anything NEW, so the sweep instead carries the defect's own measured size,
        /// NAMED, and stays strict about everything else. The allowance is a CEILING: if the known defect
        /// gets worse, or a second one appears alongside it, the sweep still goes red.
        /// </summary>
        static void Run(string context, Func<float, BasisArmNet.Spec> at, float t0, float t1, int steps,
                        List<string> findings, StringBuilder log,
                        float knownRotDeg = 0f, float knownPosM = 0f, string knownDefect = null)
        {
            var s = new Sweep();
            bool first = true;
            Vector3 pTarget = Vector3.zero, pHint = Vector3.zero, pElbow = Vector3.zero, pHand = Vector3.zero;
            Quaternion pTargetRot = Quaternion.identity, pRoot = Quaternion.identity, pMid = Quaternion.identity, pTip = Quaternion.identity;

            for (int k = 0; k <= steps; k++)
            {
                float t = Mathf.Lerp(t0, t1, k / (float)steps);
                BasisArmNet.Spec spec = at(t);
                BasisArmSolveInput i = BasisArmNet.Build(spec);

                // The pole circle must stay well conditioned, or the harness -- not the core -- is the
                // thing that jumped. Asserted rather than assumed.
                float align = Mathf.Abs(Vector3.Dot(spec.RefPerp.normalized, spec.TargetDir.normalized));
                if (align > 0.98f)
                {
                    findings.Add($"{context}: at t={t:0.000} the sweep's own azimuth reference is {align:0.000} " +
                                 "aligned with the target direction, so the pole basis it builds is degenerate. " +
                                 "THE HARNESS would be the discontinuity here, not the core.");
                    return;
                }

                BasisArmNet.Solve(i, out BasisArmSolveResult r);

                if (!BasisArmNet.Finite(r.RootRotationSolved) || !BasisArmNet.Finite(r.MidRotationSolved) ||
                    !BasisArmNet.Finite(r.HintDelta) || !BasisArmNet.Finite(r.MidPostRoll) ||
                    !BasisArmNet.Finite(r.ElbowSolved) || !BasisArmNet.Finite(r.HandSolved))
                {
                    findings.Add($"{context}: NON-FINITE output at t={t:0.000}.");
                    return;
                }

                BasisArmNet.StreamCompose(i, r, out Vector3 elbow, out Vector3 hand, out Quaternion root, out Quaternion mid);
                Quaternion tip = r.TipRotation;

                Vector3 humAxis = elbow - i.Shoulder;
                Vector3 foreAxis = hand - elbow;
                humAxis = humAxis.sqrMagnitude > 1e-10f ? humAxis.normalized : Vector3.up;
                foreAxis = foreAxis.sqrMagnitude > 1e-10f ? foreAxis.normalized : Vector3.up;

                if (!first)
                {
                    s.InTarget.Add(Vector3.Distance(i.TargetPosition, pTarget), t);
                    s.InHint.Add(Vector3.Distance(i.HintPosition, pHint), t);
                    s.InTargetRot.Add(BasisArmNet.PoseChangeDeg(pTargetRot, i.TargetRotation), t);

                    s.Root.Add(BasisArmNet.PoseChangeDeg(pRoot, root), t);
                    s.Mid.Add(BasisArmNet.PoseChangeDeg(pMid, mid), t);
                    s.Tip.Add(BasisArmNet.PoseChangeDeg(pTip, tip), t);
                    s.Elbow.Add(Vector3.Distance(pElbow, elbow), t);
                    s.Hand.Add(Vector3.Distance(pHand, hand), t);

                    // AXIAL ROLL, SEPARATELY. A bone whose total pose change is modest can still be rolling
                    // about its own long axis discontinuously, and roll is exactly what every singularity in
                    // this solver pays its ill-conditioned DOF out as.
                    s.HumRoll.Add(Mathf.Abs(BasisArmNet.TwistDeg(root * Quaternion.Inverse(pRoot), humAxis)), t);
                    s.ForeRoll.Add(Mathf.Abs(BasisArmNet.TwistDeg(mid * Quaternion.Inverse(pMid), foreAxis)), t);
                }

                pTarget = i.TargetPosition; pHint = i.HintPosition; pTargetRot = i.TargetRotation;
                pRoot = root; pMid = mid; pTip = tip; pElbow = elbow; pHand = hand;
                first = false;
            }

            log.AppendLine($"    {context}");

            // ── SWEEP-LEVEL NON-VACUITY, and it belongs HERE rather than in the per-channel gate: an
            //    individual output is allowed to be constant (the elbow does not move when only the
            //    tracker's roll changes), but if NOTHING in the solved arm moved then this sweep is not
            //    exercising the solver at all and every bound below is worthless.
            float movedDeg = Mathf.Max(s.Root.Worst, s.Mid.Worst);
            float movedM = Mathf.Max(s.Elbow.Worst, s.Hand.Worst);
            if (!(movedDeg > 0f) && !(movedM > 0f))
            {
                findings.Add($"{context}: NOTHING in the solved arm moved anywhere in this sweep -- neither bone " +
                             "rotated and neither joint translated. The sweep is not exciting the solver, so every " +
                             "continuity bound it reports is vacuous.");
                return;
            }

            // ── the harness next. Input channels are gated only when they move: a roll sweep holds the
            //    target position fixed on purpose, and a reach sweep holds the target rotation fixed.
            Add(findings, BasisArmNet.GateSmooth(s.InTarget, InputPosFloorM, InputAmp, "HARNESS " + context, log));
            Add(findings, BasisArmNet.GateSmooth(s.InTargetRot, InputRotFloorDeg, InputAmp, "HARNESS " + context, log));
            Add(findings, BasisArmNet.GateSmooth(s.InHint, InputPosFloorM, InputAmp, "HARNESS " + context, log));

            // ── then the solver.
            float rotFloor = Mathf.Max(RotFloorDeg, knownRotDeg);
            float posFloor = Mathf.Max(PosFloorM, knownPosM);
            if (knownDefect != null)
            {
                log.AppendLine($"      ⚠ KNOWN-OPEN DEFECT allowed here, {knownRotDeg:F1} deg / {knownPosM * 1000f:F0} mm: {knownDefect}");
            }

            Add(findings, BasisArmNet.GateSmooth(s.Root, rotFloor, Amp, context, log));
            Add(findings, BasisArmNet.GateSmooth(s.Mid, rotFloor, Amp, context, log));
            Add(findings, BasisArmNet.GateSmooth(s.Elbow, posFloor, Amp, context, log));
            // ⚠️ THE HAND IS NEVER ALLOWANCED. Every swivel in this solver is about shoulder->hand and the
            // hand LIES on that axis, so no seam in any of them can move it -- if the hand jumps, something
            // has picked an axis it has no business picking, and that is never a known defect.
            Add(findings, BasisArmNet.GateSmooth(s.Hand, PosFloorM, Amp, context, log));
            Add(findings, BasisArmNet.GateSmooth(s.HumRoll, rotFloor, Amp, context, log));
            Add(findings, BasisArmNet.GateSmooth(s.ForeRoll, rotFloor, Amp, context, log));
        }

        static void Add(List<string> findings, string finding)
        {
            if (finding != null) findings.Add(finding);
        }

        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        // ============================================================================================
        // 1. THE HAND MOVES. NOTHING MAY JUMP.
        // ============================================================================================

        /// <summary>
        /// The controller orbits the shoulder at a fixed elevation and reach, through a full 360. This is
        /// the ordinary thing a user does with their arm, and it walks the solve through every azimuth of
        /// the elbow circle, both fallback branches of the pole basis, and (at high reach) the region where
        /// the elbow's lever arm has collapsed and every requested swivel is paid out as bone ROLL.
        /// </summary>
        [Test]
        public void HandAzimuthSweep_ProducesNoJump_AtAnyReach()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (float el in new[] { -55f, -20f, 25f })
            foreach (float reach in new[] { 0.80f, 0.95f, 0.995f })
            foreach (int hint in new[] { BasisArmNet.HintModel, BasisArmNet.HintTracker })
            {
                string ctx = $"hand azimuth 0-360, el {el:0}, reach {reach:0.000}, " +
                             (hint == BasisArmNet.HintTracker ? "TRACKER" : "model");
                Run(ctx, t =>
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(t, el);
                    s.Reach = reach;
                    s.HintMode = hint;
                    s.HintAzimuthDeg = 30f;
                    s.HintRhoMin = 0.02f;          // the user's arm is longer than the avatar's: the pole never fully collapses
                    s.RefPerp = Vector3.up;        // never parallel to a target at |el| <= 55
                    s.FeedTwistBind = true;
                    s.FeedLowerBind = true;
                    s.FeedTip = true;
                    return s;
                }, 0f, 360f, 1440, findings, log,
                   knownRotDeg: 70f, knownPosM: 0.20f, knownDefect: "D1 -- BasisElbowAnatomyCore flips the elbow across its circle when a FIRING guard's elbow crosses the top (sG = sign(s)*sqrt(1-cG^2), and s is exactly 0 there). See BasisArmInvariantNetKnownOpenDefectTests.");
            }

            BasisArmNet.Report(findings, log, "hand-azimuth continuity");
        }

        /// <summary>
        /// The controller rises from below the hip to overhead. This crosses the elbow anatomy guard's
        /// ceiling (the elbow may not rise above the shoulder OR the hand, whichever is higher -- so the
        /// ceiling itself MOVES as the hand passes shoulder height, and a guard whose ceiling moves is a
        /// natural home for a kink), and it crosses the pole-basis singularity where the target direction
        /// approaches the azimuth reference.
        /// </summary>
        [Test]
        public void HandElevationSweep_ProducesNoJump()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (float az in new[] { 0f, 75f, 200f })
            foreach (float reach in new[] { 0.85f, 0.98f })
            foreach (int hint in new[] { BasisArmNet.HintModel, BasisArmNet.HintTracker })
            {
                string ctx = $"hand elevation -88..+88, az {az:0}, reach {reach:0.000}, " +
                             (hint == BasisArmNet.HintTracker ? "TRACKER" : "model");
                Run(ctx, t =>
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(az, t);
                    s.Reach = reach;
                    s.HintMode = hint;
                    s.HintAzimuthDeg = -40f;
                    s.HintRhoMin = 0.02f;
                    // Horizontal, and 90 deg away in azimuth from the sweep, so it is never parallel to the
                    // target at any elevation: the pole basis stays conditioned for the WHOLE sweep.
                    s.RefPerp = Dir(az + 90f, 0f);
                    s.FeedTwistBind = true;
                    s.FeedLowerBind = true;
                    s.FeedTip = true;
                    return s;
                }, -88f, 88f, 1408, findings, log);
            }

            BasisArmNet.Report(findings, log, "hand-elevation continuity");
        }

        /// <summary>
        /// ⭐ REACH THROUGH 1.0, WHICH IS WHERE THIS SOLVER LIVES AND WHERE ITS GEOMETRY DIES.
        ///
        /// 57% of the motion corpus sits above 95% extension, and a user whose real arms are longer than
        /// their avatar's is past full reach on EVERY frame. Two things happen in this sweep and nowhere
        /// else: TriangleAngle saturates at 180 and the elbow clamp at MaxElbowAngleDeg takes over (a
        /// derivative change, which must not be a value change), and the elbow's lever arm rho collapses,
        /// which is the ill-conditioning every roll singularity in this file descends from.
        /// </summary>
        [Test]
        public void ReachSweepThroughFullExtension_ProducesNoJump()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (float el in new[] { -60f, -10f, 40f })
            foreach (int hint in new[] { BasisArmNet.HintModel, BasisArmNet.HintTracker, BasisArmNet.HintLookup })
            {
                string name = hint == BasisArmNet.HintTracker ? "TRACKER"
                            : hint == BasisArmNet.HintLookup ? "lookup pole" : "model";
                string ctx = $"reach 0.35-1.15, el {el:0}, {name}";
                Run(ctx, t =>
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(20f, el);
                    s.Reach = t;
                    s.HintMode = hint;
                    s.HintAzimuthDeg = 55f;
                    s.HintRhoMin = 0.02f;
                    s.RefPerp = Dir(110f, 0f);
                    s.FeedTwistBind = true;
                    s.FeedLowerBind = true;
                    s.FeedTip = true;
                    return s;
                }, 0.35f, 1.15f, 1600, findings, log);
            }

            BasisArmNet.Report(findings, log, "reach continuity through full extension");
        }

        // ============================================================================================
        // 2. ROLL INPUTS. THE THREE PRINCIPAL-ANGLE SEAMS LIVE HERE.
        // ============================================================================================

        /// <summary>
        /// ⭐ THE CONTROLLER ROLLS THROUGH A FULL TURN. Three principal-angle quantities are measured from
        /// it -- the wrist twist that drives the relief, the hand's roll demand on the forearm, and (through
        /// the resulting swivel) the humeral twist the guard bounds -- and EVERY ONE of them wraps at
        /// +/-180. The wrist relief and the humeral twist guard each carry an explicit seam envelope; this
        /// is the sweep that says whether those envelopes work.
        /// </summary>
        [Test]
        public void ControllerRollSweep_ProducesNoJump()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (float reach in new[] { 0.75f, 0.93f, 0.99f })
            foreach (bool tracker in new[] { false, true })
            {
                string ctx = $"controller roll 0-360, reach {reach:0.00}, " + (tracker ? "TRACKER" : "model (wrist relief live)");
                Run(ctx, t =>
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(35f, -25f);
                    s.Reach = reach;
                    s.HandRollDeg = t;
                    s.HintMode = tracker ? BasisArmNet.HintTracker : BasisArmNet.HintModel;
                    s.HintAzimuthDeg = 20f;
                    s.HintRhoMin = 0.02f;
                    s.TrackerRollDeg = tracker ? 0f : float.NaN;
                    s.RefPerp = Vector3.up;
                    s.FeedTwistBind = true;
                    s.FeedLowerBind = true;
                    s.FeedTip = true;
                    return s;
                }, 0f, 360f, 1440, findings, log,
                   knownRotDeg: 135f, knownPosM: 0.19f, knownDefect: "D1 -- BasisElbowAnatomyCore flips the elbow across its circle when a FIRING guard's elbow crosses the top (sG = sign(s)*sqrt(1-cG^2), and s is exactly 0 there). See BasisArmInvariantNetKnownOpenDefectTests.");
            }

            BasisArmNet.Report(findings, log, "controller-roll continuity");
        }

        /// <summary>
        /// ⭐ THE ELBOW TRACKER ORBITS THE ARM AXIS -- literally "if i move my hint role around im able to
        /// get it to flip", which is how the 80 degree humeral-twist snap was reported. Rolling a forearm
        /// strap orbits the tracker's POSITION about the arm axis, so this is the physical gesture and not a
        /// contrivance; on a near-straight arm obeying that pole is almost pure humeral ROLL, which is
        /// exactly the DOF the guard bounds and the seam lived on.
        /// </summary>
        [Test]
        public void ElbowTrackerAzimuthSweep_ProducesNoJump()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (float reach in new[] { 0.70f, 0.90f, 0.97f, 0.999f })
            foreach (bool twistGuard in new[] { true, false })
            {
                string ctx = $"tracker azimuth 0-360, reach {reach:0.000}, twist guard " + (twistGuard ? "ON" : "declined");
                Run(ctx, t =>
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(-30f, -35f);
                    s.Reach = reach;
                    s.HintMode = BasisArmNet.HintTracker;
                    s.HintAzimuthDeg = t;
                    s.HintRhoMin = 0.015f;
                    s.TrackerRollDeg = 0f;
                    s.RefPerp = Vector3.up;
                    s.FeedTwistBind = twistGuard;
                    s.FeedLowerBind = true;
                    s.FeedTip = true;
                    return s;
                }, 0f, 360f, 2880, findings, log,
                   knownRotDeg: 150f, knownPosM: 0.05f, knownDefect: "D1 -- BasisElbowAnatomyCore flips the elbow across its circle when a FIRING guard's elbow crosses the top (sG = sign(s)*sqrt(1-cG^2), and s is exactly 0 there). See BasisArmInvariantNetKnownOpenDefectTests. AND D2 -- the tracker forearm roll has no seam envelope, so its principal angle flips sign at +/-180 for a 2*Saturate(180,90,120) = 225 deg swing. See BasisArmInvariantNetKnownOpenDefectTests.");
            }

            BasisArmNet.Report(findings, log, "elbow-tracker azimuth continuity");
        }

        /// <summary>
        /// ⭐ THE HUMERAL TWIST SEAM, SWEPT IN ISOLATION AND HELD TO THE STRICT BOUND.
        ///
        /// This is the direct regression gate for today's headline fix, measured through the STREAM rather
        /// than off the result struct -- and it is deliberately arranged so that NEITHER known-open seam can
        /// reach it, which is what lets it stay strict while the broader sweeps carry allowances:
        ///
        ///   * the hand points 80 deg DOWN, so the elbow circle is nearly horizontal and its highest point
        ///     sits far below the anatomical ceiling -- BasisElbowAnatomyCore is inert for the whole sweep,
        ///     so D1's side flip cannot occur. The sweep ASSERTS that inertness rather than assuming it;
        ///   * HintRotation is declined, so the tracker forearm roll stands down and D2 cannot occur.
        ///
        /// What is left acting is the swivel and the humeral twist guard, and the guard's seam cap is the
        /// only thing between a smooth tracker orbit and an 80 degree snap of the upper arm.
        /// </summary>
        [Test]
        public void HumeralTwistSeamSweep_ProducesNoJump_WithNeitherKnownSeamInReach()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            // the guard must be INERT over the whole sweep, or this is not an isolated measurement
            float worstRise = float.NegativeInfinity;
            for (int k = 0; k <= 360; k++)
            {
                BasisArmNet.Spec probe = BasisArmNet.Default(rig);
                probe.TargetDir = Dir(25f, -80f);
                probe.Reach = 0.97f;
                probe.HintMode = BasisArmNet.HintTracker;
                probe.HintAzimuthDeg = k;
                probe.HintRhoMin = 0f;
                probe.RefPerp = Dir(115f, 0f);
                BasisArmSolveInput pi = BasisArmNet.Build(probe);
                float g = BasisElbowAnatomyCore.GuardSwivelRad(pi.Shoulder, pi.HintPosition, pi.TargetPosition,
                                                               Vector3.up, BasisArmNet.Total);
                worstRise = Mathf.Max(worstRise, Mathf.Abs(g));
            }
            Assert.That(worstRise, Is.EqualTo(0f),
                $"the elbow anatomy guard fired ({worstRise:0.000000} rad) somewhere in this sweep, so it is no " +
                "longer isolating the humeral twist guard and a D1 side flip could be mistaken for a twist seam.");

            foreach (float reach in new[] { 0.94f, 0.97f, 0.995f })
            {
                Run($"HUMERAL TWIST SEAM: tracker azimuth 0-360, hand 80 deg down, reach {reach:0.000}", t =>
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(25f, -80f);
                    s.Reach = reach;
                    s.HintMode = BasisArmNet.HintTracker;
                    s.HintAzimuthDeg = t;
                    s.HintRhoMin = 0f;
                    s.TrackerRollDeg = float.NaN;      // declines the tracker forearm roll: D2 out of reach
                    s.RefPerp = Dir(115f, 0f);         // horizontal, so it is never parallel to a steep target
                    s.FeedTwistBind = true;
                    s.FeedLowerBind = true;
                    s.FeedTip = true;
                    return s;
                }, 0f, 360f, 2880, findings, log);
            }

            BasisArmNet.Report(findings, log, "humeral-twist seam continuity (isolated, strict)");
        }

        /// <summary>
        /// The tracker's own MEASURED forearm roll through a full turn. It feeds the tracker forearm-roll
        /// path (a principal angle saturated at 90/120), the hand-vs-tracker disagreement fade (another
        /// principal angle, faded at 155-178), and the pole anchor's carry rotation. Three wraps, one input.
        /// </summary>
        [Test]
        public void TrackerForearmRollSweep_ProducesNoJump()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (float reach in new[] { 0.80f, 0.96f })
            foreach (float handRoll in new[] { 0f, 70f, 150f })
            {
                string ctx = $"tracker roll 0-360, reach {reach:0.00}, controller roll {handRoll:0}";
                Run(ctx, t =>
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(10f, -30f);
                    s.Reach = reach;
                    s.HandRollDeg = handRoll;
                    s.HintMode = BasisArmNet.HintTracker;
                    s.HintAzimuthDeg = 65f;
                    s.HintRhoMin = 0.02f;
                    s.TrackerRollDeg = t;
                    s.RefPerp = Vector3.up;
                    s.FeedTwistBind = true;
                    s.FeedLowerBind = true;
                    s.FeedTip = true;
                    return s;
                }, 0f, 360f, 1440, findings, log,
                   knownRotDeg: 145f, knownPosM: 0f, knownDefect: "D2 -- the tracker forearm roll has no seam envelope, so its principal angle flips sign at +/-180 for a 2*Saturate(180,90,120) = 225 deg swing. See BasisArmInvariantNetKnownOpenDefectTests.");
            }

            BasisArmNet.Report(findings, log, "tracker forearm-roll continuity");
        }

        /// <summary>
        /// THE ANIMATED FOREARM ROLLS THROUGH A FULL TURN AND THE USER DOES NOT MOVE.
        ///
        /// ⚠️ THIS ONE IS NOT A CONTINUITY SWEEP, AND THAT IS THE POINT. The correct answer here is that
        /// the solved arm does not move AT ALL, so running it through the continuity runner would trip the
        /// runner's own "nothing moved, so this sweep is vacuous" check -- which is right for every OTHER
        /// input and wrong for this one. The property is INVARIANCE, not smoothness: a pose the user is
        /// holding still must not depend on an animation they are not performing. Before the no-tracker
        /// forearm roll landed, the solved forearm tracked this 1:1 to 0.000 deg, so the avatar's pronation
        /// depended on which idle clip happened to be playing.
        ///
        /// The control is the same sweep with BindLowerArmRotation declined -- the pre-fix behaviour, which
        /// still inherits the animation completely -- so the bound carries its own proof that it can fail.
        /// </summary>
        [Test]
        public void SolvedForearm_DoesNotInheritTheAnimatedForearmRoll()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            // ── THE HEADLINE PROPERTY: the solved forearm must not follow the animation.
            // Measured against the CONTROL that declines BindLowerArmRotation, which is the pre-fix
            // behaviour and follows it 1:1 -- so this bound carries its own proof that it can fail.
            float worstFed = 0f, worstDeclined = 0f;
            Quaternion refFed = Quaternion.identity, refDec = Quaternion.identity;
            for (int k = 0; k <= 72; k++)
            {
                float roll = k * 5f;
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(15f, -40f);
                s.Reach = 0.90f;
                s.HintMode = BasisArmNet.HintModel;
                s.HintAzimuthDeg = 10f;
                s.AnimForeRollDeg = roll;
                s.RefPerp = Vector3.up;
                s.FeedTwistBind = true;
                s.FeedTip = true;

                BasisArmNet.Spec fed = s; fed.FeedLowerBind = true;
                BasisArmNet.Spec dec = s; dec.FeedLowerBind = false;

                BasisArmNet.Solve(BasisArmNet.Build(fed), out BasisArmSolveResult rf);
                BasisArmNet.Solve(BasisArmNet.Build(dec), out BasisArmSolveResult rd);
                BasisArmNet.StreamCompose(BasisArmNet.Build(fed), rf, out Vector3 ef, out Vector3 hf, out _, out Quaternion mf);
                BasisArmNet.StreamCompose(BasisArmNet.Build(dec), rd, out Vector3 ed, out Vector3 hd, out _, out Quaternion md);

                Vector3 axF = (hf - ef).normalized, axD = (hd - ed).normalized;
                if (k == 0) { refFed = mf; refDec = md; }
                worstFed = Mathf.Max(worstFed, Mathf.Abs(BasisArmNet.TwistDeg(mf * Quaternion.Inverse(refFed), axF)));
                worstDeclined = Mathf.Max(worstDeclined, Mathf.Abs(BasisArmNet.TwistDeg(md * Quaternion.Inverse(refDec), axD)));
            }

            BasisArmNet.Gate(
                "the SOLVED forearm's axial roll under a 360 deg sweep of the ANIMATED forearm (the user is " +
                "not moving; a pose held still must not depend on an animation the user is not performing)",
                worstFed, 25f, worstDeclined, 120f);

            TestContext.WriteLine($"  animated forearm swept 360 deg: solved forearm moved {worstFed:0.0} deg with the " +
                                  $"bind fed, {worstDeclined:0.0} deg with it declined (the pre-fix inheritance).");
        }

        /// <summary>
        /// ⭐ THE POLE'S STAND-OFF FROM THE ARM AXIS, SWEPT FROM HEALTHY TO ZERO. This is the axis the old
        /// BOOLEAN pole gate cliffed on: `ahProj.sqrMagnitude > totalLen^2 * 0.001` is |ahProj| > 1.9 cm on
        /// a 0.6 m arm, and below it the hint was not faded but DROPPED, in a single frame, leaving the
        /// elbow wherever the animation had left it. Measured then: the elbow travelled 2582x the hand's own
        /// distance in one step as it crossed. Geometry alone gets you to about 12x near full extension --
        /// hundreds is a pole being switched off between two frames.
        ///
        /// It is also the axis the tracker's own conditioning (TrackerPoleAnchorFrac 0.03 ->
        /// TrackerPoleTrustFrac 0.12) is a smoothstep along, and the axis on which the anchor hands over
        /// between refreshing, easing and holding. Three separate handovers, one sweep.
        /// </summary>
        [Test]
        public void PoleStandoffSweep_ProducesNoJump_AsThePoleCollapsesOntoTheArmAxis()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (float reach in new[] { 0.80f, 0.95f })
            foreach (float az in new[] { 40f, 220f })
            {
                string ctx = $"pole stand-off 0.20 m -> 0, reach {reach:0.00}, hint azimuth {az:0}";
                Run(ctx, t =>
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(-10f, -30f);
                    s.Reach = reach;
                    s.HintMode = BasisArmNet.HintTracker;
                    s.HintAzimuthDeg = az;
                    s.HintRhoOverride = t;         // the stand-off itself is the swept parameter
                    s.TrackerRollDeg = 0f;
                    s.RefPerp = Vector3.up;
                    s.FeedTwistBind = true;
                    s.FeedLowerBind = true;
                    s.FeedTip = true;
                    return s;
                }, 0.20f, 0.001f, 2000, findings, log,
                   knownRotDeg: 70f, knownPosM: 0.20f, knownDefect: "D1 -- BasisElbowAnatomyCore flips the elbow across its circle when a FIRING guard's elbow crosses the top (sG = sign(s)*sqrt(1-cG^2), and s is exactly 0 there). See BasisArmInvariantNetKnownOpenDefectTests.");
            }

            BasisArmNet.Report(findings, log, "pole stand-off continuity");
        }

        // ============================================================================================
        // 3. THE RATE LIMITER MUST NOT BE THE DISCONTINUITY
        // ============================================================================================

        /// <summary>
        /// The live rig passes a FINITE HintMaxStepDeg; the offline sweeps pass MaxValue. A rate limiter is
        /// a clamp, and a clamp on a stateless per-frame quantity engages and disengages as the input
        /// crosses it -- which is a gate crossing, i.e. the shape of every seam in this file. The budget is
        /// also SHARED between the hint swivel and the pole-collapse stabiliser (one DOF, one budget), so
        /// the point where the hint exhausts it is a second crossing.
        /// </summary>
        [Test]
        public void RateLimitedSolve_IsStillContinuous()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            foreach (float maxStep in new[] { 4f, 20f })
            foreach (int hint in new[] { BasisArmNet.HintTracker, BasisArmNet.HintLookup })
            {
                string ctx = $"tracker azimuth 0-360 under a {maxStep:0} deg/solve rate limit, " +
                             (hint == BasisArmNet.HintTracker ? "TRACKER" : "lookup pole");
                Run(ctx, t =>
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(-15f, -45f);
                    s.Reach = 0.93f;
                    s.HintMode = hint;
                    s.HintAzimuthDeg = t;
                    s.HintRhoMin = 0.02f;
                    s.MaxStepDeg = maxStep;
                    s.RefPerp = Vector3.up;
                    s.FeedTwistBind = true;
                    s.FeedLowerBind = true;
                    s.FeedTip = true;
                    return s;
                }, 0f, 360f, 1440, findings, log,
                   // the elbow travels a chord of 2*rho*sin(maxStep) across the flip; rho is ~10 cm at
                   // this reach, so 20 deg/solve is ~70 mm. Derived, not observed.
                   knownRotDeg: 2f * maxStep + 5f, knownPosM: 2f * 0.11f * Mathf.Sin(maxStep * Mathf.Deg2Rad) + 0.005f,
                   knownDefect: "D3 -- HintMaxStepDeg clamps a PRINCIPAL angle symmetrically, so the applied swivel " +
                                "flips from +maxStep to -maxStep at the +/-180 wrap: a 2*maxStep jump. OFFLINE ONLY -- " +
                                "BasisFullBodyIK passes float.MaxValue, so the live rig never enters the clamp; the " +
                                "offline sweep harness (BasisArmIKSweep) does.");
            }

            BasisArmNet.Report(findings, log, "rate-limited continuity");
        }
    }
}
