using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

public static class BasisLocalHeightCalculator
{
    public static void CalculatePlayerArmSpan()
    {
        // Player arm span (from *devices*) this is wrong. we need to use hand to upper arm length.
        if (BasisDeviceManagement.Instance.FindDevice(out BasisInput leftHand, BasisBoneTrackedRole.LeftHand) && BasisDeviceManagement.Instance.FindDevice(out BasisInput rightHand, BasisBoneTrackedRole.RightHand))
        {
            leftHand.PollData();
            rightHand.PollData();

            var lockToInput = BasisLocalCameraDriver.Instance?.BasisLockToInput;
            Vector3 HeadPosition = Vector3.zero;
            if (lockToInput != null ? lockToInput.BasisInput : null != null)
            {
                lockToInput.BasisInput.PollData();
                HeadPosition = lockToInput.BasisInput.UnscaledDeviceCoord.position;
            }

            Vector3 headFlat = new Vector3(HeadPosition.x, 0f, HeadPosition.z);
            Vector3 leftFlat = new Vector3(leftHand.UnscaledDeviceCoord.position.x, 0f, leftHand.UnscaledDeviceCoord.position.z);
            Vector3 rightFlat = new Vector3(rightHand.UnscaledDeviceCoord.position.x, 0f, rightHand.UnscaledDeviceCoord.position.z);

            float leftArmLength = Vector3.Distance(headFlat, leftFlat);
            float rightArmLength = Vector3.Distance(headFlat, rightFlat);

            float averageArmLength = (leftArmLength + rightArmLength) * 0.5f;
            BasisHeightDriver.PlayerArmSpan = averageArmLength * 2f;
            BasisDebug.Log($"Current Player Arm Span: {BasisHeightDriver.PlayerArmSpan}", BasisDebug.LogTag.Avatar);
        }
        else
        {
            BasisDebug.LogWarning("Both hands were not discovered. Using default player arm span.", BasisDebug.LogTag.Avatar);
            BasisHeightDriver.PlayerArmSpan = BasisHeightDriver.DefaultPlayerArmSpan;
        }
        BasisHeightDriver.ArmRatioPlayerToDefaultScale = BasisHeightDriver.PlayerArmSpan / Mathf.Max(0.0001f, BasisHeightDriver.DefaultPlayerArmSpan);
        BasisDebug.Log($"ArmRatioPlayerToDefaultScale Set To {BasisHeightDriver.ArmRatioPlayerToDefaultScale}", BasisDebug.LogTag.Avatar);
    }
    public static void CalculatePlayerEyeHeight()
    {
        if (SMModuleSitStand.IsSteatedMode)
        {
            BasisDebug.Log("Was Seated Mode taking standard size of 1.7m", BasisDebug.LogTag.Avatar);
            BasisHeightDriver.PlayerEyeHeight = BasisHeightDriver.DefaultPlayerEyeHeight;
        }
        else
        {
            var lockToInput = BasisLocalCameraDriver.Instance?.BasisLockToInput;
            if (lockToInput != null ? lockToInput.BasisInput : null != null)
            {
                lockToInput.BasisInput.PollData();
                BasisHeightDriver.PlayerEyeHeight = lockToInput.BasisInput.UnscaledDeviceCoord.position.y;
                BasisDebug.Log($"Player raw eye height from device: {BasisHeightDriver.PlayerEyeHeight}", BasisDebug.LogTag.Avatar);
            }
            else
            {
                // Prefer avatar eye height if it looks valid; otherwise fall back to default player height.
                float fallback = BasisHeightDriver.AvatarEyeHeight > 0f ? BasisHeightDriver.AvatarEyeHeight : BasisHeightDriver.DefaultPlayerEyeHeight;

                BasisHeightDriver.PlayerEyeHeight = fallback;

                BasisDebug.LogWarning("No attached input found for BasisLockToInput. Using fallback player eye height.", BasisDebug.LogTag.Avatar);
            }
        }
        if (BasisHeightDriver.PlayerEyeHeight <= 0f)
        {
            BasisHeightDriver.PlayerEyeHeight = BasisHeightDriver.DefaultPlayerEyeHeight;
            BasisDebug.LogWarning(
                $"Player eye height was invalid. Set to default: {BasisHeightDriver.DefaultPlayerEyeHeight}",
                BasisDebug.LogTag.Avatar);
        }
        BasisHeightDriver.EyeRatioPlayerToDefaultScale = BasisHeightDriver.PlayerEyeHeight / Mathf.Max(0.0001f, BasisHeightDriver.DefaultPlayerEyeHeight);
        BasisDebug.Log($"EyeRatioPlayerToDefaultScale Set To {BasisHeightDriver.EyeRatioPlayerToDefaultScale}", BasisDebug.LogTag.Avatar);
    }
    public static void CalculateAvatarEyeHeight()
    {
        BasisLocalPlayer Local = BasisLocalPlayer.Instance;
        var avatarDriver = Local.LocalAvatarDriver;
        if (avatarDriver != null)
        {
            BasisHeightDriver.AvatarEyeHeight = avatarDriver.ActiveAvatarEyeHeight();
            BasisHeightDriver.AvatarEyeHeight = BasisHeightDriver.AvatarEyeHeight > 0f ? BasisHeightDriver.AvatarEyeHeight : BasisHeightDriver.DefaultAvatarEyeHeight;
        }
        else
        {
            BasisDebug.LogWarning("LocalAvatarDriver not available. Using default avatar eye height.", BasisDebug.LogTag.Avatar);
            BasisHeightDriver.AvatarEyeHeight = BasisHeightDriver.DefaultAvatarEyeHeight;
        }
        if (BasisHeightDriver.AvatarEyeHeight <= 0f)
        {
            BasisHeightDriver.AvatarEyeHeight = BasisHeightDriver.DefaultAvatarEyeHeight;
            BasisDebug.LogWarning($"Avatar eye height was invalid. Set to default: {BasisHeightDriver.DefaultAvatarEyeHeight}", BasisDebug.LogTag.Avatar);
        }
        BasisHeightDriver.EyeRatioAvatarToAvatarDefaultScale = BasisHeightDriver.AvatarEyeHeight / Mathf.Max(0.0001f, BasisHeightDriver.DefaultAvatarEyeHeight);
        BasisDebug.Log($"EyeRatioAvatarToAvatarDefaultScale Set To {BasisHeightDriver.EyeRatioAvatarToAvatarDefaultScale}", BasisDebug.LogTag.Avatar);
    }
    public static void CalculateAvatarArmSpan()
    {
        BasisLocalPlayer Local = BasisLocalPlayer.Instance;
        var boneDriver = Local.LocalBoneDriver;

        boneDriver.FindBone(out var HeadBone, BasisBoneTrackedRole.Head);

        if (boneDriver != null && boneDriver.FindBone(out var leftHandBone, BasisBoneTrackedRole.LeftHand) && boneDriver.FindBone(out var rightHandBone, BasisBoneTrackedRole.RightHand))
        {
            BasisHeightDriver.AvatarArmSpan = Vector3.Distance(leftHandBone.TposeLocalScaled.position, rightHandBone.TposeLocalScaled.position);

            Vector3 HeadPosition = HeadBone.TposeLocalScaled.position;
            Vector3 headFlat = new Vector3(HeadPosition.x, 0f, HeadPosition.z);
            Vector3 leftFlat = new Vector3(leftHandBone.TposeLocalScaled.position.x, 0f, leftHandBone.TposeLocalScaled.position.z);
            Vector3 rightFlat = new Vector3(rightHandBone.TposeLocalScaled.position.x, 0f, rightHandBone.TposeLocalScaled.position.z);

            float leftArmLength = Vector3.Distance(headFlat, leftFlat);
            float rightArmLength = Vector3.Distance(headFlat, rightFlat);

            float averageArmLength = (leftArmLength + rightArmLength) * 0.5f;
            BasisHeightDriver.AvatarArmSpan = averageArmLength * 2f;


            BasisDebug.Log($"Current Avatar Arm Span: {BasisHeightDriver.AvatarArmSpan}", BasisDebug.LogTag.Avatar);
        }
        else
        {
            BasisDebug.LogWarning("Could not resolve avatar hand bones; using default avatar arm span.", BasisDebug.LogTag.Avatar);
            BasisHeightDriver.AvatarArmSpan = BasisHeightDriver.DefaultAvatarArmSpan;
        }
        BasisHeightDriver.ArmRatioAvatarToAvatarDefaultScale = BasisHeightDriver.AvatarArmSpan / Mathf.Max(0.0001f, BasisHeightDriver.DefaultAvatarArmSpan);
        BasisDebug.Log($"ArmRatioAvatarToAvatarDefaultScale Set To {BasisHeightDriver.ArmRatioAvatarToAvatarDefaultScale}", BasisDebug.LogTag.Avatar);
    }
}
