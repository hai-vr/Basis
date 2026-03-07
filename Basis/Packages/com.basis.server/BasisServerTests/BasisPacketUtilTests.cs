using BasisNetworkCore;
using Xunit;

namespace BasisServerTests
{
    public class BasisPacketUtilTests
    {
        [Theory]
        [InlineData(1, 0, true)]   // 1 is newer than 0
        [InlineData(10, 5, true)]  // 10 is newer than 5
        [InlineData(0, 255, true)] // Wraps around: 0 is newer than 255 (diff=1 < 128)
        [InlineData(5, 250, true)] // Wraps around: 5 is newer than 250 (diff=11 < 128)
        [InlineData(127, 0, true)] // 127 is newer than 0 (diff=127 < 128)
        public void IsNewer_ReturnsTrue_WhenSeq1IsNewer(byte seq1, byte seq2, bool expected)
        {
            Assert.Equal(expected, BasisPacketUtil.IsNewer(seq1, seq2));
        }

        [Theory]
        [InlineData(0, 1, false)]   // 0 is not newer than 1
        [InlineData(5, 10, false)]  // 5 is not newer than 10
        public void IsNewer_ReturnsFalse_WhenSeq1IsNotNewer(byte seq1, byte seq2, bool expected)
        {
            Assert.Equal(expected, BasisPacketUtil.IsNewer(seq1, seq2));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(5, 5)]
        [InlineData(255, 255)]
        public void IsNewer_EqualValues_ReturnsTrue(byte seq1, byte seq2)
        {
            // (byte)(n-n) = 0, and 0 < 128 = true
            // ValidatePacket handles the "must be different" check separately
            Assert.True(BasisPacketUtil.IsNewer(seq1, seq2));
        }

        [Fact]
        public void ValidatePacket_NewerAndDifferent_ReturnsTrue()
        {
            Assert.True(BasisPacketUtil.ValidatePacket(1, 0));
            Assert.True(BasisPacketUtil.ValidatePacket(10, 5));
        }

        [Fact]
        public void ValidatePacket_EqualValues_ReturnsFalse()
        {
            // Even though IsNewer returns true for equal, ValidatePacket requires New != Old
            Assert.False(BasisPacketUtil.ValidatePacket(5, 5));
            Assert.False(BasisPacketUtil.ValidatePacket(0, 0));
            Assert.False(BasisPacketUtil.ValidatePacket(255, 255));
        }

        [Fact]
        public void ValidatePacket_OlderPacket_ReturnsFalse()
        {
            // 5 is older than 10 in normal sequence
            Assert.False(BasisPacketUtil.ValidatePacket(5, 10));
        }

        [Fact]
        public void ValidatePacket_WrappedSequence_ReturnsTrue()
        {
            // Sequence 0 wraps past 255
            Assert.True(BasisPacketUtil.ValidatePacket(0, 255));
            Assert.True(BasisPacketUtil.ValidatePacket(1, 255));
        }

        [Theory]
        [InlineData(128, 0)]   // diff=128, NOT < 128 => false
        [InlineData(200, 50)]  // diff=150, NOT < 128 => false
        public void IsNewer_ReturnsFalse_WhenDiffIs128OrMore(byte seq1, byte seq2)
        {
            Assert.False(BasisPacketUtil.IsNewer(seq1, seq2));
        }
    }
}
