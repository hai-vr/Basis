using System.Diagnostics;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using K4os.Compression.LZ4;
using Xunit;
using Xunit.Abstractions;

namespace BasisServerTests;

/// <summary>
/// Whether bundle compression is worth moving to a GPU. It is not, and this records why so the
/// question does not get re-opened on the strength of the same reasoning that opened it.
///
/// ── What was built ───────────────────────────────────────────────────────────────────────────
///
/// A complete LZ4 block encoder as an ILGPU kernel, one thread per bundle, hash table in shared
/// memory. It is correct — every bundle round-trips through the same
/// <see cref="LZ4Codec.Decode"/> the clients use — and it compresses slightly BETTER than K4os
/// (0.8438 vs 0.8487 on the corpus below), because it probes every position where LZ4's fast mode
/// skips ahead on data it judges incompressible.
///
/// ── Why it is not worth shipping (2026-08-18, RTX 4090, 32-core host) ────────────────────────
///
/// One tick's batch at 2000 players — 1037 bundles, 1.24 MB:
///
///   CPU, one core, whole batch      1.678 ms     0.106 cores at 63 ticks/s
///   CPU, across 8 send workers      0.237 ms wall
///   GPU, full round trip            1.684 ms
///
/// The GPU exactly ties ONE core and loses 7x to the send workers. Two independent reasons, and
/// neither is a kernel-tuning problem:
///
/// 1. THE BATCH IS TOO SMALL TO FILL A GPU. A tick produces ~1000 independent bundles. That is
///    ~32 warps of work spread over 128 SMs, so most of the card is idle and memory latency is
///    never hidden. Scaling the batch does not rescue it either — measured at 1037 / 4148 /
///    16592 / 66368 bundles, the GPU loses at every size and falls off a cliff past 16K when the
///    working set leaves L2.
///
/// 2. THE ACCESS PATTERN IS THE OPPOSITE OF WHAT A GPU IS GOOD AT. Random hash probes and serial
///    match extension, per thread, with a data dependency between every match and the next search
///    position. Moving the hash table from global to shared memory — the single biggest available
///    win — took it from 3.5 ms to 1.75 ms and still did not reach the CPU.
///
/// And the prize was never large: the whole codec is ~0.1 cores of an 11-core server at 2000
/// players. The bundle path also compresses inside the per-receiver send loop and retries with a
/// smaller chunk on MTU overshoot, so batching it on a device would mean turning a pipelined
/// compress-measure-shrink into build-all / compress-all / send-all, which serialises a phase
/// that currently overlaps.
///
/// The figures above come from a standalone harness, not from this test. Under the xunit host
/// everything measures several times slower — the GPU included — because BasisServerTests does not
/// turn off tiered PGO the way BasisServerBenchmark deliberately does. The RATIO is what survives
/// the move: the GPU loses to the send workers in both, at 0.14x standalone and 0.70x here. That
/// is why this asserts correctness and prints speed rather than asserting on it.
///
/// ⚠️ MEASURE WITH A WARMED BASELINE. An earlier version of this timed 11 repetitions and read
/// the CPU at 14.3 ms — eight times its real cost — which made a GPU that ties one core look like
/// a 2x win. The CPU side is JIT-sensitive; 31 best-of repetitions is what it took to settle.
/// This is the same trap CompressionBench's "best-of, not mean" note is about.
///
/// ── The counterargument, measured: does it free CPU? ─────────────────────────────────────────
///
/// Wall time is the wrong yardstick on its own. The send phase and the transport's per-peer pass
/// overlap on one machine, so cores handed back are worth more than milliseconds saved inside the
/// phase — which is the whole reason BasisCpuBudget exists. Measured with processor time rather
/// than elapsed, and with ScheduleBlockingSync so waiting does not spin:
///
///   CPU, 8 send workers      0.93 ms wall     6.56 ms CPU
///   GPU, blocking sync       3.95 ms wall     0.73 ms CPU
///
/// So it DOES free about 0.37 cores. It still should not ship, and the reason has moved: the cost
/// is now 3 ms of extra wall time in a phase that is already ~87% of the tick and is the critical
/// path for delivery. Trading 3 ms of send latency for a third of a core is a bad trade on a
/// server whose capacity ceiling is egress, not arithmetic.
///
/// Two caveats that both cut toward the GPU and still do not close it: the card these figures come
/// from was ~46% busy with unrelated work, and the CPU column above pays a Parallel.For fork/join
/// that the server would not — in situ, compression runs inside the send loop that is already
/// running, so its true cost is nearer the profiler's ~0.23 cores than the 0.41 measured here.
///
/// ⚠️ ASYNC DISPATCH DOES NOT RESCUE THIS, AND THE MEASUREMENT SAYS SO. The obvious next idea is
/// to stop synchronising inside the phase — dispatch at the top of the tick, do the rest of the
/// send work, collect at the end — so the wall time is hidden rather than paid. That reasoning is
/// wrong here, because it treats a throughput problem as a latency problem. Timed with the kernel
/// alone: 200 launches queued back to back, one synchronise at the end, no upload, no download,
/// no per-call overhead of any kind:
///
///   LZ4 kernel alone         4.891 ms      CPU, 8 workers   0.469 ms wall   -> device is 0.10x
///   distance kernel alone    0.204 ms      CPU, 8 workers   1.382 ms wall   -> device is 6.77x
///
/// The LZ4 kernel by itself is ten times slower than the CPU finishes the same batch in. Overlap
/// hides latency; it cannot make a slower unit faster. Pipelining it would take compression from
/// ~0.45 ms of send-phase wall to ~4.9 ms and make the device the phase's bottleneck — a strictly
/// worse tick, bought with a large restructure of the hottest loop in the server.
///
/// Those two rows are the whole lesson, and they are the same card in the same process: a dense
/// arithmetic sweep over a contiguous array wins by 6.8x, and serial byte-shuffling with random
/// hash probes loses by 10x. What decides it is the shape of the work, not the size of the GPU.
///
/// ── What was NOT attempted, and why ──────────────────────────────────────────────────────────
///
/// Zstd. The hybrid codec routes keyframe bundles to Zstd -2 against a trained dictionary, and
/// that half is far harder than LZ4 rather than easier: FSE and Huffman entropy coding with
/// adaptive tables, bit-exact, against a 16 KiB dictionary. nvCOMP does not ship Zstd compression
/// either, and it is NVIDIA-proprietary, so it is not an option here regardless. LZ4 was the
/// tractable half and it is the one that lost.
/// </summary>
public sealed class GpuLz4Experiment
{
    private const int MinMatch = 4;
    private const int MfLimit = 12;
    private const int LastLiterals = 5;
    private const int GroupSize = 64;
    private const int HashLog = 8;

