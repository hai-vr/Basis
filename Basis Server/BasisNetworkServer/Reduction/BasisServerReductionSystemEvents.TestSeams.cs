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
    }
}
