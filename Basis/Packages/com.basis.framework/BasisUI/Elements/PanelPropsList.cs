using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.UI.UI_Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SerializableBasis;
using Debug = UnityEngine.Debug;

namespace Basis.BasisUI
{
    /// <summary>
    /// Stores and reconciles cached PROP bundles + saved keys.
    /// </summary>
    public static class CachedPropData
    {
        public static List<BasisLoadableBundle> PropBundles = new();
        public static bool Initialized;

        public static async Task FillPreloadedBundles(List<BasisLoadableBundle> bundles)
        {
            PropBundles.Clear();
            PropBundles.AddRange(bundles);

            int preloadedCount = bundles.Count;
            for (int i = 0; i < preloadedCount; i++)
            {
                BasisLoadableBundle loadableBundle = bundles[i];
                BasisDataStorePropKeys.PropKey key = new()
                {
                    Pass = loadableBundle.UnlockPassword,
                    Url = loadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation
                };

                BasisDataStorePropKeys.PropKey[] keys = BasisDataStorePropKeys.DisplayKeys();

                bool found = false;
                for (int index = 0; index < keys.Length; index++)
                {
                    var cur = keys[index];
                    if (cur != null && cur.Url == key.Url && cur.Pass == key.Pass)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    await BasisDataStorePropKeys.AddNewKey(key);
                }
            }
        }

        public static async Task Initialize()
        {
            await BasisDataStorePropKeys.LoadKeys();
            List<BasisDataStorePropKeys.PropKey> activeKeys = new(BasisDataStorePropKeys.DisplayKeys());
            List<BasisDataStorePropKeys.PropKey> keysToRemove = new();

            foreach (BasisDataStorePropKeys.PropKey key in activeKeys)
            {
                // If the metadata is missing on disk, remove the key and DO NOT attempt to create a bundle from it.
                if (!BasisLoadHandler.IsMetaDataOnDisc(key.Url, out BasisBEEExtensionMeta info))
                {
                    Debug.Log($"PropData: Did NOT find {key.Url}");
                    keysToRemove.Add(key);
                    continue;
                }

                // If we already have a bundle entry for this url, do nothing.
                if (PropBundles.Exists(b => b.BasisRemoteBundleEncrypted.RemoteBeeFileLocation == key.Url))
                {
                    continue;
                }

                // Otherwise create a bundle entry from stored meta.
                BasisLoadableBundle bundle = new()
                {
                    BasisRemoteBundleEncrypted = info.StoredRemote,
                    BasisLocalEncryptedBundle = info.StoredLocal,
                    UnlockPassword = key.Pass,

                    BasisBundleConnector = new BasisBundleConnector()
                    {
                        BasisBundleDescription = new BasisBundleDescription(),
                        BasisBundleGenerated = new BasisBundleGenerated[] { new() },
                        UniqueVersion = "",
                    },
                };

                PropBundles.Add(bundle);
            }

            foreach (BasisDataStorePropKeys.PropKey key in keysToRemove)
            {
                await BasisDataStorePropKeys.RemoveKey(key);
            }

            Initialized = true;
        }
    }

    /// <summary>
    /// UI panel that lists PROPS/WORLDS, and spawns/unspawns them.
    /// </summary>
    public class PanelPropsList : PanelSelectionGroup
    {
        public static class PropListStyles
        {
            public static string Default = "Packages/com.basis.sdk/Prefabs/Panel Elements/Prop List Page.prefab";
        }

        public static PanelPropsList CreateNew(Component parent) => CreateNew<PanelPropsList>(PropListStyles.Default, parent);
        public class PropMenuItem
        {
            public PanelButton Button;
            public BasisTrackedBundleWrapper Wrapper;
            public Texture2D IconTexture;
            public Sprite IconSprite;

            public void Clear()
            {
                Button.ReleaseInstance();
                if (IconTexture) UnityEngine.Object.Destroy(IconTexture);
                if (IconSprite) UnityEngine.Object.Destroy(IconSprite);
            }

