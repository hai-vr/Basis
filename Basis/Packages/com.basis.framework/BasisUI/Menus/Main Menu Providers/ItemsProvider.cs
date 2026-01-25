using Basis.Scripts.UI.UI_Panels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Basis.BasisUI
{
    public partial class ItemsProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new ItemsProvider());
        }

        public override string Title => "Items";
        public override string IconAddress => AddressableAssets.Sprites.Items;
        public override int Order => 1; // after Settings

        public override void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title) return;

            BasisMenuPanel panel = BasisMainMenu.CreateActiveMenu(
                BasisMenuPanel.PanelData.Standard(Title),
                BasisMenuPanel.PanelStyles.Page);

            var titleLabel = panel.Descriptor.TitleLabel;
            titleLabel.text = Title;

            BoundButton?.BindActiveStateToAddressablesInstance(panel);

            PanelTabGroup tabGroup = PanelTabGroup.CreateNew(panel.Descriptor.ContentParent, LayoutDirection.Vertical);

            BasisDataStoreItemKeys.ItemKey[] data = BasisDataStoreItemKeys.DisplayKeys();

            List<BasisDataStoreItemKeys.ItemKey> props = new();
            List<BasisDataStoreItemKeys.ItemKey> worlds = new();
            List<BasisDataStoreItemKeys.ItemKey> avatars = new();
            BasisDebug.Log($"Stored Item Keys were {data.Length}");
            for (int i = 0; i < data.Length; i++)
            {
                var k = data[i];
                switch (k.Mode)
                {
                    case BundledContentHolder.Mode.Prop: props.Add(k); break;
                    case BundledContentHolder.Mode.World: worlds.Add(k); break;
                    case BundledContentHolder.Mode.Avatar: avatars.Add(k); break;
                    default:
                        BasisDebug.LogError($"Mode Not Implented! {k.Mode}");
                        break;
                }
            }

            tabGroup.AddTab("Props", null, PropsTab(tabGroup, props));
            tabGroup.AddTab("Worlds", null, WorldsTab(tabGroup, worlds));
            tabGroup.AddTab("Avatars", null, AvatarsTab(tabGroup, avatars));

            tabGroup.AddExtraAction("Add New Item", AddNewItem);

            panel.Descriptor.ForceRebuild();
        }
        public static async void AddNewItem()
        {
            BasisDataStoreItemKeys.ItemKey Key = new BasisDataStoreItemKeys.ItemKey
            {
                Url = "a5742fb62455e10f9e7019d1c5a2b39bbcb59eb5447f4206e6c0c71e40d2d6b1",
                Pass = "https://BasisFramework.b-cdn.net/Version2/Props/Truck/truck2/00e1f4a32a6a451fb450fa79d729defd20260124.BEE",
                Mode = BundledContentHolder.Mode.Prop
            };

            await BasisDataStoreItemKeys.AddNewKey(Key);
        }
        public static PanelTabPage PropsTab(PanelTabGroup tabGroup, List<BasisDataStoreItemKeys.ItemKey> items)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            var d = tab.Descriptor;
            d.SetTitle("Props");
            d.SetDescription("Cached / saved prop bundles.");
            BuildItemsList(items, tab);
            d.ForceRebuild();
            return tab;
        }
        public static PanelTabPage WorldsTab(PanelTabGroup tabGroup, List<BasisDataStoreItemKeys.ItemKey> items)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            var d = tab.Descriptor;
            d.SetTitle("Worlds");
            d.SetDescription("Cached / saved world bundles.");
            BuildItemsList(items, tab);
            d.ForceRebuild();
            return tab;
        }
        public static PanelTabPage AvatarsTab(PanelTabGroup tabGroup, List<BasisDataStoreItemKeys.ItemKey> items)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            var d = tab.Descriptor;
            d.SetTitle("Avatars");
            d.SetDescription("Cached / saved avatar bundles.");
            BuildItemsList(items, tab);
            d.ForceRebuild();
            return tab;
        }
        private static void BuildItemsList(List<BasisDataStoreItemKeys.ItemKey> items, PanelTabPage tab)
        {
            RectTransform container = tab.Descriptor.ContentParent;
            PanelElementDescriptor header = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            header.SetTitle("Items");
            header.SetDescription("Meta-only loads. Use actions to load, copy, or remove.");

            PanelButton refresh = PanelButton.CreateNew(header.ContentParent);
            refresh.Descriptor.SetTitle("Refresh List");
            refresh.Descriptor.SetDescription("Reloads tab UI from stored keys.");
            refresh.OnClicked += () =>
            {

            };

            // Empty state
            if (items == null || items.Count == 0)
            {
                PanelElementDescriptor empty =
                    PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
                empty.SetTitle("No items");
                empty.SetDescription("Nothing cached in this category yet.");
                return;
            }

            // List entries
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                CreateItemCard(item, container);
            }
        }

        private static void CreateItemCard(BasisDataStoreItemKeys.ItemKey item, RectTransform container)
        {
            // Initial group with placeholder info; will be upgraded when metadata finishes loading.
            PanelElementDescriptor group =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);

            group.SetTitle("Loading...");
            group.SetDescription(item.Url);

            // Action row (works even before meta load)
            PanelButton copyUrl = PanelButton.CreateNew(group.ContentParent);
            copyUrl.Descriptor.SetTitle("Copy URL");
            copyUrl.OnClicked += () => GUIUtility.systemCopyBuffer = item.Url ?? string.Empty;

            PanelButton copyPass = PanelButton.CreateNew(group.ContentParent);
            copyPass.Descriptor.SetTitle("Copy Password");
            copyPass.OnClicked += () => GUIUtility.systemCopyBuffer = item.Pass ?? string.Empty;

            PanelButton remove = PanelButton.CreateNew(group.ContentParent);
            remove.Descriptor.SetTitle("Remove");
            remove.Descriptor.SetDescription("Removes this key from the datastore.");
            remove.OnClicked += async () =>
            {
                try
                {
                    // If your API differs, swap this out for whatever remove method exists in your project.
                    await BasisDataStoreItemKeys.RemoveKey(item);
                    UnityEngine.Object.Destroy(group.gameObject);
                }
                catch (Exception e)
                {
                    BasisDebug.LogError(e);
                }
            };

            PanelButton loadFull = PanelButton.CreateNew(group.ContentParent);
            loadFull.Descriptor.SetTitle("Load");
            loadFull.Descriptor.SetDescription("Loads the bundle (non-meta).");
            loadFull.OnClicked += async () =>
            {
                try
                {
                    var wrapper = BuildWrapper(item);
                    var report = new BasisProgressReport();
                    //  await BasisBeeManagement.HandleFullLoad(wrapper, report, CancellationToken.None);
                }
                catch (Exception e)
                {
                    BasisDebug.LogError(e);
                }
            };

            // Kick meta-only load that will fill title/icon/description
            var wrapperForMeta = BuildWrapper(item);
            var reportForMeta = new BasisProgressReport();

            // Fire and forget; UI updates happen inside.
            _ = LoadItemMetaIntoGroup(wrapperForMeta, reportForMeta, CancellationToken.None, group);
        }

        private static BasisTrackedBundleWrapper BuildWrapper(BasisDataStoreItemKeys.ItemKey item)
        {
            var wrapper = new BasisTrackedBundleWrapper();
            var loadable = new BasisLoadableBundle
            {
                BasisLocalEncryptedBundle = new BasisStoredEncryptedBundle(),
                BasisRemoteBundleEncrypted = new BasisRemoteEncyptedBundle(),
                BasisBundleConnector = new BasisBundleConnector(),
                UnlockPassword = item.Pass
            };
            loadable.BasisRemoteBundleEncrypted.RemoteBeeFileLocation = item.Url;
            wrapper.LoadableBundle = loadable;
            return wrapper;
        }
        private static async Task LoadItemMetaIntoGroup( BasisTrackedBundleWrapper wrapper, BasisProgressReport report, CancellationToken cancellationToken, PanelElementDescriptor group)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await BasisBeeManagement.HandleMetaOnlyLoad(wrapper, report, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                var desc = wrapper.LoadableBundle.BasisBundleConnector?.BasisBundleDescription;

                string title = "Unknown Bundle";

                if (desc != null)
                {
                    if (!string.IsNullOrWhiteSpace(desc.AssetBundleName))
                        title = desc.AssetBundleName;
                }

                Sprite iconSprite = null;
                string imageBase64 = wrapper.LoadableBundle.BasisBundleConnector?.ImageBase64;
                if (!string.IsNullOrEmpty(imageBase64))
                {
                    var tex = BasisTextureCompression.FromPngBytes(imageBase64);
                    if (tex != null)
                    {
                        iconSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    }
                }

                group.SetIcon(iconSprite);
                group.SetTitle(title);

                string metaLine = string.Empty;
                group.SetDescription(string.IsNullOrEmpty(metaLine) ? wrapper.LoadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation : metaLine);

                group.ForceRebuild();
            }
            catch (Exception e)
            {
                BasisDebug.LogError(e);
                BasisLoadHandler.RemoveDiscInfo(wrapper.LoadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation);

                group.SetTitle("Failed to load meta");
                group.SetDescription(e.Message);
                group.ForceRebuild();
            }
        }
    }
}
