

using System;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;
using static Basis.Scripts.UI.UI_Panels.BasisDataStoreItemKeys;

namespace Basis.BasisUI
{
    public class EmbeddedItems
    {
        /// <summary>
        /// When enabled, this array will be used to populate the initial key store
        /// if no keys file exists on disk or when the store is empty. Edit this
        /// array to hard-define entries.
        /// </summary>
        public static bool UseHardcodedKeys = false;

        public static ItemKey[] HardcodedKeys = new ItemKey[]
        {
            // Example entry (uncomment and edit):
            new ItemKey { 
                Mode = BundledContentHolder.Mode.Prop, 
                Url = "Personal Mirror", 
                Pass = "", 
                IsEmbedded = true, 
                PlacementType = BundledContentHolder.PlacementType.SpawnInFrontOfPlayer 
            },
            new ItemKey { 
                Mode = BundledContentHolder.Mode.Prop, 
                Url = "Photo Camera", 
                Pass = "", 
                IsEmbedded = true, 
                PlacementType = BundledContentHolder.PlacementType.SpawnInFrontOfPlayer 
            },
        };

        public static Sprite GetSpriteForEmbeddedItem(ItemKey item)
        {
            switch(item.Url)
            {
                case "Photo Camera":
                    return AddressableAssets.GetSprite(AddressableAssets.Sprites.Camera);
                case "Personal Mirror":
                    return AddressableAssets.GetSprite(AddressableAssets.Sprites.Mirror);
                default:
                    BasisDebug.Log($"GetSpriteForEmbeddedItem() item = {item.Url} does not have a specified icon defined. please define it, here.");
                    break;
            }

            return null;
        }

        public static BasisBounds GetBoundsForEmbeddedItem(ItemKey item)
        {
            switch(item.Url)
            {
                case "Photo Camera":
                    return new BasisBounds(new Vector3(0.25f, 0.15f, 0.1f), Vector3.zero);
                case "Personal Mirror":
                    return new BasisBounds(new Vector3(0.5f, 0.75f, 0.1f), Vector3.zero);
                default:
                    BasisDebug.Log($"GetBoundsForEmbeddedItem() item = {item.Url} does not have specified bounds defined. please define it, here.");
                    break;
            }

            return new BasisBounds(Vector3.one, Vector3.zero);
        }

        internal static Vector3 GetOffsetForEmbeddedItem(ItemKey item, Vector3 playerPosReference, Vector3 playerPosForwardReference)
        {
            switch(item.Url)
            {
                case "Photo Camera":
                    return playerPosReference + playerPosForwardReference * 0.5f;
                case "Personal Mirror":
                    return playerPosReference + playerPosForwardReference * 0.5f;
                default:
                    BasisDebug.Log($"GetOffsetForEmbeddedItem() item = {item.Url} does not have specified offset defined. please define it, here.");
                    break;
            }

            return playerPosReference;
        }
    }
}