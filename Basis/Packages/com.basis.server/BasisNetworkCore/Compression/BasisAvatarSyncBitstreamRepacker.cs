using System;
using Basis.Network.Core.Compression;

namespace Basis.Scripts.Networking.NetworkedAvatar
{
    public static class BasisAvatarSyncBitstreamRepacker
    {
        // Must match your layout: Position -> Scale -> Rotation -> Muscles(bitpacked)
        static int HeaderBytes => BasisBitPackingConstants.WritePosition + BasisBitPackingConstants.WriteScale +  BasisBitPackingConstants.WriteRotation;

        public static byte[] HighToMedium(byte[] highArray) => Repack(highArray,  BasisBitPackingConstants.BitQuality.High, BasisBitPackingConstants.BitQuality.Medium);

        public static byte[] HighToLow(byte[] highArray) => Repack(highArray,  BasisBitPackingConstants.BitQuality.High, BasisBitPackingConstants.BitQuality.Low);

        public static byte[] Repack(byte[] srcArray,  BasisBitPackingConstants.BitQuality srcQuality, BasisBitPackingConstants.BitQuality dstQuality)
        {
            if (srcArray == null) throw new ArgumentNullException(nameof(srcArray));

            int slots = BasisBitPackingConstants.WRITE_ORDER.Length;

            byte[] srcBits = BasisBitPackingConstants.GetBitsPerSlot(srcQuality);
            byte[] dstBits = BasisBitPackingConstants.GetBitsPerSlot(dstQuality);

            // Compute src/dst bit offsets + sizes
            int[] srcOffs = new int[slots];
            int srcTotalBits = 0;
            for (int i = 0; i < slots; i++) { srcOffs[i] = srcTotalBits; srcTotalBits += srcBits[i]; }
            int srcBytes = (srcTotalBits + 7) >> 3;

            int[] dstOffs = new int[slots];
            int dstTotalBits = 0;
            for (int i = 0; i < slots; i++) { dstOffs[i] = dstTotalBits; dstTotalBits += dstBits[i]; }
            int dstBytes = (dstTotalBits + 7) >> 3;

            // Output message = header + dst muscle bytes
            int outSize = HeaderBytes + dstBytes;
            var dstArray = new byte[outSize];

            // Copy header (pos/scale/rot) verbatim.
            // Assumes the header is identical regardless of quality (it is in your design).
            int headerCopy = Math.Min(HeaderBytes, srcArray.Length);
            Buffer.BlockCopy(srcArray, 0, dstArray, 0, headerCopy);

            // Repack muscles: read from src muscle stream, rescale integer, write into dst stream.
            int srcMuscleBase = HeaderBytes;
            int dstMuscleBase = HeaderBytes;

            // Safety: ensure src has enough bytes for the src muscle stream
            if (srcArray.Length < HeaderBytes + srcBytes)
            {
                throw new ArgumentException($"srcArray too small for {srcQuality} muscle stream. Need >= {HeaderBytes + srcBytes} bytes, got {srcArray.Length}.");
            }

            // Clear destination packed region (we OR bits as we write)
            Array.Clear(dstArray, dstMuscleBase, dstBytes);

            for (int slot = 0; slot < slots; slot++)
            {
                int bSrc = srcBits[slot];
                int bDst = dstBits[slot];

                uint qSrc = BitReader.ReadBits(srcArray, srcMuscleBase, srcOffs[slot], bSrc);

                uint qDst = RescaleQuant(qSrc, bSrc, bDst);

                BitWriter.WriteBits(dstArray, dstMuscleBase, dstOffs[slot], qDst, bDst);
            }

            return dstArray;
        }

        // Maps q in [0..(2^bSrc-1)] to [0..(2^bDst-1)] with rounding.
        static uint RescaleQuant(uint qSrc, int bSrc, int bDst)
        {
            if (bSrc == bDst) return qSrc;
            if (bDst <= 0) return 0;

            ulong maxSrc = ((ulong)1 << bSrc) - 1UL;
            ulong maxDst = ((ulong)1 << bDst) - 1UL;

            // round(qSrc * maxDst / maxSrc) using integer math:
            // (qSrc*maxDst + maxSrc/2) / maxSrc
            ulong num = (ulong)qSrc * maxDst + (maxSrc >> 1);
            ulong qDst = num / maxSrc;

            return (uint)qDst;
        }

        static class BitReader
        {
            public static uint ReadBits(byte[] src, int baseByteOffset, int bitPos, int bitCount)
            {
                int bytePos = baseByteOffset + (bitPos >> 3);
                int bitInByte = bitPos & 7;

                uint result = 0;
                int written = 0;

                while (bitCount > 0)
                {
                    int room = 8 - bitInByte;
                    int take = bitCount < room ? bitCount : room;

                    uint cur = src[bytePos];
                    cur >>= bitInByte;

                    uint mask = (uint)((1u << take) - 1u);
                    uint chunk = cur & mask;

                    result |= (chunk << written);

                    written += take;
                    bitCount -= take;

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
