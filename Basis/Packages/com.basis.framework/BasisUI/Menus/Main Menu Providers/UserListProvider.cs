using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    /// <summary>
    /// Main menu provider that displays a searchable grid of all connected players.
    /// Search matches against both display name and UUID.
    /// Clicking a remote player opens their IndividualPlayerProvider panel.
    /// </summary>
    public class UserListProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new UserListProvider());
        }

        public const string StaticTitleKey = "menu.provider.players";
        public static string StaticTitle => BasisLocalization.Get(StaticTitleKey);
        public override string Title => StaticTitle;
        public override string IconAddress => AddressableAssets.Sprites.Avatars;
        public override int Order => 40;
        public override bool Hidden => !BasisNetworkConnection.LocalPlayerIsConnected;

        private UserListController _controller;

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

            // Vertical scrollable page (same pattern as IndividualPlayerProvider)
            PanelTabPage tab = PanelTabPage.CreateVertical(panel.Descriptor.ContentParent);
            tab.Descriptor.SetTitle(BasisLocalization.Get("menu.provider.players"));
            tab.Descriptor.SetIcon(AddressableAssets.Sprites.Avatars);
            RectTransform root = tab.Descriptor.ContentParent;

            // Search field at the very top
            PanelTextField searchField = PanelTextField.CreateNewEntry(root);
            searchField.Descriptor.SetTitle(BasisLocalization.Get("ui.search.label"));
            searchField.Descriptor.SetDescription(BasisLocalization.Get("menu.players.search.byNameOrUuid"));

            // Sort mode dropdown. Entries are stable English identifiers — the
            // controller switches on the literal string, so adding a translated
            // label here would silently disable sort.
            PanelDropdown sortDropdown = PanelDropdown.CreateNewEntry(root);
            sortDropdown.Descriptor.SetTitle(BasisLocalization.Get("menu.players.sortMode"));
            sortDropdown.Descriptor.SetDescription(BasisLocalization.Get("menu.players.sortMode.description"));
            sortDropdown.AssignLocalizedEntries(
                new List<string> { "Default", "Distance", "Name", "Platform", "Join Time" },
                new List<string> { "menu.players.sortMode.default", "menu.players.sortMode.distance", "menu.players.sortMode.name", "menu.players.sortMode.platform", "menu.players.sortMode.joinTime" });
            sortDropdown.SetValueWithoutNotify("Default");

            // Player count header
            var headerGroup = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, root);

            // Attach controller — fields must be assigned before Initialize()
            _controller = panel.gameObject.AddComponent<UserListController>();
            _controller.GridParent = BuildPlayerGrid(root);
            _controller.HeaderGroup = headerGroup;
            _controller.SearchField = searchField;
            _controller.SortDropdown = sortDropdown;
            _controller.TabDescriptor = tab.Descriptor;
            _controller.Initialize();

            panel.Descriptor.ForceRebuild();
        }

        public override void OnReleaseEvent()
        {
            _controller = null;
        }

        // ======== Helpers ========

        // A player has no thumbnail, so the cards are sized for an icon and a name
        // rather than for the library's 200x250 cover art.
        private static readonly Vector2 CardSize = new Vector2(300f, 100f);

        // PE Button stacks its icon, label and indicator on the same full-stretch rect,
        // so the platform sprite goes on as an overlay and the label is inset past it
        // rather than being centred on top of it.
        private const float CardIconStripWidth = 68f;

        // Right-hand strip of the card reserved for the pin and the distance chip.
        private const float CardInfoStripWidth = 120f;

        private static RectTransform BuildPlayerGrid(RectTransform parent)
        {
            GameObject gridGO = new GameObject("PlayerGrid", typeof(RectTransform));
            RectTransform gridRect = (RectTransform)gridGO.transform;
            gridRect.SetParent(parent, false);
            gridRect.anchorMin = new Vector2(0f, 1f);
            gridRect.anchorMax = new Vector2(1f, 1f);
            gridRect.pivot = new Vector2(0.5f, 1f);

            GridLayoutGroup grid = gridGO.AddComponent<GridLayoutGroup>();
            grid.cellSize = CardSize;
            grid.spacing = new Vector2(10f, 15f);
            grid.padding = new RectOffset(10, 10, 10, 10);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;

            ContentSizeFitter fitter = gridGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LayoutElement layout = gridGO.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;

            return gridRect;
        }

        /// <summary>
        /// Distance chip in the card's top-left corner, the same overlay treatment
        /// LibraryProvider gives its stack count. Returns the label so the refresh
        /// tick can rewrite it without walking the hierarchy.
        /// </summary>
        private static TextMeshProUGUI AddInfoChip(PanelButton buttonPanel)
        {
            PanelElementDescriptor desc = buttonPanel.Descriptor;

            GameObject chipGo = new GameObject("Info Chip", typeof(RectTransform));
            RectTransform rt = (RectTransform)chipGo.transform;
            rt.SetParent(desc.rectTransform, false);
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-54, 0);
            rt.sizeDelta = new Vector2(88, 34);

            Image background = chipGo.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.6f);
            background.raycastTarget = false;

            LayoutElement layoutElement = chipGo.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            GameObject textGo = new GameObject("Value", typeof(RectTransform));
            RectTransform textRt = (RectTransform)textGo.transform;
            textRt.SetParent(rt, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            TextMeshProUGUI label = textGo.AddComponent<TextMeshProUGUI>();
            if (desc.TitleLabel != null)
            {
                label.font = desc.TitleLabel.font;
                label.fontSharedMaterial = desc.TitleLabel.fontSharedMaterial;
                label.color = desc.TitleLabel.color;
            }
            label.fontSize = 22;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        private static PanelImage AddOverlayIcon(PanelButton buttonPanel, string spriteAddress, Vector2 anchor, Vector2 anchoredPosition, float size)
        {
            PanelImage icon = PanelImage.CreateNew(buttonPanel.Descriptor);
            icon.SetIcon(AddressableAssets.GetSprite(spriteAddress), true);
            icon.rectTransform.anchorMin = anchor;
            icon.rectTransform.anchorMax = anchor;
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            icon.rectTransform.anchoredPosition = anchoredPosition;
            icon.rectTransform.sizeDelta = new Vector2(size, size);
            return icon;
        }

        private static PanelImage AddPlatformIcon(PanelButton buttonPanel, string spriteAddress) =>
            AddOverlayIcon(buttonPanel, spriteAddress, new Vector2(0, 0.5f), new Vector2(36, 0), 40f);

        private static PanelImage AddPinIcon(PanelButton buttonPanel) =>
            AddOverlayIcon(buttonPanel, AddressableAssets.Sprites.Pin, new Vector2(1, 0.5f), new Vector2(-116, 0), 28f);

        public static string GetPlatformIconAddress(string platform)
        {
            if (string.IsNullOrEmpty(platform)) return string.Empty;
            string lower = platform.ToLowerInvariant();
            if (lower.Contains("windows")) return AddressableAssets.Sprites.PlatformStandaloneWindows64;
            if (lower.Contains("osx") || lower.Contains("mac")) return AddressableAssets.Sprites.PlatformStandaloneOSX;
            if (lower.Contains("linux")) return AddressableAssets.Sprites.PlatformStandaloneLinux64;
            if (lower.Contains("android")) return AddressableAssets.Sprites.PlatformMobileAndroid;
            if (lower.Contains("iphone") || lower.Contains("ios")) return AddressableAssets.Sprites.PlatformMobileiOS;
            if (lower.Contains("generic")) return AddressableAssets.Sprites.PlatformGeneric;
            return string.Empty;
        }

        public static string GetPlatformLabel(string platform)
        {
            if (string.IsNullOrEmpty(platform)) return BasisLocalization.Get("ui.unknown");
            string lower = platform.ToLowerInvariant();
            // Platform names are proper nouns and don't get translated.
            if (lower.Contains("windows")) return "Windows";
            if (lower.Contains("osx") || lower.Contains("mac")) return "macOS";
            if (lower.Contains("linux")) return "Linux";
            if (lower.Contains("android")) return "Android";
            if (lower.Contains("iphone") || lower.Contains("ios")) return "iOS";
            return platform;
        }

        // ======== Types ========

        private enum SortMode { Default, Distance, Name, Platform, JoinTime }

        private struct PlayerEntry
        {
            public BasisNetworkPlayer NetPlayer;
            public PanelButton Button;
            public TextMeshProUGUI InfoLabel;
            public PanelImage PinIcon;
        }

        /// <summary>
        /// Manages the player grid, search/filter, sort, and join/leave events.
        /// Player cards are the only children of <see cref="GridParent"/>, so
        /// reordering is a straight sibling-index pass.
        /// </summary>
        private sealed class UserListController : MonoBehaviour
        {
            public RectTransform GridParent;
            public PanelElementDescriptor HeaderGroup;
            public PanelTextField SearchField;
            public PanelDropdown SortDropdown;
            public PanelElementDescriptor TabDescriptor;

            private readonly Dictionary<ushort, PlayerEntry> _entries = new();
            private SortMode _sortMode = SortMode.Default;
            private string _lastQuery = string.Empty;

            // Reused buffer for sort comparisons \u2014 avoids per-tick allocation.
            private readonly List<BasisNetworkPlayer> _orderBuffer = new();

            // Periodic refresh of the distance chip and the hover tooltip's
            // "joined Xs ago" text. Players move continuously but the player list is
            // a low-detail surface \u2014 0.5s feels live without rebuilding text every frame.
            private float _refreshTimer;
            private const float RefreshInterval = 0.5f;

            public void Initialize()
            {
                BasisNetworkPlayer.OnRemotePlayerJoined += OnRemoteJoined;
                BasisNetworkPlayer.OnRemotePlayerLeft += OnRemoteLeft;
                PinnedPlayers.Changed += OnPinsChanged;

                SearchField.OnValueChanged += OnSearchChanged;
                SortDropdown.OnValueChanged += OnSortChanged;

                RebuildFullList();
                RebuildGridLayout();
            }

            /// <summary>
            /// The grid's height changes whenever cards are added, removed or filtered, and
            /// TabDescriptor's own rect carries no layout controller — rebuilding it resizes
            /// nothing. Walk outward from the grid instead, stopping at the scroll content.
            /// </summary>
            private void RebuildGridLayout()
            {
                if (GridParent == null) return;
                PanelElementDescriptor.RebuildLayoutChain(
                    GridParent, TabDescriptor != null ? TabDescriptor.ContentParent : null);
            }

            private void OnDestroy()
            {
                BasisNetworkPlayer.OnRemotePlayerJoined -= OnRemoteJoined;
                BasisNetworkPlayer.OnRemotePlayerLeft -= OnRemoteLeft;
                PinnedPlayers.Changed -= OnPinsChanged;
                ClearAllEntries();
            }

            private void Update()
            {
                _refreshTimer += Time.unscaledDeltaTime;
                if (_refreshTimer < RefreshInterval) return;
                _refreshTimer = 0f;

                RefreshCardInfo();

                if (_sortMode == SortMode.Distance || _sortMode == SortMode.JoinTime)
                {
                    ReorderButtons();
                }
            }

            private void OnRemoteJoined(BasisNetworkPlayer netPlayer, BasisRemotePlayer _)
            {
                AddPlayerEntry(netPlayer);
                ReorderButtons();
                ApplyFilter();
                UpdateHeader();
                RebuildGridLayout();
            }

            private void OnPinsChanged()
            {
                // Pin status feeds the comparator; just resort, no rebuild needed.
                RefreshCardInfo();
                ReorderButtons();
                RebuildGridLayout();
            }

            private void OnRemoteLeft(BasisNetworkPlayer netPlayer, BasisRemotePlayer _)
            {
                RemovePlayerEntry(netPlayer.playerId);
                UpdateHeader();
                RebuildGridLayout();
            }

            private void OnSortChanged(string value)
            {
                _sortMode = value switch
                {
                    "Distance" => SortMode.Distance,
                    "Name" => SortMode.Name,
                    "Platform" => SortMode.Platform,
                    "Join Time" => SortMode.JoinTime,
                    _ => SortMode.Default,
                };
                ReorderButtons();
                RefreshCardInfo();
                RebuildGridLayout();
            }

            private void OnSearchChanged(string query)
            {
                _lastQuery = query ?? string.Empty;
                ApplyFilter();
                RebuildGridLayout();
            }

            private void UpdateHeader()
            {
                int total = BasisNetworkPlayers.Players.Count;
                int visible = 0;
                foreach (var kvp in _entries)
                {
                    if (kvp.Value.Button != null && kvp.Value.Button.gameObject.activeSelf)
                        visible++;
                }

                bool hasFilter = !string.IsNullOrEmpty(_lastQuery);
                if (visible < total && hasFilter)
                    HeaderGroup.SetTitle(BasisLocalization.Get("menu.players.header.filtered", visible, total));
                else
                    HeaderGroup.SetTitle(BasisLocalization.Get("menu.players.header", total));

                HeaderGroup.SetDescription(BasisLocalization.Get("menu.players.header.description"));
            }

            private void ClearAllEntries()
            {
                foreach (var kvp in _entries)
                {
                    if (kvp.Value.Button != null) kvp.Value.Button.ReleaseInstance();
                }
                _entries.Clear();
            }

            private void RebuildFullList()
            {
                ClearAllEntries();
                foreach (BasisNetworkPlayer player in BasisNetworkPlayers.Players.Values)
                {
                    AddPlayerEntry(player);
                }
                ReorderButtons();
                ApplyFilter();
                UpdateHeader();
            }

            private void AddPlayerEntry(BasisNetworkPlayer netPlayer)
            {
                if (_entries.ContainsKey(netPlayer.playerId)) return;
                if (!GridParent) return;

                PanelButton btn = PanelButton.CreateNew(GridParent);

                bool isLocal = netPlayer.Player != null && netPlayer.Player.IsLocal;
                string name = netPlayer.SafeDisplayName;
                if (string.IsNullOrEmpty(name)) name = BasisLocalization.Get("ui.unknown");

                btn.Descriptor.SetTitle(isLocal ? BasisLocalization.Get("menu.players.you", name) : name);

                // PE Button has no description label, so the full platform/distance/joined
                // line lives on the hover tooltip and the distance alone gets a chip.
                btn.Descriptor.SetTooltip(BuildDescription(netPlayer));

                if (btn.Descriptor.TitleLabel != null)
                {
                    btn.Descriptor.TitleLabel.margin = new Vector4(CardIconStripWidth, 0f, CardInfoStripWidth, 0f);
                    btn.Descriptor.TitleLabel.alignment = TextAlignmentOptions.Left;
                    btn.Descriptor.TitleLabel.overflowMode = TextOverflowModes.Ellipsis;
                }

                string platformIcon = GetPlatformIconAddress(
                    netPlayer.Player != null ? netPlayer.Player.PlayerPlatform : string.Empty);
                if (!string.IsNullOrEmpty(platformIcon)) AddPlatformIcon(btn, platformIcon);

                if (isLocal)
                {
                    btn.ButtonComponent.interactable = false;
                    if (!btn.TryGetComponent(out CanvasGroup canvasGroup))
                        canvasGroup = btn.gameObject.AddComponent<CanvasGroup>();
                    canvasGroup.alpha = 0.4f;
                }

                TextMeshProUGUI infoLabel = AddInfoChip(btn);
                PanelImage pinIcon = AddPinIcon(btn);

                btn.OnClicked += () => OnPlayerClicked(netPlayer);

                PlayerEntry entry = new PlayerEntry
                {
                    NetPlayer = netPlayer,
                    Button = btn,
                    InfoLabel = infoLabel,
                    PinIcon = pinIcon,
                };
                _entries[netPlayer.playerId] = entry;
                RefreshCard(entry);
            }

            private void RemovePlayerEntry(ushort playerId)
            {
                if (_entries.TryGetValue(playerId, out PlayerEntry entry))
                {
                    if (entry.Button != null) entry.Button.ReleaseInstance();
                    _entries.Remove(playerId);
                }
            }

            // ---- Description ----

            private static string BuildDescription(BasisNetworkPlayer netPlayer)
            {
                IBasisPlayer p = netPlayer.Player;
                bool isPinned = p != null && PinnedPlayers.IsPinned(p.UUID);
                bool isLocal = p != null && p.IsLocal;

                string platformLabel = GetPlatformLabel(p != null ? p.PlayerPlatform : "");

                var parts = new List<string>(5) { platformLabel };

                if (isPinned)
                {
                    parts.Add(BasisLocalization.Get("menu.players.pinned"));
                }

                // Distance + range only make sense for remote peers.
                if (!isLocal && p != null && BasisLocalCameraDriver.HasInstance)
                {
                    Vector3 localPos = BasisLocalCameraDriver.Position;
                    Vector3 remotePos = GetRemotePosition(p);
                    float dist = Vector3.Distance(localPos, remotePos);
                    parts.Add(BasisLocalization.Get("menu.players.distanceMeters", dist));

                    if (p is BasisRemotePlayer remote && remote.OutOfRangeFromLocal)
                    {
                        parts.Add(BasisLocalization.Get("menu.players.outOfRange"));
                    }
                }

                if (!isLocal)
                {
                    parts.Add(FormatJoinedAgo(netPlayer.JoinTime));
                }

                return string.Join(" \u2022 ", parts);
            }

            private static Vector3 GetRemotePosition(IBasisPlayer p)
            {
                if (p is BasisRemotePlayer remote && remote.MouthTransform != null)
                    return remote.MouthTransform.position;
                return p.Transform.position;
            }

            private static string FormatJoinedAgo(double joinTime)
            {
                float ago = (float)math.max(0f, Time.realtimeSinceStartupAsDouble - joinTime);
                if (ago < 60f)
                    return BasisLocalization.Get("menu.players.joinedAgoSeconds", Mathf.FloorToInt(ago));
                if (ago < 3600f)
                    return BasisLocalization.Get("menu.players.joinedAgoMinutes", Mathf.FloorToInt(ago / 60f));
                int hours = Mathf.FloorToInt(ago / 3600f);
                int minutes = Mathf.FloorToInt((ago % 3600f) / 60f);
                return BasisLocalization.Get("menu.players.joinedAgoHours", hours, minutes);
            }

            private void RefreshCardInfo()
            {
                foreach (var kvp in _entries)
                {
                    RefreshCard(kvp.Value);
                }
            }

            private static void RefreshCard(PlayerEntry entry)
            {
                if (entry.Button == null || entry.NetPlayer == null) return;

                entry.Button.Descriptor.SetTooltip(BuildDescription(entry.NetPlayer));

                IBasisPlayer p = entry.NetPlayer.Player;
                bool isLocal = p != null && p.IsLocal;

                if (entry.PinIcon != null)
                {
                    bool isPinned = p != null && PinnedPlayers.IsPinned(p.UUID);
                    entry.PinIcon.gameObject.SetActive(isPinned);
                }

                if (entry.InfoLabel == null) return;

                if (isLocal || p == null || !BasisLocalCameraDriver.HasInstance)
                {
                    entry.InfoLabel.transform.parent.gameObject.SetActive(false);
                    return;
                }

                entry.InfoLabel.transform.parent.gameObject.SetActive(true);

                float dist = Vector3.Distance(BasisLocalCameraDriver.Position, GetRemotePosition(p));
                entry.InfoLabel.SetText(BasisLocalization.Get("menu.players.distanceMeters", dist));

                // "Out of Range" does not fit the chip, so dim the distance instead —
                // the full wording is still on the hover tooltip.
                bool outOfRange = p is BasisRemotePlayer remote && remote.OutOfRangeFromLocal;
                Color baseColor = entry.Button.Descriptor.TitleLabel != null
                    ? entry.Button.Descriptor.TitleLabel.color
                    : Color.white;
                entry.InfoLabel.color = outOfRange ? baseColor * new Color(1f, 1f, 1f, 0.45f) : baseColor;
            }

            // ---- Sorting / Reordering ----

            private static float DistanceTo(IBasisPlayer p)
            {
                if (p == null || !BasisLocalCameraDriver.HasInstance) return float.MaxValue;
                return Vector3.Distance(BasisLocalCameraDriver.Position, GetRemotePosition(p));
            }

            private int CompareForCurrentSort(BasisNetworkPlayer a, BasisNetworkPlayer b)
            {
                // Pinned players group above unpinned ones in every sort mode \u2014
                // the pin is intended as a "keep this person at the top" signal,
                // so secondary sorting only orders within each group.
                bool aPinned = a.Player != null && PinnedPlayers.IsPinned(a.Player.UUID);
                bool bPinned = b.Player != null && PinnedPlayers.IsPinned(b.Player.UUID);
                if (aPinned != bPinned) return aPinned ? -1 : 1;

                switch (_sortMode)
                {
                    case SortMode.Distance:
                    {
                        float da = DistanceTo(a.Player);
                        float db = DistanceTo(b.Player);
                        return da.CompareTo(db);
                    }
                    case SortMode.Name:
                    {
                        return string.Compare(
                            a.SafeDisplayName ?? "",
                            b.SafeDisplayName ?? "",
                            StringComparison.OrdinalIgnoreCase);
                    }
                    case SortMode.Platform:
                    {
                        string pa = a.Player != null ? GetPlatformLabel(a.Player.PlayerPlatform) : "";
                        string pb = b.Player != null ? GetPlatformLabel(b.Player.PlayerPlatform) : "";
                        int cmp = string.Compare(pa, pb, StringComparison.OrdinalIgnoreCase);
                        if (cmp != 0) return cmp;
                        return string.Compare(
                            a.SafeDisplayName ?? "",
                            b.SafeDisplayName ?? "",
                            StringComparison.OrdinalIgnoreCase);
                    }
                    case SortMode.JoinTime:
                        // Most recent arrival first \u2014 common ask is "who just joined?"
                        return b.JoinTime.CompareTo(a.JoinTime);
                    default:
                        // Default: oldest-first arrival order, mirrors the previous
                        // pinned-then-append behavior for users who liked it.
                        return a.JoinTime.CompareTo(b.JoinTime);
                }
            }

            private void ReorderButtons()
            {
                if (GridParent == null) return;

                _orderBuffer.Clear();
                foreach (var kvp in _entries)
                {
                    if (kvp.Value.NetPlayer != null) _orderBuffer.Add(kvp.Value.NetPlayer);
                }
                _orderBuffer.Sort(CompareForCurrentSort);

                int expected = 0;
                bool inOrder = true;
                for (int i = 0; i < _orderBuffer.Count && inOrder; i++)
                {
                    if (_entries.TryGetValue(_orderBuffer[i].playerId, out PlayerEntry check))
                    {
                        if (check.Button != null && check.Button.transform.GetSiblingIndex() != expected++)
                            inOrder = false;
                    }
                }
                if (inOrder) return;

                int sibling = 0;
                for (int i = 0; i < _orderBuffer.Count; i++)
                {
                    if (_entries.TryGetValue(_orderBuffer[i].playerId, out PlayerEntry entry))
                    {
                        if (entry.Button != null)
                            entry.Button.transform.SetSiblingIndex(sibling++);
                    }
                }
            }

            // ---- Filter / Search ----

            private void ApplyFilter()
            {
                string query = _lastQuery.Trim();
                bool hasQuery = query.Length > 0;
                string queryLower = hasQuery ? query.ToLowerInvariant() : string.Empty;

                foreach (var kvp in _entries)
                {
                    PlayerEntry entry = kvp.Value;
                    if (entry.Button == null || entry.NetPlayer == null) continue;

                    bool show = true;

                    if (hasQuery)
                    {
                        string n = entry.NetPlayer.SafeDisplayName ?? "";
                        string uuid = entry.NetPlayer.Player != null
                            ? entry.NetPlayer.Player.UUID ?? "" : "";
                        show = n.ToLowerInvariant().Contains(queryLower)
                            || uuid.ToLowerInvariant().Contains(queryLower);
                    }

                    entry.Button.gameObject.SetActive(show);
                }

                UpdateHeader();
            }

            // ---- Click handling ----

            private void OnPlayerClicked(BasisNetworkPlayer netPlayer)
            {
                if (netPlayer.Player == null) return;

                if (!netPlayer.Player.IsLocal)
                {
                    IndividualPlayerProvider.remotePlayer = (BasisRemotePlayer)netPlayer.Player;
                    BasisMainMenu.OpenWithProvider(IndividualPlayerProvider.StaticTitle);
                }
            }
        }
    }
}
