using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Basis.Editor
{
    /// <summary>
    /// Reports the dependency graph behind the current Addressable entries so groups
    /// can be planned without duplicating shared assets. Any dependency that is not
    /// itself addressable is copied into every bundle that references it; this flags
    /// the ones spanning multiple groups (the duplication hotspots) and writes a
    /// shareable report to the project root. Sizes are best-effort: assets in
    /// immutable packages (Library/PackageCache) may show 0.
    /// </summary>
    public static class BasisAddressableDependencyReport
    {
        [MenuItem("Basis/Build/Addressables/Dependency Report", false, 342)]
        public static void Generate()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[BasisAddressables] AddressableAssetSettings not found.");
                return;
            }

            List<(string path, string group)> entries = new List<(string, string)>();
            HashSet<string> addressablePaths = new HashSet<string>();
            foreach (AddressableAssetGroup g in settings.groups)
            {
                if (g == null)
                {
                    continue;
                }
                foreach (AddressableAssetEntry e in g.entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.AssetPath) || AssetDatabase.IsValidFolder(e.AssetPath))
                    {
                        continue;
                    }
                    addressablePaths.Add(e.AssetPath);
                    entries.Add((e.AssetPath, g.Name));
                }
            }

            Dictionary<string, HashSet<string>> depToGroups = new Dictionary<string, HashSet<string>>();
            Dictionary<string, int> depRefCount = new Dictionary<string, int>();
            Dictionary<string, int> groupEntryCount = new Dictionary<string, int>();
            Dictionary<string, HashSet<string>> groupImplicitDeps = new Dictionary<string, HashSet<string>>();

            foreach ((string path, string group) in entries)
            {
                groupEntryCount[group] = groupEntryCount.TryGetValue(group, out int gc) ? gc + 1 : 1;
                if (!groupImplicitDeps.TryGetValue(group, out HashSet<string> gdeps))
                {
                    gdeps = new HashSet<string>();
                    groupImplicitDeps[group] = gdeps;
                }

                string[] deps = AssetDatabase.GetDependencies(path, true);
                for (int i = 0; i < deps.Length; i++)
                {
                    string dep = deps[i];
                    if (dep == path || addressablePaths.Contains(dep))
                    {
                        continue;
                    }
                    if (dep.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || dep.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    gdeps.Add(dep);
                    if (!depToGroups.TryGetValue(dep, out HashSet<string> set))
                    {
                        set = new HashSet<string>();
                        depToGroups[dep] = set;
                    }
                    set.Add(group);
                    depRefCount[dep] = depRefCount.TryGetValue(dep, out int c) ? c + 1 : 1;
                }
            }

            List<KeyValuePair<string, HashSet<string>>> hotspots = depToGroups
                .Where(kv => kv.Value.Count >= 2)
                .OrderByDescending(kv => kv.Value.Count)
                .ThenByDescending(kv => depRefCount[kv.Key])
                .ToList();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Basis Addressables Dependency Report  {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Addressable entries: {entries.Count}");
            sb.AppendLine();

            sb.AppendLine("== Per-group footprint (implicit, non-addressable deps pulled into the bundle) ==");
            foreach (KeyValuePair<string, int> g in groupEntryCount.OrderByDescending(k => k.Value))
            {
                HashSet<string> gdeps = groupImplicitDeps[g.Key];
                long bytes = gdeps.Sum(FileSize);
                sb.AppendLine($"  {g.Key}: {g.Value} entries, {gdeps.Count} implicit deps, {Human(bytes)}");
            }
            sb.AppendLine();

            sb.AppendLine("== Duplication hotspots: implicit deps referenced from MULTIPLE groups ==");
            sb.AppendLine("   (each is copied into every listed group's bundle)");
            long wasted = 0;
            foreach (KeyValuePair<string, HashSet<string>> kv in hotspots)
            {
                long size = FileSize(kv.Key);
                wasted += size * (kv.Value.Count - 1);
                sb.AppendLine($"  {kv.Key}");
                sb.AppendLine($"      {Human(size)} | {depRefCount[kv.Key]} refs | groups: {string.Join(", ", kv.Value.OrderBy(x => x))}");
            }
            sb.AppendLine();
            sb.AppendLine($"Cross-group shared deps: {hotspots.Count}");
            sb.AppendLine($"Estimated duplicated bytes (extra copies): {Human(wasted)}");

            string outPath = Path.Combine(Directory.GetCurrentDirectory(), "BasisAddressableDependencyReport.txt");
            File.WriteAllText(outPath, sb.ToString());

            Debug.Log($"[BasisAddressables] {hotspots.Count} cross-group shared deps, ~{Human(wasted)} duplicated. Report: {outPath}");
        }

        private static long FileSize(string assetPath)
        {
            try
            {
                FileInfo fi = new FileInfo(assetPath);
                return fi.Exists ? fi.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string Human(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1)
            {
                v /= 1024;
                u++;
            }
            return v.ToString("0.#", CultureInfo.InvariantCulture) + units[u];
        }
    }
}