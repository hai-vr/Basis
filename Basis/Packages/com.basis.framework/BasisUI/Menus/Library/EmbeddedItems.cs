

using System;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;
using static Basis.Scripts.UI.UI_Panels.BasisDataStoreItemKeys;

namespace Basis.BasisUI
{
    public class EmbeddedItems
    {
        /// <summary>
        /// This is where you can define embedded items 
        /// for the moment the addressable assets exist for:
        /// Personal Mirror
        /// Photo Camera
        /// 
        /// These are defined here for the menu only so we can spawn them locally within the LibraryProvider.cs
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

        /// <summary>
        /// returns the sprite of a item key that is embedded
        /// </summary>
        public static Sprite GetSpriteForEmbeddedItem(ItemKey item)
        {
            if(!item.IsEmbedded)
            {
                BasisDebug.LogError($"GetSpriteForEmbeddedItem() was invoked for item = {item.Url} it is not embedded. Returning NULL.");
                return null;
            }

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

        /// <summary>
        /// returns returns the bounds for the embedded item must be defined
        /// </summary>
        public static BasisBounds GetBoundsForEmbeddedItem(ItemKey item)
        {
            BasisBounds defaultBounds = new BasisBounds(Vector3.one, Vector3.zero);
            if(!item.IsEmbedded)
            {
                BasisDebug.LogError($"GetBoundsForEmbeddedItem() was invoked for item = {item.Url} it is not embedded. returning default BasisBounds({defaultBounds})");
                return defaultBounds;
            }

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

            return defaultBounds;
        }
        
        /// <summary>
        /// returns the offset for an embedded item when spawned with BundledContentHolder.PlacementType.SpawnInFrontOfPlayer
        /// </summary>
        internal static Vector3 GetOffsetForEmbeddedItem(ItemKey item, Vector3 playerPosReference, Vector3 playerPosForwardReference)
        {
            if(!item.IsEmbedded)
            {
                BasisDebug.LogError($"GetOffsetForEmbeddedItem() was invoked for item = {item.Url} it is not embedded. returning playerPosReference + playerPosForwardReference * 0.5f!");
                return playerPosReference + playerPosForwardReference * 0.5f;
            }

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