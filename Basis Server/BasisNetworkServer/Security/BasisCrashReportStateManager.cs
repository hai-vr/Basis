using Basis.Network.Core;
using System.Threading;
using static BasisNetworkCore.Serializable.SerializableBasis;

namespace BasisNetworkServer.Security
{
    /// <summary>
    /// Server toggle for client error/exception reporting. Seeded from
    /// Configuration.CrashReportingEnabled at boot and pushed to clients so they only
    /// transmit reports while it is on. Persisted via the admin path that flips it.
    /// </summary>
    public static class BasisCrashReportStateManager
    {
        private static int _enabled = 1;

        public static bool Enabled => Interlocked.CompareExchange(ref _enabled, 0, 0) == 1;

        public static void InitializeFromConfig(Configuration config)
        {
            Interlocked.Exchange(ref _enabled, config.CrashReportingEnabled ? 1 : 0);
        }

        public static bool SetEnabled(bool enabled)
        {
            int requestedValue = enabled ? 1 : 0;
            int previousValue = Interlocked.Exchange(ref _enabled, requestedValue);
            return previousValue != requestedValue;
        }

        public static void SendStateToPeer(NetPeer peer)
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            try
            {
                new AdminRequest().Serialize(writer, AdminRequestMode.GlobalGetCrashReportState);
                writer.Put(Enabled);
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
                new AdminRequest().Serialize(writer, AdminRequestMode.GlobalGetCrashReportState);
                writer.Put(Enabled);
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
    }
}
