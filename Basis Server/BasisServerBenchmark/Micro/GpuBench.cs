using System.Diagnostics;
using System.Text;
using Basis.Benchmark.Machine;
using Basis.Network.Compute;
using Basis.Network.Core.Compute;

namespace Basis.Benchmark.Micro;

/// <summary>One population measured on both backends.</summary>
public sealed record GpuSweepPoint(
    int Players,
    double CpuMs,
    double GpuSolveMs,
    double GpuTotalMs,
    double DownloadMegabytes,
    double CpuProcessorMs,
    double GpuProcessorMs)
{
    /// <summary>
    /// Processor time the offload gives back per sweep.
    ///
    /// <para>This, not elapsed time, is what the offload is for. The send phase and the
    /// transport's per-peer pass overlap on one machine, so a core handed back is worth more than
    /// a millisecond saved inside the phase — and it only materialises if waiting on the device
    /// blocks rather than spins, which is why the backend asks for ScheduleBlockingSync.</para>
    /// </summary>
    public double ProcessorMsFreed => CpuProcessorMs - GpuProcessorMs;

    /// <summary>How much faster the offload is once the transfer and the scatter are paid for.</summary>
    public double Speedup => GpuTotalMs > 0 ? CpuMs / GpuTotalMs : 0;

    /// <summary>The scatter back into the per-receiver cache, which only the device path pays.</summary>
    public double ScatterMs => GpuTotalMs - GpuSolveMs;
}

public sealed class GpuBenchResult
{
    public required IReadOnlyList<GpuSweepPoint> Points { get; init; }
    public required string DeviceName { get; init; }
    public required string Backend { get; init; }
    public required long PairsVerified { get; init; }
    public required long QualityDisagreements { get; init; }
    public required long IntervalDisagreements { get; init; }
    public required int SweepIntervalTicks { get; init; }
    public required bool Recommended { get; init; }
    public required string Verdict { get; init; }
    public string? EncoderDrift { get; init; }

    public GpuSweepPoint? At(int players) => Points.FirstOrDefault(p => p.Players == players);

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  Device: {DeviceName} [{Backend}]");
        if (EncoderDrift != null)
        {
            sb.AppendLine($"    ! {EncoderDrift}");
            return sb.ToString();
        }

        sb.AppendLine("    The whole NxN sweep, both backends, against the cache the send loop reads.");
        sb.AppendLine("    GPU total includes upload, kernel, download and the scatter back into the cache.");
        sb.AppendLine();
        sb.AppendLine("    players    CPU wall   GPU wall    CPU proc   GPU proc   proc freed   speedup");
        foreach (GpuSweepPoint p in Points)
        {
            sb.AppendLine($"    {p.Players,7}  {p.CpuMs,9:F2}  {p.GpuTotalMs,9:F2}  {p.CpuProcessorMs,10:F2} {p.GpuProcessorMs,10:F2}  {p.ProcessorMsFreed,10:F2}  {p.Speedup,8:F2}x");
        }

        sb.AppendLine();
        if (QualityDisagreements == 0 && IntervalDisagreements == 0)
        {
            sb.AppendLine($"    Agreement: exact over {PairsVerified:N0} pairs.");
        }
        else
        {
            double qPct = PairsVerified > 0 ? QualityDisagreements * 100.0 / PairsVerified : 0;
            double iPct = PairsVerified > 0 ? IntervalDisagreements * 100.0 / PairsVerified : 0;
            sb.AppendLine($"    Agreement over {PairsVerified:N0} pairs: quality {QualityDisagreements:N0} ({qPct:F6}%), " +
                          $"interval byte {IntervalDisagreements:N0} ({iPct:F6}%).");
            sb.AppendLine("    The device contracts the three squared terms into fused multiply-adds, which rounds");
            sb.AppendLine("    once where the CPU rounds three times. Disagreements are one quantisation step, on");
            sb.AppendLine("    pairs sitting within an ulp of a boundary, in a cache the next sweep overwrites.");
        }

        sb.AppendLine();
        sb.AppendLine($"    {Verdict}");
        return sb.ToString();
    }
}

