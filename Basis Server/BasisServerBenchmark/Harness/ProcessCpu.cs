using System.Diagnostics;

namespace Basis.Benchmark.Harness;

/// <summary>
/// Tracks one process's CPU as a number of cores.
///
/// <para>Cores rather than percent, because percent has no fixed meaning across the machines this
/// runs on — 400% is the whole box on a 4-vCPU container and a rounding error on a 128-core host,
/// and the settings being fitted are precisely the ones that depend on which of those it is.</para>
///
/// <para>Sampled from the process rather than the machine so the load client's cost is excluded.
/// On a single-box run the client is often the larger of the two, and a machine-wide reading would
/// attribute all of it to the server and conclude the server had run out of headroom when what ran
/// out was the harness.</para>
/// </summary>
public sealed class ProcessCpu
{
    private readonly Process? _process;
    private TimeSpan _lastCpu;
    private long _lastTimestamp;

    public ProcessCpu(Process? process)
    {
        _process = process;
        Reset();
    }

    public bool Alive
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch { return false; }
        }
    }

    public void Reset()
    {
        _lastTimestamp = Stopwatch.GetTimestamp();
        _lastCpu = Read();
    }

    /// <summary>Cores consumed since the previous call. Resets the interval.</summary>
    public double SampleCores()
    {
        long now = Stopwatch.GetTimestamp();
        TimeSpan cpu = Read();

        double seconds = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        double cores = seconds <= 0 ? 0 : (cpu - _lastCpu).TotalSeconds / seconds;

        _lastTimestamp = now;
        _lastCpu = cpu;
        return cores < 0 ? 0 : cores;
    }

    /// <summary>Resident memory, MB. 0 when the process is gone.</summary>
    public double WorkingSetMb
    {
        get
        {
            try
            {
                if (_process == null || _process.HasExited) return 0;
                _process.Refresh();
                return _process.WorkingSet64 / 1048576.0;
            }
            catch { return 0; }
        }
    }

    private TimeSpan Read()
    {
        try
        {
            if (_process == null || _process.HasExited) return _lastCpu;
            _process.Refresh();
            return _process.TotalProcessorTime;
        }
        catch
        {
            // A process that exits mid-window must not report negative CPU on the next sample.
            return _lastCpu;
        }
    }
}
