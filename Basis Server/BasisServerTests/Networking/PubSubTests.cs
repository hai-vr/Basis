using Basis.Network.Core;
using BasisNetworkServer;
using Xunit;
using static SerializableBasis;

namespace BasisServerTests;

[Collection("BasisServer shared network statics")]
public class PubSubTests
{
    private class TestDataProvider : IPubSubDataProvider
    {
        public byte[] InitialState;
        public List<byte[]> GetInitialState() => InitialState == null ? new List<byte[]>() : new List<byte[]> { InitialState };
    }

    [Fact]
    public void RegisterChannel_SucceedsAndCanReRegister()
    {
        TestDataProvider provider = new TestDataProvider();
        string channelName = "test.channel.reg";
        
        // We can't easily check existence anymore via API, but we can verify registration doesn't throw
        BasisNetworkHandlePubSub.RegisterChannel(channelName, provider);
        
        // Duplicate should still throw
        Assert.Throws<InvalidOperationException>(() => BasisNetworkHandlePubSub.RegisterChannel(channelName, provider));
        
        BasisNetworkHandlePubSub.UnregisterChannel(channelName);
        
        // Should be able to register again after unregister
        BasisNetworkHandlePubSub.RegisterChannel(channelName, provider);
        BasisNetworkHandlePubSub.UnregisterChannel(channelName);
    }

    [Fact]
    public void RegisterChannel_DuplicateName_ThrowsInvalidOperationException()
    {
        TestDataProvider provider = new TestDataProvider();
        string channelName = "test.channel.dup";
        BasisNetworkHandlePubSub.RegisterChannel(channelName, provider);
        
        Assert.Throws<InvalidOperationException>(() => BasisNetworkHandlePubSub.RegisterChannel(channelName, provider));
        
        BasisNetworkHandlePubSub.UnregisterChannel(channelName);
    }

    [Fact]
    public void HandleSubscribeRequest_SendsInitialStateAndAddsSubscriber()
    {
        using var scope = new ServerStaticsScope();
        string channelName = "test.subscribe";
        byte[] initialState = new byte[] { 1, 2, 3 };
        var provider = new TestDataProvider { InitialState = initialState };
        BasisNetworkHandlePubSub.RegisterChannel(channelName, provider);

        var peer = new FakeNetPeer(1, "127.0.0.1");
        var requestId = Guid.NewGuid();
        var request = new PubSubSubscribeRequest
        {
            ChannelName = channelName,
            RequestID = requestId
        };

        BasisNetworkHandlePubSub.HandleSubscribeRequest(peer, request);

        // Verify initial state sent
        var sentMessage = peer.Sent.FirstOrDefault(s => s.Channel == BasisNetworkCommons.PubSubChannel);
        Assert.NotNull(sentMessage);

        var reader = new NetDataReader(sentMessage.Data);
        Assert.Equal(BasisNetworkCommons.PubSub_Initial, reader.GetByte());
        
        var response = new PubSubInitialState();
        Assert.True(response.Deserialize(reader));
        Assert.Equal(channelName, response.ChannelName);
        Assert.Equal(initialState, response.Data);
        Assert.Equal(requestId, response.RequestID);

        BasisNetworkHandlePubSub.UnregisterChannel(channelName);
    }

    [Fact]
    public void HandleUnsubscribeRequest_RemovesSpecificSubscription()
    {
        using var scope = new ServerStaticsScope();
        string channelName = "test.unsubscribe";
        var provider = new TestDataProvider();
        BasisNetworkHandlePubSub.RegisterChannel(channelName, provider);

        var peer = new FakeNetPeer(1, "127.0.0.1");
        NetworkServer.AuthenticatedPeers[1] = peer;
        NetworkServer.RebuildPeerSnapshot();

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        BasisNetworkHandlePubSub.HandleSubscribeRequest(peer, new PubSubSubscribeRequest { ChannelName = channelName, RequestID = id1 });
        BasisNetworkHandlePubSub.HandleSubscribeRequest(peer, new PubSubSubscribeRequest { ChannelName = channelName, RequestID = id2 });

        // Both subscribed, verify publish reaches peer
        byte[] updateData = new byte[] { 42 };
        BasisNetworkHandlePubSub.Publish(channelName, updateData);
        
        // One update should be sent (peer is unique subscriber)
        Assert.Single(peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.PubSub_Message));
        peer.Sent.Clear();

        // Unsubscribe one
        BasisNetworkHandlePubSub.HandleUnsubscribeRequest(peer, new PubSubUnsubscribeRequest { ChannelName = channelName, RequestID = id1 });

        // Still one subscription left, should still receive updates
        BasisNetworkHandlePubSub.Publish(channelName, updateData);
        Assert.Single(peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.PubSub_Message));
        peer.Sent.Clear();

        // Unsubscribe second
        BasisNetworkHandlePubSub.HandleUnsubscribeRequest(peer, new PubSubUnsubscribeRequest { ChannelName = channelName, RequestID = id2 });

        // No more subscriptions, should not receive updates
        BasisNetworkHandlePubSub.Publish(channelName, updateData);
        Assert.Empty(peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.PubSub_Message));

        BasisNetworkHandlePubSub.UnregisterChannel(channelName);
    }

    [Fact]
    public void RemovePlayerSubscriptions_CleansUpAllChannels()
    {
        using var scope = new ServerStaticsScope();
        string chan1 = "test.cleanup.1";
        string chan2 = "test.cleanup.2";
        BasisNetworkHandlePubSub.RegisterChannel(chan1, new TestDataProvider());
        BasisNetworkHandlePubSub.RegisterChannel(chan2, new TestDataProvider());

        var peer = new FakeNetPeer(1, "127.0.0.1");
        NetworkServer.AuthenticatedPeers[1] = peer;
        NetworkServer.RebuildPeerSnapshot();

        BasisNetworkHandlePubSub.HandleSubscribeRequest(peer, new PubSubSubscribeRequest { ChannelName = chan1, RequestID = Guid.NewGuid() });
        BasisNetworkHandlePubSub.HandleSubscribeRequest(peer, new PubSubSubscribeRequest { ChannelName = chan2, RequestID = Guid.NewGuid() });

        BasisNetworkHandlePubSub.RemovePlayerSubscriptions(peer.Id);

        byte[] updateData = new byte[] { 0 };
        BasisNetworkHandlePubSub.Publish(chan1, updateData);
        BasisNetworkHandlePubSub.Publish(chan2, updateData);

        Assert.Empty(peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.PubSub_Message));

        BasisNetworkHandlePubSub.UnregisterChannel(chan1);
        BasisNetworkHandlePubSub.UnregisterChannel(chan2);
    }
}
