using Basis.BasisUI;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace HVR.LicenseReview
{
    public static class SettingsProviderLicenses
    {
        [RuntimeInitializeOnLoadMethod]
        static void Register()
        {
            SettingsProvider.LicensesBuilder = BuildSection;
        }

        static void BuildSection(RectTransform container)
        {
            var licenseManifest = Addressables.LoadAssetAsync<LicenseManifest>("BasisFrameworkLicenseManifest")
                .WaitForCompletion();

            foreach (var license in licenseManifest.baked)
            {
                var toggle = PanelToggle.CreateNew(container);
                toggle.Descriptor.SetTitle(license.productName);
                toggle.Descriptor.SetDescription(license.fullLicenseName);
                toggle.SetValueWithoutNotify(false);

                var licenseText = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
                licenseText.SetTitle($"{license.packageName}/{license.licensePath}");
                licenseText.SetDescription(license.fullLicenseText);

                var openUrl = PanelButton.CreateNew(PanelButton.ButtonStyles.Default, licenseText);
                openUrl.Descriptor.SetTitle(BasisLocalization.Get("settings.thirdpartylicenses.openlicenseurl"));
                openUrl.OnClicked += () =>
                {
                    if (license.licenseUrl.StartsWith("https://"))
                    {
                        Application.OpenURL(license.licenseUrl);
                    }
                };

                licenseText.SetActive(false);
                toggle.OnValueChanged += value =>
                {
                    licenseText.SetActive(value);
                };
            }
        }
    }
}
