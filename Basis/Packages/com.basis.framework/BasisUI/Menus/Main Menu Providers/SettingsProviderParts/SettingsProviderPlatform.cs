using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using System.Collections.Generic;
using UnityEngine;

public static class SettingsProviderPlatform
{
    /// <summary>
    /// All user-facing device modes to probe against registered BaseTypes.
    /// </summary>
    private static readonly string[] AllModes = new string[]
    {
        BasisConstants.Desktop,
        BasisConstants.OpenVRLoader,
        BasisConstants.OpenXRLoader,
    };

    public static void BuildDeviceModeUI(RectTransform container)
    {
        string currentMode = BasisDeviceManagement.StaticCurrentMode ?? BasisConstants.None;

        // Current mode info
        PanelElementDescriptor infoGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        infoGroup.SetTitle("Device Mode");
        infoGroup.SetDescription("The active device mode for this session.");

        PanelPasswordField currentModeField = PanelPasswordField.CreateNew(infoGroup.ContentParent);
        currentModeField.Descriptor.SetTitle("Active Mode");
        currentModeField.SetPassword(currentMode);

        // Discover available modes from registered BaseTypes
        List<string> availableModes = new List<string>();
        BasisDeviceManagement dm = BasisDeviceManagement.Instance;
        if (dm != null)
        {
            foreach (string mode in AllModes)
            {
                if (dm.TryFindBasisBaseTypeManagement(mode, out _, OnlyFinding: true))
                {
                    availableModes.Add(mode);
                }
            }
        }

        if (availableModes.Count > 0)
        {
            foreach (string mode in availableModes)
            {
                string capturedMode = mode;
                string displayName = GetModeDisplayName(capturedMode);
                bool isActive = string.Equals(currentMode, capturedMode, System.StringComparison.Ordinal);
                string suffix = isActive ? " [ACTIVE]" : "";

                PanelButton modeButton = PanelButton.CreateNew(infoGroup.ContentParent);
                modeButton.Descriptor.SetTitle($"Switch To {displayName}{suffix}");
                modeButton.Descriptor.SetDescription(GetModeDescription(capturedMode));
                modeButton.OnClicked += () =>
                {
                    if (isActive) return;
                    BasisMainMenu.Instance.OpenDialogue($"Switch To {displayName}",
                        $"Are you sure you want to switch to {displayName}?",
                        $"Switch To {displayName}",
                        "Cancel",
                        async value =>
                        {
                            if (!value) return;
                            await BasisDeviceManagement.Instance.SwitchSetMode(capturedMode);
                        });
                };
            }
        }
    }

    private static string GetModeDisplayName(string mode)
    {
        if (mode == BasisConstants.OpenVRLoader) return "OpenVR (SteamVR)";
        if (mode == BasisConstants.OpenXRLoader) return "OpenXR";
        return mode;
    }

    private static string GetModeDescription(string mode)
    {
        if (mode == BasisConstants.Desktop) return "Desktop mode (no VR). Shortcut: F9";
        if (mode == BasisConstants.OpenVRLoader) return "SteamVR / OpenVR runtime. Shortcut: F11";
        if (mode == BasisConstants.OpenXRLoader) return "OpenXR runtime. Shortcut: F10";
        return mode;
    }
}
