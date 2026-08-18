using System.Text;

namespace Basis.Benchmark.Cli;

public sealed record ConsoleCommand(string Name, string Usage, string Description, Action<string[]> Handler);

/// <summary>
/// The command loop.
///
/// <para>An interactive console rather than a set of command-line flags because of what the tool
/// spends its time doing: an <c>/auto</c> is hours long, it is normally started over SSH, and the
/// thing an operator wants throughout is to watch it and ask it questions — how far along, what has
/// it found, stop. Flags cannot express any of that, and re-running a two-hour job because an
/// argument was wrong is a poor way to find out.</para>
///
/// <para>Long jobs run on their own thread so the prompt stays live while they print. Only one runs
/// at a time: they all drive the same server binary on the same port, so a second would silently
/// contend with the first and both measurements would be wrong.</para>
///
/// <para>Redirected stdin is handled rather than assumed away — under systemd, <c>nohup</c> or a
/// pipe there is no terminal, <c>ReadLine</c> returns null immediately, and a loop that ignored
/// that would spin a core forever.</para>
/// </summary>
public sealed class BenchmarkConsole
{
    private readonly Dictionary<string, ConsoleCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ConsoleCommand> _ordered = new();
    private readonly object _outputGate = new();

    private Thread? _job;
    private CancellationTokenSource? _jobCancellation;
    private string _jobName = "";
    private DateTime _jobStarted;

    public bool Running { get; private set; } = true;

    /// <summary>
    /// False when stdin is a pipe or a file rather than a terminal, which changes how jobs run.
    ///
    /// <para>Backgrounding a job only makes sense when there is somebody at a keyboard to use the
    /// prompt it frees up. Piped input is a script, and a script expects its commands to happen in
    /// the order it wrote them — background them and <c>/write</c> executes while <c>/auto</c> is
    /// still on its first ladder rung, writing a profile out of an empty result set.</para>
    /// </summary>
    public bool Interactive { get; } = !Console.IsInputRedirected;

    public void Register(string name, string usage, string description, Action<string[]> handler)
    {
        var command = new ConsoleCommand(name, usage, description, handler);
        _commands[name] = command;
        _ordered.Add(command);
    }

    /// <summary>Writes a line without interleaving with a job thread's output.</summary>
    public void Write(string line)
    {
        lock (_outputGate) Console.WriteLine(line);
    }

    public void WriteBlock(string text)
    {
        lock (_outputGate) Console.Write(text.EndsWith('\n') ? text : text + "\n");
    }

    public bool JobRunning => _job is { IsAlive: true };
    public string JobName => _jobName;
    public TimeSpan JobElapsed => JobRunning ? DateTime.UtcNow - _jobStarted : TimeSpan.Zero;

    /// <summary>
    /// Starts a long-running command on its own thread.
    ///
    /// Refuses when one is already running rather than queueing it. Two jobs would drive the same
    /// server binary on the same port at the same time, and the failure would look like a bad
    /// measurement rather than a mistake.
    /// </summary>
    public bool StartJob(string name, Action<CancellationToken> work)
    {
        if (JobRunning)
        {
            Write($"  '{_jobName}' is still running ({JobElapsed.TotalMinutes:F0} min). /stop it first.");
            return false;
        }

        _jobCancellation?.Dispose();
        _jobCancellation = new CancellationTokenSource();
        _jobName = name;
        _jobStarted = DateTime.UtcNow;

        CancellationToken token = _jobCancellation.Token;
        void Body()
        {
            try
            {
                work(token);
            }
            catch (OperationCanceledException)
            {
                Write($"  {name} stopped.");
            }
            catch (Exception ex)
            {
                Write($"  {name} failed: {ex.Message}");
            }
            finally
            {
                Write($"  {name} finished after {(DateTime.UtcNow - _jobStarted).TotalMinutes:F1} min." +
                      (Interactive ? " Type /help for what to do next." : ""));
            }
        }

        if (!Interactive)
        {
            // Scripted: run it here and now, so the next line of the script sees its results.
            Body();
            return true;
        }

        _job = new Thread(Body) { IsBackground = true, Name = "bench-" + name };
        _job.Start();
        return true;
    }

    /// <summary>
    /// Asks the running job to stop.
    ///
    /// Cooperative, and it can take a while to take effect: the load runner checks between windows
    /// and between arms, so a stop issued in the middle of a 30-second window is honoured at the
    /// end of it. Killing it outright would leave a server and a thousand load clients running with
    /// nothing owning them, and the operator's configs unrestored.
    /// </summary>
    public void StopJob()
    {
        if (!JobRunning)
        {
            Write("  Nothing is running.");
            return;
        }
        Write($"  Stopping '{_jobName}' - it finishes the window it is in first, then tears down and restores configs.");
        _jobCancellation?.Cancel();
    }

    public void WaitForJob() => _job?.Join();

    public void Quit()
    {
        if (JobRunning)
        {
            Write($"  '{_jobName}' is still running. Stopping it before exit...");
            _jobCancellation?.Cancel();
            _job?.Join(TimeSpan.FromMinutes(2));
        }
        Running = false;
    }

    public string RenderHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        int width = _ordered.Max(c => (c.Name + " " + c.Usage).TrimEnd().Length);
        foreach (ConsoleCommand command in _ordered)
        {
            string left = (command.Name + " " + command.Usage).TrimEnd();
            sb.AppendLine($"  {left.PadRight(width)}   {command.Description}");
        }
        return sb.ToString();
    }

    /// <summary>Reads and dispatches until told to stop or input runs out.</summary>
    public void Loop()
    {
        while (Running)
        {
            if (Interactive)
            {
                lock (_outputGate) Console.Write("bench> ");
            }

            string? line = Console.ReadLine();
            if (line == null)
            {
                // End of input. Under a pipe or a service host there is nothing more coming, so
                // wait out whatever is running rather than exiting from under it.
                if (JobRunning)
                {
                    Write("  Input closed; waiting for the running job to finish.");
                    WaitForJob();
                }
                break;
            }

            Dispatch(line.Trim());
        }
    }

    public void Dispatch(string input)
    {
        if (input.Length == 0) return;

        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string name = parts[0];
        // A leading slash is how the server's console reads, so both accept it; typing the bare
        // word works too rather than being an error nobody learns anything from.
        if (!name.StartsWith('/')) name = "/" + name;

        if (!_commands.TryGetValue(name, out ConsoleCommand? command))
        {
            Write($"  Unknown command '{parts[0]}'. Type /help.");
            return;
        }

        try
        {
            command.Handler(parts.Skip(1).ToArray());
        }
        catch (Exception ex)
        {
            Write($"  {name} failed: {ex.Message}");
        }
    }
}
