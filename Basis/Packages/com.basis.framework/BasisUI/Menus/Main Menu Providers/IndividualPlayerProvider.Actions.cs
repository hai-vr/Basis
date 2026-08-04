using System;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
using UnityEngine;
using P2PState = Basis.Scripts.Networking.BasisP2PManager.P2PSessionState;

namespace Basis.BasisUI
{
    public partial class IndividualPlayerProvider
    {
        /// <summary>
        /// One multicast hook per piece of per-player state the panel exposes in more than one
        /// place. The Actions tab and the detail tabs both subscribe to a hook and both fire it,
        /// so pressing a control on either side repaints the other without either side needing a
        /// reference to it. Subscribed handlers only paint — they must never raise change events
        /// or the two sides would ping-pong.
        /// </summary>
        private sealed class PlayerActionSync
        {
            public Action<BasisPlayerSettingsData> Volume;
            public Action<BasisPlayerSettingsData> Blocked;
            public Action<BasisPlayerSettingsData> AvatarVisible;
            public Action<BasisPlayerSettingsData> JiggleGrab;
            public Action Pin;
            public Action Highlight;
            public Action TalkModes;
            public Action DirectConnection;
            public Action Shout;
            public Action EyeHeight;
        }

        /// <summary>
        /// True when the world beacon is currently pointing at this player. Only one beacon
        /// exists at a time, so a live beacon aimed at somebody else reads as "not highlighted".
        /// </summary>
        private static bool IsHighlighted(IBasisPlayer player)
        {
            return s_beaconGO != null && s_beaconTarget != null && s_beaconTarget.Player == player;
        }

        /// <summary>
        /// Whether the direct-connection button should refuse outright. A lock applied after the
        /// fact doesn't tear down a session that is already up, so a live link still reports
        /// itself and can still be cancelled.
        /// </summary>
        private static bool DirectConnectionBlocked(P2PState state)
        {
            if (state == P2PState.Connected || state == P2PState.Reconnecting || state == P2PState.PartialConnection)
            {
                return false;
            }
            return BasisSettingsDefaults.DisableDirectConnections.RawValue ||
                (BasisNetworkModeration.GlobalDirectConnectLocked && !BasisNetworkModeration.LocalPlayerHasGlobalLockBypass());
        }

        /// <summary>
        /// Runs the direct-connection button's decision tree: cancel an in-flight or live session,
        /// refuse with an explanation when the feature is locked locally or server-side, and
        /// otherwise confirm before handing both sides each other's IP address. Shared so the
        /// Actions tab and the Network tab cannot drift apart on the gating.
        /// </summary>
        public static void ToggleDirectConnection(BasisRemotePlayer player)
        {
            if (player == null) return;
            if (!BasisNetworkPlayers.PlayerToNetworkedPlayer(player, out BasisNetworkPlayer net)) return;

            ushort playerId = net.playerId;
            P2PState state = BasisP2PManager.GetSessionState(playerId);

            // Only Idle and Failed start a new request; anything else is already in flight.
            if (state != P2PState.Idle && state != P2PState.Failed)
            {
                BasisP2PManager.CancelSession(playerId);
                return;
            }

            if (BasisNetworkModeration.GlobalDirectConnectLocked && !BasisNetworkModeration.LocalPlayerHasGlobalLockBypass())
            {
                BasisMainMenu.Instance.OpenDialogue(
                    BasisLocalization.Get("menu.individualPlayer.directConnection.disabledDialog.title"),
                    BasisLocalization.Get("menu.individualPlayer.directConnection.serverLockedDialog.body"),
                    BasisLocalization.Get("ui.ok"),
                    _ => { },
                    severity: BasisPanelSeverity.Caution);
                return;
            }

            if (BasisSettingsDefaults.DisableDirectConnections.RawValue)
            {
                BasisMainMenu.Instance.OpenDialogue(
                    BasisLocalization.Get("menu.individualPlayer.directConnection.disabledDialog.title"),
                    BasisLocalization.Get("menu.individualPlayer.directConnection.disabledDialog.body"),
                    BasisLocalization.Get("ui.ok"),
                    _ => { },
                    severity: BasisPanelSeverity.Caution);
                return;
            }

            // Hot: confirming this hands both sides each other's IP address.
            BasisMainMenu.Instance.OpenDialogue(
                BasisLocalization.Get("menu.individualPlayer.directConnection.outgoingDialog.title"),
                BasisLocalization.Get("menu.individualPlayer.directConnection.outgoingDialog.body", player.DisplayName, player.UUID),
                BasisLocalization.Get("menu.individualPlayer.directConnection.request"),
                BasisLocalization.Get("ui.cancel"),
                confirmed =>
                {
                    if (!confirmed) return;
                    BasisP2PManager.SendRequest(playerId);
                },
                severity: BasisPanelSeverity.Hot);
        }

