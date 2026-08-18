using Basis.Benchmark.Machine;

namespace Basis.Benchmark.Tuning;

/// <summary>Which resource runs out first, and therefore what to buy or change to get more.</summary>
public enum BindingConstraint
{
    /// <summary>Quality collapses before any resource is exhausted — the reduction system is the limit.</summary>
    Quality,
    Cpu,
    Memory,
    Bandwidth,

    /// <summary>Not enough rungs to say anything.</summary>
    Unknown,
}

public sealed record Ceiling(BindingConstraint Constraint, int Players, string Explanation, bool Extrapolated)
{
    /// <summary>
    /// True when the fit put this ceiling so far above anything measured that the number is not
    /// worth quoting — only the fact that this resource is not what limits the box.
    /// </summary>
    public bool BeyondUsefulRange { get; init; }

    /// <summary>
    /// True when this is "at least N" rather than "N". A ladder that ran out of rungs while
    /// everything still worked has measured a floor, not a ceiling, and the two must never be
    /// printed the same way.
    /// </summary>
    public bool IsLowerBound { get; init; }

    /// <summary>How to write the figure: a number, or an honest "further than we can say".</summary>
    public string PlayersText => BeyondUsefulRange
        ? $"over {Players:N0}"
        : Players.ToString("N0");
}

/// <summary>
/// What this machine can be expected to do, derived from the ladder rather than asserted.
///
/// <para>The ladder answers "how many players did it serve well". That is the important number and
/// it is not the whole answer, because it does not say <em>why</em> it stopped — and the why is what
/// decides whether the fix is a bigger box, more memory, a faster link, or nothing at all. Each
/// resource is fitted separately against population and solved for where it runs out, so the
/// summary can name the one that binds first instead of leaving an operator to guess.</para>
///
/// <para><b>Every cost here is fitted with a quadratic term, and that is not curve-fitting for its
/// own sake.</b> This workload is genuinely O(N²) in places: every player is tracked against every
/// other, so the reduction system's per-receiver tracking array alone is 32 bytes × N per player,
/// and egress is a fan-out over pairs. A linear fit through two rungs would understate the ceiling
/// badly at the top and is the reason a small measurement cannot simply be multiplied up.</para>
///
/// <para><b>Anything beyond the highest rung actually run is flagged as extrapolated,</b> and the
/// summary says so. A model fitted to 1000 players and solved out to 12,000 is arithmetic, not a
/// measurement, and the difference matters to somebody deciding what hardware to buy.</para>
/// </summary>
public sealed class CapabilityModel
{
    private readonly Fit _cores;
    private readonly Fit _memoryMb;
    private readonly Fit _egressMbps;

    public IReadOnlyList<LadderRung> Rungs { get; }
    public MachineProfile Machine { get; }
    public NetworkLink? Link { get; }

    /// <summary>Highest population actually measured. Past this everything is model, not data.</summary>
    public int MeasuredTo { get; }

    /// <summary>Population the ladder found still delivering essentially everything it produced.</summary>
    public int FullQualityPlayers { get; }

    /// <summary>
    /// False when the ladder never found a failing rung, which makes
    /// <see cref="FullQualityPlayers"/> a lower bound rather than a ceiling.
    /// </summary>
    public bool KneeFound { get; }

    public CapabilityModel(IReadOnlyList<LadderRung> rungs, MachineProfile machine, NetworkLink? link,
        int fullQualityPlayers, bool kneeFound = true)
    {
        Rungs = rungs;
        Machine = machine;
        Link = link;
        FullQualityPlayers = fullQualityPlayers;
        KneeFound = kneeFound;
        MeasuredTo = rungs.Count == 0 ? 0 : rungs.Max(r => r.Players);

        // Rungs whose CPU could not be read are excluded outright rather than fitted as zero.
        _cores = Fit.Through(rungs.Where(r => r.HasCores).Select(r => ((double)r.Players, r.Cores)));
        CoresMeasurable = rungs.Count(r => r.HasCores) >= 2;
        _memoryMb = Fit.Through(rungs.Select(r => ((double)r.Players, r.CommittedMb)));
        _egressMbps = Fit.Through(rungs.Select(r => ((double)r.Players, r.MegabytesPerSecond * 8.0)));
    }

    public bool HasData => Rungs.Count > 0;

    // ── per-player costs, quoted at a population ────────────────────────────────────────

    public double CoresAt(int players) => Math.Max(0, _cores.At(players));
    public double MemoryMbAt(int players) => Math.Max(0, _memoryMb.At(players));
    public double EgressMbpsAt(int players) => Math.Max(0, _egressMbps.At(players));

