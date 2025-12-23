using Basis.Scripts.BasisSdk.Players;
using UnityEngine;

public class SMModuleCalibration : BasisSettingsBase
{
    public static float SelectedPlayerHeight = 1.6f;
    public static BasisSelectedHeightMode HeightMode = BasisSelectedHeightMode.EyeHeight;
    public static bool ApplyCustomScale = false;
    public static float SelectedScale = 1.6f;

    // Cache last applied state so we only apply when it actually changes.
    private static bool _hasApplied;
    private static BasisSelectedHeightMode _lastHeightMode;
    private static float _lastSelectedAvatarHeight;
    private static float _lastSelectedPlayerHeight;
    private static float _lastSelectedScale;
    private static bool _lastApplyCustomScale;

    private static bool _dirty;

    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        switch (matchedSettingName.ToLower())
        {
            case "ik mode":
                {
                    var old = HeightMode;

                    // Note: your optionValue strings must match exactly; consider trimming.
                    switch (optionValue)
                    {
                        case "Eye Height":
                            HeightMode = BasisSelectedHeightMode.EyeHeight;
                            break;
                        case "Arm Distance":
                            HeightMode = BasisSelectedHeightMode.ArmSpan;
                            break;
                        case "Calibration Eye Height":
                            HeightMode = BasisSelectedHeightMode.Custom;
                            break;
                    }

                    if (HeightMode != old) _dirty = true;
                    break;
                }

            case "selectedheight":
                {
                    var old = SelectedPlayerHeight;
                    if (SliderReadOption(optionValue, out var parsed))
                    {
                        // Avoid tiny float jitter causing re-apply spam.
                        if (!Mathf.Approximately(old, parsed))
                        {
                            SelectedPlayerHeight = parsed;
                            _dirty = true;
                        }
                    }
                    else
                    {
                        BasisDebug.LogError("Missing Selected Height", BasisDebug.LogTag.Device);
                    }
                    break;
                }

            case "custom scale":
                {
                    var old = ApplyCustomScale;
                    if (bool.TryParse(optionValue, out var parsed) && parsed != old)
                    {
                        ApplyCustomScale = parsed;
                        _dirty = true;
                    }
                    break;
                }

            case "selected scale":
                {
                    var old = SelectedScale;
                    if (SliderReadOption(optionValue, out var parsed))
                    {
                        if (!Mathf.Approximately(old, parsed))
                        {
                            SelectedScale = parsed;
                            _dirty = true;
                        }
                    }
                    else
                    {
                        BasisDebug.LogError("Missing Selected Scale", BasisDebug.LogTag.Device);
                    }
                    break;
                }

            default:
                BasisDebug.LogError($"UnImplemented Settings Name! {matchedSettingName}", BasisDebug.LogTag.Device);
                break;
        }
    }

    public override void ChangedSettings()
    {
        // If UI calls ChangedSettings a lot, this prevents reapplying.
        if (!_dirty && _hasApplied)
            return;

        bool isCustom = HeightMode == BasisSelectedHeightMode.Custom;
        float avatarHeight = BasisHeightDriver.SelectedAvatarHeight;

        // Compare against last applied values, so even if _dirty gets missed,
        // we still won't spam applies.
        bool sameAsLast =
            _hasApplied &&
            _lastHeightMode == HeightMode &&
            Mathf.Approximately(_lastSelectedAvatarHeight, avatarHeight) &&
            Mathf.Approximately(_lastSelectedPlayerHeight, SelectedPlayerHeight) &&
            Mathf.Approximately(_lastSelectedScale, SelectedScale) &&
            _lastApplyCustomScale == ApplyCustomScale;

        if (sameAsLast)
        {
            _dirty = false;
            return;
        }

        BasisHeightDriver.SetCustomHeight(isCustom,avatarHeight,SelectedPlayerHeight,SelectedScale,ApplyCustomScale);

        _hasApplied = true;
        _lastHeightMode = HeightMode;
        _lastSelectedAvatarHeight = avatarHeight;
        _lastSelectedPlayerHeight = SelectedPlayerHeight;
        _lastSelectedScale = SelectedScale;
        _lastApplyCustomScale = ApplyCustomScale;

        _dirty = false;

        BasisDebug.Log(
            $"Applied height settings. HeightMode {HeightMode} " +
            $"SelectedAvatarHeight {avatarHeight}, SelectedPlayerHeight {SelectedPlayerHeight}, " +
            $"SelectedScale {SelectedScale}, ApplyCustomScale {ApplyCustomScale}"
        );
    }
}
