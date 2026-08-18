using Basis.Benchmark.Harness;

namespace Basis.Benchmark.Measure;

/// <summary>
/// One closed measurement window: everything that happened between two health samples, reduced to
/// rates.
///
/// <para><b>Every rate here is per second of wall time, never per tick.</b> That is not a style
/// choice. The reduction system adapts its tick rate under load, so a cheaper tick simply means
/// more ticks — a change that halves the work per tick while doubling the tick rate has done
/// nothing, and reads as a 50% improvement in any per-tick figure. This has already produced a
/// false result once in this codebase: an optimisation "improved" the update phase from 4.556 to
/// 3.340 ms/tick while performing identical work per second. Per-tick numbers are kept only for
/// the report, and never compared across configurations.</para>
/// </summary>
public sealed record MeasurementWindow
{
    public required double DurationSeconds { get; init; }
    public required int Players { get; init; }

    /// <summary>Server process CPU, in cores, averaged across the window.</summary>
    public required double ServerCores { get; init; }

    /// <summary>Load-client process CPU. Reported so a run where the CLIENT is the bottleneck is visible.</summary>
    public required double ClientCores { get; init; }

    public required double MegabytesOutPerSecond { get; init; }
    public required double DatagramsOutPerSecond { get; init; }
    public required double DropsPerSecond { get; init; }
    public required double VoiceDropsPerSecond { get; init; }
    public required double SendsPerSecond { get; init; }
    public required double PairHzBeforeLoss { get; init; }

    public required double TickMs { get; init; }

    /// <summary>Send workers the pool actually ran at, averaged across the window.</summary>
    public required double SendWorkers { get; init; }

    /// <summary>Workers the core allocator was granting the send pass, averaged.</summary>
    public required double SendWorkerCap { get; init; }

    /// <summary>
    /// What the tick's non-send phases cost, as a fraction of the period. What
    /// BSRSendPhaseBudgetPercent has to leave room for; see HealthSample.NonSendShareOfPeriod for
    /// why this and not the send pass's own duty is the thing to fit against.
    /// </summary>
    public required double NonSendShareOfPeriod { get; init; }

    /// <summary>The budget share the server was running with while this window was measured.</summary>
    public required double SendBudgetPercent { get; init; }
    public required double OverrunRatio { get; init; }
    public required double SliceCount { get; init; }
    public required double ShedTier { get; init; }
    public required long IntervalMs { get; init; }

    public required double CommittedMb { get; init; }
    public required double FragmentedMb { get; init; }
    public required double GcPausePercent { get; init; }
    public required double BundleDeflateMsPerTick { get; init; }
    public required double BundleRatio { get; init; }

    /// <summary>Kernel UDP receive-buffer drops per second, or -1 where the counter is unreadable.</summary>
    public required double KernelReceiveDropsPerSecond { get; init; }

    /// <summary>
    /// Share of what the reduction system tried to send that the transport actually accepted.
    ///
    /// Drops happen downstream of the send call, at the per-peer queue bound, so everything
    /// dropped was also counted as attempted. A ratio below about 0.95 means the operating point
    /// is past capacity however comfortable the CPU looks.
    /// </summary>
    public double DeliveryRatio
    {
        get
        {
            double attempted = SendsPerSecond;
            if (attempted <= 0) return DropsPerSecond > 0 ? 0 : 1;
            double ratio = 1.0 - DropsPerSecond / attempted;
            return ratio < 0 ? 0 : ratio > 1 ? 1 : ratio;
        }
    }

    /// <summary>
    /// <b>The quality number.</b> Receiver visit rate after losses — an upper bound on how often a
    /// player learns anything about any one other player.
    ///
    /// <para>Everything this tool decides is decided on this. It is the product of the three
    /// levers a struggling server pulls, so no single one of them can be gamed: lengthening the
    /// tick lowers it, slicing the roster lowers it, and shedding at the queue lowers it. CPU
    /// appears nowhere in it, deliberately — CPU falls when a server starts shedding, so any
    /// objective containing it rewards the failure.</para>
    ///
    /// <para>A bound rather than the true rate, because the per-pair interval also widens with
    /// distance. That is why it is only ever compared between arms at the same population and
    /// spawn radius, where the distance distribution is identical and cancels, and never quoted as
    /// an absolute figure.</para>
    /// </summary>
    public double DeliveredPairHz => PairHzBeforeLoss * DeliveryRatio;

