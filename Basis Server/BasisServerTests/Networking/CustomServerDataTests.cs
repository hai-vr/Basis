using Basis.Network.Core;
using BasisNetworkServer;
using Xunit;
using static SerializableBasis;

namespace BasisServerTests;

[Collection("BasisServer shared network statics")]
public class CustomServerDataTests
{
    private class TestDataPublisher : IBasisCustomServerDataPublisher
    {
        public byte[] InitialState;
        public List<byte[]> GetInitialState() => InitialState == null ? new List<byte[]>() : new List<byte[]> { InitialState };
    }

    [Fact]
    public void RegisterChannel_SucceedsAndCanReRegister()
    {
        TestDataPublisher publisher = new TestDataPublisher();
        string channelName = "test.channel.reg";
        
        // We can't easily check existence anymore via API, but we can verify registration doesn't throw
        BasisNetworkHandleCustomServerData.RegisterChannel(channelName, publisher);
        
        // Duplicate should still throw
        Assert.Throws<InvalidOperationException>(() => BasisNetworkHandleCustomServerData.RegisterChannel(channelName, publisher));
        
        BasisNetworkHandleCustomServerData.UnregisterChannel(channelName);
        
        // Should be able to register again after unregister
        BasisNetworkHandleCustomServerData.RegisterChannel(channelName, publisher);
        BasisNetworkHandleCustomServerData.UnregisterChannel(channelName);
    }

    [Fact]
    public void RegisterChannel_DuplicateName_ThrowsInvalidOperationException()
    {
        TestDataPublisher publisher = new TestDataPublisher();
        string channelName = "test.channel.dup";
        BasisNetworkHandleCustomServerData.RegisterChannel(channelName, publisher);
        
        Assert.Throws<InvalidOperationException>(() => BasisNetworkHandleCustomServerData.RegisterChannel(channelName, publisher));
        
        BasisNetworkHandleCustomServerData.UnregisterChannel(channelName);
    }

    [Fact]
    public void HandleSubscribeRequest_SendsInitialStateAndAddsSubscriber()
    {
        using var scope = new ServerStaticsScope();
        string channelName = "test.subscribe";
        byte[] initialState = new byte[] { 1, 2, 3 };
        var provider = new TestDataPublisher { InitialState = initialState };
        BasisNetworkHandleCustomServerData.RegisterChannel(channelName, provider);

        var peer = new FakeNetPeer(1, "127.0.0.1");
        var requestId = Guid.NewGuid();
        var request = new CustomServerDataSubscribeRequest
        {
            ChannelPattern = channelName,
            RequestID = requestId
        };

        BasisNetworkHandleCustomServerData.HandleSubscribeRequest(peer, request);

        // Verify ProvideChannelId sent first
        var provideIdMessage = peer.Sent.FirstOrDefault(s => s.Data[0] == BasisNetworkCommons.CustomServerData_ProvideChannelId);
        Assert.NotNull(provideIdMessage);
        var provideIdReader = new NetDataReader(provideIdMessage.Data);
        Assert.Equal(BasisNetworkCommons.CustomServerData_ProvideChannelId, provideIdReader.GetByte());
        var provideIdResponse = new CustomServerDataProvideChannelId();
        Assert.True(provideIdResponse.Deserialize(provideIdReader));
        Assert.Equal(channelName, provideIdResponse.ChannelName);
        Assert.Equal(requestId, provideIdResponse.RequestID);
        ushort assignedId = provideIdResponse.ChannelId;

        // Verify initial state sent
        var sentMessage = peer.Sent.FirstOrDefault(s => s.Data[0] == BasisNetworkCommons.CustomServerData_InitialState);
        Assert.NotNull(sentMessage);

        var reader = new NetDataReader(sentMessage.Data);
        Assert.Equal(BasisNetworkCommons.CustomServerData_InitialState, reader.GetByte());
        
        var response = new CustomServerDataInitialState();
        Assert.True(response.Deserialize(reader));
        Assert.Equal(assignedId, response.ChannelId);
        Assert.Equal(initialState, response.Data);
        Assert.Equal(requestId, response.RequestID);

        BasisNetworkHandleCustomServerData.UnregisterChannel(channelName);
    }

