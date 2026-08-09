using Basis.Network.Core.Compression;
using Xunit;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;
using S = BasisServerTests.DeltaTestSupport;

namespace BasisServerTests;

/// <summary>
/// The primitives both avatar codecs are built on. These are the invariants that, if they break,
/// break everything downstream silently rather than loudly.
/// </summary>
public class ResidualCodecTests
{
    // ── Channel map ──────────────────────────────────────────────────────────

    /// <summary>
    /// The channel list must be a TOTAL PARTITION of the payload — contiguous, non-overlapping, and
    /// covering every bit including structural padding. Both codecs rebuild payloads purely from
    /// channel values, so any bit not in a channel would silently take the baseline's value forever.
    /// </summary>
    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void ChannelMap_TotallyPartitionsThePayload(BitQuality q)
    {
        var layout = S.Layout(q);
        int expected = 0;
        foreach (var ch in layout.Channels)
        {
            Assert.Equal(expected, ch.BitOffset);
            Assert.InRange((int)ch.Width, 1, BasisResidualCodec.MaxWidth);
            expected += ch.Width;
        }
        Assert.Equal(layout.PayloadBits, expected);
        Assert.Equal(layout.PayloadBits, layout.TotalChannelBits);
        Assert.Equal(S.PayloadSize(q), layout.PayloadBytes);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void ChannelMap_FieldBoundsAreContiguousAndCoverEveryChannel(BitQuality q)
    {
        var layout = S.Layout(q);
        Assert.Equal(BasisAvatarDeltaCompression.FieldCount, layout.FieldCount);
        Assert.Equal(0, layout.FieldChannelStart(0));
        for (int f = 0; f < layout.FieldCount; f++)
            Assert.Equal(layout.FieldChannelEnd(f), layout.FieldChannelStart(f + 1));
        Assert.Equal(layout.Channels.Length, layout.FieldChannelEnd(layout.FieldCount - 1));

        // The end-effector field is empty below High, where the block is not sent at all.
        int effField = BasisAvatarDeltaCompression.FieldCount - 1;
        int effChannels = layout.FieldChannelEnd(effField) - layout.FieldChannelStart(effField);
        if (S.EndEffectorBytes(q) > 0) Assert.True(effChannels > 0);
        else Assert.Equal(0, effChannels);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void ReadChannel_WriteChannel_RoundTripEveryChannel(BitQuality q)
    {
        var rng = new Random(31 + (int)q);
        var layout = S.Layout(q);
        var payload = S.MakePayload(q, rng);
        foreach (var ch in layout.Channels)
        {
            uint v = (uint)rng.NextInt64() & ch.Mask;
            BasisAvatarDeltaCompression.WriteChannel(payload, ch, v);
            Assert.Equal(v, BasisAvatarDeltaCompression.ReadChannel(payload, ch));
        }
        // Writing every channel back must not have disturbed a neighbour: read them all again.
        var expected = new uint[layout.Channels.Length];
        for (int i = 0; i < layout.Channels.Length; i++)
            expected[i] = BasisAvatarDeltaCompression.ReadChannel(payload, layout.Channels[i]);
        var rebuilt = new byte[payload.Length];
        for (int i = 0; i < layout.Channels.Length; i++)
            BasisAvatarDeltaCompression.WriteChannel(rebuilt, layout.Channels[i], expected[i]);
        Assert.Equal(payload, rebuilt);
    }

    // ── Exponential-Golomb ───────────────────────────────────────────────────

    [Fact]
    public void SignedEg_RoundTrips_AndCostMatchesTheAdvertisedBitCount()
    {
        var buf = new byte[64];
        var values = new List<int> { 0, 1, -1, 2, -2, 3, -3, 6, -6, 7, -7, 100, -100, 65535, -65535, int.MaxValue / 4, -(int.MaxValue / 4) };
        var rng = new Random(7);
        for (int i = 0; i < 5000; i++) values.Add(rng.Next(-1 << 24, 1 << 24));

        foreach (int v in values)
        {
            Array.Clear(buf);
            var w = new BasisResidualCodec.BitWriter(buf, 0);
            w.WriteSignedEg(v);
            Assert.Equal(BasisResidualCodec.SignedEgBits(v), w.BitPosition);

            var r = new BasisResidualCodec.BitReader(buf, 0, w.BitPosition);
            Assert.Equal(v, r.ReadSignedEg());
            Assert.False(r.Failed);
            Assert.Equal(w.BitPosition, r.BitPosition);
        }
    }

    [Fact]
    public void SignedEg_ZeroCostsOneBit_AndCostGrowsWithMagnitude()
    {
        Assert.Equal(1, BasisResidualCodec.SignedEgBits(0));
        Assert.Equal(3, BasisResidualCodec.SignedEgBits(1));
        Assert.Equal(3, BasisResidualCodec.SignedEgBits(-1));
        Assert.Equal(5, BasisResidualCodec.SignedEgBits(2));
        Assert.Equal(5, BasisResidualCodec.SignedEgBits(-2));
        int prev = 0;
        for (int v = 0; v < 4096; v++)
        {
            int bits = BasisResidualCodec.SignedEgBits(v);
            Assert.True(bits >= prev, "cost must be non-decreasing in magnitude");
            prev = bits;
        }
    }

    [Fact]
    public void BitReader_PastTheEnd_FailsInsteadOfThrowing()
    {
        var buf = new byte[4];
        var r = new BasisResidualCodec.BitReader(buf, 0, 8);
        r.ReadBits(8);
        Assert.False(r.Failed);
        r.ReadBits(1);
        Assert.True(r.Failed);

        // An all-zero buffer is an unterminated Exp-Golomb prefix; it must give up, not spin.
        var r2 = new BasisResidualCodec.BitReader(new byte[512], 0, 512 * 8);
        r2.ReadSignedEg();
        Assert.True(r2.Failed);
    }

    // ── Exactness ────────────────────────────────────────────────────────────

    /// <summary>
    /// Residual coding must be LOSSLESS for every channel width and every possible pair of values.
    /// An earlier revision companded residuals above a small linear zone; that approximation produced
    /// up to a 180° single-frame bone error whenever a smallest-three index flipped (the 2-bit index
    /// changes what the other three components mean, so a residual measured across a flip is measured
    /// against a stale mapping). This is the property that stops it coming back.
    /// </summary>
    [Fact]
    public void ResidualCoding_IsLossless_ForEveryWidthAndValuePair()
    {
        var rng = new Random(4242);
        var buf = new byte[16];
        for (int w = 2; w <= BasisResidualCodec.MaxWidth; w++)
        {
            uint mask = (1u << w) - 1u;
            for (int i = 0; i < 4000; i++)
            {
                uint cur = (uint)rng.NextInt64() & mask;
                uint est = (uint)rng.NextInt64() & mask;

                int residual = BasisResidualCodec.WrapSigned((int)cur - (int)est, w);

                Array.Clear(buf);
                var wtr = new BasisResidualCodec.BitWriter(buf, 0);
                wtr.WriteSignedEg(residual);
                var rdr = new BasisResidualCodec.BitReader(buf, 0, wtr.BitPosition);
                int decoded = rdr.ReadSignedEg();
                Assert.False(rdr.Failed);
                Assert.Equal(residual, decoded);

                // The reconstruction the codecs perform must land exactly on the sender's value.
                Assert.Equal(cur, (uint)((int)est + decoded) & mask);
            }
        }
    }

    /// <summary>
    /// The escape hatch that makes exactness affordable: a residual can cost more than the value it
    /// describes, so both codecs fall back to a verbatim field. This locks the bound that makes that
    /// fallback correct — a field is never worse than its own width plus one mode bit.
    /// </summary>
    [Fact]
    public void VerbatimFallback_BoundsTheWorstCaseResidual()
    {
        for (int w = 2; w <= BasisResidualCodec.MaxWidth; w++)
        {
            int limit = 1 << (w - 1);
            int worst = 0;
            foreach (int v in new[] { -limit, -limit + 1, -1, 0, 1, limit - 1 })
                worst = Math.Max(worst, BasisResidualCodec.SignedEgBits(v));
            // Exp-Golomb of a full-range residual can be about twice the raw width — which is exactly
            // why the verbatim mode exists rather than being an optimisation.
            Assert.True(worst > w, $"w={w}: worst residual {worst} bits should exceed the {w}-bit raw form");
            Assert.True(worst <= 2 * w + 1);
        }
    }

    [Fact]
    public void WrapSigned_RoundTripsThroughMaskedReconstruction()
    {
        var rng = new Random(99);
        for (int w = 2; w <= BasisResidualCodec.MaxWidth; w++)
        {
            uint mask = (1u << w) - 1u;
            for (int i = 0; i < 2000; i++)
            {
                uint a = (uint)rng.NextInt64() & mask;
                uint b = (uint)rng.NextInt64() & mask;
                int diff = BasisResidualCodec.WrapSigned((int)a - (int)b, w);
                Assert.InRange(diff, -(1 << (w - 1)), (1 << (w - 1)) - 1);
                Assert.Equal(a, (uint)((int)b + diff) & mask);
            }
        }
    }
}
