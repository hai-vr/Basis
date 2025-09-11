using System.Runtime.CompilerServices;
using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Unity.Mathematics;
using UnityEngine;
namespace Basis.Scripts.Drivers
{
    [System.Serializable]
    public class BasisRemoteBoneDriver
    {
        public BasisRemotePlayer RemotePlayer { get; private set; }
        // Only the controls we actually need
        public Bone Head, Neck, Chest, Spine, Hips, CenterEye, Mouth;
        // Cache last scale to avoid per-frame recompute
        private Vector3 _lastScale = Vector3.one;
        public Transform RemotePlayerTransform;
        [System.Serializable]
        public struct Bone
        {
            public BasisCalibratedCoords IncomingData;   // filled by upstream (tracker or pose sampler)
            public BasisCalibratedCoords Outgoing;       // what we compute and hand off to the avatar
            public BasisCalibratedCoords TposeLocal;     // initial local-to-avatar in T-pose
            public BasisCalibratedCoords TposeLocalScaled;
            public Vector3 Offset;                       // (AssignedTo.Tpose - Target.Tpose) at scale = 1
            public Vector3 ScaledOffset;                 // Offset * scale
        }
        /// <summary>
        /// Capture T-pose positions for the 7 roles we care about and compute lock offsets.
        /// Call this once after the avatar is loaded / posed in T.
        /// </summary>
        public void InitializeFromAvatar(BasisRemotePlayer remotePlayer)
        {
            RemotePlayer = remotePlayer;
            var avatar = RemotePlayer.BasisAvatar;
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

            // Compute lock offsets at scale 1  (child - parent)
            Neck.Offset = Neck.TposeLocalScaled.position - Head.TposeLocalScaled.position;
            Chest.Offset = Chest.TposeLocalScaled.position - Neck.TposeLocalScaled.position;
            Spine.Offset = Spine.TposeLocalScaled.position - Chest.TposeLocalScaled.position;
            Hips.Offset = Hips.TposeLocalScaled.position - Spine.TposeLocalScaled.position;
            CenterEye.Offset = CenterEye.TposeLocalScaled.position - Head.TposeLocalScaled.position;
            Mouth.Offset = Mouth.TposeLocalScaled.position - Head.TposeLocalScaled.position;

            // Initialize scaled offsets
            Neck.ScaledOffset = Neck.Offset;
            Chest.ScaledOffset = Chest.Offset;
            Spine.ScaledOffset = Spine.Offset;
            Hips.ScaledOffset = Hips.Offset;
            CenterEye.ScaledOffset = CenterEye.Offset;
            Mouth.ScaledOffset = Mouth.Offset;

            _lastScale = Vector3.one;
        }

        private static void SetInitialFromWorld(Transform root, Transform Trans, ref Bone bone)
        {
            if (Trans == null)
            {
                bone.TposeLocal.position = Vector3.zero;
                bone.TposeLocal.rotation = quaternion.identity;
                return;
            }
            Trans.GetPositionAndRotation(out Vector3 wpos, out bone.TposeLocal.rotation);
            // Convert world to avatar-local once and stash
            bone.TposeLocal.position = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(root, wpos);
        }
        private static void SetInitialFromWorld(Transform root, float3 world, ref Bone bone)
        {
            bone.TposeLocal.position = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(root, world);
            bone.TposeLocal.rotation = quaternion.identity;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CopyScaledEqualsUnscaled(ref Bone b)
        {
            b.TposeLocalScaled.position = b.TposeLocal.position;
            b.TposeLocalScaled.rotation = b.TposeLocal.rotation;
        }
        /// <summary>
        /// Apply the lock chain, with optional non-uniform scale for avatar space.
        /// Call every frame after you’ve updated Head.Incoming (and optionally others).
        /// </summary>
        public void SimulateAndApply(Vector3 nowScale)
        {
            // Re-scale T-pose locals and offsets only if scale changed meaningfully
            if (nowScale != _lastScale)
            {
                RescaleOnlyThisBone(ref Head, nowScale);
                RescaleOnlyThisBone(ref Neck, nowScale);
                RescaleOnlyThisBone(ref Chest, nowScale);
                RescaleOnlyThisBone(ref Spine, nowScale);
                RescaleOnlyThisBone(ref Hips, nowScale);
                RescaleOnlyThisBone(ref CenterEye, nowScale);
                RescaleOnlyThisBone(ref Mouth, nowScale);
                _lastScale = nowScale;
            }
            // Cache frame-local data
            var references = RemotePlayer.RemoteAvatarDriver.References;
            Vector3 rrt = RemotePlayerTransform.position;
            // Remove T-pose influence
            Head.Outgoing.rotation = references.TposeHead.rotation * references.head.rotation;
            Hips.Outgoing.rotation = references.TposeHips.rotation * references.Hips.rotation;
            Head.Outgoing.position = references.head.position - rrt;
            Hips.Outgoing.position = references.Hips.position - rrt;

            // Apply hard locks in strict order (use parent’s rotation + child offset)
            ApplyChildLock(ref Neck, in Head);
            ApplyChildLock(ref Chest, in Neck);
            ApplyChildLock(ref Spine, in Chest);
            ApplyChildLock(ref CenterEye, in Head);
            ApplyChildLock(ref Mouth, in Head);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ApplyChildLock(ref Bone child, in Bone parent)
        {
            Quaternion R = parent.Outgoing.rotation;
            Vector3 P = parent.Outgoing.position;
            Vector3 off = R * child.ScaledOffset;

            child.Outgoing.position = P + off;
            child.Outgoing.rotation = R;
        }
        // NOTE: Only rescale the bone itself; do not touch the parent/target here.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RescaleOnlyThisBone(ref Bone b, Vector3 s)
        {
            // TposeLocalScaled = TposeLocal .* s (non-uniform allowed)
            var p = b.TposeLocal.position;
            b.TposeLocalScaled.position = new Vector3(p.x * s.x, p.y * s.y, p.z * s.z);

            // ScaledOffset = Offset .* s
            var o = b.Offset;
            b.ScaledOffset = new Vector3(o.x * s.x, o.y * s.y, o.z * s.z);

            // Rotation doesn't scale; keep as-is
            b.TposeLocalScaled.rotation = b.TposeLocal.rotation;
        }
        public BasisCalibratedCoords GetMouthPosition() => Mouth.Outgoing;
        public BasisCalibratedCoords GetHeadPosition() => Head.Outgoing;
        public BasisCalibratedCoords GetHipsPosition() => Hips.Outgoing;
        public Vector3 GetMouthTposePosition() => Mouth.TposeLocalScaled.position;
    }
}