    [Fact]
    public void HandleUnsubscribeRequest_RemovesSpecificSubscription()
    {
        using var scope = new ServerStaticsScope();
        string channelName = "test.unsubscribe";
        var provider = new TestDataPublisher();
        BasisNetworkHandleCustomServerData.RegisterChannel(channelName, provider);

        var peer = new FakeNetPeer(1, "127.0.0.1");
        NetworkServer.AuthenticatedPeers[1] = peer;
        NetworkServer.RebuildPeerSnapshot();

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        BasisNetworkHandleCustomServerData.HandleSubscribeRequest(peer, new CustomServerDataSubscribeRequest { ChannelPattern = channelName, RequestID = id1 });
        BasisNetworkHandleCustomServerData.HandleSubscribeRequest(peer, new CustomServerDataSubscribeRequest { ChannelPattern = channelName, RequestID = id2 });

        // Both subscribed, verify publish reaches peer
        byte[] updateData = new byte[] { 42 };
        BasisNetworkHandleCustomServerData.Publish(channelName, updateData);
        
        // One update should be sent (peer is unique subscriber)
        Assert.Single(peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.CustomServerData_Message));
        peer.Sent.Clear();

        // Unsubscribe one
        BasisNetworkHandleCustomServerData.HandleUnsubscribeRequest(peer, new CustomServerDataUnsubscribeRequest { ChannelPattern = channelName, RequestID = id1 });

