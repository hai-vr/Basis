using Basis.BasisUI;
using System;
using System.Collections.Generic;
using UnityEngine;

public static class SettingsProviderTrustedUrls
{
    public static void Populate(RectTransform container, string ownerTabKey, PanelElementDescriptor tabDescriptor = null)
    {
        PanelSectionToggle trustedToggle = PanelSectionToggle.CreateNewEntry(container);
        trustedToggle.SetTitle(BasisLocalization.Get("settings.trustedUrls.title"));
        int trustedStart = container.childCount;
        RectTransform content = container;

        PanelElementDescriptor infoGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, content);
        infoGroup.SetDescription(BasisLocalization.Get("settings.trustedUrls.description"));

        PanelElementDescriptor addGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, content);
        addGroup.SetTitle(BasisLocalization.Get("settings.trustedUrls.add.title"));
        addGroup.SetDescription(BasisLocalization.Get("settings.trustedUrls.add.description"));

        PanelTextField addField = PanelTextField.CreateNewEntry(addGroup.ContentParent);
        addField.Descriptor.SetTitle(BasisLocalization.Get("settings.trustedUrls.add.field"));
        addField.Descriptor.SetTooltip(BasisLocalization.Get("settings.trustedUrls.add.field.tooltip"));
        addField.SetValidator(text =>
        {
            string trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return BasisLocalization.Get("ui.validation.required");
            }
            return trimmed.StartsWith("https://")
                ? null
                : BasisLocalization.Get("settings.trustedUrls.add.invalid.https");
        }, gradeImmediately: false);

        PanelButton addButton = PanelButton.CreateNew(addGroup.ContentParent);
        addButton.Descriptor.SetTitle(BasisLocalization.Get("settings.trustedUrls.add.button"));
        addButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.trustedUrls.add.button.tooltip"));
        addButton.OnClicked += () =>
        {
            if (!addField.Validate()) return;

            string candidate = addField.Value?.Trim();
            BasisTrustedUrls.Add(candidate);
            BasisMainMenu.Close();
            SettingsProvider.OpenToTab(ownerTabKey);
        };

        PopulateAddedByYou(content, ownerTabKey);
        PopulateBuiltIn(content);

        PanelSectionToggleHelpers.FinalizeFlatSectionFromIndex(trustedToggle, container, trustedStart, false,
            _ => tabDescriptor?.ForceRebuild());
    }

    private static void PopulateAddedByYou(RectTransform container, string ownerTabKey)
    {
        List<string> urls = BasisTrustedUrls.GetUserAdded();
        urls.Sort(StringComparer.OrdinalIgnoreCase);

        PanelElementDescriptor addedGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        addedGroup.SetTitle(BasisLocalization.Get("settings.trustedUrls.added.title"));

        if (urls.Count == 0)
        {
            addedGroup.SetDescription(BasisLocalization.Get("settings.trustedUrls.empty.description"));
            return;
        }

        addedGroup.SetDescription(BasisLocalization.Get("settings.trustedUrls.count", urls.Count));

        string removeHint = BasisLocalization.Get("settings.trustedUrls.clickToRemove");
        foreach (string url in urls)
        {
            string capturedUrl = url;
            PanelButton urlButton = PanelButton.CreateNew(addedGroup.ContentParent);
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

        PanelButton clearAllButton = PanelButton.CreateNew(addedGroup.ContentParent);
        clearAllButton.Descriptor.SetTitle(BasisLocalization.Get("settings.trustedUrls.clearAll"));
        clearAllButton.Descriptor.SetTooltip(BasisLocalization.Get("settings.trustedUrls.clearAll.tooltip"));
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
    }

    private static void PopulateBuiltIn(RectTransform container)
    {
        List<string> urls = BasisTrustedUrls.GetBuiltIn();
        if (urls.Count == 0) return;
        urls.Sort(StringComparer.OrdinalIgnoreCase);

        PanelElementDescriptor builtInGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        builtInGroup.SetTitle(BasisLocalization.Get("settings.trustedUrls.builtIn.title"));
        builtInGroup.SetDescription(BasisLocalization.Get("settings.trustedUrls.count", urls.Count));

        string lockedReason = BasisLocalization.Get("settings.trustedUrls.builtIn.locked");
        foreach (string url in urls)
        {
            PanelButton urlButton = PanelButton.CreateNew(builtInGroup.ContentParent);
            urlButton.Descriptor.DisableRichText();
            urlButton.Descriptor.SetTitle(url);
            urlButton.SetInteractable(false, lockedReason);
        }
    }

    public static void Reset()
    {
        BasisTrustedUrls.Reset();
    }
}
