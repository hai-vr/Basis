using System.Collections.Generic;
using System.IO;
using Basis.BasisUI;
using Basis.Scripts.Networking;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Developer-tab "Backup &amp; Restore" section. Creating an archive is offered everywhere; restoring
/// is Windows/Linux only (<see cref="BasisUserDataBackup.RestoreSupported"/>) and lists the archives
/// found in the backups folder, plus a field for a path copied in from elsewhere.
/// </summary>
public static class SettingsProviderBackup
{
    private static bool _busy;

    public static void BuildSection(RectTransform container, PanelElementDescriptor tabDescriptor)
    {
        PanelElementDescriptor createGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        createGroup.SetTitle(BasisLocalization.Get("settings.developer.backup.create.title"));
        createGroup.SetDescription(BasisLocalization.Get("settings.developer.backup.create.description"));

        RectTransform createParent = createGroup.ContentParent;

        PanelToggle includeIdentity = PanelToggle.CreateNewEntry(createParent);
        includeIdentity.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.includeIdentity"));
        includeIdentity.Descriptor.SetTooltip(BasisLocalization.Get("settings.developer.backup.includeIdentity.tooltip"));
        includeIdentity.SetValueWithoutNotify(true);

        PanelToggle includeCache = PanelToggle.CreateNewEntry(createParent);
        includeCache.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.includeCache"));
        includeCache.Descriptor.SetTooltip(BasisLocalization.Get("settings.developer.backup.includeCache.tooltip"));
        includeCache.SetValueWithoutNotify(false);

        PanelButton createButton = PanelButton.CreateNew(createParent);
        createButton.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.create"));
        createButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.developer.backup.create.tooltip"));

        PanelButton revealButton = PanelButton.CreateNew(createParent);
        revealButton.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.openFolder"));
        revealButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.developer.backup.openFolder.tooltip"));
        revealButton.OnClicked += RevealBackupsFolder;

        if (!BasisUserDataBackup.RestoreSupported)
        {
            createButton.OnClicked += () => CreateBackup(createButton, includeCache.Value, includeIdentity.Value, null);

            PanelElementDescriptor unsupported =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            unsupported.SetTitle(BasisLocalization.Get("settings.developer.backup.restore.title"));
            unsupported.SetDescription(BasisLocalization.Get("settings.developer.backup.restore.unsupported"));
            return;
        }

        PanelElementDescriptor restoreGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        restoreGroup.SetTitle(BasisLocalization.Get("settings.developer.backup.restore.title"));
        restoreGroup.SetDescription(BasisLocalization.Get("settings.developer.backup.restore.description"));

        RectTransform restoreParent = restoreGroup.ContentParent;

        PanelToggle restoreIdentity = PanelToggle.CreateNewEntry(restoreParent);
        restoreIdentity.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.restoreIdentity"));
        restoreIdentity.Descriptor.SetTooltip(BasisLocalization.Get("settings.developer.backup.restoreIdentity.tooltip"));
        restoreIdentity.SetValueWithoutNotify(true);

        PanelTextField pathField = PanelTextField.CreateNewEntry(restoreParent);
        pathField.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.path"));
        pathField.Descriptor.SetTooltip(BasisLocalization.Get("settings.developer.backup.path.tooltip"));
        pathField.SetValueWithoutNotify(string.Empty);

        PanelButton pathRestoreButton = PanelButton.CreateNew(restoreParent);
        pathRestoreButton.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.restoreFromPath"));
        pathRestoreButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.developer.backup.restoreFromPath.tooltip"));

        PanelElementDescriptor listGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, restoreParent);
        listGroup.SetTitle(BasisLocalization.Get("settings.developer.backup.available"));

        void Refresh()
        {
            if (listGroup == null) return;
            PopulateArchiveList(listGroup, restoreIdentity);
            RebuildChain(listGroup, restoreGroup, tabDescriptor);
        }

        PanelButton refreshButton = PanelButton.CreateNew(restoreParent);
        refreshButton.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.refresh"));
        refreshButton.OnClicked += Refresh;

        createButton.OnClicked += () => CreateBackup(createButton, includeCache.Value, includeIdentity.Value, Refresh);
        pathRestoreButton.OnClicked += () =>
        {
            string path = ReadField(pathField).Trim().Trim('"');
            if (string.IsNullOrEmpty(path))
            {
                Notify(BasisLocalization.Get("settings.developer.backup.path.missing"));
                return;
            }
            ConfirmRestore(path, Path.GetFileName(path), restoreIdentity.Value);
        };

