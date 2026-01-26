using Basis.Scripts.UI.UI_Panels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static Basis.BasisUI.PanelButton;

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

        public static BasisMenuPanel panel;
        public override void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title) return;

            panel = BasisMainMenu.CreateActiveMenu(
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
            var Background = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Overlay, panel);
            var Descriptor = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, Background);

            Descriptor.rectTransform.localPosition = new Vector3(0, 0, 0);
            Descriptor.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            Descriptor.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            Descriptor.rectTransform.anchoredPosition = Vector2.zero;
            Descriptor.SetSize(new Vector2(700, 400));
            Descriptor.SetDescription("Background Panel");


            PanelTabGroup AcceptORDenyPanel = PanelTabGroup.CreateNew(Descriptor, LayoutDirection.Horizontal);
            PanelButton NoPanel = PanelButton.CreateNew(ButtonStyles.CancelButton, AcceptORDenyPanel.TabButtonParent);
            PanelButton YesPanel = PanelButton.CreateNew(ButtonStyles.AcceptButton, AcceptORDenyPanel.TabButtonParent);
            NoPanel.Descriptor.SetTitle("Cancel");
            YesPanel.Descriptor.SetTitle("Add");
            NoPanel.Descriptor.SetWidth(270);
            NoPanel.Descriptor.SetHeight(60);
            YesPanel.Descriptor.SetWidth(270);
            YesPanel.Descriptor.SetHeight(60);

            BasisDataStoreItemKeys.ItemKey Key = new BasisDataStoreItemKeys.ItemKey
            {
                Pass = "a5742fb62455e10f9e7019d1c5a2b39bbcb59eb5447f4206e6c0c71e40d2d6b1",
                Url = "https://BasisFramework.b-cdn.net/Version2/Props/Truck/truck2/00e1f4a32a6a451fb450fa79d729defd20260124.BEE",
                Mode = BundledContentHolder.Mode.Prop
            };
            await BasisDataStoreItemKeys.AddNewKey(Key);
        }
        public static PanelTabPage PropsTab(PanelTabGroup tabGroup, List<BasisDataStoreItemKeys.ItemKey> items)
        {
            PanelTabPage tab = PanelTabPage.CreateGrid(tabGroup.Descriptor.ContentParent);
            var d = tab.Descriptor;
            d.SetTitle("Props");
            BuildItemsList(items, tab);
            d.ForceRebuild();
            return tab;
        }
        public static PanelTabPage WorldsTab(PanelTabGroup tabGroup, List<BasisDataStoreItemKeys.ItemKey> items)
        {
            PanelTabPage tab = PanelTabPage.CreateGrid(tabGroup.Descriptor.ContentParent);
            var d = tab.Descriptor;
            d.SetTitle("Worlds");
            BuildItemsList(items, tab);
            d.ForceRebuild();
            return tab;
        }
        public static PanelTabPage AvatarsTab(PanelTabGroup tabGroup, List<BasisDataStoreItemKeys.ItemKey> items)
        {
            PanelTabPage tab = PanelTabPage.CreateGrid(tabGroup.Descriptor.ContentParent);
            var d = tab.Descriptor;
            d.SetTitle("Avatars");
            BuildItemsList(items, tab);
            d.ForceRebuild();
            return tab;
        }
        private static void BuildItemsList(List<BasisDataStoreItemKeys.ItemKey> items, PanelTabPage tab)
        {
            RectTransform container = tab.Descriptor.ContentParent;
            // List entries
            for (int Index = 0; Index < items.Count; Index++)
            {
                var item = items[Index];
                CreateItemCard(item, container);
            }
        }

        private static void CreateItemCard(BasisDataStoreItemKeys.ItemKey item, RectTransform container)
        {
            PanelButton Buttonpanel = PanelButton.CreateNew(ButtonStyles.Prop, container);

            // Kick meta-only load that will fill title/icon/description
            var wrapperForMeta = BuildWrapper(item);
            var reportForMeta = new BasisProgressReport();

            // Fire and forget; UI updates happen inside.
            _ = LoadItemMetaIntoGroup(wrapperForMeta, reportForMeta, CancellationToken.None, Buttonpanel);
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
        private static async Task LoadItemMetaIntoGroup( BasisTrackedBundleWrapper wrapper, BasisProgressReport report, CancellationToken cancellationToken, PanelButton Buttonpanel)
        {
            var descripter = Buttonpanel.Descriptor;
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
                Buttonpanel.SetIcon(iconSprite,false);
                descripter.SetTitle(title);
                string metaLine = string.Empty;
                descripter.SetDescription(wrapper.LoadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation);

                descripter.ForceRebuild();
            }
            catch (Exception e)
            {
                BasisDebug.LogError(e);
                BasisLoadHandler.RemoveDiscInfo(wrapper.LoadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation);

                descripter.SetTitle("Failed to load meta");
                descripter.SetDescription(e.Message);
                descripter.ForceRebuild();
            }
        }
    }
}
