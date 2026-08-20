using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Basis.Bench.Agent;
using Basis.Benchmark.Machine;
using Basis.Benchmark.Measure;

namespace Basis.Benchmark.Harness;

/// <summary>What one arm of a comparison produced.</summary>
public sealed class RunResult
{
    public required string Label { get; init; }
    public required int RequestedPlayers { get; init; }
    public required IReadOnlyDictionary<string, string> Settings { get; init; }
    public required IReadOnlyList<MeasurementWindow> Windows { get; init; }
    public required bool Completed { get; init; }
    public required string? Failure { get; init; }

    /// <summary>Peak clients the server reported connected. Below the request means admission failed.</summary>
    public required int PeakConnected { get; init; }

    /// <summary>Share of simulated voice frames a receiver actually got, from the load client. -1 when unknown.</summary>
    public required double VoiceDeliveredFraction { get; init; }

    public IReadOnlyList<double> Series(Func<MeasurementWindow, double> select) =>
        Windows.Select(select).ToList();

    public double Median(Func<MeasurementWindow, double> select) => Stats.Median(Series(select));

    /// <summary>
    /// Whether this arm is even eligible to be compared.
    ///
    /// <para>A run that could not seat its crowd, or that dropped voice, has not produced a slower
    /// version of the same result — it has produced a different result. Comparing it on delivered
    /// quality would let a configuration win by refusing half the connections, since the players it
    /// never admitted cost nothing to serve.</para>
    /// </summary>
    public bool IsValid(out string? reason)
    {
        reason = null;
        if (!Completed) { reason = Failure ?? "run did not complete"; return false; }
        if (Windows.Count < Stats.MinimumWindows) { reason = $"only {Windows.Count} windows closed"; return false; }
        if (PeakConnected < RequestedPlayers * 0.98)
        {
            reason = $"only {PeakConnected} of {RequestedPlayers} clients connected";
            return false;
        }
        double voiceDrops = Median(w => w.VoiceDropsPerSecond);
        if (voiceDrops > 1)
        {
            reason = $"dropping {voiceDrops:N0} voice packets/s - audio nobody heard";
            return false;
        }
        return true;
    }
}

/// <summary>
/// Drives one measurement: configure, start the server, seat the crowd, close N windows, tear down.
///
/// <para>Every arm gets a full restart, including the ones whose settings are applied live. Two
/// reasons, both learned the hard way. Boot-time settings genuinely cannot change any other way —
/// SO_REUSEPORT has to be on the primary socket before bind, so the socket count is fixed at
/// <c>Start()</c> and a config edit under a running server changes nothing while appearing to.
/// And the runtime's own adaptive state — the core allocator's measured ceilings, the slicing
/// controller's position, the packet pool's high-water mark — carries across a reconfiguration, so
/// an arm run second inherits the arm run first and the comparison measures the order.</para>
/// </summary>
public sealed class LoadRunner
{
    private static readonly Regex VoiceLine = new(@"\[VOICE\] delivered ([0-9]+(?:\.[0-9]+)?)%", RegexOptions.Compiled);

    private readonly ConfigPatcher _configs;
    private readonly Action<string> _log;

    public LoadRunner(ConfigPatcher configs, Action<string> log)
    {
        _configs = configs;
        _log = log;
    }

