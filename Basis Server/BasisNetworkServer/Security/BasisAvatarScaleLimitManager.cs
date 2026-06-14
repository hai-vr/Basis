using Basis.Network.Core;
using System.Threading;
using static BasisNetworkCore.Serializable.SerializableBasis;

namespace BasisNetworkServer.Security
{
    /// <summary>
    /// Server-defined minimum/maximum avatar eye height (in metres) a non-admin player may scale to.
    /// Seeded from Configuration at boot and pushed to clients via GlobalGetAvatarScaleLimits so they
    /// clamp their avatar scale to it. Admins (basis.moderation.globallock) bypass the clamp client-side.
    /// Admins can change the range live; the new values are persisted to config.xml and broadcast.
    /// </summary>
    public static class BasisAvatarScaleLimitManager
    {
        private const float DefaultMinMeters = 0.1f;
        private const float DefaultMaxMeters = 100f;
        private const float AbsoluteFloor = 0.01f;
        private const float AbsoluteCeiling = 1000f;

        private static float _minMeters = DefaultMinMeters;
        private static float _maxMeters = DefaultMaxMeters;

        public static float MinMeters => Interlocked.CompareExchange(ref _minMeters, 0f, 0f);
        public static float MaxMeters => Interlocked.CompareExchange(ref _maxMeters, 0f, 0f);

        public static void InitializeFromConfig(Configuration config)
        {
            SetLimits(config.MinAvatarEyeHeightMeters, config.MaxAvatarEyeHeightMeters);
        }

        /// <summary>Sanitize, order (min &lt;= max), set, and report whether either bound actually changed.</summary>
        public static bool SetLimits(float minMeters, float maxMeters)
        {
            Sanitize(ref minMeters, ref maxMeters);
            float prevMin = Interlocked.Exchange(ref _minMeters, minMeters);
            float prevMax = Interlocked.Exchange(ref _maxMeters, maxMeters);
            return prevMin != minMeters || prevMax != maxMeters;
        }

        public static void SendStateToPeer(NetPeer peer)
        {
            NetDataWriter writer = NetworkServer.RentWriter();
            try
            {
                new AdminRequest().Serialize(writer, AdminRequestMode.GlobalGetAvatarScaleLimits);
                writer.Put(MinMeters);
                writer.Put(MaxMeters);
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
                new AdminRequest().Serialize(writer, AdminRequestMode.GlobalGetAvatarScaleLimits);
                writer.Put(MinMeters);
                writer.Put(MaxMeters);
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

        private static void Sanitize(ref float minMeters, ref float maxMeters)
        {
            if (float.IsNaN(minMeters) || float.IsInfinity(minMeters) || minMeters <= 0f) minMeters = DefaultMinMeters;
            if (float.IsNaN(maxMeters) || float.IsInfinity(maxMeters) || maxMeters <= 0f) maxMeters = DefaultMaxMeters;
            if (minMeters < AbsoluteFloor) minMeters = AbsoluteFloor;
            if (maxMeters > AbsoluteCeiling) maxMeters = AbsoluteCeiling;
            if (maxMeters < minMeters) maxMeters = minMeters;
        }
    }
}
