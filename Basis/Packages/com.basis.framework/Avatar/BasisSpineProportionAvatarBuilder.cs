using System.Collections.Generic;
using UnityEngine;

namespace Basis.Scripts.Avatar
{
    /// <summary>
    /// Rebuilds a humanoid <see cref="Avatar"/> at runtime with the spine bones re-spaced by a small factor,
    /// so the wearer's avatar torso matches their real torso (see BasisSpineProportionCore). Unity humanoid
    /// forbids changing a posed bone's local transform at runtime -- the only way to lengthen a segment and
    /// have it render under a live Animator is to bake it into the Avatar definition. This edits the spine
    /// bones' local positions on the (T-posed) hierarchy, then rebuilds the Avatar from that pose via
    /// <see cref="AvatarBuilder.BuildHumanAvatar"/>, preserving the source avatar's muscle settings and limits.
    /// The caller MUST have the hierarchy in T-pose on entry (BuildHumanAvatar bakes the current local
    /// transforms as the new bind) and must reassign <c>animator.avatar</c> to the result and re-run the
    /// avatar calibration (rig rebuild + T-pose recapture) afterwards.
    /// </summary>
    public static class BasisSpineProportionAvatarBuilder
    {
        static readonly HumanBodyBones[] SpineBones =
        {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
        };

        /// <summary>
        /// Builds a new humanoid Avatar identical to the animator's current one except the spine bones' bind
        /// positions are scaled by <paramref name="scale"/> about the hips. Returns false and leaves the
        /// hierarchy untouched on any failure (so the caller keeps the original avatar). The hierarchy must be
        /// in T-pose on entry.
        /// </summary>
        public static bool TryRebuildScaledSpine(Animator animator, float scale, out UnityEngine.Avatar newAvatar)
        {
            newAvatar = null;
            if (animator == null || animator.avatar == null || !animator.isHuman || !(scale > 0f))
            {
                return false;
            }

            HumanDescription src = animator.avatar.humanDescription;

            // Scale the spine bones' local positions on the live T-posed hierarchy. Snapshot first so a failed
            // build can restore the exact original values.
            var edited = new List<(Transform t, Vector3 local)>(SpineBones.Length);
            for (int i = 0; i < SpineBones.Length; i++)
            {
                Transform t = animator.GetBoneTransform(SpineBones[i]);
                if (t != null)
                {
                    edited.Add((t, t.localPosition));
                    t.localPosition *= scale;
                }
            }

            HumanDescription hd = new HumanDescription
            {
                upperArmTwist = src.upperArmTwist,
                lowerArmTwist = src.lowerArmTwist,
                upperLegTwist = src.upperLegTwist,
                lowerLegTwist = src.lowerLegTwist,
                armStretch = src.armStretch,
                legStretch = src.legStretch,
                feetSpacing = src.feetSpacing,
                hasTranslationDoF = src.hasTranslationDoF,
                human = BuildHuman(src, animator),
                skeleton = BuildSkeleton(animator.transform),
            };

            UnityEngine.Avatar built = AvatarBuilder.BuildHumanAvatar(animator.gameObject, hd);
            if (built == null || !built.isValid || !built.isHuman)
            {
                for (int i = 0; i < edited.Count; i++)
                {
                    edited[i].t.localPosition = edited[i].local;
                }
                if (built != null)
                {
                    Object.Destroy(built);
                }
                return false;
            }

            newAvatar = built;
            return true;
        }

        // Prefer the source avatar's human array -- it carries the exact bone-name map AND the per-bone muscle
        // limits, so the rebuilt avatar keeps the original range of motion. Reconstruct with default limits
        // only if the runtime humanDescription getter returned an empty/partial array (version-dependent).
        static HumanBone[] BuildHuman(HumanDescription src, Animator animator)
        {
            if (src.human != null && src.human.Length > 0)
            {
                return src.human;
            }

            var list = new List<HumanBone>((int)HumanBodyBones.LastBone);
            for (int b = 0; b < (int)HumanBodyBones.LastBone; b++)
            {
                Transform t = animator.GetBoneTransform((HumanBodyBones)b);
                if (t == null)
                {
                    continue;
                }
                HumanBone hb = new HumanBone { humanName = HumanTrait.BoneName[b], boneName = t.name };
                hb.limit.useDefaultValues = true;
                list.Add(hb);
            }
            return list.ToArray();
        }

        // Every transform in the rig (including the root, which the humanoid build requires) with its current
        // local TRS -- this pose, with the spine already scaled above, becomes the new bind.
        static SkeletonBone[] BuildSkeleton(Transform root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            SkeletonBone[] bones = new SkeletonBone[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                bones[i] = new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                };
            }
            return bones;
        }
    }
}
