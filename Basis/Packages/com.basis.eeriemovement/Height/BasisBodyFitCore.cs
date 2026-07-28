using UnityEngine;

namespace Basis.IK
{
    public enum BasisBodyFitStatus
    {
        Fitted,
        Disabled,
        PlayerEyeHeightMissing,
        AvatarEyeHeightMissing,
        PlayerArmSpanMissing,
        AvatarArmSpanMissing,
        ArmLengthDegenerate,
        ArmRatioOutOfBand,
        HipsTrackerMissing,
        HipHeightImplausible,
        AvatarHipHeightMissing,
        AvatarLegSpanDegenerate,
        AvatarSpineSpanDegenerate,
        HipRatioOutOfBand,
    }

    public struct BasisBodyFitMeasurements
    {
        public float PlayerEyeHeight;
        public float PlayerArmSpan;
        public float PlayerHipHeight;
        public float AvatarEyeHeight;
        public float AvatarArmSpan;
        public float AvatarHipHeight;
        public float AvatarLegSpan;
        public float AvatarSpineSpan;
        public float AvatarShoulderWidth;

        /// <summary>
        /// The uniform scale actually applied to the avatar — what one player metre became in avatar
        /// metres. The fit measures its residual against THIS, so whatever the uniform scale left over
        /// is exactly what the segments take up. Deriving it from eye height instead (as this did
        /// before <see cref="BasisScaleFitCore"/> existed) silently assumed the scale had matched eye
        /// height, so in any other height mode the fit computed a residual that did not exist and
        /// pulled against the scale rather than completing it.
        ///
        /// Zero or less falls back to the eye-height ratio, which is the correct answer whenever the
        /// scale did match eye height.
        /// </summary>
        public float UniformScale;
    }

    public struct BasisBodyFitResult
    {
        public float ArmScale;
        public float LegScale;
        public float TorsoScale;
        public BasisBodyFitStatus ArmStatus;
        public BasisBodyFitStatus BodyStatus;

        public bool HasArmFit => ArmStatus == BasisBodyFitStatus.Fitted;
        public bool HasBodyFit => BodyStatus == BasisBodyFitStatus.Fitted;

        public static BasisBodyFitResult Identity => new BasisBodyFitResult
        {
            ArmScale = 1f,
            LegScale = 1f,
            TorsoScale = 1f,
            ArmStatus = BasisBodyFitStatus.Disabled,
            BodyStatus = BasisBodyFitStatus.Disabled,
        };

        public bool IsIdentity =>
            Mathf.Approximately(ArmScale, 1f) &&
            Mathf.Approximately(LegScale, 1f) &&
            Mathf.Approximately(TorsoScale, 1f);
    }

    public static class BasisBodyFitCore
    {
        public const float DefaultMaxDeviation = 0.15f;
        public const float MaxDeviationCeiling = 0.5f;

        const float k_MinSegmentMeters = 0.05f;
        const float k_MinRatio = 0.5f;
        const float k_MaxRatio = 2f;
        const float k_MinHipFraction = 0.35f;
        const float k_MaxHipFraction = 0.75f;

        public static string Describe(BasisBodyFitStatus status) => status switch
        {
            BasisBodyFitStatus.Fitted => "fitted",
            BasisBodyFitStatus.Disabled => "turned off",
            BasisBodyFitStatus.PlayerEyeHeightMissing => $"your eye height has not been measured (needs more than {k_MinSegmentMeters:F2} m)",
            BasisBodyFitStatus.AvatarEyeHeightMissing => $"the avatar's eye height is unreadable (needs more than {k_MinSegmentMeters:F2} m)",
            BasisBodyFitStatus.PlayerArmSpanMissing => "your arm span has not been measured — hold both controllers out and calibrate",
            BasisBodyFitStatus.AvatarArmSpanMissing => "the avatar's arm span is unreadable — its hand bones may be missing",
            BasisBodyFitStatus.ArmLengthDegenerate => "the avatar's shoulders are as wide as its arm span, leaving no arm to resize",
            BasisBodyFitStatus.ArmRatioOutOfBand => $"your arms differ from the avatar's by more than the sane band ({k_MinRatio:F1}x to {k_MaxRatio:F1}x) — likely a bad calibration frame",
            BasisBodyFitStatus.HipsTrackerMissing => "no hips tracker, so your leg-to-torso split is unknown",
            BasisBodyFitStatus.HipHeightImplausible => $"your hips tracker is not sitting at a hip height (expected {k_MinHipFraction:P0} to {k_MaxHipFraction:P0} of your eye height) — it may be assigned to the wrong body part",
            BasisBodyFitStatus.AvatarHipHeightMissing => "the avatar's hip bone height is unreadable",
            BasisBodyFitStatus.AvatarLegSpanDegenerate => "the avatar has no measurable thigh-to-ankle length to resize",
            BasisBodyFitStatus.AvatarSpineSpanDegenerate => "the avatar has no measurable hips-to-head length to resize",
            BasisBodyFitStatus.HipRatioOutOfBand => $"your hip height differs from the avatar's by more than the sane band ({k_MinRatio:F1}x to {k_MaxRatio:F1}x) — likely a bad calibration frame",
            _ => "unknown",
        };

        /// <summary>
        /// Avatar-metres of arm-span mismatch the arm fit can absorb at this deviation, for
        /// <see cref="BasisScaleFitSample.Slack"/>. The arm fit scales arm length (span minus shoulders)
        /// by at most 1±deviation, so the reachable span moves by that much of the arm-only part —
        /// shoulder width does not scale, which is why this is not simply deviation × span.
        /// Must stay in step with <see cref="SolveArms"/>.
        /// </summary>
        public static float ArmSpanSlack(in BasisBodyFitMeasurements m, float maxDeviation)
        {
            float deviation = Mathf.Clamp(maxDeviation, 0f, MaxDeviationCeiling);
            float armOnly = m.AvatarArmSpan - Mathf.Max(0f, m.AvatarShoulderWidth);
            return armOnly > 0f ? deviation * armOnly : 0f;
        }

