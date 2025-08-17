using UnityEngine;
using TMPro;

[RequireComponent(typeof(TMP_Dropdown))]
public class BasisTMP_DropdownSetting : MonoBehaviour
{
    public string settingKey;
    public BasisPlatformDefault<int> platformDefault;

    public TMP_Dropdown dropdown;

    private void Awake()
    {
        int defaultValue = platformDefault.GetDefault();
        dropdown.value = BasisSettings.LoadInt(settingKey, defaultValue);

        dropdown.onValueChanged.AddListener(async v =>
        {
            await BasisSettings.SetIntAsync(settingKey, v);
        });

        BasisSettings.OnSettingChanged += HandleSettingChanged;
    }

    private void OnDestroy()
    {
        BasisSettings.OnSettingChanged -= HandleSettingChanged;
    }

    private void HandleSettingChanged(string key, string value)
    {
        if (key == settingKey && int.TryParse(value, out int i))
            dropdown.SetValueWithoutNotify(i);
    }
    private void OnValidate()
    {
        // Auto-assign in the editor if missing
        if (dropdown == null)
            dropdown = GetComponent<TMP_Dropdown>();
    }
}
