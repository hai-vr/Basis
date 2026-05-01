using BasisNetworkCore.Security;
using System;
using System.Collections.Generic;
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
            descriptor.SetDescription(BasisLocalization.Get("settings.admin.description"));

            RectTransform container = descriptor.ContentParent;

            AdminTabController controller = tab.gameObject.AddComponent<AdminTabController>();

            // --- Global lock group ---
            PanelElementDescriptor lockGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            lockGroup.SetTitle("Global Content Locks");
            lockGroup.SetDescription("Globally disable loading for all non-admin players. Everyone is notified.");

            PanelToggle avatarLock = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            avatarLock.Descriptor.SetTitle("Lock Avatars");
            avatarLock.Descriptor.SetDescription("Prevents all non-admin avatar loading over the network.");
            avatarLock.SetValueWithoutNotify(BasisNetworkModeration.GlobalAvatarsLocked);
            avatarLock.OnValueChanged += _ => BasisNetworkModeration.GlobalToggleAvatars();

            PanelToggle propLock = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            propLock.Descriptor.SetTitle("Lock Props");
            propLock.Descriptor.SetDescription("Prevents all non-admin prop loading over the network.");
            propLock.SetValueWithoutNotify(BasisNetworkModeration.GlobalPropsLocked);
            propLock.OnValueChanged += _ => BasisNetworkModeration.GlobalToggleProps();

            PanelToggle worldLock = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            worldLock.Descriptor.SetTitle("Lock Worlds");
            worldLock.Descriptor.SetDescription("Prevents all non-admin world loading over the network.");
            worldLock.SetValueWithoutNotify(BasisNetworkModeration.GlobalWorldsLocked);
            worldLock.OnValueChanged += _ => BasisNetworkModeration.GlobalToggleWorlds();

            PanelToggle serverShareLock = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            serverShareLock.Descriptor.SetTitle("Lock Server Sharing");
            serverShareLock.Descriptor.SetDescription("Prevents non-admin players from sharing saved-server entries through the content-share system.");
            serverShareLock.SetValueWithoutNotify(BasisNetworkModeration.GlobalServersLocked);
            serverShareLock.OnValueChanged += _ => BasisNetworkModeration.GlobalToggleServers();

            PanelToggle headlessAudioToggle = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            headlessAudioToggle.Descriptor.SetTitle("Headless audio off");
            headlessAudioToggle.Descriptor.SetDescription("Silences headless BasisAudioClipPlayer clients over the network.");
            headlessAudioToggle.SetValueWithoutNotify(BasisNetworkModeration.GlobalHeadlessAudioOff);
            headlessAudioToggle.OnValueChanged += value => BasisNetworkModeration.SetGlobalHeadlessAudio(value);

            PanelToggle disallowHeadlessToggle = PanelToggle.CreateNewEntry(lockGroup.ContentParent);
            disallowHeadlessToggle.Descriptor.SetTitle("Disallow headless");
            disallowHeadlessToggle.Descriptor.SetDescription("Disconnects connected headless clients and blocks new headless clients while enabled.");
            disallowHeadlessToggle.SetValueWithoutNotify(BasisNetworkModeration.GlobalHeadlessDisallowed);
            disallowHeadlessToggle.OnValueChanged += value => BasisNetworkModeration.SetGlobalHeadlessDisallow(value);

            PanelSlider opusPacketLossSlider = PanelSlider.CreateNew(PanelSlider.SliderStyles.Entry, lockGroup.ContentParent);
            opusPacketLossSlider.SetSliderSettings(PanelSlider.SliderSettings.Percentage("Opus FEC loss %"));
            opusPacketLossSlider.Descriptor.SetDescription("Sets OPUS_SET_PACKET_LOSS_PERC on every client's voice encoder. Higher = more bitrate spent on redundant FEC data, better recovery under packet loss.");
            opusPacketLossSlider.SetValueWithoutNotify(BasisNetworkModeration.GlobalOpusPacketLossPercent);
            opusPacketLossSlider.OnValueChanged += value => BasisNetworkModeration.SetGlobalOpusPacketLoss(Mathf.RoundToInt(value));

            controller.AvatarLockToggle = avatarLock;
            controller.PropLockToggle = propLock;
            controller.WorldLockToggle = worldLock;
            controller.ServerShareLockToggle = serverShareLock;
            controller.HeadlessAudioToggle = headlessAudioToggle;
            controller.HeadlessDisallowToggle = disallowHeadlessToggle;
            controller.OpusPacketLossSlider = opusPacketLossSlider;

            // --- Server configuration (persisted to config.xml on every change) ---
            PanelElementDescriptor serverGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            serverGroup.SetTitle("Server Configuration");
            serverGroup.SetDescription("Display name and MOTD returned by the server-info query, plus whitelist controls. Changes are saved to config.xml.");

            PanelTextField serverNameField = PanelTextField.CreateNewEntry(serverGroup.ContentParent);
            serverNameField.Descriptor.SetTitle("Server Name");
            serverNameField.Descriptor.SetDescription("Public name returned to clients in the server list.");

            PanelButton applyServerName = PanelButton.CreateNew(serverGroup.ContentParent);
            applyServerName.Descriptor.SetTitle("Apply Server Name");
            applyServerName.OnClicked += () =>
            {
                BasisNetworkModeration.SetServerName(serverNameField.Value ?? string.Empty);
            };

            PanelTextField serverMotdField = PanelTextField.CreateNewEntry(serverGroup.ContentParent);
            serverMotdField.Descriptor.SetTitle("MOTD");
            serverMotdField.Descriptor.SetDescription("Short message of the day shown next to the server name. Leave blank to clear.");

            TMP_InputField motdInput = serverMotdField.GetComponentInChildren<TMP_InputField>(true);
            if (motdInput)
            {
                motdInput.lineType = TMP_InputField.LineType.MultiLineNewline;
                motdInput.scrollSensitivity = 2f;
            }

            PanelButton applyServerMotd = PanelButton.CreateNew(serverGroup.ContentParent);
            applyServerMotd.Descriptor.SetTitle("Apply MOTD");
            applyServerMotd.OnClicked += () =>
            {
                BasisNetworkModeration.SetServerMotd(serverMotdField.Value ?? string.Empty);
            };

            PanelToggle whitelistToggle = PanelToggle.CreateNewEntry(serverGroup.ContentParent);
            whitelistToggle.Descriptor.SetTitle("Whitelist Only");
            whitelistToggle.Descriptor.SetDescription("When on, only UUIDs in BasisWhiteList.txt may connect. Setting persists to config.xml.");
            whitelistToggle.OnValueChanged += value =>
            {
                BasisNetworkModeration.SetWhitelistMode(
                    value ? BasisUserRestrictionMode.WhiteList : BasisUserRestrictionMode.Normal);
            };

            PanelTextField whitelistUuidField = PanelTextField.CreateNewEntry(serverGroup.ContentParent);
            whitelistUuidField.Descriptor.SetTitle("Whitelist UUID");
            whitelistUuidField.Descriptor.SetDescription("Player UUID to add or remove from BasisWhiteList.txt.");

            PanelButton addWhitelistButton = PanelButton.CreateNew(serverGroup.ContentParent);
            addWhitelistButton.Descriptor.SetTitle("Add to Whitelist");
            addWhitelistButton.OnClicked += () =>
            {
                string uuid = whitelistUuidField.Value?.Trim();
                if (string.IsNullOrEmpty(uuid)) return;
                BasisNetworkModeration.AddWhitelist(uuid);
            };

            PanelButton removeWhitelistButton = PanelButton.CreateNew(serverGroup.ContentParent);
            removeWhitelistButton.Descriptor.SetTitle("Remove from Whitelist");
            removeWhitelistButton.OnClicked += () =>
            {
                string uuid = whitelistUuidField.Value?.Trim();
                if (string.IsNullOrEmpty(uuid)) return;
                BasisNetworkModeration.RemoveWhitelist(uuid);
            };

            // --- Default Library (saved to disk on the server, broadcast to all clients) ---
            BuildDefaultLibrarySection(container);

            // Permissions section
            SettingsProviderPermissionsTab.BuildPermissionsUI(container, tab.gameObject);

            descriptor.ForceRebuild();
            return tab;
        }

        // Modes mirror BundledContentHolder.Mode (Avatar=0, World=1, Prop=2).
        private static readonly string[] DefaultLibraryModeNames = { "Avatar", "World", "Prop" };

        private static void BuildDefaultLibrarySection(RectTransform container)
        {
            PanelElementDescriptor group =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            group.SetTitle("Default Library");
            group.SetDescription("Add an avatar, world, or prop the server will offer to every player. Saved to defaultlibrary/ on disk and pushed live to connected clients.");

            PanelTextField urlField = PanelTextField.CreateNewEntry(group.ContentParent);
            urlField.Descriptor.SetTitle("BEE URL");
            urlField.Descriptor.SetDescription("Direct URL to the .bee file the server should hand out.");

            PanelTextField passwordField = PanelTextField.CreateNewEntry(group.ContentParent);
            passwordField.Descriptor.SetTitle("Password");
            passwordField.Descriptor.SetDescription("Optional unlock password for encrypted bundles. Leave blank if none.");

            PanelDropdown modeDropdown = PanelDropdown.CreateNewEntry(group.ContentParent);
            modeDropdown.Descriptor.SetTitle("Type");
            modeDropdown.Descriptor.SetDescription("Which library tab the entry will appear in.");
            modeDropdown.AssignEntries(new List<string>(DefaultLibraryModeNames));
            modeDropdown.SetValueWithoutNotify(DefaultLibraryModeNames[0]);

            PanelButton addButton = PanelButton.CreateNew(group.ContentParent);
            addButton.Descriptor.SetTitle("Add to Server Defaults");
            addButton.Descriptor.SetDescription("Persist this entry on the server and push it to every connected client.");
            addButton.OnClicked += () =>
            {
                string url = urlField.Value?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(url))
                {
                    BasisDebug.LogError("Default library URL is empty.");
                    return;
                }

                byte mode = ModeNameToByte(modeDropdown.Value);
                string password = passwordField.Value ?? string.Empty;

                WithConfirm(
                    "Add to server defaults?",
                    $"Save this {DefaultLibraryModeNames[mode]} to the server's default library? It will appear in every connected player's library and persist across server restarts.",
                    "Add",
                    "Cancel",
                    () => BasisNetworkModeration.AddDefaultLibraryItem(mode, url, password));
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
            public PanelToggle HeadlessAudioToggle;
            public PanelToggle HeadlessDisallowToggle;
            public PanelSlider OpusPacketLossSlider;

            private void OnEnable()
            {
                BasisNetworkModeration.OnGlobalLockStateChanged -= OnGlobalLockStateChanged;
                BasisNetworkModeration.OnGlobalLockStateChanged += OnGlobalLockStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessAudioStateChanged -= OnGlobalHeadlessAudioStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessAudioStateChanged += OnGlobalHeadlessAudioStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessDisallowStateChanged -= OnGlobalHeadlessDisallowStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessDisallowStateChanged += OnGlobalHeadlessDisallowStateChanged;
                BasisNetworkModeration.OnGlobalOpusPacketLossChanged -= OnGlobalOpusPacketLossChanged;
                BasisNetworkModeration.OnGlobalOpusPacketLossChanged += OnGlobalOpusPacketLossChanged;
            }

            private void OnDisable()
            {
                BasisNetworkModeration.OnGlobalLockStateChanged -= OnGlobalLockStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessAudioStateChanged -= OnGlobalHeadlessAudioStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessDisallowStateChanged -= OnGlobalHeadlessDisallowStateChanged;
                BasisNetworkModeration.OnGlobalOpusPacketLossChanged -= OnGlobalOpusPacketLossChanged;
            }

            private void OnDestroy()
            {
                BasisNetworkModeration.OnGlobalLockStateChanged -= OnGlobalLockStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessAudioStateChanged -= OnGlobalHeadlessAudioStateChanged;
                BasisNetworkModeration.OnGlobalHeadlessDisallowStateChanged -= OnGlobalHeadlessDisallowStateChanged;
                BasisNetworkModeration.OnGlobalOpusPacketLossChanged -= OnGlobalOpusPacketLossChanged;
            }

            private void OnGlobalLockStateChanged(bool avatars, bool props, bool worlds, bool servers)
            {
                if (AvatarLockToggle != null) AvatarLockToggle.SetValueWithoutNotify(avatars);
                if (PropLockToggle != null) PropLockToggle.SetValueWithoutNotify(props);
                if (WorldLockToggle != null) WorldLockToggle.SetValueWithoutNotify(worlds);
                if (ServerShareLockToggle != null) ServerShareLockToggle.SetValueWithoutNotify(servers);
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
        }
    }
}
