using System.Globalization;
using Basis.Benchmark.Cli;
using Basis.Benchmark.Harness;
using Basis.Benchmark.Output;
using Basis.Benchmark.Tuning;

namespace Basis.Benchmark;

public static class Program
{
    public static int Main(string[] args)
    {
        var startup = Startup.Parse(args);
        if (startup.ShowHelp) { Startup.PrintUsage(); return 0; }

        var console = new BenchmarkConsole();
        var session = new BenchmarkSession(startup.ServerDirectory, startup.LoadClientDirectory,
            startup.OutputDirectory, console.Write);

        if (!AttachAgent(console, session, startup)) return 1;

        Register(console, session, startup);

        console.Write(Banner(session));

        // Ctrl-C stops the running job rather than killing the process, because the process is
        // holding the operator's configs. A hard exit here leaves a server and a few thousand load
        // clients running with nothing owning them, and config.xml full of harness settings.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            if (console.JobRunning) console.StopJob();
            else console.Quit();
        };

        // Unattended entry point, for systemd, nohup or CI: do the whole thing and exit, no prompt.
        if (startup.RunAutoImmediately)
        {
            console.Dispatch("/auto " + startup.AutoModeArgument);
            console.WaitForJob();
            console.Dispatch("/report");
            console.Dispatch("/expect");
            // No /write here: the run writes its own profile now, and a second call would only
            // reprint the same block against the same path.
            return 0;
        }

