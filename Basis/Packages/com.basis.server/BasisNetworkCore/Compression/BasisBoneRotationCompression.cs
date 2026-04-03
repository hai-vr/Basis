using System;
using System.Runtime.CompilerServices;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Bone rotation compression using "smallest three" quaternion encoding.
    /// Pure C# — no Unity dependencies. Can run on the server.
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
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Maps slot index (0..53) to HumanBodyBones enum value.
        /// Grouped: 3-DOF body → 2-DOF limbs → 2-DOF extremities → toes/eyes/jaw → finger proximal → finger mid/distal.
        /// </summary>
        public static readonly int[] BONE_WRITE_ORDER = new int[]
        {
            // 3-DOF body (9 bones): Spine, Chest, UpperChest, Neck, Head, UpperArms, UpperLegs
            7, 8, 54, 9, 10, 13, 14, 1, 2,
            // 2-DOF limbs (4 bones): LowerArms, LowerLegs
            15, 16, 3, 4,
            // 2-DOF extremities (6 bones): Shoulders, Hands, Feet
            11, 12, 17, 18, 5, 6,
            // 1-2 DOF toes/eyes/jaw (5 bones)
            19, 20, 21, 22, 23,
            // 2-DOF finger proximal (10 bones)
            24, 27, 30, 33, 36, 39, 42, 45, 48, 51,
            // 1-DOF finger intermediate (10 bones)
            25, 28, 31, 34, 37, 40, 43, 46, 49, 52,
            // 1-DOF finger distal (10 bones)
            26, 29, 32, 35, 38, 41, 44, 47, 50, 53,
        };

        /// <summary>
        /// Reverse lookup: HumanBodyBones enum value → slot index.
        /// Index 0 (Hips) = -1. Bones 1..54 map to slots 0..53.
        /// </summary>
        public static readonly int[] BONE_TO_SLOT;

        static BasisBoneRotationCompression()
        {
            BONE_TO_SLOT = new int[55];
            for (int i = 0; i < 55; i++) BONE_TO_SLOT[i] = -1;
            for (int slot = 0; slot < SyncBoneCount; slot++)
                BONE_TO_SLOT[BONE_WRITE_ORDER[slot]] = slot;
        }

        // ────────────────────────────────────────────────────────────
        //  Bits-per-component tables (per quality level)
        //  Total bits per bone = 2 (index) + 3 * BPC
        // ────────────────────────────────────────────────────────────

        public static readonly byte[] BPC_HIGH = new byte[]
        {
            10,10,10,10,10,10,10,10,10, // 3-DOF body
            8,8,8,8,                     // 2-DOF limbs
            7,7,7,7,7,7,                 // 2-DOF extremities
            5,5,5,5,5,                   // toes/eyes/jaw
            6,6,6,6,6,6,6,6,6,6,        // finger proximal
            4,4,4,4,4,4,4,4,4,4,        // finger intermediate
            4,4,4,4,4,4,4,4,4,4,        // finger distal
        };

        public static readonly byte[] BPC_MEDIUM = new byte[]
        {
            8,8,8,8,8,8,8,8,8,
            7,7,7,7,
            6,6,6,6,6,6,
            4,4,4,4,4,
            5,5,5,5,5,5,5,5,5,5,
            3,3,3,3,3,3,3,3,3,3,
            3,3,3,3,3,3,3,3,3,3,
        };

        public static readonly byte[] BPC_LOW = new byte[]
        {
            6,6,6,6,6,6,6,6,6,
            5,5,5,5,
            5,5,5,5,5,5,
            3,3,3,3,3,
            4,4,4,4,4,4,4,4,4,4,
            3,3,3,3,3,3,3,3,3,3,
            3,3,3,3,3,3,3,3,3,3,
        };

        public static readonly byte[] BPC_VERY_LOW = new byte[]
        {
            5,5,5,5,5,5,5,5,5,
            4,4,4,4,
            4,4,4,4,4,4,
            3,3,3,3,3,
            3,3,3,3,3,3,3,3,3,3,
            2,2,2,2,2,2,2,2,2,2,
            2,2,2,2,2,2,2,2,2,2,
        };

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

        public static int RotationBytes(BasisAvatarBitPacking.BitQuality q)
        {
            byte[] bpc = GetBpcTable(q);
            int totalBits = 0;
            for (int i = 0; i < bpc.Length; i++)
                totalBits += 2 + 3 * bpc[i];
            return (totalBits + 7) >> 3;
        }

        public static int ConvertToSize(BasisAvatarBitPacking.BitQuality q)
        {
            return WritePosition + RotationBytes(q) + TailBytes;
        }

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
        //  Smallest-Three Encode / Decode (pure floats, no Unity types)
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Encodes a unit quaternion (x,y,z,w) using "smallest three" compression.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong EncodeSmallestThree(float qx, float qy, float qz, float qw, int bpc)
        {
            float ax = Math.Abs(qx), ay = Math.Abs(qy), az = Math.Abs(qz), aw = Math.Abs(qw);

            // Find largest absolute component
            int maxIdx = 0;
            float maxVal = ax;
            if (ay > maxVal) { maxIdx = 1; maxVal = ay; }
            if (az > maxVal) { maxIdx = 2; maxVal = az; }
            if (aw > maxVal) { maxIdx = 3; }

            // Negate quaternion if largest is negative
            float sign = 1f;
            switch (maxIdx)
            {
                case 0: if (qx < 0f) sign = -1f; break;
                case 1: if (qy < 0f) sign = -1f; break;
                case 2: if (qz < 0f) sign = -1f; break;
                case 3: if (qw < 0f) sign = -1f; break;
            }
            qx *= sign; qy *= sign; qz *= sign; qw *= sign;

            // Extract the 3 remaining components
            float a, b, c;
            switch (maxIdx)
            {
                case 0:  a = qy; b = qz; c = qw; break;
                case 1:  a = qx; b = qz; c = qw; break;
                case 2:  a = qx; b = qy; c = qw; break;
                default: a = qx; b = qy; c = qz; break;
            }

            uint maxQ = (uint)((1 << bpc) - 1);
            uint qa = Clamp((uint)Math.Round((a / InvSqrt2 * 0.5f + 0.5f) * maxQ), 0, maxQ);
            uint qA = Clamp((uint)Math.Round((b / InvSqrt2 * 0.5f + 0.5f) * maxQ), 0, maxQ);
            uint qC = Clamp((uint)Math.Round((c / InvSqrt2 * 0.5f + 0.5f) * maxQ), 0, maxQ);

            return (ulong)maxIdx | ((ulong)qa << 2) | ((ulong)qA << (2 + bpc)) | ((ulong)qC << (2 + 2 * bpc));
        }

        /// <summary>
        /// Decodes a "smallest three" compressed quaternion into (x,y,z,w).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecodeSmallestThree(ulong packed, int bpc, out float qx, out float qy, out float qz, out float qw)
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

            float d2 = 1f - a * a - b * b - c * c;
            float d = d2 > 0f ? (float)Math.Sqrt(d2) : 0f;

            switch (maxIdx)
            {
                case 0:  qx = d; qy = a; qz = b; qw = c; break;
                case 1:  qx = a; qy = d; qz = b; qw = c; break;
                case 2:  qx = a; qy = b; qz = d; qw = c; break;
                default: qx = a; qy = b; qz = c; qw = d; break;
            }

            // Normalize
            float len = (float)Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (len > 1e-8f)
            {
                float inv = 1f / len;
                qx *= inv; qy *= inv; qz *= inv; qw *= inv;
            }
            else
            {
                qx = 0f; qy = 0f; qz = 0f; qw = 1f;
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Bitstream read/write (pure C#)
        // ────────────────────────────────────────────────────────────

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Clamp(uint v, uint min, uint max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
