using Basis.Network.Core.Compression;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;

namespace Basis.Scripts.Networking.NetworkedAvatar
{
    /// <summary>
    /// Burst-compiled job that converts bone local rotations into the rig-neutral generic
    /// rotation space and compresses them into a packed byte buffer using smallest-three
    /// quaternion encoding.
    ///
    /// Replaces the main-thread ExtractBoneDeltas() + CompressBoneRotations() calls
    /// with a single Burst-optimized pass. The job reads current bone local rotations
    /// (written by a prior TransformAccessArray read), maps them into generic space with the
    /// folded operators cached at calibration, and writes the compressed bitstream.
    ///
    /// The mapped value is <c>g = pre * currentLocal * post</c> — see
    /// <see cref="Basis.Network.Core.Compression.BasisGenericBoneRotation"/> for what g means and
    /// why the operators fold this way. Conjugation preserves rotation angle, so g occupies
    /// exactly the same magnitude range the old T-pose-relative local delta did and the
    /// smallest-three budget below is unchanged.
    ///
    /// This runs as an IJob (not parallel) because the bit-packed output is sequential.
    /// Burst still provides significant wins via SIMD quaternion math and branch elimination.
    /// </summary>
    [BurstCompile]
    public struct BasisBoneDeltaAndCompressJob : IJob
    {
        /// <summary>Current local rotations read from transforms. Length = SyncBoneCount.</summary>
        [ReadOnly] public NativeArray<quaternion> CurrentLocalRotations;

        /// <summary>
        /// Left factor of the generic-space encode, per bone slot: <c>restFrame * conj(tposeLocal)</c>.
        /// Length = SyncBoneCount. Folded at capture rather than rebuilt per bone per tick — the
        /// rest pose is fixed for the life of the avatar, so deriving it inside the encode loop
        /// would recompute a constant every send.
        /// </summary>
        [ReadOnly] public NativeArray<quaternion> EncodePre;

        /// <summary>Right factor of the generic-space encode, per bone slot: <c>conj(restFrame)</c>.</summary>
        [ReadOnly] public NativeArray<quaternion> EncodePost;

        /// <summary>Bits-per-component per slot. Length = SyncBoneCount.</summary>
        [ReadOnly] public NativeArray<byte> BitsPerComponent;

        /// <summary>Max quaternion component range per slot. Length = SyncBoneCount.</summary>
        [ReadOnly] public NativeArray<float> MaxComponent;

        /// <summary>Output byte buffer (the full packet array). Must be pre-cleared in the rotation region.</summary>
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> OutputBuffer;

        /// <summary>Byte offset where the rotation bitstream starts (after position bytes).</summary>
        public int RotationByteOffset;

        /// <summary>Explicit bone slots to encode (wire slots 0..BoneCount-1).</summary>
        public int BoneCount;

        /// <summary>Ten curl/splay pairs appended after the explicit slots. See BasisBoneRotationCompression.</summary>
        [ReadOnly] public NativeArray<float2> FingerPercentages;
        public int CurlBits;
        public int SplayBits;

        /// <summary>Computed generic-space rotations, written for other consumers (e.g., interpolation).</summary>
        public NativeArray<quaternion> BoneDeltas;

        public void Execute()
        {
            int bitPos = RotationByteOffset << 3;

            for (int slot = 0; slot < BoneCount; slot++)
            {
                // generic = (restFrame * conj(tpose)) * current * conj(restFrame)
                quaternion current = CurrentLocalRotations[slot];
                quaternion delta = math.mul(math.mul(EncodePre[slot], current), EncodePost[slot]);

                // Normalize
                float4 dv = delta.value;
                float lenSq = math.lengthsq(dv);
                delta = lenSq > 1e-8f ? new quaternion(dv * math.rsqrt(lenSq)) : quaternion.identity;

                BoneDeltas[slot] = delta;

                // Encode smallest-three
                int bpc = BitsPerComponent[slot];
                int totalBits = 2 + 3 * bpc;
                float maxRange = MaxComponent[slot];

                ulong packed = EncodeSmallestThree(delta.value.x, delta.value.y, delta.value.z, delta.value.w, bpc, maxRange);
                WriteBits(bitPos, packed, totalBits);
                bitPos += totalBits;
            }

            int fingerWidth = CurlBits + SplayBits;
            for (int finger = 0; finger < FingerPercentages.Length; finger++)
            {
                float2 pct = FingerPercentages[finger];
                ulong curl = EncodeSignedUnit(pct.x, CurlBits);
                ulong splay = EncodeSignedUnit(pct.y, SplayBits);
                WriteBits(bitPos, curl | (splay << CurlBits), fingerWidth);
                bitPos += fingerWidth;
            }
        }

