using System.Diagnostics;
using System.Text;

namespace Basis.Benchmark.Micro;

/// <summary>One worker-count rung of the scaling sweep.</summary>
public sealed record CoreScalingPoint(int Workers, double ItemsPerSecond, double CoresUsed)
{
    /// <summary>
    /// Work completed per CPU-second. This is the number that matters and the one a
    /// wall-clock-only benchmark cannot see.
    ///
    /// Throughput alone always looks flat-to-rising as workers are added, so a sweep judged on it
    /// concludes "more is never worse" and hands back the core count. Efficiency shows what the
    /// extra workers actually cost: on the pass this models it falls off a cliff past the knee,
    /// which is the same shape the server measured in production (32 workers = 11.0 cores,
    /// 16 = 8.6, 8 = 6.6, 4 = 6.4 at equal delivered work).
    /// </summary>
    public double ItemsPerCoreSecond => CoresUsed <= 0 ? 0 : ItemsPerSecond / CoresUsed;
}

public sealed class CoreBenchResult
{
    public required IReadOnlyList<CoreScalingPoint> Points { get; init; }
    public required int KneeWorkers { get; init; }
    public required double SingleCoreItemsPerSecond { get; init; }
    public required int Frequency { get; init; }

    /// <summary>
    /// True when the sweep found an actual discontinuity — a width where efficiency falls away
    /// markedly faster than the surrounding trend, usually the physical-core boundary.
    ///
    /// <para>False means efficiency decayed smoothly, and it is important that the report says so.
    /// On a smooth curve there is no width the machine picks out; the recommended one is a stated
    /// trade-off between cost and pass latency, and presenting a policy choice as a measurement is
    /// how a number nobody can defend ends up in a config file.</para>
    ///
    /// <para>⚠️ It also means the recommendation is <b>not reproducible between runs</b>. A
    /// threshold on a smooth curve sits between two rungs, and contention with whatever else is on
    /// the box costs a wide pool more than a narrow one — so a quiet machine flattens the curve and
    /// the pick moves out a rung. Observed directly on the development box: three consecutive runs
    /// chose 8, a fourth on an idler machine chose 16. Both are defensible, which is exactly why
    /// <see cref="WidestUsefulWorkers"/> exists and why the load sweep is what settles it.</para>
    /// </summary>
    public required bool HasSharpKnee { get; init; }

    /// <summary>
    /// The widest width still worth considering, where <see cref="KneeWorkers"/> is the narrowest.
    /// Equal when the curve has a real boundary; a range when it does not.
    /// </summary>
    public required int WidestUsefulWorkers { get; init; }

    /// <summary>True when the two ends disagree, so the answer is a range rather than a value.</summary>
    public bool IsRange => WidestUsefulWorkers > KneeWorkers;

    /// <summary>
    /// Efficiency at the knee against efficiency at full width. 1.0 means width is free on this
    /// box; 2.0 means running full-width burns twice the CPU for the same delivered work.
    /// </summary>
    public double WidthPenalty
    {
        get
        {
            double atKnee = Points.FirstOrDefault(p => p.Workers == KneeWorkers)?.ItemsPerCoreSecond ?? 0;
            double atMax = Points.Count > 0 ? Points[^1].ItemsPerCoreSecond : 0;
            return atMax <= 0 ? 1 : atKnee / atMax;
        }
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  Short parallel regions, back to back (models the {Frequency} Hz per-peer update pass):");
        sb.AppendLine("    workers      items/s     cores   items/core-s");
        foreach (CoreScalingPoint p in Points)
        {
            string mark = p.Workers == KneeWorkers ? "  <-- recommended width" : "";
            sb.AppendLine($"    {p.Workers,7}   {p.ItemsPerSecond,12:N0}   {p.CoresUsed,7:F2}   {p.ItemsPerCoreSecond,12:N0}{mark}");
        }
        sb.AppendLine(HasSharpKnee
            ? $"    Efficiency falls away sharply past {KneeWorkers} workers - a real boundary on this machine, " +
              "usually where logical cores stop being physical ones."
            : IsRange
                ? $"    Efficiency decays smoothly, so the machine does not single out a width: anything from " +
                  $"{KneeWorkers} to {WidestUsefulWorkers} is defensible and {KneeWorkers} is the conservative end. " +
                  "Expect this pick to move between runs - a busy box costs a wide pool more than a narrow one, " +
                  "so ambient load alone shifts it. The load sweep is what settles it."
                : $"    Efficiency decays smoothly, with no width the machine singles out. {KneeWorkers} is the " +
                  "widest still holding most of the narrow-pool efficiency - a stated trade-off, not a discovery.");
        sb.AppendLine($"    Running the full {Points.LastOrDefault()?.Workers ?? 0} costs {WidthPenalty:F2}x the CPU per unit of work.");
        return sb.ToString();
    }
}

