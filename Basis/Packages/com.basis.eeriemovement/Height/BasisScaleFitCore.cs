using UnityEngine;

namespace Basis.IK
{
    /// <summary>
    /// One player-to-avatar measurement pair offered to the scale fit.
    ///
    /// <see cref="Slack"/> is the crux: how many avatar-metres of mismatch the positional stretcher can
    /// absorb for this measurement before it runs out of budget. It is stated in metres rather than as
    /// a fraction because that is the form the body fit actually works in, and the two must agree
    /// exactly or the scale would hand the stretcher a residual it cannot take.
    /// </summary>
    public struct BasisScaleFitSample
    {
        /// <summary>The player's measurement, in real metres.</summary>
        public float Player;
        /// <summary>The same measurement on the authored avatar, in avatar metres.</summary>
        public float Avatar;
        /// <summary>Avatar-metres of mismatch the stretcher can take up here. Zero makes it a hard pin.</summary>
        public float Slack;
        /// <summary>Only decides who gives way when two constraints cannot both be satisfied.</summary>
        public float Weight;

        public static BasisScaleFitSample None => default;
    }

    /// <summary>
    /// Everything known about the player's body against the avatar's, for one scale decision. Eye
    /// height is the preference rather than a constraint: its residual is taken up by shifting the play
    /// space (the grounding lift), which costs a floor offset, not a proportion error.
    /// </summary>
    public struct BasisScaleFitInput
    {
        public BasisScaleFitSample Eye;
        public BasisScaleFitSample ArmSpan;
        public BasisScaleFitSample HipHeight;
        public BasisScaleFitSample LegSpan;

        /// <summary>How far the uniform scale may drift from the eye-matched scale, as a fraction. Caps
        /// the floor offset a wild limb measurement can inflict on the viewpoint.</summary>
        public float MaxEyeDeviation;
    }

    public enum BasisScaleFitStatus
    {
        /// <summary>Nothing measurable — the caller should keep its previous scale.</summary>
        NoData,
        /// <summary>The eye-matched scale already leaves every segment inside the stretcher's budget, so
        /// eye height matches exactly and the stretcher does all the work. The ideal outcome.</summary>
        EyeExact,
        /// <summary>A segment fell outside what the stretcher could absorb, so the scale moved the
        /// minimum needed to bring it back in range.</summary>
        Adjusted,
        /// <summary>Two segments wanted incompatible scales; fell back to the weighted geometric mean.</summary>
        Compromised,
    }

    public struct BasisScaleFitResult
    {
        /// <summary>The single uniform scale mapping player metres into avatar metres.</summary>
        public float Scale;
        public BasisScaleFitStatus Status;
        /// <summary>How many measurement pairs were usable.</summary>
        public int UsedCount;

        /// <summary>Per-metric leftover after the uniform scale: the metric's own ratio over the applied
        /// scale. 1 means exactly matched; the stretcher (or, for the eye, the grounding lift) makes up
        /// the difference.</summary>
        public float EyeResidual;
        public float ArmResidual;
        public float HipResidual;
        public float LegResidual;

        public bool IsValid => Status != BasisScaleFitStatus.NoData && Scale > 0f;

        public static BasisScaleFitResult Invalid => new BasisScaleFitResult
        {
            Scale = 0f,
            Status = BasisScaleFitStatus.NoData,
            UsedCount = 0,
            EyeResidual = 1f,
            ArmResidual = 1f,
            HipResidual = 1f,
            LegResidual = 1f,
        };
    }

