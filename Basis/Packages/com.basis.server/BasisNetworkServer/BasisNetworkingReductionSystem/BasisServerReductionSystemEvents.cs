using Basis.Network.Core;
using Basis.Network.Core.Compression;
using BasisNetworkServer.BasisNetworking;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static SerializableBasis;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public class QueuedMessage
    {
        public NetPeer FromPeer;
        public byte Sequence;
        public LocalAvatarSyncMessage AvatarMessage;
    }

    /// <summary>
    /// Combined per-peer tracking data. Two longs in one struct = one cache line fetch
    /// instead of two parallel array accesses in the O(N²) send loop.
    /// </summary>
    public struct PeerTrackingData
    {
        public long LastSentTime;
        public long LastSeenGeneration;
    }

    public class PlayerState
    {
        public NetPeer Peer;
        public bool IsActive;

        // Used for distance decisions
        public Basis.Scripts.Networking.Compression.Vector3 Position;

        // Base message shell (we swap avatarSerialization before send)
        public ServerSideSyncPlayerMessage SyncMessage;

        // Combined per-peer tracking: last sent tick + last seen generation in one struct
        // for cache-friendly O(1) access in the send loop. Indexed by player id.
        public PeerTrackingData[] PeerTracking;

        // Generation counter: incremented each time this player receives new avatar data.
        // Receivers compare against their LastSeenGeneration to know if there is new data.
        // Access via Interlocked.Read/Increment for thread safety on 32-bit or cross-core visibility.
        public long DataGeneration;

        // Cached during ProcessMessage to avoid dereference chain in the inner send loop.
        public bool HasAdditionalData;

        // Cached per-quality payloads (payload bytes only, plus DataQualityLevel)
        public LocalAvatarSyncMessage AvatarHigh;
        public LocalAvatarSyncMessage AvatarMedium;
        public LocalAvatarSyncMessage AvatarLow;
        public LocalAvatarSyncMessage AvatarVeryLow;

        // Inbound sequence tracking for unreliable client→server packets
        public byte LastInboundSequence;
        public bool HasReceivedFirst;

        // Outbound sequence stamped into pre-serialized data (increments per new avatar update)
        public byte OutboundSequence;

        // Pre-serialized keyframe bytes per quality [PlayerID:2][interval_placeholder:1][sequence:1][quality:1][array:N][additionalSize:1]
        // The interval byte at offset 2 is filled per-recipient in the send loop.
        public byte[][] SerializedKeyframe = new byte[4][];
        public int[] SerializedKeyframeLength = new int[4];
    }

    public partial class BasisServerReductionSystemEvents
    {
        private static readonly CancellationTokenSource cts = new();
        // Initial capacity for PeerTracking array on PlayerState.
        // Grows if a player ID exceeds this.
        private const int InitialPlayerArrayCapacity = 2048;

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

        // Maintained incrementally via ProcessMessage/ProcessPendingRemovals instead of rebuilt every tick.
        private static readonly List<(int id, PlayerState state)> _activePlayers = new();
        private static readonly object _activePlayersLock = new();
        private static (int id, PlayerState state)[] _activePlayersSnapshot = Array.Empty<(int, PlayerState)>();
        private static volatile bool _activePlayersDirty = false;

        private static readonly ConcurrentQueue<int> playersToRemove = new();

        // Reusable snapshot list for draining currentMessages each tick — avoids allocation per tick.
        private static readonly List<QueuedMessage> _messagesSnapshot = new(1024);

        // Distance -> Quality thresholds (squared meters)
        public static float HighDistanceSq = 9f;        // 3m
        public static float MediumDistanceSq = 100f;    // 10m
        public static float LowDistanceSq = 400f;       // 20m

        // Tick slicing: only process a subset of receivers each tick to spread the O(N²) work.
        // Adaptive: increases when ticks take too long, decreases when under budget.
        private static int _sliceCount = 1;
        private static int _sliceIndex = 0;

        // Thread-local NetDataWriter for serialization — eliminates shared pool contention.
        [ThreadStatic]
        private static NetDataWriter t_serializeWriter;

        // Cached muscle+tail byte counts for the position-only fast path (skip repack).
        private static readonly int HighMuscleAndTailBytes = MuscleBytes(BitQuality.High) + TailBytes;

        static BasisServerReductionSystemEvents()
        {
            _ = StartBackgroundProcessingAsync();
        }

        public static void HandleAvatarMovement(NetPacketReader reader, NetPeer fromPeer)
        {
            // Read the application-level sequence byte prepended by the client
            if (!reader.TryGetByte(out byte sequence))
            {
                reader.Recycle();
                return;
            }
            var localMessage = new LocalAvatarSyncMessage();
            localMessage.Deserialize(reader);
            reader.Recycle();

            if (localMessage.array == null)
            {
                BNL.LogError($"[HandleAvatarMovement] Deserialized avatar message has null array from peer {fromPeer.Id}");
                return;
            }

            AddMessage(fromPeer, localMessage, sequence);
        }

        public static void AddMessage(NetPeer fromPeer, LocalAvatarSyncMessage localMessage, byte sequence)
        {
            var message = QueuedMessagePool.Rent();
            message.FromPeer = fromPeer;
            message.Sequence = sequence;
            message.AvatarMessage = localMessage;

            // Simple overwrite — stale detection is handled in ProcessMessage via LastInboundSequence.
            // Avoid pool operations inside AddOrUpdate factories (factory can be called multiple times
            // under contention, causing double-return / use-after-return).
            currentMessages.AddOrUpdate(fromPeer.Id, message, (_, _) => message);
        }

        private static async Task StartBackgroundProcessingAsync()
        {
            long intervalMs = 2;

            while (!cts.Token.IsCancellationRequested)
            {
                long startTick = Stopwatch.GetTimestamp();

                // Snapshot messages safely — reuse list to avoid allocation
                _messagesSnapshot.Clear();
                foreach (var kvp in currentMessages)
                {
                    if (currentMessages.TryRemove(kvp.Key, out var msg))
                    {
                        _messagesSnapshot.Add(msg);
                    }
                }

                // Process messages (also adds players)
                Parallel.ForEach(_messagesSnapshot, parallelOptions, msg =>
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
                long now = Stopwatch.GetTimestamp();
                UpdateCommunicationAndDistances(now);
                BasisNetworkPIPCamera.UpdatePIPPositions(now);

                if (NetworkServer.Server != null && NetworkServer.Server.manager != null)
                {
                    NetworkServer.Server.manager.TriggerUpdate();
                }

                // Throttle loop if under time budget
                long elapsedTicks = Stopwatch.GetTimestamp() - startTick;
                double elapsedMs = elapsedTicks / MsToTick;
                long remainingMs = intervalMs - (long)elapsedMs;

                // Adaptive slice count: if tick took > 1.5ms, increase slicing; if < 0.5ms, decrease.
                if (elapsedMs > 1.5 && _sliceCount < 32)
                    _sliceCount++;
                else if (elapsedMs < 0.5 && _sliceCount > 1)
                    _sliceCount--;

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

                    // Remove from active players list
                    lock (_activePlayersLock)
                    {
                        for (int i = _activePlayers.Count - 1; i >= 0; i--)
                        {
                            if (_activePlayers[i].id == id)
                            {
                                _activePlayers.RemoveAt(i);
                                _activePlayersDirty = true;
                                break;
                            }
                        }
                    }


                    // Clear stale per-player tracking data for the removed ID across all remaining players.
                    // Without this, when a new player reuses this ID, other players LastSeenGeneration
                    // would still hold the old (high) generation value, causing the new-data check
                    // (senderGen > seenGens[jId]) to fail -- no data would be sent for the new player.
                    foreach (var kvp in playerStates)
                    {
                        var otherState = kvp.Value;
                        if (id < otherState.PeerTracking.Length)
                        {
                            otherState.PeerTracking[id] = default;
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
            // Double-buffered snapshot: only rebuild when dirty
            if (_activePlayersDirty)
            {
                lock (_activePlayersLock)
                {
                    if (_activePlayersDirty)
                    {
                        _activePlayersSnapshot = _activePlayers.ToArray();
                        _activePlayersDirty = false;
                    }
                }
            }
            var activeCopy = _activePlayersSnapshot;

            int playerCount = activeCopy.Length;
            if (playerCount == 0) return;

            // Tick slicing: only process a slice of receivers per tick
            int sliceSize = (playerCount + _sliceCount - 1) / _sliceCount;
            int start = _sliceIndex * sliceSize;
            int end = Math.Min(start + sliceSize, playerCount);
            _sliceIndex = (_sliceIndex + 1) % _sliceCount;

            if (start >= playerCount) return;

            // Create a span/segment view for the slice to iterate
            Parallel.For(start, end, parallelOptions, i =>
            {
                var playerI = activeCopy[i];
                var stateI = playerI.state;
                var peer = stateI.Peer;

                // Congestion check — aggregate across all per-quality avatar channels
                int queueDepth = 0;
                for (int ch = 0; ch < BasisNetworkCommons.PlayerAvatarQualityChannels.Length; ch++)
                    queueDepth += peer.GetPacketsCountInQueue(BasisNetworkCommons.PlayerAvatarQualityChannels[ch], DeliveryMethod.Unreliable);

                // Severe congestion — skip this peer entirely
                if (queueDepth > 512) return;

                // Graduated quality cap based on congestion
                int maxQi;
                int intervalMultiplier;
                if (queueDepth > 256)
                {
                    maxQi = 0;            // force VeryLow
                    intervalMultiplier = 2; // double intervals
                }
                else if (queueDepth > 128)
                {
                    maxQi = 1;            // cap at Low
                    intervalMultiplier = 1;
                }
                else if (queueDepth > 64)
                {
                    maxQi = 2;            // cap at Medium
                    intervalMultiplier = 1;
                }
                else
                {
                    maxQi = 3;            // normal quality
                    intervalMultiplier = 1;
                }

                var tracking = stateI.PeerTracking;
                if (tracking == null) return;

                for (int index = 0; index < playerCount; index++)
                {
                    var playerJ = activeCopy[index];
                    if (playerI.id == playerJ.id)
                    {
                        continue;
                    }

                    var stateJ = playerJ.state;
                    int jId = playerJ.id;

                    // Bounds check — grow array if needed (rare, only when IDs exceed capacity)
                    if (jId >= tracking.Length)
                    {
                        lock (stateI)
                        {
                            if (jId >= stateI.PeerTracking.Length)
                            {
                                int newLen = Math.Max(stateI.PeerTracking.Length * 2, jId + 1);
                                Array.Resize(ref stateI.PeerTracking, newLen);
                            }
                            tracking = stateI.PeerTracking;
                        }
                    }

                    float distSq = DistanceSquared(stateI.Position, stateJ.Position);

                    CalculateIntervalFromDistanceSq(distSq, out byte startAtZeroInterval, out int actualInterval);

                    long lastSent = tracking[jId].LastSentTime;
                    long elapsed = nowTicks - lastSent;
                    if (elapsed < 0) elapsed = 0;

                    long required = (long)((actualInterval * intervalMultiplier) * MsToTick);

                    // Generation-based new data check: compare sender's generation with what we last saw
                    long senderGen = Interlocked.Read(ref stateJ.DataGeneration);
                    bool hasNewData = senderGen > tracking[jId].LastSeenGeneration;

                    if (hasNewData && elapsed >= required)
                    {
                        // Pick quality by distance, capped by congestion
                        int qi = Math.Min(GetQualityIndex(distSq), maxQi);

                        // Route to quality + additional-data-specific channel (cached bool, no dereference)
                        byte avatarChannel = BasisNetworkCommons.GetPlayerAvatarChannelForQuality(qi, stateJ.HasAdditionalData);
                        SendPreSerialized(peer, qi, startAtZeroInterval,
                            avatarChannel,
                            stateJ.SerializedKeyframe, stateJ.SerializedKeyframeLength);

                        tracking[jId].LastSentTime = nowTicks;
                        tracking[jId].LastSeenGeneration = senderGen;
                    }
                }
            });
        }

        /// <summary>
        /// Sends a pre-serialized message, patching the interval byte at offset 2.
        /// Uses a thread-local writer to avoid shared pool contention.
        /// </summary>
        private static void SendPreSerialized(NetPeer peer, int qi,byte interval, byte channel, byte[][] serializedArray, int[] lengthArray)
        {
            int len = lengthArray[qi];
            byte[] src = serializedArray[qi];
            if (src == null || len == 0)
                return;

            NetDataWriter writer = GetThreadWriter();
            writer.Put(src, 0, len);

            // Patch interval byte at offset 2 (after PlayerID ushort)
            if (writer.Length > 2)
            {
                writer.Data[2] = interval;
            }

            peer.Send(writer, channel, DeliveryMethod.Unreliable);
            BasisNetworkStatistics.RecordOutbound(channel, writer.Length);
        }

        /// <summary>
        /// Returns a thread-local NetDataWriter, creating one if needed.
        /// Avoids contention on the shared ConcurrentQueue writer pool.
        /// </summary>
        private static NetDataWriter GetThreadWriter()
        {
            var w = t_serializeWriter;
            if (w == null)
            {
                w = new NetDataWriter(true, 512);
                t_serializeWriter = w;
            }
            else
            {
                w.Reset();
            }
            return w;
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

        /// <summary>
        /// Propagates AdditionalAvatarData from the high quality message to lower quality variants.
        /// BuildAllLowerFromHighInto only handles the muscle/position/rotation payload;
        /// additional data (blendshapes, custom avatar behaviours) must be propagated separately.
        /// VeryLow quality strips additional data entirely — face/detail data is invisible at 20m+.
        /// </summary>
        private static void PropagateAdditionalData(
            in LocalAvatarSyncMessage high,
            ref LocalAvatarSyncMessage medium,
            ref LocalAvatarSyncMessage low,
            ref LocalAvatarSyncMessage veryLow)
        {
            medium.AdditionalAvatarDatas = high.AdditionalAvatarDatas;
            medium.AdditionalAvatarDataSize = high.AdditionalAvatarDataSize;
            medium.LinkedAvatarIndex = high.LinkedAvatarIndex;

            low.AdditionalAvatarDatas = high.AdditionalAvatarDatas;
            low.AdditionalAvatarDataSize = high.AdditionalAvatarDataSize;
            low.LinkedAvatarIndex = high.LinkedAvatarIndex;

            veryLow.AdditionalAvatarDatas = high.AdditionalAvatarDatas;
            veryLow.AdditionalAvatarDataSize = high.AdditionalAvatarDataSize;
            veryLow.LinkedAvatarIndex = high.LinkedAvatarIndex;
        }
        private static void ProcessMessage(QueuedMessage message)
        {
            int id = message.FromPeer.Id;
            byte inboundSeq = message.Sequence;

            var high = message.AvatarMessage;

            if (high.array == null)
            {
                BNL.LogError($"[ProcessMessage] Avatar array is null for peer {id}");
                QueuedMessagePool.Return(message);
                return;
            }

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
                    SyncMessage = new ServerSideSyncPlayerMessage
                    {
                        playerIdMessage = new PlayerIdMessage { playerID = (ushort)id },
                        avatarSerialization = high
                    },
                    AvatarHigh = high,
                    PeerTracking = new PeerTrackingData[InitialPlayerArrayCapacity],
                    DataGeneration = 1,
                    LastInboundSequence = inboundSeq,
                    HasReceivedFirst = true,
                    OutboundSequence = 0,
                };

                // Build lower qualities using the zero-alloc Into variant.
                // First call allocates the arrays; subsequent calls reuse them.
                try
                {
                    AvatarQualityRepacker.BuildAllLowerFromHighInto(high, ref state.AvatarMedium, ref state.AvatarLow, ref state.AvatarVeryLow);
                }
                catch (Exception ex)
                {
                    BNL.LogError($"[ProcessMessage] Repack failed: {ex}");
                    state.AvatarMedium = high;
                    state.AvatarLow = high;
                    state.AvatarVeryLow = high;
                }

                // Propagate additional avatar data (e.g. blendshapes) to quality variants.
                // BuildAllLowerFromHighInto only handles muscle/position payload;
                // additional data must be copied separately.
                PropagateAdditionalData(high, ref state.AvatarMedium, ref state.AvatarLow, ref state.AvatarVeryLow);
                state.HasAdditionalData = high.AdditionalAvatarDatas != null && high.AdditionalAvatarDatas.Length > 0;

                // First frame: pre-serialize
                PreSerializeAll(state);

                playerStates[id] = state;

                // Add to active players list
                lock (_activePlayersLock)
                {
                    _activePlayers.Add((id, state));
                    _activePlayersDirty = true;
                }
            }
            else
            {
                // Drop stale inbound packets (unreliable can deliver out of order)
                if (state.HasReceivedFirst)
                {
                    byte delta = unchecked((byte)(inboundSeq - state.LastInboundSequence));
                    if (delta == 0 || delta >= 128)
                    {
                        // Duplicate or stale — discard
                        QueuedMessagePool.Return(message);
                        return;
                    }
                }
                state.LastInboundSequence = inboundSeq;
                state.HasReceivedFirst = true;

                if (!state.IsActive)
                {
                    state.IsActive = true;
                }

                state.Position = pos;

                // Increment outbound sequence for this sender's new update
                unchecked { state.OutboundSequence++; }

                // Check if muscles+tail changed (skip expensive bit repacking if only position moved).
                // Muscle+tail region starts at WritePosition (12) and runs to end of array.
                byte[] prevArray = state.AvatarHigh.array;
                bool musclesOrTailChanged = prevArray == null
                    || prevArray.Length != high.array.Length
                    || !high.array.AsSpan(WritePosition, HighMuscleAndTailBytes)
                        .SequenceEqual(prevArray.AsSpan(WritePosition, HighMuscleAndTailBytes));

                // Update high quality
                state.AvatarHigh = high;

                if (musclesOrTailChanged)
                {
                    // Full repack needed — muscles or rotation/scale changed
                    try
                    {
                        AvatarQualityRepacker.BuildAllLowerFromHighInto(high, ref state.AvatarMedium, ref state.AvatarLow, ref state.AvatarVeryLow);
                    }
                    catch (Exception ex)
                    {
                        BNL.LogError($"[ProcessMessage] Repack failed: {ex}");
                        state.AvatarMedium = high;
                        state.AvatarLow = high;
                        state.AvatarVeryLow = high;
                    }
                }
                else
                {
                    // Position-only change — just copy position bytes to lower quality arrays.
                    // Muscles and tail are unchanged, no bit repacking needed.
                    if (state.AvatarMedium.array != null)
                        Buffer.BlockCopy(high.array, 0, state.AvatarMedium.array, 0, WritePosition);
                    if (state.AvatarLow.array != null)
                        Buffer.BlockCopy(high.array, 0, state.AvatarLow.array, 0, WritePosition);
                    if (state.AvatarVeryLow.array != null)
                        Buffer.BlockCopy(high.array, 0, state.AvatarVeryLow.array, 0, WritePosition);
                }

                // Propagate additional avatar data to quality variants
                PropagateAdditionalData(high, ref state.AvatarMedium, ref state.AvatarLow, ref state.AvatarVeryLow);
                state.HasAdditionalData = high.AdditionalAvatarDatas != null && high.AdditionalAvatarDatas.Length > 0;

                // Keep SyncMessage in sync (shell)
                state.SyncMessage.avatarSerialization = high;

                PreSerializeAll(state);

                // Single atomic increment replaces O(N) CAS bit-setting across all other players.
                // Receivers detect new data by comparing this generation against their LastSeenGeneration.
                Interlocked.Increment(ref state.DataGeneration);
            }

            QueuedMessagePool.Return(message);
        }

        #region Pre-serialization

        /// <summary>
        /// Pre-serializes keyframe messages for all 4 quality levels.
        /// The interval byte (offset 2) is left as 0 and patched per-recipient during send.
        /// Uses thread-local writers to avoid shared pool contention.
        /// </summary>
        private static void PreSerializeAll(PlayerState state)
        {
            ushort playerId = state.SyncMessage.playerIdMessage.playerID;

            PreSerializeKeyframe(state, 0, state.AvatarVeryLow, playerId);
            PreSerializeKeyframe(state, 1, state.AvatarLow, playerId);
            PreSerializeKeyframe(state, 2, state.AvatarMedium, playerId);
            PreSerializeKeyframe(state, 3, state.AvatarHigh, playerId);
        }

        private static void PreSerializeKeyframe(PlayerState state, int qi, LocalAvatarSyncMessage msg, ushort playerId)
        {
            if (msg.array == null)
            {
                return;
            }

            var quality = (BitQuality)msg.DataQualityLevel;
            int expectedPayload = BasisAvatarBitPacking.ConvertToSize(quality);

            // Skip if the array is undersized (e.g. client sent wrong quality level)
            if (msg.array.Length < expectedPayload)
            {
                BNL.LogError($"[PreSerializeKeyframe] Array undersized for quality {quality}: got {msg.array.Length}, need {expectedPayload}. Skipping.");
                return;
            }

            // [PlayerID:2][interval:1][sequence:1][quality:1][array:N][additionalSize:1][additional...]
            int additionalSize = 0;
            if (msg.AdditionalAvatarDatas != null && msg.AdditionalAvatarDatas.Length > 0)
            {
                additionalSize = 1; // LinkedAvatarIndex
                for (int i = 0; i < msg.AdditionalAvatarDatas.Length; i++)
                {
                    additionalSize += 1 + 1 + (msg.AdditionalAvatarDatas[i].array?.Length ?? 0); // PayloadSize + messageIndex + data
                }
            }

            int totalSize = 2 + 1 + 1 + 1 + expectedPayload + 1 + additionalSize;

            if (state.SerializedKeyframe[qi] == null || state.SerializedKeyframe[qi].Length < totalSize)
            {
                state.SerializedKeyframe[qi] = new byte[totalSize];
            }

            NetDataWriter writer = GetThreadWriter();
            writer.Put(playerId);
            writer.Put((byte)0); // interval placeholder
            writer.Put(state.OutboundSequence); // sequence byte
            writer.Put(msg.DataQualityLevel);
            writer.Put(msg.array, 0, expectedPayload);

            // Additional avatar data (from current msg)
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
            Buffer.BlockCopy(writer.Data, 0, state.SerializedKeyframe[qi], 0, written);
            state.SerializedKeyframeLength[qi] = written;
        }

        #endregion
    }
}