        console.Loop();
        return 0;
    }

    /// <summary>
    /// Connects to a remote agent when one was named, and refuses to start if it cannot.
    ///
    /// Failing here rather than at the first rung is deliberate: a run that silently fell back to
    /// local load would produce packet-rate findings that look measured and are not, which is the
    /// exact confusion --agent exists to remove.
    /// </summary>
    private static bool AttachAgent(BenchmarkConsole console, BenchmarkSession session, Startup startup)
    {
        if (startup.Agent == null) return true;

        string host = startup.Agent;
        int port = Basis.Bench.Agent.BenchAgentProtocol.DefaultPort;
        int colon = host.LastIndexOf(':');
        if (colon > 0 && int.TryParse(host[(colon + 1)..], out int parsed)) { port = parsed; host = host[..colon]; }

        if (startup.ServerHost.Length == 0)
        {
            console.Write($"  --agent needs --server-host: the agent on {host} has to be told how to reach THIS");
            console.Write("  machine, and the address this box calls itself is not the one another machine uses.");
            return false;
        }

        try
        {
            var driver = new RemoteLoadClientDriver(host, port, startup.ServerHost);
            Basis.Bench.Agent.AgentResponse hello = driver.Hello();
            if (!hello.Ok)
            {
                console.Write($"  The agent at {host}:{port} refused: {hello.Error}");
                return false;
            }

            session.Driver = driver;
            console.Write($"{Environment.NewLine}  Load agent: {host}:{port} - {hello.Cores} cores, {hello.Os}");
            console.Write($"  The crowd runs there and reaches this server at {startup.ServerHost}. Packet-rate and");
            console.Write("  socket findings are measurable in this topology, unlike a single-box run.");
            return true;
        }
        catch (Exception ex)
        {
            console.Write($"  Could not reach the agent at {host}:{port}: {ex.Message}");
            console.Write("  Start BasisBenchAgent on that machine, or drop --agent to run the crowd locally.");
            return false;
        }
    }

    private static string Banner(BenchmarkSession session) =>
        "\n  Basis server benchmark\n" +
        session.Machine.Describe() +
        session.Gpu.Describe() +
        (session.HasConfigs
            ? $"  Server         {session.ServerDirectory}\n"
            : $"  ! No config under {session.ServerDirectory}/config - start the server once so it writes its\n" +
              "    defaults (the first boot runs an interactive wizard, which this cannot answer for you).\n") +
        "\n  /help for commands. /auto runs the whole thing.\n";

    private static void Register(BenchmarkConsole console, BenchmarkSession session, Startup startup)
    {
        console.Register("/help", "", "Lists these commands.",
            _ => console.WriteBlock(console.RenderHelp()));

        console.Register("/machine", "", "What this box is, and whether the kernel is limiting it.",
            _ =>
            {
                console.WriteBlock("\n" + session.Machine.Describe() + session.Gpu.Describe());
                if (session.Machine.Kernel?.RemediationSnippet() is { } fix)
                {
                    console.Write("  The kernel is clamping the 32 MB socket buffer the server asks for. Fix with:");
                    foreach (string line in fix.Split('\n')) console.Write("      " + line);
                }
            });

        console.Register("/profile", "", "Offline benchmarks only - core scaling, codec cost, compute offload. ~2 min, no server.",
            _ => console.StartJob("profile", session.RunOffline));

        console.Register("/measure", "[players]", "One operating point at the given population, printed.",
            args =>
            {
                if (!TryPlayers(args, console, out int players)) return;
                if (!Ready(session, console)) return;
                console.StartJob($"measure {players}", cancel => session.RunMeasure(players, cancel));
            });

        console.Register("/auto", "[quick|medium|long] [full]",
            "Measure, fit, and write the profile. quick ~5 min, medium ~15 min, long ~2 h. Default medium.",
            args =>
            {
                if (!Ready(session, console)) return;

                AutoMode mode = AutoMode.Medium;
                bool untrusted = false;
                foreach (string arg in args)
                {
                    if (Enum.TryParse(arg, ignoreCase: true, out AutoMode parsed)) mode = parsed;
                    else if (arg.Equals("full", StringComparison.OrdinalIgnoreCase)) untrusted = true;
                    else { console.Write($"  '{arg}' is not a mode. Use quick, medium or long, optionally with 'full'."); return; }
                }

                session.IncludeUntrusted = untrusted;
                console.Write(mode switch
                {
                    AutoMode.Quick =>
                        "  Quick: offline benchmarks, one load point, one join burst. Gives the codec settings,\n" +
                        "  the pass width and the auth window - but one population is a point, not a curve, so it\n" +
                        "  cannot say what limits this box.",
                    AutoMode.Medium =>
                        "  Medium: adds a three-rung ladder, which is the fewest a curve can be fitted through.\n" +
                        "  This is where the player cap and the binding constraint become real.",
                    _ =>
                        "  Long: full ladder plus the A/B setting sweep. The sweep is most of the wall time, and on\n" +
                        "  a box with headroom it usually finds nothing - it earns its cost on one that is working.",
                });
                if (untrusted)
                    console.Write("  Including the settings loopback cannot judge. Measured and reported, never written.");

                console.StartJob($"auto {mode}".ToLowerInvariant(), cancel => session.RunAuto(mode, cancel));
            });

        console.Register("/burst", "[players]", "Everyone connects at once, the way they do after a restart.",
            args =>
            {
                if (!TryPlayers(args, console, out int players)) return;
                if (!Ready(session, console)) return;
                console.Write("  This measures admission, not throughput - a box that serves a crowd comfortably can");
                console.Write("  still be unable to get that crowd in, and a restart is where you find out.");
                console.StartJob($"burst {players}", cancel => session.RunBurst(players, cancel));
            });

        console.Register("/status", "", "What is running, and how far along.",
            _ => console.Write(console.JobRunning
                ? $"  '{console.JobName}' running for {console.JobElapsed.TotalMinutes:F1} min. /stop to end it."
                : "  Idle." + (session.Recommendations.Count > 0
                    ? $" {session.Recommendations.Count(r => r.IsChange && r.Writable)} change(s) ready to /write."
                    : "")));

        console.Register("/stop", "", "Stops the running job. Finishes the current window first, then restores configs.",
            _ => console.StopJob());

        console.Register("/report", "", "Prints the full report and saves it.",
            _ =>
            {
                BenchmarkReport report = session.BuildReport();
                console.WriteBlock("\n" + Report.Render(report));
                try
                {
                    Report.WriteTo(session.OutputDirectory, report);
                    console.Write($"  Saved to {session.OutputDirectory}");
                }
                catch (Exception ex) { console.Write($"  Could not save the report: {ex.Message}"); }
            });

        console.Register("/write", "[path]", "Writes the tuning profile the server reads on its next boot.",
            args =>
            {
                if (session.Recommendations.Count == 0)
                {
                    console.Write("  Nothing measured yet. Run /profile or /auto first.");
                    return;
                }
                string? destination = args.Length > 0 ? args[0] : null;
                console.WriteBlock(TuningProfileWriter.Write(session, destination));
            });

        console.Register("/expect", "", "Plain-English summary of what this machine can do. Also written to disc.",
            _ =>
            {
                if (session.Capability == null)
                {
                    console.Write("  Nothing measured under load yet - /auto builds this. /profile alone cannot:");
                    console.Write("  player counts come from watching the machine actually fill up.");
                    return;
                }

                console.WriteBlock("\n" + CapabilitySummary.Render(session, session.Capability));
                try
                {
                    string path = CapabilitySummary.WriteTo(session.OutputDirectory, session, session.Capability);
                    console.Write($"  Saved to {path}");
                    console.Write($"  and to {Path.Combine(session.OutputDirectory, CapabilitySummary.FileName)}");
                }
                catch (Exception ex) { console.Write($"  Could not save it: {ex.Message}"); }
            });

        console.Register("/findings", "", "The recommendations so far, without the whole report.",
            _ =>
            {
                if (session.Recommendations.Count == 0) { console.Write("  Nothing measured yet."); return; }
                console.Write("");
                foreach (Recommendation r in session.Recommendations.OrderByDescending(r => r.IsChange))
                {
                    string mark = !r.IsChange ? "  =" : r.Writable ? "  +" : "  ?";
                    console.Write($"{mark} {r.Setting}: {r.CurrentValue}" +
                                  (r.IsChange ? $" -> {r.ProposedValue}" : "") + $"   [{r.Evidence}]");
                }
                console.Write("\n  + will be written, ? was measured but the topology cannot judge it, = unchanged.");
            });

        console.Register("/show", "", "Run parameters.",
            _ =>
            {
                console.Write("");
                var rows = session.Settings.Describe().ToList();
                int width = rows.Max(r => r.Value.Length);
                foreach ((string name, string value, string note) in rows)
                    console.Write($"  {name,-13} {value.PadRight(width)}   {note}");
                console.Write("\n  /set <name> <value> to change one. They apply to the next job, not a running one.");
            });

        console.Register("/set", "<name> <value>", "Changes a run parameter.",
            args =>
            {
                if (args.Length < 2) { console.Write("  /set <name> <value>. Type /show for the names."); return; }
                session.Settings.TrySet(args[0], string.Join(' ', args.Skip(1)), out string message);
                console.Write("  " + message);
            });

        console.Register("/gpu", "[index|name]", "Compute devices on this box, and which one to measure.",
            args =>
            {
                if (args.Length > 0)
                {
                    session.Settings.ComputeDevice = string.Join(" ", args);
                    console.Write($"  ComputeDevice = '{session.Settings.ComputeDevice}' (copy this into config.xml to match).");
                }
                console.WriteBlock(Environment.NewLine + session.Gpu.Describe());
                if (session.Settings.ComputeDevice.Length > 0)
                    console.Write($"  Measuring: '{session.Settings.ComputeDevice}'");
            });

        console.Register("/quit", "", "Stops anything running and exits.", _ => console.Quit());
        console.Register("/exit", "", "Same as /quit.", _ => console.Quit());
    }

    private static bool Ready(BenchmarkSession session, BenchmarkConsole console)
    {
        if (session.HasConfigs) return true;
        console.Write($"  No config under {session.ServerDirectory}/config. Start the server once by hand so it");
        console.Write("  writes its defaults, then come back. Point elsewhere with --server if it is not there.");
        return false;
    }

    private static bool TryPlayers(string[] args, BenchmarkConsole console, out int players)
    {
        players = 500;
        if (args.Length == 0) return true;
        if (int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out players) && players > 0) return true;
        console.Write($"  '{args[0]}' is not a player count.");
        return false;
    }
}

