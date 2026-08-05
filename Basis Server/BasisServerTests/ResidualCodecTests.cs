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

    // ── Companding ───────────────────────────────────────────────────────────

    [Fact]
    public void Compand_IsExactInsideTheLinearZone()
    {
        for (int w = 4; w <= BasisResidualCodec.MaxWidth; w++)
            for (int v = -BasisResidualCodec.LinearZone; v <= BasisResidualCodec.LinearZone; v++)
            {
                int code = BasisResidualCodec.Compand(v, w);
                Assert.Equal(v, code);
                Assert.Equal(v, BasisResidualCodec.Decompand(code, w));
            }
    }

    [Fact]
    public void Compand_IsMonotone_BoundedInError_AndNeverEscapesTheChannel()
    {
        for (int w = 2; w <= BasisResidualCodec.MaxWidth; w++)
        {
            int limit = 1 << (w - 1);
            int prev = int.MinValue;
            for (int v = -limit; v < limit; v++)
            {
                int code = BasisResidualCodec.Compand(v, w);
                int back = BasisResidualCodec.Decompand(code, w);

                Assert.True(Math.Abs(code) <= BasisResidualCodec.MaxCode(w));
                Assert.True(Math.Abs(back) <= limit, $"w={w} v={v}: decompanded {back} exceeds the wrap range");
                Assert.True(code >= prev, $"w={w}: Compand must be non-decreasing (v={v})");
                prev = code;

                // Above the linear zone the law is geometric with ratio 7/5, so the reconstruction is
                // within ~20% of the input. Sign must always survive.
                if (v != 0) Assert.True(Math.Sign(back) == Math.Sign(v));
                int err = Math.Abs(back - v);
                Assert.True(err <= BasisResidualCodec.LinearZone + Math.Abs(v) / 4,
                    $"w={w} v={v}: companding error {err} too large (back={back})");
            }
        }
    }

    [Fact]
    public void Compand_UsesNoFloatingPoint_SoBothEndsAgreeBitForBit()
    {
        // Guard against a refactor reintroducing Math.Pow/Math.Log: the table must be reproducible by
        // exact integer arithmetic. Rebuild it here the same way and compare.
        for (int w = 2; w <= BasisResidualCodec.MaxWidth; w++)
        {
            int limit = 1 << (w - 1);
            var mags = new List<int>();
            for (int c = 0; c <= BasisResidualCodec.LinearZone && c <= limit; c++) mags.Add(c);
            int v = mags[^1];
            while (v < limit)
            {
                int next = (v * 7 + 4) / 5;
                if (next <= v) next = v + 1;
                if (next > limit) next = limit;
                mags.Add(next);
                v = next;
            }
            Assert.Equal(mags.Count - 1, BasisResidualCodec.MaxCode(w));
            for (int c = 0; c < mags.Count; c++)
                Assert.Equal(mags[c], BasisResidualCodec.Decompand(c, w));
        }
    }

    // ── Gray code and the sweep ──────────────────────────────────────────────

    [Fact]
    public void Gray_RoundTrips_AndAdjacentValuesDifferInExactlyOneBit()
    {
        for (uint v = 0; v < 70000; v++)
            Assert.Equal(v, BasisResidualCodec.FromGray(BasisResidualCodec.ToGray(v)));

        for (uint v = 0; v < 70000; v++)
        {
            uint diff = BasisResidualCodec.ToGray(v) ^ BasisResidualCodec.ToGray(v + 1);
            Assert.Equal(1, System.Numerics.BitOperations.PopCount(diff));
        }
    }

    /// <summary>
    /// Gray adjacency must also hold across the wrap for every channel width, because a signed field
    /// crossing zero moves between the top and bottom of its range. If it did not, a sweep bit near
    /// the wrap would move the estimate by half the channel instead of one step.
    /// </summary>
    [Fact]
    public void Gray_IsCyclic_ForEveryChannelWidth()
    {
        for (int w = 2; w <= BasisResidualCodec.MaxWidth; w++)
        {
            uint top = (1u << w) - 1u;
            uint diff = BasisResidualCodec.ToGray(top) ^ BasisResidualCodec.ToGray(0);
            Assert.Equal(1, System.Numerics.BitOperations.PopCount(diff));
        }
    }

    /// <summary>
    /// One pass of the sweep must visit every bit position exactly once, for every width. If it were
    /// not a permutation, some bit would never be published and a receiver could stay permanently
    /// wrong in that position after a loss — the exact failure the sweep exists to prevent.
    /// </summary>
    [Fact]
    public void SweepBitIndex_IsAPermutationOfEveryWidth()
    {
        for (int w = 1; w <= BasisResidualCodec.MaxWidth; w++)
        {
            var seen = new bool[w];
            for (int s = 0; s < w; s++)
            {
                int idx = BasisResidualCodec.SweepBitIndex(s, w);
                Assert.InRange(idx, 0, w - 1);
                Assert.False(seen[idx], $"width {w}: bit {idx} visited twice within one pass");
                seen[idx] = true;
            }
            Assert.All(seen, Assert.True);

            // Also true starting from any phase, since sequence numbers wrap at 256.
            for (int phase = 0; phase < 40; phase++)
            {
                var seen2 = new bool[w];
                for (int s = phase; s < phase + w; s++) seen2[BasisResidualCodec.SweepBitIndex(s, w)] = true;
                Assert.All(seen2, Assert.True);
            }
        }
    }

    [Fact]
    public void SweepBitIndex_StartsHigh_SoLargeErrorsAreCorrectedFirst()
    {
        // The first position of a pass is the top bit: a wrong high bit is a large error, and waiting
        // a whole pass to fix it is what makes a naive low-to-high sweep feel like a stuck avatar.
        for (int w = 2; w <= BasisResidualCodec.MaxWidth; w++)
            Assert.Equal(w - 1, BasisResidualCodec.SweepBitIndex(0, w));
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
