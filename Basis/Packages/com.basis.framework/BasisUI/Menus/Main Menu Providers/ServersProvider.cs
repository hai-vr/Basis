using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    public class ServersProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new ServersProvider());
        }

        public const string TitleKey = "menu.provider.servers";
        public static string TitleStatic => BasisLocalization.Get(TitleKey);
        public override string Title => TitleStatic;
        public override string IconAddress => AddressableAssets.Sprites.Servers;
        public override int Order => 3;
        public override bool Hidden => false;

        public static string UsernameFileName = "CachedUserName.BAS";
        public static string LastConnectedServerIdFile = "LastConnectedServerId.BAS";

        // Built-in default. This entry is virtual: always rendered at the top of the list,
        // never persisted to disk, and not editable/removable from the UI. The address is a
        // hostname so the official server can be re-IP'd without shipping a client update —
        // BasisServerInfoClient and LiteNetLib both resolve it via DNS at connect time.
        private const string DefaultServerId = "__default__";
        private const string DefaultServerAddress = "server1.basisvr.org";
        private const ushort DefaultServerPort = 4296;
        private const string DefaultServerPassword = "default_password";

        // ── Per-session state ────────────────────────────────────────────────
        private List<SavedServerEntry> _servers;
        private readonly Dictionary<string, ServerRow> _rows = new();
        private string _editingId; // null = not editing, "" = adding new, otherwise = id of server being edited

        private static SavedServerEntry CreateDefaultEntry() => new SavedServerEntry
        {
            Id = DefaultServerId,
            DisplayName = BasisLocalization.Get("menu.servers.list.default"),
            Address = DefaultServerAddress,
            Port = DefaultServerPort,
            Password = DefaultServerPassword,
            HasPassword = true,
        };

        private static bool IsDefault(SavedServerEntry entry) =>
            entry != null && entry.Id == DefaultServerId;

        // ── Static UI references rebuilt every RunAction() ────────────────────
        private BasisMenuPanel _panel;
        private RectTransform _listContainer;
        private PanelElementDescriptor _editorSection;
        private PanelElementDescriptor _emptyState;
        private PanelTextField _editAddress;
        private PanelTextField _editPort;
        private PanelPasswordField _editPassword;
        private PanelButton _editSaveButton;
        private PanelButton _editCancelButton;
        private PanelTextField _usernameField;
        private PanelButton _addServerButton;
        private PanelButton _refreshAllButton;
        private PanelToggle _advancedToggle;
        private PanelButton _hostButton;
        private PanelToggle _autoConnectToggle;
        private PanelElementDescriptor _info;

        private CancellationTokenSource _queryCts;

        private static bool _autoConnectAttempted;

        public override void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title)
            {
                BasisMainMenu.Instance.ActiveMenu.ReleaseInstance();
                return;
            }

            BasisMenuPanel panel = BasisMainMenu.CreateActiveMenu(
                BasisMenuPanel.PanelData.Standard(Title),
                BasisMenuPanel.PanelStyles.Page);
            BoundButton?.BindActiveStateToAddressablesInstance(panel);
            _panel = panel;

            panel.OnInstanceReleased += OnPanelClosed;

            RectTransform container = panel.Descriptor.ContentParent;
            PanelElementDescriptor scroll = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.ScrollViewVertical, container);
            container = scroll.ContentParent;

            BuildHeader(container);
            BuildEditorSection(container);

            _listContainer = container;
            _emptyState = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            _emptyState.SetTitle(BasisLocalization.Get("menu.servers.list.empty"));
            _emptyState.SetDescription(string.Empty);

            _info = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            _info.SetTitle(string.Empty);
            _info.SetDescription(string.Empty);

            BuildAdvancedSection(container);

            _servers = SavedServerStore.Load();
            HideEditor();
            RebuildRows();
            _ = RefreshAllAsync();

            if (BasisNetworkManagement.Instance != null)
            {
                TryAutoConnect();
            }
            else
            {
                BasisNetworkManagement.OnIstanceCreated += TryAutoConnect;
            }
        }

        private void OnPanelClosed()
        {
            _queryCts?.Cancel();
            _queryCts = null;
            _rows.Clear();
            _servers = null;
            _panel = null;
        }

        // ── Header ───────────────────────────────────────────────────────────

        private void BuildHeader(RectTransform container)
        {
            _usernameField = PanelTextField.CreateNewEntry(container);
            _usernameField.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.username"));
            _usernameField.SetValueWithoutNotify(BasisDataStore.LoadString(UsernameFileName, string.Empty));

            _addServerButton = PanelButton.CreateNew(container);
            _addServerButton.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.list.addServer"));
            _addServerButton.Descriptor.SetHeight(70);
            _addServerButton.OnClicked += () => ShowEditor(null);

            _refreshAllButton = PanelButton.CreateNew(container);
            _refreshAllButton.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.list.refreshAll"));
            _refreshAllButton.Descriptor.SetHeight(60);
            _refreshAllButton.OnClicked += () => _ = RefreshAllAsync();
        }

        private void BuildAdvancedSection(RectTransform container)
        {
            _advancedToggle = PanelToggle.CreateNewEntry(container);
            _advancedToggle.Descriptor.SetTitle(BasisLocalization.Get("ui.advanced"));
            _advancedToggle.SetValueWithoutNotify(false);

            _hostButton = PanelButton.CreateNew(container);
            _hostButton.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.host"));
            _hostButton.Descriptor.SetDescription(BasisLocalization.Get("menu.servers.hostMode.description"));
            _hostButton.Descriptor.SetHeight(70);
            _hostButton.OnClicked += () => _ = ConnectToAsync(CreateHostEntry(), isHostMode: true);

            _autoConnectToggle = PanelToggle.CreateNewEntry(container);
            _autoConnectToggle.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.autoConnect"));
            _autoConnectToggle.Descriptor.SetDescription(BasisLocalization.Get("menu.servers.autoConnect.description"));
            _autoConnectToggle.AssignBinding(BasisSettingsDefaults.AutoConnect);

            _hostButton.gameObject.SetActive(false);
            _autoConnectToggle.gameObject.SetActive(false);

            _advancedToggle.OnValueChanged += (val) =>
            {
                _hostButton.gameObject.SetActive(val);
                _autoConnectToggle.gameObject.SetActive(val);
            };
        }

        // Synthetic entry used by the Host button — same shape as a saved server but
        // pointing at localhost so the in-process BasisNetworkServerRunner is the peer.
        private static SavedServerEntry CreateHostEntry() => new SavedServerEntry
        {
            Id = "__host__",
            DisplayName = string.Empty,
            Address = "localhost",
            Port = DefaultServerPort,
            Password = DefaultServerPassword,
            HasPassword = true,
        };

        // ── Add/Edit form ────────────────────────────────────────────────────

        private void BuildEditorSection(RectTransform container)
        {
            _editorSection = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            RectTransform editorContent = _editorSection.ContentParent;

            _editAddress = PanelTextField.CreateNewEntry(editorContent);
            _editAddress.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.ipAddress"));

            _editPort = PanelTextField.CreateNewEntry(editorContent);
            _editPort.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.port"));

            _editPassword = PanelPasswordField.CreateNewEntry(editorContent);
            _editPassword.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.password"));

            _editSaveButton = PanelButton.CreateNew(editorContent);
            _editSaveButton.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.list.save"));
            _editSaveButton.OnClicked += SaveEditor;

            _editCancelButton = PanelButton.CreateNew(editorContent);
            _editCancelButton.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.list.cancel"));
            _editCancelButton.OnClicked += HideEditor;
        }

        private void ShowEditor(SavedServerEntry existing)
        {
            _editingId = existing?.Id ?? string.Empty;
            _editorSection.SetTitle(BasisLocalization.Get(existing == null
                ? "menu.servers.list.newServer"
                : "menu.servers.list.editing"));
            _editAddress.SetValueWithoutNotify(existing?.Address ?? string.Empty);
            _editPort.SetValueWithoutNotify((existing?.Port ?? (ushort)4296).ToString());
            _editPassword.SetPassword(existing?.Password ?? "default_password");
            _editorSection.SetActive(true);
        }

        private void HideEditor()
        {
            _editingId = null;
            _editorSection.SetActive(false);
        }

        private void SaveEditor()
        {
            string address = _editAddress.Value?.Trim();
            if (!ushort.TryParse(_editPort.Value, out ushort port) || port == 0)
            {
                _info.SetTitle(BasisLocalization.Get("ui.error"));
                _info.SetDescription("Port must be 1-65535");
                return;
            }
            if (string.IsNullOrEmpty(address))
            {
                _info.SetTitle(BasisLocalization.Get("ui.error"));
                _info.SetDescription(BasisLocalization.Get("menu.servers.ipAddress"));
                return;
            }

            SavedServerEntry entry;
            if (string.IsNullOrEmpty(_editingId))
            {
                entry = new SavedServerEntry();
                _servers.Add(entry);
            }
            else
            {
                entry = _servers.Find(s => s.Id == _editingId);
                if (entry == null)
                {
                    entry = new SavedServerEntry { Id = _editingId };
                    _servers.Add(entry);
                }
            }

            // No display-name field anymore — the row title comes from the server's own
            // info-query response, with the address as the offline fallback.
            entry.DisplayName = string.Empty;
            entry.Address = address;
            entry.Port = port;
            entry.HasPassword = true;
            entry.Password = _editPassword.Password ?? string.Empty;

            SavedServerStore.Save(_servers);

            string savedId = entry.Id;
            HideEditor();
            RebuildRows();
            _ = RefreshOneAsync(savedId);
        }

        // ── Server list rows ─────────────────────────────────────────────────

        private class ServerRow
        {
            public PanelElementDescriptor Group;
            public PanelButton ConnectButton;
            public PanelButton EditButton;
            public PanelButton RemoveButton;
        }

        private void RebuildRows()
        {
            foreach (KeyValuePair<string, ServerRow> kv in _rows)
            {
                if (kv.Value.Group != null && kv.Value.Group.gameObject != null)
                    UnityEngine.Object.Destroy(kv.Value.Group.gameObject);
            }
            _rows.Clear();

            // The default row always renders, so the empty-state copy only makes sense
            // when the user has zero *additional* servers. Hide it whenever the default
            // alone is enough to populate the list.
            _emptyState.SetActive(false);

            BuildRow(CreateDefaultEntry());
            foreach (SavedServerEntry s in _servers)
            {
                BuildRow(s);
            }
        }

        private void BuildRow(SavedServerEntry entry)
        {
            bool isDefault = IsDefault(entry);

            ServerRow row = new ServerRow();
            row.Group = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, _listContainer);
            // Slot the row just above the status info element so rows live between the
            // empty-state hint and the advanced section.
            row.Group.transform.SetSiblingIndex(_info.transform.GetSiblingIndex());

            string baseTitle = string.IsNullOrEmpty(entry.DisplayName) ? entry.Address : entry.DisplayName;
            row.Group.SetTitle(isDefault
                ? string.Format(BasisLocalization.Get("menu.servers.list.defaultBadge"), baseTitle)
                : baseTitle);
            row.Group.SetDescription(string.Format(BasisLocalization.Get("menu.servers.list.address"), entry.Address, entry.Port));

            RectTransform actions = BuildActionRow(row.Group.ContentParent);

            row.ConnectButton = PanelButton.CreateNew(actions);
            row.ConnectButton.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.connect"));
            row.ConnectButton.OnClicked += () => _ = ConnectToAsync(entry);

            if (!isDefault)
            {
                row.EditButton = PanelButton.CreateNew(actions);
                row.EditButton.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.list.edit"));
                row.EditButton.OnClicked += () => ShowEditor(entry);

                row.RemoveButton = PanelButton.CreateNew(actions);
                row.RemoveButton.Descriptor.SetTitle(BasisLocalization.Get("menu.servers.list.remove"));
                row.RemoveButton.OnClicked += () => _ = ConfirmAndRemoveAsync(entry);
            }

            _rows[entry.Id] = row;
        }

        private async Task ConfirmAndRemoveAsync(SavedServerEntry entry)
        {
            if (_panel == null) return;
            string label = string.IsNullOrEmpty(entry.DisplayName) ? entry.Address : entry.DisplayName;
            bool confirmed = await LibraryProviderDialogRemove.PromptUserForRemoval(_panel, label, "Server");
            if (!confirmed) return;
            // Panel may have closed while the dialog was open — bail out cleanly.
            if (_servers == null) return;
            _servers.RemoveAll(s => s.Id == entry.Id);
            SavedServerStore.Save(_servers);
            RebuildRows();
        }

        /// <summary>
        /// Inline horizontal action row — same pattern UserListProvider.BuildActionRow
        /// uses for the per-player Mute/Highlight/Block buttons. Lives as a child of
        /// the row's content parent so it gets destroyed with the row.
        /// </summary>
        private static RectTransform BuildActionRow(RectTransform parent)
        {
            GameObject rowGO = new GameObject("ServerRowActions", typeof(RectTransform));
            RectTransform rowRect = (RectTransform)rowGO.transform;
            rowRect.SetParent(parent, false);

            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);

            HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(8, 8, 4, 8);

            ContentSizeFitter fitter = rowGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LayoutElement layout = rowGO.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;

            return rowRect;
        }

        // ── Querying ─────────────────────────────────────────────────────────

        private async Task RefreshAllAsync()
        {
            if (_servers == null) return;

            _queryCts?.Cancel();
            _queryCts = new CancellationTokenSource();
            CancellationToken token = _queryCts.Token;

            foreach (ServerRow r in _rows.Values)
                r.Group.SetDescription(BasisLocalization.Get("menu.servers.list.querying"));

            List<SavedServerEntry> probeTargets = new List<SavedServerEntry>(_servers.Count + 1);
            probeTargets.Add(CreateDefaultEntry());
            probeTargets.AddRange(_servers);

            List<Task> tasks = new List<Task>(probeTargets.Count);
            foreach (SavedServerEntry s in probeTargets)
                tasks.Add(QueryAndUpdateAsync(s, token));

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Panel was closed mid-refresh — no-op.
            }
        }

        private async Task RefreshOneAsync(string id)
        {
            SavedServerEntry e = id == DefaultServerId
                ? CreateDefaultEntry()
                : _servers.Find(s => s.Id == id);
            if (e == null) return;
            CancellationToken token = (_queryCts ??= new CancellationTokenSource()).Token;
            await QueryAndUpdateAsync(e, token);
        }

        private async Task QueryAndUpdateAsync(SavedServerEntry entry, CancellationToken ct)
        {
            BasisServerInfoClient.ServerInfoResult result;
            try
            {
                result = await BasisServerInfoClient.QueryAsync(entry.Address, entry.Port, 3000, ct);
            }
            catch (OperationCanceledException) { return; }

            if (ct.IsCancellationRequested) return;
            if (!_rows.TryGetValue(entry.Id, out ServerRow row)) return;
            if (row.Group == null || row.Group.gameObject == null) return;

            if (result != null && result.Reachable)
            {
                BasisServerInfoClient.ServerInfo info = result.Info;
                string name = info.Name;
                if (string.IsNullOrEmpty(name)) name = entry.DisplayName;
                if (string.IsNullOrEmpty(name)) name = entry.Address;
                if (IsDefault(entry))
                    name = string.Format(BasisLocalization.Get("menu.servers.list.defaultBadge"), name);
                row.Group.SetTitle(string.Format("{0} - {1}",
                    name,
                    string.Format(BasisLocalization.Get("menu.servers.list.players"), info.Online, info.Max)));
                row.Group.SetDescription(string.Format("{0}  •  {1}",
                    string.Format(BasisLocalization.Get("menu.servers.list.address"), entry.Address, entry.Port),
                    string.Format(BasisLocalization.Get("menu.servers.list.ping"), info.RoundTripMs)));
            }
            else
            {
                string name = entry.DisplayName;
                if (string.IsNullOrEmpty(name)) name = entry.Address;
                if (IsDefault(entry))
                    name = string.Format(BasisLocalization.Get("menu.servers.list.defaultBadge"), name);
                row.Group.SetTitle(name);
                row.Group.SetDescription(string.Format("{0}  •  {1}",
                    string.Format(BasisLocalization.Get("menu.servers.list.address"), entry.Address, entry.Port),
                    BasisLocalization.Get("menu.servers.list.offline")));
            }
        }

        // ── Connection ───────────────────────────────────────────────────────

        private void TryAutoConnect()
        {
            if (_autoConnectAttempted) return;
            // The deferred OnIstanceCreated callback can fire after the panel was closed —
            // bail out if our session state was torn down.
            if (_servers == null) return;
            _autoConnectAttempted = true;

            if (!BasisSettingsDefaults.AutoConnect.RawValue) return;
            if (BasisNetworkConnection.LocalPlayerIsConnected) return;

            string username = BasisDataStore.LoadString(UsernameFileName, string.Empty);
            if (string.IsNullOrEmpty(username)) return;

            string lastId = BasisDataStore.LoadString(LastConnectedServerIdFile, string.Empty);
            SavedServerEntry target = ResolveAutoConnectTarget(lastId);
            if (target == null) return;

            _usernameField?.SetValueWithoutNotify(username);
            _ = ConnectToAsync(target);
        }

        private SavedServerEntry ResolveAutoConnectTarget(string lastId)
        {
            if (lastId == DefaultServerId) return CreateDefaultEntry();
            if (!string.IsNullOrEmpty(lastId))
            {
                SavedServerEntry found = _servers.Find(s => s.Id == lastId);
                if (found != null) return found;
            }
            // Fall back to the built-in default — user has Auto Connect on but no usable target yet.
            return CreateDefaultEntry();
        }

        private async Task ConnectToAsync(SavedServerEntry entry, bool isHostMode = false)
        {
            try
            {
                _info.SetTitle(BasisLocalization.Get("menu.servers.status.connecting"));
                _info.SetDescription(BasisLocalization.Get("menu.servers.status.initializing"));

                string userName = _usernameField._inputField.text;
                if (string.IsNullOrEmpty(userName))
                {
                    _info.SetTitle(BasisLocalization.Get("ui.error"));
                    _info.SetDescription(BasisLocalization.Get("menu.servers.error.emptyName"));
                    return;
                }

                if (BasisNetworkConnection.LocalPlayerIsConnected)
                {
                    _info.SetTitle(BasisLocalization.Get("menu.servers.status.disconnecting"));
                    _info.SetDescription(BasisLocalization.Get("menu.servers.status.disconnecting"));
                    BasisDebug.Log("Disconnecting from current connection", BasisDebug.LogTag.Networking);

                    using CancellationTokenSource cts = new CancellationTokenSource();
                    Task rebootWait = BasisNetworkConnection.WaitForRebootCompleteAsync(cts.Token);
                    await BasisNetworkLifeCycle.Destroy(BasisNetworkManagement.Instance);
                    await rebootWait;
                    BasisNetworkLifeCycle.Initalize(BasisNetworkManagement.Instance);
                }

                if (BasisNetworkManagement.Instance == null)
                {
                    _info.SetTitle(BasisLocalization.Get("ui.error"));
                    _info.SetDescription(BasisLocalization.Get("menu.servers.error.noNetworkLayer"));
                    BasisDebug.LogError("Missing Networking layer!");
                    return;
                }

                _info.SetTitle(BasisLocalization.Get("menu.servers.status.connecting"));
                _info.SetDescription(BasisLocalization.Get("menu.servers.status.preparing"));
                BasisLocalPlayer.Instance.DisplayName = userName;
                BasisLocalPlayer.Instance.SetSafeDisplayname();
                BasisDataStore.SaveString(BasisLocalPlayer.Instance.DisplayName, UsernameFileName);
                BasisDataStore.SaveString(entry.Id, LastConnectedServerIdFile);

                _info.SetDescription(BasisLocalization.Get("menu.servers.status.loadingBundle"));

                BasisNetworkManagement.Instance.Port = entry.Port;
                BasisNetworkManagement.Instance.Ip = entry.Address;
                BasisNetworkManagement.Instance.Password = entry.HasPassword ? entry.Password : string.Empty;
                BasisNetworkManagement.Instance.IsHostMode = isHostMode;

                BasisMainMenu.Close();
                BasisCursorManagement.OnReset();
                await CreateAssetBundle();
                BasisNetworkManagement.Instance.Connect();
                if (BasisDesktopEye.Instance != null)
                {
                    BasisDesktopEye.Instance.LockEye();
                }
            }
            catch (TimeoutException tex)
            {
                _info.SetTitle(BasisLocalization.Get("ui.error"));
                _info.SetDescription(BasisLocalization.Get("menu.servers.error.timeout"));
                BasisDebug.LogError(tex.ToString());
            }
            catch (Exception ex)
            {
                _info.SetTitle(BasisLocalization.Get("ui.error"));
                _info.SetDescription(BasisLocalization.Get("menu.servers.error.connectFailed"));
                BasisDebug.LogError(ex.ToString());
            }
        }

        public async Task CreateAssetBundle()
        {
            if (BundledContentHolder.Instance.UseSceneProvidedHere)
            {
                BasisDebug.Log("using Local Asset Bundle or Addressable", BasisDebug.LogTag.Networking);
                if (BundledContentHolder.Instance.UseAddressablesToLoadScene)
                {
                    await BasisSceneLoad.LoadSceneAddressables(
                        BundledContentHolder.Instance.DefaultScene
                            .BasisRemoteBundleEncrypted.RemoteBeeFileLocation);
                }
                else
                {
                    await BasisSceneLoad.LoadSceneAssetBundle(BundledContentHolder.Instance.DefaultScene);
                }
            }
        }
    }
}
