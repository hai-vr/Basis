using System;
using System.Collections;
using System.Collections.Generic;

namespace Basis.BasisUI
{
    public partial class SettingsProvider
    {
        private const float SharedSettingViewFraction = 0.25f;
        private const string SettingPathSeparator = " › ";

        private static readonly List<PanelSectionToggle> _sharedSettingChain = new();

        public static bool TryGetSettingLocation(PanelComponent control, out string tabKey, out string path)
        {
            tabKey = null;
            path = null;
            if (control == null) return false;

            for (int i = 0; i < _tabSearches.Count; i++)
            {
                TabSearch tab = _tabSearches[i];
                if (tab.Page == null || !control.transform.IsChildOf(tab.Page.transform)) continue;

                tabKey = tab.Key;
                path = DescribeSettingPath(tab.Key, control);
                return true;
            }
            return false;
        }

        public static string DescribeSettingPath(string tabKey, PanelComponent control)
        {
            List<string> parts = new List<string> { BasisLocalization.Get(tabKey) };
            PanelSectionToggle.GetSectionChain(PanelSectionToggle.GetOwningSection(control.transform), _sharedSettingChain);
            for (int i = 0; i < _sharedSettingChain.Count; i++)
            {
                PanelSectionToggle section = _sharedSettingChain[i];
                string title = section != null && section.Descriptor != null ? section.Descriptor.Title : null;
                if (!string.IsNullOrEmpty(title)) parts.Add(title);
            }
            _sharedSettingChain.Clear();
            return string.Join(SettingPathSeparator, parts);
        }

        public static void RevealSetting(string tabKey, Predicate<PanelComponent> match, Action<PanelComponent, string> onShown, Action onMissing)
        {
            _pendingTabKey = tabKey;
            BasisMainMenu.OpenWithProvider(StaticTitle);
            _pendingTabKey = null;

            TabSearch tab = FindTabSearch(tabKey);
            bool onTab = tab != null && tab.Page != null && _searchTabGroup != null && _searchTabGroup.Value == tab.Index;
            PanelComponent control = onTab ? BasisPanelSearch.RevealControl(tab.Page.Descriptor.ContentParent, match) : null;
            if (control == null)
            {
                onMissing?.Invoke();
                return;
            }
            tab.Page.StartCoroutine(ShowRevealedSetting(tab.Page, control, DescribeSettingPath(tabKey, control), onShown));
        }

        private static IEnumerator ShowRevealedSetting(PanelTabPage page, PanelComponent control, string path, Action<PanelComponent, string> onShown)
        {
            yield return null;
            yield return null;
            if (page == null || control == null) yield break;

            BasisPanelSearch.ScrollToRow(page.Descriptor.ContentParent, control.rectTransform, SharedSettingViewFraction);
            onShown?.Invoke(control, path);
        }
    }
}
