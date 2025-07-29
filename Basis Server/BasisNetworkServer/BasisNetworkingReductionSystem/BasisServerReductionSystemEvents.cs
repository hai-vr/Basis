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
        public Dictionary<int, byte> DeliveryIntervals = new();
        public Dictionary<int, long> LastSentTimes = new();
    }

    public partial class BasisServerReductionSystemEvents
    {
        private static readonly CancellationTokenSource cts = new();
        private static readonly int MaxConcurrentPlayers = 1024;
        private static readonly ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
        };

        public static ConcurrentDictionary<int, PlayerState> playerStates = new();
        private static ConcurrentDictionary<int, QueuedMessage> currentMessages = new();

        public static float BSRBaseMultiplier = 1.0f;
        public static float BSRSIncreaseRate = 0.01f;
        public static int BSRSMillisecondDefaultInterval = 50;
        private static readonly double MsToTick = Stopwatch.Frequency / 1000.0;

        static BasisServerReductionSystemEvents()
        {
            _ = StartBackgroundProcessingAsync(); // fire-and-forget async background task
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

        private static async Task StartBackgroundProcessingAsync()
        {
            long intervalMs = 25;

            while (!cts.Token.IsCancellationRequested)
            {
                long startTick = Stopwatch.GetTimestamp();

                Profiling.StartTimer("ProcessMessages", out long t1);
                Parallel.ForEach(currentMessages.Values, parallelOptions, msg =>
                {
                    try { ProcessMessage(msg); }
                    catch (Exception ex) { BNL.LogError($"[ProcessMessage] Exception: {ex}"); }
                });
                currentMessages.Clear();
                Profiling.EndTimer("ProcessMessages", t1);

                Profiling.StartTimer("SimulateCommunicationFromCache_Full", out long t2);
                UpdateCommunicationAndDistances(Stopwatch.GetTimestamp());
                Profiling.EndTimer("SimulateCommunicationFromCache_Full", t2);

                Profiling.TryPrint();

                long elapsedTicks = Stopwatch.GetTimestamp() - startTick;
                long elapsedMs = (long)(elapsedTicks / MsToTick);
                long remainingMs = intervalMs - elapsedMs;

                if (remainingMs > 0)
                    await Task.Delay((int)remainingMs, cts.Token);
                else
                    await Task.Yield(); // if over budget, yield control briefly
            }
        }
        private static List<(int id, PlayerState state)> _threadLocalActivePlayers = new();
        private static void UpdateCommunicationAndDistances(long nowTicks)
        {
            _threadLocalActivePlayers.Clear();
            foreach (var kvp in playerStates)
            {
                if (kvp.Value.IsActive)
                {
                    _threadLocalActivePlayers.Add((kvp.Key, kvp.Value));
                }
            }
            int PlayerCount = _threadLocalActivePlayers.Count;

            Parallel.ForEach(_threadLocalActivePlayers, parallelOptions, playerI =>
            {
                var stateI = playerI.state;
                var peer = stateI.Peer;

                bool canSend = peer.GetPacketsCountInQueue(BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced) <= 1024;
                var intervals = stateI.DeliveryIntervals;
                var sentTimes = stateI.LastSentTimes;

                for (int Index = 0; Index < PlayerCount; Index++)
                {
                    var playerJ = _threadLocalActivePlayers[Index];
                    if (playerI.id == playerJ.id)
                        continue;

                    var stateJ = playerJ.state;
                    float distSq = DistanceSquared(stateI.Position, stateJ.Position);
                    byte interval = CalculateIntervalFromDistanceSq(distSq);
                    intervals[playerJ.id] = interval;

                    if (!sentTimes.ContainsKey(playerJ.id))
                        sentTimes[playerJ.id] = 0;

                    if (canSend && stateI.HasNewDataFrom != null)
                    {
                        long elapsed = nowTicks - sentTimes[playerJ.id];
                        long required = (long)(interval * MsToTick);

                        if (elapsed >= required && stateI.HasNewDataFrom.Get(playerJ.id))
                        {
                            var tempMsg = stateJ.SyncMessage;
                            tempMsg.interval = interval;
                            NetDataWriter Writer = RentWriter();
                            tempMsg.Serialize(Writer);
                            peer.Send(Writer, BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced);
                            stateI.HasNewDataFrom.Set(playerJ.id, false);
                            ReturnWriter(Writer);
                            sentTimes[playerJ.id] = nowTicks;
                        }
                    }
                }
            });
        }
        public static readonly ConcurrentQueue<NetDataWriter> WriterPool = new();

        public static NetDataWriter RentWriter()
        {
            return WriterPool.TryDequeue(out var writer) ? writer : new NetDataWriter(true, 208);
        }

        public static void ReturnWriter(NetDataWriter writer)
        {
            writer.Reset();
            WriterPool.Enqueue(writer);
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
            if (playerStates.TryRemove(id, out var removedState))
            {
                removedState.IsActive = false;

                // Clean up HasNewDataFrom bitsets in other players
                foreach (var kvp in playerStates)
                {
                    kvp.Value.HasNewDataFrom?.Set(id, false);
                    kvp.Value.DeliveryIntervals?.Remove(id);
                    kvp.Value.LastSentTimes?.Remove(id);
                }

                BNL.Log($"Player {id} removed and cleaned up.");
            }
            else
            {
                BNL.LogError("Missing Player From Index this is scary! " + id);
            }
        }

        public struct Player
        {
            public readonly int Id;
            public ServerSideSyncPlayerMessage syncMsg;

            public Player(int id, ServerSideSyncPlayerMessage syncMsg)
            {
                Id = id;
                this.syncMsg = syncMsg;
            }
        }

        private static void ProcessMessage(QueuedMessage message)
        {
            int id = message.FromPeer.Id;
            if (!playerStates.TryGetValue(id, out var state))
            {
                state = new PlayerState
                {
                    Peer = message.FromPeer,
                    IsActive = true,
                    Position = BasisNetworkCompressionExtensions.ReadPosition(ref message.AvatarMessage.array),
                    HasNewDataFrom = new FastBitSet(MaxConcurrentPlayers),
                    SyncMessage = new ServerSideSyncPlayerMessage
                    {
                        playerIdMessage = new PlayerIdMessage { playerID = (ushort)id },
                        avatarSerialization = message.AvatarMessage
                    },
                };
                state.HasNewDataFrom.SetAll(true);
                playerStates[id] = state;

                foreach (var kvp in playerStates)
                {
                    if (kvp.Key == id || !kvp.Value.IsActive) continue;
                    kvp.Value.HasNewDataFrom.Set(id, true);
                }
            }
            else
            {
                if (!state.IsActive) state.IsActive = true;

                state.Position = BasisNetworkCompressionExtensions.ReadPosition(ref message.AvatarMessage.array);
                state.SyncMessage.avatarSerialization = message.AvatarMessage;
                state.HasNewDataFrom.SetAll(true);
            }

            QueuedMessagePool.Return(message);
        }
    }
}
