using Basis.BasisUI;
using System.Collections.Generic;
using UnityEngine;

public static class SettingsProviderStorage
{
    public static PanelTabPage StorageTab(PanelTabGroup tabGroup)
    {
        PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
        PanelElementDescriptor descriptor = tab.Descriptor;
        descriptor.SetIcon(AddressableAssets.Sprites.Settings);
        descriptor.SetTitle("Downloads & Cache");

        RectTransform container = descriptor.ContentParent;

        // Download limits
        PanelElementDescriptor downloadGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        downloadGroup.SetTitle("Download Limits");
        downloadGroup.SetDescription("Configure download size and concurrency limits.");

        PanelSlider avatarDownloadSize = PanelSlider.CreateEntryAndBind(
            downloadGroup.ContentParent,
            PanelSlider.SliderSettings.Advanced("Avatar Download Size", 5, 1024, false, 0, ValueDisplayMode.MemorySize),
            BasisSettingsDefaults.AvatarDownloadSize);

        // Concurrency gates for avatar loading. Three separate gates because the
        // network / disc / in-memory paths each have a different bottleneck. Tuning
        // these higher helps crowded rooms catch up faster; tuning downloads too high
        // just splits bandwidth and makes everyone wait longer on the loading avatar.
        PanelSlider maxDownloads = PanelSlider.CreateEntryAndBind(
            downloadGroup.ContentParent,
            PanelSlider.SliderSettings.Advanced("Max Concurrent Downloads", 1, 32, true, 0, ValueDisplayMode.Raw),
            BasisSettingsDefaults.MaxConcurrentAvatarDownloads);

        PanelSlider maxDiscLoads = PanelSlider.CreateEntryAndBind(
            downloadGroup.ContentParent,
            PanelSlider.SliderSettings.Advanced("Max Concurrent Disc Loads", 1, 64, true, 0, ValueDisplayMode.Raw),
            BasisSettingsDefaults.MaxConcurrentAvatarDiscLoads);

        PanelSlider maxAddressables = PanelSlider.CreateEntryAndBind(
            downloadGroup.ContentParent,
            PanelSlider.SliderSettings.Advanced("Max Concurrent Addressables", 1, 128, true, 0, ValueDisplayMode.Raw),
            BasisSettingsDefaults.MaxConcurrentAvatarAddressables);

        // Cache size limit slider (lightweight, no file I/O)
        PanelElementDescriptor limitGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        limitGroup.SetTitle("Cache Settings");
        limitGroup.SetDescription("Set the maximum disk space for cached BEE (basis encrypted extension) files.");

        PanelSlider cacheSizeSlider = PanelSlider.CreateEntryAndBind(
            limitGroup.ContentParent,
            PanelSlider.SliderSettings.Advanced("Max Cache Size (GB)", 1, 512, true, 0, ValueDisplayMode.Raw),
            BasisSettingsDefaults.CacheMaxSizeGB);

        // Button to load and display all storage data on demand
        PanelButton loadDataButton = PanelButton.CreateNew(container);
        loadDataButton.Descriptor.SetTitle("Load Storage Data");
        loadDataButton.Descriptor.SetDescription("Scan disk for cached BEE (basis encrypted extension) files. This may take a moment.");
        loadDataButton.OnClicked += () =>
        {
            // Remove the load button itself
            Object.Destroy(loadDataButton.gameObject);

            PopulateStorageData(container);
            descriptor.ForceRebuild();
        };

        // One reset button for this whole page
        SettingsProvider.AddResetPageButton(container, "Storage Settings", ResetStorageDefaults);

        descriptor.ForceRebuild();
        return tab;
    }

    private static void PopulateStorageData(RectTransform container)
    {
        long totalBytes = BasisStorageManagement.GetTotalCacheSizeBytes();
        string sizeText = BasisStorageManagement.FormatBytes(totalBytes);
        string limitText = BasisStorageManagement.FormatBytes(BasisStorageManagement.MaxCacheSizeBytes);

        List<BasisStorageManagement.StoredBeeFileInfo> storedFiles = BasisStorageManagement.GetAllStoredFiles();

        // Cache size info group
        PanelElementDescriptor infoGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        infoGroup.SetTitle("Cache Info");

        PanelPasswordField cacheInfoField = PanelPasswordField.CreateNew(infoGroup.ContentParent);
        cacheInfoField.Descriptor.SetTitle("Total Cache Size");
        cacheInfoField.SetPassword($"{sizeText} / {limitText}");

        PanelPasswordField fileCountField = PanelPasswordField.CreateNew(infoGroup.ContentParent);
        fileCountField.Descriptor.SetTitle("Stored Files");
        fileCountField.SetPassword($"{storedFiles.Count} files");

        // Clear all cache button
        PanelButton clearAllButton = PanelButton.CreateNew(container);
        clearAllButton.Descriptor.SetTitle("Clear All Cache");
        clearAllButton.Descriptor.SetDescription("Delete all downloaded BEE (basis encrypted extension) files from disk.");
        clearAllButton.OnClicked += () =>
        {
            BasisMainMenu.Instance.OpenDialogue(
                "Clear All Cache",
                $"Delete all {storedFiles.Count} cached files ({sizeText})? This cannot be undone.",
                "Clear All",
                "Cancel",
                value =>
                {
                    if (!value) return;
                    BasisStorageManagement.ClearAllCache();
                    BasisMainMenu.Close();
                    BasisMainMenu.OpenWithProvider(SettingsProvider.StaticTitle);
                });
        };

        // Individual file list group
        if (storedFiles.Count > 0)
        {
            PanelElementDescriptor filesGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            filesGroup.SetTitle("Stored BEE (basis encrypted extension) Files");
            filesGroup.SetDescription("Individual cached files. Click to delete.");

            foreach (var file in storedFiles)
            {
                string fileName = file.UniqueVersion;
                string size = BasisStorageManagement.FormatBytes(file.FileSizeBytes);
                string loadedStatus = file.IsLoadedInMemory ? " [IN USE]" : "";
                string remoteUrl = file.RemoteUrl;

                PanelButton fileButton = PanelButton.CreateNew(filesGroup.ContentParent);
                fileButton.Descriptor.SetTitle($"{fileName} ({size}){loadedStatus}");
                fileButton.Descriptor.SetDescription(remoteUrl);
                fileButton.OnClicked += () =>
                {
                    BasisMainMenu.Instance.OpenDialogue(
                        "Delete File",
                        $"Delete cached file '{fileName}' ({size})?{(file.IsLoadedInMemory ? "\n\nWarning: This file is currently in use. Deleting it will unload the asset." : "")}",
                        "Delete",
                        "Cancel",
                        value =>
                        {
                            if (!value) return;
                            BasisStorageManagement.DeleteStoredFile(remoteUrl);
                            BasisMainMenu.Close();
                            BasisMainMenu.OpenWithProvider(SettingsProvider.StaticTitle);
                        });
                };
            }
        }
    }

    private static void ResetStorageDefaults()
    {
        BasisSettingsDefaults.AvatarDownloadSize.ResetToDefault();
        BasisSettingsDefaults.CacheMaxSizeGB.ResetToDefault();
        BasisSettingsDefaults.MaxConcurrentAvatarDownloads.ResetToDefault();
        BasisSettingsDefaults.MaxConcurrentAvatarDiscLoads.ResetToDefault();
        BasisSettingsDefaults.MaxConcurrentAvatarAddressables.ResetToDefault();
    }
}
