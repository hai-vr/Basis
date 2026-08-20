using System.Collections.Generic;
using Basis.Network.Core;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using Xunit;

namespace BasisServerTests;

/// <summary>
/// The order a receiver's pending avatar sends are written to its peer.
///
/// <para>The unreliable queue discards from the front when it is over budget, so this order decides
/// who keeps moving on an overloaded server. These tests pin the two properties that guarantee it:
/// channels stay grouped (bundling needs the runs) and the rarest-updated tier is written last.</para>
/// </summary>
public sealed class AvatarFlushOrderTests
{
    private static readonly byte[] SmallIdChannels =
    {
        BasisNetworkCommons.PlayerAvatarVeryLowChannel,
        BasisNetworkCommons.PlayerAvatarLowChannel,
        BasisNetworkCommons.PlayerAvatarMediumChannel,
        BasisNetworkCommons.PlayerAvatarHighChannel,
    };

    private static readonly byte[] LargeIdChannels =
    {
        BasisNetworkCommons.PlayerAvatarVeryLowLargeChannel,
        BasisNetworkCommons.PlayerAvatarLowLargeChannel,
        BasisNetworkCommons.PlayerAvatarMediumLargeChannel,
        BasisNetworkCommons.PlayerAvatarHighLargeChannel,
    };

    [Fact]
    public void RarestTierIsWrittenLast()
    {
        byte[] channels = BuildMixedChannels();
        PendingAvatarSend[] sorted = Sort(channels, out int count);

        for (int quality = 0; quality < 4; quality++)
        {
            for (int higher = quality + 1; higher < 4; higher++)
            {
                Assert.True(LastIndexOfTier(sorted, count, higher) < FirstIndexOfTier(sorted, count, quality),
                    $"every tier-{higher} entry must be written before every tier-{quality} entry");
            }
        }
    }

    [Fact]
    public void IdWidthDoesNotDecideWhoSurvives()
    {
        byte[] channels = BuildMixedChannels();
        PendingAvatarSend[] sorted = Sort(channels, out int count);

        for (int quality = 0; quality < 4; quality++)
        {
            int small = FirstIndexOf(sorted, count, SmallIdChannels[quality]);
            int large = FirstIndexOf(sorted, count, LargeIdChannels[quality]);
            int nextTierStart = quality == 0 ? count : FirstIndexOfTier(sorted, count, quality - 1);
            Assert.True(small < nextTierStart && large < nextTierStart,
                "both id widths of a tier must stay inside that tier's run");
        }
    }

    [Fact]
    public void DeltaFramesOutliveNearKeyframesButNotDistantOnes()
    {
        var channels = new List<byte>(BuildMixedChannels());
        for (int i = 0; i < 3; i++) channels.Add(BasisNetworkCommons.DeltaAvatarChannel);
        PendingAvatarSend[] sorted = Sort(channels.ToArray(), out int count);

        int firstDelta = FirstIndexOf(sorted, count, BasisNetworkCommons.DeltaAvatarChannel);
        int lastDelta = firstDelta + 2;
        Assert.Equal(BasisNetworkCommons.DeltaAvatarChannel, sorted[lastDelta].Channel);

        Assert.True(LastIndexOfTier(sorted, count, 3) < firstDelta, "High keyframes are cheaper to lose than a delta");
        Assert.True(lastDelta < FirstIndexOfTier(sorted, count, 2), "a delta is cheaper to lose than a Medium keyframe");
    }

    [Fact]
    public void ChannelsStayGroupedAndNothingIsLost()
    {
        byte[] channels = BuildMixedChannels();
        PendingAvatarSend[] sorted = Sort(channels, out int count);

        var expected = new Dictionary<byte, int>();
        foreach (byte channel in channels)
        {
            expected.TryGetValue(channel, out int n);
            expected[channel] = n + 1;
        }

        var actual = new Dictionary<byte, int>();
        var closed = new HashSet<byte>();
        byte current = sorted[0].Channel;
        for (int i = 0; i < count; i++)
        {
            byte channel = sorted[i].Channel;
            actual.TryGetValue(channel, out int n);
            actual[channel] = n + 1;

            if (channel != current)
            {
                Assert.True(closed.Add(current), $"channel {current} appears in more than one run");
                current = channel;
            }
        }
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void OrderWithinAChannelIsPreserved()
    {
        byte[] channels = BuildMixedChannels();
        PendingAvatarSend[] sorted = Sort(channels, out int count);

        var lastSeen = new Dictionary<byte, int>();
        for (int i = 0; i < count; i++)
        {
            byte channel = sorted[i].Channel;
            int length = sorted[i].Length;
            if (lastSeen.TryGetValue(channel, out int previous))
            {
                Assert.True(previous < length, "the sort must stay stable so the sender rotation still decides order within a channel");
            }
            lastSeen[channel] = length;
        }
    }

    private static byte[] BuildMixedChannels()
    {
        var channels = new List<byte>();
        for (int repeat = 0; repeat < 3; repeat++)
        {
            for (int quality = 3; quality >= 0; quality--)
            {
                channels.Add(SmallIdChannels[quality]);
                channels.Add(LargeIdChannels[quality]);
                channels.Add((byte)(SmallIdChannels[quality] + 1));
                channels.Add((byte)(LargeIdChannels[quality] + 1));
            }
        }
        return channels.ToArray();
    }

    private static PendingAvatarSend[] Sort(byte[] channels, out int count)
    {
        count = channels.Length;
        var state = new PlayerState { PendingSends = new PendingAvatarSend[count] };
        for (int i = 0; i < count; i++)
        {
            state.PendingSends[i] = new PendingAvatarSend
            {
                Source = new byte[4],
                Length = i + 1,
                Channel = channels[i],
                IntervalOffset = 1,
            };
        }
        BasisServerReductionSystemEvents.TestOnly_SortPendingByChannel(state, state.PendingSends, count);
        return state.PendingSends;
    }

    private static bool IsTier(byte channel, int quality) =>
        channel == SmallIdChannels[quality] || channel == SmallIdChannels[quality] + 1
        || channel == LargeIdChannels[quality] || channel == LargeIdChannels[quality] + 1;

    private static int FirstIndexOfTier(PendingAvatarSend[] sorted, int count, int quality)
    {
        for (int i = 0; i < count; i++)
        {
            if (IsTier(sorted[i].Channel, quality)) return i;
        }
        return -1;
    }

    private static int LastIndexOfTier(PendingAvatarSend[] sorted, int count, int quality)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            if (IsTier(sorted[i].Channel, quality)) return i;
        }
        return -1;
    }

    private static int FirstIndexOf(PendingAvatarSend[] sorted, int count, byte channel)
    {
        for (int i = 0; i < count; i++)
        {
            if (sorted[i].Channel == channel) return i;
        }
        return -1;
    }
}
