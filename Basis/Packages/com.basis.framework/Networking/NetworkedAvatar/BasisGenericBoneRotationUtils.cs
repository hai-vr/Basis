using Basis.Network.Core.Compression;
using Basis.Scripts.Common;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Quat = Basis.Network.Core.Compression.BasisGenericBoneRotation.Quat;

namespace Basis.Scripts.Networking.NetworkedAvatar
{
    /// <summary>
    /// Unity-side bridge to <see cref="BasisGenericBoneRotation"/> — the rig-neutral rotation space
    /// the avatar stream carries. Read that type first; this file only moves the same math across
    /// the Unity.Mathematics / NativeArray boundary and pulls the two rest quantities it needs out
    /// of a <see cref="BasisTransformMapping"/>.
    ///
    /// The two quantities, both captured by BasisTransformMapping.RecordPoses during calibration:
    ///   T — TposeLocal[bone].rotation,    the bone's rest rotation relative to its PARENT
    ///   F — TposeFromRoot[bone].rotation, the bone's rest rotation relative to the AVATAR ROOT
    ///
    /// Everything here runs at calibration time (avatar load / swap / recalibration), never per
    /// frame: the point of the folded operator tables is that the per-frame jobs do two quaternion
    /// multiplies and no trig.
    /// </summary>
    public static class BasisGenericBoneRotationUtils
    {
        /// <summary>Rest frame used when a rig has no data for a bone. Collapses the remap to the legacy local-delta scheme.</summary>
        public static readonly quaternion IdentityRestFrame = quaternion.identity;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static Quat ToQuat(quaternion q) => new Quat(q.value.x, q.value.y, q.value.z, q.value.w);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static quaternion ToUnity(in Quat q) => new quaternion(q.x, q.y, q.z, q.w);

        // ────────────────────────────────────────────────────────────
        //  Single bone
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Encode pair for one bone: <c>generic = pre * currentLocal * post</c>.
        /// </summary>
        public static void BuildEncodeOperators(quaternion restFrame, quaternion tposeLocal,
            out quaternion pre, out quaternion post)
        {
            BasisGenericBoneRotation.BuildEncodeOperators(ToQuat(restFrame), ToQuat(tposeLocal), out Quat p, out Quat q);
            pre = ToUnity(p);
            post = ToUnity(q);
        }

        /// <summary>
        /// Decode pair for one bone: <c>currentLocal = pre * generic * post</c>.
        /// </summary>
        public static void BuildDecodeOperators(quaternion restFrame, quaternion tposeLocal,
            out quaternion pre, out quaternion post)
        {
            BasisGenericBoneRotation.BuildDecodeOperators(ToQuat(restFrame), ToQuat(tposeLocal), out Quat p, out Quat q);
            pre = ToUnity(p);
            post = ToUnity(q);
        }

        // ────────────────────────────────────────────────────────────
        //  Rest-frame lookup
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads F for one bone out of a mapping's TposeFromRoot, falling back to identity when the
        /// rig lacks the bone or calibration never recorded it. RecordPoses stores absent bones as
        /// an all-zero BasisCalibratedCoords, which is not a rotation — Normalize inside the
        /// operator builders turns that into identity, but resolve it here too so callers that
        /// inspect the frame directly see the same value.
        /// </summary>
        public static quaternion GetRestFrame(BasisTransformMapping mapping, HumanBodyBones bone)
        {
            if (mapping?.TposeFromRoot != null && mapping.TposeFromRoot.TryGetValue(bone, out var coords))
            {
                Quaternion r = coords.rotation;
                float lenSq = r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w;
                if (lenSq > 1e-12f) return math.normalize(new quaternion(r.x, r.y, r.z, r.w));
            }
            return IdentityRestFrame;
        }

        /// <summary>Reads T for one bone out of a mapping's TposeLocal, with the same absent-bone handling.</summary>
        public static quaternion GetRestLocal(BasisTransformMapping mapping, HumanBodyBones bone)
        {
            if (mapping?.TposeLocal != null && mapping.TposeLocal.TryGetValue(bone, out var coords))
            {
                Quaternion r = coords.rotation;
                float lenSq = r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w;
                if (lenSq > 1e-12f) return math.normalize(new quaternion(r.x, r.y, r.z, r.w));
            }
            return quaternion.identity;
        }

        // ────────────────────────────────────────────────────────────
        //  Whole-skeleton tables, in BONE_WRITE_ORDER slot order
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills slot-order encode tables straight from a calibrated mapping. Both outputs must be
        /// at least <see cref="BasisBoneRotationCompression.SyncBoneCount"/> long.
        /// </summary>
        public static void BuildEncodeTables(BasisTransformMapping mapping,
            NativeArray<quaternion> outPre, NativeArray<quaternion> outPost)
        {
            int[] order = BasisBoneRotationCompression.BONE_WRITE_ORDER;
            for (int slot = 0; slot < BasisBoneRotationCompression.SyncBoneCount; slot++)
            {
                var bone = (HumanBodyBones)order[slot];
                BuildEncodeOperators(GetRestFrame(mapping, bone), GetRestLocal(mapping, bone),
                    out quaternion pre, out quaternion post);
                outPre[slot] = pre;
                outPost[slot] = post;
            }
        }

        /// <summary>
        /// Fills slot-order decode tables from per-bone arrays indexed by HumanBodyBones enum value
        /// (the shape BasisAvatarModelCache caches, so a repeat instance of a known avatar skips
        /// the dictionary walk entirely).
        ///
        /// <paramref name="bonePresent"/> gates each slot: a bone the rig does not have gets the
        /// legacy identity-rest pair, which composes the incoming generic value straight onto the
        /// (unused) rest local rather than through a meaningless frame.
        /// </summary>
        public static unsafe void BuildDecodeTables(
            quaternion[] restFrameByBone, quaternion[] tposeLocalByBone, bool[] bonePresent,
            quaternion* outPre, quaternion* outPost)
        {
            int[] order = BasisBoneRotationCompression.BONE_WRITE_ORDER;
            for (int slot = 0; slot < BasisBoneRotationCompression.SyncBoneCount; slot++)
            {
                int bone = order[slot];
                bool present = bonePresent == null || (bone < bonePresent.Length && bonePresent[bone]);
                quaternion rest = present && restFrameByBone != null && bone < restFrameByBone.Length
                    ? restFrameByBone[bone]
                    : IdentityRestFrame;
                quaternion local = present && tposeLocalByBone != null && bone < tposeLocalByBone.Length
                    ? tposeLocalByBone[bone]
                    : quaternion.identity;

                BuildDecodeOperators(rest, local, out quaternion pre, out quaternion post);
                outPre[slot] = pre;
                outPost[slot] = post;
            }
        }
    }
}
