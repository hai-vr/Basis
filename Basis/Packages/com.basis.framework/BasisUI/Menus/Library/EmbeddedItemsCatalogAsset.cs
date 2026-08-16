using System;
using System.Collections.Generic;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;
using static Basis.Scripts.UI.UI_Panels.BasisDataStoreItemKeys;

namespace Basis.BasisUI
{
    [Serializable]
    public class EmbeddedItemDefinition
    {
        public ItemKey Key = new ItemKey
        {
            EmbeddedSettings = EmbeddedSettings.BEEUrl,
            PinnedSettings = PinnedSettings.Default
        };

        public bool HasCustomIconAddress;
        public string IconAddress;

        /// <summary>
        /// What the library shows this item as. Without one the address is used, which is an asset
        /// name rather than a label and cannot be translated. A localization key rather than text,
        /// so the item reads in the language the rest of the panel is in.
        /// </summary>
        public bool HasCustomDisplayName;
        public string DisplayNameKey;

        public bool HasCustomBounds;
        public BasisBounds Bounds = new BasisBounds(Vector3.one, Vector3.zero);

        public bool HasCustomSpawnOffset;
        public Vector3 SpawnOffsetFromPlayerReference = new Vector3(0f, 0f, 0.5f);
    }

    [CreateAssetMenu(fileName = "EmbeddedItemsCatalog", menuName = "Basis/Embedded Items Catalog", order = 1)]
    public class EmbeddedItemsCatalogAsset : ScriptableObject
    {
        public List<EmbeddedItemDefinition> Entries = new List<EmbeddedItemDefinition>();
    }
}
