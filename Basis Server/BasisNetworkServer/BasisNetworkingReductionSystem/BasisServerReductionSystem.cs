using Basis.Network.Core.Compression;
using Basis.Network.Server.Generic;
using Basis.Scripts.Networking.Compression;
using BasisNetworkCore;
using BasisNetworkCore.Pooling;
using LiteNetLib;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using static SerializableBasis;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public class QueuedMessage
    {
        public NetPeer FromPeer;
        public LocalAvatarSyncMessage AvatarMessage;
    }

    public class BasisServerReductionSystemEvents
    {
        private const int MillisecondsPerFrame = 15;

        private static readonly ConcurrentQueue<QueuedMessage> MessageQueue = new ConcurrentQueue<QueuedMessage>();
        private static readonly ConcurrentDictionary<int, QueuedMessage> LatestMessages = new ConcurrentDictionary<int, QueuedMessage>();
        private static readonly TimeSpan ProcessingInterval = TimeSpan.FromMilliseconds(MillisecondsPerFrame);
        private static readonly CancellationTokenSource cts = new CancellationTokenSource();
        public static EfficientScheduler scheduler;
        static BasisServerReductionSystemEvents()
        {
            scheduler = new EfficientScheduler();
            StartBackgroundProcessing();
        }

        /// <summary>
        /// Queues avatar movement data for later processing.
        /// </summary>
        public static void HandleAvatarMovement(NetPacketReader reader, NetPeer fromPeer)
        {
            var localMessage = new LocalAvatarSyncMessage();
            localMessage.Deserialize(reader);
            reader.Recycle();

            var message = QueuedMessagePool.Rent();
            message.FromPeer = fromPeer;
            message.AvatarMessage = localMessage;

            MessageQueue.Enqueue(message);
        }

        /// <summary>
        /// Starts a background task that processes avatar messages at fixed intervals.
        /// </summary>
        private static void StartBackgroundProcessing()
        {
            Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(ProcessingInterval, cts.Token);

                    var peerSnapshot = BasisPlayerArray.UnsafeArrayOfNetPeers;
                    int peerCount = peerSnapshot.Length;

                    ProcessQueuedMessages();
                    DispatchMessagesToPeers(peerSnapshot, peerCount);
                }
            }, cts.Token);
        }

        /// <summary>
        /// Dequeues all messages and stores only the latest per peer.
        /// </summary>
        private static void ProcessQueuedMessages()
        {
            LatestMessages.Clear();

            while (MessageQueue.TryDequeue(out var msg))
            {
                if (msg?.FromPeer == null)
                {
                    //  BNL.LogWarning("[Dequeue] Skipped null message or null peer.");
                    continue;
                }

                int peerId = msg.FromPeer.Id;

                LatestMessages.AddOrUpdate(peerId, msg, (key, oldMsg) =>
                {
                    ///   BNL.Log($"[Dequeue] Replacing message for peer {peerId}");
                    return msg;
                });
            }
        }

        /// <summary>
        /// Processes and sends messages to all peers.
        /// </summary>
        private static void DispatchMessagesToPeers(NetPeer[] peers, int peerCount)
        {
            Parallel.ForEach(LatestMessages.Values, msg =>
            {
                try
                {
                    ProcessMessage(msg, peers, peerCount);
                }
                catch (Exception ex)
                {
                    BNL.LogError($"[ProcessMessage] Exception: {ex}");
                }
            });
        }

        /// <summary>
        /// Processes an individual message and updates pulses for all other peers.
        /// </summary>
        private static void ProcessMessage(QueuedMessage message, NetPeer[] peers, int peerCount)
        {
            int fromPeerId = message.FromPeer.Id;


            var syncMsg = CreateServerSideSyncPlayerMessage(message.AvatarMessage, (ushort)fromPeerId);
            BasisSavedState.AddLastData(fromPeerId, message.AvatarMessage);

            Vector3 position = BasisNetworkCompressionExtensions.DecompressAndProcessAvatarFaster(message.AvatarMessage);
            BasisServerReductionSystem.UpdateLastInformation(syncMsg, position, fromPeerId);

            for (int Index = 0; Index < peerCount; Index++)
            {
                var targetPeer = peers[Index];
                if (targetPeer == null || targetPeer.Id == fromPeerId)
                    continue;

                int targetPeerId = targetPeer.Id;

                var pulse = BasisServerReductionSystem.PlayerSync.GetPulse(targetPeerId)
                            ?? CreateNewPulse(syncMsg, position, targetPeerId);

                pulse.SupplyNewData(targetPeer, syncMsg, fromPeerId, position);
            }
            QueuedMessagePool.Return(message);

            UpdateAllPlayerIntervals(peerCount);
        }

        /// <summary>
        /// Creates a new sync pulse for a peer.
        /// </summary>
        private static SyncedToPlayerPulse CreateNewPulse(ServerSideSyncPlayerMessage syncMsg, Vector3 position, int peerId)
        {
            var newPulse = new SyncedToPlayerPulse
            {
                Position = position
            };
            BasisServerReductionSystem.PlayerSync.SetPulse(peerId, newPulse);
            return newPulse;
        }

        /// <summary>
        /// Creates a sync message to be sent to other peers.
        /// </summary>
        public static ServerSideSyncPlayerMessage CreateServerSideSyncPlayerMessage(LocalAvatarSyncMessage local, ushort clientId)
        {
            return new ServerSideSyncPlayerMessage
            {
                playerIdMessage = new PlayerIdMessage { playerID = clientId },
                avatarSerialization = local
            };
        }

        /// <summary>
        /// Gracefully shuts down background processing.
        /// </summary>
        public static void Shutdown()
        {
            cts.Cancel();
        }

        /// <summary>
        /// Updates intervals for all players based on relative distance.
        /// </summary>
        public static void UpdateAllPlayerIntervals(int count)
        {
            for (int TopIndex = 0; TopIndex < count; TopIndex++)
            {
                var pulse = BasisServerReductionSystem.PlayerSync.GetPulse(TopIndex);
                if (pulse == null) continue;

                for (int LowerIndex = 0; LowerIndex < count; LowerIndex++)
                {
                    var playerData = pulse.ChunkedServerSideReducablePlayerArray.GetPlayer(LowerIndex);
                    if (playerData != null)
                    {
                        UpdatePlayerInterval(playerData, pulse.Position);
                    }
                }
            }
        }

        /// <summary>
        /// Updates sync timer interval for a single player based on distance.
        /// </summary>
        private static void UpdatePlayerInterval(ServerSideReducablePlayer playerData, Vector3 referencePosition)
        {
            float distanceSq = (referencePosition - playerData.Position).SquaredMagnitude();
            int rawInterval = (int)(SyncedToPlayerPulse.BSRSMillisecondDefaultInterval * (SyncedToPlayerPulse.BSRBaseMultiplier + (distanceSq * SyncedToPlayerPulse.BSRSIncreaseRate)));

            int clampedInterval = Math.Min(rawInterval, byte.MaxValue);
            byte intervalByte = (byte)clampedInterval;

            playerData.LastInterval = intervalByte;
            playerData.serverSideSyncPlayerMessage.interval = intervalByte;
        }
    }
}
