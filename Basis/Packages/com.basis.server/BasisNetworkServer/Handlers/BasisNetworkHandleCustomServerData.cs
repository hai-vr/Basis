using Basis.Network.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BasisNetworkServer
{
    public interface IBasisCustomServerDataPublisher
    {
        /// <summary>
        /// Generates an initial state message for a new subscriber.
        /// If it returns an empty list, no initial state will be sent.
        /// </summary>
        List<byte[]> GetInitialState();
    }

    /// <summary>
    /// Provides a custom server data PubSub service typically for use by props, so that they may arbitrary live information from modified servers.
    /// </summary>
    public static class BasisNetworkHandleCustomServerData
    {
        private sealed class ChannelState
        {
            public readonly ushort Id;
            public readonly string Name;
            public readonly IBasisCustomServerDataPublisher Publisher;
            
            public readonly ConcurrentDictionary<int, HashSet<Guid>> PeerToSubscriptionsDict = new();
            public readonly object Lock = new object();

            public ChannelState(ushort id, string name, IBasisCustomServerDataPublisher publisher)
            {
                Id = id;
                Name = name;
                Publisher = publisher;
            }
        }

        private static readonly ConcurrentDictionary<string, ChannelState> Channels = new();
        private static int _nextChannelId = 1;
        
        public static void HandleEvent(NetPeer peer, NetPacketReader reader)
        {
            if (!reader.TryGetByte(out byte sub)) { reader.Recycle(); return; }

            if (sub == BasisNetworkCommons.CustomServerData_Subscribe)
            {
                var req = new SerializableBasis.CustomServerDataSubscribeRequest();
                if (req.Deserialize(reader))
                    HandleSubscribeRequest(peer, req);
            }
            else if (sub == BasisNetworkCommons.CustomServerData_Unsubscribe)
            {
                var req = new SerializableBasis.CustomServerDataUnsubscribeRequest();
                if (req.Deserialize(reader))
                    HandleUnsubscribeRequest(peer, req);
            }
            reader.Recycle();
        }

        public static void RegisterChannel(string name, IBasisCustomServerDataPublisher publisher)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Channel name cannot be empty", nameof(name));
            if (publisher == null) throw new ArgumentNullException(nameof(publisher));

            ushort id = (ushort)Interlocked.Increment(ref _nextChannelId);
            if (!Channels.TryAdd(name, new ChannelState(id, name, publisher)))
            {
                throw new InvalidOperationException($"Channel '{name}' is already registered.");
            }
        }

        public static bool UnregisterChannel(string name)
        {
            return Channels.TryRemove(name, out _);
        }

        public static void HandleSubscribeRequest(NetPeer peer, SerializableBasis.CustomServerDataSubscribeRequest request)
        {
            if (!Channels.TryGetValue(request.ChannelName, out var channel))
            {
                BNL.LogWarning($"Peer {peer.Id} tried to subscribe to non-existent channel: {request.ChannelName}");
                return;
            }

            // Send ProvideChannelId first
            var provideId = new SerializableBasis.CustomServerDataProvideChannelId
            {
                ChannelName = channel.Name,
                ChannelId = channel.Id
            };
            SendProvideIdToSpecificPeer(peer, provideId);

            List<byte[]> initialStateMessages = null;
            lock (channel.Lock)
            {
                var peerToSubscription = channel.PeerToSubscriptionsDict.GetOrAdd(peer.Id, _ => new HashSet<Guid>());
                peerToSubscription.Add(request.RequestID);
                initialStateMessages = channel.Publisher.GetInitialState();
            }

            foreach (byte[] initialState in initialStateMessages)
            {
                var initial = new SerializableBasis.CustomServerDataInitialState
                {
                    ChannelId = channel.Id,
                    Data = initialState,
                    RequestID = request.RequestID
                };
                SendInitialStateToSpecificPeer(peer, initial);
            }
        }

        public static void HandleUnsubscribeRequest(NetPeer peer, SerializableBasis.CustomServerDataUnsubscribeRequest request)
        {
            if (!Channels.TryGetValue(request.ChannelName, out var channel))
            {
                return;
            }

            lock (channel.Lock)
            {
                if (channel.PeerToSubscriptionsDict.TryGetValue(peer.Id, out var peerToSubscription))
                {
                    peerToSubscription.Remove(request.RequestID);
                    if (peerToSubscription.Count == 0)
                    {
                        channel.PeerToSubscriptionsDict.TryRemove(peer.Id, out _);
                    }
                }
            }
        }

        public static void Publish(string channelName, byte[] data)
        {
            if (!Channels.TryGetValue(channelName, out var channel))
            {
                return;
            }

            int[] targets;
            lock (channel.Lock)
            {
                targets = channel.PeerToSubscriptionsDict.Keys.ToArray();
            }

            if (targets.Length == 0) return;

            var update = new SerializableBasis.CustomServerDataMessage
            {
                ChannelId = channel.Id,
                Data = data
            };

            NetDataWriter writer = NetworkServer.RentWriter();
            writer.Put(BasisNetworkCommons.CustomServerData_Message);
            update.Serialize(writer);

            foreach (var peerId in targets)
            {
                if (NetworkServer.AuthenticatedPeers.TryGetValue(peerId, out var peer))
                {
                    peer.Send(writer, BasisNetworkCommons.CustomServerDataChannel, DeliveryMethod.ReliableOrdered);
                }
            }

            NetworkServer.ReturnWriter(writer);
        }

        public static void RemovePlayerSubscriptions(int peerId)
        {
            foreach (var channel in Channels.Values)
            {
                lock (channel.Lock)
                {
                    channel.PeerToSubscriptionsDict.TryRemove(peerId, out _);
                }
            }
        }

        private static void SendProvideIdToSpecificPeer(NetPeer peer, SerializableBasis.CustomServerDataProvideChannelId message)
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            writer.Put(BasisNetworkCommons.CustomServerData_ProvideChannelId);
            message.Serialize(writer);
            peer.Send(writer, BasisNetworkCommons.CustomServerDataChannel, DeliveryMethod.ReliableOrdered);
            NetworkServer.ReturnWriter(writer);
        }

        private static void SendInitialStateToSpecificPeer(NetPeer peer, SerializableBasis.CustomServerDataInitialState message)
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            writer.Put(BasisNetworkCommons.CustomServerData_InitialState);
            message.Serialize(writer);
            peer.Send(writer, BasisNetworkCommons.CustomServerDataChannel, DeliveryMethod.ReliableOrdered);
            NetworkServer.ReturnWriter(writer);
        }
    }
}
