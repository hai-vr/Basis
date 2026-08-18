using System.Text;
using Basis.Benchmark.Harness;

namespace Basis.Benchmark.Tuning;

public sealed record LadderRung(int Players, RunResult Result)
{
    public double DeliveryRatio => Result.Median(w => w.DeliveryRatio);
    public double DeliveredPairHz => Result.Median(w => w.DeliveredPairHz);
    public double Cores => Result.Median(w => w.ServerCores);
    public double MegabytesPerSecond => Result.Median(w => w.MegabytesOutPerSecond);
    public double SliceCount => Result.Median(w => w.SliceCount);
    public double CommittedMb => Result.Median(w => w.CommittedMb);
    public double KernelDropsPerSecond => Result.Median(w => w.KernelReceiveDropsPerSecond);
}

public sealed class CapacityResult
{
    public required IReadOnlyList<LadderRung> Rungs { get; init; }

    /// <summary>
    /// The largest population still delivering essentially everything it produced. This is the
    /// number an operator should set their player cap near, and the population the settings are
    /// fitted at.
    /// </summary>
    public required int FullQualityPlayers { get; init; }

    /// <summary>The largest population that stayed up at all, whatever it was delivering.</summary>
    public required int MaxStablePlayers { get; init; }

    /// <summary>What limited the box at the knee, as far as the measurements can tell.</summary>
    public required string Bottleneck { get; init; }

    /// <summary>
    /// Whether the design point leaves the server with nothing to do — in which case no setting
    /// sweep run there can tell anything apart.
    ///
    /// <para>This is the failure mode a tuner is least likely to notice, because it does not look
    /// like a failure. Under a population the box handles comfortably the reduction system holds
    /// its fastest tick, slices once, sheds nothing, and delivers a number with no variance at all
    /// — so every arm returns exactly the same figure, every comparison honestly reports no
    /// difference, and the run concludes that none of the settings matter. They do; there was
    /// simply no pressure for any of them to relieve. Settings only become distinguishable once
    /// something is scarce.</para>
    /// </summary>
    public bool DesignPointIsIdle(int cores)
    {
        LadderRung? rung = Rungs.FirstOrDefault(r => r.Players == FullQualityPlayers);
        if (rung == null) return false;
        return rung.SliceCount <= 1.01 && rung.DeliveryRatio >= 0.999 && rung.Cores < cores * 0.25;
    }

    /// <summary>The population to sweep settings at, and why it may not be usable.</summary>
    public string? IdleWarning(int cores) => !DesignPointIsIdle(cores) ? null :
        $"At {FullQualityPlayers:N0} players this machine is barely working - " +
        $"{Rungs.First(r => r.Players == FullQualityPlayers).Cores:F2} of {cores} cores, no slicing, nothing " +
        "dropped. Nothing is scarce, so no setting can measurably change the outcome and the sweep will " +
        "correctly but uselessly report that every one of them makes no difference. Climb to a population " +
        "that actually loads the box before sweeping.";

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine("   players    cores     MB/s   Hz/pair   delivery   slice   committed");
        foreach (LadderRung r in Rungs)
        {
            string mark = r.Players == FullQualityPlayers ? "  <-- full quality" : "";
            sb.AppendLine($"   {r.Players,7}   {r.Cores,6:F2}   {r.MegabytesPerSecond,6:N0}   {r.DeliveredPairHz,7:F2}   {r.DeliveryRatio,8:P1}   {r.SliceCount,5:F1}   {r.CommittedMb,7:N0} MB{mark}");
        }
        sb.AppendLine();
        sb.AppendLine($"   Full-quality ceiling : {FullQualityPlayers:N0} players");
        sb.AppendLine($"   Stays up to          : {MaxStablePlayers:N0} players (degraded)");
        sb.AppendLine($"   Limited by           : {Bottleneck}");
        return sb.ToString();
    }
}

/// <summary>
/// Finds where this box stops delivering, by climbing until it does.
///
/// <para><b>This has to run before any setting is swept,</b> because "the best value" is not a
/// property of a setting on its own — it is a property of the setting at a population. A worker
/// cap that is right at 500 players is wrong at 4000, and a sweep run at an arbitrary population
/// fits the settings to that arbitrary point.</para>
///
/// <para><b>The knee is read from delivery, not from CPU,</b> and the difference is the whole
/// reason this class is careful. CPU is not monotonic in load here: past capacity the server sheds
/// avatar updates at the queue bound, shedding is cheaper than sending, and so CPU comes back
/// <em>down</em> while quality collapses. Measured on one box, drops of enqueued sends ran roughly
/// 0% at 500 players, 0.2% at 1000 and 30% at 2000 — a very sharp knee — while CPU at 2000 read
/// lower than at 1000. A ladder that stops when CPU flattens would have called that box a
/// 2000-player machine.</para>
/// </summary>
public static class CapacityLadder
{
    /// <summary>Delivery below this is not a slower instance, it is a different one.</summary>
    private const double FullQualityDeliveryFloor = 0.98;

