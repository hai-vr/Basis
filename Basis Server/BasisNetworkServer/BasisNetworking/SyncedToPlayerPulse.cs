using System;
using System.Threading;
using Basis.Network.Core;
using Basis.Scripts.Networking.Compression;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using LiteNetLib;
using LiteNetLib.Utils;
using static LockedBoolArray;
using static SerializableBasis;

/// <summary>
/// Structure to synchronize data with a specific player.
/// </summary>
public class SyncedToPlayerPulse
{
    public Vector3 Position;
    public LockedBoolArray SyncBoolArray = new LockedBoolArray();
    public LockedServerSideReducablePlayerArray ChunkedServerSideReducablePlayerArray = new LockedServerSideReducablePlayerArray();


    public static int BSRSMillisecondDefaultInterval = 50;
    public static byte ByteBSRSMillisecondDefaultInterval;
    public static int BSRBaseMultiplier = 1;
    public static float BSRSIncreaseRate = 0.005f;
    public static int MaxMessages = 80;
    /// <summary>
    /// Supply new data to a specific player.
    /// </summary>
    public void SupplyNewData(NetPeer playerID, ServerSideSyncPlayerMessage serverSideSyncPlayerMessage, int PlayerIndex, Vector3 Position)
    {
        ServerSideReducablePlayer playerData = ChunkedServerSideReducablePlayerArray.GetPlayer(PlayerIndex);
        if (playerData != null)
        {
            playerData.serverSideSyncPlayerMessage = serverSideSyncPlayerMessage;
            playerData.Position = Position;
            ChunkedServerSideReducablePlayerArray.SetPlayer(PlayerIndex, playerData);
        }
        else
        {
            ServerReductionClientPayload clientPayload = new ServerReductionClientPayload
            {
                localClient = playerID,
                dataCameFromThisUser = PlayerIndex
            };
            serverSideSyncPlayerMessage.interval = ByteBSRSMillisecondDefaultInterval;

            ServerSideReducablePlayer newPlayer = new ServerSideReducablePlayer
            {
                serverSideSyncPlayerMessage = serverSideSyncPlayerMessage,
                //  timer = new Timer(SendPlayerData, clientPayload, BSRSMillisecondDefaultInterval, BSRSMillisecondDefaultInterval),
                Writer = new NetDataWriter(true, 204),
                Position = Position,
            };

            BasisServerReductionSystemEvents.scheduler.Schedule(() => SendPlayerData(clientPayload), BSRSMillisecondDefaultInterval);
            SendPlayerData(clientPayload);
            ChunkedServerSideReducablePlayerArray.SetPlayer(PlayerIndex, newPlayer);
        }

        SyncBoolArray.SetBool(PlayerIndex, true);
    }
    /// <summary>
    /// Callback function to send player data at regular intervals.
    /// </summary>
    private void SendPlayerData(ServerReductionClientPayload clientPayload)
    {

        ServerSideReducablePlayer playerData = ChunkedServerSideReducablePlayerArray.GetPlayer(clientPayload.dataCameFromThisUser);
        if (playerData == null)
        {
            return;
        }
        if (clientPayload.localClient != null)
        {
            BasisServerReductionSystemEvents.scheduler.Schedule(() => SendPlayerData(clientPayload), playerData.LastInterval);
        }
        else
        {
            return;
        }

        if (!SyncBoolArray.GetBool(clientPayload.dataCameFromThisUser))
        {
            return;
        }

        SyncBoolArray.SetBool(clientPayload.dataCameFromThisUser, false);

        try
        {
            int Size = clientPayload.localClient.GetPacketsCountInQueue(BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced);


            if (Size < MaxMessages && playerData.Writer != null)
            {
                playerData.serverSideSyncPlayerMessage.Serialize(playerData.Writer);
                NetworkServer.TrySend(clientPayload.localClient, playerData.Writer, BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced);
                playerData.Writer.Reset();
            }
        }
        catch (Exception e)
        {
            if (e.InnerException != null)
            {
                BNL.LogError($"SRS Encounter Issue {e.Message} {e.StackTrace} {e.InnerException}");
            }
            else
            {
                BNL.LogError($"SRS Encounter Issue {e.Message} {e.StackTrace}");
            }
        }
    }
}
