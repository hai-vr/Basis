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
        //  Character basis — what makes two DIFFERENT rigs agree
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// The avatar's own ANATOMICAL frame, in root space: forward = feet→toes, up = hips→chest,
        /// right = left-hip→right-hip, measured from the T-pose by
        /// <see cref="BasisTransformMapping.RecordPoses"/> and re-orthonormalised there.
        ///
        /// This exists because the animator root is NOT a shared frame. TposeFromRoot divides out
        /// the root's rotation, which makes F independent of where the avatar is standing — but it
        /// leaves whatever rotation the exporter baked between the animator transform and the
        /// skeleton sitting inside F. A Blender Z-up conversion node, an `Armature` child rotated
        /// −90° about X, a model authored facing −Z: all of them are legal humanoid rigs and all of
        /// them put a different constant in F. Unity's humanoid Avatar constrains the RIG
        /// DEFINITION, not the GameObject hierarchy above it.
        ///
        /// For same-rig playback that constant cancels (encode and decode share it), which is why
        /// it hid: the pose is only ever decoded on a foreign rig during the window an avatar swap
        /// is downloading, and the old avatar stays worn through it
        /// (BasisAvatarFactory's no-dummy swap). In that window the mismatch conjugates every
        /// joint's rotation axis — limbs twist — and corrupts the hips rotation, which the receiver
        /// backs the root pose out of, so the whole avatar also shifts. Then the new rig lands, the
        /// frames match again, and it snaps correct.
        ///
        /// Normalising F by this basis removes the exporter's constant from the wire entirely, so
        /// two rigs agree on what "+Y up, +Z forward" means regardless of how they were authored.
        /// Falls back to identity when the T-pose geometry is unreadable, which reproduces the
        /// raw-root behaviour rather than inventing a frame.
        /// </summary>
        public static quaternion GetCharacterBasis(BasisTransformMapping mapping)
        {
            if (mapping == null) return quaternion.identity;
            return GetCharacterBasis(mapping.AvatarForwards, mapping.AvatarUpwards);
        }

        /// <summary>
        /// Character basis from a raw root-space forward/up pair — the shape
        /// BasisAvatarModelCache stores, so the cached path builds the identical basis without
        /// going back through the mapping.
        /// </summary>
        public static quaternion GetCharacterBasis(float3 forward, float3 up)
        {
            // RecordPoses already orthonormalises the trio; LookRotationSafe is the guard for a
            // cache entry written before that ran, or a rig degenerate enough to defeat it.
            if (math.lengthsq(forward) < 1e-8f || math.lengthsq(up) < 1e-8f) return quaternion.identity;
            quaternion basis = quaternion.LookRotationSafe(forward, up);
            return math.all(math.isfinite(basis.value)) && math.lengthsq(basis.value) > 1e-8f
                ? math.normalize(basis)
                : quaternion.identity;
        }

        // ────────────────────────────────────────────────────────────
        //  Rest-frame lookup
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads this bone's rest frame — TposeFromRoot's rotation re-expressed in the avatar's own
        /// anatomical basis (see <see cref="GetCharacterBasis"/>), which is what makes the value on
        /// the wire mean the same thing on a rig that was authored differently.
        ///
        /// Falls back to identity when the rig lacks the bone or calibration never recorded it.
        /// RecordPoses stores absent bones as an all-zero BasisCalibratedCoords, which is not a
        /// rotation — Normalize inside the operator builders turns that into identity, but resolve
        /// it here too so callers that inspect the frame directly see the same value.
        /// </summary>
        public static quaternion GetRestFrame(BasisTransformMapping mapping, HumanBodyBones bone)
            => GetRestFrame(mapping, bone, GetCharacterBasis(mapping));

        /// <summary>
        /// Rest-frame lookup with a pre-built basis, for callers resolving many bones at once —
        /// the basis is one value per avatar, not per bone.
        /// </summary>
        public static quaternion GetRestFrame(BasisTransformMapping mapping, HumanBodyBones bone, quaternion characterBasis)
        {
            if (mapping?.TposeFromRoot != null && mapping.TposeFromRoot.TryGetValue(bone, out var coords))
            {
                Quaternion r = coords.rotation;
                float lenSq = r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w;
                if (lenSq > 1e-12f)
                {
                    return NormalizeRestFrame(new quaternion(r.x, r.y, r.z, r.w), characterBasis);
                }
            }
            return IdentityRestFrame;
        }

        /// <summary>
        /// Re-expresses a raw root-space rest rotation in the character basis:
        /// <c>F̂ = conj(basis) * F</c>. With an identity basis this is the raw root frame, so every
        /// fallback path degrades to the previous behaviour rather than to a different one.
        /// </summary>
        public static quaternion NormalizeRestFrame(quaternion rawRestFrame, quaternion characterBasis)
            => math.normalize(math.mul(math.conjugate(characterBasis), rawRestFrame));

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
            quaternion basis = GetCharacterBasis(mapping);
            for (int slot = 0; slot < BasisBoneRotationCompression.SyncBoneCount; slot++)
            {
                var bone = (HumanBodyBones)order[slot];
                BuildEncodeOperators(GetRestFrame(mapping, bone, basis), GetRestLocal(mapping, bone),
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
        ///
        /// <paramref name="restFrameByBone"/> holds RAW root-space rest rotations (what the cache
        /// stores); <paramref name="characterBasis"/> is what turns them into the shared frame, so
        /// it must be the basis measured for the SAME avatar those rotations came from.
        /// </summary>
        public static unsafe void BuildDecodeTables(
            quaternion[] restFrameByBone, quaternion[] tposeLocalByBone, bool[] bonePresent,
            quaternion characterBasis, quaternion* outPre, quaternion* outPost)
        {
            int[] order = BasisBoneRotationCompression.BONE_WRITE_ORDER;
            for (int slot = 0; slot < BasisBoneRotationCompression.SyncBoneCount; slot++)
            {
                int bone = order[slot];
                bool present = bonePresent == null || (bone < bonePresent.Length && bonePresent[bone]);
                quaternion rest = present && restFrameByBone != null && bone < restFrameByBone.Length
                    ? NormalizeRestFrame(restFrameByBone[bone], characterBasis)
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
