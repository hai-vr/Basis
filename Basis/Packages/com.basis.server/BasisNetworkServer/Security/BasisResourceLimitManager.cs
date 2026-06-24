using Basis.Network.Core;
using System.Threading;
using static BasisNetworkCore.Serializable.SerializableBasis;

namespace BasisNetworkServer.Security
{
    /// <summary>
    /// Server-defined caps that bound per-client resource use (persistent database growth and
    /// content-share spheres). Seeded from Configuration at boot, editable live from the admin
    /// panel (gated by basis.moderation.globallock), persisted to config.xml, and broadcast so
    /// admin panels stay in sync.
    /// </summary>
    public static class BasisResourceLimitManager
    {
        private const int DefaultMaxDatabaseEntries = 10000;
        private const int DefaultMaxDatabaseNameLength = 256;
        private const int DefaultMaxDatabasePayloadEntries = 1000;
        private const int DefaultMaxContentSpheresPerPlayer = 32;

        private const int AbsoluteMaxDatabaseEntries = 1000000;
        private const int AbsoluteMaxDatabaseNameLength = 8192;
        private const int AbsoluteMaxDatabasePayloadEntries = 100000;
        private const int AbsoluteMaxContentSpheresPerPlayer = 4096;

        private static int _maxDatabaseEntries = DefaultMaxDatabaseEntries;
        private static int _maxDatabaseNameLength = DefaultMaxDatabaseNameLength;
        private static int _maxDatabasePayloadEntries = DefaultMaxDatabasePayloadEntries;
        private static int _maxContentSpheresPerPlayer = DefaultMaxContentSpheresPerPlayer;

        public static int MaxDatabaseEntries => Interlocked.CompareExchange(ref _maxDatabaseEntries, 0, 0);
        public static int MaxDatabaseNameLength => Interlocked.CompareExchange(ref _maxDatabaseNameLength, 0, 0);
        public static int MaxDatabasePayloadEntries => Interlocked.CompareExchange(ref _maxDatabasePayloadEntries, 0, 0);
        public static int MaxContentSpheresPerPlayer => Interlocked.CompareExchange(ref _maxContentSpheresPerPlayer, 0, 0);

        public static void InitializeFromConfig(Configuration config)
        {
            SetLimits(config.MaxDatabaseEntries, config.MaxDatabaseNameLength, config.MaxDatabasePayloadEntries, config.MaxContentSpheresPerPlayer);
        }

        public static bool SetLimits(int maxDatabaseEntries, int maxDatabaseNameLength, int maxDatabasePayloadEntries, int maxContentSpheresPerPlayer)
        {
            Sanitize(ref maxDatabaseEntries, ref maxDatabaseNameLength, ref maxDatabasePayloadEntries, ref maxContentSpheresPerPlayer);
            int prevEntries = Interlocked.Exchange(ref _maxDatabaseEntries, maxDatabaseEntries);
            int prevNameLength = Interlocked.Exchange(ref _maxDatabaseNameLength, maxDatabaseNameLength);
            int prevPayloadEntries = Interlocked.Exchange(ref _maxDatabasePayloadEntries, maxDatabasePayloadEntries);
            int prevSpheres = Interlocked.Exchange(ref _maxContentSpheresPerPlayer, maxContentSpheresPerPlayer);
            return prevEntries != maxDatabaseEntries
                || prevNameLength != maxDatabaseNameLength
                || prevPayloadEntries != maxDatabasePayloadEntries
                || prevSpheres != maxContentSpheresPerPlayer;
        }

        public static void SendStateToPeer(NetPeer peer)
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            try
            {
                Write(writer);
                NetworkServer.TrySend(peer, writer, BasisNetworkCommons.AdminChannel, DeliveryMethod.ReliableOrdered);
            }
            finally
            {
                NetworkServer.ReturnWriter(writer);
            }
        }

        public static void BroadcastState()
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            try
            {
                Write(writer);
                NetworkServer.BroadcastMessageToClients(
                    writer,
                    BasisNetworkCommons.AdminChannel,
                    NetworkServer.PeerSnapshot,
                    DeliveryMethod.ReliableOrdered);
            }
            finally
            {
                NetworkServer.ReturnWriter(writer);
            }
        }

        private static void Write(NetDataWriter writer)
        {
            new AdminRequest().Serialize(writer, AdminRequestMode.GlobalGetResourceLimits);
            writer.Put(MaxDatabaseEntries);
            writer.Put(MaxDatabaseNameLength);
            writer.Put(MaxDatabasePayloadEntries);
            writer.Put(MaxContentSpheresPerPlayer);
        }

        private static void Sanitize(ref int entries, ref int nameLength, ref int payloadEntries, ref int spheres)
        {
            if (entries < 1) entries = DefaultMaxDatabaseEntries;
            if (entries > AbsoluteMaxDatabaseEntries) entries = AbsoluteMaxDatabaseEntries;
            if (nameLength < 1) nameLength = DefaultMaxDatabaseNameLength;
            if (nameLength > AbsoluteMaxDatabaseNameLength) nameLength = AbsoluteMaxDatabaseNameLength;
            if (payloadEntries < 1) payloadEntries = DefaultMaxDatabasePayloadEntries;
            if (payloadEntries > AbsoluteMaxDatabasePayloadEntries) payloadEntries = AbsoluteMaxDatabasePayloadEntries;
            if (spheres < 1) spheres = DefaultMaxContentSpheresPerPlayer;
            if (spheres > AbsoluteMaxContentSpheresPerPlayer) spheres = AbsoluteMaxContentSpheresPerPlayer;
        }
    }
}
