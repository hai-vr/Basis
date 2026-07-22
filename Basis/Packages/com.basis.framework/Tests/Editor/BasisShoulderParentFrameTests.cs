using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// THE CLAVICLE'S PARENT IS THE UPPERCHEST, AND THE GIRDLE FRAME HAS TO BE THE PARENT'S.
    ///
    /// THE DEFECT: BasisFullBodyIK.SolveShoulder read HandleChest for the girdle frame. The clavicle's
    /// actual parent is the UpperChest, and the result is written with SetRotation -- which is
    /// SetWorldRotation, so the parent chain is DISCARDED and whatever frame the solve was built in is the
    /// frame the bone lands in. SolveSpine writes the UpperChest TWICE beforehand, independently of the
    /// Chest (DistributeSpineBend's pitch/roll/routed-twist, and ApplyArmSwingChestFollow), so at runtime
    /// the UpperChest is genuinely somewhere the Chest does not know about: up to 0.75x the torso twist,
    /// about 30 deg on a 40 deg rotation. Every one of those degrees landed on the clavicle as error.
    /// Both the live read and the TposeChestRot bake now take Mapping.Upperchest, with a Chest fallback.
    ///
    /// WHAT IS GATED HERE, AND HOW. BasisShoulderSolveCore takes the girdle frame as two inputs -- ChestRot
    /// (live) and TposeChestRot (bind) -- so at this level the fix is "which frame gets passed in", and the
    /// core cannot be asked which bone that was. What CAN be pinned, exactly and without the job, is the
    /// consequence, and it is the whole reason the frame matters:
    ///
    ///     THE GIRDLE ROTATION THE CORE REPORTS IS THE ROTATION THE BONE ACTUALLY RECEIVES RELATIVE TO ITS
    ///     PARENT -- IF AND ONLY IF THE FRAME IT WAS GIVEN IS THE PARENT'S.
    ///
    /// The core's own contract is anatomical: AppliedAngleDeg is a girdle swing away from the authored
    /// bind, twist-stripped and clamped to MaxShoulderDeg. Measure the result the way the rig will actually
    /// see it -- inverse(live UpperChest) * ShoulderRotation, against inverse(bind UpperChest) *
    /// TposeShoulderRot -- and fed the UpperChest that measurement equals AppliedAngleDeg to floating
    /// point. Fed the Chest it does not: it is off by the UpperChest's independent motion, the clamp stops
    /// bounding anything, and the authored bind clavicle is not recovered even when the arm is at rest.
    ///
    /// NOT GATED HERE (it needs an Animator, a PlayableGraph and the constraint job, none of which are
    /// reachable from an EditMode unit test): that BasisFullBodyIK.SolveShoulder reads HandleUpperChest and
    /// that Create() bakes TposeChestRot from Mapping.Upperchest. These tests gate the CONSEQUENCE of that
    /// wiring, not the wiring itself. Nor is "unchanged when UpperChest is Chest" a discovery at this level
    /// -- the frame arrives as one input, so it is definitional; it is asserted below as a bit-identity
    /// regression on the fallback path rather than dressed up as more than it is.
    /// </summary>
    public class BasisShoulderParentFrameTests
    {
        const float ArmLen = 0.54f;
        const float CoupleRatio = 0.4f;      // k_ShoulderCoupleRatio, live
        const float MaxShoulderDeg = 25f;    // k_ShoulderMaxDeg, live

        // ---- the rig's bind, where the UpperChest is NOT coincident with the Chest ----------------------
        static readonly Quaternion ChestBind = Quaternion.identity;
        static readonly Quaternion UpperChestBind = Quaternion.Euler(12f, 0f, 0f);
        static Quaternion ShoulderBind(bool isLeft) => Quaternion.Euler(0f, 0f, isLeft ? 8f : -8f);

        // ---- the live torso: a 40 deg twist, of which the UpperChest carries an EXTRA 30 deg of its own --
        // This is the shape of the measured defect: SolveSpine routes up to 0.75x of the axial twist into
        // the UpperChest on top of whatever the Chest already did.
        const float TorsoTwistDeg = 40f;
        const float UpperChestExtraDeg = 30f;
        static readonly Quaternion ChestLive = Quaternion.Euler(0f, TorsoTwistDeg, 0f) * ChestBind;
        static readonly Quaternion UpperChestLive = Quaternion.Euler(0f, TorsoTwistDeg + UpperChestExtraDeg, 0f) * UpperChestBind;

        static Vector3 RestDirLocal(bool isLeft) => new Vector3(isLeft ? -1f : 1f, -0.05f, 0.05f).normalized;

        /// <summary>The bind arm direction in WORLD space, which is what the core is handed. It is anchored
        /// to the UPPERCHEST's bind, because that is the bone the clavicle hangs off.</summary>
        static Vector3 BindArmDirWorld(bool isLeft) => UpperChestBind * RestDirLocal(isLeft);

        static Vector3 DirFromAzEl(float azDeg, float elDeg, bool isLeft)
        {
            float er = elDeg * Mathf.Deg2Rad, ar = azDeg * Mathf.Deg2Rad;
            float ch = Mathf.Cos(er);
            float x = Mathf.Cos(ar) * ch;
            float y = Mathf.Sin(er);
            float z = Mathf.Sin(ar) * ch;
            if (isLeft) x = -x;
            return new Vector3(x, y, z).normalized;
        }

        static readonly Vector2[] SampleAzEl =
        {
            new Vector2(0f, 0f), new Vector2(0f, 45f), new Vector2(0f, 80f), new Vector2(45f, 30f),
            new Vector2(90f, 10f), new Vector2(90f, 60f), new Vector2(130f, 20f), new Vector2(-50f, -25f),
            new Vector2(30f, -60f), new Vector2(150f, 35f),
        };

        /// <summary>Solve the girdle for a fixed WORLD arm direction against a chosen frame pair. The arm
        /// geometry is identical whichever frame is passed -- it comes from trackers and IK targets in
        /// world space -- so the frame is the only variable.</summary>
        static BasisShoulderSolveResult Solve(Vector3 worldArmDir, Quaternion liveFrame, Quaternion bindFrame, bool isLeft)
        {
            BasisShoulderSolveInput i = default;
            i.ShoulderPos = Vector3.zero;
            i.ElbowPos = worldArmDir.normalized * (0.95f * ArmLen);
            i.HandTargetPos = i.ElbowPos;
            i.HasElbow = true;
            i.HasShoulderTracker = false;
            i.ChestRot = liveFrame;
            i.TposeChestRot = bindFrame;
            i.TposeShoulderRot = ShoulderBind(isLeft);
            i.TposeArmDirWorld = BindArmDirWorld(isLeft);
            i.TposeArmLength = ArmLen;
            i.ElevationFactor = 1f;      // the user sliders wide open, so the girdle is large enough that
            i.ProtractionFactor = 1f;    // "equals AppliedAngleDeg" is a real comparison and not 0 == 0
            i.CoupleRatio = CoupleRatio;
            i.MaxShoulderDeg = MaxShoulderDeg;
            i.TrackerFinal = Quaternion.identity;
            i.IsLeft = isLeft;

            BasisShoulderSolveCore.Solve(i, out var r);
            return r;
        }

        /// <summary>How far the solved clavicle sits from its AUTHORED bind, measured the way the rig will
        /// see it: in the parent's frame. This is the quantity the girdle solve believes it is producing.</summary>
        static float GirdleInParentFrameDeg(Quaternion solvedWorld, Quaternion parentLive, Quaternion parentBind, bool isLeft)
        {
            Quaternion local = Quaternion.Inverse(parentLive) * solvedWorld;
            Quaternion bindLocal = Quaternion.Inverse(parentBind) * ShoulderBind(isLeft);
            return Quaternion.Angle(local, bindLocal);
        }

        /// <summary>The world arm direction for a chest-local sample, carried by the bone the arm actually
        /// hangs off -- the UpperChest. (The arm rides the twisted torso; that is what makes the Chest's
        /// disagreement with it show up as clavicle error rather than as a different pose.)</summary>
        static Vector3 WorldDir(Vector2 azel, bool isLeft) => UpperChestLive * DirFromAzEl(azel.x, azel.y, isLeft);

        static Vector3 WorldRestDir(bool isLeft) => UpperChestLive * RestDirLocal(isLeft);

        // ==================================================================================

        /// <summary>
        /// THE HEADLINE. With the UpperChest carrying 30 deg the Chest knows nothing about, only the
        /// UpperChest frame puts the clavicle where the solve says it put it. Fed the Chest, the reported
        /// girdle angle and the rotation the bone actually receives are different numbers.
        /// </summary>
        [Test]
        public void Clavicle_LandsWhereTheSolveSaysOnly_WhenGivenTheUpperChestFrame()
        {
            float worstUpper = 0f, worstChest = 0f, biggestGirdle = 0f;
            string worstChestAt = "";

            foreach (bool isLeft in new[] { false, true })
            {
                foreach (Vector2 azel in SampleAzEl)
                {
                    Vector3 dir = WorldDir(azel, isLeft);

                    var upper = Solve(dir, UpperChestLive, UpperChestBind, isLeft);
                    var chest = Solve(dir, ChestLive, ChestBind, isLeft);
                    Assert.That(upper.Apply && chest.Apply, Is.True, "the solve declined; nothing to compare.");

                    // The clavicle is parented to the UpperChest in BOTH cases -- that is a fact about the
                    // rig, not about which frame the solver was handed. So both results are measured there.
                    float upperErr = Mathf.Abs(GirdleInParentFrameDeg(upper.ShoulderRotation, UpperChestLive, UpperChestBind, isLeft)
                                               - upper.AppliedAngleDeg);
                    float chestErr = Mathf.Abs(GirdleInParentFrameDeg(chest.ShoulderRotation, UpperChestLive, UpperChestBind, isLeft)
                                               - chest.AppliedAngleDeg);

                    worstUpper = Mathf.Max(worstUpper, upperErr);
                    if (chestErr > worstChest)
                    {
                        worstChest = chestErr;
                        worstChestAt = $"{(isLeft ? "L" : "R")} az={azel.x:0} el={azel.y:0}";
                    }
                    biggestGirdle = Mathf.Max(biggestGirdle, upper.AppliedAngleDeg);
                }
            }

            Assert.That(biggestGirdle, Is.GreaterThan(8f),
                $"the girdle never moved more than {biggestGirdle:0.0} deg across the sample set; the comparison below would be 0 == 0.");
            Assert.That(worstUpper, Is.LessThan(0.1f),
                $"given the UPPERCHEST frame the clavicle still landed {worstUpper:0.000} deg away from the girdle rotation the core reported. The solve and the bone must agree exactly.");
            Assert.That(worstChest, Is.GreaterThan(20f),
                $"given the CHEST frame the clavicle only missed the reported girdle by {worstChest:0.0} deg. The defect this test was written for is no longer reproducible, so the test is not proving the fix.");

            TestContext.WriteLine($"parent-frame error vs reported girdle: UpperChest {worstUpper:0.000} deg, Chest {worstChest:0.0} deg (worst at {worstChestAt}); largest girdle {biggestGirdle:0.0} deg.");
        }

        /// <summary>
        /// The clamp is a promise about the CLAVICLE, and a promise about the clavicle is a promise in its
        /// parent's frame. Fed the Chest, MaxShoulderDeg stops bounding the bone at all.
        /// </summary>
        [Test]
        public void GirdleClamp_BoundsTheBone_OnlyInTheUpperChestFrame()
        {
            float worstUpper = 0f, worstChest = 0f;

            foreach (bool isLeft in new[] { false, true })
            {
                foreach (Vector2 azel in SampleAzEl)
                {
                    Vector3 dir = WorldDir(azel, isLeft);
                    var upper = Solve(dir, UpperChestLive, UpperChestBind, isLeft);
                    var chest = Solve(dir, ChestLive, ChestBind, isLeft);

                    worstUpper = Mathf.Max(worstUpper, GirdleInParentFrameDeg(upper.ShoulderRotation, UpperChestLive, UpperChestBind, isLeft));
                    worstChest = Mathf.Max(worstChest, GirdleInParentFrameDeg(chest.ShoulderRotation, UpperChestLive, UpperChestBind, isLeft));
                }
            }

            Assert.That(worstUpper, Is.LessThan(MaxShoulderDeg + 0.1f),
                $"the clavicle rotated {worstUpper:0.0} deg from its bind in its parent's frame, past the {MaxShoulderDeg:0} deg clamp, even with the UpperChest frame.");
            Assert.That(worstChest, Is.GreaterThan(MaxShoulderDeg + 2f),
                $"the Chest frame kept the clavicle within {worstChest:0.0} deg of its bind; the clamp escape this test exists to catch is not being reproduced.");

            TestContext.WriteLine($"clavicle-vs-bind in the parent frame: UpperChest worst {worstUpper:0.0} deg (clamp {MaxShoulderDeg:0}), Chest worst {worstChest:0.0} deg.");
        }

        /// <summary>
        /// AT REST, THE AUTHORED CLAVICLE MUST COME BACK EXACTLY. Nobody has moved: the torso is at bind and
        /// the arm is at its bind direction, so the girdle has nothing to contribute and the bone belongs
        /// exactly where the artist put it. Given the UpperChest it does. Given the Chest -- whose bind
        /// differs from the UpperChest's by the rig's own spine tilt -- the clavicle is bent off the
        /// authored pose while the avatar stands perfectly still.
        ///
        /// This is also the case that makes the PAIRING load-bearing: reading the live rotation from one
        /// bone and the bind from another turns the girdle frame into a since-bind delta plus a constant
        /// offset, and the constant offset is visible with the body at rest.
        /// </summary>
        [Test]
        public void AtRest_TheAuthoredClavicleIsRecovered_OnlyWithAMatchedBoneAndBind()
        {
            foreach (bool isLeft in new[] { false, true })
            {
                Vector3 restDir = UpperChestBind * RestDirLocal(isLeft);   // everything at bind: nobody has moved

                var matched = Solve(restDir, UpperChestBind, UpperChestBind, isLeft);
                var mismatched = Solve(restDir, UpperChestBind, ChestBind, isLeft);   // live UpperChest, bind Chest
                var chestOnly = Solve(restDir, ChestBind, ChestBind, isLeft);

                float matchedErr = Quaternion.Angle(matched.ShoulderRotation, ShoulderBind(isLeft));
                float mismatchedErr = Quaternion.Angle(mismatched.ShoulderRotation, ShoulderBind(isLeft));
                float chestOnlyErr = Quaternion.Angle(chestOnly.ShoulderRotation, ShoulderBind(isLeft));

                Assert.That(matchedErr, Is.LessThan(0.5f),
                    $"{(isLeft ? "L" : "R")}: with the bone and its bind matched, a body at rest still moved the clavicle {matchedErr:0.00} deg off the authored bind.");
                Assert.That(mismatchedErr, Is.GreaterThan(8f),
                    $"{(isLeft ? "L" : "R")}: a mismatched live-bone/bind-bone pair only cost {mismatchedErr:0.00} deg at rest; this test is not reproducing the constant-offset failure it describes.");

                TestContext.WriteLine($"{(isLeft ? "L" : "R")} at rest: matched {matchedErr:0.00} deg, live-UpperChest/bind-Chest {mismatchedErr:0.00} deg, Chest-only {chestOnlyErr:0.00} deg off the authored clavicle.");
            }
        }

        /// <summary>
        /// THE FALLBACK. A rig with no UpperChest bone falls back to the Chest for BOTH the live read and
        /// the bake, and that pairing must reproduce the pre-fix result bit for bit -- the fix may not
        /// change what such a rig does. (At this level the frame is a single input pair, so this is a
        /// regression fence on the fallback path rather than a discovery; see the class doc.)
        /// </summary>
        [Test]
        public void WhenUpperChestCoincidesWithChest_TheResultIsBitIdentical()
        {
            // A rig whose UpperChest is coincident with the Chest -- either because it tracks it exactly, or
            // because there is no UpperChest bone at all and the fallback supplied the Chest.
            Quaternion coincidentLive = ChestLive;
            Quaternion coincidentBind = ChestBind;

            foreach (bool isLeft in new[] { false, true })
            {
                foreach (Vector2 azel in SampleAzEl)
                {
                    Vector3 dir = ChestLive * DirFromAzEl(azel.x, azel.y, isLeft);

                    var viaUpperChest = Solve(dir, coincidentLive, coincidentBind, isLeft);
                    var viaFallback = Solve(dir, ChestLive, ChestBind, isLeft);

                    Assert.That(viaUpperChest.Apply, Is.EqualTo(viaFallback.Apply));
                    Quaternion a = viaUpperChest.ShoulderRotation, b = viaFallback.ShoulderRotation;
                    Assert.IsTrue(a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w,
                        $"{(isLeft ? "L" : "R")} az={azel.x:0} el={azel.y:0}: the coincident-frame result is not bit-identical ({Quaternion.Angle(a, b):0.000000} deg apart).");
                    Assert.That(viaUpperChest.AppliedAngleDeg, Is.EqualTo(viaFallback.AppliedAngleDeg));
                }
            }
        }

        /// <summary>
        /// The girdle solve is FRAME-RELATIVE: rotate the parent and the arm together and the clavicle
        /// follows rigidly, contributing nothing of its own. This is why feeding the wrong parent shows up
        /// as error one-for-one with that parent's independent motion, and it is what makes the headline
        /// number above (30 deg of UpperChest motion, ~30 deg of clavicle error) a prediction rather than
        /// an observation.
        /// </summary>
        [Test]
        public void GirdleIsRigidInItsParentFrame()
        {
            Quaternion extra = Quaternion.Euler(-9f, 47f, 13f);
            float worst = 0f;

            foreach (bool isLeft in new[] { false, true })
            {
                foreach (Vector2 azel in SampleAzEl)
                {
                    Vector3 dir = WorldDir(azel, isLeft);
                    var baseline = Solve(dir, UpperChestLive, UpperChestBind, isLeft);
                    var rotated = Solve(extra * dir, extra * UpperChestLive, UpperChestBind, isLeft);

                    worst = Mathf.Max(worst, Quaternion.Angle(extra * baseline.ShoulderRotation, rotated.ShoulderRotation));
                }
            }

            Assert.That(worst, Is.LessThan(0.1f),
                $"rotating the parent and the arm together moved the clavicle {worst:0.000} deg relative to the parent; the girdle solve is not frame-relative, so the frame it is given cannot be reasoned about the way these tests do.");
        }
    }
}
