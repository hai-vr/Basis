using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    /// <summary>
    /// Main menu provider that displays a searchable list of all connected players.
    /// Supports filtering by Name and UUID via dropdown.
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
                BasisMainMenu.CloseActivePanel();
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
            searchField.Descriptor.SetDescription(BasisLocalization.Get("menu.players.search.byName"));

            // Search mode dropdown right below search. Entries stay as stable
            // identifiers ("Name"/"UUID") because the dropdown value is compared
            // against those strings when filtering.
            PanelDropdown modeDropdown = PanelDropdown.CreateNewEntry(root);
            modeDropdown.Descriptor.SetTitle(BasisLocalization.Get("menu.players.searchMode"));
            modeDropdown.AssignEntries(new List<string> { "Name", "UUID" });
            modeDropdown.SetValueWithoutNotify("Name");

            // Sort mode dropdown. Entries are stable English identifiers — the
            // controller switches on the literal string the same way ModeDropdown
            // does, so adding a translated label here would silently disable sort.
            PanelDropdown sortDropdown = PanelDropdown.CreateNewEntry(root);
            sortDropdown.Descriptor.SetTitle(BasisLocalization.Get("menu.players.sortMode"));
            sortDropdown.Descriptor.SetDescription(BasisLocalization.Get("menu.players.sortMode.description"));
            sortDropdown.AssignEntries(new List<string> { "Default", "Distance", "Name", "Platform", "Join Time" });
            sortDropdown.SetValueWithoutNotify("Default");

            // Direction filter dropdown — hide players in front / behind based on
            // the local camera's horizontal facing.
            PanelDropdown directionDropdown = PanelDropdown.CreateNewEntry(root);
            directionDropdown.Descriptor.SetTitle(BasisLocalization.Get("menu.players.directionFilter"));
            directionDropdown.Descriptor.SetDescription(BasisLocalization.Get("menu.players.directionFilter.description"));
            directionDropdown.AssignEntries(new List<string> { "All", "In Front", "Behind" });
            directionDropdown.SetValueWithoutNotify("All");

            // Player count header
            var headerGroup = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, root);

            // Player rows are virtualized into the same scroll content below the
            // controls — fields must be assigned before Initialize().
            _controller = panel.gameObject.AddComponent<UserListController>();
            _controller.GridParent = root;
            _controller.HeaderGroup = headerGroup;
            _controller.SearchField = searchField;
            _controller.ModeDropdown = modeDropdown;
            _controller.SortDropdown = sortDropdown;
            _controller.DirectionDropdown = directionDropdown;
            _controller.TabDescriptor = tab.Descriptor;
            _controller.Initialize();

            panel.Descriptor.ForceRebuild();
        }

        public override void OnReleaseEvent()
        {
            _controller = null;
        }

        // ======== Helpers ========

        public static string GetPlatformIconAddress(string platform)
        {
            if (string.IsNullOrEmpty(platform)) return string.Empty;
            string lower = platform.ToLowerInvariant();
            if (lower.Contains("windows")) return AddressableAssets.Sprites.PlatformStandaloneWindows64;
            if (lower.Contains("osx") || lower.Contains("mac")) return AddressableAssets.Sprites.PlatformStandaloneOSX;
            if (lower.Contains("linux")) return AddressableAssets.Sprites.PlatformStandaloneLinux64;
            if (lower.Contains("android")) return AddressableAssets.Sprites.PlatformMobileAndroid;
            if (lower.Contains("iphone") || lower.Contains("ios")) return AddressableAssets.Sprites.PlatformMobileiOS;
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

        private enum SearchMode { Name, UUID }
        private enum SortMode { Default, Distance, Name, Platform, JoinTime }
        private enum DirectionFilter { All, InFront, Behind }

        /// <summary>
        /// Manages the player list, search/filter, sort, direction filter, and
        /// join/leave events. Backed by a <see cref="PanelVirtualScrollList"/>: the
        /// filtered + sorted player order is the data source, and only the on-screen
        /// rows exist as buttons, so the list scales to thousands of players without
        /// the per-frame ClipperRegistry/batch cost of one button per player. The
        /// search/sort controls share the scroll content above the rows (reserved via
        /// the list's HeaderHeight) and scroll with them, as before.
        /// </summary>
        private sealed class UserListController : MonoBehaviour
        {
            public RectTransform GridParent;
            public PanelElementDescriptor HeaderGroup;
            public PanelTextField SearchField;
            public PanelDropdown ModeDropdown;
            public PanelDropdown SortDropdown;
            public PanelDropdown DirectionDropdown;
            public PanelElementDescriptor TabDescriptor;

            // Fixed row height: a title plus up to two lines of description
            // (platform • pinned • distance • direction • joined-ago). Tune in-editor
            // if the button style's natural height differs.
            private const float RowHeight = 110f;

            private PanelVirtualScrollList _list;

            // Current filtered + sorted player order — the virtual list's data source.
            private readonly List<BasisNetworkPlayer> _ordered = new();

            private SearchMode _searchMode = SearchMode.Name;
            private SortMode _sortMode = SortMode.Default;
            private DirectionFilter _directionFilter = DirectionFilter.All;
            private string _lastQuery = string.Empty;

            // Periodic refresh of distance, direction label, and "joined Xs ago"
            // text. Players move continuously but the player list is a low-detail
            // surface — 0.5s feels live without rebuilding text every frame.
            private float _refreshTimer;
            private const float RefreshInterval = 0.5f;

            public void Initialize()
            {
                // The controls were just added to the scroll content; lay them out and
                // total their height so the virtual list reserves that space for them.
                float headerHeight = MeasureHeaderHeight();

                _list = PanelVirtualScrollList.AttachTo(GridParent, RowHeight);
                if (_list != null) _list.HeaderHeight = headerHeight;

                BasisNetworkPlayer.OnRemotePlayerJoined += OnRemoteJoined;
                BasisNetworkPlayer.OnRemotePlayerLeft += OnRemoteLeft;
                PinnedPlayers.Changed += OnPinsChanged;

                SearchField.OnValueChanged += OnSearchChanged;
                ModeDropdown.OnValueChanged += OnModeChanged;
                SortDropdown.OnValueChanged += OnSortChanged;
                DirectionDropdown.OnValueChanged += OnDirectionChanged;

                UpdateSearchHint();
                Rebuild();
            }

            private void OnDestroy()
            {
                BasisNetworkPlayer.OnRemotePlayerJoined -= OnRemoteJoined;
                BasisNetworkPlayer.OnRemotePlayerLeft -= OnRemoteLeft;
                PinnedPlayers.Changed -= OnPinsChanged;
            }

            // Sum the laid-out heights of the control children currently in GridParent
            // (no player rows exist yet at Initialize time). Done before AttachTo
            // disables the content's layout group, which freezes the controls in place.
            private float MeasureHeaderHeight()
            {
                if (GridParent == null) return 0f;

                LayoutRebuilder.ForceRebuildLayoutImmediate(GridParent);

                float spacing = 0f, pad = 0f;
                if (GridParent.TryGetComponent(out VerticalLayoutGroup vlg))
                {
                    spacing = vlg.spacing;
                    pad = vlg.padding.top + vlg.padding.bottom;
                }

                float total = pad;
                int n = GridParent.childCount;
                for (int i = 0; i < n; i++)
                {
                    if (GridParent.GetChild(i) is RectTransform c)
                    {
                        total += c.rect.height;
                        if (i < n - 1) total += spacing;
                    }
                }
                return total;
            }

            private void Update()
            {
                _refreshTimer += Time.unscaledDeltaTime;
                if (_refreshTimer < RefreshInterval) return;
                _refreshTimer = 0f;

                // Distance sort order and the direction filter depend on positions that
                // drift every frame, so re-evaluate the ordering. Otherwise the order is
                // stable and only the visible rows' text (distance, joined-ago) needs a
                // refresh — cheap, since only the on-screen rows exist.
                if (_sortMode == SortMode.Distance || _directionFilter != DirectionFilter.All)
                {
                    Rebuild();
                }
                else
                {
                    _list?.RebindVisible();
                }
            }

            private void OnRemoteJoined(BasisNetworkPlayer netPlayer, BasisRemotePlayer _) => Rebuild();
            private void OnRemoteLeft(BasisNetworkPlayer netPlayer, BasisRemotePlayer _) => Rebuild();
            private void OnPinsChanged() => Rebuild();

            private void OnModeChanged(string value)
            {
                _searchMode = value == "UUID" ? SearchMode.UUID : SearchMode.Name;
                UpdateSearchHint();
                Rebuild();
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
                Rebuild();
            }

            private void OnDirectionChanged(string value)
            {
                _directionFilter = value switch
                {
                    "In Front" => DirectionFilter.InFront,
                    "Behind" => DirectionFilter.Behind,
                    _ => DirectionFilter.All,
                };
                Rebuild();
            }

            private void OnSearchChanged(string query)
            {
                _lastQuery = query ?? string.Empty;
                Rebuild();
            }

            private void UpdateSearchHint()
            {
                SearchField.Descriptor.SetDescription(BasisLocalization.Get(
                    _searchMode == SearchMode.UUID
                        ? "menu.players.search.byUuid"
                        : "menu.players.search.byName"));
            }

            // ---- Data source ----

            private void Rebuild()
            {
                RebuildOrdered();
                _list?.SetDataSource(_ordered.Count, CreateRow, BindRow);
                UpdateHeader();
            }

            private void RebuildOrdered()
            {
                _ordered.Clear();

                string query = _lastQuery.Trim();
                bool hasQuery = query.Length > 0;
                string queryLower = hasQuery ? query.ToLowerInvariant() : string.Empty;

                bool hasCamera = BasisLocalCameraDriver.HasInstance;
                Vector3 localPos = hasCamera ? BasisLocalCameraDriver.Position : Vector3.zero;
                Vector3 forward = hasCamera ? BasisLocalCameraDriver.Forward() : Vector3.zero;
                forward.y = 0f;
                bool forwardValid = forward.sqrMagnitude > 0.0001f;
                if (forwardValid) forward.Normalize();

                foreach (BasisNetworkPlayer netPlayer in BasisNetworkPlayers.Players.Values)
                {
                    if (netPlayer == null) continue;
                    if (!PassesFilter(netPlayer, hasQuery, queryLower, forwardValid, forward, localPos)) continue;
                    _ordered.Add(netPlayer);
                }

                _ordered.Sort(CompareForCurrentSort);
            }

            private bool PassesFilter(BasisNetworkPlayer netPlayer, bool hasQuery, string queryLower, bool forwardValid, Vector3 forward, Vector3 localPos)
            {
                if (hasQuery)
                {
                    if (_searchMode == SearchMode.UUID)
                    {
                        string uuid = netPlayer.Player != null ? netPlayer.Player.UUID ?? "" : "";
                        if (!uuid.ToLowerInvariant().Contains(queryLower)) return false;
                    }
                    else
                    {
                        string n = netPlayer.SafeDisplayName ?? "";
                        if (!n.ToLowerInvariant().Contains(queryLower)) return false;
                    }
                }

                // Direction filter: skip the local player (no direction relative to
                // itself) and skip when the camera isn't ready yet, otherwise a
                // freshly-opened menu would briefly hide everyone.
                if (_directionFilter != DirectionFilter.All)
                {
                    IBasisPlayer p = netPlayer.Player;
                    bool isLocal = p != null && p.IsLocal;
                    if (!isLocal && p != null && forwardValid)
                    {
                        Vector3 toRemote = GetRemotePosition(p) - localPos;
                        toRemote.y = 0f;
                        if (toRemote.sqrMagnitude > 0.0001f)
                        {
                            toRemote.Normalize();
                            float dot = Vector3.Dot(forward, toRemote);
                            bool show = _directionFilter == DirectionFilter.InFront ? dot > 0f : dot <= 0f;
                            if (!show) return false;
                        }
                    }
                }

                return true;
            }

            private RectTransform CreateRow(RectTransform content)
            {
                PanelButton btn = PanelButton.CreateNew(content);
                if (!btn.TryGetComponent(out CanvasGroup _))
                {
                    btn.gameObject.AddComponent<CanvasGroup>();
                }
                return (RectTransform)btn.transform;
            }

            private void BindRow(RectTransform row, int index)
            {
                if (index < 0 || index >= _ordered.Count) return;
                BasisNetworkPlayer netPlayer = _ordered[index];
                if (netPlayer == null) return;

                PanelButton btn = row.GetComponent<PanelButton>();
                if (btn == null) return;

                IBasisPlayer player = netPlayer.Player;
                bool isLocal = player != null && player.IsLocal;
                string name = netPlayer.SafeDisplayName;
                if (string.IsNullOrEmpty(name)) name = BasisLocalization.Get("ui.unknown");

                btn.Descriptor.SetTitle(isLocal ? BasisLocalization.Get("menu.players.you", name) : name);
                btn.Descriptor.SetDescription(BuildDescription(netPlayer));

                if (btn.ButtonComponent != null) btn.ButtonComponent.interactable = !isLocal;
                if (btn.TryGetComponent(out CanvasGroup cg)) cg.alpha = isLocal ? 0.4f : 1f;

                // OnClicked is a plain assignable field, so replacing it per bind is
                // safe on a recycled button — no handler stacking.
                btn.OnClicked = () => OnPlayerClicked(netPlayer);
            }

            private void UpdateHeader()
            {
                int total = BasisNetworkPlayers.Players.Count;
                int visible = _ordered.Count;

                bool hasFilter = !string.IsNullOrEmpty(_lastQuery) || _directionFilter != DirectionFilter.All;
                if (visible < total && hasFilter)
                    HeaderGroup.SetTitle(BasisLocalization.Get("menu.players.header.filtered", visible, total));
                else
                    HeaderGroup.SetTitle(BasisLocalization.Get("menu.players.header", total));

                HeaderGroup.SetDescription(BasisLocalization.Get("menu.players.header.description"));
            }

            // ---- Description ----

            private string BuildDescription(BasisNetworkPlayer netPlayer)
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

                // Distance + direction + range only make sense for remote peers.
                if (!isLocal && p != null && BasisLocalCameraDriver.HasInstance)
                {
                    Vector3 localPos = BasisLocalCameraDriver.Position;
                    Vector3 remotePos = GetRemotePosition(p);
                    float dist = Vector3.Distance(localPos, remotePos);
                    parts.Add(BasisLocalization.Get("menu.players.distanceMeters", dist));

                    string dirLabel = ComputeDirectionLabel(localPos, remotePos);
                    if (!string.IsNullOrEmpty(dirLabel))
                    {
                        parts.Add(dirLabel);
                    }

                    if (p is BasisRemotePlayer remote && remote.OutOfRangeFromLocal)
                    {
                        parts.Add(BasisLocalization.Get("menu.players.outOfRange"));
                    }
                }

                if (!isLocal)
                {
                    parts.Add(FormatJoinedAgo(netPlayer.JoinTime));
                }

                return string.Join(" • ", parts);
            }

            private static string ComputeDirectionLabel(Vector3 localPos, Vector3 remotePos)
            {
                Vector3 toRemote = remotePos - localPos;
                toRemote.y = 0f;
                if (toRemote.sqrMagnitude < 0.0001f) return string.Empty;

                Vector3 forward = BasisLocalCameraDriver.Forward();
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) return string.Empty;

                forward.Normalize();
                toRemote.Normalize();
                float dot = Vector3.Dot(forward, toRemote);
                return dot > 0f
                    ? BasisLocalization.Get("menu.players.inFront")
                    : BasisLocalization.Get("menu.players.behind");
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

            // ---- Sorting ----

            private static float DistanceTo(IBasisPlayer p)
            {
                if (p == null || !BasisLocalCameraDriver.HasInstance) return float.MaxValue;
                return Vector3.Distance(BasisLocalCameraDriver.Position, GetRemotePosition(p));
            }

            private int CompareForCurrentSort(BasisNetworkPlayer a, BasisNetworkPlayer b)
            {
                // Pinned players group above unpinned ones in every sort mode —
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
                        // Most recent arrival first — common ask is "who just joined?"
                        return b.JoinTime.CompareTo(a.JoinTime);
                    default:
                        // Default: oldest-first arrival order, mirrors the previous
                        // pinned-then-append behavior for users who liked it.
                        return a.JoinTime.CompareTo(b.JoinTime);
                }
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
