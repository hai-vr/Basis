using Basis.Scripts.UI.NamePlate;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Settings tab for remote nameplate appearance.
    /// Exposes an enable toggle plus width, size, and transparency controls.
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

            descriptor.SetTitle("Nameplates");
            descriptor.SetDescription("Controls the appearance of nameplates above other players.");

            RectTransform container = descriptor.ContentParent;

            // ─────────────── VISIBILITY TOGGLES ───────────────
            PanelToggle toggleEnabled = PanelToggle.CreateNewEntry(container);
            toggleEnabled.Descriptor.SetTitle("Show Nameplates");
            toggleEnabled.AssignBinding(BasisSettingsDefaults.NPEnabled);

            PanelToggle toggleMenuOnly = PanelToggle.CreateNewEntry(container);
            toggleMenuOnly.Descriptor.SetTitle("Only Show When Menu Open");
            toggleMenuOnly.AssignBinding(BasisSettingsDefaults.NPMenuOnly);

            // ─────────────── NAMEPLATE APPEARANCE GROUP ───────────────
            PanelElementDescriptor nameplateGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            nameplateGroup.SetTitle("Nameplate Appearance");
            nameplateGroup.SetDescription("Adjust the width, size, and transparency of remote player nameplates.");

            PanelSlider sliderWidth = PanelSlider.CreateEntryAndBind(
                nameplateGroup,
                PanelSlider.SliderSettings.Advanced("Width", 10f, 60f, false, 1, ValueDisplayMode.Raw),
                BasisSettingsDefaults.NPWidth);

            PanelSlider sliderSize = PanelSlider.CreateEntryAndBind(
                nameplateGroup,
                PanelSlider.SliderSettings.Advanced("Size", 0.5f, 2f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.NPSize);

            PanelSlider sliderTransparency = PanelSlider.CreateEntryAndBind(
                nameplateGroup,
                PanelSlider.SliderSettings.Advanced("Transparency", 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.NPTransparency);

            // Menu-only toggle and appearance group only visible when nameplates are enabled
            bool isEnabled = BasisSettingsDefaults.NPEnabled.RawValue;
            toggleMenuOnly.Descriptor.SetActive(isEnabled);
            nameplateGroup.SetActive(isEnabled);
            toggleEnabled.OnValueChanged += (val) =>
            {
                toggleMenuOnly.Descriptor.SetActive(val);
                nameplateGroup.SetActive(val);
                container.GetComponentInParent<PanelElementDescriptor>()?.ForceRebuild();
            };

            // ─────────────── RESET BUTTON ───────────────
            SettingsProvider.AddResetPageButton(container, "Nameplates", ResetNamePlateDefaults);

            descriptor.ForceRebuild();
            return tab;
        }

        private static void ResetNamePlateDefaults()
        {
            BasisSettingsDefaults.NPEnabled.ResetToDefault();
            BasisSettingsDefaults.NPMenuOnly.ResetToDefault();
            BasisSettingsDefaults.NPWidth.ResetToDefault();
            BasisSettingsDefaults.NPSize.ResetToDefault();
            BasisSettingsDefaults.NPTransparency.ResetToDefault();
            ApplyNamePlateSettings();
        }

        public static void ApplyNamePlateSettings()
        {
            if (BasisRemoteNamePlateDriver.Instance != null)
            {
                BasisRemoteNamePlateDriver.Instance.ApplyNamePlateSettingsFromUI();
            }
        }
    }
}
