namespace Basis.Network.Core.Compression
{
    public static class BasisBitPackingConstants
    {
        public const int FloatSize = sizeof(float);
        public const int UShortSize = sizeof(ushort);
        public const int Vector3Size = 3 * FloatSize;

        public const int WritePosition = 12;
        public const int WriteRotation = 16;
        public const int WriteScale = 2;

        public enum BitQuality : byte
        {
            Low = 0,
            Medium = 1,
            High = 2,
        }
        public static int ConvertToSize(BitQuality Quality)
        {
               return WritePosition + WriteScale + WriteRotation + SumBitsPerSlotBytes(GetBitsPerSlot(Quality));
        }
        private static int SumBitsPerSlotBytes(byte[] bitsPerSlot)
        {
            int totalBits = 0;
            for (int i = 0; i < bitsPerSlot.Length; i++)
            {
                totalBits += bitsPerSlot[i];
            }

            // Convert bits → bytes, rounding up
            return (totalBits + 7) >> 3;
        }

        public static byte[] GetBitsPerSlot(BitQuality q) => q switch
        {
            BitQuality.High => BITS_PER_SLOT_HIGH,
            BitQuality.Medium => BITS_PER_SLOT_MEDIUM,
            BitQuality.Low => BITS_PER_SLOT_LOW,
            _ => BITS_PER_SLOT_MEDIUM
        };

        // slot -> muscle index (exactly your existing order, skipping 15..20)
        public static readonly int[] WRITE_ORDER = new int[]
        {
            // 0..14
            0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,

            // Left Leg
            21,22,23,24,25,26,27,28,

            // Right Leg
            29,30,31,32,33,34,35,36,

            // Left Arm
            37,38,39,40,41,42,43,44,45,

            // Right Arm
            46,47,48,49,50,51,52,53,54,

            // Left Hand Fingers
            55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,

            // Right Hand Fingers
            75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,
        };

        // -----------------------------------------
        // MEDIUM = your current table (UNCHANGED)
        // SumBitsPerSlotBytes = 134 bytes
        // AvatarSyncSize = 12 + 16 + 2 + 134 = 164
        // -----------------------------------------
        public static readonly byte[] BITS_PER_SLOT_MEDIUM = new byte[]
        {
            // Spine/Chest/Head (0..14)
            15,15,15,
            15,15,15,
            14,14,14,
            15,15,15,
            15,15,15,

            // Left Leg (15..22 -> muscles 21..28)
            15,15,15,
            15,16,15,
            13,8,

            // Right Leg (23..30 -> muscles 29..36)
            15,15,15,
            15,16,15,
            13,8,

            // Left Arm (31..39 -> muscles 37..45)
            12,12,
            16,16,16,
            15,16,
            15,14,

            // Right Arm (40..48 -> muscles 46..54)
            12,12,
            16,16,16,
            15,16,
            15,14,

            // Left Hand Fingers (49..68 -> muscles 55..74)
            8,13,8,8,
            8,12,8,8,
            8,11,8,8,
            8,11,8,8,
            8,12,8,8,

            // Right Hand Fingers (69..88 -> muscles 75..94)
            8,13,8,8,
            8,12,8,8,
            8,11,8,8,
            8,11,8,8,
            8,12,8,8,
        };

        // -----------------------------------------
        // LOW = 8..12 bits max (requested)
        // SumBitsPerSlotBytes = 116 bytes
        // AvatarSyncSize = 12 + 16 + 2 + 116 = 146
        // -----------------------------------------
        public static readonly byte[] BITS_PER_SLOT_LOW = new byte[]
        {
            // Spine/Chest/Head
            12,12,12,
            12,12,12,
            11,11,11,
            12,12,12,
            12,12,12,

            // Left Leg
            12,12,12,
            12,12,11,
            10,11,

            // Right Leg
            12,12,12,
            12,12,11,
            10,11,

            // Left Arm
            9,9,
            12,12,12,
            11,12,
            11,10,

            // Right Arm
            9,9,
            12,12,12,
            11,12,
            11,10,

            // Left Fingers
            9,10,9,9,
            9,10,9,9,
            9,9,9,9,
            9,9,9,9,
            9,10,9,9,

            // Right Fingers
            9,10,9,9,
            9,10,9,9,
            9,9,9,9,
            9,9,9,9,
            9,10,9,9,
        };

        // -----------------------------------------
        // HIGH = higher precision than your current table
        // SumBitsPerSlotBytes = 164 bytes
        // AvatarSyncSize = 12 + 16 + 2 + 164 = 194
        // -----------------------------------------
        public static readonly byte[] BITS_PER_SLOT_HIGH = new byte[]
        {
            // Spine/Chest/Head
            17,17,17,
            17,17,17,
            16,16,16,
            17,17,17,
            17,17,17,

            // Left Leg
            17,17,17,
            17,18,17,
            15,15,

            // Right Leg
            17,17,17,
            17,18,17,
            15,15,

            // Left Arm
            14,14,
            18,18,18,
            17,18,
            17,16,

            // Right Arm
            14,14,
            18,18,18,
            17,18,
            17,16,

            // Left Fingers (bends up, spreads up)
            12,14,12,12,
            12,13,12,12,
            12,12,12,12,
            12,12,12,12,
            12,13,12,12,

            // Right Fingers
            12,14,12,12,
            12,13,12,12,
            12,12,12,12,
            12,12,12,12,
            12,13,12,12,
        };
    }
}
