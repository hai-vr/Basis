using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Basis.Network.Server;
using Basis.Network.Server.Auth;
using BasisDidLink;
using BasisNetworkServer;
using BasisNetworkServer.BasisNetworking;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using BasisNetworkServer.Security;
using BasisServerHandle;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;
using static BasisPermissions.PermissionManager;

public static class NetworkServer
{
    public static EventBasedNetListener Listener;
    public static NetManager Server;
    public static ConcurrentDictionary<int, NetPeer> AuthenticatedPeers = new();
    public static readonly object AuthenticatedPeerTag = new object();
    public static Configuration Configuration;
    /// <summary>
    /// Allow-list consulted at <see cref="BasisServerHandle.BasisServerHandleEvents.OnNetworkAccepted"/>
    /// when <see cref="Configuration.BasisUserRestrictionMode"/> is set to <c>WhiteList</c>.
    /// File-backed (BasisWhiteList.txt under the config folder) so admin-panel mutations
    /// persist across restarts.
    /// </summary>
    public static BasisNetworkServer.Security.BasisWhiteList Whitelist;
    // Cached snapshot rebuilt on connect/disconnect — avoids ToArray() alloc on every broadcast.
    private static volatile NetPeer[] _peerSnapshot = Array.Empty<NetPeer>();
    public static NetPeer[] PeerSnapshot => _peerSnapshot;

    public static void RebuildPeerSnapshot()
    {
        _peerSnapshot = AuthenticatedPeers.Values.ToArray();
    }

    // Centralized NetDataWriter pool — single source of truth for all server code.
    // Capped so writers don't accumulate unboundedly after player count spikes.
    private static readonly ConcurrentQueue<NetDataWriter> _writerPool = new();
    private const int MaxPooledWriters = 64;
    public static NetDataWriter RentWriter(int initialCapacity = 208)
    {
        if (_writerPool.TryDequeue(out var writer)) return writer;
        return new NetDataWriter(true, initialCapacity);
    }
    public static void ReturnWriter(NetDataWriter writer)
    {
        writer.Reset();
        if (_writerPool.Count < MaxPooledWriters)
        {
            _writerPool.Enqueue(writer);
        }
        // else: drop it — GC reclaims, keeps pool bounded
    }

    public static IAuth Auth;
    public static IAuthIdentity AuthIdentity;
    public static int HighQualityLength;
    #region Server Entry Point

    public static void StartServer(Configuration configuration)
    {
        StopServer();
        Configuration = configuration;

        HighQualityLength = BasisAvatarBitPacking.ConvertToSize(BitQuality.High);
        InitializePulseSettings();
        InitializeAuth();
        BasisHeadlessConnectionPolicyManager.InitializeFromConfig(configuration.DisallowHeadless);
        BasisNetworkServer.Security.BasisGlobalLockManager.InitializeFromConfig(configuration);
        BasisNetworkServer.Security.BasisCrashReportStateManager.InitializeFromConfig(configuration);
        BasisNetworkServer.Security.BasisAudioRangeLimitManager.InitializeFromConfig(configuration);
        SetupServer(configuration);
        SubscribeEvents(Configuration);

        if (configuration.EnableStatistics)
        {
            BasisStatistics.StartWorkerThread(Server);
        }

        BasisNetworkUdpDropMonitor.Start();

        BNL.Log("Server Worker Threads Booted");
    }

    public static void StopServer()
    {
        if (Server == null) return;
        try
        {
            Server.Stop();
        }
        catch (Exception ex)
        {
            BNL.LogWarning($"NetworkServer.StopServer failed: {ex.Message}");
        }
        BasisNetworkUdpDropMonitor.Stop();
        Server = null;
        Listener = null;
        AuthenticatedPeers.Clear();
        _peerSnapshot = Array.Empty<NetPeer>();
    }

