using System.Text;
using Basis.Benchmark.Harness;

namespace Basis.Benchmark.Tuning;

public sealed record LadderRung(int Players, RunResult Result)
{
    public double DeliveryRatio => Result.Median(w => w.DeliveryRatio);
    public double DeliveredPairHz => Result.Median(w => w.DeliveredPairHz);
    /// <summary>
    /// Server CPU, or NaN when no window in this rung could read it. Unreadable samples are
    /// excluded rather than counted as zero — see ProcessCpu.SampleCores for why that distinction
    /// is worth carrying this far.
    /// </summary>
    public double Cores
    {
        get
        {
            var usable = Result.Windows.Select(w => w.ServerCores).Where(c => !double.IsNaN(c)).ToList();
            return usable.Count == 0 ? double.NaN : Measure.Stats.Median(usable);
        }
    }

    /// <summary>True when this rung's CPU figure is real and can be fitted against.</summary>
    public bool HasCores => !double.IsNaN(Cores);

    /// <summary>Renders cores, or "?" where the reading failed. Never a plausible-looking zero.</summary>
    public static string Fmt(double cores) => double.IsNaN(cores) ? "?" : cores.ToString("F2");
    public double MegabytesPerSecond => Result.Median(w => w.MegabytesOutPerSecond);
    public double SliceCount => Result.Median(w => w.SliceCount);
    public double CommittedMb => Result.Median(w => w.CommittedMb);
    public double KernelDropsPerSecond => Result.Median(w => w.KernelReceiveDropsPerSecond);

    /// <summary>Load-client CPU, which is not part of the score but decides whether it can be trusted.</summary>
    public double ClientCores => Result.Median(w => w.ClientCores);

