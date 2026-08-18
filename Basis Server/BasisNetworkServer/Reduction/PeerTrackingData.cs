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
    public struct PeerTrackingData
    {
        public long LastSentTime;
        public long LastSeenGeneration;
        // Delta baseline tracking: the sender-keyframe generation + quality this receiver was last
        // sent a keyframe for. A delta is only sent when these match the sender's current keyframe;
        // otherwise the receiver is (re)sent a keyframe first. Reset to 0/default on peer removal.
        public long BaselineKeyframeGen;
        // Cached by the slow distance loop, read by the fast send loop (~250Hz). Eliminates per-pair
        // distance math from the hot path. int, not long: this is an interval, and Stopwatch ticks
        // for even a 200-second interval fit comfortably — the real values are tens of milliseconds.
        public int CachedIntervalTicks;
        public byte CachedQualityIndex;
        public byte CachedIntervalByte;
        public byte BaselineQuality;
    }
}
