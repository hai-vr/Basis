using Basis.Network.Core.Compression;
using Basis.Network.Server.Generic;
using Basis.Scripts.Networking.Compression;
using BasisNetworkCore;
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
        private static readonly ConcurrentQueue<QueuedMessage> MessageQueue = new();
        private static readonly ConcurrentDictionary<int, QueuedMessage> LatestMessages = new();
        private static readonly TimeSpan ProcessingInterval = TimeSpan.FromMilliseconds(15);
        private static CancellationTokenSource cts = new();
        private static readonly object ctsLock = new();

        static BasisServerReductionSystemEvents()
        {
            StartBackgroundProcessing();
        }

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

        private static void StartBackgroundProcessing()
        {
            Task.Run(async () =>
            {
                var peers = BasisPlayerArray.UnsafeArrayOfNetPeers;

                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(ProcessingInterval, cts.Token);

                        while (MessageQueue.TryDequeue(out var msg))
                        {
                            LatestMessages.AddOrUpdate(msg.FromPeer.Id, msg, (_, _) => msg);
                        }

                        Parallel.ForEach(LatestMessages.Values, (msg) =>
                        {
                            try
                            {
                                ProcessMessage(msg, peers);
                            }
                            catch (Exception ex)
                            {
                                BNL.LogError($"Error processing message: {ex}");
                            }
                        });

                        LatestMessages.Clear(); // Clear after processing to reuse next tick
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception ex)
                    {
                        BNL.LogError($"Unhandled exception in processing loop: {ex}");
                    }
                }
            }, cts.Token);
        }

        private static void ProcessMessage(QueuedMessage message, NetPeer[] peers)
        {
            if (message.FromPeer == null) return;

            int senderId = message.FromPeer.Id;
            var syncMsg = CreateServerSideSyncPlayerMessage(message.AvatarMessage, (ushort)senderId);

            BasisSavedState.AddLastData(senderId, message.AvatarMessage);
            Vector3 position = BasisNetworkCompressionExtensions.DecompressAndProcessAvatarFaster(message.AvatarMessage);

            BasisServerReductionSystem.UpdateLastInformation(syncMsg, position, senderId);

            for (int i = 0, len = peers.Length; i < len; ++i)
            {
                NetPeer peer = peers[i];
                if (peer == null || peer.Id == senderId) continue;

                int peerId = peer.Id;
                var pulse = BasisServerReductionSystem.PlayerSync.GetPulse(peerId);

                if (pulse == null)
                {
                    pulse = new SyncedToPlayerPulse
                    {
                        lastPlayerInformation = syncMsg,
                        Position = position,
                    };
                    BasisServerReductionSystem.PlayerSync.SetPulse(peerId, pulse);
                }

                pulse.SupplyNewData(peer, syncMsg, senderId, position);
            }

            QueuedMessagePool.Return(message);
        }

        public static ServerSideSyncPlayerMessage CreateServerSideSyncPlayerMessage(LocalAvatarSyncMessage local, ushort clientId)
        {
            return new ServerSideSyncPlayerMessage
            {
                playerIdMessage = new PlayerIdMessage { playerID = clientId },
                avatarSerialization = local
            };
        }

        public static void Shutdown()
        {
            lock (ctsLock)
            {
                Interlocked.Exchange(ref cts, new CancellationTokenSource()).Cancel();
            }
        }

        public class QueuedMessagePool
        {
            private static readonly ConcurrentBag<QueuedMessage> pool = new();

            public static QueuedMessage Rent()
            {
                return pool.TryTake(out var msg) ? msg : new QueuedMessage();
            }

            public static void Return(QueuedMessage msg)
            {
                msg.FromPeer = null;
              //  msg.AvatarMessage;
                pool.Add(msg);
            }
        }
    }
}
