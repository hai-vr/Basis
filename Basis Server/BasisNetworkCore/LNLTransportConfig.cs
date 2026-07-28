using System;

namespace Basis.Network.Core
{
    [Serializable]
    public sealed class LNLTransportConfig
    {
        /// <summary>Bump to force existing files to be rewritten; newly-added fields are healed automatically on load.</summary>
        public const int CurrentConfigVersion = 5;
        /// <summary>Schema version stamped into the file; 0 = pre-versioning, upgraded on load.</summary>
        public int ConfigVersion = 0;

        public bool UseNativeSockets = true;
        public bool NatPunchEnabled = true;
        public int NatPortPredictionRange = 32;
        public int PingInterval = 1500;
        public int DisconnectTimeout = 30000;
        public bool SimulatePacketLoss = false;
        public bool SimulateLatency = false;
        public int SimulationPacketLossChance = 10;
        public int SimulationMinLatency = 50;
        public int SimulationMaxLatency = 150;
        public int ReconnectDelay = 500;
        public int MaxConnectAttempts = 10;
        public bool ReuseAddresss = false;
        public bool DontRoute = false;
        public bool IPv6Enabled = true;
        public int MtuOverride = 0;
        public bool MtuDiscovery = true;
        public bool DisconnectOnUnreachable = false;
        public bool AllowPeerAddressChange = true;
        public int MultiSocketCount = 1;
        public int PacketPoolSizePerPeer = 48;
        public int PacketPoolSizeMax = 262144;

        /// <summary>
        /// How long (ms) a partly-filled merge buffer may wait for more data before being sent.
        ///
        /// The logic loop used to flush every peer's merge buffer on every pass, so at a few
        /// hundred passes a second most datagrams left less than half the MTU used and the server
        /// paid full per-packet cost for them. Holding a partial buffer briefly lets consecutive
        /// passes coalesce into one datagram.
        ///
        /// A full buffer is always sent immediately, so this only ever delays small sends, and it
        /// is a ceiling rather than an added delay — traffic that already fills the MTU is
        /// unaffected. 0 restores the old flush-every-pass behaviour.
        ///
        /// Measured at 500 players, same bytes on the wire throughout: 0 ms = 175K datagrams/s
        /// (~670 B each, under half the MTU), 3 ms = 147K (-16%), 8 ms = 96K (-45%, ~1125 B each).
        /// 3 is the default because it is most of the packet-rate win for a delay well under one
        /// avatar send interval; raise it toward 8 if packet rate matters more than voice latency.
        /// </summary>
        public float MergeHoldMs = 3f;

        /// <summary>
        /// Worker cap for the transport's per-peer update pass. 0 = a quarter of the cores,
        /// floored at 4 and capped at 8.
        ///
        /// This pass runs hundreds of times a second and does little work per peer, so letting it
        /// spread across every core costs far more in thread wake-up and GC-poll traffic than it
        /// saves. Profiling a 500-player server found 40 threads in this pass, three quarters of
        /// all GC-poll time coming from the parallel machinery itself rather than the work.
        /// </summary>
        public int PeerUpdateParallelism = 0;

        /// <summary>
        /// Maximum unreliable packets queued per peer before the oldest are dropped. 0 = unbounded.
        ///
        /// This is the backstop that keeps an overloaded server alive. Unbounded, a server that
        /// cannot drain its send queue grows the backlog instead — measured at 2000 players, the
        /// queue reached ~40 GB before every peer timed out and the instance was lost. Bounded, the
        /// same overload costs dropped position updates and everyone stays connected.
        /// </summary>
        public int MaxUnreliableQueuePerPeer = 256;
    }
}
