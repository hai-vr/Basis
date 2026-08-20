using UnityEngine;

namespace Basis.Scripts.BasisSdk.Interactions
{

    /// <summary>
    /// Chooses which interactable a hand takes when the pointer ray and the proximity bubble disagree.
    /// Pure geometry with no scene, player or network dependency so the rules can be tested directly.
    ///
    /// Three bands, in order:
    ///
    /// • <b>InHand</b> — inside the object's own grab radius of the hand bone. Closing your hand on a
    ///   prop takes it no matter where you are pointing, in any direction.
    ///
    /// • <b>Aimed</b> — inside a cone about the pointer ray that widens with distance, so aim tolerance
    ///   stays roughly constant in screen terms rather than collapsing at arm's length. Scored by
    ///   distance along the ray with off-axis distance as a light tiebreak, matching
    ///   <see cref="BasisJiggleGrabPicker.TryScorePointing"/>.
    ///
    /// • <b>Nearby</b> — within reach but off-aim. Preserves loose bubble grabbing for props beside the
    ///   hand while keeping them behind anything actually aimed at.
    /// </summary>
    public static class BasisInteractTargetPicker
    {
        /// <summary>How much off-axis distance counts against an aimed candidate, relative to depth.</summary>
        public const float OffAxisWeight = 0.5f;

        /// <summary>Half angle of the aim cone.</summary>
        public const float AimConeHalfAngleDegrees = 18f;

        /// <summary>Floor on the aim cone radius so candidates right at the hand are still aimable.</summary>
        public const float AimConeMinRadius = 0.03f;

        /// <summary>How much wider the bands are for the target already held or hovered.</summary>
        public const float StickyScale = 1.4f;

        /// <summary>How much better a challenger must score before it takes the target off the incumbent.</summary>
        public const float SwitchMargin = 0.05f;

        /// <summary>Radius of the aim cone at a given depth along the ray.</summary>
        public static float AimConeRadius(float distanceAlongAim, float scale)
        {
            float spread = distanceAlongAim > 0f ? distanceAlongAim * Mathf.Tan(AimConeHalfAngleDegrees * Mathf.Deg2Rad) : 0f;
            return Mathf.Max(AimConeMinRadius, spread) * Mathf.Max(scale, 0f);
        }

        /// <summary>
        /// Bands and scores one candidate point. Lower band wins first, then lower score.
        /// </summary>
        public static BasisInteractReach Classify(
            Vector3 point,
            Vector3 aimOrigin,
            Vector3 aimDirection,
            Vector3 handPosition,
            float grabRadius,
            float reachRadius,
            float scale,
            out float score)
        {
            score = float.MaxValue;
            scale = Mathf.Max(scale, 0f);

            float handDistance = Vector3.Distance(point, handPosition);
            if (handDistance <= grabRadius * scale)
            {
                score = handDistance;
                return BasisInteractReach.InHand;
            }

            Vector3 direction = aimDirection.sqrMagnitude > 1e-10f ? aimDirection.normalized : Vector3.forward;
            Vector3 toPoint = point - aimOrigin;
            float alongAim = Vector3.Dot(toPoint, direction);
            if (alongAim >= 0f)
            {
                float offAxis = Vector3.Distance(toPoint, direction * alongAim);
                if (offAxis <= AimConeRadius(alongAim, scale))
                {
                    score = alongAim + offAxis * OffAxisWeight;
                    return BasisInteractReach.Aimed;
                }
            }

            if (handDistance <= reachRadius * scale)
            {
                score = handDistance;
                return BasisInteractReach.Nearby;
            }

            return BasisInteractReach.None;
        }

        /// <summary>
        /// Whether a challenger displaces the current best. A positive margin defends the holder.
        /// </summary>
        public static bool Beats(
            BasisInteractReach challengerReach,
            float challengerScore,
            BasisInteractReach holderReach,
            float holderScore,
            float margin)
        {
            if (challengerReach == BasisInteractReach.None)
            {
                return false;
            }
            if (holderReach == BasisInteractReach.None)
            {
                return true;
            }
            if (challengerReach != holderReach)
            {
                return challengerReach < holderReach;
            }
            return challengerScore < holderScore - margin;
        }
    }
}
