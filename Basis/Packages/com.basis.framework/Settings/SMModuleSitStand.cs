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
        if (optionValue == SettingsProviderIK.SeatedMode_Standing.ToLower())
        {
            BasisDebug.Log($"Mode Set To Standing Mode");
            bool wasSeated = IsSteatedMode;
            MissingHeightDelta = 0;
            IsSteatedMode = false;
            if (wasSeated && Basis.Scripts.BasisSdk.Players.BasisLocalPlayer.Instance != null)
            {
                // Restore the standing eye height (persisted/genuine when available; live poll for a
                // fresh user) and re-resolve heights so the height-changed callback fires — camera
                // scale and the menu reposition to the un-lifted tracking space.
                BasisHeightDriver.CapturePlayerHeight(recaptureEyeHeight: false);
                BasisHeightDriver.ApplyScaleAndHeight();
                BasisHeightDriver.ScheduleHeightChangeCallback(BasisHeightDriver.HeightModeChange.OnSitStandChanged);
            }
        }
        else
        {
            if (optionValue == SettingsProviderIK.SeatedMode_Seated.ToLower())
            {
                if (!IsSteatedMode)
                {
                    BasisHeightDriver.CapturePlayerHeight();
                    MissingHeightDelta = BasisHeightDriver.FallbackHeightInMeters - BasisHeightDriver.PlayerEyeHeight;
                    IsSteatedMode = true;
                    BasisDebug.Log($"Mode Set To Seated Mode {MissingHeightDelta}");
                    if (Basis.Scripts.BasisSdk.Players.BasisLocalPlayer.Instance != null)
                    {
                        // Now that seated mode is active, the denominator becomes the virtual standing
                        // eye (the seated capture above measured the real seated eye only to size the
                        // lift). Re-resolve so DeviceScale matches the lifted space, and fire the
                        // height-changed callback — without it the menu stayed at the pre-lift height.
                        BasisHeightDriver.CapturePlayerHeight();
                        BasisHeightDriver.ApplyScaleAndHeight();
                        BasisHeightDriver.ScheduleHeightChangeCallback(BasisHeightDriver.HeightModeChange.OnSitStandChanged);
                    }
                }
            }
        }
    }

    public override void ChangedSettings()
    {
    }
}
