#if UNITY_EDITOR
using System.Globalization;
using UnityEngine;

namespace Basis.IK.Mocap
{
    /// <summary>
    /// THE MOCAP SUBJECT IS WEARING THEIR OWN BODY. A VR USER NEVER IS.
    ///
    /// Every accuracy number this project quotes was measured with a CMU subject driving THEIR OWN
    /// proportions: the hand target and the arm it hangs off came from the same skeleton, so
    /// |shoulder->hand| / armLength &lt;= 1 on 100% of frames. That is not what the runtime is handed. The
    /// live rig gets the RAW CONTROLLER TARGET -- where the USER's hand is -- against whatever arm the
    /// AVATAR happens to have. Anyone whose real arms are longer than their avatar's is beyond that ratio
    /// permanently, and that gap has already shipped a bug: the swivel polynomials had no domain clamp, so
    /// out there a 3rd-order fit with coefficients up to 15 was a random number generator, and the elbows
    /// went up by the ears in a headset while every test in the suite stayed green (see
    /// BasisLegSwivelModel's THE DOMAIN CLAMP block and BasisSwivelHintConformanceTests' header).
    ///
    /// This file manufactures the missing input. It takes a clip and produces a PROPORTION-RESCALED
    /// VARIANT: the same motion performed by a different body. Pair the rescaled clip's SKELETON with the
    /// original clip's HAND AND FOOT TARGETS and the harness is finally driving the solver the way the
    /// runtime drives it -- one body's limbs chasing another body's effectors.
    ///
    /// WHAT "RESCALE" MEANS HERE, EXACTLY
    ///   Every bone keeps its WORLD DIRECTION and changes its LENGTH. Joint positions are rebuilt outward
    ///   from the hips along the source clip's own bone vectors, each multiplied by its group's scale.
    ///   Because no direction moves, every joint's world ROTATION is unchanged and is copied through
    ///   verbatim -- the pose is preserved, only the skeleton's dimensions differ. That is the property the
    ///   whole measurement rests on: if the rescale perturbed the motion, every mismatch number taken from
    ///   it would be measuring the perturbation instead of the mismatch.
    ///
    /// BIT-EXACT AT 1.0x, AND NOT BY SHORT-CIRCUITING
    ///   The rebuild accumulates a DISPLACEMENT (`delta`), not an absolute position:
    ///       delta[child] = delta[parent] + bone * (scale - 1)
    ///       newPos[j]    = srcPos[j] + delta[j]
    ///   At scale 1.0 the factor is exactly 0f, so every delta stays exactly zero and every position comes
    ///   back exactly equal to the source -- while the full code path still RUNS. The obvious alternative,
    ///   `newPos[child] = newPos[parent] + bone * scale`, is NOT exact at 1.0 (a + (b - a) != b in
    ///   floating point), and an `if (IsIdentity) return copy;` early-out would make the identity test
    ///   guard nothing at all. This project has already shipped two metrics that were quietly lying -- a
    ///   pop detector whose count was identical whether its cause was present or absent, and a smoothness
    ///   score that preferred over-smoothed mush to a real human -- so the ruler gets guarded, and the
    ///   guard has to be able to fail.
    ///
    /// THE PELVIS DOES NOT MOVE, AND THE CLIP IS NOT RE-GROUNDED. Shortening the legs leaves the hips where
    /// the human's hips were, so the feet float. That is deliberate and it is stated rather than silently
    /// fixed: nothing this file's consumers measure is floor-relative (BasisBvhLoader.Validate is entirely
    /// relative/rotational, BasisMocapAccuracy takes every root from the clip and every error as a
    /// distance), and a grounding pass would be an unmeasured extra transform sitting between the corpus
    /// and the numbers. A floor-contact consumer must ground it itself.
    ///
    /// EDITOR-ONLY, IN THE RUNTIME ASSEMBLY, for the reason written into BasisBvhLoader's header: a mocap
    /// analysis layer has no business in a player build, but Basis.Framework.IK.Tests does not reference
    /// BasisEditor, so putting the maths there would force the tests to hand-mirror it -- the drift trap
    /// the whole core/sweep/gate convention exists to avoid.
    /// </summary>
    public enum BasisSegmentGroup
    {
        /// <summary>The hips. No bone ends here, so no scale applies.</summary>
        Root,

        /// <summary>Spine chain, neck, head, the clavicles, AND the two offsets that place the limb ROOTS
        /// (hips->upper leg, shoulder->upper arm). Those two are torso WIDTH, not limb length: scaling them
        /// with the arm would move the shoulder joint and confound "the avatar's arm is short" with "the
        /// avatar's shoulders are narrow", which are different mismatches with different consequences.</summary>
        Torso,

