using Basis.Network.Core;
using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Threading;

namespace BasisNetworkCore
{
    public static class BasisPlayerArray
    {
        private static readonly object PlayerLock = new object();

        // Mutable list for internal tracking
        private static readonly List<NetPeer> InternalList = new List<NetPeer>(BasisNetworkCommons.MaxConnections);

        // Atomic read-only snapshot for fast reads
        private static NetPeer[] Snapshot = Array.Empty<NetPeer>();

        // Versioning for latest info
        private static int Version = 0;
        public static NetPeer[] UnsafeArrayOfNetPeers = Array.Empty<NetPeer>();
        public static void AddPlayer(NetPeer player)
        {
            if (player == null) return;

            lock (PlayerLock)
            {
                InternalList.Add(player);
                UpdateSnapshot();
                Interlocked.Increment(ref Version);
            }
        }

        public static void RemovePlayer(NetPeer player)
        {
            if (player == null) return;

            lock (PlayerLock)
            {
                InternalList.Remove(player);
                UpdateSnapshot();
                Interlocked.Increment(ref Version);
            }
        }

        private static void UpdateSnapshot()
        {
            // Replace the entire snapshot atomically
            UnsafeArrayOfNetPeers = InternalList.ToArray();
            Interlocked.Exchange(ref Snapshot, UnsafeArrayOfNetPeers);
        }

        public static ReadOnlySpan<NetPeer> GetSnapshot()
        {
            // Zero-copy, zero-lock, atomic read
            return new ReadOnlySpan<NetPeer>(Volatile.Read(ref Snapshot));
        }
    }
}
