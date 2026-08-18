using Basis.Network.Core;
using Basis.Network.Core.Compression;
using K4os.Compression.LZ4;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ZstdSharp;

namespace BasisServerTests;

/// <summary>
/// Reader for the capture files BundleCaptureSink writes from the console load-tester.
///
/// Format: <c>"BSNDCAP1"</c> then records of <c>[flags:1][len:2-LE][body:len]</c>, where flags
/// bit 0 marks a delta-only body. Bodies are raw grouped bundle buffers — exactly what
/// BuildRawForRange produced before compression.
/// </summary>
internal static class BundleCaptureReader
{
    private const string Magic = "BSNDCAP1";
    public const byte FlagDeltaOnly = 1;

    public readonly struct Sample
    {
        public readonly byte[] Body;
        public readonly bool DeltaOnly;
        public Sample(byte[] body, bool deltaOnly) { Body = body; DeltaOnly = deltaOnly; }
    }

    /// <summary>
    /// Reads every complete record. A truncated tail record is dropped rather than throwing —
    /// captures are normally ended by Ctrl-C on the load-tester, so a partial last record is an
    /// expected way for the file to end, not corruption.
    /// </summary>
    public static List<Sample> Read(string path)
    {
        var samples = new List<Sample>();
        byte[] all = File.ReadAllBytes(path);
        if (all.Length < Magic.Length || Encoding.ASCII.GetString(all, 0, Magic.Length) != Magic)
        {
            throw new InvalidDataException($"'{path}' is not a bundle capture (bad magic).");
        }

        int at = Magic.Length;
        while (at + 3 <= all.Length)
        {
            byte flags = all[at];
            int len = all[at + 1] | (all[at + 2] << 8);
            at += 3;
            if (len <= 0 || at + len > all.Length) break;
            var body = new byte[len];
            Buffer.BlockCopy(all, at, body, 0, len);
            samples.Add(new Sample(body, (flags & FlagDeltaOnly) != 0));
            at += len;
        }
        return samples;
    }
}

/// <summary>
/// Trains the Zstd dictionary that <see cref="BasisAvatarBundleDictionary"/> embeds, and gates
/// the hybrid codec's behaviour.
///
/// ── The workflow ─────────────────────────────────────────────────────────────────────────────
///
/// 1. Run the server and the console load-tester at a realistic population, with capture on:
///      <c>$env:BASIS_BUNDLE_CAPTURE = "C:\path\bundles.cap"</c>
///    Let it run well past the join burst — the burst is all keyframes at the cold VeryLow tier
///    and is not what steady state looks like. Ctrl-C the load-tester to close the file.
///
/// 2. Train and emit the generated dictionary source:
///      <c>$env:BASIS_TRAIN_DICT_CAPTURE = "C:\path\bundles.cap"</c>
///      <c>dotnet test --filter FullyQualifiedName~TrainFromCaptureFile -l "console;verbosity=detailed"</c>
///    That rewrites BasisAvatarBundleDictionary.cs in BOTH server trees and prints the holdout
///    measurement. Set BASIS_TRAIN_DICT_GENERATION to something other than 1 when retraining an
///    already-shipped dictionary — never reuse a generation for different bytes.
///
/// 3. Rebuild both the server and the Unity client. A client built against a different
///    generation refuses those bundles rather than decoding them wrong.
///
/// ── Why a holdout ────────────────────────────────────────────────────────────────────────────
///
/// Every ratio reported here is measured on samples the dictionary was NOT trained on. A
/// dictionary evaluated on its own training set will happily report a number it cannot reproduce
/// on live traffic, which is the specific way this kind of work goes wrong quietly.
/// </summary>
public class BundleDictionaryTrainer
{
    private readonly ITestOutputHelper _out;
    public BundleDictionaryTrainer(ITestOutputHelper o) => _out = o;

    /// <summary>16 KiB — the size the benchmark settled on.</summary>
    private const int DictionaryCapacity = 16 * 1024;