/// <summary>
/// Whether the distance sweep is worth moving to a compute device on this host.
///
/// <para>The sweep is the only phase of the tick that can move at all. The send loop is bound by
/// the sends themselves rather than by choosing them, and bundling is a per-receiver
/// compress-measure-shrink loop with a serial dependency on its own output; neither survives a
/// round trip to a coprocessor. The sweep is the exception because it is pure arithmetic over a
/// dense array with no syscall and no shared mutable state in its inner loop.</para>
///
/// <para>What this measures is therefore narrow and specific, and it is measured end to end —
/// upload, kernel, download, and the scatter back into the per-receiver cache the send loop reads.
/// A kernel time on its own would flatter the device by leaving out the only part of the exchange
/// that is guaranteed to cost something.</para>
/// </summary>
public static class GpuBench
{
    private const int Repetitions = 7;
    private const double SpeedupToRecommend = 1.5;

    public static GpuBenchResult? Run(GpuProfile gpu, int designPlayers, int cpuWorkers, int sweepIntervalTicks, string? deviceSelector, Action<string> log)
    {
        if (gpu.Availability != GpuAvailability.Present) return null;

        GpuDistanceSolver? solver = GpuDistanceSolver.TryCreate(deviceSelector, out string? failure);
        if (solver == null)
        {
            log($"    No usable compute device ({failure}); the sweep stays on the CPU.");
            return null;
        }

        using (solver)
        {
            var parameters = new BasisDistanceSolveParameters
            {
                HighDistanceSq = 100f,
                MediumDistanceSq = 900f,
                LowDistanceSq = 2500f,
                BaseMultiplier = 1.0f,
                IncreaseRate = 0.01f,
                BaseIntervalMs = 50,
            };

            if (DistanceMath.VerifyAgainstProtocol(parameters.BaseIntervalMs) is { } driftAt)
            {
                return new GpuBenchResult
                {
                    Points = Array.Empty<GpuSweepPoint>(),
                    DeviceName = solver.DeviceName,
                    Backend = solver.Backend,
                    PairsVerified = 0,
                    QualityDisagreements = 0,
                    IntervalDisagreements = 0,
                    SweepIntervalTicks = sweepIntervalTicks,
                    Recommended = false,
                    Verdict = "Not recommended - the kernel's interval encoder no longer matches the protocol's.",
                    EncoderDrift = $"DistanceMath.Encode disagrees with BasisNetworkCommons at {driftAt} ms. " +
                                   "Offload is refused until they agree.",
                };
            }

            int[] populations = BuildLadder(designPlayers);
            var points = new List<GpuSweepPoint>();
            long pairsVerified = 0, qualityDiff = 0, intervalDiff = 0;

            foreach (int n in populations)
            {
                GpuSweepPoint point = Measure(solver, n, cpuWorkers, parameters,
                    out long verified, out long qd, out long id);
                points.Add(point);
                if (n == populations[^1])
                {
                    pairsVerified = verified;
                    qualityDiff = qd;
                    intervalDiff = id;
                }
                log($"    {n,5} players: CPU {point.CpuMs:F2} ms, GPU {point.GpuTotalMs:F2} ms ({point.Speedup:F2}x)");
            }

            GpuSweepPoint design = points.OrderBy(p => Math.Abs(p.Players - designPlayers)).First();
            bool recommended = design.Speedup >= SpeedupToRecommend;

            return new GpuBenchResult
            {
                Points = points,
                DeviceName = solver.DeviceName,
                Backend = solver.Backend,
                PairsVerified = pairsVerified,
                QualityDisagreements = qualityDiff,
                IntervalDisagreements = intervalDiff,
                SweepIntervalTicks = sweepIntervalTicks,
                Recommended = recommended,
                Verdict = BuildVerdict(design, recommended, sweepIntervalTicks, cpuWorkers),
            };
        }
    }