    private readonly ITestOutputHelper _output;

    public GpuLz4Experiment(ITestOutputHelper output) => _output = output;

    private static void Lz4Kernel(
        ArrayView<byte> src, ArrayView<int> srcOff,
        ArrayView<byte> dst, ArrayView<int> dstOff, ArrayView<int> dstLen,
        int bundleCount)
    {
        const int hashSize = 1 << HashLog;

        var shared = SharedMemory.Allocate<ushort>(GroupSize * hashSize);
        int tid = Group.IdxX;
        int b = Grid.IdxX * Group.DimX + tid;
        if (b >= bundleCount) return;

        int tableBase = tid * hashSize;
        for (int i = 0; i < hashSize; i++) shared[tableBase + i] = 0;

        int inStart = srcOff[b];
        int srcLen = srcOff[b + 1] - inStart;
        int op = dstOff[b];
        int opStart = op;

        if (srcLen < MfLimit + 1)
        {
            EmitLiteralRun(src, dst, ref op, inStart, 0, srcLen);
            dstLen[b] = op - opStart;
            return;
        }

        int mflimit = srcLen - MfLimit;
        int matchlimit = srcLen - LastLiterals;

        uint v0 = Read32(src, inStart);
        shared[tableBase + ((v0 * 2654435761u) >> (32 - HashLog))] = 1;

        int ip = 1;
        int anchor = 0;

        while (ip < mflimit)
        {
            uint v = Read32(src, inStart + ip);
            uint h = (v * 2654435761u) >> (32 - HashLog);
            int cand = shared[tableBase + h] - 1;
            shared[tableBase + h] = (ushort)(ip + 1);

            bool isMatch = false;
            if (cand >= 0 && (ip - cand) < 65536)
            {
                isMatch = Read32(src, inStart + cand) == v;
            }

            if (!isMatch)
            {
                ip++;
                continue;
            }

            int litLen = ip - anchor;

            int m = ip + MinMatch;
            int r = cand + MinMatch;
            while (m < matchlimit && src[inStart + m] == src[inStart + r]) { m++; r++; }
            int mlCode = m - ip - MinMatch;

            int tokenPos = op++;
            dst[tokenPos] = (byte)(((litLen >= 15 ? 15 : litLen) << 4) | (mlCode >= 15 ? 15 : mlCode));

            if (litLen >= 15) EmitLength(dst, ref op, litLen - 15);
            for (int k = 0; k < litLen; k++) dst[op++] = src[inStart + anchor + k];

            int offset = ip - cand;
            dst[op++] = (byte)(offset & 0xFF);
            dst[op++] = (byte)((offset >> 8) & 0xFF);

            if (mlCode >= 15) EmitLength(dst, ref op, mlCode - 15);

            ip = m;
            anchor = ip;
        }

        EmitLiteralRun(src, dst, ref op, inStart, anchor, srcLen - anchor);
        dstLen[b] = op - opStart;
    }