    /// <summary>
    /// True when the harness and the server together left little of the machine spare.
    ///
    /// <para>The client's CPU is excluded from the server's figure, but not from the machine: past
    /// a point the two are fighting for cores, cache and memory bandwidth, and the server's number
    /// stops describing the server. It also raises a worse possibility - that the CLIENT ran out
    /// first, so the server looks comfortable only because nothing was pushing it hard enough.</para>
    /// </summary>
    public bool Contended(int machineCores) =>
        HasCores && machineCores > 0 && (Cores + ClientCores) > machineCores * 0.70;
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
    /// True when a rung actually failed to deliver, so <see cref="FullQualityPlayers"/> is a real
    /// ceiling. False when the climb simply ran out of rungs with everything still working — in
    /// which case that figure is a LOWER BOUND and must be reported as "at least", never as a
    /// capacity. Reporting the two the same way is how "comfortably 1,000" ends up describing a box
    /// that was at 19% of its cores and had never been pushed.
    /// </summary>
    public required bool KneeFound { get; init; }

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
        return rung.SliceCount <= 1.01 && rung.DeliveryRatio >= 0.999 && rung.HasCores && rung.Cores < cores * 0.25;
    }

    /// <summary>
    /// Rungs where the harness and the server together took most of the machine, so the server's
    /// own figure is no longer only about the server.
    /// </summary>
    public string? ContentionWarning(int cores)
    {
        var contended = Rungs.Where(r => r.Contended(cores)).ToList();
        if (contended.Count == 0) return null;

        LadderRung worst = contended.OrderByDescending(r => r.Cores + r.ClientCores).First();
        return
            $"At {worst.Players:N0} players the server took {worst.Cores:F1} cores and the load client " +
            $"{worst.ClientCores:F1}, which is {(worst.Cores + worst.ClientCores) / cores:P0} of this machine. " +
            "The client's CPU is excluded from the server's score, but the contention is not: past this point " +
            "the two compete for cores, cache and memory bandwidth, and it becomes possible that the CLIENT ran " +
            "out first - which would make the server look comfortable only because nothing was pushing it. Run " +
            "the load clients on another machine before trusting figures at this population.";
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
        sb.AppendLine("   players    cores   client     MB/s   Hz/pair   delivery   slice   committed");
        foreach (LadderRung r in Rungs)
        {
            string mark = r.Players == FullQualityPlayers ? "  <-- full quality" : "";
            sb.AppendLine($"   {r.Players,7}   {LadderRung.Fmt(r.Cores),6}   {r.ClientCores,6:F2}   {r.MegabytesPerSecond,6:N0}   {r.DeliveredPairHz,7:F2}   {r.DeliveryRatio,8:P1}   {r.SliceCount,5:F1}   {r.CommittedMb,7:N0} MB{mark}");
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

    /// <summary>
    /// How many bisection steps to spend narrowing the knee once the coarse climb has bracketed it.
    ///
    /// Two, which cuts the bracket to a quarter of its width. The coarse rungs are far apart on
    /// purpose — a doubling ladder is cheap but leaves the answer known only to within a factor of
    /// two, and "somewhere between 1,000 and 2,000" is not a number anyone can set a player cap
    /// from. Each step costs one more run, so this is where the budget goes furthest.
    /// </summary>
    private const int DefaultRefinements = 2;

    /// <summary>
    /// Climbs the coarse rungs until one fails, then bisects between the failure and the last
    /// success to find where the knee actually is.
    /// </summary>
    public static CapacityResult Run(
        LoadRunner runner,
        RunOptions template,
        IReadOnlyList<int> populations,
        Action<string> log,
        CancellationToken cancel,
        int refinements = DefaultRefinements)
    {
        var rungs = new List<LadderRung>();
        int lastGood = 0;
        int firstBad = 0;

        foreach (int players in populations)
        {
            if (cancel.IsCancellationRequested) break;

            LadderRung? rung = RunRung(runner, template, players, log, cancel);
            if (rung == null) { firstBad = players; break; }

            rungs.Add(rung);

            if (rung.DeliveryRatio < FullQualityDeliveryFloor)
            {
                firstBad = players;
                if (rung.DeliveryRatio < AbandonBelow)
                    log($"  Delivery has collapsed to {rung.DeliveryRatio:P0}; higher rungs would only measure how it sheds.");
                break;
            }

            lastGood = players;
        }

        // Bisect the bracket. Only worth doing when the climb actually found one - a ladder that ran
        // out of rungs while everything still worked has no knee to narrow, and halving an interval
        // whose upper end was never tested would invent one.
        for (int i = 0; i < refinements && firstBad > 0 && !cancel.IsCancellationRequested; i++)
        {
            int midpoint = (lastGood + firstBad) / 2;

            // Stop once the bracket is tighter than the step, or the midpoint repeats a rung.
            if (midpoint <= lastGood || midpoint >= firstBad) break;
            if (rungs.Any(r => r.Players == midpoint)) break;

            log($"\n  Narrowing: {lastGood:N0} held, {firstBad:N0} did not. Trying halfway.");
            LadderRung? rung = RunRung(runner, template, midpoint, log, cancel);
            if (rung == null) { firstBad = midpoint; continue; }

            rungs.Add(rung);
            if (rung.DeliveryRatio >= FullQualityDeliveryFloor) lastGood = midpoint;
            else firstBad = midpoint;
        }

        // Sorted, because bisection appends out of order and every curve fitted downstream assumes
        // ascending population.
        rungs.Sort((a, b) => a.Players.CompareTo(b.Players));

        return new CapacityResult
        {
            Rungs = rungs,
            FullQualityPlayers = rungs.LastOrDefault(r => r.DeliveryRatio >= FullQualityDeliveryFloor)?.Players
                                 ?? rungs.FirstOrDefault()?.Players ?? 0,
            MaxStablePlayers = rungs.LastOrDefault()?.Players ?? 0,
            Bottleneck = DiagnoseBottleneck(rungs),
            // Whether the climb ended because the machine gave out, or merely because it ran out of
            // rungs. Without this the two are indistinguishable in the result, and the top rung of a
            // ladder that simply stopped gets reported as a capacity - which is how "comfortably
            // 1,000" ends up describing a box that was at 19% of its cores and never pushed.
            KneeFound = rungs.Any(r => r.DeliveryRatio < FullQualityDeliveryFloor),
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
    /// <summary>Runs one rung. Null when the server could not seat the crowd at all.</summary>
    private static LadderRung? RunRung(
        LoadRunner runner, RunOptions template, int players, Action<string> log, CancellationToken cancel)
    {
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
        if (!result.Completed)
        {
            log($"  {players:N0} players: {result.Failure}");
            return null;
        }

        var rung = new LadderRung(players, result);
        log($"  {players:N0} players: {rung.DeliveredPairHz:F2} Hz/pair, {LadderRung.Fmt(rung.Cores)} cores, " +
            $"delivery {rung.DeliveryRatio:P1}");
        return rung;
    }

    private static string DiagnoseBottleneck(IReadOnlyList<LadderRung> rungs)
    {
        LadderRung? last = rungs.LastOrDefault();
        if (last == null) return "nothing measured";

        if (last.KernelDropsPerSecond > 100)
            return $"the kernel is discarding {last.KernelDropsPerSecond:N0} inbound datagrams/s - the receive path, " +
                   "not the CPU. Raise MultiSocketCount, and check net.core.rmem_max";

        if (!last.HasCores)
            return "undetermined - the server's CPU could not be read during this run";

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
    /// The coarse rungs, deliberately few and far apart.
    ///
    /// <para>250 / 1,000 / 2,000 / 4,000 rather than a doubling from 250, because the intermediate
    /// rungs of a doubling ladder mostly confirm what the one below already showed. Their job is
    /// only to bracket the knee; the bisection afterwards is what locates it, and a run spent
    /// narrowing a bracket buys far more than one spent widening it.</para>
    ///
    /// <para>It also bounds the cost. A doubling ladder gets slower the better the hardware is —
    /// a strong box passes every rung and pays for all of them — which is a perverse way to spend
    /// a test budget.</para>
    /// </summary>
    public static IReadOnlyList<int> DefaultPopulations(int maximum)
    {
        var values = new List<int>();
        foreach (int p in new[] { 250, 1000, 2000, 4000 })
            if (p <= maximum) values.Add(p);

        if (values.Count == 0) values.Add(maximum);
        else if (values[^1] < maximum && maximum >= values[^1] * 2) values.Add(maximum);
        return values;
    }
}