            public async Task LoadItemData(BasisProgressReport report, CancellationToken cancellationToken)
            {
                string title = "Prop";

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await BasisBeeManagement.HandleMetaOnlyLoad(Wrapper, report, cancellationToken);
                    if (cancellationToken.IsCancellationRequested) return;

                    var desc = Wrapper.LoadableBundle.BasisBundleConnector?.BasisBundleDescription;
                    if (desc != null && !string.IsNullOrWhiteSpace(desc.AssetBundleName))
                        title = desc.AssetBundleName;

                    string imageBytes = Wrapper.LoadableBundle.BasisBundleConnector?.ImageBase64;
                    if (!string.IsNullOrEmpty(imageBytes))
                    {
                        IconTexture = BasisTextureCompression.FromPngBytes(imageBytes);
                        if (IconTexture)
                            IconSprite = Sprite.Create(IconTexture, new Rect(0, 0, IconTexture.width, IconTexture.height), Vector2.zero);
                    }

                    if (IconSprite)
                        Button.Descriptor.SetIcon(IconSprite);
                }
                catch (Exception e)
                {
                    BasisDebug.LogError(e);
                    BasisLoadHandler.RemoveDiscInfo(
                        Wrapper.LoadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation
                    );
                    return;
                }

                Button.Descriptor.SetTitle(title);
            }
        }

        // (renamed) PreLoaded props
        public List<BasisLoadableBundle> PreLoadedProps = new();

        [Header("Spawn/World Behaviour")]
        [Tooltip("If true, clicking Load will unload if already loaded; if false, Load always loads (and you must use Unload separately if you expose it).")]
        public bool ToggleLoadUnload = true;

        [Tooltip("If true, clicking 'Remove Prop' (remove from list) will also unload it if currently loaded.")]
        public bool RemoveAlsoUnloads = true;

        [Tooltip("If enabled, spawned content persists across scene changes on the client.")]
        public bool Persistent = false;

        [Header("Optional Spawn Overrides (Props only)")]
        public bool UseCustomSpawnPosition = false;
        public Vector3 CustomSpawnPosition;
        public Quaternion CustomSpawnRotation = Quaternion.identity;
        public bool ApplyCustomScale = false;
        public Vector3 CustomSpawnScale = Vector3.one;

        public BasisProgressReport Report = new();
        public CancellationTokenSource CancellationSource = new();
        public PropMenuItem SelectedProp;

        public TextMeshProUGUI CreationDateLabel;
        public TextMeshProUGUI FileSizeLabel;
        public PanelPasswordField PropIDField;
        public PanelPasswordField PropUrlField;
        public PanelPasswordField PropPasswordField;
        public GameObject WindowsIcon;
        public GameObject MacIcon;
        public GameObject LinuxIcon;
        public GameObject AndroidIcon;
        public GameObject IOSIcon;
        public PanelPropAddNew NewPropPanel; // you might rename later, kept for compatibility
        public PanelButton NewPropButton;

        public PanelButton RemovePropButton;  // Removes from saved list
        public PanelButton LoadPropButton;    // Load/Unload toggle

        public List<PropMenuItem> MenuItems = new();

        public override void OnCreateEvent()
        {
            base.OnCreateEvent();
            ClearPropInfo();

            NewPropButton.OnClicked += NewPropPanel.Show;

            // Toggle-style behaviour:
            LoadPropButton.OnClicked += () => _ = LoadOrUnloadSelected();

            // Remove-from-list behaviour:
            RemovePropButton.OnClicked += RemoveProp;

            NewPropPanel.Hide();

            _ = LoadPropBundles();
        }

        public override void OnReleaseEvent()
        {
            base.OnReleaseEvent();

            if (CancellationSource != null)
            {
                CancellationSource.Cancel();
                CancellationSource.Dispose();
            }
        }

        private async Task LoadPropBundles()
        {
            if (!CachedPropData.Initialized)
            {
                await CachedPropData.FillPreloadedBundles(PreLoadedProps);
                await CachedPropData.Initialize();
            }

            await CreateButtons();
        }

