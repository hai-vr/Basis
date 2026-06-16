using Basis.BasisUI;
using UnityEngine;

namespace Basis.Scripts.Device_Management.Devices.Pairing
{
    /// <summary>
    /// The fusion tunables, read once per frame. Lives apart from the live settings so the offline temporal
    /// sweep can drive the exact same math with fixed numbers (and so the per-frame read stays cheap).
    /// <see cref="Default"/> mirrors the BasisSettingsDefaults.Pairing* defaults; <see cref="FromSettings"/>
    /// is what the runtime feeds.
    /// </summary>
    public struct BasisMidpointFusionTunables
    {
        public float SurprisePenalty;
        public float SurpriseClamp;
        public float EmaFloor;
        public float MaxCorrectionStrength;
        public float SoftSnapHalfLife;
        public float LockstepTolerance;
        public float EmaAlpha;
        public float DistanceEmaAlpha;
        public float WeightSmoothing;
        public float RotationHalfLife;

        public static BasisMidpointFusionTunables Default()
        {
            return new BasisMidpointFusionTunables
            {
                SurprisePenalty = 2f,
                SurpriseClamp = 3f,
                EmaFloor = 0.005f,
                MaxCorrectionStrength = 0.3f,
                SoftSnapHalfLife = 0.05f,
                LockstepTolerance = 0.05f,
                EmaAlpha = 0.1f,
                DistanceEmaAlpha = 0.05f,
                WeightSmoothing = 0.25f,
                RotationHalfLife = 0f, // off by default -- the bone sim already smooths; a second low-pass lags fast turns
            };
        }

        public static BasisMidpointFusionTunables FromSettings()
        {
            return new BasisMidpointFusionTunables
            {
                SurprisePenalty = BasisSettingsDefaults.PairingSurprisePenalty.RawValue,
                SurpriseClamp = BasisSettingsDefaults.PairingSurpriseClamp.RawValue,
                EmaFloor = BasisSettingsDefaults.PairingEmaFloor.RawValue,
                MaxCorrectionStrength = BasisSettingsDefaults.PairingMaxCorrectionStrength.RawValue,
                SoftSnapHalfLife = BasisSettingsDefaults.PairingSoftSnapHalfLife.RawValue,
                LockstepTolerance = BasisSettingsDefaults.PairingLockstepTolerance.RawValue,
                EmaAlpha = BasisSettingsDefaults.PairingEmaAlpha.RawValue,
                DistanceEmaAlpha = BasisSettingsDefaults.PairingDistanceEmaAlpha.RawValue,
                WeightSmoothing = BasisSettingsDefaults.PairingWeightSmoothing.RawValue,
                RotationHalfLife = BasisSettingsDefaults.PairingRotationHalfLife.RawValue,
            };
        }
    }

    /// <summary>
    /// Per-pair fusion state carried across frames (velocity EMAs, smoothed confidence weights, the captured
    /// rest distance + half-rest offset, and the rotation low-pass). A fresh pair starts at <see cref="Fresh"/>.
    /// </summary>
    public struct BasisMidpointFusionState
    {
        public bool HasLastPositions;
        public Vector3 LastA;
        public Vector3 LastB;
        // Last published midpoint, carried so a partner dropping out can keep the midpoint
        // anchored to the live partner instead of collapsing toward origin.
        public Vector3 LastMid;
        public float EmaVelA;
        public float EmaVelB;
        public float ExpectedDistance;
        // Smoothed confidence weights -- smoothing is what stops the midpoint from snapping when a moving
        // tracker briefly looks surprising relative to its at-rest baseline.
        public float SmoothedWA;
        public float SmoothedWB;
        // Half of the rest-pose relative rotation captured on the first frame; see BasisMidpointRotationFusion.
        public Quaternion HalfRestOffset;
        // Temporal low-pass state for the fused rotation; strength is RotationHalfLife.
        public Quaternion SmoothedMidRot;
        public bool HasSmoothedRot;

