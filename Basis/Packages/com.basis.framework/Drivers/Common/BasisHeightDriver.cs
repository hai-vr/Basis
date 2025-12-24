using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
public static class BasisHeightDriver
{
    public static void ApplyScaleAndHeight()
    {
        RevaluateUnscaledHeight(SMModuleCalibration.HeightMode);
        ApplyScale(SMModuleCalibration.ApplyCustomScale, SMModuleCalibration.SelectedScale);
        ChooseHeightToUse(SMModuleCalibration.HeightMode);
    }
    public static void OnAvatarFBCalibration()
    {
        CapturePlayerHeight();
        ApplyScaleAndHeight();
    }
    public static void ApplyScale(bool ScaleAvatar,float SelectedScale)
    {
        // compute and apply scale
        heightScaleFactor = SelectedScale / UnScaledSelectedAvatarHeight;

        BasisDebug.Log($"Applying Scale to Avatar {heightScaleFactor}", BasisDebug.LogTag.Avatar);
        if (!ScaleAvatar)
        {
            heightScaleFactor = 1;
        }
        ApplyAvatarScale(heightScaleFactor);
        BasisLocalPlayer.Instance.ExecuteNextFrame(() =>
        {
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame?.Invoke();
        });
    }
    /// <summary>
    /// 1 = normal
    /// </summary>
    /// <param name="ScaleFactor"></param>
    public static void ApplyAvatarScale(float ScaleFactor)
    {
        var player = BasisLocalPlayer.Instance;
        if (player == null)
        {
            BasisDebug.LogError("No local player instance.", BasisDebug.LogTag.Avatar);
            return;
        }
        var avatarDriver = player.LocalAvatarDriver;
        var boneDriver = player.LocalBoneDriver;

        if (avatarDriver == null || boneDriver == null)
        {
            BasisDebug.LogError("Avatar or Bone driver missing; cannot apply custom height.", BasisDebug.LogTag.Avatar);
            return;
        }
        BasisDebug.Log($"Height Scaling Factor is {ScaleFactor}", BasisDebug.LogTag.Avatar);
        avatarDriver.ScaleAvatarModification.SetAvatarheightOverride(ScaleFactor);
        int count = boneDriver.ControlsLength;
        for (int Index = 0; Index < count; Index++)
        {
            //we scale up the local tpose data so it matches (avatar related still)
            BasisLocalBoneControl c = boneDriver.Controls[Index];
            c.TposeLocalScaled.position = c.TposeLocal.position * ScaleFactor;
            c.TposeLocalScaled.rotation = c.TposeLocal.rotation;
            c.ScaledOffset = c.Offset * ScaleFactor;
        }
    }
    /// <summary>
    /// we always capture the right player height as we only use unscaled data.
    /// </summary>
    public static void CapturePlayerHeight()
    {
        BasisLocalHeightCalculator.CalculatePlayerEyeHeight();
        BasisLocalHeightCalculator.CalculatePlayerArmSpan();
    }
    /// <summary>
    /// captures the avatar scale
    /// it does this by first scaling the avatar back to its original size and then up from that.
    /// </summary>
    public static void CaptureAvatarHeight()
    {
        var player = BasisLocalPlayer.Instance;
        if (player == null)
        {
            BasisDebug.LogError("No local player instance.", BasisDebug.LogTag.Avatar);
            return;
        }
        var avatarDriver = player.LocalAvatarDriver;
        if (avatarDriver == null)
        {
            BasisDebug.LogError("Avatar or Bone driver missing; cannot apply custom height.", BasisDebug.LogTag.Avatar);
            return;
        }
        float ApplyScale = avatarDriver.ScaleAvatarModification.ApplyScale;
        ApplyAvatarScale(1);//we set the avatar scale to 1 to grab good arm spans
        BasisLocalHeightCalculator.CalculateAvatarEyeHeight();
        BasisLocalHeightCalculator.CalculateAvatarArmSpan();
        ApplyAvatarScale(ApplyScale);
        BasisLocalPlayer.Instance.ExecuteNextFrame(() =>
        {
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame?.Invoke();
        });
    }
    public static void UpdateRatios()
    {
        BasisHeightDriver.EyeRatioAvatarToAvatarDefaultScale = BasisHeightDriver.AvatarEyeHeight / Mathf.Max(0.0001f, BasisHeightDriver.FallbackSizeInMeters);
        BasisDebug.Log($"EyeRatioAvatarToAvatarDefaultScale Set To {BasisHeightDriver.EyeRatioAvatarToAvatarDefaultScale}", BasisDebug.LogTag.Avatar);
        BasisHeightDriver.EyeRatioPlayerToDefaultScale = BasisHeightDriver.PlayerEyeHeight / Mathf.Max(0.0001f, BasisHeightDriver.DefaultPlayerEyeHeight);
        BasisDebug.Log($"EyeRatioPlayerToDefaultScale Set To {BasisHeightDriver.EyeRatioPlayerToDefaultScale}", BasisDebug.LogTag.Avatar);
        BasisHeightDriver.ArmRatioPlayerToDefaultScale = BasisHeightDriver.PlayerArmSpan / Mathf.Max(0.0001f, BasisHeightDriver.DefaultPlayerArmSpan);
        BasisDebug.Log($"ArmRatioPlayerToDefaultScale Set To {BasisHeightDriver.ArmRatioPlayerToDefaultScale}", BasisDebug.LogTag.Avatar);
        BasisHeightDriver.ArmRatioAvatarToAvatarDefaultScale = BasisHeightDriver.AvatarArmSpan / Mathf.Max(0.0001f, BasisHeightDriver.DefaultAvatarArmSpan);
        BasisDebug.Log($"ArmRatioAvatarToAvatarDefaultScale Set To {BasisHeightDriver.ArmRatioAvatarToAvatarDefaultScale}", BasisDebug.LogTag.Avatar);
    }
    public static void RevaluateUnscaledHeight(BasisSelectedHeightMode Height)
    {
        switch (Height)
        {
            case BasisSelectedHeightMode.ArmSpan:
                UnScaledSelectedAvatarHeight = AvatarArmSpan;
                break;
            case BasisSelectedHeightMode.EyeHeight:
                UnScaledSelectedAvatarHeight = AvatarEyeHeight;
                break;
            case BasisSelectedHeightMode.Custom://currently unusued while we fix everything else
                UnScaledSelectedAvatarHeight = AvatarEyeHeight;
                break;
        }
    }
    /// <summary>
    /// Chooses the active height metrics and scale ratios based on the provided mode.
    /// </summary>
    /// <param name="Height">Selection mode: <see cref="BasisSelectedHeightMode.ArmSpan"/>,
    /// <see cref="BasisSelectedHeightMode.EyeHeight"/>, or <see cref="BasisSelectedHeightMode.Custom"/>.</param>
    public static void ChooseHeightToUse(BasisSelectedHeightMode Height)
    {
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            Height = BasisSelectedHeightMode.EyeHeight;
        }
        UpdateRatios();
        var player = BasisLocalPlayer.Instance;
        if (player == null)
        {
            BasisDebug.LogError("No local player instance.", BasisDebug.LogTag.Avatar);
            return;
        }
        var avatarDriver = player.LocalAvatarDriver;
        if (avatarDriver == null)
        {
            BasisDebug.LogError("Avatar or Bone driver missing; cannot apply custom height.", BasisDebug.LogTag.Avatar);
            return;
        }
        float ApplyScale = avatarDriver.ScaleAvatarModification.ApplyScale;
        switch (Height)
        {
            case BasisSelectedHeightMode.ArmSpan:
                SelectedPlayerHeight = PlayerArmSpan * ApplyScale;
                SelectedAvatarHeight = AvatarArmSpan * ApplyScale;
                SelectedPlayerToDefaultScale = ArmRatioPlayerToDefaultScale * ApplyScale;
                SelectedAvatarToAvatarDefaultScale = ArmRatioAvatarToAvatarDefaultScale * ApplyScale;

                UnScaledSelectedAvatarHeight = AvatarArmSpan;
                break;
            case BasisSelectedHeightMode.EyeHeight:
                SelectedPlayerHeight = PlayerEyeHeight * ApplyScale;
                SelectedAvatarHeight = AvatarEyeHeight * ApplyScale;
                SelectedPlayerToDefaultScale = EyeRatioPlayerToDefaultScale * ApplyScale;
                SelectedAvatarToAvatarDefaultScale = EyeRatioAvatarToAvatarDefaultScale * ApplyScale;

                UnScaledSelectedAvatarHeight = AvatarEyeHeight;
                break;
            case BasisSelectedHeightMode.Custom://currently unusued while we fix everything else
                SelectedPlayerHeight = CustomPlayerEyeHeight * ApplyScale;
                SelectedAvatarHeight = AvatarEyeHeight * ApplyScale;
                SelectedPlayerToDefaultScale = (SelectedPlayerHeight / FallbackSizeInMeters) * ApplyScale;
                SelectedAvatarToAvatarDefaultScale = (SelectedPlayerHeight / FallbackSizeInMeters) * ApplyScale;

                UnScaledSelectedAvatarHeight = AvatarEyeHeight;
                break;
        }
        if (SelectedPlayerHeight <= 0f)
        {
            SelectedPlayerHeight = 1.6f;
        }
        if (SelectedAvatarHeight <= 0f)
        {
            SelectedAvatarHeight = 1.6f;
        }
        if (SelectedPlayerToDefaultScale <= 0f)
        {
            SelectedPlayerToDefaultScale = 1;
        }
        if (SelectedAvatarToAvatarDefaultScale <= 0f)
        {
            SelectedAvatarToAvatarDefaultScale = 1;
        }
        BasisDebug.Log($"Height Mode is {Height} with height {SelectedPlayerHeight} with avatar height {SelectedAvatarHeight} with selected player to default scale {SelectedPlayerToDefaultScale} select avatar to avatar scale {SelectedAvatarToAvatarDefaultScale}", BasisDebug.LogTag.Avatar);
    }
    public static float heightScaleFactor = 1;
    /// <summary>
    /// Fallback height (meters) used when no measurement is available.
    /// not the total height but the eye height
    /// </summary>
    public const float FallbackSizeInMeters = 1.61f;

    /// <summary>
    /// Default measured eye height for the player (meters).
    /// </summary>
    public static float DefaultPlayerEyeHeight = FallbackSizeInMeters;

    /// <summary>
    /// Default measured arm span for the player (meters).
    /// </summary>
    public static float DefaultPlayerArmSpan = FallbackSizeInMeters;

    /// <summary>
    /// Default measured arm span for the avatar (meters).
    /// </summary>
    public static float DefaultAvatarArmSpan = FallbackSizeInMeters;

    /// <summary>
    /// Measured eye height for the player (meters). Defaults to <see cref="BasisLocalPlayer.FallbackSize"/>.
    /// </summary>
    public static float PlayerEyeHeight = FallbackSizeInMeters;

    /// <summary>
    /// Measured eye height for the avatar (meters). Defaults to <see cref="BasisLocalPlayer.FallbackSize"/>.
    /// </summary>
    public static float AvatarEyeHeight = FallbackSizeInMeters;

    /// <summary>
    /// Measured arm span for the player (meters). Defaults to <see cref="BasisLocalPlayer.FallbackSize"/>.
    /// </summary>
    public static float PlayerArmSpan = FallbackSizeInMeters;

    /// <summary>
    /// Measured arm span for the avatar (meters). Defaults to <see cref="BasisLocalPlayer.FallbackSize"/>.
    /// </summary>
    public static float AvatarArmSpan = FallbackSizeInMeters;

    /// <summary>
    /// Custom player eye height (meters) supplied by user or calibration UI.
    /// </summary>
    public static float CustomPlayerEyeHeight = FallbackSizeInMeters;

    /// <summary>
    /// Ratio mapping the player's measured eye height to a default reference scale.
    /// </summary>
    public static float EyeRatioPlayerToDefaultScale = 1f;

    /// <summary>
    /// Ratio mapping the avatar's measured eye height to the avatar's default reference scale.
    /// </summary>
    public static float EyeRatioAvatarToAvatarDefaultScale = 1f; // should be used for the player

    /// <summary>
    /// Ratio mapping the player's measured arm span to a default reference scale.
    /// </summary>
    public static float ArmRatioPlayerToDefaultScale = 1f;

    /// <summary>
    /// Ratio mapping the avatar's measured arm span to the avatar's default reference scale.
    /// </summary>
    public static float ArmRatioAvatarToAvatarDefaultScale = 1f; // should be used for the player

    /// <summary>
    /// The player height (meters)"/>.
    /// </summary>
    public static float SelectedPlayerHeight = FallbackSizeInMeters;

    /// <summary>
    /// The avatar height (meters)/>.
    /// </summary>
    public static float SelectedAvatarHeight = FallbackSizeInMeters;

    /// <summary>
    /// The avatar height (meters)/>.
    /// </summary>
    public static float UnScaledSelectedAvatarHeight = FallbackSizeInMeters;
    /// <summary>
    /// The player-to-default scale/>.
    /// </summary>
    public static float SelectedPlayerToDefaultScale = 1f;

    /// <summary>
    /// The avatar-to-avatar-default scale currently"/>.
    /// </summary>
    public static float SelectedAvatarToAvatarDefaultScale = 1f;
}