        private async Task CreateButtons()
        {
            foreach (PanelButton button in SelectionButtons)
                button.ReleaseInstance();

            SelectionButtons.Clear();
            MenuItems.Clear();

            foreach (BasisLoadableBundle bundle in CachedPropData.PropBundles)
            {
                PanelButton button = PanelButton.CreateNew(PanelButton.ButtonStyles.Prop, TabButtonParent);
                SelectionButtons.Add(button);
                button.Descriptor.SetTitle("Prop");

                BasisTrackedBundleWrapper wrapper = new()
                {
                    LoadableBundle = bundle,
                };

                PropMenuItem item = new()
                {
                    Button = button,
                    Wrapper = wrapper
                };

                MenuItems.Add(item);

                button.OnClicked += () => OnTabSelected(button);
                button.OnClicked += () => ShowPropInfo(item);
            }

            foreach (PropMenuItem item in MenuItems)
                await item.LoadItemData(Report, CancellationSource.Token);
        }

        public async Task AppendNewProp(BasisLoadableBundle bundle, bool selectAfterCreate)
        {
            PanelButton button = PanelButton.CreateNew(PanelButton.ButtonStyles.Prop, TabButtonParent);
            SelectionButtons.Add(button);
            button.Descriptor.SetTitle("Prop");

            BasisTrackedBundleWrapper wrapper = new()
            {
                LoadableBundle = bundle,
            };

            PropMenuItem item = new()
            {
                Button = button,
                Wrapper = wrapper
            };

            MenuItems.Add(item);

            button.OnClicked += () => OnTabSelected(button);
            button.OnClicked += () => ShowPropInfo(item);

            await item.LoadItemData(Report, CancellationSource.Token);

            CachedPropData.PropBundles.Add(bundle);

            if (selectAfterCreate) button.OnClick();
        }

        private void ClearPropInfo()
        {
            SelectedProp = null;

            CreationDateLabel.text = string.Empty;
            FileSizeLabel.text = string.Empty;
            Descriptor.SetIcon(string.Empty);
            Descriptor.SetTitle(string.Empty);
            Descriptor.SetDescription(string.Empty);

            PropIDField.SetValue(false);
            PropIDField.SetPassword(string.Empty);
            PropUrlField.SetValue(false);
            PropUrlField.SetPassword(string.Empty);
            PropPasswordField.SetValue(false);
            PropPasswordField.SetPassword(string.Empty);

            WindowsIcon.SetActive(false);
            MacIcon.SetActive(false);
            LinuxIcon.SetActive(false);
            AndroidIcon.SetActive(false);
            IOSIcon.SetActive(false);

            NewPropPanel.Hide();

            RefreshLoadButtonLabel();
        }

