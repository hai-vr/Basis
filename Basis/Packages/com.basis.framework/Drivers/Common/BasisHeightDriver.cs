using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

/// <summary>
/// Centralized height/scale orchestration for the local player avatar.
///
/// This class is responsible for:
/// - Capturing player measurements (eye height and arm span) from tracking data.
/// - Capturing avatar measurements (eye height and arm span) from the avatar rig in an *unscaled* reference state.
/// - Computing an avatar scale factor that maps an avatar measurement to a desired target measurement.
/// - Computing useful ratios for converting values between:
///     - player-relative space,
///     - avatar-relative space,
///     - and a project "default" reference height.
///
/// ⚠️ Terminology:
/// - "Height" in this file generally refers to *eye height* (meters), not full standing height.
/// - "Scaled" means after the avatar's current applied scale (ApplyScale) is considered.
/// - "Unscaled" means avatar measurements taken with the avatar scale forced to 1.0.
/// </summary>
public static class BasisHeightDriver
{
    /// <summary>
    /// Fallback eye height (meters) used when no valid measurement is available.
    ///
    /// This is a *reference eye height*, not total body height.
    /// Many ratios in this class are computed relative to this constant.
    /// </summary>
    public const float FallbackHeightInMeters = 1.61f;

    /// <summary>
    /// High-level entry point: recompute the unscaled avatar measurement selection,
    /// apply avatar scale (if enabled), then compute the active height metrics and ratios
    /// for the chosen height mode.
    /// </summary>
    public static void ApplyScaleAndHeight()
    {
        RevaluateUnscaledHeight(SMModuleCalibration.HeightMode);
        ApplyScale(SMModuleCalibration.ApplyCustomScale, SMModuleCalibration.SelectedScale);
        ChooseHeightToUse(SMModuleCalibration.HeightMode);
    }

    /// <summary>
    /// Call when the avatar is going through full-body calibration.
    /// Captures player measurements first (unscaled by avatar), then updates scale and ratios.
    /// </summary>
    public static void OnAvatarFBCalibration()
    {
        CapturePlayerHeight();
        ApplyScaleAndHeight();
    }

    /// <summary>
    /// Applies a custom avatar scale based on a target measurement.
    ///
    /// <paramref name="ScaleAvatar"/> controls whether scaling is applied.
    /// <paramref name="SelectedScale"/> is the target measurement in meters (e.g., desired eye height or arm span).
    ///
    /// Internally:
    /// - <see cref="SelectedUnScaledAvatarHeight"/> is used as the divisor (unscaled reference).
    /// - The scale factor is computed as: target / unscaledAvatarMeasurement.
    /// - Then <see cref="ApplyAvatarScale(float)"/> propagates the factor into the avatar systems.
    /// </summary>
    /// <param name="ScaleAvatar">If false, scaling is forced to 1.0.</param>
    /// <param name="SelectedScale">Target measurement in meters.</param>
    public static void ApplyScale(bool ScaleAvatar, float SelectedScale)
    {
        // Compute and apply scale.
        // We prefer the currently selected unscaled avatar metric, but fall back if invalid.
        var unscaled = SelectedUnScaledAvatarHeight;
        if (!(unscaled > 0f) || float.IsNaN(unscaled) || float.IsInfinity(unscaled))
        {
            unscaled = FallbackHeightInMeters;
        }

        ScaledToMatchValue = SelectedScale / unscaled;

        BasisDebug.Log($"Applying Scale to Avatar {ScaledToMatchValue}", BasisDebug.LogTag.Avatar);

        if (!ScaleAvatar)
        {
            ScaledToMatchValue = 1f;
        }

        ApplyAvatarScale(ScaledToMatchValue);

        // Notify next frame so listeners read consistent updated values.
        BasisLocalPlayer.Instance.ExecuteNextFrame(() =>
        {
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame?.Invoke(HeightModeChange.ScaleOnly);
        });
    }
    public enum HeightModeChange
    {
        ScaleOnly,
        ScaleAndMode
    }
    /// <summary>
    /// Applies a scale factor to the local avatar and updates any cached bone offsets
    /// that must remain consistent with the avatar's scale.
    ///
    /// ScaleFactor:
    /// - 1.0 = default (no scaling)
    /// - &gt;1.0 = larger avatar
    /// - &lt;1.0 = smaller avatar
    /// </summary>
    /// <param name="ScaleFactor">Multiplier applied to avatar scale-related data.</param>
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