    /// <summary>
    /// Picks the single uniform avatar scale that fits the player into the avatar best, given every
    /// body measurement we hold.
    ///
    /// The scale sits as close to the eye-matched scale as the other segments allow. While the
    /// arm/leg/torso mismatch stays inside what the positional stretcher can absorb, eye height is
    /// matched EXACTLY and the stretcher takes up the whole difference — the ideal, because an exact
    /// eye match costs no floor offset at all. Only when a segment falls outside the stretcher's budget
    /// does the scale move, and then by the minimum that brings it back into range.
    ///
    /// Each constraint admits an interval of acceptable scales — <c>Player * s</c> must land within
    /// <c>Slack</c> of <c>Avatar</c> — so the feasible set is the intersection of those intervals and
    /// the answer is the eye-matched scale clamped into it. When the intersection is empty (two
    /// segments demand incompatible scales: a bad measurement, or genuinely unfittable proportions) it
    /// falls back to the weight-biased geometric mean, which is the least-squares answer in log space.
    /// </summary>
    public static class BasisScaleFitCore
    {
        /// <summary>Below this a measurement is noise, not a body segment.</summary>
        public const float MinMeasureMeters = 0.05f;
        /// <summary>A player/avatar ratio outside this band is a broken measurement, not a small player.</summary>
        public const float MinRatio = 0.5f;
        public const float MaxRatio = 2f;
        /// <summary>Default cap on how far the uniform scale may drift from the eye-matched scale.</summary>
        public const float DefaultMaxEyeDeviation = 0.15f;

        /// <summary>Eye height: the stablest measurement and the one the viewpoint rides on.</summary>
        public const float EyeWeight = 1f;
        /// <summary>Arm span: trustworthy once the player has actually reached out — the caller scales
        /// this down while the measurement is still a guess.</summary>
        public const float ArmSpanWeight = 0.7f;
        /// <summary>Hip and leg: real evidence, but a belt-worn puck sits off the joint it names.</summary>
        public const float HipWeight = 0.4f;
        public const float LegWeight = 0.4f;

        public static BasisScaleFitResult Solve(in BasisScaleFitInput input)
        {
            BasisScaleFitResult result = BasisScaleFitResult.Invalid;

            bool hasEye = TryRatio(input.Eye, out float eyeRatio);
            bool hasArm = TryRatio(input.ArmSpan, out float armRatio);
            bool hasHip = TryRatio(input.HipHeight, out float hipRatio);
            bool hasLeg = TryRatio(input.LegSpan, out float legRatio);

            int used = (hasEye ? 1 : 0) + (hasArm ? 1 : 0) + (hasHip ? 1 : 0) + (hasLeg ? 1 : 0);
            if (used == 0)
            {
                return result;
            }
            result.UsedCount = used;

            // Feasible scales, in log space so the clamp and the geometric-mean fallback share units.
            float lo = float.NegativeInfinity;
            float hi = float.PositiveInfinity;
            AccumulateBand(hasArm, input.ArmSpan, ref lo, ref hi);
            AccumulateBand(hasHip, input.HipHeight, ref lo, ref hi);
            AccumulateBand(hasLeg, input.LegSpan, ref lo, ref hi);
            bool feasible = lo <= hi;

            float logScale;
            if (hasEye)
            {
                float logEye = Mathf.Log(eyeRatio);
                if (feasible)
                {
                    logScale = Mathf.Clamp(logEye, lo, hi);
                    // Approximately() would call a move of one millionth "exact"; a real clamp moves the
                    // scale by percent, so an exact compare is the honest test.
                    result.Status = logScale == logEye ? BasisScaleFitStatus.EyeExact : BasisScaleFitStatus.Adjusted;
                }
                else
                {
                    logScale = WeightedLogMean(in input, hasEye, eyeRatio, hasArm, armRatio, hasHip, hipRatio, hasLeg, legRatio);
                    result.Status = BasisScaleFitStatus.Compromised;
                }

                // Never let a limb drag the viewpoint far from the player's real eye height: past this the
                // floor offset the grounding lift must invent costs more than the proportion error saved.
                float maxDeviation = input.MaxEyeDeviation > 0f ? input.MaxEyeDeviation : DefaultMaxEyeDeviation;
                float logBudget = Mathf.Log(1f + maxDeviation);
                float clamped = Mathf.Clamp(logScale, logEye - logBudget, logEye + logBudget);
                if (clamped != logScale)
                {
                    logScale = clamped;
                    result.Status = BasisScaleFitStatus.Compromised;
                }
            }
            else if (feasible && !float.IsInfinity(lo) && !float.IsInfinity(hi))
            {
                // No eye height to prefer: sit in the middle of what the limbs allow.
                logScale = (lo + hi) * 0.5f;
                result.Status = BasisScaleFitStatus.Adjusted;
            }
            else
            {
                logScale = WeightedLogMean(in input, hasEye, eyeRatio, hasArm, armRatio, hasHip, hipRatio, hasLeg, legRatio);
                result.Status = feasible ? BasisScaleFitStatus.Adjusted : BasisScaleFitStatus.Compromised;
            }

            float scale = Mathf.Exp(logScale);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
            {
                return BasisScaleFitResult.Invalid;
            }

            result.Scale = scale;
            result.EyeResidual = hasEye ? eyeRatio / scale : 1f;
            result.ArmResidual = hasArm ? armRatio / scale : 1f;
            result.HipResidual = hasHip ? hipRatio / scale : 1f;
            result.LegResidual = hasLeg ? legRatio / scale : 1f;
            return result;
        }

