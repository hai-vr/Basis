using Basis.BasisUI;

public class SMModuleSitStand : BasisSettingsBase
{
    public static bool IsSteatedMode = false;
    public static float MissingHeightDelta = 0;

    // --- Canonical setting key (from defaults) ---
    private static string K_SEATED_MODE => BasisSettingsDefaults.SeatedMode.BindingKey; // "seated mode"

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        // Only react to the seated/standing mode setting
        if (matchedSettingName != K_SEATED_MODE)
            return;

        switch (optionValue)
        {
            case "Seated Mode":
                if (!IsSteatedMode)
                {
                    BasisHeightDriver.CapturePlayerHeight();
                    MissingHeightDelta =
                        BasisHeightDriver.FallbackHeightInMeters -
                        BasisHeightDriver.PlayerEyeHeight;

                    IsSteatedMode = true;
                }
                break;

            case "Standing Mode":
                MissingHeightDelta = 0;
                IsSteatedMode = false;
                break;
        }
    }

    public override void ChangedSettings()
    {
    }
}
