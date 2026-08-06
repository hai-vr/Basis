using System;
using System.Collections.Concurrent;
using System.Threading;

namespace LiteNetLib
{
    public partial class NetManager
    {
        private readonly ConcurrentQueue<NetPacket> _pool = new ConcurrentQueue<NetPacket>();
        private int _poolCount;

        /// <summary>
        /// Per-thread packet cache, in front of the shared pool.
        ///
        /// Every get and recycle used to touch the shared queue plus an interlocked counter, and at
        /// a few hundred thousand packets a second from every sending thread that contention was
        /// measurable: 7.9% of server CPU at 2000 players sat in PoolRecycle alone. A thread that
        /// recycles a packet is very likely to want one immediately afterwards, so almost all of
        /// that traffic can be served without leaving the thread.
        ///
        /// Static rather than per-manager on purpose. A NetPacket is a plain buffer with no
        /// affinity to whichever manager pooled it, and the load tester runs thousands of managers
        /// across a few dozen threads — one hot cache per thread beats one cold queue per manager.
        /// The shared pool stays as the balancing layer, which is what makes the asymmetry work:
        /// packets are often rented on a receive thread and recycled on a send thread, so a purely
        /// thread-local scheme would drain one side and overflow the other.
        ///
        /// Uses <see cref="NetPacket.Next"/>, which is already the pool's link field, so the list
        /// itself costs no allocation.
        /// </summary>
        [ThreadStatic] private static NetPacket t_freeList;
        [ThreadStatic] private static int t_freeCount;

        /// <summary>
        /// Packets a single thread keeps to itself. Bounds the memory this adds to
        /// threads x this x MaxPacketSize — a few MB at any realistic thread count — while being
        /// deep enough to absorb a tick's worth of get/recycle churn without reaching the shared pool.
        /// </summary>
        private const int ThreadLocalPoolCap = 128;

        /// <summary>
        /// Maximum packet pool size (increase if you have tons of packets sending)
        /// </summary>
        public int PacketPoolSize = 1000;

        public int PoolCount => _poolCount;

        private NetPacket PoolGetWithData(PacketProperty property, byte[] data, int start, int length)
        {
            int headerSize = NetPacket.GetHeaderSize(property);
            NetPacket packet = PoolGetPacket(length + headerSize);
            packet.Property = property;
            Buffer.BlockCopy(data, start, packet.RawData, headerSize, length);
            return packet;
        }

        //Get packet with size
        private NetPacket PoolGetWithProperty(PacketProperty property, int size)
        {
            NetPacket packet = PoolGetPacket(size + NetPacket.GetHeaderSize(property));
            packet.Property = property;
            return packet;
        }

        private NetPacket PoolGetWithProperty(PacketProperty property)
        {
            NetPacket packet = PoolGetPacket(NetPacket.GetHeaderSize(property));
            packet.Property = property;
            return packet;
        }

        /// <summary>
        /// Packets kept per connected peer. The pool's job is to absorb the gap between a burst of
        /// sends draining it and those packets coming back, and that gap grows with the peer count —
        /// a fixed ceiling that is generous at 100 players is a wall at 3,000, where the pool swings
        /// between full (recycled packets thrown away) and empty (every get allocating). 0 disables
        /// scaling and uses <see cref="PacketPoolSize"/> alone.
        /// </summary>
        public int PacketPoolSizePerPeer = 0;

        /// <summary>Upper bound on the scaled pool so peer count can't translate into unbounded memory. 0 = no cap.</summary>
        public int PacketPoolSizeMax = 0;

        /// <summary>
        /// Cached result of <see cref="RecomputePoolCap"/>.
        ///
        /// A plain field, read once per recycle. It used to be recomputed there, which meant an
        /// interlocked read of the peer count on a cache line every sending thread was touching —
        /// several hundred thousand times a second — to obtain a number that only changes when
        /// somebody joins or leaves. Refreshed from those two events instead.
        /// </summary>
        private int _effectivePoolCap = 1000;

