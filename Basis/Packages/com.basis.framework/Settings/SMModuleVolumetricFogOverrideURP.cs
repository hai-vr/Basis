using System.Runtime.CompilerServices;
using Basis.BasisUI;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Accessibility settings module that controls volumetric fog density
/// by modifying VolumetricFogVolumeComponent overrides on all existing Volumes in the scene.
/// When the override toggle is disabled the scene's authored fog overrides are restored.
/// </summary>
public class SMModuleVolumetricFogOverrideURP : BasisSettingsBase
{
    private sealed class AuthoredFogState
    {
        public bool EnabledOverride;
        public bool EnabledValue;
        public bool DensityOverride;
        public float DensityValue;
        public bool ApvContributionOverride;
        public bool ApvContributionValue;
        public bool ApvModeOverride;
        public VolumetricFogAPVMode ApvModeValue;
    }

    private bool _overrideEnabled;
    private float _pendingDensity = 0.2f;
    private bool _bakedAPV;

    private readonly ConditionalWeakTable<VolumetricFogVolumeComponent, AuthoredFogState> _authored = new();

    private static string K_USE_FOG_OVERRIDE => BasisSettingsDefaults.UseVolumetricFogOverride.BindingKey;
    private static string K_FOG_DENSITY => BasisSettingsDefaults.VolumetricFogDensity.BindingKey;
    private static string K_FOG_BAKED_APV => BasisSettingsDefaults.VolumetricFogBakedAPV.BindingKey;

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        if (matchedSettingName == K_USE_FOG_OVERRIDE)
        {
            _overrideEnabled = optionValue == "true";
            ApplyOverride();
        }
        else if (matchedSettingName == K_FOG_DENSITY)
        {
            if (SliderReadOption(optionValue, out float density))
            {
                _pendingDensity = density;
                ApplyOverride();
            }
        }
        else if (matchedSettingName == K_FOG_BAKED_APV)
        {
            _bakedAPV = optionValue == "true";
            ApplyOverride();
            // Convert the world's APV to a baked volume now so baked mode has data to sample straight away.
            if (_bakedAPV)
                VolumetricFogAPVBaker.RequestRebake();
        }
    }

    public override void ChangedSettings()
    {
    }

    private void ApplyOverride()
    {
        Volume[] volumes = FindObjectsByType<Volume>(FindObjectsInactive.Exclude);
        foreach (Volume volume in volumes)
        {
            if (volume.profile == null)
                continue;

            if (!volume.profile.TryGet<VolumetricFogVolumeComponent>(out VolumetricFogVolumeComponent fog))
                continue;

            if (!_authored.TryGetValue(fog, out AuthoredFogState authored))
            {
                authored = new AuthoredFogState
                {
                    EnabledOverride = fog.enabled.overrideState,
                    EnabledValue = fog.enabled.value,
                    DensityOverride = fog.density.overrideState,
                    DensityValue = fog.density.value,
                    ApvContributionOverride = fog.enableAPVContribution.overrideState,
                    ApvContributionValue = fog.enableAPVContribution.value,
                    ApvModeOverride = fog.apvMode.overrideState,
                    ApvModeValue = fog.apvMode.value,
                };
                _authored.Add(fog, authored);
            }

            if (_overrideEnabled)
            {
                fog.enabled.overrideState = true;
                fog.enabled.value = true;
                fog.density.overrideState = true;
                fog.density.value = _pendingDensity;
            }
            else
            {
                fog.enabled.overrideState = authored.EnabledOverride;
                fog.enabled.value = authored.EnabledValue;
                fog.density.overrideState = authored.DensityOverride;
                fog.density.value = authored.DensityValue;
            }

            if (_bakedAPV)
            {
                // Force the baked APV path on. enableAPVContribution must be on for the fog to sample APV
                // at all, and apvMode selects the static baked volume over live per-step evaluation.
                fog.enableAPVContribution.overrideState = true;
                fog.enableAPVContribution.value = true;
                fog.apvMode.overrideState = true;
                fog.apvMode.value = VolumetricFogAPVMode.Baked;
            }
            else
            {
                fog.enableAPVContribution.overrideState = authored.ApvContributionOverride;
                fog.enableAPVContribution.value = authored.ApvContributionValue;
                fog.apvMode.overrideState = authored.ApvModeOverride;
                fog.apvMode.value = authored.ApvModeValue;
            }
        }
    }
}