        // Still one subscription left, should still receive updates
        BasisNetworkHandleCustomServerData.Publish(channelName, updateData);
        Assert.Single(peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.CustomServerData_Message));
        peer.Sent.Clear();

        // Unsubscribe second
        BasisNetworkHandleCustomServerData.HandleUnsubscribeRequest(peer, new CustomServerDataUnsubscribeRequest { ChannelPattern = channelName, RequestID = id2 });

        // No more subscriptions, should not receive updates
        BasisNetworkHandleCustomServerData.Publish(channelName, updateData);
        Assert.Empty(peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.CustomServerData_Message));

        BasisNetworkHandleCustomServerData.UnregisterChannel(channelName);
    }

    [Fact]
    public void MqttMatch_MultiLevelWildcard()
    {
        using var scope = new ServerStaticsScope();
        var provider = new TestDataPublisher();
        BasisNetworkHandleCustomServerData.RegisterChannel("/a/b/c", provider);
        BasisNetworkHandleCustomServerData.RegisterChannel("/a/d/e", provider);
        BasisNetworkHandleCustomServerData.RegisterChannel("/other", provider);

        var peer = new FakeNetPeer(1, "127.0.0.1");
        NetworkServer.AuthenticatedPeers[1] = peer;
        NetworkServer.RebuildPeerSnapshot();

        var requestId = Guid.NewGuid();
        BasisNetworkHandleCustomServerData.HandleSubscribeRequest(peer, new CustomServerDataSubscribeRequest { ChannelPattern = "/a/#", RequestID = requestId });

        // Should match /a/b/c and /a/d/e
        byte[] data = new byte[] { 1 };
        BasisNetworkHandleCustomServerData.Publish("/a/b/c", data);
        BasisNetworkHandleCustomServerData.Publish("/a/d/e", data);
        BasisNetworkHandleCustomServerData.Publish("/other", data);

        var messages = peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.CustomServerData_Message).ToList();
        Assert.Equal(2, messages.Count);

        // Unsubscribe using pattern
        BasisNetworkHandleCustomServerData.HandleUnsubscribeRequest(peer, new CustomServerDataUnsubscribeRequest { ChannelPattern = "/a/#", RequestID = requestId });
        peer.Sent.Clear();

        BasisNetworkHandleCustomServerData.Publish("/a/b/c", data);
        Assert.Empty(peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.CustomServerData_Message));

        BasisNetworkHandleCustomServerData.UnregisterChannel("/a/b/c");
        BasisNetworkHandleCustomServerData.UnregisterChannel("/a/d/e");
        BasisNetworkHandleCustomServerData.UnregisterChannel("/other");
    }

    [Fact]
    public void MqttMatch_SingleLevelWildcard()
    {
        using var scope = new ServerStaticsScope();
        var provider = new TestDataPublisher();
        BasisNetworkHandleCustomServerData.RegisterChannel("/a/b/c", provider);
        BasisNetworkHandleCustomServerData.RegisterChannel("/a/d/c", provider);
        BasisNetworkHandleCustomServerData.RegisterChannel("/a/b/d", provider);

        var peer = new FakeNetPeer(1, "127.0.0.1");
        NetworkServer.AuthenticatedPeers[1] = peer;
        NetworkServer.RebuildPeerSnapshot();

        var requestId = Guid.NewGuid();
        BasisNetworkHandleCustomServerData.HandleSubscribeRequest(peer, new CustomServerDataSubscribeRequest { ChannelPattern = "/a/+/c", RequestID = requestId });

        byte[] data = new byte[] { 1 };
        BasisNetworkHandleCustomServerData.Publish("/a/b/c", data);
        BasisNetworkHandleCustomServerData.Publish("/a/d/c", data);
        BasisNetworkHandleCustomServerData.Publish("/a/b/d", data);

        var messages = peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.CustomServerData_Message).ToList();
        Assert.Equal(2, messages.Count);

        BasisNetworkHandleCustomServerData.UnregisterChannel("/a/b/c");
        BasisNetworkHandleCustomServerData.UnregisterChannel("/a/d/c");
        BasisNetworkHandleCustomServerData.UnregisterChannel("/a/b/d");
    }

    [Fact]
    public void MqttMatch_WildcardWithinSegment()
    {
        using var scope = new ServerStaticsScope();
        var provider = new TestDataPublisher();
        BasisNetworkHandleCustomServerData.RegisterChannel("/a/foo-bar/c", provider);
        BasisNetworkHandleCustomServerData.RegisterChannel("/a/baz-bar/c", provider);
        BasisNetworkHandleCustomServerData.RegisterChannel("/a/foo-qux/c", provider);

        var peer = new FakeNetPeer(1, "127.0.0.1");
        NetworkServer.AuthenticatedPeers[1] = peer;
        NetworkServer.RebuildPeerSnapshot();

        var requestId = Guid.NewGuid();
        BasisNetworkHandleCustomServerData.HandleSubscribeRequest(peer, new CustomServerDataSubscribeRequest { ChannelPattern = "/a/*-bar/c", RequestID = requestId });

        byte[] data = new byte[] { 1 };
        BasisNetworkHandleCustomServerData.Publish("/a/foo-bar/c", data);
        BasisNetworkHandleCustomServerData.Publish("/a/baz-bar/c", data);
        BasisNetworkHandleCustomServerData.Publish("/a/foo-qux/c", data);

        var messages = peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.CustomServerData_Message).ToList();
        Assert.Equal(2, messages.Count);

        BasisNetworkHandleCustomServerData.UnregisterChannel("/a/foo-bar/c");
        BasisNetworkHandleCustomServerData.UnregisterChannel("/a/baz-bar/c");
        BasisNetworkHandleCustomServerData.UnregisterChannel("/a/foo-qux/c");
    }

    [Fact]
    public void Unsubscribe_RequiresOriginalPattern()
    {
        using var scope = new ServerStaticsScope();
        string channelName = "/a/b/c";
        var provider = new TestDataPublisher();
        BasisNetworkHandleCustomServerData.RegisterChannel(channelName, provider);

        var peer = new FakeNetPeer(1, "127.0.0.1");
        NetworkServer.AuthenticatedPeers[1] = peer;
        NetworkServer.RebuildPeerSnapshot();

        var requestId = Guid.NewGuid();
        BasisNetworkHandleCustomServerData.HandleSubscribeRequest(peer, new CustomServerDataSubscribeRequest { ChannelPattern = "/a/#", RequestID = requestId });

        // Try to unsubscribe from the individual channel instead of the pattern
        BasisNetworkHandleCustomServerData.HandleUnsubscribeRequest(peer, new CustomServerDataUnsubscribeRequest { ChannelPattern = channelName, RequestID = requestId });

        // Should still be subscribed because pattern didn't match
        byte[] data = new byte[] { 1 };
        BasisNetworkHandleCustomServerData.Publish(channelName, data);
        Assert.Single(peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.CustomServerData_Message));

        BasisNetworkHandleCustomServerData.UnregisterChannel(channelName);
    }

    [Fact]
    public void RemovePlayerSubscriptions_CleansUpAllChannels()
    {
        using var scope = new ServerStaticsScope();
        string chan1 = "test.cleanup.1";
        string chan2 = "test.cleanup.2";
        BasisNetworkHandleCustomServerData.RegisterChannel(chan1, new TestDataPublisher());
        BasisNetworkHandleCustomServerData.RegisterChannel(chan2, new TestDataPublisher());

        var peer = new FakeNetPeer(1, "127.0.0.1");
        NetworkServer.AuthenticatedPeers[1] = peer;
        NetworkServer.RebuildPeerSnapshot();

        BasisNetworkHandleCustomServerData.HandleSubscribeRequest(peer, new CustomServerDataSubscribeRequest { ChannelPattern = chan1, RequestID = Guid.NewGuid() });
        BasisNetworkHandleCustomServerData.HandleSubscribeRequest(peer, new CustomServerDataSubscribeRequest { ChannelPattern = chan2, RequestID = Guid.NewGuid() });

        BasisNetworkHandleCustomServerData.RemovePlayerSubscriptions(peer.Id);

        byte[] updateData = new byte[] { 0 };
        BasisNetworkHandleCustomServerData.Publish(chan1, updateData);
        BasisNetworkHandleCustomServerData.Publish(chan2, updateData);

        Assert.Empty(peer.Sent.Where(s => s.Data[0] == BasisNetworkCommons.CustomServerData_Message));

        BasisNetworkHandleCustomServerData.UnregisterChannel(chan1);
        BasisNetworkHandleCustomServerData.UnregisterChannel(chan2);
    }
}
