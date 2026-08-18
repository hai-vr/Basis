using Basis.Network.Core;
using Basis.Network.Core.Compression;
using BasisNetworkServer.BasisNetworking;
using K4os.Compression.LZ4;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static SerializableBasis;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;

using Basis.Network.Core;
using Basis.Network.Core.Compute;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public partial class BasisServerReductionSystemEvents
    {
        // Cached muscle+tail byte counts for the position-only fast path (skip repack).
        private static readonly int HighMuscleAndTailBytes = MuscleBytes(BitQuality.High) + TailBytes;

        // Generation snapshot: populated once per tick before the O(N²) send loop.
        // Eliminates Interlocked.Read per pair (N² memory fences → N).
        // Pre-allocated to InitialPlayerArrayCapacity to avoid reallocation on early player joins.
        private static long[] _generationSnapshot = new long[InitialPlayerArrayCapacity];

        // Position snapshots: contiguous arrays for cache-friendly reads in the inner loop.
        // Avoids pointer-chasing through scattered heap PlayerState objects per pair.
        private static float[] _posXSnapshot = new float[InitialPlayerArrayCapacity];
        private static float[] _posYSnapshot = new float[InitialPlayerArrayCapacity];
        private static float[] _posZSnapshot = new float[InitialPlayerArrayCapacity];

        // Roster-order copies of the same positions. The snapshots above are indexed by peer id and
        // are therefore full of holes; a device transfer would pay for every one of them, so the
        // offload path keeps its own dense copy rather than sending a mostly-empty array.
        private static float[] _denseX = new float[InitialPlayerArrayCapacity];
        private static float[] _denseY = new float[InitialPlayerArrayCapacity];
        private static float[] _denseZ = new float[InitialPlayerArrayCapacity];

        private static byte[] _deviceIntervalByte = Array.Empty<byte>();
        private static byte[] _deviceQuality = Array.Empty<byte>();

        // CachedIntervalTicks for every interval byte, so the device never has to send it.
        private static int[] _intervalTickTable;

        private static IBasisDistanceSolver _distanceSolver;
        private static bool _distanceSolverTried;
        private static bool _distanceSolverVerified;

        public static bool EnableComputeOffload = true;

        /// <summary>Which device to use when the host has more than one. Empty picks the best.</summary>
        public static string ComputeDevice = "";

        /// <summary>
        /// Refresh period to use while a device is carrying the sweep. Read through
        /// <see cref="EffectiveDistanceIntervalTicks"/>, never directly, so that losing the backend
        /// mid-run puts the period back rather than leaving the CPU on a schedule it cannot meet.
        /// </summary>
        public static int ComputeDistanceUpdateIntervalTicks = 32;

        /// <summary>
        /// The period actually in force this tick.
        ///
        /// <para>A cheaper sweep is worth almost nothing as saved CPU and quite a lot as reduced
        /// staleness, so the device's whole return is taken here rather than in cores. It is keyed
        /// off the live solver rather than off configuration, which is what makes the fallback
        /// automatic: the moment the backend is refused, this reads the CPU period again.</para>
        /// </summary>
        private static int EffectiveDistanceIntervalTicks =>
            _distanceSolver != null ? ComputeDistanceUpdateIntervalTicks : DistanceUpdateIntervalTicks;

        /// <summary>Which backend the sweep is running on, for the boot log.</summary>
        public static string DistanceBackend { get; private set; } = "cpu";

        private static bool UpdateDistanceCacheSlice()
        {
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
            if (activeCopy.Length == 0)
            {
                _distanceSliceCursor = 0;
                _distanceSweepRoster = Array.Empty<(int, PlayerState)>();
                return false;
            }

            // Between sweeps: the refresh period is still DistanceUpdateIntervalTicks, we just do the
            // work in chunks rather than all on one tick.
            if (_distanceSliceCursor == 0 && ++_distanceTickCounter < EffectiveDistanceIntervalTicks)
            {
                return false;
            }

            // ⚠️ Pin the roster for the whole sweep instead of re-reading _activePlayersSnapshot each
            // slice. SnapshotPositions sizes the position arrays from this array's peer ids and runs
            // only on the first slice, so a player who joined mid-sweep with an id above the
            // sweep-start maximum indexed past those arrays — an IndexOutOfRangeException thrown
            // inside the Parallel.For in RunDistanceSlice, which takes down the whole tick. Pinning
            // also puts the roster on the same single frame the positions were already documented to
            // use; a mid-sweep joiner is picked up by the next sweep, which is exactly the case the
            // send path's "not yet in the distance cache" fallback interval already covers.
            if (_distanceSliceCursor == 0)
            {
                _distanceSweepRoster = activeCopy;
            }
            activeCopy = _distanceSweepRoster;
            int playerCount = activeCopy.Length;

            // ⚠️ Slice size is bounded BELOW for a reason. This work is a Parallel.For over receivers,
            // so a slice must carry enough receivers to be worth dispatching — sizing it as
            // playerCount/interval gives ~8 at 1000 players, and the parallel setup then costs far
            // more than the distance math it is scheduling. Measured: the naive split made the whole
            // phase ~30x more expensive than the periodic full sweep it replaced. Larger chunks mean
            // the sweep finishes early and simply idles until the next period, which is the point.
            int interval = EffectiveDistanceIntervalTicks;
            int perTick = Math.Max(MinDistanceSliceReceivers,
                (playerCount + interval - 1) / interval);

            if (_distanceSliceCursor >= playerCount)
            {
                _distanceSliceCursor = 0;
            }
            int sliceStart = _distanceSliceCursor;
            int sliceEnd = Math.Min(sliceStart + perTick, playerCount);
            if (sliceEnd >= playerCount)
            {
                // Sweep complete — restart the period counter.
                _distanceSliceCursor = 0;
                _distanceTickCounter = 0;
            }
            else
            {
                _distanceSliceCursor = sliceEnd;
            }

            // Positions are re-snapshotted only when starting a fresh sweep, so every receiver in
            // one sweep is measured against the same frame rather than a moving target.
            if (sliceStart == 0)
            {
                SnapshotPositions(activeCopy, playerCount);
                EnsureDistanceSolver();
                if (_distanceSolver != null) SnapshotDensePositions(activeCopy, playerCount);
            }

            if (!TryRunDistanceSliceOnDevice(activeCopy, playerCount, sliceStart, sliceEnd))
            {
                RunDistanceSlice(activeCopy, playerCount, sliceStart, sliceEnd);
            }
            return true;
        }

        private static void SnapshotPositions((int id, PlayerState state)[] activeCopy, int playerCount)
        {

            // Snapshot positions into contiguous arrays for cache-friendly distance math.
            int maxId = 0;
            for (int i = 0; i < playerCount; i++)
            {
                if (activeCopy[i].id > maxId) maxId = activeCopy[i].id;
            }
            int snapshotLen = maxId + 1;
            if (_posXSnapshot.Length < snapshotLen)
            {
                int newLen = Math.Max(snapshotLen, _posXSnapshot.Length * 2);
                _posXSnapshot = new float[newLen];
                _posYSnapshot = new float[newLen];
                _posZSnapshot = new float[newLen];
            }
            for (int i = 0; i < playerCount; i++)
            {
                int id = activeCopy[i].id;
                var state = activeCopy[i].state;
                _posXSnapshot[id] = state.Position.x;
                _posYSnapshot[id] = state.Position.y;
                _posZSnapshot[id] = state.Position.z;
            }
        }

        private static void RunDistanceSlice((int id, PlayerState state)[] activeCopy, int playerCount, int sliceStart, int sliceEnd)
        {
            Parallel.For(sliceStart, sliceEnd, parallelOptions, i =>
            {
                var (id, state) = activeCopy[i];
                var tracking = state.PeerTracking;
                if (tracking == null) return;

                float iX = _posXSnapshot[id];
                float iY = _posYSnapshot[id];
                float iZ = _posZSnapshot[id];

                for (int index = 0; index < playerCount; index++)
                {
                    int jId = activeCopy[index].id;
                    if (id == jId) continue;

                    // Grow tracking array if needed (same logic as send loop)
                    if (jId >= tracking.Length)
                    {
                        lock (state)
                        {
                            if (jId >= state.PeerTracking.Length)
                            {
                                int newLen = Math.Max(state.PeerTracking.Length * 2, jId + 1);
                                Array.Resize(ref state.PeerTracking, newLen);
                            }
                            tracking = state.PeerTracking;
                        }
                    }

                    float dx = iX - _posXSnapshot[jId];
                    float dy = iY - _posYSnapshot[jId];
                    float dz = iZ - _posZSnapshot[jId];
                    float distSq = dx * dx + dy * dy + dz * dz;

                    CalculateIntervalFromDistanceSq(distSq, out byte intervalByte, out int actualInterval);

                    tracking[jId].CachedIntervalTicks = (int)(actualInterval * MsToTick);
                    tracking[jId].CachedQualityIndex = (byte)GetQualityIndex(distSq);
                    tracking[jId].CachedIntervalByte = intervalByte;
                }
            });
        }

        /// <summary>
        /// Resolves the compute backend once, on the first sweep that has a roster to measure.
        /// Deferred to first use because loading it is the expensive part, and a server that never
        /// reaches a population worth sweeping should never pay it.
        /// </summary>
        private static void EnsureDistanceSolver()
        {
            if (_distanceSolverTried) return;
            _distanceSolverTried = true;

            _intervalTickTable = new int[256];
            for (int b = 0; b < 256; b++)
            {
                _intervalTickTable[b] = (int)(BasisNetworkCommons.DecodeAvatarIntervalMs((byte)b, BSRSMillisecondDefaultInterval) * MsToTick);
            }

            if (!EnableComputeOffload)
            {
                DistanceBackend = "cpu (offload disabled)";
                return;
            }

            _distanceSolver = BasisComputeBackend.TryLoadDistanceSolver(BSRSMillisecondDefaultInterval, ComputeDevice);
            if (_distanceSolver == null)
            {
                DistanceBackend = "cpu";
                BNL.Log("[BSR] Distance sweep on the CPU - " + BasisComputeBackend.Status + ".");
                return;
            }

            DistanceBackend = _distanceSolver.Backend;
            BNL.Log("[BSR] Distance sweep offloaded to " + BasisComputeBackend.Status +
                    ". Refresh period " + DistanceUpdateIntervalTicks + " -> " +
                    ComputeDistanceUpdateIntervalTicks + " ticks while it holds. It is checked against " +
                    "the CPU on its first slice and dropped if it disagrees.");

            string devices = BasisComputeBackend.DescribeDevices();
            if (devices != null && devices.IndexOf("[1]", StringComparison.Ordinal) >= 0)
            {
                BNL.Log("[BSR] This host has more than one compute device. Set ComputeDevice in config.xml "
                        + "to an index or a name to choose:\n" + devices.TrimEnd());
            }
        }

        private static BasisDistanceSolveParameters CurrentSolveParameters()
        {
            return new BasisDistanceSolveParameters
            {
                HighDistanceSq = HighDistanceSq,
                MediumDistanceSq = MediumDistanceSq,
                LowDistanceSq = LowDistanceSq,
                BaseMultiplier = BSRBaseMultiplier,
                IncreaseRate = BSRSIncreaseRate,
                BaseIntervalMs = BSRSMillisecondDefaultInterval,
            };
        }

        private static void SnapshotDensePositions((int id, PlayerState state)[] activeCopy, int playerCount)
        {
            if (_denseX.Length < playerCount)
            {
                int length = Math.Max(playerCount, _denseX.Length * 2);
                _denseX = new float[length];
                _denseY = new float[length];
                _denseZ = new float[length];
            }

            for (int i = 0; i < playerCount; i++)
            {
                var position = activeCopy[i].state.Position;
                _denseX[i] = position.x;
                _denseY[i] = position.y;
                _denseZ[i] = position.z;
            }
        }

        /// <summary>
        /// Runs one slice on the device and writes the result into the same cache the CPU sweep
        /// writes. Returns false when the device could not be used, so the caller runs the CPU
        /// sweep and no slice is ever left half-updated.
        /// </summary>
        private static bool TryRunDistanceSliceOnDevice((int id, PlayerState state)[] activeCopy, int playerCount, int sliceStart, int sliceEnd)
        {
            IBasisDistanceSolver solver = _distanceSolver;
            if (solver == null) return false;

            int sliceLength = sliceEnd - sliceStart;
            if (sliceLength <= 0) return false;

            long resultLength = (long)sliceLength * playerCount;
            if (resultLength > int.MaxValue) return false;

            if (_deviceIntervalByte.Length < resultLength)
            {
                _deviceIntervalByte = new byte[resultLength];
                _deviceQuality = new byte[resultLength];
            }

            BasisDistanceSolveRequest request = new BasisDistanceSolveRequest
            {
                PosX = _denseX,
                PosY = _denseY,
                PosZ = _denseZ,
                PlayerCount = playerCount,
                SliceStart = sliceStart,
                SliceEnd = sliceEnd,
                Parameters = CurrentSolveParameters(),
            };

            try
            {
                solver.Solve(ref request, _deviceIntervalByte, _deviceQuality);
            }
            catch (Exception ex)
            {
                BNL.LogWarning("[BSR] The compute backend failed mid-sweep (" + ex.GetType().Name + ": " + ex.Message +
                               "). Dropping back to the CPU sweep for the rest of this process.");
                DisableDistanceSolver();
                return false;
            }

            if (!_distanceSolverVerified && !VerifyDeviceAgainstCpu(playerCount, sliceStart, sliceEnd))
            {
                return false;
            }

            ScatterDeviceResults(activeCopy, playerCount, sliceStart, sliceEnd);
            return true;
        }

        /// <summary>
        /// Checks the device against the CPU on the first slice it produces, and refuses it if they
        /// disagree on a quality tier.
        ///
        /// <para>The offload is on by default, so something has to establish that this driver on
        /// this card agrees with the arithmetic the rest of the server assumes. A tier disagreement
        /// means a receiver is sent the wrong avatar detail, which is a visible defect. The interval
        /// byte is allowed to differ by one step: a device that contracts the three squared terms
        /// into fused multiply-adds rounds once where the CPU rounds three times, and what that
        /// changes is a cache entry the next sweep overwrites.</para>
        /// </summary>
        private static bool VerifyDeviceAgainstCpu(int playerCount, int sliceStart, int sliceEnd)
        {
            int checkedPairs = 0;
            int intervalDrift = 0;
            int receiverStep = Math.Max(1, (sliceEnd - sliceStart) / 32);
            int senderStep = Math.Max(1, playerCount / 64);

            for (int s = sliceStart; s < sliceEnd; s += receiverStep)
            {
                int local = s - sliceStart;
                float iX = _denseX[s], iY = _denseY[s], iZ = _denseZ[s];
                long baseOffset = (long)local * playerCount;

                for (int j = 0; j < playerCount; j += senderStep)
                {
                    if (s == j) continue;

                    float dx = iX - _denseX[j];
                    float dy = iY - _denseY[j];
                    float dz = iZ - _denseZ[j];
                    float distSq = dx * dx + dy * dy + dz * dz;

                    CalculateIntervalFromDistanceSq(distSq, out byte expectedByte, out _);
                    byte expectedQuality = (byte)GetQualityIndex(distSq);

                    checkedPairs++;
                    byte deviceQuality = _deviceQuality[baseOffset + j];
                    if (deviceQuality != expectedQuality)
                    {
                        BNL.LogWarning("[BSR] The compute backend disagrees with the CPU on a quality tier (device " +
                                       deviceQuality + ", cpu " + expectedQuality + "). Refusing the offload; the " +
                                       "sweep stays on the CPU.");
                        DisableDistanceSolver();
                        return false;
                    }

                    int difference = _deviceIntervalByte[baseOffset + j] - expectedByte;
                    if (difference < -1 || difference > 1) intervalDrift++;
                }
            }

            if (intervalDrift > 0)
            {
                BNL.LogWarning("[BSR] The compute backend's interval byte differs by more than one step on " +
                               intervalDrift + " of " + checkedPairs + " sampled pairs. Refusing the offload.");
                DisableDistanceSolver();
                return false;
            }

            _distanceSolverVerified = true;
            BNL.Log("[BSR] Compute backend agrees with the CPU over " + checkedPairs + " sampled pairs.");
            return true;
        }

        private static void DisableDistanceSolver()
        {
            try
            {
                if (_distanceSolver != null) _distanceSolver.Dispose();
            }
            catch (Exception)
            {
                // A backend that is already broken enough to be refused is allowed to fail on the
                // way out too; there is nothing left to salvage and the CPU path does not care.
            }
            _distanceSolver = null;
            DistanceBackend = "cpu (backend refused)";
            BNL.Log("[BSR] Refresh period back to " + DistanceUpdateIntervalTicks +
                    " ticks now the sweep is on the CPU again.");
        }

        private static void ScatterDeviceResults((int id, PlayerState state)[] activeCopy, int playerCount, int sliceStart, int sliceEnd)
        {
            int[] tickTable = _intervalTickTable;

            Parallel.For(sliceStart, sliceEnd, parallelOptions, i =>
            {
                var (id, state) = activeCopy[i];
                var tracking = state.PeerTracking;
                if (tracking == null) return;

                long baseOffset = (long)(i - sliceStart) * playerCount;

                for (int index = 0; index < playerCount; index++)
                {
                    int jId = activeCopy[index].id;
                    if (id == jId) continue;

                    if (jId >= tracking.Length)
                    {
                        lock (state)
                        {
                            if (jId >= state.PeerTracking.Length)
                            {
                                int newLen = Math.Max(state.PeerTracking.Length * 2, jId + 1);
                                Array.Resize(ref state.PeerTracking, newLen);
                            }
                            tracking = state.PeerTracking;
                        }
                    }

                    byte encoded = _deviceIntervalByte[baseOffset + index];
                    tracking[jId].CachedIntervalTicks = tickTable[encoded];
                    tracking[jId].CachedQualityIndex = _deviceQuality[baseOffset + index];
                    tracking[jId].CachedIntervalByte = encoded;
                }
            });
        }

    }
}
