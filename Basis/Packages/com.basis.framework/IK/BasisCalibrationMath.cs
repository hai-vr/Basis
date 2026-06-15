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
}
