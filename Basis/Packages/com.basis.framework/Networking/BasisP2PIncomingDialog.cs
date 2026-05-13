using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using UnityEngine;

namespace Basis.Scripts.Networking
{
    public static class BasisP2PIncomingDialog
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            BasisP2PManager.OnIncomingRequest -= OnIncoming;
            BasisP2PManager.OnIncomingRequest += OnIncoming;
        }

        private static void OnIncoming(ushort senderPlayerId, string token)
        {
            BasisDebug.Log($"[P2P] Incoming direct-connection request from player {senderPlayerId} (token {token}).");
            BasisDeviceManagement.EnqueueOnMainThread(() =>
            {
                // Open() must come before the Instance null-check — Instance is null
                // on a target that hasn't opened the menu yet.
                BasisMainMenu.Open();
                if (BasisMainMenu.Instance == null)
                {
                    BasisDebug.LogWarning("[P2P] BasisMainMenu.Instance unavailable; auto-declining incoming request.");
                    BasisP2PManager.DeclineIncoming(senderPlayerId, token);
                    return;
                }

                string displayName = senderPlayerId.ToString();
                if (BasisNetworkPlayers.Players.TryGetValue(senderPlayerId, out var netPlayer) &&
                    netPlayer != null && netPlayer.Player != null)
                {
                    displayName = netPlayer.Player.DisplayName ?? displayName;
                }

                BasisDebug.Log($"[P2P] Opening accept dialog for direct connection from {displayName}.");
                BasisMainMenu.Instance.OpenDialogue(
                    BasisLocalization.Get("menu.individualPlayer.directConnection.incomingDialog.title"),
                    BasisLocalization.Get("menu.individualPlayer.directConnection.incomingDialog.body", displayName),
                    BasisLocalization.Get("menu.individualPlayer.directConnection.accept"),
                    BasisLocalization.Get("menu.individualPlayer.directConnection.decline"),
                    accepted =>
                    {
                        BasisDebug.Log($"[P2P] User {(accepted ? "accepted" : "declined")} direct-connection request from {displayName}.");
                        if (accepted)
                        {
                            BasisP2PManager.AcceptIncoming(senderPlayerId, token);
                        }
                        else
                        {
                            BasisP2PManager.DeclineIncoming(senderPlayerId, token);
                        }
                    });
            });
        }
    }
}
