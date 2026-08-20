using System;
using System.Numerics;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public partial class BasisServerReductionSystemEvents
    {
        internal static void TestOnly_PreSerializeFrame(PlayerState state, long publishGen, bool forceKeyframe)
            => PreSerializeFrame(state, publishGen, forceKeyframe);

        internal static int TestOnly_BuildRawForRange(PlayerState state, PendingAvatarSend[] pending, int start, int end)
            => BuildRawForRange(state, pending, start, end);

        internal static void TestOnly_SortPendingByChannel(PlayerState state, PendingAvatarSend[] pending, int count)
            => SortPendingByChannel(state, pending, count);

        internal static void TestOnly_RunDistanceSweep((int id, PlayerState state)[] roster)
        {
            SnapshotPositions(roster, roster.Length);
            RunDistanceSlice(roster, roster.Length, 0, roster.Length);
        }

#if NET10_0_OR_GREATER
        internal static void TestOnly_EncodeAvatarIntervals(int[] rawIntervals, int baseIntervalMs, int[] encoded, int[] actualMs)
        {
            int width = Vector<int>.Count;
            int[] lanes = new int[width];
            for (int i = 0; i < rawIntervals.Length; i += width)
            {
                int take = Math.Min(width, rawIntervals.Length - i);
                Array.Copy(rawIntervals, i, lanes, 0, take);
                EncodeAvatarIntervals(new Vector<int>(lanes), baseIntervalMs,
                    out Vector<int> encodedLanes, out Vector<int> actualMsLanes);
                for (int lane = 0; lane < take; lane++)
                {
                    encoded[i + lane] = encodedLanes[lane];
                    actualMs[i + lane] = actualMsLanes[lane];
                }
            }
        }
#endif
    }
}
