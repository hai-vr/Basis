using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;
public static class BasisAnimationRuntimeUtils
{
    const float k_SqrEpsilon = 1e-8f;

    // ------------------------ TWO-BONE IK (ARMS) ------------------------
    public static void SolveTwoBoneIKArms(
        AnimationStream stream,
        ReadWriteTransformHandle root,
        ReadWriteTransformHandle mid,
        ReadWriteTransformHandle tip,
        AffineTransform target,
        AffineTransform hint,
        bool hintWeight,
        AffineTransform targetOffset
    )
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

        // Prefer current bend plane; fallbacks to hint / at if collinear.
        Vector3 axis = Vector3.Cross(ab, bc);
        if (axis.sqrMagnitude < k_SqrEpsilon)
        {
            axis = hintWeight ? Vector3.Cross(hint.translation - aPosition, bc) : Vector3.zero;
            if (axis.sqrMagnitude < k_SqrEpsilon) axis = Vector3.Cross(at, bc);
            if (axis.sqrMagnitude < k_SqrEpsilon) axis = Vector3.up;
        }
        axis = Vector3.Normalize(axis);

        float a = 0.5f * (oldAbcAngle - newAbcAngle);
        float sin = Mathf.Sin(a);
        float cos = Mathf.Cos(a);
        Quaternion deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);
        mid.SetRotation(stream, deltaR * mid.GetRotation(stream));

        cPosition = tip.GetPosition(stream);
        ac = cPosition - aPosition;
        root.SetRotation(stream, QuaternionExt.FromToRotation(ac, at) * root.GetRotation(stream));

        if (hintWeight)
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

    // ------------------------ TWO-BONE IK (LEGS/TORSO) ------------------------
    public static void SolveTwoBoneIKLegsAndTorso(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, AffineTransform target, AffineTransform hint, bool HasHint, AffineTransform targetOffset, Vector3 BendNormal)
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
            if (axis.sqrMagnitude < k_SqrEpsilon) axis = Vector3.Cross(at, bc);
            if (axis.sqrMagnitude < k_SqrEpsilon) axis = BendNormal;
        }
        else axis = BendNormal;

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

    // ------------------------ INVERSE SETUP HELPERS ------------------------
    public static void InverseSolveTwoBoneIK(
        AnimationStream stream,
        ReadOnlyTransformHandle root,
        ReadOnlyTransformHandle mid,
        ReadOnlyTransformHandle tip,
        ReadWriteTransformHandle target,
        ReadWriteTransformHandle hint,
        float posWeight,
        float rotWeight,
        float hintWeight,
        AffineTransform targetOffset
    )
    {
        Vector3 rootPosition = root.GetPosition(stream);
        Vector3 midPosition = mid.GetPosition(stream);
        tip.GetGlobalTR(stream, out var tipPosition, out var tipRotation);
        target.GetGlobalTR(stream, out var targetPosition, out var targetRotation);
        bool isHintValid = hint.IsValid(stream);
        Vector3 hintPosition = Vector3.zero;
        if (isHintValid) hintPosition = hint.GetPosition(stream);

        InverseSolveTwoBoneIK(rootPosition, midPosition, tipPosition, tipRotation, ref targetPosition,
            ref targetRotation, ref hintPosition, isHintValid, posWeight, rotWeight, hintWeight, targetOffset);

        target.SetPosition(stream, targetPosition);
        target.SetRotation(stream, targetRotation);
        hint.SetPosition(stream, hintPosition);
    }

    public static void InverseSolveTwoBoneIK(
        Vector3 rootPosition,
        Vector3 midPosition,
        Vector3 tipPosition, Quaternion tipRotation,
        ref Vector3 targetPosition, ref Quaternion targetRotation,
        ref Vector3 hintPosition, bool isHintValid,
        float posWeight,
        float rotWeight,
        float hintWeight,
        AffineTransform targetOffset
    )
    {
        targetPosition = (posWeight > 0f) ? tipPosition + targetOffset.translation : targetPosition;
        targetRotation = (rotWeight > 0f) ? tipRotation * targetOffset.rotation : targetRotation;

        if (isHintValid)
        {
            var ac = tipPosition - rootPosition;
            var ab = midPosition - rootPosition;
            var bc = tipPosition - midPosition;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;

            var acSqrMag = Vector3.Dot(ac, ac);
            var projectionPoint = rootPosition;
            if (acSqrMag > k_SqrEpsilon)
                projectionPoint += Vector3.Dot(ab / acSqrMag, ac) * ac;
            var poleVectorDirection = midPosition - projectionPoint;

            var scale = abLen + bcLen;
            hintPosition = (hintWeight > 0f) ? projectionPoint + (poleVectorDirection.normalized * scale) : hintPosition;
        }
    }

    // ------------------------ COLLISION MATH ------------------------
    public static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float abSqr = Vector3.Dot(ab, ab);
        if (abSqr <= k_SqrEpsilon) return a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / abSqr);
        return a + ab * t;
    }

    /// <summary>Find closest points between two line segments P1Q1 and P2Q2 (returns s,t in [0..1] and the points).</summary>
    public static void SegmentSegmentClosestPoints(
        Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2,
        out float s, out float t,
        out Vector3 c1, out Vector3 c2)
    {
        Vector3 d1 = q1 - p1; // Direction vector of segment S1
        Vector3 d2 = q2 - p2; // Direction vector of segment S2
        Vector3 r = p1 - p2;
        float a = Vector3.Dot(d1, d1); // Squared length of segment S1, always nonnegative
        float e = Vector3.Dot(d2, d2); // Squared length of segment S2, always nonnegative
        float f = Vector3.Dot(d2, r);

        if (a <= k_SqrEpsilon && e <= k_SqrEpsilon)
        {
            s = t = 0.0f;
            c1 = p1;
            c2 = p2;
            return;
        }
        if (a <= k_SqrEpsilon)
        {
            s = 0.0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= k_SqrEpsilon)
            {
                t = 0.0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;

                if (denom != 0.0f)
                    s = Mathf.Clamp01((b * f - c * e) / denom);
                else
                    s = 0.0f;

                t = (b * s + f) / e;
                if (t < 0.0f)
                {
                    t = 0.0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1.0f)
                {
                    t = 1.0f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }

        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }

    /// <summary>
    /// Resolve capsule-vs-capsule penetration by returning a translation to apply to the FIRST capsule (p1,q1,r1)
    /// that separates them minimally. If not intersecting, returns Vector3.zero.
    /// </summary>
    public static Vector3 CapsuleCapsuleResolve(
        Vector3 p1, Vector3 q1, float r1,
        Vector3 p2, Vector3 q2, float r2)
    {
        SegmentSegmentClosestPoints(p1, q1, p2, q2, out _, out _, out var c1, out var c2);
        Vector3 n = c1 - c2;
        float dSqr = Vector3.Dot(n, n);
        float rSum = r1 + r2;

        if (dSqr >= rSum * rSum) return Vector3.zero; // no penetration

        // build a stable normal
        Vector3 normal;
        if (dSqr > k_SqrEpsilon) normal = n / Mathf.Sqrt(dSqr);
        else
        {
            // segments almost overlapping perfectly; pick any normal orthogonal to the chest axis
            Vector3 axis = (q2 - p2);
            normal = Vector3.Normalize(Vector3.Cross(axis, Vector3.up));
            if (normal.sqrMagnitude < 1e-6f)
                normal = Vector3.Normalize(Vector3.Cross(axis, Vector3.right));
            if (normal.sqrMagnitude < 1e-6f)
                normal = Vector3.up;
        }

        float d = Mathf.Sqrt(Mathf.Max(dSqr, 0f));
        float penetration = (rSum - d);
        return normal * penetration;
    }

    /// <summary>
    /// Rotate chain around AC (root→tip) to move elbow B towards desired pushed point B*. Keeps A and C fixed (pre-solve re-run recommended).
    /// </summary>
    public static void SwingElbowAroundAC(
        AnimationStream stream,
        ReadWriteTransformHandle root,
        ReadWriteTransformHandle mid,
        ReadWriteTransformHandle tip,
        Vector3 desiredB
    )
    {
        Vector3 A = root.GetPosition(stream);
        Vector3 C = tip.GetPosition(stream);
        Vector3 B = mid.GetPosition(stream);

        Vector3 AC = C - A;
        float acSqr = Vector3.Dot(AC, AC);
        if (acSqr <= k_SqrEpsilon) return;

        Vector3 n = AC / Mathf.Sqrt(acSqr); // axis
        // project B-A and desiredB-A onto plane orthogonal to AC
        Vector3 v1 = B - A; v1 -= n * Vector3.Dot(v1, n);
        Vector3 v2 = desiredB - A; v2 -= n * Vector3.Dot(v2, n);

        float v1Sqr = Vector3.Dot(v1, v1);
        float v2Sqr = Vector3.Dot(v2, v2);
        if (v1Sqr <= k_SqrEpsilon || v2Sqr <= k_SqrEpsilon) return;

        v1 /= Mathf.Sqrt(v1Sqr);
        v2 /= Mathf.Sqrt(v2Sqr);

        float dot = Mathf.Clamp(Vector3.Dot(v1, v2), -1f, 1f);
        float ang = Mathf.Acos(dot);
        Vector3 cross = Vector3.Cross(v1, v2);
        float dir = Mathf.Sign(Vector3.Dot(cross, n));
        Quaternion swing = Quaternion.AngleAxis(ang * dir * Mathf.Rad2Deg, n);

        root.SetRotation(stream, swing * root.GetRotation(stream));
    }

    // ------------------------ MISC UTILS ------------------------
    static float TriangleAngle(float aLen, float aLen1, float aLen2)
    {
        float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (aLen1 * aLen2) / 2.0f, -1.0f, 1.0f);
        return Mathf.Acos(c);
    }

    public static float SqrDistance(Vector3 lhs, Vector3 rhs) => (rhs - lhs).sqrMagnitude;
    public static float Square(float value) => value * value;
    public static Vector3 Lerp(Vector3 a, Vector3 b, Vector3 t) => Vector3.Scale(a, Vector3.one - t) + Vector3.Scale(b, t);
    public static float Select(float a, float b, float c) => (c > 0f) ? b : a;
    public static Vector3 Select(Vector3 a, Vector3 b, Vector3 c) => new Vector3(Select(a.x, b.x, c.x), Select(a.y, b.y, c.y), Select(a.z, b.z, c.z));

    public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal)
    {
        float sqrMag = Vector3.Dot(planeNormal, planeNormal);
        var dot = Vector3.Dot(vector, planeNormal);
        return new Vector3(
            vector.x - planeNormal.x * dot / sqrMag,
            vector.y - planeNormal.y * dot / sqrMag,
            vector.z - planeNormal.z * dot / sqrMag
        );
    }

    public static void PassThrough(AnimationStream stream, ReadWriteTransformHandle handle)
    {
        handle.GetLocalTRS(stream, out Vector3 position, out Quaternion rotation, out Vector3 scale);
        handle.SetLocalTRS(stream, position, rotation, scale);
    }
    public static Vector3 PushOutFromCapsule(Vector3 p, Vector3 a, Vector3 b, float radiusWithSkin)
    {
        // Closest point on segment AB to p
        Vector3 q = ClosestPointOnSegment(p, a, b);
        Vector3 qp = p - q;
        float dSqr = Vector3.Dot(qp, qp);

        // outside -> no change
        if (dSqr >= radiusWithSkin * radiusWithSkin) return p;

        // inside -> push out to the surface along normal
        float d = Mathf.Sqrt(Mathf.Max(dSqr, k_SqrEpsilon));
        Vector3 n = (d > 0f) ? (qp / d) : Vector3.up; // fallback normal if exactly centered
        return q + n * radiusWithSkin;
    }
}
