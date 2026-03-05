using Basis.Network.Core;
using Basis.Network.Core.Compression;
using BasisNetworkServer.BasisNetworking;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static BasisNetworkServer.BasisNetworkingReductionSystem.BasisServerReductionSystemEvents;
using static SerializableBasis;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;

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

        // Used for distance decisions
        public Basis.Scripts.Networking.Compression.Vector3 Position;

        // Who needs updates FROM whom (bitset indexed by player id)
        public FastBitSet HasNewDataFrom;

        // Base message shell (we swap avatarSerialization before send)
        public ServerSideSyncPlayerMessage SyncMessage;

        // Per-target last sent tick (used by distance-based interval logic)
        public Dictionary<int, long> LastSentTimes = new();

        // Cached per-quality payloads (payload bytes only, plus DataQualityLevel)
        public LocalAvatarSyncMessage AvatarHigh;
        public LocalAvatarSyncMessage AvatarMedium;
        public LocalAvatarSyncMessage AvatarLow;
        public LocalAvatarSyncMessage AvatarVeryLow;

        // --- Delta compression state ---
        public int KeyframeTickCounter;

        // Last keyframe payload per quality index (0=VeryLow,1=Low,2=Medium,3=High)
        public byte[][] KeyframeBaseline = new byte[4][];

        // Pre-allocated delta encode buffers per quality
        public byte[][] DeltaBuffer = new byte[4][];
        public int[] DeltaLength = new int[4];
        public bool[] DeltaComputed = new bool[4];

        // Per quality, tracks which recipients have received the current baseline keyframe
        public FastBitSet[] HasReceivedKeyframe = new FastBitSet[4];

        // Pre-serialized keyframe bytes per quality [PlayerID:2][interval_placeholder:1][quality:1][array:N][additionalSize:1]
        // The interval byte at offset 2 is filled per-recipient in the send loop.
        public byte[][] SerializedKeyframe = new byte[4][];
        public int[] SerializedKeyframeLength = new int[4];

        // Pre-serialized delta bytes per quality [PlayerID:2][interval_placeholder:1][quality:1][deltaPayload:M][additionalSize:1]
        public byte[][] SerializedDelta = new byte[4][];
        public int[] SerializedDeltaLength = new int[4];
    }

    public partial class BasisServerReductionSystemEvents
    {
        private static readonly CancellationTokenSource cts = new();
        private static readonly int MaxConcurrentPlayers = ushort.MaxValue;

        private static readonly ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        public static ConcurrentDictionary<int, PlayerState> playerStates = new();
        private static ConcurrentDictionary<int, QueuedMessage> currentMessages = new();

        public static float BSRBaseMultiplier = 1.0f;
        public static float BSRSIncreaseRate = 0.01f;
        public static int BSRSMillisecondDefaultInterval = 50;
        private static readonly double MsToTick = Stopwatch.Frequency / 1000.0;

        private static List<(int id, PlayerState state)> _threadLocalActivePlayers = new();
        public static readonly ConcurrentQueue<NetDataWriter> WriterPool = new();
        private static readonly ConcurrentQueue<int> playersToRemove = new();

        // Distance -> Quality thresholds (squared meters)
        public static float HighDistanceSq = 9f;        // 3m
        public static float MediumDistanceSq = 100f;    // 10m
        public static float LowDistanceSq = 400f;       // 20m

        // Delta compression: keyframe every N source updates (per player)
        public const int KeyframeInterval = 5;

        static BasisServerReductionSystemEvents()
        {
            _ = StartBackgroundProcessingAsync();
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
            long intervalMs = 2;

            while (!cts.Token.IsCancellationRequested)
            {
                long startTick = Stopwatch.GetTimestamp();

                // Snapshot messages safely
                var messagesSnapshot = new List<QueuedMessage>(currentMessages.Count);
                foreach (var kvp in currentMessages)
                {
                    if (currentMessages.TryRemove(kvp.Key, out var msg))
                    {
                        messagesSnapshot.Add(msg);
                    }
                }

                // Process messages (also adds players)
                Parallel.ForEach(messagesSnapshot, parallelOptions, msg =>
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

                ProcessPendingRemovals();

                // Network updates
                UpdateCommunicationAndDistances(Stopwatch.GetTimestamp());

                if (NetworkServer.Server != null && NetworkServer.Server.manager != null)
                {
                    NetworkServer.Server.manager.TriggerUpdate();
                }

                // Throttle loop if under time budget
                long elapsedTicks = Stopwatch.GetTimestamp() - startTick;
                long elapsedMs = (long)(elapsedTicks / MsToTick);
                long remainingMs = intervalMs - elapsedMs;

                if (remainingMs > 0)
                {
                    await Task.Delay((int)remainingMs, cts.Token);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }

        private static void ProcessPendingRemovals()
        {
            while (playersToRemove.TryDequeue(out int id))
            {
                if (playerStates.TryRemove(id, out var removedState))
                {
                    removedState.IsActive = false;

                    foreach (var kvp in playerStates)
                    {
                        var state = kvp.Value;
                        lock (state)
                        {
                            state.HasNewDataFrom?.Set(id, false);
                            state.LastSentTimes?.Remove(id);
                        }
                    }

                    BNL.Log($"Player {id} removed and cleaned up.");
                }
                else
                {
                    BNL.LogError("Missing Player From Index this is scary! " + id);
                }
            }
        }

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

            int playerCount = _threadLocalActivePlayers.Count;

            Parallel.ForEach(_threadLocalActivePlayers, parallelOptions, playerI =>
            {
                var stateI = playerI.state;
                var peer = stateI.Peer;

                bool canSend = peer.GetPacketsCountInQueue(BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced) < 2048;
                var sentTimes = stateI.LastSentTimes;

                for (int index = 0; index < playerCount; index++)
                {
                    (int id, PlayerState state) playerJ = _threadLocalActivePlayers[index];
                    if (playerI.id == playerJ.id)
                    {
                        continue;
                    }

                    var stateJ = playerJ.state;

                    float distSq = DistanceSquared(stateI.Position, stateJ.Position);

                    CalculateIntervalFromDistanceSq(distSq, out byte startAtZeroInterval, out int actualInterval);

                    if (!sentTimes.ContainsKey(playerJ.id))
                    {
                        sentTimes[playerJ.id] = 0;
                    }

                    if (stateI.HasNewDataFrom == null)
                    {
                        continue;
                    }

                    long lastSent = sentTimes[playerJ.id];
                    long elapsed = nowTicks - lastSent;
                    elapsed = Math.Max(0, elapsed);

                    long required = (long)(actualInterval * MsToTick);

                    bool hasNewData = stateI.HasNewDataFrom.Get(playerJ.id);

                    if (canSend && hasNewData && elapsed >= required)
                    {
                        stateI.HasNewDataFrom.Set(playerJ.id, false);

                        // Pick quality by distance
                        int qi = GetQualityIndex(distSq);

                        // Decide keyframe vs delta
                        bool needsKeyframe = stateJ.HasReceivedKeyframe[qi] == null
                            || !stateJ.HasReceivedKeyframe[qi].Get(playerI.id);

                        if (needsKeyframe || !stateJ.DeltaComputed[qi])
                        {
                            // Send keyframe using pre-serialized bytes
                            SendPreSerialized(peer, stateJ, qi, startAtZeroInterval,
                                BasisNetworkCommons.PlayerAvatarChannel,
                                stateJ.SerializedKeyframe, stateJ.SerializedKeyframeLength);

                            // Mark recipient as having received this baseline
                            stateJ.HasReceivedKeyframe[qi]?.Set(playerI.id, true);
                        }
                        else
                        {
                            // Send delta using pre-serialized bytes
                            SendPreSerialized(peer, stateJ, qi, startAtZeroInterval,
                                BasisNetworkCommons.DeltaPlayerAvatarChannel,
                                stateJ.SerializedDelta, stateJ.SerializedDeltaLength);
                        }

                        sentTimes[playerJ.id] = nowTicks;
                    }
                }
            });
        }

        /// <summary>
        /// Sends a pre-serialized message, patching the interval byte at offset 2.
        /// </summary>
        private static void SendPreSerialized(NetPeer peer, PlayerState sourceState, int qi,
            byte interval, byte channel, byte[][] serializedArray, int[] lengthArray)
        {
            int len = lengthArray[qi];
            byte[] src = serializedArray[qi];
            if (src == null || len == 0)
                return;

            NetDataWriter writer = RentWriter();
            writer.Put(src, 0, len);

            // Patch interval byte at offset 2 (after PlayerID ushort)
            if (writer.Length > 2)
            {
                writer.Data[2] = interval;
            }

            peer.Send(writer, channel, DeliveryMethod.Sequenced);
            BasisNetworkStatistics.RecordOutbound(channel, writer.Length);

            ReturnWriter(writer);
        }

        /// <summary>
        /// Maps squared distance to quality index (matches BitQuality enum values).
        /// </summary>
        private static int GetQualityIndex(float distSq)
        {
            if (distSq <= HighDistanceSq) return 3;   // High
            if (distSq <= MediumDistanceSq) return 2;  // Medium
            if (distSq <= LowDistanceSq) return 1;     // Low
            return 0;                                   // VeryLow
        }

        /// <summary>
        /// Gets the LocalAvatarSyncMessage for a given quality index.
        /// </summary>
        private static LocalAvatarSyncMessage GetAvatarForQuality(PlayerState state, int qi)
        {
            return qi switch
            {
                3 => state.AvatarHigh,
                2 => state.AvatarMedium,
                1 => state.AvatarLow,
                _ => state.AvatarVeryLow,
            };
        }

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

        private static void CalculateIntervalFromDistanceSq(float distanceSq, out byte offsetByte, out int actualInterval)
        {
            int rawInterval = (int)(BSRSMillisecondDefaultInterval * (BSRBaseMultiplier + (distanceSq * BSRSIncreaseRate)));
            int encodedInterval = rawInterval - BSRSMillisecondDefaultInterval;

            offsetByte = (byte)Math.Clamp(encodedInterval, 0, byte.MaxValue);
            actualInterval = offsetByte + BSRSMillisecondDefaultInterval;
        }

        public static void Shutdown() => cts.Cancel();

        public static void RemovePlayer(int id)
        {
            playersToRemove.Enqueue(id);
        }

        private static void ProcessMessage(QueuedMessage message)
        {
            int id = message.FromPeer.Id;

            var high = message.AvatarMessage;

            if (high.DataQualityLevel != (byte)BitQuality.High)
            {
                BNL.LogError($"Quality Level was {high.DataQualityLevel}");
                high.DataQualityLevel = (byte)BitQuality.High;
            }

            var pos = BasisNetworkCompressionExtensions.ReadPosition(ref high.array);

            if (!playerStates.TryGetValue(id, out var state))
            {
                state = new PlayerState
                {
                    Peer = message.FromPeer,
                    IsActive = true,
                    Position = pos,
                    HasNewDataFrom = new FastBitSet(MaxConcurrentPlayers),
                    SyncMessage = new ServerSideSyncPlayerMessage
                    {
                        playerIdMessage = new PlayerIdMessage { playerID = (ushort)id },
                        avatarSerialization = high
                    },
                    AvatarHigh = high,
                };

                // Build lower qualities using the zero-alloc Into variant.
                // First call allocates the arrays; subsequent calls reuse them.
                try
                {
                    AvatarQualityRepacker.BuildAllLowerFromHighInto(
                        high, ref state.AvatarMedium, ref state.AvatarLow, ref state.AvatarVeryLow);
                }
                catch (Exception ex)
                {
                    BNL.LogError($"[ProcessMessage] Repack failed: {ex}");
                    state.AvatarMedium = high;
                    state.AvatarLow = high;
                    state.AvatarVeryLow = high;
                }

                // Initialize delta compression
                for (int q = 0; q < 4; q++)
                {
                    state.HasReceivedKeyframe[q] = new FastBitSet(MaxConcurrentPlayers);
                }
                state.KeyframeTickCounter = 0;

                // First frame: set baselines and pre-serialize
                UpdateBaselines(state);
                PreSerializeAll(state);

                state.HasNewDataFrom.SetAll(true);
                playerStates[id] = state;

                // Everyone else needs new data from this new player
                foreach (var kvp in playerStates)
                {
                    if (kvp.Key == id || !kvp.Value.IsActive)
                    {
                        continue;
                    }

                    kvp.Value.HasNewDataFrom.Set(id, true);
                }
            }
            else
            {
                if (!state.IsActive)
                    state.IsActive = true;

                state.Position = pos;

                // Update high quality
                state.AvatarHigh = high;

                // Build lower qualities reusing existing buffers (zero-alloc after warmup)
                try
                {
                    AvatarQualityRepacker.BuildAllLowerFromHighInto(
                        high, ref state.AvatarMedium, ref state.AvatarLow, ref state.AvatarVeryLow);
                }
                catch (Exception ex)
                {
                    BNL.LogError($"[ProcessMessage] Repack failed: {ex}");
                    state.AvatarMedium = high;
                    state.AvatarLow = high;
                    state.AvatarVeryLow = high;
                }

                // Keep SyncMessage in sync (shell)
                state.SyncMessage.avatarSerialization = high;

                // Delta keyframe management
                state.KeyframeTickCounter++;
                if (state.KeyframeTickCounter >= KeyframeInterval)
                {
                    state.KeyframeTickCounter = 0;
                    UpdateBaselines(state);
                }

                // Compute deltas and pre-serialize all quality levels
                ComputeDeltas(state);
                PreSerializeAll(state);

                // Mark all other players as having new data FROM this sender
                foreach (var kvp in playerStates)
                {
                    if (kvp.Key == id)
                    {
                        continue;
                    }

                    var other = kvp.Value;
                    if (!other.IsActive)
                    {
                        continue;
                    }

                    other.HasNewDataFrom?.Set(id, true);
                }
            }

            QueuedMessagePool.Return(message);
        }

        #region Delta Compression Helpers

        private static void UpdateBaselines(PlayerState state)
        {
            UpdateBaselineForQuality(state, 0, state.AvatarVeryLow);
            UpdateBaselineForQuality(state, 1, state.AvatarLow);
            UpdateBaselineForQuality(state, 2, state.AvatarMedium);
            UpdateBaselineForQuality(state, 3, state.AvatarHigh);

            // Force all recipients to receive a new keyframe
            for (int q = 0; q < 4; q++)
            {
                state.HasReceivedKeyframe[q]?.SetAll(false);
                state.DeltaComputed[q] = false;
            }
        }

        private static void UpdateBaselineForQuality(PlayerState state, int qi, LocalAvatarSyncMessage msg)
        {
            if (msg.array == null) return;
            int len = msg.array.Length;
            if (state.KeyframeBaseline[qi] == null || state.KeyframeBaseline[qi].Length != len)
                state.KeyframeBaseline[qi] = new byte[len];
            Buffer.BlockCopy(msg.array, 0, state.KeyframeBaseline[qi], 0, len);
        }

        private static void ComputeDeltas(PlayerState state)
        {
            ComputeDeltaForQuality(state, 0, state.AvatarVeryLow);
            ComputeDeltaForQuality(state, 1, state.AvatarLow);
            ComputeDeltaForQuality(state, 2, state.AvatarMedium);
            ComputeDeltaForQuality(state, 3, state.AvatarHigh);
        }

        private static void ComputeDeltaForQuality(PlayerState state, int qi, LocalAvatarSyncMessage msg)
        {
            state.DeltaComputed[qi] = false;
            if (msg.array == null || state.KeyframeBaseline[qi] == null) return;
            if (msg.array.Length != state.KeyframeBaseline[qi].Length) return;

            int maxDelta = AvatarDeltaCompression.MaxDeltaSize(msg.array.Length);
            if (state.DeltaBuffer[qi] == null || state.DeltaBuffer[qi].Length < maxDelta)
                state.DeltaBuffer[qi] = new byte[maxDelta];

            state.DeltaLength[qi] = AvatarDeltaCompression.EncodeDelta(
                msg.array, state.KeyframeBaseline[qi], state.DeltaBuffer[qi]);
            state.DeltaComputed[qi] = true;
        }

        #endregion

        #region Pre-serialization

        /// <summary>
        /// Pre-serializes keyframe and delta messages for all 4 quality levels.
        /// The interval byte (offset 2) is left as 0 and patched per-recipient during send.
        /// </summary>
        private static void PreSerializeAll(PlayerState state)
        {
            ushort playerId = state.SyncMessage.playerIdMessage.playerID;

            PreSerializeKeyframe(state, 0, state.AvatarVeryLow, playerId);
            PreSerializeKeyframe(state, 1, state.AvatarLow, playerId);
            PreSerializeKeyframe(state, 2, state.AvatarMedium, playerId);
            PreSerializeKeyframe(state, 3, state.AvatarHigh, playerId);

            if (state.DeltaComputed[0]) PreSerializeDelta(state, 0, state.AvatarVeryLow, playerId);
            if (state.DeltaComputed[1]) PreSerializeDelta(state, 1, state.AvatarLow, playerId);
            if (state.DeltaComputed[2]) PreSerializeDelta(state, 2, state.AvatarMedium, playerId);
            if (state.DeltaComputed[3]) PreSerializeDelta(state, 3, state.AvatarHigh, playerId);
        }

        private static void PreSerializeKeyframe(PlayerState state, int qi, LocalAvatarSyncMessage msg, ushort playerId)
        {
            if (msg.array == null) return;

            var quality = (BitQuality)msg.DataQualityLevel;
            int expectedPayload = BasisAvatarBitPacking.ConvertToSize(quality);

            // [PlayerID:2][interval:1][quality:1][array:N][additionalSize:1][additional...]
            int additionalSize = 0;
            if (msg.AdditionalAvatarDatas != null && msg.AdditionalAvatarDatas.Length > 0)
            {
                additionalSize = 1; // LinkedAvatarIndex
                for (int i = 0; i < msg.AdditionalAvatarDatas.Length; i++)
                {
                    additionalSize += 1 + 1 + (msg.AdditionalAvatarDatas[i].array?.Length ?? 0); // PayloadSize + messageIndex + data
                }
            }

            int totalSize = 2 + 1 + 1 + expectedPayload + 1 + additionalSize;

            if (state.SerializedKeyframe[qi] == null || state.SerializedKeyframe[qi].Length < totalSize)
                state.SerializedKeyframe[qi] = new byte[totalSize];

            // Use a temporary writer to serialize correctly
            NetDataWriter writer = RentWriter();
            writer.Put(playerId);
            writer.Put((byte)0); // interval placeholder
            msg.Serialize(writer, quality);

            int written = writer.Length;
            Buffer.BlockCopy(writer.Data, 0, state.SerializedKeyframe[qi], 0, written);
            state.SerializedKeyframeLength[qi] = written;

            ReturnWriter(writer);
        }

        private static void PreSerializeDelta(PlayerState state, int qi, LocalAvatarSyncMessage msg, ushort playerId)
        {
            if (msg.array == null || !state.DeltaComputed[qi]) return;

            // [PlayerID:2][interval:1][quality:1][deltaPayload:M][additionalSize:1][additional...]
            int additionalSize = 0;
            if (msg.AdditionalAvatarDatas != null && msg.AdditionalAvatarDatas.Length > 0)
            {
                additionalSize = 1;
                for (int i = 0; i < msg.AdditionalAvatarDatas.Length; i++)
                {
                    additionalSize += 1 + 1 + (msg.AdditionalAvatarDatas[i].array?.Length ?? 0);
                }
            }

            int totalSize = 2 + 1 + 1 + state.DeltaLength[qi] + 1 + additionalSize;

            if (state.SerializedDelta[qi] == null || state.SerializedDelta[qi].Length < totalSize)
                state.SerializedDelta[qi] = new byte[totalSize];

            NetDataWriter writer = RentWriter();
            writer.Put(playerId);
            writer.Put((byte)0); // interval placeholder
            writer.Put(msg.DataQualityLevel);
            writer.Put(state.DeltaBuffer[qi], 0, state.DeltaLength[qi]);

            // Write additional avatar data (same as keyframe)
            if (msg.AdditionalAvatarDatas == null || msg.AdditionalAvatarDatas.Length == 0 || msg.AdditionalAvatarDatas.Length > 256)
            {
                writer.Put((byte)0);
            }
            else
            {
                writer.Put((byte)msg.AdditionalAvatarDatas.Length);
                writer.Put(msg.LinkedAvatarIndex);
                for (int i = 0; i < msg.AdditionalAvatarDatas.Length; i++)
                {
                    msg.AdditionalAvatarDatas[i].Serialize(writer);
                }
            }

            int written = writer.Length;
            Buffer.BlockCopy(writer.Data, 0, state.SerializedDelta[qi], 0, written);
            state.SerializedDeltaLength[qi] = written;

            ReturnWriter(writer);
        }

        #endregion
    }
}
