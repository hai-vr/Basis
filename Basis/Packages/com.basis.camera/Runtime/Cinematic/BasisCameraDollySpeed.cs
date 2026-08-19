using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// The speed envelope a dolly move runs under, and the colour that shows it on the track.
    ///
    /// <para>The move's speed setting is the cruise: the ease shapes how the camera gets up to it
    /// off the first waypoint and how it comes off it at the last. Both are read from the same
    /// place here, so the colour drawn along the path is the speed the solver will actually use
    /// rather than a second guess at it.</para>
    /// </summary>
    public static class BasisCameraDollySpeed
    {
        /// <summary>
        /// The slowest the envelope will run the move, as a fraction of cruise. A curve that
        /// reaches zero would leave the camera parked on the first waypoint with nothing to carry
        /// it off, and Back's undershoot would reverse it into the end of the track and finish the
        /// move before it began.
        /// </summary>
        public const float MinimumWeight = 0.05f;

        /// <summary>Ceiling for the two curves that overshoot, so a surge stays a surge.</summary>
        public const float MaximumWeight = 2f;

        /// <summary>Longest run-up or run-down, as a fraction of the track.</summary>
        public const float MaximumEasePortion = 0.5f;

        /// <summary>Top of the colour ramp in metres per second, at default avatar scale.</summary>
        public const float ReferenceSpeed = 6f;

        private static readonly Color[] RampStops =
        {
            new Color(0.20f, 0.35f, 0.95f),
            new Color(0.20f, 0.85f, 0.95f),
            new Color(0.30f, 0.95f, 0.40f),
            new Color(1.00f, 0.80f, 0.20f),
            new Color(1.00f, 0.25f, 0.20f),
        };

        /// <summary>
        /// How much of cruise speed the move is travelling at, <paramref name="normalized"/> being
        /// the position along the track in 0..1.
        ///
        /// <para>A negative speed runs the track backwards, so the run-up is measured from the end
        /// it actually starts at. A looped track has no first or last waypoint to ease off, and an
        /// envelope applied to one would put a slow spot at the seam on every lap, so it runs flat.</para>
        /// </summary>
        public static float Weight(in BasisCameraDollySettings dolly, float normalized, bool looped)
        {
            if (looped)
            {
                return 1f;
            }

            float t = Mathf.Clamp01(normalized);
            if (dolly.speed < 0f)
            {
                t = 1f - t;
            }

            float weight = 1f;

            float runUp = Mathf.Clamp(dolly.easeInPortion, 0f, MaximumEasePortion);
            if (runUp > 1e-4f && t < runUp)
            {
                weight = BasisCameraEasing.In(dolly.easeIn, t / runUp);
            }

            float runDown = Mathf.Clamp(dolly.easeOutPortion, 0f, MaximumEasePortion);
            if (runDown > 1e-4f && t > 1f - runDown)
            {
                weight = Mathf.Min(weight, BasisCameraEasing.Out(dolly.easeOut, (1f - t) / runDown));
            }

            return Mathf.Clamp(weight, MinimumWeight, MaximumWeight);
        }

        /// <summary>
        /// Metres per second the camera passes through a point at. <paramref name="stretch"/> is how
        /// far apart the path is there against its own average — the playhead advances at one rate
        /// in waypoints, so a long span between two points is covered faster than a short one.
        /// </summary>
        public static float MetresPerSecond(in BasisCameraDollySettings dolly, float normalized, bool looped, float stretch)
            => Mathf.Abs(dolly.speed) * Weight(dolly, normalized, looped) * Mathf.Max(0f, stretch);

        /// <summary>
        /// The colour for a speed, cool through warm. Square-rooted rather than read straight off
        /// the scale: a dolly move lives in the bottom of the range, and a linear read would push
        /// every ordinary move into one colour with the ease invisible inside it.
        /// </summary>
        public static Color Sample(float metresPerSecond, float scale)
        {
            float reference = ReferenceSpeed * Mathf.Max(0.05f, scale);
            float t = Mathf.Sqrt(Mathf.Clamp01(metresPerSecond / reference));
            return Ramp(t);
        }

        /// <summary>Straight lookup into the ramp, 0 slow through 1 fast.</summary>
        public static Color Ramp(float t)
        {
            t = Mathf.Clamp01(t) * (RampStops.Length - 1);
            int stop = Mathf.Min(Mathf.FloorToInt(t), RampStops.Length - 2);
            return Color.Lerp(RampStops[stop], RampStops[stop + 1], t - stop);
        }
    }
}