        public static BasisMidpointFusionState Fresh()
        {
            return new BasisMidpointFusionState
            {
                SmoothedWA = 1f,
                SmoothedWB = 1f,
                HalfRestOffset = Quaternion.identity,
                SmoothedMidRot = Quaternion.identity,
            };
        }
    }

    /// <summary>
    /// The exact per-frame fusion <see cref="BasisVirtualMidpointInput"/> runs: a confidence-weighted position
    /// blend with a continuous pull toward the calibrated rest distance, a rest-offset-projected rotation slerp,
    /// and a rotation low-pass. Pulled out of the MonoBehaviour so the offline temporal sweep drives the
    /// identical stateful math frame-by-frame with no scene or play mode.
    /// </summary>
    public static class BasisMidpointFusionCore
    {
        public static void Step(
            ref BasisMidpointFusionState s,
            Vector3 a, Vector3 b, Quaternion aRot, Quaternion bRot,
            in BasisMidpointFusionTunables t, float dt,
            out Vector3 mid, out Quaternion midRot, out float wA, out float wB, out float blendT)
        {
            blendT = 0.5f;

            bool aUsable = BasisMidpointRotationFusion.IsUsable(aRot);
            bool bUsable = BasisMidpointRotationFusion.IsUsable(bRot);

            if (!s.HasLastPositions)
            {
                mid = (a + b) * 0.5f;
                midRot = BasisMidpointRotationFusion.ProjectAndSlerp(aRot, bRot, 0.5f, s.HalfRestOffset);
                wA = s.SmoothedWA;
                wB = s.SmoothedWB;
                // Defer the one-shot prime (rest distance + half-rest offset) until both
                // partners actually have a pose, so a pair created mid-dropout can't
                // capture a garbage baseline that biases every later frame.
                if (aUsable && bUsable)
                {
                    s.ExpectedDistance = Vector3.Distance(a, b);
                    s.EmaVelA = 0f;
                    s.EmaVelB = 0f;
                    s.SmoothedWA = 1f;
                    s.SmoothedWB = 1f;
                    s.HalfRestOffset = BasisMidpointRotationFusion.ComputeHalfRestOffset(aRot, bRot);
                    s.HasLastPositions = true;
                }
            }
            else
            {
                float softSnapHalfLife = Mathf.Max(t.SoftSnapHalfLife, 1e-4f);
                float weightSmoothing = Mathf.Clamp01(t.WeightSmoothing);

                float velA = Vector3.Distance(a, s.LastA);
                float velB = Vector3.Distance(b, s.LastB);

                // Shared baseline: judge BOTH trackers against the higher recent EMA (plus floor) so a frozen
                // tracker doesn't get a "low baseline -> easy to look surprising" effect and a real bilateral
                // motion onset doesn't fool both into looking surprising.
                float sharedBaseline = Mathf.Max(s.EmaVelA, s.EmaVelB) + t.EmaFloor;
                float surpriseA = velA / sharedBaseline;
                float surpriseB = velB / sharedBaseline;
                float excessA = Mathf.Max(0f, surpriseA - 1f);
                float excessB = Mathf.Max(0f, surpriseB - 1f);
                float instantWA = 1f / (1f + excessA * excessA * t.SurprisePenalty);
                float instantWB = 1f / (1f + excessB * excessB * t.SurprisePenalty);

                s.SmoothedWA = Mathf.Lerp(s.SmoothedWA, instantWA, weightSmoothing);
                s.SmoothedWB = Mathf.Lerp(s.SmoothedWB, instantWB, weightSmoothing);
                wA = s.SmoothedWA;
                wB = s.SmoothedWB;

                float currentDistance = Vector3.Distance(a, b);
                float distanceError = currentDistance - s.ExpectedDistance;
                float absDistanceError = Mathf.Abs(distanceError);

                float weightSum = Mathf.Max(wA + wB, 1e-6f);
                if (aUsable && bUsable)
                {
                    // Soft pull toward the rigid rest-distance solution, ramping continuously with the divergence;
                    // disabled when weights are imbalanced so the symmetric pull can't drag the trusted tracker.
                    float weightBalance = 4f * wA * wB / (weightSum * weightSum);
                    float correctionStrength = t.MaxCorrectionStrength
                                               * weightBalance
                                               * (1f - 1f / (1f + absDistanceError / softSnapHalfLife));
                    Vector3 dir = currentDistance > 1e-6f ? (b - a) / currentDistance : Vector3.zero;
                    Vector3 aIdeal = a + dir * (distanceError * 0.5f);
                    Vector3 bIdeal = b - dir * (distanceError * 0.5f);
                    Vector3 aSoft = Vector3.Lerp(a, aIdeal, correctionStrength);
                    Vector3 bSoft = Vector3.Lerp(b, bIdeal, correctionStrength);

                    float tB = wB / weightSum;
                    blendT = tB;
                    mid = aSoft * (1f - tB) + bSoft * tB;
                }
                else if (!aUsable && !bUsable)
                {
                    // Whole pair dark -- hold the last midpoint instead of collapsing toward origin.
                    mid = s.LastMid;
                }
                else if (aUsable)
                {
                    // One partner dark -- follow the live one, carrying the last good midpoint offset so
                    // there is no jump at the dropout (the live partner is still where it just was).
                    blendT = 0f;
                    mid = a + (s.LastMid - s.LastA);
                }
                else
                {
                    blendT = 1f;
                    mid = b + (s.LastMid - s.LastB);
                }
                // Rotation uses a FIXED 0.5 blend, decoupled from the position-confidence tB: the rest-offset
                // projection makes it t-invariant for a rigid pair, and fixing it stops the fused rotation
                // swinging across the midA/midB gap as tB shifts with motion under flex -- the snap the rotation
                // low-pass used to mask. Position keeps the confidence blend (averaging two trackers helps there).
                midRot = BasisMidpointRotationFusion.ProjectAndSlerp(aRot, bRot, 0.5f, s.HalfRestOffset);

                // Update the velocity EMA only when the current sample isn't a clear outlier.
                if (surpriseA <= t.SurpriseClamp) s.EmaVelA = Mathf.Lerp(s.EmaVelA, velA, t.EmaAlpha);
                if (surpriseB <= t.SurpriseClamp) s.EmaVelB = Mathf.Lerp(s.EmaVelB, velB, t.EmaAlpha);

                // Update the rest-distance EMA only when both trackers look healthy AND inside lockstep.
                if (surpriseA <= t.SurpriseClamp && surpriseB <= t.SurpriseClamp && absDistanceError < t.LockstepTolerance)
                {
                    s.ExpectedDistance = Mathf.Lerp(s.ExpectedDistance, currentDistance, t.DistanceEmaAlpha);
                }
            }

            if (aUsable) s.LastA = a;
            if (bUsable) s.LastB = b;
            s.LastMid = mid;

            // Neither partner had a pose this frame -- hold the last published rotation
            // instead of snapping the hip to identity.
            if (s.HasSmoothedRot && !aUsable && !bUsable)
            {
                midRot = s.SmoothedMidRot;
            }

            // Low-pass the fused rotation. Half-life <= 0 disables.
            if (!s.HasSmoothedRot || t.RotationHalfLife <= 1e-5f)
            {
                s.SmoothedMidRot = midRot;
                s.HasSmoothedRot = true;
            }
            else
            {
                float rotDt = Mathf.Max(dt, 0f);
                float rotAlpha = 1f - Mathf.Pow(2f, -rotDt / t.RotationHalfLife);
                s.SmoothedMidRot = Quaternion.Slerp(s.SmoothedMidRot, midRot, rotAlpha);
            }
            midRot = s.SmoothedMidRot;
        }
    }
}
