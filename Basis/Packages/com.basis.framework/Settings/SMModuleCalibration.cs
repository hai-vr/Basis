using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
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
                            if (BasisDeviceManagement.IsUserInDesktop())
                            {
                                HeightMode = BasisSelectedHeightMode.EyeHeight;
                            }
                            else
                            {
                                HeightMode = BasisSelectedHeightMode.ArmSpan;
                            }
                            break;
                    }

                    if (HeightMode != old) _dirty = true;
                    break;
                }

            case "selectedheight":
                {
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
            // ---------- GLOBAL ONE EURO PARAMS ----------
            case "FBIKMinCutoff":
                {
                    if (float.TryParse(optionValue, out var f))
                        BasisLocalRigDriver.MinCutoff = f;
                    break;
                }
            case "FBIKBeta":
                {
                    if (float.TryParse(optionValue, out var f))
                        BasisLocalRigDriver.Beta = f;
                    break;
                }
            case "FBIKDerivativeCutoff":
                {
                    if (float.TryParse(optionValue, out var f))
                        BasisLocalRigDriver.DerivativeCutoff = f;
                    break;
                }
            case "FBIKPositionSmoothingHz":
                {
                    if (float.TryParse(optionValue, out var f))
                        BasisLocalRigDriver.PositionSmoothingHz = f;
                    break;
                }
            case "FBIKRotationSmoothingHz":
                {
                    if (float.TryParse(optionValue, out var f))
                        BasisLocalRigDriver.RotationSmoothingHz = f;
                    break;
                }

            // ---------- HIPS ----------
            case "FBIKHipsSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_Hips] = parsed;
                    break;
                }
            case "FBIKHipsSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_Hips] = parsed;
                    break;
                }
            case "FBIKHipsEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_Hips] = parsed;
                    break;
                }
            case "FBIKHipsEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_Hips] = parsed;
                    break;
                }

            // ---------- HEAD ----------
            case "FBIKHeadSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_Head] = parsed;
                    break;
                }
            case "FBIKHeadSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_Head] = parsed;
                    break;
                }
            case "FBIKHeadEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_Head] = parsed;
                    break;
                }
            case "FBIKHeadEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_Head] = parsed;
                    break;
                }

            // ---------- LEFT FOOT ----------
            case "FBIKLeftFootSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_LeftFoot] = parsed;
                    break;
                }
            case "FBIKLeftFootSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_LeftFoot] = parsed;
                    break;
                }
            case "FBIKLeftFootEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_LeftFoot] = parsed;
                    break;
                }
            case "FBIKLeftFootEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_LeftFoot] = parsed;
                    break;
                }

            // ---------- RIGHT FOOT ----------
            case "FBIKRightFootSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_RightFoot] = parsed;
                    break;
                }
            case "FBIKRightFootSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_RightFoot] = parsed;
                    break;
                }
            case "FBIKRightFootEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_RightFoot] = parsed;
                    break;
                }
            case "FBIKRightFootEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_RightFoot] = parsed;
                    break;
                }

            // ---------- CHEST ----------
            case "FBIKChestSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_Chest] = parsed;
                    break;
                }
            case "FBIKChestSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_Chest] = parsed;
                    break;
                }
            case "FBIKChestEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_Chest] = parsed;
                    break;
                }
            case "FBIKChestEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_Chest] = parsed;
                    break;
                }

            // ---------- LEFT LOWER LEG ----------
            case "FBIKLeftLowerLegSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_LeftLowerLeg] = parsed;
                    break;
                }
            case "FBIKLeftLowerLegSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_LeftLowerLeg] = parsed;
                    break;
                }
            case "FBIKLeftLowerLegEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_LeftLowerLeg] = parsed;
                    break;
                }
            case "FBIKLeftLowerLegEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_LeftLowerLeg] = parsed;
                    break;
                }

            // ---------- RIGHT LOWER LEG ----------
            case "FBIKRightLowerLegSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_RightLowerLeg] = parsed;
                    break;
                }
            case "FBIKRightLowerLegSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_RightLowerLeg] = parsed;
                    break;
                }
            case "FBIKRightLowerLegEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_RightLowerLeg] = parsed;
                    break;
                }
            case "FBIKRightLowerLegEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_RightLowerLeg] = parsed;
                    break;
                }

            // ---------- LEFT HAND ----------
            case "FBIKLeftHandSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_LeftHand] = parsed;
                    break;
                }
            case "FBIKLeftHandSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_LeftHand] = parsed;
                    break;
                }
            case "FBIKLeftHandEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_LeftHand] = parsed;
                    break;
                }
            case "FBIKLeftHandEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_LeftHand] = parsed;
                    break;
                }

            // ---------- RIGHT HAND ----------
            case "FBIKRightHandSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_RightHand] = parsed;
                    break;
                }
            case "FBIKRightHandSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_RightHand] = parsed;
                    break;
                }
            case "FBIKRightHandEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_RightHand] = parsed;
                    break;
                }
            case "FBIKRightHandEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_RightHand] = parsed;
                    break;
                }

            // ---------- LEFT LOWER ARM ----------
            case "FBIKLeftLowerArmSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_LeftLowerArm] = parsed;
                    break;
                }
            case "FBIKLeftLowerArmSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_LeftLowerArm] = parsed;
                    break;
                }
            case "FBIKLeftLowerArmEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_LeftLowerArm] = parsed;
                    break;
                }
            case "FBIKLeftLowerArmEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_LeftLowerArm] = parsed;
                    break;
                }

            // ---------- RIGHT LOWER ARM ----------
            case "FBIKRightLowerArmSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_RightLowerArm] = parsed;
                    break;
                }
            case "FBIKRightLowerArmSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_RightLowerArm] = parsed;
                    break;
                }
            case "FBIKRightLowerArmEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_RightLowerArm] = parsed;
                    break;
                }
            case "FBIKRightLowerArmEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_RightLowerArm] = parsed;
                    break;
                }

            // ---------- LEFT TOE ----------
            case "FBIKLeftToeSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_LeftToe] = parsed;
                    break;
                }
            case "FBIKLeftToeSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_LeftToe] = parsed;
                    break;
                }
            case "FBIKLeftToeEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_LeftToe] = parsed;
                    break;
                }
            case "FBIKLeftToeEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_LeftToe] = parsed;
                    break;
                }

            // ---------- RIGHT TOE ----------
            case "FBIKRightToeSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_RightToe] = parsed;
                    break;
                }
            case "FBIKRightToeSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_RightToe] = parsed;
                    break;
                }
            case "FBIKRightToeEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_RightToe] = parsed;
                    break;
                }
            case "FBIKRightToeEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_RightToe] = parsed;
                    break;
                }

            // ---------- LEFT SHOULDER ----------
            case "FBIKLeftShoulderSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_LeftShoulder] = parsed;
                    break;
                }
            case "FBIKLeftShoulderSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_LeftShoulder] = parsed;
                    break;
                }
            case "FBIKLeftShoulderEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_LeftShoulder] = parsed;
                    break;
                }
            case "FBIKLeftShoulderEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_LeftShoulder] = parsed;
                    break;
                }

            // ---------- RIGHT SHOULDER ----------
            case "FBIKRightShoulderSmoothPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothPos[BasisLocalRigDriver.S_RightShoulder] = parsed;
                    break;
                }
            case "FBIKRightShoulderSmoothRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.SmoothRot[BasisLocalRigDriver.S_RightShoulder] = parsed;
                    break;
                }
            case "FBIKRightShoulderEuroPos":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroPos[BasisLocalRigDriver.S_RightShoulder] = parsed;
                    break;
                }
            case "FBIKRightShoulderEuroRot":
                {
                    if (bool.TryParse(optionValue, out var parsed))
                        BasisLocalRigDriver.EuroRot[BasisLocalRigDriver.S_RightShoulder] = parsed;
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

        // Compare against last applied values, so even if _dirty gets missed,
        // we still won't spam applies.
        bool sameAsLast =
            _hasApplied &&
            _lastHeightMode == HeightMode &&
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
        _lastSelectedScale = SelectedScale;
        _lastApplyCustomScale = ApplyCustomScale;

        _dirty = false;

        BasisDebug.Log(
            $"Applied height settings. HeightMode {HeightMode} " +
            $"SelectedScale {SelectedScale}, ApplyCustomScale {ApplyCustomScale}"
        );
    }
}
