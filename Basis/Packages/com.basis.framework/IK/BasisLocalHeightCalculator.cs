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
        bool hasLeftHand = false;
        bool hasRightHand = false;
        Vector3 HeadPosition = new Vector3(0, 1.6f, 0);

        if (BasisDeviceManagement.Instance.FindDevice(out BasisInput leftHand, BasisBoneTrackedRole.LeftHand))
        {
            hasLeftHand = true;
            leftHand.PollData();
        }
        if (BasisDeviceManagement.Instance.FindDevice(out BasisInput rightHand, BasisBoneTrackedRole.RightHand))
        {
            hasRightHand = true;
            rightHand.PollData();
        }
        var lockToInput = BasisLocalCameraDriver.Instance?.BasisLockToInput;
        if (lockToInput != null && lockToInput.BasisInput != null)
        {
            lockToInput.BasisInput.PollData();
            HeadPosition = lockToInput.BasisInput.UnscaledDeviceCoord.position;
        }
        else
        {
            BasisDebug.LogError("Missing Head During Arm Span calculation");
        }
        if (hasLeftHand || hasRightHand)
        {
            if (hasLeftHand == false)
            {
                leftHand = rightHand;
            }
            if (hasRightHand == false)
            {
                rightHand = leftHand;
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
            BasisHeightDriver.PlayerArmSpan = BasisHeightDriver.FallbackHeightInMeters;
        }
    }
    public static void CalculatePlayerEyeHeight()
    {
        if (SMModuleSitStand.IsSteatedMode)
        {
            BasisDebug.Log("Was Seated Mode taking standard size of 1.7m", BasisDebug.LogTag.Avatar);
            BasisHeightDriver.PlayerEyeHeight = BasisHeightDriver.FallbackHeightInMeters;
        }
        else
        {
            var lockToInput = BasisLocalCameraDriver.Instance?.BasisLockToInput;
            if (lockToInput != null && lockToInput.BasisInput != null)
            {
                lockToInput.BasisInput.PollData();
                BasisHeightDriver.PlayerEyeHeight = lockToInput.BasisInput.UnscaledDeviceCoord.position.y;
                BasisDebug.Log($"Player raw eye height from device: {BasisHeightDriver.PlayerEyeHeight}", BasisDebug.LogTag.Avatar);
            }
            else
            {
                // Prefer avatar eye height if it looks valid; otherwise fall back to default player height.
                float fallback = BasisHeightDriver.AvatarEyeHeight > 0f ? BasisHeightDriver.AvatarEyeHeight : BasisHeightDriver.FallbackHeightInMeters;

                BasisHeightDriver.PlayerEyeHeight = fallback;

                BasisDebug.LogWarning("No attached input found for BasisLockToInput. Using fallback player eye height.", BasisDebug.LogTag.Avatar);
            }
        }
        if (BasisHeightDriver.PlayerEyeHeight <= 0f)
        {
            BasisHeightDriver.PlayerEyeHeight = BasisHeightDriver.FallbackHeightInMeters;
            BasisDebug.LogWarning(
                $"Player eye height was invalid. Set to default: {BasisHeightDriver.FallbackHeightInMeters}",
                BasisDebug.LogTag.Avatar);
        }
    }
    public static void CalculateAvatarEyeHeight()
    {
        BasisLocalPlayer Local = BasisLocalPlayer.Instance;
        if (Local == null)
        {
            BasisDebug.LogError("Missing BasisLocalPlayer");
            return;
        }
        BasisHeightDriver.AvatarEyeHeight = Local.LocalAvatarDriver.ActiveAvatarEyeHeight();
        BasisHeightDriver.AvatarEyeHeight = BasisHeightDriver.AvatarEyeHeight > 0f ? BasisHeightDriver.AvatarEyeHeight : BasisHeightDriver.FallbackHeightInMeters;
        if (BasisHeightDriver.AvatarEyeHeight <= 0f)
        {
            BasisHeightDriver.AvatarEyeHeight = BasisHeightDriver.FallbackHeightInMeters;
            BasisDebug.LogWarning($"Avatar eye height was invalid. Set to default: {BasisHeightDriver.FallbackHeightInMeters}", BasisDebug.LogTag.Avatar);
        }
    }
    public static void CalculateAvatarArmSpan()
    {
        BasisLocalPlayer Local = BasisLocalPlayer.Instance;
        if (Local == null)
        {
            BasisDebug.LogError("Missing BasisLocalPlayer");
            return;
        }
        var boneDriver = Local.LocalBoneDriver;
        boneDriver.FindBone(out var HeadBone, BasisBoneTrackedRole.Head);
        boneDriver.FindBone(out var leftHandBone, BasisBoneTrackedRole.LeftHand);
        boneDriver.FindBone(out var rightHandBone, BasisBoneTrackedRole.RightHand);

        Vector3 HeadPosition = HeadBone.TposeLocal.position;
        Vector3 headFlat = new Vector3(HeadPosition.x, 0f, HeadPosition.z);
        Vector3 leftFlat = new Vector3(leftHandBone.TposeLocal.position.x, 0f, leftHandBone.TposeLocal.position.z);
        Vector3 rightFlat = new Vector3(rightHandBone.TposeLocal.position.x, 0f, rightHandBone.TposeLocal.position.z);

        float leftArmLength = Vector3.Distance(headFlat, leftFlat);
        float rightArmLength = Vector3.Distance(headFlat, rightFlat);

        float averageArmLength = (leftArmLength + rightArmLength) * 0.5f;
        BasisHeightDriver.AvatarArmSpan = averageArmLength * 2f;
        BasisDebug.Log($"Current Avatar Arm Span: {BasisHeightDriver.AvatarArmSpan}", BasisDebug.LogTag.Avatar);
    }
}
