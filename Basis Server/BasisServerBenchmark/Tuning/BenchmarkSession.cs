using System.Globalization;
using Basis.Benchmark.Harness;
using Basis.Benchmark.Machine;
using Basis.Benchmark.Micro;
using Basis.Benchmark.Output;

namespace Basis.Benchmark.Tuning;

/// <summary>How much of the tuning run to do.</summary>
public enum AutoMode
{
    /// <summary>
    /// Offline benchmarks, one load point, and a join burst. Around five minutes.
    ///
    /// <para>Produces every finding that does not need a curve: the codec settings, the per-peer
    /// pass width, and the auth window. What it cannot produce is a scaling curve — one population
    /// is a point, not a trend — so there is no memory or bandwidth ceiling and the player cap is
    /// only "this much worked", not "this is where it stops".</para>
    /// </summary>
    Quick,

    /// <summary>
    /// Adds a three-rung capacity ladder. Around fifteen minutes.
    ///
    /// <para>The cheapest run that can say what limits this machine, because three points are the
    /// fewest a quadratic can be fitted through — and the cost curve here genuinely is quadratic,
    /// since every player is tracked against every other. This is where the player cap, the
    /// binding constraint and the capability summary become real rather than indicative.</para>
    /// </summary>
    Medium,

    /// <summary>
    /// Full ladder plus the A/B setting sweep. A couple of hours.
    ///
    /// <para>The sweep is roughly three quarters of the wall time — one server restart per arm —
    /// and on a box with headroom it most often concludes that nothing measurably changed, because
    /// nothing was scarce enough for a setting to relieve. It earns its cost on a machine that is
    /// actually working at the population it serves.</para>
    /// </summary>
    Long,
}

/// <summary>Timings and ladder shape for one mode.</summary>
public sealed record RunProfile(
    int WarmupSeconds,
    int Windows,
    int WindowSeconds,
    int LadderRungs,
    bool Sweep,
    int Refinements)
{
    public static RunProfile For(AutoMode mode) => mode switch
    {
        // Warmup never drops below 45s in any mode. It is not padding: the slicing controller
        // oscillates over several windows, and under about 45s a run records wherever that
        // oscillation happened to be rather than the steady state. Windows are what gets traded.
        //
        // Refinements are bisection steps taken after the coarse rungs bracket the knee. They are
        // the best value per run available here - each one halves the uncertainty in the player cap
        // - but they are still runs, so the budget is per mode rather than fixed.
        AutoMode.Quick => new RunProfile(45, 3, 20, 1, false, 0),
        AutoMode.Medium => new RunProfile(45, 4, 25, 3, false, 1),
        _ => new RunProfile(60, 6, 30, 99, true, 2),
    };
}

/// <summary>Run parameters an operator can change from the console between jobs.</summary>
public sealed class SessionSettings
{
    public int Windows { get; set; } = 6;
    public int WindowSeconds { get; set; } = 30;
    public int WarmupSeconds { get; set; } = 60;
    public int MaxPlayers { get; set; } = 4000;
    public string? CorpusPath { get; set; }

    /// <summary>
    /// Which compute device to measure, when the host has more than one. Empty measures the best
    /// one. Same grammar as the server's ComputeDevice setting - an index or part of a name - so a
    /// value that measured well here can be copied straight into config.xml.
    /// </summary>
    public string ComputeDevice { get; set; } = "";

    /// <summary>
    /// Write the tuning profile automatically when a run finishes. On by default.
    ///
    /// <para>Measuring and then requiring a separate command to act on it is a good way to have
    /// nobody act on it — an operator who has just waited out a ladder has been told what to change
    /// and is one forgotten step away from changing nothing. Writing is safe to do unprompted: the
    /// profile is applied once, folded into config.xml, fingerprinted to this machine, and every
    /// value is logged on the boot that consumes it.</para>
    /// </summary>
    public bool AutoWrite { get; set; } = true;

