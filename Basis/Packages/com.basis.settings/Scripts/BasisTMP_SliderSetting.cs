using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class BasisTMP_SliderSetting : MonoBehaviour
{
    public string settingKey;

    [SerializeField]
    public BasisPlatformDefault<float> platformDefault;

    public Slider slider;
    public TextMeshProUGUI Text;
    public Button applyButton; // NEW: Apply button reference

    [Header("Display Options")]
    public bool displayAsPercentage = true; // default true (percentage)

    [Header("Editor Auto Setup")]
    public bool autoValidate = true; // when true, OnValidate can set defaults automatically

    private float pendingValue; // NEW: holds unsaved slider value

    private void Awake()
    {
        Initialize();
    }

    public void Initialize()
    {
        float defaultValue = platformDefault.GetDefault();
        float savedValue = BasisSettingsSystem.LoadFloat(settingKey, defaultValue);

        slider.value = savedValue;
        pendingValue = savedValue;

        UpdateText(slider.value);

        // Only update preview text, not saving immediately
        slider.onValueChanged.AddListener(v =>
        {
            pendingValue = v;
            UpdateText(v);
        });

        // Apply button saves the value
        if (applyButton != null)
            applyButton.onClick.AddListener(ApplySetting);

        BasisSettingsSystem.OnSettingChanged += HandleSettingChanged;
    }

    private void OnDestroy()
    {
        BasisSettingsSystem.OnSettingChanged -= HandleSettingChanged;

        if (applyButton != null)
            applyButton.onClick.RemoveListener(ApplySetting);
    }

    private async void ApplySetting()
    {
        await BasisSettingsSystem.SetFloatAsync(settingKey, pendingValue);
     //   Debug.Log($"[Settings] Applied {settingKey} = {pendingValue}");
    }

    private void HandleSettingChanged(string key, string value)
    {
        if (key == settingKey && float.TryParse(value, out float f))
        {
            slider.SetValueWithoutNotify(f);
            pendingValue = f;
            UpdateText(f);
        }
    }

    private void UpdateText(float value)
    {
        if (Text == null || slider == null) return;

        if (displayAsPercentage)
        {
            float range = slider.maxValue - slider.minValue;
            float normalized = (range > 0f) ? (value - slider.minValue) / range : 0f;
            Text.text = $"{Mathf.RoundToInt(normalized * 100f)}%";
        }
        else
        {
            // Show raw value with 2 decimals
            Text.text = value.ToString("0.##");
        }
    }

    private void OnValidate()
    {
        if (slider == null)
            slider = GetComponent<Slider>();

        if (Text == null)
            Text = GetComponentInChildren<TextMeshProUGUI>();

        if (string.IsNullOrEmpty(settingKey))
            settingKey = this.gameObject.name;

        if (autoValidate && Mathf.Approximately(platformDefault.GetDefault(), 0f) && slider != null)
        {
            platformDefault.android = slider.value;
            platformDefault.windows = slider.value;
            platformDefault.linux = slider.value;
            platformDefault.other = slider.value;
        }

        if (slider != null)
            UpdateText(slider.value);
    }
}
