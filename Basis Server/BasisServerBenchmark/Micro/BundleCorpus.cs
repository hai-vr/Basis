using System.Buffers.Binary;

namespace Basis.Benchmark.Micro;

/// <summary>Where a corpus came from. The report must say, because it changes what the numbers mean.</summary>
public enum CorpusOrigin
{
    /// <summary>Bundles captured off a real (or simulated) crowd. Ratios are trustworthy.</summary>
    Captured,

    /// <summary>Generated here. Throughput is trustworthy; ratios are indicative only.</summary>
    Synthetic,
}

/// <summary>
/// The bundle payloads the codec benchmark compresses.
///
/// <para><b>Corpus fidelity is the whole benchmark.</b> A compression measurement is a measurement
/// of its input, and this input is unusually easy to get catastrophically wrong: the redundancy the
/// codecs live on is <em>structural</em> — a room full of near-idle players emitting nearly the
/// same payload — and it is entirely absent from anything built out of random bytes. A first
/// attempt at exactly this corpus elsewhere in the tree measured a ratio of 1.005, pure literal-run
/// overhead with no matches at all, because it filled the payload bodies randomly. Real production
/// traffic sits near 0.87.</para>
///
/// <para>So the generator below models the crowd, not the bytes: a population where most players
/// are resting and therefore repeat each other, a minority moving and therefore not. Prefer a
/// captured corpus regardless — set BASIS_BUNDLE_CAPTURE on the load client and point
/// <c>--corpus</c> at the result. The generator exists so the tool still says something useful on a
/// machine that has never run a crowd, and it labels itself so its ratios are never mistaken for
/// measured ones.</para>
/// </summary>
public sealed class BundleCorpus
{
    public required IReadOnlyList<byte[]> Chunks { get; init; }
    public required CorpusOrigin Origin { get; init; }
    public required string Label { get; init; }

    public long TotalBytes => Chunks.Sum(c => (long)c.Length);

    /// <summary>Raw bytes per bundle chunk: about one MTU of payload before compression.</summary>
    public const int TargetChunkBytes = 1400;

    /// <summary>
    /// Loads captured bundles from a directory of raw files, or a single concatenated file of
    /// length-prefixed records.
    /// </summary>
    public static BundleCorpus? TryLoad(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*.bin").Concat(Directory.GetFiles(path, "*.bundle")).ToArray();
                if (files.Length == 0) return null;
                var chunks = files.Select(File.ReadAllBytes).Where(b => b.Length > 16).ToList();
                if (chunks.Count == 0) return null;
                return new BundleCorpus
                {
                    Chunks = chunks,
                    Origin = CorpusOrigin.Captured,
                    Label = $"{chunks.Count} captured bundles from {path}",
                };
            }

            if (File.Exists(path))
            {
                byte[] all = File.ReadAllBytes(path);
                var chunks = new List<byte[]>();
                int offset = 0;
                while (offset + 4 <= all.Length)
                {
                    int len = BinaryPrimitives.ReadInt32LittleEndian(all.AsSpan(offset));
                    offset += 4;
                    if (len <= 0 || offset + len > all.Length) break;
                    chunks.Add(all.AsSpan(offset, len).ToArray());
                    offset += len;
                }
                if (chunks.Count == 0) return null;
                return new BundleCorpus
                {
                    Chunks = chunks,
                    Origin = CorpusOrigin.Captured,
                    Label = $"{chunks.Count} captured bundles from {Path.GetFileName(path)}",
                };
            }
        }
        catch { /* fall through to synthetic */ }
        return null;
    }

    /// <summary>
    /// Builds a corpus from a modelled crowd.
    /// </summary>
    /// <param name="chunkCount">How many MTU-sized bundles to produce.</param>
    /// <param name="movingShare">
    /// Fraction of the crowd in motion. This is the single parameter that decides the answer:
    /// a resting crowd is where essentially all of the compression win lives (measured ~20.8%
    /// saved on keyframes), while a crowd all moving at once compresses to slightly WORSE than
    /// raw. 0.25 is a busy-but-normal instance.
    /// </param>
    /// <param name="deltaShare">
    /// Fraction of entries on the delta channel rather than keyframe. Since the switch that put
    /// steady state on deltas, this is most of the traffic — and deltas compress far less well
    /// (3.7% against 20.8%), so a corpus that under-weights them overstates the codec badly.
    /// </param>
    public static BundleCorpus Generate(int chunkCount = 512, double movingShare = 0.25, double deltaShare = 0.8)
    {
        var rng = new Random(20260818);
        var chunks = new List<byte[]>(chunkCount);

        // The resting crowd's shared shapes. Idle players do not emit identical bytes — they emit
        // payloads that differ in a few low bits of a few fields — so the corpus keeps a small set
        // of archetypes and perturbs them. That is what produces long matches at realistic
        // distances, which is what the codecs are actually paid for.
        const int archetypes = 6;
        var keyframeArchetypes = new byte[archetypes][];
        var deltaArchetypes = new byte[archetypes][];
        for (int a = 0; a < archetypes; a++)
        {
            keyframeArchetypes[a] = new byte[96];
            rng.NextBytes(keyframeArchetypes[a]);
            deltaArchetypes[a] = new byte[18];
            rng.NextBytes(deltaArchetypes[a]);
        }

        var buffer = new byte[TargetChunkBytes + 256];
        for (int c = 0; c < chunkCount; c++)
        {
            int written = 0;
            while (written < TargetChunkBytes)
            {
                bool delta = rng.NextDouble() < deltaShare;
                bool moving = rng.NextDouble() < movingShare;
                byte[] archetype = delta
                    ? deltaArchetypes[rng.Next(archetypes)]
                    : keyframeArchetypes[rng.Next(archetypes)];

                int bodyLength = archetype.Length;
                if (written + 3 + bodyLength > buffer.Length) break;

                // [origChannel:1][len:2-LE][body] - the entry framing the bundle packer emits.
                buffer[written++] = delta ? (byte)52 : (byte)51;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(written), (ushort)bodyLength);
                written += 2;

                archetype.CopyTo(buffer.AsSpan(written));
                if (moving)
                {
                    // A moving player rewrites its position and rotation fields outright. Everything
                    // after them still matches its archetype, which is why a moving crowd degrades
                    // the ratio rather than destroying it.
                    int churn = Math.Min(bodyLength, delta ? 10 : 28);
                    for (int k = 0; k < churn; k++) buffer[written + k] = (byte)rng.Next(256);
                }
                else
                {
                    // Resting is not frozen: micro-jitter in the low bits of the first field, which
                    // is what an idle tracked player actually sends.
                    buffer[written] ^= (byte)rng.Next(4);
                }

                written += bodyLength;
            }

            chunks.Add(buffer.AsSpan(0, written).ToArray());
        }

        return new BundleCorpus
        {
            Chunks = chunks,
            Origin = CorpusOrigin.Synthetic,
            Label = $"{chunks.Count} generated bundles ({movingShare:P0} of the crowd moving, {deltaShare:P0} delta entries)",
        };
    }
}
