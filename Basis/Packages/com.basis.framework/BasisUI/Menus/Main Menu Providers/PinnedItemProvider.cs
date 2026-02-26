using System.Threading;
using System.Threading.Tasks;
using Basis.Scripts.UI.UI_Panels;

namespace Basis.BasisUI
{
    public class PinnedItemProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        private BasisDataStoreItemKeys.ItemKey _key;
        private readonly string _title;
        private readonly string _iconAddress;

        public PinnedItemProvider(BasisDataStoreItemKeys.ItemKey item, CachedMetaData.CachedContent cachedItemData)
        {
            _key = item;
            _title = LibraryProvider.TitleToCase(cachedItemData.BasisBundleConnector.BasisBundleDescription.AssetBundleName);
            _iconAddress = item.IsEmbedded ? EmbeddedItems.GetAddressableSpriteForEmbeddedItem(item) : AddressableAssets.Sprites.Pin;
        }

        public override string Title => _title; // or a nicer name
        public override string IconAddress => _iconAddress;
        public override int Order => 10; // after static items
        public override bool Hidden => false;

        public override async void RunAction()
        {
            // load / spawn / do whatever
            BasisDebug.Log( $"Pinned Provider Action for item = {_key.Url}" );
            await LibraryProvider.LoadSelectedItem(_key, _key.PinnedSettings.NetworkType, !_key.PinnedSettings.IsEphemeral); 
        }
        
    }
}