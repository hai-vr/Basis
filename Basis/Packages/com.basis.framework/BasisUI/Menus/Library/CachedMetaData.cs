using Basis.Scripts.UI.UI_Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using static Basis.BasisUI.LibraryProvider;

namespace Basis.BasisUI
{
    /// <summary>
    /// this class handles cached metadata for items in the library, such as the name, thumbnail, and other info that can be retrieved from the BEE file without fully loading the content. 
    /// This allows for faster filtering and sorting in the library UI without needing to load each item first.
    /// </summary>
    public static class CachedMetaData
    {
        // Represents a cached metadata entry for an item
        public class CachedContent
        {
            public string Name;
            public DateTime? Created;

            public string AssetBundleDescription;
            public string ImageBase64;
            public Sprite CachedSprite;
            public string DateOfCreation;
            public string UniqueVersion;

            public BasisLoadableBundle BasisLoadableBundle;
            public BasisBundleConnector BasisBundleConnector;
        }

        private static readonly Dictionary<string, CachedContent> _metaCache = new();

        public static bool TryGetMeta(string url, out CachedContent meta)
        {
            return _metaCache.TryGetValue(url ?? string.Empty, out meta);
        }

        public static void SetMetaData(string url, CachedContent meta)
        {
            if (string.IsNullOrEmpty(url) || meta == null) return;
            _metaCache[url] = meta;
        }

        public static bool ContainsMetaData(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return _metaCache.ContainsKey(url);
        }

        public static void ClearMetaDataCache()
        {
            _metaCache.Clear();
        }

        public static Sprite CreateSpriteFromMetaData(CachedContent meta)
        {
            if (meta == null) return null;

            if (meta.CachedSprite != null)
                return meta.CachedSprite;

            if (string.IsNullOrEmpty(meta.ImageBase64))
                return null;

            var tex = BasisTextureCompression.FromPngBytes(meta.ImageBase64);
            if (tex == null)
                return null;

            meta.CachedSprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f)
            );

            return meta.CachedSprite;
        }

        public static async Task PreloadMetaDataForItem(BasisDataStoreItemKeys.ItemKey item)
        {
            if (item == null) return;
            if (item.IsEmbedded) return; // skip preloading metadata for embedded items

            var urlKey = item.Url ?? string.Empty;
            if (ContainsMetaData(urlKey)) return;

            try
            {


                // make a new wrapper to load the metadata into
                BasisLoadableBundleWrapper newWrapper = await CreateNewWrapperFromItem(item);

                // new report and CancellationSource source
                BasisProgressReport Report = new BasisProgressReport();
                CancellationTokenSource CancellationSource = new CancellationTokenSource();

                // perform the action to download the file or grab it from disc?
                await BasisBeeManagement.HandleMetaOnlyLoad(newWrapper.basisTrackedBundleWrapper, Report, CancellationSource.Token);
                
                // grab the wrapper from disc, we can pass in our wrapper
                BasisLoadableBundleWrapper wrapper = await LoadWrapperFromDisc(item, newWrapper);//on disc call? 
                var connector = wrapper.BasisLoadableBundle.BasisBundleConnector; //wrapper.LoadableBundle.BasisBundleConnector;

                if(wrapper == null)
                {
                    BasisDebug.LogError("Missing Wrapper!, was the data provided correct?");
                    return;
                }

                var cached = new CachedContent
                {
                    Name = connector?.BasisBundleDescription?.AssetBundleName ?? string.Empty,
                    AssetBundleDescription = connector?.BasisBundleDescription?.AssetBundleDescription,
                    ImageBase64 = connector?.ImageBase64,
                    DateOfCreation = connector?.DateOfCreation,
                    UniqueVersion = connector?.UniqueVersion,
                    BasisBundleConnector = connector,
                    BasisLoadableBundle = wrapper.BasisLoadableBundle,
                };

                string dateStrCache = connector?.DateOfCreation;
                if (!string.IsNullOrEmpty(dateStrCache) && DateTime.TryParse(dateStrCache, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate))
                {
                    cached.Created = parsedDate;
                }

                SetMetaData(urlKey, cached);
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }
        [HideInCallstack]
        public static void LogError(Exception ex)
        {
            BasisDebug.LogError(ex);
        }
        public static async Task PreloadMetaForItems(IEnumerable<BasisDataStoreItemKeys.ItemKey> items)
        {
            if (items == null) return;

            try
            {
                await Task.WhenAll(items.Select(PreloadMetaDataForItem));
            }
            catch (Exception ex)
            {
                BasisDebug.LogError(ex);
            }
        }
    }
}