        /// <summary>Upper arm and forearm -- exactly the two bones whose sum BasisMocapAccuracy calls
        /// `armLen` and divides the reach ratio by.</summary>
        Arm,

        /// <summary>Thigh, shin and foot. The first two are exactly the pair BasisMocapAccuracy sums into
        /// `legLen`; the toe bone rides along because a leg that shrinks with adult-sized feet is not a
        /// body anyone ships.</summary>
        Leg,
    }

    /// <summary>
    /// How much bigger the AVATAR's segments are than the HUMAN's, per group. 1.0 is a perfectly matched
    /// avatar -- the (fictional) case every existing accuracy number was measured in.
    /// </summary>
    public struct BasisProportionScale
    {
        public float Arm;
        public float Leg;
        public float Torso;

        public static BasisProportionScale Identity => new BasisProportionScale { Arm = 1f, Leg = 1f, Torso = 1f };

        public static BasisProportionScale Of(float arm, float leg, float torso)
            => new BasisProportionScale { Arm = arm, Leg = leg, Torso = torso };

        /// <summary>Vary ONE group and hold the rest matched -- the shape the mismatch sweep wants, because
        /// a full cross product confounds two causes in every cell.</summary>
        public static BasisProportionScale ArmOnly(float arm) => Of(arm, 1f, 1f);
        public static BasisProportionScale LegOnly(float leg) => Of(1f, leg, 1f);
        public static BasisProportionScale TorsoOnly(float torso) => Of(1f, 1f, torso);

        public bool IsIdentity => Arm == 1f && Leg == 1f && Torso == 1f;

        public float For(BasisSegmentGroup group)
        {
            switch (group)
            {
                case BasisSegmentGroup.Arm: return Arm;
                case BasisSegmentGroup.Leg: return Leg;
                case BasisSegmentGroup.Torso: return Torso;
                default: return 1f;   // Root: no bone ends at the hips
            }
        }

