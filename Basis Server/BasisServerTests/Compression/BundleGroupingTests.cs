using Basis.Network.Core;
using Basis.Network.Core.Compression;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using Xunit;

namespace BasisServerTests;

/// <summary>
/// Round-trips the v50 grouped avatar bundle: real writer (BuildRawForRange) into the real reader
/// (BasisAvatarBundleCodec.TryFlatten), asserting every entry comes back on its original channel
/// with its original bytes and this receiver's interval byte patched in.
///
/// The format's two sharp edges are the column-transposed DeltaAvatarChannel group and the
/// per-receiver interval patch, which the writer applies mid-transpose. Both are covered here with
/// ragged body lengths, since equal-length bodies would hide an indexing error in the un-transpose.
/// </summary>
public class BundleGroupingTests
{
    /// <summary>One expected entry, in the order the writer will emit it.</summary>
    private sealed record Expected(byte Channel, byte[] Body);

    private static PendingAvatarSend Make(byte channel, byte intervalOffset, byte interval, params byte[] body)
        => new()
        {
            Source = body,
            Length = body.Length,
            Channel = channel,
            Interval = interval,
            IntervalOffset = intervalOffset,
        };

    /// <summary>Writer → reader, returning the flat entries the client would dispatch.</summary>
    private static List<(byte Channel, byte[] Body)> RoundTrip(PendingAvatarSend[] pending, int count)
    {
        var state = new PlayerState();
        BasisServerReductionSystemEvents.TestOnly_SortPendingByChannel(state, pending, count);
        int rawLen = BasisServerReductionSystemEvents.TestOnly_BuildRawForRange(state, pending, 0, count);

        var flat = new byte[BasisAvatarBundleCodec.MaxFlatSize(rawLen)];
        Assert.True(
            BasisAvatarBundleCodec.TryFlatten(state.BundleRawScratch.AsSpan(0, rawLen), flat, out int flatLen),
            "TryFlatten rejected output the writer just produced");

        var outp = new List<(byte, byte[])>();
        int offset = 0;
        while (offset + 3 <= flatLen)
        {
            byte channel = flat[offset];
            int len = flat[offset + 1] | (flat[offset + 2] << 8);
            offset += 3;
            Assert.True(len > 0 && offset + len <= flatLen, "flat frame overran the buffer");
            outp.Add((channel, flat.AsSpan(offset, len).ToArray()));
            offset += len;
        }
        Assert.Equal(flatLen, offset);
        return outp;
    }

    /// <summary>What the receiver must see: the source bytes with its own interval byte patched in.</summary>
    private static byte[] Patched(in PendingAvatarSend p)
    {
        var b = (byte[])p.Source.Clone();
        b[p.IntervalOffset] = p.Interval;
        return b;
    }

    private static void AssertRoundTrips(PendingAvatarSend[] pending, int count)
    {
        // Snapshot before the sort reorders the array under us.
        var expected = new List<Expected>();
        for (int i = 0; i < count; i++) expected.Add(new Expected(pending[i].Channel, Patched(pending[i])));

        var got = RoundTrip(pending, count);

        // Grouping reorders entries, so compare as multisets keyed by channel.
        Assert.Equal(expected.Count, got.Count);
        foreach (var group in expected.GroupBy(e => e.Channel))
        {
            var mine = got.Where(g => g.Channel == group.Key).Select(g => g.Body).ToList();
            Assert.Equal(group.Count(), mine.Count);
            foreach (var want in group)
                Assert.True(mine.Any(m => m.AsSpan().SequenceEqual(want.Body)),
                    $"channel {group.Key}: no returned body matched {BitConverter.ToString(want.Body)}");
        }
    }

    private static byte[] Ramp(int len, int seed)
    {
        var b = new byte[len];
        for (int i = 0; i < len; i++) b[i] = (byte)(seed * 31 + i * 7);
        return b;
    }

    [Fact]
    public void FixedSizeQualityGroup_RoundTrips()
    {
        var pending = new[]
        {
            Make(BasisNetworkCommons.PlayerAvatarHighChannel, 1, 40, Ramp(12, 1)),
            Make(BasisNetworkCommons.PlayerAvatarHighChannel, 1, 41, Ramp(12, 2)),
            Make(BasisNetworkCommons.PlayerAvatarLowChannel, 1, 42, Ramp(8, 3)),
        };
        AssertRoundTrips(pending, pending.Length);
    }

