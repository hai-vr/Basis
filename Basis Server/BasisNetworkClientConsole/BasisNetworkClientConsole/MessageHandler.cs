using Basis.Network.Core;
using static SerializableBasis;

namespace Basis.Network
{
    public static class MessageHandler
    {
        public static void OnDisconnect(NetPeer peer, DisconnectInfo info)
        {
            BNL.LogError($"Peer {peer.Id} disconnected.");
        }

        public static void OnReceive(ConsoleClientIdentity identity, NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            if (peer.Id != 0) return;

            switch (channel)
            {
                case BasisNetworkCommons.AuthIdentityChannel:
                    AuthIdentityMessage(identity, peer, reader);
                    return; // already recycled inside
                case BasisNetworkCommons.metaDataChannel:
                    if (identity != null)
                    {
                        identity.Authenticated = true;
                    }
                    break;
                case BasisNetworkCommons.PlayerAvatarVeryLowChannel:
                case BasisNetworkCommons.PlayerAvatarVeryLowAdditionalChannel:
                case BasisNetworkCommons.PlayerAvatarLowChannel:
                case BasisNetworkCommons.PlayerAvatarLowAdditionalChannel:
                case BasisNetworkCommons.PlayerAvatarMediumChannel:
                case BasisNetworkCommons.PlayerAvatarMediumAdditionalChannel:
                case BasisNetworkCommons.PlayerAvatarHighChannel:
                case BasisNetworkCommons.PlayerAvatarHighAdditionalChannel:
                    // Full avatar update — just consume it
                    break;
                case BasisNetworkCommons.DisconnectionChannel:
                    // Just consume disconnection messages
                    break;
                default:
                    // Silently consume other channels
                    break;
            }

            reader.Recycle();
        }

        public static void AuthIdentityMessage(ConsoleClientIdentity identity, NetPeer peer, NetPacketReader reader)
        {
            if (identity != null && identity.TryRespondToChallenge(reader, out NetDataWriter writer))
            {
                peer.Send(writer, BasisNetworkCommons.AuthIdentityChannel, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                BNL.LogError("Failed to respond to auth challenge!");
            }
            reader.Recycle();
        }
    }
}
