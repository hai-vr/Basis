using Basis.Network.Core;
using BasisNetworkServer.BasisNetworking;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using System;
using System.Collections.Concurrent;

namespace BasisNetworkServer
{
    /// <summary>
    /// Server-side relay for jiggle grab lifecycle events on
    /// <see cref="BasisNetworkCommons.EventsChannel"/> with sub-byte
    /// <see cref="BasisNetworkCommons.EventType_JiggleGrab"/>.
    ///
    /// The server never inspects avatar data: it rewrites the payload with the
    /// authenticated sender id, rate limits per peer, and fans out. Start (which is
    /// also the periodic keepalive) is relevance-filtered against the reduction
    /// system's player positions so a thousand concurrent grabs don't broadcast
    /// instance-wide; Stop and Deny are rare and broadcast so state can never leak.
    /// </summary>
    public static class BasisNetworkHandleJiggleGrab
    {
        public const float RelevanceDistance = 64f;
        private const float RelevanceDistanceSquared = RelevanceDistance * RelevanceDistance;
        public const float TokensPerSecond = 8f;
        public const float TokenBurst = 16f;
        private const int MaxTrackedPeers = 4096;

        private class TokenBucket
        {
            public float Tokens = TokenBurst;
            public long LastRefillTicks = DateTime.UtcNow.Ticks;
        }

        private static readonly ConcurrentDictionary<int, TokenBucket> buckets = new ConcurrentDictionary<int, TokenBucket>();

        private static bool TryConsumeToken(NetPeer peer)
        {
            if (buckets.Count > MaxTrackedPeers)
            {
                buckets.Clear();
            }
            TokenBucket bucket = buckets.GetOrAdd(peer.Id, _ => new TokenBucket());
            lock (bucket)
            {
                long now = DateTime.UtcNow.Ticks;
                float elapsedSeconds = (now - bucket.LastRefillTicks) / (float)TimeSpan.TicksPerSecond;
                bucket.LastRefillTicks = now;
                bucket.Tokens = Math.Min(TokenBurst, bucket.Tokens + elapsedSeconds * TokensPerSecond);
                if (bucket.Tokens < 1f)
                {
                    return false;
                }
                bucket.Tokens -= 1f;
                return true;
            }
        }

        public static void HandleEvent(NetPacketReader reader, NetPeer peer, byte eventType)
        {
            byte op = reader.GetByte();
            switch (op)
            {
                case BasisNetworkCommons.JiggleGrabOp_Start:
                {
                    ushort targetId = reader.GetUShort();
                    byte rigIndex = reader.GetByte();
                    ushort pointIndex = reader.GetUShort();
                    byte hand = reader.GetByte();
                    uint boneNameHash = reader.GetUInt();
                    ushort offsetX = reader.GetUShort();
                    ushort offsetY = reader.GetUShort();
                    ushort offsetZ = reader.GetUShort();
                    reader.Recycle();

                    if (!TryConsumeToken(peer))
                    {
                        return;
                    }

                    NetDataWriter writer = NetworkServer.RentWriter();
                    writer.Put(eventType);
                    writer.Put(op);
                    writer.Put((ushort)peer.Id);
                    writer.Put(targetId);
                    writer.Put(rigIndex);
                    writer.Put(pointIndex);
                    writer.Put(hand);
                    writer.Put(boneNameHash);
                    writer.Put(offsetX);
                    writer.Put(offsetY);
                    writer.Put(offsetZ);
                    SendStartFiltered(writer, peer, targetId);
                    NetworkServer.ReturnWriter(writer);
                    break;
                }
                case BasisNetworkCommons.JiggleGrabOp_Stop:
                {
                    ushort targetId = reader.GetUShort();
                    byte rigIndex = reader.GetByte();
                    ushort pointIndex = reader.GetUShort();
                    reader.Recycle();

                    if (!TryConsumeToken(peer))
                    {
                        return;
                    }

                    NetDataWriter writer = NetworkServer.RentWriter();
                    writer.Put(eventType);
                    writer.Put(op);
                    writer.Put((ushort)peer.Id);
                    writer.Put(targetId);
                    writer.Put(rigIndex);
                    writer.Put(pointIndex);
                    NetworkServer.BroadcastMessageToClients(writer, BasisNetworkCommons.EventsChannel, peer, NetworkServer.PeerSnapshot, DeliveryMethod.ReliableOrdered);
                    NetworkServer.ReturnWriter(writer);
                    break;
                }
                case BasisNetworkCommons.JiggleGrabOp_Deny:
                {
                    ushort grabberId = reader.GetUShort();
                    reader.Recycle();

                    if (!TryConsumeToken(peer))
                    {
                        return;
                    }

                    NetDataWriter writer = NetworkServer.RentWriter();
                    writer.Put(eventType);
                    writer.Put(op);
                    writer.Put((ushort)peer.Id);
                    writer.Put(grabberId);
                    NetworkServer.BroadcastMessageToClients(writer, BasisNetworkCommons.EventsChannel, peer, NetworkServer.PeerSnapshot, DeliveryMethod.ReliableOrdered);
                    NetworkServer.ReturnWriter(writer);
                    break;
                }
                default:
                    reader.Recycle();
                    break;
            }
        }

        /// <summary>
        /// Sends a Start to every peer near the grab target (the reduction system's live
        /// positions), always including the target itself. Missing position state fails
        /// open to a full broadcast — correctness beats bandwidth here.
        /// </summary>
        private static void SendStartFiltered(NetDataWriter writer, NetPeer sender, ushort targetId)
        {
            NetPeer[] peers = NetworkServer.PeerSnapshot;
            if (peers == null)
            {
                return;
            }
            bool haveTargetState = BasisServerReductionSystemEvents.playerStates.TryGetValue(targetId, out PlayerState targetState)
                && targetState.IsActive;
            if (!haveTargetState)
            {
                NetworkServer.BroadcastMessageToClients(writer, BasisNetworkCommons.EventsChannel, sender, peers, DeliveryMethod.ReliableOrdered);
                return;
            }

            int count = peers.Length;
            for (int Index = 0; Index < count; Index++)
            {
                NetPeer recipient = peers[Index];
                if (recipient == null || recipient.Id == sender.Id)
                {
                    continue;
                }
                if ((ushort)recipient.Id != targetId
                    && BasisServerReductionSystemEvents.playerStates.TryGetValue(recipient.Id, out PlayerState recipientState)
                    && recipientState.IsActive
                    && DistanceSquared(recipientState.Position, targetState.Position) > RelevanceDistanceSquared)
                {
                    continue;
                }
                NetworkServer.TrySend(recipient, writer, BasisNetworkCommons.EventsChannel, DeliveryMethod.ReliableOrdered);
            }
        }

        private static float DistanceSquared(Basis.Scripts.Networking.Compression.Vector3 a, Basis.Scripts.Networking.Compression.Vector3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            float dz = a.z - b.z;
            return dx * dx + dy * dy + dz * dz;
        }
    }
}
