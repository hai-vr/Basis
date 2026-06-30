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

        internal NetPacket PoolGetPacket(int size)
        {
            if (size > NetConstants.MaxPacketSize)
                return new NetPacket(size);

            if (!_pool.TryDequeue(out NetPacket packet))
                return new NetPacket(size);
            Interlocked.Decrement(ref _poolCount);

            packet.Next = null;
            packet.Size = size;
            if (packet.RawData.Length < size)
                packet.RawData = new byte[size];
            return packet;
        }

        internal void PoolRecycle(NetPacket packet)
        {
            if (packet.RawData.Length > NetConstants.MaxPacketSize ||
                Interlocked.CompareExchange(ref _poolCount, 0, 0) >= PacketPoolSize)
            {
                //Don't pool big packets. Save memory
                return;
            }

            //Clean fragmented flag
            packet.RawData[0] = 0;
            packet.Next = null;
            _pool.Enqueue(packet);
            Interlocked.Increment(ref _poolCount);
        }
    }
}
