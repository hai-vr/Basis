using Basis.Network.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using static SerializableBasis;

namespace Basis.Network.Server.Generic
{
    public static class BasisNetworkingGeneric
    {
        private const int MissingPeerReportIntervalSeconds = 10;
        private static readonly long MissingPeerReportIntervalTicks = TimeSpan.TicksPerSecond * MissingPeerReportIntervalSeconds;
        private static long _missingPeerCount;
        private static long _missingPeerNextReportTicks;

        private static void ReportMissingPeer()
        {
            Interlocked.Increment(ref _missingPeerCount);

            long now = DateTime.UtcNow.Ticks;
            long next = Interlocked.Read(ref _missingPeerNextReportTicks);
            if (now < next)
            {
                return;
            }
            if (Interlocked.CompareExchange(ref _missingPeerNextReportTicks, now + MissingPeerReportIntervalTicks, next) != next)
            {
                return;
            }

            long dropped = Interlocked.Exchange(ref _missingPeerCount, 0);
            if (dropped > 0)
            {
                BNL.Log($"Missing Peer! dropped {dropped} targeted message(s) for peers that were not authenticated in the last {MissingPeerReportIntervalSeconds}s.");
            }
        }

        [ThreadStatic]
        private static List<NetPeer> _targetedClients;
        private static List<NetPeer> GetTargetedList()
        {
            if (_targetedClients == null) _targetedClients = new List<NetPeer>();
            else _targetedClients.Clear();
            return _targetedClients;
        }

        [ThreadStatic]
        private static HashSet<ushort> _seenRecipients;
        private static HashSet<ushort> GetSeenSet()
        {
            if (_seenRecipients == null) _seenRecipients = new HashSet<ushort>();
            else _seenRecipients.Clear();
            return _seenRecipients;
        }

        public static void HandleScene(NetPacketReader Reader, DeliveryMethod DeliveryMethod, NetPeer sender, byte broadcastChannel = BasisNetworkCommons.SceneChannel)
        {
            SceneDataMessage SceneDataMessage = new SceneDataMessage();
            SceneDataMessage.Deserialize(Reader);
            Reader.Recycle();

            byte[] payload = SceneDataMessage.payload;
            int payloadLength = (payload != null) ? payload.Length : 0;

            ServerSceneDataMessage serverSceneDataMessage = new ServerSceneDataMessage
            {
                sceneDataMessage = new RemoteSceneDataMessage()
                {
                    messageIndex = SceneDataMessage.messageIndex,
                    payload = payload,
                    payloadLength = payloadLength
                },
                playerIdMessage = new PlayerIdMessage
                {
                    playerID = (ushort)sender.Id,
                }
            };

            byte Channel = broadcastChannel;
            NetDataWriter Writer = NetworkServer.RentWriter();
            serverSceneDataMessage.Serialize(Writer);
            if (SceneDataMessage.recipientsSize != 0)
            {
                List<NetPeer> targetedClients = GetTargetedList();
                HashSet<ushort> seen = GetSeenSet();

                int recipientsLength = SceneDataMessage.recipientsSize;
                for (int index = 0; index < recipientsLength; index++)
                {
                    ushort recipient = SceneDataMessage.recipients[index];
                    if (!seen.Add(recipient))
                    {
                        continue;
                    }
                    if (NetworkServer.AuthenticatedPeers.TryGetValue(recipient, out NetPeer client))
                    {
                        targetedClients.Add(client);
                    }
                    else
                    {
                        ReportMissingPeer();
                    }
                }

                if (targetedClients.Count > 0)
                {
                    NetworkServer.BroadcastMessageToClients(Writer, Channel, ref targetedClients, DeliveryMethod);
                }
            }
            else
            {
                NetworkServer.BroadcastMessageToClients(Writer, Channel, sender, NetworkServer.PeerSnapshot, DeliveryMethod);
            }
            NetworkServer.ReturnWriter(Writer);
            serverSceneDataMessage.sceneDataMessage.Release();
        }
        public static void HandleAvatar(NetPacketReader Reader, DeliveryMethod DeliveryMethod, NetPeer sender, byte broadcastChannel = BasisNetworkCommons.AvatarChannel)
        {
            AvatarDataMessage avatarDataMessage = new AvatarDataMessage();
            avatarDataMessage.Deserialize(Reader);
            Reader.Recycle();
            ServerAvatarDataMessage serverAvatarDataMessage = new ServerAvatarDataMessage
            {
                avatarDataMessage = new RemoteAvatarDataMessage()
                {
                    messageIndex = avatarDataMessage.messageIndex,
                    payload = avatarDataMessage.payload,
                    PlayerIdMessage = avatarDataMessage.PlayerIdMessage,
                    AvatarLinkIndex = avatarDataMessage.AvatarLinkIndex,
                },
                playerIdMessage = new PlayerIdMessage
                {
                    playerID = (ushort)sender.Id
                }
            };
            byte Channel = broadcastChannel;
            NetDataWriter Writer = NetworkServer.RentWriter();
            serverAvatarDataMessage.Serialize(Writer);
            if (avatarDataMessage.recipientsSize != 0)
            {
                List<NetPeer> targetedClients = GetTargetedList();
                HashSet<ushort> seen = GetSeenSet();

                int recipientsLength = avatarDataMessage.recipientsSize;
                for (int index = 0; index < recipientsLength; index++)
                {
                    ushort recipient = avatarDataMessage.recipients[index];
                    if (!seen.Add(recipient))
                    {
                        continue;
                    }
                    if (NetworkServer.AuthenticatedPeers.TryGetValue(recipient, out NetPeer client))
                    {
                        targetedClients.Add(client);
                    }
                    else
                    {
                        ReportMissingPeer();
                    }
                }

                if (targetedClients.Count > 0)
                {
                    NetworkServer.BroadcastMessageToClients(Writer, Channel, ref targetedClients, DeliveryMethod);
                }
            }
            else
            {
                NetworkServer.BroadcastMessageToClients(Writer, Channel, sender, NetworkServer.PeerSnapshot, DeliveryMethod);
            }
            NetworkServer.ReturnWriter(Writer);
        }
    }
}
