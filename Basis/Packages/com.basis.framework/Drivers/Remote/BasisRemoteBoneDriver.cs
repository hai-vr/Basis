using System.Runtime.CompilerServices;
using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Scripts.Drivers
{
    [System.Serializable]
    public class BasisRemoteBoneDriver
    {
        public BasisRemotePlayer RemotePlayer { get; private set; }
        public Transform RemotePlayerTransform;

        // -------- Per-bone fields (SoA exploded) --------
        // T-pose locals and offsets at scale=1 (avatar space)
        private float3 _tposeLocal_unscaled_Head;
        private float3 _tposeLocal_unscaled_Neck;
        private float3 _tposeLocal_unscaled_Chest;
        private float3 _tposeLocal_unscaled_Spine;
        private float3 _tposeLocal_unscaled_Hips;
        private float3 _tposeLocal_unscaled_CenterEye;
        private float3 _tposeLocal_unscaled_Mouth;

        private float3 _offsets_unscaled_Neck;
        private float3 _offsets_unscaled_Chest;
        private float3 _offsets_unscaled_Spine;
        private float3 _offsets_unscaled_CenterEye;
        private float3 _offsets_unscaled_Mouth;

        private float3 _tposeLocal_scaled_Hips;
        private float3 _tposeLocal_scaled_Mouth;

        private float3 _offsets_scaled_Neck;
        private float3 _offsets_scaled_Chest;
        private float3 _offsets_scaled_Spine;
        private float3 _offsets_scaled_CenterEye;
        private float3 _offsets_scaled_Mouth;

        // Per-frame outputs (avatar space)
        private float3 _outgoingPos_Head;
        private float3 _outgoingPos_Neck;
        private float3 _outgoingPos_Chest;
        private float3 _outgoingPos_Spine;
        private float3 _outgoingPos_Hips;
        private float3 _outgoingPos_CenterEye;
        private float3 _outgoingPos_Mouth;

        private quaternion _outgoingRot_Head;
        private quaternion _outgoingRot_Neck;
        private quaternion _outgoingRot_Chest;
        private quaternion _outgoingRot_Spine;
        private quaternion _outgoingRot_Hips;
        private quaternion _outgoingRot_CenterEye;
        private quaternion _outgoingRot_Mouth;

        // Scale cache (math-only)
        private float3 _lastScaleF3 = new float3(1f, 1f, 1f);

        public float DifferencebetweenHipAndHead;

        // Bone index layout (kept for public API compatibility)
        public const int Head = 0;
        public const int Neck = 1;
        public const int Chest = 2;
        public const int Spine = 3;
        public const int Hips = 4;
        public const int CenterEye = 5;
        public const int Mouth = 6;
        public const int BoneCount = 7;

        /// <summary>
        /// Capture T-pose positions for the bones we care about and compute child-parent offsets.
        /// Call once after the avatar is loaded / posed in T.
        /// </summary>
        public void InitializeFromAvatar(BasisRemotePlayer remotePlayer)
        {
            RemotePlayer = remotePlayer;
            var avatar = RemotePlayer.BasisAvatar;
            RemotePlayerTransform = avatar.Animator.transform;

            var refs = remotePlayer.RemoteAvatarDriver.References;

            // Fill T-pose locals from current world pose → avatar space
            // Head
            if (refs.head != null)
            {
                refs.head.GetPositionAndRotation(out Vector3 wpos, out _);
                _tposeLocal_unscaled_Head = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(RemotePlayerTransform, wpos);
            }
            else _tposeLocal_unscaled_Head = math.float3(0f);

            // Neck
            if (refs.neck != null)
            {
                refs.neck.GetPositionAndRotation(out Vector3 wpos, out _);
                _tposeLocal_unscaled_Neck = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(RemotePlayerTransform, wpos);
            }
            else _tposeLocal_unscaled_Neck = math.float3(0f);

            // Chest
            if (refs.chest != null)
            {
                refs.chest.GetPositionAndRotation(out Vector3 wpos, out _);
                _tposeLocal_unscaled_Chest = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(RemotePlayerTransform, wpos);
            }
            else _tposeLocal_unscaled_Chest = math.float3(0f);

            // Spine
            if (refs.spine != null)
            {
                refs.spine.GetPositionAndRotation(out Vector3 wpos, out _);
                _tposeLocal_unscaled_Spine = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(RemotePlayerTransform, wpos);
            }
            else _tposeLocal_unscaled_Spine = math.float3(0f);

            // Hips
            if (refs.Hips != null)
            {
                refs.Hips.GetPositionAndRotation(out Vector3 wpos, out _);
                _tposeLocal_unscaled_Hips = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(RemotePlayerTransform, wpos);
            }
            else _tposeLocal_unscaled_Hips = math.float3(0f);

            // CenterEye from authored point (world)
            float3 worldEye = BasisHelpers.ConvertFromLocalSpace(
                BasisHelpers.AvatarPositionConversion(avatar.AvatarEyePosition),
                RemotePlayerTransform.position
            );
            _tposeLocal_unscaled_CenterEye = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(RemotePlayerTransform, worldEye);

            // Mouth from authored point (world)
            float3 worldMouth = BasisHelpers.ConvertFromLocalSpace(
                BasisHelpers.AvatarPositionConversion(avatar.AvatarMouthPosition),
                RemotePlayerTransform.position
            );
            _tposeLocal_unscaled_Mouth = BasisLocalBoneDriver.ConvertToAvatarSpaceInitial(RemotePlayerTransform, worldMouth);

            // Compute unscaled offsets at scale=1 (child - parent)
            ComputeUnscaledOffsets(
                in _tposeLocal_unscaled_Head,
                in _tposeLocal_unscaled_Neck,
                in _tposeLocal_unscaled_Chest,
                in _tposeLocal_unscaled_Spine,
                in _tposeLocal_unscaled_Hips,
                in _tposeLocal_unscaled_CenterEye,
                in _tposeLocal_unscaled_Mouth,
                out _offsets_unscaled_Neck,
                out _offsets_unscaled_Chest,
                out _offsets_unscaled_Spine,
                out _offsets_unscaled_CenterEye,
                out _offsets_unscaled_Mouth
            );

            // Initialize scaled copies to scale=1
            _tposeLocal_scaled_Hips = _tposeLocal_unscaled_Hips;
            _tposeLocal_scaled_Mouth = _tposeLocal_unscaled_Mouth;
            _offsets_scaled_Neck = _offsets_unscaled_Neck;
            _offsets_scaled_Chest = _offsets_unscaled_Chest;
            _offsets_scaled_Spine = _offsets_unscaled_Spine;
            _offsets_scaled_CenterEye = _offsets_unscaled_CenterEye;
            _offsets_scaled_Mouth = _offsets_unscaled_Mouth;

            // Match legacy field semantics
            DifferencebetweenHipAndHead = _tposeLocal_scaled_Mouth.y - _tposeLocal_scaled_Hips.y;
        }

        /// <summary>
        /// Pure-math pass operating directly on fields. Call every frame.
        /// </summary>
        public void SimulateAndApply(Vector3 nowScaleF3)
        {
            var refs = RemotePlayer.RemoteAvatarDriver.References;

            // Frame inputs (world)
            float3 rootWorld = RemotePlayerTransform.position;
            float3 headWPos = refs.head.position;
            float3 hipsWPos = refs.Hips.position;

            quaternion headWRot = (quaternion)refs.head.rotation;
            quaternion hipsWRot = (quaternion)refs.Hips.rotation;
            quaternion tposeHeadRot = (quaternion)refs.TposeHead.rotation;
            quaternion tposeHipsRot = (quaternion)refs.TposeHips.rotation;

            // --- Rescale lazily if scale changed ---
            float3 nowScale = nowScaleF3;
            if (math.any(nowScale != _lastScaleF3))
            {
                _tposeLocal_scaled_Hips = _tposeLocal_unscaled_Hips * nowScale;
                _tposeLocal_scaled_Mouth = _tposeLocal_unscaled_Mouth * nowScale;
                _offsets_scaled_Neck = _offsets_unscaled_Neck * nowScale;
                _offsets_scaled_Chest = _offsets_unscaled_Chest * nowScale;
                _offsets_scaled_Spine = _offsets_unscaled_Spine * nowScale;
                _offsets_scaled_CenterEye = _offsets_unscaled_CenterEye * nowScale;
                _offsets_scaled_Mouth = _offsets_unscaled_Mouth * nowScale;

                _lastScaleF3 = nowScale;
            }

            // --- Remove T-pose influence (rot) and convert world→avatar (pos) ---
            {
                quaternion headR = math.mul(tposeHeadRot, headWRot); // Tpose * Current
                _outgoingRot_Head = headR;
                _outgoingPos_Head = headWPos - rootWorld;

                quaternion hipsR = math.mul(tposeHipsRot, hipsWRot);
                _outgoingRot_Hips = hipsR;
                _outgoingPos_Hips = hipsWPos - rootWorld;
            }

            // --- Hard-locks: child = parent + (parent.R * childOffsetScaled) ---
            {
                quaternion R = _outgoingRot_Head;
                float3 P = _outgoingPos_Head;
                float3 off = math.mul(R, _offsets_scaled_Neck);
                _outgoingRot_Neck = R;
                _outgoingPos_Neck = P + off;
            }
            {
                quaternion R = _outgoingRot_Neck;
                float3 P = _outgoingPos_Neck;
                float3 off = math.mul(R, _offsets_scaled_Chest);
                _outgoingRot_Chest = R;
                _outgoingPos_Chest = P + off;
            }
            {
                quaternion R = _outgoingRot_Chest;
                float3 P = _outgoingPos_Chest;
                float3 off = math.mul(R, _offsets_scaled_Spine);
                _outgoingRot_Spine = R;
                _outgoingPos_Spine = P + off;
            }
            {
                quaternion R = _outgoingRot_Head;
                float3 P = _outgoingPos_Head;
                float3 off = math.mul(R, _offsets_scaled_CenterEye);
                _outgoingRot_CenterEye = R;
                _outgoingPos_CenterEye = P + off;
            }
            {
                quaternion R = _outgoingRot_Head;
                float3 P = _outgoingPos_Head;
                float3 off = math.mul(R, _offsets_scaled_Mouth);
                _outgoingRot_Mouth = R;
                _outgoingPos_Mouth = P + off;
            }

            // Matches the legacy field semantics
            DifferencebetweenHipAndHead = _tposeLocal_scaled_Mouth.y - _tposeLocal_scaled_Hips.y;
        }

        // ----------------- Public read API -----------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 GetOutgoingPosition(int boneIndex)
        {
            switch (boneIndex)
            {
                case Head: return _outgoingPos_Head;
                case Neck: return _outgoingPos_Neck;
                case Chest: return _outgoingPos_Chest;
                case Spine: return _outgoingPos_Spine;
                case Hips: return _outgoingPos_Hips;
                case CenterEye: return _outgoingPos_CenterEye;
                case Mouth: return _outgoingPos_Mouth;
                default: return float3.zero;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public quaternion GetOutgoingRotation(int boneIndex)
        {
            switch (boneIndex)
            {
                case Head: return _outgoingRot_Head;
                case Neck: return _outgoingRot_Neck;
                case Chest: return _outgoingRot_Chest;
                case Spine: return _outgoingRot_Spine;
                case Hips: return _outgoingRot_Hips;
                case CenterEye: return _outgoingRot_CenterEye;
                case Mouth: return _outgoingRot_Mouth;
                default: return quaternion.identity;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 GetMouthPosition() => _outgoingPos_Mouth;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public quaternion GetMouthRotation() => _outgoingRot_Mouth;

        // ---------- Static helper (still handy for init) ----------
        public static void ComputeUnscaledOffsets(
            in float3 tposeLocal_unscaled_Head,
            in float3 tposeLocal_unscaled_Neck,
            in float3 tposeLocal_unscaled_Chest,
            in float3 tposeLocal_unscaled_Spine,
            in float3 tposeLocal_unscaled_Hips,
            in float3 tposeLocal_unscaled_CenterEye,
            in float3 tposeLocal_unscaled_Mouth,
            out float3 offsets_unscaled_Neck,
            out float3 offsets_unscaled_Chest,
            out float3 offsets_unscaled_Hips,
            out float3 offsets_unscaled_CenterEye,
            out float3 offsets_unscaled_Mouth
        )
        {
            offsets_unscaled_Neck = tposeLocal_unscaled_Neck - tposeLocal_unscaled_Head;
            offsets_unscaled_Chest = tposeLocal_unscaled_Chest - tposeLocal_unscaled_Neck;
            offsets_unscaled_Hips = tposeLocal_unscaled_Hips - tposeLocal_unscaled_Spine;
            offsets_unscaled_CenterEye = tposeLocal_unscaled_CenterEye - tposeLocal_unscaled_Head;
            offsets_unscaled_Mouth = tposeLocal_unscaled_Mouth - tposeLocal_unscaled_Head;
        }
    }
}
