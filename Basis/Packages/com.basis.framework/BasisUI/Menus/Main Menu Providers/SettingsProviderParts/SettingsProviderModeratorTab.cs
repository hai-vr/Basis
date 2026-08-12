using Basis.Scripts.BasisCharacterController;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    /// <summary>
    /// Per-user moderation tab — player list, kicks/bans/IP-bans/unbans,
    /// teleports, direct messages, broadcast, and shout-mode toggles.
    /// Server config and other persistent admin tools live on the Admin tab.
    /// </summary>
    public static class SettingsProviderModeratorTab
    {
        /// <summary>Bitrate the per-player override slider starts on before an admin moves it.</summary>
        private const int DefaultPlayerOpusBitrate = 32000;

        public static PanelTabPage ModeratorTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;

            descriptor.SetIcon(AddressableAssets.Sprites.Settings);
            descriptor.SetTitle(BasisLocalization.Get("settings.moderator.title"));

            RectTransform container = descriptor.ContentParent;

            // --- Player list group ---
            PanelElementDescriptor playersGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            playersGroup.SetTitle(BasisLocalization.Get("menu.provider.players"));

            ModeratorTabController controller = tab.gameObject.AddComponent<ModeratorTabController>();
            controller.PlayerListParent = playersGroup.ContentParent;

            PanelTextField playerSearch = PanelTextField.CreateNewEntry(playersGroup.ContentParent);
            playerSearch.Descriptor.SetTitle(BasisLocalization.Get("ui.search.label"));
            playerSearch.Descriptor.SetTooltip(BasisLocalization.Get("ui.search.label.tooltip"));
            playerSearch.OnValueChanged += controller.OnSearchChanged;
            controller.SearchField = playerSearch;

            PanelButton refreshPlayers = PanelButton.CreateNew(playersGroup.ContentParent);
            refreshPlayers.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.refreshPlayers"));
            refreshPlayers.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.refreshPlayers.tooltip"));
            refreshPlayers.OnClicked += controller.RebuildPlayerList;

            PanelToggle autoRefreshToggle = PanelToggle.CreateNewEntry(playersGroup.ContentParent);
            autoRefreshToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.autoRefresh"));
            autoRefreshToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.autoRefresh.tooltip"));
            autoRefreshToggle.AssignBinding(BasisSettingsDefaults.AdminAutoRefreshPlayerList);

            // --- Target group ---
            PanelElementDescriptor targetGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            targetGroup.SetTitle(BasisLocalization.Get("settings.admin.target"));

            PanelTextField uuidField = PanelTextField.CreateNewEntry(targetGroup.ContentParent);
            uuidField.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.uuidTarget"));
            uuidField.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.uuidTarget.tooltip"));

            PanelTextField reasonField = PanelTextField.CreateNewEntry(targetGroup.ContentParent);
            reasonField.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.reason"));
            reasonField.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.reason.tooltip"));

            TMP_InputField reasonInput = reasonField.GetComponentInChildren<TMP_InputField>(true);
            if (reasonInput)
            {
                reasonInput.lineType = TMP_InputField.LineType.MultiLineNewline;
                reasonInput.scrollSensitivity = 2f;
            }

            controller.UUIDField = uuidField;
            controller.ReasonField = reasonField;

            // --- Actions group ---
            PanelElementDescriptor actionsGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            actionsGroup.SetTitle(BasisLocalization.Get("settings.admin.actions"));

            // Teleport
            PanelButton teleportToSelected = PanelButton.CreateNew(actionsGroup.ContentParent);
            teleportToSelected.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.teleportTo"));
            teleportToSelected.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.teleportTo.tooltip"));
            GuardedClick(teleportToSelected, BasisLocalization.Get("settings.admin.confirm.teleportTo.title"),
                BasisLocalization.Get("settings.admin.confirm.teleportTo.body"),
                BasisLocalization.Get("settings.admin.confirm.teleportTo.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisNetworkModeration.TryTeleportToPlayer(target.playerId);
                });

            PanelButton teleportAll = PanelButton.CreateNew(actionsGroup.ContentParent);
            teleportAll.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.teleportAll"));
            teleportAll.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.teleportAll.tooltip"));
            GuardedClick(teleportAll, BasisLocalization.Get("settings.admin.confirm.teleportAll.title"),
                BasisLocalization.Get("settings.admin.confirm.teleportAll.body"),
                BasisLocalization.Get("settings.admin.confirm.teleportAll.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisNetworkModeration.TeleportAll(target.playerId);
                });

            PanelButton teleportHere = PanelButton.CreateNew(actionsGroup.ContentParent);
            teleportHere.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.teleportHere"));
            teleportHere.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.teleportHere.tooltip"));
            GuardedClick(teleportHere, BasisLocalization.Get("settings.admin.confirm.teleportHere.title"),
                BasisLocalization.Get("settings.admin.confirm.teleportHere.body"),
                BasisLocalization.Get("settings.admin.confirm.teleportHere.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisNetworkModeration.TeleportHere(target.playerId);
                });

            // Moderation
            PanelButton ban = PanelButton.CreateNew(actionsGroup.ContentParent);
            ban.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.banUuid"));
            ban.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.banUuid.tooltip"));
            GuardedClick(ban, BasisLocalization.Get("settings.admin.confirm.ban.title"),
                BasisLocalization.Get("settings.admin.confirm.ban.body"),
                BasisLocalization.Get("settings.admin.confirm.ban.confirm"),
                () =>
                {
                    string uuid = controller.GetUUIDText();
                    if (string.IsNullOrWhiteSpace(uuid)) { BasisDebug.LogError("UUID is empty."); return; }
                    BasisNetworkModeration.SendBan(uuid, controller.GetReasonText());
                });

            PanelButton kick = PanelButton.CreateNew(actionsGroup.ContentParent);
            kick.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.kickUuid"));
            kick.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.kickUuid.tooltip"));
            GuardedClick(kick, BasisLocalization.Get("settings.admin.confirm.kick.title"),
                BasisLocalization.Get("settings.admin.confirm.kick.body"),
                BasisLocalization.Get("settings.admin.confirm.kick.confirm"),
                () =>
                {
                    string uuid = controller.GetUUIDText();
                    if (string.IsNullOrWhiteSpace(uuid)) { BasisDebug.LogError("UUID is empty."); return; }
                    BasisNetworkModeration.SendKick(uuid, controller.GetReasonText());
                });

            PanelButton ipBan = PanelButton.CreateNew(actionsGroup.ContentParent);
            ipBan.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.ipBanUuid"));
            ipBan.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.ipBanUuid.tooltip"));
            GuardedClick(ipBan, BasisLocalization.Get("settings.admin.confirm.ipBan.title"),
                BasisLocalization.Get("settings.admin.confirm.ipBan.body"),
                BasisLocalization.Get("settings.admin.confirm.ipBan.confirm"),
                () =>
                {
                    string uuid = controller.GetUUIDText();
                    if (string.IsNullOrWhiteSpace(uuid)) { BasisDebug.LogError("UUID is empty."); return; }
                    BasisNetworkModeration.SendIPBan(uuid, controller.GetReasonText());
                });

            PanelButton unban = PanelButton.CreateNew(actionsGroup.ContentParent);
            unban.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.unbanUuid"));
            unban.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.unbanUuid.tooltip"));
            GuardedClick(unban, BasisLocalization.Get("settings.admin.confirm.unban.title"),
                BasisLocalization.Get("settings.admin.confirm.unban.body"),
                BasisLocalization.Get("settings.admin.confirm.unban.confirm"),
                () =>
                {
                    string uuid = controller.GetUUIDText();
                    if (string.IsNullOrWhiteSpace(uuid)) { BasisDebug.LogError("UUID is empty."); return; }
                    BasisNetworkModeration.UnBan(uuid);
                });

            // An IP ban is stored against the banned UUID's recorded address, so lifting it needs
            // its own command — a plain Unban leaves the address blocked.
            PanelButton unIpBan = PanelButton.CreateNew(actionsGroup.ContentParent);
            unIpBan.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.unIpBanUuid"));
            unIpBan.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.unIpBanUuid.tooltip"));
            GuardedClick(unIpBan, BasisLocalization.Get("settings.admin.confirm.unIpBan.title"),
                BasisLocalization.Get("settings.admin.confirm.unIpBan.body"),
                BasisLocalization.Get("settings.admin.confirm.unIpBan.confirm"),
                () =>
                {
                    string uuid = controller.GetUUIDText();
                    if (string.IsNullOrWhiteSpace(uuid)) { BasisDebug.LogError("UUID is empty."); return; }
                    BasisNetworkModeration.UnIpBan(uuid);
                });

            // Messaging
            PanelButton sendMessage = PanelButton.CreateNew(actionsGroup.ContentParent);
            sendMessage.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.sendMessageUuid"));
            sendMessage.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.sendMessageUuid.tooltip"));
            GuardedClick(sendMessage, BasisLocalization.Get("settings.admin.confirm.sendMessage.title"),
                BasisLocalization.Get("settings.admin.confirm.sendMessage.body"),
                BasisLocalization.Get("settings.admin.confirm.sendMessage.confirm"),
                () =>
                {
                    string uuid = controller.GetUUIDText();
                    if (string.IsNullOrWhiteSpace(uuid)) { BasisDebug.LogError("UUID is empty."); return; }
                    if (controller.TryFindId(uuid, out ushort id))
                        BasisNetworkModeration.SendMessage(id, controller.GetReasonText());
                    else
                        BasisDebug.LogError("Can't find ID for UUID: " + uuid);
                });

            PanelButton sendAll = PanelButton.CreateNew(actionsGroup.ContentParent);
            sendAll.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.sendAll"));
            sendAll.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.sendAll.tooltip"));
            GuardedClick(sendAll, BasisLocalization.Get("settings.admin.confirm.sendAll.title"),
                BasisLocalization.Get("settings.admin.confirm.sendAll.body"),
                BasisLocalization.Get("settings.admin.confirm.sendAll.confirm"),
                () =>
                {
                    string msg = controller.GetReasonText();
                    if (string.IsNullOrWhiteSpace(msg)) { BasisDebug.LogError("Message/Reason is empty."); return; }
                    BasisNetworkModeration.SendMessageAll(msg);
                });

            // Shout
            PanelButton enableShout = PanelButton.CreateNew(actionsGroup.ContentParent);
            enableShout.Descriptor.SetTitle(BasisLocalization.Get("menu.individualPlayer.shout.enable"));
            enableShout.Descriptor.SetTooltip(BasisLocalization.Get("menu.individualPlayer.shout.enable.tooltip"));
            GuardedClick(enableShout, BasisLocalization.Get("settings.admin.confirm.shoutEnable.title"),
                BasisLocalization.Get("settings.admin.confirm.shoutEnable.body"),
                BasisLocalization.Get("settings.admin.confirm.shoutEnable.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisNetworkModeration.EnableShoutMode(target.playerId);
                });

            PanelButton disableShout = PanelButton.CreateNew(actionsGroup.ContentParent);
            disableShout.Descriptor.SetTitle(BasisLocalization.Get("menu.individualPlayer.shout.disable"));
            disableShout.Descriptor.SetTooltip(BasisLocalization.Get("menu.individualPlayer.shout.disable.tooltip"));
            GuardedClick(disableShout, BasisLocalization.Get("settings.admin.confirm.shoutDisable.title"),
                BasisLocalization.Get("settings.admin.confirm.shoutDisable.body"),
                BasisLocalization.Get("settings.admin.confirm.shoutDisable.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisNetworkModeration.DisableShoutMode(target.playerId);
                });

            // Full-quality broadcast
            PanelButton enableFullQuality = PanelButton.CreateNew(actionsGroup.ContentParent);
            enableFullQuality.Descriptor.SetTitle(BasisLocalization.Get("menu.individualPlayer.fullquality.enable"));
            enableFullQuality.Descriptor.SetTooltip(BasisLocalization.Get("menu.individualPlayer.fullquality.enable.tooltip"));
            GuardedClick(enableFullQuality, BasisLocalization.Get("settings.admin.confirm.fullQualityEnable.title"),
                BasisLocalization.Get("settings.admin.confirm.fullQualityEnable.body"),
                BasisLocalization.Get("settings.admin.confirm.fullQualityEnable.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisNetworkModeration.SetFullQualityBroadcast(target.playerId, true);
                });

            PanelButton disableFullQuality = PanelButton.CreateNew(actionsGroup.ContentParent);
            disableFullQuality.Descriptor.SetTitle(BasisLocalization.Get("menu.individualPlayer.fullquality.disable"));
            disableFullQuality.Descriptor.SetTooltip(BasisLocalization.Get("menu.individualPlayer.fullquality.disable.tooltip"));
            GuardedClick(disableFullQuality, BasisLocalization.Get("settings.admin.confirm.fullQualityDisable.title"),
                BasisLocalization.Get("settings.admin.confirm.fullQualityDisable.body"),
                BasisLocalization.Get("settings.admin.confirm.fullQualityDisable.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisNetworkModeration.SetFullQualityBroadcast(target.playerId, false);
                });

            // --- Force avatar ---
            // Offers this server's handed-out avatars plus the moderator's own saved ones. Only the
            // url and password travel; the target loads the bundle itself, so it can only be sent an
            // avatar it is able to fetch on its own.
            PanelElementDescriptor avatarGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            avatarGroup.SetTitle(BasisLocalization.Get("settings.admin.forceAvatar"));

            PanelDropdown avatarDropdown = PanelDropdown.CreateNewEntry(avatarGroup.ContentParent);
            avatarDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.forceAvatar.pick"));
            avatarDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.forceAvatar.pick.tooltip"));
            controller.AvatarDropdown = avatarDropdown;

            PanelButton refreshAvatars = PanelButton.CreateNew(avatarGroup.ContentParent);
            refreshAvatars.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.forceAvatar.refresh"));
            refreshAvatars.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.forceAvatar.refresh.tooltip"));
            refreshAvatars.OnClicked += controller.RebuildAvatarList;

            PanelButton forceAvatar = PanelButton.CreateNew(avatarGroup.ContentParent);
            forceAvatar.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.forceAvatar.apply"));
            forceAvatar.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.forceAvatar.apply.tooltip"));
            GuardedClick(forceAvatar, BasisLocalization.Get("settings.admin.confirm.forceAvatar.title"),
                BasisLocalization.Get("settings.admin.confirm.forceAvatar.body"),
                BasisLocalization.Get("settings.admin.confirm.forceAvatar.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    if (!controller.TryGetSelectedAvatar(out ForceAvatarCatalog.Entry entry))
                    {
                        BasisDebug.LogError("No avatar selected.");
                        return;
                    }
                    BasisNetworkModeration.ForceAvatar(target.playerId, entry.Item);
                });

            // Ignores the selected player entirely — this is the whole instance, so the confirmation
            // spells that out rather than reusing the single-target wording.
            PanelButton forceAvatarAll = PanelButton.CreateNew(avatarGroup.ContentParent);
            forceAvatarAll.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.forceAvatar.applyAll"));
            forceAvatarAll.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.forceAvatar.applyAll.tooltip"));
            GuardedClick(forceAvatarAll, BasisLocalization.Get("settings.admin.confirm.forceAvatarAll.title"),
                BasisLocalization.Get("settings.admin.confirm.forceAvatarAll.body"),
                BasisLocalization.Get("settings.admin.confirm.forceAvatarAll.confirm"),
                () =>
                {
                    if (!controller.TryGetSelectedAvatar(out ForceAvatarCatalog.Entry entry))
                    {
                        BasisDebug.LogError("No avatar selected.");
                        return;
                    }
                    BasisNetworkModeration.ForceAvatarAll(entry.Item);
                });

            // --- Per-player voice bitrate ---
            // Targets the runtime player id rather than a UUID, so it only applies to someone
            // currently connected. A per-user override wins over the server-wide bitrate.
            PanelElementDescriptor voiceGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            voiceGroup.SetTitle(BasisLocalization.Get("settings.admin.playerVoice"));

            PanelSlider bitrateSlider = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, voiceGroup.ContentParent);
            bitrateSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("settings.admin.playerOpusBitrate"), 6000f, 128000f, true, 0, ValueDisplayMode.Compact));
            bitrateSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.playerOpusBitrate.tooltip"));
            bitrateSlider.SetValueWithoutNotify(DefaultPlayerOpusBitrate);

            PanelButton applyBitrate = PanelButton.CreateNew(voiceGroup.ContentParent);
            applyBitrate.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.playerOpusBitrate.apply"));
            applyBitrate.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.playerOpusBitrate.apply.tooltip"));
            GuardedClick(applyBitrate, BasisLocalization.Get("settings.admin.confirm.bitrateApply.title"),
                BasisLocalization.Get("settings.admin.confirm.bitrateApply.body"),
                BasisLocalization.Get("settings.admin.confirm.bitrateApply.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisNetworkModeration.SetUserOpusBitrate(target.playerId, Mathf.RoundToInt(bitrateSlider.Value));
                });

            PanelButton clearBitrate = PanelButton.CreateNew(voiceGroup.ContentParent);
            clearBitrate.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.playerOpusBitrate.clear"));
            clearBitrate.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.playerOpusBitrate.clear.tooltip"));
            GuardedClick(clearBitrate, BasisLocalization.Get("settings.admin.confirm.bitrateClear.title"),
                BasisLocalization.Get("settings.admin.confirm.bitrateClear.body"),
                BasisLocalization.Get("settings.admin.confirm.bitrateClear.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisNetworkModeration.SetUserOpusBitrate(target.playerId, 0);
                });

            // --- Locomotion override ---
            // Targets the runtime player id like the bitrate override above, so it only reaches someone
            // currently connected. The "everyone" button ignores the selected player entirely.
            PanelSectionToggle locomotionSection = PanelSectionToggle.CreateNewEntry(container);
            locomotionSection.SetTitle(BasisLocalization.Get("settings.admin.locomotion"));
            int locomotionStart = container.childCount;

            PanelToggle jumpToggle = PanelToggle.CreateNew(container);
            jumpToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.locomotion.jumpHeight.override"));
            PanelSlider jumpSlider = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, container);
            jumpSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("settings.admin.locomotion.jumpHeight"), 0.1f, 5f, false, 2, ValueDisplayMode.Meters));
            jumpSlider.SetValueWithoutNotify(DefaultLocomotionJumpHeight);

            PanelToggle walkToggle = PanelToggle.CreateNew(container);
            walkToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.locomotion.walkSpeed.override"));
            PanelSlider walkSlider = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, container);
            walkSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("settings.admin.locomotion.walkSpeed"), 0f, 15f, false, 2, ValueDisplayMode.Raw));
            walkSlider.SetValueWithoutNotify(DefaultLocomotionWalkSpeed);

            PanelToggle runToggle = PanelToggle.CreateNew(container);
            runToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.locomotion.runSpeed.override"));
            PanelSlider runSlider = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, container);
            runSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("settings.admin.locomotion.runSpeed"), 0f, 20f, false, 2, ValueDisplayMode.Raw));
            runSlider.SetValueWithoutNotify(DefaultLocomotionRunSpeed);

            List<string> locomotionModes = BuildLocomotionModeEntries();
            PanelDropdown modeDropdown = PanelDropdown.CreateNewEntry(container);
            modeDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.locomotion.mode"));
            modeDropdown.AssignEntries(locomotionModes);
            modeDropdown.SetValueWithoutNotify(locomotionModes[0]);

            void ApplyLocomotionSliderVisibility()
            {
                jumpSlider.Descriptor.SetActive(jumpToggle.Value);
                walkSlider.Descriptor.SetActive(walkToggle.Value);
                runSlider.Descriptor.SetActive(runToggle.Value);
            }

            ApplyLocomotionSliderVisibility();
            jumpToggle.OnValueChanged += _ => { ApplyLocomotionSliderVisibility(); descriptor.ForceRebuild(); };
            walkToggle.OnValueChanged += _ => { ApplyLocomotionSliderVisibility(); descriptor.ForceRebuild(); };
            runToggle.OnValueChanged += _ => { ApplyLocomotionSliderVisibility(); descriptor.ForceRebuild(); };

            BasisLocomotionValues BuildLocomotionValues()
            {
                return ComposeLocomotionValues(
                    jumpToggle.Value, jumpSlider.Value,
                    walkToggle.Value, walkSlider.Value,
                    runToggle.Value, runSlider.Value,
                    locomotionModes.IndexOf(modeDropdown.Value));
            }

            PanelButton applyLocomotion = PanelButton.CreateNew(container);
            applyLocomotion.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.locomotion.apply"));
            applyLocomotion.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.locomotion.apply.tooltip"));
            GuardedClick(applyLocomotion, BasisLocalization.Get("settings.admin.confirm.locomotionApply.title"),
                BasisLocalization.Get("settings.admin.confirm.locomotionApply.body"),
                BasisLocalization.Get("settings.admin.confirm.locomotionApply.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisLocomotionValues values = BuildLocomotionValues();
                    if (values.Fields == BasisLocomotionField.None)
                    {
                        BasisDebug.LogError("No locomotion fields selected to override.");
                        return;
                    }
                    BasisNetworkModeration.SetLocomotionOverride(target.playerId, values);
                });

            PanelButton clearLocomotion = PanelButton.CreateNew(container);
            clearLocomotion.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.locomotion.clear"));
            clearLocomotion.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.locomotion.clear.tooltip"));
            GuardedClick(clearLocomotion, BasisLocalization.Get("settings.admin.confirm.locomotionClear.title"),
                BasisLocalization.Get("settings.admin.confirm.locomotionClear.body"),
                BasisLocalization.Get("settings.admin.confirm.locomotionClear.confirm"),
                () =>
                {
                    BasisNetworkPlayer target = controller.GetEffectivePlayer();
                    if (target == null) { BasisDebug.LogError("No player available."); return; }
                    BasisNetworkModeration.ClearLocomotionOverride(target.playerId);
                });

            // Ignores the selected player entirely — this is the whole instance, so the confirmation
            // spells that out rather than reusing the single-target wording.
            PanelButton applyLocomotionAll = PanelButton.CreateNew(container);
            applyLocomotionAll.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.locomotion.applyAll"));
            applyLocomotionAll.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.locomotion.applyAll.tooltip"));
            GuardedClick(applyLocomotionAll, BasisLocalization.Get("settings.admin.confirm.locomotionApplyAll.title"),
                BasisLocalization.Get("settings.admin.confirm.locomotionApplyAll.body"),
                BasisLocalization.Get("settings.admin.confirm.locomotionApplyAll.confirm"),
                () =>
                {
                    BasisLocomotionValues values = BuildLocomotionValues();
                    if (values.Fields == BasisLocomotionField.None)
                    {
                        BasisDebug.LogError("No locomotion fields selected to override.");
                        return;
                    }
                    BasisNetworkModeration.SetLocomotionOverrideAll(values);
                });

            PanelButton clearLocomotionAll = PanelButton.CreateNew(container);
            clearLocomotionAll.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.locomotion.clearAll"));
            clearLocomotionAll.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.locomotion.clearAll.tooltip"));
            GuardedClick(clearLocomotionAll, BasisLocalization.Get("settings.admin.confirm.locomotionClearAll.title"),
                BasisLocalization.Get("settings.admin.confirm.locomotionClearAll.body"),
                BasisLocalization.Get("settings.admin.confirm.locomotionClearAll.confirm"),
                BasisNetworkModeration.ClearLocomotionOverrideAll);

            PanelSectionToggleHelpers.FinalizeFlatSectionFromIndex(locomotionSection, container, locomotionStart, false,
                visible =>
                {
                    if (visible) ApplyLocomotionSliderVisibility();
                    descriptor.ForceRebuild();
                });

            controller.RebuildPlayerList();
            controller.RebuildAvatarList();
            descriptor.ForceRebuild();
            return tab;
        }

        /// <summary>Values the locomotion sliders start on before a moderator moves them.</summary>
        private const float DefaultLocomotionJumpHeight = 1.0f;
        private const float DefaultLocomotionWalkSpeed = 2.5f;
        private const float DefaultLocomotionRunSpeed = 4.0f;

        /// <summary>
        /// Movement-mode picker entries. Index 0 leaves the mode alone; the rest map onto
        /// <see cref="BasisLocalCharacterDriver.Mode"/> in declaration order.
        /// </summary>
        internal static List<string> BuildLocomotionModeEntries()
        {
            return new List<string>
            {
                BasisLocalization.Get("settings.admin.locomotion.mode.none"),
                BasisLocalization.Get("settings.admin.locomotion.mode.walk"),
                BasisLocalization.Get("settings.admin.locomotion.mode.fly"),
                BasisLocalization.Get("settings.admin.locomotion.mode.noclip"),
            };
        }

        /// <summary>
        /// Folds the toggle/slider state into a payload. <paramref name="modeIndex"/> is the picker index:
        /// 0 or below leaves the mode unclaimed.
        /// </summary>
        internal static BasisLocomotionValues ComposeLocomotionValues(
            bool overrideJump, float jumpHeight,
            bool overrideWalk, float walkSpeed,
            bool overrideRun, float runSpeed,
            int modeIndex)
        {
            BasisLocomotionValues values = default;

            if (overrideJump)
            {
                values.Fields |= BasisLocomotionField.JumpHeight;
                values.JumpHeight = jumpHeight;
            }
            if (overrideWalk)
            {
                values.Fields |= BasisLocomotionField.WalkSpeed;
                values.WalkSpeed = walkSpeed;
            }
            if (overrideRun)
            {
                values.Fields |= BasisLocomotionField.RunSpeed;
                values.RunSpeed = runSpeed;
            }
            if (modeIndex > 0)
            {
                values.Fields |= BasisLocomotionField.Mode;
                values.Mode = (BasisLocalCharacterDriver.Mode)(modeIndex - 1);
            }

            return values;
        }

        private static void WithConfirm(string title, string body, string confirmText, string cancelText, Action onConfirm)
        {
            if (BasisMainMenu.Instance == null)
            {
                BasisDebug.LogError("BasisMainMenu.Instance was null; cannot show confirmation dialog.");
                return;
            }
            BasisMainMenu.Instance.OpenDialogue(title, body, confirmText, cancelText, value =>
            {
                if (!value) return;
                onConfirm?.Invoke();
            });
        }

        private static void GuardedClick(PanelButton button, string title, string body, string confirmText,
            Action actionOnConfirm, string cancelText = null)
        {
            button.OnClicked += () => WithConfirm(title, body, confirmText,
                cancelText ?? BasisLocalization.Get("ui.cancel"), actionOnConfirm);
        }

        /// <summary>One row of the player list, kept so the row can be rebound to a different
        /// player instead of being destroyed and rebuilt.</summary>
        private sealed class PlayerRow
        {
            public BasisNetworkPlayer Player;
            public PanelButton Button;
            public bool Visible;
        }

        private sealed class ModeratorTabController : MonoBehaviour
        {
            public RectTransform PlayerListParent;
            public PanelTextField UUIDField;
            public PanelTextField ReasonField;
            public PanelTextField SearchField;
            public PanelDropdown AvatarDropdown;

            public BasisNetworkPlayer SelectedPlayer;
            private string _searchQuery = string.Empty;

            private readonly Dictionary<ushort, PlayerRow> _rows = new();
            private readonly List<ushort> _removeBuffer = new();
            private readonly List<ForceAvatarCatalog.Entry> _avatarEntries = new();

            // Rows a departed player left behind, rebound to the next arrival rather than
            // destroyed. A join used to tear down and re-instantiate the entire list.
            private readonly List<PlayerRow> _rowPool = new();
            private const int RowPoolCap = 32;

            // Opening the tab in a busy instance builds the whole roster at once; cap it and let
            // the following frames finish the tail.
            private const int FirstFrameRows = 24;
            private const int RowsPerFrame = 8;
            private int _lastAddFrame = -1;

            // Join, leave, refresh and keystrokes raise a flag; the list work happens once in
            // LateUpdate so a burst of arrivals costs the same as one.
            private bool _rosterDirty;
            private bool _filterDirty;

            public BasisNetworkPlayer GetEffectivePlayer()
            {
                return SelectedPlayer ?? BasisNetworkPlayer.LocalPlayer;
            }

            private void OnEnable()
            {
                // Moderator panel open → route every popup into the notification list.
                BasisNotificationCenter.BeginForcedScope();
                BasisNetworkPlayer.OnRemotePlayerJoined -= OnRemotePlayersChanged;
                BasisNetworkPlayer.OnRemotePlayerJoined += OnRemotePlayersChanged;
                BasisNetworkPlayer.OnRemotePlayerLeft -= OnRemotePlayersChanged;
                BasisNetworkPlayer.OnRemotePlayerLeft += OnRemotePlayersChanged;
                BasisServerProvidedItems.OnChanged -= RebuildAvatarList;
                BasisServerProvidedItems.OnChanged += RebuildAvatarList;
                RebuildPlayerList();
                Flush();
                RebuildAvatarList();
            }

            private void OnDisable()
            {
                // Moderator panel closed/hidden → resume normal popup handling.
                BasisNotificationCenter.EndForcedScope();
                BasisNetworkPlayer.OnRemotePlayerJoined -= OnRemotePlayersChanged;
                BasisNetworkPlayer.OnRemotePlayerLeft -= OnRemotePlayersChanged;
                BasisServerProvidedItems.OnChanged -= RebuildAvatarList;
            }

            private void OnDestroy()
            {
                BasisNetworkPlayer.OnRemotePlayerJoined -= OnRemotePlayersChanged;
                BasisNetworkPlayer.OnRemotePlayerLeft -= OnRemotePlayersChanged;
                BasisServerProvidedItems.OnChanged -= RebuildAvatarList;
                ClearAllRows();
            }

            private void OnRemotePlayersChanged(BasisNetworkPlayer _p1, BasisRemotePlayer _p2)
            {
                if (!BasisSettingsDefaults.AdminAutoRefreshPlayerList.RawValue) return;
                RebuildPlayerList();
            }

            public string GetUUIDText() => UUIDField != null ? UUIDField.Value ?? string.Empty : string.Empty;
            public string GetReasonText() => ReasonField != null ? ReasonField.Value ?? string.Empty : string.Empty;

            private void ClearAllRows()
            {
                foreach (var kvp in _rows)
                {
                    DestroyRow(kvp.Value);
                }
                _rows.Clear();

                for (int i = 0; i < _rowPool.Count; i++)
                {
                    DestroyRow(_rowPool[i]);
                }
                _rowPool.Clear();
            }

            public void OnSearchChanged(string query)
            {
                _searchQuery = query ?? string.Empty;
                _filterDirty = true;
            }

            /// <summary>
            /// Asks for the list to be brought in line with the roster. Wired to the Refresh
            /// button and to the join/leave events; the work itself happens in the next
            /// <see cref="Flush"/> so a burst of arrivals is one pass, not one pass each.
            /// </summary>
            public void RebuildPlayerList() => _rosterDirty = true;

            private void LateUpdate() => Flush();

            private void Flush()
            {
                if (!_rosterDirty && !_filterDirty) return;

                bool rosterChanged = false;
                if (_rosterDirty) rosterChanged = ReconcileRows();

                bool filterChanged = false;
                if (_filterDirty || rosterChanged) filterChanged = ApplyFilter();
                _filterDirty = false;

                if ((rosterChanged || filterChanged) && PlayerListParent)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(PlayerListParent);
                }
            }

            /// <summary>
            /// Brings the rows in line with <see cref="BasisNetworkPlayers.Players"/>, which is the
            /// authority — both join and leave events fire after that dictionary is updated.
            /// Returns true when a row was added or removed, which is the case that moves the
            /// group's height and so needs the layout rebuild.
            /// </summary>
            private bool ReconcileRows()
            {
                // The controller is attached before its fields are, so the first OnEnable can land
                // here with nothing to build into. Leave the flag set and pick it up next frame.
                if (!PlayerListParent) return false;

                bool changed = false;

                _removeBuffer.Clear();
                foreach (var kvp in _rows)
                {
                    if (!BasisNetworkPlayers.Players.ContainsKey(kvp.Key)) _removeBuffer.Add(kvp.Key);
                }
                for (int i = 0; i < _removeBuffer.Count; i++)
                {
                    ReleaseRow(_removeBuffer[i]);
                    changed = true;
                }

                // One chunk per frame however many times Flush runs — OnEnable calls it directly
                // and LateUpdate calls it again in the same frame.
                int budget = _lastAddFrame == Time.frameCount
                    ? 0
                    : _rows.Count == 0 ? FirstFrameRows : RowsPerFrame;

                bool complete = true;
                foreach (var kvp in BasisNetworkPlayers.Players)
                {
                    BasisNetworkPlayer player = kvp.Value;
                    if (player == null) continue;

                    if (_rows.TryGetValue(kvp.Key, out PlayerRow existing))
                    {
                        // Shout mode changes without a join or leave, and the Refresh button is
                        // how an admin picks that up — SetTitle no-ops when nothing moved.
                        existing.Player = player;
                        ApplyRowTitle(existing);
                        continue;
                    }

                    if (budget <= 0)
                    {
                        complete = false;
                        continue;
                    }

                    PlayerRow row = AcquireRow();
                    if (row == null) continue;

                    row.Player = player;
                    row.Visible = true;
                    row.Button.gameObject.SetActive(true);
                    ApplyRowTitle(row);

                    _rows[kvp.Key] = row;
                    _lastAddFrame = Time.frameCount;
                    budget--;
                    changed = true;
                }

                _rosterDirty = !complete;
                return changed;
            }

            private PlayerRow AcquireRow()
            {
                while (_rowPool.Count > 0)
                {
                    int last = _rowPool.Count - 1;
                    PlayerRow pooled = _rowPool[last];
                    _rowPool.RemoveAt(last);
                    if (pooled.Button != null) return pooled;
                }

                PanelButton button = PanelButton.CreateNew(PlayerListParent);
                if (button == null) return null;

                PlayerRow row = new PlayerRow { Button = button };
                // Assigned, not subscribed: a pooled row is rebound to a different player and
                // reads the current one off the row.
                button.OnClicked = () => SelectPlayer(row.Player);
                return row;
            }

            private void ReleaseRow(ushort playerId)
            {
                if (!_rows.TryGetValue(playerId, out PlayerRow row)) return;
                _rows.Remove(playerId);

                row.Player = null;
                row.Visible = false;
                if (row.Button == null) return;

                if (_rowPool.Count < RowPoolCap)
                {
                    row.Button.gameObject.SetActive(false);
                    // The list shares its parent with the search field, the Refresh button and the
                    // auto-refresh toggle, so park spare rows at the very end rather than leaving
                    // dead gaps among the live ones.
                    row.Button.transform.SetAsLastSibling();
                    _rowPool.Add(row);
                    return;
                }

                DestroyRow(row);
            }

            private static void DestroyRow(PlayerRow row)
            {
                if (row.Button == null) return;
                row.Button.OnClicked = null;
                row.Button.ReleaseInstance();
                row.Button = null;
            }

            private static void ApplyRowTitle(PlayerRow row)
            {
                BasisNetworkPlayer player = row.Player;
                if (player == null || row.Button == null) return;

                bool isLocal = BasisNetworkPlayer.LocalPlayer != null && player.playerId == BasisNetworkPlayer.LocalPlayer.playerId;
                bool isShouting = isLocal ? BasisNetworkModeration.LocalPlayerInShoutMode : BasisShoutAudioDriver.IsInShoutMode(player.playerId);
                string shoutTag = isShouting ? " [SHOUT]" : "";
                row.Button.Descriptor.SetTitle($"{player.playerId} > {player.SafeDisplayName}{shoutTag}");
            }

            public void RebuildAvatarList()
            {
                // Built on demand rather than cached: the server can push a new default library
                // mid-session and the moderator can save an avatar without leaving this tab.
                if (AvatarDropdown == null) return;

                _avatarEntries.Clear();
                _avatarEntries.AddRange(ForceAvatarCatalog.Build());
                ForceAvatarCatalog.Apply(AvatarDropdown, _avatarEntries);
            }

            public bool TryGetSelectedAvatar(out ForceAvatarCatalog.Entry entry)
            {
                return ForceAvatarCatalog.TryResolve(
                    _avatarEntries,
                    AvatarDropdown != null ? AvatarDropdown.Value : null,
                    out entry);
            }

            /// <summary>
            /// Returns true when a row's visibility actually changed — the only case that resizes
            /// the group and needs a layout rebuild.
            /// </summary>
            private bool ApplyFilter()
            {
                string query = _searchQuery.Trim();
                bool hasQuery = query.Length > 0;
                bool changed = false;

                foreach (var kvp in _rows)
                {
                    PlayerRow row = kvp.Value;
                    if (row.Button == null) continue;

                    // Ordinal-ignore-case rather than lowercasing both sides: the old form
                    // allocated two strings per row per keystroke.
                    bool show = !hasQuery || (row.Player != null && !string.IsNullOrEmpty(row.Player.SafeDisplayName)
                        && row.Player.SafeDisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (row.Visible == show) continue;
                    row.Visible = show;
                    row.Button.gameObject.SetActive(show);
                    changed = true;
                }

                return changed;
            }

            private void SelectPlayer(BasisNetworkPlayer player)
            {
                // A row reads its player off the row rather than off a captured local, so a click
                // landing on a row whose player is already gone has to be survivable.
                if (player == null || player.Player == null) return;

                SelectedPlayer = player;
                if (UUIDField != null)
                    UUIDField.SetValueWithoutNotify(SelectedPlayer.Player.UUID);

                // Forward selection so the Permissions section on the Admin tab can autofill.
                SettingsProviderAdminTab.RaisePlayerUuidSelected(SelectedPlayer.Player.UUID);
            }

            public bool TryFindId(string uuid, out ushort id)
            {
                foreach (BasisNetworkPlayer player in BasisNetworkPlayers.Players.Values)
                {
                    if (uuid == player.Player.UUID)
                    {
                        id = player.playerId;
                        return true;
                    }
                }
                id = 0;
                return false;
            }
        }
    }
}
