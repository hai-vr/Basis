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

        BasisDeviceManagement dm = BasisDeviceManagement.Instance;

#if BASIS_HAS_OPENVR || BASIS_HAS_OPENXR
        // Soft-swap status (only relevant when VR SDKs are compiled in)
        if (dm != null && dm.IsSoftSwapped)
        {
            PanelPasswordField softSwapField = PanelPasswordField.CreateNew(infoGroup.ContentParent);
            softSwapField.Descriptor.SetTitle("VR Runtime");
            softSwapField.SetPassword($"{dm.AutoSwapPreviousVRMode} (kept alive)");
        }
#endif

#if BASIS_HAS_OPENVR || BASIS_HAS_OPENXR
        // ---- Auto Swap settings group (only shown when a VR SDK is compiled in) ----
        PanelElementDescriptor autoSwapGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        autoSwapGroup.SetTitle("Auto Swap");
        autoSwapGroup.SetDescription(
            "Automatically switch between VR and Desktop based on headset presence.\n" +
            "When enabled, the XR runtime stays alive so swapping is instant.");

        PanelToggle toggleAutoSwap = PanelToggle.CreateNewEntry(autoSwapGroup);
        toggleAutoSwap.Descriptor.SetTitle("Enable Auto Swap");
        toggleAutoSwap.Descriptor.SetDescription(
            "Uses headset proximity sensor to swap between VR and Desktop without restarting the runtime.");
        toggleAutoSwap.AssignBinding(BasisSettingsDefaults.AutoSwapEnabled);

        PanelToggle toggleShutdownRuntime = PanelToggle.CreateNewEntry(autoSwapGroup);
        toggleShutdownRuntime.Descriptor.SetTitle("Shutdown Runtime On Swap");
        toggleShutdownRuntime.Descriptor.SetDescription(
            "When Auto Swap is OFF: controls whether switching modes shuts down OpenXR/OpenVR.\n" +
            "ON = current behavior (full restart). OFF = keep runtime alive during manual swap.\n" +
            "Ignored when Auto Swap is enabled.");
        toggleShutdownRuntime.AssignBinding(BasisSettingsDefaults.ShutdownRuntimeOnSwap);
#endif

        // Discover available modes from registered BaseTypes
        List<string> availableModes = new List<string>();
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
