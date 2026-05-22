using Basis.BasisUI;
using System.Collections.Generic;
using UnityEngine;

public static class SettingsProviderTrustedUrls
{
    public static void Populate(RectTransform container, string ownerTabKey)
    {
        PanelElementDescriptor infoGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        infoGroup.SetTitle(BasisLocalization.Get("settings.trustedUrls.title"));
        infoGroup.SetDescription(BasisLocalization.Get("settings.trustedUrls.description"));

        PanelElementDescriptor addGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        addGroup.SetTitle(BasisLocalization.Get("settings.trustedUrls.add.title"));
        addGroup.SetDescription(BasisLocalization.Get("settings.trustedUrls.add.description"));

        PanelTextField addField = PanelTextField.CreateNewEntry(addGroup.ContentParent);
        addField.Descriptor.SetTitle(BasisLocalization.Get("settings.trustedUrls.add.field"));

        PanelButton addButton = PanelButton.CreateNew(addGroup.ContentParent);
        addButton.Descriptor.SetTitle(BasisLocalization.Get("settings.trustedUrls.add.button"));
        addButton.OnClicked += () =>
        {
            string candidate = addField.Value?.Trim();
            if (string.IsNullOrEmpty(candidate)) return;
            if (!candidate.StartsWith("https://"))
            {
                BasisMainMenu.Instance.OpenDialogue(
                    BasisLocalization.Get("settings.trustedUrls.add.invalid.title"),
                    BasisLocalization.Get("settings.trustedUrls.add.invalid.https"),
                    BasisLocalization.Get("ui.ok"),
                    _ => { });
                return;
            }
            BasisTrustedUrls.Add(candidate);
            BasisMainMenu.Close();
            SettingsProvider.OpenToTab(ownerTabKey);
        };

        List<string> urls = BasisTrustedUrls.GetAll();

        if (urls.Count == 0)
        {
            PanelTextField emptyField = PanelTextField.CreateNew(infoGroup.ContentParent);
            emptyField.Descriptor.SetTitle(BasisLocalization.Get("settings.trustedUrls.empty"));
            emptyField.SetValue(BasisLocalization.Get("settings.trustedUrls.empty.description"));
            return;
        }

        PanelTextField countField = PanelTextField.CreateNew(infoGroup.ContentParent);
        countField.Descriptor.SetTitle(BasisLocalization.Get("settings.trustedUrls.count.title"));
        countField.SetValue(BasisLocalization.Get("settings.trustedUrls.count", urls.Count));

        PanelButton clearAllButton = PanelButton.CreateNew(container);
        clearAllButton.Descriptor.SetTitle(BasisLocalization.Get("settings.trustedUrls.clearAll"));
        clearAllButton.Descriptor.SetDescription(BasisLocalization.Get("settings.trustedUrls.clearAll.description"));
        clearAllButton.OnClicked += () =>
        {
            BasisMainMenu.Instance.OpenDialogue(
                BasisLocalization.Get("settings.trustedUrls.clearAll"),
                BasisLocalization.Get("settings.trustedUrls.clearAll.confirm", urls.Count),
                BasisLocalization.Get("settings.storage.clearAll.button"),
                BasisLocalization.Get("ui.cancel"),
                value =>
                {
                    if (!value) return;
                    BasisTrustedUrls.ClearAll();
                    BasisMainMenu.Close();
                    SettingsProvider.OpenToTab(ownerTabKey);
                });
        };

        PanelElementDescriptor urlGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        urlGroup.SetTitle(BasisLocalization.Get("settings.trustedUrls.list.title"));
        urlGroup.SetDescription(BasisLocalization.Get("settings.trustedUrls.list.description"));

        string removeHint = BasisLocalization.Get("settings.trustedUrls.clickToRemove");
        foreach (string url in urls)
        {
            string capturedUrl = url;
            PanelButton urlButton = PanelButton.CreateNew(urlGroup.ContentParent);
            urlButton.Descriptor.DisableRichText();
            urlButton.Descriptor.SetTitle(capturedUrl);
            urlButton.Descriptor.SetDescription(removeHint);
            urlButton.OnClicked += () =>
            {
                BasisMainMenu.Instance.OpenDialogue(
                    BasisLocalization.Get("settings.trustedUrls.remove.title"),
                    BasisLocalization.Get("settings.trustedUrls.remove.confirm", capturedUrl),
                    BasisLocalization.Get("library.remove"),
                    BasisLocalization.Get("ui.cancel"),
                    value =>
                    {
                        if (!value) return;
                        BasisTrustedUrls.Remove(capturedUrl);
                        BasisMainMenu.Close();
                        SettingsProvider.OpenToTab(ownerTabKey);
                    });
            };
        }
    }

    public static void Reset()
    {
        BasisTrustedUrls.Reset();
    }
}
