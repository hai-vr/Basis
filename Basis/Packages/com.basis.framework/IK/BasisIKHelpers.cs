using System.Runtime.CompilerServices;
namespace UnityEngine.Animations.Rigging
{
    public struct BasisIKHelpers
    {
        public const float k_DivisionSafetyEpsilon = 1e-10f;
        public const float k_MinSqrMagnitude = 1e-8f;
        public const float k_LengthEpsilon = 1e-5f;
        public const float k_MinMagnitude = 1e-6f;
        public const float k_MaxSpineHorizontalFactor = 0.35f;
        public const float K_Soften = 0.001f;
        public static float TriangleAngle(float aLen, float aLen1, float aLen2)
        {
            if (aLen1 <= k_MinSqrMagnitude || aLen2 <= k_MinSqrMagnitude)
            {
                return 0f;
            }

            float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (aLen1 * aLen2) / 2.0f, -1.0f, 1.0f);
            return Mathf.Acos(c);
        }
        public static Quaternion ClampRotation(Quaternion current, Quaternion reference, float maxAngleDeg)
        {
            // Angle between the two orientations
            float angle = Quaternion.Angle(reference, current);
            if (angle <= maxAngleDeg)
            {
                return current;
            }

            // Scale back toward the reference so the final difference is exactly maxAngleDeg
            float t = maxAngleDeg / Mathf.Max(angle, BasisIKHelpers.k_LengthEpsilon);
            return Quaternion.Slerp(reference, current, t);
        }
        public static Vector3 ComputeIkAxis(Vector3 bendNormal)
        {
            Vector3 axis;
            axis = bendNormal;
            float mag2 = axis.sqrMagnitude;
            if (mag2 < k_MinSqrMagnitude)
            {
                // Deterministic fallback to avoid NaNs/garbage under Burst
                return Vector3.forward;
            }

            return axis / Mathf.Sqrt(mag2);
        }
        public static void Pass(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip)
        {
            if (root.IsValid(stream)) PassThrough(stream, root);
            if (mid.IsValid(stream)) PassThrough(stream, mid);
            if (tip.IsValid(stream)) PassThrough(stream, tip);
        }
        public static void PassThrough(AnimationStream stream, ReadWriteTransformHandle handle)
        {
            handle.GetLocalTRS(stream, out Vector3 position, out Quaternion rotation, out Vector3 scale);
            handle.SetLocalTRS(stream, position, rotation, scale);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Apply(AnimationStream stream, ReadWriteTransformHandle h, Vector3Property p, Vector4Property r, Vector4Property o, BoolProperty sw)
        {
            if (h.IsValid(stream))
            {
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
        }
        public static Quaternion ConvertToQuaternion(Vector4 v) => new Quaternion(v.x, v.y, v.z, v.w);
    }
}