        /// <summary>Filename-safe tag in whole percent, e.g. "a080l100t100". Deliberately not a decimal
        /// string: these end up in CSV paths.</summary>
        public string Suffix => string.Format(CultureInfo.InvariantCulture, "a{0:000}l{1:000}t{2:000}",
            Mathf.RoundToInt(Arm * 100f), Mathf.RoundToInt(Leg * 100f), Mathf.RoundToInt(Torso * 100f));

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "arm x{0:0.00} / leg x{1:0.00} / torso x{2:0.00}", Arm, Leg, Torso);
    }

    public static class BasisClipProportion
    {
        /// <summary>
        /// The humanoid parent of each joint, in BasisMocapJoint's own indexing. Reconstructed from the CMU
        /// hierarchy BasisBvhLoader maps (Hips -> LowerBack -> Spine -> Spine1 -> Neck -> Head, with
        /// Spine1 -> LeftShoulder -> LeftArm -> LeftForeArm -> LeftHand and Hips -> LeftUpLeg -> ...);
        /// CMU's LHipJoint / RHipJoint / Neck1 are structural in-betweens that the loader does not map, so
        /// the bone recorded here spans them. That is exactly right for a proportion rescale: it is the
        /// MAPPED joint-to-joint offset that determines the limb lengths the solver reads back.
        ///
        /// BasisMocapJoint.Count marks a root (only the hips).
        /// </summary>
        static readonly BasisMocapJoint[] k_Parent =
        {
            /* Hips          */ BasisMocapJoint.Count,
            /* Spine         */ BasisMocapJoint.Hips,
            /* Chest         */ BasisMocapJoint.Spine,
            /* UpperChest    */ BasisMocapJoint.Chest,
            /* Neck          */ BasisMocapJoint.UpperChest,
            /* Head          */ BasisMocapJoint.Neck,
            /* LeftShoulder  */ BasisMocapJoint.UpperChest,
            /* LeftUpperArm  */ BasisMocapJoint.LeftShoulder,
            /* LeftLowerArm  */ BasisMocapJoint.LeftUpperArm,
            /* LeftHand      */ BasisMocapJoint.LeftLowerArm,
            /* RightShoulder */ BasisMocapJoint.UpperChest,
            /* RightUpperArm */ BasisMocapJoint.RightShoulder,
            /* RightLowerArm */ BasisMocapJoint.RightUpperArm,
            /* RightHand     */ BasisMocapJoint.RightLowerArm,
            /* LeftUpperLeg  */ BasisMocapJoint.Hips,
            /* LeftLowerLeg  */ BasisMocapJoint.LeftUpperLeg,
            /* LeftFoot      */ BasisMocapJoint.LeftLowerLeg,
            /* LeftToes      */ BasisMocapJoint.LeftFoot,
            /* RightUpperLeg */ BasisMocapJoint.Hips,
            /* RightLowerLeg */ BasisMocapJoint.RightUpperLeg,
            /* RightFoot     */ BasisMocapJoint.RightLowerLeg,
            /* RightToes     */ BasisMocapJoint.RightFoot,
        };

        /// <summary>
        /// Which group the bone ENDING at each joint belongs to -- so the arm scale reaches exactly
        /// shoulder->elbow and elbow->hand, and nothing else. Getting this wrong is the one corruption the
        /// identity test at 1.0x provably CANNOT catch (every scale is 1, so every group behaves alike), so
        /// it gets its own guard in BasisProportionMismatchTests.
        /// </summary>
        static readonly BasisSegmentGroup[] k_Group =
        {
            /* Hips          */ BasisSegmentGroup.Root,
            /* Spine         */ BasisSegmentGroup.Torso,
            /* Chest         */ BasisSegmentGroup.Torso,
            /* UpperChest    */ BasisSegmentGroup.Torso,
            /* Neck          */ BasisSegmentGroup.Torso,
            /* Head          */ BasisSegmentGroup.Torso,
            /* LeftShoulder  */ BasisSegmentGroup.Torso,   // clavicle: shoulder WIDTH, not arm length
            /* LeftUpperArm  */ BasisSegmentGroup.Torso,   // places the glenohumeral joint -- still width
            /* LeftLowerArm  */ BasisSegmentGroup.Arm,     // upper arm
            /* LeftHand      */ BasisSegmentGroup.Arm,     // forearm
            /* RightShoulder */ BasisSegmentGroup.Torso,
            /* RightUpperArm */ BasisSegmentGroup.Torso,
            /* RightLowerArm */ BasisSegmentGroup.Arm,
            /* RightHand     */ BasisSegmentGroup.Arm,
            /* LeftUpperLeg  */ BasisSegmentGroup.Torso,   // pelvis half-width
            /* LeftLowerLeg  */ BasisSegmentGroup.Leg,     // thigh
            /* LeftFoot      */ BasisSegmentGroup.Leg,     // shin
            /* LeftToes      */ BasisSegmentGroup.Leg,     // foot
            /* RightUpperLeg */ BasisSegmentGroup.Torso,
            /* RightLowerLeg */ BasisSegmentGroup.Leg,
            /* RightFoot     */ BasisSegmentGroup.Leg,
            /* RightToes     */ BasisSegmentGroup.Leg,
        };

        public static BasisMocapJoint ParentOf(BasisMocapJoint joint) => k_Parent[(int)joint];

        public static BasisSegmentGroup GroupOf(BasisMocapJoint joint) => k_Group[(int)joint];

        /// <summary>
        /// Every parent must sit EARLIER in BasisMocapJoint than its child, because the rebuild is a single
        /// forward pass that reads delta[parent] before writing delta[child]. It holds today by the enum's
        /// own ordering; a future reorder would silently produce a skeleton assembled against stale
        /// displacements, which would look like plausible motion and be wrong. Cheap to check, so it is
        /// checked rather than assumed.
        /// </summary>
        public static bool HierarchyIsTopological()
        {
            for (int j = 0; j < (int)BasisMocapJoint.Count; j++)
            {
                BasisMocapJoint parent = k_Parent[j];
                if (parent == BasisMocapJoint.Count) continue;
                if ((int)parent >= j) return false;
            }
            return true;
        }

        /// <summary>
        /// The same motion on a differently-proportioned body. Rotations, frame count, frame time and the
        /// loader's SourceToMetres are carried through untouched; only positions move, and only because
        /// their bones changed length.
        ///
        /// The returned clip's Name carries the scale suffix ALWAYS, including at 1.0x. No special case:
        /// the identity guard compares the accuracy summary's numeric fields one by one and says so, which
        /// is stronger than letting a name match paper over a field it forgot to check.
        /// </summary>
        public static BasisMotionClip Rescale(BasisMotionClip source, in BasisProportionScale scale)
        {
            if (source == null) throw new System.ArgumentNullException(nameof(source));
            Require(scale.Arm, "arm");
            Require(scale.Leg, "leg");
            Require(scale.Torso, "torso");

            int jointCount = (int)BasisMocapJoint.Count;
            var clip = new BasisMotionClip
            {
                Name = source.Name + "_" + scale.Suffix,
                FrameTime = source.FrameTime,
                FrameCount = source.FrameCount,
                SourceToMetres = source.SourceToMetres,
                Poses = new BasisMocapPose[source.Poses.Length],
            };

            // Reused across frames: every entry is written before it is read (the hips seed it at j == 0,
            // and the topology guarantees every other parent is already done).
            var delta = new Vector3[jointCount];

            for (int f = 0; f < source.FrameCount; f++)
            {
                int b = f * jointCount;
                for (int j = 0; j < jointCount; j++)
                {
                    BasisMocapPose pose = source.Poses[b + j];
                    BasisMocapJoint parent = k_Parent[j];

                    if (parent == BasisMocapJoint.Count)
                    {
                        // The hips do not move. See the header: no re-grounding, deliberately.
                        delta[j] = Vector3.zero;
                    }
                    else
                    {
                        int pi = (int)parent;
                        BasisMocapPose parentPose = source.Poses[b + pi];
                        if (!pose.Valid || !parentPose.Valid)
                        {
                            // An unmapped joint carries a default (zero) position, so its "bone" is
                            // meaningless. Inherit the parent's displacement so the CHILDREN of a gap still
                            // land somewhere coherent, and leave Valid false so consumers still know.
                            delta[j] = delta[pi];
                        }
                        else
                        {
                            Vector3 bone = pose.Position - parentPose.Position;
                            // (scale - 1f) and NOT a rebuild from the new parent position: at scale 1.0 this
                            // factor is exactly 0f, so the whole chain of deltas stays exactly zero and the
                            // output is bit-identical to the input WITH THE CODE PATH STILL RUNNING.
                            delta[j] = delta[pi] + bone * (scale.For(k_Group[j]) - 1f);
                        }
                    }

                    pose.Position += delta[j];
                    clip.Poses[b + j] = pose;   // Rotation and Valid ride through the struct copy untouched
                }
            }

            return clip;
        }

        /// <summary>Convenience overload for call sites that read better spelled out.</summary>
        public static BasisMotionClip Rescale(BasisMotionClip source, float armScale, float legScale, float torsoScale)
            => Rescale(source, BasisProportionScale.Of(armScale, legScale, torsoScale));

        /// <summary>
        /// Shoulder->elbow + elbow->hand. Transcribed from BasisMocapAccuracy.SolveArm's `armLen` so the
        /// reach ratio computed here is the SAME number the solver and BasisSwivelHintCore.Features divide
        /// by -- a second definition of "arm length" is exactly how a domain measurement ends up off by the
        /// clavicle and nobody notices.
        /// </summary>
        public static float ArmLength(BasisMotionClip clip, int frame, bool isLeft)
        {
            BasisMocapJoint s = isLeft ? BasisMocapJoint.LeftUpperArm : BasisMocapJoint.RightUpperArm;
            BasisMocapJoint e = isLeft ? BasisMocapJoint.LeftLowerArm : BasisMocapJoint.RightLowerArm;
            BasisMocapJoint h = isLeft ? BasisMocapJoint.LeftHand : BasisMocapJoint.RightHand;
            return Vector3.Distance(clip.Get(frame, s).Position, clip.Get(frame, e).Position)
                 + Vector3.Distance(clip.Get(frame, e).Position, clip.Get(frame, h).Position);
        }

        /// <summary>Hip->knee + knee->ankle. Transcribed from BasisMocapAccuracy.SolveLeg's `legLen`.</summary>
        public static float LegLength(BasisMotionClip clip, int frame, bool isLeft)
        {
            BasisMocapJoint hip = isLeft ? BasisMocapJoint.LeftUpperLeg : BasisMocapJoint.RightUpperLeg;
            BasisMocapJoint knee = isLeft ? BasisMocapJoint.LeftLowerLeg : BasisMocapJoint.RightLowerLeg;
            BasisMocapJoint foot = isLeft ? BasisMocapJoint.LeftFoot : BasisMocapJoint.RightFoot;
            return Vector3.Distance(clip.Get(frame, hip).Position, clip.Get(frame, knee).Position)
                 + Vector3.Distance(clip.Get(frame, knee).Position, clip.Get(frame, foot).Position);
        }

        static void Require(float scale, string what)
        {
            // Reject-unless-good, the house NaN convention: NaN fails every ordered comparison, so
            // `!(x > 0)` rejects it where `x <= 0` would wave it straight through into the skeleton.
            if (!(scale > 0f) || float.IsInfinity(scale))
            {
                throw new System.ArgumentOutOfRangeException(what,
                    $"{what} scale must be finite and positive; got {scale.ToString(CultureInfo.InvariantCulture)}");
            }
        }
    }
}
#endif
