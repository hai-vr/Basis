using System.Collections.Generic;
using System.IO;
using System.Text;
using Basis.IK;
using Basis.IK.Mocap;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Tests.IK
{
    /// <summary>
    /// ⭐ THE AVATAR IS NOT THE USER. Accuracy when the body driving the solver is not the body that
    /// produced the targets.
    ///
    /// ================================================================================================
    /// THE GAP THIS CLOSES
    ///
    /// Every accuracy number this project quotes -- BasisMocapAccuracyTests, BasisDynamicCorpusTests,
    /// BasisSpineCorpusAccuracyTests -- was measured with a CMU subject driving THEIR OWN proportions. The
    /// hand target and the arm it hangs off came from the same skeleton, so |shoulder->hand| / armLen &lt;= 1
    /// on 100% of frames, by the triangle inequality, always. A real VR user is NEVER in that situation:
    /// the controller reports where the USER's hand is, and the avatar's arm is whatever length the avatar
    /// happens to have.
    ///
    /// That is not a small perturbation on top of the measured error. It is a DIFFERENT INPUT DISTRIBUTION,
    /// and it has already shipped a bug. From the project's own post-mortem, verbatim:
    ///
    ///     "THE HARNESS'S INPUTS WERE NOT THE RUNTIME'S INPUTS... The mocap corpus only ever contains a
    ///      hand ON the arm, so |tipLocal| &lt;= 1 in 100% of training data. The live rig is handed the RAW
    ///      CONTROLLER TARGET, which sails past the avatar's reach constantly -- anyone whose real arms are
    ///      longer than their avatar's is outside the fit box permanently. A cubic with coefficients up to
    ///      15 out there is not 'approximate', it is a random number generator."
    ///
    /// The fix was a hard domain clamp -- saturate, never extrapolate (BasisElbowFieldModel.Elbow,
    /// BasisArmElbowNeuralFieldModel.Elbow, BasisLegSwivelModel.SwivelRad all open with
    /// `t = len > 1 ? tipLocal / len : tipLocal`). BasisSwivelHintConformanceTests pins that clamp on a
    /// SYNTHETIC T-posed rig with random directions. Nothing pinned it against realistic mismatch on real
    /// motion, and nothing anywhere measured HOW OFTEN a mismatched user actually leaves the box, or what
    /// the accuracy costs as the mismatch grows. Those three things are this file.
    ///
    /// ================================================================================================
    /// HOW THIS DIFFERS FROM BasisBodyProportionTests -- THEY ARE COMPLEMENTARY, NOT OVERLAPPING
    ///
    ///   BasisBodyProportionTests asks: does the solver behave the SAME on a differently-proportioned body?
    ///   It is an INVARIANCE suite. Its headline, Solve_IsScaleInvariant_FromChibiToGiant, scales the body
    ///   AND the pose together and demands identical angles -- uniform scale must be a NO-OP. Its bodies
    ///   are synthetic profiles built from height fractions, its poses are synthetic landmarks, and in
    ///   every single case the target belongs to the SAME body as the limb solving for it. It never
    ///   produces the mismatch condition at all.
    ///
    ///   This file asks the opposite question: what happens when the scale is NOT shared? The avatar is one
    ///   size, the effector targets come from a human of another size, and there is no invariance to appeal
    ///   to -- the reach ratio genuinely changes, frames genuinely leave the fitted domain, and the error
    ///   genuinely grows. Uniform scale is a no-op; MISMATCH is not, and that is the whole point.
    ///
    ///   The one place they touch is Arm_BeyondReachInputs_FallShortCleanly_AcrossBodyProportions, which
    ///   holds the same shortfall property this file re-checks -- on synthetic landmark poses that happen
    ///   to overshoot. This file drives it from 13,785 frames of real human motion instead, and it is
    ///   reused here (same 0.005 limb-length bound) rather than re-derived.
    ///
    /// ================================================================================================
    /// GUARDING THE RULER
    ///
    /// At 1.0x the rescaled clip must be BIT-IDENTICAL to the original and must reproduce every existing
    /// accuracy number exactly. If it does not, the rescale is corrupting the motion and every mismatch
    /// number below is fiction. That guard is mandatory here because this project has shipped several
    /// metrics that were quietly lying -- a pop detector whose count was identical whether its supposed
    /// cause was present or absent, and a smoothness metric that scored over-smoothed mush BETTER than a
    /// real human. So: an identity guard that can fail (BasisClipProportion does not short-circuit at 1.0),
    /// a structural guard the identity guard provably cannot catch (a wrong parent/group table), an
    /// anatomical guard (BasisBvhLoader.Validate on every rescaled clip), and a POSITIVE CONTROL on the
    /// out-of-domain counter itself -- it must be non-zero where the cause is present and it must move
    /// monotonically with the cause.
    ///
    /// ⚠ UNMEASURED. At the time of writing this file has never been run: the Unity suite is executed
    /// centrally and the numbers below do not exist yet. Every bound asserted here is therefore either
    /// exact (bit equality, counts, monotonicity), reused from a test that WAS calibrated
    /// (BasisBodyProportionTests' 0.005 limb-lengths), or explicitly labelled a DIVERGENCE DETECTOR -- a
    /// bound with orders of magnitude of daylight on both sides of it, chosen because there is nothing in
    /// between. The sweep table is report-only. The house doctrine is no honest threshold until the number
    /// exists, and inventing measured-sounding headroom would be worse than saying it is not measured.
    ///
    /// Corpus: the ROOT tier only, via a NON-RECURSIVE Directory.GetFiles. posture/ (44), dynamic/ (29) and
    /// slow/ (16) are deliberately isolated so adding clips to one tier cannot re-base another tier's
    /// numbers, and this file's numbers are quoted against the same 20 clips the accuracy baseline uses.
    /// </summary>
    public class BasisProportionMismatchTests
    {
        static string CorpusDir => Path.GetFullPath("Packages/com.basis.framework/Tests/MocapCorpus~");

        /// <summary>
        /// The mismatch sweep. Arm 0.8x-1.3x and leg 0.85x-1.2x span what avatars actually do to people:
        /// 0.8 is a stylised short-armed character worn by an adult (the case the shipped bug was about --
        /// the user's hand goes where the avatar's arm cannot follow, permanently), 1.3 is a long-limbed
        /// character worn by someone smaller.
        ///
        /// ONE GROUP AT A TIME, never a cross product. A cell with both arm and leg mismatched confounds
        /// two causes, and the per-joint degradation is exactly what we are trying to attribute.
        /// </summary>
        static readonly float[] k_ArmScales = { 0.80f, 0.90f, 1.00f, 1.10f, 1.20f, 1.30f };
        static readonly float[] k_LegScales = { 0.85f, 0.90f, 1.00f, 1.10f, 1.20f };

        /// <summary>The short-avatar corner, used as the driver for the domain-clamp guard because it is
        /// the corner that actually produces out-of-domain frames.</summary>
        static readonly BasisProportionScale k_ShortAvatar = BasisProportionScale.Of(0.80f, 0.85f, 1f);

        // ------------------------------------------------------------------ corpus

        static List<BasisMotionClip> RequireCorpus()
        {
            var clips = new List<BasisMotionClip>();
            if (!Directory.Exists(CorpusDir)) { Assert.Ignore($"no mocap corpus: {CorpusDir}"); return clips; }

            // NON-RECURSIVE, and that is load-bearing: the tiers under MocapCorpus~/ are isolated on purpose
            // so a clip added to posture/ or dynamic/ cannot silently re-base the root tier's numbers.
            string[] files = Directory.GetFiles(CorpusDir, "*.bvh");
            System.Array.Sort(files);
            foreach (string f in files)
            {
                Assert.That(BasisBvhLoader.TryLoad(f, out BasisMotionClip clip, out string err), Is.True,
                    $"failed to load {Path.GetFileName(f)}: {err}");
                clips.Add(clip);
            }
            if (clips.Count == 0) Assert.Ignore($"no .bvh in {CorpusDir}");
            return clips;
        }

        // ------------------------------------------------------------------ the ruler guards

        /// <summary>
        /// GUARDS: BasisClipProportion's single forward rebuild pass, which reads delta[parent] before
        /// writing delta[child] and would therefore assemble the skeleton against stale displacements if
        /// BasisMocapJoint were ever reordered. The result would be plausible-looking motion on a wrong
        /// body -- the failure mode that survives a green suite.
        /// HEADROOM: none needed, this is a topological fact and it is either true or false.
        /// </summary>
        [Test]
        public void TheJointHierarchy_ListsEveryParentBeforeItsChild()
        {
            Assert.That(BasisClipProportion.HierarchyIsTopological(), Is.True,
                "BasisMocapJoint has been reordered so that some joint's parent sits AFTER it. " +
                "BasisClipProportion.Rescale is a single forward pass and would rebuild that joint from a " +
                "stale parent displacement.");

            // Spot-check the two chains the whole measurement depends on, so a plausible-but-wrong table
            // (e.g. the clavicle bound into the arm group) fails HERE rather than as a mystery in the sweep.
            Assert.That(BasisClipProportion.GroupOf(BasisMocapJoint.LeftLowerArm), Is.EqualTo(BasisSegmentGroup.Arm));
            Assert.That(BasisClipProportion.GroupOf(BasisMocapJoint.LeftHand), Is.EqualTo(BasisSegmentGroup.Arm));
            Assert.That(BasisClipProportion.GroupOf(BasisMocapJoint.LeftUpperArm), Is.EqualTo(BasisSegmentGroup.Torso),
                "the clavicle places the SHOULDER JOINT -- it is torso width, not arm length. Scaling it with " +
                "the arm would move the shoulder and confound 'short arms' with 'narrow shoulders'.");
            Assert.That(BasisClipProportion.GroupOf(BasisMocapJoint.LeftLowerLeg), Is.EqualTo(BasisSegmentGroup.Leg));
            Assert.That(BasisClipProportion.GroupOf(BasisMocapJoint.LeftFoot), Is.EqualTo(BasisSegmentGroup.Leg));
            Assert.That(BasisClipProportion.GroupOf(BasisMocapJoint.LeftUpperLeg), Is.EqualTo(BasisSegmentGroup.Torso),
                "hips->upper leg is pelvis half-width, not leg length.");
        }

        /// <summary>
        /// ⭐ THE RULER GUARD, PART 1: at 1.0x the rescale must be a bit-exact identity on the POSE.
        ///
        /// Not by short-circuiting. BasisClipProportion deliberately has no `if (IsIdentity) return copy;`
        /// -- the full rebuild runs, and it comes out exact because it accumulates a DISPLACEMENT scaled by
        /// (scale - 1f), which is exactly 0f at unity, rather than reassembling positions from the parent
        /// (a + (b - a) != b in floating point). So this test can actually fail, which is the entire reason
        /// it is worth writing: an identity test that a `return copy;` makes true guards nothing.
        ///
        /// GUARDS: any FP drift, renormalisation, re-grounding or rotation touch introduced by the rebuild.
        /// If this fails, every mismatch number in this file is fiction.
        /// HEADROOM: zero by construction -- exact float equality on every component of every pose of every
        /// frame of all 20 clips (~13,785 frames x 22 joints x 7 floats). The one accepted looseness is
        /// that -0.0f + 0.0f = +0.0f, which `==` treats as equal and which cannot change any measurement.
        /// </summary>
        [Test]
        public void RescalingAtUnity_ReturnsTheOriginalPose_BitForBit()
        {
            int joints = (int)BasisMocapJoint.Count;
            int compared = 0;

            foreach (BasisMotionClip src in RequireCorpus())
            {
                BasisMotionClip same = BasisClipProportion.Rescale(src, BasisProportionScale.Identity);

                Assert.That(same.FrameCount, Is.EqualTo(src.FrameCount), $"{src.Name}: frame count changed");
                Assert.That(same.FrameTime, Is.EqualTo(src.FrameTime), $"{src.Name}: frame time changed");
                Assert.That(same.SourceToMetres, Is.EqualTo(src.SourceToMetres), $"{src.Name}: SourceToMetres changed");
                Assert.That(same.Poses.Length, Is.EqualTo(src.Poses.Length), $"{src.Name}: pose buffer resized");

                for (int f = 0; f < src.FrameCount; f++)
                {
                    for (int j = 0; j < joints; j++)
                    {
                        var jn = (BasisMocapJoint)j;
                        BasisMocapPose a = src.Get(f, jn);
                        BasisMocapPose b = same.Get(f, jn);

                        if (!ExactlyEqual(a.Position, b.Position) || !ExactlyEqual(a.Rotation, b.Rotation) || a.Valid != b.Valid)
                        {
                            // Assert INSIDE the branch: 300k passing Assert calls per clip is minutes of NUnit
                            // bookkeeping, and the failure message wants the exact frame and joint anyway.
                            Assert.Fail($"{src.Name} frame {f} joint {jn}: the 1.0x rescale is not an identity. " +
                                        $"pos {a.Position:F9} -> {b.Position:F9}, rot {a.Rotation:F9} -> {b.Rotation:F9}, " +
                                        $"valid {a.Valid} -> {b.Valid}. The rescale is corrupting the motion, so every " +
                                        "mismatch number measured with it is measuring the corruption.");
                        }
                        compared++;
                    }
                }
            }

            Assert.That(compared, Is.GreaterThan(100000),
                $"only {compared} poses compared -- the corpus did not load, so this guard proved nothing");
        }

        /// <summary>
        /// ⭐ THE RULER GUARD, PART 2: the 1.0x rescale must reproduce the EXISTING accuracy numbers exactly,
        /// through the real harness, on the mode that ships (ElbowField) and the mode whose numbers are
        /// quoted throughout this project (Lookup).
        ///
        /// Part 1 proves the poses are identical. This proves nothing downstream of them cares either --
        /// same seeding, same One-Euro state, same pops, same percentiles. Every numeric field of
        /// BasisMocapAccuracySummary is enumerated by hand on purpose: a helper that compared "the summary"
        /// would silently stop covering any field added later, which is the same class of quiet lie this
        /// file exists to prevent.
        ///
        /// Clip and Path are excluded and that is deliberate, not an oversight: BasisClipProportion always
        /// suffixes the name (no identity special-case, see above) and both runs are handed a null CSV path.
        /// HEADROOM: zero -- exact equality. BasisMocapAccuracy.Run is deterministic (no RNG, no time, and
        /// the ElbowField path does not touch the static swivel-dump accumulators).
        /// </summary>
        [Test]
        public void RescalingAtUnity_ReproducesEveryBaselineAccuracyNumber_Exactly()
        {
            var failures = new List<string>();

            foreach (BasisMotionClip src in RequireCorpus())
            {
                BasisMotionClip same = BasisClipProportion.Rescale(src, BasisProportionScale.Identity);

                foreach (BasisMocapHintSource hint in new[]
                {
                    BasisMocapHintSource.ElbowField,   // WHAT SHIPS for an untracked arm
                    BasisMocapHintSource.Lookup,       // the baseline quoted all over this project
                })
                {
                    BasisMocapAccuracySummary a = BasisMocapAccuracy.Run(src, hint, null);
                    BasisMocapAccuracySummary b = BasisMocapAccuracy.Run(same, hint, null);

                    Assert.That(a.Ok, Is.True, $"{src.Name} [{hint}] baseline run failed: {a.Error}");
                    Assert.That(b.Ok, Is.True, $"{src.Name} [{hint}] rescaled run failed: {b.Error}");

                    Field(failures, src.Name, hint, "Frames", a.Frames, b.Frames);
                    Field(failures, src.Name, hint, "ElbowMeanM", a.ElbowMeanM, b.ElbowMeanM);
                    Field(failures, src.Name, hint, "ElbowP95M", a.ElbowP95M, b.ElbowP95M);
                    Field(failures, src.Name, hint, "ElbowMaxM", a.ElbowMaxM, b.ElbowMaxM);
                    Field(failures, src.Name, hint, "KneeMeanM", a.KneeMeanM, b.KneeMeanM);
                    Field(failures, src.Name, hint, "KneeP95M", a.KneeP95M, b.KneeP95M);
                    Field(failures, src.Name, hint, "KneeMaxM", a.KneeMaxM, b.KneeMaxM);
                    Field(failures, src.Name, hint, "ElbowMeanFracArm", a.ElbowMeanFracArm, b.ElbowMeanFracArm);
                    Field(failures, src.Name, hint, "KneeMeanFracLeg", a.KneeMeanFracLeg, b.KneeMeanFracLeg);
                    Field(failures, src.Name, hint, "HandMaxM", a.HandMaxM, b.HandMaxM);
                    Field(failures, src.Name, hint, "FootMaxM", a.FootMaxM, b.FootMaxM);
                    Field(failures, src.Name, hint, "RigidityMaxM", a.RigidityMaxM, b.RigidityMaxM);
                    Field(failures, src.Name, hint, "ElbowPops", a.ElbowPops, b.ElbowPops);
                    Field(failures, src.Name, hint, "KneePops", a.KneePops, b.KneePops);
                }
            }

            Assert.That(failures, Is.Empty,
                "the 1.0x rescale did not reproduce the baseline accuracy numbers, so the ruler is bent:\n"
                + string.Join("\n", failures));
        }

        /// <summary>
        /// ⭐ THE STRUCTURAL GUARD, AND IT IS THE ONE THE IDENTITY TEST PROVABLY CANNOT GIVE.
        ///
        /// At 1.0x every group scales by the same 1.0, so a parent table or group table that puts the wrong
        /// bone in the wrong group is INVISIBLE -- the identity test passes with a completely scrambled
        /// mapping. This is exactly the failure shape this project has shipped before (a detector whose
        /// output was identical whether its cause was present or absent), so the mapping gets a guard that
        /// depends on the mapping.
        ///
        /// GUARDS: BasisClipProportion.k_Parent / k_Group. An arm-only rescale must scale the arm by exactly
        /// its factor, must leave the LEG untouched to the bit, and must not move the shoulder AT ALL -- if
        /// the shoulder moves, the arm sweep is silently also a torso sweep and its numbers mean nothing.
        /// HEADROOM: where a quantity is UNMOVED it is exact float equality (zero headroom, and correct: a
        /// bone whose group scales by 1.0f contributes exactly 0f to the displacement, so those joints come
        /// back bit-identical and any distance between them does too). Where a quantity is TRANSLATED or
        /// SCALED, 1e-4 relative is float noise on a difference of metre-magnitude coordinates -- the CMU
        /// root wanders several metres from the origin, so cancellation in (a + D) - (b + D) costs about
        /// eps * |position| / |bone| ~ 4e-6 in the worst case. It is not a tuned threshold: a bone in the
        /// wrong group moves the ratio by 0.20-0.30, four orders of magnitude clear of it.
        /// </summary>
        [Test]
        public void Rescaling_MovesOnlyTheTargetedSegments()
        {
            const float k_RatioTol = 1e-4f;
            var failures = new List<string>();

            foreach (BasisMotionClip src in RequireCorpus())
            {
                // Sample rather than sweep every frame: the mapping is frame-independent, and 5 spread
                // frames catch a scrambled table just as surely as 700 do.
                int step = Mathf.Max(1, src.FrameCount / 5);

                foreach (float k in new[] { 0.80f, 1.30f })
                {
                    BasisMotionClip armOnly = BasisClipProportion.Rescale(src, BasisProportionScale.ArmOnly(k));
                    BasisMotionClip legOnly = BasisClipProportion.Rescale(src, BasisProportionScale.LegOnly(k));
                    BasisMotionClip torsoOnly = BasisClipProportion.Rescale(src, BasisProportionScale.TorsoOnly(k));

                    for (int f = 0; f < src.FrameCount; f += step)
                    {
                        foreach (bool isLeft in new[] { true, false })
                        {
                            string tag = $"{src.Name} f{f} {(isLeft ? "L" : "R")} x{k:0.00}";

                            // ARM-ONLY: the arm scales, the leg does not, the shoulder does not move.
                            Ratio(failures, $"{tag} armOnly arm", BasisClipProportion.ArmLength(armOnly, f, isLeft),
                                  BasisClipProportion.ArmLength(src, f, isLeft), k, k_RatioTol);
                            Ratio(failures, $"{tag} armOnly leg", BasisClipProportion.LegLength(armOnly, f, isLeft),
                                  BasisClipProportion.LegLength(src, f, isLeft), 1f, 0f);
                            Exact(failures, $"{tag} armOnly shoulder", armOnly, src, f,
                                  isLeft ? BasisMocapJoint.LeftUpperArm : BasisMocapJoint.RightUpperArm);
                            Exact(failures, $"{tag} armOnly hips", armOnly, src, f, BasisMocapJoint.Hips);
                            Exact(failures, $"{tag} armOnly head", armOnly, src, f, BasisMocapJoint.Head);

                            // LEG-ONLY: the leg scales, the arm does not, the hip joint does not move.
                            Ratio(failures, $"{tag} legOnly leg", BasisClipProportion.LegLength(legOnly, f, isLeft),
                                  BasisClipProportion.LegLength(src, f, isLeft), k, k_RatioTol);
                            Ratio(failures, $"{tag} legOnly arm", BasisClipProportion.ArmLength(legOnly, f, isLeft),
                                  BasisClipProportion.ArmLength(src, f, isLeft), 1f, 0f);
                            Exact(failures, $"{tag} legOnly hip", legOnly, src, f,
                                  isLeft ? BasisMocapJoint.LeftUpperLeg : BasisMocapJoint.RightUpperLeg);
                            Exact(failures, $"{tag} legOnly hand", legOnly, src, f,
                                  isLeft ? BasisMocapJoint.LeftHand : BasisMocapJoint.RightHand);

                            // TORSO-ONLY: the limb ROOTS move, the limb SEGMENTS keep their lengths.
                            // NOT exact here, and deliberately so: every limb joint inherits the SAME
                            // displacement D from its root, so the bone vector is (a + D) - (b + D), which
                            // is only equal to a - b in exact arithmetic. This is the one place in this
                            // test where float noise is real, and it gets the tolerance rather than a
                            // pretend-exact assert that would fail on the first clip that walks away from
                            // the origin.
                            Ratio(failures, $"{tag} torsoOnly arm", BasisClipProportion.ArmLength(torsoOnly, f, isLeft),
                                  BasisClipProportion.ArmLength(src, f, isLeft), 1f, k_RatioTol);
                            Ratio(failures, $"{tag} torsoOnly leg", BasisClipProportion.LegLength(torsoOnly, f, isLeft),
                                  BasisClipProportion.LegLength(src, f, isLeft), 1f, k_RatioTol);
                            Exact(failures, $"{tag} torsoOnly hips", torsoOnly, src, f, BasisMocapJoint.Hips);
                        }
                    }
                }
            }

            Assert.That(failures, Is.Empty,
                "the rescale reached bones it should not have (or missed ones it should have):\n"
                + string.Join("\n", Head(failures, 40)));
        }

        /// <summary>
        /// GUARDS: that a rescaled clip is still an anatomically sane human. BasisBvhLoader.Validate is the
        /// handedness guard the whole corpus rests on -- left hand on the body's LEFT, knee anterior to the
        /// hip->ankle line -- and it is exactly the kind of property a rebuild could quietly invert (a sign
        /// slip in the displacement would mirror a limb while leaving every length correct).
        ///
        /// Run across the ENTIRE sweep grid, not just the corners: the anterior-knee check is a majority
        /// vote over sampled frames, and a mid-range scale is as capable of tipping it as an extreme one.
        /// HEADROOM: Validate's own (it demands a clear majority, not unanimity, because a walking arm
        /// legitimately crosses the midline).
        /// </summary>
        [Test]
        public void RescaledClips_AreStillAnatomicallySaneHumans_AtEveryMismatch()
        {
            var failures = new List<string>();
            int checkedClips = 0;

            foreach (BasisMotionClip src in RequireCorpus())
            {
                Assert.That(BasisBvhLoader.Validate(src, out string baseWhy), Is.True,
                    $"{src.Name} is not sane BEFORE rescaling: {baseWhy}");

                foreach (BasisProportionScale scale in SweepPoints())
                {
                    BasisMotionClip rescaled = BasisClipProportion.Rescale(src, scale);
                    if (!BasisBvhLoader.Validate(rescaled, out string why))
                    {
                        failures.Add($"{src.Name} @ {scale}: {why}");
                    }
                    checkedClips++;
                }
            }

            Assert.That(checkedClips, Is.GreaterThan(100), $"only {checkedClips} rescales validated");
            Assert.That(failures, Is.Empty, "rescaling broke the skeleton's anatomy:\n" + string.Join("\n", Head(failures, 20)));
        }

        // ------------------------------------------------------------------ the domain clamp

        /// <summary>
        /// ⭐⭐ THE SHIPPED BUG'S GUARD, DRIVEN BY REAL MISMATCH.
        ///
        /// BasisSwivelHintConformanceTests.TheModel_RefusesToExtrapolate already pins saturation -- on a
        /// static synthetic T-posed rig, with random directions, ARM ONLY. This drives the same clamp from
        /// 13,785 frames of real human motion against a real short avatar (arm 0.80x, leg 0.85x), through
        /// the real body frames BasisSwivelHintCore.BuildFrame produces, and it covers the LEG model too --
        /// BasisLegSwivelModel is the 3rd-order polynomial with coefficients up to 15 that the post-mortem
        /// is actually about, and its saturation was not pinned anywhere.
        ///
        /// THE ASSERTION IS EXACT, AND IT CAN BE. Multiplying a float by 2 or 4 is exact in binary floating
        /// point, and scaling by a power of two commutes with IEEE rounding through every operation the
        /// clamp performs: dot, sqrt and divide. So for any tipLocal already outside the domain,
        ///     t = tipLocal / |tipLocal|
        /// is BIT-IDENTICAL whether the model is handed tipLocal, 2*tipLocal or 4*tipLocal -- and therefore
        /// so is everything downstream of it. No tolerance is needed and none is invented.
        ///
        /// GUARDS: the `len > 1f ? tipLocal / len : tipLocal` clamp at the head of BasisElbowFieldModel.Elbow,
        /// BasisArmElbowNeuralFieldModel.Elbow and BasisLegSwivelModel.SwivelRad. Delete any of them and this
        /// fails on the first out-of-domain frame. (A failure could also mean the toolchain began fusing
        /// multiply-add, which would break the exactness argument rather than the clamp -- also worth knowing.)
        ///
        /// THE POSITIVE CONTROL IS THE OTHER HALF OF THIS TEST. A saturation check that never sees an
        /// out-of-domain frame passes vacuously and proves nothing, which is precisely how this project's
        /// pop detector managed to report the same count with and without its cause. So the beyond-reach
        /// sample count is asserted non-zero, and it is reported.
        /// </summary>
        [Test]
        public void TheDomainClamp_Saturates_OnRealHumanTargetsBeyondAShortAvatarsReach()
        {
            int armBeyond = 0, legBeyond = 0, armSamples = 0, legSamples = 0;
            float armReachMax = 0f, legReachMax = 0f;
            var failures = new List<string>();

            foreach (BasisMotionClip human in RequireCorpus())
            {
                BasisMotionClip avatar = BasisClipProportion.Rescale(human, k_ShortAvatar);

                for (int f = 0; f < human.FrameCount; f++)
                {
                    BasisSwivelFrame armFrame = ArmFrame(avatar, f);
                    BasisSwivelFrame legFrame = LegFrame(avatar, f);

                    for (int side = 0; side < 2; side++)
                    {
                        bool isLeft = side == 0;

                        if (armFrame.Valid)
                        {
                            armSamples++;
                            Vector3 shoulder = avatar.Get(f, isLeft ? BasisMocapJoint.LeftUpperArm : BasisMocapJoint.RightUpperArm).Position;
                            Vector3 target = human.Get(f, isLeft ? BasisMocapJoint.LeftHand : BasisMocapJoint.RightHand).Position;
                            float armLen = BasisClipProportion.ArmLength(avatar, f, isLeft);

                            BasisSwivelHintCore.Features(armFrame, shoulder, target, armLen, isLeft, out float3 tip);
                            float len = math.length(tip);
                            armReachMax = Mathf.Max(armReachMax, len);

                            // 1e-3 of clearance off the branch boundary: a sample sitting exactly at len == 1
                            // takes the unclamped branch at 1x and the clamped one at 2x, which is correct
                            // behaviour and not something to assert equality across.
                            if (len > 1f + 1e-3f)
                            {
                                armBeyond++;
                                Saturates(failures, $"{human.Name} f{f} {(isLeft ? "L" : "R")} reach {len:F3}",
                                          "BasisElbowFieldModel.Elbow",
                                          BasisElbowFieldModel.Elbow(tip),
                                          BasisElbowFieldModel.Elbow(tip * 2f),
                                          BasisElbowFieldModel.Elbow(tip * 4f));
                                Saturates(failures, $"{human.Name} f{f} {(isLeft ? "L" : "R")} reach {len:F3}",
                                          "BasisArmElbowNeuralFieldModel.Elbow",
                                          BasisArmElbowNeuralFieldModel.Elbow(tip),
                                          BasisArmElbowNeuralFieldModel.Elbow(tip * 2f),
                                          BasisArmElbowNeuralFieldModel.Elbow(tip * 4f));
                            }
                        }

                        if (legFrame.Valid)
                        {
                            legSamples++;
                            Vector3 hip = avatar.Get(f, isLeft ? BasisMocapJoint.LeftUpperLeg : BasisMocapJoint.RightUpperLeg).Position;
                            Vector3 target = human.Get(f, isLeft ? BasisMocapJoint.LeftFoot : BasisMocapJoint.RightFoot).Position;
                            float legLen = BasisClipProportion.LegLength(avatar, f, isLeft);

                            BasisSwivelHintCore.Features(legFrame, hip, target, legLen, isLeft, out float3 tip);
                            float len = math.length(tip);
                            legReachMax = Mathf.Max(legReachMax, len);

                            if (len > 1f + 1e-3f)
                            {
                                legBeyond++;
                                float a = BasisLegSwivelModel.SwivelRad(tip, out _);
                                float b = BasisLegSwivelModel.SwivelRad(tip * 2f, out _);
                                float c = BasisLegSwivelModel.SwivelRad(tip * 4f, out _);
                                if (a != b || a != c)
                                {
                                    failures.Add($"{human.Name} f{f} {(isLeft ? "L" : "R")} reach {len:F3}: " +
                                                 $"BasisLegSwivelModel.SwivelRad EXTRAPOLATED past the fit domain " +
                                                 $"({a:F9} / {b:F9} / {c:F9} at 1x / 2x / 4x overshoot)");
                                }
                            }
                        }
                    }
                }
            }

            TestContext.WriteLine(
                $"\ndomain clamp, driven by a short avatar ({k_ShortAvatar}) on real human targets:\n" +
                $"  arm: {armBeyond}/{armSamples} samples beyond the avatar's reach " +
                $"({Pct(armBeyond, armSamples):F2}%), max reach {armReachMax:F3}\n" +
                $"  leg: {legBeyond}/{legSamples} samples beyond the avatar's reach " +
                $"({Pct(legBeyond, legSamples):F2}%), max reach {legReachMax:F3}\n");

            // THE POSITIVE CONTROL. Without this the test can pass by never exercising the clamp at all.
            Assert.That(armBeyond, Is.GreaterThan(0),
                $"a {k_ShortAvatar.Arm:0.00}x avatar arm produced NO out-of-domain arm frames across {armSamples} " +
                "samples of real human motion. Either the rescale is not shortening the arm or the reach ratio " +
                "is being computed against the wrong length -- either way this test is passing vacuously.");
            Assert.That(legBeyond, Is.GreaterThan(0),
                $"a {k_ShortAvatar.Leg:0.00}x avatar leg produced NO out-of-domain leg frames across {legSamples} " +
                "samples. Same vacuous-pass problem as the arm above.");

            Assert.That(failures, Is.Empty,
                "a pole model EXTRAPOLATED past the domain it was fitted in -- this is the exact defect that " +
                "put the elbows up by the ears in a headset while the whole suite stayed green:\n"
                + string.Join("\n", Head(failures, 20)));
        }

        /// <summary>
        /// The same saturation property END TO END, through the shipped entry point
        /// (BasisSwivelHintCore.ArmHint / LegHint) rather than the bare model, on the input the runtime
        /// actually varies: THE AVATAR'S LIMB LENGTH. Halving the avatar's arm against a fixed human hand
        /// target is literally "the same user on a smaller avatar", and past full reach the elbow direction
        /// must stop responding to it.
        ///
        /// GUARDS: that nothing between the model and the hint (the tuck, the elbow-down blend at extension,
        /// the world map) re-introduces a dependence on how far past reach the target is.
        /// HEADROOM: NOT MEASURED -- this test has never been run. 0.5 deg is a DIVERGENCE DETECTOR, not a
        /// ratchet: with the clamp intact the two hints differ only by the rounding of one world-space
        /// add/subtract at metre magnitudes (order 1e-5 deg), and without it the affine field swings the
        /// elbow by tens of degrees. There is nothing in between, so a loose bound is the honest one. Tighten
        /// it to the measured value once the number exists.
        /// </summary>
        [Test]
        public void TheHint_StopsRespondingToOvershoot_WhenTheAvatarsLimbShrinksPastReach()
        {
            const float k_DivergenceDeg = 0.5f;
            var failures = new List<string>();
            int armChecks = 0, legChecks = 0;

            foreach (BasisMotionClip human in RequireCorpus())
            {
                BasisMotionClip avatar = BasisClipProportion.Rescale(human, k_ShortAvatar);
                int step = Mathf.Max(1, human.FrameCount / 60);   // the property is per-frame; 60 spread frames per clip

                for (int f = 0; f < human.FrameCount; f += step)
                {
                    BasisSwivelFrame armFrame = ArmFrame(avatar, f);
                    BasisSwivelFrame legFrame = LegFrame(avatar, f);

                    for (int side = 0; side < 2; side++)
                    {
                        bool isLeft = side == 0;

                        if (armFrame.Valid)
                        {
                            Vector3 shoulder = avatar.Get(f, isLeft ? BasisMocapJoint.LeftUpperArm : BasisMocapJoint.RightUpperArm).Position;
                            Vector3 target = human.Get(f, isLeft ? BasisMocapJoint.LeftHand : BasisMocapJoint.RightHand).Position;
                            float armLen = BasisClipProportion.ArmLength(avatar, f, isLeft);

                            if (Vector3.Distance(target, shoulder) > armLen * 1.001f
                                && BasisSwivelHintCore.ArmHint(armFrame, shoulder, target, armLen, isLeft, out Vector3 h1, out _)
                                && BasisSwivelHintCore.ArmHint(armFrame, shoulder, target, armLen * 0.5f, isLeft, out Vector3 h2, out _))
                            {
                                armChecks++;
                                float deg = Vector3.Angle(h1 - shoulder, h2 - shoulder);
                                if (!(deg <= k_DivergenceDeg))
                                {
                                    failures.Add($"{human.Name} f{f} {(isLeft ? "L" : "R")} arm: halving the avatar's " +
                                                 $"arm past full reach swung the elbow hint {deg:F2} deg -- the model is " +
                                                 "still extrapolating on overshoot");
                                }
                            }
                        }

                        if (legFrame.Valid)
                        {
                            Vector3 hip = avatar.Get(f, isLeft ? BasisMocapJoint.LeftUpperLeg : BasisMocapJoint.RightUpperLeg).Position;
                            Vector3 target = human.Get(f, isLeft ? BasisMocapJoint.LeftFoot : BasisMocapJoint.RightFoot).Position;
                            float legLen = BasisClipProportion.LegLength(avatar, f, isLeft);

                            if (Vector3.Distance(target, hip) > legLen * 1.001f
                                && BasisSwivelHintCore.LegHint(legFrame, hip, target, legLen, isLeft, out Vector3 h1, out _)
                                && BasisSwivelHintCore.LegHint(legFrame, hip, target, legLen * 0.5f, isLeft, out Vector3 h2, out _))
                            {
                                legChecks++;
                                float deg = Vector3.Angle(h1 - hip, h2 - hip);
                                if (!(deg <= k_DivergenceDeg))
                                {
                                    failures.Add($"{human.Name} f{f} {(isLeft ? "L" : "R")} leg: halving the avatar's " +
                                                 $"leg past full reach swung the knee hint {deg:F2} deg");
                                }
                            }
                        }
                    }
                }
            }

            TestContext.WriteLine($"\novershoot saturation checked on {armChecks} arm and {legChecks} leg beyond-reach samples\n");

            // Positive control again: no beyond-reach samples means no test.
            Assert.That(armChecks, Is.GreaterThan(0), "no beyond-reach arm samples were produced -- vacuous pass");
            Assert.That(legChecks, Is.GreaterThan(0), "no beyond-reach leg samples were produced -- vacuous pass");
            Assert.That(failures, Is.Empty, string.Join("\n", Head(failures, 20)));
        }

        // ------------------------------------------------------------------ ⭐ the headline sweep

        /// <summary>
        /// ⭐⭐ THE MEASUREMENT. Drive the solver with an avatar of one size while the effector targets come
        /// from a human of another, sweep the mismatch, and report per joint:
        ///
        ///   * accuracy degradation vs the matched (1.0x) baseline of THIS driver;
        ///   * the OUT-OF-DOMAIN FRACTION -- what share of frames are commanded beyond the avatar's reach.
        ///     This is the quantity the shipped bug was actually about and nothing else in this project
        ///     measures it;
        ///   * whether the solver saturates gracefully or diverges past full reach.
        ///
        /// REPORT-ONLY on the table. The asserts below are limited to properties that are unambiguous
        /// without a settled baseline: finiteness, exact reach-preservation/shortfall on the arm (reusing
        /// BasisBodyProportionTests' calibrated 0.005 limb-length bound rather than inventing one), and
        /// monotonicity of the out-of-domain counter in its own cause.
        ///
        /// ⚠ WHAT THE ELBOW ERROR IS AND IS NOT. It is measured against the HUMAN's elbow, because that is
        /// the only ground truth that exists -- but a shorter avatar arm CANNOT put its elbow where the
        /// human's was, so part of the growth is pure geometry and none of it is the solver's fault. The
        /// table therefore also reports IRREDUCIBLE: | |humanElbow - shoulder| - avatarUpperLen |, the
        /// distance from the human's elbow to the sphere the avatar's elbow is confined to.
        ///
        /// ⭐⭐ AND DO NOT READ `EXCESS` AS A SOLVE-QUALITY NUMBER. IT WAS ONCE REPORTED AS ONE AND THAT WAS
        /// WRONG, TWICE OVER. Measured offline against the real cores over this exact sweep:
        ///
        ///     scale | elbow  IRRED_sphere EXCESS_sphere | IRRED_circle |  SWIVEL-ANGLE ERR | circle r
        ///     x1.00 |  3.46      0.00         3.46      |     0.01     |     20.93 deg     |  8.65 cm
        ///     x1.10 |  8.01      3.03         4.98      |     5.97     |     19.44 deg     | 14.47 cm
        ///     x1.20 | 11.98      6.06         5.92      |    10.30     |     18.95 deg     | 18.59 cm
        ///     x1.30 | 15.54      9.09         6.45      |    14.07     |     18.85 deg     | 22.10 cm
        ///
        ///   1. THE SPHERE IS THE WRONG FLOOR. With the shoulder and the solved hand both fixed the elbow is
        ///      confined to the INTERSECTION of two spheres -- a CIRCLE (Korein's swivel; the arm's
        ///      redundancy is ONE scalar). The circle is a strict subset of the sphere, so the distance to
        ///      the circle is a strictly TIGHTER lower bound, and at x1.30 it is 14.07 cm against the
        ///      sphere's 9.09. Five of the "EXCESS" centimetres at x1.30 are geometry the sphere bound
        ///      simply could not see.
        ///
        ///   2. THE UNIT INFLATES. Position error ~ r * angular error, and r -- the elbow's lever arm about
        ///      the shoulder->hand axis -- is 0.171 of the arm at x1.00 and 0.336 at x1.30, because a LONGER
        ///      avatar arm reaching the SAME human target is MORE BENT. r nearly triples (8.65 -> 22.10 cm,
        ///      2.55x) across the sweep. The SAME angular accuracy therefore buys a bigger centimetre.
        ///
        ///   The scale-free quantity is the SWIVEL ANGLE, and it is FLAT-to-improving: 20.93 -> 18.85 deg.
        ///   PAIRED sample by sample (same clip, same frame, same side, same target, only the avatar's arm
        ///   different) the angular error at x1.30 is a mean 2.08 deg BETTER than at x1.00 and improves on
        ///   64% of the 27,530 paired samples. Stated back in centimetres so no unit can hide in it: holding
        ///   the x1.00 angular accuracy and changing ONLY the geometry predicts a 4.93 cm swivel-attributable
        ///   error at x1.30; the solver actually delivers 4.61. It beats "unchanged", it does not degrade.
        ///
        ///   So the table reports the ANGLE alongside the centimetres, and the angle is the column to read.
        ///   This project has been burned twice by a metric in the wrong unit -- a pop detector that was
        ///   scale-RELATIVE and read an identical 36 whether the hint was absent, wrong or fitted, and a
        ///   smoothness score that preferred over-smoothed mush to a real human. EXCESS was very nearly the
        ///   third.
        ///
        /// ⚠ THIS DRIVER IS NOT BasisMocapAccuracy AND ITS ABSOLUTE NUMBERS ARE NOT COMPARABLE TO THE
        /// PUBLISHED BASELINES. Two deliberate differences, stated rather than buried:
        ///   1. The LEG is driven with the LIVE RIG's model hint (BasisSwivelHintCore.LegHint, exactly as
        ///      BasisFullBodyIK.SolveLegs does, distrust and all). BasisMocapAccuracy's None/Lookup/
        ///      ElbowField modes give the knee NO hint at all and fall back to the bend normal, so its
        ///      published knee numbers describe a different leg. The model hint is required here because it
        ///      is the only path the leg's domain clamp is on.
        ///   2. The knee-swivel One-Euro post-filter that BasisMocapAccuracy mirrors is NOT mirrored here.
        ///      Copying its constants a third time is the drift trap the core/sweep/gate convention exists
        ///      to avoid. It rotates the knee about the hip->foot axis and therefore cannot change reach,
        ///      out-of-domain fraction or foot position -- the numbers this test is actually for -- but it
        ///      does move the knee, so the knee ERROR column is core-solve-only.
        /// Every row of the table uses the same driver, so the DELTAS across the sweep are exact even where
        /// the absolute values are not comparable outward. The 1.0x row is this table's own baseline.
        ///
        /// ⚠ THE THREE MODELLING TRAPS BasisMocapAccuracy DOCUMENTS, ALL AVOIDED HERE:
        ///   * the solved pose is NEVER carried forward -- the pre-IK limb is rebuilt from the bind pose
        ///     every frame, the way the Animator re-evaluates its base layer (carrying it forward once
        ///     compounded a single knee inversion into 74 cm of foot error);
        ///   * the limb is NEVER seeded from truth per-frame -- with no hint the two-bone core preserves the
        ///     current bend plane, so a truth-seeded limb is trivially "right" and the measurement circular;
        ///   * and one that is unique to THIS test: the limb is seeded from the AVATAR clip, never the human
        ///     one. Seed it from the human and the bone lengths silently become the human's, the mismatch
        ///     evaporates, and the sweep reports a flat line that looks like good news.
        /// </summary>
        [Test]
        public void ProportionMismatch_DegradationAndOutOfDomainFraction_AcrossTheSweep()
        {
            List<BasisMotionClip> corpus = RequireCorpus();

            var rows = new List<Sweep>();
            foreach (BasisProportionScale scale in SweepPoints())
            {
                var acc = new Sweep { Scale = scale };
                foreach (BasisMotionClip human in corpus)
                {
                    BasisMotionClip avatar = BasisClipProportion.Rescale(human, scale);
                    Drive(human, avatar, acc);
                }
                rows.Add(acc);
            }

            // ---------------------------------------------------------------- the report
            Sweep matched = rows.Find(r => r.Scale.IsIdentity);
            Assert.That(matched, Is.Not.Null, "the sweep must contain the matched 1.0x baseline row");
            float baseElbow = matched.ElbowMean, baseKnee = matched.KneeMean;

            var log = new StringBuilder();
            log.AppendLine("\nPROPORTION MISMATCH: an avatar of one size driven by a human of another");
            log.AppendLine($"  corpus: {corpus.Count} root-tier clips, {matched.ArmSamples} arm and {matched.LegSamples} leg samples per row");
            log.AppendLine($"  matched body: arm {matched.AvatarArmLen * 100f:F1} cm, leg {matched.AvatarLegLen * 100f:F1} cm " +
                           $"(BasisBvhLoader normalises every clip to an {BasisBvhLoader.TargetLegLengthMetres:0.00} m leg)");
            log.AppendLine("  elbow/knee error is vs the HUMAN's joint; IRRED is the geometric floor a shorter limb cannot beat;");
            log.AppendLine("  EXCESS = mean - irreducible is the part attributable to the SOLVE. OOD = frames commanded beyond reach.");
            log.AppendLine();
            log.AppendLine("  scale             | elbow mean  p95  IRRED EXCESS | vs 1.0x | arm OOD%  maxReach | shortfallDev");
            log.AppendLine("  ------------------+------------------------------+---------+--------------------+-------------");
            foreach (Sweep r in rows)
            {
                if (r.Scale.Leg != 1f && r.Scale.Arm == 1f) continue;   // leg rows go in the second table
                log.AppendLine(
                    $"  arm x{r.Scale.Arm:0.00}          | {Cm(r.ElbowMean),6} {Cm(r.ElbowP95),5} {Cm(r.ElbowIrreducibleMean),5} {Cm(r.ElbowMean - r.ElbowIrreducibleMean),6} | " +
                    $"{Delta(r.ElbowMean, baseElbow),7} | {Pct(r.ArmBeyondReach, r.ArmSamples),7:F2}%  {r.ArmReachMax,7:F3} | {r.HandShortfallDevMax * 1000f,8:F3} mm");
            }

            // ⭐ THE SAME ROWS IN THE UNIT THAT DOES NOT INFLATE. Read SWIVEL, not EXCESS: see the header.
            // IRRED_cir is the distance to the CIRCLE the elbow is actually on (tighter than the sphere
            // bound above); r is that circle's radius, which is what converts an angle into a centimetre.
            log.AppendLine();
            log.AppendLine("  scale             | elbow  IRRED_sph IRRED_cir | SWIVEL ERR deg | chord | circle r  r/arm");
            log.AppendLine("  ------------------+----------------------------+----------------+-------+----------------");
            foreach (Sweep r in rows)
            {
                if (r.Scale.Leg != 1f && r.Scale.Arm == 1f) continue;
                float rFrac = r.AvatarArmLen > 1e-5f ? r.ElbowRadiusMean / r.AvatarArmLen : float.NaN;
                log.AppendLine(
                    $"  arm x{r.Scale.Arm:0.00}          | {Cm(r.ElbowMean),6} {Cm(r.ElbowIrreducibleMean),9} {Cm(r.ElbowCircleFloorMean),9} | " +
                    $"{r.ElbowSwivelMeanDeg,6:F2} {r.ElbowSwivelP95Deg,7:F2} | {Cm(r.ElbowChordMean),5} | {Cm(r.ElbowRadiusMean),7} {rFrac,6:F3}");
            }
            log.AppendLine($"  (swivel angle undefined on {rows[0].ElbowSwivelDegenerate} of {rows[0].ElbowSwivelSamples} " +
                           "samples at the first row -- a collapsed circle has no angle, so they are excluded, not zeroed)");
            log.AppendLine();
            log.AppendLine("  scale             | knee  mean  p95  IRRED EXCESS | vs 1.0x | leg OOD%  maxReach | footErrMax");
            log.AppendLine("  ------------------+------------------------------+---------+--------------------+-------------");
            foreach (Sweep r in rows)
            {
                if (r.Scale.Arm != 1f && r.Scale.Leg == 1f) continue;
                log.AppendLine(
                    $"  leg x{r.Scale.Leg:0.00}          | {Cm(r.KneeMean),6} {Cm(r.KneeP95),5} {Cm(r.KneeIrreducibleMean),5} {Cm(r.KneeMean - r.KneeIrreducibleMean),6} | " +
                    $"{Delta(r.KneeMean, baseKnee),7} | {Pct(r.LegBeyondReach, r.LegSamples),7:F2}%  {r.LegReachMax,7:F3} | " +
                    $"{r.FootErrMax * 1000f,8:F3} mm ({r.FootErrMaxFrac:F5} leg-lengths)");
            }
            log.AppendLine();
            log.AppendLine($"  hints declined (degenerate body frame): arm {rows[0].NoArmHint}, leg {rows[0].NoLegHint} at the first row");
            TestContext.WriteLine(log.ToString());

            // ---------------------------------------------------------------- the asserts
            var failures = new List<string>();

            foreach (Sweep r in rows)
            {
                // FINITENESS. A NaN anywhere in the solve at any mismatch is unambiguous, and it is the
                // shape the un-clamped polynomial failed in.
                if (r.NonFinite > 0)
                    failures.Add($"{r.Scale}: {r.NonFinite} non-finite solved joints");

                // SATURATION, NOT DIVERGENCE, on the arm. The arm solve ends in a reach-preserving bisection,
                // so it must land exactly on a reachable target and fall exactly (distance - armLen) short of
                // an unreachable one. 0.005 limb-lengths is BasisBodyProportionTests'
                // Arm_BeyondReachInputs_FallShortCleanly bound, reused rather than re-invented -- it was
                // calibrated there against a float64 port and measured 0.000000 arm-lengths of deviation.
                if (r.HandShortfallDevMaxFrac > 0.005f)
                    failures.Add($"{r.Scale}: the hand missed its commanded/saturated position by " +
                                 $"{r.HandShortfallDevMax * 100f:F2} cm ({r.HandShortfallDevMaxFrac:F5} arm-lengths) -- " +
                                 "past full reach the arm must extend straight at the target and stop, not diverge");

                if (r.ArmSamples == 0 || r.LegSamples == 0)
                    failures.Add($"{r.Scale}: no samples taken");
            }

            // THE OUT-OF-DOMAIN COUNTER MUST RESPOND TO ITS OWN CAUSE, MONOTONICALLY. reach = |target - shoulder|
            // / avatarArmLen and only the denominator moves across the arm sweep, so the beyond-reach COUNT is
            // mathematically non-increasing in armScale. If it is not, the counter is not measuring what it
            // says it is -- which is exactly how a pop detector once reported the same number with and without
            // its cause. Integer counts, so no tolerance.
            List<Sweep> armRows = rows.FindAll(r => r.Scale.Leg == 1f);
            armRows.Sort((a, b) => a.Scale.Arm.CompareTo(b.Scale.Arm));
            for (int i = 1; i < armRows.Count; i++)
            {
                if (armRows[i].ArmBeyondReach > armRows[i - 1].ArmBeyondReach)
                    failures.Add($"arm out-of-domain count ROSE from {armRows[i - 1].ArmBeyondReach} at " +
                                 $"x{armRows[i - 1].Scale.Arm:0.00} to {armRows[i].ArmBeyondReach} at x{armRows[i].Scale.Arm:0.00} " +
                                 "-- a longer avatar arm cannot put MORE frames out of reach; the counter is wrong");
            }
            Assert.That(armRows[0].ArmBeyondReach, Is.GreaterThan(armRows[armRows.Count - 1].ArmBeyondReach),
                $"the shortest avatar arm (x{armRows[0].Scale.Arm:0.00}) put no more frames out of reach than the " +
                $"longest (x{armRows[armRows.Count - 1].Scale.Arm:0.00}) -- the out-of-domain metric is not responding " +
                "to the mismatch it exists to measure, so every OOD% in the table above is meaningless");

            List<Sweep> legRows = rows.FindAll(r => r.Scale.Arm == 1f);
            legRows.Sort((a, b) => a.Scale.Leg.CompareTo(b.Scale.Leg));
            for (int i = 1; i < legRows.Count; i++)
            {
                if (legRows[i].LegBeyondReach > legRows[i - 1].LegBeyondReach)
                    failures.Add($"leg out-of-domain count ROSE from {legRows[i - 1].LegBeyondReach} at " +
                                 $"x{legRows[i - 1].Scale.Leg:0.00} to {legRows[i].LegBeyondReach} at x{legRows[i].Scale.Leg:0.00}");
            }
            Assert.That(legRows[0].LegBeyondReach, Is.GreaterThan(legRows[legRows.Count - 1].LegBeyondReach),
                "the shortest avatar leg put no more frames out of reach than the longest -- the leg OOD metric is dead");

            Assert.That(failures, Is.Empty, "the solver did not degrade gracefully under proportion mismatch:\n"
                + string.Join("\n", Head(failures, 30)));
        }

        // ================================================================== the mismatch driver

        /// <summary>
        /// ⚠ A TRANSCRIPTION of BasisMocapAccuracy's private Limb / SeedLimb / AnimatedPose, not a call:
        /// they are private and BasisMocapAccuracy is off-limits to edit. The PRE-IK limb, modelled the way
        /// the runtime produces it -- a fixed bind pose riding the parent, rebuilt from scratch every frame,
        /// with IK layered on top, exactly as the Animator re-evaluates its base layer. If BasisMocapAccuracy's
        /// version changes, change this one. See the class header for why the difference matters.
        /// </summary>
        struct Limb
        {
            public Quaternion RootLocal, MidLocal, TipLocal;
            public Vector3 UpperDirLocal, LowerDirLocal;
            public float UpperLen, LowerLen;
            public bool Seeded;
        }

        static void SeedLimb(ref Limb l, Quaternion parentRot, Vector3 root, Vector3 mid, Vector3 tip,
                             Quaternion rootRot, Quaternion midRot, Quaternion tipRot)
        {
            l.RootLocal = Quaternion.Inverse(parentRot) * rootRot;
            l.MidLocal = Quaternion.Inverse(rootRot) * midRot;
            l.TipLocal = Quaternion.Inverse(midRot) * tipRot;
            l.UpperLen = Vector3.Distance(root, mid);
            l.LowerLen = Vector3.Distance(mid, tip);
            l.UpperDirLocal = Quaternion.Inverse(rootRot) * (mid - root).normalized;
            l.LowerDirLocal = Quaternion.Inverse(midRot) * (tip - mid).normalized;
            l.Seeded = true;
        }

        static void AnimatedPose(in Limb l, Quaternion parentRot, Vector3 root,
                                 out Quaternion rootRot, out Quaternion midRot, out Quaternion tipRot,
                                 out Vector3 mid, out Vector3 tip)
        {
            rootRot = parentRot * l.RootLocal;
            midRot = rootRot * l.MidLocal;
            tipRot = midRot * l.TipLocal;
            mid = root + (rootRot * l.UpperDirLocal) * l.UpperLen;
            tip = mid + (midRot * l.LowerDirLocal) * l.LowerLen;
        }

        sealed class Sweep
        {
            public BasisProportionScale Scale;

            public readonly List<float> ElbowErr = new List<float>();
            public readonly List<float> KneeErr = new List<float>();

            public int ArmSamples, LegSamples;
            public int ArmBeyondReach, LegBeyondReach;
            public float ArmReachMax, LegReachMax;
            public float ElbowIrreducibleSum, KneeIrreducibleSum;

            /// <summary>⭐ THE SCALE-FREE COLUMN. The elbow's error decomposes EXACTLY, in cylindrical
            /// coordinates about the shoulder->solved-hand axis, into a part no swivel could have fixed and
            /// a part that is purely the swivel being wrong:
            ///     err^2 = [ z^2 + (rho_h - r)^2 ] + [ 4 * rho_h * r * sin^2(dTheta/2) ]
            ///           =        dCircle^2        +              chord^2
            /// dCircle is the distance from the human's elbow to the CIRCLE the avatar's elbow actually
            /// lives on -- the true geometric floor, tighter than the sphere bound above. dTheta is the
            /// swivel-angle error, and it is the only one of these that does not inflate with the avatar's
            /// arm length. See the header for why that matters.</summary>
            public readonly List<float> ElbowSwivelErrDeg = new List<float>();
            public float ElbowCircleFloorSum, ElbowChordSum, ElbowRadiusSum;
            public int ElbowSwivelSamples, ElbowSwivelDegenerate;

            public float ElbowCircleFloorMean => ElbowSwivelSamples > 0 ? ElbowCircleFloorSum / ElbowSwivelSamples : float.NaN;
            // Over the samples whose ANGLE is defined, not over all of them: the floor and the radius exist
            // on a collapsed circle, the chord does not.
            public float ElbowChordMean => ElbowSwivelErrDeg.Count > 0 ? ElbowChordSum / ElbowSwivelErrDeg.Count : float.NaN;
            public float ElbowRadiusMean => ElbowSwivelSamples > 0 ? ElbowRadiusSum / ElbowSwivelSamples : float.NaN;
            public float ElbowSwivelMeanDeg => Mean(ElbowSwivelErrDeg);
            public float ElbowSwivelP95Deg => Pct95(ElbowSwivelErrDeg);

            /// <summary>Worst |handErr - expectedShortfall|, in metres for the report AND as a fraction of
            /// THAT SAMPLE'S OWN arm length for the assert. Two fields because the bound being reused from
            /// BasisBodyProportionTests is expressed in limb-lengths, and reducing to a single aggregate arm
            /// length to convert it would silently pick whichever sample happened to be last.</summary>
            public float HandShortfallDevMax, HandShortfallDevMaxFrac;
            public float FootErrMax, FootErrMaxFrac;

            /// <summary>The last sample's avatar limb length, for the report header only -- never for an
            /// assert. Every corpus clip is normalised by BasisBvhLoader to an 0.85 m leg, so these barely
            /// vary within a row, but "barely" is not "exactly" and a threshold must never be built on it.</summary>
            public float AvatarArmLen, AvatarLegLen;

            public int NonFinite, NoArmHint, NoLegHint;

            public float ElbowMean => Mean(ElbowErr);
            public float KneeMean => Mean(KneeErr);
            public float ElbowP95 => Pct95(ElbowErr);
            public float KneeP95 => Pct95(KneeErr);
            public float ElbowIrreducibleMean => ArmSamples > 0 ? ElbowIrreducibleSum / ArmSamples : float.NaN;
            public float KneeIrreducibleMean => LegSamples > 0 ? KneeIrreducibleSum / LegSamples : float.NaN;
        }

        static void Drive(BasisMotionClip human, BasisMotionClip avatar, Sweep acc)
        {
            var arms = new Limb[2];
            var legs = new Limb[2];

            for (int f = 0; f < human.FrameCount; f++)
            {
                // The AVATAR's torso, because the avatar's arm hangs off the avatar's chest. With torso at
                // 1.0x these are bit-identical to the human's (rotations are never touched by the rescale and
                // a Torso-group bone at 1.0 contributes exactly zero displacement), which is asserted below.
                Quaternion hipsRot = avatar.Get(f, BasisMocapJoint.Hips).Rotation;
                Quaternion chestRot = avatar.Get(f, BasisMocapJoint.Chest).Rotation;
                Vector3 playerUp = hipsRot * Vector3.up;

                BasisSwivelFrame armFrame = ArmFrame(avatar, f);
                BasisSwivelFrame legFrame = LegFrame(avatar, f);

                for (int side = 0; side < 2; side++)
                {
                    bool isLeft = side == 0;
                    SolveArmMismatch(human, avatar, f, isLeft, ref arms[side], armFrame, chestRot, playerUp, acc);
                    SolveLegMismatch(human, avatar, f, isLeft, ref legs[side], legFrame, hipsRot, acc);
                }
            }
        }

        /// <summary>
        /// THE LIVE SITUATION, ONE ARM, ONE FRAME: the avatar's arm, rooted at the avatar's shoulder, is
        /// commanded to the position of the HUMAN's hand. Wired exactly as BasisMocapAccuracy.SolveArm wires
        /// the ElbowField (shipping) path -- same core, same entry point for the hint, same unclamped
        /// HintMaxStepDeg (the rate limiter bounds change from the PREVIOUS SOLVE and this is a memoryless
        /// solve off the bind pose), and no elbow One-Euro because ElbowField is not one of the modes that
        /// runs one.
        /// </summary>
        static void SolveArmMismatch(BasisMotionClip human, BasisMotionClip avatar, int f, bool isLeft,
                                     ref Limb limb, in BasisSwivelFrame frame, Quaternion chestRot, Vector3 playerUp,
                                     Sweep acc)
        {
            BasisMocapJoint jS = isLeft ? BasisMocapJoint.LeftUpperArm : BasisMocapJoint.RightUpperArm;
            BasisMocapJoint jE = isLeft ? BasisMocapJoint.LeftLowerArm : BasisMocapJoint.RightLowerArm;
            BasisMocapJoint jH = isLeft ? BasisMocapJoint.LeftHand : BasisMocapJoint.RightHand;

            // THE AVATAR: its shoulder, its bone lengths, its bind pose.
            Vector3 shoulder = avatar.Get(f, jS).Position;
            float armLen = BasisClipProportion.ArmLength(avatar, f, isLeft);

            // THE USER: the controller reports where the HUMAN's hand is. This is the whole experiment.
            Vector3 target = human.Get(f, jH).Position;
            Quaternion targetRot = human.Get(f, jH).Rotation;
            Vector3 humanElbow = human.Get(f, jE).Position;

            if (!limb.Seeded)
            {
                // ⚠ SEEDED FROM THE AVATAR. Seed this from `human` and the limb silently takes the human's
                // bone lengths, the mismatch disappears, and the sweep reports a reassuring flat line.
                SeedLimb(ref limb, chestRot, shoulder, avatar.Get(f, jE).Position, avatar.Get(f, jH).Position,
                         avatar.Get(f, jS).Rotation, avatar.Get(f, jE).Rotation, avatar.Get(f, jH).Rotation);
            }

            AnimatedPose(limb, chestRot, shoulder, out Quaternion rootRot, out Quaternion midRot, out Quaternion tipRot,
                         out Vector3 elbow, out Vector3 hand);

            BasisArmSolveInput i = default;
            i.Shoulder = shoulder;
            i.Elbow = elbow;
            i.Hand = hand;
            i.RootRotation = rootRot;
            i.MidRotation = midRot;
            i.TipRotation = tipRot;               // ElbowField feeds the animated wrist (wrist-roll relief)
            i.TargetPosition = target;
            i.TargetRotation = targetRot;
            i.TargetOffset = Quaternion.identity;
            i.PlayerUp = playerUp;
            i.HintMaxStepDeg = float.MaxValue;

            if (frame.Valid && BasisSwivelHintCore.ArmHint(frame, shoulder, target, armLen, isLeft,
                                                           out Vector3 hint, out _))
            {
                i.HintPosition = hint;
                i.HintWeight = true;
                i.HintIsTracker = false;
            }
            else
            {
                acc.NoArmHint++;
            }

            BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);

            acc.ArmSamples++;
            acc.AvatarArmLen = armLen;

            if (!Finite(r.ElbowSolved) || !Finite(r.HandSolved)) acc.NonFinite++;

            float dist = Vector3.Distance(target, shoulder);
            float reach = armLen > 1e-5f ? dist / armLen : float.NaN;
            acc.ArmReachMax = Mathf.Max(acc.ArmReachMax, reach);
            if (reach > 1f) acc.ArmBeyondReach++;

            // SATURATION: in reach, land on it; past reach, extend straight and fall exactly short.
            // Recorded in BOTH units at the sample site -- the limb-length fraction is what the assert reads,
            // and computing it here rather than dividing an aggregate later keeps every sample measured
            // against its own arm.
            float solvedArm = r.UpperLength + r.LowerLength;
            float expected = Mathf.Max(0f, dist - solvedArm);
            float dev = Mathf.Abs(Vector3.Distance(r.HandSolved, target) - expected);
            acc.HandShortfallDevMax = Mathf.Max(acc.HandShortfallDevMax, dev);
            if (solvedArm > 1e-5f)
                acc.HandShortfallDevMaxFrac = Mathf.Max(acc.HandShortfallDevMaxFrac, dev / solvedArm);

            float elbowErr = Vector3.Distance(r.ElbowSolved, humanElbow);
            acc.ElbowErr.Add(elbowErr);

            // The geometric floor: the avatar's elbow is confined to a sphere of radius UpperLength about the
            // shoulder, so it cannot get closer to the human's elbow than this. Not the solver's fault.
            // ⚠ IT IS THE LOOSER OF THE TWO FLOORS -- the elbow is really on a CIRCLE, not the whole sphere.
            // Kept because it is what the historical numbers were quoted against; read the circle floor below.
            acc.ElbowIrreducibleSum += Mathf.Abs(Vector3.Distance(humanElbow, shoulder) - r.UpperLength);

            // ⭐ THE SWIVEL DECOMPOSITION. Every remaining degree of freedom this solver has is a rotation
            // about the shoulder->hand axis -- the hint, the pole-collapse stabilizer, the wrist-roll relief
            // and the anatomy guard ALL swivel about exactly that line -- so the set of elbow positions the
            // solve could have produced is precisely the circle of radius `radius` about it. Measuring the
            // ANGLE around that circle is measuring the solve; measuring the centimetre is measuring the
            // circle's radius, which the avatar's arm length controls. See the test header.
            Vector3 swivelAxis = r.HandSolved - shoulder;
            float axisLen = swivelAxis.magnitude;
            if (!(axisLen > 1e-5f)) { acc.ElbowSwivelDegenerate++; return; }
            Vector3 axisN = swivelAxis / axisLen;

            Vector3 toElbow = r.ElbowSolved - shoulder;
            float elbowAlong = Vector3.Dot(toElbow, axisN);
            Vector3 elbowPerp = toElbow - axisN * elbowAlong;
            float radius = elbowPerp.magnitude;

            Vector3 toHuman = humanElbow - shoulder;
            float humanAlong = Vector3.Dot(toHuman, axisN);
            Vector3 humanPerp = toHuman - axisN * humanAlong;
            float humanRadius = humanPerp.magnitude;

            float dAlong = humanAlong - elbowAlong;
            float dRadial = humanRadius - radius;
            acc.ElbowCircleFloorSum += Mathf.Sqrt(dAlong * dAlong + dRadial * dRadial);
            acc.ElbowRadiusSum += radius;

            // The angle is undefined when either lever collapses onto the axis. Counted, never faked -- a
            // swivel error averaged over samples that had no swivel would be exactly the kind of quietly
            // meaningless number the header is about.
            if (!(radius > 1e-5f) || !(humanRadius > 1e-5f)) { acc.ElbowSwivelDegenerate++; acc.ElbowSwivelSamples++; return; }

            float cosSwivel = Mathf.Clamp(Vector3.Dot(elbowPerp, humanPerp) / (radius * humanRadius), -1f, 1f);
            float swivelErrRad = Mathf.Acos(cosSwivel);
            acc.ElbowSwivelErrDeg.Add(swivelErrRad * Mathf.Rad2Deg);
            acc.ElbowChordSum += 2f * Mathf.Sqrt(humanRadius * radius) * Mathf.Sin(0.5f * swivelErrRad);
            acc.ElbowSwivelSamples++;
        }

        /// <summary>
        /// The leg's half, wired as BasisFullBodyIK.SolveLegs wires the LIVE untracked knee: the model hint
        /// at full weight with the confidence spent as POLE DISTRUST (never as a weight fade -- a fade is
        /// discontinuous at zero and that jump IS the pop it was meant to avoid), over the hips-right bend
        /// normal. See the class header for what this driver does NOT mirror.
        /// </summary>
        static void SolveLegMismatch(BasisMotionClip human, BasisMotionClip avatar, int f, bool isLeft,
                                     ref Limb limb, in BasisSwivelFrame frame, Quaternion hipsRot, Sweep acc)
        {
            BasisMocapJoint jHip = isLeft ? BasisMocapJoint.LeftUpperLeg : BasisMocapJoint.RightUpperLeg;
            BasisMocapJoint jKnee = isLeft ? BasisMocapJoint.LeftLowerLeg : BasisMocapJoint.RightLowerLeg;
            BasisMocapJoint jFoot = isLeft ? BasisMocapJoint.LeftFoot : BasisMocapJoint.RightFoot;

            Vector3 hip = avatar.Get(f, jHip).Position;
            float legLen = BasisClipProportion.LegLength(avatar, f, isLeft);

            Vector3 target = human.Get(f, jFoot).Position;
            Quaternion targetRot = human.Get(f, jFoot).Rotation;
            Vector3 humanKnee = human.Get(f, jKnee).Position;

            if (!limb.Seeded)
            {
                SeedLimb(ref limb, hipsRot, hip, avatar.Get(f, jKnee).Position, avatar.Get(f, jFoot).Position,
                         avatar.Get(f, jHip).Rotation, avatar.Get(f, jKnee).Rotation, avatar.Get(f, jFoot).Rotation);
            }

            AnimatedPose(limb, hipsRot, hip, out Quaternion rootRot, out Quaternion midRot, out _,
                         out Vector3 knee, out Vector3 foot);

            BasisLegSolveInput i = default;
            i.Root = hip;
            i.Mid = knee;
            i.Tip = foot;
            i.RootRotation = rootRot;
            i.MidRotation = midRot;
            i.TargetPosition = target;
            i.TargetRotation = targetRot;
            i.TargetOffset = Quaternion.identity;
            i.BendNormal = hipsRot * Vector3.right;   // the runtime's no-tracker knee bend normal

            if (frame.Valid && BasisSwivelHintCore.LegHint(frame, hip, target, legLen, isLeft,
                                                           out Vector3 hint, out float conf))
            {
                i.HintPosition = hint;
                i.HintWeight = 1f;
                i.HintDistrust = 1f - BasisSwivelHintCore.LegModelTrust(conf);
            }
            else
            {
                acc.NoLegHint++;
            }

            BasisLegSolveCore.Solve(i, out BasisLegSolveResult r);

            acc.LegSamples++;
            acc.AvatarLegLen = legLen;

            if (!Finite(r.KneeSolved) || !Finite(r.FootSolved)) acc.NonFinite++;

            float dist = Vector3.Distance(target, hip);
            float reach = legLen > 1e-5f ? dist / legLen : float.NaN;
            acc.LegReachMax = Mathf.Max(acc.LegReachMax, reach);
            if (reach > 1f) acc.LegBeyondReach++;

            // Reported, never gated: the leg's weight-scaled quaternion hint is documented as NOT
            // reach-preserving, so foot slip here is a solver property to be measured. (BasisMocapAccuracy.Gate
            // draws the same distinction between the hand, which it asserts, and the foot, which it bounds.)
            float solvedLeg = r.UpperLength + r.LowerLength;
            float footErr = Vector3.Distance(r.FootSolved, target);
            acc.FootErrMax = Mathf.Max(acc.FootErrMax, footErr);
            if (solvedLeg > 1e-5f)
                acc.FootErrMaxFrac = Mathf.Max(acc.FootErrMaxFrac, footErr / solvedLeg);

            acc.KneeErr.Add(Vector3.Distance(r.KneeSolved, humanKnee));
            acc.KneeIrreducibleSum += Mathf.Abs(Vector3.Distance(humanKnee, hip) - r.UpperLength);
        }

        // ================================================================== scaffolding

        /// <summary>The sweep grid: one group varied at a time, with the shared 1.0x/1.0x row emitted once.</summary>
        static IEnumerable<BasisProportionScale> SweepPoints()
        {
            foreach (float a in k_ArmScales) yield return BasisProportionScale.ArmOnly(a);
            foreach (float l in k_LegScales)
            {
                if (l == 1f) continue;   // the matched row is already emitted by the arm sweep
                yield return BasisProportionScale.LegOnly(l);
            }
        }

        // Same construction BasisMocapAccuracy's ElbowField path and BasisFullBodyIK.BuildArmFrame use:
        // shoulder line for RIGHT, chest->neck for UP. Positions, never bone rotations -- a bone's local axes
        // are a rig convention and do not transfer; anatomy does.
        static BasisSwivelFrame ArmFrame(BasisMotionClip c, int f) => BasisSwivelHintCore.BuildFrame(
            c.Get(f, BasisMocapJoint.LeftUpperArm).Position, c.Get(f, BasisMocapJoint.RightUpperArm).Position,
            c.Get(f, BasisMocapJoint.Chest).Position, c.Get(f, BasisMocapJoint.Neck).Position);

        // BasisFullBodyIK.BuildLegFrame: the leg's frame hangs off the PELVIS, not the chest.
        static BasisSwivelFrame LegFrame(BasisMotionClip c, int f) => BasisSwivelHintCore.BuildFrame(
            c.Get(f, BasisMocapJoint.LeftUpperLeg).Position, c.Get(f, BasisMocapJoint.RightUpperLeg).Position,
            c.Get(f, BasisMocapJoint.Hips).Position, c.Get(f, BasisMocapJoint.Chest).Position);

        static void Saturates(List<string> failures, string where, string what, float3 a, float3 b, float3 c)
        {
            if (Same(a, b) && Same(a, c)) return;
            failures.Add($"{where}: {what} EXTRAPOLATED past the fit domain -- 1x/2x/4x overshoot in the same " +
                         $"direction gave {a} / {b} / {c}. Multiplying by a power of two is exact in IEEE " +
                         "floating point, so with the domain clamp intact these are bit-identical.");
        }

        static bool Same(float3 a, float3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        static bool ExactlyEqual(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        static bool ExactlyEqual(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        static void Field(List<string> failures, string clip, BasisMocapHintSource hint, string name, float a, float b)
        {
            // NaN == NaN is false, but ElbowMeanFracArm is legitimately NaN on a degenerate arm, and "both NaN"
            // is the identity we want there.
            if (a == b || (float.IsNaN(a) && float.IsNaN(b))) return;
            failures.Add($"{clip} [{hint}] {name}: baseline {a:R} != 1.0x rescale {b:R}");
        }

        static void Field(List<string> failures, string clip, BasisMocapHintSource hint, string name, int a, int b)
        {
            if (a == b) return;
            failures.Add($"{clip} [{hint}] {name}: baseline {a} != 1.0x rescale {b}");
        }

        static void Ratio(List<string> failures, string what, float got, float from, float expected, float tol)
        {
            if (from <= 1e-6f) return;
            if (tol == 0f)
            {
                // The untouched case: EXACT, because a bone whose group scales by 1.0f contributes exactly 0f
                // to the displacement and its root's displacement cancels in the difference.
                if (got != from) failures.Add($"{what}: length changed {from:R} -> {got:R} but nothing should have touched it");
                return;
            }
            float ratio = got / from;
            if (!(Mathf.Abs(ratio - expected) <= tol))
                failures.Add($"{what}: length ratio {ratio:F7}, expected {expected:F7}");
        }

        static void Exact(List<string> failures, string what, BasisMotionClip got, BasisMotionClip from, int f, BasisMocapJoint j)
        {
            Vector3 a = from.Get(f, j).Position, b = got.Get(f, j).Position;
            if (!ExactlyEqual(a, b)) failures.Add($"{what} {j}: moved {a:F9} -> {b:F9} but should not have moved at all");
        }

        static float Mean(List<float> v) { float t = 0f; for (int i = 0; i < v.Count; i++) t += v[i]; return v.Count > 0 ? t / v.Count : float.NaN; }

        static float Pct95(List<float> v)
        {
            if (v.Count == 0) return float.NaN;
            var copy = new List<float>(v);
            copy.Sort();
            return copy[Mathf.Clamp(Mathf.RoundToInt(0.95f * (copy.Count - 1)), 0, copy.Count - 1)];
        }

        static float Pct(int n, int of) => of > 0 ? 100f * n / of : float.NaN;

        static string Cm(float m) => (m * 100f).ToString("F2");

        static string Delta(float m, float baseline)
        {
            float d = (m - baseline) * 100f;
            return (d >= 0f ? "+" : "") + d.ToString("F2");
        }

        static List<string> Head(List<string> all, int n) => all.Count <= n ? all : all.GetRange(0, n);
    }
}
