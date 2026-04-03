using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Bone rotation compression using "smallest three" quaternion encoding.
    /// Replaces the muscle-based HumanPose system with per-bone delta rotations
    /// that are avatar-agnostic (T-pose offset removed) and fully job-compatible.
    ///
    /// Each bone is assigned a bits-per-component (BPC) value based on its DOF:
    ///   3-DOF body joints: 10 BPC (32 bits total)
    ///   2-DOF limb joints: 8 BPC (26 bits total)
    ///   2-DOF extremities: 7 BPC (23 bits total)
    ///   1-2 DOF toes/eyes/jaw: 5 BPC (17 bits total)
    ///   2-DOF finger proximal: 6 BPC (20 bits total)
    ///   1-DOF finger mid/distal: 4 BPC (14 bits total)
    /// </summary>
    public static class BasisBoneRotationCompression
    {
        // ────────────────────────────────────────────────────────────
        //  Constants
        // ────────────────────────────────────────────────────────────

        /// <summary>Number of bones synced (HumanBodyBones 1..54, excluding Hips=0 which is body rotation).</summary>
        public const int SyncBoneCount = 54;

        /// <summary>Inverse of sqrt(2), the max magnitude of any non-dropped smallest-three component.</summary>
        public const float InvSqrt2 = 0.70710678118f;

        // Reuse position/scale/rotation sizes from BasisAvatarBitPacking
        public const int WritePosition = BasisAvatarBitPacking.WritePosition;   // 12
        public const int WriteScale    = BasisAvatarBitPacking.WriteScale;      // 2
        public const int WriteRotation = BasisAvatarBitPacking.WriteRotation;   // 7
        public const int TailBytes     = WriteScale + WriteRotation;            // 9

        // ────────────────────────────────────────────────────────────
        //  Bone write order: HumanBodyBones enum values (excluding Hips=0)
        //  Grouped by DOF/precision for cache-friendly packing.
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Maps slot index (0..53) to HumanBodyBones enum value.
        /// Grouped: 3-DOF body → 2-DOF limbs → 2-DOF extremities → toes/eyes/jaw → finger proximal → finger mid/distal.
        /// </summary>
        public static readonly int[] BONE_WRITE_ORDER = new int[]
        {
            // ── 3-DOF body (9 bones): Spine, Chest, UpperChest, Neck, Head, UpperArms, UpperLegs ──
            7,   // Spine
            8,   // Chest
            54,  // UpperChest
            9,   // Neck
            10,  // Head
            13,  // LeftUpperArm
            14,  // RightUpperArm
            1,   // LeftUpperLeg
            2,   // RightUpperLeg

            // ── 2-DOF limbs (4 bones): LowerArms, LowerLegs ──
            15,  // LeftLowerArm
            16,  // RightLowerArm
            3,   // LeftLowerLeg
            4,   // RightLowerLeg

            // ── 2-DOF extremities (6 bones): Shoulders, Hands, Feet ──
            11,  // LeftShoulder
            12,  // RightShoulder
            17,  // LeftHand
            18,  // RightHand
            5,   // LeftFoot
            6,   // RightFoot

            // ── 1-2 DOF toes/eyes/jaw (5 bones) ──
            19,  // LeftToes
            20,  // RightToes
            21,  // LeftEye
            22,  // RightEye
            23,  // Jaw

            // ── 2-DOF finger proximal (10 bones): curl + spread ──
            24,  // LeftThumbProximal
            27,  // LeftIndexProximal
            30,  // LeftMiddleProximal
            33,  // LeftRingProximal
            36,  // LeftLittleProximal
            39,  // RightThumbProximal
            42,  // RightIndexProximal
            45,  // RightMiddleProximal
            48,  // RightRingProximal
            51,  // RightLittleProximal

            // ── 1-DOF finger intermediate (10 bones): curl only ──
            25,  // LeftThumbIntermediate
            28,  // LeftIndexIntermediate
            31,  // LeftMiddleIntermediate
            34,  // LeftRingIntermediate
            37,  // LeftLittleIntermediate
            40,  // RightThumbIntermediate
            43,  // RightIndexIntermediate
            46,  // RightMiddleIntermediate
            49,  // RightRingIntermediate
            52,  // RightLittleIntermediate

            // ── 1-DOF finger distal (10 bones): curl only ──
            26,  // LeftThumbDistal
            29,  // LeftIndexDistal
            32,  // LeftMiddleDistal
            35,  // LeftRingDistal
            38,  // LeftLittleDistal
            41,  // RightThumbDistal
            44,  // RightIndexDistal
            47,  // RightMiddleDistal
            50,  // RightRingDistal
            53,  // RightLittleDistal
        };

        /// <summary>
        /// Reverse lookup: HumanBodyBones enum value → slot index in BONE_WRITE_ORDER.
        /// Index 0 (Hips) = -1. Bones 1..54 map to slots 0..53.
        /// </summary>
        public static readonly int[] BONE_TO_SLOT;

        static BasisBoneRotationCompression()
        {
            // Build reverse lookup
            BONE_TO_SLOT = new int[55]; // HumanBodyBones 0..54
            for (int i = 0; i < 55; i++) BONE_TO_SLOT[i] = -1;
            for (int slot = 0; slot < SyncBoneCount; slot++)
            {
                BONE_TO_SLOT[BONE_WRITE_ORDER[slot]] = slot;
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Bits-per-component tables (per quality level)
        //  Total bits per bone = 2 (index) + 3 * BPC
        // ────────────────────────────────────────────────────────────

        /// <summary>HIGH quality: BPC per slot. Total = 1095 bits = 137 bytes.</summary>
        public static readonly byte[] BPC_HIGH = new byte[]
        {
            // 3-DOF body (9): 10 BPC → 32 bits each
            10, 10, 10, 10, 10, 10, 10, 10, 10,
            // 2-DOF limbs (4): 8 BPC → 26 bits each
            8, 8, 8, 8,
            // 2-DOF extremities (6): 7 BPC → 23 bits each
            7, 7, 7, 7, 7, 7,
            // Toes/eyes/jaw (5): 5 BPC → 17 bits each
            5, 5, 5, 5, 5,
            // Finger proximal (10): 6 BPC → 20 bits each
            6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
            // Finger intermediate (10): 4 BPC → 14 bits each
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
            // Finger distal (10): 4 BPC → 14 bits each
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
        };

        /// <summary>MEDIUM quality: reduced precision across the board.</summary>
        public static readonly byte[] BPC_MEDIUM = new byte[]
        {
            // 3-DOF body (9): 8 BPC → 26 bits
            8, 8, 8, 8, 8, 8, 8, 8, 8,
            // 2-DOF limbs (4): 7 BPC → 23 bits
            7, 7, 7, 7,
            // 2-DOF extremities (6): 6 BPC → 20 bits
            6, 6, 6, 6, 6, 6,
            // Toes/eyes/jaw (5): 4 BPC → 14 bits
            4, 4, 4, 4, 4,
            // Finger proximal (10): 5 BPC → 17 bits
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
            // Finger intermediate (10): 3 BPC → 11 bits
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            // Finger distal (10): 3 BPC → 11 bits
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        };

        /// <summary>LOW quality: minimal precision for distant avatars.</summary>
        public static readonly byte[] BPC_LOW = new byte[]
        {
            // 3-DOF body (9): 6 BPC → 20 bits
            6, 6, 6, 6, 6, 6, 6, 6, 6,
            // 2-DOF limbs (4): 5 BPC → 17 bits
            5, 5, 5, 5,
            // 2-DOF extremities (6): 5 BPC → 17 bits
            5, 5, 5, 5, 5, 5,
            // Toes/eyes/jaw (5): 3 BPC → 11 bits
            3, 3, 3, 3, 3,
            // Finger proximal (10): 4 BPC → 14 bits
            4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
            // Finger intermediate (10): 3 BPC → 11 bits
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            // Finger distal (10): 3 BPC → 11 bits
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        };

        /// <summary>VERY LOW quality: extremely distant avatars.</summary>
        public static readonly byte[] BPC_VERY_LOW = new byte[]
        {
            // 3-DOF body (9): 5 BPC → 17 bits
            5, 5, 5, 5, 5, 5, 5, 5, 5,
            // 2-DOF limbs (4): 4 BPC → 14 bits
            4, 4, 4, 4,
            // 2-DOF extremities (6): 4 BPC → 14 bits
            4, 4, 4, 4, 4, 4,
            // Toes/eyes/jaw (5): 3 BPC → 11 bits
            3, 3, 3, 3, 3,
            // Finger proximal (10): 3 BPC → 11 bits
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            // Finger intermediate (10): 2 BPC → 8 bits
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            // Finger distal (10): 2 BPC → 8 bits
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        };

        /// <summary>Returns the BPC table for a given quality level.</summary>
        public static byte[] GetBpcTable(BasisAvatarBitPacking.BitQuality q) => q switch
        {
            BasisAvatarBitPacking.BitQuality.High     => BPC_HIGH,
            BasisAvatarBitPacking.BitQuality.Medium   => BPC_MEDIUM,
            BasisAvatarBitPacking.BitQuality.Low      => BPC_LOW,
            BasisAvatarBitPacking.BitQuality.VeryLow  => BPC_VERY_LOW,
            _ => BPC_HIGH
        };

        // ────────────────────────────────────────────────────────────
        //  Size calculations
        // ────────────────────────────────────────────────────────────

        /// <summary>Total bytes for the bone rotation bitstream at a given quality.</summary>
        public static int RotationBytes(BasisAvatarBitPacking.BitQuality q)
        {
            byte[] bpc = GetBpcTable(q);
            int totalBits = 0;
            for (int i = 0; i < bpc.Length; i++)
                totalBits += 2 + 3 * bpc[i]; // 2 index bits + 3 components
            return (totalBits + 7) >> 3;
        }

        /// <summary>Total packet size: position + bone rotations + scale + body rotation.</summary>
        public static int ConvertToSize(BasisAvatarBitPacking.BitQuality q)
        {
            return WritePosition + RotationBytes(q) + TailBytes;
        }

        /// <summary>Precomputes bit offsets per slot for a BPC table. Returns total bit count.</summary>
        public static int ComputeBitOffsets(byte[] bpc, int[] outBitOffsets)
        {
            int pos = 0;
            for (int i = 0; i < bpc.Length; i++)
            {
                outBitOffsets[i] = pos;
                pos += 2 + 3 * bpc[i];
            }
            return pos;
        }

        // ────────────────────────────────────────────────────────────
        //  Smallest-Three Encode / Decode
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Encodes a unit quaternion using "smallest three" compression.
        /// Drops the largest-magnitude component, stores a 2-bit index + 3×BPC components.
        /// The dropped component is always positive (quaternion negated if needed).
        /// </summary>
        /// <param name="q">Unit quaternion to encode.</param>
        /// <param name="bpc">Bits per component (2..15).</param>
        /// <param name="outBits">Receives the packed bits. Layout: [index:2][a:BPC][b:BPC][c:BPC].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EncodeSmallestThree(quaternion q, int bpc, out ulong outBits)
        {
            float4 v = q.value;

            // Find largest absolute component
            float4 abs4 = math.abs(v);
            int maxIdx = 0;
            float maxVal = abs4.x;
            if (abs4.y > maxVal) { maxIdx = 1; maxVal = abs4.y; }
            if (abs4.z > maxVal) { maxIdx = 2; maxVal = abs4.z; }
            if (abs4.w > maxVal) { maxIdx = 3; }

            // Negate quaternion if largest is negative (equivalent quaternion, positive recovery)
            if (v[maxIdx] < 0f) v = -v;

            // Extract the 3 remaining components (in order, skipping maxIdx)
            float a, b, c;
            switch (maxIdx)
            {
                case 0:  a = v.y; b = v.z; c = v.w; break;
                case 1:  a = v.x; b = v.z; c = v.w; break;
                case 2:  a = v.x; b = v.y; c = v.w; break;
                default: a = v.x; b = v.y; c = v.z; break;
            }

            // Each remaining component is in [-1/√2, 1/√2]
            uint maxQ = (uint)((1 << bpc) - 1);

            uint qa = (uint)math.clamp(math.round((a / InvSqrt2 * 0.5f + 0.5f) * maxQ), 0, maxQ);
            uint qb = (uint)math.clamp(math.round((b / InvSqrt2 * 0.5f + 0.5f) * maxQ), 0, maxQ);
            uint qc = (uint)math.clamp(math.round((c / InvSqrt2 * 0.5f + 0.5f) * maxQ), 0, maxQ);

            // Pack: [maxIdx:2][a:BPC][b:BPC][c:BPC]
            outBits = (ulong)maxIdx | ((ulong)qa << 2) | ((ulong)qb << (2 + bpc)) | ((ulong)qc << (2 + 2 * bpc));
        }

        /// <summary>
        /// Decodes a "smallest three" compressed quaternion.
        /// </summary>
        /// <param name="packed">Packed bits from EncodeSmallestThree.</param>
        /// <param name="bpc">Bits per component (must match encode).</param>
        /// <returns>Normalized unit quaternion.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion DecodeSmallestThree(ulong packed, int bpc)
        {
            uint mask = (uint)((1 << bpc) - 1);
            int maxIdx = (int)(packed & 3UL);
            uint qa = (uint)((packed >> 2) & mask);
            uint qb = (uint)((packed >> (2 + bpc)) & mask);
            uint qc = (uint)((packed >> (2 + 2 * bpc)) & mask);

            float fMax = (float)mask;
            float a = (qa / fMax * 2f - 1f) * InvSqrt2;
            float b = (qb / fMax * 2f - 1f) * InvSqrt2;
            float c = (qc / fMax * 2f - 1f) * InvSqrt2;

            // Recover the dropped component (always positive)
            float d = math.sqrt(math.max(0f, 1f - a * a - b * b - c * c));

            float4 v;
            switch (maxIdx)
            {
                case 0:  v = new float4(d, a, b, c); break;
                case 1:  v = new float4(a, d, b, c); break;
                case 2:  v = new float4(a, b, d, c); break;
                default: v = new float4(a, b, c, d); break;
            }

            return math.normalizesafe(new quaternion(v), quaternion.identity);
        }

        // ────────────────────────────────────────────────────────────
        //  Bitstream read/write (mirrors BasisAvatarBitPacking pattern)
        // ────────────────────────────────────────────────────────────

        /// <summary>Writes up to 48 bits into a byte array at the given bit position.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteBits(byte[] dst, int bitPos, ulong value, int bitCount)
        {
            int bytePos = bitPos >> 3;
            int bitInByte = bitPos & 7;
            ulong v = value;
            int bitsLeft = bitCount;

            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;
                ulong maskVal = (1UL << take) - 1UL;
                byte chunk = (byte)(v & maskVal);
                dst[bytePos] = (byte)(dst[bytePos] | (chunk << bitInByte));
                v >>= take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }
        }

        /// <summary>Reads up to 48 bits from a byte array at the given bit position.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadBits(byte[] src, ref int bitPos, int bitCount)
        {
            int bytePos = bitPos >> 3;
            int bitInByte = bitPos & 7;
            ulong outV = 0;
            int outShift = 0;
            int bitsLeft = bitCount;

            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;
                ulong maskVal = (1UL << take) - 1UL;
                ulong chunk = ((ulong)src[bytePos] >> bitInByte) & maskVal;
                outV |= chunk << outShift;
                outShift += take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }

            bitPos += bitCount;
            return outV;
        }

        // ────────────────────────────────────────────────────────────
        //  Burst-compatible NativeArray versions for jobs
        // ────────────────────────────────────────────────────────────

        /// <summary>Writes bits into a NativeArray byte buffer (for Burst jobs).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteBitsNative(NativeArray<byte> dst, int bitPos, ulong value, int bitCount)
        {
            int bytePos = bitPos >> 3;
            int bitInByte = bitPos & 7;
            ulong v = value;
            int bitsLeft = bitCount;

            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;
                ulong maskVal = (1UL << take) - 1UL;
                byte chunk = (byte)(v & maskVal);
                dst[bytePos] = (byte)(dst[bytePos] | (chunk << bitInByte));
                v >>= take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }
        }

        /// <summary>Reads bits from a NativeArray byte buffer (for Burst jobs).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadBitsNative(NativeArray<byte> src, ref int bitPos, int bitCount)
        {
            int bytePos = bitPos >> 3;
            int bitInByte = bitPos & 7;
            ulong outV = 0;
            int outShift = 0;
            int bitsLeft = bitCount;

            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;
                ulong maskVal = (1UL << take) - 1UL;
                ulong chunk = ((ulong)src[bytePos] >> bitInByte) & maskVal;
                outV |= chunk << outShift;
                outShift += take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }

            bitPos += bitCount;
            return outV;
        }

        // ────────────────────────────────────────────────────────────
        //  High-level compress / decompress (managed byte[] for network)
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Compresses an array of 54 bone delta rotations into a packed byte buffer.
        /// </summary>
        /// <param name="boneDeltas">54 delta quaternions (T-pose-relative), indexed by slot.</param>
        /// <param name="quality">Compression quality level.</param>
        /// <param name="dst">Destination byte array (must be at least RotationBytes(quality) long).</param>
        /// <param name="byteOffset">Starting byte offset in dst. Advanced past the written data.</param>
        public static void CompressBoneRotations(NativeArray<quaternion> boneDeltas, BasisAvatarBitPacking.BitQuality quality, byte[] dst, ref int byteOffset)
        {
            byte[] bpc = GetBpcTable(quality);
            int bitPos = byteOffset << 3;

            for (int slot = 0; slot < SyncBoneCount; slot++)
            {
                int bitsPerComp = bpc[slot];
                int totalBits = 2 + 3 * bitsPerComp;

                EncodeSmallestThree(boneDeltas[slot], bitsPerComp, out ulong packed);
                WriteBits(dst, bitPos, packed, totalBits);
                bitPos += totalBits;
            }

            byteOffset = (bitPos + 7) >> 3;
        }

        /// <summary>
        /// Decompresses bone rotations from a packed byte buffer into a NativeArray.
        /// </summary>
        /// <param name="src">Source byte array.</param>
        /// <param name="quality">Quality level (must match compression).</param>
        /// <param name="boneDeltas">Output: 54 delta quaternions.</param>
        /// <param name="byteOffset">Starting byte offset. Advanced past the read data.</param>
        public static void DecompressBoneRotations(byte[] src, BasisAvatarBitPacking.BitQuality quality, ref NativeArray<quaternion> boneDeltas, ref int byteOffset)
        {
            byte[] bpc = GetBpcTable(quality);
            int bitPos = byteOffset << 3;

            for (int slot = 0; slot < SyncBoneCount; slot++)
            {
                int bitsPerComp = bpc[slot];
                int totalBits = 2 + 3 * bitsPerComp;

                ulong packed = ReadBits(src, ref bitPos, totalBits);
                boneDeltas[slot] = DecodeSmallestThree(packed, bitsPerComp);
            }

            byteOffset = (bitPos + 7) >> 3;
        }
    }
}