    private static string BuildVerdict(GpuSweepPoint design, bool recommended, int sweepIntervalTicks, int cpuWorkers)
    {
        double tickMs = design.Players >= 2000 ? 20.0 : 15.0;
        double sweepSeconds = sweepIntervalTicks * tickMs / 1000.0;
        double cpuCores = design.CpuMs * cpuWorkers / (sweepSeconds * 1000.0);
        double gpuCores = design.GpuTotalMs * cpuWorkers / (sweepSeconds * 1000.0);

        var sb = new StringBuilder();
        if (!recommended)
        {
            sb.Append($"Not recommended: {design.Speedup:F2}x at {design.Players} players is inside the noise the ");
            sb.Append("round trip adds. The sweep stays on the CPU.");
            return sb.ToString();
        }

        sb.AppendLine($"Recommended: {design.Speedup:F2}x at {design.Players} players.");
        sb.AppendLine();
        double coresFreed = design.ProcessorMsFreed * cpuWorkers / (sweepSeconds * 1000.0) / cpuWorkers;
        sb.AppendLine($"      Processor time given back: {design.CpuProcessorMs:F2} ms -> {design.GpuProcessorMs:F2} ms per sweep, which at the");
        sb.AppendLine($"      shipped {sweepIntervalTicks}-tick refresh is about {design.ProcessorMsFreed / (sweepSeconds * 1000.0):F4} cores. Small in absolute terms - this is");
        sb.AppendLine("      not where a broadcast server's CPU goes, and it should not be sold as if it were.");
        sb.AppendLine();
        sb.AppendLine("      It is only real because the backend waits with ScheduleBlockingSync. CUDA's default");
        sb.AppendLine("      spins, which returns the wall time and none of the core.");
        sb.AppendLine();
        sb.AppendLine("      Read as staleness, it is worth more, and that is where the server spends it. The");
        sb.AppendLine("      refresh period is long because the sweep is expensive; the same tick budget buys a");
        sb.AppendLine($"      sweep {design.Speedup:F1}x more often, so the quality tier and interval a pair is served at track");
        sb.AppendLine("      the distance between them more closely.");
        sb.AppendLine();
        sb.AppendLine($"      ComputeDistanceUpdateIntervalTicks is what takes it: {sweepIntervalTicks} -> about");
        sb.AppendLine($"      {Math.Max(1, (int)(sweepIntervalTicks / design.Speedup))} ticks, applied only while a device is carrying the sweep and");
        sb.AppendLine("      dropped back the moment one is not.");
        return sb.ToString().TrimEnd();
    }

    private static int[] BuildLadder(int designPlayers)
    {
        var ladder = new List<int>();
        foreach (int n in new[] { 500, 1000, 2000, 4000 })
        {
            if (n <= designPlayers * 2) ladder.Add(n);
        }
        if (!ladder.Contains(designPlayers) && designPlayers > 0) ladder.Add(designPlayers);
        ladder.Sort();
        return ladder.Distinct().ToArray();
    }

    private static GpuSweepPoint Measure(
        GpuDistanceSolver solver, int n, int cpuWorkers, BasisDistanceSolveParameters p,
        out long pairsVerified, out long qualityDiff, out long intervalDiff)
    {
        var rng = new Random(12345);
        var posX = new float[n];
        var posY = new float[n];
        var posZ = new float[n];
        for (int i = 0; i < n; i++)
        {
            posX[i] = (float)(rng.NextDouble() * 100.0 - 50.0);
            posY[i] = (float)(rng.NextDouble() * 4.0);
            posZ[i] = (float)(rng.NextDouble() * 100.0 - 50.0);
        }

        double msToTick = Stopwatch.Frequency / 1000.0;
        int[] tickTable = DistanceMath.BuildIntervalTickTable(p.BaseIntervalMs, msToTick);

        var cacheTicks = new int[n][];
        var cacheQuality = new byte[n][];
        var cacheInterval = new byte[n][];
        for (int i = 0; i < n; i++)
        {
            cacheTicks[i] = new int[n];
            cacheQuality[i] = new byte[n];
            cacheInterval[i] = new byte[n];
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = cpuWorkers };

        void CpuFusedSweep()
        {
            Parallel.For(0, n, options, i =>
            {
                float iX = posX[i], iY = posY[i], iZ = posZ[i];
                int[] ticks = cacheTicks[i];
                byte[] quality = cacheQuality[i];
                byte[] interval = cacheInterval[i];
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    float dx = iX - posX[j];
                    float dy = iY - posY[j];
                    float dz = iZ - posZ[j];
                    float distSq = dx * dx + dy * dy + dz * dz;

                    int raw = DistanceMath.RawInterval(distSq, p.BaseMultiplier, p.IncreaseRate, p.BaseIntervalMs);
                    byte encoded = DistanceMath.Encode(raw, p.BaseIntervalMs);
                    interval[j] = encoded;
                    ticks[j] = tickTable[encoded];
                    quality[j] = DistanceMath.Quality(distSq, p.HighDistanceSq, p.MediumDistanceSq, p.LowDistanceSq);
                }
            });
        }

