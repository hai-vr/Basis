using UnityEngine;
using UnityEngine.Rendering.Universal;
using Basis.BasisUI;

namespace BattlePhaze.SettingsManager.Intergrations
{
    public class SMModuleQualityAndQualitySetURP : BasisSettingsBase
    {
        public UniversalAdditionalCameraData Data;
        public Camera Camera;

        // --- Canonical setting key (from defaults) ---
        private static string K_QUALITY_LEVEL => BasisSettingsDefaults.QualityLevel.BindingKey; // "quality level"

        public override void ValidSettingsChange(string matchedSettingName, string optionValue)
        {
            // Only react to the quality-level setting
            if (matchedSettingName != K_QUALITY_LEVEL)
                return;

            if (Camera == null)
            {
                Camera = Camera.main;
                if (Camera != null)
                {
                    Data = Camera.GetComponent<UniversalAdditionalCameraData>();
                }
            }

            if (Data == null)
                return;

            switch (optionValue.ToLowerInvariant())
            {
                case "verylow":
                    ApplyQualitySettings(AnisotropicFiltering.Enable, 256, false, false);
                    Data.renderPostProcessing = false;
                    break;

                case "low":
                    ApplyQualitySettings(AnisotropicFiltering.Enable, 512, true, true);
                    Data.renderPostProcessing = true;
                    break;

                case "medium":
                    ApplyQualitySettings(AnisotropicFiltering.Enable, 1024, true, true);
                    Data.renderPostProcessing = true;
                    break;

                case "high":
                    ApplyQualitySettings(AnisotropicFiltering.Enable, 2048, true, true);
                    Data.renderPostProcessing = true;
                    break;

                case "ultra":
                    ApplyQualitySettings(AnisotropicFiltering.Enable, 4096, true, true);
                    Data.renderPostProcessing = true;
                    break;
            }
        }

        public override void ChangedSettings()
        {
        }

        private void ApplyQualitySettings(
            AnisotropicFiltering anisotropicFilter,
            int particleBudget,
            bool renderShadows,
            bool stopNaN)
        {
            QualitySettings.anisotropicFiltering = anisotropicFilter;
            QualitySettings.particleRaycastBudget = particleBudget;

            BasisDebug.Log("Apply Quality Settings", BasisDebug.LogTag.System);

            if (Data != null)
            {
                Data.renderShadows = renderShadows;
                Data.stopNaN = stopNaN;
            }
        }
    }
}
