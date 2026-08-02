using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Basis.Scripts.Networking.Sync
{
    /// <summary>
    /// Nearest-observer distance (and, for relevance-culled objects, the in-radius player set) for
    /// every locally-owned synced object, in one parallel pass.
    ///
    /// This used to run per object, inside TransmitIfDue: each object walked
    /// BasisNetworkPlayers.ReceiversSnapshot calling GetLatestNetworkPose. That made the POSE
    /// GATHER itself O(objects x players) — a thousand props each re-reading the same forty
    /// players — on top of the distance math. The driver now gathers each player's position once
    /// and this job does the cross product in Burst.
    ///
    /// The recipient set is emitted as a bitmask over player slots rather than a ushort[] per
    /// object: jobs cannot allocate, and the mask is a fixed small stride the driver expands only
    /// for the objects that actually cull.
    /// </summary>
    [BurstCompile]
    public struct BasisSyncReductionJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> ObjectPositions;

        /// <summary>
        /// Squared relevance radius per object, or NEGATIVE when the object does not cull (no mask
        /// written). Negative rather than zero because a radius of exactly 0 is a legal culling
        /// setting meaning "nobody", and it must still clear the mask rather than leave the
        /// previous pass's bits for the driver to expand.
        /// </summary>
        [ReadOnly] public NativeArray<float> RelevanceRadiusSq;

        [ReadOnly] public NativeArray<float3> PlayerPositions;

        /// <summary>Gathered (non-null) player count. Zero leaves NearestSq at float.MaxValue; the driver applies the empty-instance rule.</summary>
        public int PlayerCount;

        /// <summary>ulongs of mask per object — ceil(PlayerCount / 64), at least 1.</summary>
        public int MasksPerObject;

        [WriteOnly] public NativeArray<float> NearestSq;

        // Each index writes its own contiguous [i * MasksPerObject, +MasksPerObject) span, so the
        // ranges are disjoint — but they are not index i, which is what the safety system checks.
        [NativeDisableParallelForRestriction] public NativeArray<ulong> RelevanceMask;

        public void Execute(int i)
        {
            float3 objectPosition = ObjectPositions[i];
            float radiusSq = RelevanceRadiusSq[i];
            bool cull = radiusSq >= 0f;
            int maskBase = i * MasksPerObject;

            if (cull)
            {
                for (int m = 0; m < MasksPerObject; m++)
                {
                    RelevanceMask[maskBase + m] = 0ul;
                }
            }

            float nearest = float.MaxValue;
            for (int p = 0; p < PlayerCount; p++)
            {
                float d2 = math.lengthsq(PlayerPositions[p] - objectPosition);
                if (d2 < nearest)
                {
                    nearest = d2;
                }
                if (cull && d2 <= radiusSq)
                {
                    RelevanceMask[maskBase + (p >> 6)] |= 1ul << (p & 63);
                }
            }

            NearestSq[i] = nearest;
        }
    }
}
