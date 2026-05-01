using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.UI.UI_Panels;
using System;
using System.Collections.Generic;
using UnityEngine;
using static SerializableBasis;

namespace Basis.BasisUI
{
    /// <summary>
    /// In-memory store of library entries pushed by the current server on connect.
    /// Items are session-scoped: cleared when the local player disconnects so they
    /// don't pollute the user's persisted library between server visits.
    /// </summary>
    public static class BasisServerProvidedItems
    {
        private static readonly List<BasisDataStoreItemKeys.ItemKey> _items = new();

        public static IReadOnlyList<BasisDataStoreItemKeys.ItemKey> Items => _items;

        /// <summary>Fired after items change so any open UI can refresh.</summary>
        public static event Action OnChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            // Clear when the local player leaves a server. Re-subscribe defensively
            // in case the hook ever runs more than once (domain reload, etc.).
            BasisNetworkPlayer.OnLocalPlayerLeft -= OnLocalLeft;
            BasisNetworkPlayer.OnLocalPlayerLeft += OnLocalLeft;
        }

        private static void OnLocalLeft(BasisNetworkPlayer _, Basis.Scripts.BasisSdk.Players.BasisLocalPlayer __)
        {
            Clear();
        }

        public static void SetFromServer(ServerLibraryItem[] serverItems)
        {
            _items.Clear();
            if (serverItems != null)
            {
                for (int i = 0; i < serverItems.Length; i++)
                {
                    var src = serverItems[i];
                    if (string.IsNullOrEmpty(src.Url)) continue;

                    _items.Add(new BasisDataStoreItemKeys.ItemKey
                    {
                        Mode = (BundledContentHolder.Mode)src.Mode,
                        PlacementType = BundledContentHolder.PlacementType.SpawnAtRaycast,
                        Url = src.Url,
                        Pass = src.Password ?? string.Empty,
                        EmbeddedSettings = BasisDataStoreItemKeys.EmbeddedSettings.Default,
                        PinnedSettings = BasisDataStoreItemKeys.PinnedSettings.Default,
                    });
                }
            }
            OnChanged?.Invoke();
        }

        public static void Clear()
        {
            if (_items.Count == 0) return;
            _items.Clear();
            OnChanged?.Invoke();
        }
    }
}