    /// <summary>
    /// Delivered quality per core, at this population. The cost side of the same coin, used to
    /// separate configurations that deliver the same thing for different money.
    /// </summary>
    public double QualityPerCore => ServerCores <= 0 ? 0 : DeliveredPairHz * Players / ServerCores;

    /// <summary>
    /// Builds a window from its two bounding samples and the CPU measured between them.
    /// </summary>
    public static MeasurementWindow Between(
        HealthSample start,
        HealthSample end,
        IReadOnlyList<HealthSample> inner,
        double serverCores,
        double clientCores,
        double kernelDropsPerSecond)
    {
        double seconds = (end.SampledUtc - start.SampledUtc).TotalSeconds;
        if (seconds <= 0) seconds = 1;

        double Rate(long a, long b) => (b - a) / seconds;

        // Instantaneous fields are averaged across every sample inside the window rather than read
        // off either edge. The slicing controller oscillates - measured swinging 4/5/6 at a fixed
        // load, with CPU tracking it inversely by a factor of 2.2 - so an edge reading records
        // wherever the loop happened to be at that instant, not what the window was like.
        IReadOnlyList<HealthSample> all = inner.Count > 0 ? inner : new[] { start, end };

        return new MeasurementWindow
        {
            DurationSeconds = seconds,
            Players = (int)Math.Round(all.Average(s => (double)s.Visitors)),
            ServerCores = serverCores,
            ClientCores = clientCores,
            MegabytesOutPerSecond = Rate(start.BytesSent, end.BytesSent) / 1_000_000.0,
            DatagramsOutPerSecond = Rate(start.PacketsSent, end.PacketsSent),
            DropsPerSecond = Rate(start.DroppedUnreliable, end.DroppedUnreliable),
            VoiceDropsPerSecond = Rate(start.DroppedVoice, end.DroppedVoice),
            SendsPerSecond = all.Where(s => s.SendsPerSecond > 0).DefaultIfEmpty(end).Average(s => s.SendsPerSecond),
            PairHzBeforeLoss = all.Where(s => s.PairHzBeforeLoss > 0).DefaultIfEmpty(end).Average(s => s.PairHzBeforeLoss),
            TickMs = all.Average(s => s.TickMs),
            SendWorkers = all.Average(s => (double)s.SendWorkers),
            SendWorkerCap = all.Average(s => (double)s.SendWorkerCap),
            // Averaged only over samples that carried the fields, so a build predating them reads
            // as 0 - "not reported" - rather than as a tick with no non-send phases in it.
            NonSendShareOfPeriod = all.Where(s => s.SendBudgetPercent > 0)
                .Select(s => s.NonSendShareOfPeriod).DefaultIfEmpty(0).Average(),
            SendBudgetPercent = all.Where(s => s.SendBudgetPercent > 0)
                .Select(s => (double)s.SendBudgetPercent).DefaultIfEmpty(0).Average(),
            OverrunRatio = all.Average(s => s.OverrunRatio),
            SliceCount = all.Average(s => (double)s.SliceCount),
            ShedTier = all.Average(s => (double)s.ShedTier),
            IntervalMs = end.IntervalMs,
            CommittedMb = all.Max(s => s.CommittedMb),
            FragmentedMb = all.Max(s => s.FragmentedMb),
            GcPausePercent = all.Average(s => s.GcPauseTimePercent),
            BundleDeflateMsPerTick = all.Average(s => s.BundleDeflateMsPerTick),
            BundleRatio = all.Where(s => s.BundleRatio > 0).DefaultIfEmpty(end).Average(s => s.BundleRatio),
            KernelReceiveDropsPerSecond = kernelDropsPerSecond,
        };
    }
}
