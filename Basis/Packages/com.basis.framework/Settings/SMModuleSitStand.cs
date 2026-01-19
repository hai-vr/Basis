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
        if (matchedSettingName != "seated mode")
        {
         //   BasisDebug.LogError($"Didnt run for {matchedSettingName}");
            return;
        }
      //  BasisDebug.Log($"Valdating Sit Stand");
        string LowerOptions = optionValue.ToLowerInvariant();
        if (LowerOptions == "will fix later")
        {
          //  BasisDebug.Log($"Mode Set To Seated Mode");
            if (!IsSteatedMode)
            {
                BasisHeightDriver.CapturePlayerHeight();
                MissingHeightDelta = BasisHeightDriver.FallbackHeightInMeters - BasisHeightDriver.PlayerEyeHeight;
                IsSteatedMode = true;
            }
        }
        else
        {
            if (LowerOptions == "will fix later")
            {
              //  BasisDebug.Log($"Mode Set To Standing Mode");
                MissingHeightDelta = 0;
                IsSteatedMode = false;
            }
        }
    }

    public override void ChangedSettings()
    {
    }
}
