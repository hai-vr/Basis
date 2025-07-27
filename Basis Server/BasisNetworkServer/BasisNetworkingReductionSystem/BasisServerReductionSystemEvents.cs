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
using static SerializableBasis;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public class QueuedMessage
    {
        public NetPeer FromPeer;
        public LocalAvatarSyncMessage AvatarMessage;
    }

    public partial class BasisServerReductionSystemEvents
    {
        private const int DistanceRecalcIntervalMs = 250;
        private static readonly CancellationTokenSource cts = new();
        private static readonly int MaxConcurrentPlayers = 1024;
        private static readonly ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        public static ConcurrentDictionary<int, NetPeer> peers = new();
        public static ConcurrentDictionary<int, bool> isActive = new();
        public static ConcurrentDictionary<int, Basis.Scripts.Networking.Compression.Vector3> positions = new();
        public static ConcurrentDictionary<int, FastBitSet> hasNewDataFrom = new();
        public static ConcurrentDictionary<int, ServerSideSyncPlayerMessage> syncMessages = new();
        public static ConcurrentDictionary<int, NetDataWriter> writers = new();

        public static ConcurrentDictionary<int, List<Player>> nearbyPlayers = new();
        public static ConcurrentDictionary<int, Dictionary<int, byte>> deliveryIntervals = new();
        public static ConcurrentDictionary<int, Dictionary<int, long>> lastSentTimes = new();

        public static Stopwatch distanceRecalcTimer = Stopwatch.StartNew();
        public static float BSRBaseMultiplier = 1.0f;
        public static float BSRSIncreaseRate = 0.01f;
        public static int BSRSMillisecondDefaultInterval = 50;
        public static byte BSRSMillisecondDefaultIntervalBytes = (byte)BSRSMillisecondDefaultInterval;
        private static readonly double MsToTick = Stopwatch.Frequency / 1000.0;

        private static ConcurrentDictionary<int, QueuedMessage> currentMessages = new();
        private static ConcurrentDictionary<int, QueuedMessage> processingMessages = new();

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
                long intervalTicks = (long)(BSRSMillisecondDefaultInterval * MsToTick);
                long nextTick = Stopwatch.GetTimestamp();

                while (!cts.Token.IsCancellationRequested)
                {
                    long current = Stopwatch.GetTimestamp();
                    long waitTicks = nextTick - current;
                    if (waitTicks > 0)
                        Thread.Sleep(TimeSpan.FromTicks(waitTicks));

                    nextTick += intervalTicks;

                    var temp = processingMessages;
                    processingMessages = currentMessages;
                    currentMessages = temp;
                    currentMessages.Clear();

                    Profiling.StartTimer("ProcessMessages", out long t1);
                    Parallel.ForEach(processingMessages.Values, parallelOptions, msg =>
                    {
                        try { ProcessMessage(msg); }
                        catch (Exception ex) { BNL.LogError($"[ProcessMessage] Exception: {ex}"); }
                    });
                    processingMessages.Clear();
                    Profiling.EndTimer("ProcessMessages", t1);

                    Profiling.StartTimer("SimulateCommunicationFromCache_Full", out long t3);
                    SimulateCommunicationRange(Stopwatch.GetTimestamp());
                    Profiling.EndTimer("SimulateCommunicationFromCache_Full", t3);

                    if (distanceRecalcTimer.ElapsedMilliseconds >= DistanceRecalcIntervalMs)
                    {
                        Profiling.StartTimer("RecalculateDistanceCache", out long t2);
                        RecalculateDistanceCache();
                        distanceRecalcTimer.Restart();
                        Profiling.EndTimer("RecalculateDistanceCache", t2);
                    }

                    Profiling.TryPrint();
                }
            });

            backgroundThread.IsBackground = true;
            backgroundThread.Start();
        }

        private static void SimulateCommunicationRange(long nowTicks)
        {
            Parallel.ForEach(peers.Keys, parallelOptions, i =>
            {
                if (!isActive.TryGetValue(i, out var active) || !active) return;
                if (!peers.TryGetValue(i, out var peer)) return;

                if (peer.GetPacketsCountInQueue(BasisNetworkCommons.FallChannel, DeliveryMethod.Unreliable) > 512) return;

                if (!writers.TryGetValue(i, out var writer)) return;
                if (!syncMessages.TryGetValue(i, out var syncMsg)) return;
                if (!hasNewDataFrom.TryGetValue(i, out var hasNew)) return;
                if (!nearbyPlayers.TryGetValue(i, out var nearby)) return;
                if (!deliveryIntervals.TryGetValue(i, out var intervals)) return;
                if (!lastSentTimes.TryGetValue(i, out var lastSent)) return;

                foreach (var other in nearby)
                {
                    if (!isActive.TryGetValue(other.Id, out var otherActive) || !otherActive) continue;

                    if (!intervals.TryGetValue(other.Id, out byte interval))
                        interval = BSRSMillisecondDefaultIntervalBytes;

                    long elapsed = nowTicks - lastSent.GetValueOrDefault(other.Id, 0);
                    long required = (long)(interval * MsToTick);

                    if (elapsed >= required && hasNew.Get(other.Id))
                    {
                        writer.Put(BasisNetworkCommons.PlayerAvatarChannel);

                        var tempMsg = other.syncMsg;
                        tempMsg.interval = interval;
                        tempMsg.Serialize(writer);

                        peer.Send(writer, BasisNetworkCommons.FallChannel, DeliveryMethod.Unreliable);
                        hasNew.Set(other.Id, false);
                        writer.Reset();
                        lastSent[other.Id] = nowTicks;
                    }
                }
            });
        }
        private static void ProcessMessage(QueuedMessage message)
        {
            int id = message.FromPeer.Id;

            if (!isActive.GetValueOrDefault(id))
            {
                isActive[id] = true;
                peers[id] = message.FromPeer;
                hasNewDataFrom[id] = new FastBitSet(MaxConcurrentPlayers);
                hasNewDataFrom[id].SetAll(true);
                positions[id] = BasisNetworkCompressionExtensions.ReadPosition(ref message.AvatarMessage.array);
                syncMessages[id] = new ServerSideSyncPlayerMessage
                {
                    playerIdMessage = new PlayerIdMessage { playerID = (ushort)id },
                    avatarSerialization = message.AvatarMessage
                };
                writers[id] = new NetDataWriter(true, 208);

                foreach (var i in isActive.Keys)
                {
                    if (i == id || !isActive[i]) continue;
                    hasNewDataFrom[i].Set(id, true);
                }
            }
            else
            {
                if (BasisPacketUtil.ValidatePacket(message.AvatarMessage.SequenceNumber, syncMessages.GetValueOrDefault(id).avatarSerialization.SequenceNumber))
                {
                    positions[id] = BasisNetworkCompressionExtensions.ReadPosition(ref message.AvatarMessage.array);

                    // Safely get and update syncMessages
                    if (syncMessages.TryGetValue(id, out var msg))
                    {
                        msg.avatarSerialization = message.AvatarMessage;
                        syncMessages[id] = msg;
                        hasNewDataFrom[id].SetAll(true);
                    }
                    else
                    {
                        // In case syncMessages is missing key, add it back
                        syncMessages[id] = new ServerSideSyncPlayerMessage
                        {
                            playerIdMessage = new PlayerIdMessage { playerID = (ushort)id },
                            avatarSerialization = message.AvatarMessage
                        };
                        hasNewDataFrom[id].SetAll(true);
                    }
                }
            }

            QueuedMessagePool.Return(message);
        }
        private static void RecalculateDistanceCache()
        {
            Parallel.ForEach(isActive.Keys, parallelOptions, i =>
            {
                if (!isActive.GetValueOrDefault(i)) return;
                if (!positions.TryGetValue(i, out var pos)) return;

                List<Player> nearby = new();
                Dictionary<int, byte> intervals = new();
                Dictionary<int, long> sentTimes = new();

                foreach (var j in isActive.Keys)
                {
                    if (j == i || !isActive.GetValueOrDefault(j)) continue;
                    float distSq = DistanceSquared(pos, positions[j]);
                    nearby.Add(new Player { Id = j, syncMsg = syncMessages[j] });
                    intervals[j] = CalculateIntervalFromDistanceSq(distSq);
                    sentTimes[j] = 0;
                }

                nearbyPlayers[i] = nearby;
                deliveryIntervals[i] = intervals;
                lastSentTimes[i] = sentTimes;
            });
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
            isActive[id] = false;
            peers.TryRemove(id, out _);
        }

        public struct Player
        {
            public int Id;
            public ServerSideSyncPlayerMessage syncMsg;
        }
    }
}
