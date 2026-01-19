using System.Globalization;
using UnityEngine;
public abstract class BasisSettingsBase : MonoBehaviour
{
    public virtual void Awake()
    {
        BasisSettingsSystem.OnSettingChanged += OnSettingChanged;
        BasisSettingsSystem.OnSettingsFinishedChanges += ApplyFinal;
    }

    public void OnDestroy()
    {
        BasisSettingsSystem.OnSettingChanged -= OnSettingChanged;
        BasisSettingsSystem.OnSettingsFinishedChanges -= ApplyFinal;
    }
    public void ApplyFinal()
    {
        QualitySettings.SetQualityLevel(QualitySettings.GetQualityLevel(), true);
    }
    public void OnSettingChanged(string uniqueName, string optionValue)
    {
        // Normalize casing for comparison
        ValidSettingsChange(uniqueName, optionValue);
        ChangedSettings();
    }
    public bool SliderReadOption(string String, out float Value)
    {
        return float.TryParse(String, NumberStyles.Any, CultureInfo.InvariantCulture, out Value);
    }
    /// <summary>
    /// Called when a valid setting change occurs.
    /// Provides which setting was matched and the new option value.
    /// </summary>
    public abstract void ValidSettingsChange(string matchedSettingName, string optionValue);
    public abstract void ChangedSettings();
}
