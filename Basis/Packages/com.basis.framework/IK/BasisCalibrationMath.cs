using UnityEngine;

/// <summary>
/// Pure calibration/offset math shared by the runtime (BasisInput offset capture + device scaling)
/// and the editor Calibration Math sweep, so the sweep exercises the real formulas rather than a
/// copy. All functions are side-effect-free Vector3/Quaternion math.
/// </summary>
public static class BasisCalibrationMath
{
    /// <summary>
    /// Inverse-offset capture (BasisInput.CalculateOffset): store the bone pose in the tracker's
    /// local frame so the bone can follow the tracker rigidly at runtime. The stored offset is the
    /// bone position/rotation expressed relative to the tracker at calibration time.
    /// </summary>
    public static void ComputeInverseOffset(Vector3 trackerPos, Quaternion trackerRot, Vector3 bonePos, Quaternion boneRot, out Vector3 inverseOffsetPosition, out Quaternion inverseOffsetRotation)
    {
        Quaternion invTrack = Quaternion.Inverse(trackerRot);
        inverseOffsetPosition = invTrack * (bonePos - trackerPos);
        inverseOffsetRotation = invTrack * boneRot;
    }

    /// <summary>
    /// Applies a captured inverse offset (the BasisBoneSimChainJob UseInverseOffset path): land the
    /// bone at trackerPos + trackerRot*offsetPos with rotation trackerRot*offsetRot. Exact inverse
    /// of <see cref="ComputeInverseOffset"/> for the same tracker pose.
    /// </summary>
    public static void ApplyInverseOffset(Vector3 trackerPos, Quaternion trackerRot, Vector3 inverseOffsetPosition, Quaternion inverseOffsetRotation, out Vector3 bonePos, out Quaternion boneRot)
    {
        bonePos = trackerPos + trackerRot * inverseOffsetPosition;
        boneRot = trackerRot * inverseOffsetRotation;
    }

    /// <summary>
    /// Unscaled device coord → scaled (BasisInput.ConvertToScaledDeviceCoord): scale the position by
    /// DeviceScale, then apply the rigid OffsetCoords (R,t). Rotation is offset*unscaled (scale-free).
    /// </summary>
    public static void ScaleDeviceCoord(Vector3 unscaledPos, Quaternion unscaledRot, float deviceScale, Vector3 offsetPos, Quaternion offsetRot, out Vector3 scaledPos, out Quaternion scaledRot)
    {
        scaledPos = offsetPos + offsetRot * (unscaledPos * deviceScale);
        scaledRot = offsetRot * unscaledRot;
    }

    /// <summary>
    /// Exact inverse of <see cref="ScaleDeviceCoord"/>: recover the unscaled device pose from a scaled
    /// one. Used to snapshot calibration-time tracker geometry in a scale-free form so it can be
    /// rebuilt at any future DeviceScale/OffsetCoords (see <see cref="ReprojectInverseOffsetPosition"/>).
    /// </summary>
    public static void UnscaleDeviceCoord(Vector3 scaledPos, Quaternion scaledRot, float deviceScale, Vector3 offsetPos, Quaternion offsetRot, out Vector3 unscaledPos, out Quaternion unscaledRot)
    {
        Quaternion invOffset = Quaternion.Inverse(offsetRot);
        float safeScale = (float.IsNaN(deviceScale) || float.IsInfinity(deviceScale) || deviceScale <= 1e-6f) ? 1f : deviceScale;
        unscaledPos = (invOffset * (scaledPos - offsetPos)) / safeScale;
        unscaledRot = invOffset * scaledRot;
    }

    /// <summary>
    /// Re-derives a calibrated FBT tracker's POSITION inverse offset for a (possibly different) avatar
    /// and DeviceScale, from scale-free calibration snapshots — the position analog of the rotation
    /// calibration's reference mechanism, and what lets an FBT calibration survive an avatar swap or a
    /// scale change without redoing the T-pose.
    ///
    /// Rebuilds the calibration geometry at the CURRENT scale: the tracker and head snapshots (unscaled
    /// device space) are re-scaled with <see cref="ScaleDeviceCoord"/>, the avatar is virtually anchored
    /// with DriveTpose's own math (yaw-flattened head; root placed so the head bone lands on the head),
    /// and the bone reference is that anchored root plus the avatar's own T-pose bind
    /// (TposeLocalScaled, avatar-root-local — the same quantity DriveTpose uses for the head). The
    /// offset then captures exactly like BasisInput.CalculateOffset does against a live T-pose. The
    /// player's live pose at reprojection time is irrelevant.
    /// </summary>
    public static void ReprojectInverseOffsetPosition(
        Vector3 calibUnscaledTrackerPos, Quaternion calibUnscaledTrackerRot,
        Vector3 calibUnscaledHeadPos, Quaternion calibUnscaledHeadRot,
        float deviceScale, Vector3 offsetPos, Quaternion offsetRot,
        Vector3 headTposeLocalScaled, Vector3 boneTposeLocalScaled,
        out Vector3 inverseOffsetPosition)
    {
        ScaleDeviceCoord(calibUnscaledTrackerPos, calibUnscaledTrackerRot, deviceScale, offsetPos, offsetRot, out Vector3 trackerPos, out Quaternion trackerRot);
        ScaleDeviceCoord(calibUnscaledHeadPos, calibUnscaledHeadRot, deviceScale, offsetPos, offsetRot, out Vector3 headPos, out Quaternion headRot);

        ComputeTposeAnchor(headPos, headRot, headTposeLocalScaled, out Vector3 rootPos, out Quaternion rootRot);

        Vector3 reference = rootPos + rootRot * boneTposeLocalScaled;
        ComputeInverseOffset(trackerPos, trackerRot, reference, Quaternion.identity, out inverseOffsetPosition, out _);
    }

