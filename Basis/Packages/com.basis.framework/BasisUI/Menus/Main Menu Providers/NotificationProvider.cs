using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    public class NotificationProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new NotificationProvider());
        }

        public const string TitleKey = "notifications.title";
        public static string TitleStatic => BasisLocalization.Get(TitleKey);
        public override string Title => TitleStatic;
        public override string IconAddress => AddressableAssets.Sprites.FileTray;
        public override int Order => 90;
        public override bool Hidden => false;

        private NotificationPanelController _controller;

        public override void OnButtonCreated(PanelButton button)
        {
            // Pulse the bell icon while there are unresolved pending notifications.
            // No selection/indicator dot — the pulse alone signals pending items.
            NotificationBellPulse pulse = button.gameObject.AddComponent<NotificationBellPulse>();
            pulse.Target = button.Descriptor.IconImage;
        }

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

            PanelTabPage tab = PanelTabPage.CreateVertical(panel.Descriptor.ContentParent);
            tab.Descriptor.SetTitle(Title);
            tab.Descriptor.SetIcon(AddressableAssets.Sprites.FileTray);
            RectTransform root = tab.Descriptor.ContentParent;

            _controller = panel.gameObject.AddComponent<NotificationPanelController>();
            _controller.Panel = panel;
            _controller.TabDescriptor = tab.Descriptor;
            _controller.BuildActionTiles(root);
            _controller.PendingHeader = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, root);
            _controller.PendingGrid = BuildCardGrid(root, "PendingGrid");
            _controller.HistoryHeader = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, root);
            _controller.HistoryGrid = BuildCardGrid(root, "HistoryGrid");
            _controller.Initialize();

            panel.Descriptor.ForceRebuild();
        }

        public override void OnReleaseEvent()
        {
            _controller = null;
        }

        private const string AllEntry = "All";

        private static readonly Vector2 CardSize = new Vector2(440f, 100f);
        private const float CardIconStripWidth = 68f;
        private const float CardInfoStripWidth = 140f;

        private static readonly Color OnlineTint = new Color(0.45f, 0.85f, 0.5f, 1f);
        private static readonly Color OfflineTint = new Color(0.95f, 0.4f, 0.4f, 1f);
        private static readonly Color PendingTint = new Color(1f, 0.62f, 0.2f, 1f);
        private static readonly Color AcceptedTint = new Color(0.13f, 0.77f, 0.37f, 1f);
        private static readonly Color DeniedTint = new Color(0.94f, 0.27f, 0.27f, 1f);
        private static readonly Color DismissedTint = new Color(0.61f, 0.64f, 0.69f, 1f);

        private static List<string> FilterEntries()
        {
            List<string> entries = new List<string> { AllEntry };
            entries.AddRange(Enum.GetNames(typeof(BasisNotificationCategory)));
            return entries;
        }

        private static List<string> FilterLabels()
        {
            List<string> labels = new List<string> { BasisLocalization.Get("notifications.category.all") };
            Array categories = Enum.GetValues(typeof(BasisNotificationCategory));
            for (int i = 0; i < categories.Length; i++)
            {
                labels.Add(CategoryLabel((BasisNotificationCategory)categories.GetValue(i)));
            }
            return labels;
        }

        private static List<string> FilterTooltips()
        {
            List<string> tooltips = new List<string> { BasisLocalization.Get("notifications.category.all.tooltip") };
            Array categories = Enum.GetValues(typeof(BasisNotificationCategory));
            for (int i = 0; i < categories.Length; i++)
            {
                tooltips.Add(CategoryTooltip((BasisNotificationCategory)categories.GetValue(i)));
            }
            return tooltips;
        }

        private static string EntryFor(BasisNotificationCategory? category)
        {
            return category.HasValue ? category.Value.ToString() : AllEntry;
        }

        private static BasisNotificationCategory? CategoryFor(string entry)
        {
            if (Enum.TryParse(entry, out BasisNotificationCategory parsed)
                && Enum.IsDefined(typeof(BasisNotificationCategory), parsed))
            {
                return parsed;
            }
            return null;
        }

        public static string CategoryLabel(BasisNotificationCategory category)
        {
            switch (category)
            {
                case BasisNotificationCategory.Player:
                    return BasisLocalization.Get("notifications.category.player");
                case BasisNotificationCategory.Content:
                    return BasisLocalization.Get("notifications.category.content");
                case BasisNotificationCategory.Network:
                    return BasisLocalization.Get("notifications.category.network");
                case BasisNotificationCategory.Avatar:
                    return BasisLocalization.Get("notifications.category.avatar");
                case BasisNotificationCategory.Developer:
                    return BasisLocalization.Get("notifications.category.developer");
                default:
                    return BasisLocalization.Get("notifications.category.system");
            }
        }

        public static string CategoryTooltip(BasisNotificationCategory category)
        {
            switch (category)
            {
                case BasisNotificationCategory.Player:
                    return BasisLocalization.Get("notifications.category.player.tooltip");
                case BasisNotificationCategory.Content:
                    return BasisLocalization.Get("notifications.category.content.tooltip");
                case BasisNotificationCategory.Network:
                    return BasisLocalization.Get("notifications.category.network.tooltip");
                case BasisNotificationCategory.Avatar:
                    return BasisLocalization.Get("notifications.category.avatar.tooltip");
                case BasisNotificationCategory.Developer:
                    return BasisLocalization.Get("notifications.category.developer.tooltip");
                default:
                    return BasisLocalization.Get("notifications.category.system.tooltip");
            }
        }

        private static string StatusLabel(BasisNotificationStatus status)
        {
            switch (status)
            {
                case BasisNotificationStatus.Pending:
                    return BasisLocalization.Get("notifications.pending");
                case BasisNotificationStatus.Accepted:
                    return BasisLocalization.Get("notifications.outcome.accepted");
                case BasisNotificationStatus.Denied:
                    return BasisLocalization.Get("notifications.outcome.denied");
                default:
                    return BasisLocalization.Get("notifications.outcome.dismissed");
            }
        }

        private static Color StatusTint(BasisNotificationStatus status)
        {
            switch (status)
            {
                case BasisNotificationStatus.Pending:
                    return PendingTint;
                case BasisNotificationStatus.Accepted:
                    return AcceptedTint;
                case BasisNotificationStatus.Denied:
                    return DeniedTint;
                default:
                    return DismissedTint;
            }
        }

        private static string FormatTime(DateTime utc) => utc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);

        private static string FormatDateTime(DateTime utc) => utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

        private static string Flatten(string text) => text.Replace("\r", string.Empty).Replace('\n', ' ');

        private static string IconFor(BasisNotification n) =>
            string.IsNullOrEmpty(n.IconAddress) ? AddressableAssets.Sprites.Information : n.IconAddress;

        private static string TitleFor(BasisNotification n) =>
            string.IsNullOrEmpty(n.Title) ? BasisLocalization.Get("notifications.dialog.generic") : n.Title;

        private static RectTransform BuildCardGrid(RectTransform parent, string name)
        {
            GameObject gridGO = new GameObject(name, typeof(RectTransform));
            gridGO.layer = parent.gameObject.layer;
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

        private static TextMeshProUGUI AddInfoChip(PanelButton buttonPanel)
        {
            PanelElementDescriptor desc = buttonPanel.Descriptor;

            GameObject chipGo = new GameObject("Info Chip", typeof(RectTransform));
            chipGo.layer = desc.gameObject.layer;
            RectTransform rt = (RectTransform)chipGo.transform;
            rt.SetParent(desc.rectTransform, false);
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-70, 0);
            rt.sizeDelta = new Vector2(120, 34);

            Image background = chipGo.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.6f);
            background.raycastTarget = false;

            LayoutElement layoutElement = chipGo.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            GameObject textGo = new GameObject("Value", typeof(RectTransform));
            textGo.layer = chipGo.layer;
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

        private static PanelImage AddCardIcon(PanelButton buttonPanel, string spriteAddress)
        {
            PanelImage icon = PanelImage.CreateNew(buttonPanel.Descriptor);
            icon.SetIcon(AddressableAssets.GetSprite(spriteAddress), true);
            icon.rectTransform.anchorMin = new Vector2(0, 0.5f);
            icon.rectTransform.anchorMax = new Vector2(0, 0.5f);
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(36, 0);
            icon.rectTransform.sizeDelta = new Vector2(40f, 40f);
            return icon;
        }

        private static void ClampScrollViewport(RectTransform content)
        {
            if (content == null) return;

            ScrollRect scroll = content.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.viewport == null) return;

            RectTransform viewport = scroll.viewport;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(-25f, 0f);
            if (!viewport.TryGetComponent(out RectMask2D _))
            {
                viewport.gameObject.AddComponent<RectMask2D>();
            }
        }

        /// <summary>
        /// Pulses the bell button's icon color while there are unresolved pending
        /// notifications so the main menu draws the eye. Self-contained: destroyed with
        /// the button, and restores the icon's original color when nothing is pending.
        /// (No dedicated has/has-no-notification icon exists in the icon set, so colour
        /// is the signal.)
        /// </summary>
        private sealed class NotificationBellPulse : MonoBehaviour
        {
            public Graphic Target;

            private static readonly Color PulseColor = new Color(1f, 0.62f, 0.2f, 1f);
            private const float Speed = 3.2f;

            private Color _baseColor = Color.white;
            private bool _captured;
            private bool _pulsing;

            private void Update()
            {
                if (Target == null) return;
                if (!_captured)
                {
                    _baseColor = Target.color;
                    _captured = true;
                }

                if (BasisNotificationCenter.PendingCount > 0)
                {
                    float t = (Mathf.Sin(Time.unscaledTime * Speed) + 1f) * 0.5f;
                    // SetColor is a render-only update; Graphic.color calls SetVerticesDirty and
                    // would rebuild + rebatch the whole hotbar canvas every frame while pending.
                    Target.canvasRenderer.SetColor(Color.Lerp(_baseColor, PulseColor, t));
                    _pulsing = true;
                }
                else if (_pulsing)
                {
                    Target.canvasRenderer.SetColor(_baseColor);
                    _pulsing = false;
                }
            }

            private void OnDisable()
            {
                if (Target != null && _captured)
                {
                    Target.canvasRenderer.SetColor(_baseColor);
                    _pulsing = false;
                }
            }
        }

        private sealed class NotificationCard
        {
            public BasisNotification Notification;
            public PanelButton Button;
            public TextMeshProUGUI ChipLabel;
            public PanelImage Icon;
            public string IconAddress;
            public RectTransform Grid;
            public BasisNotificationStatus Status;
            public bool Visible;
        }

        private sealed class NotificationPanelController : MonoBehaviour
        {
            public BasisMenuPanel Panel;
            public PanelElementDescriptor TabDescriptor;
            public PanelElementDescriptor PendingHeader;
            public PanelElementDescriptor HistoryHeader;
            public RectTransform PendingGrid;
            public RectTransform HistoryGrid;

            private PanelButton _ignoreTile;
            private PanelButton _searchTile;

            private DialogBox<bool> _searchDialog;
            private DialogBox<bool> _detailDialog;
            private DialogBox<bool> _clearDialog;

            private readonly Dictionary<Guid, NotificationCard> _cards = new();
            private readonly HashSet<Guid> _liveIds = new();
            private readonly List<Guid> _removeBuffer = new();
            private readonly List<NotificationCard> _cardPool = new();
            private const int CardPoolCap = 32;
            private const int FirstFrameCards = 24;
            private const int CardsPerFrame = 8;
            private int _lastAddFrame = -1;
            private int _pendingCount;
            private int _historyCount;
            private int _historyVisible;
            private bool _dirty;

            public void Initialize()
            {
                _dirty = true;
                Flush();
            }

            private void OnEnable()
            {
                BasisNotificationCenter.Changed -= MarkDirty;
                BasisNotificationCenter.Changed += MarkDirty;
                _dirty = true;
            }

            private void OnDisable()
            {
                BasisNotificationCenter.Changed -= MarkDirty;
                CancelDialogs();
            }

            private void OnDestroy()
            {
                BasisNotificationCenter.Changed -= MarkDirty;
                CancelDialogs();
                ClearAllCards();
            }

            private void MarkDirty() => _dirty = true;

            private void LateUpdate() => Flush();

            private void Flush()
            {
                if (!_dirty || !PendingGrid || !HistoryGrid) return;
                _dirty = false;

                bool changed = Reconcile();
                bool orderChanged = changed && ApplySiblingOrder();
                bool filterChanged = ApplyFilter();
                UpdateHeaders();

                if (changed || orderChanged || filterChanged)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(PendingGrid);
                    PanelElementDescriptor.RebuildLayoutChain(
                        HistoryGrid, TabDescriptor != null ? TabDescriptor.ContentParent : null);
                }
            }

            // ---- Action tiles ----

            public void BuildActionTiles(RectTransform container)
            {
                RectTransform tiles = PanelElementDescriptor.BuildActionRow(container, "NotificationActions");
                if (tiles.TryGetComponent(out HorizontalLayoutGroup tilesLayout))
                {
                    tilesLayout.childForceExpandWidth = false;
                    tilesLayout.childAlignment = TextAnchor.MiddleLeft;
                }

                _ignoreTile = CreateTile(tiles, AddressableAssets.Sprites.Unlocked,
                    BasisLocalization.Get("notifications.ignore"), null);
                _ignoreTile.TooltipProvider = () => string.Format("{0}  •  {1}",
                    BasisLocalization.Get("notifications.ignore.description"),
                    BasisLocalization.Get(BasisNotificationCenter.IgnoreMode ? "ui.option.on" : "ui.option.off"));
                _ignoreTile.OnClicked += () =>
                {
                    BasisNotificationCenter.IgnoreMode = !BasisNotificationCenter.IgnoreMode;
                    UpdateIgnoreVisual();
                };
                UpdateIgnoreVisual();

                _searchTile = CreateTile(tiles, AddressableAssets.Sprites.Search,
                    BasisLocalization.Get("ui.search.label"),
                    BasisLocalization.Get("notifications.filter.search"));
                _searchTile.OnClicked += () => _ = ShowSearchDialogAsync();
                UpdateSearchVisual();

                PanelButton clearTile = CreateTile(tiles, AddressableAssets.Sprites.Trash,
                    BasisLocalization.Get("notifications.clearHistory"),
                    BasisLocalization.Get("notifications.clearHistory.tooltip"));
                clearTile.OnClicked += () => _ = ShowClearHistoryDialogAsync();
            }

            private static PanelButton CreateTile(RectTransform parent, string icon, string title, string tooltip)
            {
                PanelButton tile = PanelButton.CreateNew(PanelButton.ButtonStyles.Hotbar, parent);
                tile.SetIcon(icon);
                tile.Descriptor.SetTitle(title);
                if (!string.IsNullOrEmpty(tooltip)) tile.Descriptor.SetTooltip(tooltip);
                tile.SetSize(new Vector2(150, 150));
                tile.Layout.flexibleWidth = 0f;
                tile.EnableIconHoverAnimation();
                return tile;
            }

            private void UpdateIgnoreVisual()
            {
                if (_ignoreTile == null) return;
                bool on = BasisNotificationCenter.IgnoreMode;
                _ignoreTile.SetIcon(on ? AddressableAssets.Sprites.Locked : AddressableAssets.Sprites.Unlocked);
                if (_ignoreTile.Descriptor.IconImage != null)
                    _ignoreTile.Descriptor.IconImage.color = on ? OnlineTint : OfflineTint;
            }

            private void UpdateSearchVisual()
            {
                if (_searchTile == null || _searchTile.Descriptor.IconImage == null) return;
                _searchTile.Descriptor.IconImage.color = BasisNotificationCenter.HistoryFiltered ? OnlineTint : Color.white;
            }

            // ---- Cards ----

            private bool Reconcile()
            {
                IReadOnlyList<BasisNotification> all = BasisNotificationCenter.All;
                bool changed = false;

                int pending = 0, history = 0, historyVisible = 0;
                _liveIds.Clear();
                for (int i = 0; i < all.Count; i++)
                {
                    BasisNotification n = all[i];
                    _liveIds.Add(n.Id);
                    if (n.Status == BasisNotificationStatus.Pending)
                    {
                        pending++;
                    }
                    else
                    {
                        history++;
                        if (BasisNotificationCenter.PassesHistoryFilter(n)) historyVisible++;
                    }
                }
                _pendingCount = pending;
                _historyCount = history;
                _historyVisible = historyVisible;

                _removeBuffer.Clear();
                foreach (var kvp in _cards)
                {
                    if (!_liveIds.Contains(kvp.Key)) _removeBuffer.Add(kvp.Key);
                }
                for (int i = 0; i < _removeBuffer.Count; i++)
                {
                    ReleaseCard(_removeBuffer[i]);
                    changed = true;
                }

                int budget = _lastAddFrame == Time.frameCount
                    ? 0
                    : _cards.Count == 0 ? FirstFrameCards : CardsPerFrame;

                bool complete = true;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    BasisNotification n = all[i];

                    if (_cards.TryGetValue(n.Id, out NotificationCard existing))
                    {
                        if (existing.Status != n.Status)
                        {
                            BindCard(existing, n);
                            changed = true;
                        }
                        continue;
                    }

                    if (budget <= 0)
                    {
                        complete = false;
                        continue;
                    }

                    NotificationCard card = AcquireCard();
                    if (card == null) continue;

                    BindCard(card, n);
                    _cards[n.Id] = card;
                    _lastAddFrame = Time.frameCount;
                    budget--;
                    changed = true;
                }

                if (!complete) _dirty = true;
                return changed;
            }

            private NotificationCard AcquireCard()
            {
                while (_cardPool.Count > 0)
                {
                    int last = _cardPool.Count - 1;
                    NotificationCard pooled = _cardPool[last];
                    _cardPool.RemoveAt(last);
                    if (pooled.Button != null) return pooled;
                }
                return CreateCard();
            }

            private NotificationCard CreateCard()
            {
                PanelButton button = PanelButton.CreateNew(PendingGrid);
                if (button == null) return null;

                button.Descriptor.DisableRichText();
                if (button.Descriptor.TitleLabel != null)
                {
                    button.Descriptor.TitleLabel.margin = new Vector4(CardIconStripWidth, 0f, CardInfoStripWidth, 0f);
                    button.Descriptor.TitleLabel.alignment = TextAlignmentOptions.Left;
                    button.Descriptor.TitleLabel.overflowMode = TextOverflowModes.Ellipsis;
                }

                NotificationCard card = new NotificationCard { Button = button, Grid = PendingGrid, ChipLabel = AddInfoChip(button) };
                button.OnClicked = () => OnCardClicked(card.Notification);
                button.TooltipProvider = () => BuildCardTooltip(card);
                return card;
            }

            private void BindCard(NotificationCard card, BasisNotification n)
            {
                card.Notification = n;
                card.Status = n.Status;

                RectTransform grid = n.Status == BasisNotificationStatus.Pending ? PendingGrid : HistoryGrid;
                if (card.Grid != grid)
                {
                    card.Grid = grid;
                    card.Button.transform.SetParent(grid, false);
                }

                card.Button.Descriptor.SetTitle(TitleFor(n));
                ApplyChip(card);
                ApplyIcon(card, IconFor(n));

                card.Visible = true;
                card.Button.gameObject.SetActive(true);
            }

            private static void ApplyChip(NotificationCard card)
            {
                BasisNotification n = card.Notification;
                card.ChipLabel.color = StatusTint(n.Status);
                card.ChipLabel.SetText(n.Status == BasisNotificationStatus.Pending
                    ? FormatTime(n.CreatedUtc)
                    : StatusLabel(n.Status));
            }

            private static void ApplyIcon(NotificationCard card, string address)
            {
                if (string.Equals(card.IconAddress, address, StringComparison.Ordinal)) return;
                card.IconAddress = address;

                if (string.IsNullOrEmpty(address))
                {
                    if (card.Icon != null) card.Icon.gameObject.SetActive(false);
                    return;
                }

                if (card.Icon == null)
                {
                    card.Icon = AddCardIcon(card.Button, address);
                    return;
                }

                card.Icon.SetIcon(AddressableAssets.GetSprite(address), true);
                card.Icon.gameObject.SetActive(true);
            }

            private static string BuildCardTooltip(NotificationCard card)
            {
                BasisNotification n = card.Notification;
                if (n == null) return string.Empty;

                string meta = CategoryLabel(n.Category) + " • " + FormatDateTime(n.ResolvedUtc ?? n.CreatedUtc) + " • " + StatusLabel(n.Status);
                return string.IsNullOrEmpty(n.Description) ? meta : Flatten(n.Description) + " • " + meta;
            }

            private void ReleaseCard(Guid id)
            {
                if (!_cards.TryGetValue(id, out NotificationCard card)) return;
                _cards.Remove(id);

                card.Notification = null;
                card.Visible = false;
                if (card.Button == null) return;

                if (_cardPool.Count < CardPoolCap)
                {
                    card.Button.gameObject.SetActive(false);
                    card.Button.transform.SetAsLastSibling();
                    _cardPool.Add(card);
                    return;
                }

                DestroyCard(card);
            }

            private static void DestroyCard(NotificationCard card)
            {
                if (card.Button == null) return;
                card.Button.OnClicked = null;
                card.Button.TooltipProvider = null;
                card.Button.ReleaseInstance();
                card.Button = null;
            }

            private void ClearAllCards()
            {
                foreach (var kvp in _cards)
                {
                    DestroyCard(kvp.Value);
                }
                _cards.Clear();

                for (int i = 0; i < _cardPool.Count; i++)
                {
                    DestroyCard(_cardPool[i]);
                }
                _cardPool.Clear();
            }

            private bool ApplySiblingOrder()
            {
                IReadOnlyList<BasisNotification> all = BasisNotificationCenter.All;

                int pendingIndex = 0, historyIndex = 0;
                bool inOrder = true;
                for (int i = all.Count - 1; i >= 0 && inOrder; i--)
                {
                    if (!_cards.TryGetValue(all[i].Id, out NotificationCard card) || card.Button == null) continue;
                    int expected = card.Grid == PendingGrid ? pendingIndex++ : historyIndex++;
                    if (card.Button.transform.GetSiblingIndex() != expected) inOrder = false;
                }
                if (inOrder) return false;

                pendingIndex = 0;
                historyIndex = 0;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    if (!_cards.TryGetValue(all[i].Id, out NotificationCard card) || card.Button == null) continue;
                    card.Button.transform.SetSiblingIndex(card.Grid == PendingGrid ? pendingIndex++ : historyIndex++);
                }
                return true;
            }

            private bool ApplyFilter()
            {
                bool changed = false;
                foreach (var kvp in _cards)
                {
                    NotificationCard card = kvp.Value;
                    if (card.Button == null || card.Notification == null) continue;

                    bool show = card.Status == BasisNotificationStatus.Pending
                        || BasisNotificationCenter.PassesHistoryFilter(card.Notification);
                    if (card.Visible == show) continue;

                    card.Visible = show;
                    card.Button.gameObject.SetActive(show);
                    changed = true;
                }
                return changed;
            }

            private void UpdateHeaders()
            {
                if (PendingHeader != null)
                {
                    PendingHeader.SetTitle(BasisLocalization.Get("notifications.pending.count", _pendingCount));
                    PendingHeader.SetDescription(BasisLocalization.Get(_pendingCount == 0
                        ? "notifications.empty.pending"
                        : "notifications.pending.description"));
                }

                if (HistoryHeader != null)
                {
                    bool filtered = BasisNotificationCenter.HistoryFiltered;
                    HistoryHeader.SetTitle(filtered
                        ? BasisLocalization.Get("notifications.history.filtered", _historyVisible, _historyCount)
                        : BasisLocalization.Get("notifications.history.count", _historyCount));

                    string description;
                    if (_historyCount == 0) description = BasisLocalization.Get("notifications.empty.history");
                    else if (_historyVisible == 0) description = BasisLocalization.Get("notifications.empty.filtered");
                    else description = BasisLocalization.Get("notifications.history.description");
                    HistoryHeader.SetDescription(description);
                }
            }

            // ---- Dialogs ----

            private BasisMenuPanel ResolvePanel()
            {
                if (Panel != null && !Panel.IsReleased) return Panel;
                return GetComponentInParent<BasisMenuPanel>();
            }

            private void CancelDialogs()
            {
                DialogBox<bool> search = _searchDialog, detail = _detailDialog, clear = _clearDialog;
                _searchDialog = null;
                _detailDialog = null;
                _clearDialog = null;
                search?.Cancel(false);
                detail?.Cancel(false);
                clear?.Cancel(false);
            }

            private static PanelButton AddExitButton(DialogBox<bool> dialog)
            {
                PanelButton exitButton = PanelButton.CreateNew(PanelButton.ButtonStyles.ExitButton, dialog.Descriptor.Header);
                exitButton.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 125);
                exitButton.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 50);
                exitButton.OnClicked += () => dialog.Cancel(false);
                return exitButton;
            }

            private static PanelButton ActionButton(PanelTabGroup row, string style, string labelKey)
            {
                PanelButton button = PanelButton.CreateNew(style, row.TabButtonParent);
                button.Descriptor.SetTitle(BasisLocalization.Get(labelKey));
                button.Descriptor.SetWidth(200);
                button.Descriptor.SetHeight(60);
                return button;
            }

            private void OnCardClicked(BasisNotification n)
            {
                if (n == null) return;
                _ = ShowDetailDialogAsync(n);
            }

            private async Task ShowDetailDialogAsync(BasisNotification n)
            {
                BasisMenuPanel panel = ResolvePanel();
                if (panel == null || _detailDialog != null) return;

                DialogBox<bool> dialog = DialogBox<bool>.Create(panel, new Vector2(1000, 560), TitleFor(n), null, IconFor(n));
                if (dialog.Descriptor == null) return;
                _detailDialog = dialog;
                dialog.Descriptor.DisableRichText();

                AddExitButton(dialog);

                PanelTabPage page = PanelTabPage.CreateVertical(dialog.Descriptor.ContentParent);
                page.Descriptor.SetHeight(460f);
                ClampScrollViewport(page.Descriptor.ContentParent);
                RectTransform content = page.Descriptor.ContentParent;

                PanelElementDescriptor meta = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Entry, content);
                meta.DisableRichText();
                meta.SetTitle(StatusLabel(n.Status));
                meta.SetDescription(CategoryLabel(n.Category) + "  •  " + FormatDateTime(n.ResolvedUtc ?? n.CreatedUtc));
                meta.SetTooltip(CategoryTooltip(n.Category));
                if (meta.TitleLabel != null) meta.TitleLabel.color = StatusTint(n.Status);

                if (!string.IsNullOrEmpty(n.Description))
                {
                    PanelElementDescriptor body = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Entry, content);
                    body.DisableRichText();
                    body.SetTitle(string.Empty);
                    body.SetDescription(n.Description);
                }

                PanelTabGroup actions = PanelTabGroup.CreateNew(content, LayoutDirection.HorizontalNoBackground);
                actions.Descriptor.SetHeight(60);
                if (n.Status == BasisNotificationStatus.Pending)
                {
                    if (n.Reopen != null)
                    {
                        ActionButton(actions, PanelButton.ButtonStyles.AcceptButton, "notifications.open").OnClicked += () =>
                        {
                            dialog.CloseWithResult(true);
                            BasisNotificationCenter.Reopen(n);
                        };
                    }
                    ActionButton(actions, PanelButton.ButtonStyles.CancelButton, "notifications.dismiss").OnClicked += () =>
                    {
                        dialog.CloseWithResult(false);
                        BasisNotificationCenter.Dismiss(n);
                    };
                }
                else
                {
                    ActionButton(actions, PanelButton.ButtonStyles.AcceptButton, "ui.ok").OnClicked += () => dialog.Cancel(false);
                }

                dialog.Descriptor.ForceRebuild();

                await dialog.WaitAsync();
                if (_detailDialog == dialog) _detailDialog = null;
            }

            private async Task ShowSearchDialogAsync()
            {
                BasisMenuPanel panel = ResolvePanel();
                if (panel == null || _searchDialog != null) return;

                DialogBox<bool> dialog = DialogBox<bool>.Create(panel, new Vector2(830, 420),
                    BasisLocalization.Get("ui.search.label"),
                    BasisLocalization.Get("notifications.filter.category.description"),
                    AddressableAssets.Sprites.Search);
                if (dialog.Descriptor == null) return;
                _searchDialog = dialog;

                AddExitButton(dialog);

                PanelTextField searchField = PanelTextField.CreateNewEntry(dialog.Descriptor.ContentParent);
                searchField.Descriptor.SetTitle(BasisLocalization.Get("notifications.filter.search"));
                if (searchField._placeholderLabel != null)
                {
                    searchField._placeholderLabel.text = BasisLocalization.Get("ui.search");
                }
                searchField.SetValueWithoutNotify(BasisNotificationCenter.HistorySearch);
                if (searchField._inputField != null)
                {
                    searchField._inputField.text = BasisNotificationCenter.HistorySearch;
                    searchField._inputField.Select();
                    searchField._inputField.ActivateInputField();
                }
                searchField.OnValueChanged += value =>
                {
                    BasisNotificationCenter.HistorySearch = value;
                    UpdateSearchVisual();
                };

                PanelDropdown categoryFilter = PanelDropdown.CreateNewEntry(dialog.Descriptor.ContentParent);
                categoryFilter.Descriptor.SetTitle(BasisLocalization.Get("notifications.filter.category"));
                categoryFilter.Descriptor.SetTooltip(BasisLocalization.Get("notifications.filter.category.description"));
                categoryFilter.AssignEntries(FilterEntries(), FilterLabels(), FilterTooltips());
                categoryFilter.SetValueWithoutNotify(EntryFor(BasisNotificationCenter.HistoryCategory));
                categoryFilter.OnValueChanged += value =>
                {
                    BasisNotificationCenter.HistoryCategory = CategoryFor(value);
                    UpdateSearchVisual();
                };

                dialog.Descriptor.ForceRebuild();

                await dialog.WaitAsync();
                if (_searchDialog == dialog) _searchDialog = null;
            }

            private async Task ShowClearHistoryDialogAsync()
            {
                BasisMenuPanel panel = ResolvePanel();
                if (panel == null || _clearDialog != null) return;

                DialogBox<bool> dialog = DialogBox<bool>.Create(panel, new Vector2(650, 220),
                    BasisLocalization.Get("notifications.clearHistory"),
                    BasisLocalization.Get("notifications.clearHistory.confirm"),
                    AddressableAssets.Sprites.Trash);
                if (dialog.Descriptor == null) return;
                _clearDialog = dialog;

                PanelTabGroup actions = PanelTabGroup.CreateNew(dialog.Descriptor.ContentParent, LayoutDirection.HorizontalNoBackground);
                actions.Descriptor.SetHeight(60);

                PanelButton no = PanelButton.CreateNew(PanelButton.ButtonStyles.CancelButton, actions.TabButtonParent);
                no.Descriptor.SetTitle(BasisLocalization.Get("ui.no"));
                no.Descriptor.SetWidth(200);
                no.Descriptor.SetHeight(60);
                no.OnClicked += () => dialog.CloseWithResult(false);

                PanelButton yes = PanelButton.CreateNew(PanelButton.ButtonStyles.AcceptButton, actions.TabButtonParent);
                yes.Descriptor.SetTitle(BasisLocalization.Get("ui.yes"));
                yes.Descriptor.SetWidth(200);
                yes.Descriptor.SetHeight(60);
                yes.OnClicked += () => dialog.CloseWithResult(true);

                dialog.Descriptor.ForceRebuild();

                bool confirmed = await dialog.WaitAsync();
                if (_clearDialog == dialog) _clearDialog = null;
                if (confirmed) BasisNotificationCenter.ClearHistory();
            }
        }
    }
}
