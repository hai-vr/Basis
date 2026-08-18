using System.Globalization;
using System.Text;
using System.Text.Json;
using Basis.Benchmark.Machine;
using Basis.Benchmark.Micro;
using Basis.Benchmark.Tuning;

namespace Basis.Benchmark.Output;

/// <summary>Everything one invocation learned.</summary>
public sealed class BenchmarkReport
{
    public required DateTime StartedUtc { get; init; }
    public required string Mode { get; init; }
    public required bool Loopback { get; init; }
    public required MachineProfile Machine { get; init; }
    public GpuProfile? Gpu { get; init; }
    public CoreBenchResult? Cores { get; init; }
    public CompressionBenchResult? Compression { get; init; }
    public GpuBenchResult? GpuOffload { get; init; }
    public CapacityResult? Capacity { get; init; }
    public SweepResult? Sweep { get; init; }
    public required IReadOnlyList<Recommendation> Recommendations { get; init; }

    public IEnumerable<Recommendation> Writable => Recommendations.Where(r => r.Writable && r.IsChange);
    public IEnumerable<Recommendation> Blocked => Recommendations.Where(r => !r.Writable && r.IsChange);
    public IEnumerable<Recommendation> Unchanged => Recommendations.Where(r => !r.IsChange);

    /// <summary>
    /// Findings no setting can fix, which must be surfaced above the tuning result rather than
    /// inside it. A clamped socket buffer invalidates the whole capacity measurement, so reporting
    /// it as one row among twenty would let it be scrolled past.
    /// </summary>
    public IEnumerable<string> BlockingFindings()
    {
        if (Machine.Kernel?.AnyClamped == true)
            yield return
                $"net.core.rmem_max is {Machine.Kernel.RmemMax / 1024} KB and net.core.wmem_max is " +
                $"{Machine.Kernel.WmemMax / 1024} KB. The server asks the kernel for 32 MB on each socket and the " +
                "kernel is silently cutting that down - setsockopt reports success either way and nothing logs " +
                "the clamp. Every capacity number below was measured against the clamped buffer, so fix this " +
                "first and re-run; no setting this tool can write will compensate for it.";

        if (Capacity?.Rungs.Any(r => r.KernelDropsPerSecond > 100) == true)
            yield return
                "The kernel discarded inbound UDP datagrams during the ladder. That never shows up as CPU load - " +
                "the receive thread is pinned whether it is keeping up or not - so a box in this state looks idle " +
                "and underperforms anyway. More receive threads (MultiSocketCount) or a larger socket buffer.";
    }
}

