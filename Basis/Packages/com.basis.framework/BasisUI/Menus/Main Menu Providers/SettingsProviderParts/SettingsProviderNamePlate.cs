using Basis.Scripts.UI.NamePlate;
using UnityEngine;
using Basis.Scripts.Settings;

namespace Basis.BasisUI
{
    /// <summary>
    /// Settings tab for remote nameplate appearance.
    /// Exposes an enable toggle plus size and transparency controls.
    /// </summary>
    public static class SettingsProviderNamePlate
    {
        [RuntimeInitializeOnLoadMethod]
        static void Init()
        {
            BasisSettingsSystem.OnSettingsFinishedChanges += ApplyNamePlateSettings;
        }

        public static PanelTabPage NamePlateTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;

            descriptor.SetTitle(BasisLocalization.Get("settings.nameplates.title"));

            RectTransform container = descriptor.ContentParent;
            BuildNamePlateContent(container);

            // ─────────────── RESET BUTTON ───────────────
            SettingsProvider.AddResetPageButton(container, "settings.tab.nameplates", ResetNamePlateDefaults);

            descriptor.ForceRebuild();
            return tab;
        }

        /// <summary>
        /// Builds the nameplate group + controls into <paramref name="container"/>
        /// without adding a reset button. Used by the standalone tab and by the
        /// merged Chat tab so both share one source of truth.
        /// </summary>
        public static void BuildNamePlateContent(RectTransform container)
        {
            PanelElementDescriptor nameplateGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            nameplateGroup.SetTitle(BasisLocalization.Get("settings.nameplates.title"));

            PanelToggle toggleEnabled = PanelToggle.CreateNewEntry(nameplateGroup);
            toggleEnabled.Descriptor.SetTitle(BasisLocalization.Get("settings.nameplates.show"));
            toggleEnabled.Descriptor.SetTooltip(BasisLocalization.Get("settings.nameplates.show.tooltip"));
            toggleEnabled.AssignBinding(BasisSettingsDefaults.NPEnabled);

            PanelToggle toggleMenuOnly = PanelToggle.CreateNewEntry(nameplateGroup);
            toggleMenuOnly.Descriptor.SetTitle(BasisLocalization.Get("settings.nameplates.menuOnly"));
            toggleMenuOnly.Descriptor.SetTooltip(BasisLocalization.Get("settings.nameplates.menuOnly.tooltip"));
            toggleMenuOnly.AssignBinding(BasisSettingsDefaults.NPMenuOnly);

            PanelToggle toggleHoverMenuOnly = PanelToggle.CreateNewEntry(nameplateGroup);
            toggleHoverMenuOnly.Descriptor.SetTitle(BasisLocalization.Get("settings.nameplates.hoverMenuOnly"));
            toggleHoverMenuOnly.Descriptor.SetTooltip(BasisLocalization.Get("settings.nameplates.hoverMenuOnly.tooltip"));
            toggleHoverMenuOnly.AssignBinding(BasisSettingsDefaults.NPHoverMenuOnly);

            PanelSlider sliderSize = PanelSlider.CreateEntryAndBind(
                nameplateGroup,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.nameplates.size"), 0.5f, 2f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.NPSize);
            sliderSize.Descriptor.SetTooltip(BasisLocalization.Get("settings.nameplates.size.tooltip"));

            PanelSlider sliderTransparency = PanelSlider.CreateEntryAndBind(
                nameplateGroup,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("settings.nameplates.transparency"), 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.NPTransparency);
            sliderTransparency.Descriptor.SetTooltip(BasisLocalization.Get("settings.nameplates.transparency.tooltip"));

            // Hide appearance settings when nameplates are disabled
            bool isEnabled = BasisSettingsDefaults.NPEnabled.RawValue;
            toggleMenuOnly.Descriptor.SetActive(isEnabled);
            toggleHoverMenuOnly.Descriptor.SetActive(isEnabled);
            sliderSize.Descriptor.SetActive(isEnabled);
            sliderTransparency.Descriptor.SetActive(isEnabled);
            toggleEnabled.OnValueChanged += (val) =>
            {
                toggleMenuOnly.Descriptor.SetActive(val);
                toggleHoverMenuOnly.Descriptor.SetActive(val);
                sliderSize.Descriptor.SetActive(val);
                sliderTransparency.Descriptor.SetActive(val);
                nameplateGroup.ForceRebuild();
            };
        }

        public static void ResetNamePlateDefaults()
        {
            BasisSettingsDefaults.NPEnabled.ResetToDefault();
            BasisSettingsDefaults.NPMenuOnly.ResetToDefault();
            BasisSettingsDefaults.NPHoverMenuOnly.ResetToDefault();
            BasisSettingsDefaults.NPSize.ResetToDefault();
            BasisSettingsDefaults.NPTransparency.ResetToDefault();
            ApplyNamePlateSettings();
        }

        public static void ApplyNamePlateSettings()
        {
            BasisRemoteNamePlateDriver.ApplyNamePlateSettingsFromUI();
#if !UNITY_SERVER
            BasisNetworkPIPCameraDriver.ApplyPipNamePlateSettingsFromUI();
#endif        
        }
    }
}