/// <summary>
/// The little that has to be settled before the console opens: where the binaries are, and whether
/// there is a person to talk to. Everything else is a command.
/// </summary>
internal sealed class Startup
{
    public string ServerDirectory { get; private set; } = "";
    public string LoadClientDirectory { get; private set; } = "";
    public string OutputDirectory { get; private set; } = "benchmark-results";
    public bool RunAutoImmediately { get; private set; }

    /// <summary>Mode words passed through to /auto, e.g. "medium" or "long full".</summary>
    public string AutoModeArgument { get; private set; } = "medium";
    public bool ShowHelp { get; private set; }

    /// <summary>host[:port] of a BasisBenchAgent that should generate the load, or null for local.</summary>
    public string? Agent { get; private set; }

    /// <summary>
    /// How the AGENT's machine should reach this server. Only meaningful with --agent, and it
    /// cannot be inferred: the address this box calls itself is rarely the one another machine
    /// uses to find it.
    /// </summary>
    public string ServerHost { get; private set; } = "";

    public static Startup Parse(string[] args)
    {
        var startup = new Startup();
        for (int i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i])
            {
                case "--server": startup.ServerDirectory = Next() ?? ""; break;
                case "--client": startup.LoadClientDirectory = Next() ?? ""; break;
                case "--out": startup.OutputDirectory = Next() ?? startup.OutputDirectory; break;
                case "--agent": startup.Agent = Next(); break;
                case "--server-host": startup.ServerHost = Next() ?? startup.ServerHost; break;
                case "--auto":
                    startup.RunAutoImmediately = true;
                    // An optional mode word may follow. Peeked rather than consumed unconditionally,
                    // so "--auto --server <dir>" still parses the way it reads.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        startup.AutoModeArgument = args[++i];
                    break;
                case "--help" or "-h": startup.ShowHelp = true; break;
            }
        }

        // Both binaries sit at a known offset from this one, so the common case needs no arguments
        // at all - which matters when the tool is driven over SSH on a box where nobody wants to
        // type absolute paths. There are two layouts and they are not alike.
        var here = new DirectoryInfo(AppContext.BaseDirectory);

        // Shipped: <server>/benchmark/ with the load client under <server>/benchmark/loadclient/.
        // Recognised by the server binary sitting in the parent, which is unambiguous.
        DirectoryInfo? parent = here.Parent;
        bool shipped = parent != null &&
                       (File.Exists(Path.Combine(parent.FullName, "BasisNetworkConsole.exe")) ||
                        File.Exists(Path.Combine(parent.FullName, "BasisNetworkConsole")));

        if (shipped)
        {
            if (startup.ServerDirectory.Length == 0) startup.ServerDirectory = parent!.FullName;
            if (startup.LoadClientDirectory.Length == 0)
                startup.LoadClientDirectory = Path.Combine(here.FullName, "loadclient");
            return startup;
        }

        // Development: .../BasisServerBenchmark/bin/<Configuration>/net10.0/ - the configuration is
        // one level up, the solution four.
        string configuration = here.Parent?.Name ?? "Release";
        string? solution = here.Parent?.Parent?.Parent?.Parent?.FullName;

        if (startup.ServerDirectory.Length == 0 && solution != null)
            startup.ServerDirectory = Path.Combine(solution, "BasisServerConsole", "bin", configuration, "net10.0");
        if (startup.LoadClientDirectory.Length == 0 && solution != null)
            startup.LoadClientDirectory = Path.Combine(solution, "BasisNetworkClientConsole",
                "BasisNetworkClientConsole", "bin", configuration, "net10.0");

        return startup;
    }

    public static void PrintUsage() => Console.WriteLine(@"
BasisServerBenchmark - fits the server's settings to the machine it runs on.

  BasisServerBenchmark [--server <dir>] [--client <dir>] [--out <dir>] [--auto]

Run it with no arguments: the server and load-client directories are found relative to this
binary, and everything else is a console command. Type /help once it opens.

  --server <dir>   Directory holding BasisNetworkConsole. Found automatically in a normal build.
  --client <dir>   Directory holding BasisNetworkClientConsole. Likewise.
  --out <dir>      Where reports are written. Default ./benchmark-results
  --auto           Run /auto, /report and /write without a prompt, then exit. For systemd,
                   nohup and CI, where there is no terminal to type into.

Before the first run, start the server and the load client once each by hand so both write their
default config files - a server's first boot runs an interactive wizard this cannot answer.

The result is config/tuning-profile.xml next to the server's config. The server applies it on its
next boot, folds the values into config.xml, and stamps it so a restart does not re-apply it.
");
}
