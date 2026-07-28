using System;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Screen composition for an aim solve: where in frame the subject should sit, how far it may
    /// drift before the camera reacts, and how far it is allowed to get before the camera stops
    /// easing and simply keeps up.
    /// </summary>
    [Serializable]
    public struct BasisComposerSettings
    {
        [Range(0f, 1f)] public float screenX;
        [Range(0f, 1f)] public float screenY;

        [Tooltip("Region around the screen point the subject may move inside without the camera reacting at all.")]
        [Range(0f, 1f)] public float deadZoneWidth;
        [Range(0f, 1f)] public float deadZoneHeight;

        [Tooltip("Region the subject is never allowed outside. Beyond the dead zone the camera eases; at this edge it keeps up exactly.")]
        [Range(0f, 2f)] public float softZoneWidth;
        [Range(0f, 2f)] public float softZoneHeight;

        [Range(-0.5f, 0.5f)] public float biasX;
        [Range(-0.5f, 0.5f)] public float biasY;

        public float horizontalDamping;
        public float verticalDamping;

        public static BasisComposerSettings Default => new BasisComposerSettings
        {
            screenX = 0.5f,
            screenY = 0.55f,
            deadZoneWidth = 0.12f,
            deadZoneHeight = 0.14f,
            softZoneWidth = 0.7f,
            softZoneHeight = 0.7f,
            biasX = 0f,
            biasY = 0f,
            horizontalDamping = 0.45f,
            verticalDamping = 0.6f,
        };

        /// <summary>Centres the subject with no tolerance — the hard look-at the camera shipped with.</summary>
        public static BasisComposerSettings HardLookAt => new BasisComposerSettings
        {
            screenX = 0.5f,
            screenY = 0.5f,
            deadZoneWidth = 0f,
            deadZoneHeight = 0f,
            softZoneWidth = 0f,
            softZoneHeight = 0f,
            biasX = 0f,
            biasY = 0f,
            horizontalDamping = 0f,
            verticalDamping = 0f,
        };
    }

    /// <summary>
    /// Aim solver. Works in normalised screen space — (0,0) bottom-left, (1,1) top-right — so the
    /// dead and soft zones mean the same thing at any field of view or aspect.
    /// </summary>
    public static class BasisCameraComposer
    {
        /// <summary>
        /// Where <paramref name="target"/> currently lands in frame. False when it is behind the
        /// camera or the lens is degenerate, which callers treat as "no composition possible".
        /// </summary>
        public static bool TryGetScreenPoint(Vector3 cameraPos, Quaternion cameraRot, Vector3 target,
            float verticalFovDegrees, float aspect, out Vector2 screenPoint)
        {
            screenPoint = new Vector2(0.5f, 0.5f);

            if (aspect <= 0f || verticalFovDegrees <= 0f || verticalFovDegrees >= 180f)
            {
                return false;
            }

            Vector3 local = Quaternion.Inverse(cameraRot) * (target - cameraPos);
            if (local.z <= 1e-4f)
            {
                return false;
            }

            float tanHalf = Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            float ndcX = local.x / (local.z * tanHalf * aspect);
            float ndcY = local.y / (local.z * tanHalf);

            screenPoint = new Vector2(0.5f + ndcX * 0.5f, 0.5f + ndcY * 0.5f);
            return true;
        }

        /// <summary>
        /// The camera rotation that lands <paramref name="target"/> exactly on
        /// <paramref name="screenPoint"/>, with roll taken from <paramref name="up"/>.
        /// </summary>
        public static Quaternion RotationForScreenPoint(Vector3 cameraPos, Vector3 target,
            Vector2 screenPoint, float verticalFovDegrees, float aspect, Vector3 up)
        {
            Vector3 toTarget = target - cameraPos;
            if (toTarget.sqrMagnitude < 1e-8f)
            {
                return Quaternion.LookRotation(Vector3.forward, up);
            }

            Quaternion look = Quaternion.LookRotation(toTarget.normalized, up);

            if (aspect <= 0f || verticalFovDegrees <= 0f || verticalFovDegrees >= 180f)
            {
                return look;
            }

            float tanHalf = Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            float ndcX = (screenPoint.x - 0.5f) * 2f;
            float ndcY = (screenPoint.y - 0.5f) * 2f;

            Vector3 rayLocal = new Vector3(ndcX * tanHalf * aspect, ndcY * tanHalf, 1f).normalized;

            float rayYaw = Mathf.Atan2(rayLocal.x, rayLocal.z) * Mathf.Rad2Deg;
            float rayPitch = -Mathf.Asin(Mathf.Clamp(rayLocal.y, -1f, 1f)) * Mathf.Rad2Deg;

            return look * Quaternion.Inverse(Quaternion.Euler(rayPitch, rayYaw, 0f));
        }

        /// <summary>
        /// Full composer solve: the rotation the camera should hold this frame given where the
        /// subject is now. Inside the dead zone nothing moves, between dead and soft the camera
        /// eases back, and the subject can never leave the soft zone.
        /// </summary>
        public static Quaternion Solve(Vector3 cameraPos, Quaternion currentRot, Vector3 target,
            float verticalFovDegrees, float aspect, in BasisComposerSettings settings, Vector3 up, float deltaTime)
        {
            if (!TryGetScreenPoint(cameraPos, currentRot, target, verticalFovDegrees, aspect, out Vector2 current))
            {
                Vector3 toTarget = target - cameraPos;
                return toTarget.sqrMagnitude > 1e-8f
                    ? Quaternion.LookRotation(toTarget.normalized, up)
                    : currentRot;
            }

            float deadHalfX = Mathf.Max(0f, settings.deadZoneWidth) * 0.5f;
            float deadHalfY = Mathf.Max(0f, settings.deadZoneHeight) * 0.5f;

            float softHalfX = Mathf.Max(settings.softZoneWidth * 0.5f, deadHalfX);
            float softHalfY = Mathf.Max(settings.softZoneHeight * 0.5f, deadHalfY);

            Vector2 desired = new Vector2(
                SolveAxis(current.x, settings.screenX, deadHalfX, settings.screenX + settings.biasX, softHalfX, settings.horizontalDamping, deltaTime),
                SolveAxis(current.y, settings.screenY, deadHalfY, settings.screenY + settings.biasY, softHalfY, settings.verticalDamping, deltaTime));

            return RotationForScreenPoint(cameraPos, target, desired, verticalFovDegrees, aspect, up);
        }

        /// <summary>
        /// One screen axis of the composer solve: ease the subject back to the nearest dead-zone
        /// edge, then hard-clamp it inside the soft zone.
        /// </summary>
        public static float SolveAxis(float current, float deadCentre, float deadHalf,
            float softCentre, float softHalf, float dampTime, float deltaTime)
        {
            deadHalf = Mathf.Max(0f, deadHalf);
            GetEffectiveLimits(deadCentre, deadHalf, softCentre, softHalf, out float softLow, out float softHigh);

            float deadEdge = Mathf.Clamp(current, deadCentre - deadHalf, deadCentre + deadHalf);
            float eased = current - BasisCameraDamping.Damp(current - deadEdge, dampTime, deltaTime);
            return Mathf.Clamp(eased, softLow, softHigh);
        }

        /// <summary>
        /// The limit the solve actually clamps to on one axis: the authored soft zone widened to
        /// contain the dead zone. Widened rather than merely matched in width, because a biased soft
        /// zone is offset from the dead zone, so equal widths still leave one edge cutting into the
        /// region the camera is deliberately not reacting in — dragging a subject that a dead zone
        /// had just decided to leave alone.
        /// <para>
        /// Shared with the on-screen framing guide so the drawn limit is the enforced one; deriving
        /// it twice is how a guide quietly starts lying about what the camera will do.
        /// </para>
        /// </summary>
        public static void GetEffectiveLimits(float deadCentre, float deadHalf,
            float softCentre, float softHalf, out float low, out float high)
        {
            deadHalf = Mathf.Max(0f, deadHalf);
            softHalf = Mathf.Max(0f, softHalf);

            low = Mathf.Min(softCentre - softHalf, deadCentre - deadHalf);
            high = Mathf.Max(softCentre + softHalf, deadCentre + deadHalf);
        }

        /// <summary>
        /// Predicted position of a subject <paramref name="lookAheadTime"/> seconds from now, so a
        /// shot can lead a moving subject instead of trailing it. The velocity is expected to be
        /// pre-smoothed; a raw frame delta makes this jitter.
        /// </summary>
        public static Vector3 ApplyLookAhead(Vector3 position, Vector3 smoothedVelocity, float lookAheadTime, float maxDistance)
        {
            if (lookAheadTime <= 0f)
            {
                return position;
            }

            Vector3 lead = smoothedVelocity * lookAheadTime;
            if (maxDistance > 0f && lead.sqrMagnitude > maxDistance * maxDistance)
            {
                lead = lead.normalized * maxDistance;
            }
            return position + lead;
        }
    }
}
