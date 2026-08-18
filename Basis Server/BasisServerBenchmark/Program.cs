using System.Globalization;
using Basis.Benchmark.Harness;
using Basis.Benchmark.Machine;
using Basis.Benchmark.Micro;
using Basis.Benchmark.Output;
using Basis.Benchmark.Tuning;

namespace Basis.Benchmark;

public static class Program
{
    public static int Main(string[] args)
    {
        var options = CommandLine.Parse(args);
        if (options == null) { CommandLine.PrintUsage(); return 1; }
        if (options.Help) { CommandLine.PrintUsage(); return 0; }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n  Interrupted - tearing down and restoring configs...");
            cancellation.Cancel();
        };

        try
        {
            return options.Mode switch
            {
                "profile" => RunProfile(options),
                "autotune" => RunTuning(options, full: false, cancellation.Token),
                "sweep" => RunTuning(options, full: true, cancellation.Token),
                "measure" => RunMeasure(options, cancellation.Token),
                _ => Fail($"Unknown mode '{options.Mode}'."),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n  Failed: {ex.Message}");
            return 1;
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        CommandLine.PrintUsage();
        return 1;
    }

    // ── profile ─────────────────────────────────────────────────────────────────────────

    private static int RunProfile(BenchOptions options)
    {
        DateTime started = DateTime.UtcNow;
        MachineProfile machine = MachineProfile.Collect();

        Console.WriteLine("\nMachine");
        Console.Write(machine.Describe());

        Console.WriteLine("\nCore scaling (this takes about a minute)");
        CoreBenchResult cores = CoreBench.Run(machine.LogicalCores, TimeSpan.FromSeconds(4), Console.WriteLine);
        Console.Write(cores.Describe());

        Console.WriteLine("\nCompression");
        BundleCorpus corpus = LoadCorpus(options);
        CompressionBenchResult compression = CompressionBench.Run(corpus, Console.WriteLine);
        Console.Write(compression.Describe());

        // Read the real config when one was pointed at, so the report compares against what is
        // actually deployed rather than against the shipped defaults. Without --server the
        // "current" column is the default, which is a guess and is worth being honest about.
        Func<string, string?> readCurrent = _ => null;
        if (options.ServerDirectory != null)
        {
            var configs = new ConfigPatcher(options.ServerDirectory);
            if (configs.ConfigsExist) readCurrent = configs.Read;
            else Console.Error.WriteLine($"  ! No config under {options.ServerDirectory}; comparing against shipped defaults.");
        }

        var report = new BenchmarkReport
        {
            StartedUtc = started,
            Mode = options.ServerDirectory == null ? "profile (no load, no config read)" : "profile (no load)",
            Loopback = true,
            Machine = machine,
            Cores = cores,
            Compression = compression,
            Recommendations = DerivedSettings
                .For(machine, cores, compression, options.Players > 0 ? options.Players : 1000, readCurrent)
                .ToList(),
        };

        Console.WriteLine();
        Console.WriteLine(Report.Render(report));
        Report.WriteTo(options.OutputDirectory, report);
        Console.WriteLine($"  Written to {options.OutputDirectory}");
        return 0;
    }

    // ── autotune / sweep ────────────────────────────────────────────────────────────────

    private static int RunTuning(BenchOptions options, bool full, CancellationToken cancel)
    {
        if (options.ServerDirectory == null || options.LoadClientDirectory == null)
            return Fail("Both --server and --client are required for a load run.");

        DateTime started = DateTime.UtcNow;
        MachineProfile machine = MachineProfile.Collect();

        Console.WriteLine("\nMachine");
        Console.Write(machine.Describe());

        if (machine.Kernel?.AnyClamped == true)
        {
            Console.WriteLine();
            Console.WriteLine("  ! The kernel is clamping the server's 32 MB socket buffer request. Every capacity");
            Console.WriteLine("    number this run produces will be a measurement of that clamp. Fix it and re-run:");
            Console.WriteLine();
            foreach (string line in machine.Kernel.RemediationSnippet()!.Split('\n')) Console.WriteLine("      " + line);
            if (!options.Yes && !Confirm("  Continue anyway?")) return 1;
        }

        using var configs = new ConfigPatcher(options.ServerDirectory, options.LoadClientDirectory);
        if (!configs.ConfigsExist)
            return Fail($"No config found under {options.ServerDirectory}\\config. " +
                        "Start the server once so it writes its defaults, then re-run.");
        configs.Backup();
        Console.WriteLine("\n  Configs backed up; they are restored when this exits, including on Ctrl-C.");

        Console.WriteLine("\nCore scaling (offline)");
        CoreBenchResult cores = CoreBench.Run(machine.LogicalCores, TimeSpan.FromSeconds(full ? 6 : 3), Console.WriteLine);
        Console.Write(cores.Describe());

        Console.WriteLine("\nCompression (offline)");
        CompressionBenchResult compression = CompressionBench.Run(LoadCorpus(options), Console.WriteLine);
        Console.Write(compression.Describe());

        var template = new RunOptions
        {
            ServerDirectory = options.ServerDirectory,
            LoadClientDirectory = options.LoadClientDirectory,
            Players = options.Players > 0 ? options.Players : 500,
            Warmup = TimeSpan.FromSeconds(options.WarmupSeconds),
            WindowLength = TimeSpan.FromSeconds(options.WindowSeconds),
            Windows = options.Windows,
            HealthHost = options.HealthHost,
            HealthPort = options.HealthPort,
        };

        var runner = new LoadRunner(configs, Console.WriteLine);

        // The ladder is what makes every later number mean something: a setting's best value is a
        // property of the setting AT a population, so the design point has to be found before
        // anything is fitted to it.
        Console.WriteLine("\nCapacity ladder");
        IReadOnlyList<int> populations = options.Players > 0
            ? new[] { options.Players }
            : CapacityLadder.DefaultPopulations(full ? 4000 : 1000);

        CapacityResult capacity = CapacityLadder.Run(runner, template, populations, Console.WriteLine, cancel);
        Console.Write(capacity.Describe());

        int designPopulation = capacity.FullQualityPlayers > 0 ? capacity.FullQualityPlayers : template.Players;

        var recommendations = new List<Recommendation>();
        recommendations.AddRange(DerivedSettings.For(machine, cores, compression, designPopulation, configs.Read));

        // Sweeping a machine that is not working measures nothing, however many arms it runs. Say
        // so and stop, rather than spending an hour to report that no setting matters.
        string? idle = capacity.IdleWarning(machine.LogicalCores);
        if (idle != null)
        {
            Console.WriteLine();
            Console.WriteLine("  ! " + idle);
            if (options.Players > 0)
                Console.WriteLine("    Raise --players, or drop it entirely to let the ladder find the knee.");
        }

        SweepResult? sweep = null;
        if (!cancel.IsCancellationRequested && capacity.Rungs.Count > 0 && idle == null)
        {
            // Fitted at the design point, not above it. Past the knee the server is shedding, and a
            // setting tuned against a shedding server is tuned against a failure mode.
            var sweepTemplate = new RunOptions
            {
                ServerDirectory = template.ServerDirectory,
                LoadClientDirectory = template.LoadClientDirectory,
                Players = designPopulation,
                Warmup = template.Warmup,
                WindowLength = template.WindowLength,
                Windows = template.Windows,
                ConnectTimeout = template.ConnectTimeout,
                HealthHost = template.HealthHost,
                HealthPort = template.HealthPort,
            };

            // The autotune pass only sweeps what a single box can measure honestly; the rest is
            // derived above. The full sweep measures everything and marks the untrustworthy ones.
            var knobs = KnobCatalog.All
                .Where(k => full || k.Confidence == LoopbackConfidence.Honest)
                .Where(k => !recommendations.Any(r => r.Setting == k.Name && r.Evidence == Evidence.Derived))
                .Where(k => options.OnlyKnobs.Count == 0 || options.OnlyKnobs.Contains(k.Name))
                .ToList();

            if (options.OnlyKnobs.Count > 0)
            {
                foreach (string requested in options.OnlyKnobs.Where(n => KnobCatalog.Find(n) == null))
                    Console.Error.WriteLine($"  ! --knobs named '{requested}', which is not in the catalog.");
            }

            Console.WriteLine($"\nSweeping {knobs.Count} setting(s) at {designPopulation:N0} players");
            Console.WriteLine($"  Roughly {EstimateHours(knobs, sweepTemplate):F1} hours if every arm runs.");

            var sweeper = new KnobSweeper(runner, configs, loopback: true, machine.LogicalCores, machine.TotalMemoryBytes, Console.WriteLine);
            sweep = sweeper.Run(sweepTemplate, knobs, cancel);

            // A measured result supersedes a derived one for the same setting: the derivation was
            // the starting point, and something that actually ran under load outranks it.
            foreach (Recommendation r in sweep.Recommendations)
            {
                recommendations.RemoveAll(existing => existing.Setting == r.Setting);
                recommendations.Add(r);
            }
        }

        var report = new BenchmarkReport
        {
            StartedUtc = started,
            Mode = full ? "sweep (full)" : "autotune",
            Loopback = true,
            Machine = machine,
            Cores = cores,
            Compression = compression,
            Capacity = capacity,
            Sweep = sweep,
            Recommendations = recommendations,
        };

        Console.WriteLine();
        Console.WriteLine(Report.Render(report));
        Report.WriteTo(options.OutputDirectory, report);
        Console.WriteLine($"  Written to {options.OutputDirectory}");

        var writable = report.Writable.ToList();
        if (writable.Count == 0)
        {
            Console.WriteLine("\n  Nothing to apply.");
            return 0;
        }

        if (!options.Apply)
        {
            Console.WriteLine($"\n  {writable.Count} change(s) available. Re-run with --apply to write them.");
            return 0;
        }

        if (!options.Yes && !Confirm($"  Write {writable.Count} change(s) into the server config?"))
        {
            Console.WriteLine("  Left unchanged.");
            return 0;
        }

        // Reset first so the applied values land on the operator's original file rather than on
        // whatever the last sweep arm happened to leave behind, then mark the session done so the
        // dispose-time restore does not undo what was just written.
        configs.ResetToBackup();
        configs.Apply(writable.ToDictionary(r => r.Setting, r => r.ProposedValue));
        configs.KeepChanges();
        Console.WriteLine("  Applied. Restart the server - several of these are read once at socket bind.");
        return 0;
    }

    // ── measure ─────────────────────────────────────────────────────────────────────────

    private static int RunMeasure(BenchOptions options, CancellationToken cancel)
    {
        if (options.ServerDirectory == null || options.LoadClientDirectory == null)
            return Fail("Both --server and --client are required for a load run.");
        if (options.Players <= 0)
            return Fail("--players is required for measure mode.");

        using var configs = new ConfigPatcher(options.ServerDirectory, options.LoadClientDirectory);
        if (!configs.ConfigsExist)
            return Fail($"No config found under {options.ServerDirectory}\\config.");
        configs.Backup();

        var runner = new LoadRunner(configs, Console.WriteLine);
        RunResult result = runner.Run(new RunOptions
        {
            ServerDirectory = options.ServerDirectory,
            LoadClientDirectory = options.LoadClientDirectory,
            Players = options.Players,
            Warmup = TimeSpan.FromSeconds(options.WarmupSeconds),
            WindowLength = TimeSpan.FromSeconds(options.WindowSeconds),
            Windows = options.Windows,
            HealthHost = options.HealthHost,
            HealthPort = options.HealthPort,
            Label = "measure",
        }, cancel);

        if (!result.Completed)
        {
            Console.Error.WriteLine($"  Run failed: {result.Failure}");
            return 1;
        }

        Console.WriteLine($"\n  {result.PeakConnected:N0} players, {result.Windows.Count} windows");
        Console.WriteLine($"    delivered      {result.Median(w => w.DeliveredPairHz):F3} Hz/pair " +
                          $"(IQR {Measure.Stats.Iqr(result.Series(w => w.DeliveredPairHz)):F3})");
        Console.WriteLine($"    delivery ratio {result.Median(w => w.DeliveryRatio):P2}");
        Console.WriteLine($"    server CPU     {result.Median(w => w.ServerCores):F2} cores " +
                          $"(load client {result.Median(w => w.ClientCores):F2})");
        Console.WriteLine($"    egress         {result.Median(w => w.MegabytesOutPerSecond):N0} MB/s, " +
                          $"{result.Median(w => w.DatagramsOutPerSecond):N0} datagrams/s");
        Console.WriteLine($"    drops          {result.Median(w => w.DropsPerSecond):N0}/s avatar, " +
                          $"{result.Median(w => w.VoiceDropsPerSecond):N0}/s voice");
        Console.WriteLine($"    slicing        {result.Median(w => w.SliceCount):F1}, tick {result.Median(w => w.TickMs):F2} ms, " +
                          $"overrun {result.Median(w => w.OverrunRatio):P1}");
        Console.WriteLine($"    memory         {result.Median(w => w.CommittedMb):N0} MB committed, " +
                          $"{result.Median(w => w.FragmentedMb):N0} MB fragmented, GC pause {result.Median(w => w.GcPausePercent):F1}%");
        if (result.VoiceDeliveredFraction >= 0)
            Console.WriteLine($"    voice heard    {result.VoiceDeliveredFraction:P2} (measured at the receivers)");
        return 0;
    }

    // ── shared ──────────────────────────────────────────────────────────────────────────

    private static BundleCorpus LoadCorpus(BenchOptions options)
    {
        if (options.CorpusPath != null)
        {
            BundleCorpus? loaded = BundleCorpus.TryLoad(options.CorpusPath);
            if (loaded != null) return loaded;
            Console.Error.WriteLine($"  ! Could not read a corpus from {options.CorpusPath}; generating one instead.");
        }
        return BundleCorpus.Generate();
    }

    private static double EstimateHours(IReadOnlyCollection<Knob> knobs, RunOptions template)
    {
        int arms = 1 + knobs.Sum(k => Math.Max(0, k.ResolveCandidates(Environment.ProcessorCount, 0).Count - 1)) + 1;
        return arms * template.EstimatedDuration.TotalHours;
    }

    private static bool Confirm(string question)
    {
        Console.Write($"{question} [y/N] ");
        string? answer = Console.ReadLine();
        return answer != null && answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class BenchOptions
{
    public string Mode { get; set; } = "profile";
    public string? ServerDirectory { get; set; }
    public string? LoadClientDirectory { get; set; }
    public int Players { get; set; }
    public int Windows { get; set; } = 6;
    public int WindowSeconds { get; set; } = 30;
    public int WarmupSeconds { get; set; } = 60;
    public string? CorpusPath { get; set; }

    /// <summary>Restrict the sweep to these settings. Empty means every eligible one.</summary>
    public HashSet<string> OnlyKnobs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string OutputDirectory { get; set; } = "benchmark-results";
    public string HealthHost { get; set; } = "localhost";
    public ushort HealthPort { get; set; } = 10666;
    public bool Apply { get; set; }
    public bool Yes { get; set; }
    public bool Help { get; set; }
}

internal static class CommandLine
{
    public static BenchOptions? Parse(string[] args)
    {
        var options = new BenchOptions();
        if (args.Length == 0) return null;

        int start = 0;
        if (!args[0].StartsWith('-')) { options.Mode = args[0]; start = 1; }

        for (int i = start; i < args.Length; i++)
        {
            string arg = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (arg)
            {
                case "--server": options.ServerDirectory = Next(); break;
                case "--client": options.LoadClientDirectory = Next(); break;
                case "--players": options.Players = ParseInt(Next()); break;
                case "--windows": options.Windows = ParseInt(Next()); break;
                case "--window-sec": options.WindowSeconds = ParseInt(Next()); break;
                case "--warmup-sec": options.WarmupSeconds = ParseInt(Next()); break;
                case "--corpus": options.CorpusPath = Next(); break;
                case "--knobs":
                    foreach (string name in (Next() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                        options.OnlyKnobs.Add(name.Trim());
                    break;
                case "--out": options.OutputDirectory = Next() ?? options.OutputDirectory; break;
                case "--health-host": options.HealthHost = Next() ?? options.HealthHost; break;
                case "--health-port": options.HealthPort = (ushort)ParseInt(Next()); break;
                case "--apply": options.Apply = true; break;
                case "--yes" or "-y": options.Yes = true; break;
                case "--help" or "-h": options.Help = true; break;
                default:
                    Console.Error.WriteLine($"Unknown option '{arg}'.");
                    return null;
            }
        }

        return options;
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    public static void PrintUsage()
    {
        Console.WriteLine(@"
BasisServerBenchmark - fits the server's settings to the machine it will run on.

  BasisServerBenchmark <mode> [options]

MODES
  profile     Machine facts plus the offline microbenchmarks. No server, no clients,
              about two minutes. Answers the compression budget and the useful parallel
              width without needing a crowd.

  autotune    What an operator runs on their own box. Climbs a short capacity ladder to
              find the population this machine actually serves, sweeps the settings a
              single box can measure honestly, derives the rest from the hardware, and
              writes a fitted config. Roughly 30-60 minutes.

  sweep       The full research pass. Longer ladder, every setting measured including the
              ones loopback cannot judge - those are reported with their caveat and never
              written. Hours.

  measure     One operating point, N windows, printed. For hand-driven A/B work.

OPTIONS
  --server <dir>       Directory holding BasisNetworkConsole (Release build output).
  --client <dir>       Directory holding BasisNetworkClientConsole.
  --players <n>        Pin the population instead of climbing a ladder.
  --windows <n>        Timed windows per arm. Default 6; five is the floor for a verdict.
  --window-sec <n>     Seconds per window. Default 30.
  --warmup-sec <n>     Seconds between full population and the first window. Default 60.
  --corpus <path>      Captured avatar bundles for the compression benchmark. Without one
                       a modelled corpus is generated and labelled as such.
  --knobs <a,b,c>      Sweep only these settings. Use it to re-measure one after a change
                       instead of paying for the whole matrix again.
  --out <dir>          Where reports are written. Default ./benchmark-results
  --health-host <h>    Health endpoint host. Default localhost.
  --health-port <n>    Health endpoint port. Default 10666.
  --apply              Write the recommended settings into the config files.
  --yes                Do not prompt.

NOTES
  The server's configs are backed up before anything is changed and restored on exit,
  including on Ctrl-C.

  Start the server and the load client once each before the first run, so both have
  written their default config files.

  For a captured compression corpus, run the load client with
  BASIS_BUNDLE_CAPTURE=<path> against a populated server first.
");
    }
}