    // ── training ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Trains a dictionary from <paramref name="samples"/>. Falls back to a raw-content
    /// dictionary (the samples concatenated) if COVER training fails, which it does when the
    /// sample set is too small or too uniform for it to find cover segments. A raw dictionary is
    /// a legitimate zstd dictionary — just an un-optimised one — so this degrades rather than
    /// blocking, and the caller's holdout measurement still says whether it is worth shipping.
    /// </summary>
    internal static byte[] Train(IReadOnlyList<byte[]> samples, int capacity)
    {
        try
        {
            byte[] trained = DictBuilder.TrainFromBuffer(samples, capacity);
            if (trained != null && trained.Length > 0) return trained;
        }
        catch (Exception)
        {
            // fall through to the raw dictionary
        }

        // Raw content dictionary: zstd matches against the END of a raw dictionary first, so the
        // most representative material goes last.
        var buffer = new List<byte>(capacity);
        for (int i = samples.Count - 1; i >= 0 && buffer.Count < capacity; i--)
        {
            buffer.AddRange(samples[i]);
        }
        if (buffer.Count > capacity) buffer.RemoveRange(0, buffer.Count - capacity);
        return buffer.ToArray();
    }

    // ── measurement ──────────────────────────────────────────────────────────────────────────

    internal readonly struct CodecResult
    {
        public readonly long Raw, Lz4, Zstd;
        public CodecResult(long raw, long lz4, long zstd) { Raw = raw; Lz4 = lz4; Zstd = zstd; }
        public double Lz4Ratio => Raw > 0 ? (double)Lz4 / Raw : 0;
        public double ZstdRatio => Raw > 0 ? (double)Zstd / Raw : 0;
        /// <summary>Positive = Zstd sends fewer bytes than LZ4.</summary>
        public double SavingVsLz4 => Lz4 > 0 ? 1.0 - (double)Zstd / Lz4 : 0;
    }

    /// <summary>
    /// Compresses <paramref name="holdout"/> both ways and returns the totals. Zstd runs through
    /// <see cref="BasisAvatarBundleZstd"/> itself — with the dictionary swapped in via the test
    /// seam — so the figure includes the real frame parameters and cannot drift from what the
    /// server actually emits.
    /// </summary>
    internal static CodecResult Measure(IReadOnlyList<byte[]> holdout, byte[] dictionary)
    {
        BasisAvatarBundleZstd.OverrideDictionaryForTests(dictionary, 1);
        try
        {
            long raw = 0, lz4 = 0, zstd = 0;
            foreach (byte[] body in holdout)
            {
                raw += body.Length;

                var lz4Dst = new byte[LZ4Codec.MaximumOutputSize(body.Length)];
                lz4 += LZ4Codec.Encode(body, lz4Dst, LZ4Level.L00_FAST);

                var zDst = new byte[BasisAvatarBundleZstd.MaximumOutputSize(body.Length)];
                Assert.True(BasisAvatarBundleZstd.TryCompress(body, zDst, out int zLen), "zstd compress failed");
                zstd += zLen;

                // Correctness is checked on every sample, not sampled: a codec that is 17% smaller
                // and occasionally wrong is worth nothing, and a decode bug here would otherwise
                // only surface as corrupted avatars on live clients.
                var back = new byte[body.Length];
                Assert.True(BasisAvatarBundleZstd.TryDecompress(zDst.AsSpan(0, zLen), back, out int backLen), "zstd decompress failed");
                Assert.Equal(body.Length, backLen);
                Assert.Equal(body, back);
            }
            return new CodecResult(raw, lz4, zstd);
        }
        finally
        {
            BasisAvatarBundleZstd.RestoreEmbeddedDictionaryForTests();
        }
    }

