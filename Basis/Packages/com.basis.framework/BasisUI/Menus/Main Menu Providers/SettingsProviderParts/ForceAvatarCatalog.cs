using Basis.Scripts.UI.UI_Panels;
using System.Collections.Generic;

namespace Basis.BasisUI
{
    /// <summary>
    /// The avatars a moderator can push onto another player: everything this server handed out on
    /// connect, followed by the moderator's own saved avatars. Shared by the Moderator tab and the
    /// per-player admin panel so both offer the same list.
    /// </summary>
    public static class ForceAvatarCatalog
    {
        public readonly struct Entry
        {
            public readonly BasisDataStoreItemKeys.ItemKey Item;
            public readonly string Label;
            public readonly bool ServerProvided;

            public Entry(BasisDataStoreItemKeys.ItemKey item, string label, bool serverProvided)
            {
                Item = item;
                Label = label;
                ServerProvided = serverProvided;
            }
        }

        /// <summary>
        /// Server-provided avatars first — those are the curated ones every player on this server
        /// already has, so they are the safe default sitting at the top of the dropdown. A url the
        /// server also hands out is listed once, under its server label.
        /// </summary>
        public static List<Entry> Build()
        {
            List<Entry> entries = new List<Entry>();
            HashSet<string> seen = new HashSet<string>(System.StringComparer.Ordinal);

            IReadOnlyList<BasisDataStoreItemKeys.ItemKey> serverItems = BasisServerProvidedItems.Items;
            for (int i = 0; i < serverItems.Count; i++)
            {
                TryAdd(entries, seen, serverItems[i], true);
            }

            BasisDataStoreItemKeys.ItemKey[] localItems = BasisDataStoreItemKeys.DisplayKeys();
            if (localItems != null)
            {
                for (int i = 0; i < localItems.Length; i++)
                {
                    TryAdd(entries, seen, localItems[i], false);
                }
            }

            return entries;
        }

        private static void TryAdd(List<Entry> entries, HashSet<string> seen, BasisDataStoreItemKeys.ItemKey item, bool serverProvided)
        {
            if (item == null || item.Mode != BundledContentHolder.Mode.Avatar) return;
            if (string.IsNullOrWhiteSpace(item.Url) || !seen.Add(item.Url)) return;

            string name = CachedMetaData.TryGetMeta(item.Url, out CachedMetaData.CachedContent meta) && !string.IsNullOrWhiteSpace(meta.Name)
                ? meta.Name
                : item.Url;

            entries.Add(new Entry(
                item,
                serverProvided ? BasisLocalization.Get("settings.admin.forceAvatar.serverEntry", name) : name,
                serverProvided));
        }

        /// <summary>
        /// Repopulates <paramref name="dropdown"/> from <paramref name="entries"/>, keeping the
        /// current pick if that avatar is still listed. The dropdown's value is the url, which is
        /// what <see cref="TryResolve"/> maps back to the entry.
        /// </summary>
        public static void Apply(PanelDropdown dropdown, List<Entry> entries)
        {
            if (dropdown == null) return;

            List<string> urls = new List<string>(entries.Count);
            List<string> labels = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                urls.Add(entries[i].Item.Url);
                labels.Add(entries[i].Label);
            }

            string previous = dropdown.Value;
            dropdown.AssignEntries(urls, labels);
            dropdown.SetValueWithoutNotify(
                !string.IsNullOrEmpty(previous) && urls.Contains(previous) ? previous
                : urls.Count > 0 ? urls[0]
                : string.Empty);
        }

        public static bool TryResolve(List<Entry> entries, string url, out Entry entry)
        {
            if (entries != null && !string.IsNullOrEmpty(url))
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].Item.Url, url, System.StringComparison.Ordinal))
                    {
                        entry = entries[i];
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }
    }
}
