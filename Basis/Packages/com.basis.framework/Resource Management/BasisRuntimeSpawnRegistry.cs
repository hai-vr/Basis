// ============================================================================
// BasisRuntimeSpawnRegistry.cs
// Put this anywhere in your project (e.g. Basis/Scripts/Runtime/BasisRuntimeSpawnRegistry.cs)
// ============================================================================

using Basis.Scripts.BasisSdk;
using System.Collections.Generic;
using UnityEngine;
using static SerializableBasis;

namespace Basis
{
    /// <summary>
    /// Remembers what the UI/runtime has spawned so we can unload it later.
    /// Keyed by URL (RemoteBeeFileLocation). If you want multiple instances per URL,
    /// change the value to a List<LocalLoadResource>.
    /// </summary>
    public static class BasisRuntimeSpawnRegistry
    {
        private static readonly Dictionary<string, LocalLoadResource> LoadedByUrl = new();

        public static bool TryGet(string url, out LocalLoadResource res)
            => LoadedByUrl.TryGetValue(url, out res);

        public static void Set(string url, LocalLoadResource res)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            LoadedByUrl[url] = res;
        }

        public static void Remove(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            LoadedByUrl.Remove(url);
        }

        public static bool IsLoaded(string url)
        {
            return TryGet(url, out var res) && !string.IsNullOrEmpty(res.LoadedNetID);
        }

        public static void ClearAll()
        {
            LoadedByUrl.Clear();
        }
    }
}
