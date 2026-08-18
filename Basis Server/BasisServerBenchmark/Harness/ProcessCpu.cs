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
        _lastReadValid = TryRead(out _lastCpu);
    }

    /// <summary>
    /// Cores consumed since the previous call, or <see cref="double.NaN"/> when the process would
    /// not answer. Resets the interval either way.
    ///
    /// <para><b>NaN rather than zero, and the distinction is not pedantic.</b> Reading a child
    /// process's CPU time fails occasionally and transiently, and the natural handling — return the
    /// last known value, so the delta comes out at zero — produces a number that is not merely
    /// wrong but wrong in the most damaging direction available. Zero cores does not read as a
    /// failure; it reads as a server doing 20 MB/s for free. It was observed intermittently here,
    /// and it fed a curve fit that concluded the machine would never run out of CPU. An
    /// unmeasurable sample has to be able to say so.</para>
    /// </summary>
    public double SampleCores()
    {
        long now = Stopwatch.GetTimestamp();
        bool ok = TryRead(out TimeSpan cpu);

        double seconds = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        double cores = !ok || !_lastReadValid || seconds <= 0
            ? double.NaN
            : (cpu - _lastCpu).TotalSeconds / seconds;

        _lastTimestamp = now;
        if (ok) _lastCpu = cpu;
        _lastReadValid = ok;

        return double.IsNaN(cores) ? double.NaN : cores < 0 ? 0 : cores;
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

    private bool TryRead(out TimeSpan cpu)
    {
        cpu = _lastCpu;
        try
        {
            if (_process == null || _process.HasExited) return false;
            _process.Refresh();
            cpu = _process.TotalProcessorTime;
            return true;
        }
        catch
        {
            // Transient on Windows, and permanent once the process is gone. Either way the caller
            // must not be handed a delta computed against a value that was never read.
            return false;
        }
    }

    private bool _lastReadValid;
}
