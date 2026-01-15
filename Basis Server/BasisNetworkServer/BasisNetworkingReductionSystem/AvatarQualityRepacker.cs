using System;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public static class AvatarQualityRepacker
    {
        // Layout:
        // [Pos 12][Muscles N][Scale 2][Rotation 16]
        const int PosBytes = 12;
        const int TailBytes = 2 + 16;

        public static (SerializableBasis.LocalAvatarSyncMessage medium,
                      SerializableBasis.LocalAvatarSyncMessage low,
                      SerializableBasis.LocalAvatarSyncMessage veryLow)
            BuildAllLowerFromHigh(in SerializableBasis.LocalAvatarSyncMessage srcHigh)
        {
            if (srcHigh.array == null)
                throw new ArgumentNullException(nameof(srcHigh.array));

            byte[] srcBits = GetBitsPerSlot(BitQuality.High);
            byte[] medBits = GetBitsPerSlot(BitQuality.Medium);
            byte[] lowBits = GetBitsPerSlot(BitQuality.Low);
            byte[] vlowBits = GetBitsPerSlot(BitQuality.VeryLow);

            int slots = WRITE_ORDER.Length;

            int srcMuscleBytes = MuscleBytes(BitQuality.High);
            int medMuscleBytes = MuscleBytes(BitQuality.Medium);
            int lowMuscleBytes = MuscleBytes(BitQuality.Low);
            int vlowMuscleBytes = MuscleBytes(BitQuality.VeryLow);

            int srcExpected = PosBytes + srcMuscleBytes + TailBytes;
            if (srcHigh.array.Length < srcExpected)
                throw new ArgumentException($"High payload too small. Need >= {srcExpected}, got {srcHigh.array.Length}");

            // Allocate outputs
            var med = new SerializableBasis.LocalAvatarSyncMessage
            {
                DataQualityLevel = (byte)BitQuality.Medium,
                array = new byte[PosBytes + medMuscleBytes + TailBytes],
            };
            var low = new SerializableBasis.LocalAvatarSyncMessage
            {
                DataQualityLevel = (byte)BitQuality.Low,
                array = new byte[PosBytes + lowMuscleBytes + TailBytes],
            };
            var vlow = new SerializableBasis.LocalAvatarSyncMessage
            {
                DataQualityLevel = (byte)BitQuality.VeryLow,
                array = new byte[PosBytes + vlowMuscleBytes + TailBytes],
            };

            // 1) Copy position
            Buffer.BlockCopy(srcHigh.array, 0, med.array, 0, PosBytes);
            Buffer.BlockCopy(srcHigh.array, 0, low.array, 0, PosBytes);
            Buffer.BlockCopy(srcHigh.array, 0, vlow.array, 0, PosBytes);

            int srcMuscleBase = PosBytes;
            int medMuscleBase = PosBytes;
            int lowMuscleBase = PosBytes;
            int vlowMuscleBase = PosBytes;

            Array.Clear(med.array, medMuscleBase, medMuscleBytes);
            Array.Clear(low.array, lowMuscleBase, lowMuscleBytes);
            Array.Clear(vlow.array, vlowMuscleBase, vlowMuscleBytes);

            // Precompute bit offsets
            int[] srcOffs = new int[slots];
            int[] medOffs = new int[slots];
            int[] lowOffs = new int[slots];
            int[] vlowOffs = new int[slots];

            int sb = 0, mb = 0, lb = 0, vb = 0;
            for (int i = 0; i < slots; i++)
            {
                srcOffs[i] = sb; sb += srcBits[i];
                medOffs[i] = mb; mb += medBits[i];
                lowOffs[i] = lb; lb += lowBits[i];
                vlowOffs[i] = vb; vb += vlowBits[i];
            }

            // 2) One pass over slots: read once, write three times
            for (int slot = 0; slot < slots; slot++)
            {
                int bSrc = srcBits[slot];

                uint qSrc = BitReader.ReadBits(srcHigh.array, srcMuscleBase, srcOffs[slot], bSrc);

                int bMed = medBits[slot];
                if (bMed > 0)
                    BitWriter.WriteBits(med.array, medMuscleBase, medOffs[slot], RescaleQuant(qSrc, bSrc, bMed), bMed);

                int bLow = lowBits[slot];
                if (bLow > 0)
                    BitWriter.WriteBits(low.array, lowMuscleBase, lowOffs[slot], RescaleQuant(qSrc, bSrc, bLow), bLow);

                int bVLow = vlowBits[slot];
                if (bVLow > 0)
                    BitWriter.WriteBits(vlow.array, vlowMuscleBase, vlowOffs[slot], RescaleQuant(qSrc, bSrc, bVLow), bVLow);
            }

            // 3) Copy Scale+Rotation tail
            int srcTailOffset = PosBytes + srcMuscleBytes;

            Buffer.BlockCopy(srcHigh.array, srcTailOffset, med.array, PosBytes + medMuscleBytes, TailBytes);
            Buffer.BlockCopy(srcHigh.array, srcTailOffset, low.array, PosBytes + lowMuscleBytes, TailBytes);
            Buffer.BlockCopy(srcHigh.array, srcTailOffset, vlow.array, PosBytes + vlowMuscleBytes, TailBytes);

            return (med, low, vlow);
        }
        static uint RescaleQuant(uint qSrc, int bSrc, int bDst)
        {
            if (bSrc == bDst) return qSrc;
            if (bDst <= 0) return 0;

            ulong maxSrc = ((ulong)1 << bSrc) - 1UL;
            ulong maxDst = ((ulong)1 << bDst) - 1UL;

            // round(qSrc * maxDst / maxSrc)
            ulong num = (ulong)qSrc * maxDst + (maxSrc >> 1);
            return (uint)(num / maxSrc);
        }

        static class BitReader
        {
            public static uint ReadBits(byte[] src, int baseByteOffset, int bitPos, int bitCount)
            {
                int bytePos = baseByteOffset + (bitPos >> 3);
                int bitInByte = bitPos & 7;

                uint result = 0;
                int outShift = 0;
                int bitsLeft = bitCount;

                while (bitsLeft > 0)
                {
                    int room = 8 - bitInByte;
                    int take = bitsLeft < room ? bitsLeft : room;

                    uint cur = src[bytePos];
                    cur >>= bitInByte;

                    uint mask = (uint)((1u << take) - 1u);
                    uint chunk = cur & mask;

                    result |= (chunk << outShift);

                    outShift += take;
                    bitsLeft -= take;

                    bytePos++;
                    bitInByte = 0;
                }

                return result;
            }
        }

        static class BitWriter
        {
            public static void WriteBits(byte[] dst, int baseByteOffset, int bitPos, uint value, int bitCount)
            {
                int bytePos = baseByteOffset + (bitPos >> 3);
                int bitInByte = bitPos & 7;

                uint v = value;
                int bitsLeft = bitCount;

                while (bitsLeft > 0)
                {
                    int room = 8 - bitInByte;
                    int take = bitsLeft < room ? bitsLeft : room;

                    uint mask = (uint)((1u << take) - 1u);
                    byte chunk = (byte)(v & mask);

                    dst[bytePos] = (byte)(dst[bytePos] | (chunk << bitInByte));

                    v >>= take;
                    bitsLeft -= take;

                    bytePos++;
                    bitInByte = 0;
                }
            }
        }
    }
}
