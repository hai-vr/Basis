using Basis.Scripts.Avatar;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using UnityEngine;

public static class BasisNetworkHandleRemoval
{
    public static void HandleDisconnection(LiteNetLib.NetPacketReader reader)
    {
        while (reader.AvailableBytes >= sizeof(ushort))
        {
            if (!reader.TryGetUShort(out ushort disconnectValue))
            {
                BasisDebug.LogError("Tried to read disconnect message but data was missing!");
                break;
            }

            HandleDisconnectId(disconnectValue);
        }
    }

    public static void HandleDisconnectId(ushort DisconnectedID)
    {
        if (DisconnectedID == BasisNetworkPlayer.LocalPlayer.playerId)
        {
            BasisDebug.LogError("LocalPlayer Matched Disconnected ID returning early");
            return;
        }
        // Queue removal on Unity's main thread
        BasisNetworkManagement.MainThreadContext.Post(_ =>
        {
            // Remove from network manager
            if (BasisNetworkManagement.RemovePlayer(DisconnectedID, out BasisNetworkPlayer Network))
            {
                if(Network == null)
                {
                    BasisDebug.LogError($"z Missing Player for removing ID {DisconnectedID}");
                    return;
                }

                // Notify scripts about remote player leaving
                if (Network.Player != null)
                {
                    BasisNetworkPlayer.OnRemotePlayerLeft?.Invoke(Network, (Basis.Scripts.BasisSdk.Players.BasisRemotePlayer)Network.Player);
                }
                else
                {
                    BasisDebug.LogError($"A Missing Player for removing ID {DisconnectedID}");
                }
                BasisNetworkPlayer.OnPlayerLeft?.Invoke(Network);

                // Shutdown networking
                Network.DeInitialize();
                if (Network.Player != null)
                {
                    BasisAvatarFactory.DeleteLastAvatar(Network.Player);
                }
                else
                {
                    BasisDebug.LogError($"B Missing Player for removing ID {DisconnectedID}");
                }
                // Destroy the player GameObject
                if (Network.Player != null)
                {
                    GameObject.Destroy(Network.Player.gameObject);
                }
            }
            else
            {
                BasisDebug.LogError($"C Missing Player for removing ID {DisconnectedID}");
            }

        }, null);
    }
}
