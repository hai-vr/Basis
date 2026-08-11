using UnityEngine;

namespace Basis.IK
{
    public struct BasisButterflyKneeInput
    {
        public Vector3 HipPosition;
        public Vector3 FootPosition;
        public Vector3 FootInstepDir;
        public Vector3 OutwardDir;
        public Vector3 DefaultBendDir;
        public Vector3 PlayerUp;
        public Vector3 TorsoFacingDir;
        public float UpperLength;
        public float LowerLength;
        public float MaxOpenDeg;
        public float Strength;
        public float SupineFloor;
    }

    public struct BasisButterflyKneeResult
    {
        public Vector3 KneeHint;
        public float HintWeight;
        public float OpenAngleDeg;
        public float Supine01;
        public float FootTilt01;
        public float PullIn01;
    }

    public static class BasisButterflyKneeCore
    {
        public const float ReclineStartDot = 0.50f;
        public const float ReclineFullDot = 0.85f;

        public const float FootTiltRefDeg = 55f;

        public const float PullInStartRatio = 0.97f;
        public const float PullInFullRatio = 0.60f;

        public const float PullInFloor = 0.20f;

        public const float EngageFullThreshold = 0.30f;

        public const float DefaultMaxOpenDeg = 60f;

        const float k_Epsilon = 1e-5f;

        public static void Solve(in BasisButterflyKneeInput i, out BasisButterflyKneeResult r)
        {
            r = default;

            float maxOpenDeg = i.MaxOpenDeg > k_Epsilon ? i.MaxOpenDeg : DefaultMaxOpenDeg;

            Vector3 hipToFoot = i.FootPosition - i.HipPosition;
            float dist = hipToFoot.magnitude;
            float maxReach = i.UpperLength + i.LowerLength;
            Vector3 axis = dist > k_Epsilon ? hipToFoot / dist : Vector3.zero;

            Vector3 up = i.PlayerUp.sqrMagnitude > k_Epsilon ? i.PlayerUp.normalized : Vector3.up;
            Vector3 belly = i.TorsoFacingDir.sqrMagnitude > k_Epsilon ? i.TorsoFacingDir.normalized : Vector3.forward;
            float supine01 = Saturate(InvLerp(ReclineStartDot, ReclineFullDot, Vector3.Dot(belly, up)));

            Vector3 outward = i.OutwardDir.sqrMagnitude > k_Epsilon ? i.OutwardDir.normalized : Vector3.zero;
            Vector3 instep = i.FootInstepDir.sqrMagnitude > k_Epsilon ? i.FootInstepDir.normalized : Vector3.zero;
            float tiltSin = Mathf.Clamp(Vector3.Dot(instep, outward), -1f, 1f);
            float tiltDeg = Mathf.Asin(Mathf.Max(0f, tiltSin)) * Mathf.Rad2Deg;
            float footTilt01 = Saturate(tiltDeg / Mathf.Max(1f, FootTiltRefDeg));

            float reachRatio = maxReach > k_Epsilon ? dist / maxReach : 1f;
            float pullIn01 = Saturate(InvLerp(PullInStartRatio, PullInFullRatio, reachRatio));
            float amplify = Mathf.Lerp(PullInFloor, 1f, pullIn01);

            r.Supine01 = supine01;
            r.FootTilt01 = footTilt01;
            r.PullIn01 = pullIn01;

            float strength = Saturate(i.Strength);

            float supineGate = Mathf.Max(supine01, Saturate(i.SupineFloor));
            float engage = supineGate * footTilt01;
            if (engage <= k_Epsilon || strength <= k_Epsilon || axis == Vector3.zero)
            {
                r.HintWeight = 0f;
                r.OpenAngleDeg = 0f;
                r.KneeHint = BuildHint(i.HipPosition, i.FootPosition, i.DefaultBendDir, axis, i.UpperLength, 0f, outward);
                return;
            }

            float openFrac = Saturate(engage * amplify);
            float openDeg = openFrac * maxOpenDeg;

            r.OpenAngleDeg = openDeg;

            r.HintWeight = strength * Saturate(engage / EngageFullThreshold);
            r.KneeHint = BuildHint(i.HipPosition, i.FootPosition, i.DefaultBendDir, axis, i.UpperLength, openDeg, outward);
        }