    /// <summary>
    /// DriveTpose's anchor math, shared by everything that needs "where the T-posed avatar is
    /// anchored" WITHOUT reading the live avatar root: yaw-flatten the head, place the root so the
    /// head bone lands on the head. Reading the live AnimatorRoot instead is only valid in the
    /// instant after DriveTpose physically placed it — this derives the same frame from the head
    /// pose alone, so it also holds for captures where the avatar was never physically T-posed
    /// (device-reconnect offset captures, T-pose-free calibration).
    /// </summary>
    public static void ComputeTposeAnchor(Vector3 headPos, Quaternion headRot, Vector3 headTposeLocalScaled, out Vector3 anchorPos, out Quaternion anchorRot)
    {
        Vector3 flatFwd = headRot * Vector3.forward;
        flatFwd.y = 0f;
        anchorRot = flatFwd.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(flatFwd.normalized, Vector3.up);
        anchorPos = headPos - anchorRot * headTposeLocalScaled;
    }

    /// <summary>
    /// The DeviceScale denominator: the player's TRUE standing eye height in real-world metres -- the
    /// height the HMD center-eye sits at when standing level. DeviceScale divides the avatar's rendered
    /// eye height by this, so the avatar only feels right when this equals the real standing eye height.
    /// <paramref name="eyeReference"/> lifts a backend's tracked point up to the eyes: OpenVR fills it
    /// from SteamVR's eye-to-head transform (BasisInput.CenterEyeVerticalOffset) because it tracks the
    /// HMD pose origin, while a backend whose tracked point is already the center-eye (OpenXR
    /// centerEyePosition) passes 0. A shortfall in this denominator is exactly what the
    /// AdditionalPlayerHeight "nudge" is bridging by hand.
    /// </summary>
    public static float StandingEyeDenominator(float playerMeasuredHeight, float eyeReference, float additionalPlayerHeight)
    {
        return additionalPlayerHeight + eyeReference + playerMeasuredHeight;
    }

    /// <summary>
    /// DeviceScale: maps real HMD/tracker motion onto the avatar so the rendered first-person viewpoint
    /// lands at the avatar's scaled eye height. Equals (avatarUnscaledMetric * appliedUpScale) divided by
    /// <see cref="StandingEyeDenominator"/>. Feel is correct iff that denominator equals the player's true
    /// standing eye height E: a denominator short by g yields E/(E-g) too-tall and is cancelled by a +g
    /// nudge; a denominator long by g yields the matching too-short. Returns 1 for a degenerate
    /// (near-zero) denominator so a bad measurement can never poison the bones.
    /// </summary>
    public static float ComputeDeviceScale(float avatarUnscaledMetric, float appliedUpScale, float playerMeasuredHeight, float eyeReference, float additionalPlayerHeight)
    {
        float avatarScaledMetric = avatarUnscaledMetric * appliedUpScale;
        float denominator = StandingEyeDenominator(playerMeasuredHeight, eyeReference, additionalPlayerHeight);
        if (denominator > -1e-5f && denominator < 1e-5f)
        {
            return 1f;
        }
        return avatarScaledMetric / denominator;
    }

    /// <summary>
    /// Avatar-swap eye-height reuse decision (BasisHeightDriver.CapturePlayerHeight). Re-poll the live HMD
    /// only while no genuine standing eye height exists yet; once one does, an avatar load reuses it so fit no
    /// longer shifts with head pose at swap time. Explicit recalibration passes recapture=true to re-measure.
    /// </summary>
    /// <summary>Standing eye height as a fraction of full body height (eyes sit ~7% below the crown).</summary>
    public const float EyeToHeightRatio = 0.93f;
    /// <summary>Arm span as a fraction of full body height (ape index ≈ 1).</summary>
    public const float SpanToHeightRatio = 1.0f;
    /// <summary>
    /// How much taller the span-implied body must be than the eye-implied body before Auto trusts the
    /// arm span instead: normal anatomical variation (long-armed players, a few %) stays inside the
    /// band, while a broken eye measurement — calibrating while seated/slouched reads 25-35% short —
    /// falls far outside it.
    /// </summary>
    public const float AutoModeEyePreferenceBand = 1.08f;

    /// <summary>Full body height implied by a standing eye-height measurement.</summary>
    public static float ImpliedHeightFromEye(float playerEye) => playerEye / EyeToHeightRatio;
    /// <summary>Full body height implied by an arm-span measurement.</summary>
    public static float ImpliedHeightFromSpan(float playerSpan) => playerSpan / SpanToHeightRatio;

    /// <summary>
    /// Auto height-mode decision: trust the LONGER of the player's two body measurements. Both
    /// metrics under-measure easily (bent arms → short span; calibrating while seated or slouched →
    /// short eye height) but neither can over-measure past the real body, so the larger implied body
    /// height is the more trustworthy measurement. Eye height is preferred inside the tolerance band
    /// (it is the stabler metric and carries the standing-eye corrections); the span pair wins only
    /// when the eye measurement is implausibly short against the measured reach — e.g. calibrated
    /// sitting in a chair with arms out. Non-positive inputs disqualify that metric.
    /// </summary>
    public static bool AutoHeightModePicksArmSpan(float playerEye, float playerSpan)
    {
        if (playerEye <= 0f)
        {
            return playerSpan > 0f;
        }
        if (playerSpan <= 0f)
        {
            return false;
        }
        return ImpliedHeightFromSpan(playerSpan) > ImpliedHeightFromEye(playerEye) * AutoModeEyePreferenceBand;
    }

    public static bool ShouldRecaptureEyeHeight(bool recapture, bool hasGenuine)
    {
        return recapture || !hasGenuine;
    }
}