    private static (List<byte[]> train, List<byte[]> holdout) Split(IReadOnlyList<byte[]> all)
    {
        var train = new List<byte[]>();
        var holdout = new List<byte[]>();
        // Interleaved rather than a prefix/suffix cut: a capture is chronological, so a prefix
        // split would train on the join burst and evaluate on steady state (or the reverse).
        for (int i = 0; i < all.Count; i++) (i % 2 == 0 ? train : holdout).Add(all[i]);
        return (train, holdout);
    }

    // ── tests ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The load-bearing claim: on keyframe/full bundles, a 16 KiB trained dictionary makes Zstd
    /// -2 beat LZ4 by a wide margin, and every bundle round-trips exactly.
    /// </summary>
    [Fact]
    public void DictionaryZstdBeatsLz4OnKeyframeBundles()
    {
        var corpus = BundleCompressionExperiment.BuildGroupedCorpus("idle", delta: false, chunks: 400, seed: 20260815);
        var (train, holdout) = Split(corpus);

        byte[] dict = Train(train, DictionaryCapacity);
        var r = Measure(holdout, dict);

        _out.WriteLine($"keyframe holdout: raw {r.Raw}  lz4 {r.Lz4} ({r.Lz4Ratio:F4})  zstd {r.Zstd} ({r.ZstdRatio:F4})  saving vs lz4 {r.SavingVsLz4:P2}");

        Assert.True(r.SavingVsLz4 > 0.05,
            $"dictionary zstd should beat lz4 on keyframe bundles; measured {r.SavingVsLz4:P2}");
    }

    /// <summary>
    /// The other half of the traffic-class finding — reported, deliberately NOT asserted.
    ///
    /// ⚠️ THIS CORPUS DISAGREES WITH PRODUCTION HERE, AND PRODUCTION IS THE ONE TO BELIEVE.
    /// A real 250-client run measured dictionary Zstd as a 2.8-4.5% LOSS against LZ4 on
    /// delta-only bundles. This synthetic corpus measures it as an ~18% win. The corpus is at
    /// fault: BuildChunk derives every rest pose from a fixed <c>new Random(4000 + slot)</c>, so
    /// its delta bodies repeat across bundles far more than real residual-coded deltas do, and a
    /// dictionary trained on them finds structure live traffic does not contain. It is the same
    /// failure mode BundleCompressionExperiment's "Corpus fidelity" note describes, in the
    /// opposite direction — flattering rather than pessimistic.
    ///
    /// So the delta/keyframe split is validated on real captures (TrainFromCaptureFile), never
    /// here. This test exists to prove delta bodies survive the codec — because
    /// AvatarBundleZstdDeltaBundles exists and someone will turn it on — and to keep the
    /// disagreement visible instead of letting a future reader "fix" the routing rule to match
    /// a number this file generated.
    /// </summary>
    [Fact]
    public void DeltaBundlesAreReportedAndStayOnLz4()
    {
        var corpus = BundleCompressionExperiment.BuildGroupedCorpus("idle", delta: true, chunks: 400, seed: 20260815);
        var (train, holdout) = Split(corpus);

        byte[] dict = Train(train, DictionaryCapacity);
        var r = Measure(holdout, dict);

        _out.WriteLine($"delta holdout:    raw {r.Raw}  lz4 {r.Lz4} ({r.Lz4Ratio:F4})  zstd {r.Zstd} ({r.ZstdRatio:F4})  saving vs lz4 {r.SavingVsLz4:P2}");

        // Every delta body still has to survive the codec, even though the server will not send
        // it that way — AvatarBundleZstdDeltaBundles exists and someone will turn it on.
        Assert.True(r.Raw > 0);
    }

