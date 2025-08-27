using System;
using Basis.Scripts.Behaviour;
using LiteNetLib;

namespace HVR.Basis.Comms
{
    // Enforces the following:
    // - Only the wearer can send a ready signal to the remotes.
    // - Only remotes can send an initialization request to the wearer.
    // - Only the wearer can send data to remotes.
    // - Remotes cannot send messages to each other.
    internal class AvatarMessageProcessing
    {
        private readonly BasisAvatarMonoBehaviour _transmitter;
        private readonly bool _isWearer;
        private readonly ushort _wearerNetId;
        private readonly ResyncEveryoneRequestedDelegate _onResyncEveryoneRequested;
        private readonly ResyncRequestedDelegate _onResyncRequested;
        private readonly PacketReceivedDelegate _onPacketReceived;

        public delegate void ResyncEveryoneRequestedDelegate();
        public delegate void ResyncRequestedDelegate(ushort remoteUser);
        public delegate void PacketReceivedDelegate(ArraySegment<byte> subBuffer);


        public static AvatarMessageProcessing ForFeature(BasisAvatarMonoBehaviour transmitter, bool isWearer, ushort wearerNetId, IFeatureReceiver receiver)
        {
            return new AvatarMessageProcessing(transmitter, isWearer, wearerNetId, receiver.OnResyncEveryoneRequested, remoteUser => receiver.OnResyncRequested(new[] { remoteUser }), receiver.OnPacketReceived);
        }

        public AvatarMessageProcessing(BasisAvatarMonoBehaviour transmitter, bool isWearer, ushort wearerNetId, ResyncEveryoneRequestedDelegate onResyncEveryoneRequested, ResyncRequestedDelegate onResyncRequested, PacketReceivedDelegate onPacketReceived)
        {
            _transmitter = transmitter;
            _isWearer = isWearer;
            _wearerNetId = wearerNetId;
            _onResyncEveryoneRequested = onResyncEveryoneRequested;
            _onResyncRequested = onResyncRequested;
            _onPacketReceived = onPacketReceived;
        }

        public void OnNetworkMessageReceived(ushort remoteUser, byte[] buffer, DeliveryMethod _, bool isADifferentAvatarLocally)
        {
            HVRAvatarComms.ProtocolDebug($"Receiving message AvatarMessageProcessing (buffer length: {buffer.Length}, byte0: {(buffer.Length > 0 ? buffer[0] : 0)})");
            if (isADifferentAvatarLocally) return;
            if (buffer.Length == 0) { HVRAvatarComms.ProtocolError("Buffer was 0 bytes."); return; }
            if (!_isWearer && remoteUser != _wearerNetId) { HVRAvatarComms.ProtocolError("Illegal sender."); return; }

            var packetId = buffer[0];
            switch (packetId)
            {
                case FeatureNetworking.NewNet_WearerReady:
                {
                    if (_isWearer) { HVRAvatarComms.ProtocolError("Illegal recipient."); return; }
                    if (remoteUser != _wearerNetId) { HVRAvatarComms.ProtocolError("Illegal sender."); return; }
                    if (buffer.Length != 1) { HVRAvatarComms.ProtocolError("Illegal buffer length."); return; }
                    HVRAvatarComms.ProtocolDebug("Identified AvatarMessageProcessing is acceptable NewNet_WearerReady");
                    // Do nothing
                    break;
                }
                case FeatureNetworking.NewNet_RemoteRequestsInitialization:
                {
                    if (!_isWearer) { HVRAvatarComms.ProtocolError("Illegal recipient."); return; }
                    if (remoteUser == _wearerNetId) { HVRAvatarComms.ProtocolError("Illegal sender."); return; }
                    if (buffer.Length != 1) { HVRAvatarComms.ProtocolError("Illegal buffer length."); return; }
                    HVRAvatarComms.ProtocolDebug("Identified AvatarMessageProcessing is acceptable NewNet_RemoteRequestsInitialization");
                    _onResyncRequested.Invoke(remoteUser);
                    break;
                }
                case FeatureNetworking.NewNet_WearerData:
                {
                    if (_isWearer) { HVRAvatarComms.ProtocolError("Illegal recipient."); return; }
                    if (remoteUser != _wearerNetId) { HVRAvatarComms.ProtocolError("Illegal sender."); return; }
                    HVRAvatarComms.ProtocolDebug("Identified AvatarMessageProcessing is acceptable NewNet_WearerData");
                    _onPacketReceived.Invoke(HVRAvatarComms.SubBuffer(buffer));
                    break;
                }
                default:
                {
                    HVRAvatarComms.ProtocolError("Illegal message.");
                    break;
                }
            }
        }

        public void SendInitialPacket()
        {
            if (_isWearer)
            {
                HVRAvatarComms.ProtocolDebug("Sending AvatarMessageProcessing NewNet_WearerReady message (NetworkMessageSend)");
                _transmitter.NetworkMessageSend(new[] { FeatureNetworking.NewNet_WearerReady });
                _onResyncEveryoneRequested.Invoke();
            }
            else
            {

                HVRAvatarComms.ProtocolDebug("Sending AvatarMessageProcessing NewNet_RemoteRequestsInitialization message (NetworkMessageSend)");
                _transmitter.NetworkMessageSend(new[] { FeatureNetworking.NewNet_RemoteRequestsInitialization });
            }
        }
    }
}