    private static uint Read32(ArrayView<byte> src, int at) =>
        (uint)(src[at] | (src[at + 1] << 8) | (src[at + 2] << 16) | (src[at + 3] << 24));

    private static void EmitLength(ArrayView<byte> dst, ref int op, int remaining)
    {
        while (remaining >= 255) { dst[op++] = 255; remaining -= 255; }
        dst[op++] = (byte)remaining;
    }

    private static void EmitLiteralRun(ArrayView<byte> src, ArrayView<byte> dst, ref int op, int inStart, int from, int count)
    {
        if (count >= 15)
        {
            dst[op++] = 15 << 4;
            EmitLength(dst, ref op, count - 15);
        }
        else
        {
            dst[op++] = (byte)(count << 4);
        }
        for (int k = 0; k < count; k++) dst[op++] = src[inStart + from + k];
    }

    /// <summary>
    /// Runs the kernel, proves every bundle decodes back through the clients' own decoder, and
    /// prints the comparison. Asserts correctness only — the speed figure is the finding, and
    /// asserting on it would make this fail on whatever hardware happens to run the suite.
    /// </summary>
    [Fact]
    public void GpuLz4_IsCorrectAndStillLosesToTheSendWorkers()
    {
        const int bundleCount = 1037;
        const int sendWorkers = 8;
        const int ticksPerSecond = 63;

        var rng = new Random(4242);
        var bundles = new List<byte[]>();
        for (int i = 0; i < bundleCount; i++) bundles.Add(MakeBundle(rng, 1100 + rng.Next(250)));
        int totalRaw = bundles.Sum(b => b.Length);

        using var context = Context.Create(b => b.Default());
        Device? device = context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.Cuda);
        if (device == null)
        {
            _output.WriteLine("skipped: no CUDA device on this host");
            return;
        }

        using var accelerator = device.CreateAccelerator(context);
        var kernel = accelerator.LoadStreamKernel<ArrayView<byte>, ArrayView<int>,
            ArrayView<byte>, ArrayView<int>, ArrayView<int>, int>(Lz4Kernel);

        var flatRaw = new byte[totalRaw];
        var srcOff = new int[bundleCount + 1];
        var dstOff = new int[bundleCount + 1];
        int o = 0, d = 0;
        for (int i = 0; i < bundleCount; i++)
        {
            Buffer.BlockCopy(bundles[i], 0, flatRaw, o, bundles[i].Length);
            o += bundles[i].Length; srcOff[i + 1] = o;
            d += LZ4Codec.MaximumOutputSize(bundles[i].Length); dstOff[i + 1] = d;
        }

        using var dSrc = accelerator.Allocate1D<byte>(totalRaw);
        using var dSrcOff = accelerator.Allocate1D<int>(srcOff.Length);
        using var dDst = accelerator.Allocate1D<byte>(dstOff[bundleCount]);
        using var dDstOff = accelerator.Allocate1D<int>(dstOff.Length);
        using var dDstLen = accelerator.Allocate1D<int>(bundleCount);

