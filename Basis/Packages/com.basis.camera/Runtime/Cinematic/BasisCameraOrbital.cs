using System;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// A FreeLook orbit: three rings around the subject — low, waist, high — that the vertical axis
    /// sweeps between. Each ring is a height above the subject and a radius out from it, so the
    /// camera can swing overhead without passing through the subject on the way.
    /// </summary>
    [Serializable]
    public struct BasisCameraOrbitRig
    {
        public float height;
        public float radius;

        public BasisCameraOrbitRig(float height, float radius)
        {
            this.height = height;
            this.radius = radius;
        }

        public Vector2 AsVector => new Vector2(height, radius);
    }

    [Serializable]
    public struct BasisCameraOrbitSettings
    {
        public BasisCameraOrbitRig top;
        public BasisCameraOrbitRig middle;
        public BasisCameraOrbitRig bottom;

        [Tooltip("Ring position, 0 at the bottom rig through 1 at the top.")]
        [Range(0f, 1f)] public float verticalAxis;

        [Tooltip("Angle around the subject in degrees. 0 places the camera in front, facing back at them.")]
        public float heading;

        [Tooltip("Seconds for the orbit to catch up in heading and ring position.")]
        public float headingDamping;
        public float verticalDamping;

        [Tooltip("Turns the orbit to face the subject's own heading, so it stays in front as they turn.")]
        public bool followSubjectHeading;

        public static BasisCameraOrbitSettings Default => new BasisCameraOrbitSettings
        {
            top = new BasisCameraOrbitRig(1.6f, 1.2f),
            middle = new BasisCameraOrbitRig(0.1f, 1.8f),
            bottom = new BasisCameraOrbitRig(-0.6f, 1.4f),
            verticalAxis = 0.5f,
            heading = 0f,
            headingDamping = 0.35f,
            verticalDamping = 0.35f,
            followSubjectHeading = true,
        };
    }

    public static class BasisCameraOrbital
    {
        /// <summary>
        /// Height and radius at a point on the sweep. A quadratic Bezier whose control point is
        /// solved so the curve passes exactly through the middle rig at 0.5 — three authored rings
        /// that are all actually hit, and a smooth surface between them.
        /// </summary>
        public static Vector2 EvaluateRig(float verticalAxis, Vector2 bottom, Vector2 middle, Vector2 top)
        {
            float t = Mathf.Clamp01(verticalAxis);
            Vector2 control = 2f * middle - 0.5f * bottom - 0.5f * top;

            float inverse = 1f - t;
            return inverse * inverse * bottom + 2f * inverse * t * control + t * t * top;
        }

        public static Vector2 EvaluateRig(float verticalAxis, in BasisCameraOrbitSettings settings)
            => EvaluateRig(verticalAxis, settings.bottom.AsVector, settings.middle.AsVector, settings.top.AsVector);

        /// <summary>
        /// World position on the orbit. Heading 0 sits in front of <paramref name="subjectYaw"/>,
        /// matching the camera's existing forward-facing follow offset.
        /// </summary>
        public static Vector3 SolvePosition(Vector3 centre, Quaternion subjectYaw, float heading, float height, float radius, float scale)
        {
            Quaternion rotation = subjectYaw * BasisCameraDamping.Yaw(heading);
            return centre + rotation * new Vector3(0f, height * scale, radius * scale);
        }

        public static Vector3 SolvePosition(Vector3 centre, Quaternion subjectYaw, in BasisCameraOrbitSettings settings, float scale)
        {
            Vector2 rig = EvaluateRig(settings.verticalAxis, settings);
            Quaternion frame = settings.followSubjectHeading ? subjectYaw : Quaternion.identity;
            return SolvePosition(centre, frame, settings.heading, rig.x, rig.y, scale);
        }

        /// <summary>
        /// Heading that puts the camera where it already is, so switching a shot into orbital mode
        /// does not whip the camera around to heading 0.
        /// </summary>
        public static float HeadingFromPosition(Vector3 centre, Quaternion subjectYaw, Vector3 cameraPosition)
        {
            Vector3 local = BasisCameraDamping.Conjugate(subjectYaw) * (cameraPosition - centre);
            local.y = 0f;
            if (local.sqrMagnitude < 1e-6f)
            {
                return 0f;
            }
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }

        /// <summary>Shortest-path damping for the heading, so 350 to 10 degrees crosses zero.</summary>
        public static float DampHeading(float current, float target, float dampTime, float deltaTime)
        {
            float delta = BasisCameraDamping.NormalizeAngle(target - current);
            return current + BasisCameraDamping.Damp(delta, dampTime, deltaTime);
        }
    }
}
