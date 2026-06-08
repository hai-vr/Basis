using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI
{
    /// <summary>
    /// Lightweight vertical virtualized list. Attach to a ScrollRect (or let the
    /// static helper do it for you) and supply a row factory plus a bind delegate.
    /// Only enough rows to cover the viewport (+ a small buffer) are ever created,
    /// and scrolling rebinds existing rows to new data indices instead of
    /// instantiating new ones. Because only the visible window exists, the active
    /// RectMask2D culls a handful of graphics per frame instead of all N rows
    /// (ClipperRegistry.Cull), and batch rebuilds stay bounded.
    ///
    /// Rows may be uniform height (default, O(1) layout) or variable height: pass a
    /// per-index height function to the SetDataSource overload. Heights must be known
    /// up front (fixed per item, or measured once and cached) — a cumulative-offset
    /// table is built so the first visible row is found by binary search and the
    /// scrollbar maps exactly.
    ///
    /// Typical usage:
    ///   var descriptor = PanelElementDescriptor.CreateNew(ScrollViewVertical, parent);
    ///   var list = PanelVirtualScrollList.AttachTo(descriptor, rowHeight: 90f);
    ///   list.SetDataSource(
    ///       count: items.Count,
    ///       createRow: content => PanelButton.CreateNew(content).transform as RectTransform,
    ///       bindRow:   (row, i) => row.GetComponent&lt;PanelButton&gt;().Descriptor.SetTitle(items[i]));
    /// </summary>
    public class PanelVirtualScrollList : MonoBehaviour
    {
        [SerializeField] private float _rowHeight = 90f;
        [SerializeField] private float _spacing = 8f;
        [SerializeField] private int _rowBuffer = 2;

        private ScrollRect _scroll;
        private RectTransform _content;
        private RectTransform _viewport;

        private int _totalCount;
        private int _topRow = int.MinValue;
        private int _activeCount;
        private bool _dataBound;

        private Func<RectTransform, RectTransform> _createRow;
        private Action<RectTransform, int> _bindRow;
        private Func<int, float> _rowHeightFn;

        // Cumulative top offset of each row: _offsets[i] is row i's top in content
        // space, _offsets[_totalCount] is the total content height. Rebuilt whenever
        // the data source changes. For uniform rows this is just i * stride, but
        // keeping one table means a single code path handles both cases.
        private float[] _offsets = Array.Empty<float>();

        // Optional fixed block of non-recycled content pinned above the rows (e.g. a
        // panel's search/sort controls that live in the same scroll content). Rows are
        // sized and offset to leave room for it; it scrolls with the content.
        private float _headerHeight;

        private readonly List<RectTransform> _rowPool = new List<RectTransform>();

        public ScrollRect Scroll => _scroll;
        public int Count => _totalCount;
        public float RowStride => _rowHeight + _spacing;

        /// <summary>
        /// Height reserved above the recycled rows for fixed content sharing the scroll
        /// content. Set after AttachTo; the content is resized and rows shift down to
        /// leave room. Zero (default) means rows start at the top.
        /// </summary>
        public float HeaderHeight
        {
            get => _headerHeight;
            set
            {
                float h = value > 0f ? value : 0f;
                if (_headerHeight == h) return;
                _headerHeight = h;
                if (_dataBound)
                {
                    ApplyContentSize();
                    UpdateVisibleRows(true);
                }
            }
        }

        /// <summary>
        /// Attach (or retrieve an existing) PanelVirtualScrollList to the GameObject
        /// that hosts a ScrollRect. Walks up from <paramref name="anyChild"/> until
        /// a ScrollRect is found.
        /// </summary>
        public static PanelVirtualScrollList AttachTo(RectTransform anyChild, float rowHeight, float spacing = 8f, int rowBuffer = 2)
        {
            ScrollRect scroll = anyChild.GetComponentInParent<ScrollRect>();
            if (scroll == null)
            {
                BasisDebug.LogError("PanelVirtualScrollList.AttachTo: no ScrollRect found in parents of " + anyChild.name);
                return null;
            }
            return AttachTo(scroll, rowHeight, spacing, rowBuffer);
        }

        public static PanelVirtualScrollList AttachTo(PanelElementDescriptor scrollDescriptor, float rowHeight, float spacing = 8f, int rowBuffer = 2)
        {
            return AttachTo(scrollDescriptor.ContentParent, rowHeight, spacing, rowBuffer);
        }

        public static PanelVirtualScrollList AttachTo(ScrollRect scroll, float rowHeight, float spacing = 8f, int rowBuffer = 2)
        {
            if (!scroll.TryGetComponent(out PanelVirtualScrollList list))
            {
                list = scroll.gameObject.AddComponent<PanelVirtualScrollList>();
            }
            list._scroll = scroll;
            list._content = scroll.content;
            list._viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            list._rowHeight = rowHeight;
            list._spacing = spacing;
            list._rowBuffer = rowBuffer;

            // Virtual scrolling drives anchoredPosition directly — disable any
            // layout group or ContentSizeFitter on the content so they don't
            // fight us by reflowing children.
            if (list._content != null)
            {
                if (list._content.TryGetComponent(out LayoutGroup group)) group.enabled = false;
                if (list._content.TryGetComponent(out ContentSizeFitter fitter)) fitter.enabled = false;

                // Anchor content to the top so row y = -offset is correct.
                list._content.anchorMin = new Vector2(0f, 1f);
                list._content.anchorMax = new Vector2(1f, 1f);
                list._content.pivot = new Vector2(0.5f, 1f);
                list._content.anchoredPosition = Vector2.zero;
            }

            list.HookScroll();
            return list;
        }

        private void Awake()
        {
            if (_scroll == null) TryGetComponent(out _scroll);
            if (_scroll != null)
            {
                if (_content == null) _content = _scroll.content;
                if (_viewport == null) _viewport = _scroll.viewport != null ? _scroll.viewport : (RectTransform)_scroll.transform;
                HookScroll();
            }
        }

        private void OnEnable()
        {
            if (_dataBound) UpdateVisibleRows(true);
        }

        private bool _scrollHooked;
        private void HookScroll()
        {
            if (_scrollHooked || _scroll == null) return;
            _scroll.onValueChanged.AddListener(OnScroll);
            _scrollHooked = true;
        }

        private void OnScroll(Vector2 _)
        {
            UpdateVisibleRows(false);
        }

        /// <summary>
        /// Set or replace the data source with uniform row heights. Creates the pool
        /// if needed, sizes the content, and refreshes visible rows.
        /// </summary>
        public void SetDataSource(int count, Func<RectTransform, RectTransform> createRow, Action<RectTransform, int> bindRow)
            => SetDataSource(count, createRow, bindRow, null);

        /// <summary>
        /// Variable-height data source. <paramref name="rowHeight"/> returns the pixel
        /// height of row i and must be stable for a given data set (fixed per item, or
        /// measured once and cached). Pass null to fall back to the uniform height set
        /// at AttachTo.
        /// </summary>
        public void SetDataSource(int count, Func<RectTransform, RectTransform> createRow, Action<RectTransform, int> bindRow, Func<int, float> rowHeight)
        {
            _createRow = createRow;
            _bindRow = bindRow;
            _rowHeightFn = rowHeight;
            _totalCount = Mathf.Max(0, count);
            _dataBound = true;

            RebuildOffsets();
            ApplyContentSize();

            _topRow = int.MinValue; // force rebind
            _activeCount = 0;
            UpdateVisibleRows(true);
        }

        private void ApplyContentSize()
        {
            if (_content == null) return;
            Vector2 size = _content.sizeDelta;
            size.y = (_totalCount > 0 ? _offsets[_totalCount] : 0f) + _headerHeight;
            _content.sizeDelta = size;
        }

        private void RebuildOffsets()
        {
            if (_offsets.Length < _totalCount + 1)
            {
                _offsets = new float[_totalCount + 1];
            }

            float y = 0f;
            for (int i = 0; i < _totalCount; i++)
            {
                _offsets[i] = y;
                y += RowHeightAt(i);
                if (i < _totalCount - 1) y += _spacing;
            }
            _offsets[_totalCount] = y;
        }

        private float RowHeightAt(int i)
        {
            float h = _rowHeightFn != null ? _rowHeightFn(i) : _rowHeight;
            return h > 0f ? h : 0f;
        }

        /// <summary>
        /// Update the data count without changing factories or the height function.
        /// Cheaper to call than re-passing the delegates when only the count changed.
        /// </summary>
        public void Refresh(int newCount)
        {
            if (_createRow == null || _bindRow == null)
            {
                BasisDebug.LogError("PanelVirtualScrollList.Refresh: no data source configured — call SetDataSource first.");
                return;
            }
            SetDataSource(newCount, _createRow, _bindRow, _rowHeightFn);
        }

        /// <summary>
        /// Rebind every currently-visible row against its data index. Use when the
        /// underlying data changed but neither the count nor the ordering did
        /// (e.g. a periodic text refresh of the on-screen rows).
        /// </summary>
        public void RebindVisible()
        {
            if (!_dataBound || _bindRow == null) return;
            for (int j = 0; j < _activeCount && j < _rowPool.Count; j++)
            {
                RectTransform row = _rowPool[j];
                if (row == null || !row.gameObject.activeSelf) continue;
                int dataIndex = _topRow + j;
                if (dataIndex < 0 || dataIndex >= _totalCount) continue;
                _bindRow(row, dataIndex);
            }
        }

        public void ClearRows()
        {
            for (int i = 0; i < _rowPool.Count; i++)
            {
                if (_rowPool[i] != null) Destroy(_rowPool[i].gameObject);
            }
            _rowPool.Clear();
            _topRow = int.MinValue;
            _activeCount = 0;
            _totalCount = 0;
            _dataBound = false;
            ApplyContentSize();
        }

        // Largest index i in [0, _totalCount-1] with _offsets[i] <= y (y >= 0).
        // _offsets is strictly increasing in i (plus spacing), so binary search.
        private int IndexAtOffset(float y)
        {
            int lo = 0, hi = _totalCount - 1, result = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (_offsets[mid] <= y)
                {
                    result = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return result;
        }

        private void UpdateVisibleRows(bool force)
        {
            if (!_dataBound || _createRow == null || _bindRow == null) return;
            if (_content == null || _viewport == null) return;

            if (_totalCount <= 0)
            {
                for (int i = 0; i < _rowPool.Count; i++)
                {
                    if (_rowPool[i] != null) _rowPool[i].gameObject.SetActive(false);
                }
                _topRow = int.MinValue;
                _activeCount = 0;
                return;
            }

            float viewportHeight = _viewport.rect.height;
            if (viewportHeight <= 0f) viewportHeight = RowStride * 4f; // pre-layout fallback

            float scrollY = Mathf.Max(0f, _content.anchoredPosition.y);

            // Translate the viewport into row space — the header occupies [0, _headerHeight)
            // of the content, so row 0's top is at _headerHeight.
            float rowTop = scrollY - _headerHeight;
            float rowBottom = scrollY + viewportHeight - _headerHeight;

            int first = IndexAtOffset(Mathf.Max(0f, rowTop)) - _rowBuffer;
            first = Mathf.Clamp(first, 0, _totalCount - 1);

            int last = IndexAtOffset(Mathf.Max(0f, rowBottom)) + _rowBuffer;
            last = Mathf.Clamp(last, first, _totalCount - 1);

            int needed = last - first + 1;

            bool poolChanged = false;
            while (_rowPool.Count < needed)
            {
                RectTransform row = _createRow(_content);
                if (row == null) break;
                row.SetParent(_content, false);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                _rowPool.Add(row);
                poolChanged = true;
            }

            if (!force && !poolChanged && first == _topRow && needed == _activeCount) return;

            _topRow = first;
            _activeCount = needed;

            for (int j = 0; j < _rowPool.Count; j++)
            {
                RectTransform row = _rowPool[j];
                if (row == null) continue;

                int dataIndex = first + j;
                if (j >= needed || dataIndex >= _totalCount)
                {
                    if (row.gameObject.activeSelf) row.gameObject.SetActive(false);
                    continue;
                }

                if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);

                // Pin the row to its slot: full width (sizeDelta.x = 0 against stretched
                // anchors) and its own height, top-aligned at the cumulative offset.
                Vector2 size = row.sizeDelta;
                size.x = 0f;
                size.y = RowHeightAt(dataIndex);
                row.sizeDelta = size;

                Vector2 anchored = row.anchoredPosition;
                anchored.x = 0f;
                anchored.y = -(_headerHeight + _offsets[dataIndex]);
                row.anchoredPosition = anchored;

                _bindRow(row, dataIndex);
            }
        }
    }
}
