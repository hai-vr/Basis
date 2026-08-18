namespace Basis.Benchmark.Measure;

/// <summary>How an A/B comparison came out.</summary>
public enum Verdict
{
    /// <summary>The candidate is better by more than the noise. Adopt it.</summary>
    Better,

    /// <summary>The candidate is worse by more than the noise. Reject it.</summary>
    Worse,

    /// <summary>
    /// The two are indistinguishable at this sample size. <b>Keep the incumbent.</b>
    /// </summary>
    NoDifference,

    /// <summary>Not enough windows survived to say anything. Reported, never acted on.</summary>
    Inconclusive,
}

public sealed record Comparison(
    Verdict Verdict,
    double BaselineMedian,
    double CandidateMedian,
    double RelativeChange,
    double PValue,
    int BaselineWindows,
    int CandidateWindows)
{
    public string Describe(string unit = "") =>
        Verdict switch
        {
            Verdict.Inconclusive => $"inconclusive ({BaselineWindows} vs {CandidateWindows} windows)",
            Verdict.NoDifference => $"no measurable difference (p={PValue:F3}, {RelativeChange:+0.0%;-0.0%;0.0%})",
            _ => $"{BaselineMedian:F3} -> {CandidateMedian:F3}{unit} ({RelativeChange:+0.0%;-0.0%}, p={PValue:F3})",
        };
}

/// <summary>
/// The statistics the A/B decisions run through.
///
/// <para>Non-parametric throughout, and that is a requirement rather than a preference. The
/// quantity being compared is produced by a control loop that oscillates instead of settling: at
/// 2000 players the slicing count was measured swinging between 4, 5 and 6 at a fixed load, with
/// CPU tracking it inversely across a 2.2x range. That distribution is multi-modal, so a mean is
/// not a summary of it and a t-test's assumptions are simply false. A median plus a rank test asks
/// the only question that survives the oscillation: across all the windows, did the candidate tend
/// to land higher than the baseline?</para>
///
/// <para>The other half of the discipline is the null result. <see cref="Verdict.NoDifference"/>
/// is a real outcome and the most common one, and it must keep the incumbent value. A tuner that
/// resolves every comparison into a winner will happily bake this run's noise into the config and
/// present it as a measurement.</para>
/// </summary>
public static class Stats
{
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double[] sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0) return 0;
        double[] sorted = values.OrderBy(v => v).ToArray();
        double rank = p * (sorted.Length - 1);
        int low = (int)Math.Floor(rank);
        int high = (int)Math.Ceiling(rank);
        return low == high ? sorted[low] : sorted[low] + (rank - low) * (sorted[high] - sorted[low]);
    }

    /// <summary>Interquartile range: the spread that matters when the distribution is not normal.</summary>
    public static double Iqr(IReadOnlyList<double> values) => Percentile(values, 0.75) - Percentile(values, 0.25);

    /// <summary>
    /// Minimum windows before a comparison is allowed to reach a verdict.
    ///
    /// <para>Five, because the oscillation this has to see through has a period of several windows.
    /// A run of three can land entirely inside one phase of it and produce a confident, repeatable,
    /// wrong answer — which is exactly how a 30-second measurement reported 7.8 cores for a
    /// workload that averages 10.9.</para>
    /// </summary>
    public const int MinimumWindows = 5;

    /// <summary>
    /// Two-sided Mann-Whitney U with a normal approximation and a tie correction.
    ///
    /// Exact for the purpose: with 5-20 windows per arm the approximation is close enough that the
    /// decision threshold, not the tail accuracy, is what governs.
    /// </summary>
    public static double MannWhitneyP(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        int n1 = a.Count, n2 = b.Count;
        if (n1 == 0 || n2 == 0) return 1;

        var combined = a.Select(v => (value: v, group: 0))
                        .Concat(b.Select(v => (value: v, group: 1)))
                        .OrderBy(t => t.value)
                        .ToArray();

        // Midranks for ties, and the tie sizes, which the variance correction needs.
        var ranks = new double[combined.Length];
        var tieGroups = new List<int>();
        for (int i = 0; i < combined.Length;)
        {
            int j = i;
            while (j + 1 < combined.Length && combined[j + 1].value == combined[i].value) j++;
            double midrank = (i + j + 2) / 2.0;
            for (int k = i; k <= j; k++) ranks[k] = midrank;
            if (j > i) tieGroups.Add(j - i + 1);
            i = j + 1;
        }

        double rankSumA = 0;
        for (int i = 0; i < combined.Length; i++)
            if (combined[i].group == 0) rankSumA += ranks[i];

        double u1 = rankSumA - n1 * (n1 + 1) / 2.0;
        double u2 = (double)n1 * n2 - u1;
        double u = Math.Min(u1, u2);

        double n = n1 + n2;
        double mean = n1 * (double)n2 / 2.0;
        double tieTerm = tieGroups.Sum(t => (double)t * t * t - t);
        double variance = n1 * (double)n2 / 12.0 * (n + 1 - tieTerm / (n * (n - 1)));
        if (variance <= 0) return 1;

        // Continuity correction; without it small samples read as more significant than they are.
        double z = (Math.Abs(u - mean) - 0.5) / Math.Sqrt(variance);
        return 2.0 * (1.0 - NormalCdf(z));
    }

    /// <summary>Abramowitz and Stegun 7.1.26, good to ~1e-7 — far tighter than the decision needs.</summary>
    private static double NormalCdf(double z)
    {
        double sign = z < 0 ? -1 : 1;
        double x = Math.Abs(z) / Math.Sqrt(2.0);
        double t = 1.0 / (1.0 + 0.3275911 * x);
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return 0.5 * (1.0 + sign * y);
    }

    /// <summary>
    /// Compares two arms and returns a verdict.
    /// </summary>
    /// <param name="higherIsBetter">
    /// Which direction counts as an improvement. Quality metrics are higher-is-better; cost
    /// metrics are not.
    /// </param>
    /// <param name="minimumEffect">
    /// Relative change below which a difference is not worth acting on even when it is
    /// statistically real. Guards against baking a reproducible 0.4% into the config and calling
    /// it tuning; with enough windows anything at all becomes significant.
    /// </param>
    public static Comparison Compare(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> candidate,
        bool higherIsBetter = true,
        double alpha = 0.05,
        double minimumEffect = 0.03)
    {
        double mb = Median(baseline), mc = Median(candidate);
        double relative = mb == 0 ? 0 : (mc - mb) / Math.Abs(mb);

        if (baseline.Count < MinimumWindows || candidate.Count < MinimumWindows)
            return new Comparison(Verdict.Inconclusive, mb, mc, relative, 1, baseline.Count, candidate.Count);

        double p = MannWhitneyP(baseline, candidate);

        if (p > alpha || Math.Abs(relative) < minimumEffect)
            return new Comparison(Verdict.NoDifference, mb, mc, relative, p, baseline.Count, candidate.Count);

        bool improved = higherIsBetter ? mc > mb : mc < mb;
        return new Comparison(improved ? Verdict.Better : Verdict.Worse, mb, mc, relative, p, baseline.Count, candidate.Count);
    }
}
