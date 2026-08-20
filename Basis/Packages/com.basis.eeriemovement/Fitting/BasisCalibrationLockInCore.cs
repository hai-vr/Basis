using UnityEngine;
public static class BasisCalibrationLockInCore
{
    const float epsilon = 1e-6f;
    public const float DefaultMaxYawDeg = 18f, DefaultMaxTiltDeg = 35f;
    public static float ProximityWeight(float distance, float captureRadius, float falloffRadius)
    {
        if (falloffRadius - captureRadius <= epsilon)
        {
            return distance <= captureRadius ? 1f : 0f;
        }
        float t = (distance - captureRadius) / (falloffRadius - captureRadius);
        return Mathf.Clamp01(1f - t);
    }
    public static float ProximityRadius(float weight, float minDiameter, float maxDiameter)
    {
        return Mathf.Lerp(maxDiameter, minDiameter, Mathf.Clamp01(weight));
    }
    public static bool IsLocked(float distance, float captureRadius)
    {
        return distance <= captureRadius;
    }
    public static float FootYawDegrees(Vector3 bodyForward, Vector3 footForward, Vector3 up)
    {
        Vector3 b = Vector3.ProjectOnPlane(bodyForward, up), f = Vector3.ProjectOnPlane(footForward, up);
        if (b.sqrMagnitude < epsilon || f.sqrMagnitude < epsilon)
        {
            return 0f;
        }
        return Vector3.SignedAngle(b.normalized, f.normalized, up);
    }
    public static float FootTiltDegrees(Vector3 footUp, Vector3 worldUp)
    {
        if (footUp.sqrMagnitude < epsilon || worldUp.sqrMagnitude < epsilon)
        {
            return 0f;
        }
        return Vector3.Angle(footUp, worldUp);
    }
    public static bool IsFootAligned(float yawDeg, float tiltDeg, float maxYawDeg, float maxTiltDeg)
    {
        return Mathf.Abs(yawDeg) <= maxYawDeg && Mathf.Abs(tiltDeg) <= maxTiltDeg;
    }
    public static float FootAlignmentScore(float yawDeg, float tiltDeg, float maxYawDeg, float maxTiltDeg)
    {
        float yaw = AxisScore(Mathf.Abs(yawDeg), maxYawDeg), tilt = AxisScore(Mathf.Abs(tiltDeg), maxTiltDeg);
        return Mathf.Min(yaw, tilt);
    }
    static float AxisScore(float absDeg, float maxDeg)
    {
        if (maxDeg <= epsilon)
        {
            return absDeg <= epsilon ? 1f : 0f;
        }
        return Mathf.Clamp01(1f - absDeg / maxDeg);
    }
}
