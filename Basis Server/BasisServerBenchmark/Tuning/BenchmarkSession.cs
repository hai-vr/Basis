using System.Globalization;
using Basis.Benchmark.Harness;
using Basis.Benchmark.Machine;
using Basis.Benchmark.Micro;
using Basis.Benchmark.Output;

namespace Basis.Benchmark.Tuning;

/// <summary>Run parameters an operator can change from the console between jobs.</summary>
public sealed class SessionSettings
{
    public int Windows { get; set; } = 6;
    public int WindowSeconds { get; set; } = 30;
    public int WarmupSeconds { get; set; } = 60;
    public int MaxPlayers { get; set; } = 4000;
    public string? CorpusPath { get; set; }

    /// <summary>
    /// Restrict the sweep to these settings; empty means every eligible one.
    ///
    /// Exists for the loop this tool is actually used in: change one thing in the server, re-measure
    /// that one setting, rather than paying two hours for a matrix whose other nineteen arms cannot
    /// have moved.
    /// </summary>
    public HashSet<string> OnlyKnobs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<(string Name, string Value, string Note)> Describe()
    {
        yield return ("windows", Windows.ToString(CultureInfo.InvariantCulture),
            "timed windows per arm; five is the floor for a verdict");
        yield return ("window-sec", WindowSeconds.ToString(CultureInfo.InvariantCulture),
            "seconds per window");
        yield return ("warmup-sec", WarmupSeconds.ToString(CultureInfo.InvariantCulture),
            "settling time before the first window; the slicing loop oscillates, so shorter is riskier");
        yield return ("max-players", MaxPlayers.ToString(CultureInfo.InvariantCulture),
            "ceiling the ladder climbs to");
        yield return ("corpus", CorpusPath ?? "(generated)",
            "captured avatar bundles for the codec benchmark");
        yield return ("knobs", OnlyKnobs.Count == 0 ? "(all)" : string.Join(",", OnlyKnobs),
            "sweep only these settings; '-' for all");
    }

    public bool TrySet(string name, string value, out string message)
    {
        bool number = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
        switch (name.ToLowerInvariant())
        {
            case "windows" when number && n >= 1:
                Windows = n;
                message = n < Measure.Stats.MinimumWindows
                    ? $"windows = {n}. WARNING: below {Measure.Stats.MinimumWindows} no comparison can reach a verdict - " +
                      "every sweep result will read as inconclusive."
                    : $"windows = {n}";
                return true;
            case "window-sec" when number && n >= 5:
                WindowSeconds = n; message = $"window-sec = {n}"; return true;
            case "warmup-sec" when number && n >= 0:
                WarmupSeconds = n;
                message = n < 45
                    ? $"warmup-sec = {n}. WARNING: the slicing controller oscillates over several windows; " +
                      "under about 45s a run lands wherever it happens to be rather than at the steady state."
                    : $"warmup-sec = {n}";
                return true;
            case "max-players" when number && n >= 50:
                MaxPlayers = n; message = $"max-players = {n}"; return true;
            case "corpus":
                CorpusPath = value == "-" || value.Length == 0 ? null : value;
                message = $"corpus = {CorpusPath ?? "(generated)"}";
                return true;
            case "knobs":
                OnlyKnobs.Clear();
                if (value != "-" && value.Length > 0)
                    foreach (string knob in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        OnlyKnobs.Add(knob.Trim());

                string[] unknown = OnlyKnobs.Where(n => KnobCatalog.Find(n) == null).ToArray();
                message = $"knobs = {(OnlyKnobs.Count == 0 ? "(all)" : string.Join(",", OnlyKnobs))}" +
                          (unknown.Length > 0 ? $". WARNING: not in the catalog: {string.Join(", ", unknown)}" : "");
                return true;
            default:
                message = $"Cannot set '{name}' to '{value}'. Type /show for the settings and their ranges.";
                return false;
        }
    }
}

/// <summary>
/// Everything one console session knows, and the jobs it can run.
///
/// <para>Held across commands so the parts compose the way an operator actually works: run the
/// offline benches once, look at them, run a measurement, then start an auto that reuses what was
/// already measured rather than paying for it again. It also means <c>/report</c> and
/// <c>/write</c> have something to report on after a job that has already finished printing.</para>
/// </summary>
public sealed class BenchmarkSession
{
    private readonly Action<string> _log;

    public BenchmarkSession(string serverDirectory, string loadClientDirectory, string outputDirectory, Action<string> log)
    {
        ServerDirectory = serverDirectory;
        LoadClientDirectory = loadClientDirectory;
        OutputDirectory = outputDirectory;
        _log = log;
        Machine = MachineProfile.Collect();
    }

    public string ServerDirectory { get; }
    public string LoadClientDirectory { get; }
    public string OutputDirectory { get; }
    public SessionSettings Settings { get; } = new();