    /// <summary>
    /// Restrict the sweep to these settings; empty means every eligible one.
    ///
    /// Exists for the loop this tool is actually used in: change one thing in the server, re-measure
    /// that one setting, rather than paying two hours for a matrix whose other nineteen arms cannot
    /// have moved.
    /// </summary>
    public HashSet<string> OnlyKnobs { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Settings the operator changed by hand this session.
    ///
    /// A mode carries its own timings, and applying them over the top of a deliberate /set would
    /// silently throw that choice away — so the profile fills in only what nobody has an opinion
    /// about, and says which of its values it skipped.
    /// </summary>
    private readonly HashSet<string> _explicit = new(StringComparer.OrdinalIgnoreCase);

    public bool WasSetByHand(string name) => _explicit.Contains(name);

    /// <summary>Applies a mode's timings to everything not already set by hand.</summary>
    public IReadOnlyList<string> ApplyProfile(RunProfile profile)
    {
        var kept = new List<string>();
        if (WasSetByHand("warmup-sec")) kept.Add($"warmup-sec {WarmupSeconds}"); else WarmupSeconds = profile.WarmupSeconds;
        if (WasSetByHand("windows")) kept.Add($"windows {Windows}"); else Windows = profile.Windows;
        if (WasSetByHand("window-sec")) kept.Add($"window-sec {WindowSeconds}"); else WindowSeconds = profile.WindowSeconds;
        return kept;
    }

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
        yield return ("autowrite", AutoWrite ? "on" : "off",
            "write the tuning profile when a run finishes");
    }

    public bool TrySet(string name, string value, out string message)
    {
        _explicit.Add(name);
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
            case "autowrite":
                AutoWrite = value is "on" or "true" or "1" or "yes";
                message = $"autowrite = {(AutoWrite ? "on" : "off")}" +
                          (AutoWrite ? "" : ". Runs will report only; /write applies them.");
                return true;
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
        Gpu = GpuProfile.Collect();
    }

    public string ServerDirectory { get; }
    public string LoadClientDirectory { get; }
    public string OutputDirectory { get; }
    public SessionSettings Settings { get; } = new();

    /// <summary>
    /// <c>DistanceUpdateIntervalTicks</c>, the period the server spreads one full sweep over. Held
    /// here so the offload verdict is expressed against the refresh rate the server actually runs.
    /// </summary>
    private const int DistanceSweepIntervalTicks = 125;

    public MachineProfile Machine { get; }
    public GpuProfile Gpu { get; }
    public CoreBenchResult? Cores { get; private set; }
    public CompressionBenchResult? Compression { get; private set; }
    public GpuBenchResult? GpuOffload { get; private set; }
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
        if (cancel.IsCancellationRequested) return;

        if (Gpu.Availability == GpuAvailability.Present)
        {
            _log("  Compute offload");
            int designPlayers = DesignPlayers > 0 ? DesignPlayers : 1000;
            GpuOffload = GpuBench.Run(Gpu, designPlayers, Cores.KneeWorkers, DistanceSweepIntervalTicks, Settings.ComputeDevice, _log);
            if (GpuOffload != null) _log(GpuOffload.Describe());
        }

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

    /// <summary>The most recent join-burst measurement, or null if none has run.</summary>
    public AdmissionResult? Admission { get; private set; }

    /// <summary>
    /// Throws the whole crowd at the server at once, the way a restart does.
    /// </summary>
    public void RunBurst(int players, CancellationToken cancel)
    {
        using var configs = new ConfigPatcher(ServerDirectory, LoadClientDirectory);
        configs.Backup();

        var runner = new LoadRunner(configs, _log);
        RunOptions options = Template(players, "burst");
        Admission = AdmissionBurst.Run(runner, new RunOptions
        {
            ServerDirectory = ServerDirectory,
            LoadClientDirectory = LoadClientDirectory,
            Players = players,
            ConnectTimeout = TimeSpan.FromMinutes(5),
            HealthHost = options.HealthHost,
            HealthPort = options.HealthPort,
            // 0 is the whole point: clients start as fast as the loop runs rather than on the
            // gentle 1 ms ramp the ladder uses.
            ClientConnectIntervalMs = 0,
            Label = $"burst {players}",
        }, _log, cancel);

        // So a standalone /burst still contributes its finding rather than measuring and forgetting.
        RebuildRecommendations();
    }

    // ── the whole thing ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Climbs until the machine stops delivering, then fits the settings at the population it
    /// actually serves, then confirms the combination.
    /// </summary>
    /// <summary>
    /// Also sweep the settings loopback cannot judge honestly. They are reported with their caveat
    /// and still never written — this only decides whether the measurement is taken at all.
    /// </summary>
    public bool IncludeUntrusted { get; set; }

