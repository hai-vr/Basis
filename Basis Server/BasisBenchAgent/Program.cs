using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Basis.Bench.Agent;

/// <summary>
/// Runs the crowd on a machine that is not the server's.
///
/// <para><b>Why this exists.</b> On a single box the load clients and the server compete for the
/// same cores, cache and memory bandwidth — measured at 3.26 cores for the client alone at 1,000
/// players — and the traffic never crosses a NIC, which makes every packet-rate and socket finding
/// unmeasurable. Both problems have the same fix: put the crowd somewhere else. This is the
/// somewhere else. It owns nothing and decides nothing; the benchmark still runs the experiment, and
/// this just starts, stops and reports on load clients when told to.</para>
///
/// <para>Deliberately tiny. It runs on a machine whose whole job is to generate load, so anything it
/// spends is subtracted from the load it can generate — one socket, one child process, and a status
/// reply built from counters that were already being kept.</para>
/// </summary>
public static class Program
{
    private static readonly Regex VoiceLine =
        new(@"\[VOICE\] delivered ([0-9]+(?:\.[0-9]+)?)%", RegexOptions.Compiled);

    private static readonly object Gate = new();
    private static Process? _client;
    private static ProcessCpuSampler? _cpu;
    private static double _voiceDelivered = -1;
    private static string _clientDirectory = "";

