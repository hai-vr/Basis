using Basis.BasisUI;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Accessibility settings module that controls volumetric fog density
/// by modifying VolumetricFogVolumeComponent overrides on all existing Volumes in the scene.
/// Only applies when the volumetric fog override toggle is enabled.
/// </summary>
public class SMModuleVolumetricFogOverrideURP : BasisSettingsBase
{
    private bool _overrideEnabled;
    private float _pendingDensity = 0.2f;

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

            if (_overrideEnabled)
            {
                fog.enabled.overrideState = true;
                fog.enabled.value = true;
                fog.density.overrideState = true;
                fog.density.value = _pendingDensity;
            }
            else
            {
                fog.enabled.overrideState = false;
                fog.density.overrideState = false;
            }
        }
    }
}
