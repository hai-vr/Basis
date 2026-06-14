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
    }

    private bool _overrideEnabled;
    private float _pendingDensity = 0.2f;

    private readonly ConditionalWeakTable<VolumetricFogVolumeComponent, AuthoredFogState> _authored = new();

    private static string K_USE_FOG_OVERRIDE => BasisSettingsDefaults.UseVolumetricFogOverride.BindingKey;
    private static string K_FOG_DENSITY => BasisSettingsDefaults.VolumetricFogDensity.BindingKey;

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
        }
    }
}