        /// <summary>
        /// Avatar-metres of hip-height mismatch the leg/torso fit can absorb at this deviation, for
        /// <see cref="BasisScaleFitSample.Slack"/>. The fit trades leg length against spine length, so
        /// the shorter of the two caps the shift. Must stay in step with <see cref="SolveBody"/>.
        /// </summary>
        public static float HipHeightSlack(in BasisBodyFitMeasurements m, float maxDeviation)
        {
            float deviation = Mathf.Clamp(maxDeviation, 0f, MaxDeviationCeiling);
            float shorter = Mathf.Min(m.AvatarLegSpan, m.AvatarSpineSpan);
            return shorter > 0f ? deviation * shorter : 0f;
        }

        public static BasisBodyFitResult Solve(in BasisBodyFitMeasurements m, float maxDeviation)
        {
            BasisBodyFitResult result = BasisBodyFitResult.Identity;

            float deviation = Mathf.Clamp(maxDeviation, 0f, MaxDeviationCeiling);
            if (deviation <= 0f)
            {
                return result;
            }

            if (!Plausible(m.PlayerEyeHeight))
            {
                result.ArmStatus = BasisBodyFitStatus.PlayerEyeHeightMissing;
                result.BodyStatus = BasisBodyFitStatus.PlayerEyeHeightMissing;
                return result;
            }

            if (!Plausible(m.AvatarEyeHeight))
            {
                result.ArmStatus = BasisBodyFitStatus.AvatarEyeHeightMissing;
                result.BodyStatus = BasisBodyFitStatus.AvatarEyeHeightMissing;
                return result;
            }

            float toAvatarSpace = m.UniformScale > 0f && !float.IsNaN(m.UniformScale) && !float.IsInfinity(m.UniformScale)
                ? m.UniformScale
                : m.AvatarEyeHeight / m.PlayerEyeHeight;

            SolveArms(in m, toAvatarSpace, deviation, ref result);
            SolveBody(in m, toAvatarSpace, deviation, ref result);

            return result;
        }

        static void SolveArms(in BasisBodyFitMeasurements m, float toAvatarSpace, float deviation, ref BasisBodyFitResult result)
        {
            if (!Plausible(m.PlayerArmSpan))
            {
                result.ArmStatus = BasisBodyFitStatus.PlayerArmSpanMissing;
                return;
            }

            if (!Plausible(m.AvatarArmSpan))
            {
                result.ArmStatus = BasisBodyFitStatus.AvatarArmSpanMissing;
                return;
            }

            float shoulderWidth = Mathf.Max(0f, m.AvatarShoulderWidth);
            float avatarArm = (m.AvatarArmSpan - shoulderWidth) * 0.5f;
            float playerArm = (m.PlayerArmSpan * toAvatarSpace - shoulderWidth) * 0.5f;

            if (!Plausible(avatarArm) || !Plausible(playerArm))
            {
                result.ArmStatus = BasisBodyFitStatus.ArmLengthDegenerate;
                return;
            }

            float ratio = playerArm / avatarArm;
            if (!InRatioBand(ratio))
            {
                result.ArmStatus = BasisBodyFitStatus.ArmRatioOutOfBand;
                return;
            }

            result.ArmScale = Mathf.Clamp(ratio, 1f - deviation, 1f + deviation);
            result.ArmStatus = BasisBodyFitStatus.Fitted;
        }

        static void SolveBody(in BasisBodyFitMeasurements m, float toAvatarSpace, float deviation, ref BasisBodyFitResult result)
        {
            if (!Plausible(m.PlayerHipHeight))
            {
                result.BodyStatus = BasisBodyFitStatus.HipsTrackerMissing;
                return;
            }

            float hipFraction = m.PlayerHipHeight / m.PlayerEyeHeight;
            if (hipFraction < k_MinHipFraction || hipFraction > k_MaxHipFraction)
            {
                result.BodyStatus = BasisBodyFitStatus.HipHeightImplausible;
                return;
            }

            if (!Plausible(m.AvatarHipHeight))
            {
                result.BodyStatus = BasisBodyFitStatus.AvatarHipHeightMissing;
                return;
            }

            if (!Plausible(m.AvatarLegSpan))
            {
                result.BodyStatus = BasisBodyFitStatus.AvatarLegSpanDegenerate;
                return;
            }

            if (!Plausible(m.AvatarSpineSpan))
            {
                result.BodyStatus = BasisBodyFitStatus.AvatarSpineSpanDegenerate;
                return;
            }

            float playerHip = m.PlayerHipHeight * toAvatarSpace;
            if (!InRatioBand(playerHip / m.AvatarHipHeight))
            {
                result.BodyStatus = BasisBodyFitStatus.HipRatioOutOfBand;
                return;
            }

            float budget = deviation * Mathf.Min(m.AvatarLegSpan, m.AvatarSpineSpan);
            float shift = Mathf.Clamp(playerHip - m.AvatarHipHeight, -budget, budget);

            result.LegScale = 1f + shift / m.AvatarLegSpan;
            result.TorsoScale = 1f - shift / m.AvatarSpineSpan;
            result.BodyStatus = BasisBodyFitStatus.Fitted;
        }

        static bool Plausible(float value) => value > k_MinSegmentMeters && !float.IsNaN(value) && !float.IsInfinity(value);

        static bool InRatioBand(float ratio) => ratio >= k_MinRatio && ratio <= k_MaxRatio;
    }
}