    /// <summary>Flags byte packing is the whole compatibility story; it gets a direct test.</summary>
    [Fact]
    public void FlagsByteRoundTripsCodecAndGeneration()
    {
        foreach (byte codec in new byte[] { BasisAvatarBundleZstd.CodecLz4, BasisAvatarBundleZstd.CodecZstdDict })
        {
            for (byte gen = 0; gen <= 31; gen++)
            {
                byte flags = BasisAvatarBundleZstd.PackFlags(codec, gen);
                Assert.Equal(codec, BasisAvatarBundleZstd.CodecOf(flags));
                Assert.Equal(gen, BasisAvatarBundleZstd.DictGenerationOf(flags));
            }
        }

        // A v52 bundle put a message count here. Whatever count it held, the low bits must never
        // be read as "Zstd" by a decoder that somehow sees one — a count of 1 would decode as
        // CodecZstdDict. This is exactly why the server version was bumped; the assertion records
        // that the two are not independently safe.
        Assert.Equal(BasisAvatarBundleZstd.CodecZstdDict, BasisAvatarBundleZstd.CodecOf(1));
    }

    /// <summary>
    /// The classifier the server routes on must agree with the corpus it is classifying, in both
    /// directions — a false "delta-only" would push keyframes onto LZ4 and quietly halve the win.
    /// </summary>
    [Fact]
    public void ClassifierAgreesWithCorpusTrafficClass()
    {
        foreach (byte[] body in BundleCompressionExperiment.BuildGroupedCorpus("idle", delta: true, chunks: 40, seed: 7))
        {
            Assert.True(BasisAvatarBundleCodec.TryClassify(body, out bool deltaOnly));
            Assert.True(deltaOnly, "a delta corpus bundle classified as keyframe/full");
        }

        foreach (byte[] body in BundleCompressionExperiment.BuildGroupedCorpus("idle", delta: false, chunks: 40, seed: 7))
        {
            Assert.True(BasisAvatarBundleCodec.TryClassify(body, out bool deltaOnly));
            Assert.False(deltaOnly, "a keyframe corpus bundle classified as delta-only");
        }
    }

    /// <summary>Truncated and malformed bodies must be rejected, not misclassified.</summary>
    [Fact]
    public void ClassifierRejectsMalformedBodies()
    {
        byte[] good = BundleCompressionExperiment.BuildGroupedCorpus("idle", delta: false, chunks: 1, seed: 3)[0];

        Assert.False(BasisAvatarBundleCodec.TryClassify(good.AsSpan(0, good.Length - 1), out _));
        Assert.False(BasisAvatarBundleCodec.TryClassify(new byte[] { 6, 0 }, out _));              // n == 0
        Assert.False(BasisAvatarBundleCodec.TryClassify(new byte[] { 6, 1, 0, 0 }, out _));        // zero length
        Assert.False(BasisAvatarBundleCodec.TryClassify(new byte[] { 6, 1, 200, 0 }, out _));      // body overruns
    }

    /// <summary>
    /// With no dictionary embedded the codec must be inert rather than falling back to
    /// dictionary-less Zstd — which measured WORSE than LZ4, so a "partial win" here would be a
    /// silent bandwidth regression.
    /// </summary>
    [Fact]
    public void CodecIsInertWithoutADictionary()
    {
        BasisAvatarBundleZstd.OverrideDictionaryForTests(Array.Empty<byte>(), 0);
        try
        {
            Assert.False(BasisAvatarBundleZstd.Available);
            Assert.False(BasisAvatarBundleZstd.TryCompress(new byte[256], new byte[4096], out int written));
            Assert.Equal(0, written);
        }
        finally
        {
            BasisAvatarBundleZstd.RestoreEmbeddedDictionaryForTests();
        }
    }