        /// <summary>
        /// Burst-compatible mirror of BasisBoneRotationCompression.EncodeSignedUnit. Clamps rather
        /// than wraps, and maps a non-finite input to the midpoint so an overshooting gain or a
        /// dropped tracking frame cannot encode as a full-scale curl.
        /// </summary>
        private static ulong EncodeSignedUnit(float value, int bits)
        {
            uint maxQ = (uint)((1 << bits) - 1);
            if (math.isnan(value)) return (maxQ + 1) >> 1;
            float clamped = math.clamp(value, -1f, 1f);
            return Clamp((uint)math.round((clamped * 0.5f + 0.5f) * maxQ), 0, maxQ);
        }

        // Burst-compatible encode (inlined from BasisBoneRotationCompression)
        private ulong EncodeSmallestThree(float qx, float qy, float qz, float qw, int bpc, float maxRange)
        {
            float ax = math.abs(qx), ay = math.abs(qy), az = math.abs(qz), aw = math.abs(qw);

            int maxIdx = 0;
            float maxVal = ax;
            if (ay > maxVal) { maxIdx = 1; maxVal = ay; }
            if (az > maxVal) { maxIdx = 2; maxVal = az; }
            if (aw > maxVal) { maxIdx = 3; }

            // Negate if largest is negative
            float sign = maxIdx switch { 0 => qx, 1 => qy, 2 => qz, _ => qw };
            if (sign < 0f) { qx = -qx; qy = -qy; qz = -qz; qw = -qw; }

            float a, b, c;
            switch (maxIdx)
            {
                case 0: a = qy; b = qz; c = qw; break;
                case 1: a = qx; b = qz; c = qw; break;
                case 2: a = qx; b = qy; c = qw; break;
                default: a = qx; b = qy; c = qz; break;
            }

            float invRange = 1f / maxRange;
            uint maxQ = (uint)((1 << bpc) - 1);
            uint qa = Clamp((uint)math.round((math.clamp(a * invRange, -1f, 1f) * 0.5f + 0.5f) * maxQ), 0, maxQ);
            uint qb = Clamp((uint)math.round((math.clamp(b * invRange, -1f, 1f) * 0.5f + 0.5f) * maxQ), 0, maxQ);
            uint qc = Clamp((uint)math.round((math.clamp(c * invRange, -1f, 1f) * 0.5f + 0.5f) * maxQ), 0, maxQ);

            return (ulong)(uint)maxIdx | ((ulong)qa << 2) | ((ulong)qb << (2 + bpc)) | ((ulong)qc << (2 + 2 * bpc));
        }

        private static uint Clamp(uint v, uint min, uint max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private unsafe void WriteBits(int bitPos, ulong value, int bitCount)
        {
            byte* dst = (byte*)OutputBuffer.GetUnsafePtr();
            int bytePos = bitPos >> 3;
            int bitInByte = bitPos & 7;
            int bitsLeft = bitCount;

            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;
                ulong maskVal = (1UL << take) - 1UL;
                byte chunk = (byte)(value & maskVal);
                dst[bytePos] = (byte)(dst[bytePos] | (chunk << bitInByte));
                value >>= take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }
        }
    }

    /// <summary>
    /// Reads bone local rotations from transforms via TransformAccessArray,
    /// replacing the main-thread ExtractBoneDeltas loop.
    /// Uses a slot remap to handle missing bones (only valid transforms
    /// are in the TransformAccessArray).
    /// </summary>
    [BurstCompile]
    public struct ReadBoneLocalRotationsJob : IJobParallelForTransform
    {
        /// <summary>Maps TransformAccessArray index → slot in CurrentLocalRotations.</summary>
        [ReadOnly] public NativeArray<int> SlotRemap;

        /// <summary>Output: local rotations written at remapped slot indices.</summary>
        [NativeDisableParallelForRestriction]
        public NativeArray<quaternion> CurrentLocalRotations;

        public void Execute(int index, TransformAccess transform)
        {
            CurrentLocalRotations[SlotRemap[index]] = transform.localRotation;
        }
    }
}
