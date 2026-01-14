using System;
using Basis.Network.Core.Compression;
using static Basis.Network.Core.Compression.BasisBitPackingConstants;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public static class AvatarQualityRepacker
    {
        // Layout:
        // [Pos 12][Muscles N][Scale 2][Rotation 16]
        const int PosBytes = 12;
        const int TailBytes = 2 + 16;

        public static SerializableBasis.LocalAvatarSyncMessage BuildMediumFromHigh(in SerializableBasis.LocalAvatarSyncMessage srcHigh)
            => BuildFromHigh(srcHigh, BitQuality.Medium);

        public static SerializableBasis.LocalAvatarSyncMessage BuildLowFromHigh(in SerializableBasis.LocalAvatarSyncMessage srcHigh)
            => BuildFromHigh(srcHigh, BitQuality.Low);

        static SerializableBasis.LocalAvatarSyncMessage BuildFromHigh(in SerializableBasis.LocalAvatarSyncMessage srcHigh, BitQuality target)
        {
            if (srcHigh.array == null) throw new ArgumentNullException(nameof(srcHigh.array));

            // Source bit profile (High)
            byte[] srcBits = GetBitsPerSlot(BitQuality.High);
            byte[] dstBits = GetBitsPerSlot(target);

            int slots = WRITE_ORDER.Length;

            // Compute muscle stream sizes
            int srcMuscleBytes = MuscleBytes(BitQuality.High);
            int dstMuscleBytes = MuscleBytes(target);

            int srcExpected = PosBytes + srcMuscleBytes + TailBytes;
            if (srcHigh.array.Length < srcExpected)
                throw new ArgumentException($"High payload too small. Need >= {srcExpected}, got {srcHigh.array.Length}");

            int dstPayloadSize = PosBytes + dstMuscleBytes + TailBytes;

            var dst = new SerializableBasis.LocalAvatarSyncMessage
            {
                DataQualityLevel = (byte)target,
                array = new byte[dstPayloadSize],
            };

            // 1) Copy Position (first 12)
            Buffer.BlockCopy(srcHigh.array, 0, dst.array, 0, PosBytes);

            // 2) Repack muscle bitstream
            // Muscles start immediately after position
            int srcMuscleBase = PosBytes;
            int dstMuscleBase = PosBytes;

            Array.Clear(dst.array, dstMuscleBase, dstMuscleBytes);

            // Precompute bit offsets (could be cached statically for zero-alloc)
            int[] srcOffs = new int[slots];
            int[] dstOffs = new int[slots];
            int srcBit = 0, dstBit = 0;
            for (int i = 0; i < slots; i++)
            {
                srcOffs[i] = srcBit; srcBit += srcBits[i];
                dstOffs[i] = dstBit; dstBit += dstBits[i];
            }

            for (int slot = 0; slot < slots; slot++)
            {
                int bSrc = srcBits[slot];
                int bDst = dstBits[slot];

                uint qSrc = BitReader.ReadBits(srcHigh.array, srcMuscleBase, srcOffs[slot], bSrc);
                uint qDst = RescaleQuant(qSrc, bSrc, bDst);

                BitWriter.WriteBits(dst.array, dstMuscleBase, dstOffs[slot], qDst, bDst);
            }

            // 3) Copy Scale+Rotation tail
            // Tail in source begins after src muscle bytes
            int srcTailOffset = PosBytes + srcMuscleBytes;
            int dstTailOffset = PosBytes + dstMuscleBytes;

            Buffer.BlockCopy(srcHigh.array, srcTailOffset, dst.array, dstTailOffset, TailBytes);

            return dst;
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
