using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
public static class BasisHeightDriver
{
    public static void SetCustomHeight(bool UseCustomHeight,float customAvatarEyeHeight, float customPlayerHeight,float SelectedScale, bool ScaleAvatar)
    {
        if (customAvatarEyeHeight <= 0f)
        {
            BasisDebug.LogError("Invalid AvatarEye height. Must be greater than zero.", BasisDebug.LogTag.Avatar);
            return;
        }

        if (customPlayerHeight <= 0f)
        {
            BasisDebug.LogError("Invalid Player height. Must be greater than zero.", BasisDebug.LogTag.Avatar);
            return;
        }

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
        CaptureAvatarHeight();
        CapturePlayerHeight();

        // use the (now) known unscaled avatar eye height as baseline
        float baselineAvatarEyeHeight = AvatarEyeHeight;
        if (baselineAvatarEyeHeight <= 0f)
        {
            BasisDebug.LogError("Invalid baseline avatar eye height after recalculation. Cannot compute scale.", BasisDebug.LogTag.Avatar);
            baselineAvatarEyeHeight = 1.6f;
        }
        // compute and apply scale
        heightScaleFactor = SelectedScale / baselineAvatarEyeHeight;

        BasisDebug.Log($"Applying Scale to Avatar {heightScaleFactor}", BasisDebug.LogTag.Avatar);

        if (UseCustomHeight)
        {
            BasisDebug.Log($"Setting custom player eye height: {CustomAvatarEyeHeight}", BasisDebug.LogTag.Avatar);
            CustomAvatarEyeHeight = customAvatarEyeHeight;
            CustomPlayerEyeHeight = customPlayerHeight;
            // choose which ratios to apply for the selected mode
            ChooseHeightToUse(BasisSelectedHeightMode.Custom);
        }
        else
        {
            ChooseHeightToUse(SMModuleCalibration.HeightMode);
        }
        if (ScaleAvatar)
        {

            ApplyScaleModification(heightScaleFactor,avatarDriver,boneDriver);
        }
        else
        {
            ApplyScaleModification(1, avatarDriver, boneDriver);
        }
        BasisLocalPlayer.Instance.ExecuteNextFrame(() =>
        {
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame?.Invoke();
        });
    }
    public static void ApplyScaleModification(float heightScaleFactor,BasisLocalAvatarDriver avatarDriver,BasisLocalBoneDriver boneDriver)
    {
        BasisDebug.Log($"Height Scaling Factor is {heightScaleFactor}", BasisDebug.LogTag.Avatar);
        avatarDriver.ScaleAvatarModification.SetAvatarheightOverride(heightScaleFactor);

        // rescale bone-space TPose transforms
        int count = boneDriver.ControlsLength;
        for (int Index = 0; Index < count; Index++)
        {
            BasisLocalBoneControl c = boneDriver.Controls[Index];
            c.TposeLocalScaled.position = c.TposeLocal.position * heightScaleFactor;
            c.TposeLocalScaled.rotation = c.TposeLocal.rotation;
            c.ScaledOffset = c.Offset * heightScaleFactor;
        }
    }
    public static void CapturePlayerHeight()
    {
        BasisLocalHeightCalculator.CalculatePlayerEyeHeight();
        BasisLocalHeightCalculator.CalculatePlayerArmSpan();
    }
    public static void CaptureAvatarHeight()
    {
        BasisLocalHeightCalculator.CalculateAvatarEyeHeight();
        BasisLocalHeightCalculator.CalculateAvatarArmSpan();
    }
    public static float heightScaleFactor = 1;
    public static float SelectedAvatarToAvatarDefaultScale { get => selectedAvatarToAvatarDefaultScale;  set => selectedAvatarToAvatarDefaultScale = value; }
    public static float SelectedPlayerToDefaultScale { get => selectedPlayerToDefaultScale;  set => selectedPlayerToDefaultScale = value; }
    public static float SelectedAvatarHeight { get => selectedAvatarHeight;  set => selectedAvatarHeight = value; }
    public static float SelectedPlayerHeight { get => selectedPlayerHeight;  set => selectedPlayerHeight = value; }
    public static float ArmRatioAvatarToAvatarDefaultScale { get => armRatioAvatarToAvatarDefaultScale;  set => armRatioAvatarToAvatarDefaultScale = value; }
    public static float ArmRatioPlayerToDefaultScale { get => armRatioPlayerToDefaultScale;  set => armRatioPlayerToDefaultScale = value; }
    public static float EyeRatioAvatarToAvatarDefaultScale { get => eyeRatioAvatarToAvatarDefaultScale;  set => eyeRatioAvatarToAvatarDefaultScale = value; }
    public static float EyeRatioPlayerToDefaultScale { get => eyeRatioPlayerToDefaultScale;  set => eyeRatioPlayerToDefaultScale = value; }
    public static BasisSelectedHeightMode LastUsedHeightMode = BasisSelectedHeightMode.EyeHeight;
    /// <summary>
    /// Chooses the active height metrics and scale ratios based on the provided mode.
    /// </summary>
    /// <param name="Height">Selection mode: <see cref="BasisSelectedHeightMode.ArmSpan"/>,
    /// <see cref="BasisSelectedHeightMode.EyeHeight"/>, or <see cref="BasisSelectedHeightMode.Custom"/>.</param>
    public static void ChooseHeightToUse(BasisSelectedHeightMode Height)
    {
        LastUsedHeightMode = Height;
        switch (Height)
        {
            case BasisSelectedHeightMode.ArmSpan:
                SelectedPlayerHeight = PlayerArmSpan;
                SelectedAvatarHeight = AvatarArmSpan;
                SelectedPlayerToDefaultScale = ArmRatioPlayerToDefaultScale;
                SelectedAvatarToAvatarDefaultScale = ArmRatioAvatarToAvatarDefaultScale;
                break;
            case BasisSelectedHeightMode.EyeHeight:
                SelectedPlayerHeight = PlayerEyeHeight;
                SelectedAvatarHeight = AvatarEyeHeight;
                SelectedPlayerToDefaultScale = EyeRatioPlayerToDefaultScale;
                SelectedAvatarToAvatarDefaultScale = EyeRatioAvatarToAvatarDefaultScale;
                break;
            case BasisSelectedHeightMode.Custom:
                SelectedPlayerHeight = CustomPlayerEyeHeight;
                SelectedAvatarHeight = CustomAvatarEyeHeight;
                SelectedPlayerToDefaultScale = SelectedPlayerHeight / DefaultAvatarEyeHeight;
                SelectedAvatarToAvatarDefaultScale = SelectedAvatarHeight / DefaultPlayerEyeHeight;
                break;
        }
        BasisDebug.Log($"Height Mode is {Height} with height {SelectedPlayerHeight} with avatar height {SelectedAvatarHeight} with selected player to default scale {SelectedPlayerToDefaultScale} select avatar to avatar scale {SelectedAvatarToAvatarDefaultScale}", BasisDebug.LogTag.Avatar);
    }
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
    /// Default measured eye height for the avatar (meters).
    /// </summary>
    public static float DefaultAvatarEyeHeight = FallbackSizeInMeters;

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
    /// Custom avatar eye height (meters) supplied by user or calibration UI.
    /// </summary>
    public static float CustomAvatarEyeHeight = FallbackSizeInMeters;