        PopulateArchiveList(listGroup, restoreIdentity);
    }

    private static void PopulateArchiveList(PanelElementDescriptor listGroup, PanelToggle restoreIdentity)
    {
        RectTransform parent = listGroup.ContentParent;
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            child.SetParent(null, false);
            Object.Destroy(child.gameObject);
        }

        List<BasisUserDataBackup.ArchiveInfo> archives = BasisUserDataBackup.ListArchives();

        if (archives.Count == 0)
        {
            PanelPasswordField empty = PanelPasswordField.CreateNew(parent);
            empty.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.available"));
            empty.SetPassword(BasisLocalization.Get("settings.developer.backup.none"));
            return;
        }

        foreach (BasisUserDataBackup.ArchiveInfo archive in archives)
        {
            PanelButton entry = PanelButton.CreateNew(parent);
            entry.Descriptor.SetTitle($"{archive.FileName}  ({BasisUserDataBackup.FormatBytes(archive.SizeBytes)})");
            entry.Descriptor.SetDescription(DescribeArchive(archive));

            string path = archive.Path;
            string name = archive.FileName;
            entry.OnClicked += () =>
                ConfirmRestore(path, name, restoreIdentity == null || restoreIdentity.Value);
        }
    }

    private static string DescribeArchive(BasisUserDataBackup.ArchiveInfo archive)
    {
        BasisUserDataBackup.Manifest manifest = BasisUserDataBackup.ReadManifest(archive.Path);
        if (manifest == null) return BasisLocalization.Get("settings.developer.backup.unreadable");

        string extras = string.Empty;
        if (manifest.IncludesIdentity) extras += "  " + BasisLocalization.Get("settings.developer.backup.tag.identity");
        if (manifest.IncludesCachedContent) extras += "  " + BasisLocalization.Get("settings.developer.backup.tag.cache");

        return BasisLocalization.Get(
            "settings.developer.backup.entry.summary",
            archive.WrittenLocal.ToString("g"),
            manifest.FileCount,
            manifest.PrefCount,
            manifest.AppVersion) + extras;
    }

    private static async void CreateBackup(
        PanelButton button, bool includeCache, bool includeIdentity, System.Action refresh)
    {
        if (_busy) return;
        _busy = true;

        if (button != null)
        {
            button.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.working"));
        }

        BasisUserDataBackup.BackupResult result;
        try
        {
            result = await BasisUserDataBackup.CreateAsync(includeCache, includeIdentity);
        }
        finally
        {
            _busy = false;
            if (button != null)
            {
                button.Descriptor.SetTitle(BasisLocalization.Get("settings.developer.backup.create"));
            }
        }

        if (!result.Success)
        {
            Notify(BasisLocalization.Get("settings.developer.backup.create.failed", result.Error));
            return;
        }

        refresh?.Invoke();

        Notify(BasisLocalization.Get(
            "settings.developer.backup.create.done",
            Path.GetFileName(result.ArchivePath),
            result.FileCount,
            result.PrefCount,
            BasisUserDataBackup.FormatBytes(result.ArchiveBytes)));
    }

    private static void ConfirmRestore(string archivePath, string displayName, bool restoreIdentity)
    {
        if (_busy) return;

        ShowDialogue(
            BasisLocalization.Get("settings.developer.backup.restore.title"),
            BasisLocalization.Get("settings.developer.backup.restore.confirm", displayName),
            BasisLocalization.Get("settings.developer.backup.restore.button"),
            BasisLocalization.Get("ui.cancel"),
            accepted =>
            {
                if (!accepted) return;
                RunRestore(archivePath, restoreIdentity);
            });
    }

    private static async void RunRestore(string archivePath, bool restoreIdentity)
    {
        if (_busy) return;
        _busy = true;

        BasisUserDataBackup.RestoreResult result;
        try
        {
            result = await BasisUserDataBackup.RestoreAsync(archivePath, restoreIdentity);
        }
        finally
        {
            _busy = false;
        }

        if (!result.Success)
        {
            Notify(BasisLocalization.Get("settings.developer.backup.restore.failed", result.Error));
            return;
        }

        string message = BasisLocalization.Get(
            "settings.developer.backup.restore.done", result.FileCount, result.PrefCount);

        if (BasisAppRelaunch.IsSupported)
        {
            ShowDialogue(
                BasisLocalization.Get("settings.developer.backup.restore.title"),
                message + "\n\n" + BasisLocalization.Get("settings.developer.backup.restart.prompt"),
                BasisLocalization.Get("settings.developer.backup.restart.now"),
                BasisLocalization.Get("settings.developer.backup.restart.later"),
                accepted =>
                {
                    if (accepted) BasisAppRelaunch.RebootAndReconnect();
                });
            return;
        }

        Notify(message + "\n\n" + BasisLocalization.Get("settings.developer.backup.restart.manual"));
    }

    private static void RevealBackupsFolder()
    {
        try
        {
            string folder = BasisUserDataBackup.BackupsFolder;
            Directory.CreateDirectory(folder);
            BasisFileBrowserUtility.Reveal(folder);
        }
        catch (System.Exception e)
        {
            BasisDebug.LogWarning("Could not open the backups folder: " + e.Message);
        }
    }

    private static string ReadField(PanelTextField field)
    {
        if (field == null || field._inputField == null) return string.Empty;
        return field._inputField.text ?? string.Empty;
    }

    private static void RebuildChain(
        PanelElementDescriptor inner, PanelElementDescriptor middle, PanelElementDescriptor tabDescriptor)
    {
        if (inner != null && inner.ContentParent != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(inner.ContentParent);
        }
        if (middle != null && middle.ContentParent != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(middle.ContentParent);
        }
        if (tabDescriptor != null && tabDescriptor.ContentParent != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(tabDescriptor.ContentParent);
        }
    }

    private static void Notify(string message)
    {
        ShowDialogue(
            BasisLocalization.Get("settings.developer.backup.title"),
            message,
            BasisLocalization.Get("ui.ok"),
            null,
            null);
    }

    private static void ShowDialogue(
        string title, string description, string accept, string deny, System.Action<bool> callback)
    {
        if (BasisMainMenu.Instance == null)
        {
            BasisDebug.Log(description);
            return;
        }

        if (BasisMainMenu.Instance.Dialogue)
        {
            BasisMainMenu.Instance.Dialogue.ReleaseInstance();
        }

        if (string.IsNullOrEmpty(deny))
        {
            BasisMainMenu.Instance.OpenDialogue(title, description, accept, value => callback?.Invoke(value));
            return;
        }

        BasisMainMenu.Instance.OpenDialogue(title, description, accept, deny, value => callback?.Invoke(value));
    }
}
