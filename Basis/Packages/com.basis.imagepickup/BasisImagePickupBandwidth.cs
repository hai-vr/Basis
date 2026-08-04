using System;

namespace Basis.ImagePickup
{
    /// <summary>
    /// Byte-rate governor for image replication. Two resources are metered separately because one packet
    /// spends them at different rates: uplink tokens cover what this client writes to sockets — one copy
    /// for the relay however many players it reaches, plus one copy per directly connected peer — while
    /// relay tokens cover what the server forwards on this client's behalf, which is the packet once per
    /// relayed recipient. Peers on a direct link never touch the relay bucket, so an instance that is
    /// fully P2P transfers at the full uplink rate.
    ///
    /// Each bucket hands image replication <see cref="BasisImagePickupSettings.ShareBandwidthFraction"/>
    /// of its transport budget and leaves the remainder to pose, voice, and object sync. The uplink
    /// budget is whatever <see cref="BasisImagePickupLinkProbe"/> currently measures the link as having
    /// spare; the relay budget is a fixed ceiling, because how much a server can afford to forward is not
    /// something this client is in a position to discover.
    /// </summary>
    internal static class BasisImagePickupBandwidth
    {
        private static double _uplinkTokens;
        private static double _relayTokens;

        internal static double UplinkBytesPerSecond =>
            BasisImagePickupLinkProbe.DiscoveredUplinkBytesPerSecond
            * BasisImagePickupSettings.ShareBandwidthFraction;

        internal static double RelayBytesPerSecond =>
            BasisImagePickupSettings.RelayEgressBudgetBytesPerSecond
            * BasisImagePickupSettings.ShareBandwidthFraction;

        internal static double UplinkCapacityBytes =>
            UplinkBytesPerSecond * BasisImagePickupSettings.ShareBandwidthBurstSeconds;

        internal static double RelayCapacityBytes =>
            RelayBytesPerSecond * BasisImagePickupSettings.ShareBandwidthBurstSeconds;

        internal static double UplinkTokens => _uplinkTokens;
        internal static double RelayTokens => _relayTokens;

        /// <summary>Returns both buckets to full. Runs at arm/teardown so a session starts with its burst.</summary>
        internal static void Reset()
        {
            _uplinkTokens = UplinkCapacityBytes;
            _relayTokens = RelayCapacityBytes;
        }

        /// <summary>
        /// Credits one tick of budget, clamped to <see cref="BasisImagePickupSettings.ShareBandwidthBurstSeconds"/>
        /// so a long stall — a level load, an alt-tab, a breakpoint — cannot bank seconds of credit and then
        /// release them as one spike.
        /// </summary>
        internal static void Refill(float deltaSeconds)
        {
            if (!(deltaSeconds > 0f))
                return;

            _uplinkTokens = Math.Min(
                UplinkCapacityBytes,
                _uplinkTokens + UplinkBytesPerSecond * deltaSeconds
            );
            _relayTokens = Math.Min(
                RelayCapacityBytes,
                _relayTokens + RelayBytesPerSecond * deltaSeconds
            );
        }

        /// <summary>
        /// Charges one packet against both buckets and reports whether it may be sent now. A packet costing
        /// more than the remaining budget still goes out while the buckets are positive and leaves them in
        /// deficit until the refill repays it, so a chunk that is larger than the burst capacity — a wide
        /// fan-out, a big instance — can never wedge a transfer permanently.
        /// </summary>
        internal static bool TryConsume(int wireBytes, int directRecipients, int relayRecipients)
        {
            if (wireBytes <= 0)
                return true;

            int direct = Math.Max(0, directRecipients);
            int relay = Math.Max(0, relayRecipients);
            if (direct == 0 && relay == 0)
                return true;
            if (_uplinkTokens <= 0d)
                return false;
            if (relay > 0 && _relayTokens <= 0d)
                return false;

            _uplinkTokens -= (double)wireBytes * direct + (relay > 0 ? wireBytes : 0d);
            _relayTokens -= (double)wireBytes * relay;
            return true;
        }
    }
}
