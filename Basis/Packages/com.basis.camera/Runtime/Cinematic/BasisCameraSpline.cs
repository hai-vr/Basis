using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Catmull-Rom evaluation for the dolly track. The curve passes through every waypoint, which
    /// is what makes a hand-placed track behave: the camera visits the points you put down rather
    /// than being pulled off them the way a Bezier hull would.
    /// <para>
    /// Path position is measured in segments, so 0 is the first waypoint, 1.5 is halfway along the
    /// second segment, and the usable range is 0..Count-1 open or 0..Count looped.
    /// </para>
    /// </summary>
    public static class BasisCameraSpline
    {
        /// <summary>Highest valid path position for a track of <paramref name="count"/> waypoints.</summary>
        public static float MaxPosition(int count, bool looped)
        {
            if (count <= 1)
            {
                return 0f;
            }
            return looped ? count : count - 1;
        }

        /// <summary>Wraps or clamps a path position into range, matching the track's loop mode.</summary>
        public static float NormalizePosition(float position, int count, bool looped)
        {
            float max = MaxPosition(count, looped);
            if (max <= 0f)
            {
                return 0f;
            }

            if (!looped)
            {
                return Mathf.Clamp(position, 0f, max);
            }

            position %= max;
            if (position < 0f)
            {
                position += max;
            }
            return position;
        }

        public static Vector3 Evaluate(IReadOnlyList<Vector3> points, float position, bool looped)
        {
            if (points == null || points.Count == 0)
            {
                return Vector3.zero;
            }
            if (points.Count == 1)
            {
                return points[0];
            }
            if (points.Count == 2 && !looped)
            {
                return Vector3.Lerp(points[0], points[1], Mathf.Clamp01(position));
            }

            position = NormalizePosition(position, points.Count, looped);

            int segment = Mathf.FloorToInt(position);
            float u = position - segment;

            int last = points.Count - 1;
            if (!looped && segment >= last)
            {
                segment = last - 1;
                u = 1f;
            }

            Vector3 p0 = points[Index(segment - 1, points.Count, looped)];
            Vector3 p1 = points[Index(segment, points.Count, looped)];
            Vector3 p2 = points[Index(segment + 1, points.Count, looped)];
            Vector3 p3 = points[Index(segment + 2, points.Count, looped)];

            return CatmullRom(p0, p1, p2, p3, u);
        }

        /// <summary>Unit tangent at a path position, for orienting a camera that rides the track.</summary>
        public static Vector3 EvaluateTangent(IReadOnlyList<Vector3> points, float position, bool looped)
        {
            const float Step = 0.01f;
            Vector3 ahead = Evaluate(points, position + Step, looped);
            Vector3 behind = Evaluate(points, position - Step, looped);
            Vector3 tangent = ahead - behind;
            return tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector3.forward;
        }

        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float u2 = u * u;
            float u3 = u2 * u;

            return 0.5f * ((2f * p1)
                + (-p0 + p2) * u
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        /// <summary>
        /// Path position closest to <paramref name="query"/>. Coarse sampling followed by a local
        /// refinement — a straight analytic solve of a cubic per segment is exact but far more
        /// arithmetic per frame than an auto-dolly needs.
        /// </summary>
        public static float FindClosestPosition(IReadOnlyList<Vector3> points, Vector3 query, bool looped,
            int samplesPerSegment = 8, int refineIterations = 6)
        {
            if (points == null || points.Count == 0)
            {
                return 0f;
            }
            if (points.Count == 1)
            {
                return 0f;
            }

            float max = MaxPosition(points.Count, looped);
            int totalSamples = Mathf.Max(2, Mathf.CeilToInt(max * Mathf.Max(1, samplesPerSegment)));

            float bestPosition = 0f;
            float bestDistance = float.MaxValue;
            float step = max / totalSamples;

            for (int Index = 0; Index <= totalSamples; Index++)
            {
                float position = Index * step;
                float distance = (Evaluate(points, position, looped) - query).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPosition = position;
                }
            }

            float window = step;
            for (int Iteration = 0; Iteration < refineIterations; Iteration++)
            {
                window *= 0.5f;

                float low = bestPosition - window;
                float high = bestPosition + window;

                float lowDistance = (Evaluate(points, low, looped) - query).sqrMagnitude;
                float highDistance = (Evaluate(points, high, looped) - query).sqrMagnitude;

                if (lowDistance < bestDistance && lowDistance <= highDistance)
                {
                    bestDistance = lowDistance;
                    bestPosition = low;
                }
                else if (highDistance < bestDistance)
                {
                    bestDistance = highDistance;
                    bestPosition = high;
                }
            }

            return NormalizePosition(bestPosition, points.Count, looped);
        }

        /// <summary>Approximate arc length, for pacing a constant-speed dolly move.</summary>
        public static float ApproximateLength(IReadOnlyList<Vector3> points, bool looped, int samplesPerSegment = 8)
        {
            if (points == null || points.Count < 2)
            {
                return 0f;
            }

            float max = MaxPosition(points.Count, looped);
            int totalSamples = Mathf.Max(2, Mathf.CeilToInt(max * Mathf.Max(1, samplesPerSegment)));
            float step = max / totalSamples;

            float length = 0f;
            Vector3 previous = Evaluate(points, 0f, looped);
            for (int Index = 1; Index <= totalSamples; Index++)
            {
                Vector3 current = Evaluate(points, Index * step, looped);
                length += Vector3.Distance(previous, current);
                previous = current;
            }
            return length;
        }

        private static int Index(int index, int count, bool looped)
        {
            if (looped)
            {
                index %= count;
                if (index < 0)
                {
                    index += count;
                }
                return index;
            }
            return Mathf.Clamp(index, 0, count - 1);
        }
    }
}