/// <summary>
/// Finds how wide this machine's short, high-frequency parallel regions can usefully go.
///
/// <para>This models the one shape that dominated the server's CPU profile: a pass that runs
/// hundreds of times a second and does very little work per item. At that frequency the
/// scheduler's own machinery — worker wake-up, the task replicator, the GC poll points inside it,
/// the caller blocking on completion — costs more than the loop body, and adding workers makes it
/// worse rather than better. The production fix was a cap; the cap's correct value is a property
/// of the machine, which is what this measures.</para>
///
/// <para><b>Why this is worth measuring offline at all,</b> when the server can discover its own
/// ceiling at runtime: discovery needs load, needs the population to hold still while it probes,
/// and starts from a declared ceiling that may be badly wrong. This takes seconds, needs no
/// clients, and gives the load sweep a starting point near the answer instead of a guess to walk
/// away from.</para>
///
/// <para>The work unit is deliberately memory-touching rather than pure arithmetic. A per-peer
/// pass reads per-peer state scattered across the heap, so its cost is set by cache behaviour, and
/// a spin loop of float math would measure the ALU and report a knee that does not exist.</para>
/// </summary>
public static class CoreBench
{
    /// <summary>Items per parallel region. Roughly a mid-size instance's peer roster.</summary>
    private const int ItemsPerPass = 2048;

    /// <summary>
    /// Bytes of state per item, sized to defeat L1/L2 the way real per-peer state does.
    ///
    /// 4 KB puts the whole working set at 8 MB, which is past L2 on every machine this runs on and
    /// past L3 on most containers, so the per-item cost is set by memory rather than by the ALU.
    /// It also puts a pass at roughly a millisecond of real work, which is the range where the
    /// scheduler's per-pass overhead is a visible fraction rather than a rounding error — at a
    /// smaller size every width completes instantly and the sweep reports a flat line.
    /// </summary>
    private const int StateBytesPerItem = 4096;

    /// <summary>Nominal frequency of the real pass, recorded for the report. Not used to pace this.</summary>
    private const int DefaultFrequencyHz = 275;

    /// <summary>Shortest a rung may be timed for, whatever the caller asked.</summary>
    private static readonly TimeSpan MinimumRung = TimeSpan.FromSeconds(4);

    /// <summary>Timings per width, of which the most efficient is kept.</summary>
    private const int Repetitions = 2;

