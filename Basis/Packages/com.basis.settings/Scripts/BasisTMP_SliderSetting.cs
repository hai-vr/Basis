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

    [Header("Display Options")]
    public bool displayAsPercentage = true; // default true (percentage)

    [Header("Editor Auto Setup")]
    public bool autoValidate = true; // when true, OnValidate can set defaults automatically

    private void Awake()
    {
        Initialize();
    }

    public void Initialize()
    {
        float defaultValue = platformDefault.GetDefault();
        slider.value = BasisSettingsSystem.LoadFloat(settingKey, defaultValue);

        UpdateText(slider.value);

        slider.onValueChanged.AddListener(async v =>
        {
            await BasisSettingsSystem.SetFloatAsync(settingKey, v);
            UpdateText(v);
        });

        BasisSettingsSystem.OnSettingChanged += HandleSettingChanged;
    }

    private void OnDestroy()
    {
        BasisSettingsSystem.OnSettingChanged -= HandleSettingChanged;
    }

    private void HandleSettingChanged(string key, string value)
    {
        if (key == settingKey && float.TryParse(value, out float f))
        {
            slider.SetValueWithoutNotify(f);
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
        // Auto-assign slider if missing
        if (slider == null)
            slider = GetComponent<Slider>();

        // Auto-assign Text if missing (search in children)
        if (Text == null)
            Text = GetComponentInChildren<TextMeshProUGUI>();

        // Auto-assign setting key if empty
        if (string.IsNullOrEmpty(settingKey))
            settingKey = this.gameObject.name;

        // If AutoValidate is enabled and platform default is "0" → match slider's current value
        if (autoValidate && Mathf.Approximately(platformDefault.GetDefault(), 0f) && slider != null)
        {
            platformDefault.android = slider.value;
            platformDefault.windows = slider.value;
            platformDefault.linux = slider.value;
            platformDefault.other = slider.value;
        }

        // Keep label preview updated in editor
        if (slider != null)
            UpdateText(slider.value);
    }
}
