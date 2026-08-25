using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Basis.Scripts.Drivers;
using Basis.Scripts.Settings;
public abstract class BasisSettingsBase : MonoBehaviour
{
    public virtual void Awake()
    {
        BasisSettingsSystem.OnSettingChanged += TOLowerValidSettingsChange;
        BasisSettingsSystem.OnSettingsFinishedChanges += ChangedSettings;
    }

    public void OnDestroy()
    {
        BasisSettingsSystem.OnSettingChanged -= TOLowerValidSettingsChange;
        BasisSettingsSystem.OnSettingsFinishedChanges -= ChangedSettings;
    }
    public bool SliderReadOption(string String, out float Value)
    {
        return float.TryParse(String, NumberStyles.Any, CultureInfo.InvariantCulture, out Value);
    }
    public static bool StaticSliderReadOption(string String, out float Value)
    {
        return float.TryParse(String, NumberStyles.Any, CultureInfo.InvariantCulture, out Value);
    }
    public static int PlayerVolumeLayerMask
    {
        get
        {
            if (!BasisLocalCameraDriver.HasInstance || BasisLocalCameraDriver.Instance == null) return 1;
            Camera camera = BasisLocalCameraDriver.Instance.Camera;
            if (camera == null || !camera.TryGetComponent(out UniversalAdditionalCameraData data)) return 1;
            return data.volumeLayerMask.value;
        }
    }
    public static bool CanOverrideVolume(Volume volume, int playerVolumeLayerMask)
    {
        return volume != null && (playerVolumeLayerMask & (1 << volume.gameObject.layer)) != 0;
    }
    public void TOLowerValidSettingsChange(string matchedSettingName, string optionValue)
    {
        ValidSettingsChange(matchedSettingName.ToLower(), optionValue.ToLower());
    }
    /// <summary>
    /// Called when a valid setting change occurs.
    /// Provides which setting was matched and the new option value.
    /// </summary>
    public abstract void ValidSettingsChange(string matchedSettingName, string optionValue);
    public abstract void ChangedSettings();
}
