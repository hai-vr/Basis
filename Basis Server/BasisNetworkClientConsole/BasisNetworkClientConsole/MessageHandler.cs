using Basis.Network.Core;
using Basis.Network.Core.Compression;
using BasisNetworkClient;
using System.Collections.Concurrent;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;
using static SerializableBasis;

namespace Basis.Network
{
    public static class MessageHandler
    {
        // Baseline storage per player (keyed by playerID)
        private static readonly ConcurrentDictionary<ushort, byte[]> AvatarBaselines = new();
        private static readonly ConcurrentDictionary<ushort, byte> BaselineQuality = new();

        public static void OnDisconnect(NetPeer peer, DisconnectInfo info)
        {
            BNL.LogError($"Peer {peer.Id} disconnected.");
        }

        public static void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            if (peer.Id != 0) return;

            switch (channel)
            {
                case BasisNetworkCommons.AuthIdentityChannel:
                    AuthIdentityMessage(peer, reader, channel);
                    return; // already recycled inside
                case BasisNetworkCommons.metaDataChannel:
                    var message = new ServerMetaDataMessage();
                    message.Deserialize(reader);
                    break;
                case BasisNetworkCommons.PlayerAvatarChannel:
                    HandleAvatarKeyframe(reader);
                    break;
                case BasisNetworkCommons.DeltaPlayerAvatarChannel:
                    HandleAvatarDelta(reader);
                    break;
                case BasisNetworkCommons.DisconnectionChannel:
                    if (reader.AvailableBytes >= 2)
                    {
                        ushort disconnectedId = reader.GetUShort();
                        AvatarBaselines.TryRemove(disconnectedId, out _);
                        BaselineQuality.TryRemove(disconnectedId, out _);
                    }
                    break;
                default:
                    // Silently consume other channels
                    break;
            }

            reader.Recycle();
        }

        private static void HandleAvatarKeyframe(NetPacketReader reader)
        {
            if (reader.AvailableBytes < 4) return; // need at least playerID(2) + interval(1) + quality(1)

            ushort playerId = reader.GetUShort();
            byte interval = reader.GetByte();
            byte qualityByte = reader.GetByte();

            var quality = (BitQuality)qualityByte;
            if (!BasisAvatarBitPacking.IsValidQuality(quality)) return;

            int expectedSize = BasisAvatarBitPacking.ConvertToSize(quality);
            if (reader.AvailableBytes < expectedSize) return;

            // Read the payload and store as baseline
            if (!AvatarBaselines.TryGetValue(playerId, out byte[] baseline) || baseline == null || baseline.Length != expectedSize)
            {
                baseline = new byte[expectedSize];
                AvatarBaselines[playerId] = baseline;
            }
            reader.GetBytes(baseline, expectedSize);
            BaselineQuality[playerId] = qualityByte;

            // Skip remaining additional avatar data (just consume it)
        }

        private static void HandleAvatarDelta(NetPacketReader reader)
        {
            if (reader.AvailableBytes < 4) return;

            ushort playerId = reader.GetUShort();
            byte interval = reader.GetByte();
            byte qualityByte = reader.GetByte();

            var quality = (BitQuality)qualityByte;
            if (!BasisAvatarBitPacking.IsValidQuality(quality)) return;

            // Must have a matching baseline
            if (!AvatarBaselines.TryGetValue(playerId, out byte[] baseline) || baseline == null) return;
            if (!BaselineQuality.TryGetValue(playerId, out byte baselineQ) || baselineQ != qualityByte) return;

            int expectedSize = BasisAvatarBitPacking.ConvertToSize(quality);
            if (baseline.Length != expectedSize) return;

            // Decode XOR delta from reader (consumes bitmask + changed chunks)
            byte[] reconstructed = new byte[expectedSize];
            AvatarDeltaCompression.DecodeDelta(reader, baseline, reconstructed);

            // Skip remaining additional avatar data (just consume it)
        }

        public static void AuthIdentityMessage(NetPeer peer, NetPacketReader Reader, byte channel)
        {
            BNL.Log("Validated Size " + Reader.AvailableBytes);
            if (BasisDIDAuthIdentityClient.IdentityMessage(peer, Reader, out NetDataWriter Writer))
            {
                BNL.Log("Sent Identity To Server!");
                peer.Send(Writer, BasisNetworkCommons.AuthIdentityChannel, DeliveryMethod.ReliableOrdered);
                Reader.Recycle();
            }
            else
            {
                BNL.LogError("Failed Identity Message!");
                Reader.Recycle();
            }
            BNL.Log("Completed");
        }
    }
}
