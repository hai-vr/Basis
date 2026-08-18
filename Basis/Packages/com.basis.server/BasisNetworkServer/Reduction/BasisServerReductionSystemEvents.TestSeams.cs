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