    /// <summary>Delivery below this is a failed rung; there is nothing to learn above it.</summary>
    private const double AbandonBelow = 0.60;

    public static CapacityResult Run(
        LoadRunner runner,
        RunOptions template,
        IReadOnlyList<int> populations,
        Action<string> log,
        CancellationToken cancel)
    {
        var rungs = new List<LadderRung>();

        foreach (int players in populations)
        {
            if (cancel.IsCancellationRequested) break;

            log($"\n  Ladder rung: {players:N0} players");
            var options = new RunOptions
            {
                ServerDirectory = template.ServerDirectory,
                LoadClientDirectory = template.LoadClientDirectory,
                Players = players,
                Warmup = template.Warmup,
                WindowLength = template.WindowLength,
                Windows = template.Windows,
                ConnectTimeout = template.ConnectTimeout,
                Settings = template.Settings,
                HealthHost = template.HealthHost,
                HealthPort = template.HealthPort,
                HealthPath = template.HealthPath,
                Label = $"{players}p",
            };

            RunResult result = runner.Run(options, cancel);
            var rung = new LadderRung(players, result);

            if (!result.Completed)
            {
                log($"  {players:N0} players: {result.Failure}. Stopping the climb here.");
                break;
            }

            rungs.Add(rung);
            log($"  {players:N0} players: {rung.DeliveredPairHz:F2} Hz/pair, {rung.Cores:F2} cores, delivery {rung.DeliveryRatio:P1}");

            if (rung.DeliveryRatio < AbandonBelow)
            {
                log($"  Delivery has collapsed to {rung.DeliveryRatio:P0}; higher rungs would only measure how it sheds.");
                break;
            }
        }

        return new CapacityResult
        {
            Rungs = rungs,
            FullQualityPlayers = rungs.LastOrDefault(r => r.DeliveryRatio >= FullQualityDeliveryFloor)?.Players
                                 ?? rungs.FirstOrDefault()?.Players ?? 0,
            MaxStablePlayers = rungs.LastOrDefault()?.Players ?? 0,
            Bottleneck = DiagnoseBottleneck(rungs),
        };
    }

    /// <summary>
    /// Names what ran out first at the knee.
    ///
    /// <para>Order matters here, and it is not the order of severity — it is the order of how badly
    /// each cause is misread when something else is blamed. Kernel receive drops are checked first
    /// because they present as no symptom at all: the receive thread is pinned to one core whether
    /// it is keeping up or not, so the machine can sit at a small fraction of its cores while the
    /// kernel discards inbound datagrams, and every CPU-side reading agrees that there is plenty of
    /// headroom. Diagnosed as "CPU", that box gets a bigger CPU and behaves identically.</para>
    /// </summary>
    private static string DiagnoseBottleneck(IReadOnlyList<LadderRung> rungs)
    {
        LadderRung? last = rungs.LastOrDefault();
        if (last == null) return "nothing measured";

        if (last.KernelDropsPerSecond > 100)
            return $"the kernel is discarding {last.KernelDropsPerSecond:N0} inbound datagrams/s - the receive path, " +
                   "not the CPU. Raise MultiSocketCount, and check net.core.rmem_max";

        double coreShare = last.Cores / Environment.ProcessorCount;
        if (coreShare > 0.75)
            return $"CPU - {last.Cores:F1} of {Environment.ProcessorCount} cores at the knee";

        if (last.SliceCount > 8)
            return $"the reduction system is slicing {last.SliceCount:F0} ways at {coreShare:P0} of the machine's cores. " +
                   "Work is not reaching the idle cores: check the send-worker ceiling, which is derived from socket count";

        if (last.Result.Median(w => w.GcPausePercent) > 8)
            return $"garbage collection - {last.Result.Median(w => w.GcPausePercent):F1}% of wall time paused";

        return $"undetermined; {last.Cores:F1} cores used of {Environment.ProcessorCount}, slice {last.SliceCount:F1}";
    }

    /// <summary>
    /// The populations to climb. Doubling from a small base, so the ladder costs
    /// log(capacity) runs rather than capacity/step runs, and the knee is bracketed rather than
    /// hunted for.
    /// </summary>
    public static IReadOnlyList<int> DefaultPopulations(int maximum)
    {
        var values = new List<int>();
        for (int p = 250; p <= maximum; p *= 2) values.Add(p);
        if (values.Count == 0) values.Add(maximum);
        return values;
    }
}