        static Vector3 BuildHint(Vector3 hip, Vector3 foot, Vector3 defaultBendDir, Vector3 axis, float upperLen, float openDeg, Vector3 outward)
        {
            Vector3 mid = (hip + foot) * 0.5f;
            float radius = upperLen > k_Epsilon ? upperLen : 0.4f;

            if (axis.sqrMagnitude < k_Epsilon)
            {
                Vector3 d = defaultBendDir.sqrMagnitude > k_Epsilon ? defaultBendDir.normalized : Vector3.up;
                return mid + d * radius;
            }

            Vector3 defPerp = Vector3.ProjectOnPlane(defaultBendDir, axis);
            if (defPerp.sqrMagnitude < k_Epsilon)
            {
                defPerp = Vector3.ProjectOnPlane(Vector3.forward, axis);
                if (defPerp.sqrMagnitude < k_Epsilon) defPerp = Vector3.ProjectOnPlane(Vector3.up, axis);
            }
            defPerp.Normalize();

            Vector3 outPerp = Vector3.ProjectOnPlane(outward, axis);
            if (outPerp.sqrMagnitude < k_Epsilon || openDeg <= k_Epsilon)
            {
                return mid + defPerp * radius;
            }
            outPerp.Normalize();

            Vector3 hintDir = Vector3.RotateTowards(defPerp, outPerp, openDeg * Mathf.Deg2Rad, 0f);
            if (hintDir.sqrMagnitude < k_Epsilon) hintDir = defPerp;
            else hintDir.Normalize();
            return mid + hintDir * radius;
        }