    public RunResult Run(RunOptions options, CancellationToken cancel)
    {
        Process? server = null;
        ILoadClientDriver driver = options.Driver ?? new LocalLoadClientDriver();
        bool ownsDriver = options.Driver == null;
        int peakConnected = 0;
        var windows = new List<MeasurementWindow>();

        try
        {
            // Reset first, then apply. Apply only writes the settings it is given, so without the
            // reset this arm would silently inherit whatever the previous arm left behind and the
            // comparison would be against a moving baseline.
            _configs.ResetToBackup();

            // Applied to the files before boot, so what the server reads is what the arm declares.
            // The transport half is unreachable through environment overrides, so this is the only
            // mechanism that covers both.
            _configs.Apply(options.Settings);
            ApplyHarnessDefaults(options);

            if (!PortIsClear(options, out string occupied))
                return Failed(options, occupied, peakConnected, driver.VoiceDelivered, windows);

            _log($"  [{options.Label}] starting server...");
            server = StartServer(options);
            if (!WaitForHealth(options, server, TimeSpan.FromSeconds(60), cancel, out string startupFailure))
                return Failed(options, startupFailure, peakConnected, driver.VoiceDelivered, windows);

            _log($"  [{options.Label}] starting {options.RequestedPlayersLabel()} load clients on {driver.Where}...");
            driver.Start(options);

            if (!WaitForPopulation(options, ref peakConnected, cancel))
                return Failed(options, $"only {peakConnected} of {options.Players} clients connected within {options.ConnectTimeout.TotalMinutes:F0} min",
                    peakConnected, driver.VoiceDelivered, windows);

            _log($"  [{options.Label}] {peakConnected} connected; warming up {options.Warmup.TotalSeconds:F0}s " +
                 "(the slicing controller oscillates, so this is not optional)...");
            Sleep(options.Warmup, cancel);

            var serverCpu = new ProcessCpu(server);

            for (int i = 0; i < options.Windows && !cancel.IsCancellationRequested; i++)
            {
                MeasurementWindow? window = CloseWindow(options, serverCpu, driver, cancel);
                if (window == null) break;
                windows.Add(window);
                peakConnected = Math.Max(peakConnected, window.Players);
                _log($"  [{options.Label}] window {i + 1}/{options.Windows}: " +
                     $"{window.DeliveredPairHz:F2} Hz/pair, {window.ServerCores:F2} cores, " +
                     $"{window.MegabytesOutPerSecond:N0} MB/s, delivery {window.DeliveryRatio:P1}, slice {window.SliceCount:F1}");
            }

            return new RunResult
            {
                Label = options.Label,
                RequestedPlayers = options.Players,
                Settings = options.Settings,
                Windows = windows,
                Completed = windows.Count > 0,
                Failure = windows.Count > 0 ? null : "no windows closed",
                PeakConnected = peakConnected,
                VoiceDeliveredFraction = driver.VoiceDelivered,
            };
        }
        catch (Exception ex)
        {
            return Failed(options, ex.Message, peakConnected, driver.VoiceDelivered, windows);
        }
        finally
        {
            driver.Stop();
            if (ownsDriver) driver.Dispose();
            Kill(server);
            // The server holds its health port and log files briefly after exit; the next arm binds
            // the same port, so give the OS a moment rather than racing it.
            Thread.Sleep(3000);
        }
    }

    /// <summary>
    /// Starts a server and throws the whole crowd at it at once, sampling the population as it
    /// fills.
    ///
    /// <para>Separate from <see cref="Run"/> because it wants the opposite of what that does. There
    /// is no warmup — the burst IS the measurement, and it is over before a warmup would have
    /// finished — and no windows, because nothing here is a steady-state rate. What it needs
    /// instead is a fine-grained sample of the ramp, which the normal one-second window cadence is
    /// far too coarse to see.</para>
    /// </summary>
    public AdmissionResult RunBurst(RunOptions options, Action<double, int> onSample, CancellationToken cancel)
    {
        Process? server = null;
        Process? client = null;

        try
        {
            _configs.ResetToBackup();
            _configs.Apply(options.Settings);
            ApplyHarnessDefaults(options);
            WriteLoadClientConfig(options);

            if (!PortIsClear(options, out string occupied)) return FailedBurst(options, occupied);

            server = StartServer(options);
            if (!WaitForHealth(options, server, TimeSpan.FromSeconds(60), cancel, out string startupFailure))
                return FailedBurst(options, startupFailure);

            var clock = Stopwatch.StartNew();
            client = StartLoadClient(options, _ => { });

            int peak = 0;
            double peakAt = 0;
            DateTime lastProgress = DateTime.UtcNow;

            while (!cancel.IsCancellationRequested)
            {
                Thread.Sleep(SampleInterval);

                HealthSample? sample = HealthPoller.TryRead(options.HealthUrl);
                double seconds = clock.Elapsed.TotalSeconds;
                int connected = sample?.Visitors ?? peak;

                onSample(seconds, connected);

                if (connected > peak)
                {
                    peak = connected;
                    peakAt = seconds;
                    lastProgress = DateTime.UtcNow;
                }

                if (peak >= options.Players) break;

                // A burst that has stopped climbing is finished, whatever it reached. Waiting out
                // the connect timeout on every stalled run costs minutes per attempt and tells us
                // nothing the stall did not already.
                if (DateTime.UtcNow - lastProgress > StallTimeout) break;
                if (clock.Elapsed > options.ConnectTimeout) break;
            }

            return new AdmissionResult
            {
                Requested = options.Players,
                Admitted = peak,
                SecondsToFull = peakAt,
                Curve = Array.Empty<AdmissionSample>(),
                Completed = true,
                Failure = null,
            };
        }
        catch (Exception ex)
        {
            return FailedBurst(options, ex.Message);
        }
        finally
        {
            Kill(client);
            Kill(server);
            Thread.Sleep(3000);
        }
    }

    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private static AdmissionResult FailedBurst(RunOptions o, string reason) => new()
    {
        Requested = o.Players,
        Admitted = 0,
        SecondsToFull = 0,
        Curve = Array.Empty<AdmissionSample>(),
        Completed = false,
        Failure = reason,
    };

