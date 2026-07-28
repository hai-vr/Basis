using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Frame-rate independent damping shared by every cinematic solver. A damp time of <c>t</c>
    /// seconds removes all but 1% of the residual after <c>t</c> seconds, so the same number reads
    /// the same at any framerate, and 0 is an instant snap.
    /// </summary>
    public static class BasisCameraDamping
    {
        private const float LogNegligibleResidual = -4.605170186f;

        /// <summary>Fraction of a residual to remove this frame, in 0..1.</summary>
        public static float Fraction(float dampTime, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return 0f;
            }
            if (dampTime < 1e-5f)
            {
                return 1f;
            }
            return 1f - Mathf.Exp(LogNegligibleResidual * deltaTime / dampTime);
        }

        public static float Damp(float residual, float dampTime, float deltaTime)
            => residual * Fraction(dampTime, deltaTime);

        public static Vector3 Damp(Vector3 residual, float dampTime, float deltaTime)
            => residual * Fraction(dampTime, deltaTime);

        public static Vector3 Damp(Vector3 residual, Vector3 dampTime, float deltaTime)
            => new Vector3(
                residual.x * Fraction(dampTime.x, deltaTime),
                residual.y * Fraction(dampTime.y, deltaTime),
                residual.z * Fraction(dampTime.z, deltaTime));

        public static float Approach(float current, float target, float dampTime, float deltaTime)
            => current + Damp(target - current, dampTime, deltaTime);

        public static Vector3 Approach(Vector3 current, Vector3 target, float dampTime, float deltaTime)
            => current + Damp(target - current, dampTime, deltaTime);

        /// <summary>
        /// Damps toward a target with a separate damp time per axis of <paramref name="frame"/>,
        /// the binding space the offset was authored in. Lets a shot keep up sideways while
        /// lagging on approach, which one global damp time cannot express.
        /// </summary>
        public static Vector3 ApproachInFrame(Vector3 current, Vector3 target, Quaternion frame, Vector3 dampTime, float deltaTime)
        {
            Vector3 residual = Conjugate(frame) * (target - current);
            return current + frame * Damp(residual, dampTime, deltaTime);
        }

        /// <summary>
        /// Inverse of a rotation. Every quaternion in the pipeline is a unit rotation, for which the
        /// conjugate is the inverse, so this avoids the native call <see cref="Quaternion.Inverse"/>
        /// makes on a per-frame path.
        /// </summary>
        public static Quaternion Conjugate(Quaternion rotation)
            => new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w);

        /// <summary>Yaw-only rotation, built directly rather than through the native euler conversion.</summary>
        public static Quaternion Yaw(float degrees)
        {
            float half = degrees * 0.5f * Mathf.Deg2Rad;
            return new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
        }

        public static Quaternion ApproachRotation(Quaternion current, Quaternion target, float dampTime, float deltaTime)
            => Quaternion.Slerp(current, target, Fraction(dampTime, deltaTime));

        /// <summary>
        /// Damps yaw, pitch and roll separately in the frame of <paramref name="current"/>. Slerp
        /// cannot do this: it moves every axis at one rate, so a shot cannot hold a steady horizon
        /// while swinging freely in yaw.
        /// </summary>
        public static Quaternion ApproachRotation(Quaternion current, Quaternion target, Vector3 dampTime, float deltaTime)
        {
            Quaternion residual = Quaternion.Inverse(current) * target;
            Vector3 euler = NormalizeEuler(residual.eulerAngles);
            Vector3 applied = new Vector3(
                euler.x * Fraction(dampTime.x, deltaTime),
                euler.y * Fraction(dampTime.y, deltaTime),
                euler.z * Fraction(dampTime.z, deltaTime));
            return current * Quaternion.Euler(applied);
        }

        /// <summary>Maps each component of a Unity euler triple from 0..360 into -180..180.</summary>
        public static Vector3 NormalizeEuler(Vector3 euler)
            => new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));

        public static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle < -180f)
            {
                angle += 360f;
            }
            return angle;
        }
    }
}
