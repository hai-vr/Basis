using Basis.BasisUI;
using Basis.IK;
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

    // Small epsilon to prevent divide-by-zero and ratio explosions.
    private const float Epsilon = 1e-5f;

    public static float PlayerCenterEyeVerticalOffset = 0f;

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
    /// <summary>
    /// True once the span held is a real measurement of this player rather than the fallback — a
    /// hand-to-hand reading, a restored save, or SlimeVR's skeleton. The span needs this for exactly
    /// the reason the eye height does: an avatar load must not re-measure it from whatever pose the
    /// player happens to be in, and the span is far the more pose-sensitive of the two (arms at your
    /// sides read barely a third of your reach).
    /// </summary>
    public static bool HasGenuinePlayerArmSpan = false;
    public static float AvatarArmSpan = FallbackHeightInMeters;

    /// <summary>How settled the continuously-observed measurements are, 0..1. Weights their say in the
    /// scale fit, so a span seen once counts for less than one confirmed over a hundred samples.</summary>
    public static float ObservedEyeConfidence = 0f;
    public static float ObservedArmSpanConfidence = 0f;

    /// <summary>
    /// Where a body measurement came from. Tracked purely so the calibration panel can tell the player
    /// WHY they are the size they are — "measured" and "the generic default" deserve very different
    /// amounts of trust, and until now both looked identical from the outside.
    /// </summary>
    public enum BasisBodyMeasurementSource
    {
        Fallback,
        Stated,
        Saved,
        Measured,
        SlimeVR,
    }

    public static BasisBodyMeasurementSource EyeHeightSource = BasisBodyMeasurementSource.Fallback;
    public static BasisBodyMeasurementSource ArmSpanSource = BasisBodyMeasurementSource.Fallback;

    /// <summary>
    /// The uniform player-to-avatar scale actually applied, excluding the user's scale slider. This is
    /// what the positional stretcher measures its residual against, so the two compose instead of
    /// pulling against each other.
    ///
    /// Starts at 0, meaning "no fit solved yet, use the eye-height ratio" — the same fallback the
    /// legacy height modes leave in place. Defaulting it to 1 would claim a solved 1:1 fit to anything
    /// that read it before the first solve.
    /// </summary>
    public static float AppliedUniformScale = 0f;
    /// <summary>The most recent scale fit, for the calibration debug readout.</summary>
    public static BasisScaleFitResult LastScaleFit = BasisScaleFitResult.Invalid;

    public static float PlayerHipHeight = 0f;
    public static float PlayerEyeToHipDrop = 0f;
    public static float AvatarHipHeight = 0f;
    public static float AvatarLegSpan = 0f;
    public static float AvatarSpineSpan = 0f;
    public static float AvatarShoulderWidth = 0f;

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
    public static float HeightModeGroundingOffset = 0f;
    // The avatar's arm span AFTER the body fit has resized its arm bones. Arm-span-based scaling has to
    // divide by the span the avatar actually has, not the authored one, or DeviceScale is wrong by the
    // whole fit ratio -- which reads in headset as the avatar being too large. Eye-height mode is immune
    // because the fit is height-neutral by construction. Equals AvatarArmSpan when no arm fit is applied.
    public static float EffectiveAvatarArmSpan
    {
        get
        {
            var fit = Basis.Scripts.Drivers.BasisLocalRigDriver.AppliedBodyFit;
            if (!fit.HasArmFit || Mathf.Approximately(fit.ArmScale, 1f))
            {
                return AvatarArmSpan;
            }
            float shoulders = Mathf.Clamp(AvatarShoulderWidth, 0f, AvatarArmSpan);
            return shoulders + (AvatarArmSpan - shoulders) * fit.ArmScale;
        }
    }

    public static void ApplyScaleAndHeight()
    {
        // Order matters, and it is the opposite of what it looks like. The scale fit reads only the
        // AUTHORED avatar measurements and the player's, so it must run before the stretcher has
        // resized anything -- otherwise the two chase each other, each reacting to the other's last
        // answer. Then the stretcher fits against the scale that was just chosen, taking up exactly
        // the residual that scale left. Only then is every metric below derived from the fitted body.
        SolveUniformScale();
        BasisLocalPlayer.Instance?.LocalRigDriver?.RefreshBodyFit();

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
        // An explicit recalibration is the player saying the current fit is wrong, so the observations
        // that produced it have to go with it — otherwise the high-water estimate would simply be
        // re-adopted a moment later and recalibrating could never change anything. This is the escape
        // hatch for a session poisoned by a bad tracking episode or a different person in the headset.
        BasisBodyEvidenceSampler.ResetEvidence();
        HasGenuinePlayerArmSpan = false;
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

        // An eye that implies a body far taller than the measured span implies was measured while the
        // play space was shifted up (space drag / grounding lift / external offset) — anatomy cannot
        // produce it. Persisting it poisons every subsequent boot's scale, so it never saves.
        bool eyeLooksLiftPoisoned = spanPlausible
            && BasisCalibrationMath.EyeHeightLooksLiftPoisoned(PlayerEyeHeight, PlayerArmSpan);

        // A stated height is the strongest veto available: it bounds the answer directly rather than
        // inferring it from a sibling measurement, so a reading that contradicts it never reaches disk.
        if (eyePlausible && eyeLooksUnderMeasured == false && eyeLooksLiftPoisoned == false
            && BasisStatedHeight.IsPlausibleEye(PlayerEyeHeight))
        {
            Basis.BasisUI.BasisSettingsDefaults.SavedPlayerEyeHeight.SetValue(PlayerEyeHeight);
        }
        if (spanPlausible && spanLooksUnderMeasured == false && BasisStatedHeight.IsPlausibleSpan(PlayerArmSpan))
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
        // Heal a lift-poisoned save from before the persist guard existed: an eye that is anatomically
        // impossible against its own saved span was measured while the play space was shifted. Restore
        // the eye the span implies instead of booting every session at the poisoned scale.
        float savedSpanForCheck = Basis.BasisUI.BasisSettingsDefaults.SavedPlayerArmSpan.RawValue;
        if (savedSpanForCheck >= MinPlausibleBodyMeasure && savedSpanForCheck <= MaxPlausibleBodyMeasure
            && BasisCalibrationMath.EyeHeightLooksLiftPoisoned(savedEye, savedSpanForCheck))
        {
            float healedEye = BasisCalibrationMath.ImpliedHeightFromSpan(savedSpanForCheck) * BasisCalibrationMath.EyeToHeightRatio;
            BasisDebug.LogWarning($"Saved eye height {savedEye:F3}m is implausible against saved arm span {savedSpanForCheck:F3}m (lift-poisoned calibration); using span-implied eye {healedEye:F3}m instead. Recalibrate to re-measure.", BasisDebug.LogTag.Avatar);
            savedEye = healedEye;
        }
        PlayerEyeHeight = savedEye;
        HasGenuinePlayerEyeHeight = true;
        EyeHeightSource = BasisBodyMeasurementSource.Saved;
        // The saved size originated from an explicit calibration, so the pre-calibration ballpark
        // auto-scale estimator must not fight it.
        HasUserCalibratedHeight = true;
        float savedSpan = Basis.BasisUI.BasisSettingsDefaults.SavedPlayerArmSpan.RawValue;
        if (savedSpan >= MinPlausibleBodyMeasure && savedSpan <= MaxPlausibleBodyMeasure)
        {
            PlayerArmSpan = savedSpan;
            // A restored save is a real measurement of this player, so it is not to be re-measured from
            // whatever pose the next avatar load catches them in.
            HasGenuinePlayerArmSpan = true;
            ArmSpanSource = BasisBodyMeasurementSource.Saved;
        }
        BasisDebug.Log($"Seeded last session's calibrated body size: eye {PlayerEyeHeight:F3}m span {PlayerArmSpan:F3}m", BasisDebug.LogTag.Avatar);
    }
    /// <summary>
    /// Falls back to what the player told us about themselves when nothing has been measured yet.
    /// Ranks BELOW a saved measurement (that was a real reading of this player) and below anything the
    /// sampler observes, but far above the 1.61 m generic fallback — which is what an otherwise
    /// unmeasured player would be wearing.
    /// </summary>
    private static void SeedStatedBodyHeight()
    {
        if (HasGenuinePlayerEyeHeight || SMModuleSitStand.IsSteatedMode || !BasisStatedHeight.IsSet)
        {
            return;
        }
        PlayerEyeHeight = BasisStatedHeight.ImpliedEyeHeight;
        HasGenuinePlayerEyeHeight = true;
        EyeHeightSource = BasisBodyMeasurementSource.Stated;
        if (!HasGenuinePlayerArmSpan)
        {
            ArmSpanSource = BasisBodyMeasurementSource.Stated;
            // An ape index of 1 is the population average and a much better opening guess than the
            // fallback, but it stays NOT genuine so a real reach measurement still overrides it.
            PlayerArmSpan = BasisStatedHeight.ImpliedArmSpan;
        }
        BasisDebug.Log($"Using your stated height {BasisStatedHeight.Meters:F2}m (eye {PlayerEyeHeight:F3}m) until something is measured.", BasisDebug.LogTag.Avatar);
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

        // A per-avatar nudge rides on top of the resolved target, so an avatar whose proportions the fit
        // cannot fully rescue can be corrected without distorting the player's measured body size —
        // which would otherwise follow them onto every other avatar they wear.
        BasisPerAvatarScale.RefreshForCurrentAvatar();

        // Resolve target + denominator in eye-height metres (the space admin limits use) so the clamp
        // is correct in every height mode. Scaling off keeps the factor at 1x unless a limit pulls in.
        float avatarEye = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters);
        float targetEyeMeters = ClampToAdminEyeHeight((ScaleAvatar ? SelectedScale : avatarEye) * BasisPerAvatarScale.Current);
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
        HasGenuinePlayerArmSpan = false;
        // …including anything observed, which on desktop described a mouse-driven head and a pair of
        // synthesized hands rather than a body.
        BasisBodyEvidenceSampler.ResetEvidence();
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
        SeedStatedBodyHeight();
        if (BasisCalibrationMath.ShouldRecaptureEyeHeight(recaptureEyeHeight, HasGenuinePlayerEyeHeight))
        {
            BasisLocalHeightCalculator.CalculatePlayerEyeHeight();
        }
        // The span is gated exactly like the eye height, and it matters MORE here: an avatar load can
        // happen at any moment, and a player with their arms at their sides measures barely a third of
        // their reach. Re-measuring it there handed the body fit a span short by half a metre, which it
        // faithfully turned into arms shrunk by the full deviation budget on every single avatar load.
        if (BasisCalibrationMath.ShouldRecaptureEyeHeight(recaptureEyeHeight, HasGenuinePlayerArmSpan))
        {
            BasisLocalHeightCalculator.CalculatePlayerArmSpan();
        }
        BasisLocalHeightCalculator.CalculatePlayerHipHeight();

        AdoptObservedBodyEvidence();

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

    /// <summary>
    /// Takes the best measurement seen while the player has actually been using the world, in place of
    /// whatever a single capture happened to catch.
    ///
    /// Adoption is a simple "keep the larger", which is not a heuristic but the same argument the rest
    /// of this file rests on: a slouch, a crouch or arms at your sides all read SHORT, and nothing
    /// short of a tracking glitch reads longer than the body actually is. So between two measurements
    /// of one unchanging body, the longer is the better one — and the sampler has thousands of chances
    /// to catch the player upright and reaching where a single capture had one.
    /// </summary>
    /// <summary>What the observations would have us believe, without committing to it yet.</summary>
    public struct ObservedBodySize
    {
        public float EyeHeight;
        public float ArmSpan;
        public bool EyeIsGenuine;
        public bool SpanIsGenuine;
    }

    /// <summary>
    /// Reads the best observed body size, leaving the current values in place where nothing better was
    /// seen. Split from adoption so the live refit can stage a change and ease into it rather than
    /// teleporting the player's viewpoint the instant a measurement lands.
    /// </summary>
    private static bool TryGetObservedBodySize(out ObservedBodySize result)
    {
        result = new ObservedBodySize
        {
            EyeHeight = PlayerEyeHeight,
            ArmSpan = PlayerArmSpan,
            EyeIsGenuine = HasGenuinePlayerEyeHeight,
            SpanIsGenuine = HasGenuinePlayerArmSpan,
        };

        ObservedEyeConfidence = 0f;
        ObservedArmSpanConfidence = 0f;
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            return false;
        }

        bool changed = false;

        if (BasisBodyEvidenceSampler.TryGetArmSpan(out float observedSpan, out float spanConfidence))
        {
            ObservedArmSpanConfidence = spanConfidence;
            // A stated height caps how long anyone's reach can credibly be; past that it is a glitch.
            if (observedSpan > result.ArmSpan && Plausible(observedSpan) && BasisStatedHeight.IsPlausibleSpan(observedSpan))
            {
                result.ArmSpan = observedSpan;
                result.SpanIsGenuine = true;
                changed = true;
            }
        }

        // Seated mode substitutes a virtual standing eye; observing the chair would overwrite it.
        if (SMModuleSitStand.IsSteatedMode)
        {
            return changed;
        }
        if (BasisBodyEvidenceSampler.TryGetEyeHeight(out float observedEye, out float eyeConfidence))
        {
            ObservedEyeConfidence = eyeConfidence;
            // The one way an eye reading CAN come out too long is a vertical shift of the play space.
            // The arm span is one witness against that; a stated height is a far better one, because it
            // bounds the answer directly instead of inferring it from a second measurement.
            bool poisoned = result.SpanIsGenuine
                && BasisCalibrationMath.EyeHeightLooksLiftPoisoned(observedEye, result.ArmSpan);
            if (observedEye > result.EyeHeight && Plausible(observedEye)
                && poisoned == false && BasisStatedHeight.IsPlausibleEye(observedEye))
            {
                result.EyeHeight = observedEye;
                result.EyeIsGenuine = true;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Takes the best measurement seen while the player has actually been using the world, in place of
    /// whatever a single capture happened to catch. Immediate — used on paths that are already
    /// re-fitting the avatar anyway; the live path eases in through <see cref="TickObservedEvidence"/>.
    ///
    /// Adoption is a simple "keep the larger", which is not a heuristic but the same argument the rest
    /// of this file rests on: a slouch, a crouch or arms at your sides all read SHORT, and nothing
    /// short of a tracking glitch reads longer than the body actually is. So between two measurements
    /// of one unchanging body, the longer is the better one — and the sampler has thousands of chances
    /// to catch the player upright and reaching where a single capture had one.
    /// </summary>
    private static void AdoptObservedBodyEvidence()
    {
        if (!TryGetObservedBodySize(out ObservedBodySize observed))
        {
            return;
        }
        BasisDebug.Log(
            $"Adopting observed body size: eye {PlayerEyeHeight:F3}->{observed.EyeHeight:F3}m, span {PlayerArmSpan:F3}->{observed.ArmSpan:F3}m",
            BasisDebug.LogTag.Avatar);
        if (!Mathf.Approximately(PlayerEyeHeight, observed.EyeHeight)) EyeHeightSource = BasisBodyMeasurementSource.Measured;
        if (!Mathf.Approximately(PlayerArmSpan, observed.ArmSpan)) ArmSpanSource = BasisBodyMeasurementSource.Measured;
        PlayerEyeHeight = observed.EyeHeight;
        PlayerArmSpan = observed.ArmSpan;
        HasGenuinePlayerEyeHeight = observed.EyeIsGenuine;
        HasGenuinePlayerArmSpan = observed.SpanIsGenuine;
    }

    private static bool Plausible(float measure) =>
        measure >= MinPlausibleBodyMeasure && measure <= MaxPlausibleBodyMeasure;

    /// <summary>Metres a measurement must improve by before the avatar is refitted around it.</summary>
    public const float EvidenceReapplyThresholdMeters = 0.02f;
    /// <summary>Seconds between checks. The evidence moves on a human timescale, not a frame one.</summary>
    private const float EvidenceReapplyIntervalSeconds = 1f;
    private static float s_evidenceReapplyTimer;

    private static float s_targetEye, s_targetSpan;
    private static bool s_targetEyeGenuine, s_targetSpanGenuine;
    private static bool s_hasRefitTarget;
    private static float s_refitFromEye, s_refitFromSpan;
    private static bool s_refitHeldLogged;

    /// <summary>
    /// Folds newly-observed evidence into the LIVE fit while the player is in the world.
    ///
    /// This is what makes the arm span usable at all. It cannot be measured until the player extends
    /// their arms, which they will not have done at the instant an avatar loads — so without this the
    /// best measurement of the session would sit unused until the next avatar swap. With it, the
    /// avatar's arms settle onto the player's real reach within seconds of them gesturing once.
    ///
    /// ⚠️ The change is applied in ONE STEP, on purpose. Easing it in over a few tenths of a second is
    /// the instinct from flat-screen UI and it is wrong here: a gradual uncommanded change means
    /// several hundred milliseconds of visual motion with nothing matching it in the inner ear, which
    /// is exactly the mismatch that makes people ill — the same reason VR turns snap rather than
    /// sweeping. A single discontinuity is dismissed as a cut; a slow drift is felt. Do not "smooth"
    /// this.
    ///
    /// The change is still STAGED rather than applied the instant it is measured, because the snap has
    /// to land at a moment when a resize is harmless — see <see cref="BasisCalibrationRefitGate"/>.
    /// Adoption only ever raises a measurement, so this converges and goes quiet rather than
    /// oscillating.
    /// </summary>
    public static void TickObservedEvidence(float deltaTime)
    {
        if (BasisLocalPlayer.Instance == null
            || BasisDeviceManagement.IsUserInDesktop()
            || BasisSettingsDefaults.ContinuousBodyMeasurement.RawValue == false)
        {
            return;
        }

        s_evidenceReapplyTimer += deltaTime;
        if (s_evidenceReapplyTimer >= EvidenceReapplyIntervalSeconds)
        {
            s_evidenceReapplyTimer = 0f;
            StageObservedBodySize();
        }

        if (!s_hasRefitTarget)
        {
            return;
        }

        // The measurement keeps. Waiting costs nothing; snapping someone's scale mid-grab does.
        if (BasisCalibrationRefitGate.ShouldHoldRefit(out string reason))
        {
            if (!s_refitHeldLogged)
            {
                s_refitHeldLogged = true;
                BasisDebug.Log($"Better body measurement is ready; holding the refit until later ({reason}).", BasisDebug.LogTag.Avatar);
            }
            return;
        }
        s_refitHeldLogged = false;
        s_hasRefitTarget = false;

        if (!Mathf.Approximately(PlayerEyeHeight, s_targetEye)) EyeHeightSource = BasisBodyMeasurementSource.Measured;
        if (!Mathf.Approximately(PlayerArmSpan, s_targetSpan)) ArmSpanSource = BasisBodyMeasurementSource.Measured;
        PlayerEyeHeight = s_targetEye;
        PlayerArmSpan = s_targetSpan;
        HasGenuinePlayerEyeHeight |= s_targetEyeGenuine;
        HasGenuinePlayerArmSpan |= s_targetSpanGenuine;
        ApplyScaleAndHeight();
        AnnounceRefit(s_refitFromEye, s_refitFromSpan);
    }

    /// <summary>
    /// Looks for a better measurement and stages it, without touching the applied size. Staging exists
    /// so the snap can wait for a safe moment; a second, better measurement arriving before the first
    /// has landed simply replaces the target, so the player only ever feels one change.
    /// </summary>
    private static void StageObservedBodySize()
    {
        if (!TryGetObservedBodySize(out ObservedBodySize observed))
        {
            return;
        }

        if (Mathf.Abs(observed.EyeHeight - PlayerEyeHeight) <= EvidenceReapplyThresholdMeters
            && Mathf.Abs(observed.ArmSpan - PlayerArmSpan) <= EvidenceReapplyThresholdMeters)
        {
            return; // real but not worth moving the world for
        }

        if (!s_hasRefitTarget)
        {
            s_refitFromEye = PlayerEyeHeight;
            s_refitFromSpan = PlayerArmSpan;
        }
        s_targetEye = observed.EyeHeight;
        s_targetSpan = observed.ArmSpan;
        s_targetEyeGenuine = observed.EyeIsGenuine;
        s_targetSpanGenuine = observed.SpanIsGenuine;
        s_hasRefitTarget = true;

        BasisDebug.Log(
            $"Observed body size improved (eye {PlayerEyeHeight:F3}->{s_targetEye:F3}m, span {PlayerArmSpan:F3}->{s_targetSpan:F3}m); refitting at the next safe moment.",
            BasisDebug.LogTag.Avatar);
    }

    /// <summary>
    /// Tells the player what just happened. Without this the system's best feature — quietly getting
    /// their size right — is indistinguishable from the avatar randomly changing size.
    /// </summary>
    private static void AnnounceRefit(float fromEye, float fromSpan)
    {
        float eyeDelta = PlayerEyeHeight - fromEye;
        float spanDelta = PlayerArmSpan - fromSpan;
        string detail = Mathf.Abs(spanDelta) >= Mathf.Abs(eyeDelta)
            ? string.Format(BasisLocalization.Get("calibration.refit.reach"), PlayerArmSpan)
            : string.Format(BasisLocalization.Get("calibration.refit.height"), PlayerEyeHeight);

        BasisNotificationCenter.LogResolved(
            BasisLocalization.Get("calibration.refit.title"),
            detail,
            AddressableAssets.Sprites.Information,
            BasisNotificationStatus.Accepted);
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
        BasisLocalHeightCalculator.CalculateAvatarBodySegments();
        BasisLocalHeightCalculator.ValidateEyeToArmSizesAvatar();

        // Sanitize captured values (protect against NaN/Inf/<=0 from rig issues)
        AvatarEyeHeight = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters);
        AvatarArmSpan = SanitizePositive(AvatarArmSpan, FallbackHeightInMeters);

        ApplyAvatarScale(previousScale);
        ScheduleHeightChangeCallback(HeightModeChange.OnTpose);
    }

    private static BasisSelectedHeightMode s_lastAutoResolvedMode = BasisSelectedHeightMode.EyeHeight;

    /// <summary>
    /// Resolves <see cref="BasisSelectedHeightMode.Auto"/>. In VR it becomes
    /// <see cref="BasisSelectedHeightMode.BestFit"/>: rather than picking whichever single measurement
    /// looks most trustworthy and letting the other one be wrong, the fit uses every measurement at
    /// once and hands the leftover to the positional stretcher. Desktop always resolves to EyeHeight —
    /// its hands are not the player's hands and its eye height is a synthesized number, so there is
    /// nothing to fit against. Concrete modes pass through untouched.
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
        if (s_lastAutoResolvedMode != BasisSelectedHeightMode.BestFit)
        {
            s_lastAutoResolvedMode = BasisSelectedHeightMode.BestFit;
            BasisDebug.Log("Auto height mode resolved to BestFit", BasisDebug.LogTag.Avatar);
        }
        return BasisSelectedHeightMode.BestFit;
    }

    /// <summary>
    /// Chooses the single uniform scale that fits the player into the avatar best, from every body
    /// measurement held, and records what each segment has left over for the positional stretcher.
    ///
    /// Reads the AUTHORED avatar measurements only — never the post-fit ones — so it can run before the
    /// stretcher and hand it a fixed target. Each limb tells the solver how many avatar-metres of
    /// mismatch the stretcher could absorb for it (<see cref="BasisBodyFitCore.ArmSpanSlack"/> and
    /// friends, so the two agree by construction); while every limb stays inside its budget the scale
    /// matches eye height exactly and the stretcher does all the work.
    ///
    /// Leaves <see cref="AppliedUniformScale"/> at 0 for every mode but BestFit, which makes the body
    /// fit fall back to the eye-height ratio it has always used. The concrete modes are manual
    /// overrides and arm-span mode in particular is degenerate here (its scale is derived from the
    /// post-fit span, so scale and fit define each other); BestFit is the mode where the question is
    /// well posed, and it is what Auto now resolves to.
    /// </summary>
    public static void SolveUniformScale()
    {
        AppliedUniformScale = 0f;
        LastScaleFit = BasisScaleFitResult.Invalid;

        if (ResolveHeightMode(SMModuleCalibration.HeightMode) != BasisSelectedHeightMode.BestFit)
        {
            return;
        }
        if (TryGetArmToHeightBlend(out _))
        {
            return; // the manual arm-to-height ratio is an explicit override of the fit
        }

        bool fitEnabled = BasisSettingsDefaults.FBIKBodyFit.RawValue;
        // With the stretcher off nothing can absorb a residual, so every limb becomes a hard pin and
        // the solver has to satisfy them with the scale alone.
        float deviation = fitEnabled
            ? Mathf.Clamp(BasisSettingsDefaults.FBIKBodyFitMaxDeviation.RawValue, 0f, BasisBodyFitCore.MaxDeviationCeiling)
            : 0f;

        var measurements = new BasisBodyFitMeasurements
        {
            AvatarArmSpan = AvatarArmSpan,
            AvatarShoulderWidth = AvatarShoulderWidth,
            AvatarLegSpan = AvatarLegSpan,
            AvatarSpineSpan = AvatarSpineSpan,
        };

        // Arm span only earns a say once it is a real measurement of THIS player; the fallback value is
        // just the default height wearing a different name, and letting it steer the scale would size
        // every player as though their reach equalled their height.
        float armWeight = HasGenuinePlayerArmSpan
            ? BasisScaleFitCore.ArmSpanWeight * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(ObservedArmSpanConfidence))
            : 0f;

        var input = new BasisScaleFitInput
        {
            MaxEyeDeviation = BasisScaleFitCore.DefaultMaxEyeDeviation,
            Eye = new BasisScaleFitSample
            {
                // The denominator DeviceScale divides by: the player's TRUE standing eye, device->eye
                // correction included, so the fit and DeviceScale describe the same quantity.
                Player = SanitizePositive(PlayerEyeHeight, FallbackHeightInMeters) + PlayerCenterEyeVerticalOffset,
                Avatar = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters),
                Weight = BasisScaleFitCore.EyeWeight,
            },
            ArmSpan = new BasisScaleFitSample
            {
                Player = PlayerArmSpan,
                Avatar = AvatarArmSpan,
                Slack = BasisBodyFitCore.ArmSpanSlack(in measurements, deviation),
                Weight = armWeight,
            },
            HipHeight = new BasisScaleFitSample
            {
                Player = PlayerHipHeight,
                Avatar = AvatarHipHeight,
                Slack = BasisBodyFitCore.HipHeightSlack(in measurements, deviation),
                Weight = BasisScaleFitCore.HipWeight,
            },
            LegSpan = BasisScaleFitSample.None,
        };

        BasisScaleFitResult fit = BasisScaleFitCore.Solve(in input);
        if (!fit.IsValid)
        {
            // Nothing measurable: fall through to the eye-height behaviour rather than inventing a scale.
            return;
        }

        AppliedUniformScale = fit.Scale;
        LastScaleFit = fit;

        if (fit.Status != s_lastScaleFitStatus)
        {
            s_lastScaleFitStatus = fit.Status;
            BasisDebug.Log(
                $"Scale fit {fit.Status} from {fit.UsedCount} measurement(s): scale {fit.Scale:F4} | " +
                $"leftover for the stretcher — eye {fit.EyeResidual:F3} arm {fit.ArmResidual:F3} hip {fit.HipResidual:F3}",
                BasisDebug.LogTag.Avatar);
        }
    }

    private static BasisScaleFitStatus s_lastScaleFitStatus = BasisScaleFitStatus.NoData;

    /// <summary>
    /// Arm-to-height ratio (0 = eye height, 1 = arm distance, negative extrapolates past eye height):
    /// when enabled it replaces the selected height mode with a metric pair interpolated between the
    /// two modes. Desktop keeps eye height.
    /// </summary>
    public static bool TryGetArmToHeightBlend(out float blend)
    {
        blend = Mathf.Clamp(Basis.BasisUI.BasisSettingsDefaults.ArmToHeightBlend.RawValue,
            BasisCalibrationMath.ArmToHeightBlendMin, BasisCalibrationMath.ArmToHeightBlendMax);
        return Basis.BasisUI.BasisSettingsDefaults.EnableArmToHeightBlend.RawValue
            && BasisDeviceManagement.IsUserInDesktop() == false;
    }

    public static void RevaluateUnscaledHeight(BasisSelectedHeightMode Height)
    {
        if (TryGetArmToHeightBlend(out float armToHeightBlend))
        {
            SelectedUnScaledAvatarHeight = SanitizePositive(BasisCalibrationMath.BlendEyeSpanMetric(AvatarEyeHeight, EffectiveAvatarArmSpan, armToHeightBlend), FallbackHeightInMeters);
            SelectedUnScaledPlayerHeight = SanitizePositive(BasisCalibrationMath.BlendEyeSpanMetric(PlayerEyeHeight, PlayerArmSpan, armToHeightBlend), FallbackHeightInMeters);
            return;
        }
        Height = ResolveHeightMode(Height);
        switch (Height)
        {
            case BasisSelectedHeightMode.ArmSpan:
                SelectedUnScaledAvatarHeight = SanitizePositive(EffectiveAvatarArmSpan, FallbackHeightInMeters);
                SelectedUnScaledPlayerHeight = SanitizePositive(PlayerArmSpan, FallbackHeightInMeters);
                break;

            case BasisSelectedHeightMode.EyeHeight:
                SelectedUnScaledAvatarHeight = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters);
                SelectedUnScaledPlayerHeight = SanitizePositive(PlayerEyeHeight, FallbackHeightInMeters);
                break;

            case BasisSelectedHeightMode.BestFit:
                BestFitMetrics(out float bestAvatar, out float bestPlayer);
                SelectedUnScaledAvatarHeight = bestAvatar;
                SelectedUnScaledPlayerHeight = bestPlayer;
                break;
        }
    }

    /// <summary>
    /// Expresses the best-fit scale as an avatar/player metric pair measured against the eye-height
    /// denominator, so the shared DeviceScale formula below lands on exactly the fitted scale
    /// (<c>avatar/player = scale</c> by construction) without needing a branch of its own. Falls back
    /// to the plain eye pair when no fit was solved, which is the mode's own degenerate case.
    /// </summary>
    private static void BestFitMetrics(out float avatarMetric, out float playerMetric)
    {
        playerMetric = SanitizePositive(PlayerEyeHeight, FallbackHeightInMeters);
        float scale = AppliedUniformScale;
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
        {
            avatarMetric = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters);
            return;
        }
        avatarMetric = SanitizePositive(scale * (playerMetric + PlayerCenterEyeVerticalOffset), FallbackHeightInMeters);
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
        // (PlayerCenterEyeVerticalOffset; non-zero on OpenVR, 0 when the tracked point is already the eye).
        // The arm-to-height blend weights it by its eye-height share, so blend 0 carries the full
        // correction (pure eye mode) and blend 1 carries none (pure arm mode). The weight is capped at 1
        // for negative blends: the metric extrapolates past eye height, but the physical device->eye gap
        // does not grow with it.
        bool blendHeights = TryGetArmToHeightBlend(out float armToHeightBlend);
        float fullEyeOffset = PlayerCenterEyeVerticalOffset;
        // BestFit carries the full correction: it always measures against the eye denominator, whatever
        // the limbs pulled the scale toward.
        float eyeScaleOffset = blendHeights
            ? fullEyeOffset * Mathf.Clamp01(1f - armToHeightBlend)
            : (Height == BasisSelectedHeightMode.EyeHeight || Height == BasisSelectedHeightMode.BestFit ? fullEyeOffset : 0f);

        // AppliedUpScale multiplies BOTH player and avatar metrics.
        if (blendHeights)
        {
            float avatarBlendMetric = BasisCalibrationMath.BlendEyeSpanMetric(AvatarEyeHeight, EffectiveAvatarArmSpan, armToHeightBlend);
            float playerBlendMetric = BasisCalibrationMath.BlendEyeSpanMetric(PlayerEyeHeight, PlayerArmSpan, armToHeightBlend);

            SelectedScaledPlayerHeight = calY * ((eyeScaleOffset + playerBlendMetric) * AppliedUpScale);
            SelectedScaledAvatarHeight = calY * (avatarBlendMetric * AppliedUpScale);

            SelectedUnScaledAvatarHeight = SanitizePositive(avatarBlendMetric, FallbackHeightInMeters);
            SelectedUnScaledPlayerHeight = SanitizePositive(playerBlendMetric, FallbackHeightInMeters);
        }
        else switch (Height)
        {
            case BasisSelectedHeightMode.ArmSpan:
                float fittedArmSpan = EffectiveAvatarArmSpan;
                SelectedScaledPlayerHeight = calY * (PlayerArmSpan * AppliedUpScale);
                SelectedScaledAvatarHeight = calY * (fittedArmSpan * AppliedUpScale);

                SelectedUnScaledAvatarHeight = SanitizePositive(fittedArmSpan, FallbackHeightInMeters);
                SelectedUnScaledPlayerHeight = SanitizePositive(PlayerArmSpan, FallbackHeightInMeters);
                break;

            case BasisSelectedHeightMode.EyeHeight:
                SelectedScaledPlayerHeight = calY * ((eyeScaleOffset + PlayerEyeHeight) * AppliedUpScale);
                SelectedScaledAvatarHeight = calY * (AvatarEyeHeight * AppliedUpScale);

                SelectedUnScaledAvatarHeight = SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters);
                SelectedUnScaledPlayerHeight = SanitizePositive(PlayerEyeHeight, FallbackHeightInMeters);
                break;

            case BasisSelectedHeightMode.BestFit:
                BestFitMetrics(out float bestAvatar, out float bestPlayer);
                SelectedScaledPlayerHeight = calY * ((eyeScaleOffset + bestPlayer) * AppliedUpScale);
                SelectedScaledAvatarHeight = calY * (bestAvatar * AppliedUpScale);

                SelectedUnScaledAvatarHeight = bestAvatar;
                SelectedUnScaledPlayerHeight = bestPlayer;
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
        float playerMetric = SanitizePositive(BasisCalibrationMath.StandingEyeDenominator(SelectedUnScaledPlayerHeight, eyeScaleOffset, 0f), 1f);

        DeviceScale = SafeDivide(avatarScaledMetric, playerMetric, 1f);
        DeviceScale = SanitizePositive(DeviceScale, 1f);

        // The grounding lift's eye term includes the blended eyeScaleOffset so at blend 0 the lift is
        // exactly 0 (eye mode already grounds the feet through the denominator -- never double-count it)
        // and at blend 1 it degenerates to the pure arm-span lift (eyeScaleOffset is 0 there). Any active
        // blend is eligible: either sign can sink the feet, depending on which side of eye height the
        // player's arm measurement lies.
        // BestFit needs it too, but only when the limbs actually pulled the scale off the eye match: at
        // EyeExact the lift computes to exactly zero on its own, so this costs nothing in the common case.
        bool needsGrounding = blendHeights
            || Height == BasisSelectedHeightMode.ArmSpan
            || Height == BasisSelectedHeightMode.BestFit;
        HeightModeGroundingOffset = (needsGrounding && !SMModuleSitStand.IsSteatedMode)
            ? BasisCalibrationMath.ArmSpanFloorGroundingLift(
                SanitizePositive(AvatarEyeHeight, FallbackHeightInMeters),
                AppliedUpScale,
                DeviceScale,
                SanitizePositive(PlayerEyeHeight, FallbackHeightInMeters) + eyeScaleOffset)
            : 0f;

        BasisDebug.Log(
            $"Height Mode: {(blendHeights ? $"ArmToHeightBlend {armToHeightBlend:P0}" : Height.ToString())} | PlayerMetric(scaled): {SelectedScaledPlayerHeight}m | " +
            $"AvatarMetric(scaled): {SelectedScaledAvatarHeight}m | " +
            $"PlayerToAvatar: {PlayerToAvatarRatioScaled} | AvatarToPlayer: {AvatarToPlayerRatioScaled} | " +
            $"PlayerToDefault: {PlayerToDefaultRatioScaledWithAvatarScale} | AvatarToDefault: {AvatarToDefaultRatioScaledWithAvatarScale} | " +
            $"DeviceScale: {DeviceScale}",
            BasisDebug.LogTag.Avatar
        );
        // Denominator breakdown so a systematic eye-height bias stays readable in-headset: compare the
        // true-eye estimate against your tape-measured standing eye height.
        BasisDebug.Log(
            $"Eye-height denominator (true standing eye estimate): {playerMetric:F3}m = raw {SelectedUnScaledPlayerHeight:F3} " +
            $"+ eye offset {eyeScaleOffset:F3} (device->eye {PlayerCenterEyeVerticalOffset:F3}" +
            $"{(blendHeights ? $", weighted {Mathf.Clamp01(1f - armToHeightBlend):P0} by the arm-to-height ratio" : "")})",
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
