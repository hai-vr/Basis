using Basis.Network.Core;
using BasisNetworkServer.BasisNetworking;
using static SerializableBasis;

namespace BasisNetworkServer
{
    public static class BasisNetworkCameraShutterSound
    {
        /// <summary>
        /// Client says they took a photo. Broadcast the shutter sound event to all other peers.
        /// </summary>
        public static void HandleShutterSound(NetPacketReader reader, NetPeer peer)
        {
            ClientCameraShutterSoundMessage clientMsg = new ClientCameraShutterSoundMessage();
            clientMsg.Deserialize(reader);
            reader.Recycle();

            ushort peerId = (ushort)peer.Id;

            CameraShutterSoundMessage outMsg = new CameraShutterSoundMessage
            {
                PlayerID = peerId,
                PositionX = clientMsg.PositionX,
                PositionY = clientMsg.PositionY,
                PositionZ = clientMsg.PositionZ,
            };

            NetDataWriter writer = NetworkServer.RentWriter();
            outMsg.Serialize(writer);
            NetworkServer.BroadcastMessageToClients(writer, BasisNetworkCommons.CameraShutterSoundChannel, peer, NetworkServer.PeerSnapshot, DeliveryMethod.Sequenced);
            NetworkServer.ReturnWriter(writer);
        }
    }
}