        long resultLength = (long)n * n;
        var flatInterval = new byte[resultLength];
        var flatQuality = new byte[resultLength];
        var request = new BasisDistanceSolveRequest
        {
            PosX = posX,
            PosY = posY,
            PosZ = posZ,
            PlayerCount = n,
            SliceStart = 0,
            SliceEnd = n,
            Parameters = p,
        };

        void Scatter()
        {
            Parallel.For(0, n, options, i =>
            {
                int[] ticks = cacheTicks[i];
                byte[] quality = cacheQuality[i];
                byte[] interval = cacheInterval[i];
                long baseOffset = (long)i * n;
                for (int j = 0; j < n; j++)
                {
                    byte encoded = flatInterval[baseOffset + j];
                    interval[j] = encoded;
                    ticks[j] = tickTable[encoded];
                    quality[j] = flatQuality[baseOffset + j];
                }
            });
        }

        CpuFusedSweep();
        solver.Solve(ref request, flatInterval, flatQuality);
        Scatter();

        double cpuMs = BestOf(CpuFusedSweep);
        double gpuSolveMs = BestOf(() => solver.Solve(ref request, flatInterval, flatQuality));
        double gpuTotalMs = BestOf(() => { solver.Solve(ref request, flatInterval, flatQuality); Scatter(); });

        double cpuProcessorMs = ProcessorCostOf(CpuFusedSweep);
        double gpuProcessorMs = ProcessorCostOf(() => { solver.Solve(ref request, flatInterval, flatQuality); Scatter(); });

        CpuFusedSweep();
        var referenceInterval = new byte[resultLength];
        var referenceQuality = new byte[resultLength];
        for (int i = 0; i < n; i++)
        {
            long baseOffset = (long)i * n;
            for (int j = 0; j < n; j++)
            {
                referenceInterval[baseOffset + j] = cacheInterval[i][j];
                referenceQuality[baseOffset + j] = cacheQuality[i][j];
            }
        }

        solver.Solve(ref request, flatInterval, flatQuality);
        long verified = 0, qd = 0, id = 0;
        for (int i = 0; i < n; i++)
        {
            long baseOffset = (long)i * n;
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                verified++;
                if (flatQuality[baseOffset + j] != referenceQuality[baseOffset + j]) qd++;
                if (flatInterval[baseOffset + j] != referenceInterval[baseOffset + j]) id++;
            }
        }

        pairsVerified = verified;
        qualityDiff = qd;
        intervalDiff = id;

        return new GpuSweepPoint(n, cpuMs, gpuSolveMs, gpuTotalMs, resultLength * 2 / (1024.0 * 1024.0),
            cpuProcessorMs, gpuProcessorMs);
    }

    /// <summary>
    /// Processor time one call costs, averaged over a run long enough for the OS accounting clock
    /// to resolve it. Averaged rather than best-of because the quantity of interest is what the
    /// work actually consumes, not the least it could ever consume.
    /// </summary>
    private static double ProcessorCostOf(Action action)
    {
        const int Iterations = 40;
        action();
        System.Diagnostics.Process self = System.Diagnostics.Process.GetCurrentProcess();
        TimeSpan before = self.TotalProcessorTime;
        for (int i = 0; i < Iterations; i++) action();
        return (self.TotalProcessorTime - before).TotalMilliseconds / Iterations;
    }

    private static double BestOf(Action action)
    {
        double best = double.MaxValue;
        for (int r = 0; r < Repetitions; r++)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        return best;
    }
}