        private void ShowPropInfo(PropMenuItem item)
        {
            if (item == null)
            {
                BasisDebug.LogError("No prop menu item provided.");
                ClearPropInfo();
                return;
            }

            SelectedProp = item;

            BasisLoadableBundle bundle = item.Wrapper.LoadableBundle;
            if (bundle == null)
            {
                BasisDebug.LogError($"Bundle on PropMenuItem {item} not found.");
                RemovePropItem(item);
                ClearPropInfo();
                return;
            }

            BasisBundleDescription description = bundle.BasisBundleConnector?.BasisBundleDescription;
            if (description == null)
            {
                BasisDebug.LogError($"Bundle Description on PropMenuItem {item} not found.");
                RemovePropItem(item);
                ClearPropInfo();
                return;
            }

            Descriptor.SetIcon(item.IconSprite);
            Descriptor.SetTitle(description.AssetBundleName);
            Descriptor.SetDescription(description.AssetBundleDescription);

            PropIDField.SetValue(false);
            PropIDField.SetPassword(bundle.BasisBundleConnector.UniqueVersion);
            PropUrlField.SetValue(false);
            PropUrlField.SetPassword(bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation);
            PropPasswordField.SetValue(false);
            PropPasswordField.SetPassword(bundle.UnlockPassword);

            string creationDate = bundle.BasisBundleConnector.DateOfCreation;
            if (string.IsNullOrEmpty(creationDate))
            {
                creationDate = string.Empty;
            }
            else
            {
                creationDate = DateTime
                    .Parse(creationDate, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                    .ToString(CultureInfo.InvariantCulture);

                creationDate += " UTC";
            }

            CreationDateLabel.text = creationDate;

            string[] platforms = bundle.BasisBundleConnector.BasisBundleGenerated
                .Select(pair => pair.Platform).ToArray();

            WindowsIcon.SetActive(false);
            MacIcon.SetActive(false);
            LinuxIcon.SetActive(false);
            AndroidIcon.SetActive(false);
            IOSIcon.SetActive(false);

            foreach (string platform in platforms)
            {
                switch (platform)
                {
                    case "StandaloneWindows64":
                        WindowsIcon.SetActive(true);
                        break;
                    case "StandaloneOSX":
                        MacIcon.SetActive(true);
                        break;
                    case "StandaloneLinux64":
                        LinuxIcon.SetActive(true);
                        break;
                    case "Android":
                        AndroidIcon.SetActive(true);
                        break;
                    case "iOS":
                        IOSIcon.SetActive(true);
                        break;
                }
            }

            NewPropPanel.Hide();

            RefreshLoadButtonLabel();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }

        // --------------------------------------------------------------------
        //  Spawn helpers
        // --------------------------------------------------------------------
        private Vector3 GetSpawnPosition()
        {
            if (UseCustomSpawnPosition) return CustomSpawnPosition;

            if (BasisLocalPlayer.Instance != null)
                return BasisLocalPlayer.Instance.transform.position;

            // Fallback: camera, then zero.
            if (Camera.main != null)
                return Camera.main.transform.position;

            return Vector3.zero;
        }

        private Quaternion GetSpawnRotation()
        {
            if (CustomSpawnRotation == new Quaternion(0, 0, 0, 0))
                CustomSpawnRotation = Quaternion.identity;

            return UseCustomSpawnPosition ? CustomSpawnRotation : Quaternion.identity;
        }

        private Vector3 GetSpawnScale()
        {
            if (!ApplyCustomScale) return Vector3.one;
            if (CustomSpawnScale == Vector3.zero) CustomSpawnScale = Vector3.one;
            return CustomSpawnScale;
        }

        private string SelectedUrl()
        {
            return SelectedProp?.Wrapper?.LoadableBundle?.BasisRemoteBundleEncrypted?.RemoteBeeFileLocation ?? string.Empty;
        }

        private void RefreshLoadButtonLabel()
        {
            if (LoadPropButton == null) return;

            string url = SelectedUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                LoadPropButton.Descriptor.SetTitle("Load");
                return;
            }

            if (ToggleLoadUnload && Basis.BasisRuntimeSpawnRegistry.IsLoaded(url))
                LoadPropButton.Descriptor.SetTitle("Unload");
            else
                LoadPropButton.Descriptor.SetTitle("Load");
        }

        // --------------------------------------------------------------------
        //  UI actions: Load/Unload/Remove
        // --------------------------------------------------------------------
        public async Task LoadOrUnloadSelected()
        {
            if (SelectedProp == null)
            {
                BasisDebug.LogError("No selected bundle.");
                return;
            }

            var bundle = SelectedProp.Wrapper.LoadableBundle;
            if (bundle == null)
            {
                BasisDebug.LogError("Selected bundle is null.");
                return;
            }

            string url = bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;
            if (string.IsNullOrWhiteSpace(url))
            {
                BasisDebug.LogError("Selected bundle URL is empty.");
                return;
            }

            if (ToggleLoadUnload && Basis.BasisRuntimeSpawnRegistry.IsLoaded(url))
            {
                UnloadSelected();
                RefreshLoadButtonLabel();
                return;
            }

            await LoadSelected();
            RefreshLoadButtonLabel();
        }

