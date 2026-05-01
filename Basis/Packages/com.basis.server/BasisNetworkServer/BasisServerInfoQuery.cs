using Basis.Network.Core;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

namespace BasisServerHandle
{
    /// <summary>
    /// Handles unconnected "server info" probes — Minecraft-server-list-ping equivalents
    /// for the LiteNetLib UDP transport. A client sends a tiny query packet to the
    /// server's listening port and gets back the public server name, current/max player
    /// count, MOTD, and the original nonce so the client can measure RTT.
    ///
    /// Wire format (little-endian):
    ///   Query:    [u32 ServerInfoQueryMagic][u16 protoVersion][u16 nonce]
    ///   Response: [u32 ServerInfoResponseMagic][u16 protoVersion][u16 nonce]
    ///             [u16 online][u16 max][string name][string motd]
    /// </summary>
    public static class BasisServerInfoQuery
    {
        // Per-IP throttle to limit reflection/amplification abuse. The query is 8 bytes
        // and the response is up to ~330 bytes, so we cap each remote IP to roughly two
        // probes per second.
        private const int MinIntervalMs = 500;
        private static readonly ConcurrentDictionary<IPAddress, long> _lastSeen = new();
        private static readonly Stopwatch _clock = Stopwatch.StartNew();

        public static void Subscribe()
        {
            NetworkServer.Listener.NetworkReceiveUnconnectedEvent += HandleQuery;
        }

        public static void Unsubscribe()
        {
            NetworkServer.Listener.NetworkReceiveUnconnectedEvent -= HandleQuery;
        }

        private static void HandleQuery(IPEndPoint remoteEndPoint, NetPacketReader reader)
        {
            try
            {
                if (reader.AvailableBytes < 8)
                {
                    reader.Recycle(true);
                    return;
                }

                uint magic = reader.GetUInt();
                if (magic != BasisNetworkCommons.ServerInfoQueryMagic)
                {
                    reader.Recycle(true);
                    return;
                }

                ushort _protoVersion = reader.GetUShort();
                ushort nonce = reader.GetUShort();
                reader.Recycle(true);

                if (!ShouldRespond(remoteEndPoint.Address))
                    return;

                Configuration cfg = NetworkServer.Configuration;
                int online = NetworkServer.AuthenticatedPeers.Count;
                int max = cfg != null ? cfg.PeerLimit : 0;
                string serverName = cfg?.ServerName ?? string.Empty;
                string motd = cfg?.ServerMotd ?? string.Empty;

                NetDataWriter writer = NetworkServer.RentWriter();
                try
                {
                    writer.Put(BasisNetworkCommons.ServerInfoResponseMagic);
                    writer.Put(BasisNetworkCommons.ServerInfoProtocolVersion);
                    writer.Put(nonce);
                    writer.Put((ushort)Math.Min(online, ushort.MaxValue));
                    writer.Put((ushort)Math.Min(Math.Max(max, 0), ushort.MaxValue));
                    writer.Put(serverName, BasisNetworkCommons.ServerInfoNameMaxLength);
                    writer.Put(motd, BasisNetworkCommons.ServerInfoMotdMaxLength);
                    NetworkServer.Server.SendUnconnectedMessage(writer, remoteEndPoint);
                }
                finally
                {
                    NetworkServer.ReturnWriter(writer);
                }
            }
            catch (Exception ex)
            {
                BNL.LogWarning($"ServerInfoQuery failed for {remoteEndPoint}: {ex.Message}");
            }
        }

        private static bool ShouldRespond(IPAddress address)
        {
            long nowMs = _clock.ElapsedMilliseconds;
            long previous = _lastSeen.GetOrAdd(address, 0L);
            if (previous != 0 && nowMs - previous < MinIntervalMs) return false;
            _lastSeen[address] = nowMs;
            return true;
        }
    }
}
