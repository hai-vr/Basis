using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Basis.Bench.Agent;

namespace Basis.Benchmark.Harness;

/// <summary>
/// Where the crowd comes from.
///
/// <para>Two implementations, and the choice between them is the single biggest factor in whether a
/// run's networking findings mean anything. Locally the clients share the server's cores, cache and
/// memory bandwidth — measured at 3.26 cores for the client alone at 1,000 players — and the traffic
/// never crosses a NIC, which is why packet-rate and socket settings are marked untrusted and never
/// written. Remotely both problems disappear at once.</para>
/// </summary>
public interface ILoadClientDriver : IDisposable
{
    /// <summary>Where this crowd runs, for the report. "this machine" or the agent's address.</summary>
    string Where { get; }

    /// <summary>True when the crowd is off-box, so packet-rate findings can be trusted.</summary>
    bool IsRemote { get; }

    /// <summary>Starts the clients. Throws with a usable message if it cannot.</summary>
    void Start(RunOptions options);

    /// <summary>Cores the load client is using, or NaN when unknown. Never part of the score.</summary>
    double SampleCores();

    /// <summary>Share of simulated voice frames a receiver got, or -1 when unknown.</summary>
    double VoiceDelivered { get; }

    void Stop();
}

/// <summary>Spawns the load client as a child process on this machine.</summary>
public sealed class LocalLoadClientDriver : ILoadClientDriver
{
    private static readonly Regex VoiceLine =
        new(@"\[VOICE\] delivered ([0-9]+(?:\.[0-9]+)?)%", RegexOptions.Compiled);

    private Process? _process;
    private ProcessCpu? _cpu;
    private double _voice = -1;

    public string Where => "this machine (loopback)";
    public bool IsRemote => false;
    public double VoiceDelivered => Volatile.Read(ref _voice);

    public void Start(RunOptions options)
    {
        WriteConfig(options);

        string exe = Executable(options.LoadClientDirectory);
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = options.LoadClientDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Open only so the client can be asked to leave the server before it is killed.
            RedirectStandardInput = true,
        };

        Process process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {exe}");
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            Match m = VoiceLine.Match(e.Data);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                Volatile.Write(ref _voice, pct / 100.0);
        };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _process = process;
        _cpu = new ProcessCpu(process);
    }

    public double SampleCores() => _cpu?.SampleCores() ?? double.NaN;

    public void Stop()
    {
        Process? process = _process;
        _process = null;
        _cpu = null;
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
        finally { try { process.Dispose(); } catch { } }
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

    public void Dispose() => Stop();

    private static string Executable(string directory)
        => LaunchTarget.Resolve(directory, "BasisNetworkClientConsole");

    /// <summary>
    /// Points the load client at this run.
    ///
    /// Voice stays on. A silent crowd is not a cheaper version of a real one — voice is a fan-in
    /// that grows with how many talkers are audible, and it is the traffic whose loss is
    /// unrecoverable, so a run without it measures neither the load nor the failure mode that
    /// matters.
    /// </summary>
    private static void WriteConfig(RunOptions options)
    {
        string path = Path.Combine(options.LoadClientDirectory, "ClientSimConfig.xml");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Load client config not found at {path}. Run BasisNetworkClientConsole once so it writes its defaults.", path);

        XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        XElement root = doc.Root ?? throw new InvalidDataException($"{path} has no root element.");

        void Set(string name, string value)
        {
            XElement? element = root.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            if (element != null) element.Value = value;
            else root.Add(new XElement(name, value));
        }

        Set("ClientCount", options.Players.ToString(CultureInfo.InvariantCulture));
        Set("SimulateVoice", "true");
        if (options.ClientConnectIntervalMs is { } interval)
            Set("ClientConnectIntervalMs", interval.ToString(CultureInfo.InvariantCulture));

        string temp = path + ".benchtmp";
        doc.Save(temp);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>
/// Drives a <c>BasisBenchAgent</c> on another machine.
///
/// <para>The connection is held open for the whole run on purpose: the agent stops its clients when
/// the control channel closes, which is the only way a benchmark that dies mid-run does not leave a
/// thousand clients hammering the server with nothing owning them.</para>
/// </summary>
public sealed class RemoteLoadClientDriver : ILoadClientDriver
{
    private readonly string _host;
    private readonly int _port;

    /// <summary>How the agent's machine should address the server. Not necessarily how we do.</summary>
    private readonly string _serverHost;

    private TcpClient? _connection;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private double _voice = -1;
    private double _cores = double.NaN;

    public RemoteLoadClientDriver(string host, int port, string serverHost)
    {
        _host = host;
        _port = port;
        _serverHost = serverHost;
    }

    public string Where => $"{_host}:{_port} (over the network)";
    public bool IsRemote => true;
    public double VoiceDelivered => Volatile.Read(ref _voice);

    /// <summary>Connects and checks the agent is alive and speaking the same protocol.</summary>
    public AgentResponse Hello()
    {
        Connect();
        return Send(new AgentRequest { Command = "hello" });
    }

    public void Start(RunOptions options)
    {
        Connect();

        AgentResponse response = Send(new AgentRequest
        {
            Command = "start",
            Clients = options.Players,
            Host = _serverHost,
            Port = options.ServerPort,
            ConnectIntervalMs = options.ClientConnectIntervalMs ?? 1,
        });

        if (!response.Ok)
            throw new InvalidOperationException($"the agent refused to start the crowd: {response.Error}");
    }

    public double SampleCores()
    {
        Poll();
        return Volatile.Read(ref _cores);
    }

    /// <summary>Refreshes cores and voice in one round trip; both come from the same status reply.</summary>
    private void Poll()
    {
        try
        {
            AgentResponse response = Send(new AgentRequest { Command = "status" });
            if (!response.Ok) return;
            Volatile.Write(ref _cores, response.ClientCores);
            Volatile.Write(ref _voice, response.VoiceDelivered);
        }
        catch
        {
            // A dead agent must not read as an idle one.
            Volatile.Write(ref _cores, double.NaN);
        }
    }

    public void Stop()
    {
        try { if (_connection != null) Send(new AgentRequest { Command = "stop" }); }
        catch { /* the close below stops it anyway */ }

        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _connection?.Dispose(); } catch { }
        _writer = null;
        _reader = null;
        _connection = null;
    }

    public void Dispose() => Stop();

    private void Connect()
    {
        if (_connection is { Connected: true }) return;

        var connection = new TcpClient();
        connection.Connect(_host, _port);
        NetworkStream stream = connection.GetStream();
        stream.ReadTimeout = 15000;
        stream.WriteTimeout = 15000;

        _connection = connection;
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    private AgentResponse Send(AgentRequest request)
    {
        if (_writer == null || _reader == null) throw new InvalidOperationException("not connected to the agent");

        _writer.WriteLine(JsonSerializer.Serialize(request));
        string? line = _reader.ReadLine();
        if (line == null) throw new IOException("the agent closed the connection");

        return JsonSerializer.Deserialize<AgentResponse>(line)
               ?? throw new IOException("the agent sent an unparseable reply");
    }
}