        /// <summary>
        /// Flips this player's avatar visibility and reloads them. Clearing the failed-load flags
        /// is what makes showing an avatar again actually re-attempt the download — without it a
        /// player who tripped <see cref="BasisRemotePlayer.HasFailedAvatarLoadGlobally"/> stays
        /// invisible no matter how often the button is pressed.
        /// </summary>
        private static async System.Threading.Tasks.Task<BasisPlayerSettingsData> ToggleAvatarVisible(BasisRemotePlayer player)
        {
            var settings = await BasisPlayerSettingsManager.RequestPlayerSettings(player.UUID);
            settings.AvatarVisible = !settings.AvatarVisible;
            await BasisPlayerSettingsManager.SetPlayerSettings(settings);

            if (player != null)
            {
                player.HasFailedAvatarLoadGlobally = false;
                player.AvatarLoadErrorMessage = null;
                player.OnAvatarFailedStateChanged?.Invoke();
                player.ReloadAvatar();
            }

            return settings;
        }

        /// <summary>
        /// Persists the jiggle-grab permission and pushes it onto the live player. Turning it off
        /// revokes any grab already in flight rather than waiting for the grabber to let go.
        /// </summary>
        private static async System.Threading.Tasks.Task<BasisPlayerSettingsData> SetJiggleGrabAllowed(BasisRemotePlayer player, bool allowed)
        {
            var settings = await BasisPlayerSettingsManager.RequestPlayerSettings(player.UUID);
            settings.JiggleGrabAllowed = allowed;
            await BasisPlayerSettingsManager.SetPlayerSettings(settings);

            if (player != null)
            {
                player.JiggleGrabAllowed = allowed;
                if (!allowed && BasisNetworkPlayers.PlayerToNetworkedPlayer(player, out BasisNetworkPlayer net))
                {
                    Basis.Scripts.BasisSdk.Interactions.BasisJiggleGrabDriver.RevokePlayer(net.playerId);
                }
            }

            return settings;
        }

        /// <summary>
        /// Applies this player's eye height to the local avatar. Custom scale has to be on first
        /// or the applied height is ignored, so the toggle is flipped for the user rather than
        /// failing silently.
        /// </summary>
        private static bool ApplyMatchedEyeHeight(BasisRemotePlayer player, out float eyeHeight)
        {
            if (!BasisHeightDriver.TryGetMatchedEyeHeightOverrideMeters(player, out eyeHeight))
            {
                BasisDebug.LogWarning("Cannot match eye height because the selected remote avatar eye height is unavailable.", BasisDebug.LogTag.Avatar);
                return false;
            }

            if (!SMModuleCalibration.ApplyCustomScale)
            {
                BasisSettingsDefaults.CustomScale.SetValue(true);
                SMModuleCalibration.ApplyCustomScale = true;
            }

            BasisHeightDriver.ApplyRuntimeOscEyeHeightOverride(eyeHeight);
            return true;
        }

        /// <summary>
        /// Builds the Actions tab — the panel's landing page. Every row is a single press for
        /// something that otherwise sits several tabs deep; nothing here is exclusive to this tab,
        /// so the detail tabs remain the place to go for the surrounding context and settings.
        /// Rows carry no inline description so the whole set fits on one screen — the long-form
        /// text the detail tabs show moves onto the hover tooltip instead.
        /// </summary>
        private static void BuildActionsPage(PanelTabPage page, BasisRemotePlayer player, BasisPlayerSettingsData settings, PlayerActionSync sync)
        {
            RectTransform root = page.Descriptor.ContentParent;

            var group = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, root);
            group.SetTitle(BasisLocalization.Get("menu.individualPlayer.actions"));
            group.SetDescription(BasisLocalization.Get("menu.individualPlayer.actions.description"));
            RectTransform content = group.ContentParent;

            PanelButton NewAction(string tooltipKey)
            {
                PanelButton button = PanelButton.CreateNew(content);
                button.Descriptor.SetDescription(string.Empty);
                button.Descriptor.SetTooltip(BasisLocalization.Get(tooltipKey));
                return button;
            }

            // ---- Block ----
            PanelButton blockBtn = NewAction("menu.individualPlayer.block.button.description");
            void PaintBlock(BasisPlayerSettingsData s)
            {
                if (blockBtn == null || blockBtn.Descriptor == null) return;
                blockBtn.Descriptor.SetTitle(BasisLocalization.Get(
                    s.IsBlocked ? "menu.individualPlayer.unblock" : "menu.individualPlayer.blockButton"));
            }
            PaintBlock(settings);
            sync.Blocked += PaintBlock;
            blockBtn.OnClicked += async () =>
            {
                await ToggleBlockWithConfirmation(player);
                sync.Blocked?.Invoke(await BasisPlayerSettingsManager.RequestPlayerSettings(player.UUID));
            };

