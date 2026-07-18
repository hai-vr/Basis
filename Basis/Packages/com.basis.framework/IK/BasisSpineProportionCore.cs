using UnityEngine;
namespace Basis.IK
{
    /// <summary>
    /// A small, per-avatar spine-length correction for the head+hips-tracker case. With both spine ends
    /// pinned (head to the HMD, hips to the tracker), an avatar whose torso is longer or shorter than the
    /// wearer's forces the rotation-only spine solve to buckle (crumple) or over-stretch. This computes a
    /// single uniform scale that brings the avatar's spine length toward the wearer's real torso, captured
    /// once at calibration so genuine bends stay faithful. Exactly 1 (a no-op) when the proportions already
    /// match or the correction is off. The scale is applied by re-spacing the head-to-hips chain in the job.
    /// </summary>
    public static class BasisSpineProportionCore
    {
        // Below this length (metres) a captured torso is a bad frame (avatar not built, tracker glitch),
        // and a ratio outside this band is a space/scale mismatch rather than a real proportion difference.
        const float k_MinTorsoMeters = 0.05f;
        const float k_MinRatio = 0.5f;
        const float k_MaxRatio = 2f;

        /// <summary>
        /// wearer torso / avatar torso, both as straight head-to-hips distances in the same space. Returns 1
        /// (treated as no correction) for a nonsensical measurement, so a bad calibration frame stays inert.
        /// </summary>
        public static float ComputeRatio(float userTorso, float avatarTorso)
        {
            if (!(userTorso > k_MinTorsoMeters) || !(avatarTorso > k_MinTorsoMeters))
            {
                return 1f;
            }
            float ratio = userTorso / avatarTorso;
            if (!(ratio >= k_MinRatio) || !(ratio <= k_MaxRatio))
            {
                return 1f;
            }
            return ratio;
        }

        /// <summary>
        /// The uniform spine scale to apply: the proportion ratio clamped to +/- maxScale around 1. maxScale
        /// is the "very small amount" cap; a matching avatar (ratio 1) always returns exactly 1, and an
        /// invalid ratio returns 1 so the caller re-spaces nothing.
        /// </summary>
        public static float ComputeScale(float ratio, float maxScale)
        {
            if (!(ratio > 0f))
            {
                return 1f;
            }
            float cap = maxScale > 0f ? maxScale : 0f;
            float lo = 1f - cap;
            float hi = 1f + cap;
            if (ratio < lo) return lo;
            if (ratio > hi) return hi;
            return ratio;
        }
    }
}
