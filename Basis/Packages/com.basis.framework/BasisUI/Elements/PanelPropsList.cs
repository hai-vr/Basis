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

                // Default persistent for preloaded: false unless you store it elsewhere.
                BasisDataStorePropKeys.PropKey key = new()
                {
                    Pass = loadableBundle.UnlockPassword,
                    Url = loadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation,
                    Persistent = false
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

            int count = activeKeys.Count;
            for (int Index = 0; Index < count; Index++)
            {
                BasisDataStorePropKeys.PropKey key = activeKeys[Index];

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
    /// Supports MULTIPLE instances per URL + per-entry persistent.
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

            // New: persistent default for this entry (saved in PropKeyStore.json)
            public bool DefaultPersistent;

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
                    BasisLoadHandler.RemoveDiscInfo(Wrapper.LoadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation);
                    return;
                }

                Button.Descriptor.SetTitle(title);
            }
        }

        // (renamed) PreLoaded props
        public List<BasisLoadableBundle> PreLoadedProps = new();

        [Header("Spawn/World Behaviour")]
        [Tooltip("If true, clicking Load will unload ALL instances if any are loaded; if false, Load always spawns a new instance.")]
        public bool ToggleLoadUnload = true;

        [Tooltip("If true, clicking 'Remove Prop' (remove from list) will also unload ALL instances currently loaded for this URL.")]
        public bool RemoveAlsoUnloads = true;

        [Tooltip("Legacy global default persistent if you do not use per-entry persistence.")]
        public bool Persistent = false;

        [Header("Optional Per-Entry Persistent UI")]
        [Tooltip("Optional UI toggle to edit per-entry Persistent setting. If not assigned, the global 'Persistent' bool is used.")]
        public Toggle PersistentToggle;

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

        public PanelButton RemovePropButton; // Removes from saved list
        public PanelButton LoadPropButton;   // Spawn / Unload-all toggle

        [Header("Optional Instance Controls")]
        [Tooltip("Optional: unload ONLY the most recently spawned instance for the selected URL.")]
        public PanelButton UnloadLastButton;

        [Tooltip("Optional: unload ALL spawned instances for the selected URL.")]
        public PanelButton UnloadAllButton;

        public List<PropMenuItem> MenuItems = new();

        public override void OnCreateEvent()
        {
            base.OnCreateEvent();

            ClearPropInfo();

            NewPropButton.OnClicked += NewPropPanel.Show;

            // Main action: spawn or unload-all depending on ToggleLoadUnload + existing instances
            LoadPropButton.OnClicked += () => _ = LoadOrUnloadSelected();

            // Remove-from-list behaviour
            RemovePropButton.OnClicked += RemoveProp;

            if (UnloadLastButton != null)
                UnloadLastButton.OnClicked += UnloadLastInstanceSelected;

            if (UnloadAllButton != null)
                UnloadAllButton.OnClicked += UnloadAllInstancesSelected;

            if (PersistentToggle != null)
                PersistentToggle.onValueChanged.AddListener(OnPersistentToggleChanged);

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

            if (PersistentToggle != null)
                PersistentToggle.onValueChanged.RemoveListener(OnPersistentToggleChanged);
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

        private bool TryGetStoredKeyForUrlPass(string url, string pass, out BasisDataStorePropKeys.PropKey key)
        {
            key = null;
            var keys = BasisDataStorePropKeys.DisplayKeys();
            if (keys == null) return false;

            for (int i = 0; i < keys.Length; i++)
            {
                var cur = keys[i];
                if (cur != null && cur.Url == url && cur.Pass == pass)
                {
                    key = cur;
                    return true;
                }
            }

            return false;
        }

        private async Task CreateButtons()
        {
            foreach (PanelButton button in SelectionButtons) button.ReleaseInstance();
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

                var url = bundle?.BasisRemoteBundleEncrypted?.RemoteBeeFileLocation ?? string.Empty;
                var pass = bundle?.UnlockPassword ?? string.Empty;

                bool entryPersistent = false;
                if (!string.IsNullOrWhiteSpace(url) && TryGetStoredKeyForUrlPass(url, pass, out var storedKey))
                    entryPersistent = storedKey.Persistent;

                PropMenuItem item = new()
                {
                    Button = button,
                    Wrapper = wrapper,
                    DefaultPersistent = entryPersistent
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

            BasisTrackedBundleWrapper wrapper = new() { LoadableBundle = bundle };

            // Pull persistent from stored key (it was saved in AddNewKey)
            bool entryPersistent = false;
            var url = bundle?.BasisRemoteBundleEncrypted?.RemoteBeeFileLocation ?? string.Empty;
            var pass = bundle?.UnlockPassword ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(url) && TryGetStoredKeyForUrlPass(url, pass, out var storedKey))
                entryPersistent = storedKey.Persistent;

            PropMenuItem item = new()
            {
                Button = button,
                Wrapper = wrapper,
                DefaultPersistent = entryPersistent
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

            if (PersistentToggle != null)
                PersistentToggle.SetIsOnWithoutNotify(Persistent);

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

            // Per-entry persistent UI (optional)
            if (PersistentToggle != null)
                PersistentToggle.SetIsOnWithoutNotify(item.DefaultPersistent);

            string creationDate = bundle.BasisBundleConnector.DateOfCreation;
            if (string.IsNullOrEmpty(creationDate))
            {
                creationDate = string.Empty;
            }
            else
            {
                creationDate = DateTime
                    .Parse(creationDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
                    .ToString(CultureInfo.InvariantCulture);
                creationDate += " UTC";
            }

            CreationDateLabel.text = creationDate;

            string[] platforms = bundle.BasisBundleConnector.BasisBundleGenerated
                .Select(pair => pair.Platform)
                .ToArray();

            WindowsIcon.SetActive(false);
            MacIcon.SetActive(false);
            LinuxIcon.SetActive(false);
            AndroidIcon.SetActive(false);
            IOSIcon.SetActive(false);

            foreach (string platform in platforms)
            {
                switch (platform)
                {
                    case "StandaloneWindows64": WindowsIcon.SetActive(true); break;
                    case "StandaloneOSX": MacIcon.SetActive(true); break;
                    case "StandaloneLinux64": LinuxIcon.SetActive(true); break;
                    case "Android": AndroidIcon.SetActive(true); break;
                    case "iOS": IOSIcon.SetActive(true); break;
                }
            }

            NewPropPanel.Hide();
            RefreshLoadButtonLabel();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }

        private void OnPersistentToggleChanged(bool newValue)
        {
            // Update selected entry persistent + save it back to the key store.
            if (SelectedProp == null) return;

            SelectedProp.DefaultPersistent = newValue;

            var bundle = SelectedProp.Wrapper?.LoadableBundle;
            if (bundle == null) return;

            string url = bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;
            string pass = bundle.UnlockPassword;
            if (string.IsNullOrWhiteSpace(url)) return;

            _ = SavePersistentForKey(url, pass, newValue);
        }

        private async Task SavePersistentForKey(string url, string pass, bool persistent)
        {
            // Update the key in the keystore (array-based): remove old then add updated.
            var keys = BasisDataStorePropKeys.DisplayKeys();
            if (keys == null) return;

            BasisDataStorePropKeys.PropKey existing = null;
            for (int i = 0; i < keys.Length; i++)
            {
                var cur = keys[i];
                if (cur != null && cur.Url == url && cur.Pass == pass)
                {
                    existing = cur;
                    break;
                }
            }

            if (existing == null) return;

            // Replace by remove+add (simple and safe with your current store design).
            await BasisDataStorePropKeys.RemoveKey(existing);

            existing.Persistent = persistent;
            await BasisDataStorePropKeys.AddNewKey(existing);
        }

        // --------------------------------------------------------------------
        // Spawn helpers
        // --------------------------------------------------------------------
        private Vector3 GetSpawnPosition()
        {
            if (UseCustomSpawnPosition) return CustomSpawnPosition;
            if (BasisLocalPlayer.Instance != null) return BasisLocalPlayer.Instance.transform.position;
            if (Camera.main != null) return Camera.main.transform.position;
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

        private bool GetSpawnPersistent()
        {
            // If we have a persistent toggle UI: per-entry uses SelectedProp.DefaultPersistent.
            if (PersistentToggle != null && SelectedProp != null)
                return SelectedProp.DefaultPersistent;

            // Otherwise fallback to old global bool.
            return Persistent;
        }

        private void RefreshLoadButtonLabel()
        {
            if (LoadPropButton == null) return;

            string url = SelectedUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                LoadPropButton.Descriptor.SetTitle("Spawn");
                return;
            }

            int instanceCount = Basis.BasisRuntimeSpawnRegistry.Count(url);

            if (ToggleLoadUnload && instanceCount > 0)
            {
                LoadPropButton.Descriptor.SetTitle($"Unload All ({instanceCount})");
            }
            else
            {
                LoadPropButton.Descriptor.SetTitle("Spawn (+1)");
            }
        }

        // --------------------------------------------------------------------
        // UI actions: Spawn / Unload / Remove
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

            // Toggle means: if anything is loaded, unload ALL; otherwise spawn a new instance.
            if (ToggleLoadUnload && Basis.BasisRuntimeSpawnRegistry.HasAny(url))
            {
                UnloadAllInstancesSelected();
                RefreshLoadButtonLabel();
                return;
            }

            await SpawnSelectedNewInstance();
            RefreshLoadButtonLabel();
        }

        /// <summary>
        /// Always spawns a NEW instance, even if already spawned before.
        /// </summary>
        public async Task SpawnSelectedNewInstance()
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

            Vector3 spawnPos = GetSpawnPosition();
            Quaternion spawnRot = GetSpawnRotation();
            Vector3 spawnScale = GetSpawnScale();

            bool persistent = GetSpawnPersistent();

            BasisNetworkSpawnItem.RequestGameObjectLoad(
                pass, url,
                spawnPos, spawnRot, spawnScale,
                persistent,
                ApplyCustomScale,
                out LocalLoadResource loadedProp
            );

            // Store as a new instance (1-to-many)
            Basis.BasisRuntimeSpawnRegistry.Add(url, loadedProp.LoadedNetID, persistent, out _);

            await Task.CompletedTask;
        }
        /// <summary>
        /// Unloads ONLY the most recently spawned instance for the selected URL.
        /// </summary>
        public void UnloadLastInstanceSelected()
        {
            string url = SelectedUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                BasisDebug.LogError("No selected bundle URL.");
                return;
            }

            //  if (!Basis.BasisRuntimeSpawnRegistry.RemoveInstance(url, out var removed))
            //{
            //     BasisDebug.Log("Nothing loaded for this item.");
            //    RefreshLoadButtonLabel();
            //     return;
            // }

            //  if (!string.IsNullOrEmpty(removed.LoadedNetID))
            //     BasisNetworkSpawnItem.RequestGameObjectUnLoad(removed.LoadedNetID);

            RefreshLoadButtonLabel();
        }
        /// <summary>
        /// Unloads ALL spawned instances for the selected URL.
        /// </summary>
        public void UnloadAllInstancesSelected()
        {
            string url = SelectedUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                BasisDebug.LogError("No selected bundle URL.");
                return;
            }

            var instances = Basis.BasisRuntimeSpawnRegistry.GetInstances(url);
            if (instances == null || instances.Count == 0)
            {
                BasisDebug.Log("Nothing loaded for this item.");
                RefreshLoadButtonLabel();
                return;
            }

            for (int i = instances.Count - 1; i >= 0; i--)
            {
                var inst = instances[i];
                if (inst != null && !string.IsNullOrEmpty(inst.LoadedNetID))
                    BasisNetworkSpawnItem.RequestGameObjectUnLoad(inst.LoadedNetID);
            }

            Basis.BasisRuntimeSpawnRegistry.ClearAll(url);
            RefreshLoadButtonLabel();
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
                }
            );
        }

        public void RemovePropItem(PropMenuItem menuItem)
        {
            if (menuItem == null) return;

            var bundle = menuItem.Wrapper.LoadableBundle;

            if (bundle != null && RemoveAlsoUnloads)
            {
                string url = bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;

                // Unload ALL instances for this URL
                var instances = Basis.BasisRuntimeSpawnRegistry.GetInstances(url);
                for (int i = instances.Count - 1; i >= 0; i--)
                {
                    var inst = instances[i];
                    if (inst != null && !string.IsNullOrEmpty(inst.LoadedNetID))
                        BasisNetworkSpawnItem.RequestGameObjectUnLoad(inst.LoadedNetID);
                }
                Basis.BasisRuntimeSpawnRegistry.ClearAll(url);
            }

            // Remove saved key
            BasisDataStorePropKeys.PropKey key = new()
            {
                Pass = menuItem.Wrapper.LoadableBundle.UnlockPassword,
                Url = menuItem.Wrapper.LoadableBundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation,
                Persistent = menuItem.DefaultPersistent
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