    [Fact]
    public void TransposedDeltaGroup_WithRaggedLengths_RoundTrips()
    {
        // Ragged on purpose: equal lengths make a column-index bug invisible.
        var pending = new[]
        {
            Make(BasisNetworkCommons.DeltaAvatarChannel, 2, 50, Ramp(5, 1)),
            Make(BasisNetworkCommons.DeltaAvatarChannel, 2, 51, Ramp(17, 2)),
            Make(BasisNetworkCommons.DeltaAvatarChannel, 2, 52, Ramp(3, 3)),
            Make(BasisNetworkCommons.DeltaAvatarChannel, 3, 53, Ramp(31, 4)),
        };
        AssertRoundTrips(pending, pending.Length);
    }

    [Fact]
    public void MixedChannels_InterleavedOnArrival_RoundTrip()
    {
        // Deliberately interleaved so the sort has real work to do.
        var pending = new[]
        {
            Make(BasisNetworkCommons.DeltaAvatarChannel, 2, 60, Ramp(9, 1)),
            Make(BasisNetworkCommons.PlayerAvatarHighChannel, 1, 61, Ramp(14, 2)),
            Make(BasisNetworkCommons.DeltaAvatarChannel, 2, 62, Ramp(4, 3)),
            Make(BasisNetworkCommons.PlayerAvatarVeryLowLargeChannel, 2, 63, Ramp(11, 4)),
            Make(BasisNetworkCommons.PlayerAvatarHighChannel, 1, 64, Ramp(14, 5)),
            Make(BasisNetworkCommons.DeltaAvatarChannel, 3, 65, Ramp(22, 6)),
            Make(BasisNetworkCommons.PlayerAvatarHighAdditionalChannel, 1, 66, Ramp(19, 7)),
        };
        AssertRoundTrips(pending, pending.Length);
    }

    [Fact]
    public void EntriesShorterThanTheirIntervalOffset_AreDropped()
    {
        // Length <= IntervalOffset means there is no room for the interval byte; the writer skips
        // these, and the group count must reflect that rather than counting them and desyncing.
        var pending = new[]
        {
            Make(BasisNetworkCommons.PlayerAvatarHighChannel, 1, 70, Ramp(10, 1)),
            Make(BasisNetworkCommons.PlayerAvatarHighChannel, 5, 71, Ramp(3, 2)),   // dropped
            Make(BasisNetworkCommons.PlayerAvatarHighChannel, 1, 72, Ramp(10, 3)),
        };
        var got = RoundTrip(pending, pending.Length);
        Assert.Equal(2, got.Count);
        Assert.All(got, g => Assert.Equal(BasisNetworkCommons.PlayerAvatarHighChannel, g.Channel));
    }

    [Fact]
    public void SortIsStableEnoughThatEveryChannelFormsOneRun()
    {
        var pending = new[]
        {
            Make(BasisNetworkCommons.DeltaAvatarChannel, 2, 80, Ramp(6, 1)),
            Make(BasisNetworkCommons.PlayerAvatarHighChannel, 1, 81, Ramp(6, 2)),
            Make(BasisNetworkCommons.DeltaAvatarChannel, 2, 82, Ramp(6, 3)),
            Make(BasisNetworkCommons.PlayerAvatarHighChannel, 1, 83, Ramp(6, 4)),
        };
        var state = new PlayerState();
        BasisServerReductionSystemEvents.TestOnly_SortPendingByChannel(state, pending, pending.Length);

        for (int i = 1; i < pending.Length; i++)
            Assert.True(pending[i - 1].Channel <= pending[i].Channel, "pending was not channel-ordered");
    }

    [Fact]
    public void TruncatedGroup_IsRejectedRatherThanMisparsed()
    {
        var pending = new[]
        {
            Make(BasisNetworkCommons.DeltaAvatarChannel, 2, 90, Ramp(12, 1)),
            Make(BasisNetworkCommons.DeltaAvatarChannel, 2, 91, Ramp(12, 2)),
        };
        var state = new PlayerState();
        int rawLen = BasisServerReductionSystemEvents.TestOnly_BuildRawForRange(state, pending, 0, pending.Length);

        var flat = new byte[BasisAvatarBundleCodec.MaxFlatSize(rawLen)];
        for (int cut = 1; cut < rawLen; cut++)
        {
            Assert.False(
                BasisAvatarBundleCodec.TryFlatten(state.BundleRawScratch.AsSpan(0, rawLen - cut), flat, out _),
                $"a body truncated by {cut} byte(s) was accepted");
        }
    }
}
