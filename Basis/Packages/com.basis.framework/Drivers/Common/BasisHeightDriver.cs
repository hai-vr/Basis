using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

/// <summary>
/// Centralized height/scale orchestration for the local player avatar.
/// </summary>
public static class BasisHeightDriver
{
    public const float FallbackHeightInMeters = 1.61f;

    public const float StandingHeightCorrectionMin = -0.20f;
    public const float StandingHeightCorrectionMax = 0.20f;

    // Small epsilon to prevent divide-by-zero and ratio explosions.
    private const float Epsilon = 1e-5f;

    public static float PlayerCenterEyeVerticalOffset = 0f;

    // Pitch calibration: computed from up/down/forward HMD samples
    public static bool HasPitchCalibratedHeight = false;
    public static float PitchCalibratedEyeHeight = FallbackHeightInMeters;

    public static float AppliedUpScale = 1f;

    /// <summary>
    /// The most recently applied scale factor used to match the avatar to the selected target measurement.
    /// </summary>
    public static float ScaledToMatchValue = 1f;

    public static float PlayerEyeHeight = FallbackHeightInMeters;
    public static bool HasGenuinePlayerEyeHeight = false;
    public static bool HasUserCalibratedHeight = false;
    public static float AvatarEyeHeight = FallbackHeightInMeters;

    public static float PlayerArmSpan = FallbackHeightInMeters;
    public static float AvatarArmSpan = FallbackHeightInMeters;

    public static float SelectedScaledPlayerHeight = FallbackHeightInMeters;
    public static float SelectedScaledAvatarHeight = FallbackHeightInMeters;

    public static float SelectedUnScaledAvatarHeight = FallbackHeightInMeters;
    public static float SelectedUnScaledPlayerHeight = FallbackHeightInMeters;

    public static float PlayerToAvatarRatioScaled = 1f;
    public static float AvatarToPlayerRatioScaled = 1f;

    public static float PlayerToDefaultRatioScaledWithAvatarScale = 1f;
    public static float AvatarToDefaultRatioScaledWithAvatarScale = 1f;

    public static float PlayerToDefaultRatioScaled = 1f;
    public static float AvatarToDefaultRatioScaled = 1f;

    public static bool HasRuntimeOscEyeHeightOverride = false;
    public static float RuntimeOscEyeHeightMeters = FallbackHeightInMeters;

    public static float DeviceScale = 1f;
    public static void ApplyScaleAndHeight()
    {
        RevaluateUnscaledHeight(SMModuleCalibration.HeightMode);
        if (HasRuntimeOscEyeHeightOverride)
        {
            if (SMModuleCalibration.ApplyCustomScale)
            {
                ApplyRuntimeOscEyeHeightOverride(RuntimeOscEyeHeightMeters);
                return;
            }

            ClearRuntimeOscEyeHeightOverride();
        }

        ApplyScale(SMModuleCalibration.ApplyCustomScale, SMModuleCalibration.SelectedScale);
        ChooseHeightToUse(SMModuleCalibration.HeightMode);
        ScheduleHeightChangeCallback(HeightModeChange.OnApplyHeightAndScale);

        // DeviceScale (and possibly the avatar) just re-resolved: re-derive the calibrated FBT position
        // offsets so the existing T-pose calibration keeps fitting (avatar swap / scale slider) instead
        // of going stale by the scale delta. No-op with no stored calibration.
        Basis.Scripts.Avatar.BasisAvatarIKStageCalibration.ReprojectTrackerOffsetsForCurrentAvatar();
    }

    public static void OnAvatarFBCalibration()
    {
        HasUserCalibratedHeight = true;
        CapturePlayerHeight();
        PersistCalibratedBodySize();
        ApplyScaleAndHeight();
        ScheduleHeightChangeCallback(HeightModeChange.OnAvatarFBCalibration);
    }

    // Plausibility band for persisted body measurements (metres): outside this, treat as junk.
    public const float MinPlausibleBodyMeasure = 0.8f;
    public const float MaxPlausibleBodyMeasure = 2.8f;