    /// <summary>
    /// Ratio mapping the player's measured eye height to a default reference scale.
    /// </summary>
    private static float eyeRatioPlayerToDefaultScale = 1f;

    /// <summary>
    /// Ratio mapping the avatar's measured eye height to the avatar's default reference scale.
    /// </summary>
    private static float eyeRatioAvatarToAvatarDefaultScale = 1f; // should be used for the player

    /// <summary>
    /// Ratio mapping the player's measured arm span to a default reference scale.
    /// </summary>
    private static float armRatioPlayerToDefaultScale = 1f;

    /// <summary>
    /// Ratio mapping the avatar's measured arm span to the avatar's default reference scale.
    /// </summary>
    private static float armRatioAvatarToAvatarDefaultScale = 1f; // should be used for the player

    /// <summary>
    /// The player height (meters)"/>.
    /// </summary>
    private static float selectedPlayerHeight = FallbackSizeInMeters;

    /// <summary>
    /// The avatar height (meters)/>.
    /// </summary>
    private static float selectedAvatarHeight = FallbackSizeInMeters;

    /// <summary>
    /// The player-to-default scale/>.
    /// </summary>
    private static float selectedPlayerToDefaultScale = 1f;

    /// <summary>
    /// The avatar-to-avatar-default scale currently"/>.
    /// </summary>
    private static float selectedAvatarToAvatarDefaultScale = 1f;
}
