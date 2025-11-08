using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;
public static class BasisAnimationFullBodyIK
{
    public static void SolveHipsAndSpine(
        AnimationStream stream,

        // --- Hips minimal driver ---
        Vector3Property targetPositionHips,
        Vector4Property targetRotationHips,
        Vector4Property offsetRotationHips,

        // --- Head + Legs (classic TwoBone) ---
        BoolProperty EnableSpineIK,

        ReadWriteTransformHandle HandleHips,
        ReadWriteTransformHandle HandleSpine,
        ReadWriteTransformHandle HandleChest,
        ReadWriteTransformHandle HandleUpperChest,
        ReadWriteTransformHandle HandleNeck,
        ReadWriteTransformHandle HandleHead,
        Vector3Property targetPositionHead,
        Vector4Property targetRotationHead,
        Vector3Property hintPositionHead,
        Vector4Property hintRotationHead,
        BoolProperty hintWeightChestArea,
        AffineTransform targetOffsetHead,
        Vector3Property bendNormalHead
    )
    {
        if (!EnableSpineIK.Get(stream))
        {
            Pass(stream, HandleChest, HandleNeck, HandleHead);
            BasisAnimationRuntimeUtils.PassThrough(stream, HandleHips);
            return;
        }
        else
        {
            if (HandleHips.IsValid(stream))
            {
                Vector3 hipPos = targetPositionHips.Get(stream);
                Quaternion hipRot = V4ToQuat(targetRotationHips.Get(stream));
                Quaternion hipOff = V4ToQuat(offsetRotationHips.Get(stream));

                HandleHips.SetPosition(stream, hipPos);
                HandleHips.SetRotation(stream, hipRot * hipOff); // apply offset in target space
            }
        }

        if (!(HandleChest.IsValid(stream) && HandleNeck.IsValid(stream) && HandleHead.IsValid(stream)))
        {
            Pass(stream, HandleChest, HandleNeck, HandleHead);
            return;
        }

        Quaternion tRot = V4ToQuat(targetRotationHead.Get(stream));
        Quaternion hRot = V4ToQuat(hintRotationHead.Get(stream));

        AffineTransform target = new AffineTransform(targetPositionHead.Get(stream), tRot);
        AffineTransform hint = new AffineTransform(hintPositionHead.Get(stream), hRot);
        Vector3 bendNormal = bendNormalHead.Get(stream);

        SolveTwoBoneSpine(
            stream,
            HandleChest, HandleNeck, HandleHead,
            target,
            hint,
            hintWeightChestArea.Get(stream),
            targetOffsetHead,
            bendNormal
        );
    }
    public static float TriangleAngle(float aLen, float aLen1, float aLen2)
    {
        float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (aLen1 * aLen2) / 2.0f, -1.0f, 1.0f);
        return Mathf.Acos(c);
    }
    const float k_SqrEpsilon = 1e-8f;
    /// <summary>
    /// Evaluates the Two-Bone IK algorithm.
    /// </summary>
    /// <param name="stream">The animation stream to work on.</param>
    /// <param name="root">The transform handle for the root transform.</param>
    /// <param name="mid">The transform handle for the mid transform.</param>
    /// <param name="tip">The transform handle for the tip transform.</param>
    /// <param name="target">The transform handle for the target transform.</param>
    /// <param name="hint">The transform handle for the hint transform.</param>
    /// <param name="HasHint">The weight for which hint transform has an effect on IK calculations. This is a value in between 0 and 1.</param>
    /// <param name="targetOffset">The offset applied to the target transform.</param>
    public static void SolveTwoBoneSpine(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, AffineTransform target, AffineTransform hint, bool HasHint, AffineTransform targetOffset, Vector3 BendNormal)
    {
        Vector3 aPosition = root.GetPosition(stream);
        Vector3 bPosition = mid.GetPosition(stream);
        Vector3 cPosition = tip.GetPosition(stream);

        Vector3 targetPos = target.translation;
        Quaternion targetRot = target.rotation;

        Vector3 tPosition = targetPos + targetOffset.translation;
        Quaternion tRotation = targetRot * targetOffset.rotation;

        Vector3 ab = bPosition - aPosition;
        Vector3 bc = cPosition - bPosition;
        Vector3 ac = cPosition - aPosition;
        Vector3 at = tPosition - aPosition;

        float abLen = ab.magnitude;
        float bcLen = bc.magnitude;
        float acLen = ac.magnitude;
        float atLen = at.magnitude;

        float oldAbcAngle = TriangleAngle(acLen, abLen, bcLen);
        float newAbcAngle = TriangleAngle(atLen, abLen, bcLen);
        Vector3 axis;
        if (HasHint)
        {
            axis = Vector3.Cross(hint.translation - aPosition, bc);

            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                axis = Vector3.Cross(at, bc);
            }

            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                axis = BendNormal;
            }
        }
        else
        {
            axis = BendNormal;
        }

        axis = Vector3.Normalize(axis);

        float halfAngle = 0.5f * (oldAbcAngle - newAbcAngle);
        float sin = Mathf.Sin(halfAngle);
        float cos = Mathf.Cos(halfAngle);
        Quaternion deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);
        mid.SetRotation(stream, deltaR * mid.GetRotation(stream));

        cPosition = tip.GetPosition(stream);
        ac = cPosition - aPosition;
        root.SetRotation(stream, QuaternionExt.FromToRotation(ac, at) * root.GetRotation(stream));

        if (HasHint)
        {
            float acSqrMag = ac.sqrMagnitude;
            if (acSqrMag > 0f)
            {
                bPosition = mid.GetPosition(stream);
                cPosition = tip.GetPosition(stream);
                ab = bPosition - aPosition;
                ac = cPosition - aPosition;

                Vector3 acNorm = ac / Mathf.Sqrt(acSqrMag);
                Vector3 ah = hint.translation - aPosition;
                Vector3 abProj = ab - acNorm * Vector3.Dot(ab, acNorm);
                Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);

                float maxReach = abLen + bcLen;
                if (abProj.sqrMagnitude > (maxReach * maxReach * 0.001f) && ahProj.sqrMagnitude > 0f)
                {
                    Quaternion hintR = QuaternionExt.FromToRotation(abProj, ahProj);
                    hintR = QuaternionExt.NormalizeSafe(hintR);
                    root.SetRotation(stream, hintR * root.GetRotation(stream));
                }
            }
        }

        tip.SetRotation(stream, tRotation);
    }
    public static void Pass(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip)
    {
        if (root.IsValid(stream)) BasisAnimationRuntimeUtils.PassThrough(stream, root);
        if (mid.IsValid(stream)) BasisAnimationRuntimeUtils.PassThrough(stream, mid);
        if (tip.IsValid(stream)) BasisAnimationRuntimeUtils.PassThrough(stream, tip);
    }
    public static Quaternion V4ToQuat(Vector4 v) => new Quaternion(v.x, v.y, v.z, v.w);
}