    /// <summary>
    /// Saves the explicitly calibrated body size so the NEXT session boots at the right scale instead
    /// of the fallback (seeded back in <see cref="CapturePlayerHeight"/>). Seated calibrations measure
    /// the virtual standing eye, not the player's body, so they are never saved.
    /// </summary>
    private static void PersistCalibratedBodySize()
    {
        if (SMModuleSitStand.IsSteatedMode || HasGenuinePlayerEyeHeight == false)
        {
            return;
        }
        bool eyePlausible = PlayerEyeHeight >= MinPlausibleBodyMeasure && PlayerEyeHeight <= MaxPlausibleBodyMeasure;
        bool spanPlausible = PlayerArmSpan >= MinPlausibleBodyMeasure && PlayerArmSpan <= MaxPlausibleBodyMeasure;

        // A measurement that reads far shorter than its sibling implies was under-measured — a
        // physically-seated calibration reads the eye ~25-35% short, bent arms read the span short.
        // Don't overwrite the saved good value with it; the sibling still saves.
        bool eyeLooksUnderMeasured = spanPlausible
            && BasisCalibrationMath.AutoHeightModePicksArmSpan(PlayerEyeHeight, PlayerArmSpan);
        bool spanLooksUnderMeasured = eyePlausible
            && BasisCalibrationMath.ImpliedHeightFromEye(PlayerEyeHeight)
               > BasisCalibrationMath.ImpliedHeightFromSpan(PlayerArmSpan) * BasisCalibrationMath.AutoModeEyePreferenceBand;

        if (eyePlausible && eyeLooksUnderMeasured == false)
        {
            Basis.BasisUI.BasisSettingsDefaults.SavedPlayerEyeHeight.SetValue(PlayerEyeHeight);
        }
        if (spanPlausible && spanLooksUnderMeasured == false)
        {
            Basis.BasisUI.BasisSettingsDefaults.SavedPlayerArmSpan.SetValue(PlayerArmSpan);
        }
    }

    /// <summary>
    /// Seeds the last session's calibrated body size when no genuine measurement exists yet, so the
    /// default scale is right from the very first avatar load (and the standing height is restored on
    /// leaving seated mode) instead of the fallback / a stance-dependent first poll. Never seeds while
    /// seated — there the virtual standing eye (FallbackHeightInMeters) must stay the denominator.
    /// Self-limiting: seeding marks the height genuine, and explicit calibrates re-poll regardless.
    /// </summary>
    private static void SeedPersistedBodySize()
    {
        if (HasGenuinePlayerEyeHeight || SMModuleSitStand.IsSteatedMode)
        {
            return;
        }
        float savedEye = Basis.BasisUI.BasisSettingsDefaults.SavedPlayerEyeHeight.RawValue;
        if (savedEye < MinPlausibleBodyMeasure || savedEye > MaxPlausibleBodyMeasure)
        {
            return;
        }
        PlayerEyeHeight = savedEye;
        HasGenuinePlayerEyeHeight = true;
        // The saved size originated from an explicit calibration, so the pre-calibration ballpark
        // auto-scale estimator must not fight it.
        HasUserCalibratedHeight = true;
        float savedSpan = Basis.BasisUI.BasisSettingsDefaults.SavedPlayerArmSpan.RawValue;
        if (savedSpan >= MinPlausibleBodyMeasure && savedSpan <= MaxPlausibleBodyMeasure)
        {
            PlayerArmSpan = savedSpan;
        }
        BasisDebug.Log($"Seeded last session's calibrated body size: eye {PlayerEyeHeight:F3}m span {PlayerArmSpan:F3}m", BasisDebug.LogTag.Avatar);
    }
    public static void ScheduleHeightChangeCallback(HeightModeChange Mode)
    {
        BasisLocalPlayer.Instance.ExecuteNextFrame(() =>
        {
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame?.Invoke(Mode);
        });
    }
    /// <summary>
    /// Applies a custom avatar scale based on a target measurement.
    /// </summary>
    public static void ApplyScale(bool ScaleAvatar, float SelectedScale)
    {
        SelectedScale = SanitizePositive(SelectedScale, FallbackHeightInMeters);

        // Resolve target + denominator in eye-height metres (the space admin limits use) so the clamp
        // is correct in every height mode. Scaling off keeps the factor at 1x unless a limit pulls in.
        float avatarEye = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters);
        float targetEyeMeters = ClampToAdminEyeHeight(ScaleAvatar ? SelectedScale : avatarEye);
        ScaledToMatchValue = targetEyeMeters / avatarEye;