    public static int Main(string[] args)
    {
        int port = BenchAgentProtocol.DefaultPort;
        string bind = "0.0.0.0";

        for (int i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i])
            {
                case "--port": int.TryParse(Next(), out port); break;
                case "--bind": bind = Next() ?? bind; break;
                case "--client": _clientDirectory = Next() ?? ""; break;
                case "--help" or "-h": PrintUsage(); return 0;
            }
        }

        if (_clientDirectory.Length == 0) _clientDirectory = DiscoverClientDirectory();

        if (LaunchTarget.Find(_clientDirectory, "BasisNetworkClientConsole") == null)
        {
            Console.Error.WriteLine($"No BasisNetworkClientConsole under '{_clientDirectory}'. Pass --client <dir>.");
            return 1;
        }

        var listener = new TcpListener(IPAddress.Parse(bind), port);
        listener.Start();

        Console.WriteLine($"Basis benchmark agent listening on {bind}:{port}");
        Console.WriteLine($"  load client: {_clientDirectory}");
        Console.WriteLine($"  {Environment.ProcessorCount} cores, {RuntimeOs()}");
        Console.WriteLine("  Ctrl-C to stop. Any running load clients are stopped with it.");

        Console.CancelKeyPress += (_, e) => { e.Cancel = false; StopClient(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopClient();

        while (true)
        {
            try
            {
                using TcpClient connection = listener.AcceptTcpClient();
                Serve(connection);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  connection failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Serves one connection until it closes.
    ///
    /// One at a time, on the accepting thread. Two benchmarks driving the same crowd would each
    /// think they owned it, and the failure would look like a bad measurement rather than a
    /// mistake — the same reason the benchmark refuses to run two jobs at once.
    /// </summary>
    private static void Serve(TcpClient connection)
    {
        using NetworkStream stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

        Console.WriteLine($"  connected: {connection.Client.RemoteEndPoint}");

        while (reader.ReadLine() is { } line)
        {
            if (line.Trim().Length == 0) continue;

            AgentResponse response;
            try
            {
                AgentRequest? request = JsonSerializer.Deserialize<AgentRequest>(line);
                response = request == null
                    ? new AgentResponse { Ok = false, Error = "unparseable request" }
                    : Handle(request);
            }
            catch (Exception ex)
            {
                response = new AgentResponse { Ok = false, Error = ex.Message };
            }

            writer.WriteLine(JsonSerializer.Serialize(response));
        }

        Console.WriteLine("  disconnected");

        // A benchmark that dies mid-run must not leave a thousand clients hammering the server with
        // nothing owning them. The control connection closing is the only signal available for that.
        StopClient();
    }

    private static AgentResponse Handle(AgentRequest request)
    {
        if (request.Version != BenchAgentProtocol.Version)
            return new AgentResponse
            {
                Ok = false,
                Error = $"protocol version {request.Version} against this agent's {BenchAgentProtocol.Version}. " +
                        "Update whichever side is older - a mismatched pair refuses rather than guessing.",
            };

        switch (request.Command.ToLowerInvariant())
        {
            case "hello":
                return new AgentResponse
                {
                    Ok = true,
                    Agent = "BasisBenchAgent",
                    Cores = Environment.ProcessorCount,
                    Os = RuntimeOs(),
                };

            case "start":
                return StartClient(request);

            case "status":
                lock (Gate)
                {
                    bool running = _client is { HasExited: false };
                    return new AgentResponse
                    {
                        Ok = true,
                        Running = running,
                        ClientCores = _cpu?.SampleCores() ?? -1,
                        VoiceDelivered = Volatile.Read(ref _voiceDelivered),
                    };
                }

            case "stop":
                StopClient();
                return new AgentResponse { Ok = true };

            default:
                return new AgentResponse { Ok = false, Error = $"unknown command '{request.Command}'" };
        }
    }

    private static AgentResponse StartClient(AgentRequest request)
    {
        StopClient();

        if (request.Clients <= 0) return new AgentResponse { Ok = false, Error = "clients must be positive" };
        if (request.Host.Length == 0) return new AgentResponse { Ok = false, Error = "host is required" };

        // Resolves the path AND repairs a missing execute bit, which is the usual reason a load
        // client will not start on a Linux agent box. The error, if any, names the fix.
        if (!LaunchTarget.TryResolve(_clientDirectory, "BasisNetworkClientConsole", out string? exe, out string? resolveError))
            return new AgentResponse { Ok = false, Error = resolveError ?? $"no load client under '{_clientDirectory}'" };

        try
        {
            WriteConfig(request);

            var info = new ProcessStartInfo(exe)
            {
                WorkingDirectory = _clientDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Open only so the client can be asked to leave the server before it is killed.
                RedirectStandardInput = true,
            };

            Process process = Process.Start(info) ?? throw new InvalidOperationException("could not start the load client");
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                Match m = VoiceLine.Match(e.Data);
                if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                    Volatile.Write(ref _voiceDelivered, pct / 100.0);
            };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            lock (Gate)
            {
                _client = process;
                _cpu = new ProcessCpuSampler(process);
                Volatile.Write(ref _voiceDelivered, -1);
            }

            Console.WriteLine($"  started {request.Clients} clients -> {request.Host}:{request.Port} " +
                              $"(connect interval {request.ConnectIntervalMs} ms)");
            return new AgentResponse { Ok = true };
        }
        catch (Exception ex)
        {
            return new AgentResponse { Ok = false, Error = ex.Message };
        }
    }

    /// <summary>Points the load client at this run. Patches in place so its other settings survive.</summary>
    private static void WriteConfig(AgentRequest request)
    {
        string path = Path.Combine(_clientDirectory, "ClientSimConfig.xml");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"No ClientSimConfig.xml at {path}. Run the load client once by hand so it writes its defaults.", path);

        XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        XElement root = doc.Root ?? throw new InvalidDataException($"{path} has no root element.");

        void Set(string name, string value)
        {
            XElement? element = root.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            if (element != null) element.Value = value;
            else root.Add(new XElement(name, value));
        }

        Set("ClientCount", request.Clients.ToString(CultureInfo.InvariantCulture));
        Set("Ip", request.Host);
        Set("Port", request.Port.ToString(CultureInfo.InvariantCulture));
        Set("ClientConnectIntervalMs", request.ConnectIntervalMs.ToString(CultureInfo.InvariantCulture));
        Set("SimulateVoice", "true");

        string temp = path + ".agenttmp";
        doc.Save(temp);
        File.Move(temp, path, overwrite: true);
    }

    private static void StopClient()
    {
        Process? process;
        lock (Gate)
        {
            process = _client;
            _client = null;
            _cpu = null;
        }

        if (process == null) return;
        try
        {
            if (!process.HasExited && !TryStopGracefully(process, TimeSpan.FromSeconds(10)))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(15000);
            }
        }
        catch { /* already gone */ }
        finally
        {
            try { process.Dispose(); } catch { }
            Console.WriteLine("  load clients stopped");
        }
    }

    /// <summary>
    /// Asks the load client to leave the server before killing it, and returns whether it did.
    ///
    /// <para>Killing a process runs no managed code, so every client it was simulating vanishes
    /// without a word and the server holds each one until it times out — which pollutes the next
    /// run's population and its admission timings. Writing to stdin is the one graceful stop that
    /// works on both platforms: there is no SIGTERM to send on Windows, and a console app cannot be
    /// asked to close politely any other way.</para>
    ///
    /// <para>The kill still happens if it does not go quietly. This buys a clean departure when it
    /// is available; it never trades away the guarantee that the process dies.</para>
    /// </summary>
    private static bool TryStopGracefully(Process process, TimeSpan timeout)
    {
        try
        {
            process.StandardInput.WriteLine("stop");
            process.StandardInput.Flush();
        }
        catch
        {
            return false;
        }

        try { return process.WaitForExit((int)timeout.TotalMilliseconds); }
        catch { return false; }
    }


    private static string DiscoverClientDirectory()
    {
        // Beside the agent first - that is how it ships - then the development layout.
        string here = AppContext.BaseDirectory;
        foreach (string candidate in new[]
                 {
                     Path.Combine(here, "loadclient"),
                     here,
                 })
        {
            if (LaunchTarget.Find(candidate, "BasisNetworkClientConsole") != null) return candidate;
        }
        return Path.Combine(here, "loadclient");
    }

    private static string RuntimeOs() => System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim();

    private static void PrintUsage() => Console.WriteLine(@"
BasisBenchAgent - runs Basis load clients on this machine, driven by a remote benchmark.

  BasisBenchAgent [--port <n>] [--bind <addr>] [--client <dir>]

Run this on the machine that should generate the load, then point the benchmark at it:

  BasisServerBenchmark --agent <this-machine>:4297

  --port <n>       Control port. Default 4297. NOT the game port - the load clients on this
                   machine are talking to the server's 4296, and sharing the number would stop
                   the agent ever running on the server's own box.
  --bind <addr>    Interface to listen on. Default 0.0.0.0.
  --client <dir>   Directory holding BasisNetworkClientConsole. Found automatically when it sits
                   beside this agent, or under ./loadclient.

The control channel is unauthenticated and will start processes on request, so run it on a trusted
network or bind it to one.

Run the load client once by hand first so it writes its default ClientSimConfig.xml; the agent patches that
file rather than replacing it, so its crowd settings - spawn radius, voice behaviour - are yours.
");
}

/// <summary>
/// Cores consumed by the load client, sampled between calls.
///
/// NaN when the process will not answer, never zero: a failed read that reports as zero looks like
/// a load generator doing its job for free, which is precisely the wrong conclusion.
/// </summary>
internal sealed class ProcessCpuSampler
{
    private readonly Process _process;
    private TimeSpan _lastCpu;
    private long _lastTimestamp;
    private bool _lastValid;

    public ProcessCpuSampler(Process process)
    {
        _process = process;
        _lastTimestamp = Stopwatch.GetTimestamp();
        _lastValid = TryRead(out _lastCpu);
    }

    public double SampleCores()
    {
        long now = Stopwatch.GetTimestamp();
        bool ok = TryRead(out TimeSpan cpu);

        double seconds = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        double cores = !ok || !_lastValid || seconds <= 0
            ? double.NaN
            : (cpu - _lastCpu).TotalSeconds / seconds;

        _lastTimestamp = now;
        if (ok) _lastCpu = cpu;
        _lastValid = ok;

        return double.IsNaN(cores) ? double.NaN : cores < 0 ? 0 : cores;
    }

    private bool TryRead(out TimeSpan cpu)
    {
        cpu = _lastCpu;
        try
        {
            if (_process.HasExited) return false;
            _process.Refresh();
            cpu = _process.TotalProcessorTime;
            return true;
        }
        catch { return false; }
    }
}