    /// <summary>
    /// Where the crowd comes from. Null means local, which is the default and the compromised one.
    ///
    /// Held on the session rather than created per run so one control connection spans every arm of
    /// a sweep - the agent stops its clients when that connection closes, so reconnecting per arm
    /// would tear the crowd down between measurements.
    /// </summary>
    public ILoadClientDriver? Driver { get; set; }

    /// <summary>True when the crowd runs off-box, which is what makes packet-rate findings usable.</summary>
    public bool RemoteLoad => Driver?.IsRemote == true;

    public void RunAuto(AutoMode mode, CancellationToken cancel)
    {
        LastMode = mode switch
        {
            AutoMode.Quick => "auto (quick)",
            AutoMode.Medium => "auto (medium)",
            _ => "auto (long, with sweep)",
        };

        RunProfile profile = RunProfile.For(mode);
        IReadOnlyList<string> kept = Settings.ApplyProfile(profile);
        _log($"  {mode} run: warmup {Settings.WarmupSeconds}s, {Settings.Windows} x {Settings.WindowSeconds}s windows" +
             (profile.Sweep ? ", with the setting sweep." : ", no setting sweep."));
        if (kept.Count > 0)
            _log($"  Keeping what you set by hand: {string.Join(", ", kept)}.");

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

        // How far the ladder climbs is the mode's main lever. Three rungs is the fewest a quadratic
        // can be fitted through, and the cost curve here genuinely is quadratic, so that is the
        // line between "indicative" and "this is what limits your box".
        var populations = CapacityLadder.DefaultPopulations(Settings.MaxPlayers)
            .Take(profile.LadderRungs).ToList();
        if (populations.Count == 0) populations.Add(Math.Min(500, Settings.MaxPlayers));

        _log($"\n  Ladder: {string.Join(", ", populations.Select(p => p.ToString("N0")))} players, about " +
             $"{populations.Sum(p => Template(p, "x").EstimatedDuration.TotalMinutes):F0} min in total.");
        Capacity = CapacityLadder.Run(runner, Template(0, "ladder"), populations, _log, cancel, profile.Refinements);
        _log(Capacity.Describe());

        // Surfaced right after the table, because every CPU-derived conclusion below inherits it.
        if (Capacity.ContentionWarning(Machine.LogicalCores) is { } contended)
            _log("  ! " + contended + Environment.NewLine);

        if (cancel.IsCancellationRequested) { RebuildRecommendations(); return; }

        int design = Capacity.FullQualityPlayers;
        if (design <= 0)
        {
            _log("  No rung completed, so there is no population to fit settings at.");
            RebuildRecommendations();
            return;
        }

        // The burst runs in every mode. It is a minute and a half and it is the only thing that
        // exercises admission at all, which is a different subsystem from the one the ladder just
        // measured and the one an operator meets first after a restart.
        // Run the burst at the population the ladder settled on. Admission is a different
        // subsystem from steady state, so a box that serves this crowd well may still be unable
        // to admit it after a restart - and that is the failure an operator meets first.
        if (!cancel.IsCancellationRequested)
        {
            _log($"\n  Join burst at {design:N0} players - what a restart looks like");
            RunBurst(design, cancel);
        }

        RebuildRecommendations(design);

        if (!profile.Sweep)
        {
            _log($"{Environment.NewLine}  {mode} run: stopping before the setting sweep. Everything above came from the ladder, the");
            _log("  offline benchmarks and the burst - the auth window, the codec settings and the pass width,");
            _log(populations.Count >= 3
                ? "  plus the player cap and what limits this box."
                : "  A single load point cannot give a scaling curve, so there is no memory or bandwidth ceiling"
                  + Environment.NewLine
                  + "  and the player cap is only 'this much worked'. /auto medium adds those for ten more minutes.");
            _log("  The A/B-measured settings cost roughly another hour and a half: /auto long when it suits.");
            Apply();
            return;
        }

        string? idle = Capacity.IdleWarning(Machine.LogicalCores);
        if (idle != null)
        {
            _log("\n  ! " + idle);
            _log("    Skipping the sweep - it would spend hours proving that nothing matters.");
            Apply();
            return;
        }

        var knobs = KnobCatalog.All
            // A remote crowd makes the packet-rate and socket settings measurable, so they join the
            // sweep by default there instead of needing to be asked for.
            .Where(k => IncludeUntrusted || RemoteLoad || k.Confidence == LoopbackConfidence.Honest)
            .Where(k => !Recommendations.Any(r => r.Setting == k.Name && r.Evidence == Evidence.Derived))
            .Where(k => Settings.OnlyKnobs.Count == 0 || Settings.OnlyKnobs.Contains(k.Name))
            .ToList();

        if (knobs.Count == 0)
        {
            _log("\n  Nothing left to sweep - every eligible setting is already derived, or the /set knobs " +
                 "filter excludes all of them.");
            Apply();
            return;
        }

        int arms = 1 + knobs.Sum(k => Math.Max(0, k.ResolveCandidates(Machine.LogicalCores, Machine.TotalMemoryBytes).Count - 1)) + 1;
        _log($"\n  Sweeping {knobs.Count} setting(s) at {design:N0} players: {arms} arms, " +
             $"about {arms * Template(design, "x").EstimatedDuration.TotalHours:F1} hours. /stop to cut it short.");

        var sweeper = new KnobSweeper(runner, configs, loopback: !RemoteLoad,
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

        Apply();
    }

    /// <summary>
    /// Writes the tuning profile, unless the operator has turned that off.
    ///
    /// Called from every path out of a run that produced findings, rather than once at the end,
    /// because most of those paths are early returns - a quick run, an idle box, a filtered sweep -
    /// and each of them has still measured something worth keeping.
    /// </summary>
    private void Apply()
    {
        if (!Settings.AutoWrite)
        {
            int pending = Recommendations.Count(r => r.IsChange && r.Writable);
            if (pending > 0)
                _log($"{Environment.NewLine}  autowrite is off: {pending} change(s) measured but not written. /write applies them.");
            return;
        }

        try { _log(Output.TuningProfileWriter.Write(this)); }
        catch (Exception ex) { _log($"  Could not write the tuning profile: {ex.Message}"); }
    }

    /// <summary>
    /// What this machine can be expected to do, fitted from the ladder. Null until one has run.
    /// </summary>
    public CapabilityModel? Capability { get; private set; }

    /// <summary>
    /// Recomputes every recommendation the session has the evidence for.
    ///
    /// <para>Each source is gated on what it actually needs rather than on a single all-or-nothing
    /// check, so a command that measures one thing contributes what it measured. A standalone
    /// <c>/burst</c> produces an auth-window finding without a ladder or the offline benches; a
    /// <c>/profile</c> produces the codec and width findings without load. Requiring everything
    /// before reporting anything silently discarded a measurement the operator had just paid
    /// for.</para>
    /// </summary>
    private void RebuildRecommendations(int designPlayers = 0)
    {
        if (designPlayers <= 0) designPlayers = Capacity?.FullQualityPlayers > 0 ? Capacity.FullQualityPlayers : 1000;

        var configs = new ConfigPatcher(ServerDirectory, LoadClientDirectory);
        Func<string, string?> read = configs.ConfigsExist ? configs.Read : _ => null;

        var recommendations = new List<Recommendation>();
        if (Cores != null && Compression != null)
            recommendations.AddRange(DerivedSettings.For(Machine, Cores, Compression, designPlayers, read, Capacity));

        // The player cap comes out of the capability model rather than the sweep, because it is not
        // a tuning choice at all - it is the measurement, written down. It needs the ladder, so it
        // only appears once one has run.
        Capability = Capacity == null
            ? null
            : new CapabilityModel(Capacity.Rungs, Machine, Machine.Link, Capacity.FullQualityPlayers,
                Capacity.KneeFound);

        if (Capability != null && DerivedSettings.RecommendPeerLimit(Capability, read) is { } peerLimit)
            recommendations.Add(peerLimit);

        // The auth window can only be fitted from a burst; the ladder ramps gently by design and
        // never exercises the race this setting exists to survive.
        if (DerivedSettings.RecommendAuthTimeout(Admission, read) is { } authWindow)
            recommendations.Add(authWindow);

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
        Driver = Driver,
    };

    // ── output ──────────────────────────────────────────────────────────────────────────

    public BenchmarkReport BuildReport() => new()
    {
        StartedUtc = StartedUtc,
        Mode = LastMode,
        Loopback = !RemoteLoad,
        Machine = Machine,
        Cores = Cores,
        Compression = Compression,
        Gpu = Gpu,
        GpuOffload = GpuOffload,
        Capacity = Capacity,
        Sweep = Sweep,
        Recommendations = Recommendations,
    };

    public int DesignPlayers => Capacity?.FullQualityPlayers ?? 0;
}