        BasisDebug.Log($"Applying Scale to Avatar {ScaledToMatchValue}", BasisDebug.LogTag.Avatar);

        ApplyAvatarScale(ScaledToMatchValue);
    }

    /// <summary>
    /// Clamp a target avatar eye height (metres) to the server-pushed admin scale limits. Admins
    /// (basis.moderation.globallock) bypass it; the default 0.1..100 m range is effectively a no-op.
    /// </summary>
    public static float ClampToAdminEyeHeight(float eyeHeightMeters)
    {
        if (BasisNetworkModeration.LocalPlayerHasGlobalLockBypass())
        {
            return eyeHeightMeters;
        }

        float min = BasisNetworkModeration.ServerMinAvatarEyeHeightMeters;
        float max = BasisNetworkModeration.ServerMaxAvatarEyeHeightMeters;
        if (float.IsNaN(min) || float.IsInfinity(min) || min <= 0f) min = 0.1f;
        if (float.IsNaN(max) || float.IsInfinity(max) || max <= 0f) max = 100f;
        if (max < min) max = min;
        return Mathf.Clamp(eyeHeightMeters, min, max);
    }

    public enum HeightModeChange
    {
        OnAvatarFBCalibration,
        OnTpose,
        OnApplyHeightAndScale,
        // Sit/stand mode switch: the eye teleports vertically, so consumers that normally hold their
        // anchor through scale changes (the play-space-stable menu) must re-anchor fully.
        OnSitStandChanged
    }

    public static bool ApplyRuntimeOscEyeHeightOverride(float eyeHeightMeters)
    {
        eyeHeightMeters = SanitizePositive(eyeHeightMeters, FallbackHeightInMeters);
        eyeHeightMeters = ClampToAdminEyeHeight(eyeHeightMeters);

        float unscaledAvatarEyeHeight = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters);
        float scaleFactor = eyeHeightMeters / unscaledAvatarEyeHeight;

        HasRuntimeOscEyeHeightOverride = true;
        RuntimeOscEyeHeightMeters = eyeHeightMeters;
        SMModuleCalibration.SelectedScale = eyeHeightMeters;
        BasisSettingsDefaults.SelectedScale.SetValueWithoutNotify(eyeHeightMeters);
        SettingsProviderIK.SetAvatarScaleSliderValueWithoutNotify(eyeHeightMeters);
        ScaledToMatchValue = scaleFactor;

        ApplyAvatarScale(scaleFactor);
        RefreshScaledHeightState(HeightModeChange.OnApplyHeightAndScale);
        return true;
    }

    public static void ClearRuntimeOscEyeHeightOverride()
    {
        HasRuntimeOscEyeHeightOverride = false;
        RuntimeOscEyeHeightMeters = FallbackHeightInMeters;
    }

    public static void RefreshScaledHeightState(HeightModeChange mode)
    {
        RevaluateUnscaledHeight(SMModuleCalibration.HeightMode);
        ChooseHeightToUse(SMModuleCalibration.HeightMode);
        ScheduleHeightChangeCallback(mode);
        Basis.Scripts.Avatar.BasisAvatarIKStageCalibration.ReprojectTrackerOffsetsForCurrentAvatar();
    }

    public static bool TryGetMatchedEyeHeightOverrideMeters(BasisRemotePlayer target, out float eyeHeightMeters)
    {
        eyeHeightMeters = 0f;
        if (target?.NetworkReceiver == null || target.BasisAvatar == null || BasisLocalPlayer.Instance?.LocalAvatarDriver == null)
        {
            return false;
        }

        // Mirror the REMOTE avatar's rendered eye height: its authored (already rendered-space) eye height
        // at its current network root scale. Reading local measurements was the bug.
        target.NetworkReceiver.GetLatestNetworkPose(out _, out _, out var networkScale);
        float remoteAuthoredEye = target.BasisAvatar.AvatarEyePosition.x;
        if (float.IsNaN(remoteAuthoredEye) || float.IsInfinity(remoteAuthoredEye) || remoteAuthoredEye <= 0f)
        {
            return false;
        }

        float remoteRootScale = networkScale.y;
        if (float.IsNaN(remoteRootScale) || float.IsInfinity(remoteRootScale) || remoteRootScale <= 0f)
        {
            remoteRootScale = 1f;
        }

        eyeHeightMeters = remoteAuthoredEye * remoteRootScale;
        return !float.IsNaN(eyeHeightMeters) && !float.IsInfinity(eyeHeightMeters) && eyeHeightMeters > 0f;
    }

    /// <summary>
    /// Applies a scale factor to the local avatar and updates cached bone offsets.
    /// </summary>
    public static void ApplyAvatarScale(float ScaleFactor)
    {
        // sanitize ScaleFactor to avoid NaN/Inf poisoning bones.
        ScaleFactor = SanitizePositive(ScaleFactor, 1f);

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

        // Update cached per-bone data expected to be in "scaled" space.
        int count = boneDriver.ControlsLength;
        for (int Index = 0; Index < count; Index++)
        {
            BasisLocalBoneControl c = boneDriver.Controls[Index];

            c.SetTposeScaled(c.TposeLocal.position * ScaleFactor, c.TposeLocal.rotation);
            c.ScaledOffset = c.Offset * ScaleFactor;
        }
    }

    /// <summary>
    /// Entering VR: whatever eye height the previous mode left applied is not the player's VR
    /// standing height (desktop's is a virtual value, and it gets marked genuine), so the applied
    /// calibration must be dropped and REAPPLIED from stored data — the persisted body size seeds
    /// back in, the scale re-resolves, FBT position offsets reproject, and the FBT rotation
    /// references re-derive. Without this, a desktop stint poisoned the VR scale until the user
    /// manually recalibrated. Fresh installs with nothing persisted fall through to the normal
    /// live-poll flow.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookBootModeChanged()
    {
        BasisDeviceManagement.OnBootModeChanged -= OnBootModeChangedReapplyCalibration;
        BasisDeviceManagement.OnBootModeChanged += OnBootModeChangedReapplyCalibration;
    }

    private static void OnBootModeChangedReapplyCalibration(string mode)
    {
        if (BasisDeviceManagement.IsCurrentModeVR() == false)
        {
            return;
        }
        // Remove the previous mode's applied measurement…
        HasGenuinePlayerEyeHeight = false;
        if (BasisLocalPlayer.Instance == null)
        {
            return; // boot-time switch: the first avatar load runs this same reapply flow itself
        }
        // …and reapply the stored calibration through the normal pipeline.
        CapturePlayerHeight(recaptureEyeHeight: false);
        ApplyScaleAndHeight();
        Basis.Scripts.Avatar.BasisAvatarIKStageCalibration.ApplyCalibrationToCurrentAvatar();
    }

    public static void CapturePlayerHeight(bool recaptureEyeHeight = true)
    {
        BasisDebug.Log("Capturing Player Height", BasisDebug.LogTag.IK);
        SeedPersistedBodySize();
        if (BasisCalibrationMath.ShouldRecaptureEyeHeight(recaptureEyeHeight, HasGenuinePlayerEyeHeight))
        {
            BasisLocalHeightCalculator.CalculatePlayerEyeHeight();
        }
        BasisLocalHeightCalculator.CalculatePlayerArmSpan();

        if (Basis.BasisUI.BasisSettingsDefaults.FBIKArmHeightRatioEnabled.RawValue)
        {
            float ratio = Mathf.Max(0.1f, Basis.BasisUI.BasisSettingsDefaults.FBIKArmHeightRatio.RawValue);
            PlayerArmSpan = SanitizePositive(PlayerEyeHeight, FallbackHeightInMeters) * ratio;
        }
        else
        {
            BasisLocalHeightCalculator.ValidateEyeToArmSizesPlayer();
        }

        // Optional safety: sanitize captured values in case calculator produced junk.
        PlayerEyeHeight = SanitizePositive(PlayerEyeHeight, FallbackHeightInMeters);
        PlayerArmSpan = SanitizePositive(PlayerArmSpan, FallbackHeightInMeters);
    }

    public static void CaptureAvatarHeightDuringTpose()
    {
        ClearRuntimeOscEyeHeightOverride();

        var player = BasisLocalPlayer.Instance;
        if (player == null)
        {
            BasisDebug.LogError("No local player instance.", BasisDebug.LogTag.Avatar);
            return;
        }

        var avatarDriver = player.LocalAvatarDriver;
        if (avatarDriver == null)
        {
            BasisDebug.LogError("Avatar driver missing; cannot capture avatar height.", BasisDebug.LogTag.Avatar);
            return;
        }

        // do not use AppliedUpScale as a temp restore var; use a local snapshot.
        float previousScale = SanitizePositive(avatarDriver.ScaleAvatarModification.ApplyScale, 1f);

        AppliedUpScale = previousScale;

        ApplyAvatarScale(1f); // Force unscaled to capture correct baseline measurements.

        BasisLocalHeightCalculator.CalculateAvatarEyeHeight();
        BasisLocalHeightCalculator.CalculateAvatarArmSpan();
        BasisLocalHeightCalculator.ValidateEyeToArmSizesAvatar();

        // Sanitize captured values (protect against NaN/Inf/<=0 from rig issues)
        AvatarEyeHeight = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters);
        AvatarArmSpan = SanitizePositive(AvatarArmSpan, FallbackHeightInMeters);

        ApplyAvatarScale(previousScale);
        ScheduleHeightChangeCallback(HeightModeChange.OnTpose);
    }

    private static BasisSelectedHeightMode s_lastAutoResolvedMode = BasisSelectedHeightMode.EyeHeight;

    /// <summary>
    /// Resolves <see cref="BasisSelectedHeightMode.Auto"/> to a concrete metric pair by trusting the
    /// LONGER of the player's own measurements (see BasisCalibrationMath.AutoHeightModePicksArmSpan):
    /// body metrics under-measure easily (seated/slouched → short eye height, bent arms → short span)
    /// but cannot over-measure, so the larger implied body height is the trustworthy one. Arm span
    /// wins only when the eye measurement is implausibly short against the measured reach — the
    /// "calibrated sitting in a chair with arms out" case. Desktop always resolves to EyeHeight.
    /// Concrete modes pass through untouched.
    /// </summary>
    public static BasisSelectedHeightMode ResolveHeightMode(BasisSelectedHeightMode mode)
    {
        if (mode != BasisSelectedHeightMode.Auto)
        {
            return mode;
        }
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            return BasisSelectedHeightMode.EyeHeight;
        }
        float playerEye = SanitizePositive(PlayerEyeHeight, FallbackHeightInMeters);
        float playerSpan = SanitizePositive(PlayerArmSpan, FallbackHeightInMeters);
        BasisSelectedHeightMode resolved = BasisCalibrationMath.AutoHeightModePicksArmSpan(playerEye, playerSpan)
            ? BasisSelectedHeightMode.ArmSpan
            : BasisSelectedHeightMode.EyeHeight;
        if (resolved != s_lastAutoResolvedMode)
        {
            s_lastAutoResolvedMode = resolved;
            BasisDebug.Log(
                $"Auto height mode resolved to {resolved}: implied body height from eye {BasisCalibrationMath.ImpliedHeightFromEye(playerEye):F2}m vs from span {BasisCalibrationMath.ImpliedHeightFromSpan(playerSpan):F2}m",
                BasisDebug.LogTag.Avatar);
        }
        return resolved;
    }

    public static void RevaluateUnscaledHeight(BasisSelectedHeightMode Height)
    {
        Height = ResolveHeightMode(Height);
        switch (Height)
        {
            case BasisSelectedHeightMode.ArmSpan:
                SelectedUnScaledAvatarHeight = SanitizePositive(AvatarArmSpan, FallbackHeightInMeters);
                SelectedUnScaledPlayerHeight = SanitizePositive(PlayerArmSpan, FallbackHeightInMeters);
                break;

            case BasisSelectedHeightMode.EyeHeight:
                SelectedUnScaledAvatarHeight = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters);
                SelectedUnScaledPlayerHeight = SanitizePositive(PlayerEyeHeight, FallbackHeightInMeters);
                break;
        }
    }

    public static void ChooseHeightToUse(BasisSelectedHeightMode Height)
    {
        // Desktop uses eye-height as the stable metric.
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            Height = BasisSelectedHeightMode.EyeHeight;
        }
        Height = ResolveHeightMode(Height);

        var player = BasisLocalPlayer.Instance;
        if (player == null)
        {
            BasisDebug.LogError("No local player instance.", BasisDebug.LogTag.Avatar);
            return;
        }

        var avatarDriver = player.LocalAvatarDriver;
        if (avatarDriver == null)
        {
            BasisDebug.LogError("Avatar driver missing; cannot choose height.", BasisDebug.LogTag.Avatar);
            return;
        }

        Vector3 calibrationScale = avatarDriver.ScaleAvatarModification.DuringCalibrationScale;

        // sanitize calibration scale to prevent divide-by-zero / negative surprises.
        float calY = SanitizePositive(calibrationScale.y, 1f);
        calibrationScale.y = calY;

        // Current applied avatar scale (1 = unscaled).
        AppliedUpScale = SanitizePositive(avatarDriver.ScaleAvatarModification.ApplyScale, 1f);

        // eyeScaleOffset lifts the measured HMD height up to the player's TRUE standing eye height before
        // DeviceScale divides by it. It carries the backend's device-origin->eye correction
        // (PlayerCenterEyeVerticalOffset; non-zero on OpenVR, 0 when the tracked point is already the eye)
        // plus the persisted systematic correction that bridges what that under-reports -- the gap that
        // otherwise renders too tall on OpenVR and makes users nudge up every calibration.
        float standingEyeCorrection = Basis.BasisUI.BasisSettingsDefaults.EnableStandingEyeHeightCorrection.RawValue
            ? Basis.BasisUI.BasisSettingsDefaults.CalibrationStandingEyeHeightMeters.RawValue
            : 0f;
        float eyeScaleOffset = (Height == BasisSelectedHeightMode.EyeHeight) ? PlayerCenterEyeVerticalOffset + standingEyeCorrection : 0f;

        // Standing-height nudge (the old "AdditionalPlayerHeight"): a separate ± bridge fed straight into the
        // DeviceScale denominator below, independent of the eye-height modifier. Gated by its own toggle.
        float additionalPlayerHeight = Basis.BasisUI.BasisSettingsDefaults.EnableStandingHeightNudge.RawValue
            ? Basis.BasisUI.BasisSettingsDefaults.AdditionalPlayerHeight.RawValue
            : 0f;

        // AppliedUpScale multiplies BOTH player and avatar metrics.
        switch (Height)
        {
            case BasisSelectedHeightMode.ArmSpan:
                SelectedScaledPlayerHeight = calY * (PlayerArmSpan * AppliedUpScale);
                SelectedScaledAvatarHeight = calY * (AvatarArmSpan * AppliedUpScale);

                SelectedUnScaledAvatarHeight = SanitizePositive(AvatarArmSpan, FallbackHeightInMeters);
                SelectedUnScaledPlayerHeight = SanitizePositive(PlayerArmSpan, FallbackHeightInMeters);
                break;

            case BasisSelectedHeightMode.EyeHeight:
                SelectedScaledPlayerHeight = calY * ((eyeScaleOffset + PlayerEyeHeight) * AppliedUpScale);
                SelectedScaledAvatarHeight = calY * (AvatarEyeHeight * AppliedUpScale);

                SelectedUnScaledAvatarHeight = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters);
                SelectedUnScaledPlayerHeight = SanitizePositive(PlayerEyeHeight, FallbackHeightInMeters);
                break;
        }

        // stronger guards (NaN/Inf too), not only <=0.
        SelectedScaledPlayerHeight = SanitizePositive(SelectedScaledPlayerHeight, 1.6f);
        SelectedScaledAvatarHeight = SanitizePositive(SelectedScaledAvatarHeight, 1.6f);

        // "Default" denominator in the same space as SelectedScaled* (which currently includes calY)
        float defaultScaled = SanitizePositive(FallbackHeightInMeters * calY, FallbackHeightInMeters);

        PlayerToDefaultRatioScaled = SafeDivide(SelectedScaledPlayerHeight, FallbackHeightInMeters, 1f);
        AvatarToDefaultRatioScaled = SafeDivide(SelectedScaledAvatarHeight, FallbackHeightInMeters, 1f);
        // Use SafeDivide for all ratios (prevents NaN/Inf/0 denom explosions)
        PlayerToDefaultRatioScaledWithAvatarScale = SafeDivide(SelectedScaledPlayerHeight, defaultScaled, 1f);
        AvatarToDefaultRatioScaledWithAvatarScale = SafeDivide(SelectedScaledAvatarHeight, defaultScaled, 1f);

        // Relative ratios between player and avatar.
        PlayerToAvatarRatioScaled = SafeDivide(SelectedScaledPlayerHeight, SelectedScaledAvatarHeight, 1f);
        AvatarToPlayerRatioScaled = SafeDivide(SelectedScaledAvatarHeight, SelectedScaledPlayerHeight, 1f);

        // clamp/clean ratios against NaN/Inf/<=0.
        PlayerToAvatarRatioScaled = SanitizePositive(PlayerToAvatarRatioScaled, 1f);
        AvatarToPlayerRatioScaled = SanitizePositive(AvatarToPlayerRatioScaled, 1f);

        // Defensive clamps for unscaled metrics.
        SelectedUnScaledAvatarHeight = SanitizePositive(SelectedUnScaledAvatarHeight, 1f);
        SelectedUnScaledPlayerHeight = SanitizePositive(SelectedUnScaledPlayerHeight, 1f);

        // DeviceScale: keep your original intent/math, but make it safe.
        // avatarScaledMetric in meters-equivalent (unscaled metric * applied scale).
        float avatarScaledMetric = SanitizePositive(SelectedUnScaledAvatarHeight * AppliedUpScale, 1f);
        // playerMetric is the player's TRUE standing eye height the avatar is scaled against. Assembled
        // by the shared pure helper so this runtime path and BasisCalibrationMathSweep exercise the same
        // formula. eyeScaleOffset already carries the device-origin->eye correction (OpenVR) plus the
        // gated standing-eye-height correction applied above.
        float playerMetric = SanitizePositive(BasisCalibrationMath.StandingEyeDenominator(SelectedUnScaledPlayerHeight, eyeScaleOffset, additionalPlayerHeight), 1f);

        DeviceScale = SafeDivide(avatarScaledMetric, playerMetric, 1f);
        DeviceScale = SanitizePositive(DeviceScale, 1f);

        BasisDebug.Log(
            $"Height Mode: {Height} | PlayerMetric(scaled): {SelectedScaledPlayerHeight}m | " +
            $"AvatarMetric(scaled): {SelectedScaledAvatarHeight}m | " +
            $"PlayerToAvatar: {PlayerToAvatarRatioScaled} | AvatarToPlayer: {AvatarToPlayerRatioScaled} | " +
            $"PlayerToDefault: {PlayerToDefaultRatioScaledWithAvatarScale} | AvatarToDefault: {AvatarToDefaultRatioScaledWithAvatarScale} | " +
            $"DeviceScale: {DeviceScale}",
            BasisDebug.LogTag.Avatar
        );
        // Denominator breakdown so the systematic eye-height bias is readable in-headset: compare the
        // true-eye estimate against your tape-measured standing eye height. If it reads low (avatar too
        // tall), the shortfall is the value to put in CalibrationStandingEyeHeightMeters.
        BasisDebug.Log(
            $"Eye-height denominator (true standing eye estimate): {playerMetric:F3}m = raw {SelectedUnScaledPlayerHeight:F3} " +
            $"+ device->eye {PlayerCenterEyeVerticalOffset:F3} + correction {standingEyeCorrection:F3} + nudge {additionalPlayerHeight:F3}",
            BasisDebug.LogTag.Avatar
        );
    }
    private static float SanitizePositive(float value, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
        {
            BasisDebug.LogError("Data In Height Driver failed validation Stage 1", BasisDebug.LogTag.IK);
            return fallback;
        }

        return value;
    }

    private static float SafeDivide(float numerator, float denominator, float fallback)
    {
        if (float.IsNaN(numerator) || float.IsInfinity(numerator))
        {
            BasisDebug.LogError("Data In Height Driver failed validation Stage 2", BasisDebug.LogTag.IK);
            return fallback;
        }

        if (float.IsNaN(denominator) || float.IsInfinity(denominator))
        {
            BasisDebug.LogError("Data In Height Driver failed validation Stage 3", BasisDebug.LogTag.IK);
            return fallback;
        }

        if (denominator > -Epsilon && denominator < Epsilon)
        {
            BasisDebug.LogError("Data In Height Driver failed validation Stage 4", BasisDebug.LogTag.IK);
            return fallback;
        }

        return numerator / denominator;
    }
}
