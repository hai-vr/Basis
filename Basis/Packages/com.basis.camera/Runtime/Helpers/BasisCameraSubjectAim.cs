using Basis.Cinematics;
using UnityEngine;

public static class BasisCameraSubjectAim
{
    public const float FallbackTopAboveHeadRatio = 0.12f;
    public const float MinimumHeight = 0.05f;
    public const float MinimumRadius = 0.05f;

    public static Vector3 LookPoint(BasisCameraAimPoint aim, Vector3 normalPoint, Vector3 headPoint, Vector3 groundPos, float topY, float aimHeightOffset)
    {
        Vector3 point = normalPoint;
        switch (aim)
        {
            case BasisCameraAimPoint.Head:
                point = headPoint;
                break;
            case BasisCameraAimPoint.FullBody:
                if (topY - groundPos.y >= MinimumHeight)
                {
                    point = new Vector3(normalPoint.x, (groundPos.y + topY) * 0.5f, normalPoint.z);
                }
                break;
        }
        return point + Vector3.up * aimHeightOffset;
    }

    public static float FramingRadius(BasisCameraAimPoint aim, float framingRadius, float height, float scale)
    {
        float manual = Mathf.Max(MinimumRadius, framingRadius);
        if (aim != BasisCameraAimPoint.FullBody || height < MinimumHeight)
        {
            return manual;
        }
        float divisor = scale > 1e-4f ? scale : 1f;
        return Mathf.Max(MinimumRadius, height * 0.5f / divisor);
    }

    public static float FallbackTop(float groundY, float headY) => headY + Mathf.Max(0f, headY - groundY) * FallbackTopAboveHeadRatio;
}
