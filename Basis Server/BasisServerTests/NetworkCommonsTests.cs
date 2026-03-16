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
        public void TotalChannels_Is38()
        {
            Assert.Equal(38, BasisNetworkCommons.TotalChannels);
        }

        [Fact]
        public void AllChannelConstants_AreUnique()
        {
            var channels = new HashSet<byte>
            {
                BasisNetworkCommons.DeprecatedChannel,
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
                BasisNetworkCommons.ChatChannel,
                BasisNetworkCommons.CameraPIPStateChannel,
                BasisNetworkCommons.CameraPIPPositionChannel,
                BasisNetworkCommons.PlayerAvatarVeryLowChannel,
                BasisNetworkCommons.PlayerAvatarVeryLowAdditionalChannel,
                BasisNetworkCommons.PlayerAvatarLowChannel,
                BasisNetworkCommons.PlayerAvatarLowAdditionalChannel,
                BasisNetworkCommons.PlayerAvatarMediumChannel,
                BasisNetworkCommons.PlayerAvatarMediumAdditionalChannel,
                BasisNetworkCommons.PlayerAvatarHighChannel,
                BasisNetworkCommons.PlayerAvatarHighAdditionalChannel,
            };

            Assert.Equal(BasisNetworkCommons.TotalChannels, channels.Count);
        }

        [Fact]
        public void AllChannels_AreLessThanTotalChannels()
        {
            Assert.True(BasisNetworkCommons.DeprecatedChannel < BasisNetworkCommons.TotalChannels);
            Assert.True(BasisNetworkCommons.PlayerAvatarHighAdditionalChannel < BasisNetworkCommons.TotalChannels);
        }

        [Fact]
        public void ChannelConstants_AreSequential()
        {
            // Channels should range from 0 to TotalChannels-1
            Assert.Equal(0, BasisNetworkCommons.DeprecatedChannel);
            Assert.Equal(37, BasisNetworkCommons.PlayerAvatarHighAdditionalChannel);
        }

        [Fact]
        public void GetPlayerAvatarChannelForQuality_MapsCorrectly()
        {
            Assert.Equal(BasisNetworkCommons.PlayerAvatarVeryLowChannel, BasisNetworkCommons.GetPlayerAvatarChannelForQuality(0, false));
            Assert.Equal(BasisNetworkCommons.PlayerAvatarVeryLowAdditionalChannel, BasisNetworkCommons.GetPlayerAvatarChannelForQuality(0, true));
            Assert.Equal(BasisNetworkCommons.PlayerAvatarLowChannel, BasisNetworkCommons.GetPlayerAvatarChannelForQuality(1, false));
            Assert.Equal(BasisNetworkCommons.PlayerAvatarLowAdditionalChannel, BasisNetworkCommons.GetPlayerAvatarChannelForQuality(1, true));
            Assert.Equal(BasisNetworkCommons.PlayerAvatarMediumChannel, BasisNetworkCommons.GetPlayerAvatarChannelForQuality(2, false));
            Assert.Equal(BasisNetworkCommons.PlayerAvatarMediumAdditionalChannel, BasisNetworkCommons.GetPlayerAvatarChannelForQuality(2, true));
            Assert.Equal(BasisNetworkCommons.PlayerAvatarHighChannel, BasisNetworkCommons.GetPlayerAvatarChannelForQuality(3, false));
            Assert.Equal(BasisNetworkCommons.PlayerAvatarHighAdditionalChannel, BasisNetworkCommons.GetPlayerAvatarChannelForQuality(3, true));
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
