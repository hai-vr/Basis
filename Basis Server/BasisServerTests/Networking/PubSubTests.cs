using Basis.Network.Server.Messaging;
using Xunit;

namespace BasisServerTests;

public class PubSubTests
{
    private class TestDataProvider : IPubSubDataProvider
    {
        public byte[] InitialState;
        public List<byte[]> GetInitialState() => new() { InitialState };
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
}
