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
            if (BasisMainMenu.ActiveMenuTitle == Title)
            {
                return;
            }

            BasisMenuPanel panel = BasisMainMenu.CreateActiveMenu(BasisMenuPanel.PanelData.Standard(Title), BasisMenuPanel.PanelStyles.Page);

            TextMeshProUGUI titleLabel = panel.Descriptor.TitleLabel;
            titleLabel.text = Title;

            BoundButton?.BindActiveStateToAddressablesInstance(panel);

            PanelTabGroup tabGroup = PanelTabGroup.CreateNew(panel.Descriptor.ContentParent, LayoutDirection.Vertical);


            BasisDataStoreItemKeys.ItemKey[] Data = BasisDataStoreItemKeys.DisplayKeys();

            List<BasisDataStoreItemKeys.ItemKey> Props = new List<BasisDataStoreItemKeys.ItemKey>();
            List<BasisDataStoreItemKeys.ItemKey> Worlds = new List<BasisDataStoreItemKeys.ItemKey>();
            List<BasisDataStoreItemKeys.ItemKey> Avatars = new List<BasisDataStoreItemKeys.ItemKey>();
            for (int Index = 0; Index < Data.Length; Index++)
            {
                BasisDataStoreItemKeys.ItemKey ItemKey = Data[Index];
                BundledContentHolder.Mode Title = ItemKey.Mode;
                switch (Title)
                {
                    case BundledContentHolder.Mode.Prop:
                        Props.Add(ItemKey);
                        break;
                    case BundledContentHolder.Mode.World:
                        Worlds.Add(ItemKey);
                        break;
                    case BundledContentHolder.Mode.Avatar:
                        Avatars.Add(ItemKey);
                        break;
                }
            }
            tabGroup.AddTab("Props", null, PropsTab(tabGroup, Props));
            tabGroup.AddTab("Worlds", null, WorldsTab(tabGroup, Worlds));
            tabGroup.AddTab("Avatars", null, AvatarsTab(tabGroup, Avatars));
            tabGroup.AddExtraAction("Add New Item", AddNewItem);

            panel.Descriptor.ForceRebuild();
        }
        public static void AddNewItem()
        {

        }
        public static PanelTabPage PropsTab(PanelTabGroup tabGroup, List<BasisDataStoreItemKeys.ItemKey> Items)
        {
            PanelTabPage tab = PanelTabPage.CreateHorizontal(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;
            for (int Index = 0; Index < Items.Count; Index++)
            {
                BasisDataStoreItemKeys.ItemKey item = Items[Index];
                BasisTrackedBundleWrapper wrapper = new BasisTrackedBundleWrapper();
                var LoadableBundle = new BasisLoadableBundle
                {
                    BasisLocalEncryptedBundle = new BasisStoredEncryptedBundle(),
                    BasisRemoteBundleEncrypted = new BasisRemoteEncyptedBundle(),
                    BasisBundleConnector = new BasisBundleConnector()
                };
                LoadableBundle.UnlockPassword = item.Pass;
                LoadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation = item.Url;
                wrapper.LoadableBundle = LoadableBundle;

                BasisProgressReport Report = new BasisProgressReport();
                LoadItemData(wrapper, Report, new CancellationToken());
            }
            descriptor.ForceRebuild();
            return tab;
        }
        public static PanelTabPage WorldsTab(PanelTabGroup tabGroup, List<BasisDataStoreItemKeys.ItemKey> Items)
        {
            PanelTabPage tab = PanelTabPage.CreateHorizontal(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;
            for (int Index = 0; Index < Items.Count; Index++)
            {
                BasisDataStoreItemKeys.ItemKey item = Items[Index];

                //    item.
            }
            descriptor.ForceRebuild();
            return tab;
        }
        public static PanelTabPage AvatarsTab(PanelTabGroup tabGroup, List<BasisDataStoreItemKeys.ItemKey> Items)
        {
            PanelTabPage tab = PanelTabPage.CreateHorizontal(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;
            for (int Index = 0; Index < Items.Count; Index++)
            {
                BasisDataStoreItemKeys.ItemKey item = Items[Index];
                //    item.
            }
            descriptor.SetTitle("Props");
            RectTransform container = descriptor.ContentParent;

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            group.SetTitle("Player Inventory");
            group.SetDescription("Items currently owned by the player.");

            descriptor.ForceRebuild();
            return tab;
        }
        public static async void LoadItemData(BasisTrackedBundleWrapper Wrapper, BasisProgressReport report, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await BasisBeeManagement.HandleMetaOnlyLoad(Wrapper, report, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var desc = Wrapper.LoadableBundle.BasisBundleConnector?.BasisBundleDescription;
                if (desc != null && !string.IsNullOrWhiteSpace(desc.AssetBundleName))
                {
                    string title = desc.AssetBundleName;
                }

                string imageBytes = Wrapper.LoadableBundle.BasisBundleConnector?.ImageBase64;
                if (!string.IsNullOrEmpty(imageBytes))
                {
                    var IconTexture = BasisTextureCompression.FromPngBytes(imageBytes);
                    var IconSprite = Sprite.Create(IconTexture, new Rect(0, 0, IconTexture.width, IconTexture.height), Vector2.zero);
                    //                    Button.Descriptor.SetIcon(IconSprite);

                    // Button.Descriptor.SetTitle(title);
                }
            }
            catch (Exception e)
            {
                BasisDebug.LogError(e);
                BasisLoadHandler.RemoveDiscInfo(Wrapper.LoadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation);
                return;
            }
        }
    }
}
