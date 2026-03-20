using Basis.Network.Core;
using BasisNetworkServer.BasisNetworking;
using static SerializableBasis;

namespace BasisNetworkServer
{
    /// <summary>
    /// Routes incoming messages on <see cref="BasisNetworkCommons.EventsChannel"/>.
    /// The first byte is the event type; the rest is event-specific payload.
    /// </summary>
    public static class BasisNetworkEvents
    {
        public static void HandleEvent(NetPacketReader reader, NetPeer peer)
        {
            byte eventType = reader.GetByte();

            switch (eventType)
            {
                case BasisNetworkCommons.EventType_CameraShutterSound:
                    HandleCameraShutterSound(reader, peer, eventType);
                    break;

                default:
                    BNL.LogError($"Unknown EventsChannel event type: {eventType}");
                    reader.Recycle();
                    break;
            }
        }

        private static void HandleCameraShutterSound(NetPacketReader reader, NetPeer peer, byte eventType)
        {
            ClientCameraShutterSoundMessage clientMsg = new ClientCameraShutterSoundMessage();
            clientMsg.Deserialize(reader);
            reader.Recycle();

            ushort peerId = (ushort)peer.Id;

            NetDataWriter writer = NetworkServer.RentWriter();
            writer.Put(eventType);

            CameraShutterSoundMessage outMsg = new CameraShutterSoundMessage
            {
                PlayerID = peerId,
                PositionX = clientMsg.PositionX,
                PositionY = clientMsg.PositionY,
                PositionZ = clientMsg.PositionZ,
            };
            outMsg.Serialize(writer);

            NetworkServer.BroadcastMessageToClients(writer, BasisNetworkCommons.EventsChannel, peer, NetworkServer.PeerSnapshot, DeliveryMethod.Sequenced);
            NetworkServer.ReturnWriter(writer);
        }
    }
}
