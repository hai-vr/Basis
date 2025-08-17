using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class BasisTMP_SliderSetting : MonoBehaviour
{
    public string settingKey;
    [SerializeField]
    public BasisPlatformDefault<float> platformDefault;

    public Slider slider;

    private void Awake()
    {
        Initalize();
    }
    public void Initalize()
    {
        float defaultValue = platformDefault.GetDefault();
        slider.value = BasisSettings.LoadFloat(settingKey, defaultValue);

        slider.onValueChanged.AddListener(async v =>
        {
            await BasisSettings.SetFloatAsync(settingKey, v);
        });

        BasisSettings.OnSettingChanged += HandleSettingChanged;
    }

    private void OnDestroy()
    {
        BasisSettings.OnSettingChanged -= HandleSettingChanged;
    }

    private void HandleSettingChanged(string key, string value)
    {
        if (key == settingKey && float.TryParse(value, out float f))
            slider.SetValueWithoutNotify(f);
    }
    private void OnValidate()
    {
        // Auto-assign in the editor if missing
        if (slider == null)
            slider = GetComponent<Slider>();
    }
}
