using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Pins <see cref="BasisSwivelHintCore"/> -- the LIVE RIG's feature construction for the swivel models --
    /// against the construction the models were actually FITTED in (BasisMocapAccuracy, which IS the fit
    /// pipeline).
    ///
    /// ================================================================================================
    /// WHY THIS FILE HAS TO EXIST.
    ///
    /// The mocap harness CANNOT verify a live-rig change. It drives the solve CORES directly; it never runs the
    /// animation job. So the harness can prove the model is good and still tell you nothing about whether the
    /// rig is feeding it correctly -- and a swivel model fed wrong does not degrade gracefully, it produces
    /// CONFIDENT GARBAGE. That already happened once here: a fit done in a separate pipeline scored 3.77% there
    /// and 31% in the harness, because the two disagreed about the mirror, and the predicted swivel came out 145
    /// degrees off. Nothing crashed. Nothing warned.
    ///
    /// These tests are the bridge. They build a synthetic rig where the harness's own formula is known in closed
    /// form, and assert the runtime path reproduces it -- and then assert the two invariances the runtime needs
    /// that the harness never had to care about, because a BVH's rest pose is identity and a real avatar's is
    /// not.
    /// ================================================================================================
    /// </summary>
    public class BasisSwivelHintConformanceTests
    {
        const float k_Tol = 1e-4f;

        // A mocap-like rig: T-posed, upright, facing +Z, at world identity. This is the ONE configuration in
        // which the harness's construction and the runtime's are meant to coincide term for term, so it is
        // where they get compared.
        static readonly Vector3 k_LeftUpperArm = new Vector3(-0.17f, 1.40f, 0f);
        static readonly Vector3 k_RightUpperArm = new Vector3(0.17f, 1.40f, 0f);
        static readonly Vector3 k_Chest = new Vector3(0f, 1.25f, 0f);
        static readonly Vector3 k_Neck = new Vector3(0f, 1.50f, 0f);

        static readonly Vector3 k_LeftUpperLeg = new Vector3(-0.09f, 0.92f, 0f);
        static readonly Vector3 k_RightUpperLeg = new Vector3(0.09f, 0.92f, 0f);
        static readonly Vector3 k_Hips = new Vector3(0f, 0.95f, 0f);

        const float k_ArmLen = 0.60f;
        const float k_LegLen = 0.85f;

        static BasisSwivelFrame ArmFrame() =>
            BasisSwivelHintCore.BuildFrame(k_LeftUpperArm, k_RightUpperArm, k_Chest, k_Neck);

        static BasisSwivelFrame LegFrame() =>
            BasisSwivelHintCore.BuildFrame(k_LeftUpperLeg, k_RightUpperLeg, k_Hips, k_Chest);

        // ---------------------------------------------------------------------------------------------------
        // THE HARNESS'S OWN CONSTRUCTION, transcribed from BasisMocapAccuracy.SolveArm. Deliberately a COPY and
        // not a call: the point is to pin the runtime against the fit's formula as WRITTEN, so that if someone
        // edits either one this test fails instead of the elbow quietly moving 145 degrees in a shipped build.
        // ---------------------------------------------------------------------------------------------------
        static void HarnessArmFeatures(Vector3 lSh, Vector3 rSh, Vector3 neck, Vector3 chest,
                                       Vector3 shoulder, Vector3 hand, Quaternion handRot,
                                       float armLen, bool isLeft,
                                       out float3 tipLocal, out float3x3 tipOrient)
        {
            Vector3 bUp = (neck - chest).normalized;
            Vector3 bRight = rSh - lSh;
            bRight = (bRight - bUp * Vector3.Dot(bRight, bUp)).normalized;
            Vector3 bFwd = Vector3.Cross(bRight, bUp);
            Vector3 bOut = isLeft ? -bRight : bRight;

            Vector3 s2h = hand - shoulder;
            tipLocal = new float3(Vector3.Dot(s2h, bOut) / armLen,
                                  Vector3.Dot(s2h, bUp) / armLen,
                                  Vector3.Dot(s2h, bFwd) / armLen);

            Vector3 hX = handRot * Vector3.right;
            Vector3 hY = handRot * Vector3.up;
            Vector3 hZ = handRot * Vector3.forward;
            float3 InBody(Vector3 v) => new float3(Vector3.Dot(v, bOut), Vector3.Dot(v, bUp), Vector3.Dot(v, bFwd));
            tipOrient = new float3x3(InBody(hX), InBody(hY), InBody(hZ));
        }

        static void AssertClose(float3 a, float3 b, string what)
        {
            Assert.AreEqual(a.x, b.x, k_Tol, what + ".x");
            Assert.AreEqual(a.y, b.y, k_Tol, what + ".y");
            Assert.AreEqual(a.z, b.z, k_Tol, what + ".z");
        }

        static void AssertClose(float3x3 a, float3x3 b, string what)
        {
            AssertClose(a.c0, b.c0, what + ".c0");
            AssertClose(a.c1, b.c1, what + ".c1");
            AssertClose(a.c2, b.c2, what + ".c2");
        }

        /// <summary>
        /// THE CONFORMANCE TEST. On a rig shaped like the mocap skeleton -- T-pose rotations identity, body frame
        /// world-aligned -- the runtime's features must equal the fit's, term for term, on BOTH sides.
        ///
        /// This is the test that would have caught the mirror bug. It is checked on both arms because a
        /// left/right mirror error is invisible on the right arm and catastrophic on the left.
        /// </summary>
        [Test]
        public void ArmFeatures_MatchTheFitPipeline_OnAMocapShapedRig()
        {
            BasisSwivelFrame frame = ArmFrame();
            Assert.IsTrue(frame.Valid, "the T-posed reference rig must produce a valid frame");

            var rng = new System.Random(20260714);
            for (int t = 0; t < 200; t++)
            {
                bool isLeft = (t & 1) == 0;
                Vector3 shoulder = isLeft ? k_LeftUpperArm : k_RightUpperArm;

                Vector3 hand = shoulder + RandomInBall(rng, 0.95f * k_ArmLen);
                Quaternion handRot = RandomRotation(rng);

                // The rig's T-pose IS identity here, so the runtime's T-pose division is a no-op and the two
                // constructions must coincide exactly.
                BasisSwivelHintCore.Features(frame, frame, shoulder, hand, handRot, Quaternion.identity,
                                             k_ArmLen, isLeft, out float3 local, out float3x3 orient);

                HarnessArmFeatures(k_LeftUpperArm, k_RightUpperArm, k_Neck, k_Chest, shoulder, hand, handRot,
                                   k_ArmLen, isLeft, out float3 hLocal, out float3x3 hOrient);

                AssertClose(hLocal, local, $"tipLocal (iter {t}, {(isLeft ? "L" : "R")})");
                AssertClose(hOrient, orient, $"tipOrient (iter {t}, {(isLeft ? "L" : "R")})");
            }
        }

        /// <summary>
        /// ⭐ THE ONE THE HARNESS COULD NEVER HAVE CAUGHT.
        ///
        /// The T-pose is captured with the player standing at whatever world yaw they happened to be facing, and
        /// `handRot * inverse(handTposeRot)` is a WORLD-space delta -- so a naive division leaves the orientation
        /// features multiplied by a constant rotation nobody fitted. The POSITION features are fine (the body
        /// frame yaws with the player, so it cancels), which is exactly what makes this bug so quiet: the elbow
        /// would still land in roughly the right region, just systematically rotated, and only for users who
        /// calibrated facing a different way than whoever tested it.
        ///
        /// Rotate the ENTIRE world -- the T-pose capture and the live pose together -- and every feature must be
        /// bit-stable. This passes only because Features() applies the delta to the T-POSE FRAME's own axes
        /// rather than to world x/y/z.
        /// </summary>
        [Test]
        public void ArmFeatures_AreInvariant_ToTheWorldYawAtCalibration()
        {
            var rng = new System.Random(4242);
            BasisSwivelFrame frameT = ArmFrame();

            for (int t = 0; t < 120; t++)
            {
                bool isLeft = (t & 1) == 0;
                Vector3 shoulder = isLeft ? k_LeftUpperArm : k_RightUpperArm;
                Vector3 hand = shoulder + RandomInBall(rng, 0.95f * k_ArmLen);
                Quaternion handRot = RandomRotation(rng);
                Quaternion handTpose = RandomRotation(rng);   // an arbitrary rig convention, too

                // The live body frame: the player has MOVED since calibration, so build it from a live pose.
                // (Same bones, mildly perturbed, so the frame is genuinely different from the T-pose frame.)
                Vector3 lSh = k_LeftUpperArm + 0.02f * RandomInBall(rng, 1f);
                Vector3 rSh = k_RightUpperArm + 0.02f * RandomInBall(rng, 1f);
                Vector3 neck = k_Neck + 0.02f * RandomInBall(rng, 1f);
                Vector3 chest = k_Chest + 0.02f * RandomInBall(rng, 1f);
                BasisSwivelFrame frameNow = BasisSwivelHintCore.BuildFrame(lSh, rSh, chest, neck);

                BasisSwivelHintCore.Features(frameNow, frameT, shoulder, hand, handRot, handTpose,
                                             k_ArmLen, isLeft, out float3 baseLocal, out float3x3 baseOrient);

                // Now rotate EVERYTHING -- calibration and runtime alike -- by the same world rotation.
                Quaternion q = RandomRotation(rng);
                BasisSwivelFrame frameT2 = BasisSwivelHintCore.BuildFrame(
                    q * k_LeftUpperArm, q * k_RightUpperArm, q * k_Chest, q * k_Neck);
                BasisSwivelFrame frameNow2 = BasisSwivelHintCore.BuildFrame(
                    q * lSh, q * rSh, q * chest, q * neck);

                BasisSwivelHintCore.Features(frameNow2, frameT2, q * shoulder, q * hand, q * handRot, q * handTpose,
                                             k_ArmLen, isLeft, out float3 rotLocal, out float3x3 rotOrient);

                AssertClose(baseLocal, rotLocal, $"tipLocal must not depend on the world yaw at calibration (iter {t})");
                AssertClose(baseOrient, rotOrient, $"tipOrient must not depend on the world yaw at calibration (iter {t})");
            }
        }

        /// <summary>
        /// The rig-independence the T-pose division is FOR: give the hand bone an arbitrary rest rotation (which
        /// is all a "bone's local axes" ever are -- a modelling convention), applied to both the rest pose and
        /// the live pose, and the features must not move. Without the division this is what makes a model fitted
        /// to CMU's skeleton useless on a real avatar.
        /// </summary>
        [Test]
        public void ArmFeatures_AreInvariant_ToTheHandBonesRestConvention()
        {
            var rng = new System.Random(99);
            BasisSwivelFrame frame = ArmFrame();

            for (int t = 0; t < 120; t++)
            {
                bool isLeft = (t & 1) == 0;
                Vector3 shoulder = isLeft ? k_LeftUpperArm : k_RightUpperArm;
                Vector3 hand = shoulder + RandomInBall(rng, 0.95f * k_ArmLen);

                // The PHYSICAL hand orientation: a world delta from rest. This is the thing the model must key on.
                Quaternion physical = RandomRotation(rng);

                // Rig A: hand bone rest = identity (the mocap skeleton).
                BasisSwivelHintCore.Features(frame, frame, shoulder, hand, physical, Quaternion.identity,
                                             k_ArmLen, isLeft, out float3 aLocal, out float3x3 aOrient);

                // Rig B: the SAME physical pose on a rig whose hand bone rest is some arbitrary convention.
                Quaternion rest = RandomRotation(rng);
                BasisSwivelHintCore.Features(frame, frame, shoulder, hand, physical * rest, rest,
                                             k_ArmLen, isLeft, out float3 bLocal, out float3x3 bOrient);

                AssertClose(aLocal, bLocal, $"tipLocal must not depend on the rig's hand-bone convention (iter {t})");
                AssertClose(aOrient, bOrient, $"tipOrient must not depend on the rig's hand-bone convention (iter {t})");
            }
        }

        /// <summary>
        /// The whole POINT of predicting an angle: the hint lands ON the reachable circle. It must be exactly
        /// perpendicular to the shoulder->hand axis, at EVERY extension including the near-straight arm where
        /// the old bend-vector path fell apart and handed the pole to a fallback (the reported snap).
        /// </summary>
        [Test]
        public void ArmHint_IsPerpendicularToTheLimbAxis_AtEveryExtension()
        {
            BasisSwivelFrame frame = ArmFrame();
            var rng = new System.Random(7);

            // Sweep extension right up to the singularity, where the circle has collapsed to a point.
            foreach (float ext in new[] { 0.10f, 0.50f, 0.90f, 0.95f, 0.98f, 0.999f, 0.99999f })
            {
                for (int t = 0; t < 40; t++)
                {
                    bool isLeft = (t & 1) == 0;
                    Vector3 shoulder = isLeft ? k_LeftUpperArm : k_RightUpperArm;
                    Vector3 dir = RandomInBall(rng, 1f).normalized;
                    Vector3 hand = shoulder + dir * (ext * k_ArmLen);

                    bool ok = BasisSwivelHintCore.ArmHint(frame, frame, shoulder, hand, RandomRotation(rng),
                                                          Quaternion.identity, k_ArmLen, isLeft,
                                                          out Vector3 hint, out float conf);
                    Assert.IsTrue(ok, $"the arm hint must be produced at extension {ext}");
                    Assert.IsTrue(float.IsFinite(conf), "confidence must be finite");

                    Vector3 axis = (hand - shoulder).normalized;
                    Vector3 bend = (hint - shoulder).normalized;
                    Assert.AreEqual(0f, Vector3.Dot(axis, bend), 1e-3f,
                        $"the hint must lie on the elbow's circle (perp to the limb axis) at extension {ext}");

                    // ...and it must be a real offset, not a degenerate zero-length "hint" at the shoulder.
                    Assert.AreEqual(0.5f * k_ArmLen, Vector3.Distance(hint, shoulder), 1e-3f,
                        "the hint must sit half an arm-length off the shoulder, as the solver expects");
                }
            }
        }

        /// <summary>
        /// The same, for the knee. This one matters more than it looks: the leg previously had NO hint model at
        /// all and fell back to a FIXED hips-right bend normal, which collapses exactly when the leg straightens
        /// -- and standing IS a straight leg.
        /// </summary>
        [Test]
        public void LegHint_IsPerpendicularToTheLimbAxis_AtEveryExtension()
        {
            BasisSwivelFrame frame = LegFrame();
            Assert.IsTrue(frame.Valid, "the T-posed reference rig must produce a valid leg frame");
            var rng = new System.Random(11);

            foreach (float ext in new[] { 0.30f, 0.70f, 0.95f, 0.99f, 0.999f, 0.99999f })
            {
                for (int t = 0; t < 40; t++)
                {
                    bool isLeft = (t & 1) == 0;
                    Vector3 hip = isLeft ? k_LeftUpperLeg : k_RightUpperLeg;
                    Vector3 dir = RandomInBall(rng, 1f).normalized;
                    Vector3 foot = hip + dir * (ext * k_LegLen);

                    bool ok = BasisSwivelHintCore.LegHint(frame, frame, hip, foot, RandomRotation(rng),
                                                          Quaternion.identity, k_LegLen, isLeft,
                                                          out Vector3 hint, out float conf);
                    Assert.IsTrue(ok, $"the leg hint must be produced at extension {ext}");
                    Assert.IsTrue(float.IsFinite(conf), "confidence must be finite");

                    Vector3 axis = (foot - hip).normalized;
                    Vector3 bend = (hint - hip).normalized;
                    Assert.AreEqual(0f, Vector3.Dot(axis, bend), 1e-3f,
                        $"the hint must lie on the knee's circle (perp to the limb axis) at extension {ext}");
                }
            }
        }

        /// <summary>
        /// THE MIRROR, pinned on the quantity where it is actually true.
        ///
        /// "+x is OUTWARD for both limbs, so one model serves both" is a claim with teeth: reflect the pose
        /// through the sagittal plane onto the other arm and the model must see THE SAME tipLocal -- not a
        /// mirrored one, an IDENTICAL one. Get this wrong and the left arm's elbow goes somewhere confident and
        /// absurd while the right arm looks perfect, which is precisely the class of bug a right-handed developer
        /// testing by hand will never catch.
        ///
        /// ⚠ NOTE WHAT IS *NOT* ASSERTED HERE, DELIBERATELY. The full HINT does not mirror, and it is not
        /// supposed to. The orientation feature is built as B^T*R -- the hand's axes written in the (mirrored)
        /// body frame -- so the left side gets M*X where a true reflection would be M*X*M. That asymmetry is
        /// baked into the FIT: both sides went into one pooled regression on exactly these features, and the
        /// model learned them as they are. Asserting hint-mirror-symmetry here would be asserting a property the
        /// shipped model does not have, and "fixing" the frame to make it true would silently invalidate every
        /// coefficient in BasisArmSwivelModel. Never re-frame what you did not re-fit.
        /// </summary>
        [Test]
        public void ThePositionFeatures_AreIdenticalAcrossTheMirror_SoOneModelServesBothLimbs()
        {
            BasisSwivelFrame frame = ArmFrame();
            var rng = new System.Random(1234);

            for (int t = 0; t < 150; t++)
            {
                Vector3 offset = RandomInBall(rng, 0.9f * k_ArmLen);

                BasisSwivelHintCore.Features(frame, frame, k_RightUpperArm, k_RightUpperArm + offset,
                                             Quaternion.identity, Quaternion.identity, k_ArmLen, false,
                                             out float3 rLocal, out _);

                // The same reach, reflected onto the left arm.
                BasisSwivelHintCore.Features(frame, frame, k_LeftUpperArm, k_LeftUpperArm + MirrorX(offset),
                                             Quaternion.identity, Quaternion.identity, k_ArmLen, true,
                                             out float3 lLocal, out _);

                AssertClose(rLocal, lLocal,
                    $"a mirrored reach must produce an IDENTICAL tipLocal -- that is what makes one model serve both arms (iter {t})");
            }
        }

        /// <summary>
        /// A degenerate rig (collapsed bones) must DECLINE, not answer. The caller then leaves the limb on the
        /// two-bone core's own fallback pole, which is what it did before any of this existed. Answering with a
        /// garbage frame would be strictly worse than not answering.
        /// </summary>
        [Test]
        public void ADegenerateRig_ProducesNoFrameAndNoHint()
        {
            // Shoulders coincident: there is no shoulder line, so there is no body right.
            BasisSwivelFrame collapsed = BasisSwivelHintCore.BuildFrame(Vector3.zero, Vector3.zero, k_Chest, k_Neck);
            Assert.IsFalse(collapsed.Valid, "coincident shoulders cannot define a body frame");

            // Chest and neck coincident: no body up.
            BasisSwivelFrame noUp = BasisSwivelHintCore.BuildFrame(k_LeftUpperArm, k_RightUpperArm, k_Chest, k_Chest);
            Assert.IsFalse(noUp.Valid, "a zero-length spine cannot define a body frame");

            Assert.IsFalse(BasisSwivelHintCore.ArmHint(collapsed, ArmFrame(), k_RightUpperArm, Vector3.zero,
                                                       Quaternion.identity, Quaternion.identity, k_ArmLen, false,
                                                       out _, out _),
                           "no live frame => no hint");
            Assert.IsFalse(BasisSwivelHintCore.ArmHint(ArmFrame(), collapsed, k_RightUpperArm, Vector3.zero,
                                                       Quaternion.identity, Quaternion.identity, k_ArmLen, false,
                                                       out _, out _),
                           "no T-pose frame => no hint");
            Assert.IsFalse(BasisSwivelHintCore.ArmHint(ArmFrame(), ArmFrame(), k_RightUpperArm, Vector3.zero,
                                                       Quaternion.identity, Quaternion.identity, 0f, false,
                                                       out _, out _),
                           "a zero-length limb => no hint");
        }

        /// <summary>
        /// A NaN hand target must be REFUSED, not solved on.
        ///
        /// This is not hypothetical. A single NaN MediaPipe landmark used to walk through every guard in the
        /// tracking path (because every `x &lt; limit` comparison is FALSE for NaN), latch into the one-euro
        /// filters, reach the Burst IK job, and become `(int)NaN` == int.MinValue inside the old lookup's
        /// trilinear sampler -- aborting the process with no managed stack. There is no int cast on this path,
        /// but a NaN hint still poisons the solve, so it is stopped at the door.
        /// </summary>
        [Test]
        public void ANaNTarget_IsRefused_RatherThanSolvedOn()
        {
            BasisSwivelFrame frame = ArmFrame();
            var nan = new Vector3(float.NaN, 0f, 0f);

            Assert.IsFalse(BasisSwivelHintCore.ArmHint(frame, frame, k_RightUpperArm, nan, Quaternion.identity,
                                                       Quaternion.identity, k_ArmLen, false, out _, out _),
                           "a NaN hand target must produce no hint");

            BasisSwivelFrame leg = LegFrame();
            Assert.IsFalse(BasisSwivelHintCore.LegHint(leg, leg, k_RightUpperLeg, nan, Quaternion.identity,
                                                       Quaternion.identity, k_LegLen, false, out _, out _),
                           "a NaN foot target must produce no hint");

            // ...and a NaN frame cannot be built in the first place.
            Assert.IsFalse(BasisSwivelHintCore.BuildFrame(nan, k_RightUpperArm, k_Chest, k_Neck).Valid,
                           "a NaN bone position must not yield a 'valid' frame");
        }

        /// <summary>
        /// The elbow hangs DOWN. Reaching straight out to the side, a human's elbow sits BELOW the
        /// shoulder->hand line, not above it -- passing +up instead of -up as the swivel reference put the elbow
        /// above the shoulder and cost 34.98% error, so this is pinned rather than trusted.
        /// </summary>
        [Test]
        public void TheElbow_HangsBelowTheShoulder_OnALateralReach()
        {
            BasisSwivelFrame frame = ArmFrame();

            // Right arm reaching out to the right, half extended, hand in its rest orientation.
            Vector3 hand = k_RightUpperArm + new Vector3(0.5f * k_ArmLen, 0f, 0.2f * k_ArmLen);
            Assert.IsTrue(BasisSwivelHintCore.ArmHint(frame, frame, k_RightUpperArm, hand, Quaternion.identity,
                                                      Quaternion.identity, k_ArmLen, false,
                                                      out Vector3 hint, out _));

            Assert.Less(hint.y, k_RightUpperArm.y,
                "the derived elbow must hang BELOW the shoulder on a lateral reach, not wing up above it");
        }

        // -------------------------------------------------------------------------------------------------

        static Vector3 MirrorX(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        static Vector3 RandomInBall(System.Random rng, float radius)
        {
            for (int i = 0; i < 64; i++)
            {
                var v = new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0));
                if (v.sqrMagnitude > 1e-4f && v.sqrMagnitude <= 1f)
                {
                    return v * radius;
                }
            }
            return new Vector3(radius, 0f, 0f);
        }

        static Quaternion RandomRotation(System.Random rng)
        {
            // Shoemake's uniform random quaternion.
            double u1 = rng.NextDouble(), u2 = rng.NextDouble(), u3 = rng.NextDouble();
            double s1 = System.Math.Sqrt(1.0 - u1), s2 = System.Math.Sqrt(u1);
            return new Quaternion(
                (float)(s1 * System.Math.Sin(2.0 * System.Math.PI * u2)),
                (float)(s1 * System.Math.Cos(2.0 * System.Math.PI * u2)),
                (float)(s2 * System.Math.Sin(2.0 * System.Math.PI * u3)),
                (float)(s2 * System.Math.Cos(2.0 * System.Math.PI * u3)));
        }
    }
}
