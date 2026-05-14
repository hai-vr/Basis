using Basis.Network.Core;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Basis.Scripts.Networking
{
    /// <summary>
    /// Registers the LiteNetLib server-info probe with <see cref="BasisNetworkStackRegistry"/>.
    /// Fires a single unconnected "server info" packet at the listening port (the LiteNetLib
    /// equivalent of a Minecraft Server List Ping) and awaits the response.
    /// </summary>
    public static class BasisServerInfoClient
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister()
        {
            BasisNetworkStackRegistry.RegisterProbe(BasisNetworkStackRegistry.LiteNetLibId, ProbeAsync);
        }

        public static async Task<ServerProbeResult> ProbeAsync(ConnectionTarget target, int timeoutMs, CancellationToken ct)
        {
            ServerProbeResult result = new ServerProbeResult();
            if (target == null)
            {
                result.Error = "Target is null";
                return result;
            }

            string host = target.Get(ConnectionTarget.Keys.Address, string.Empty);
            if (string.IsNullOrWhiteSpace(host))
            {
                result.Error = "Host is empty";
                return result;
            }

            string portString = target.Get(ConnectionTarget.Keys.Port, LNLConnectionTargetParser.DefaultPort.ToString(CultureInfo.InvariantCulture));
            if (!ushort.TryParse(portString, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort port) || port == 0)
            {
                result.Error = "Port is invalid";
                return result;
            }

            EventBasedNetListener listener = new EventBasedNetListener();
            Configuration probeConfig = new Configuration { NetworkStackId = BasisNetworkStackRegistry.LiteNetLibId };
            NetManager manager = BasisNetworkStackRegistry.Create(probeConfig.NetworkStackId, listener, probeConfig);

            ushort nonce;
            unchecked { nonce = (ushort)Guid.NewGuid().GetHashCode(); }

            TaskCompletionSource<ServerProbeResult> tcs = new TaskCompletionSource<ServerProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Stopwatch rtt = new Stopwatch();

            void OnReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader)
            {
                try
                {
                    if (reader.AvailableBytes < 8) return;
                    uint magic = reader.GetUInt();
                    if (magic != BasisNetworkCommons.ServerInfoResponseMagic) return;

                    ushort proto = reader.GetUShort();
                    ushort returnedNonce = reader.GetUShort();
                    if (returnedNonce != nonce) return;

                    ushort online = reader.GetUShort();
                    ushort max = reader.GetUShort();
                    string name = reader.GetString();
                    string motd = reader.GetString();

                    tcs.TrySetResult(new ServerProbeResult
                    {
                        Reachable = true,
                        Online = online,
                        Max = max,
                        ProtocolVersion = proto,
                        Name = name,
                        Motd = motd,
                        RoundTripMs = (int)rtt.ElapsedMilliseconds,
                    });
                }
                catch
                {
                }
                finally
                {
                    reader.Recycle(true);
                }
            }

            listener.NetworkReceiveUnconnectedEvent += OnReceiveUnconnected;

            try
            {
                IPAddress address = await ResolveHostAsync(host, ct).ConfigureAwait(false);
                if (address == null)
                {
                    result.Error = "DNS resolution failed";
                    return result;
                }

                IPEndPoint endpoint = new IPEndPoint(address, port);

                manager.Start(IPAddress.Any, IPAddress.IPv6Any, 0);

                NetDataWriter writer = new NetDataWriter(true, BasisNetworkCommons.ServerInfoMinRequestBytes);
                writer.Put(BasisNetworkCommons.ServerInfoQueryMagic);
                writer.Put(BasisNetworkCommons.ServerInfoProtocolVersion);
                writer.Put(nonce);
                int padBytes = BasisNetworkCommons.ServerInfoMinRequestBytes - writer.Length;
                if (padBytes > 0) writer.Put(new byte[padBytes]);

                rtt.Start();
                if (!manager.SendUnconnectedMessage(writer, endpoint))
                {
                    result.Error = "Send failed";
                    return result;
                }

                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);
                using CancellationTokenRegistration reg = cts.Token.Register(() => tcs.TrySetCanceled());

                try
                {
                    ServerProbeResult probed = await tcs.Task.ConfigureAwait(false);
                    return probed;
                }
                catch (OperationCanceledException)
                {
                    result.TimedOut = true;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                listener.NetworkReceiveUnconnectedEvent -= OnReceiveUnconnected;
                try { manager.Stop(); } catch { }
            }

            return result;
        }

        private static async Task<IPAddress> ResolveHostAsync(string host, CancellationToken ct)
        {
            if (IPAddress.TryParse(host, out IPAddress parsed))
                return parsed;

            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            if (addresses == null || addresses.Length == 0)
                return null;

            foreach (IPAddress a in addresses)
                if (a.AddressFamily == AddressFamily.InterNetwork) return a;

            foreach (IPAddress a in addresses)
                if (a.AddressFamily == AddressFamily.InterNetworkV6) return a;

            return addresses[0];
        }
    }
}
