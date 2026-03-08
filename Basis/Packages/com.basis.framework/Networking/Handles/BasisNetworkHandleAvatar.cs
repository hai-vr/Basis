using Basis.Network.Core;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
using Basis.Scripts.Profiler;
using System.Collections.Concurrent;
using static SerializableBasis;
public static class BasisNetworkHandleAvatar
{
    public static ConcurrentQueue<ServerSideSyncPlayerMessage> Message = new ConcurrentQueue<ServerSideSyncPlayerMessage>();

    public static void HandleAvatarUpdate(NetPacketReader reader, DeliveryMethod deliveryMethod)
    {
        HandleFullAvatarUpdate(reader);
    }

    private static void HandleFullAvatarUpdate(NetPacketReader reader)
    {
        BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerSideSyncPlayer, reader.AvailableBytes);

        if (!Message.TryDequeue(out ServerSideSyncPlayerMessage ssm))
            ssm = new ServerSideSyncPlayerMessage();

        // Normal full deserialize – matches ServerSideSyncPlayerMessage.Serialize on server
        ssm.Deserialize(reader);

        ushort playerId = ssm.playerIdMessage.playerID;

        if (BasisNetworkPlayers.RemotePlayers.TryGetValue(playerId, out BasisNetworkReceiver player))
        {
            BasisNetworkAvatarDecompressor.DecompressAndProcessAvatar(player, ssm);
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
        ServerAvatarChangeMessage msg = new ServerAvatarChangeMessage();
        msg.Deserialize(reader);

        ushort playerId = msg.uShortPlayerId.playerID;
        if (BasisNetworkPlayers.Players.TryGetValue(playerId, out BasisNetworkPlayer player))
        {
            ((BasisNetworkReceiver)player).ReceiveAvatarChangeRequest(msg);
        }
        else
        {
            BasisDebug.Log("Missing Player For Message " + playerId);
        }
    }
}