    private static void InitializePulseSettings()
    {
        BasisServerReductionSystemEvents.BSRBaseMultiplier = Configuration.BSRBaseMultiplier;
        BasisServerReductionSystemEvents.BSRSMillisecondDefaultInterval = Configuration.BSRSMillisecondDefaultInterval;
        BasisServerReductionSystemEvents.BSRSIncreaseRate = Configuration.BSRSIncreaseRate;
        BasisServerReductionSystemEvents.HighDistanceSq = Configuration.HighQualityDistance * Configuration.HighQualityDistance;
        BasisServerReductionSystemEvents.MediumDistanceSq = Configuration.MediumQualityDistance * Configuration.MediumQualityDistance;
        BasisServerReductionSystemEvents.LowDistanceSq = Configuration.LowQualityDistance * Configuration.LowQualityDistance;
        BasisServerReductionSystemEvents.EnableAvatarBundleCompression = Configuration.EnableAvatarBundleCompression;
        BasisServerReductionSystemEvents.AvatarBundleMinMessages = Configuration.AvatarBundleMinMessages;
        BasisServerReductionSystemEvents.AvatarBundleMinBytes = Configuration.AvatarBundleMinBytes;
        BSRProfiler.Enabled = Configuration.EnableBSRProfiling;
        BNL.Log($"[BSR] AvatarBundleCompression={Configuration.EnableAvatarBundleCompression} (minMsgs={Configuration.AvatarBundleMinMessages}, minBytes={Configuration.AvatarBundleMinBytes})");
    }

    private static void InitializeAuth()
    {
        var HasFileSupport = Configuration.HasFileSupport;
        BasisPlayerModeration.UseFileOnDisc = HasFileSupport;
        IAuthIdentity.HasFileSupport = HasFileSupport;

        Auth = new PasswordAuth(Configuration.Password ?? string.Empty);
        AuthIdentity = new BasisDIDAuthIdentity();

        if (HasFileSupport)
        {
            // Keep permissions with other config files
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string configDir = Path.Combine(baseDir, Configuration.ConfigFolderName);

            Directory.CreateDirectory(configDir);
            PermissionIntegration.Init(Path.Combine(configDir, "permissions.xml"));
            Whitelist = new BasisNetworkServer.Security.BasisWhiteList(Path.Combine(configDir, "BasisWhiteList.txt"));
        }
        else
        {
            PermissionIntegration.InitWithoutDisc();
            // Best-effort in-memory whitelist when the host disabled disk support.
            Whitelist = new BasisNetworkServer.Security.BasisWhiteList();
        }
    }

    private static void SubscribeEvents(Configuration Configuration)
    {
        BasisServerHandleEvents.SubscribeServerEvents();
        BasisPlayerModeration.LoadBannedPlayers();
        BasisNetworkChat.LoadWordFilter(Configuration);
        BasisNetworkStackRegistry.RegisterIntroducerFactory(
            BasisNetworkStackRegistry.LiteNetLibId,
            _ => new BasisNetworkServer.LNLPeerIntroducer());
        BasisNetworkServer.BasisServerP2PBroker.Initialize();
    }

    #endregion

    #region Server Setup

    public static void SetupServer(Configuration configuration)
    {
        Listener = new EventBasedNetListener();
        Server = BasisNetworkStackRegistry.Create(configuration.NetworkStackId, Listener, configuration);

        NetDebug.Logger = new BasisServerLogger();
        StartListening(configuration);
    }