        static float InvLerp(float a, float b, float v) => Mathf.Approximately(a, b) ? (v >= b ? 1f : 0f) : (v - a) / (b - a);
        static float Saturate(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    public struct BasisSwingContinuityState
    {
        public Vector3 LastDir;
        public Vector3 LastAxis;
        public Vector3 LastTarget;
        public int SmoothState;
        public bool Seeded;
    }

    public struct BasisSwingContinuityResult
    {
        public bool Valid;
        public bool ApplySwing;
        public Vector3 NewDir;
        public BasisSwingContinuityState State;
    }

    public static class BasisSwingContinuityCore
    {
        const float k_SqrEpsilon = 1e-8f;
        const float k_Epsilon = 1e-5f;

        public static void Step(BasisSwingContinuityState s, Vector3 a, Vector3 b, Vector3 c,
            Vector3 targetPos, int collided, float rateDegPerSec, float dt, out BasisSwingContinuityResult r)
        {
            r = default;
            r.State = s;

            Vector3 ac = c - a;
            float acSqr = ac.sqrMagnitude;
            if (acSqr < k_SqrEpsilon)
            {
                return;
            }
            Vector3 axis = ac / Mathf.Sqrt(acSqr);

            Vector3 perp = b - a;
            perp -= axis * Vector3.Dot(perp, axis);
            float perpSqr = perp.sqrMagnitude;
            if (perpSqr < k_SqrEpsilon)
            {
                return;
            }
            Vector3 currentDir = perp / Mathf.Sqrt(perpSqr);
            r.Valid = true;

            bool armed = s.SmoothState < 0;
            bool collisionChanged = !armed && collided != s.SmoothState;

            bool seeded = s.Seeded;
            float chainLen = (b - a).magnitude + (c - b).magnitude;
            float teleThresh = 0.6f * chainLen;
            bool teleport = seeded && (targetPos - s.LastTarget).sqrMagnitude > teleThresh * teleThresh;
            if (rateDegPerSec <= 0f || !seeded || teleport || (!armed && !collisionChanged))
            {
                r.State = new BasisSwingContinuityState
                {
                    LastDir = currentDir,
                    LastAxis = axis,
                    LastTarget = targetPos,
                    SmoothState = collided,
                    Seeded = true,
                };
                return;
            }

            int smoothState = -1;

            Vector3 carried = BasisQuaternionExt.FromToRotation(s.LastAxis, axis) * s.LastDir;
            carried -= axis * Vector3.Dot(carried, axis);
            float carriedSqr = carried.sqrMagnitude;
            bool easing = false;
            if (carriedSqr >= k_SqrEpsilon)
            {
                carried /= Mathf.Sqrt(carriedSqr);
                float angleDeg = Vector3.Angle(carried, currentDir);
                float maxStep = rateDegPerSec * dt;
                if (angleDeg > maxStep && angleDeg > k_Epsilon)
                {
                    Vector3 newDir = Vector3.Slerp(carried, currentDir, maxStep / angleDeg);
                    r.ApplySwing = true;
                    r.NewDir = newDir;
                    currentDir = newDir;
                    easing = true;
                }
            }

            if (!easing)
            {
                smoothState = collided;
            }

            r.State = new BasisSwingContinuityState
            {
                LastDir = currentDir,
                LastAxis = axis,
                LastTarget = targetPos,
                SmoothState = smoothState,
                Seeded = true,
            };
        }
    }

    public static class BasisTrackerBendNormalCore
    {
        const float k_SqrEpsilon = 1e-8f;

        public static Vector3 CaptureLocalAxis(Quaternion trackerRotCalib, Vector3 worldNormalCalib)
        {
            if (worldNormalCalib.sqrMagnitude < k_SqrEpsilon)
            {
                return Vector3.zero;
            }

            return Quaternion.Inverse(trackerRotCalib) * worldNormalCalib.normalized;
        }

        public static Vector3 ResolveWorldNormal(Quaternion trackerRotLive, Vector3 localAxis, Vector3 fallbackWorldNormal)
        {
            if (localAxis.sqrMagnitude < k_SqrEpsilon)
            {
                return fallbackWorldNormal;
            }

            Vector3 world = trackerRotLive * localAxis;
            return world.sqrMagnitude < k_SqrEpsilon ? fallbackWorldNormal : world.normalized;
        }
    }

    public struct BasisTwistSolveInput
    {
        public Quaternion ParentRotation;
        public Quaternion ChildRotation;
        public Vector3 ParentToChild;
        public float Fraction;
    }

    public struct BasisTwistSolveResult
    {
        public bool Apply;
        public Quaternion TwistWorldRotation;
        public Quaternion TwistOnly;
        public float TwistAngleDeg;
    }

    public static class BasisTwistSolveCore
    {
        const float k_SqrEpsilon = 1e-8f;

        public static void Solve(in BasisTwistSolveInput i, out BasisTwistSolveResult r)
        {
            r = default;
            if (i.Fraction <= 0f || i.ParentToChild.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            Vector3 axis = (Quaternion.Inverse(i.ParentRotation) * i.ParentToChild).normalized;
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            Quaternion childLocal = Quaternion.Inverse(i.ParentRotation) * i.ChildRotation;
            Quaternion twistOnly = ExtractTwist(childLocal, axis);
            Quaternion partialTwist = Quaternion.Slerp(Quaternion.identity, twistOnly, Mathf.Clamp01(i.Fraction));

            r.Apply = true;
            r.TwistWorldRotation = i.ParentRotation * partialTwist;
            r.TwistOnly = twistOnly;
            r.TwistAngleDeg = Quaternion.Angle(Quaternion.identity, twistOnly);
        }

        public static float SignedTwistAngleDeg(Quaternion q, Vector3 axis)
        {
            Quaternion t = ExtractTwist(q, axis);
            float s = t.x * axis.x + t.y * axis.y + t.z * axis.z;
            float w = t.w;
            if (w < 0f) { w = -w; s = -s; }
            return 2f * Mathf.Atan2(s, w) * Mathf.Rad2Deg;
        }

        public static Quaternion ExtractTwist(Quaternion q, Vector3 axis)
        {
            Vector3 ra = new Vector3(q.x, q.y, q.z);
            Vector3 p = Vector3.Project(ra, axis);
            Quaternion twist = new Quaternion(p.x, p.y, p.z, q.w);
            float magSq = twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w;
            if (magSq < k_SqrEpsilon)
            {
                return Quaternion.identity;
            }

            float invMag = 1f / Mathf.Sqrt(magSq);
            return new Quaternion(twist.x * invMag, twist.y * invMag, twist.z * invMag, twist.w * invMag);
        }

        public static float SegmentPositionFraction(Vector3 parentPos, Vector3 childPos, Vector3 twistPos)
        {
            Vector3 seg = childPos - parentPos;
            float segLen2 = seg.sqrMagnitude;
            if (segLen2 < k_SqrEpsilon) return 0f;
            return Mathf.Clamp01(Vector3.Dot(twistPos - parentPos, seg) / segLen2);
        }

        public static Quaternion RelaxAroundAxis(Quaternion delta, Vector3 axis, float keep) => ShapeReachStep(delta, axis, keep, 1f);

        public static Quaternion ShapeReachStep(Quaternion delta, Vector3 axis, float twistKeep, float swingScale)
        {
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                return Quaternion.Slerp(Quaternion.identity, delta, Mathf.Clamp01(swingScale));
            }
            axis = axis.normalized;
            Quaternion twist = ExtractTwist(delta, axis);
            Quaternion swing = delta * Quaternion.Inverse(twist);
            Quaternion scaledSwing = Quaternion.Slerp(Quaternion.identity, swing, Mathf.Clamp01(swingScale));
            Quaternion scaledTwist = Quaternion.Slerp(Quaternion.identity, twist, Mathf.Clamp01(twistKeep));
            return scaledSwing * scaledTwist;
        }
    }
}