            // ---- Mute ----
            PanelButton muteBtn = NewAction("menu.individualPlayer.mute.description");
            void PaintMute(BasisPlayerSettingsData s)
            {
                if (muteBtn == null || muteBtn.Descriptor == null) return;
                muteBtn.Descriptor.SetTitle(BasisLocalization.Get(
                    s.VolumeLevel <= 0f ? "menu.individualPlayer.unmute" : "menu.individualPlayer.mute"));
            }
            PaintMute(settings);
            sync.Volume += PaintMute;
            muteBtn.OnClicked += async () =>
            {
                await ToggleMute(player);
                sync.Volume?.Invoke(await BasisPlayerSettingsManager.RequestPlayerSettings(player.UUID));
            };

            // ---- Direct connection ----
            bool hasNetTarget = BasisNetworkPlayers.PlayerToNetworkedPlayer(player, out BasisNetworkPlayer netTarget);
            ushort netPlayerId = hasNetTarget ? netTarget.playerId : (ushort)0;

            PanelButton directConnBtn = NewAction("menu.individualPlayer.directConnection.description");
            void PaintDirectConn()
            {
                if (directConnBtn == null || directConnBtn.Descriptor == null) return;
                P2PState state = BasisP2PManager.GetSessionState(netPlayerId);
                string key;
                if (DirectConnectionBlocked(state))
                {
                    key = "menu.individualPlayer.directConnection.disabled";
                }
                else
                {
                    key = state == P2PState.Idle || state == P2PState.Failed
                        ? "menu.individualPlayer.directConnection.request"
                        : "menu.individualPlayer.directConnection.cancel";
                }
                directConnBtn.Descriptor.SetTitle(BasisLocalization.Get(key));
            }
            PaintDirectConn();
            sync.DirectConnection += PaintDirectConn;
            directConnBtn.OnClicked += () =>
            {
                ToggleDirectConnection(player);
                sync.DirectConnection?.Invoke();
            };

            // ---- Pin ----
            string uuid = player.UUID;
            PanelButton pinBtn = NewAction("menu.individualPlayer.pin.button.description");
            void PaintPin()
            {
                if (pinBtn == null || pinBtn.Descriptor == null) return;
                pinBtn.Descriptor.SetTitle(BasisLocalization.Get(
                    PinnedPlayers.IsPinned(uuid) ? "menu.individualPlayer.unpin" : "menu.individualPlayer.pinButton"));
            }
            PaintPin();
            sync.Pin += PaintPin;
            pinBtn.OnClicked += () =>
            {
                PinnedPlayers.Toggle(uuid);
                sync.Pin?.Invoke();
            };

            // ---- Highlight ----
            PanelButton highlightBtn = NewAction("menu.individualPlayer.highlight.description");
            void PaintHighlight()
            {
                if (highlightBtn == null || highlightBtn.Descriptor == null) return;
                highlightBtn.Descriptor.SetTitle(BasisLocalization.Get(
                    IsHighlighted(player) ? "menu.individualPlayer.removeHighlight" : "menu.individualPlayer.highlight"));
            }
            PaintHighlight();
            sync.Highlight += PaintHighlight;
            highlightBtn.OnClicked += () =>
            {
                if (hasNetTarget) SetHighlight(netTarget);
                sync.Highlight?.Invoke();
            };

            // ---- Group chat (the private voice set) and private chat (this person only) ----
            PanelButton groupChatBtn = NewAction("menu.individualPlayer.privateChat.add.description");
            PanelButton privateChatBtn = NewAction("menu.individualPlayer.talkToOnly.description");
            void PaintTalkModes()
            {
                if (groupChatBtn != null && groupChatBtn.Descriptor != null)
                {
                    groupChatBtn.Descriptor.SetTitle(BasisLocalization.Get(
                        hasNetTarget && BasisTalkModeManager.IsPrivateMember(netPlayerId)
                            ? "menu.individualPlayer.privateChat.remove"
                            : "menu.individualPlayer.privateChat.add"));
                }
                if (privateChatBtn != null && privateChatBtn.Descriptor != null)
                {
                    privateChatBtn.Descriptor.SetTitle(BasisLocalization.Get(
                        hasNetTarget && BasisTalkModeManager.IsTalkingOnlyTo(netPlayerId)
                            ? "menu.individualPlayer.talkToOnly.stop"
                            : "menu.individualPlayer.talkToOnly"));
                }
            }
            PaintTalkModes();
            sync.TalkModes += PaintTalkModes;

