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
        private static Dictionary<Guid, ChannelRegistration> _requestToEffectiveChannels = new();

        private class ChannelRegistration
        {
            public Guid RequestId;
            public string ChannelPattern;
            public HashSet<string> ResolvedChannelNames;
        }

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
            if (_requestToEffectiveChannels.ContainsKey(request.RequestID))
            {
                BNL.LogWarning($"Peer {peer.Id} tried to subscribe to channel {request.ChannelPattern} with request ID {request.RequestID} that is already used. It will be ignored");
                return;
            }
            
            var channelNames = Match(request.ChannelPattern);
            if (channelNames.Count == 0)
            {
                BNL.LogWarning($"Peer {peer.Id} tried to subscribe to a channel, but there were no matches: {request.ChannelPattern}");
                return;
            }
            
            foreach (var channelName in channelNames)
            {
                ProcessSubscribeToChannel(peer, channelName, request.RequestID);
            }

            _requestToEffectiveChannels.Add(request.RequestID, new ChannelRegistration
            {
                RequestId = request.RequestID,
                ChannelPattern = request.ChannelPattern,
                ResolvedChannelNames = channelNames,
            });
        }

        private static HashSet<string> Match(string channelPattern)
        {
            var result = new HashSet<string>();
            
            var isSpecialPattern = channelPattern.StartsWith("/") && (channelPattern.Contains("#") || channelPattern.Contains("*") || channelPattern.Contains("+"));
            if (!isSpecialPattern)
            {
                if (Channels.TryGetValue(channelPattern, out var channelName))
                {
                    result.Add(channelName.Name);
                }
            }
            else
            {
                var patternSegments = channelPattern.Split('/');
                if (patternSegments.Length <= 1 || (patternSegments.Length == 2 && string.IsNullOrEmpty(patternSegments[0])))
                {
                    return result;
                }
                
                foreach (var channel in Channels.Values)
                {
                    if (MqttLikeMatch(channel.Name, channelPattern))
                    {
                        result.Add(channel.Name);
                    }
                }
            }
            
            return result;
        }

        private static bool MqttLikeMatch(string channelName, string channelPattern)
        {
            // Split both the channel name and pattern into segments
            var nameSegments = channelName.Split('/');
            var patternSegments = channelPattern.Split('/');

            // Track positions in both arrays
            int nameIndex = 0;
            int patternIndex = 0;

            while (nameIndex < nameSegments.Length && patternIndex < patternSegments.Length)
            {
                string patternSegment = patternSegments[patternIndex];

                // '#' matches zero or more segments (multi-level wildcard)
                if (patternSegment == "#")
                {
                    // '#' must be the last segment in the pattern
                    return patternIndex == patternSegments.Length - 1;
                }

                // '+' matches exactly one segment (single-level wildcard)
                if (patternSegment == "+")
                {
                    nameIndex++;
                    patternIndex++;
                    continue;
                }

                // '*' matches any characters within a single segment
                if (patternSegment.Contains("*"))
                {
                    if (!WildcardMatch(nameSegments[nameIndex], patternSegment))
                    {
                        return false;
                    }
                    nameIndex++;
                    patternIndex++;
                    continue;
                }

                // Exact match required
                if (nameSegments[nameIndex] != patternSegment)
                {
                    return false;
                }

                nameIndex++;
                patternIndex++;
            }

            // Both must be fully consumed for a match
            return nameIndex == nameSegments.Length && patternIndex == patternSegments.Length;
        }

        private static bool WildcardMatch(string text, string pattern)
        {
            int textIndex = 0;
            int patternIndex = 0;
            int starIndex = -1;
            int matchIndex = 0;

            while (textIndex < text.Length)
            {
                if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
                {
                    starIndex = patternIndex;
                    matchIndex = textIndex;
                    patternIndex++;
                }
                else if (patternIndex < pattern.Length && pattern[patternIndex] == text[textIndex])
                {
                    textIndex++;
                    patternIndex++;
                }
                else if (starIndex != -1)
                {
                    patternIndex = starIndex + 1;
                    matchIndex++;
                    textIndex = matchIndex;
                }
                else
                {
                    return false;
                }
            }

            while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                patternIndex++;
            }

            return patternIndex == pattern.Length;
        }

        private static void ProcessSubscribeToChannel(NetPeer peer, string channelName, Guid requestId)
        {
            if (!Channels.TryGetValue(channelName, out var channel))
            {
                BNL.LogWarning($"Peer {peer.Id} tried to subscribe to non-existent channel: {channelName}");
                return;
            }

            var provideId = new SerializableBasis.CustomServerDataProvideChannelId
            {
                ChannelName = channel.Name,
                ChannelId = channel.Id,
                RequestID = requestId
            };
            SendProvideIdToSpecificPeer(peer, provideId);

            List<byte[]> initialStateMessages = null;
            lock (channel.Lock)
            {
                var peerToSubscription = channel.PeerToSubscriptionsDict.GetOrAdd(peer.Id, _ => new HashSet<Guid>());
                peerToSubscription.Add(requestId);
                initialStateMessages = channel.Publisher.GetInitialState();
            }

            foreach (byte[] initialState in initialStateMessages)
            {
                var initial = new SerializableBasis.CustomServerDataInitialState
                {
                    ChannelId = channel.Id,
                    Data = initialState,
                    RequestID = requestId
                };
                SendInitialStateToSpecificPeer(peer, initial);
            }
        }

        public static void HandleUnsubscribeRequest(NetPeer peer, SerializableBasis.CustomServerDataUnsubscribeRequest request)
        {
            if (!_requestToEffectiveChannels.TryGetValue(request.RequestID, out var registration))
            {
                return;
            }
            if (registration.ChannelPattern != request.ChannelPattern)
            {
                BNL.LogWarning($"Peer {peer.Id} tried to unsubscribe from channel pattern {request.ChannelPattern}, but request ID {request.RequestID} was not used to subscribe to that channel pattern.");
                return;
            }
            
            foreach (string channelName in registration.ResolvedChannelNames)
            {
                if (Channels.TryGetValue(channelName, out var channel))
                {
                    lock (channel.Lock)
                    {
                        _requestToEffectiveChannels.Remove(request.RequestID);
                
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
