using System.Diagnostics;
using Basis.Logging;
using Basis.Network;
using Basis.Config;
using Basis.Utils;
using Basis.Network.Core;

namespace Basis
{
    partial class Program
    {
        private const double DriverTickMs = 15.0;
        private const double MovementIntervalMs = 90.0;
        private static volatile bool _running = true;

        public static async Task Main(string[] args)
        {
            ErrorHandlers.AttachGlobalHandlers();
            ConfigManager.LoadOrCreateConfigXml("Config.xml");
            NetDebug.Logger = new BasisClientLogger();

            // Face-data test mode: BASIS_EMIT_FACE=1 attaches a synthetic AdditionalAvatarData to
            // every avatar send and logs when other clients' additional data arrives — an
            // end-to-end probe of the face-tracking transport over real UDP. Companions:
            //   BASIS_FACE_SPACING=<m>  pin client i at (i*m,1,0), no random walk (distance tiers)
            //   BASIS_UPLINK_DELTAS=0   legacy all-keyframe uploads (no v42 uplink deltas)
            //   BASIS_PACKET_LOSS=<pct> simulate inbound/outbound UDP loss on every client
            if (Environment.GetEnvironmentVariable("BASIS_EMIT_FACE") == "1")
            {
                MovementSender.EmitFaceData = true;
                BNL.Log("[FaceObserver] EmitFaceData enabled — every avatar send carries additional data.");
            }
            if (float.TryParse(Environment.GetEnvironmentVariable("BASIS_FACE_SPACING"), out float spacing) && spacing > 0f)
            {
                MovementSender.PinSpacingMeters = spacing;
                BNL.Log($"[FaceObserver] Positions pinned at {spacing}m spacing.");
            }
            if (Environment.GetEnvironmentVariable("BASIS_UPLINK_DELTAS") == "0")
            {
                MovementSender.UseUplinkDeltas = false;
                BNL.Log("[FaceObserver] Uplink deltas disabled — legacy all-keyframe uploads.");
            }

            var clientManager = new ClientManager();
            clientManager.Prepare();

            AppDomain.CurrentDomain.ProcessExit += (_, __) =>
            {
                Console.WriteLine("Shutting down...");
                _running = false;
                clientManager.StopClientsAsync().GetAwaiter().GetResult();
            };

            MovementSender.Initialize(clientManager.ClientCount);

            // Drive all clients from one worker per CPU core
            StartClientDriverLoops(clientManager.FinalClients, clientManager.FinalPeers);

            // Simulated UDP loss (BASIS_PACKET_LOSS=<1-100>): set on the LiteNetLib transport
            // config BEFORE clients construct their managers, so uplink and downlink frames both
            // drop — exercises the keyframe-NACK/re-key recovery paths under realistic conditions.
            if (int.TryParse(Environment.GetEnvironmentVariable("BASIS_PACKET_LOSS"), out int lossPct) && lossPct > 0)
            {
                var lnl = Basis.Network.Core.BasisTransportConfigStore.Get<Basis.Network.Core.LNLTransportConfig>(
                    Basis.Network.Core.BasisNetworkStackRegistry.LiteNetLibId);
                lnl.SimulatePacketLoss = true;
                lnl.SimulationPacketLossChance = Math.Min(lossPct, 100);
                BNL.Log($"[FaceObserver] Simulating {lossPct}% packet loss on every client.");
            }

            await clientManager.StartClientsAsync();

            // Periodic observer summary so a timed run ends with machine-readable totals.
            if (MovementSender.EmitFaceData)
            {
                _ = Task.Run(async () =>
                {
                    while (_running)
                    {
                        await Task.Delay(5000);
                        BNL.Log(MessageHandler.Summary());
                    }
                });
            }

            // Start random reconnects
            _ = StartRandomReconnectLoop(clientManager);

            await Task.Delay(-1); // keep main alive
        }

        public static void StopClient(ClientManager manager, int index)
        {
            var peer = Volatile.Read(ref manager.FinalPeers[index]);
            if (peer != null)
            {
                peer.Disconnect();
            }
        }

        private static void StartClientDriverLoops(NetworkClient[] clients, NetPeer[] peers)
        {
            int count = peers.Length;
            int workerCount = Math.Min(Environment.ProcessorCount, count);
            if (workerCount <= 0) return;

            int chunkSize = (count + workerCount - 1) / workerCount;

            for (int w = 0; w < workerCount; w++)
            {
                int start = w * chunkSize;
                int end = Math.Min(start + chunkSize, count);
                if (start >= end) break;

                double phaseOffsetMs = MovementIntervalMs * w / workerCount;

                var thread = new Thread(() => DriveSlice(clients, peers, start, end, phaseOffsetMs))
                {
                    Name = $"ClientDriver({start}-{end})",
                    IsBackground = true
                };
                thread.Start();
            }
        }

        private static void DriveSlice(NetworkClient[] clients, NetPeer[] peers, int start, int end, double phaseOffsetMs)
        {
            var sw = Stopwatch.StartNew();
            double lastTickMs = 0;
            double lastMovementMs = phaseOffsetMs - MovementIntervalMs;

            while (_running)
            {
                double nowMs = sw.Elapsed.TotalMilliseconds;
                float dt = (float)(nowMs - lastTickMs);
                lastTickMs = nowMs;

                for (int i = start; i < end; i++)
                {
                    var client = Volatile.Read(ref clients[i]);
                    if (client != null)
                    {
                        client.Poll();
                        client.Update(dt);
                    }
                }

                if (nowMs - lastMovementMs >= MovementIntervalMs)
                {
                    lastMovementMs = nowMs;
                    for (int i = start; i < end; i++)
                    {
                        var peer = Volatile.Read(ref peers[i]);
                        if (peer != null && (peer.Tag as ConsoleClientIdentity)?.Authenticated == true)
                            MovementSender.ProcessSingle(peer, i);
                    }
                }

                int sleepMs = (int)(DriverTickMs - (sw.Elapsed.TotalMilliseconds - nowMs));
                if (sleepMs > 0)
                    Thread.Sleep(sleepMs);
            }
        }

        private static async Task StartRandomReconnectLoop(ClientManager clientManager)
        {
            int totalClients = clientManager.ClientCount;

            while (true)
            {
                int waitMinutes = Random.Shared.Next(1, 21); // 1–20 minutes
                await Task.Delay(TimeSpan.FromMinutes(waitMinutes));

                int indexToRestart = Random.Shared.Next(0, totalClients);
                BNL.Log($"Randomly restarting client at index {indexToRestart}");

                await clientManager.ReconnectClientAsync(indexToRestart);
            }
        }
    }
}