    private static RunResult Failed(RunOptions o, string reason, int peak, double voice, IReadOnlyList<MeasurementWindow> windows) =>
        new()
        {
            Label = o.Label,
            RequestedPlayers = o.Players,
            Settings = o.Settings,
            Windows = windows,
            Completed = false,
            Failure = reason,
            PeakConnected = peak,
            VoiceDeliveredFraction = voice,
        };

    /// <summary>
    /// Settings the harness needs regardless of what is being swept.
    ///
    /// The BSR profiling flag is the important one: without it the health endpoint serves no
    /// window object at all, so send rates, tick breakdown and bundle figures are simply absent and
    /// every derived quality number silently reads zero.
    /// </summary>
    private void ApplyHarnessDefaults(RunOptions options)
    {
        var harness = new Dictionary<string, string>
        {
            ["EnableStatistics"] = "true",
            ["HealthIncludeBSRProfiling"] = "true",
            ["EnableConsole"] = "false",
            ["HealthCheckHost"] = options.HealthHost,
            ["HealthCheckPort"] = options.HealthPort.ToString(CultureInfo.InvariantCulture),
            ["HealthPath"] = options.HealthPath,
        };

        // Never override something the arm is deliberately setting.
        foreach (string key in options.Settings.Keys) harness.Remove(key);
        _configs.Apply(harness);
    }