        // The avatar driver owns the authoritative scale override.
        avatarDriver.ScaleAvatarModification.SetAvatarheightOverride(ScaleFactor);

        // Update cached per-bone data that is expected to be in "scaled" space.
        int count = boneDriver.ControlsLength;
        for (int Index = 0; Index < count; Index++)
        {
            // We scale the local T-pose position and offsets so downstream math can use scaled-space values.
            BasisLocalBoneControl c = boneDriver.Controls[Index];
            c.TposeLocalScaled.position = c.TposeLocal.position * ScaleFactor;
            c.TposeLocalScaled.rotation = c.TposeLocal.rotation;
            c.ScaledOffset = c.Offset * ScaleFactor;
        }
    }

    /// <summary>
    /// Captures player measurements from tracking data.
    ///
    /// Notes:
    /// - These represent the *player* (real-world) metrics, not the avatar.
    /// - Values are expected to be in meters.
    /// </summary>
    public static void CapturePlayerHeight()
    {
        BasisDebug.Log("Capturing Player Height", BasisDebug.LogTag.IK);
        BasisLocalHeightCalculator.CalculatePlayerEyeHeight();
        BasisLocalHeightCalculator.CalculatePlayerArmSpan();
        BasisLocalHeightCalculator.ValidateEyeToArmSizesPlayer();
    }

    /// <summary>
    /// Captures avatar measurements in an unscaled reference state.
    ///
    /// The avatar is temporarily forced to scale = 1.0 to ensure measurements represent the rig's
    /// true authored proportions (unscaled space). After measurement, the previous scale is restored.
    ///
    /// Measurements captured:
    /// - <see cref="AvatarEyeHeight"/>
    /// - <see cref="AvatarArmSpan"/>
    ///
    /// Also runs <see cref="BasisLocalHeightCalculator.ValidateEyeToArmSizesAvatar"/> to sanity-check the rig.
    /// </summary>
    public static void CaptureAvatarHeightDuringTpose()
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

        // Preserve the currently applied scale, measure at scale=1, then restore.
        AppliedUpScale = avatarDriver.ScaleAvatarModification.ApplyScale;

        ApplyAvatarScale(1f); // Force unscaled to capture correct baseline measurements.

        BasisLocalHeightCalculator.CalculateAvatarEyeHeight();
        BasisLocalHeightCalculator.CalculateAvatarArmSpan();
        BasisLocalHeightCalculator.ValidateEyeToArmSizesAvatar();

        ApplyAvatarScale(AppliedUpScale);

        // Notify next frame so listeners read consistent updated values.
        BasisLocalPlayer.Instance.ExecuteNextFrame(() =>
        {
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame?.Invoke(HeightModeChange.ScaleAndMode);
        });
    }

    /// <summary>
    /// Updates <see cref="SelectedUnScaledAvatarHeight"/> based on the requested selection mode.
    /// This value should represent the avatar's measurement in *unscaled* space.
    /// </summary>
    /// <param name="Height">Which avatar metric to select as the unscaled reference.</param>
    public static void RevaluateUnscaledHeight(BasisSelectedHeightMode Height)
    {
        switch (Height)
        {
            case BasisSelectedHeightMode.ArmSpan:
                SelectedUnScaledAvatarHeight = AvatarArmSpan;
                SelectedUnScaledPlayerHeight = PlayerArmSpan;
                break;

            case BasisSelectedHeightMode.EyeHeight:
                SelectedUnScaledAvatarHeight = AvatarEyeHeight;
                SelectedUnScaledPlayerHeight = PlayerEyeHeight;
                break;
        }
    }

    /// <summary>
    /// Chooses the active height metrics and conversion ratios based on the selection mode.
    ///
    /// This method sets:
    /// - <see cref="SelectedScaledPlayerHeight"/>
    /// - <see cref="SelectedScaledAvatarHeight"/>
    /// - <see cref="SelectedUnScaledAvatarHeight"/> (unscaled reference)
    ///
    /// And computes ratios:
    /// - <see cref="PlayerToDefaultRatioScaled"/>
    /// - <see cref="AvatarToDefaultRatioScaled"/>
    /// - <see cref="PlayerToAvatarRatioScaled"/>
    /// - <see cref="AvatarToPlayerRatioScaled"/>
    ///
    /// Desktop override:
    /// If the user is in desktop mode, height mode is forced to EyeHeight.
    /// </summary>
    /// <param name="Height">
    /// Selection mode:
    /// <see cref="BasisSelectedHeightMode.ArmSpan"/> or <see cref="BasisSelectedHeightMode.EyeHeight"/>.
    /// </param>
    public static void ChooseHeightToUse(BasisSelectedHeightMode Height)
    {
        // Desktop uses eye-height as the stable metric.
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            Height = BasisSelectedHeightMode.EyeHeight;
        }

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
        Vector3 CalibrationScale = avatarDriver.ScaleAvatarModification.DuringCalibrationScale;
        // Current applied avatar scale (1 = unscaled).
        AppliedUpScale = avatarDriver.ScaleAvatarModification.ApplyScale;

        switch (Height)
        {
            case BasisSelectedHeightMode.ArmSpan:
                SelectedScaledPlayerHeight = CalibrationScale.y * ((AdditionalPlayerHeight + PlayerArmSpan) * AppliedUpScale);
                SelectedScaledAvatarHeight = CalibrationScale.y * (AvatarArmSpan * AppliedUpScale);
                SelectedUnScaledAvatarHeight = AvatarArmSpan;
                SelectedUnScaledPlayerHeight = PlayerArmSpan;
                break;

            case BasisSelectedHeightMode.EyeHeight:
                SelectedScaledPlayerHeight = CalibrationScale.y * ((AdditionalPlayerHeight + PlayerEyeHeight) * AppliedUpScale);
                SelectedScaledAvatarHeight = CalibrationScale.y * (AvatarEyeHeight * AppliedUpScale);
                SelectedUnScaledAvatarHeight = AvatarEyeHeight;
                SelectedUnScaledPlayerHeight = PlayerEyeHeight;
                break;
        }

        // Guard against invalid values.
        if (SelectedScaledPlayerHeight <= 0f)
        {
            BasisDebug.LogError("Selected Scaled Player Height was at or below zero.", BasisDebug.LogTag.IK);
            SelectedScaledPlayerHeight = 1.6f;
        }

        if (SelectedScaledAvatarHeight <= 0f)
        {
            BasisDebug.LogError("Selected Scaled Avatar Height was at or below zero.", BasisDebug.LogTag.IK);
            SelectedScaledAvatarHeight = 1.6f;
        }

        // Ratios relative to the project's default reference eye height.
        AvatarToDefaultRatioScaled = (CalibrationScale.y * SelectedScaledAvatarHeight) / (CalibrationScale.y * FallbackHeightInMeters);
        PlayerToDefaultRatioScaled = (CalibrationScale.y * SelectedScaledPlayerHeight) / (CalibrationScale.y * FallbackHeightInMeters);

        // Relative ratios between player and avatar.
        PlayerToAvatarRatioScaled = (CalibrationScale.y * SelectedScaledPlayerHeight) / (CalibrationScale.y * SelectedScaledAvatarHeight);
        AvatarToPlayerRatioScaled = (CalibrationScale.y * SelectedScaledAvatarHeight) / (CalibrationScale.y * SelectedScaledPlayerHeight);

        // Defensive clamps to prevent invalid downstream multipliers.
        if (PlayerToAvatarRatioScaled <= 0f)
        {
            PlayerToAvatarRatioScaled = 1f;
        }

        if (AvatarToPlayerRatioScaled <= 0f)
        {
            AvatarToPlayerRatioScaled = 1f;
        }

        // Defensive clamps to prevent invalid downstream multipliers.
        if (SelectedUnScaledAvatarHeight <= 0f)
        {
            SelectedUnScaledAvatarHeight = 1f;
        }

        if (SelectedUnScaledPlayerHeight <= 0f)
        {
            SelectedUnScaledPlayerHeight = 1f;
        }
        //if we 10x the player scale we need to 10x the avatar scale
        var avatarScaledMetric = SelectedUnScaledAvatarHeight * AppliedUpScale;
        var playerMetric = AdditionalPlayerHeight + SelectedUnScaledPlayerHeight;

        DeviceScale = CalibrationScale.y * (avatarScaledMetric / playerMetric);

        BasisDebug.Log(
            $"Height Mode: {Height} | PlayerMetric(scaled): {SelectedScaledPlayerHeight}m | " +
            $"AvatarMetric(scaled): {SelectedScaledAvatarHeight}m | " +
            $"PlayerToAvatar: {PlayerToAvatarRatioScaled} | AvatarToPlayer: {AvatarToPlayerRatioScaled} | " +
            $"PlayerToDefault: {PlayerToDefaultRatioScaled} | AvatarToDefault: {AvatarToDefaultRatioScaled}",
            BasisDebug.LogTag.Avatar
        );
    }
    public static float AdditionalPlayerHeight = 0;


    public static float AppliedUpScale = 1;
    /// <summary>
    /// The most recently applied scale factor used to match the avatar to the selected target measurement.
    /// 1.0 means no scaling.
    /// </summary>
    public static float ScaledToMatchValue = 1f;

    /// <summary>
    /// Measured player eye height (meters). Defaults to <see cref="FallbackHeightInMeters"/>.
    /// </summary>
    public static float PlayerEyeHeight = FallbackHeightInMeters;

    /// <summary>
    /// Measured avatar eye height (meters), captured in unscaled reference state. Defaults to <see cref="FallbackHeightInMeters"/>.
    /// </summary>
    public static float AvatarEyeHeight = FallbackHeightInMeters;

    /// <summary>
    /// Measured player arm span (meters). Defaults to <see cref="FallbackHeightInMeters"/>.
    /// </summary>
    public static float PlayerArmSpan = FallbackHeightInMeters;

    /// <summary>
    /// Measured avatar arm span (meters), captured in unscaled reference state. Defaults to <see cref="FallbackHeightInMeters"/>.
    /// </summary>
    public static float AvatarArmSpan = FallbackHeightInMeters;

    /// <summary>
    /// Selected player metric (meters) in scaled space (after current avatar scale is applied).
    /// This is the "active" player measurement used for ratio calculations.
    /// </summary>
    public static float SelectedScaledPlayerHeight = FallbackHeightInMeters;

    /// <summary>
    /// Selected avatar metric (meters) in scaled space (after current avatar scale is applied).
    /// This is the "active" avatar measurement used for ratio calculations.
    /// </summary>
    public static float SelectedScaledAvatarHeight = FallbackHeightInMeters;

    /// <summary>
    /// Selected avatar metric (meters) in unscaled space (avatar scale forced to 1.0).
    /// This value is used when computing the scale factor: target / unscaledAvatarMetric.
    /// </summary>
    public static float SelectedUnScaledAvatarHeight = FallbackHeightInMeters;

    /// <summary>
    /// Selected player metric (meters) in unscaled space (player scale forced to 1.0).
    /// This value is used when computing the scale factor: target / unscaledAvatarMetric.
    /// </summary>
    public static float SelectedUnScaledPlayerHeight = FallbackHeightInMeters;
    /// <summary>
    /// Ratio of player metric to avatar metric after scaling:
    /// <code>PlayerToAvatarRatioScaled = SelectedScaledPlayerHeight / SelectedScaledAvatarHeight</code>
    ///
    /// Interpretation:
    /// - 1.0: player and avatar are equal relative size for the selected metric
    /// - &gt;1.0: player is larger than avatar
    /// - &lt;1.0: player is smaller than avatar
    ///
    /// Useful for converting values from avatar-relative units into player-relative units.
    /// (e.g., bring an object sized for the avatar into player-relative scaling)
    /// </summary>
    public static float PlayerToAvatarRatioScaled = 1f;

    /// <summary>
    /// Inverse of <see cref="PlayerToAvatarRatioScaled"/>:
    /// <code>AvatarToPlayerRatioScaled = SelectedScaledAvatarHeight / SelectedScaledPlayerHeight</code>
    ///
    /// Interpretation:
    /// - 1.0: equal relative size
    /// - &gt;1.0: avatar larger than player
    /// - &lt;1.0: avatar smaller than player
    ///
    /// Useful for converting values from player-relative units into avatar-relative units.
    /// </summary>
    public static float AvatarToPlayerRatioScaled = 1f;

    /// <summary>
    /// Ratio of the selected player metric to the project default reference:
    /// <code>PlayerToDefaultRatioScaled = SelectedScaledPlayerHeight / FallbackHeightInMeters</code>
    ///
    /// Useful for scaling player-relative systems (trackers, camera rigs, locomotion tuning, etc.)
    /// against a stable default reference.
    /// </summary>
    public static float PlayerToDefaultRatioScaled = 1f;

    /// <summary>
    /// Ratio of the selected avatar metric to the project default reference:
    /// <code>AvatarToDefaultRatioScaled = SelectedScaledAvatarHeight / FallbackHeightInMeters</code>
    ///
    /// Useful for scaling avatar-relative systems against a stable default reference.
    /// </summary>
    public static float AvatarToDefaultRatioScaled = 1f;

    public static float DeviceScale = 1f;
}
