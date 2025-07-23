using Basis.Network.Core;
using Basis.Network.Core.Compression;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
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
        private const int TotalPlayers = 1024;
        private const int DistanceRecalcIntervalMs = 250;
        private static readonly CancellationTokenSource cts = new CancellationTokenSource();

        public static PlayerWrapper[] players = new PlayerWrapper[TotalPlayers];
        public static CachedCommunicationData[] cachedData = new CachedCommunicationData[1024];
        public static Stopwatch distanceRecalcTimer = Stopwatch.StartNew();
        public static float BSRBaseMultiplier = 1.0f;
        public static float BSRSIncreaseRate = 0.01f;
        public static int BSRSMillisecondDefaultInterval = 50;
        private static readonly double MsToTick = Stopwatch.Frequency / 1000.0;

        private static ConcurrentDictionary<int, QueuedMessage> currentMessages = new();
        private static ConcurrentDictionary<int, QueuedMessage> processingMessages = new();

        static BasisServerReductionSystemEvents()
        {
            for (int Index = 0; Index < cachedData.Length; Index++)
            {
                cachedData[Index] = new CachedCommunicationData(TotalPlayers);
            }

            StartBackgroundProcessing();
        }

        public static void HandleAvatarMovement(NetPacketReader reader, NetPeer fromPeer)
        {
            var localMessage = new LocalAvatarSyncMessage();
            localMessage.Deserialize(reader);
            reader.Recycle();
            AddMessage(fromPeer, localMessage);
        }
        public static void AddMessage(NetPeer fromPeer, LocalAvatarSyncMessage localMessage)
        {
            var message = QueuedMessagePool.Rent();
            message.FromPeer = fromPeer;
            message.AvatarMessage = localMessage;
            currentMessages.AddOrUpdate(fromPeer.Id, message, (_, _) => message);
        }
        private static void StartBackgroundProcessing()
        {
            Task.Run(async () =>
            {
                long intervalTicks = (long)(BSRSMillisecondDefaultInterval * MsToTick);
                long nextTick = Stopwatch.GetTimestamp() + intervalTicks;

                while (!cts.Token.IsCancellationRequested)
                {
                    long current = Stopwatch.GetTimestamp();
                    long waitTicks = nextTick - current;

                    if (waitTicks > 0)
                        await Task.Delay(TimeSpan.FromTicks(waitTicks), cts.Token);

                    nextTick += intervalTicks;

                    // Swap message buffers
                    var temp = processingMessages;
                    processingMessages = currentMessages;
                    currentMessages = temp;
                    currentMessages.Clear();

                    Parallel.ForEach(processingMessages.Values, msg =>
                    {
                        try
                        {
                            ProcessMessage(msg);
                        }
                        catch (Exception ex)
                        {
                            BNL.LogError($"[ProcessMessage] Exception: {ex}");
                        }
                    });

                    processingMessages.Clear();

                    if (distanceRecalcTimer.ElapsedMilliseconds >= DistanceRecalcIntervalMs)
                    {
                        RecalculateDistanceCache();
                        distanceRecalcTimer.Restart();
                    }

                    SimulateCommunicationFromCache();
                }
            }, cts.Token);
        }

        private static void ProcessMessage(QueuedMessage message)
        {
            int id = message.FromPeer.Id;
            ServerSideSyncPlayerMessage syncMsg = CreateServerSideSyncPlayerMessage(message.AvatarMessage, (ushort)id);
            var position = BasisNetworkCompressionExtensions.DecompressAndProcessAvatarFaster(message.AvatarMessage);

            if (!players[id].IsActive)
            {
                var data = new PlayerWrapper();
                var Player = new Player
                {
                    syncMsg = syncMsg,
                    Peer = message.FromPeer,
                    Id = id,
                    Position = position,
                    HasNewDataFrom = new BitArray(TotalPlayers, true),
                    Writer = new NetDataWriter(true, 208)
                };
                data.Player = Player;
                data.IsActive = true;
                players[id] = data;

                for (int Index = 0; Index < TotalPlayers; Index++)
                {
                    if (Index == id) continue;
                    if (players[Index].IsActive)
                    {
                        players[Index].Player.HasNewDataFrom.Set(id, true);
                    }
                }
            }
            else
            {
                var player = players[id].Player;
                player.Position = position;
                player.syncMsg = syncMsg;
                player.HasNewDataFrom.SetAll(true);
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

        public static void Shutdown() => cts.Cancel();

        public static void RemovePlayer(int id)
        {
            players[id].IsActive = false;
            players[id].Player = null;
        }

        private static void RecalculateDistanceCache()
        {
            int batchSize = 64;
            int totalBatches = TotalPlayers / batchSize;
            int maxThreads = Math.Max(Environment.ProcessorCount - 1, 1);

            Parallel.For(0, totalBatches, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, batchIndex =>
            {
                int start = batchIndex * batchSize;
                int end = Math.Min(start + batchSize, TotalPlayers);

                for (int i = start; i < end; i++)
                {
                    if (!players[i].IsActive) continue;

                    var player = players[i].Player;
                    var cacheEntry = cachedData[i];
                    cacheEntry.Count = 0;

                    for (int otherId = 0; otherId < TotalPlayers; otherId++)
                    {
                        if (otherId == i || !players[otherId].IsActive) continue;

                        var other = players[otherId].Player;
                        float distSq = DistanceSquared(player.Position, other.Position);

                        int insertIndex = cacheEntry.Count;

                        cacheEntry.NearbyPlayers[insertIndex] = other;
                        cacheEntry.DeliveryIntervals[insertIndex] = CalculateIntervalFromDistanceSq(distSq);

                        int previousIndex = Array.FindIndex(cacheEntry.NearbyPlayers, p => p?.Id == other.Id);
                        cacheEntry.LastSentTime[insertIndex] = previousIndex >= 0
                            ? cacheEntry.LastSentTime[previousIndex]
                            : Stopwatch.GetTimestamp();

                        cacheEntry.Count++;
                    }
                }
            });
        }

        private static float DistanceSquared(Basis.Scripts.Networking.Compression.Vector3 a, Basis.Scripts.Networking.Compression.Vector3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            float dz = a.z - b.z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static void SimulateCommunicationFromCache()
        {
            long nowTicks = Stopwatch.GetTimestamp();
            int batchSize = 64;
            int totalBatches = TotalPlayers / batchSize;
            int maxThreads = Math.Max(Environment.ProcessorCount - 1, 1);

            Parallel.For(0, totalBatches, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, batchIndex =>
            {
                int start = batchIndex * batchSize;
                int end = Math.Min(start + batchSize, TotalPlayers);

                for (int i = start; i < end; i++)
                {
                    if (!players[i].IsActive || players[i].Player == null) continue;

                    var player = players[i].Player;
                    int queuedMessages = player.Peer.GetPacketsCountInQueue(BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced);
                    if (queuedMessages > 20) continue;

                    var cacheEntry = cachedData[i];
                    for (int entryIndex = 0; entryIndex < cacheEntry.Count; entryIndex++)
                    {
                        long elapsedTicks = nowTicks - cacheEntry.LastSentTime[entryIndex];
                        byte interval = cacheEntry.DeliveryIntervals[entryIndex];
                        long requiredTicks = (long)(interval * MsToTick);

                        Player other = cacheEntry.NearbyPlayers[entryIndex];
                        if (elapsedTicks >= requiredTicks && other != null && players[other.Id].IsActive && player.HasNewDataFrom.Get(other.Id))
                        {
                            NetDataWriter writer = player.Writer;
                            other.syncMsg.interval = interval;
                            other.syncMsg.Serialize(writer);
                            player.Peer.Send(writer, BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced);
                            player.HasNewDataFrom.Set(other.Id, false);
                            writer.Reset();
                            cacheEntry.LastSentTime[entryIndex] = nowTicks;
                        }
                    }
                }
            });
        }

        private static byte CalculateIntervalFromDistanceSq(float distanceSq)
        {
            int rawInterval = (int)(BSRSMillisecondDefaultInterval * (BSRBaseMultiplier + (distanceSq * BSRSIncreaseRate)));
            return Math.Min((byte)rawInterval, byte.MaxValue);
        }

        public class CachedCommunicationData
        {
            public Player[] NearbyPlayers;
            public byte[] DeliveryIntervals;
            public long[] LastSentTime;
            public int Count;

            public CachedCommunicationData(int capacity)
            {
                NearbyPlayers = new Player[capacity];
                DeliveryIntervals = new byte[capacity];
                LastSentTime = new long[capacity];
                Count = 0;
            }
        }

        public struct PlayerWrapper
        {
            public Player Player;
            public bool IsActive;
        }

        public class Player
        {
            public NetDataWriter Writer;
            public NetPeer Peer;
            public int Id;
            public Basis.Scripts.Networking.Compression.Vector3 Position;
            public ServerSideSyncPlayerMessage syncMsg;
            public BitArray HasNewDataFrom;
        }
    }
}