    private Process StartServer(RunOptions options)
    {
        string exe = ExecutablePath(options.ServerDirectory, "BasisNetworkConsole");
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = options.ServerDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        Process p = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {exe}");
        // Drained but discarded: an unread pipe fills its buffer and blocks the server mid-write,
        // which would show up as a mysterious stall in the middle of a measurement window.
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private Process StartLoadClient(RunOptions options, Action<string> onLine)
    {
        string exe = ExecutablePath(options.LoadClientDirectory, "BasisNetworkClientConsole");
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = options.LoadClientDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        Process p = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {exe}");
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    /// <summary>
    /// Points the load client at this run: player count, and the server it should connect to.
    ///
    /// Voice stays on. A silent crowd is not a cheaper version of a real one — voice is a fan-in
    /// that scales with how many talkers are audible, and it is the traffic whose loss is
    /// unrecoverable, so a run without it measures neither the load nor the failure mode that
    /// matters.
    /// </summary>
    private static void WriteLoadClientConfig(RunOptions options)
    {
        string path = Path.Combine(options.LoadClientDirectory, "ClientSimConfig.xml");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Load client config not found at {path}. Run BasisNetworkClientConsole once so it writes its defaults.", path);

        var doc = System.Xml.Linq.XDocument.Load(path, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        System.Xml.Linq.XElement root = doc.Root ?? throw new InvalidDataException($"{path} has no root element.");

        void Set(string name, string value)
        {
            System.Xml.Linq.XElement? el = root.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            if (el != null) el.Value = value;
            else root.Add(new System.Xml.Linq.XElement(name, value));
        }

        Set("ClientCount", options.Players.ToString(CultureInfo.InvariantCulture));
        Set("SimulateVoice", "true");
        if (options.ClientConnectIntervalMs is { } interval)
            Set("ClientConnectIntervalMs", interval.ToString(CultureInfo.InvariantCulture));

        string temp = path + ".benchtmp";
        doc.Save(temp);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Waits for the server this run started to become healthy.
    ///
    /// <para>⚠️ <b>The liveness check is not belt-and-braces, it is the whole point.</b> A health
    /// endpoint answering does not mean OUR server answered it. If another instance already holds
    /// the UDP port, the one we started fails to bind and exits within a second — and the port it
    /// could not take belongs to a server that is still cheerfully serving /health. Without this
    /// check the run proceeds against a process it does not own: CPU comes back unreadable, the
    /// profiler fields are absent because that server was configured by somebody else, and the
    /// numbers that do arrive describe a different server entirely. Observed exactly that,
    /// reporting 255 MB/s alongside NaN cores and zero slicing.</para>
    /// </summary>
    private bool WaitForHealth(RunOptions options, Process server, TimeSpan timeout, CancellationToken cancel,
        out string failure)
    {
        failure = "";
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && !cancel.IsCancellationRequested)
        {
            bool exited;
            try { exited = server.HasExited; }
            catch { exited = true; }

            if (exited)
            {
                failure =
                    "the server exited during startup. The usual cause is another instance already holding the " +
                    $"port - check for a running BasisNetworkConsole, or a stale one from an earlier run. Note that " +
                    $"{options.HealthUrl} may still answer, because that is the OTHER server replying.";
                return false;
            }

            if (HealthPoller.TryRead(options.HealthUrl) is { Ready: true }) return true;
            Thread.Sleep(500);
        }

        failure = "server never reported healthy";
        return false;
    }

    /// <summary>
    /// Refuses to start when something is already answering on the health port.
    ///
    /// <para>⚠️ <b>Checked before launching, because afterwards it is too late to tell.</b> Waiting
    /// for health and checking the process is alive is not enough: a server that cannot bind takes
    /// a moment to notice and exit, while the instance already holding the port answers instantly,
    /// so the wait returns success on the first poll against a process the harness does not own.
    /// The race is only closable from this side of the launch — if anything answers here, it is by
    /// definition not ours.</para>
    ///
    /// <para>The symptom without this is quiet and convincing: real throughput and delivery
    /// figures, alongside NaN CPU and zero slicing, because the numbers come from a stranger's
    /// server that was never configured for profiling.</para>
    /// </summary>
    private bool PortIsClear(RunOptions options, out string failure)
    {
        failure = "";
        if (HealthPoller.TryRead(options.HealthUrl) == null) return true;

        failure =
            $"something is already serving {options.HealthUrl}. That is another Basis server, and this run would " +
            "measure it instead of the one it starts - the server it launches cannot bind the port and exits, " +
            "leaving throughput figures from a process with no profiling enabled and no CPU this tool can read. " +
            "Stop the running server (or point --health-port elsewhere) and try again.";
        return false;
    }

    private bool WaitForPopulation(RunOptions options, ref int peak, CancellationToken cancel)
    {
        DateTime deadline = DateTime.UtcNow + options.ConnectTimeout;
        int stalledFor = 0;
        int previous = 0;

        while (DateTime.UtcNow < deadline && !cancel.IsCancellationRequested)
        {
            HealthSample? sample = HealthPoller.TryRead(options.HealthUrl);
            if (sample != null)
            {
                peak = Math.Max(peak, sample.Visitors);
                if (sample.Visitors >= options.Players) return true;

                // A ramp that stops climbing has failed, and waiting out the full timeout on it
                // wastes minutes per arm. Admission starvation is the usual cause: a large join
                // burst can leave clients unable to finish the handshake inside the auth window,
                // and the server logs a rejection that says nothing about why.
                stalledFor = sample.Visitors == previous ? stalledFor + 1 : 0;
                previous = sample.Visitors;
                if (stalledFor > 60) return false;
            }
            Thread.Sleep(1000);
        }
        return false;
    }

    private MeasurementWindow? CloseWindow(RunOptions options, ProcessCpu serverCpu, ILoadClientDriver driver, CancellationToken cancel)
    {
        HealthSample? start = HealthPoller.TryRead(options.HealthUrl);
        if (start == null) return null;

        long kernelDropsStart = KernelTuning.ReadUdpReceiveBufferErrors();
        serverCpu.Reset();
        driver.SampleCores();

        // Sampled once a second across the window rather than only at its edges. The instantaneous
        // fields - slice count, tick time, shed tier - oscillate, so an edge reading records a
        // phase of the controller instead of the window's behaviour.
        var inner = new List<HealthSample> { start };
        DateTime end = DateTime.UtcNow + options.WindowLength;
        while (DateTime.UtcNow < end && !cancel.IsCancellationRequested)
        {
            Thread.Sleep(1000);
            HealthSample? s = HealthPoller.TryRead(options.HealthUrl);
            if (s != null) inner.Add(s);
        }

        HealthSample? finish = inner.Count > 1 ? inner[^1] : null;
        if (finish == null) return null;

        double serverCores = serverCpu.SampleCores();
        double clientCores = driver.SampleCores();

        long kernelDropsEnd = KernelTuning.ReadUdpReceiveBufferErrors();
        double kernelDropRate = kernelDropsStart >= 0 && kernelDropsEnd >= kernelDropsStart
            ? (kernelDropsEnd - kernelDropsStart) / options.WindowLength.TotalSeconds
            : -1;

        return MeasurementWindow.Between(start, finish, inner, serverCores, clientCores, kernelDropRate);
    }

    private static void Sleep(TimeSpan duration, CancellationToken cancel)
    {
        DateTime end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end && !cancel.IsCancellationRequested) Thread.Sleep(500);
    }

    private static string ExecutablePath(string directory, string baseName)
        => LaunchTarget.Resolve(directory, baseName);

    private static void Kill(Process? p)
    {
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(15000);
            }
        }
        catch { /* already gone */ }
        finally { try { p.Dispose(); } catch { } }
    }
}

internal static class RunOptionsExtensions
{
    public static string RequestedPlayersLabel(this RunOptions o) => o.Players.ToString("N0", CultureInfo.InvariantCulture);
}
