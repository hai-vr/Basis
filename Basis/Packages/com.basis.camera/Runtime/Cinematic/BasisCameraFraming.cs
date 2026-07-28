using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Distance and framing maths: how far back to sit so a subject fills a chosen share of the
    /// frame, how to bound a group of subjects, and how far to pull in when geometry blocks the shot.
    /// </summary>
    public static class BasisCameraFraming
    {
        /// <summary>
        /// Distance at which a sphere of <paramref name="radius"/> spans
        /// <paramref name="screenFraction"/> of the frame. Height is normally the binding axis, but
        /// both are checked so a tall narrow window still fits the subject.
        /// </summary>
        public static float DistanceToFit(float radius, float verticalFovDegrees, float aspect, float screenFraction)
        {
            if (radius <= 0f || screenFraction <= 1e-4f || aspect <= 0f ||
                verticalFovDegrees <= 0f || verticalFovDegrees >= 180f)
            {
                return 0f;
            }

            float tanHalf = Mathf.Tan(verticalFovDegrees * 0.5f * Mathf.Deg2Rad);
            float vertical = radius / (screenFraction * tanHalf);
            float horizontal = radius / (screenFraction * tanHalf * aspect);
            return Mathf.Max(vertical, horizontal);
        }

        /// <summary>
        /// Field of view at which a sphere of <paramref name="radius"/> spans
        /// <paramref name="screenFraction"/> of the frame from <paramref name="distance"/> away.
        /// The inverse of <see cref="DistanceToFit"/>, for shots that zoom rather than dolly.
        /// </summary>
        public static float FovToFit(float radius, float distance, float screenFraction)
        {
            if (radius <= 0f || distance <= 1e-4f || screenFraction <= 1e-4f)
            {
                return 0f;
            }

            float tanHalf = radius / (screenFraction * distance);
            return Mathf.Clamp(2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg, 1f, 179f);
        }

        /// <summary>
        /// Bounding sphere of a set of weighted subjects. Weights let a shot favour one player in a
        /// group without dropping the others out of frame; a zero-weight member is ignored entirely.
        /// </summary>
        public static bool TryGetGroupBounds(IReadOnlyList<Vector3> positions, IReadOnlyList<float> radii,
            IReadOnlyList<float> weights, out Vector3 centre, out float radius)
        {
            centre = Vector3.zero;
            radius = 0f;

            if (positions == null || positions.Count == 0)
            {
                return false;
            }

            float totalWeight = 0f;
            for (int Index = 0; Index < positions.Count; Index++)
            {
                float weight = weights != null && Index < weights.Count ? Mathf.Max(0f, weights[Index]) : 1f;
                if (weight <= 0f)
                {
                    continue;
                }
                centre += positions[Index] * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f)
            {
                return false;
            }

            centre /= totalWeight;

            for (int Index = 0; Index < positions.Count; Index++)
            {
                float weight = weights != null && Index < weights.Count ? Mathf.Max(0f, weights[Index]) : 1f;
                if (weight <= 0f)
                {
                    continue;
                }
                float memberRadius = radii != null && Index < radii.Count ? Mathf.Max(0f, radii[Index]) : 0f;
                radius = Mathf.Max(radius, Vector3.Distance(centre, positions[Index]) + memberRadius);
            }

            return true;
        }

        /// <summary>
        /// Pulls the camera along the ray back to the subject so it sits just in front of whatever
        /// blocked the shot. <paramref name="hitDistanceFromTarget"/> is measured from the subject
        /// outward, which is the direction the occlusion cast runs.
        /// </summary>
        public static Vector3 PullIn(Vector3 target, Vector3 desiredCameraPos,
            float hitDistanceFromTarget, float padding, float minDistance)
        {
            Vector3 offset = desiredCameraPos - target;
            float desiredDistance = offset.magnitude;
            if (desiredDistance <= 1e-4f)
            {
                return desiredCameraPos;
            }

            float allowed = Mathf.Clamp(hitDistanceFromTarget - padding, minDistance, desiredDistance);
            return target + offset / desiredDistance * allowed;
        }

        /// <summary>
        /// Keeps a position inside an axis-aligned box. Returns the original when the bounds are
        /// empty, so an unconfigured confiner never moves the shot.
        /// </summary>
        public static Vector3 Confine(Vector3 position, Bounds bounds)
        {
            if (bounds.size.sqrMagnitude <= 0f)
            {
                return position;
            }
            return bounds.ClosestPoint(position);
        }
    }
}
