using System.Diagnostics;
using System.Text;
using Basis.Network.Core.Compression;
using K4os.Compression.LZ4;

namespace Basis.Benchmark.Micro;

/// <summary>One codec configuration measured over the corpus.</summary>
public sealed record CodecPoint(string Codec, int Level, double Ratio, double MegabytesPerSecond)
{
    /// <summary>Fraction of the raw bytes this configuration removes. Negative means it expands.</summary>
    public double Saved => 1.0 - Ratio;

    /// <summary>
    /// Bytes removed per millisecond of one core's time. The exchange rate the decision turns on.
    ///
    /// Ratio alone picks the highest level every time, and throughput alone picks the lowest. This
    /// is the only figure that lets the two be compared, and it is what a tick budget is actually
    /// spending: the server has some number of milliseconds per tick it can give to compression,
    /// and it wants the most egress removed for them.
    /// </summary>
    public double BytesSavedPerCoreMs => MegabytesPerSecond * 1_000_000.0 / 1000.0 * Saved;
}

public sealed class CompressionBenchResult
{
    public required IReadOnlyList<CodecPoint> Points { get; init; }
    public required BundleCorpus Corpus { get; init; }
    public required bool ZstdDictionaryPresent { get; init; }
    public required int RecommendedZstdLevel { get; init; }
    public required bool RecommendZstdEnabled { get; init; }

    public CodecPoint? Lz4 => Points.FirstOrDefault(p => p.Codec == "lz4");
    public CodecPoint? BestZstd => Points.Where(p => p.Codec == "zstd")
                                         .OrderByDescending(p => p.BytesSavedPerCoreMs)
                                         .FirstOrDefault();

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  Corpus: {Corpus.Label}");
        if (Corpus.Origin == CorpusOrigin.Synthetic)
            sb.AppendLine("    (generated, not captured - throughput below is real, ratios are indicative)");
        if (!ZstdDictionaryPresent)
            sb.AppendLine("    (no zstd dictionary embedded in this build - the zstd rows are dictionary-less and understate it)");

        sb.AppendLine("    codec  level     ratio    saved      MB/s   bytes saved per core-ms");
        foreach (CodecPoint p in Points)
        {
            sb.AppendLine($"    {p.Codec,-5}  {(p.Level == int.MinValue ? "  -" : p.Level.ToString()),5}   {p.Ratio,7:F4}  {p.Saved,7:P1}  {p.MegabytesPerSecond,8:N0}   {p.BytesSavedPerCoreMs,12:N0}");
        }
        return sb.ToString();
    }
}

/// <summary>
/// What compression costs and what it returns, on this machine's cores.
///
/// <para>The reason this is worth measuring rather than assuming: the codec's value is not a
/// property of the codec. It swings by a factor of five with what the crowd is doing — a resting
/// room compresses about 20.8% out of keyframes, a room where everyone is moving <em>expands</em>
/// by about half a percent — and the CPU it costs is a property of the host, which ranges from a
/// few fast cores to a hundred slow ones. Those two facts multiply, and the shipped default was
/// fitted at one point in that space.</para>
///
/// <para>What it must not conclude: that the codec is the thing to optimise. Measured on this
/// corpus the encoder runs several hundred MB/s per core, which is far above the rate the
/// production figures imply — meaning most of the time attributed to "compression" in a tick is
/// the surrounding buffer building, chunk selection and retries, not the codec call. So a level
/// change moves less than the ms/tick figure suggests, and this benchmark reports the codec's own
/// cost separately for exactly that reason.</para>
/// </summary>
public static class CompressionBench
{
    private const int WarmupPasses = 3;

    /// <summary>
    /// Repetitions per configuration, of which the FASTEST is reported.
    ///
    /// <para>Best-of rather than mean, because the noise here is one-sided: a scheduling
    /// preemption, a background process or a thermal step can only ever make a pass slower, never
    /// faster than the machine is capable of. Averaging folds those in and reports a number that
    /// belongs to the machine's interruptions rather than to the codec. An early version of this
    /// timed each level once over a tenth of a second and produced a table where level -1 ran three
    /// and a half times faster than level -2 — an ordering that is not physically possible and was
    /// pure sampling noise.</para>
    /// </summary>
    private const int Repetitions = 3;

    /// <summary>Minimum wall time per repetition, so a fast codec is not timed over a few microseconds.</summary>
    private static readonly TimeSpan MinimumRepetition = TimeSpan.FromMilliseconds(400);

