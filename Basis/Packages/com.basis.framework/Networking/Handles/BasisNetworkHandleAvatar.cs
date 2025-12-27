using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
using Basis.Scripts.Profiler;
using System;
using System.Collections.Concurrent;
using static SerializableBasis;

public static class BasisNetworkHandleAvatar
{
    public static ConcurrentQueue<ServerSideSyncPlayerMessage> Message = new ConcurrentQueue<ServerSideSyncPlayerMessage>();

    // Baseline 176-byte array per player (fixed once per player)
    private static readonly ConcurrentDictionary<ushort, byte[]> AvatarBaselines = new ConcurrentDictionary<ushort, byte[]>();

    public static void HandleAvatarUpdate(NetPacketReader reader, DeliveryMethod deliveryMethod)
    {
        HandleFullAvatarUpdate(reader);
    }

    private static void HandleFullAvatarUpdate(NetPacketReader reader)
    {
        BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerSideSyncPlayer, reader.AvailableBytes);

        if (Message.TryDequeue(out ServerSideSyncPlayerMessage ssm) == false)
        {
            ssm = new ServerSideSyncPlayerMessage();
        }

        // Normal full deserialize – matches ServerSideSyncPlayerMessage.Serialize on server
        ssm.Deserialize(reader);

        ushort playerId = ssm.playerIdMessage.playerID;

        // Build / cache baseline ONLY on first full for this player
        var lav = ssm.avatarSerialization;
        if (lav.array != null && lav.array.Length == BasisBitPackingConstants.AvatarSyncSize)
        {
            if (!AvatarBaselines.ContainsKey(playerId))
            {
                byte[] baseline = new byte[BasisBitPackingConstants.AvatarSyncSize];
                Buffer.BlockCopy(lav.array, 0, baseline, 0, BasisBitPackingConstants.AvatarSyncSize);
                AvatarBaselines[playerId] = baseline;
            }
            // If a baseline already exists, we DON'T overwrite it.
        }

        if (BasisNetworkPlayers.RemotePlayers.TryGetValue(playerId, out BasisNetworkReceiver player))
        {
            BasisNetworkAvatarDecompressor.DecompressAndProcessAvatar(player, ssm);
        }
        else
        {
            // Still keep baseline; player may spawn later.
            // BasisDebug.Log($"Missing player for full avatar update {playerId}");
        }

        Message.Enqueue(ssm);
        TrimQueue();
    }
    private static void TrimQueue()
    {
        if (Message.Count > 256)
        {
            while (Message.TryDequeue(out _)) { }
            BasisDebug.LogError("Messages Exceeded 250! Resetting");
        }
    }

    public static void HandleAvatarChangeMessage(NetPacketReader reader)
    {
        ServerAvatarChangeMessage ServerAvatarChangeMessage = new ServerAvatarChangeMessage();
        ServerAvatarChangeMessage.Deserialize(reader);
        ushort PlayerID = ServerAvatarChangeMessage.uShortPlayerId.playerID;
        if (BasisNetworkPlayers.Players.TryGetValue(PlayerID, out BasisNetworkPlayer Player))
        {
            BasisNetworkReceiver networkReceiver = (BasisNetworkReceiver)Player;
            networkReceiver.ReceiveAvatarChangeRequest(ServerAvatarChangeMessage);
        }
        else
        {
            BasisDebug.Log("Missing Player For Message " + ServerAvatarChangeMessage.uShortPlayerId.playerID);
        }
    }
}
