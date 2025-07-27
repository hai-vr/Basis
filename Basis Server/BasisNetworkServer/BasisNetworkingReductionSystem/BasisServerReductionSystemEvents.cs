using Basis.Network.Core;
using Basis.Network.Core.Compression;
using BasisNetworkCore;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static BasisNetworkServer.BasisNetworkingReductionSystem.BasisServerReductionSystemEvents;
using static SerializableBasis;
namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public class QueuedMessage
    {
        public NetPeer FromPeer;
        public LocalAvatarSyncMessage AvatarMessage;
    }

    public class PlayerState
    {
        public NetPeer Peer;
        public bool IsActive;
        public Basis.Scripts.Networking.Compression.Vector3 Position;
        public FastBitSet HasNewDataFrom;
        public ServerSideSyncPlayerMessage SyncMessage;
        public NetDataWriter Writer;
        public List<BasisServerReductionSystemEvents.Player> NearbyPlayers = new();
        public Dictionary<int, byte> DeliveryIntervals = new();
        public Dictionary<int, long> LastSentTimes = new();
    }

    public partial class BasisServerReductionSystemEvents
    {
        public static float BSRBaseMultiplier = 1.0f;
        public static float BSRSIncreaseRate = 0.01f;
        public static int BSRSMillisecondDefaultInterval = 50;
        public static byte BSRSMillisecondDefaultIntervalBytes = (byte)BSRSMillisecondDefaultInterval;

        private static readonly CancellationTokenSource cts = new();
        private static readonly int MaxConcurrentPlayers = 1024;
        private static readonly ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        public static ConcurrentDictionary<int, PlayerState> players = new();
        private static readonly double MsToTick = Stopwatch.Frequency / 1000.0;
        private static ConcurrentDictionary<int, QueuedMessage> currentMessages = new();

        static BasisServerReductionSystemEvents()
        {
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
            Thread backgroundThread = new(() =>
            {
                long intervalTicks = (long)(5 * MsToTick);
                long nextTick = Stopwatch.GetTimestamp();

                while (!cts.Token.IsCancellationRequested)
                {
                    long current = Stopwatch.GetTimestamp();
                    long waitTicks = nextTick - current;
                    if (waitTicks > 0)
                        Thread.Sleep(TimeSpan.FromTicks(waitTicks));

                    nextTick += intervalTicks;

                    Profiling.StartTimer("ProcessMessages", out long t1);
                    Parallel.ForEach(currentMessages.Values, parallelOptions, msg =>
                    {
                        try { ProcessMessage(msg); }
                        catch (Exception ex) { BNL.LogError($"[ProcessMessage] Exception: {ex}"); }
                    });
                    currentMessages.Clear();
                    Profiling.EndTimer("ProcessMessages", t1);

                    Profiling.StartTimer("SimulateCommunicationFromCache_Full", out long t3);
                    SimulateCommunicationAndRecalculate(Stopwatch.GetTimestamp());
                    Profiling.EndTimer("SimulateCommunicationFromCache_Full", t3);

                    Profiling.TryPrint();
                }
            });

            backgroundThread.IsBackground = true;
            backgroundThread.Start();
        }

        private static void SimulateCommunicationAndRecalculate(long nowTicks)
        {
            Parallel.ForEach(players, parallelOptions, kvp =>
            {
                int i = kvp.Key;
                var player = kvp.Value;

                if (!player.IsActive) return;
                if (player.Peer.GetPacketsCountInQueue(BasisNetworkCommons.FallChannel, DeliveryMethod.Unreliable) > 512) return;
                if (player.Writer == null || player.HasNewDataFrom == null) return;

                foreach (var otherKvp in players)
                {
                    int j = otherKvp.Key;
                    var other = otherKvp.Value;

                    if (j == i || !other.IsActive) continue;

                    float distSq = DistanceSquared(player.Position, other.Position);
                    byte interval = CalculateIntervalFromDistanceSq(distSq);

                    long elapsed = nowTicks - player.LastSentTimes.GetValueOrDefault(j, 0);
                    long required = (long)(interval * MsToTick);

                    if (elapsed >= required && player.HasNewDataFrom.Get(j))
                    {
                        player.Writer.Put(BasisNetworkCommons.PlayerAvatarChannel);

                        var tempMsg = other.SyncMessage;
                        tempMsg.interval = interval;
                        tempMsg.Serialize(player.Writer);

                        player.Peer.Send(player.Writer, BasisNetworkCommons.FallChannel, DeliveryMethod.Unreliable);
                        player.HasNewDataFrom.Set(j, false);
                        player.Writer.Reset();
                        player.LastSentTimes[j] = nowTicks;
                    }
                }
            });
        }

        private static void ProcessMessage(QueuedMessage message)
        {
            int id = message.FromPeer.Id;

            var state = players.GetOrAdd(id, _ =>
            {
                var newState = new PlayerState
                {
                    Peer = message.FromPeer,
                    IsActive = true,
                    HasNewDataFrom = new FastBitSet(MaxConcurrentPlayers),
                    Position = BasisNetworkCompressionExtensions.ReadPosition(ref message.AvatarMessage.array),
                    SyncMessage = new ServerSideSyncPlayerMessage
                    {
                        playerIdMessage = new PlayerIdMessage { playerID = (ushort)id },
                        avatarSerialization = message.AvatarMessage
                    },
                    Writer = new NetDataWriter(true, 208)
                };
                newState.HasNewDataFrom.SetAll(true);
                foreach (var other in players.Values)
                {
                    other.HasNewDataFrom?.Set(id, true);
                }
                return newState;
            });

            if (!state.IsActive)
                state.IsActive = true;

            if (BasisPacketUtil.ValidatePacket(message.AvatarMessage.SequenceNumber, state.SyncMessage.avatarSerialization.SequenceNumber))
            {
                state.Position = BasisNetworkCompressionExtensions.ReadPosition(ref message.AvatarMessage.array);
                state.SyncMessage.avatarSerialization = message.AvatarMessage;
                state.HasNewDataFrom.SetAll(true);
            }

            QueuedMessagePool.Return(message);
        }

        private static float DistanceSquared(Basis.Scripts.Networking.Compression.Vector3 a, Basis.Scripts.Networking.Compression.Vector3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            float dz = a.z - b.z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static byte CalculateIntervalFromDistanceSq(float distanceSq)
        {
            int rawInterval = (int)(BSRSMillisecondDefaultInterval * (BSRBaseMultiplier + (distanceSq * BSRSIncreaseRate)));
            return (byte)Math.Min(rawInterval, byte.MaxValue);
        }

        public static void Shutdown() => cts.Cancel();

        public static void RemovePlayer(int id)
        {
            if (players.TryGetValue(id, out var state))
            {
                state.IsActive = false;
            }
        }

        public struct Player
        {
            public int Id;
            public ServerSideSyncPlayerMessage syncMsg;
        }
    }
}
