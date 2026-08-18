using System;
using System.Threading.Tasks;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;

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
            if (_distanceSliceCursor == 0 && ++_distanceTickCounter < DistanceUpdateIntervalTicks)
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
            int perTick = Math.Max(MinDistanceSliceReceivers,
                (playerCount + DistanceUpdateIntervalTicks - 1) / DistanceUpdateIntervalTicks);

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
            }

            RunDistanceSlice(activeCopy, playerCount, sliceStart, sliceEnd);
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
    }
}
