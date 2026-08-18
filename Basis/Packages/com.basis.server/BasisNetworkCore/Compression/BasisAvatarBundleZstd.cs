using System;
using System.Collections.Concurrent;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Zstd half of the hybrid avatar-bundle codec, plus the flags byte that tells the two
    /// halves apart on the wire.
    ///
    /// ── Why hybrid ───────────────────────────────────────────────────────────────────────────
    ///
    /// Bundle payloads split into two populations that want different codecs, measured on a
    /// 250-client synthetic workload:
    ///
    ///   keyframe / full bundles   Zstd -2 with a 16 KiB trained dictionary   -16.7% to -18.1%
    ///   delta-only bundles        Zstd -2 loses to LZ4                       +2.8% to +4.5%
    ///
    /// A delta bundle is a few hundred bytes of already-residual-coded, column-transposed data —
    /// there is almost no literal redundancy left for Zstd's entropy stage to pay for itself on,
    /// so its larger frame overhead and slower encode buy nothing. Keyframes are the opposite:
    /// near-identical whole payloads across a resting crowd, which is exactly what a trained
    /// dictionary turns into short references. So the server routes each bundle by traffic
    /// class (see BasisServerReductionSystemEvents.ChunkIsDeltaOnly) rather than trying one
    /// codec everywhere.
    ///
    /// ── The dictionary is load-bearing ───────────────────────────────────────────────────────
    ///
    /// Dictionary-less Zstd at -2 measured WORSE than LZ4 on this data (1296 B vs 1236 B on a
    /// 1400 B synthetic bundle). Essentially the whole win comes from the dictionary, not from
    /// Zstd being a stronger algorithm at this level. So when no dictionary is embedded
    /// (<see cref="BasisAvatarBundleDictionary.Generation"/> == 0) this codec reports
    /// <see cref="Available"/> = false and the server stays on LZ4 for everything. Shipping
    /// Zstd without a dictionary would be a regression, not a partial win.
    ///
    /// ── Frame shape ──────────────────────────────────────────────────────────────────────────
    ///
    /// Frames are written magicless with no content size, no checksum and no dictionary id:
    /// 5 bytes of frame header removed per bundle, which at a ~600 B compressed bundle is
    /// ~0.8% — a real fraction of the total win. All four are recoverable from context: the
    /// bundle header already carries rawLen, the transport already checksums, and the
    /// dictionary generation is in the flags byte. The cost is that a captured payload is not
    /// readable by the stock `zstd` CLI without re-prepending the magic; see
    /// <see cref="MagiclessFormat"/>.
    /// </summary>
    public static class BasisAvatarBundleZstd
    {
        // ── Flags byte (bundle header byte 0) ────────────────────────────────────────────────
        //
        // Byte 0 used to be a message count that every decoder documented as "ignored, rawLen is
        // authoritative" and none actually read. Repurposing it costs zero wire bytes; the
        // count is still available to diagnostics by walking the decoded groups.
        //
        //   bits 0..2   codec id
        //   bits 3..7   dictionary generation the payload was compressed against (0 = none)

        /// <summary>LZ4 block, K4os <c>L00_FAST</c>. The v50 codec; still used for delta-only bundles.</summary>
        public const byte CodecLz4 = 0;

        /// <summary>Zstd magicless raw block against the embedded dictionary of the generation in the flags byte.</summary>
        public const byte CodecZstdDict = 1;

        private const int CodecBits = 3;
        private const byte CodecMask = (1 << CodecBits) - 1;
        private const byte MaxDictGeneration = 0x1F;

        /// <summary>Packs a codec id and dictionary generation into the bundle's flags byte.</summary>
        public static byte PackFlags(byte codec, byte dictGeneration)
            => (byte)((codec & CodecMask) | ((dictGeneration & MaxDictGeneration) << CodecBits));

        /// <summary>Codec id carried by a bundle flags byte.</summary>
        public static byte CodecOf(byte flags) => (byte)(flags & CodecMask);

        /// <summary>Dictionary generation carried by a bundle flags byte; 0 when the codec uses no dictionary.</summary>
        public static byte DictGenerationOf(byte flags) => (byte)((flags >> CodecBits) & MaxDictGeneration);

        // ── Frame parameters ─────────────────────────────────────────────────────────────────

        // zstd maps these two onto experimental parameter slots:
        //   #define ZSTD_c_format ZSTD_c_experimentalParam2
        //   #define ZSTD_d_format ZSTD_d_experimentalParam1
        // ZstdSharp exposes the slots but not the aliases, so they are re-established here.
        private const ZSTD_cParameter ZSTD_c_format = (ZSTD_cParameter)ZSTD_cParameter.ZSTD_c_experimentalParam2;
        private const ZSTD_dParameter ZSTD_d_format = (ZSTD_dParameter)ZSTD_dParameter.ZSTD_d_experimentalParam1;

        /// <summary>ZSTD_f_zstd1_magicless — drops the 4-byte frame magic.</summary>
        private const int MagiclessFormat = 1;

        /// <summary>
        /// 128 KiB ceiling on the window descriptor the frame header declares, which is what a
        /// receiving context sizes its buffers from — worth having on Android/Quest clients.
        /// Comfortably covers the 16 KiB dictionary plus the largest raw bundle the packer can
        /// build (~28 KiB at the smallest permitted compression-ratio estimate).
        ///
        /// A ceiling and nothing else: zstd shrinks the window to fit the source once it knows
        /// the size, so a real bundle declares far less than this — a 1.4 KiB body measures at
        /// windowLog 11 (2 KiB). Neutral on ratio and throughput, so do not re-tune it expecting
        /// a CPU win.
        /// </summary>
        private const int WindowLog = 17;

        /// <summary>Compression level the benchmark settled on. -3 costs less CPU but gives up ~2pp of ratio.</summary>
        public const int DefaultLevel = -2;

        /// <summary>Lowest level zstd accepts. Constructing a Compressor outside [Min, Max] throws.</summary>
        public static int MinLevel => Compressor.MinCompressionLevel;

        /// <summary>Highest level zstd accepts.</summary>
        public static int MaxLevel => Compressor.MaxCompressionLevel;

        // ── Codec availability ───────────────────────────────────────────────────────────────

        private static byte[] _dictionary = BasisAvatarBundleDictionary.Bytes;
        private static byte _dictionaryGeneration = BasisAvatarBundleDictionary.Generation;

        /// <summary>Dictionary generation both ends compress and decompress against; 0 when none is embedded.</summary>
        public static byte DictionaryGeneration => System.Threading.Volatile.Read(ref _dictionaryGeneration);

        /// <summary>
        /// True when a dictionary is embedded and this codec is worth using. False leaves the
        /// server on LZ4 for every bundle — see the class remarks for why dictionary-less Zstd
        /// is not a partial win.
        /// </summary>
        public static bool Available => DictionaryGeneration != 0 && _dictionary.Length != 0;

        /// <summary>
        /// Test seam (InternalsVisibleTo): swaps in a dictionary without rebuilding the generated
        /// file, so tests and the trainer's evaluation pass exercise the real codec — magicless
        /// framing, pooled contexts, flags byte and all — rather than a second copy of the
        /// parameter setup that could drift from it. Pass <c>generation: 0</c> for the
        /// "no dictionary" state.
        ///
        /// This is process-global. Pair every call with <see cref="RestoreEmbeddedDictionaryForTests"/>
        /// in a finally — blanking it instead leaves the codec inert for everything that runs
        /// afterwards in the same process, which reads as "the server chose LZ4" rather than as a
        /// failure.
        /// </summary>
        internal static void OverrideDictionaryForTests(byte[] dictionary, byte generation)
        {
            System.Threading.Volatile.Write(ref _dictionary, dictionary ?? Array.Empty<byte>());
            System.Threading.Volatile.Write(ref _dictionaryGeneration, generation);
            // Pooled contexts hold the previous dictionary digested inside them.
            System.Threading.Interlocked.Increment(ref _epoch);
        }

        /// <summary>Puts the generated dictionary back after <see cref="OverrideDictionaryForTests"/>.</summary>
        internal static void RestoreEmbeddedDictionaryForTests()
            => OverrideDictionaryForTests(BasisAvatarBundleDictionary.Bytes, BasisAvatarBundleDictionary.Generation);

        // ── Context pooling ──────────────────────────────────────────────────────────────────
        //
        // Compressor/Decompressor each wrap a zstd context that is expensive to build and NOT
        // thread-safe. The server's send phase runs across a parallel pool whose worker set
        // churns as load oscillates, so [ThreadStatic] would strand one context per thread the
        // process ever saw. A bag instead bounds live contexts by peak concurrency and lets a
        // retired thread's context be reused rather than leaked.
        //
        // Epoch invalidates pooled contexts when the configured level changes at runtime; a
        // stale context is disposed on rent rather than reset, which is rare enough not to matter.

        /// <summary>
        /// A Compressor carrying this codec's frame parameters and no dictionary. Callers that
        /// need to encode outside the pooled path — the benchmark measuring a build that has no
        /// dictionary yet — go through this rather than repeating the parameter setup, so a
        /// measurement cannot silently be of different framing than the server emits.
        /// </summary>
        public static Compressor CreateCompressor(int level)
        {
            var c = new Compressor(level);
            c.SetParameter(ZSTD_cParameter.ZSTD_c_contentSizeFlag, 0);
            c.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 0);
            c.SetParameter(ZSTD_cParameter.ZSTD_c_dictIDFlag, 0);
            c.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, WindowLog);
            c.SetParameter(ZSTD_c_format, MagiclessFormat);
            return c;
        }

        /// <summary>Decompressor matching <see cref="CreateCompressor"/>'s framing, no dictionary.</summary>
        public static Decompressor CreateDecompressor()
        {
            var d = new Decompressor();
            d.SetParameter(ZSTD_d_format, MagiclessFormat);
            return d;
        }

        private sealed class Pooled<T> where T : IDisposable
        {
            public T Value;
            public int Epoch;
        }

        private static readonly ConcurrentBag<Pooled<Compressor>> _compressors = new ConcurrentBag<Pooled<Compressor>>();
        private static readonly ConcurrentBag<Pooled<Decompressor>> _decompressors = new ConcurrentBag<Pooled<Decompressor>>();
        private static int _level = DefaultLevel;
        private static int _epoch;

        /// <summary>
        /// Sets the compression level used by subsequent compressions. Pooled contexts built at
        /// the previous level are discarded as they are rented. No-op when unchanged.
        /// </summary>
        public static void SetLevel(int level)
        {
            if (System.Threading.Volatile.Read(ref _level) == level) return;
            System.Threading.Volatile.Write(ref _level, level);
            System.Threading.Interlocked.Increment(ref _epoch);
        }

        /// <summary>Level currently in effect.</summary>
        public static int Level => System.Threading.Volatile.Read(ref _level);

        private static Pooled<Compressor> RentCompressor()
        {
            int epoch = System.Threading.Volatile.Read(ref _epoch);
            while (_compressors.TryTake(out var pooled))
            {
                if (pooled.Epoch == epoch) return pooled;
                pooled.Value.Dispose();
            }

            var c = CreateCompressor(System.Threading.Volatile.Read(ref _level));
            // Digested once per context and retained across compressions: ZSTD_compress2 resets
            // the session but keeps a dictionary loaded through ZSTD_CCtx_loadDictionary.
            c.LoadDictionary(System.Threading.Volatile.Read(ref _dictionary));
            return new Pooled<Compressor> { Value = c, Epoch = epoch };
        }

        private static Pooled<Decompressor> RentDecompressor()
        {
            int epoch = System.Threading.Volatile.Read(ref _epoch);
            while (_decompressors.TryTake(out var pooled))
            {
                if (pooled.Epoch == epoch) return pooled;
                pooled.Value.Dispose();
            }

            var d = CreateDecompressor();
            d.LoadDictionary(System.Threading.Volatile.Read(ref _dictionary));
            return new Pooled<Decompressor> { Value = d, Epoch = epoch };
        }

        // ── Codec ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Worst-case compressed size for <paramref name="rawLen"/> bytes. Zstd can expand
        /// incompressible input, so callers sizing a scratch buffer must use this rather than
        /// assuming compression shrinks.
        /// </summary>
        public static int MaximumOutputSize(int rawLen) => Compressor.GetCompressBound(rawLen);

        /// <summary>
        /// Compresses <paramref name="raw"/> into <paramref name="dst"/>. Returns false when the
        /// result would not fit, which is the same "overshoot, retry with a smaller chunk"
        /// signal the LZ4 path gives the bundle packer.
        /// </summary>
        public static bool TryCompress(ReadOnlySpan<byte> raw, Span<byte> dst, out int written)
        {
            written = 0;
            if (!Available) return false;

            var pooled = RentCompressor();
            try
            {
                return pooled.Value.TryWrap(raw, dst, out written);
            }
            catch (Exception)
            {
                // A throwing context is not safely reusable — drop it rather than pool it, and
                // report failure so the caller falls back to an uncompressed send.
                pooled.Value.Dispose();
                pooled = null;
                written = 0;
                return false;
            }
            finally
            {
                if (pooled != null) _compressors.Add(pooled);
            }
        }

        /// <summary>
        /// Decompresses one bundle payload. Returns false on any malformed input — this runs on
        /// network-supplied bytes, so a corrupt or hostile payload must drop the datagram rather
        /// than throw out of the receive path.
        /// </summary>
        public static bool TryDecompress(ReadOnlySpan<byte> src, Span<byte> dst, out int written)
        {
            written = 0;
            if (!Available) return false;

            var pooled = RentDecompressor();
            try
            {
                return pooled.Value.TryUnwrap(src, dst, out written);
            }
            catch (Exception)
            {
                pooled.Value.Dispose();
                pooled = null;
                written = 0;
                return false;
            }
            finally
            {
                if (pooled != null) _decompressors.Add(pooled);
            }
        }
    }
}
