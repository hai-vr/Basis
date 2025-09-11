using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Scripts.Drivers
{
    /// <summary>
    /// Slim remote bone driver:
    /// Locks Neck->Head, Chest->Neck, Spine->Chest, Hips->Spine, CenterEye->Head, Mouth->Head.
    /// Head follows its incoming/tracker data (source of truth).
    /// </summary>
    [System.Serializable]
    public class BasisRemoteBoneDriver
    {
        // Source of truth
        public BasisRemotePlayer RemotePlayer { get; private set; }
        // Only the controls we actually need
        public Bone Head, Neck, Chest, Spine, Hips, CenterEye, Mouth;
        // Cache last scale to avoid per-frame recompute
        Vector3 _lastScale = Vector3.one;
        public Transform RemotePlayerTransform;
        [System.Serializable]
        public struct Bone
        {
            public BasisCalibratedCoords IncomingData;      // filled by upstream (tracker or pose sampler)
            public BasisCalibratedCoords Outgoing;      // what we compute and hand off to the avatar
            public BasisCalibratedCoords TposeLocal;    // initial local-to-avatar in T-pose
            public BasisCalibratedCoords TposeLocalScaled;
            public float3 Offset;       // (AssignedTo.Tpose - Target.Tpose) at scale = 1
            public float3 ScaledOffset; // Offset * scale
        }
        /// <summary>
        /// Capture T-pose positions for the 7 roles we care about and compute lock offsets.
        /// Call this once after the avatar is loaded / posed in T.
        /// </summary>
        public void InitializeFromAvatar(BasisRemotePlayer remotePlayer)
        {
            RemotePlayer = remotePlayer;
            var avatar = remotePlayer.BasisAvatar;
            var animator = avatar.Animator;
            RemotePlayerTransform = animator.transform;
            // Helper: get a bone transform safely
            Transform B(HumanBodyBones b) => animator.avatar != null && animator.avatar.isHuman ? animator.GetBoneTransform(b) : null;

            // Fill T-pose locals from current world pose
            SetInitialFromWorld(RemotePlayerTransform, B(HumanBodyBones.Head), ref Head);
            SetInitialFromWorld(RemotePlayerTransform, B(HumanBodyBones.Neck), ref Neck);
            SetInitialFromWorld(RemotePlayerTransform, B(HumanBodyBones.Chest), ref Chest);
            SetInitialFromWorld(RemotePlayerTransform, B(HumanBodyBones.Spine), ref Spine);
            SetInitialFromWorld(RemotePlayerTransform, B(HumanBodyBones.Hips), ref Hips);

            // CenterEye / Mouth come from avatar’s authored points
            float3 worldEye = BasisHelpers.ConvertFromLocalSpace(BasisHelpers.AvatarPositionConversion(avatar.AvatarEyePosition), RemotePlayerTransform.position);
            SetInitialFromWorld(RemotePlayerTransform, worldEye, ref CenterEye);
            float3 worldMouth = BasisHelpers.ConvertFromLocalSpace(BasisHelpers.AvatarPositionConversion(avatar.AvatarMouthPosition), RemotePlayerTransform.position);
            SetInitialFromWorld(RemotePlayerTransform, worldMouth, ref Mouth);

            // At initialization, scaled == unscaled (scale = 1)
            CopyScaledEqualsUnscaled(ref Head);
            CopyScaledEqualsUnscaled(ref Neck);
            CopyScaledEqualsUnscaled(ref Chest);
            CopyScaledEqualsUnscaled(ref Spine);
            CopyScaledEqualsUnscaled(ref Hips);
            CopyScaledEqualsUnscaled(ref CenterEye);
            CopyScaledEqualsUnscaled(ref Mouth);

            // Compute lock offsets at scale 1
            // AssignedTo.Offset = AssignedTo.TposeScaled - Target.TposeScaled
            Neck.Offset = Neck.TposeLocalScaled.position - Head.TposeLocalScaled.position;
            Chest.Offset = Chest.TposeLocalScaled.position - Neck.TposeLocalScaled.position;
            Spine.Offset = Spine.TposeLocalScaled.position - Chest.TposeLocalScaled.position;
            Hips.Offset = Hips.TposeLocalScaled.position - Spine.TposeLocalScaled.position;
            CenterEye.Offset = CenterEye.TposeLocalScaled.position - Head.TposeLocalScaled.position;
            Mouth.Offset = Mouth.TposeLocalScaled.position - Head.TposeLocalScaled.position;

            Neck.ScaledOffset = Neck.Offset;
            Chest.ScaledOffset = Chest.Offset;
            Spine.ScaledOffset = Spine.Offset;
            Hips.ScaledOffset = Hips.Offset;
            CenterEye.ScaledOffset = CenterEye.Offset;
            Mouth.ScaledOffset = Mouth.Offset;

            _lastScale = Vector3.one;
        }

        static void SetInitialFromWorld(Transform root, Transform t, ref Bone bone)
        {
            if (t == null)
            {
                // Fallback: zeroed local, rotation identity
                bone.TposeLocal.position = Vector3.zero;
                bone.TposeLocal.rotation = quaternion.identity;
                return;
            }

            t.GetPositionAndRotation(out Vector3 wpos, out Quaternion wrot);
            // Convert world to avatar-local once and stash
            bone.TposeLocal.position = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(root, wpos);
            bone.TposeLocal.rotation = wrot; // rotation is kept as-is; driver may post-multiply elsewhere
        }

        static void SetInitialFromWorld(Transform root, float3 world, ref Bone bone)
        {
            bone.TposeLocal.position = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(root, world);
            bone.TposeLocal.rotation = quaternion.identity;
        }

        static void CopyScaledEqualsUnscaled(ref Bone b)
        {
            b.TposeLocalScaled.position = b.TposeLocal.position;
            b.TposeLocalScaled.rotation = b.TposeLocal.rotation;
        }
        /// <summary>
        /// Apply the lock chain, with optional non-uniform scale for avatar space.
        /// call every frame after you’ve updated Head.Incoming (and optionally others).
        /// </summary>
        public void SimulateAndApply(Vector3 nowScale)
        {
            // Re-scale T-pose locals and offsets only if scale changed
            if (nowScale != _lastScale)
            {
                Rescale(ref Neck, ref Head, nowScale);
                Rescale(ref Chest, ref Neck, nowScale);
                Rescale(ref Spine, ref Chest, nowScale);
                Rescale(ref Hips, ref Spine, nowScale);
                Rescale(ref CenterEye, ref Head, nowScale);
                Rescale(ref Mouth, ref Head, nowScale);
                _lastScale = nowScale;
            }
            var References = RemotePlayer.RemoteAvatarDriver.References;
            // Remove T-pose influence
            Head.Outgoing.rotation = References.TposeHead.rotation * References.head.rotation;
            Hips.Outgoing.rotation = References.TposeHips.rotation * References.Hips.rotation;

            Vector3 rrt = RemotePlayerTransform.position;

            Head.Outgoing.position = References.head.position - rrt;
            Hips.Outgoing.position = References.Hips.position - rrt;


            // 2) Apply hard locks in strict order (use target’s rotation + offset)
            // Neck locked to Head
            ApplyChildLock(ref Neck, in Head);

            // Chest locked to Neck
            ApplyChildLock(ref Chest, in Neck);

            // Spine locked to Chest
            ApplyChildLock(ref Spine, in Chest);

            // CenterEye and Mouth locked to Head
            ApplyChildLock(ref CenterEye, in Head);
            ApplyChildLock(ref Mouth, in Head);
        }

        static void ApplyChildLock(ref Bone child, in Bone target)
        {
            Quaternion R = target.Outgoing.rotation;
            Vector3 P = target.Outgoing.position;
            Vector3 off = R * (Vector3)child.ScaledOffset;

            child.Outgoing.position = P + off;
            child.Outgoing.rotation = R;
        }

        static void Rescale(ref Bone assignedTo, ref Bone target, Vector3 scale)
        {
            // TposeLocalScaled = TposeLocal .* scale (non-uniform allowed)
            assignedTo.TposeLocalScaled.position = Vector3.Scale(assignedTo.TposeLocal.position, scale);
            target.TposeLocalScaled.position = Vector3.Scale(target.TposeLocal.position, scale);

            // Offset is defined in avatar-local; scale it the same way
            assignedTo.ScaledOffset = Vector3.Scale(assignedTo.Offset, scale);
        }

        public BasisCalibratedCoords GetMouthPosition()
        {
            return Mouth.Outgoing;
        }
        public BasisCalibratedCoords GetHeadPosition()
        {
            return Head.Outgoing;
        }
        public BasisCalibratedCoords GetHipsPosition()
        {
            return Hips.Outgoing;
        }
        public Vector3 GetMouthTposePosition()
        {
            return Mouth.TposeLocalScaled.position;
        }
    }
}
