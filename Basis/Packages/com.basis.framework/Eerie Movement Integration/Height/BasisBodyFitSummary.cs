using Basis.BasisUI;
using Basis.IK;
using UnityEngine;

/// <summary>
/// Says, in words the player can act on, what size the system thinks they are and how well the avatar
/// they are wearing matches it.
///
/// Until this existed the only feedback was a developer-gated tracker count, so a player whose avatar
/// felt wrong had nothing to look at and no way to tell whether the problem was their measurement or
/// the avatar's proportions. The distinction matters because the fixes are opposite: one is
/// "re-measure me", the other is "nudge this avatar".
/// </summary>
public static class BasisBodyFitSummary
{
    /// <summary>The facts behind the summary, separated from their wording so they can be asserted on.</summary>
    public struct Facts
    {
        /// <summary>Full body height implied by the eye measurement in use.</summary>
        public float BodyHeight;
        public BasisHeightDriver.BasisBodyMeasurementSource HeightSource;

        public float Reach;
        public BasisHeightDriver.BasisBodyMeasurementSource ReachSource;
        /// <summary>False while the reach is still a guess — the player has not yet stretched out.</summary>
        public bool ReachMeasured;
        /// <summary>0..1, how settled the observation of the reach is.</summary>
        public float ReachConfidence;

        public bool HasAvatar;
        /// <summary>Signed fraction: +0.06 means this avatar's arms are 6% longer than yours, at the
        /// applied scale. This is a property of the AVATAR, which is the whole point of phrasing it
        /// this way — it tells the player where to point the blame.</summary>
        public float AvatarArmDifference;
        public float AvatarLegDifference;
        /// <summary>Whether the stretcher could absorb the arm difference, or ran out of room.</summary>
        public bool ArmsFitted;
        public bool LegsFitted;

        public BasisScaleFitStatus FitStatus;
        /// <summary>Signed fraction the uniform scale had to move away from matching eye height.</summary>
        public float ScaleDeviation;

        public bool DifferentPersonSuspected;
    }

    public static Facts Gather()
    {
        var facts = new Facts
        {
            BodyHeight = BasisCalibrationMath.ImpliedHeightFromEye(BasisHeightDriver.PlayerEyeHeight),
            HeightSource = BasisHeightDriver.EyeHeightSource,
            Reach = BasisHeightDriver.PlayerArmSpan,
            ReachSource = BasisHeightDriver.ArmSpanSource,
            ReachMeasured = BasisHeightDriver.HasGenuinePlayerArmSpan,
            ReachConfidence = BasisHeightDriver.ObservedArmSpanConfidence,
            DifferentPersonSuspected = BasisBodyEvidenceSampler.LooksLikeADifferentPerson(),
        };

        BasisBodyFitResult fit = Basis.Scripts.Drivers.BasisLocalRigDriver.AppliedBodyFit;
        facts.ArmsFitted = fit.HasArmFit;
        facts.LegsFitted = fit.HasBodyFit;

        BasisScaleFitResult scaleFit = BasisHeightDriver.LastScaleFit;
        facts.FitStatus = scaleFit.Status;
        if (scaleFit.IsValid && scaleFit.EyeResidual > 0f)
        {
            // How far the scale sits from the one that would have matched eye height exactly.
            facts.ScaleDeviation = (1f / scaleFit.EyeResidual) - 1f;
        }

        // The residual IS the difference the stretcher was asked to cover, expressed avatar-over-player.
        facts.HasAvatar = scaleFit.IsValid;
        facts.AvatarArmDifference = scaleFit.ArmResidual - 1f;
        facts.AvatarLegDifference = scaleFit.HipResidual - 1f;
        return facts;
    }

    /// <summary>The whole readout, one fact per line.</summary>
    public static string Build()
    {
        Facts facts = Gather();
        var sb = new System.Text.StringBuilder(320);

        sb.Append(BasisLocalization.Get("calibration.summary.height")).Append(' ')
          .Append(BasisStatedHeight.Format(facts.BodyHeight)).Append("  —  ")
          .Append(DescribeSource(facts.HeightSource)).Append('\n');

        sb.Append(BasisLocalization.Get("calibration.summary.reach")).Append(' ');
        if (facts.ReachMeasured)
        {
            sb.Append(BasisStatedHeight.Format(facts.Reach)).Append("  —  ").Append(DescribeSource(facts.ReachSource));
        }
        else
        {
            sb.Append(BasisLocalization.Get("calibration.summary.reach.unmeasured"));
        }
        sb.Append('\n');

        if (facts.HasAvatar)
        {
            sb.Append(DescribeAvatar(facts)).Append('\n');
            sb.Append(DescribeFit(facts));
        }

        if (facts.DifferentPersonSuspected)
        {
            sb.Append('\n').Append(BasisLocalization.Get("calibration.summary.differentPerson"));
        }

        return sb.ToString();
    }

    static string DescribeSource(BasisHeightDriver.BasisBodyMeasurementSource source) => source switch
    {
        BasisHeightDriver.BasisBodyMeasurementSource.Measured => BasisLocalization.Get("calibration.summary.source.measured"),
        BasisHeightDriver.BasisBodyMeasurementSource.Stated => BasisLocalization.Get("calibration.summary.source.stated"),
        BasisHeightDriver.BasisBodyMeasurementSource.Saved => BasisLocalization.Get("calibration.summary.source.saved"),
        BasisHeightDriver.BasisBodyMeasurementSource.SlimeVR => BasisLocalization.Get("calibration.summary.source.slimevr"),
        _ => BasisLocalization.Get("calibration.summary.source.fallback"),
    };

    /// <summary>
    /// Phrased as a property of the avatar rather than of the player, deliberately: "this avatar's arms
    /// are 18% longer than yours" points at the avatar, where "your arms are 18% short" reads as the
    /// player being measured wrong.
    /// </summary>
    static string DescribeAvatar(in Facts facts)
    {
        float arm = facts.AvatarArmDifference;
        if (Mathf.Abs(arm) < 0.02f)
        {
            return BasisLocalization.Get("calibration.summary.avatar.matches");
        }
        string key = arm > 0f ? "calibration.summary.avatar.armsLonger" : "calibration.summary.avatar.armsShorter";
        string phrase = string.Format(BasisLocalization.Get(key), Mathf.Abs(arm));
        string handling = facts.ArmsFitted
            ? BasisLocalization.Get("calibration.summary.avatar.adjusted")
            : BasisLocalization.Get("calibration.summary.avatar.beyondAdjustment");
        return $"{phrase} {handling}";
    }

    static string DescribeFit(in Facts facts)
    {
        switch (facts.FitStatus)
        {
            case BasisScaleFitStatus.EyeExact:
                return BasisLocalization.Get("calibration.summary.fit.exact");
            case BasisScaleFitStatus.Adjusted:
                return string.Format(BasisLocalization.Get("calibration.summary.fit.adjusted"), Mathf.Abs(facts.ScaleDeviation));
            case BasisScaleFitStatus.Compromised:
                return BasisLocalization.Get("calibration.summary.fit.compromised");
            default:
                return BasisLocalization.Get("calibration.summary.fit.none");
        }
    }
}