    public static CoreBenchResult Run(int cores, TimeSpan perRung, Action<string>? progress = null)
    {
        int frequency = DefaultFrequencyHz;
        var state = new double[(long)ItemsPerPass * StateBytesPerItem / sizeof(double)];
        for (int i = 0; i < state.Length; i++) state[i] = i * 0.5 + 1.0;

        // Touch every rung of the doubling ladder, plus the core count itself so full width is
        // always represented even when it is not a power of two.
        var widths = new List<int>();
        for (int w = 1; w <= cores; w *= 2) widths.Add(w);
        if (widths.Count == 0 || widths[^1] != cores) widths.Add(cores);

        // Let tiering settle before anything is timed. Without this the first rung measures the
        // JIT and is reported as the slowest width, which inverts the whole curve.
        RunPass(state, 1, DateTime.UtcNow.AddMilliseconds(400), frequency, out _);

        // Floored regardless of what the caller asked for. Below this the rung is short enough that
        // one preemption moves it several percent, and the knee detection - which compares
        // rung-to-rung differences of a few percent - starts reporting a different answer on every
        // run of the same machine.
        if (perRung < MinimumRung) perRung = MinimumRung;

        var points = new List<CoreScalingPoint>();
        foreach (int workers in widths)
        {
            progress?.Invoke($"    {workers} worker{(workers == 1 ? "" : "s")}...");

            // Best of two, on efficiency. The noise here is one-sided - a preemption or a
            // background process can only ever make a rung look worse than the machine is capable
            // of - so the better reading is the more honest one, and averaging would fold the
            // interference into the curve the knee is read off.
            CoreScalingPoint? best = null;
            for (int rep = 0; rep < Repetitions; rep++)
            {
                CoreScalingPoint point = MeasureWidth(state, workers, perRung, frequency);
                if (best == null || point.ItemsPerCoreSecond > best.ItemsPerCoreSecond) best = point;
            }
            points.Add(best!);
        }

        (int knee, int widest, bool sharp) = FindKnee(points);
        return new CoreBenchResult
        {
            Points = points,
            KneeWorkers = knee,
            WidestUsefulWorkers = widest,
            HasSharpKnee = sharp,
            SingleCoreItemsPerSecond = points.Count > 0 ? points[0].ItemsPerSecond : 0,
            Frequency = frequency,
        };
    }

    private static CoreScalingPoint MeasureWidth(double[] state, int workers, TimeSpan duration, int frequency)
    {
        Process self = Process.GetCurrentProcess();
        TimeSpan cpuBefore = self.TotalProcessorTime;
        long start = Stopwatch.GetTimestamp();

        RunPass(state, workers, DateTime.UtcNow + duration, frequency, out long items);

        double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        self.Refresh();
        double cpuSeconds = (self.TotalProcessorTime - cpuBefore).TotalSeconds;

        return new CoreScalingPoint(
            workers,
            seconds <= 0 ? 0 : items / seconds,
            seconds <= 0 ? 0 : cpuSeconds / seconds);
    }

    /// <summary>
    /// Efficiency below this share of the two-worker figure is not worth the width. Only used when
    /// no discontinuity was found, and reported as the policy it is.
    /// </summary>
    private const double SmoothDecayFloor = 0.80;

    /// <summary>
    /// A rung losing this much more efficiency than the typical rung is a real boundary rather
    /// than more of the same decay.
    /// </summary>
    private const double DiscontinuityFactor = 1.5;

    /// <summary>
    /// The widest width still worth running, and whether the machine actually singled it out.
    ///
    /// <para>Two things this must not do. It must not take the argmax of efficiency, which is
    /// always one worker — a single worker has no coordination cost at all, so it wins that
    /// comparison on every machine, and it is the correct answer to a question nobody asked. The
    /// pass has a latency budget as well as a cost: it is the floor under reliable delivery, and a
    /// pass too narrow for its population was measured peaking at 1204 ms, which times out
    /// handshakes several layers away with nothing in the logs to connect the two.</para>
    ///
    /// <para>And it must not invent a knee where the curve is smooth. The honest reading of a
    /// smooth decay is that there is no natural width and the choice is a policy, so that case is
    /// detected and reported rather than dressed up.</para>
    /// </summary>
    /// <summary>
    /// Efficiency above this share of the narrow-pool figure is still arguably worth the width.
    /// The gap between this and <see cref="SmoothDecayFloor"/> is the range the curve does not
    /// resolve, and reporting it is more honest than picking a point inside it.
    /// </summary>
    private const double SmoothDecayGenerousFloor = 0.70;