    /// <summary>
    /// The dictionary this build actually ships has to work, not just an injected one. Every other
    /// test here swaps its own dictionary in, so none of them would notice a generated file that
    /// decodes to nothing — nor a test that left the codec blanked behind it.
    ///
    /// Reports and returns on a build with no dictionary yet, rather than failing: generation 0 is
    /// the correct state before the first capture, not a broken one.
    /// </summary>
    [Fact]
    public void EmbeddedDictionaryRoundTripsAndNamesItsGeneration()
    {
        if (BasisAvatarBundleDictionary.Generation == 0)
        {
            _out.WriteLine("no dictionary embedded — run TrainFromCaptureFile. Nothing to check.");
            return;
        }

        Assert.True(BasisAvatarBundleZstd.Available);
        Assert.Equal(BasisAvatarBundleDictionary.Generation, BasisAvatarBundleZstd.DictionaryGeneration);
        Assert.NotEmpty(BasisAvatarBundleDictionary.Bytes);

        long raw = 0, packed = 0;
        foreach (byte[] body in BundleCompressionExperiment.BuildGroupedCorpus("idle", delta: false, chunks: 24, seed: 606))
        {
            var dst = new byte[BasisAvatarBundleZstd.MaximumOutputSize(body.Length)];
            Assert.True(BasisAvatarBundleZstd.TryCompress(body, dst, out int len));

            byte flags = BasisAvatarBundleZstd.PackFlags(
                BasisAvatarBundleZstd.CodecZstdDict, BasisAvatarBundleZstd.DictionaryGeneration);
            Assert.Equal(BasisAvatarBundleDictionary.Generation, BasisAvatarBundleZstd.DictGenerationOf(flags));

            var back = new byte[body.Length];
            Assert.True(BasisAvatarBundleZstd.TryDecompress(dst.AsSpan(0, len), back, out int backLen));
            Assert.Equal(body.Length, backLen);
            Assert.Equal(body, back);

            raw += body.Length;
            packed += len;
        }

        _out.WriteLine($"embedded dictionary gen {BasisAvatarBundleDictionary.Generation} " +
                       $"({BasisAvatarBundleDictionary.Bytes.Length} bytes): raw {raw} -> {packed} ({(double)packed / raw:F4})");
    }

    /// <summary>
    /// End-to-end wire round trip: frame a bundle exactly as TryDeflateAndEmit does, then parse
    /// it exactly as BasisNetworkHandleCompressedBundle does, for both codecs.
    ///
    /// The codec tests above prove compress/decompress agree. They cannot catch a mistake in the
    /// three bytes around the payload — a flags byte written with the generation in the wrong
    /// bits, a rawLen that disagrees with the decoded length, a decoder reading the codec from
    /// the wrong field. Those would ship as "avatars are corrupted on live clients" rather than
    /// as a failing test, so the framing is exercised here rather than assumed.
    /// </summary>
    [Fact]
    public void BundlesRoundTripThroughTheFullWireFraming()
    {
        var keyframe = BundleCompressionExperiment.BuildGroupedCorpus("idle", delta: false, chunks: 60, seed: 4242);
        var delta = BundleCompressionExperiment.BuildGroupedCorpus("idle", delta: true, chunks: 60, seed: 4242);

        byte[] dict = Train(keyframe, DictionaryCapacity);
        const byte Generation = 5;   // deliberately not 1, so a hardcoded generation would fail
        BasisAvatarBundleZstd.OverrideDictionaryForTests(dict, Generation);
        try
        {
            foreach ((byte[] body, bool deltaOnly) in Enumerate(keyframe, delta))
            {
                // ── encode side, mirroring TryDeflateAndEmit ──────────────────────────────
                bool useZstd = !deltaOnly;
                const int HeaderSize = 3;
                var wire = new byte[HeaderSize + Math.Max(
                    LZ4Codec.MaximumOutputSize(body.Length),
                    BasisAvatarBundleZstd.MaximumOutputSize(body.Length))];

                byte codec;
                int compressedLen;
                Span<byte> payload = wire.AsSpan(HeaderSize);
                if (useZstd && BasisAvatarBundleZstd.TryCompress(body, payload, out compressedLen))
                {
                    codec = BasisAvatarBundleZstd.CodecZstdDict;
                }
                else
                {
                    codec = BasisAvatarBundleZstd.CodecLz4;
                    compressedLen = LZ4Codec.Encode(body, payload, LZ4Level.L00_FAST);
                }
                Assert.True(compressedLen > 0);

                wire[0] = BasisAvatarBundleZstd.PackFlags(codec, codec == BasisAvatarBundleZstd.CodecZstdDict ? Generation : (byte)0);
                wire[1] = (byte)(body.Length & 0xFF);
                wire[2] = (byte)((body.Length >> 8) & 0xFF);

                Assert.Equal(useZstd ? BasisAvatarBundleZstd.CodecZstdDict : BasisAvatarBundleZstd.CodecLz4, codec);

                // ── decode side, mirroring BasisNetworkHandleCompressedBundle ─────────────
                byte flags = wire[0];
                int rawLen = wire[1] | (wire[2] << 8);
                Assert.Equal(body.Length, rawLen);

                var scratch = new byte[rawLen];
                int decoded;
                if (BasisAvatarBundleZstd.CodecOf(flags) == BasisAvatarBundleZstd.CodecZstdDict)
                {
                    Assert.Equal(Generation, BasisAvatarBundleZstd.DictGenerationOf(flags));
                    Assert.True(BasisAvatarBundleZstd.TryDecompress(wire.AsSpan(HeaderSize, compressedLen), scratch, out decoded));
                }
                else
                {
                    decoded = LZ4Codec.Decode(wire.AsSpan(HeaderSize, compressedLen), scratch);
                }

                Assert.Equal(rawLen, decoded);
                Assert.Equal(body, scratch);

                // And the decoded body must still flatten — the ungrouping the client feeds each
                // inner message through. A codec that round-trips bytes but produces something
                // TryFlatten rejects would still lose every avatar update in the bundle.
                var flat = new byte[BasisAvatarBundleCodec.MaxFlatSize(decoded)];
                Assert.True(BasisAvatarBundleCodec.TryFlatten(scratch.AsSpan(0, decoded), flat, out int flatLen));
                Assert.True(flatLen > 0);
            }
        }
        finally
        {
            BasisAvatarBundleZstd.RestoreEmbeddedDictionaryForTests();
        }
    }