    public double MemoryMbPerPlayer(int players) => players <= 0 ? 0 : MemoryMbAt(players) / players;
    public double EgressKbpsPerPlayer(int players) => players <= 0 ? 0 : EgressMbpsAt(players) * 1000.0 / players;

    // ── ceilings ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cores are left in reserve rather than run to the wall. A server at 100% of its cores has no
    /// headroom for a join burst, a GC pause or the box's own housekeeping, and the first of those
    /// to arrive turns a busy instance into a failing one.
    /// </summary>
    private const double CpuHeadroom = 0.85;

    /// <summary>
    /// Memory is held further back than CPU. Running out of CPU degrades; running out of memory
    /// ends the process, and the per-player state is genuinely live so there is nothing for the
    /// collector to give back under pressure.
    /// </summary>
    private const double MemoryHeadroom = 0.75;

    /// <summary>
    /// A link run near its rated speed queues rather than drops, which shows up as latency long
    /// before it shows up as loss. Sizing to 70% keeps the burst headroom a crowd needs.
    /// </summary>
    private const double LinkHeadroom = 0.70;

    /// <summary>False when too few rungs produced a usable CPU reading to fit anything.</summary>
    public bool CoresMeasurable { get; }

    public Ceiling? CpuCeiling()
    {
        if (!CoresMeasurable) return null;

        double budget = Machine.LogicalCores * CpuHeadroom;
        int players = _cores.SolveFor(budget);
        if (players <= 0) return null;

        return new Ceiling(BindingConstraint.Cpu, players,
            $"{budget:F1} of {Machine.LogicalCores} cores ({CpuHeadroom:P0}, leaving headroom for join bursts and GC)",
            players > MeasuredTo);
    }

    public Ceiling MemoryCeiling()
    {
        double budgetMb = Machine.TotalMemoryBytes / 1048576.0 * MemoryHeadroom;
        int players = _memoryMb.SolveFor(budgetMb);
        return new Ceiling(BindingConstraint.Memory, players,
            $"{budgetMb / 1024:F1} GB of {Machine.TotalMemoryGb:F1} GB ({MemoryHeadroom:P0})",
            players > MeasuredTo);
    }

    public Ceiling? BandwidthCeiling()
    {
        if (Link is not { SpeedMbps: > 0 }) return null;
        double budgetMbps = Link.SpeedMbps * LinkHeadroom;
        int players = _egressMbps.SolveFor(budgetMbps);
        return new Ceiling(BindingConstraint.Bandwidth, players,
            $"{NetworkLink.FormatSpeed((long)budgetMbps)} of the link's {NetworkLink.FormatSpeed(Link.SpeedMbps)} ({LinkHeadroom:P0})",
            players > MeasuredTo);
    }

    public Ceiling QualityCeiling() => new(BindingConstraint.Quality, FullQualityPlayers,
        KneeFound
            ? "measured: the largest population still delivering everything it produced, and the next rung did not"
            : "measured, but a LOWER BOUND - this population delivered everything and the ladder stopped there "
              + "rather than finding a limit",
        false) { IsLowerBound = !KneeFound };

    /// <summary>
    /// How far past the measured range a fitted ceiling is still worth quoting as a number.
    ///
    /// <para>Beyond this it is reported as "over N" instead. A curve fitted to three populations
    /// and solved two hundred times past the largest of them is not an estimate of anything — the
    /// quadratic term is within the noise at that distance, so the answer is set by measurement
    /// error rather than by the machine. The useful content in such a result is only ever "this
    /// resource is not what limits you", and printing a precise-looking 201,845 states something
    /// far stronger than the data supports.</para>
    /// </summary>
    private const int ExtrapolationFactor = 10;

    private Ceiling Bound(Ceiling ceiling)
    {
        int limit = Math.Max(1, MeasuredTo) * ExtrapolationFactor;
        return ceiling.Players <= limit
            ? ceiling
            : ceiling with { Players = limit, BeyondUsefulRange = true };
    }

    /// <summary>Every ceiling that could be computed, lowest first.</summary>
    public IReadOnlyList<Ceiling> AllCeilings()
    {
        var ceilings = new List<Ceiling>();
        if (!HasData) return ceilings;

        if (FullQualityPlayers > 0) ceilings.Add(QualityCeiling());
        if (CpuCeiling() is { } cpu) ceilings.Add(Bound(cpu));
        ceilings.Add(Bound(MemoryCeiling()));
        if (BandwidthCeiling() is { } bandwidth) ceilings.Add(Bound(bandwidth));

        return ceilings.Where(c => c.Players > 0).OrderBy(c => c.Players).ToList();
    }