    public static CompressionBenchResult Run(BundleCorpus corpus, Action<string>? progress = null)
    {
        var points = new List<CodecPoint>();

        progress?.Invoke("    lz4 (fast)...");
        points.Add(MeasureLz4(corpus));

        // The levels worth asking about. Zstd's negative levels are the fast end and are where a
        // real-time send path belongs; the positive end is included only so the report can show
        // what it would cost, because "why not level 3" is the obvious question and the answer is
        // an order of magnitude of throughput.
        bool dictionary = BasisAvatarBundleZstd.Available;
        foreach (int level in new[] { -5, -3, -2, -1, 1, 3 })
        {
            if (level < BasisAvatarBundleZstd.MinLevel || level > BasisAvatarBundleZstd.MaxLevel) continue;
            progress?.Invoke($"    zstd level {level}...");
            points.Add(MeasureZstd(corpus, level, dictionary));
        }

        CodecPoint lz4 = points.First(p => p.Codec == "lz4");
        CodecPoint bestZstd = points.Where(p => p.Codec == "zstd")
                                    .OrderByDescending(p => p.BytesSavedPerCoreMs)
                                    .First();

        // Zstd has to beat LZ4 on the exchange rate, not on ratio. A configuration that removes
        // more bytes for more CPU than the server has to give is not an improvement, it is a
        // different failure. The margin keeps a coin-flip difference from churning the config.
        bool zstdWins = bestZstd.BytesSavedPerCoreMs > lz4.BytesSavedPerCoreMs * 1.10 && bestZstd.Saved > 0;

        return new CompressionBenchResult
        {
            Points = points,
            Corpus = corpus,
            ZstdDictionaryPresent = dictionary,
            RecommendedZstdLevel = bestZstd.Level,
            // Never recommend turning zstd on when this build has no dictionary to compress
            // against: without one the codec is inert by design, and a dictionary-less measurement
            // is not evidence about the configuration that would actually run.
            RecommendZstdEnabled = zstdWins && dictionary,
        };
    }

    private static CodecPoint MeasureLz4(BundleCorpus corpus)
    {
        int maxOut = corpus.Chunks.Max(c => LZ4Codec.MaximumOutputSize(c.Length));
        var dst = new byte[maxOut];
        return Measure("lz4", int.MinValue, corpus,
            chunk => LZ4Codec.Encode(chunk, dst, LZ4Level.L00_FAST));
    }

    /// <summary>
    /// Times one compress function over the corpus: warm up, then take the best of several
    /// repetitions, each long enough to be timed honestly.
    /// </summary>
    private static CodecPoint Measure(string codec, int level, BundleCorpus corpus, Func<byte[], int> compress)
    {
        for (int pass = 0; pass < WarmupPasses; pass++)
            foreach (byte[] chunk in corpus.Chunks) compress(chunk);

        // Ratio comes from a single clean pass - it is deterministic, so repeating it would only
        // measure the same bytes again.
        long compressed = 0, raw = 0;
        foreach (byte[] chunk in corpus.Chunks)
        {
            int n = compress(chunk);
            compressed += n > 0 ? n : chunk.Length;
            raw += chunk.Length;
        }

        // Enough passes that one repetition clears the minimum, sized from the warmup's own speed
        // so a slow level is not given the same pass count as a fast one.
        long probeStart = Stopwatch.GetTimestamp();
        foreach (byte[] chunk in corpus.Chunks) compress(chunk);
        double onePass = Stopwatch.GetElapsedTime(probeStart).TotalSeconds;
        int passes = onePass <= 0 ? 32 : Math.Clamp((int)(MinimumRepetition.TotalSeconds / onePass), 1, 4096);

        double bestMbPerSecond = 0;
        for (int rep = 0; rep < Repetitions; rep++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int pass = 0; pass < passes; pass++)
                foreach (byte[] chunk in corpus.Chunks) compress(chunk);
            double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;

            double mbPerSecond = seconds <= 0 ? 0 : corpus.TotalBytes * passes / seconds / 1_000_000.0;
            if (mbPerSecond > bestMbPerSecond) bestMbPerSecond = mbPerSecond;
        }

        return new CodecPoint(codec, level, raw > 0 ? (double)compressed / raw : 1, bestMbPerSecond);
    }

    private static CodecPoint MeasureZstd(BundleCorpus corpus, int level, bool dictionary)
    {
        int previous = BasisAvatarBundleZstd.Level;
        BasisAvatarBundleZstd.SetLevel(level);
        try
        {
            int maxOut = corpus.Chunks.Max(c => BasisAvatarBundleZstd.MaximumOutputSize(c.Length));
            var dst = new byte[maxOut];

            // Without an embedded dictionary the server's wrapper refuses to compress at all, so
            // fall back to a bare compressor from the codec's own factory. Built there rather than
            // here so the frame parameters cannot drift out of step with what the server emits: a
            // hand-rolled copy of this setup was omitting magicless framing, which put 4 bytes of
            // frame magic on every measured chunk that the server would never have sent.
            using var fallback = dictionary ? null : BasisAvatarBundleZstd.CreateCompressor(level);

            int Compress(byte[] chunk)
            {
                if (dictionary)
                    return BasisAvatarBundleZstd.TryCompress(chunk, dst, out int written) ? written : chunk.Length;
                return fallback!.Wrap(chunk, dst);
            }

            return Measure("zstd", level, corpus, Compress);
        }
        finally
        {
            BasisAvatarBundleZstd.SetLevel(previous);
        }
    }
}
