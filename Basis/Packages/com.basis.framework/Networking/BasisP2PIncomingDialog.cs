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

            if (BasisSettingsDefaults.DisableDirectConnections.RawValue)
            {
                BasisDebug.Log($"[P2P] DisableDirectConnections is on; auto-declining request from player {senderPlayerId}.");
                BasisP2PManager.DeclineIncoming(senderPlayerId, token);
                return;
            }

            BasisDeviceManagement.EnqueueOnMainThread(() =>
            {
                string displayName = senderPlayerId.ToString();
                string uuid = null;
                if (BasisNetworkPlayers.Players.TryGetValue(senderPlayerId, out var netPlayer) &&
                    netPlayer != null && netPlayer.Player != null)
                {
                    displayName = netPlayer.Player.DisplayName ?? displayName;
                    uuid = netPlayer.Player.UUID;
                }

                // A saved per-person policy bypasses the prompt entirely.
                BasisDirectConnectionPolicy policy = BasisTrustedConnections.GetPolicy(uuid);
                if (policy == BasisDirectConnectionPolicy.AlwaysAccept)
                {
                    BasisDebug.Log($"[P2P] Always-trust policy for {displayName}; auto-accepting.");
                    BasisP2PManager.AcceptIncoming(senderPlayerId, token);
                    return;
                }
                if (policy == BasisDirectConnectionPolicy.AlwaysDecline)
                {
                    BasisDebug.Log($"[P2P] Always-decline policy for {displayName}; auto-declining.");
                    BasisP2PManager.DeclineIncoming(senderPlayerId, token);
                    return;
                }

                // Show the prompt (with the per-person policy dropdown). The prompt itself
                // handles do-not-disturb routing, opening the menu, and recovery if closed.
                BasisDebug.Log($"[P2P] Prompting for direct connection from {displayName}.");
                BasisDirectConnectionPrompt.Show(displayName, uuid, accepted =>
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
