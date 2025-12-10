using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;

namespace Basis.BasisUI
{
    public static class CachedAvatarData
    {
        public static List<BasisLoadableBundle> AvatarBundles = new();
        public static bool Initialized;

        public static async Task FillPreloadedBundles(List<BasisLoadableBundle> bundles)
        {
            AvatarBundles.Clear();
            AvatarBundles.AddRange(bundles);

            int preloadedCount = bundles.Count;
            for (int Index = 0; Index < preloadedCount; Index++)
            {
                BasisLoadableBundle loadableBundle = bundles[Index];
                var key = new BasisDataStoreAvatarKeys.AvatarKey
                {
                    Pass = loadableBundle.UnlockPassword,
                    Url = loadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation
                };

                if (!BasisDataStoreAvatarKeys.DisplayKeys().Exists(k => k.Url == key.Url && k.Pass == key.Pass))
                {
                    await BasisDataStoreAvatarKeys.AddNewKey(key);
                }
            }

        }

        public static async Task Initialize()
        {
            await BasisDataStoreAvatarKeys.LoadKeys();
            List<BasisDataStoreAvatarKeys.AvatarKey> activeKeys = new(BasisDataStoreAvatarKeys.DisplayKeys());
            List<BasisDataStoreAvatarKeys.AvatarKey> keysToRemove = new();

            foreach (BasisDataStoreAvatarKeys.AvatarKey key in activeKeys)
            {
                if (BasisLoadHandler.IsMetaDataOnDisc(key.Url, out BasisBEEExtensionMeta info))
                {
                    // Debug.Log($"AvatarData: Found {key.Url}");
                }
                else
                {
                    Debug.Log($"AvatarData: Did NOT find {key.Url}");
                    keysToRemove.Add(key);
                }

                if (AvatarBundles.Exists(b => b.BasisRemoteBundleEncrypted.RemoteBeeFileLocation == key.Url))
                {
                    // Debug.Log($"AvatarData: Found entry for {key.Url}");
                }
                else
                {
                    // Debug.Log($"AvatarData: Did not entry file for {key.Url}. Creating now.");

                    BasisLoadableBundle bundle = new()
                    {
                        BasisRemoteBundleEncrypted = info.StoredRemote,
                        BasisLocalEncryptedBundle =  info.StoredLocal,
                        UnlockPassword = key.Pass,

                        BasisBundleConnector = new BasisBundleConnector()
                        {
                            BasisBundleDescription = new BasisBundleDescription(),
                            BasisBundleGenerated = new BasisBundleGenerated[]{new()},
                            UniqueVersion = "",
                        },
                    };

                    AvatarBundles.Add(bundle);
                }
            }

            foreach (BasisDataStoreAvatarKeys.AvatarKey key in keysToRemove)
            {
                await BasisDataStoreAvatarKeys.RemoveKey(key);
            }

            Initialized = true;
        }
    }

    public class PanelAvatarList : PanelSelectionGroup
    {

        public Sprite FallbackAvatarSprite;
        public List<BasisLoadableBundle> PreLoadedAvatars = new();

        public BasisProgressReport Report = new();
        public CancellationToken CancellationToken = CancellationToken.None;


        public override void OnCreateEvent()
        {
            base.OnCreateEvent();
            Task task = LoadAvatars();
        }

        private async Task LoadAvatars()
        {
            if (!CachedAvatarData.Initialized)
            {
                await CachedAvatarData.FillPreloadedBundles(PreLoadedAvatars);
                await CachedAvatarData.Initialize();
            }

            await CreateButtons();
        }

        private async Task CreateButtons()
        {
            foreach (PanelButton button in SelectionButtons)
                button.ReleaseInstance();

            SelectionButtons.Clear();

            foreach (BasisLoadableBundle bundle in CachedAvatarData.AvatarBundles)
            {
                PanelButton button = PanelButton.CreateNew(PanelButton.ButtonStyles.Avatar, TabButtonParent);
                SelectionButtons.Add(button);
                button.OnClicked += () => OnTabSelected(button);
                button.OnClicked += () => ShowAvatarInfo(bundle);

                BasisTrackedBundleWrapper wrapper = new()
                {
                    LoadableBundle = bundle,
                };

                string title = "Avatar";
                Sprite icon = FallbackAvatarSprite;
                if (bundle.UnlockPassword == BasisBeeConstants.DefaultAvatar)
                {
                    title = BasisBeeConstants.DefaultAvatar;
                }
                else
                {
                    try
                    {
                        await BasisBeeManagement.HandleMetaOnlyLoad(wrapper, Report, CancellationToken);
                        title = wrapper.LoadableBundle.BasisBundleConnector.BasisBundleDescription.AssetBundleName;
                        byte[] imageBytes = wrapper.LoadableBundle.BasisBundleConnector.ImageBytes;
                        if (imageBytes != null)
                        {
                            Texture2D raw = BasisTextureCompression.FromPngBytes(wrapper.LoadableBundle.BasisBundleConnector.ImageBytes);
                            icon = Sprite.Create(raw, new Rect(0, 0, raw.width, raw.height), Vector2.zero);
                        }
                    }
                    catch (Exception e)
                    {
                        BasisDebug.LogError(e);
                        BasisLoadHandler.RemoveDiscInfo(bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation);
                        throw;
                    }
                }

                button.Descriptor.SetTitle(title);
                button.Descriptor.SetIcon(icon);

                button.OnClicked += () => OnTabSelected(button);
            }
        }

        private void ShowAvatarInfo(BasisLoadableBundle bundle)
        {
            Debug.Log($"AvatarData: Selected {bundle.BasisBundleConnector.BasisBundleDescription.AssetBundleName}");
        }
    }
}
