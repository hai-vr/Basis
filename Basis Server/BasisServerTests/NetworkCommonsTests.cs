using System.Collections.Generic;
using Basis.Network.Core;
using Xunit;

namespace BasisServerTests
{
    public class BasisNetworkCommonsTests
    {
        [Fact]
        public void MaxConnections_IsUShortMax()
        {
            Assert.Equal(ushort.MaxValue, BasisNetworkCommons.MaxConnections);
        }

        [Fact]
        public void TotalChannels_Is27()
        {
            Assert.Equal(27, BasisNetworkCommons.TotalChannels);
        }

        [Fact]
        public void AllChannelConstants_AreUnique()
        {
            var channels = new HashSet<byte>
            {
                BasisNetworkCommons.FallChannel,
                BasisNetworkCommons.AuthIdentityChannel,
                BasisNetworkCommons.PlayerAvatarChannel,
                BasisNetworkCommons.VoiceChannel,
                BasisNetworkCommons.SceneChannel,
                BasisNetworkCommons.AvatarChannel,
                BasisNetworkCommons.CreateRemotePlayerChannel,
                BasisNetworkCommons.CreateRemotePlayersForNewPeerChannel,
                BasisNetworkCommons.AvatarChangeMessageChannel,
                BasisNetworkCommons.GetCurrentOwnerRequestChannel,
                BasisNetworkCommons.ChangeCurrentOwnerRequestChannel,
                BasisNetworkCommons.RemoveCurrentOwnerRequestChannel,
                BasisNetworkCommons.AudioRecipientsChannel,
                BasisNetworkCommons.DisconnectionChannel,
                BasisNetworkCommons.netIDAssignChannel,
                BasisNetworkCommons.NetIDAssignsChannel,
                BasisNetworkCommons.LoadResourceChannel,
                BasisNetworkCommons.UnloadResourceChannel,
                BasisNetworkCommons.AdminChannel,
                BasisNetworkCommons.ContentShareChannel,
                BasisNetworkCommons.ContentShareCleanupChannel,
                BasisNetworkCommons.ServerBoundChannel,
                BasisNetworkCommons.metaDataChannel,
                BasisNetworkCommons.StoreDatabaseChannel,
                BasisNetworkCommons.RequestStoreDatabaseChannel,
                BasisNetworkCommons.ServerStatisticsChannel,
                BasisNetworkCommons.ServerIsAdminChannel,
            };

            Assert.Equal(BasisNetworkCommons.TotalChannels, channels.Count);
        }

        [Fact]
        public void AllChannels_AreLessThanTotalChannels()
        {
            Assert.True(BasisNetworkCommons.FallChannel < BasisNetworkCommons.TotalChannels);
            Assert.True(BasisNetworkCommons.ServerIsAdminChannel < BasisNetworkCommons.TotalChannels);
        }

        [Fact]
        public void ChannelConstants_AreSequential()
        {
            // Channels should range from 0 to TotalChannels-1
            Assert.Equal(0, BasisNetworkCommons.FallChannel);
            Assert.Equal(26, BasisNetworkCommons.ServerIsAdminChannel);
        }
    }

    public class BasisNetworkVersionTests
    {
        [Fact]
        public void ServerVersion_IsPositive()
        {
            Assert.True(BasisNetworkVersion.ServerVersion > 0);
        }
    }

    public class DeliveryMethodTests
    {
        [Fact]
        public void DeliveryMethod_ValuesAreCorrect()
        {
            Assert.Equal((byte)4, (byte)DeliveryMethod.Unreliable);
            Assert.Equal((byte)0, (byte)DeliveryMethod.ReliableUnordered);
            Assert.Equal((byte)1, (byte)DeliveryMethod.Sequenced);
            Assert.Equal((byte)2, (byte)DeliveryMethod.ReliableOrdered);
            Assert.Equal((byte)3, (byte)DeliveryMethod.ReliableSequenced);
        }
    }
}
