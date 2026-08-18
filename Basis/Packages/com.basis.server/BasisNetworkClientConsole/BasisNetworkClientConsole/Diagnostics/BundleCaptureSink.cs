using Basis.Network.Core.Compression;
using System;
using System.IO;
using System.Threading;

namespace Basis.Network
{
    /// <summary>
    /// Harvests real avatar-bundle bodies off the wire into a capture file, which
    /// BundleDictionaryTrainer (in BasisServerTests) turns into the Zstd dictionary embedded in
    /// <see cref="BasisAvatarBundleDictionary"/>.
    ///
    /// ── Why capture here and not in the server ───────────────────────────────────────────────
    ///
    /// MessageHandler.SniffBundle already holds the decompressed grouped body, and that body is
    /// byte-for-byte the buffer the server compressed. So the load-tester can produce training
    /// material with no hook in the server's send path at all — the path where a per-bundle
    /// branch would be paid ~1000 times per tick at 1000 players, forever, to support something
    /// that runs a handful of times in the project's life.
    ///
    /// It also samples the right distribution by construction: what a *receiver* actually gets,
    /// after distance tiering, slicing and the delta/keyframe split, rather than what the packer
    /// happened to assemble.
    ///
    /// ── Sampling ─────────────────────────────────────────────────────────────────────────────
    ///
    /// Capture is decimated (<c>everyNth</c>) rather than "first N bundles". A join burst is all
    /// keyframes at the cold VeryLow tier and looks nothing like steady state; a dictionary
    /// trained on the first few thousand bundles of a run would be tuned for the least
    /// interesting minute of it. Decimating spreads the sample across the whole run.
    ///
    /// Both traffic classes are recorded and labelled. The trainer only feeds keyframe/full
    /// samples to the dictionary builder, but keeping the delta ones makes the capture usable
    /// for re-checking the traffic-class finding itself.
    /// </summary>
    public static class BundleCaptureSink
    {
        /// <summary>File magic + format version. Bump the digit if the record layout changes.</summary>
        private static readonly byte[] Magic = System.Text.Encoding.ASCII.GetBytes("BSNDCAP1");

        /// <summary>Record flag: this body contained only DeltaAvatarChannel groups.</summary>
        public const byte FlagDeltaOnly = 1;

        private static readonly object _gate = new object();
        private static FileStream _file;
        private static int _everyNth = 1;
        private static long _seen;
        private static int _maxSamples;
        private static int _written;
        private static int _writtenDelta;
        private static long _rawBytes;
        private static long _wireBytes;

        /// <summary>True once <see cref="Configure"/> has opened a capture file.</summary>
        public static bool Enabled { get; private set; }

        /// <summary>
        /// Opens <paramref name="path"/> for capture. <paramref name="maxSamples"/> bounds the
        /// file so an overnight run cannot fill the disk; <paramref name="everyNth"/> decimates.
        /// </summary>
        public static void Configure(string path, int maxSamples, int everyNth)
        {
            lock (_gate)
            {
                if (_file != null) return;
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
                _file.Write(Magic, 0, Magic.Length);
                _maxSamples = Math.Max(1, maxSamples);
                _everyNth = Math.Max(1, everyNth);
                Enabled = true;
            }
        }

        /// <summary>
        /// Records one decoded bundle body. <paramref name="compressedLen"/> and
        /// <paramref name="codec"/> are stored only in the summary — the file holds raw bodies,
        /// so a capture taken with one codec can train a dictionary used by another.
        /// </summary>
        public static void Capture(byte[] grouped, int length, int compressedLen, byte codec)
        {
            if (!Enabled || length <= 0) return;

            // Decimate before taking the lock so the common case is one interlocked increment.
            if (Interlocked.Increment(ref _seen) % _everyNth != 0) return;

            if (!BasisAvatarBundleCodec.TryClassify(grouped.AsSpan(0, length), out bool deltaOnly)) return;

            lock (_gate)
            {
                if (_file == null || _written >= _maxSamples) return;

                Span<byte> header = stackalloc byte[3];
                header[0] = deltaOnly ? FlagDeltaOnly : (byte)0;
                header[1] = (byte)(length & 0xFF);
                header[2] = (byte)((length >> 8) & 0xFF);
                _file.Write(header);
                _file.Write(grouped, 0, length);

                _written++;
                if (deltaOnly) _writtenDelta++;
                _rawBytes += length;
                _wireBytes += compressedLen;
            }
        }

        /// <summary>Closes the capture file and returns a one-line summary, or null if capture was off.</summary>
        public static string Finish()
        {
            lock (_gate)
            {
                if (_file == null) return null;
                _file.Flush();
                _file.Dispose();
                _file = null;
                Enabled = false;

                int keyframe = _written - _writtenDelta;
                double ratio = _rawBytes > 0 ? (double)_wireBytes / _rawBytes : 0;
                return $"[BundleCapture] {_written} samples ({keyframe} keyframe/full, {_writtenDelta} delta-only) " +
                       $"from {Interlocked.Read(ref _seen)} bundles, {_rawBytes} raw bytes, observed wire ratio {ratio:F4}.";
            }
        }
    }
}
