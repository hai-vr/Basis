using UnityEngine;

/// <summary>
/// Pure math for the continuous tracker calibration refresh (BasisContinuousCalibration):
/// the yaw-flattened head body frame, the standing-idle gate checks, and the capped
/// exponential blend that walks a tracker's calibration snapshot toward its freshly
/// observed body-local pose. Kept free of scene / device state so the NUnit tests
/// exercise the exact runtime formulas.
///
/// All positions are UNSCALED device space (real-world metres, playspace floor at y = 0),
/// so every threshold below is a physical distance regardless of avatar scale.
/// </summary>
public static class BasisContinuousCalibrationCore
{
    const float k_Epsilon = 1e-6f;

    /// <summary>A tracker farther than this from its calibration spot means the limb is posed, not slipped.</summary>
    public const float PoseGateMeters = 0.12f;
    /// <summary>Gates must hold continuously this long before any correction is applied.</summary>
    public const float DwellSeconds = 1.5f;
    /// <summary>Per-tracker stillness ceiling (m/s).</summary>
    public const float TrackerStillSpeedMetersPerSecond = 0.10f;
    /// <summary>Head stillness ceiling (m/s) — a little looser, heads sway.</summary>
    public const float HeadStillSpeedMetersPerSecond = 0.15f;
    /// <summary>Head must sit within this fraction of the calibrated standing eye height.</summary>
    public const float HeadHeightBandFraction = 0.06f;
    /// <summary>Minimum XZ magnitude of the head forward — below this (pitch past ~60°) the yaw frame is unreliable.</summary>
    public const float MinFlatForward = 0.5f;
    /// <summary>Exponential time constant of the correction blend (seconds of in-pose standing).</summary>
    public const float CorrectionTauSeconds = 8f;
    /// <summary>Total correction can never carry a snapshot farther than this from its calibrated home.</summary>
    public const float CorrectionCapMeters = 0.05f;
    /// <summary>Position drift beyond this is flagged as "recalibrate" (still corrected up to the cap).</summary>
    public const float FlagPositionMeters = 0.075f;
    /// <summary>Rotation drift beyond this is flagged as "recalibrate" (rotation is never auto-corrected).</summary>
    public const float FlagRotationDegrees = 20f;
    /// <summary>Residual below which a tracker counts as settled again.</summary>
    public const float SettledResidualMeters = 0.005f;
    /// <summary>Blend steps smaller than this are skipped so idle frames never touch the sim.</summary>
    public const float MinAppliedStepMeters = 1e-4f;
    /// <summary>A snapshot moving more than this without us writing it means an external recapture.</summary>
    public const float ExternalSnapshotEpsilonMeters = 1e-3f;

    /// <summary>
    /// Body frame from the head pose alone: origin on the playspace floor under the head,
    /// rotation the yaw-flattened head facing — the same normalization the constellation
    /// classifier and ComputeTposeAnchor use. False when the head pitches so far the yaw
    /// is degenerate; callers must not compare body-local poses across frames that frame.
    /// </summary>
    public static bool TryComputeBodyFrame(Vector3 headPosition, Quaternion headRotation, out Vector3 origin, out Quaternion rotation)
    {
        origin = new Vector3(headPosition.x, 0f, headPosition.z);
        Vector3 flat = headRotation * Vector3.forward;
        flat.y = 0f;
        if (flat.sqrMagnitude < MinFlatForward * MinFlatForward)
        {
            rotation = Quaternion.identity;
            return false;
        }
        rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
        return true;
    }

    /// <summary>Express a playspace pose in a body frame. Inverse of <see cref="FromBodyLocalPosition"/> for position.</summary>
    public static void ToBodyLocal(Vector3 origin, Quaternion rotation, Vector3 position, Quaternion poseRotation, out Vector3 localPosition, out Quaternion localRotation)
    {
        Quaternion inverse = Quaternion.Inverse(rotation);
        localPosition = inverse * (position - origin);
        localRotation = inverse * poseRotation;
    }

    /// <summary>Lift a body-local position back into the playspace of the given frame.</summary>
    public static Vector3 FromBodyLocalPosition(Vector3 origin, Quaternion rotation, Vector3 localPosition)
    {
        return origin + rotation * localPosition;
    }

    /// <summary>
    /// True when the head sits at its calibration standing height: within
    /// <see cref="HeadHeightBandFraction"/> (of the standing eye height) of the head pose captured at
    /// ritual calibration. The gated quantity is the head CONTROL, which rides an avatar-proportional
    /// eye-to-head-bone offset below the raw eye — comparing head-to-head cancels that offset, where
    /// gating against the eye height itself sat the band off-center by the gap (often the whole band).
    /// Rejects seated/crouched play and a degenerate eye height.
    /// </summary>
    public static bool IsStandingHeight(float headHeight, float calibrationHeadHeight, float standingEyeHeight)
    {
        if (standingEyeHeight < 0.5f)
        {
            return false;
        }
        return Mathf.Abs(headHeight - calibrationHeadHeight) <= standingEyeHeight * HeadHeightBandFraction;
    }

    /// <summary>Fraction of the remaining error one frame closes for an exponential blend of time constant tau.</summary>
    public static float StepFraction(float deltaTime, float tauSeconds)
    {
        if (tauSeconds <= k_Epsilon)
        {
            return 1f;
        }
        return 1f - Mathf.Exp(-Mathf.Max(deltaTime, 0f) / tauSeconds);
    }

    /// <summary>
    /// One correction step: move <paramref name="stored"/> toward <paramref name="target"/> by
    /// <paramref name="fraction"/>, then clamp the result so it never sits farther than
    /// <paramref name="capMeters"/> from <paramref name="home"/> — the total-correction budget
    /// that keeps a mis-gated blend from ever walking the calibration away. Returns the length
    /// of the step actually applied.
    /// </summary>
    public static float BlendWithCap(Vector3 home, Vector3 stored, Vector3 target, float fraction, float capMeters, out Vector3 result)
    {
        Vector3 blended = Vector3.Lerp(stored, target, Mathf.Clamp01(fraction));
        Vector3 fromHome = blended - home;
        float distance = fromHome.magnitude;
        if (distance > capMeters)
        {
            blended = capMeters <= k_Epsilon ? home : home + fromHome * (capMeters / distance);
        }
        result = blended;
        return (result - stored).magnitude;
    }
}