        ArrayView<byte> vSrc = dSrc.View.BaseView, vDst = dDst.View.BaseView;
        ArrayView<int> vSrcOff = dSrcOff.View.BaseView, vDstOff = dDstOff.View.BaseView, vDstLen = dDstLen.View.BaseView;
        vSrcOff.CopyFromCPU(srcOff);
        vDstOff.CopyFromCPU(dstOff);

        var hostDst = new byte[dstOff[bundleCount]];
        var hostLen = new int[bundleCount];

        void GpuRun()
        {
            vSrc.CopyFromCPU(flatRaw);
            kernel(((bundleCount + GroupSize - 1) / GroupSize, GroupSize), vSrc, vSrcOff, vDst, vDstOff, vDstLen, bundleCount);
            accelerator.Synchronize();
            vDstLen.CopyToCPU(hostLen);
            vDst.CopyToCPU(hostDst);
        }

        GpuRun();

        int failures = 0;
        long gpuBytes = 0;
        var roundTrip = new byte[4096];
        for (int i = 0; i < bundleCount; i++)
        {
            gpuBytes += hostLen[i];
            int decoded = LZ4Codec.Decode(hostDst.AsSpan(dstOff[i], hostLen[i]), roundTrip.AsSpan(0, bundles[i].Length));
            if (decoded != bundles[i].Length || !roundTrip.AsSpan(0, decoded).SequenceEqual(bundles[i])) failures++;
        }

        long cpuBytes = 0;
        var scratch = new byte[LZ4Codec.MaximumOutputSize(2048)];
        foreach (byte[] bundle in bundles) cpuBytes += LZ4Codec.Encode(bundle, scratch, LZ4Level.L00_FAST);

        double cpuSerial = BestOf(() =>
        {
            var s = new byte[LZ4Codec.MaximumOutputSize(2048)];
            foreach (byte[] bundle in bundles) LZ4Codec.Encode(bundle, s, LZ4Level.L00_FAST);
        });
        var options = new ParallelOptions { MaxDegreeOfParallelism = sendWorkers };
        double cpuParallel = BestOf(() => Parallel.For(0, sendWorkers, options, w =>
        {
            var s = new byte[LZ4Codec.MaximumOutputSize(2048)];
            for (int i = w; i < bundles.Count; i += sendWorkers) LZ4Codec.Encode(bundles[i], s, LZ4Level.L00_FAST);
        }));
        double gpu = BestOf(GpuRun);

        _output.WriteLine($"{device.Name}, {bundleCount} bundles, {totalRaw / 1024.0:F0} KB raw");
        _output.WriteLine($"  ratio      cpu {cpuBytes / (double)totalRaw:F4}   gpu {gpuBytes / (double)totalRaw:F4}");
        _output.WriteLine($"  cpu 1 core      {cpuSerial,7:F3} ms  ({cpuSerial * ticksPerSecond / 1000.0:F3} cores at {ticksPerSecond} ticks/s)");
        _output.WriteLine($"  cpu {sendWorkers} workers    {cpuParallel,7:F3} ms wall");
        _output.WriteLine($"  gpu round trip  {gpu,7:F3} ms   ({cpuParallel / gpu:F2}x the send workers)");

        Assert.Equal(0, failures);
        Assert.True(gpuBytes < totalRaw, "the kernel must actually compress");
    }

    private static double BestOf(Action action, int repetitions = 31)
    {
        double best = double.MaxValue;
        for (int r = 0; r < repetitions; r++)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    private static byte[] MakeBundle(Random rng, int length)
    {
        var b = new byte[length];
        int i = 0;
        while (i < length)
        {
            int run = Math.Min(length - i, 8 + rng.Next(40));
            if (rng.NextDouble() < 0.2)
            {
                byte v = (byte)rng.Next(256);
                for (int k = 0; k < run && i < length; k++, i++) b[i] = v;
            }
            else
            {
                for (int k = 0; k < run && i < length; k++, i++) b[i] = (byte)rng.Next(256);
            }
        }
        return b;
    }
}