        public async Task LoadSelected()
        {
            if (SelectedProp == null)
            {
                BasisDebug.LogError("No selected bundle.");
                return;
            }

            var bundle = SelectedProp.Wrapper.LoadableBundle;
            if (bundle == null)
            {
                BasisDebug.LogError("Selected bundle is null.");
                return;
            }

            string url = bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;
            string pass = bundle.UnlockPassword;

            if (string.IsNullOrWhiteSpace(url))
            {
                BasisDebug.LogError("Bundle URL is empty.");
                return;
            }

            // If already loaded, do nothing.
            if (Basis.BasisRuntimeSpawnRegistry.IsLoaded(url))
            {
                BasisDebug.Log($"Already loaded: {url}");
                return;
            }
            /*
            if (isWorld)
            {
                BasisNetworkSpawnItem.RequestSceneLoad(pass, url, Persistent, out LocalLoadResource loaded);
                Basis.BasisRuntimeSpawnRegistry.Set(url, loaded);
                await Task.CompletedTask;
                return;
            }
            */

            // Prop/GameObject spawn
            Vector3 spawnPos = GetSpawnPosition();
            Quaternion spawnRot = GetSpawnRotation();
            Vector3 spawnScale = GetSpawnScale();

            BasisNetworkSpawnItem.RequestGameObjectLoad(
                pass,
                url,
                spawnPos,
                spawnRot,
                spawnScale,
                Persistent,
                ApplyCustomScale,
                out LocalLoadResource loadedProp
            );

            Basis.BasisRuntimeSpawnRegistry.Set(url, loadedProp);
            await Task.CompletedTask;
        }

        public void UnloadSelected()
        {
            if (SelectedProp == null)
            {
                BasisDebug.LogError("No selected bundle.");
                return;
            }

            var bundle = SelectedProp.Wrapper.LoadableBundle;
            if (bundle == null) return;

            string url = bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;
            if (string.IsNullOrWhiteSpace(url)) return;

            if (!Basis.BasisRuntimeSpawnRegistry.TryGet(url, out LocalLoadResource res) || string.IsNullOrEmpty(res.LoadedNetID))
            {
                BasisDebug.Log("Nothing loaded for this item.");
                return;
            }

            //   bool isWorld = IsWorld(bundle);

            // if (isWorld)
            //     BasisNetworkSpawnItem.RequestSceneUnLoad(res.LoadedNetID);
            // else
            BasisNetworkSpawnItem.RequestGameObjectUnLoad(res.LoadedNetID);

            Basis.BasisRuntimeSpawnRegistry.Remove(url);
        }

        public void RemoveProp()
        {
            if (SelectedProp == null)
            {
                BasisDebug.LogError("No selected bundle.");
                return;
            }

            BasisMainMenu.Instance.OpenDialogue(
                "Basis VR",
                "Are you sure you want to remove this item from your list?",
                "Cancel",
                "Remove",
                value =>
                {
                    if (value) return; // your dialog uses "value==true means cancel" pattern
                    RemovePropItem(SelectedProp);
                });
        }

        public void RemovePropItem(PropMenuItem menuItem)
        {
            if (menuItem == null) return;

            var bundle = menuItem.Wrapper.LoadableBundle;
            if (bundle != null && RemoveAlsoUnloads)
            {
                string url = bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;

                if (Basis.BasisRuntimeSpawnRegistry.TryGet(url, out var res) && !string.IsNullOrEmpty(res.LoadedNetID))
                {
                    // bool isWorld = IsWorld(bundle);
                    //  if (isWorld) BasisNetworkSpawnItem.RequestSceneUnLoad(res.LoadedNetID);
                    // else
                    BasisNetworkSpawnItem.RequestGameObjectUnLoad(res.LoadedNetID);

                    Basis.BasisRuntimeSpawnRegistry.Remove(url);
                }
            }

            BasisDataStorePropKeys.PropKey key = new()
            {
                Pass = menuItem.Wrapper.LoadableBundle.UnlockPassword,
                Url = menuItem.Wrapper.LoadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation
            };

            MenuItems.Remove(menuItem);
            SelectionButtons.Remove(menuItem.Button);

            menuItem.Clear();
            _ = RemoveKey(key);

            ClearPropInfo();
        }

        public async Task RemoveKey(BasisDataStorePropKeys.PropKey key)
        {
            await BasisDataStorePropKeys.RemoveKey(key);
        }
    }
}
