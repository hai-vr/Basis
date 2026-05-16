using System;

namespace Basis.Network.Core
{
    [Serializable]
    public sealed class LNLTransportConfig
    {
        public bool UseNativeSockets = true;
        public bool NatPunchEnabled = true;
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
    }
}
