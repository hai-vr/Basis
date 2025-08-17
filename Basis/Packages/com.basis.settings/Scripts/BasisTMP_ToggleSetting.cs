using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Toggle))]
public class BasisTMP_ToggleSetting : MonoBehaviour
{
    public string settingKey;
    public BasisPlatformDefault<bool> platformDefault;

    public Toggle toggle;

    private void Awake()
    {
        bool defaultValue = platformDefault.GetDefault();
        toggle.isOn = BasisSettings.LoadBool(settingKey, defaultValue);

        toggle.onValueChanged.AddListener(async v =>
        {
            await BasisSettings.SetBoolAsync(settingKey, v);
        });

        BasisSettings.OnSettingChanged += HandleSettingChanged;
    }

    private void OnDestroy()
    {
        BasisSettings.OnSettingChanged -= HandleSettingChanged;
    }

    private void HandleSettingChanged(string key, string value)
    {
        if (key == settingKey)
            toggle.SetIsOnWithoutNotify(BasisSettings.LoadBool(settingKey));
    }
    private void OnValidate()
    {
        // Auto-assign in the editor if missing
        if (toggle == null)
            toggle = GetComponent<Toggle>();
    }
}
