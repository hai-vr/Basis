using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Hand ROLL must recruit the upper arm — with or without an elbow tracker.
    ///
    /// THE GAP THIS EXISTS TO CLOSE. The tip is written straight to the target rotation, so every degree of
    /// controller roll used to land in the wrist joint; the twist bones only redistribute it along the
    /// forearm MESH. Nothing upstream read the hand's roll at all — the elbow field model is position-only,
    /// and a hand twisted IN PLACE cannot move a position-only elbow. Twist your hand and the upper arm sat
    /// rigid while the wrist candy-wrapped. A real arm pronates ~80° each way and then ROTATES THE HUMERUS,
    /// which with the hand pinned is exactly the swivel DOF: the elbow swings around its circle.
    ///
    /// WHAT THE RELIEF PROMISES, AND WHAT THESE TESTS HOLD IT TO:
    ///   - the roll is measured against the ANIMATED wrist carried onto the SOLVED forearm, so a neutral
    ///     hand is a structural no-op, and whatever swivel a tracker or the model already applied is
    ///     already relieved before it is measured (nothing double-compensates);
    ///   - in-band roll recruits the humerus at a fixed share (except on the tracker path, where the
    ///     measured elbow already contains the user's real share, and the pole must not be second-guessed);
    ///   - past the comfort band the humerus takes the excess ~1:1, capped, and faded to zero approaching
    ///     the ±180° principal-angle seam so the two sides of the same pose meet at zero;
    ///   - the relief is a swivel about shoulder→hand, so the hand CANNOT leave its target (geometry, not
    ///     tolerance), and the anatomy guard still has the last word on where the elbow may sit.
    ///
    /// The relief's sign is an identity, not a convention (swivelling the arm rolls the carried neutral with
    /// it), so pronation flares the elbow OUT on BOTH arms with no handedness flag — the mirror test below
    /// is what makes that claim falsifiable.
    /// </summary>
    public class BasisArmWristRollReliefTests
    {
        const float UpperLen = 0.30f, ForeLen = 0.30f;
        const float ArmLen = UpperLen + ForeLen;
        static readonly Vector3 BoneAxis = Vector3.right;   // shoulder→elbow in the bone's own frame (T-pose arm)
        static readonly Vector3 Shoulder = Vector3.zero;

        const float Band = BasisArmSolveCore.WristRollComfortDeg;        // 80
        const float Share = BasisArmSolveCore.WristRollInBandShare;      // 0.2
        const float Cap = BasisArmSolveCore.WristRollMaxReliefDeg;       // 70

        /// <summary>A RIGHT arm reaching `straight`, elbow bulged toward `bulge`, hand exactly on target and
        /// the animated hand riding the forearm (carried wrist local = identity), so TargetRotation == the
        /// carried neutral and any roll applied to it is EXACTLY the twist the relief should measure.</summary>
        static BasisArmSolveInput Pose(Vector3 straight, Vector3 bulge, float halfFlexDeg)
        {
            float f = halfFlexDeg * Mathf.Deg2Rad;
            Vector3 upperDir = (straight * Mathf.Cos(f) + bulge * Mathf.Sin(f)).normalized;
            Vector3 lowerDir = (straight * Mathf.Cos(f) - bulge * Mathf.Sin(f)).normalized;

            BasisArmSolveInput i = default;
            i.Shoulder = Shoulder;
            i.Elbow = Shoulder + upperDir * UpperLen;
            i.Hand = i.Elbow + lowerDir * ForeLen;
            i.RootRotation = Quaternion.FromToRotation(BoneAxis, upperDir);
            i.MidRotation = Quaternion.FromToRotation(BoneAxis, lowerDir);
            i.TipRotation = i.MidRotation;
            i.TargetPosition = i.Hand;
            i.TargetRotation = i.MidRotation;
            i.TargetOffset = Quaternion.identity;
            i.PlayerUp = Vector3.up;
            i.HintMaxStepDeg = float.MaxValue;
            return i;
        }

        static void Roll(ref BasisArmSolveInput i, float rollDeg)
        {
            Vector3 foreDir = (i.Hand - i.Elbow).normalized;
            i.TargetRotation = Quaternion.AngleAxis(rollDeg, foreDir) * i.MidRotation;
        }

        /// <summary>Signed elbow travel around the shoulder→hand circle, animated → solved. This is the ONLY
        /// motion the relief is allowed to produce.</summary>
        static float SwivelDeg(in BasisArmSolveInput i, in BasisArmSolveResult r)
        {
            Vector3 acN = (r.HandSolved - i.Shoulder).normalized;
            Vector3 before = i.Elbow - i.Shoulder;
            before -= acN * Vector3.Dot(before, acN);
            Vector3 after = r.ElbowSolved - i.Shoulder;
            after -= acN * Vector3.Dot(after, acN);
            return Vector3.SignedAngle(before, after, acN);
        }

        /// <summary>The residual roll left in the wrist after the solve: target vs the animated wrist carried
        /// onto the SOLVED forearm — the same quantity the relief measures, recomputed from the outputs.</summary>
        static float ResidualRollDeg(in BasisArmSolveInput i, in BasisArmSolveResult r)
        {
            Quaternion neutral = r.MidRotationSolved * Quaternion.Inverse(i.MidRotation) * i.TipRotation;
            Quaternion rel = i.TargetRotation * Quaternion.Inverse(neutral);
            if (rel.w < 0f) rel = new Quaternion(-rel.x, -rel.y, -rel.z, -rel.w);
            Vector3 foreDir = (r.HandSolved - r.ElbowSolved).normalized;
            Vector3 v = new Vector3(rel.x, rel.y, rel.z);
            return 2f * Mathf.Atan2(Vector3.Dot(v, foreDir), rel.w) * Mathf.Rad2Deg;
        }

        static void AssertHandOnTarget(in BasisArmSolveResult r, string when)
        {
            Assert.That(r.HandError, Is.LessThan(1e-4f),
                $"the relief is a swivel about shoulder→hand, so the hand must stay ON target ({when})");
        }

        [Test]
        public void NeutralRoll_IsAnExactNoOp()
        {
            BasisArmSolveInput i = Pose(Vector3.forward, Vector3.down, 25f);
            BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);

            Assert.That(r.WristReliefDeg, Is.EqualTo(0f), "no roll demanded → the relief must not engage");
            Assert.That(Mathf.Abs(SwivelDeg(i, r)), Is.LessThan(0.01f), "a neutral hand must not move the elbow");
            AssertHandOnTarget(r, "neutral");
        }

        [Test]
        public void ZeroTipRotation_DisablesTheRelief()
        {
            BasisArmSolveInput i = Pose(Vector3.forward, Vector3.down, 25f);
            Roll(ref i, 130f);
            i.TipRotation = default;   // what every offline caller that predates the field passes

            BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);

            Assert.That(r.WristReliefDeg, Is.EqualTo(0f), "a zero TipRotation is the documented off-switch");
            Assert.That(Mathf.Abs(SwivelDeg(i, r)), Is.LessThan(0.01f),
                "with the relief off, a rolled target must not move the elbow (the pre-feature behaviour)");
        }

        [TestCase(40f)]
        [TestCase(-40f)]
        [TestCase(70f)]
        public void InBandRoll_RecruitsTheHumerus_ByTheShare(float rollDeg)
        {
            BasisArmSolveInput i = Pose(Vector3.forward, Vector3.down, 25f);
            Roll(ref i, rollDeg);

            BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);

            Assert.That(r.WristTwistDeg, Is.EqualTo(rollDeg).Within(0.5f), "the measured roll IS the applied roll here");
            Assert.That(SwivelDeg(i, r), Is.EqualTo(Share * rollDeg).Within(0.75f),
                "in-band roll recruits the humerus at the fixed share, in the roll's own direction");
            Assert.That(Mathf.Abs(ResidualRollDeg(i, r)), Is.LessThan(Mathf.Abs(rollDeg)),
                "recruiting the humerus must RELIEVE the wrist, not just move the elbow");
            AssertHandOnTarget(r, $"roll {rollDeg}");
        }

        [Test]
        public void BeyondTheBand_TheHumerusTakesTheExcess()
        {
            BasisArmSolveInput i = Pose(Vector3.forward, Vector3.down, 25f);
            Roll(ref i, 120f);

            BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);

            float expected = Share * Band + (120f - Band);   // 16 + 40 = 56
            Assert.That(SwivelDeg(i, r), Is.EqualTo(expected).Within(1f),
                "past the comfort band the humerus takes the excess on top of its in-band share");
            Assert.That(Mathf.Abs(ResidualRollDeg(i, r)), Is.LessThan(Band),
                "the whole point: the wrist must be brought back inside what a forearm can render");
            AssertHandOnTarget(r, "roll 120");
        }

        [Test]
        public void Tracker_InBandThePoleIsNotSecondGuessed_BeyondItTheWristIsStillSaved()
        {
            BasisArmSolveInput i = Pose(Vector3.forward, Vector3.down, 25f);
            i.HintWeight = true;
            i.HintIsTracker = true;
            i.HintPosition = i.Elbow;   // the tracker agrees with the animated elbow

            Roll(ref i, 40f);
            BasisArmSolveCore.Solve(i, out BasisArmSolveResult rIn);
            Assert.That(Mathf.Abs(SwivelDeg(i, rIn)), Is.LessThan(0.1f),
                "a measured elbow already contains the user's real in-band share — do not add another");

            Roll(ref i, 140f);
            BasisArmSolveCore.Solve(i, out BasisArmSolveResult rOut);
            Assert.That(SwivelDeg(i, rOut), Is.EqualTo(140f - Band).Within(1f),
                "past the band the avatar's wrist is broken regardless of what the tracker says — relieve it");
            AssertHandOnTarget(rOut, "tracker roll 140");
        }

        /// <summary>Pronation must flare the elbow toward the OUTWARD side on BOTH arms. The core has no
        /// handedness input, so this is asserted by mirroring the entire problem through the YZ plane and
        /// requiring the answer to mirror with it — the test that catches any hidden world-axis dependence.</summary>
        [Test]
        public void MirroredArm_MirrorsTheRelief()
        {
            BasisArmSolveInput right = Pose(Vector3.forward, Vector3.down, 25f);
            Roll(ref right, 120f);
            BasisArmSolveCore.Solve(right, out BasisArmSolveResult rR);

            BasisArmSolveInput left = right;
            left.Shoulder = MirrorV(right.Shoulder);
            left.Elbow = MirrorV(right.Elbow);
            left.Hand = MirrorV(right.Hand);
            left.TargetPosition = MirrorV(right.TargetPosition);
            left.HintPosition = MirrorV(right.HintPosition);
            left.PlayerUp = MirrorV(right.PlayerUp);
            left.RootRotation = MirrorQ(right.RootRotation);
            left.MidRotation = MirrorQ(right.MidRotation);
            left.TipRotation = MirrorQ(right.TipRotation);
            left.TargetRotation = MirrorQ(right.TargetRotation);
            left.TargetOffset = MirrorQ(right.TargetOffset);
            BasisArmSolveCore.Solve(left, out BasisArmSolveResult rL);

            Vector3 expected = MirrorV(rR.ElbowSolved);
            Assert.That(Vector3.Distance(rL.ElbowSolved, expected), Is.LessThan(1e-4f),
                "a mirrored problem must produce the mirrored elbow — pronation flares OUT on both arms");

            // And on the right arm the flare really is OUTWARD (+X for a right arm reaching +Z from an
            // elbow-down rest): the anatomical direction, pinned in absolute terms exactly once.
            Assert.That(rR.ElbowSolved.x, Is.GreaterThan(right.Elbow.x + 0.05f),
                "pronating past the band must flare a right elbow toward +X (out), not across the body");
        }

        [Test]
        public void Continuity_AcrossTheBandEdge_AndTheWrapSeam()
        {
            float prev = 0f;
            bool seeded = false;
            for (float roll = 60f; roll <= 100f; roll += 1f)
            {
                BasisArmSolveInput i = Pose(Vector3.forward, Vector3.down, 25f);
                Roll(ref i, roll);
                BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);
                float s = SwivelDeg(i, r);
                if (seeded)
                {
                    Assert.That(Mathf.Abs(s - prev), Is.LessThan(1.5f),
                        $"crossing the band edge must not step the elbow (roll {roll})");
                }
                AssertHandOnTarget(r, $"sweep roll {roll}");
                prev = s; seeded = true;
            }

            seeded = false;
            for (float roll = 150f; roll <= 179.5f; roll += 0.5f)
            {
                BasisArmSolveInput i = Pose(Vector3.forward, Vector3.down, 25f);
                Roll(ref i, roll);
                BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);
                float s = SwivelDeg(i, r);
                Assert.That(Mathf.Abs(s), Is.LessThanOrEqualTo(Cap + 0.5f), "the relief must respect its cap");
                if (seeded)
                {
                    Assert.That(Mathf.Abs(s - prev), Is.LessThan(3f),
                        $"the wrap fade must be a fade, not a step (roll {roll})");
                }
                prev = s; seeded = true;
            }

            // The seam itself: +179.5° and -179.5° are one hand pose. Both must answer ~zero.
            BasisArmSolveInput a = Pose(Vector3.forward, Vector3.down, 25f);
            Roll(ref a, 179.5f);
            BasisArmSolveCore.Solve(a, out BasisArmSolveResult ra);
            BasisArmSolveInput b = Pose(Vector3.forward, Vector3.down, 25f);
            Roll(ref b, -179.5f);
            BasisArmSolveCore.Solve(b, out BasisArmSolveResult rb);
            Assert.That(Mathf.Abs(SwivelDeg(a, ra)), Is.LessThan(1f), "the +180 side of the seam must fade to zero");
            Assert.That(Mathf.Abs(SwivelDeg(b, rb)), Is.LessThan(1f), "the -180 side of the seam must fade to zero");
        }

        /// <summary>The guard still owns the outcome: start the elbow OUT (horizontal) so a big pronation
        /// flare heads for the sky, and require the anatomy ceiling to hold anyway.</summary>
        [Test]
        public void TheAnatomyGuard_StillHasTheLastWord()
        {
            BasisArmSolveInput i = Pose(Vector3.forward, Vector3.right, 25f);   // elbow bulged OUT, hand at shoulder height
            Roll(ref i, 150f);

            BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);

            Assert.That(r.WristReliefDeg, Is.GreaterThan(0f), "the flare this test guards against must actually be demanded");
            float ceiling = Mathf.Max(i.Shoulder.y, r.HandSolved.y);
            Assert.That(r.ElbowSolved.y, Is.LessThan(ceiling + BasisElbowAnatomyCore.HardMarginFracLimb * ArmLen),
                "however hard the wrist asks, the elbow may not cross the anatomy ceiling's asymptote");
            AssertHandOnTarget(r, "guarded flare");
        }

        static Vector3 MirrorV(Vector3 v) => new Vector3(-v.x, v.y, v.z);
        static Quaternion MirrorQ(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);
    }
}