        /// <summary>
        /// Narrows the feasible scale interval by one segment: <c>Player * s</c> has to land within the
        /// segment's slack of <c>Avatar</c>, so the segment admits
        /// <c>[(Avatar-Slack)/Player, (Avatar+Slack)/Player]</c>. Slack of zero pins the scale to the
        /// segment's own ratio.
        /// </summary>
        static void AccumulateBand(bool has, in BasisScaleFitSample sample, ref float lo, ref float hi)
        {
            if (!has)
            {
                return;
            }
            float slack = Mathf.Max(0f, sample.Slack);
            float lowTarget = sample.Avatar - slack;
            if (lowTarget < MinMeasureMeters)
            {
                // Slack wider than the segment itself: it can shrink to nothing, so there is no lower bound.
                lowTarget = MinMeasureMeters;
            }
            float low = Mathf.Log(lowTarget / sample.Player);
            float high = Mathf.Log((sample.Avatar + slack) / sample.Player);
            if (low > lo) lo = low;
            if (high < hi) hi = high;
        }

        static float WeightedLogMean(
            in BasisScaleFitInput input,
            bool hasEye, float eyeRatio,
            bool hasArm, float armRatio,
            bool hasHip, float hipRatio,
            bool hasLeg, float legRatio)
        {
            float sum = 0f;
            float weights = 0f;
            Accumulate(hasEye, eyeRatio, input.Eye.Weight, ref sum, ref weights);
            Accumulate(hasArm, armRatio, input.ArmSpan.Weight, ref sum, ref weights);
            Accumulate(hasHip, hipRatio, input.HipHeight.Weight, ref sum, ref weights);
            Accumulate(hasLeg, legRatio, input.LegSpan.Weight, ref sum, ref weights);
            return weights > 0f ? sum / weights : 0f;
        }

        static void Accumulate(bool has, float ratio, float weight, ref float sum, ref float weights)
        {
            if (!has)
            {
                return;
            }
            float w = Mathf.Max(0f, weight);
            if (w <= 0f)
            {
                return;
            }
            sum += w * Mathf.Log(ratio);
            weights += w;
        }

        /// <summary>
        /// Avatar-over-player ratio for one pair, or false when either side is unmeasured, weightless,
        /// or so far apart that it can only be a bad reading.
        /// </summary>
        static bool TryRatio(in BasisScaleFitSample sample, out float ratio)
        {
            ratio = 1f;
            if (!Usable(sample.Player) || !Usable(sample.Avatar) || sample.Weight <= 0f)
            {
                return false;
            }
            float r = sample.Avatar / sample.Player;
            if (float.IsNaN(r) || float.IsInfinity(r) || r < MinRatio || r > MaxRatio)
            {
                return false;
            }
            ratio = r;
            return true;
        }

        static bool Usable(float value) => value > MinMeasureMeters && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
