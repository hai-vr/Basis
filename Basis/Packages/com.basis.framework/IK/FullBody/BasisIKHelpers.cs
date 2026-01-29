using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace UnityEngine.Animations.Rigging
{
    public struct BasisIKHelpers
    {
        public const int iterations = 6;
        public const float k_DivisionSafetyEpsilon = 1e-10f;
        public const float k_MinSqrMagnitude = 1e-8f;
        public const float k_LengthEpsilon = 1e-5f;
        public const float k_MinMagnitude = 1e-6f;
        public const float k_MaxSpineHorizontalFactor = 0.35f;
        public const float K_Soften = 0.001f;

        public static float TriangleAngle(float aLen, float aLen1, float aLen2)
        {
            if (aLen1 <= k_MinSqrMagnitude || aLen2 <= k_MinSqrMagnitude)
                return 0f;

            float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (aLen1 * aLen2) / 2.0f, -1.0f, 1.0f);
            return Mathf.Acos(c);
        }

        public static Quaternion ClampRotation(Quaternion current, Quaternion reference, float maxAngleDeg)
        {
            float angle = Quaternion.Angle(reference, current);
            if (angle <= maxAngleDeg) return current;

            float t = maxAngleDeg / Mathf.Max(angle, k_LengthEpsilon);
            return Quaternion.Slerp(reference, current, t);
        }

        public static Vector3 ComputeIkAxis(Vector3 bendNormal)
        {
            Vector3 axis = bendNormal;
            float mag2 = axis.sqrMagnitude;
            if (mag2 < k_MinSqrMagnitude) return Vector3.forward;
            return axis / Mathf.Sqrt(mag2);
        }

        public static void Pass(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip)
        {
            if (root.IsValid(stream)) PassThrough(stream, root);
            if (mid.IsValid(stream)) PassThrough(stream, mid);
            if (tip.IsValid(stream)) PassThrough(stream, tip);
        }

        public static void ApplyRotation(AnimationStream stream, BoolProperty enabledProp, ReadWriteTransformHandle handle, Vector4Property targetRotProp, Quaternion RotationOffset)
        {
            if (!handle.IsValid(stream)) return;

            if (enabledProp.Get(stream))
                handle.SetRotation(stream, ConvertToQuaternion(targetRotProp.Get(stream)) * RotationOffset);
            else
                PassThrough(stream, handle);
        }

        public static void PassThrough(AnimationStream stream, ReadWriteTransformHandle handle)
        {
            handle.GetLocalTRS(stream, out Vector3 position, out Quaternion rotation, out Vector3 scale);
            handle.SetLocalTRS(stream, position, rotation, scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Apply(AnimationStream stream, ReadWriteTransformHandle h, Vector3Property p, Vector4Property r, Vector4Property o, BoolProperty sw)
        {
            if (!h.IsValid(stream)) return;

            if (sw.Get(stream))
            {
                Vector3 targetPos = p.Get(stream);
                Quaternion targetRot = ConvertToQuaternion(r.Get(stream));
                Quaternion offsetRot = ConvertToQuaternion(o.Get(stream));
                Quaternion finalRot = targetRot * offsetRot;

                h.SetPosition(stream, targetPos);
                h.SetRotation(stream, finalRot);
            }
            else
            {
                PassThrough(stream, h);
            }
        }

        public static Quaternion ConvertToQuaternion(Vector4 v) => new Quaternion(v.x, v.y, v.z, v.w);

        public static ReadWriteTransformHandle GetSpineHandle(BasisIKSpine c, int idxHipsToHead)
        {
            return idxHipsToHead switch
            {
                0 => c.J0,
                1 => c.J1,
                2 => c.J2,
                3 => c.J3,
                4 => c.J4,
                _ => c.J5
            };
        }

        public static float3 SafeDir(float3 v, float eps, float3 fallback)
        {
            float lsq = math.lengthsq(v);
            if (lsq > eps * eps) return v * math.rsqrt(lsq);
            return fallback;
        }

        // ----------------------------
        // Shoulder distribution helpers
        // ----------------------------
        public static float ComputeReach01(Vector3 shoulderPos, Vector3 wristTarget, float maxReach)
        {
            float d = (wristTarget - shoulderPos).magnitude;
            return Mathf.Clamp01((d - 0.7f * maxReach) / (0.3f * maxReach));
        }

        public static float ComputeCrossBody01(Quaternion chestRot, Vector3 shoulderPos, Vector3 wristTarget, bool isLeft)
        {
            Vector3 right = chestRot * Vector3.right;
            float side = Vector3.Dot(wristTarget - shoulderPos, right); // + right, - left
            float s = isLeft ? Mathf.Clamp01((side) / 0.25f) : Mathf.Clamp01((-side) / 0.25f);
            return s;
        }

        // ----------------------------
        // Twist / normalization helpers
        // ----------------------------
        public static Quaternion NormalizeSafe(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag > 1e-8f)
                return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);

            return Quaternion.identity;
        }

        public static void SwingTwist(Quaternion q, Vector3 axis, out Quaternion swing, out Quaternion twist)
        {
            axis = axis.normalized;
            Vector3 r = new Vector3(q.x, q.y, q.z);
            Vector3 p = Vector3.Project(r, axis);

            twist = NormalizeSafe(new Quaternion(p.x, p.y, p.z, q.w));
            swing = NormalizeSafe(q * Quaternion.Inverse(twist));
        }

        public static Quaternion ClampTwistDegrees(Quaternion twist, float maxAbsDeg)
        {
            twist = NormalizeSafe(twist);
            twist.ToAngleAxis(out float ang, out Vector3 ax);

            if (ax.sqrMagnitude < 1e-8f || Mathf.Abs(ang) < 1e-6f)
                return Quaternion.identity;

            if (ang > 180f) ang -= 360f;
            float clamped = Mathf.Clamp(ang, -maxAbsDeg, maxAbsDeg);

            ax.Normalize();
            return Quaternion.AngleAxis(clamped, ax);
        }

        // ----------------------------
        // Cone clamp helper (you already had this)
        // ----------------------------
        public static Vector3 ClampDirectionInCone(Vector3 dir, Vector3 coneAxis, float coneHalfAngleDeg)
        {
            float dirLen = dir.magnitude;
            if (dirLen < k_MinMagnitude) return coneAxis.normalized;

            Vector3 d = dir / dirLen;
            Vector3 a = coneAxis.normalized;

            float dot = Mathf.Clamp(Vector3.Dot(a, d), -1f, 1f);
            float ang = Mathf.Acos(dot) * Mathf.Rad2Deg;
            if (ang <= coneHalfAngleDeg) return d;

            Vector3 rotAxis = Vector3.Cross(a, d);
            if (rotAxis.sqrMagnitude < 1e-8f)
            {
                rotAxis = Vector3.Cross(a, Vector3.up);
                if (rotAxis.sqrMagnitude < 1e-8f)
                    rotAxis = Vector3.Cross(a, Vector3.right);
            }
            rotAxis.Normalize();

            Quaternion q = Quaternion.AngleAxis(coneHalfAngleDeg, rotAxis);
            return (q * a).normalized;
        }

        // ----------------------------
        // 7) Soft reach clamp (no snapping)
        // ----------------------------
        public static Vector3 SoftClampToReach(Vector3 rootPos, Vector3 target, float maxReach, float softZone)
        {
            Vector3 v = target - rootPos;
            float d = v.magnitude;
            if (d <= maxReach) return target;
            if (d < 1e-6f) return rootPos;

            Vector3 reachable = rootPos + (v / d) * maxReach;

            float over = d - maxReach;
            float t = Mathf.Clamp01(over / Mathf.Max(softZone, 1e-5f));

            float ease = t * t * (3f - 2f * t); // smoothstep
            return Vector3.Lerp(target, reachable, ease);
        }

        // ----------------------------
        // 8) Point vs capsule resolve (for elbow)
        // ----------------------------
        public static Vector3 PointCapsuleResolve(Vector3 p, Vector3 a, Vector3 b, float r)
        {
            Vector3 q = BasisFullIKConstraintJob.ClosestPointOnSegment(p, a, b);
            Vector3 d = p - q;
            float dSqr = Vector3.Dot(d, d);
            if (dSqr >= r * r) return Vector3.zero;

            if (dSqr < 1e-10f)
                return Vector3.up * r;

            float dist = Mathf.Sqrt(dSqr);
            return (d / dist) * (r - dist);
        }

        // ----------------------------
        // 6) Wrist: swing+twist, clamp twist around forearm axis
        // ----------------------------
        public static Quaternion ClampWristTwistAroundForearm(Vector3 forearmAxisWorld, Quaternion currentWristRot, Quaternion desiredWristRot, float twistLimitDeg)
        {
            Quaternion delta = NormalizeSafe(desiredWristRot * Quaternion.Inverse(currentWristRot));
            SwingTwist(delta, forearmAxisWorld, out var swing, out var twist);
            Quaternion twistClamped = ClampTwistDegrees(twist, twistLimitDeg);
            return NormalizeSafe((swing * twistClamped) * currentWristRot);
        }
    }
}
