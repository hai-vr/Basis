using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// THE TWO THINGS THE HUMERAL TWIST GUARD SHIPPED WITHOUT A GATE FOR, AND BOTH WERE BROKEN.
    ///
    /// ================================================================================================
    /// BasisHumeralTwistGuardTests holds the guard to four promises -- it bounds, it moves nothing, it is
    /// the exact identity inside the envelope, and it declines field by field. Every one of them is
    /// evaluated ONE POSE AT A TIME, and two defects lived in the gaps between them:
    ///
    ///   1. ⭐ THE +/-180 SEAM. The twist is a PRINCIPAL angle, so +179.5 and -179.5 are the same humerus
    ///      one degree apart, and the old correction `sign(twist) * guarded - twist` mapped them to +140
    ///      and -140 -- poses 80 degrees apart. MEASURED on the live core before the fix: an 80.009 degree
    ///      single-step jump in the upper arm's pose for a 0.05 degree step of tracker roll, at every
    ///      flexion tested, against an unguarded control that moved 0.056 degrees. A discontinuity is
    ///      invisible in any single pose by construction, which is exactly why a per-pose suite could not
    ///      see it, and it is reachable by rolling the elbow tracker round its circle -- which is what the
    ///      user did when they reported "if i move my hint role around im able to get it to flip".
    ///
    ///   2. ⭐ THE HAND. The guard rotates the ROOT about shoulder->elbow and folds that into HintDelta,
    ///      and BasisFullBodyIK applies HintDelta to the root -- which carries the forearm and the hand
    ///      RIGIDLY. The elbow lies on that axis; the hand does not. MEASURED by replaying the runtime's
    ///      own composition order: the hand moved up to 205.2 mm.
    ///
    ///      ⚠️ AND THE EXISTING TEST FOR THIS CANNOT FAIL.
    ///      HumeralTwistGuard_MovesNoJoint_AndActsAboutShoulderToElbow asserts it by comparing
    ///      `c.HandSolved` with `g.HandSolved` -- and the guard writes NEITHER. It updates rootRot and
    ///      hintR and nothing else, so the two numbers are equal because they are both untouched, not
    ///      because the hand stayed put. It reported 0.000 mm throughout. THAT is why this file replays
    ///      the STREAM instead of reading the solver's bookkeeping, and why it asserts the replay is
    ///      faithful before it asserts anything with it.
    /// ================================================================================================
    ///
    /// ⚠️ WHY THE SEAM FIX IS A CAP AND NOT A CLEAN CLAMP. The obstruction is topological. The twist lives
    /// on a circle and the envelope is an arc; any map from the circle onto the arc that is the identity
    /// inside the arc must either be discontinuous, or traverse the arc's whole 300 degree complement over
    /// the 60 degree illegal wedge -- a 5x amplifier that at 180 degrees of twist drives the humerus to
    /// ZERO twist, i.e. 180 degrees from the pose it is supposed to be guarding. There is no third option,
    /// so the guard caps its correction by the distance to the seam (this file's own established answer to
    /// a principal-angle seam -- see BasisArmSolveCore's wrist-roll relief, which does the same thing) and
    /// pays for continuity by not enforcing the hard bound in the last ~15 degrees before 180.
    /// </summary>
    public class BasisArmHumeralTwistSeamTests
    {
        const float UpperLen = 0.30f, ForeLen = 0.30f;
        static readonly Vector3 Shoulder = Vector3.zero;
        static readonly Vector3 HangDir = Vector3.down;
        static readonly Quaternion ClavBind = Quaternion.Euler(4f, -11f, 6f);

        static readonly float[] Flexions = { 170f, 160f, 150f, 135f, 120f, 90f };

        struct Bind { public Quaternion HumerusRot; public Vector3 Dir; public Vector3 RefAxis; }

        /// <summary>Mirrors BasisFullBodyIK.BakeHumerusTwistBind, exactly as BasisHumeralTwistGuardTests does.</summary>
        static Bind Bake(Quaternion bindRot, Vector3 bindDir)
        {
            Bind b = default;
            b.HumerusRot = bindRot;
            b.Dir = bindDir.normalized;
            Vector3 localBone = Quaternion.Inverse(bindRot) * b.Dir;
            Vector3 refLocal = Mathf.Abs(localBone.y) < 0.9f ? Vector3.up : Vector3.forward;
            Vector3 perp = refLocal - localBone * Vector3.Dot(refLocal, localBone);
            b.RefAxis = perp.sqrMagnitude > 1e-8f ? perp.normalized : Vector3.zero;
            return b;
        }

        static Bind DefaultRig() => Bake(BasisQuaternionExt.FromToRotation(Vector3.right, HangDir), HangDir);

        /// <summary>A hanging arm bent to `flexDeg`, with the elbow tracker rolled `rollDeg` about the
        /// shoulder->hand axis. Identical in shape to BasisHumeralTwistGuardTests.Pose, so the two files
        /// are measuring the same rig; `feedGuard` false leaves the five bind fields at their struct
        /// default, which is the unguarded control.</summary>
        static BasisArmSolveInput Pose(in Bind b, float flexDeg, float rollDeg, bool feedGuard)
        {
            Vector3 elbow0 = HangDir * UpperLen;
            Quaternion bend = Quaternion.AngleAxis(180f - flexDeg, Vector3.right);
            Vector3 hand0 = elbow0 + (bend * HangDir) * ForeLen;
            Vector3 hint0 = Quaternion.AngleAxis(rollDeg, hand0.normalized) * elbow0;

            BasisArmSolveInput i = default;
            i.Shoulder = Shoulder;
            i.Elbow = elbow0;
            i.Hand = hand0;
            i.RootRotation = b.HumerusRot;
            i.MidRotation = bend * b.HumerusRot;
            i.TargetPosition = hand0;
            i.TargetRotation = i.MidRotation;
            i.TargetOffset = Quaternion.identity;
            i.PlayerUp = Vector3.up;
            i.TorsoUp = Vector3.up;
            i.HintWeight = true;
            i.HintIsTracker = true;          // stands the wrist relief and the world-down stabiliser down
            i.HintPosition = hint0;
            i.HintMaxStepDeg = float.MaxValue;

            if (feedGuard)
            {
                i.ClavicleRotation = ClavBind;
                i.BindClavicleRotation = ClavBind;
                i.BindHumerusRotation = b.HumerusRot;
                i.BindHumerusDir = b.Dir;
                i.BindHumerusRefAxis = b.RefAxis;
            }
            return i;
        }

        /// <summary>
        /// ⭐ THE RUNTIME'S OWN COMPOSITION, REPLAYED. BasisFullBodyIK.SolveTwoBoneIKArms ends with
        ///
        ///   mid.SetRotation (MidDelta * mid);  root.SetRotation(RootDelta * root);
        ///   root.SetRotation(HintDelta * root); mid.SetRotation(MidPostRoll * mid); tip.SetRotation(...)
        ///
        /// and setting a PARENT's rotation in an AnimationStream carries its children rigidly: the child's
        /// local transform is preserved, so the child's world rotation is left-multiplied and its position
        /// pivots about the parent's origin. That is the whole content of this function, and it is the
        /// difference between what the solver BOOKKEEPS and what the user's arm actually does.
        /// </summary>
        static void StreamCompose(float flexDeg, in BasisArmSolveResult r, out Vector3 elbow, out Vector3 hand)
        {
            Vector3 a = Shoulder;
            Vector3 bp = HangDir * UpperLen;
            Quaternion bend = Quaternion.AngleAxis(180f - flexDeg, Vector3.right);
            Vector3 cp = bp + (bend * HangDir) * ForeLen;

            cp = bp + r.MidDelta * (cp - bp);                    // mid.SetRotation: the tip pivots about the elbow
            bp = a + r.RootDelta * (bp - a);                     // root.SetRotation: both children pivot about the shoulder
            cp = a + r.RootDelta * (cp - a);
            bp = a + r.HintDelta * (bp - a);                     // root.SetRotation again
            cp = a + r.HintDelta * (cp - a);
            cp = bp + r.MidPostRoll * (cp - bp);                 // mid.SetRotation: the tip pivots about the elbow

            elbow = bp;
            hand = cp;
        }

        /// <summary>
        /// ⚠️ atan2, NOT 2*acos(|dot|). acos is ill-conditioned where its argument approaches 1, which is
        /// exactly the regime a continuity gate lives in: measured, `2*acos(|dot|)` returns up to 0.09 deg
        /// for BIT-IDENTICAL float32 rotations. A gate written as "assert change < 0.001" on that form can
        /// never detect a real sub-0.09 deg regression while appearing to be tight -- it reports noise as
        /// signal and would mask precisely the class of defect this file exists to catch.
        /// The half-angle via atan2 of |vector part| against |scalar part| is stable at both ends.
        /// </summary>
        static float PoseChangeDeg(Quaternion from, Quaternion to)
        {
            Quaternion d = to * Quaternion.Inverse(from);
            float v = Mathf.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
            return 2f * Mathf.Atan2(v, Mathf.Abs(d.w)) * Mathf.Rad2Deg;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 1. CONTINUITY ACROSS THE SEAM
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ ROLL THE TRACKER THROUGH 180 DEGREES OF TWIST IN FINE STEPS. THE ARM MAY NOT JUMP.
        ///
        /// The methodology is this repo's own for a discontinuity: it is invisible in any single pose and
        /// exists only BETWEEN two of them, so sweep finely and measure the worst single-step change --
        /// against a CONTROL sweep that proves the input itself is smooth, so a jump can only be the
        /// guard's.
        ///
        /// The gate is an AMPLIFICATION, not an absolute angle, because the honest bound is structural: the
        /// correction is capped by `PI - |twist|`, whose slope is -1, against a `pull` whose slope is +1, so
        /// min() of the two has slope magnitude 1 and the POSE can change at most 2x as fast as the input.
        /// 4x leaves room for the saturation curve's own slope and for float, and nothing else. Before the
        /// fix this measured 1429.9x.
        /// </summary>
        [Test]
        public void TheHumeralTwistGuard_IsContinuousAcrossTheSeam()
        {
            const float k_Step = 0.05f;
            const float k_MaxAmplification = 4f;
            const float k_MaxAbsoluteJumpDeg = 1.0f;

            Bind b = DefaultRig();
            var report = new System.Text.StringBuilder();
            float mostGuard = 0f;
            bool measuredSomething = false;

            foreach (float flex in Flexions)
            {
                float worstGuarded = 0f, worstControl = 0f, atRoll = 0f;
                Quaternion prevG = Quaternion.identity, prevC = Quaternion.identity;
                bool first = true;

                for (float roll = 150f; roll <= 210.0001f; roll += k_Step)
                {
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, true), out BasisArmSolveResult g);
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, false), out BasisArmSolveResult c);

                    if (!first)
                    {
                        float jg = PoseChangeDeg(prevG, g.RootRotationSolved);
                        float jc = PoseChangeDeg(prevC, c.RootRotationSolved);
                        if (jg > worstGuarded) { worstGuarded = jg; atRoll = roll; }
                        if (jc > worstControl) worstControl = jc;
                    }

                    mostGuard = Mathf.Max(mostGuard, Mathf.Abs(g.HumeralTwistGuardDeg));
                    prevG = g.RootRotationSolved;
                    prevC = c.RootRotationSolved;
                    first = false;
                }

                // ANTI-TAUTOLOGY, per flexion: if the CONTROL never moves, the sweep is not exciting the
                // degree of freedom and "the guarded sweep did not jump" would be worth nothing.
                Assert.That(worstControl, Is.GreaterThan(1e-3f),
                    $"flex={flex}: the UNGUARDED control moved only {worstControl:0.0000} deg per step across the " +
                    "whole seam sweep -- this sweep is not rolling the humerus at all, so it cannot detect a jump.");

                measuredSomething = true;
                float amp = worstGuarded / worstControl;
                report.AppendLine(
                    $"    flex {flex,5:F0}: worst step guarded {worstGuarded,7:F3} deg (roll {atRoll:F2}), " +
                    $"control {worstControl,6:F3} deg, amplification {amp,7:F1}x");

                Assert.That(worstGuarded, Is.LessThan(k_MaxAbsoluteJumpDeg),
                    $"flex={flex}: THE ARM JUMPED {worstGuarded:0.000} deg in a single {k_Step:0.00} deg step of tracker " +
                    $"roll, at roll {atRoll:0.00}. The twist is a PRINCIPAL angle and +179.5 / -179.5 are the same " +
                    "humerus; a correction of sign(twist)*guarded sends them to opposite ends of the envelope and " +
                    "snaps the arm across. See BasisArmSolveCore's seam cap.");
                Assert.That(amp, Is.LessThan(k_MaxAmplification),
                    $"flex={flex}: the guard amplified a smooth input {amp:0.0}x (guarded {worstGuarded:0.000} deg vs " +
                    $"control {worstControl:0.000} deg per step). The seam cap bounds this at 2x BY CONSTRUCTION " +
                    "(min of a +1 and a -1 slope), so anything near this gate means the cap has been changed for " +
                    "something that merely RELOCATES the discontinuity.");
            }

            // ANTI-TAUTOLOGY, overall: the guard has to have actually engaged somewhere in the sweep, or
            // every assertion above is measuring the unguarded solve twice.
            Assert.That(mostGuard, Is.GreaterThan(5f),
                $"the guard never applied more than {mostGuard:0.000} deg anywhere in the seam sweep, so this test " +
                "is comparing the unguarded arm with itself.");
            Assert.IsTrue(measuredSomething, "no flexion was swept.");

            TestContext.WriteLine("\n  seam continuity, 0.05 deg steps of tracker roll through 150-210 deg:\n" + report);
        }

        /// <summary>
        /// AND THE CAP MUST NOT HAVE EATEN THE ORDINARY REGIME. Below the crossing where `PI - |twist|`
        /// overtakes the correction (|twist| ~ 158.3 deg) the cap does not bind and the guard is the exact
        /// saturation it always was. If a future change makes the cap bind earlier it would quietly stop
        /// enforcing the hard bound over ordinary poses, and that is what this pins.
        /// </summary>
        [Test]
        public void BelowTheSeamWindow_TheGuardIsStillTheUnchangedSaturation()
        {
            Bind b = DefaultRig();
            const float soft = BasisArmSolveCore.HumeralTwistSoftDeg;
            const float hard = BasisArmSolveCore.HumeralTwistHardDeg;

            int checkedPoses = 0;
            float worstErr = 0f;

            foreach (float flex in Flexions)
            {
                for (float roll = 0f; roll <= 360f; roll += 0.5f)
                {
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, true), out BasisArmSolveResult g);

                    float mag = Mathf.Abs(g.HumeralTwistDeg);
                    if (mag <= soft || mag > 155f)
                    {
                        continue;   // below the guard, or inside the seam window where the cap is meant to bind
                    }

                    float want = BasisJointLimitCore.Saturate(mag, soft, hard);
                    float got = mag - Mathf.Abs(g.HumeralTwistGuardDeg);
                    worstErr = Mathf.Max(worstErr, Mathf.Abs(got - want));
                    checkedPoses++;
                }
            }

            Assert.That(checkedPoses, Is.GreaterThan(50),
                $"only {checkedPoses} poses landed between the soft limit and the seam window; this test measured " +
                "almost nothing.");
            Assert.That(worstErr, Is.LessThan(0.05f),
                $"between the soft limit and 155 deg the guard departed from the plain saturation by {worstErr:0.000} " +
                "deg. The seam cap is only supposed to bind ABOVE ~158 deg; if it is binding here the hard bound has " +
                "been quietly given up over ordinary poses.");

            TestContext.WriteLine($"  {checkedPoses} poses between the soft limit and 155 deg match the unchanged " +
                                  $"saturation to {worstErr:0.0000} deg.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 2. THE HAND, MEASURED THROUGH THE STREAM AND NOT THROUGH THE BOOKKEEPING
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ THE GUARD MUST NOT MOVE THE HAND -- AND THIS IS THE MEASUREMENT THAT CAN SAY SO.
        ///
        /// The correction is folded into HintDelta, which the runtime applies to the ROOT, which carries
        /// the forearm and the hand rigidly. Its axis is shoulder->ELBOW, and the hand is not on it, so a
        /// 40 degree correction at 90 degrees of elbow flexion orbits the hand 205 mm.
        ///
        /// ⚠️ THE ORDER OF THE ASSERTIONS IS THE POINT. The replay is checked against the solver on the
        /// UNGUARDED path FIRST. If those disagree, the replay is wrong and every number after it is
        /// worthless -- and asserting the hand is still with an unfaithful replay is precisely the failure
        /// this test exists to replace.
        /// </summary>
        [Test]
        public void TheHumeralTwistGuard_DoesNotMoveTheHand_ThroughTheAnimationStream()
        {
            Bind b = DefaultRig();

            // ── the replay must reproduce the solver's own bookkeeping where that bookkeeping is trusted.
            float worstReplayErr = 0f;
            foreach (float flex in Flexions)
            {
                for (float roll = 0f; roll <= 360f; roll += 3f)
                {
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, false), out BasisArmSolveResult c);
                    StreamCompose(flex, c, out Vector3 e, out Vector3 h);
                    worstReplayErr = Mathf.Max(worstReplayErr, Vector3.Distance(e, c.ElbowSolved));
                    worstReplayErr = Mathf.Max(worstReplayErr, Vector3.Distance(h, c.HandSolved));
                }
            }
            Assert.That(worstReplayErr, Is.LessThan(1e-5f),
                $"the stream replay disagrees with the solver by {worstReplayErr * 1000f:0.000000} mm on the UNGUARDED " +
                "path, where the two must agree exactly. The replay is wrong, so nothing measured with it below " +
                "means anything.");

            // ── now the guarded path.
            var offenders = new System.Text.StringBuilder();
            float mostGuard = 0f, worstHand = 0f, worstElbow = 0f, worstBookkeeping = 0f;

            foreach (float flex in Flexions)
            {
                for (float roll = 0f; roll <= 360f; roll += 1f)
                {
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, false), out BasisArmSolveResult c);
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, true), out BasisArmSolveResult g);

                    StreamCompose(flex, c, out Vector3 ce, out Vector3 ch);
                    StreamCompose(flex, g, out Vector3 ge, out Vector3 gh);

                    mostGuard = Mathf.Max(mostGuard, Mathf.Abs(g.HumeralTwistGuardDeg));
                    float handMoved = Vector3.Distance(ch, gh);
                    worstHand = Mathf.Max(worstHand, handMoved);
                    worstElbow = Mathf.Max(worstElbow, Vector3.Distance(ce, ge));
                    worstBookkeeping = Mathf.Max(worstBookkeeping, Vector3.Distance(c.HandSolved, g.HandSolved));

                    if (handMoved > 1e-4f)
                    {
                        offenders.AppendLine(
                            $"  flex={flex:0} roll={roll:0}: correction {g.HumeralTwistGuardDeg:0.0} deg moved the hand " +
                            $"{handMoved * 1000f:0.0} mm through the stream (the solver reported " +
                            $"{Vector3.Distance(c.HandSolved, g.HandSolved) * 1000f:0.000} mm)");
                    }
                }
            }

            // ANTI-TAUTOLOGY: with no correction anywhere, "the hand did not move" is trivially true.
            Assert.That(mostGuard, Is.GreaterThan(5f),
                $"the guard never applied more than {mostGuard:0.000} deg, so this test compared the unguarded arm " +
                "with itself.");

            Assert.That(worstElbow, Is.LessThan(1e-5f),
                $"the guard moved the ELBOW {worstElbow * 1000f:0.000000} mm through the stream. It is applied about " +
                "shoulder->elbow and the elbow lies ON that axis, so this must be zero however the correction is " +
                "composed.");

            if (offenders.Length > 0)
            {
                Assert.Fail(
                    $"THE HUMERAL TWIST GUARD MOVED THE HAND, worst {worstHand * 1000f:0.0} mm:\n" + offenders +
                    "\nThe correction goes into HintDelta, the runtime applies HintDelta to the ROOT, and a parent's " +
                    "rotation carries its children rigidly -- so a rotation about shoulder->ELBOW orbits the hand. " +
                    "BasisArmSolveCore takes it back off the forearm by right-multiplying MidPostRoll with " +
                    "inverse(twistR); if that has been removed, this is what it costs.\n" +
                    $"⚠️ NOTE the solver's own bookkeeping reported {worstBookkeeping * 1000f:0.000} mm for the very " +
                    "same frames, because the guard writes neither cPosition nor midRot. Comparing HandSolved before " +
                    "and after the guard CANNOT detect this and must not be used to.");
            }

            TestContext.WriteLine(
                $"  worst correction {mostGuard:0.0} deg; hand moved {worstHand * 1000f:0.000000} mm and elbow " +
                $"{worstElbow * 1000f:0.000000} mm through the real stream composition.");
        }
    }
}