        /// <summary>
        /// Host hook: given the connected peer count, returns the packet pool ceiling to use.
        ///
        /// Null by default, which keeps <see cref="PacketPoolSizeMax"/> governing exactly as it did
        /// — LiteNetLib on its own has no opinion about how big the host's box is. Basis injects a
        /// resolver that sizes this against available memory instead of a shipped constant.
        /// </summary>
        public System.Func<int, int> ResolvePacketPoolMax;

        /// <summary>
        /// Host hook: given the connected peer count, returns the per-peer unreliable queue bound.
        /// Null keeps <see cref="MaxUnreliableQueuePerPeer"/>, the standalone behaviour.
        /// </summary>
        public System.Func<int, int> ResolveUnreliableQueuePerPeer;

        /// <summary>
        /// Resolved per-peer unreliable bound, refreshed by <see cref="RecomputePoolCap"/>.
        ///
        /// Cached as a plain field for the same reason <see cref="_effectivePoolCap"/> is: the
        /// enqueue path reads it on every unreliable send — millions of times a second at a few
        /// thousand players — and must not pay for a delegate call or a peer-count read there.
        /// </summary>
        public int EffectiveUnreliableQueuePerPeer = 256;

        /// <summary><see cref="PacketPoolSize"/> is the floor; peer count raises it up to the resolved ceiling.</summary>
        internal void RecomputePoolCap()
        {
            int peers = ConnectedPeersCount;

            // Both ceilings move with population, and this is the one place that already runs on
            // every join and leave — so they are refreshed together rather than each growing its
            // own trigger.
            var queueResolver = ResolveUnreliableQueuePerPeer;
            EffectiveUnreliableQueuePerPeer = queueResolver != null
                ? queueResolver(peers)
                : MaxUnreliableQueuePerPeer;

            var poolResolver = ResolvePacketPoolMax;
            int poolMax = poolResolver != null ? poolResolver(peers) : PacketPoolSizeMax;

            if (PacketPoolSizePerPeer <= 0)
            {
                _effectivePoolCap = PacketPoolSize;
                return;
            }

            long scaled = (long)peers * PacketPoolSizePerPeer;
            if (scaled < PacketPoolSize)
                scaled = PacketPoolSize;
            if (poolMax > 0 && scaled > poolMax)
                scaled = poolMax;
            _effectivePoolCap = (int)scaled;
        }

        internal NetPacket PoolGetPacket(int size)
        {
            if (size > NetConstants.MaxPacketSize)
                return new NetPacket(size);

            // This thread's own cache first — no atomics, no shared cache lines.
            NetPacket packet = t_freeList;
            if (packet != null)
            {
                t_freeList = packet.Next;
                t_freeCount--;
            }
            else
            {
                // Empty: this thread rents more than it recycles (a receive thread does exactly
                // that). Fall through to the shared pool, which is where the other side's surplus
                // ends up.
                if (!_pool.TryDequeue(out packet))
                    return new NetPacket(size);
                Interlocked.Decrement(ref _poolCount);
            }

            packet.Next = null;
            packet.Size = size;
            if (packet.RawData.Length < size)
                packet.RawData = new byte[size];
            return packet;
        }

        internal void PoolRecycle(NetPacket packet)
        {
            //Don't pool big packets. Save memory
            if (packet.RawData.Length > NetConstants.MaxPacketSize)
                return;

            //Clean fragmented flag
            packet.RawData[0] = 0;

            // Keep it on this thread if there is room. This is the common case by a wide margin —
            // a thread that recycles a packet almost always wants one again shortly.
            if (t_freeCount < ThreadLocalPoolCap)
            {
                packet.Next = t_freeList;
                t_freeList = packet;
                t_freeCount++;
                return;
            }

            // Local cache full: this thread recycles more than it rents, so hand the surplus to the
            // shared pool for the threads that are short.
            //
            // Plain reads: both are advisory here, and this runs on every recycle, so the
            // read-modify-write the interlocked form implies is pure overhead at that rate.
            if (Volatile.Read(ref _poolCount) >= _effectivePoolCap)
                return;

            packet.Next = null;
            _pool.Enqueue(packet);
            Interlocked.Increment(ref _poolCount);
        }
    }
}
