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

        /// <summary>HIGH quality. Prioritizes shoulders/limbs over fingers.
        /// Total = 1014 bits = 127 bytes. Packet = 148 bytes.</summary>
        public static readonly byte[] BPC_HIGH = new byte[]
        {
            // 3-DOF body (9): spine, chest, upperchest, neck, head, upper arms, upper legs
            10,10,10,10,10,10,10,10,10,
            // 2-DOF limbs (4): lower arms, lower legs — high precision to avoid cascading error to hands/feet
            10,10,10,10,
            // 2-DOF extremities (6): shoulders(2) — high (cascade to arms), hands(2), feet(2)
            10,10, 8,8, 8,8,
            // toes/eyes/jaw (5)
            4,4,4,4,4,
            // finger proximal (10): curl+spread, moderate precision
            4,4,4,4,4,4,4,4,4,4,
            // finger intermediate (10): curl only, low precision is fine
            3,3,3,3,3,3,3,3,3,3,
            // finger distal (10): curl only
            3,3,3,3,3,3,3,3,3,3,
        };

        public static readonly byte[] BPC_MEDIUM = new byte[]
        {
            8,8,8,8,8,8,8,8,8,          // body
            8,8,8,8,                     // limbs
            8,8, 6,6, 6,6,              // shoulders, hands, feet
            3,3,3,3,3,                   // toes/eyes/jaw
            3,3,3,3,3,3,3,3,3,3,        // finger proximal
            2,2,2,2,2,2,2,2,2,2,        // finger intermediate
            2,2,2,2,2,2,2,2,2,2,        // finger distal
        };

        public static readonly byte[] BPC_LOW = new byte[]
        {
            6,6,6,6,6,6,6,6,6,          // body
            6,6,6,6,                     // limbs
            6,6, 5,5, 5,5,              // shoulders, hands, feet
            3,3,3,3,3,                   // toes/eyes/jaw
            3,3,3,3,3,3,3,3,3,3,        // finger proximal
            2,2,2,2,2,2,2,2,2,2,        // finger intermediate
            2,2,2,2,2,2,2,2,2,2,        // finger distal
        };

        public static readonly byte[] BPC_VERY_LOW = new byte[]
        {
            5,5,5,5,5,5,5,5,5,          // body
            5,5,5,5,                     // limbs
            5,5, 4,4, 4,4,              // shoulders, hands, feet
            2,2,2,2,2,                   // toes/eyes/jaw
            2,2,2,2,2,2,2,2,2,2,        // finger proximal
            2,2,2,2,2,2,2,2,2,2,        // finger intermediate
            2,2,2,2,2,2,2,2,2,2,        // finger distal
        };

        // ────────────────────────────────────────────────────────────
        //  Per-bone max component range (joint limits)
        //  maxComp = sin(maxAngle/2) with ~15% safety margin, capped at InvSqrt2.
        //  Tighter range → more precision at the same bit count.
        //  Precision multiplier = InvSqrt2 / maxComp.
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Maximum quaternion component magnitude per bone slot, derived from
        /// realistic joint rotation limits from T-pose. Components are quantized
        /// within [-maxComp, maxComp] instead of the full [-0.707, 0.707] range.
        /// </summary>
        public static readonly float[] MAX_COMPONENT = new float[]
        {
            // 3-DOF body (9): Spine, Chest, UpperChest, Neck, Head, UpperArms, UpperLegs
            0.45f,                  // Spine         ~52° max → sin(26°)=0.44, +margin → 1.57x precision
            0.45f,                  // Chest         ~52° max → 1.57x
            0.38f,                  // UpperChest    ~42° max → sin(21°)=0.36, +margin → 1.86x
            0.55f,                  // Neck          ~66° max → sin(33°)=0.54, +margin → 1.29x
            InvSqrt2,               // Head          ~90° max → full range needed
            InvSqrt2, InvSqrt2,     // UpperArms     ~180° shoulder range
            InvSqrt2, InvSqrt2,     // UpperLegs     ~130° hip flexion

            // 2-DOF limbs (4): LowerArms, LowerLegs
            InvSqrt2, InvSqrt2,     // LowerArms     ~150° elbow bend
            InvSqrt2, InvSqrt2,     // LowerLegs     ~150° knee bend

            // 2-DOF extremities (6): Shoulders, Hands, Feet
            0.45f, 0.45f,           // Shoulders     ~52° shrug → 1.57x
            0.65f, 0.65f,           // Hands         ~80° wrist bend → 1.09x
            0.52f, 0.52f,           // Feet          ~60° ankle → 1.36x

            // toes/eyes/jaw (5)
            0.40f, 0.40f,           // Toes          ~46° curl → 1.77x
            0.28f, 0.28f,           // Eyes          ~32° look range → 2.53x
            0.28f,                  // Jaw           ~32° open → 2.53x

            // finger proximal (10): curl + spread combined can be ~100°
            InvSqrt2, InvSqrt2, InvSqrt2, InvSqrt2, InvSqrt2,
            InvSqrt2, InvSqrt2, InvSqrt2, InvSqrt2, InvSqrt2,

            // finger intermediate (10): curl ~110°
            InvSqrt2, InvSqrt2, InvSqrt2, InvSqrt2, InvSqrt2,
            InvSqrt2, InvSqrt2, InvSqrt2, InvSqrt2, InvSqrt2,

            // finger distal (10): curl ~80°
            0.62f, 0.62f, 0.62f, 0.62f, 0.62f,
            0.62f, 0.62f, 0.62f, 0.62f, 0.62f,
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
        /// Components are quantized within [-maxRange, maxRange] for better precision
        /// on joints with limited rotation. Use InvSqrt2 for full-range joints.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong EncodeSmallestThree(float qx, float qy, float qz, float qw, int bpc, float maxRange = InvSqrt2)
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

            // Quantize within [-maxRange, maxRange] (clamped for edge cases)
            float invRange = 1f / maxRange;
            uint maxQ = (uint)((1 << bpc) - 1);
            uint qa = Clamp((uint)Math.Round((ClampF(a * invRange, -1f, 1f) * 0.5f + 0.5f) * maxQ), 0, maxQ);
            uint qA = Clamp((uint)Math.Round((ClampF(b * invRange, -1f, 1f) * 0.5f + 0.5f) * maxQ), 0, maxQ);
            uint qC = Clamp((uint)Math.Round((ClampF(c * invRange, -1f, 1f) * 0.5f + 0.5f) * maxQ), 0, maxQ);

            return (ulong)maxIdx | ((ulong)qa << 2) | ((ulong)qA << (2 + bpc)) | ((ulong)qC << (2 + 2 * bpc));
        }

        /// <summary>
        /// Decodes a "smallest three" compressed quaternion into (x,y,z,w).
        /// maxRange must match the value used during encoding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecodeSmallestThree(ulong packed, int bpc, out float qx, out float qy, out float qz, out float qw, float maxRange = InvSqrt2)
        {
            uint mask = (uint)((1 << bpc) - 1);
            int maxIdx = (int)(packed & 3UL);
            uint qa = (uint)((packed >> 2) & mask);
            uint qb = (uint)((packed >> (2 + bpc)) & mask);
            uint qc = (uint)((packed >> (2 + 2 * bpc)) & mask);

            float fMax = (float)mask;
            float a = (qa / fMax * 2f - 1f) * maxRange;
            float b = (qb / fMax * 2f - 1f) * maxRange;
            float c = (qc / fMax * 2f - 1f) * maxRange;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float ClampF(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
