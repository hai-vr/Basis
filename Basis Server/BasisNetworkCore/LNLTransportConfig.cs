using System;

namespace Basis.Network.Core
{
    [Serializable]
    public sealed class LNLTransportConfig
    {
        /// <summary>Bump to force existing files to be rewritten; newly-added fields are healed automatically on load.</summary>
        public const int CurrentConfigVersion = 7;
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

        /// <summary>
        /// Ceiling on send sockets the server may add at runtime when the send path is the
        /// bottleneck. Linux only (needs SO_REUSEPORT); 0 or 1 disables growth.
        ///
        /// The send loop is the one part that gets slower with more threads — measured at 1000
        /// players, 8 to 16 to 32 send workers took the update phase from 6.1 to 12.9 to 15.4 ms
        /// per tick while throughput fell from 497 to 393 MB/s, because they queue on one socket.
        /// Another socket is another independent path, and the send worker ceiling is derived from
        /// how many are bound, so adding one widens both.
        ///
        /// It also scales the receive side, which is what matters most on hosts with many weak
        /// cores: one receive thread is one core's worth of syscall throughput, and past that the
        /// kernel discards inbound datagrams. That never shows up as high CPU — the thread is
        /// pinned either way — so it is read from the kernel's RcvbufErrors counter. Field reports
        /// put the useful range as high as 64 sockets on such machines.
        ///
        /// 0 = auto: half the cores, floored at four and never above the core count. A fixed
        /// default cannot be right for both a 4-vCPU container and a 128-core host of slow cores —
        /// and on that 128-core host the derivation lands on 64, which is where the field reports
        /// above put it. Nothing is capped at a number picked by hand.
        ///
        /// This is only a ceiling. Growth still has to earn each socket: sustained pressure — the
        /// pool pinned at its ceiling, the tick missing its budget, and cores still free — or
        /// receive drops, which act immediately because losing packets is worse than being busy.
        /// A socket added because of drops is then put on trial: if the drop rate does not fall,
        /// growth stops and says so, because what is losing packets is not something more receive
        /// threads can fix (an undersized net.core.rmem_max, or a saturated link).
        ///
        /// Grow-only: sockets are cheap to hold, and tearing a live receive thread down mid-flight
        /// is not.
        /// </summary>
        public int MaxSendSockets = 0;
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
        /// Peers each worker in the transport's per-peer pass is expected to service. Lower means
        /// more workers for the same population. 0 = 128.
        ///
        /// This is what decides how much of a large host the server can use. 128 was fitted to a
        /// 32-thread machine with fast cores, and it caps workers by population rather than by the
        /// machine: at 4000 peers it picks 31 workers however many cores exist, so a 128-core host
        /// sits near a quarter utilisation. Halve it to double the workers.
        ///
        /// Tune against the pass time in the [CPU] log line — over 25 ms with cores to spare means
        /// this is too high. Slower cores want a lower value, since a worker covers fewer peers.
        /// </summary>
        public int PeerUpdatePeersPerWorker = 0;

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
