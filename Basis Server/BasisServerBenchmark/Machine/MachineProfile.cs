using System.Runtime.InteropServices;
using System.Text;

namespace Basis.Benchmark.Machine;

/// <summary>
/// What the box is, read once before anything is measured.
///
/// Half the settings this tool exists to fit are functions of facts the machine already knows —
/// core count, memory, whether the kernel offers SO_REUSEPORT — and those cost nothing to collect
/// and cannot be wrong. Measuring is reserved for the things a fact cannot answer, which is why
/// this runs first and why the report quotes it: a recommendation that contradicts the machine is
/// a bug in the measurement, not a discovery.
/// </summary>
public sealed class MachineProfile
{
    public int LogicalCores { get; init; }
    public long TotalMemoryBytes { get; init; }
    public bool IsContainerLimited { get; init; }
    public string Os { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string RuntimeVersion { get; init; } = "";
    public bool SupportsReusePort { get; init; }

    /// <summary>Kernel socket-buffer ceilings, Linux only; null elsewhere.</summary>
    public KernelTuning? Kernel { get; init; }

    /// <summary>
    /// The interface player traffic would leave by, or null when none could be identified.
    ///
    /// Read because egress is one of the four things that can run out, and it is the only one an
    /// operator cannot infer from the spec sheet they bought — a 128-core host on a 1 Gbit link
    /// runs out of link long before it runs out of anything else, and nothing in the server says so.
    /// </summary>
    public NetworkLink? Link { get; init; }

    public static MachineProfile Collect()
    {
        long total = 0;
        try { total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; } catch { /* fall through */ }
        if (total <= 0) total = 4L * 1024 * 1024 * 1024;

        bool linux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        return new MachineProfile
        {
            LogicalCores = Environment.ProcessorCount,
            TotalMemoryBytes = total,
            // A container limit is what makes the host and the process disagree about the machine.
            // It matters because every ceiling this tool writes is derived from cores and memory,
            // and a cgroup limit is exactly the case where "what the box has" is not "what we may
            // use". BasisPopulationScale already reads the limited figure; this only records that
            // the number came from a cgroup so the report says so.
            IsContainerLimited = DetectContainerLimit(),
            Os = RuntimeInformation.OSDescription.Trim(),
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            // SO_REUSEPORT is the precondition for multi-socket, not an optimisation of it. Without
            // it a second bind returns AddressAlreadyInUse, so MultiSocketCount above 1 is not
            // merely less useful here — it is unusable, and must never be recommended.
            SupportsReusePort = linux,
            Kernel = linux ? KernelTuning.Read() : null,
            Link = NetworkLink.Primary(),
        };
    }

    private static bool DetectContainerLimit()
    {
        try
        {
            if (File.Exists("/sys/fs/cgroup/memory.max")) return true;                   // cgroup v2
            if (File.Exists("/sys/fs/cgroup/memory/memory.limit_in_bytes")) return true; // cgroup v1
        }
        catch { /* not a container, or not readable */ }
        return false;
    }

    public double TotalMemoryGb => TotalMemoryBytes / (1024.0 * 1024 * 1024);

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  Cores          {LogicalCores} logical");
        sb.AppendLine($"  Memory         {TotalMemoryGb:F1} GB{(IsContainerLimited ? " (container-limited)" : "")}");
        sb.AppendLine($"  OS             {Os} / {Architecture}");
        sb.AppendLine($"  Runtime        {RuntimeVersion}");
        sb.AppendLine($"  SO_REUSEPORT   {(SupportsReusePort ? "available" : "unavailable on this OS - multi-socket cannot be used")}");
        if (Link != null) sb.Append(Link.Describe());
        if (Kernel != null) sb.Append(Kernel.Describe());
        return sb.ToString();
    }
}
