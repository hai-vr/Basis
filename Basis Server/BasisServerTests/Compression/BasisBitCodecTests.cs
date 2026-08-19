using System;
using Basis.Network.Core.Compression;
using Xunit;

namespace BasisServerTests.Compression
{
    /// <summary>
    /// Pins <see cref="BasisBitCodec"/> against a bit-at-a-time oracle written independently below.
    ///
    /// <para>The codec has two paths — a single unaligned 64-bit access, and the original byte-walking
    /// loop for fields too close to the end of the buffer to load a full word over. Both are checked
    /// here, and against the oracle rather than against each other, so a shared misunderstanding of
    /// the bit order cannot pass. The buffer is sized so that low offsets exercise the wide path and
    /// high ones fall into the narrow path; <see cref="BothPathsAreActuallyExercised"/> asserts that
    /// split rather than assuming it.</para>
    /// </summary>
    public class BasisBitCodecTests
    {
        private const int BufferBytes = 32;
        private const int BufferBits = BufferBytes * 8;

        // ── Oracle: the definition of the format, one bit at a time. LSB-first within a byte. ──

        private static ulong OracleRead(byte[] src, int bitPos, int bitCount)
        {
            ulong value = 0;
            for (int k = 0; k < bitCount; k++)
            {
                int b = bitPos + k;
                if ((src[b >> 3] & (1 << (b & 7))) != 0) value |= 1UL << k;
            }
            return value;
        }

        private static void OracleOr(byte[] dst, int bitPos, ulong value, int bitCount)
        {
            for (int k = 0; k < bitCount; k++)
            {
                if (((value >> k) & 1UL) == 0) continue;
                int b = bitPos + k;
                dst[b >> 3] |= (byte)(1 << (b & 7));
            }
        }

        private static void OracleReplace(byte[] dst, int bitPos, ulong value, int bitCount)
        {
            for (int k = 0; k < bitCount; k++)
            {
                int b = bitPos + k;
                int bit = 1 << (b & 7);
                if (((value >> k) & 1UL) != 0) dst[b >> 3] |= (byte)bit;
                else dst[b >> 3] &= (byte)~bit;
            }
        }

        private static byte[] Pattern(int seed)
        {
            var rng = new Random(seed);
            var buffer = new byte[BufferBytes];
            rng.NextBytes(buffer);
            return buffer;
        }

        [Fact]
        public void ReadMatchesTheOracleAtEveryOffsetAndWidth()
        {
            byte[] source = Pattern(12345);

            for (int bitPos = 0; bitPos < BufferBits; bitPos++)
            {
                int maxWidth = Math.Min(64, BufferBits - bitPos);
                for (int width = 1; width <= maxWidth; width++)
                {
                    Assert.Equal(OracleRead(source, bitPos, width), BasisBitCodec.Read(source, bitPos, width));
                }
            }
        }

        [Fact]
        public void OrMatchesTheOracleAtEveryOffsetAndWidth()
        {
            // Values with bits set above the width prove the codec masks rather than bleeding into
            // the neighbouring field — the failure mode that corrupts an adjacent bone silently.
            ulong[] values = { 0UL, 1UL, 0x5555555555555555UL, 0xAAAAAAAAAAAAAAAAUL, ulong.MaxValue };

            for (int bitPos = 0; bitPos < BufferBits; bitPos++)
            {
                int maxWidth = Math.Min(64, BufferBits - bitPos);
                for (int width = 1; width <= maxWidth; width++)
                {
                    foreach (ulong value in values)
                    {
                        byte[] actual = Pattern(777);
                        byte[] expected = (byte[])actual.Clone();

                        BasisBitCodec.Or(actual, bitPos, value, width);
                        OracleOr(expected, bitPos, value, width);

                        Assert.Equal(expected, actual);
                    }
                }
            }
        }

        [Fact]
        public void ReplaceMatchesTheOracleAtEveryOffsetAndWidth()
        {
            ulong[] values = { 0UL, 1UL, 0x5555555555555555UL, 0xAAAAAAAAAAAAAAAAUL, ulong.MaxValue };

            for (int bitPos = 0; bitPos < BufferBits; bitPos++)
            {
                int maxWidth = Math.Min(64, BufferBits - bitPos);
                for (int width = 1; width <= maxWidth; width++)
                {
                    foreach (ulong value in values)
                    {
                        byte[] actual = Pattern(999);
                        byte[] expected = (byte[])actual.Clone();

                        BasisBitCodec.Replace(actual, bitPos, value, width);
                        OracleReplace(expected, bitPos, value, width);

                        Assert.Equal(expected, actual);
                    }
                }
            }
        }

        /// <summary>
        /// A round trip through the exact-sized buffers the codecs really use. Every field lands at
        /// the very end of its buffer at some point, which is the case the narrow path exists for.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(5)]
        [InlineData(8)]
        [InlineData(9)]
        public void RoundTripsAtTheEndOfAnExactSizedBuffer(int sizeBytes)
        {
            for (int width = 1; width <= Math.Min(57, sizeBytes * 8); width++)
            {
                for (int bitPos = 0; bitPos + width <= sizeBytes * 8; bitPos++)
                {
                    ulong value = 0xDEADBEEFCAFEF00DUL & ((width >= 64) ? ulong.MaxValue : (1UL << width) - 1UL);

                    var buffer = new byte[sizeBytes];
                    BasisBitCodec.Replace(buffer, bitPos, value, width);
                    Assert.Equal(value, BasisBitCodec.Read(buffer, bitPos, width));

                    // Replace must leave every other bit alone.
                    var neighbours = new byte[sizeBytes];
                    for (int k = 0; k < sizeBytes; k++) neighbours[k] = 0xFF;
                    var expected = (byte[])neighbours.Clone();
                    BasisBitCodec.Replace(neighbours, bitPos, value, width);
                    OracleReplace(expected, bitPos, value, width);
                    Assert.Equal(expected, neighbours);
                }
            }
        }

        /// <summary>
        /// Guards the coverage itself: if the buffer above were ever sized so that everything fell
        /// down the narrow path, the tests would still pass and the wide path would ship unchecked.
        /// </summary>
        [Fact]
        public void BothPathsAreActuallyExercised()
        {
            const int wordBytes = 8;
            int wideOffsets = 0, narrowOffsets = 0;

            for (int bitPos = 0; bitPos < BufferBits; bitPos++)
            {
                if ((bitPos >> 3) + wordBytes <= BufferBytes) wideOffsets++;
                else narrowOffsets++;
            }

            Assert.True(wideOffsets > 0, "no offset in the fixture reaches the wide path");
            Assert.True(narrowOffsets > 0, "no offset in the fixture reaches the narrow path");
        }

        /// <summary>
        /// Widths past <see cref="BasisBitCodec.MaxWideBits"/> cannot use a single word load and must
        /// still be correct — they are the reason the narrow path handles up to 64 bits.
        /// </summary>
        [Fact]
        public void WidthsBeyondTheWideLimitStillRoundTrip()
        {
            for (int width = BasisBitCodec.MaxWideBits + 1; width <= 64; width++)
            {
                for (int bitPos = 0; bitPos < 16; bitPos++)
                {
                    ulong mask = width >= 64 ? ulong.MaxValue : (1UL << width) - 1UL;
                    ulong value = 0x0123456789ABCDEFUL & mask;

                    var buffer = new byte[BufferBytes];
                    BasisBitCodec.Replace(buffer, bitPos, value, width);
                    Assert.Equal(value, BasisBitCodec.Read(buffer, bitPos, width));
                }
            }
        }
    }
}
