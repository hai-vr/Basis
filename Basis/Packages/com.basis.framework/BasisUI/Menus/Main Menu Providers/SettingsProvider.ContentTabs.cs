using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.BasisUI
{
    public partial class SettingsProvider
    {
        private sealed class ContentTab
        {
            public string Key;
            public string Label;
            public bool Visible = true;
            public Func<PanelTabGroup, PanelTabPage> Builder;
        }

        private static readonly List<ContentTab> _contentTabs = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearContentTabs() => _contentTabs.Clear();

        public static bool HasContentTab(string key) => FindContentTab(key) != null;

        public static bool AddContentTab(string key, string label, Func<PanelTabGroup, PanelTabPage> builder)
        {
            if (string.IsNullOrEmpty(key) || builder == null || FindContentTab(key) != null)
            {
                return false;
            }

            ContentTab tab = new ContentTab { Key = key, Label = label ?? string.Empty, Builder = builder };
            _contentTabs.Add(tab);
            if (_searchTabGroup != null)
            {
                AddContentLazyTab(_searchTabGroup, tab);
            }
            return true;
        }

        public static void RemoveContentTab(string key)
        {
            ContentTab tab = FindContentTab(key);
            if (tab == null)
            {
                return;
            }

            _contentTabs.Remove(tab);
            if (_searchTabGroup != null)
            {
                SetTabVisible(_searchTabGroup, key, false);
            }
            _tabKeyToIndex.Remove(key);
            _lazyTabs.RemoveAll(lazy => lazy.Key == key);
            _tabSearches.RemoveAll(search => search.Key == key);
        }

        public static void RenameContentTab(string key, string label)
        {
            ContentTab tab = FindContentTab(key);
            if (tab == null)
            {
                return;
            }

            tab.Label = label ?? string.Empty;
            PanelButton button = ContentTabButton(key);
            if (button != null)
            {
                button.Descriptor.SetTitle(tab.Label);
            }
        }

        public static void SetContentTabVisible(string key, bool visible)
        {
            ContentTab tab = FindContentTab(key);
            if (tab == null || tab.Visible == visible)
            {
                return;
            }

            tab.Visible = visible;
            if (_searchTabGroup != null)
            {
                SetTabVisible(_searchTabGroup, key, visible);
            }
        }

        public static bool IsContentTabShown(string key)
        {
            return FindContentTab(key) != null &&
                   _searchTabGroup != null &&
                   BasisMainMenu.ActiveMenuTitle == StaticTitle &&
                   _tabKeyToIndex.TryGetValue(key, out int index) &&
                   _searchTabGroup.Value == index;
        }

        private static void AddContentTabs(PanelTabGroup tabGroup)
        {
            for (int i = 0; i < _contentTabs.Count; i++)
            {
                AddContentLazyTab(tabGroup, _contentTabs[i]);
            }
        }

        private static void AddContentLazyTab(PanelTabGroup tabGroup, ContentTab tab)
        {
            AddLazyTab(tabGroup, tab.Key, tab.Label, () =>
            {
                try
                {
                    return tab.Builder(tabGroup);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
                }
            });

            PanelButton button = ContentTabButton(tab.Key);
            if (button == null)
            {
                return;
            }

            button.Descriptor.DisableRichText();
            if (!tab.Visible)
            {
                button.gameObject.SetActive(false);
            }
        }

        private static PanelButton ContentTabButton(string key)
        {
            if (_searchTabGroup == null || !_tabKeyToIndex.TryGetValue(key, out int index) ||
                index < 0 || index >= _searchTabGroup.SelectionButtons.Count)
            {
                return null;
            }

            PanelButton button = _searchTabGroup.SelectionButtons[index];
            return button != null ? button : null;
        }

        private static string TabLabel(string key)
        {
            ContentTab tab = FindContentTab(key);
            return tab != null ? tab.Label : BasisLocalization.Get(key);
        }

        private static ContentTab FindContentTab(string key)
        {
            for (int i = 0; i < _contentTabs.Count; i++)
            {
                if (_contentTabs[i].Key == key)
                {
                    return _contentTabs[i];
                }
            }
            return null;
        }
    }
}
