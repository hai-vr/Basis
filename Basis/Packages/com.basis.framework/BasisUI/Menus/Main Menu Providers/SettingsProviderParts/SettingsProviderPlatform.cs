using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using UnityEngine;

public static class SettingsProviderPlatform
{
    public static PanelTabPage PlatformTab(PanelTabGroup tabGroup)
    {
        PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
        PanelElementDescriptor descriptor = tab.Descriptor;
        descriptor.SetIcon(AddressableAssets.Sprites.Settings);
        descriptor.SetTitle("Platform");

        RectTransform container = descriptor.ContentParent;

        // Current mode info
        PanelElementDescriptor infoGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        infoGroup.SetTitle("Current Mode");
        infoGroup.SetDescription("The active device mode for this session.");

        PanelPasswordField currentMode = PanelPasswordField.CreateNew(infoGroup.ContentParent);
        currentMode.Descriptor.SetTitle("Active Mode");
        currentMode.SetPassword(BasisDeviceManagement.StaticCurrentMode ?? "Unknown");

        // Switch mode group
        PanelElementDescriptor switchGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        switchGroup.SetTitle("Switch Mode");
        switchGroup.SetDescription("Change the active device mode. This will reload the session.");

        PanelButton openVrButton = PanelButton.CreateNew(switchGroup.ContentParent);
        openVrButton.Descriptor.SetTitle("Switch To OpenVR");
        openVrButton.Descriptor.SetDescription("Use SteamVR / OpenVR runtime.");
        openVrButton.OnClicked += () =>
        {
            BasisMainMenu.Instance.OpenDialogue("Switch To OpenVR",
                "Are you sure you want to swap to OpenVR?",
                "Switch To OpenVR",
                "Cancel",
                async value =>
                {
                    if (!value) return;
                    await BasisDeviceManagement.Instance.SwitchSetMode(BasisConstants.OpenVRLoader);
                });
        };

        PanelButton openXrButton = PanelButton.CreateNew(switchGroup.ContentParent);
        openXrButton.Descriptor.SetTitle("Switch To OpenXR");
        openXrButton.Descriptor.SetDescription("Use OpenXR runtime.");
        openXrButton.OnClicked += () =>
        {
            BasisMainMenu.Instance.OpenDialogue("Switch To OpenXR",
                "Are you sure you want to swap to OpenXR?",
                "Switch To OpenXR",
                "Cancel",
                async value =>
                {
                    if (!value) return;
                    await BasisDeviceManagement.Instance.SwitchSetMode(BasisConstants.OpenXRLoader);
                });
        };

        PanelButton desktopButton = PanelButton.CreateNew(switchGroup.ContentParent);
        desktopButton.Descriptor.SetTitle("Switch To Desktop");
        desktopButton.Descriptor.SetDescription("Use Desktop mode (no VR).");
        desktopButton.OnClicked += () =>
        {
            BasisMainMenu.Instance.OpenDialogue("Switch To Desktop",
                "Are you sure you want to swap to Desktop?",
                "Switch To Desktop",
                "Cancel",
                async value =>
                {
                    if (!value) return;
                    await BasisDeviceManagement.Instance.SwitchSetMode(BasisConstants.Desktop);
                });
        };

        descriptor.ForceRebuild();
        return tab;
    }
}