    public MachineProfile Machine { get; }
    public CoreBenchResult? Cores { get; private set; }
    public CompressionBenchResult? Compression { get; private set; }
    public CapacityResult? Capacity { get; private set; }
    public SweepResult? Sweep { get; private set; }
    public IReadOnlyList<Recommendation> Recommendations { get; private set; } = Array.Empty<Recommendation>();
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public string LastMode { get; private set; } = "session";

    public bool HasConfigs => new ConfigPatcher(ServerDirectory).ConfigsExist;

    // ── offline ─────────────────────────────────────────────────────────────────────────

    /// <summary>The microbenchmarks. No server, no crowd, about a minute and a half.</summary>
    public void RunOffline(CancellationToken cancel)
    {
        _log("\n  Core scaling");
        Cores = CoreBench.Run(Machine.LogicalCores, TimeSpan.FromSeconds(5), _log);
        _log(Cores.Describe());
        if (cancel.IsCancellationRequested) return;

        _log("  Compression");
        Compression = CompressionBench.Run(LoadCorpus(), _log);
        _log(Compression.Describe());

        RebuildRecommendations();
        LastMode = "profile (offline only)";
    }

    private BundleCorpus LoadCorpus()
    {
        if (Settings.CorpusPath != null)
        {
            BundleCorpus? loaded = BundleCorpus.TryLoad(Settings.CorpusPath);
            if (loaded != null) return loaded;
            _log($"    ! Could not read a corpus from {Settings.CorpusPath}; generating one instead.");
        }
        return BundleCorpus.Generate();
    }

    // ── one operating point ─────────────────────────────────────────────────────────────

    public void RunMeasure(int players, CancellationToken cancel)
    {
        using var configs = new ConfigPatcher(ServerDirectory, LoadClientDirectory);
        configs.Backup();

        var runner = new LoadRunner(configs, _log);
        RunResult result = runner.Run(Template(players, "measure"), cancel);

        if (!result.Completed)
        {
            _log($"  Run failed: {result.Failure}");
            return;
        }

        _log(FormatMeasurement(result));
    }

    public static string FormatMeasurement(RunResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"\n  {result.PeakConnected:N0} players, {result.Windows.Count} windows");
        sb.AppendLine($"    delivered      {result.Median(w => w.DeliveredPairHz):F3} Hz/pair " +
                      $"(IQR {Measure.Stats.Iqr(result.Series(w => w.DeliveredPairHz)):F3})");
        sb.AppendLine($"    delivery ratio {result.Median(w => w.DeliveryRatio):P2}");
        sb.AppendLine($"    server CPU     {result.Median(w => w.ServerCores):F2} cores " +
                      $"(load client {result.Median(w => w.ClientCores):F2})");
        sb.AppendLine($"    egress         {result.Median(w => w.MegabytesOutPerSecond):N0} MB/s, " +
                      $"{result.Median(w => w.DatagramsOutPerSecond):N0} datagrams/s");
        sb.AppendLine($"    drops          {result.Median(w => w.DropsPerSecond):N0}/s avatar, " +
                      $"{result.Median(w => w.VoiceDropsPerSecond):N0}/s voice");
        sb.AppendLine($"    slicing        {result.Median(w => w.SliceCount):F1}, tick {result.Median(w => w.TickMs):F2} ms, " +
                      $"overrun {result.Median(w => w.OverrunRatio):P1}");
        sb.AppendLine($"    memory         {result.Median(w => w.CommittedMb):N0} MB committed, " +
                      $"{result.Median(w => w.FragmentedMb):N0} MB fragmented, GC pause {result.Median(w => w.GcPausePercent):F1}%");
        double kernel = result.Median(w => w.KernelReceiveDropsPerSecond);
        if (kernel > 0)
            sb.AppendLine($"    kernel drops   {kernel:N0}/s inbound datagrams discarded - the receive path is the limit");
        if (result.VoiceDeliveredFraction >= 0)
            sb.AppendLine($"    voice heard    {result.VoiceDeliveredFraction:P2} (measured at the receivers)");
        return sb.ToString();
    }

