using Basis.Network.Core;
using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using BasisPermissions;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Scripts.Networking
{
    public enum BasisTalkMode : byte
    {
        Normal = 0,
        Private = 1,
        Direct = 2,
        ThisPerson = 3,
        Shout = 4,
    }

    public static class BasisTalkModeManager
    {
        public static BasisTalkMode CurrentMode { get; private set; } = BasisTalkMode.Normal;

        public static event Action OnLocalTalkModeChanged;

        private static readonly HashSet<ushort> privateMembers = new HashSet<ushort>();
        private static ushort thisPersonTarget;
        private static bool hasThisPersonTarget;
        private static BasisTalkMode pendingShoutExitMode;
        private static bool hasPendingShoutExitMode;

        [RuntimeInitializeOnLoadMethod]
        private static void Init()
        {
            BasisNetworkModeration.OnShoutModeChanged -= HandleShoutModeChanged;
            BasisNetworkModeration.OnShoutModeChanged += HandleShoutModeChanged;
            BasisNetworkPlayer.OnRemotePlayerJoined -= HandleRemotePlayerJoined;
            BasisNetworkPlayer.OnRemotePlayerJoined += HandleRemotePlayerJoined;
            BasisNetworkPlayer.OnRemotePlayerLeft -= HandleRemotePlayerLeft;
            BasisNetworkPlayer.OnRemotePlayerLeft += HandleRemotePlayerLeft;
            BasisP2PManager.OnSessionStateChanged -= HandleP2PSessionChanged;
            BasisP2PManager.OnSessionStateChanged += HandleP2PSessionChanged;
            BasisNetworkManagement.OnlocalPermissionsChanged -= HandlePermissionsChanged;
            BasisNetworkManagement.OnlocalPermissionsChanged += HandlePermissionsChanged;
            BasisSettingsDefaults.ShoutShowOnMenuBar.OnChanged -= HandleShoutMenuBarPrefChanged;
            BasisSettingsDefaults.ShoutShowOnMenuBar.OnChanged += HandleShoutMenuBarPrefChanged;
#if !BASIS_DISABLE_MICROPHONE
            BasisLocalMicrophoneDriver.OnPausedAction -= HandleLocalMuteChanged;
            BasisLocalMicrophoneDriver.OnPausedAction += HandleLocalMuteChanged;
#endif
        }

        public static bool LocalCanShout()
        {
            return BasisNetworkManagement.LocalPermissions != null &&
                   BasisNetworkManagement.LocalPermissions.Contains(PermNodes.PermissionsView);
        }

        public static bool ShoutAvailableOnMenuBar()
        {
            return LocalCanShout() && BasisSettingsDefaults.ShoutShowOnMenuBar.RawValue;
        }

        private static readonly BasisTalkMode[] CycleOrder =
        {
            BasisTalkMode.Normal,
            BasisTalkMode.Private,
            BasisTalkMode.ThisPerson,
            BasisTalkMode.Direct,
            BasisTalkMode.Shout,
        };

        /// <summary>
        /// True only when there is a reason to expose the mic-mode button: we're already
        /// in a non-normal mode, shout is enabled on the menu bar, have a private set, a
        /// marked person, or at least one P2P-connected peer.
        /// </summary>
        public static bool ShouldShowModeButton()
        {
            if (CurrentMode != BasisTalkMode.Normal) return true;
            if (ShoutAvailableOnMenuBar()) return true;
            if (privateMembers.Count > 0) return true;
            if (hasThisPersonTarget) return true;
            return BasisP2PManager.GetConnectedSessionCount() > 0;
        }

        public static bool ModeAvailable(BasisTalkMode mode)
        {
            switch (mode)
            {
                case BasisTalkMode.Normal: return true;
                case BasisTalkMode.Private: return privateMembers.Count > 0;
                case BasisTalkMode.ThisPerson: return hasThisPersonTarget;
                case BasisTalkMode.Direct: return BasisP2PManager.GetConnectedSessionCount() > 0;
                case BasisTalkMode.Shout: return ShoutAvailableOnMenuBar();
                default: return false;
            }
        }

        public static void CycleMode()
        {
            int start = Array.IndexOf(CycleOrder, CurrentMode);
            if (start < 0) start = 0;
            for (int step = 1; step <= CycleOrder.Length; step++)
            {
                BasisTalkMode candidate = CycleOrder[(start + step) % CycleOrder.Length];
                if (candidate == CurrentMode) continue;
                if (ModeAvailable(candidate))
                {
                    SetMode(candidate);
                    return;
                }
            }
        }

        public static void SetMode(BasisTalkMode mode)
        {
            if (mode == BasisTalkMode.Shout)
            {
                if (LocalCanShout() && BasisNetworkPlayer.LocalPlayer != null)
                {
                    BasisNetworkModeration.EnableShoutMode(BasisNetworkPlayer.LocalPlayer.playerId);
                }
                return;
            }

            if (CurrentMode == BasisTalkMode.Shout && BasisNetworkPlayer.LocalPlayer != null)
            {
                pendingShoutExitMode = mode;
                hasPendingShoutExitMode = true;
                BasisNetworkModeration.DisableShoutMode(BasisNetworkPlayer.LocalPlayer.playerId);
                return;
            }

            ApplyMode(mode);
        }

        public static bool TogglePrivateMember(ushort playerId)
        {
            bool added;
            if (privateMembers.Contains(playerId))
            {
                privateMembers.Remove(playerId);
                added = false;
            }
            else
            {
                privateMembers.Add(playerId);
                added = true;
            }

            if (!added && privateMembers.Count == 0 && CurrentMode == BasisTalkMode.Private)
            {
                // Removed the last private member while in Private mode — fall back to Normal.
                SetMode(BasisTalkMode.Normal);
                return added;
            }

            if (CurrentMode == BasisTalkMode.Private)
            {
                BasisTransmissionResults.ForceVoiceRecipientResend = true;
            }
            OnLocalTalkModeChanged?.Invoke();
            return added;
        }

        public static bool IsPrivateMember(ushort playerId) => privateMembers.Contains(playerId);

        public static void SetThisPersonTarget(ushort playerId)
        {
            thisPersonTarget = playerId;
            hasThisPersonTarget = true;
            SetMode(BasisTalkMode.ThisPerson);
        }

        public static bool TryGetThisPersonTarget(out ushort playerId)
        {
            playerId = thisPersonTarget;
            return hasThisPersonTarget;
        }

        public static bool IsTalkingOnlyTo(ushort playerId)
        {
            return CurrentMode == BasisTalkMode.ThisPerson && hasThisPersonTarget && thisPersonTarget == playerId;
        }

        public static void StopThisPerson()
        {
            hasThisPersonTarget = false;
            if (CurrentMode == BasisTalkMode.ThisPerson)
            {
                ApplyMode(BasisTalkMode.Normal);
            }
            else
            {
                OnLocalTalkModeChanged?.Invoke();
            }
        }

        public static bool IsRecipient(ushort playerId)
        {
            switch (CurrentMode)
            {
                case BasisTalkMode.Private: return privateMembers.Contains(playerId);
                case BasisTalkMode.ThisPerson: return hasThisPersonTarget && playerId == thisPersonTarget;
                default: return false;
            }
        }

        public static void OnRemoteTalkModeReceived(ushort playerId, byte modeByte)
        {
            if (BasisNetworkPlayers.Players.TryGetValue(playerId, out BasisNetworkPlayer np) &&
                np != null && np.Player is BasisRemotePlayer remote)
            {
                remote.SetTalkMode((BasisTalkMode)modeByte);
            }
        }

        private static void ApplyMode(BasisTalkMode mode)
        {
            CurrentMode = mode;
            BasisTransmissionResults.ForceVoiceRecipientResend = true;
            BroadcastLocalMode();
            OnLocalTalkModeChanged?.Invoke();
        }

        private static void BroadcastLocalMode()
        {
            if (BasisNetworkConnection.LocalPlayerPeer == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(BasisNetworkCommons.EventType_TalkModeChanged);
            writer.Put((byte)CurrentMode);
            BasisNetworkConnection.LocalPlayerPeer.Send(
                writer,
                BasisNetworkCommons.EventsChannel,
                DeliveryMethod.ReliableOrdered);
        }

        private static void HandleLocalMuteChanged(bool muted)
        {
            BroadcastLocalMute(muted);
        }

        private static void BroadcastLocalMute(bool muted)
        {
            if (BasisNetworkConnection.LocalPlayerPeer == null) return;

            NetDataWriter writer = new NetDataWriter();
            writer.Put(BasisNetworkCommons.EventType_MuteStateChanged);
            writer.Put((byte)(muted ? 1 : 0));
            BasisNetworkConnection.LocalPlayerPeer.Send(
                writer,
                BasisNetworkCommons.EventsChannel,
                DeliveryMethod.ReliableOrdered);
        }

        public static void OnRemoteMuteReceived(ushort playerId, bool muted)
        {
            if (BasisNetworkPlayers.Players.TryGetValue(playerId, out BasisNetworkPlayer np) &&
                np != null && np.Player is BasisRemotePlayer remote)
            {
                remote.SetSelfMuted(muted);
            }
        }

        private static void HandleShoutModeChanged(ushort playerId, bool enabled)
        {
            if (BasisNetworkPlayer.LocalPlayer == null || playerId != BasisNetworkPlayer.LocalPlayer.playerId) return;

            if (enabled)
            {
                hasPendingShoutExitMode = false;
                ApplyMode(BasisTalkMode.Shout);
            }
            else if (CurrentMode == BasisTalkMode.Shout)
            {
                BasisTalkMode target = hasPendingShoutExitMode ? pendingShoutExitMode : BasisTalkMode.Normal;
                hasPendingShoutExitMode = false;
                ApplyMode(target);
            }
        }

        private static void HandleRemotePlayerJoined(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            if (CurrentMode != BasisTalkMode.Normal)
            {
                BroadcastLocalMode();
            }
#if !BASIS_DISABLE_MICROPHONE
            if (BasisLocalMicrophoneDriver.isPaused)
            {
                BroadcastLocalMute(true);
            }
#endif
        }

        private static void HandleRemotePlayerLeft(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            if (networkPlayer == null) return;
            ushort id = networkPlayer.playerId;

            if (hasThisPersonTarget && thisPersonTarget == id)
            {
                hasThisPersonTarget = false;
                if (CurrentMode == BasisTalkMode.ThisPerson)
                {
                    ApplyMode(BasisTalkMode.Normal);
                    return;
                }
            }

            if (privateMembers.Remove(id) && CurrentMode == BasisTalkMode.Private)
            {
                BasisTransmissionResults.ForceVoiceRecipientResend = true;
            }
            OnLocalTalkModeChanged?.Invoke();
        }

        private static void HandleP2PSessionChanged(ushort otherPlayerId, BasisP2PManager.P2PSessionState state)
        {
            OnLocalTalkModeChanged?.Invoke();
        }

        private static void HandlePermissionsChanged()
        {
            OnLocalTalkModeChanged?.Invoke();
        }

        private static void HandleShoutMenuBarPrefChanged(bool _)
        {
            OnLocalTalkModeChanged?.Invoke();
        }
    }
}
