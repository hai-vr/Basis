using Basis.Scripts.BasisSdk.Players;
using System.Security.Cryptography;
using UnityEngine;

public class SMModuleCalibration : BasisSettingsBase
{
    public static BasisSelectedHeightMode HeightMode = BasisSelectedHeightMode.EyeHeight;
    public static bool ApplyCustomScale = false;
    public static float SelectedScale = 1.6f;
    public static float SelectedEyeHeight = 1.61f;

    // Cache last applied state so we only apply when it actually changes.
    private static bool _hasApplied;
    private static BasisSelectedHeightMode _lastHeightMode;
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
                    }

                    if (HeightMode != old) _dirty = true;
                    break;
                }

            case "selectedheight":
                {
                    var old = BasisHeightDriver.CustomPlayerEyeHeight;
                    if (SliderReadOption(optionValue, out var parsed))
                    {
                        // Avoid tiny float jitter causing re-apply spam.
                        if (!Mathf.Approximately(old, parsed))
                        {
                            BasisHeightDriver.CustomPlayerEyeHeight = parsed;
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
            case "real world eye height":
                {
                    var old = SelectedEyeHeight;
                    if (SliderReadOption(optionValue, out var CurrentSelectedEyeHeight))
                    {
                        if (!Mathf.Approximately(old, CurrentSelectedEyeHeight))
                        {
                            SelectedEyeHeight = CurrentSelectedEyeHeight;
                            _dirty = true;
                        }
                    }
                    else
                    {
                        BasisDebug.LogError("Missing Selected Scale", BasisDebug.LogTag.Device);
                    }
                }
                break;
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

        // Compare against last applied values, so even if _dirty gets missed,
        // we still won't spam applies.
        bool sameAsLast =
            _hasApplied &&
            _lastHeightMode == HeightMode &&
            Mathf.Approximately(_lastSelectedPlayerHeight, BasisHeightDriver.CustomPlayerEyeHeight) &&
            Mathf.Approximately(_lastSelectedScale, SelectedScale) &&
            _lastApplyCustomScale == ApplyCustomScale;

        if (sameAsLast)
        {
            _dirty = false;
            return;
        }
        if (ApplyCustomScale || ApplyCustomScale == false && _lastApplyCustomScale  == true)
        {
            BasisHeightDriver.ApplyScaleAndHeight();
        }
        _hasApplied = true;
        _lastHeightMode = HeightMode;
        _lastSelectedPlayerHeight = BasisHeightDriver.CustomPlayerEyeHeight;
        _lastSelectedScale = SelectedScale;
        _lastApplyCustomScale = ApplyCustomScale;

        _dirty = false;

        BasisDebug.Log(
            $"Applied height settings. HeightMode {HeightMode} " +
            $"SelectedPlayerHeight {BasisHeightDriver.CustomPlayerEyeHeight}, " +
            $"SelectedScale {SelectedScale}, ApplyCustomScale {ApplyCustomScale}"
        );
    }
}