    // ── the whole thing ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Climbs until the machine stops delivering, then fits the settings at the population it
    /// actually serves, then confirms the combination.
    /// </summary>
    public void RunAuto(bool full, CancellationToken cancel)
    {
        LastMode = full ? "auto (full sweep)" : "auto";

        if (Machine.Kernel?.AnyClamped == true)
        {
            _log("\n  ! The kernel is clamping the 32 MB socket buffer the server asks for. Every capacity number");
            _log("    below is a measurement of that clamp, not of this machine. Fix it and run /auto again:");
            foreach (string line in Machine.Kernel.RemediationSnippet()!.Split('\n')) _log("      " + line);
        }

        if (Cores == null || Compression == null) RunOffline(cancel);
        if (cancel.IsCancellationRequested) return;

        using var configs = new ConfigPatcher(ServerDirectory, LoadClientDirectory);
        if (!configs.ConfigsExist)
        {
            _log($"  No config under {ServerDirectory}/config. Start the server once so it writes its defaults.");
            return;
        }
        configs.Backup();
        _log("  Configs backed up; restored when this finishes or is stopped.");

        var runner = new LoadRunner(configs, _log);

        _log($"\n  Climbing to at most {Settings.MaxPlayers:N0} players. Each rung is about " +
             $"{Template(1000, "x").EstimatedDuration.TotalMinutes:F0} min.");
        Capacity = CapacityLadder.Run(runner, Template(0, "ladder"),
            CapacityLadder.DefaultPopulations(Settings.MaxPlayers), _log, cancel);
        _log(Capacity.Describe());

        if (cancel.IsCancellationRequested) { RebuildRecommendations(); return; }

        int design = Capacity.FullQualityPlayers;
        if (design <= 0)
        {
            _log("  No rung completed, so there is no population to fit settings at.");
            RebuildRecommendations();
            return;
        }

        RebuildRecommendations(design);

        string? idle = Capacity.IdleWarning(Machine.LogicalCores);
        if (idle != null)
        {
            _log("\n  ! " + idle);
            _log("    Skipping the sweep - it would spend hours proving that nothing matters.");
            return;
        }

        var knobs = KnobCatalog.All
            .Where(k => full || k.Confidence == LoopbackConfidence.Honest)
            .Where(k => !Recommendations.Any(r => r.Setting == k.Name && r.Evidence == Evidence.Derived))
            .Where(k => Settings.OnlyKnobs.Count == 0 || Settings.OnlyKnobs.Contains(k.Name))
            .ToList();

        if (knobs.Count == 0)
        {
            _log("\n  Nothing left to sweep - every eligible setting is already derived, or the /set knobs " +
                 "filter excludes all of them.");
            return;
        }

        int arms = 1 + knobs.Sum(k => Math.Max(0, k.ResolveCandidates(Machine.LogicalCores, Machine.TotalMemoryBytes).Count - 1)) + 1;
        _log($"\n  Sweeping {knobs.Count} setting(s) at {design:N0} players: {arms} arms, " +
             $"about {arms * Template(design, "x").EstimatedDuration.TotalHours:F1} hours. /stop to cut it short.");

        var sweeper = new KnobSweeper(runner, configs, loopback: true,
            Machine.LogicalCores, Machine.TotalMemoryBytes, _log);
        Sweep = sweeper.Run(Template(design, "sweep"), knobs, cancel);

        // A measured result supersedes a derived one for the same setting: the derivation was the
        // starting point, and something that actually ran under load outranks it.
        var merged = Recommendations.ToList();
        foreach (Recommendation r in Sweep.Recommendations)
        {
            merged.RemoveAll(existing => existing.Setting == r.Setting);
            merged.Add(r);
        }
        Recommendations = merged;
    }

    /// <summary>
    /// What this machine can be expected to do, fitted from the ladder. Null until one has run.
    /// </summary>
    public CapabilityModel? Capability { get; private set; }

    private void RebuildRecommendations(int designPlayers = 0)
    {
        if (Cores == null || Compression == null) return;
        if (designPlayers <= 0) designPlayers = Capacity?.FullQualityPlayers > 0 ? Capacity.FullQualityPlayers : 1000;

        var configs = new ConfigPatcher(ServerDirectory, LoadClientDirectory);
        Func<string, string?> read = configs.ConfigsExist ? configs.Read : _ => null;
        var recommendations = DerivedSettings.For(Machine, Cores, Compression, designPlayers, read, Capacity).ToList();

        // The player cap comes out of the capability model rather than the sweep, because it is not
        // a tuning choice at all - it is the measurement, written down. It needs the ladder, so it
        // only appears once one has run.
        Capability = Capacity == null
            ? null
            : new CapabilityModel(Capacity.Rungs, Machine, Machine.Link, Capacity.FullQualityPlayers);

        if (Capability != null && DerivedSettings.RecommendPeerLimit(Capability, read) is { } peerLimit)
            recommendations.Add(peerLimit);

        Recommendations = recommendations;
    }

    private RunOptions Template(int players, string label) => new()
    {
        ServerDirectory = ServerDirectory,
        LoadClientDirectory = LoadClientDirectory,
        Players = players > 0 ? players : 500,
        Warmup = TimeSpan.FromSeconds(Settings.WarmupSeconds),
        WindowLength = TimeSpan.FromSeconds(Settings.WindowSeconds),
        Windows = Settings.Windows,
        Label = label,
    };

    // ── output ──────────────────────────────────────────────────────────────────────────

    public BenchmarkReport BuildReport() => new()
    {
        StartedUtc = StartedUtc,
        Mode = LastMode,
        Loopback = true,
        Machine = Machine,
        Cores = Cores,
        Compression = Compression,
        Capacity = Capacity,
        Sweep = Sweep,
        Recommendations = Recommendations,
    };

    public int DesignPlayers => Capacity?.FullQualityPlayers ?? 0;
}