    public static void StartListening(Configuration configuration)
    {
        if (configuration.OverrideAutoDiscoveryOfIpv)
        {
            IPAddress? IPv4Address, IPv6Address;
            if (!IPAddress.TryParse(Configuration.IPv4Address, out IPv4Address))
            {
                BNL.LogWarning("Failed to parse IPv4 bind address, falling back to 0.0.0.0");
                IPv4Address = IPAddress.Parse("0.0.0.0");
            }

            if (!IPAddress.TryParse(Configuration.IPv6Address, out IPv6Address))
            {
                BNL.LogWarning("Failed to parse IPv6 bind address, falling back to ::1");
                IPv6Address = IPAddress.Parse("::1");
            }

            BNL.Log($"Server Wiring up SetPort {Configuration.SetPort} IPv6Address {Configuration.IPv6Address}");
            Server.Start(IPv4Address, IPv6Address, Configuration.SetPort);
        }
        else
        {
            BNL.Log($"Server Wiring up SetPort {Configuration.SetPort}");
            Server.Start(IPAddress.Any, IPAddress.IPv6Any, Configuration.SetPort);
        }
    }
    #endregion
    public static void BroadcastMessageToClients(NetDataWriter writer, byte channel, NetPeer sender, ReadOnlySpan<NetPeer> clients, DeliveryMethod deliveryMethod = DeliveryMethod.Sequenced, int maxMessages = 70)
    {
        if (!CheckValidated(writer))
        {
            return;
        }

        int senderId = sender.Id;
        int sent = 0;
        foreach (var client in clients)
        {
            if (client.Id != senderId && TrySendNoRecord(client, writer, channel, deliveryMethod, maxMessages))
            {
                sent++;
            }
        }
        BasisNetworkStatistics.RecordOutboundBatch(channel, sent, (long)sent * writer.Length);
    }
    public static void BroadcastMessageToClients(NetDataWriter writer, byte channel, ReadOnlySpan<NetPeer> clients, DeliveryMethod deliveryMethod = DeliveryMethod.Sequenced, int maxMessages = 70)
    {
        if (!CheckValidated(writer))
        {
            return;
        }

        int sent = 0;
        foreach (var client in clients)
        {
            if (TrySendNoRecord(client, writer, channel, deliveryMethod, maxMessages))
            {
                sent++;
            }
        }
        BasisNetworkStatistics.RecordOutboundBatch(channel, sent, (long)sent * writer.Length);
    }

    public static void BroadcastMessageToClients(NetDataWriter writer, byte channel, ref List<NetPeer> clients, DeliveryMethod deliveryMethod = DeliveryMethod.Sequenced, int maxMessages = 70)
    {
        if (!CheckValidated(writer))
        {
            return;
        }

        int count = clients.Count;
        int sent = 0;
        for (int Index = 0; Index < count; Index++)
        {
            NetPeer client = clients[Index];
            if (TrySendNoRecord(client, writer, channel, deliveryMethod, maxMessages))
            {
                sent++;
            }
        }
        BasisNetworkStatistics.RecordOutboundBatch(channel, sent, (long)sent * writer.Length);
    }

    public static void TrySend(NetPeer client, NetDataWriter writer, byte channel, DeliveryMethod deliveryMethod, int maxMessages = 70)
    {
        if (TrySendNoRecord(client, writer, channel, deliveryMethod, maxMessages))
        {
            BasisNetworkStatistics.RecordOutbound(channel, writer.Length);
        }
    }

    // Returns true if the send actually went out (vs dropped by the per-channel queue cap).
    // Splits the queue/send decision from the stats record so broadcast loops can fold N×
    // Interlocked into one RecordOutboundBatch call per (channel, broadcast).
    private static bool TrySendNoRecord(NetPeer client, NetDataWriter writer, byte channel, DeliveryMethod deliveryMethod, int maxMessages)
    {
        if (deliveryMethod == DeliveryMethod.Sequenced || deliveryMethod == DeliveryMethod.Unreliable)
        {
            int queuedMessages = client.GetPacketsCountInQueue(channel, deliveryMethod);
            if (queuedMessages > maxMessages)
            {
                return false;
            }
        }
        client.Send(writer, channel, deliveryMethod);
        return true;
    }
    public static bool CheckValidated(NetDataWriter writer)
    {
        if (writer.Length == 0)
        {
            BNL.LogError("Trying to send a message with zero length!");
            return false;
        }
        return true;
    }
}
