using Basis.BasisUI;

public class SMModuleSitStand : BasisSettingsBase
{
    public static bool IsSteatedMode = false;
    public static float MissingHeightDelta = 0;
    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        if (matchedSettingName != BasisSettingsDefaults.SitStand.BindingKey)
        {
            return;
        }
        if (optionValue == SettingsProviderIK.SeatedMode_Standing)
        {
            BasisDebug.Log($"Mode Set To Standing Mode");
            MissingHeightDelta = 0;
            IsSteatedMode = false;
        }
        else
        {
            BasisDebug.Log($"Mode Set To Seated Mode");
            if (!IsSteatedMode)
            {
                BasisHeightDriver.CapturePlayerHeight();
                MissingHeightDelta = BasisHeightDriver.FallbackHeightInMeters - BasisHeightDriver.PlayerEyeHeight;
                IsSteatedMode = true;
            }
        }
    }

    public override void ChangedSettings()
    {
    }
}
