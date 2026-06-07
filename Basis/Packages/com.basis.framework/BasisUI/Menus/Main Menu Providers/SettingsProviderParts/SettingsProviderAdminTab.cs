using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using BasisNetworkCore.Security;
using System;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Admin tab — server-level configuration that persists to disk.
    /// Per-user moderation lives in <see cref="SettingsProviderModeratorTab"/>.
    /// </summary>
    public static class SettingsProviderAdminTab
    {
        /// <summary>Fired when a player is selected in the moderator player list. Carries the UUID.</summary>
        public static event Action<string> OnPlayerUuidSelected;

        /// <summary>Allow the Moderator tab (separate file) to fan-out player selection
        /// to the Permissions section that still lives on this tab.</summary>
        public static void RaisePlayerUuidSelected(string uuid) => OnPlayerUuidSelected?.Invoke(uuid);

        public static PanelTabPage AdminTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;

            descriptor.SetIcon(AddressableAssets.Sprites.Settings);
            descriptor.SetTitle(BasisLocalization.Get("settings.admin.title"));

            RectTransform container = descriptor.ContentParent;

            AdminTabController controller = tab.gameObject.AddComponent<AdminTabController>();

            // --- Menu-bar shout (local opt-in; off by default) ---
            PanelElementDescriptor shoutGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            shoutGroup.SetTitle(BasisLocalization.Get("settings.admin.title.shout"));
            shoutGroup.SetDescription("Local-only preference for your own menu. Does not affect other players or the server.");

            PanelToggle shoutOnMenuBarToggle = PanelToggle.CreateNewEntry(shoutGroup.ContentParent);
            shoutOnMenuBarToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.showShoutOnMenuBar"));
            shoutOnMenuBarToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.showShoutOnMenuBar.tooltip"));
            shoutOnMenuBarToggle.Descriptor.SetDescription("Adds the Shout option to the mic-mode button on your main menu bar. Off by default, so the button stays hidden until you enable it here.");
            shoutOnMenuBarToggle.AssignBinding(BasisSettingsDefaults.ShoutShowOnMenuBar);

            // --- Global lock group ---
            PanelElementDescriptor lockGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            lockGroup.SetTitle(BasisLocalization.Get("settings.admin.title.globalContentLocks"));
            lockGroup.SetDescription("Globally disable loading for all non-admin players. Everyone is notified.");

            PanelToggle avatarLock = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            avatarLock.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.lockAvatars"));
            avatarLock.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.lockAvatars.tooltip"));
            avatarLock.Descriptor.SetDescription("Prevents all non-admin avatar loading over the network.");
            avatarLock.SetValueWithoutNotify(BasisNetworkModeration.GlobalAvatarsLocked);
            avatarLock.OnValueChanged += _ => BasisNetworkModeration.GlobalToggleAvatars();

            PanelToggle propLock = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            propLock.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.lockProps"));
            propLock.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.lockProps.tooltip"));
            propLock.Descriptor.SetDescription("Prevents all non-admin prop loading over the network.");
            propLock.SetValueWithoutNotify(BasisNetworkModeration.GlobalPropsLocked);
            propLock.OnValueChanged += _ => BasisNetworkModeration.GlobalToggleProps();

            PanelToggle worldLock = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            worldLock.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.lockWorlds"));
            worldLock.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.lockWorlds.tooltip"));
            worldLock.Descriptor.SetDescription("Prevents all non-admin world loading over the network.");
            worldLock.SetValueWithoutNotify(BasisNetworkModeration.GlobalWorldsLocked);
            worldLock.OnValueChanged += _ => BasisNetworkModeration.GlobalToggleWorlds();

            PanelToggle serverShareLock = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            serverShareLock.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.lockServerSharing"));
            serverShareLock.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.lockServerSharing.tooltip"));
            serverShareLock.Descriptor.SetDescription("Prevents non-admin players from sharing saved-server entries through the content-share system.");
            serverShareLock.SetValueWithoutNotify(BasisNetworkModeration.GlobalServersLocked);
            serverShareLock.OnValueChanged += _ => BasisNetworkModeration.GlobalToggleServers();

            PanelToggle headlessAudioToggle = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            headlessAudioToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.headlessAudioOff"));
            headlessAudioToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.headlessAudioOff.tooltip"));
            headlessAudioToggle.Descriptor.SetDescription("Silences headless BasisAudioClipPlayer clients over the network.");
            headlessAudioToggle.SetValueWithoutNotify(BasisNetworkModeration.GlobalHeadlessAudioOff);
            headlessAudioToggle.OnValueChanged += value => BasisNetworkModeration.SetGlobalHeadlessAudio(value);

            PanelToggle disallowHeadlessToggle = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            disallowHeadlessToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.disallowHeadless"));
            disallowHeadlessToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.disallowHeadless.tooltip"));
            disallowHeadlessToggle.Descriptor.SetDescription("Disconnects connected headless clients and blocks new headless clients while enabled.");
            disallowHeadlessToggle.SetValueWithoutNotify(BasisNetworkModeration.GlobalHeadlessDisallowed);
            disallowHeadlessToggle.OnValueChanged += value => BasisNetworkModeration.SetGlobalHeadlessDisallow(value);

            // Server-broadcast lock for the desktop third-person camera. The toggle sends
            // GlobalToggleThirdPerson; the server flips, persists, and broadcasts the new
            // GlobalGetLockState payload back to every connected client.
            PanelToggle thirdPersonLock = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            thirdPersonLock.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.disableThirdPersonCamera"));
            thirdPersonLock.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.disableThirdPersonCamera.tooltip"));
            thirdPersonLock.Descriptor.SetDescription("Disables the desktop third-person camera for all connected players. Snaps anyone currently in third-person back to first-person.");
            thirdPersonLock.SetValueWithoutNotify(BasisNetworkModeration.GlobalThirdPersonDisabled);
            thirdPersonLock.OnValueChanged += _ => BasisNetworkModeration.GlobalToggleThirdPerson();

            PanelToggle additionalAvatarDataLock = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            additionalAvatarDataLock.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.stripAdditionalAvatarData"));
            additionalAvatarDataLock.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.stripAdditionalAvatarData.tooltip"));
            additionalAvatarDataLock.Descriptor.SetDescription("Strips additional avatar data (blendshapes, custom-behaviour params) from every player's network broadcast. Muscle, position, and rotation still sync normally.");
            additionalAvatarDataLock.SetValueWithoutNotify(BasisNetworkModeration.GlobalAdditionalAvatarDataLock);
            additionalAvatarDataLock.OnValueChanged += _ => BasisNetworkModeration.GlobalToggleAdditionalAvatarDataLock();

            PanelSlider opusPacketLossSlider = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, lockGroup.ContentParent);
            opusPacketLossSlider.SetSliderSettings(PanelSlider.SliderSettings.Percentage(BasisLocalization.Get("settings.admin.opusFecLoss")));
            opusPacketLossSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.opusFecLoss.tooltip"));
            opusPacketLossSlider.Descriptor.SetDescription("Sets OPUS_SET_PACKET_LOSS_PERC on every client's voice encoder. Higher = more bitrate spent on redundant FEC data, better recovery under packet loss.");
            opusPacketLossSlider.SetValueWithoutNotify(BasisNetworkModeration.GlobalOpusPacketLossPercent);
            opusPacketLossSlider.OnValueChanged += value => BasisNetworkModeration.SetGlobalOpusPacketLoss(Mathf.RoundToInt(value));

            PanelSlider maxMicrophoneRangeSlider = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, lockGroup.ContentParent);
            maxMicrophoneRangeSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.admin.maxMicrophoneRange"), 1f, 200f, true, 0, ValueDisplayMode.Meters));
            maxMicrophoneRangeSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.maxMicrophoneRange.tooltip"));
            maxMicrophoneRangeSlider.Descriptor.SetDescription("Maximum microphone (voice transmit) range in metres any client may set. Each client's Microphone Range slider and effective range is clamped to this.");
            maxMicrophoneRangeSlider.SetValueWithoutNotify(BasisNetworkModeration.ServerMaxMicrophoneRangeMeters);

            PanelSlider maxHearingRangeSlider = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, lockGroup.ContentParent);
            maxHearingRangeSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.admin.maxHearingRange"), 1f, 200f, true, 0, ValueDisplayMode.Meters));
            maxHearingRangeSlider.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.maxHearingRange.tooltip"));
            maxHearingRangeSlider.Descriptor.SetDescription("Maximum hearing (audio receive) range in metres any client may set. Each client's Hearing Range slider and effective range is clamped to this.");
            maxHearingRangeSlider.SetValueWithoutNotify(BasisNetworkModeration.ServerMaxHearingRangeMeters);

            controller.AvatarLockToggle = avatarLock;
            controller.PropLockToggle = propLock;
            controller.WorldLockToggle = worldLock;
            controller.ServerShareLockToggle = serverShareLock;
            controller.ThirdPersonLockToggle = thirdPersonLock;
            controller.AdditionalAvatarDataLockToggle = additionalAvatarDataLock;
            controller.HeadlessAudioToggle = headlessAudioToggle;
            controller.HeadlessDisallowToggle = disallowHeadlessToggle;
            controller.OpusPacketLossSlider = opusPacketLossSlider;
            controller.MaxMicrophoneRangeSlider = maxMicrophoneRangeSlider;
            controller.MaxHearingRangeSlider = maxHearingRangeSlider;
            controller.MaxMicrophoneRangeMeters = BasisNetworkModeration.ServerMaxMicrophoneRangeMeters;
            controller.MaxHearingRangeMeters = BasisNetworkModeration.ServerMaxHearingRangeMeters;
            maxMicrophoneRangeSlider.OnValueChanged += value =>
            {
                controller.MaxMicrophoneRangeMeters = value;
                BasisNetworkModeration.SetGlobalAudioRangeLimits(controller.MaxMicrophoneRangeMeters, controller.MaxHearingRangeMeters);
            };
            maxHearingRangeSlider.OnValueChanged += value =>
            {
                controller.MaxHearingRangeMeters = value;
                BasisNetworkModeration.SetGlobalAudioRangeLimits(controller.MaxMicrophoneRangeMeters, controller.MaxHearingRangeMeters);
            };

            // --- Camera photo metadata policy (per-category disallow; default allowed) ---
            PanelElementDescriptor cameraPolicyGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            cameraPolicyGroup.SetTitle(BasisLocalization.Get("settings.admin.title.cameraPhotoMetadata"));
            cameraPolicyGroup.SetDescription("Disallow categories of metadata that players' handheld cameras may embed into saved photos. Off = allowed.");

            PanelToggle camTagPeople = PanelToggle.CreateNewEntry(cameraPolicyGroup.ContentParent);
            camTagPeople.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.disallowTaggingPeople"));
            camTagPeople.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.disallowTaggingPeople.tooltip"));
            camTagPeople.Descriptor.SetDescription("Blocks embedding the names and boxes of people in photos.");

            PanelToggle camPersonDetails = PanelToggle.CreateNewEntry(cameraPolicyGroup.ContentParent);
            camPersonDetails.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.disallowPerPersonDetails"));
            camPersonDetails.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.disallowPerPersonDetails.tooltip"));
            camPersonDetails.Descriptor.SetDescription("Blocks embedding avatar name, UUID, platform, distance and 3D position per person.");

            PanelToggle camExif = PanelToggle.CreateNewEntry(cameraPolicyGroup.ContentParent);
            camExif.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.disallowCameraSettingsExif"));
            camExif.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.disallowCameraSettingsExif.tooltip"));
            camExif.Descriptor.SetDescription("Blocks embedding focal length, f-stop, shutter, ISO.");

            PanelToggle camCapture = PanelToggle.CreateNewEntry(cameraPolicyGroup.ContentParent);
            camCapture.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.disallowCaptureInfo"));
            camCapture.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.disallowCaptureInfo.tooltip"));
            camCapture.Descriptor.SetDescription("Blocks embedding app/version and capture date.");

            PanelToggle camPhotographer = PanelToggle.CreateNewEntry(cameraPolicyGroup.ContentParent);
            camPhotographer.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.disallowPhotographer"));
            camPhotographer.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.disallowPhotographer.tooltip"));
            camPhotographer.Descriptor.SetDescription("Blocks embedding the photographer's name and UUID.");

            PanelToggle camWorld = PanelToggle.CreateNewEntry(cameraPolicyGroup.ContentParent);
            camWorld.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.disallowWorldViewpoint"));
            camWorld.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.disallowWorldViewpoint.tooltip"));
            camWorld.Descriptor.SetDescription("Blocks embedding the world name and camera position/rotation.");

            byte BuildCameraMask()
            {
                byte mask = 0;
                if (camTagPeople.Value) mask |= BasisNetworkModeration.CameraPolicy_TagPeople;
                if (camPersonDetails.Value) mask |= BasisNetworkModeration.CameraPolicy_PersonDetails;
                if (camExif.Value) mask |= BasisNetworkModeration.CameraPolicy_CameraExif;
                if (camCapture.Value) mask |= BasisNetworkModeration.CameraPolicy_CaptureInfo;
                if (camPhotographer.Value) mask |= BasisNetworkModeration.CameraPolicy_Photographer;
                if (camWorld.Value) mask |= BasisNetworkModeration.CameraPolicy_World;
                return mask;
            }

            void ApplyCameraMask(byte mask)
            {
                camTagPeople.SetValueWithoutNotify((mask & BasisNetworkModeration.CameraPolicy_TagPeople) != 0);
                camPersonDetails.SetValueWithoutNotify((mask & BasisNetworkModeration.CameraPolicy_PersonDetails) != 0);
                camExif.SetValueWithoutNotify((mask & BasisNetworkModeration.CameraPolicy_CameraExif) != 0);
                camCapture.SetValueWithoutNotify((mask & BasisNetworkModeration.CameraPolicy_CaptureInfo) != 0);
                camPhotographer.SetValueWithoutNotify((mask & BasisNetworkModeration.CameraPolicy_Photographer) != 0);
                camWorld.SetValueWithoutNotify((mask & BasisNetworkModeration.CameraPolicy_World) != 0);
            }

            ApplyCameraMask(BasisNetworkModeration.GlobalCameraDisallowMask);
            camTagPeople.OnValueChanged += _ => BasisNetworkModeration.SetGlobalCameraPolicy(BuildCameraMask());
            camPersonDetails.OnValueChanged += _ => BasisNetworkModeration.SetGlobalCameraPolicy(BuildCameraMask());
            camExif.OnValueChanged += _ => BasisNetworkModeration.SetGlobalCameraPolicy(BuildCameraMask());
            camCapture.OnValueChanged += _ => BasisNetworkModeration.SetGlobalCameraPolicy(BuildCameraMask());
            camPhotographer.OnValueChanged += _ => BasisNetworkModeration.SetGlobalCameraPolicy(BuildCameraMask());
            camWorld.OnValueChanged += _ => BasisNetworkModeration.SetGlobalCameraPolicy(BuildCameraMask());

            controller.ApplyCameraMask = ApplyCameraMask;

            // --- Server configuration (persisted to config.xml on every change) ---
            PanelElementDescriptor serverGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            serverGroup.SetTitle(BasisLocalization.Get("settings.admin.title.serverConfiguration"));
            serverGroup.SetDescription("Display name and MOTD returned by the server-info query, plus whitelist controls. Changes are saved to config.xml.");

            PanelTextField serverNameField = PanelTextField.CreateNewEntry(serverGroup.ContentParent);
            serverNameField.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.serverName"));
            serverNameField.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.serverName.tooltip"));
            serverNameField.Descriptor.SetDescription("Public name returned to clients in the server list.");

            PanelButton applyServerName = PanelButton.CreateNew(serverGroup.ContentParent);
            applyServerName.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.applyServerName"));
            applyServerName.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.applyServerName.tooltip"));
            applyServerName.OnClicked += () =>
            {
                BasisNetworkModeration.SetServerName(serverNameField.Value ?? string.Empty);
            };

            PanelTextField serverMotdField = PanelTextField.CreateNewEntry(serverGroup.ContentParent);
            serverMotdField.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.motd"));
            serverMotdField.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.motd.tooltip"));
            serverMotdField.Descriptor.SetDescription("Short message of the day shown next to the server name. Leave blank to clear.");

            TMP_InputField motdInput = serverMotdField.GetComponentInChildren<TMP_InputField>(true);
            if (motdInput)
            {
                motdInput.lineType = TMP_InputField.LineType.MultiLineNewline;
                motdInput.scrollSensitivity = 2f;
            }

            PanelButton applyServerMotd = PanelButton.CreateNew(serverGroup.ContentParent);
            applyServerMotd.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.applyMotd"));
            applyServerMotd.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.applyMotd.tooltip"));
            applyServerMotd.OnClicked += () =>
            {
                BasisNetworkModeration.SetServerMotd(serverMotdField.Value ?? string.Empty);
            };

            // Pre-populate the Server Name and MOTD fields with whatever the
            // connected server is currently advertising, so the admin can see
            // and tweak the live values instead of typing into blank fields.
            // Fire-and-forget; failure is silent (the fields just stay blank).
            _ = PrefillServerInfoFieldsAsync(serverNameField, serverMotdField);

            PanelToggle whitelistToggle = PanelToggle.CreateNewEntry(serverGroup.ContentParent);
            whitelistToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.whitelistOnly"));
            whitelistToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.whitelistOnly.tooltip"));
            whitelistToggle.Descriptor.SetDescription("When on, only UUIDs in BasisWhiteList.txt may connect. Setting persists to config.xml.");
            whitelistToggle.OnValueChanged += value =>
            {
                BasisNetworkModeration.SetWhitelistMode(
                    value ? BasisUserRestrictionMode.WhiteList : BasisUserRestrictionMode.Normal);
            };

            PanelTextField whitelistUuidField = PanelTextField.CreateNewEntry(serverGroup.ContentParent);
            whitelistUuidField.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.whitelistUuid"));
            whitelistUuidField.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.whitelistUuid.tooltip"));
            whitelistUuidField.Descriptor.SetDescription("Player UUID to add or remove from BasisWhiteList.txt.");

            PanelButton addWhitelistButton = PanelButton.CreateNew(serverGroup.ContentParent);
            addWhitelistButton.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.addToWhitelist"));
            addWhitelistButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.addToWhitelist.tooltip"));
            addWhitelistButton.OnClicked += () =>
            {
                string uuid = whitelistUuidField.Value?.Trim();
                if (string.IsNullOrEmpty(uuid)) return;
                BasisNetworkModeration.AddWhitelist(uuid);
            };

            PanelButton removeWhitelistButton = PanelButton.CreateNew(serverGroup.ContentParent);
            removeWhitelistButton.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.removeFromWhitelist"));
            removeWhitelistButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.removeFromWhitelist.tooltip"));
            removeWhitelistButton.OnClicked += () =>
            {
                string uuid = whitelistUuidField.Value?.Trim();
                if (string.IsNullOrEmpty(uuid)) return;
                BasisNetworkModeration.RemoveWhitelist(uuid);
            };

            // --- Server logs (admin pulls logs/ + CrashReports/ to disk) ---
            PanelElementDescriptor logsGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            logsGroup.SetTitle(BasisLocalization.Get("settings.admin.title.serverLogs"));
            logsGroup.SetDescription("Pull the server's logs and crash reports. They are bundled and compressed on the server, sent over the network, and unpacked into a dated folder next to your settings.");

            PanelButton requestLogsButton = PanelButton.CreateNew(logsGroup.ContentParent);
            requestLogsButton.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.requestAllLogs"));
            requestLogsButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.requestAllLogs.tooltip"));
            requestLogsButton.Descriptor.SetDescription("Asks the server to bundle and send every log and crash report. Requires the basis.admin.logs permission.");
            requestLogsButton.OnClicked += () => BasisNetworkModeration.RequestAllLogs();

            // --- Default Library (saved to disk on the server, broadcast to all clients) ---
            BuildDefaultLibrarySection(container);

            // Permissions section
            SettingsProviderPermissionsTab.BuildPermissionsUI(container, tab.gameObject);

            descriptor.ForceRebuild();
            return tab;
        }

        /// <summary>
        /// Fire a one-shot info-query against the currently connected server and
        /// drop the response's name/MOTD into the admin fields. Lets admins see
        /// the live values instead of guessing what's in config.xml.
        /// </summary>
        private static async System.Threading.Tasks.Task PrefillServerInfoFieldsAsync(
            PanelTextField nameField, PanelTextField motdField)
        {
            if (!BasisNetworkManagement.IsInitialized) return;
            string ip = BasisNetworkManagement.Ip;
            ushort port = BasisNetworkManagement.Port;
            if (string.IsNullOrEmpty(ip) || port == 0) return;

            try
            {
                using CancellationTokenSource cts = new CancellationTokenSource(3500);
                Basis.Network.Core.ConnectionTarget target = new Basis.Network.Core.ConnectionTarget(
                    Basis.Network.Core.BasisNetworkStackRegistry.LiteNetLibId, $"{ip}:{port}");
                target.Set(Basis.Network.Core.ConnectionTarget.Keys.Address, ip);
                target.Set(Basis.Network.Core.ConnectionTarget.Keys.Port, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Basis.Network.Core.ServerProbeResult result =
                    await Basis.Network.Core.BasisNetworkStackRegistry.ProbeAsync(target, 3000, cts.Token);
                if (result == null || !result.Reachable) return;

                if (nameField != null && string.IsNullOrEmpty(nameField.Value))
                    nameField.SetValueWithoutNotify(result.Name ?? string.Empty);
                if (motdField != null && string.IsNullOrEmpty(motdField.Value))
                    motdField.SetValueWithoutNotify(result.Motd ?? string.Empty);
            }
            catch (Exception ex)
            {
                BasisDebug.LogWarning($"Server info prefill failed: {ex.Message}");
            }
        }

        // Modes mirror BundledContentHolder.Mode (Avatar=0, World=1, Prop=2).
        private static readonly string[] DefaultLibraryModeNames = { "Avatar", "World", "Prop" };

        private static void BuildDefaultLibrarySection(RectTransform container)
        {
            PanelElementDescriptor group =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            group.SetTitle(BasisLocalization.Get("settings.admin.title.defaultLibrary"));
            group.SetDescription("Add an avatar, world, or prop the server will offer to every player. Saved to defaultlibrary/ on disk and pushed live to connected clients.");

            PanelTextField urlField = PanelTextField.CreateNewEntry(group.ContentParent);
            urlField.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.beeUrl"));
            urlField.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.beeUrl.tooltip"));
            urlField.Descriptor.SetDescription("Direct URL to the .bee file the server should hand out. Pasting a url#password share string will be split automatically.");

            PanelTextField passwordField = PanelTextField.CreateNewEntry(group.ContentParent);
            passwordField.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.password"));
            passwordField.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.password.tooltip"));
            passwordField.Descriptor.SetDescription("Optional unlock password for encrypted bundles. Leave blank if none, or if the URL already carries a #password fragment.");

            PanelDropdown modeDropdown = PanelDropdown.CreateNewEntry(group.ContentParent);
            modeDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.type"));
            modeDropdown.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.type.tooltip"));
            modeDropdown.Descriptor.SetDescription("Which library tab the entry will appear in. Auto-detected from the BEE metadata when possible; this dropdown is only used as a fallback for legacy bundles.");
            modeDropdown.AssignEntries(new List<string>(DefaultLibraryModeNames));
            modeDropdown.SetValueWithoutNotify(DefaultLibraryModeNames[0]);

            PanelButton addButton = PanelButton.CreateNew(group.ContentParent);
            addButton.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.addToServerDefaults"));
            addButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.addToServerDefaults.tooltip"));
            addButton.Descriptor.SetDescription("Persist this entry on the server and push it to every connected client.");
            addButton.OnClicked += async () =>
            {
                string rawUrl = urlField.Value ?? string.Empty;
                string rawPassword = passwordField.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rawUrl))
                {
                    BasisDebug.LogError("Default library URL is empty.");
                    return;
                }

                // Peel #password fragment off the URL using the same splitter the in-game
                // add dialog uses, so a copy-pasted share string lands in the right fields.
                InputValidation.SplitUrlFragmentPassword(rawUrl, rawPassword, out string url, out string password);

                // Try auto-detecting the content type from the bundle metadata. If that
                // succeeds, override the dropdown — admins can leave the dropdown alone.
                // If detection fails (legacy bundle, unreachable URL), fall back to whatever
                // the admin picked.
                BundledContentHolder.Mode detected;
                try
                {
                    detected = await LibraryProvider.TryDetectModeFromUrl(url, password);
                }
                catch (Exception ex)
                {
                    BasisDebug.LogWarning($"Default library mode detection failed for {url}: {ex.Message}");
                    detected = BundledContentHolder.Mode.Legacy;
                }

                byte mode = detected switch
                {
                    BundledContentHolder.Mode.Avatar => (byte)0,
                    BundledContentHolder.Mode.World => (byte)1,
                    BundledContentHolder.Mode.Prop => (byte)2,
                    _ => ModeNameToByte(modeDropdown.Value),
                };

                // Reflect the resolved mode back into the dropdown so the admin can see what
                // they're about to commit before they confirm.
                if (mode < DefaultLibraryModeNames.Length)
                    modeDropdown.SetValueWithoutNotify(DefaultLibraryModeNames[mode]);

                WithConfirm(
                    "Add to server defaults?",
                    $"Save this {DefaultLibraryModeNames[mode]} to the server's default library? It will appear in every connected player's library and persist across server restarts.",
                    "Add",
                    "Cancel",
                    () => BasisNetworkModeration.AddDefaultLibraryItem(mode, url, password));
            };

            PanelButton removeButton = PanelButton.CreateNew(group.ContentParent);
            removeButton.Descriptor.SetTitle(BasisLocalization.Get("settings.admin.title.removeFromServerDefaults"));
            removeButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.admin.title.removeFromServerDefaults.tooltip"));
            removeButton.Descriptor.SetDescription("Drop every default-library entry whose URL matches the BEE URL field above. Entry is deleted on disk and removed from every connected client.");
            removeButton.OnClicked += () =>
            {
                string rawUrl = urlField.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rawUrl))
                {
                    BasisDebug.LogError("Default library URL is empty.");
                    return;
                }

                InputValidation.SplitUrlFragmentPassword(rawUrl, string.Empty, out string url, out _);

                WithConfirm(
                    "Remove from server defaults?",
                    $"Drop every default-library entry matching '{url}'? The change is immediate and propagates to every connected player.",
                    "Remove",
                    "Cancel",
                    () => BasisNetworkModeration.RemoveDefaultLibraryItem(url));
            };
        }

        private static byte ModeNameToByte(string name)
        {
            for (byte i = 0; i < DefaultLibraryModeNames.Length; i++)
            {
                if (DefaultLibraryModeNames[i] == name) return i;
            }
            return 0;
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

        /// <summary>
        /// Holds references to the lock toggles + opus slider so the controller can
        /// reflect server-pushed state changes on them.
        /// </summary>
        private sealed class AdminTabController : MonoBehaviour
        {
            public PanelToggle AvatarLockToggle;
            public PanelToggle PropLockToggle;
            public PanelToggle WorldLockToggle;
            public PanelToggle ServerShareLockToggle;
            public PanelToggle ThirdPersonLockToggle;
            public PanelToggle AdditionalAvatarDataLockToggle;
            public PanelToggle HeadlessAudioToggle;
            public PanelToggle HeadlessDisallowToggle;
            public PanelSlider OpusPacketLossSlider;
            public PanelSlider MaxMicrophoneRangeSlider;
            public PanelSlider MaxHearingRangeSlider;
            public float MaxMicrophoneRangeMeters;
            public float MaxHearingRangeMeters;
            public System.Action<byte> ApplyCameraMask;

            private void OnEnable()
            {
                // Admin panel open → route every popup into the notification list.
                BasisNotificationCenter.BeginForcedScope();
                BasisNetworkModeration.OnGlobalLockStateChanged -= OnGlobalLockStateChanged;
                BasisNetworkModeration.OnGlobalLockStateChanged += OnGlobalLockStateChanged;
                BasisNetworkModeration.OnGlobalThirdPersonDisabledChanged -= OnGlobalThirdPersonDisabledChanged;
                BasisNetworkModeration.OnGlobalThirdPersonDisabledChanged += OnGlobalThirdPersonDisabledChanged;
                BasisNetworkModeration.OnGlobalAdditionalAvatarDataLockChanged -= OnGlobalAdditionalAvatarDataLockChanged;
                BasisNetworkModeration.OnGlobalAdditionalAvatarDataLockChanged += OnGlobalAdditionalAvatarDataLockChanged;
                BasisNetworkModeration.OnGlobalHeadlessAudioStateChanged -= OnGlobalHeadlessAudioStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessAudioStateChanged += OnGlobalHeadlessAudioStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessDisallowStateChanged -= OnGlobalHeadlessDisallowStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessDisallowStateChanged += OnGlobalHeadlessDisallowStateChanged;
                BasisNetworkModeration.OnGlobalOpusPacketLossChanged -= OnGlobalOpusPacketLossChanged;
                BasisNetworkModeration.OnGlobalOpusPacketLossChanged += OnGlobalOpusPacketLossChanged;
                BasisNetworkModeration.OnAudioRangeLimitsChanged -= OnAudioRangeLimitsChanged;
                BasisNetworkModeration.OnAudioRangeLimitsChanged += OnAudioRangeLimitsChanged;
                BasisNetworkModeration.OnGlobalCameraPolicyChanged -= OnGlobalCameraPolicyChanged;
                BasisNetworkModeration.OnGlobalCameraPolicyChanged += OnGlobalCameraPolicyChanged;
            }

            private void OnDisable()
            {
                // Admin panel closed/hidden → resume normal popup handling.
                BasisNotificationCenter.EndForcedScope();
                BasisNetworkModeration.OnGlobalLockStateChanged -= OnGlobalLockStateChanged;
                BasisNetworkModeration.OnGlobalThirdPersonDisabledChanged -= OnGlobalThirdPersonDisabledChanged;
                BasisNetworkModeration.OnGlobalAdditionalAvatarDataLockChanged -= OnGlobalAdditionalAvatarDataLockChanged;
                BasisNetworkModeration.OnGlobalHeadlessAudioStateChanged -= OnGlobalHeadlessAudioStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessDisallowStateChanged -= OnGlobalHeadlessDisallowStateChanged;
                BasisNetworkModeration.OnGlobalOpusPacketLossChanged -= OnGlobalOpusPacketLossChanged;
                BasisNetworkModeration.OnAudioRangeLimitsChanged -= OnAudioRangeLimitsChanged;
                BasisNetworkModeration.OnGlobalCameraPolicyChanged -= OnGlobalCameraPolicyChanged;
            }

            private void OnDestroy()
            {
                BasisNetworkModeration.OnGlobalLockStateChanged -= OnGlobalLockStateChanged;
                BasisNetworkModeration.OnGlobalThirdPersonDisabledChanged -= OnGlobalThirdPersonDisabledChanged;
                BasisNetworkModeration.OnGlobalAdditionalAvatarDataLockChanged -= OnGlobalAdditionalAvatarDataLockChanged;
                BasisNetworkModeration.OnGlobalHeadlessAudioStateChanged -= OnGlobalHeadlessAudioStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessDisallowStateChanged -= OnGlobalHeadlessDisallowStateChanged;
                BasisNetworkModeration.OnGlobalOpusPacketLossChanged -= OnGlobalOpusPacketLossChanged;
                BasisNetworkModeration.OnAudioRangeLimitsChanged -= OnAudioRangeLimitsChanged;
                BasisNetworkModeration.OnGlobalCameraPolicyChanged -= OnGlobalCameraPolicyChanged;
            }

            private void OnGlobalLockStateChanged(bool avatars, bool props, bool worlds, bool servers)
            {
                if (AvatarLockToggle != null) AvatarLockToggle.SetValueWithoutNotify(avatars);
                if (PropLockToggle != null) PropLockToggle.SetValueWithoutNotify(props);
                if (WorldLockToggle != null) WorldLockToggle.SetValueWithoutNotify(worlds);
                if (ServerShareLockToggle != null) ServerShareLockToggle.SetValueWithoutNotify(servers);
            }

            private void OnGlobalThirdPersonDisabledChanged(bool disabled)
            {
                if (ThirdPersonLockToggle != null) ThirdPersonLockToggle.SetValueWithoutNotify(disabled);
            }

            private void OnGlobalAdditionalAvatarDataLockChanged(bool locked)
            {
                if (AdditionalAvatarDataLockToggle != null) AdditionalAvatarDataLockToggle.SetValueWithoutNotify(locked);
            }

            private void OnGlobalHeadlessAudioStateChanged(bool headlessAudioOff)
            {
                if (HeadlessAudioToggle != null) HeadlessAudioToggle.SetValueWithoutNotify(headlessAudioOff);
            }

            private void OnGlobalHeadlessDisallowStateChanged(bool headlessDisallowed)
            {
                if (HeadlessDisallowToggle != null) HeadlessDisallowToggle.SetValueWithoutNotify(headlessDisallowed);
            }

            private void OnGlobalOpusPacketLossChanged(int percent)
            {
                if (OpusPacketLossSlider != null) OpusPacketLossSlider.SetValueWithoutNotify(percent);
            }

            private void OnAudioRangeLimitsChanged(float microphoneMeters, float hearingMeters)
            {
                MaxMicrophoneRangeMeters = microphoneMeters;
                MaxHearingRangeMeters = hearingMeters;
                if (MaxMicrophoneRangeSlider != null) MaxMicrophoneRangeSlider.SetValueWithoutNotify(microphoneMeters);
                if (MaxHearingRangeSlider != null) MaxHearingRangeSlider.SetValueWithoutNotify(hearingMeters);
            }

            private void OnGlobalCameraPolicyChanged(byte mask)
            {
                ApplyCameraMask?.Invoke(mask);
            }
        }
    }
}