            groupChatBtn.OnClicked += () =>
            {
                if (!hasNetTarget) return;
                BasisTalkModeManager.TogglePrivateMember(netPlayerId);
                sync.TalkModes?.Invoke();
            };

            privateChatBtn.OnClicked += () =>
            {
                if (!hasNetTarget) return;
                if (BasisTalkModeManager.IsTalkingOnlyTo(netPlayerId))
                    BasisTalkModeManager.StopThisPerson();
                else
                    BasisTalkModeManager.SetThisPersonTarget(netPlayerId);
                sync.TalkModes?.Invoke();
            };

            // ---- Match eye height ----
            PanelButton eyeHeightBtn = NewAction("menu.individualPlayer.matchEyeHeight.unavailable");
            eyeHeightBtn.Descriptor.SetTitle(BasisLocalization.Get("menu.individualPlayer.matchEyeHeight"));
            void PaintEyeHeight()
            {
                if (eyeHeightBtn == null || eyeHeightBtn.Descriptor == null) return;
                eyeHeightBtn.Descriptor.SetTooltip(
                    BasisHeightDriver.TryGetMatchedEyeHeightOverrideMeters(player, out float height)
                        ? BasisLocalization.Get("menu.individualPlayer.matchEyeHeight.description", height)
                        : BasisLocalization.Get("menu.individualPlayer.matchEyeHeight.unavailable"));
            }
            PaintEyeHeight();
            sync.EyeHeight += PaintEyeHeight;
            eyeHeightBtn.OnClicked += () =>
            {
                ApplyMatchedEyeHeight(player, out _);
                sync.EyeHeight?.Invoke();
            };

            // ---- Hide / show avatar ----
            PanelButton avatarBtn = NewAction("menu.individualPlayer.toggleAvatar.description");
            void PaintAvatarVisible(BasisPlayerSettingsData s)
            {
                if (avatarBtn == null || avatarBtn.Descriptor == null) return;
                avatarBtn.Descriptor.SetTitle(BasisLocalization.Get(
                    s.AvatarVisible ? "menu.individualPlayer.hideAvatar" : "menu.individualPlayer.showAvatar"));
            }
            PaintAvatarVisible(settings);
            sync.AvatarVisible += PaintAvatarVisible;
            avatarBtn.OnClicked += async () =>
            {
                sync.AvatarVisible?.Invoke(await ToggleAvatarVisible(player));
            };

            // ---- Jiggle grabbing ----
            PanelToggle jiggleGrabToggle = PanelToggle.CreateNewEntry(content);
            jiggleGrabToggle.Descriptor.SetTitle(BasisLocalization.Get("menu.individualPlayer.jiggleGrab"));
            jiggleGrabToggle.Descriptor.SetDescription(string.Empty);
            jiggleGrabToggle.Descriptor.SetTooltip(BasisLocalization.Get("menu.individualPlayer.jiggleGrab.description"));
            jiggleGrabToggle.SetValueWithoutNotify(settings.JiggleGrabAllowed);
            sync.JiggleGrab += s =>
            {
                if (jiggleGrabToggle == null) return;
                jiggleGrabToggle.SetValueWithoutNotify(s.JiggleGrabAllowed);
            };
            jiggleGrabToggle.OnValueChanged += async allowed =>
            {
                sync.JiggleGrab?.Invoke(await SetJiggleGrabAllowed(player, allowed));
            };

            // ---- Shout mode (admin only, same gate the Admin tab uses) ----
            if (hasNetTarget && BasisTalkModeManager.LocalCanShout())
            {
                PanelButton shoutBtn = NewAction("menu.individualPlayer.shout.description");
                void PaintShout()
                {
                    if (shoutBtn == null || shoutBtn.Descriptor == null) return;
                    shoutBtn.Descriptor.SetTitle(BasisLocalization.Get(
                        BasisShoutAudioDriver.IsInShoutMode(netPlayerId)
                            ? "menu.individualPlayer.shout.disable"
                            : "menu.individualPlayer.shout.enable"));
                }
                PaintShout();
                sync.Shout += PaintShout;
                // No repaint here — shout is a server round trip, so RunAction repaints from
                // BasisNetworkModeration.OnShoutModeChanged once the change actually lands.
                shoutBtn.OnClicked += () =>
                {
                    if (BasisShoutAudioDriver.IsInShoutMode(netPlayerId))
                        BasisNetworkModeration.DisableShoutMode(netPlayerId);
                    else
                        BasisNetworkModeration.EnableShoutMode(netPlayerId);
                };
            }
        }
    }
}
