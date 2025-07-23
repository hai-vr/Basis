using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Basis.Network.Server.Generic;
using Basis.Scripts.Networking.Compression;
using BasisNetworkCore;
using BasisNetworkCore.Pooling;
using LiteNetLib;
using LiteNetLib.Utils;
using Org.BouncyCastle.Utilities;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
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
    //  BasisSavedState.AddLastData(fromPeerId, message.AvatarMessage);
    public class BasisServerReductionSystemEvents
    {
        private const int MillisecondsPerFrame = 15;
        private const int TotalPlayers = 1024;
        private const int DistanceRecalcIntervalMs = 50;
        private static readonly TimeSpan ProcessingInterval = TimeSpan.FromMilliseconds(MillisecondsPerFrame);
        private static readonly CancellationTokenSource cts = new CancellationTokenSource();
        // Simulation state
        private static PlayerWrapper[] players = new PlayerWrapper[TotalPlayers];
        private static CachedCommunicationData[] cachedData = new CachedCommunicationData[1024];
        private static Stopwatch distanceRecalcTimer = Stopwatch.StartNew();
        public static float BSRBaseMultiplier = 1.0f;
        public static float BSRSIncreaseRate = 0.01f;
        public static int BSRSMillisecondDefaultInterval = 50;
        private static readonly double MsToTick = Stopwatch.Frequency / 1000.0;

        static BasisServerReductionSystemEvents()
        {

            for (int Index = 0; Index < cachedData.Length; Index++)
            {
                cachedData[Index] = new CachedCommunicationData(TotalPlayers);
            }

            StartBackgroundProcessing();
        }

        // Replace both queues with just one dictionary
        private static readonly ConcurrentDictionary<int, QueuedMessage> LatestMessages = new();

        // Modify HandleAvatarMovement:
        public static void HandleAvatarMovement(NetPacketReader reader, NetPeer fromPeer)
        {
            var localMessage = new LocalAvatarSyncMessage();
            localMessage.Deserialize(reader);
            reader.Recycle();

            var message = QueuedMessagePool.Rent();
            message.FromPeer = fromPeer;
            message.AvatarMessage = localMessage;

            // Replace or update directly
            LatestMessages.AddOrUpdate(fromPeer.Id, message, (_, _) => message);
        }
        private static void StartBackgroundProcessing()
        {
            Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(ProcessingInterval, cts.Token);
                    Parallel.ForEach(LatestMessages.Values, msg =>
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
                    LatestMessages.Clear();

                    // Process all 1024 players directly, no subset array needed
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
            Basis.Scripts.Networking.Compression.Vector3 position = BasisNetworkCompressionExtensions.DecompressAndProcessAvatarFaster(message.AvatarMessage);
            if (players[id].IsActive == false)
            {
                var data = new PlayerWrapper();
                var Player = new Player();
                data.Player = Player;
                data.IsActive = true;
                Player.syncMsg = syncMsg;
                Player.Peer = message.FromPeer;
                Player.Id = id;
                Player.Position = position;
                Player.HasNewDataFrom = new BitArray(TotalPlayers, true); // all true initially
                Player.Writer = new NetDataWriter(true, 208);
                players[id] = data;
            }
            else
            {
                Player player = players[id].Player;
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
        public static void Shutdown()
        {
            cts.Cancel();
        }
        public static void RemovePlayer(int id)
        {
            players[id].IsActive = false;
            players[id].Player = null;
        }
        private static void RecalculateDistanceCache()
        {
            Parallel.For(0, TotalPlayers, i =>
            {
                if (!players[i].IsActive) return;

                var player = players[i].Player;
                var cacheEntry = cachedData[i];
                cacheEntry.Count = 0;

                for (int PlayerIndex = 0; PlayerIndex < TotalPlayers; PlayerIndex++)
                {
                    if (PlayerIndex == i || !players[PlayerIndex].IsActive) continue;

                    var other = players[PlayerIndex].Player;
                    float distSq = DistanceSquared(player.Position, other.Position);

                    int insertIndex = cacheEntry.Count;

                    cacheEntry.NearbyPlayers[insertIndex] = other;
                    cacheEntry.DeliveryIntervals[insertIndex] = CalculateIntervalFromDistanceSq(distSq);

                    // Preserve old last sent time if still valid
                    int previousIndex = Array.FindIndex(cacheEntry.NearbyPlayers, p => p?.Id == other.Id);
                    cacheEntry.LastSentTime[insertIndex] = previousIndex >= 0 ? cacheEntry.LastSentTime[previousIndex] : Stopwatch.GetTimestamp();

                    cacheEntry.Count++;
                }
            });
        }
        private static float DistanceSquared(Basis.Scripts.Networking.Compression.Vector3 position1, Basis.Scripts.Networking.Compression.Vector3 position2)
        {
            float dx = position1.x - position2.x;
            float dy = position1.y - position2.y;
            float dz = position1.z - position2.z;
            return dx * dx + dy * dy + dz * dz;
        }
        private static void SimulateCommunicationFromCache()
        {
            long nowTicks = Stopwatch.GetTimestamp();

            Parallel.For(0, TotalPlayers, Index =>
            {
                if (!players[Index].IsActive || players[Index].Player == null) return;

                var player = players[Index].Player;
                var cacheEntry = cachedData[Index];

                for (int entryIndex = 0; entryIndex < cacheEntry.Count; entryIndex++)
                {
                    long elapsedTicks = nowTicks - cacheEntry.LastSentTime[entryIndex];
                    byte Interval = cacheEntry.DeliveryIntervals[entryIndex];
                    long requiredTicks = (long)((long)Interval * MsToTick);

                    Player other = cacheEntry.NearbyPlayers[entryIndex];
                    if (elapsedTicks >= requiredTicks && other != null && players[other.Id].IsActive && player.HasNewDataFrom.Get(other.Id))
                    {
                        NetDataWriter Writer = player.Writer;
                        other.syncMsg.interval = Interval;
                        other.syncMsg.Serialize(Writer);
                        NetworkServer.TrySend(player.Peer, Writer, BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced);
                        player.HasNewDataFrom.Set(other.Id, false);
                        Writer.Reset();
                        cacheEntry.LastSentTime[entryIndex] = nowTicks;

                    }
                }
            });
        }
        private static byte CalculateIntervalFromDistanceSq(float distanceSq)
        {
            int rawInterval = (int)(BSRSMillisecondDefaultInterval * (BSRBaseMultiplier + (distanceSq * BSRSIncreaseRate)));
            return Math.Min((byte)rawInterval, byte.MaxValue);
        }

        class CachedCommunicationData
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

        struct PlayerWrapper
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