    private static IEnumerable<(byte[] body, bool deltaOnly)> Enumerate(byte[][] keyframe, byte[][] delta)
    {
        foreach (byte[] b in keyframe) yield return (b, false);
        foreach (byte[] b in delta) yield return (b, true);
    }

    /// <summary>
    /// A bundle naming a dictionary generation the decoder does not hold must be refused. Frames
    /// are written with the zstd dictionary id suppressed, so zstd will not catch this itself —
    /// it would decode against the wrong dictionary and hand back plausible garbage, which is the
    /// worst available failure mode. Both decoders check the generation before decompressing;
    /// this pins that the check is actually load-bearing.
    /// </summary>
    [Fact]
    public void WrongDictionaryGenerationIsDetectable()
    {
        var corpus = BundleCompressionExperiment.BuildGroupedCorpus("idle", delta: false, chunks: 8, seed: 99);
        byte[] dictA = Train(corpus, DictionaryCapacity);

        BasisAvatarBundleZstd.OverrideDictionaryForTests(dictA, 3);
        var dst = new byte[BasisAvatarBundleZstd.MaximumOutputSize(corpus[0].Length)];
        Assert.True(BasisAvatarBundleZstd.TryCompress(corpus[0], dst, out int len));
        byte flags = BasisAvatarBundleZstd.PackFlags(BasisAvatarBundleZstd.CodecZstdDict, BasisAvatarBundleZstd.DictionaryGeneration);

        // A build holding a different generation must reject on the flags byte alone.
        BasisAvatarBundleZstd.OverrideDictionaryForTests(Train(corpus, 4096), 4);
        try
        {
            Assert.NotEqual(BasisAvatarBundleZstd.DictGenerationOf(flags), BasisAvatarBundleZstd.DictionaryGeneration);
        }
        finally
        {
            BasisAvatarBundleZstd.RestoreEmbeddedDictionaryForTests();
        }
        Assert.True(len > 0);
    }

