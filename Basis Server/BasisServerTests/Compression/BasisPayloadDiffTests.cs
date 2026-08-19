using System;
using Basis.Network.Core.Compression;
using Xunit;

namespace BasisServerTests.Compression
{
    /// <summary>
    /// Pins <see cref="BasisPayloadDiff"/>, and — more importantly — pins the property the delta
    /// encoder actually relies on: a word the scanner reports as clean contains no differing byte.
    /// That is the only thing standing between the prefilter and a silently dropped field, so it is
    /// checked directly rather than inferred from the mask happening to look right.
    /// </summary>
    public class BasisPayloadDiffTests
    {
        private static ulong Oracle(byte[] a, byte[] b, int length)
        {
            ulong mask = 0;
            for (int i = 0; i < length; i++)
            {
                if (a[i] != b[i]) mask |= 1UL << (i >> 3);
            }
            return mask;
        }

        [Fact]
        public void IdenticalPayloadsReportNothingDirty()
        {
            var rng = new Random(4242);
            for (int length = 1; length <= 200; length++)
            {
                var a = new byte[length];
                rng.NextBytes(a);
                var b = (byte[])a.Clone();
                Assert.Equal(0UL, BasisPayloadDiff.WordDiffMask(a, b, length));
            }
        }

        /// <summary>
        /// One byte changed at a time, across every length and every position — this is where a
        /// vector-block boundary or a ragged tail gets a byte wrong.
        /// </summary>
        [Fact]
        public void SingleByteDifferencesLandInTheRightWord()
        {
            var rng = new Random(99);
            for (int length = 1; length <= 200; length++)
            {
                var a = new byte[length];
                rng.NextBytes(a);

                for (int i = 0; i < length; i++)
                {
                    var b = (byte[])a.Clone();
                    b[i] ^= 0x01;

                    ulong expected = 1UL << (i >> 3);
                    Assert.Equal(expected, BasisPayloadDiff.WordDiffMask(a, b, length));
                }
            }
        }

        [Fact]
        public void MatchesTheOracleOnRandomDifferences()
        {
            var rng = new Random(20260819);
            for (int trial = 0; trial < 400; trial++)
            {
                int length = 1 + rng.Next(200);
                var a = new byte[length];
                rng.NextBytes(a);
                var b = (byte[])a.Clone();

                int changes = rng.Next(6);
                for (int c = 0; c < changes; c++)
                {
                    int i = rng.Next(length);
                    b[i] ^= (byte)(1 + rng.Next(255));
                }

                Assert.Equal(Oracle(a, b, length), BasisPayloadDiff.WordDiffMask(a, b, length));
            }
        }

        /// <summary>
        /// The safety property, stated as the encoder uses it: for every word the scanner leaves
        /// clear, all eight of its bytes really are equal. A false negative here is a field the
        /// encoder would skip while it had in fact moved.
        /// </summary>
        [Fact]
        public void AWordReportedCleanContainsNoDifference()
        {
            var rng = new Random(777);
            for (int trial = 0; trial < 400; trial++)
            {
                int length = 1 + rng.Next(400);
                var a = new byte[length];
                var b = new byte[length];
                rng.NextBytes(a);
                Array.Copy(a, b, length);
                for (int c = rng.Next(8); c > 0; c--) b[rng.Next(length)] ^= (byte)(1 + rng.Next(255));

                ulong mask = BasisPayloadDiff.WordDiffMask(a, b, length);

                for (int i = 0; i < length; i++)
                {
                    if ((mask & (1UL << (i >> 3))) != 0) continue;
                    Assert.Equal(a[i], b[i]);
                }
            }
        }

        /// <summary>
        /// The scanner is only ever handed the first <c>length</c> bytes. Buffers are pooled and
        /// routinely longer than the payload, so trailing junk must not register as motion.
        /// </summary>
        [Fact]
        public void IgnoresBytesPastTheStatedLength()
        {
            var a = new byte[128];
            var b = new byte[128];
            new Random(5).NextBytes(a);
            Array.Copy(a, b, a.Length);

            for (int i = 100; i < 128; i++) b[i] = (byte)~b[i];

            Assert.Equal(0UL, BasisPayloadDiff.WordDiffMask(a, b, 100));
        }

        /// <summary>
        /// Every avatar payload must fit the single-ulong word map, or the layout silently loses the
        /// prefilter. Checked here so growth past the ceiling fails loudly at test time.
        /// </summary>
        [Theory]
        [InlineData(BasisAvatarBitPacking.BitQuality.VeryLow)]
        [InlineData(BasisAvatarBitPacking.BitQuality.Low)]
        [InlineData(BasisAvatarBitPacking.BitQuality.Medium)]
        [InlineData(BasisAvatarBitPacking.BitQuality.High)]
        public void EveryQualityLayoutFitsTheWordMap(BasisAvatarBitPacking.BitQuality quality)
        {
            var layout = BasisAvatarChannelMap.For(quality);

            Assert.True(layout.PayloadBytes <= BasisPayloadDiff.MaxPayloadBytes,
                $"{quality} payload is {layout.PayloadBytes} B, past the {BasisPayloadDiff.MaxPayloadBytes} B word-map ceiling");
            Assert.True(layout.WordMaskUsable);
        }

        /// <summary>
        /// The map must cover every bit each field owns. If a field's channel fell outside its word
        /// set, the encoder could skip a field that had moved — so this recomputes the coverage from
        /// the channels rather than trusting the constructor.
        /// </summary>
        [Theory]
        [InlineData(BasisAvatarBitPacking.BitQuality.VeryLow)]
        [InlineData(BasisAvatarBitPacking.BitQuality.Low)]
        [InlineData(BasisAvatarBitPacking.BitQuality.Medium)]
        [InlineData(BasisAvatarBitPacking.BitQuality.High)]
        public void FieldWordMaskCoversEveryBitOfEveryField(BasisAvatarBitPacking.BitQuality quality)
        {
            var layout = BasisAvatarChannelMap.For(quality);

            for (int f = 0; f < layout.FieldCount; f++)
            {
                ulong words = layout.FieldWordMask[f];
                for (int c = layout.FieldChannelStart(f); c < layout.FieldChannelEnd(f); c++)
                {
                    var channel = layout.Channels[c];
                    for (int bit = channel.BitOffset; bit < channel.BitOffset + channel.Width; bit++)
                    {
                        int word = bit >> 6;
                        Assert.True((words & (1UL << word)) != 0,
                            $"{quality} field {f}: bit {bit} (word {word}) is not covered by its word mask");
                    }
                }
            }
        }
    }
}
