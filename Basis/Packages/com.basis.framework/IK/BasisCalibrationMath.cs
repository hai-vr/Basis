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
    public static bool ShouldRecaptureEyeHeight(bool recapture, bool hasGenuine)
    {
        return recapture || !hasGenuine;
    }
}
