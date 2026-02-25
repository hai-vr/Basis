using System.Threading;
using System.Threading.Tasks;
using Basis.Scripts.UI.UI_Panels;

namespace Basis.BasisUI
{
    public class PinnedItemProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        private BasisDataStoreItemKeys.ItemKey _key;
        private readonly string _title;

        public PinnedItemProvider(BasisDataStoreItemKeys.ItemKey item, CachedMetaData.CachedContent cachedItemData)
        {
            _key = item;
            _title = LibraryProvider.TitleToCase(cachedItemData.BasisBundleConnector.BasisBundleDescription.AssetBundleName);

            // // grab the item name from cache
            // if(!item.IsEmbedded)
            // {
            //     var tempWrapper = LibraryProvider.CreateNewWrapperFromItem(_key);
            //     BasisProgressReport Report = new BasisProgressReport();
            //     CancellationTokenSource CancellationSource = new CancellationTokenSource();
            //     // Attempt a meta-only load (this will download or read connector info and cache meta on disk)
            //     bool isValid = await BasisBeeManagement.HandleMetaOnlyLoad(tempWrapper.basisTrackedBundleWrapper, Report, CancellationSource.Token);
                
            //     BasisDebug.Log($"Creating a new pinned item provider for item = {item.Url}");
            //     _title = LibraryProvider.TitleToCase(wrapper.BasisLoadableBundle.BasisBundleConnector.BasisBundleDescription.AssetBundleName);

            //     // Try get cached meta once
            //     // CachedMetaData.CachedContent cachedMeta;
            //     // if(CachedMetaData.TryGetMeta(url.item, out cachedMeta))
            //     // {
                    
            //     // }

                
            // }
            // else
            // {
            //     _title = item.Url;
            // }
            
        }

        public override string Title => _title; // or a nicer name
        public override string IconAddress => AddressableAssets.Sprites.Pin;
        public override int Order => 10; // after static items
        public override bool Hidden => false;

        public override async void RunAction()
        {
            //
            // load / spawn / do whatever
            BasisDebug.Log( $"Pinned Provider Action for item = {_key.Url}" );
            await LibraryProvider.LoadSelectedItem(_key, BundledContentHolder.NetworkType.Local, false); 
        }
        
    }
}