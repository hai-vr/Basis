using Basis.Network.Core;
using Basis.Scripts.Networking.Compression;
using LiteNetLib;
using static LockedBoolArray;
using static SerializableBasis;
public class BasisServerReductionSystem
{
    public static LockedSyncedToPlayerPulseArray PlayerSync = new LockedSyncedToPlayerPulseArray();
    public static void RemovePlayer(NetPeer playerID)
    {
        int playerIndex = playerID.Id;
        SyncedToPlayerPulse targetPulse = PlayerSync.GetPulse(playerIndex);

        // Clear the target player's pulse and dispose their associated timers
        PlayerSync.SetPulse(playerIndex, null);

        if (targetPulse != null)
        {
            ClearReducablePlayers(targetPulse);
        }

        // Ensure all other pulses remove any references to this player
        for (int Index = 0; Index < BasisNetworkCommons.MaxConnections; Index++)
        {
            SyncedToPlayerPulse otherPulse = PlayerSync.GetPulse(Index);
            if (otherPulse != null)
            {
                ServerSideReducablePlayer playerRef = otherPulse.ChunkedServerSideReducablePlayerArray.GetPlayer(playerIndex);
                if (playerRef != null)
                {
                  //  playerRef.timer.Dispose();
                    otherPulse.ChunkedServerSideReducablePlayerArray.SetPlayer(playerIndex, null);
                }
            }
        }
    }

    /// <summary>
    /// Disposes all ServerSideReducablePlayer timers and clears references from the given pulse.
    /// </summary>
    private static void ClearReducablePlayers(SyncedToPlayerPulse pulse)
    {
        for (int Index = 0; Index < BasisNetworkCommons.MaxConnections; Index++)
        {
            ServerSideReducablePlayer player = pulse.ChunkedServerSideReducablePlayerArray.GetPlayer(Index);
            if (player != null)
            {
              //  player.timer.Dispose();
                pulse.ChunkedServerSideReducablePlayerArray.SetPlayer(Index, null);
            }
        }
    }
    public static void UpdateLastInformation(ServerSideSyncPlayerMessage ssspm, Vector3 Position, int Index)
    {
        SyncedToPlayerPulse playerData = BasisServerReductionSystem.PlayerSync.GetPulse(Index);
        //stage 1 lets update whoever send us this datas last player information
        if (playerData != null)
        {
            playerData.Position = Position;
        }
    }
}
