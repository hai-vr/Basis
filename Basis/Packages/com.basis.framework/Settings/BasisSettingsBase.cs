using System.Globalization;
using UnityEngine;
public abstract class BasisSettingsBase : MonoBehaviour
{
    [Tooltip("List of setting names this component will react to.")]
    public string[] SettingsNames;
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
        string lowered = uniqueName.ToLower();
        PushSettings(lowered, optionValue);
        ChangedSettings();
    }
    public void PushSettings(string lowered,string optionValue)
    {
        foreach (string setting in SettingsNames)
        {
            if (lowered == setting.ToLower())
            {
                // Found which setting name matched
                ValidSettingsChange(setting, optionValue);
                return;
            }
        }
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
