using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Search across the whole Settings menu. Every tab carries its own field, and typing in one
    /// filters that tab in place while listing what matched on all the others — so a setting can be
    /// found from wherever the user happens to be, not only from the page that owns it.
    ///
    /// <para>Everything expensive is kept off the keystroke path. Only the tab on screen is filtered;
    /// the rest just record the query and catch up when shown. Building the tabs the user never opened
    /// — and opening their lazy sections so their rows exist to be found — happens one page per frame,
    /// and only once typing has paused, so it never competes with the field for a frame.</para>
    /// </summary>
    public partial class SettingsProvider
    {
        /// <summary>Cap on cross-tab result rows. Past this the group says how many were left off.</summary>
        private const int MaxSearchResults = 40;

        /// <summary>
        /// Query length before the unopened tabs get built. Building a tab runs its whole setup —
        /// pollers start, lists fetch — so a single stray character should not trigger all of it.
        /// </summary>
        private const int MinIndexQueryLength = 2;

        /// <summary>
        /// Quiet time after the last keystroke before results are rebuilt and background indexing is
        /// allowed to run. Both are frame-expensive; neither should land while the user is still typing.
        /// </summary>
        private const float SearchIdleDelay = 0.2f;

        private sealed class TabSearch
        {
            public string Key;
            public int Index;
            public PanelTabPage Page;
            public BasisPanelSearch Search;
            public PanelElementDescriptor Results;
            public readonly List<PanelButton> ResultRows = new();
            public bool ResultsDirty = true;
            public bool Prepared;
        }

        private static readonly List<TabSearch> _tabSearches = new();
        private static readonly List<BasisPanelSearchHit> _hitBuffer = new();

        private static PanelTabGroup _searchTabGroup;
        private static string _searchQuery = string.Empty;
        private static float _lastSearchEdit;
        private static Coroutine _searchIndexRoutine;
        private static Coroutine _searchResultsRoutine;

        /// <summary>
        /// Drops all search state and binds to a freshly built tab group. Called when the panel is
        /// (re)opened and when it is released, so nothing points at a torn-down page.
        /// </summary>
        private static void ResetSearch(PanelTabGroup tabGroup)
        {
            StopSearchRoutines();
            _tabSearches.Clear();
            _lazyTabs.Clear();
            _hitBuffer.Clear();
            _searchQuery = string.Empty;
            _searchTabGroup = tabGroup;
        }

        private static void StopSearchRoutines()
        {
            if (_searchTabGroup != null)
            {
                if (_searchIndexRoutine != null) _searchTabGroup.StopCoroutine(_searchIndexRoutine);
                if (_searchResultsRoutine != null) _searchTabGroup.StopCoroutine(_searchResultsRoutine);
            }

            _searchIndexRoutine = null;
            _searchResultsRoutine = null;
        }

        /// <summary>
        /// Gives a freshly built tab its search field and the group that lists matches from elsewhere.
        /// A tab realized while a query is running takes the query but not the filtering — that waits
        /// until the tab is actually shown.
        /// </summary>
        private static void AttachTabSearch(string tabKey, int index, PanelTabPage page)
        {
            if (page == null || _searchTabGroup == null) return;

            RectTransform content = page.Descriptor.ContentParent;
            BasisPanelSearch search = BasisPanelSearch.Attach(content);
            if (search == null) return;

            TabSearch entry = new TabSearch
            {
                Key = tabKey,
                Index = index,
                Page = page,
                Search = search,
            };

            entry.Results = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);
            if (entry.Results != null)
            {
                entry.Results.SetTitle(BasisLocalization.Get("settings.search.otherTabs"));
                entry.Results.SetActive(false);
                entry.Results.transform.SetSiblingIndex(search.Field.transform.GetSiblingIndex() + 1);
                search.KeepVisible(entry.Results);
            }

            search.OnQueryChanged += query => OnSearchQueryChanged(entry, query);
            _tabSearches.Add(entry);

            if (_searchQuery.Length > 0) search.SetQueryDeferred(_searchQuery);
        }

        /// <summary>
        /// Carries a query typed on one tab to every other one. The others only record it — filtering a
        /// page nobody is looking at costs a layout pass for nothing, which is what made typing crawl.
        /// </summary>
        private static void OnSearchQueryChanged(TabSearch source, string query)
        {
            _searchQuery = query;
            _lastSearchEdit = Time.realtimeSinceStartup;

            for (int i = 0; i < _tabSearches.Count; i++)
            {
                TabSearch other = _tabSearches[i];
                if (other != source && other.Search != null) other.Search.SetQueryDeferred(query);
            }

            MarkResultsDirty();

            if (query.Length == 0)
            {
                StopSearchRoutines();
                for (int i = 0; i < _tabSearches.Count; i++)
                {
                    ClearResultRows(_tabSearches[i]);
                }
                return;
            }

            ScheduleIdleWork();
        }

        /// <summary>
        /// Re-applies the running query to a tab the user just switched to, and takes down result rows
        /// that were answering an older query. Nothing to do when no search is active.
        /// </summary>
        private static void OnTabShown(string tabKey)
        {
            TabSearch entry = FindTabSearch(tabKey);
            if (entry?.Search == null) return;

            entry.Search.ApplyPendingQuery();
            if (_searchQuery.Length == 0) return;

            if (entry.ResultsDirty) ClearResultRows(entry);
            ScheduleIdleWork();
        }

        private static TabSearch FindTabSearch(string tabKey)
        {
            for (int i = 0; i < _tabSearches.Count; i++)
            {
                if (_tabSearches[i].Key == tabKey) return _tabSearches[i];
            }
            return null;
        }

        private static TabSearch FindVisibleTabSearch()
        {
            if (_searchTabGroup == null) return null;

            for (int i = 0; i < _tabSearches.Count; i++)
            {
                if (_tabSearches[i].Index == _searchTabGroup.Value) return _tabSearches[i];
            }
            return null;
        }

        private static void MarkResultsDirty()
        {
            for (int i = 0; i < _tabSearches.Count; i++)
            {
                _tabSearches[i].ResultsDirty = true;
            }
        }

        // ------------------
        // WORK THAT WAITS FOR A PAUSE IN TYPING
        // ------------------

        private static void ScheduleIdleWork()
        {
            if (_searchTabGroup == null || !_searchTabGroup.isActiveAndEnabled) return;
            if (_searchResultsRoutine != null) return;

            _searchResultsRoutine = _searchTabGroup.StartCoroutine(RunIdleWork());
        }

        private static IEnumerator RunIdleWork()
        {
            while (WaitingOnTyping()) yield return null;

            _searchResultsRoutine = null;
            if (_searchQuery.Length == 0) yield break;

            StartSearchIndexing();

            // Only the page being looked at pays for result rows; the others rebuild when shown.
            TabSearch visible = FindVisibleTabSearch();
            if (visible != null) BuildResultRows(visible);
        }

        private static bool WaitingOnTyping() =>
            Time.realtimeSinceStartup - _lastSearchEdit < SearchIdleDelay;

        private static void StartSearchIndexing()
        {
            if (_searchIndexRoutine != null) return;
            if (_searchQuery.Length < MinIndexQueryLength) return;
            if (_searchTabGroup == null || !_searchTabGroup.isActiveAndEnabled) return;

            _searchIndexRoutine = _searchTabGroup.StartCoroutine(IndexRemainingTabs());
        }

        /// <summary>
        /// Makes the tabs the user never opened searchable — building the page, then opening the lazy
        /// sections whose rows only exist while expanded. Both instantiate prefabs, so it is one page
        /// per frame and it stands down whenever the user starts typing again.
        /// </summary>
        private static IEnumerator IndexRemainingTabs()
        {
            for (int i = 0; i < _lazyTabs.Count; i++)
            {
                if (_searchTabGroup == null || _searchQuery.Length == 0) break;
                if (_lazyTabs[i].Built) continue;

                while (WaitingOnTyping()) yield return null;

                RealizeTab(_searchTabGroup, _lazyTabs[i], forSearch: true);
                yield return null;
            }

            // _tabSearches grows while the loop above runs, so this walks it by index rather than
            // snapshotting a count that is already stale.
            for (int i = 0; i < _tabSearches.Count; i++)
            {
                if (_searchTabGroup == null || _searchQuery.Length == 0) break;

                TabSearch entry = _tabSearches[i];
                if (entry.Prepared || entry.Search == null) continue;

                while (WaitingOnTyping()) yield return null;

                entry.Prepared = true;
                entry.Search.Prepare();
                MarkResultsDirty();
                ScheduleIdleWork();
                yield return null;
            }

            _searchIndexRoutine = null;
        }

        // ------------------
        // CROSS-TAB RESULTS
        // ------------------

        private static void BuildResultRows(TabSearch host)
        {
            if (host.Results == null || !host.ResultsDirty) return;
            host.ResultsDirty = false;

            int shown = 0;
            int dropped = 0;

            for (int i = 0; i < _tabSearches.Count; i++)
            {
                TabSearch other = _tabSearches[i];
                if (other == host || other.Search == null) continue;

                other.Search.CollectHits(_searchQuery, _hitBuffer);
                for (int h = 0; h < _hitBuffer.Count; h++)
                {
                    if (shown >= MaxSearchResults)
                    {
                        dropped += _hitBuffer.Count - h;
                        break;
                    }

                    SetResultRow(host, shown, other, _hitBuffer[h]);
                    shown++;
                }
            }
            _hitBuffer.Clear();

            // Rows are reused rather than released and rebuilt — every one is an Addressable
            // instantiate, and a query grown one character at a time would pay for all of them again.
            ReleaseResultRowsFrom(host, shown);

            // Say what was left off rather than quietly truncating — a capped list that looks complete
            // is worse than a short one that admits it.
            host.Results.SetDescription(dropped > 0
                ? BasisLocalization.Get("settings.search.otherTabs.more", dropped)
                : string.Empty);

            host.Results.SetActive(shown > 0);
            host.Results.ForceRebuild();
        }

        private static void SetResultRow(TabSearch host, int index, TabSearch target, BasisPanelSearchHit hit)
        {
            PanelButton row;
            if (index < host.ResultRows.Count)
            {
                row = host.ResultRows[index];
            }
            else
            {
                row = PanelButton.CreateNew(host.Results.ContentParent);
                if (row == null) return;
                host.ResultRows.Add(row);
            }

            if (row == null) return;
            row.gameObject.SetActive(true);

            string tabName = BasisLocalization.Get(target.Key);
            string section = hit.SectionTitle;
            string title = hit.Title;

            row.Descriptor.SetTitle(title);
            row.Descriptor.SetDescription(string.IsNullOrEmpty(section) || section == title
                ? tabName
                : $"{tabName} › {section}");

            string targetKey = target.Key;
            PanelSectionToggle targetSection = hit.Section;
            row.OnClicked = () => OpenSearchResult(targetKey, targetSection, title);
        }

        private static void ReleaseResultRowsFrom(TabSearch host, int from)
        {
            for (int i = host.ResultRows.Count - 1; i >= from; i--)
            {
                PanelButton row = host.ResultRows[i];
                if (row != null && !row.IsReleased) row.ReleaseInstance();
                host.ResultRows.RemoveAt(i);
            }
        }

        /// <summary>
        /// Jumps to the tab a result lives on, dropping the search on the way. Landing on a page still
        /// filtered by a query the user is finished with hides everything around the setting they came
        /// for, so the query goes, the setting's section is opened, and the page scrolls to it.
        /// </summary>
        private static void OpenSearchResult(string tabKey, PanelSectionToggle section, string title)
        {
            ClearSearch();
            NavigateToTab(_searchTabGroup, tabKey);

            // After the clear, a lazy section is collapsed and its rows are gone again — expanding it
            // rebuilds them, so this has to come after the navigation rather than with the hit. That
            // also means the row is a new object, which is why ScrollTo finds it by title.
            if (section != null) section.SetExpanded(true);

            TabSearch entry = FindTabSearch(tabKey);
            entry?.Search?.ScrollTo(title);
        }

        /// <summary>Drops the query from every tab and takes the cross-tab result rows down with it.</summary>
        private static void ClearSearch()
        {
            StopSearchRoutines();
            _searchQuery = string.Empty;

            TabSearch visible = FindVisibleTabSearch();
            for (int i = 0; i < _tabSearches.Count; i++)
            {
                TabSearch entry = _tabSearches[i];
                entry.Search?.SetQueryDeferred(string.Empty);
                ClearResultRows(entry);
            }

            // Off-screen pages unfilter when they are next shown; the one on screen has to do it now.
            visible?.Search?.ApplyPendingQuery();
        }

        private static void ClearResultRows(TabSearch entry)
        {
            ReleaseResultRowsFrom(entry, 0);
            if (entry.Results != null) entry.Results.SetActive(false);
        }
    }
}