    private static (int Knee, int Widest, bool Sharp) FindKnee(IReadOnlyList<CoreScalingPoint> points)
    {
        if (points.Count < 4)
        {
            int only = points.FirstOrDefault()?.Workers ?? 1;
            return (only, only, false);
        }

        // ⚠️ The 1-worker rung is excluded from every comparison below, and kept only for the
        // table. Parallel.For with a degree of 1 does not schedule anything - it runs the body
        // inline on the calling thread - so that rung measures a different code path from every
        // other one, and the 1-to-2 step is a jump between mechanisms rather than a point on the
        // scaling curve. Including it inflated the typical-loss figure enough to move the detected
        // knee between runs of the same machine.
        int first = points[0].Workers == 1 ? 1 : 0;
        if (points.Count - first < 3) return (points[first].Workers, points[first].Workers, false);

        // Relative efficiency lost stepping from each rung to the next.
        var losses = new List<double>();
        for (int i = first + 1; i < points.Count; i++)
        {
            double previous = points[i - 1].ItemsPerCoreSecond;
            losses.Add(previous <= 0 ? 0 : 1.0 - points[i].ItemsPerCoreSecond / previous);
        }

        double[] ordered = losses.OrderBy(v => v).ToArray();
        double typical = ordered[ordered.Length / 2];

        // The first step that costs markedly more than the typical one. First rather than largest:
        // once past a real boundary every later rung is also bad, and the boundary is where the
        // width should stop.
        if (typical > 0)
        {
            for (int i = 0; i < losses.Count; i++)
            {
                if (losses[i] > typical * DiscontinuityFactor && losses[i] > 0.10)
                {
                    // A real boundary is a single answer, not a range: past it every wider rung is
                    // worse for a reason the machine itself supplied.
                    int at = points[first + i].Workers;
                    return (at, at, true);
                }
            }
        }

        double reference = points[first].ItemsPerCoreSecond;
        int knee = points[first].Workers;
        int widest = knee;
        if (reference > 0)
        {
            for (int i = first; i < points.Count; i++)
            {
                if (points[i].ItemsPerCoreSecond >= reference * SmoothDecayFloor) knee = points[i].Workers;
                if (points[i].ItemsPerCoreSecond >= reference * SmoothDecayGenerousFloor) widest = points[i].Workers;
            }
        }

        return (knee, Math.Max(widest, knee), false);
    }

    /// <summary>
    /// Runs passes back to back until the deadline.
    ///
    /// <para><b>No pacing loop, deliberately.</b> The obvious construction is to launch a pass on
    /// the real 275 Hz cadence and see what each width costs — and it does not work, because the
    /// gap between passes has to be waited out somehow. A spin burns a core and lands in the same
    /// CPU total being measured; a sleep overshoots badly enough on Windows to miss the cadence
    /// entirely. Either way the reading is of the waiting, not of the work.</para>
    ///
    /// <para>Back-to-back removes the question. What is wanted is cost per unit of work, and the
    /// per-pass overhead is charged per pass either way, so items per core-second comes out the
    /// same as it would under a cadence — while the idle time that was polluting it is simply not
    /// there.</para>
    /// </summary>
    private static void RunPass(double[] state, int workers, DateTime until, int frequency, out long items)
    {
        long completed = 0;
        var options = new ParallelOptions { MaxDegreeOfParallelism = workers };
        int perItem = state.Length / ItemsPerPass;

        while (DateTime.UtcNow < until)
        {
            Parallel.For(0, ItemsPerPass, options, i =>
            {
                int start = i * perItem;
                double acc = 0;
                for (int k = 0; k < perItem; k++) acc += state[start + k] * 1.000001;
                state[start] = acc / perItem;
            });

            completed += ItemsPerPass;
        }

        items = completed;
    }
}