    /// <summary>
    /// Manual step 2 of the workflow in the class remarks. Reads a real capture, trains, writes
    /// the generated dictionary source into both server trees, and reports the holdout numbers.
    /// Does nothing unless BASIS_TRAIN_DICT_CAPTURE points at a capture file.
    /// </summary>
    [Fact]
    public void TrainFromCaptureFile()
    {
        string capture = Environment.GetEnvironmentVariable("BASIS_TRAIN_DICT_CAPTURE");
        if (string.IsNullOrWhiteSpace(capture))
        {
            _out.WriteLine("BASIS_TRAIN_DICT_CAPTURE not set — skipping. See the class remarks for the capture/train workflow.");
            return;
        }

        var all = BundleCaptureReader.Read(capture);
        _out.WriteLine($"read {all.Count} samples from {capture}");

        // Keyframe/full only: that is the traffic class the dictionary serves, and including
        // delta bodies would spend dictionary budget on material the codec never sees.
        var keyframe = new List<byte[]>();
        foreach (var s in all) if (!s.DeltaOnly) keyframe.Add(s.Body);
        _out.WriteLine($"{keyframe.Count} keyframe/full samples, {all.Count - keyframe.Count} delta-only (not used for training)");

        Assert.True(keyframe.Count >= 64,
            $"only {keyframe.Count} keyframe/full samples — capture a longer run, or lower BASIS_BUNDLE_CAPTURE_EVERY");

        var (train, holdout) = Split(keyframe);
        byte[] dict = Train(train, DictionaryCapacity);
        var r = Measure(holdout, dict);

        _out.WriteLine($"dictionary: {dict.Length} bytes from {train.Count} samples");
        _out.WriteLine($"holdout ({holdout.Count} samples): raw {r.Raw}  lz4 {r.Lz4} ({r.Lz4Ratio:F4})  zstd {r.Zstd} ({r.ZstdRatio:F4})");
        _out.WriteLine($"SAVING VS LZ4: {r.SavingVsLz4:P2}");

        if (r.SavingVsLz4 <= 0)
        {
            _out.WriteLine("Dictionary does not beat LZ4 on the holdout — NOT writing the generated file.");
            Assert.Fail($"trained dictionary is not an improvement ({r.SavingVsLz4:P2}); capture more representative traffic before shipping it");
        }

        byte generation = 1;
        string genText = Environment.GetEnvironmentVariable("BASIS_TRAIN_DICT_GENERATION");
        if (!string.IsNullOrWhiteSpace(genText))
        {
            Assert.True(byte.TryParse(genText, out generation) && generation >= 1 && generation <= 31,
                "BASIS_TRAIN_DICT_GENERATION must be 1..31");
        }

        foreach (string path in GeneratedFilePaths())
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes(RenderGeneratedFile(dict, generation, r)));
            _out.WriteLine($"wrote {path}");
        }
        _out.WriteLine("Rebuild the server AND the Unity client — a mismatched generation is refused, not decoded.");
    }

    /// <summary>
    /// Both copies of the generated file. The server source is mirrored between the standalone
    /// solution and the Unity package, and a dictionary that exists in only one of them means the
    /// client refuses every bundle the server sends.
    /// </summary>
    private static IEnumerable<string> GeneratedFilePaths()
    {
        DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Basis Server"))) dir = dir.Parent;
        Assert.NotNull(dir);

        string standalone = Path.Combine(dir.FullName, "Basis Server", "BasisNetworkCore", "Compression", "BasisAvatarBundleDictionary.cs");
        string mirror = Path.Combine(dir.FullName, "Basis", "Packages", "com.basis.server", "BasisNetworkCore", "Compression", "BasisAvatarBundleDictionary.cs");

        Assert.True(File.Exists(standalone), $"missing {standalone}");
        Assert.True(File.Exists(mirror), $"missing {mirror} — the mirror tree needs the file (and a .meta) before training can write to it");
        yield return standalone;
        yield return mirror;
    }

    /// <summary>
    /// Renders BasisAvatarBundleDictionary.cs. CRLF throughout and UTF-8 without a BOM, matching
    /// the rest of the repo — a whole-file line-ending flip would show up as a rewrite in every
    /// diff and silently desynchronise the two trees.
    /// </summary>
    private static string RenderGeneratedFile(byte[] dictionary, byte generation, CodecResult holdout)
    {
        const string NL = "\r\n";
        string b64 = Convert.ToBase64String(dictionary);

        // Wrapped so the generated file stays diffable and no line is pathologically long.
        var chunks = new List<string>();
        const int Width = 120;
        for (int i = 0; i < b64.Length; i += Width) chunks.Add(b64.Substring(i, Math.Min(Width, b64.Length - i)));

        var sb = new StringBuilder();
        sb.Append("using System;").Append(NL).Append(NL);
        sb.Append("namespace Basis.Network.Core.Compression").Append(NL).Append('{').Append(NL);
        sb.Append("    /// <summary>").Append(NL);
        sb.Append("    /// The Zstd dictionary both ends of <see cref=\"BasisAvatarBundleZstd\"/> compress against.").Append(NL);
        sb.Append("    ///").Append(NL);
        sb.Append("    /// \u26a0\ufe0f GENERATED by BundleDictionaryTrainer.TrainFromCaptureFile \u2014 do not hand-edit.").Append(NL);
        sb.Append("    /// Server and client must hold byte-identical content: frames are written with the").Append(NL);
        sb.Append("    /// dictionary id suppressed, so a mismatch is not caught by zstd itself. The generation").Append(NL);
        sb.Append("    /// below travels in the bundle flags byte, and a decoder that does not hold it drops the").Append(NL);
        sb.Append("    /// bundle rather than decoding it against the wrong dictionary.").Append(NL);
        sb.Append("    ///").Append(NL);
        sb.Append($"    /// Trained on {dictionary.Length} bytes. Holdout measurement at training time:").Append(NL);
        sb.Append($"    ///   lz4 ratio {holdout.Lz4Ratio:F4}, zstd ratio {holdout.ZstdRatio:F4}, saving vs lz4 {holdout.SavingVsLz4:P2}.").Append(NL);
        sb.Append("    /// </summary>").Append(NL);
        sb.Append("    public static class BasisAvatarBundleDictionary").Append(NL).Append("    {").Append(NL);
        sb.Append("        /// <summary>Dictionary generation, 1..31. 0 means none is embedded and the Zstd bundle codec is inert.</summary>").Append(NL);
        sb.Append($"        public const byte Generation = {generation};").Append(NL).Append(NL);
        sb.Append("        /// <summary>Base64 of the raw zstd dictionary.</summary>").Append(NL);
        sb.Append("        public const string Base64 =").Append(NL);
        for (int i = 0; i < chunks.Count; i++)
        {
            sb.Append("            \"").Append(chunks[i]).Append('"');
            sb.Append(i == chunks.Count - 1 ? ";" : " +").Append(NL);
        }
        sb.Append(NL);
        sb.Append("        /// <summary>Decoded dictionary bytes; empty when no dictionary is embedded.</summary>").Append(NL);
        sb.Append("        public static readonly byte[] Bytes = Decode();").Append(NL).Append(NL);
        sb.Append("        private static byte[] Decode()").Append(NL).Append("        {").Append(NL);
        sb.Append("            string b64 = Base64;").Append(NL);
        sb.Append("            if (b64.Length == 0) return Array.Empty<byte>();").Append(NL);
        sb.Append("            return Convert.FromBase64String(b64);").Append(NL);
        sb.Append("        }").Append(NL);
        sb.Append("    }").Append(NL);
        sb.Append('}').Append(NL);
        return sb.ToString();
    }
}