    /// <summary>
    /// What actually limits this box.
    ///
    /// <para>Quality is listed alongside the physical resources on purpose, and it usually wins.
    /// The reduction system is designed to degrade rather than fail, so on a healthy machine the
    /// server gives up delivering at full rate long before it exhausts a core, a byte or a bit —
    /// and "the software chose to shed" is a completely different answer from "the box ran out",
    /// with completely different remedies.</para>
    /// </summary>
    public Ceiling Binding() => AllCeilings().FirstOrDefault()
        ?? new Ceiling(BindingConstraint.Unknown, 0, "no rung completed", false);

    /// <summary>
    /// Least-squares quadratic through the rungs, with the physical shape imposed.
    ///
    /// Coefficients are clamped non-negative because none of these costs can fall as population
    /// rises. Unclamped, three noisy points routinely produce a downward-curving fit that solves to
    /// a nonsensical ceiling — an artefact of the noise, presented with total confidence.
    /// </summary>
    private sealed class Fit
    {
        private readonly double _c0, _c1, _c2;
        private readonly int _points;

        private Fit(double c0, double c1, double c2, int points)
        {
            _c0 = c0; _c1 = c1; _c2 = c2; _points = points;
        }

        public double At(double n) => _c0 + _c1 * n + _c2 * n * n;

        /// <summary>Population at which this cost reaches <paramref name="budget"/>, or 0.</summary>
        public int SolveFor(double budget)
        {
            if (_points == 0) return 0;
            if (At(0) >= budget) return 0;

            // Bisection rather than the quadratic formula: the fit may be linear (c2 == 0) or
            // constant, and the closed form has to special-case each of those anyway. The function
            // is monotonic by construction, so bisection cannot pick a wrong root.
            // A cost that never reaches the budget inside any plausible population means the fit
            // says this resource is not a limit. Returning the search cap would dress that up as a
            // one-million-player ceiling, which is the kind of number that ends up in a slide.
            double low = 0, high = 1;
            while (At(high) < budget && high < 1_000_000) high *= 2;
            if (high >= 1_000_000) return 0;

            for (int i = 0; i < 60; i++)
            {
                double mid = (low + high) / 2;
                if (At(mid) < budget) low = mid; else high = mid;
            }
            return (int)Math.Round(low);
        }

        public static Fit Through(IEnumerable<(double N, double Y)> samples)
        {
            var points = samples.Where(p => p.N > 0).ToArray();
            if (points.Length == 0) return new Fit(0, 0, 0, 0);
            if (points.Length == 1) return new Fit(0, points[0].Y / points[0].N, 0, 1);

            if (points.Length == 2)
            {
                // Straight line, and no pretence of curvature from two points.
                (double n1, double y1) = points[0];
                (double n2, double y2) = points[1];
                double slope = n2 == n1 ? 0 : (y2 - y1) / (n2 - n1);
                if (slope < 0) slope = 0;
                return new Fit(Math.Max(0, y1 - slope * n1), slope, 0, 2);
            }

            // Normal equations for y = c0 + c1*n + c2*n^2, solved by Gaussian elimination on a
            // 3x3. Scaled by the largest population first: raw values run to 4000^4 = 2.6e14 and
            // the unscaled matrix loses most of its precision to that spread.
            double scale = points.Max(p => p.N);
            var matrix = new double[3, 4];
            foreach ((double rawN, double y) in points)
            {
                double n = rawN / scale;
                double[] basis = { 1, n, n * n };
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++) matrix[r, c] += basis[r] * basis[c];
                    matrix[r, 3] += basis[r] * y;
                }
            }

            double[]? solution = Solve3(matrix);
            if (solution == null)
            {
                (double nLast, double yLast) = points[^1];
                return new Fit(0, yLast / nLast, 0, points.Length);
            }

            double k0 = solution[0], k1 = solution[1] / scale, k2 = solution[2] / (scale * scale);
            return new Fit(Math.Max(0, k0), Math.Max(0, k1), Math.Max(0, k2), points.Length);
        }

        private static double[]? Solve3(double[,] m)
        {
            for (int col = 0; col < 3; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < 3; r++)
                    if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;

                if (Math.Abs(m[pivot, col]) < 1e-12) return null;

                if (pivot != col)
                    for (int c = 0; c < 4; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);

                for (int r = 0; r < 3; r++)
                {
                    if (r == col) continue;
                    double factor = m[r, col] / m[col, col];
                    for (int c = col; c < 4; c++) m[r, c] -= factor * m[col, c];
                }
            }

            return new[] { m[0, 3] / m[0, 0], m[1, 3] / m[1, 1], m[2, 3] / m[2, 2] };
        }
    }
}