/// <summary>
/// Turns a run into something a person can act on and a machine can diff.
///
/// <para>The text report leads with what was <em>not</em> changed, and that ordering is
/// deliberate. The natural failure of a tool like this is to present a list of confident-looking
/// numbers and let the reader assume each one was measured; most of them were not, and the
/// difference between "this beat the default under load on your hardware" and "this follows from
/// your core count" and "this looked better on loopback, where it cannot be measured honestly" is
/// the entire value of the exercise.</para>
/// </summary>
public static class Report
{
    public static string Render(BenchmarkReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("================================================================================");
        sb.AppendLine($" Basis server tuning report - {report.Mode}");
        sb.AppendLine($" {report.StartedUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        sb.AppendLine("MACHINE");
        sb.Append(report.Machine.Describe());
        if (report.Gpu != null) sb.Append(report.Gpu.Describe());
        sb.AppendLine();

        string[] blocking = report.BlockingFindings().ToArray();
        if (blocking.Length > 0)
        {
            sb.AppendLine("BLOCKING FINDINGS - fix these before trusting anything below");
            foreach (string finding in blocking) sb.AppendLine(Wrap(finding, "  ! "));
            if (report.Machine.Kernel?.RemediationSnippet() is { } snippet)
            {
                sb.AppendLine();
                foreach (string line in snippet.Split('\n')) sb.AppendLine("      " + line);
            }
            sb.AppendLine();
        }

        if (report.Cores != null)
        {
            sb.AppendLine("CORE SCALING (offline)");
            sb.Append(report.Cores.Describe());
            sb.AppendLine();
        }

        if (report.Compression != null)
        {
            sb.AppendLine("COMPRESSION BUDGET (offline)");
            sb.Append(report.Compression.Describe());
            sb.AppendLine();
        }

        if (report.GpuOffload != null)
        {
            sb.AppendLine("COMPUTE OFFLOAD (offline)");
            sb.Append(report.GpuOffload.Describe());
            sb.AppendLine();
        }

        if (report.Capacity != null)
        {
            sb.AppendLine("CAPACITY");
            sb.Append(report.Capacity.Describe());
            sb.AppendLine();

            if (report.Capacity.IdleWarning(report.Machine.LogicalCores) is { } idle)
            {
                sb.AppendLine("  " + Wrap(idle, "").TrimStart());
                sb.AppendLine();
            }
        }

        // Only when load actually ran. In profile mode nothing went over a socket at all, so a
        // caveat about loopback would be describing a measurement that was never taken.
        if (report.Loopback && (report.Capacity != null || report.Sweep != null))
        {
            sb.AppendLine("TOPOLOGY CAVEAT");
            sb.AppendLine(Wrap(
                "This was a single-box run: the load clients and the server shared a machine and talked over " +
                "loopback. CPU-side findings - parallel widths, codec cost, allocation behaviour - measure the " +
                "same either way and are reported as measured. Packet-rate and socket findings do not: the " +
                "kernel performs receive-side processing inline inside the sender and its cost scales with " +
                "bytes rather than with datagrams, so a change that removes half the datagrams while moving " +
                "the same bytes measures as roughly zero here and is a real win over a NIC. Those settings are " +
                "measured and reported below but deliberately not written.", "  "));
            sb.AppendLine();
        }

        sb.AppendLine("RECOMMENDED CHANGES");
        var writable = report.Writable.ToArray();
        if (writable.Length == 0)
        {
            sb.AppendLine("  None. Every setting measured is already at its best value on this machine.");
        }
        else
        {
            foreach (Recommendation r in writable)
            {
                sb.AppendLine($"  {r.Setting}: {r.CurrentValue} -> {r.ProposedValue}   [{FileName(r.File)}, {r.Evidence}]");
                sb.AppendLine(Wrap(r.Rationale, "      "));
                sb.AppendLine();
            }
        }

        var blocked = report.Blocked.ToArray();
        if (blocked.Length > 0)
        {
            sb.AppendLine("MEASURED BUT NOT WRITTEN");
            foreach (Recommendation r in blocked)
            {
                sb.AppendLine($"  {r.Setting}: would be {r.CurrentValue} -> {r.ProposedValue}   [{r.Evidence}]");
                sb.AppendLine(Wrap(r.Rationale, "      "));
                sb.AppendLine();
            }
        }

        var unchanged = report.Unchanged.ToArray();
        if (unchanged.Length > 0)
        {
            sb.AppendLine("LEFT ALONE");
            foreach (Recommendation r in unchanged)
            {
                sb.AppendLine($"  {r.Setting} = {r.CurrentValue}");
                sb.AppendLine(Wrap(r.Comparison?.Describe() ?? r.Rationale, "      "));
                sb.AppendLine();
            }
        }

        if (report.Sweep is { Skipped.Count: > 0 })
        {
            sb.AppendLine("NOT MEASURED");
            foreach (string s in report.Sweep.Skipped) sb.AppendLine(Wrap(s, "  - "));
            sb.AppendLine();
        }

        if (report.Sweep?.ConfirmationComparison is { } confirmation)
        {
            sb.AppendLine("CONFIRMATION");
            sb.AppendLine(Wrap(
                $"The accepted settings were re-run together against the original baseline: " +
                $"{confirmation.Describe(" Hz/pair")}. " +
                (report.Sweep.Confirmed == true
                    ? "The combination holds."
                    : report.Sweep.Confirmed == false
                        ? "It does not hold - the individual wins did not survive being applied together, so the " +
                          "whole set has been withdrawn rather than partly kept."
                        : "The result was not conclusive, so nothing was adopted on the strength of it."), "  "));
            sb.AppendLine();
        }

        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(Wrap(
            "A note on what this optimises. Every decision above was made on delivered updates per pair per " +
            "second - how often a player actually receives news of any one other player, losses included - and " +
            "never on CPU. Those two disagree at exactly the moment it matters: past capacity the server sheds " +
            "avatar updates at the queue bound, shedding is cheaper than sending, so CPU comes back DOWN while " +
            "quality collapses. Any tuner scored on CPU picks that configuration and calls it an improvement.", "  "));

        return sb.ToString();
    }

    public static string RenderJson(BenchmarkReport report)
    {
        var payload = new
        {
            startedUtc = report.StartedUtc,
            mode = report.Mode,
            loopback = report.Loopback,
            machine = new
            {
                cores = report.Machine.LogicalCores,
                memoryGb = Math.Round(report.Machine.TotalMemoryGb, 2),
                os = report.Machine.Os,
                containerLimited = report.Machine.IsContainerLimited,
                reusePort = report.Machine.SupportsReusePort,
                rmemMax = report.Machine.Kernel?.RmemMax,
                wmemMax = report.Machine.Kernel?.WmemMax,
                socketBufferClamped = report.Machine.Kernel?.AnyClamped,
            },
            gpu = report.Gpu == null ? null : new
            {
                availability = report.Gpu.Availability.ToString(),
                failure = report.Gpu.Failure,
                devices = report.Gpu.Devices.Select(d => new
                {
                    d.Name,
                    backend = d.Backend.ToString(),
                    memoryGb = Math.Round(d.MemoryGb, 2),
                    d.Architecture,
                    d.MultiProcessors,
                }),
            },
            computeOffload = report.GpuOffload == null ? null : new
            {
                device = report.GpuOffload.DeviceName,
                backend = report.GpuOffload.Backend,
                recommended = report.GpuOffload.Recommended,
                sweepIntervalTicks = report.GpuOffload.SweepIntervalTicks,
                pairsVerified = report.GpuOffload.PairsVerified,
                qualityDisagreements = report.GpuOffload.QualityDisagreements,
                intervalDisagreements = report.GpuOffload.IntervalDisagreements,
                encoderDrift = report.GpuOffload.EncoderDrift,
                points = report.GpuOffload.Points.Select(pt => new
                {
                    pt.Players,
                    cpuMs = Math.Round(pt.CpuMs, 3),
                    solveMs = Math.Round(pt.GpuSolveMs, 3),
                    scatterMs = Math.Round(pt.ScatterMs, 3),
                    totalMs = Math.Round(pt.GpuTotalMs, 3),
                    downloadMegabytes = Math.Round(pt.DownloadMegabytes, 2),
                    speedup = Math.Round(pt.Speedup, 3),
                }),
            },
            coreScaling = report.Cores == null ? null : new
            {
                kneeWorkers = report.Cores.KneeWorkers,
                widthPenalty = Math.Round(report.Cores.WidthPenalty, 3),
                points = report.Cores.Points.Select(p => new
                {
                    p.Workers,
                    itemsPerSecond = Math.Round(p.ItemsPerSecond),
                    cores = Math.Round(p.CoresUsed, 3),
                    itemsPerCoreSecond = Math.Round(p.ItemsPerCoreSecond),
                }),
            },
            compression = report.Compression == null ? null : new
            {
                corpus = report.Compression.Corpus.Label,
                corpusOrigin = report.Compression.Corpus.Origin.ToString(),
                dictionaryPresent = report.Compression.ZstdDictionaryPresent,
                recommendZstd = report.Compression.RecommendZstdEnabled,
                recommendedLevel = report.Compression.RecommendedZstdLevel,
                points = report.Compression.Points.Select(p => new
                {
                    p.Codec,
                    level = p.Level == int.MinValue ? (int?)null : p.Level,
                    ratio = Math.Round(p.Ratio, 4),
                    megabytesPerSecond = Math.Round(p.MegabytesPerSecond, 1),
                    bytesSavedPerCoreMs = Math.Round(p.BytesSavedPerCoreMs),
                }),
            },
            capacity = report.Capacity == null ? null : new
            {
                fullQualityPlayers = report.Capacity.FullQualityPlayers,
                maxStablePlayers = report.Capacity.MaxStablePlayers,
                bottleneck = report.Capacity.Bottleneck,
                rungs = report.Capacity.Rungs.Select(r => new
                {
                    r.Players,
                    cores = Math.Round(r.Cores, 2),
                    megabytesPerSecond = Math.Round(r.MegabytesPerSecond, 1),
                    deliveredPairHz = Math.Round(r.DeliveredPairHz, 3),
                    deliveryRatio = Math.Round(r.DeliveryRatio, 4),
                    sliceCount = Math.Round(r.SliceCount, 2),
                    committedMb = Math.Round(r.CommittedMb),
                }),
            },
            recommendations = report.Recommendations.Select(r => new
            {
                setting = r.Setting,
                file = FileName(r.File),
                current = r.CurrentValue,
                proposed = r.ProposedValue,
                evidence = r.Evidence.ToString(),
                writable = r.Writable,
                changed = r.IsChange,
                rationale = r.Rationale,
                comparison = r.Comparison == null ? null : new
                {
                    verdict = r.Comparison.Verdict.ToString(),
                    baselineMedian = Math.Round(r.Comparison.BaselineMedian, 4),
                    candidateMedian = Math.Round(r.Comparison.CandidateMedian, 4),
                    relativeChange = Math.Round(r.Comparison.RelativeChange, 4),
                    pValue = Math.Round(r.Comparison.PValue, 4),
                },
            }),
            blockingFindings = report.BlockingFindings(),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FileName(SettingFile file) =>
        file == SettingFile.Transport ? "config/transports/litenetlib.xml" : "config/config.xml";

    /// <summary>Wraps prose to 96 columns with a hanging indent, so a terminal report stays readable.</summary>
    private static string Wrap(string text, string indent, int width = 96)
    {
        var sb = new StringBuilder();
        int usable = Math.Max(20, width - indent.Length);
        string hanging = new(' ', indent.Length);
        bool first = true;

        foreach (string paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > usable)
                {
                    sb.AppendLine((first ? indent : hanging) + line);
                    first = false;
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            if (line.Length > 0)
            {
                sb.Append((first ? indent : hanging) + line);
                first = false;
            }
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    public static void WriteTo(string directory, BenchmarkReport report)
    {
        Directory.CreateDirectory(directory);
        string stamp = report.StartedUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(directory, $"tuning-{stamp}.txt"), Render(report));
        File.WriteAllText(Path.Combine(directory, $"tuning-{stamp}.json"), RenderJson(report));
    }
}
