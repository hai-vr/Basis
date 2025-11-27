using Basis.Scripts.Networking;
using Basis.Scripts.Networking.Receivers;
using LiteNetLib;
using static SerializableBasis;
using Basis.Scripts.Networking.NetworkedAvatar;
using System.Collections.Concurrent;
using System;
public static class BasisNetworkHandleAvatar
{
    public static ConcurrentQueue<ServerSideSyncPlayerMessage> Message = new ConcurrentQueue<ServerSideSyncPlayerMessage>();

    // Baseline 176-byte array per player
    private static readonly ConcurrentDictionary<ushort, byte[]> AvatarBaselines = new ConcurrentDictionary<ushort, byte[]>();

    public static void HandleAvatarUpdate(NetPacketReader reader, LiteNetLib.DeliveryMethod deliveryMethod)
    {
        if (deliveryMethod == DeliveryMethod.ReliableOrdered)
        {
            // This is your full frame – treat it as the canonical baseline.
            HandleFullAvatarUpdate(reader);
        }
        else
        {
            // This is your delta frame (sent with DeliveryMethod.Sequenced on the server)
            HandleDeltaAvatarUpdate(reader);
        }
    }
    private static void HandleFullAvatarUpdate(NetPacketReader reader)
    {
        if (Message.TryDequeue(out ServerSideSyncPlayerMessage ssm) == false)
        {
            ssm = new ServerSideSyncPlayerMessage();
        }

        // Old normal path – matches ServerSideSyncPlayerMessage.Serialize on the server
        ssm.Deserialize(reader);

        ushort playerId = ssm.playerIdMessage.playerID;

        // Update / create client baseline for this player
        var lav = ssm.avatarSerialization;
        if (lav.array != null && lav.array.Length == LocalAvatarSyncMessage.AvatarSyncSize)
        {
            byte[] baseline = AvatarBaselines.GetOrAdd( playerId, _ => new byte[LocalAvatarSyncMessage.AvatarSyncSize]);
            Buffer.BlockCopy(lav.array, 0, baseline, 0, LocalAvatarSyncMessage.AvatarSyncSize);
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
    private static void HandleDeltaAvatarUpdate(NetPacketReader reader)
    {
        // 1) PlayerIdMessage
        PlayerIdMessage playerIdMsg = new PlayerIdMessage();
        playerIdMsg.Deserialize(reader);
        ushort playerId = playerIdMsg.playerID;

        // 2) interval
        // Match the type on ServerSideSyncPlayerMessage.interval
        // If it's double, use reader.GetDouble();
        byte interval = reader.GetByte();

        // 3) delta section for 176-byte avatar array
        byte changeCount = reader.GetByte();

        // Get or create baseline (all zeros if we somehow never got a full baseline yet)
        byte[] baseline = AvatarBaselines.GetOrAdd( playerId, _ => new byte[LocalAvatarSyncMessage.AvatarSyncSize]);

        // Clone baseline → new avatar bytes
        byte[] newAvatarArray = new byte[LocalAvatarSyncMessage.AvatarSyncSize];
        Buffer.BlockCopy(baseline, 0, newAvatarArray, 0, LocalAvatarSyncMessage.AvatarSyncSize);

        for (int Index = 0; Index < changeCount; Index++)
        {
            byte index = reader.GetByte();
            byte value = reader.GetByte();

            // Sanity check – server only writes 0..175
            if (index < LocalAvatarSyncMessage.AvatarSyncSize)
            {
                newAvatarArray[index] = value;
            }
            else
            {
                BasisDebug.LogError($"Delta index out of range: {index} for player {playerId}");
            }
        }

        // 4) AdditionalAvatarData section (mirrors server)
        LocalAvatarSyncMessage lav = new LocalAvatarSyncMessage();
        lav.array = newAvatarArray;

        byte additionalSize = reader.GetByte();
        if (additionalSize == 0)
        {
            lav.AdditionalAvatarDatas = Array.Empty<AdditionalAvatarData>();
            lav.LinkedAvatarIndex = 0;
        }
        else
        {
            lav.LinkedAvatarIndex = reader.GetByte();
            lav.AdditionalAvatarDatas = new AdditionalAvatarData[additionalSize];

            for (int i = 0; i < additionalSize; i++)
            {
                AdditionalAvatarData aad = new AdditionalAvatarData();
                aad.Deserialize(reader);
                lav.AdditionalAvatarDatas[i] = aad;
            }
        }

        // Update baseline to the newly reconstructed bytes
        Buffer.BlockCopy(newAvatarArray, 0, baseline, 0, LocalAvatarSyncMessage.AvatarSyncSize);

        // Reuse queue object to avoid GC churn, same as full path
        if (Message.TryDequeue(out ServerSideSyncPlayerMessage ssm) == false)
        {
            ssm = new ServerSideSyncPlayerMessage();
        }

        ssm.playerIdMessage = playerIdMsg;
        ssm.interval = interval;
        ssm.avatarSerialization = lav;

        if (BasisNetworkPlayers.RemotePlayers.TryGetValue(playerId, out BasisNetworkReceiver player))
        {
            BasisNetworkAvatarDecompressor.DecompressAndProcessAvatar(player, ssm);
        }
        else
        {
            // We still advanced the baseline; player might arrive later.
            // BasisDebug.Log($"Missing player for delta avatar update {playerId}");
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
