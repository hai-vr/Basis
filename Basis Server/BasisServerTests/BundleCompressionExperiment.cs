using System.Diagnostics;
using Basis.Network.Core;
using Basis.Network.Core.Compression;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using K4os.Compression.LZ4;
using Xunit;
using Xunit.Abstractions;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;

namespace BasisServerTests;

/// <summary>
/// Where the compressed-avatar-bundle win actually comes from, and what it costs.
///
/// BasisServerReductionSystemEvents.TryDeflateAndEmit LZ4s a chunk of
/// <c>[origChannel:1][len:2-LE][bytes]</c> entries per receiver per tick. The comment on
/// AvatarBundleMaxRatio records the production figures: ratio ~0.87, and ~19 ms of CPU per 4 ms
/// tick at 1000 players (~4.8 of 32 cores). This re-packs identical information four ways and
/// compresses each, to find out where the 13% actually comes from:
///
///   Current    [ch][len:2][body]           per entry — today's wire format
///   NoLen      [ch][body]                  len is implied by ch for fixed-size quality channels
///   Grouped    sorted by ch, [ch][n] once  then n bodies back to back
///   Transposed as Grouped, but each group's bodies are column-transposed
///
/// ── What it found (2026-08-06, net10.0, 32-core desktop) ──────────────────────────────────────
///
/// 1. THE ENCODER IS NOT THE BOTTLENECK. Single-threaded K4os L00_FAST runs 478-1291 MB/s here,
///    not the ~225 MB/s/core that falls out of dividing the production figures. So most of that
///    19 ms is NOT the LZ4 call — it is BuildRawForRange's memcpy, chunk selection, retries and
///    the send itself. Swapping in native liblz4 would buy far less than the raw port-overhead
///    argument suggests; measure the surrounding code before touching the codec.
///
/// 2. LZ4'S VALUE IS WILDLY UNEVEN, and the ~0.87 average hides it:
///      keyframe + resting crowd   saves 20.8%   ← where essentially all of the win lives
///      delta    + resting crowd   saves  3.7%
///      keyframe + everyone moving costs  0.5%   (expansion; AvatarBundleMaxRatio catches it)
///      delta    + everyone moving costs  0.5%
///    Since v42 put steady state on DeltaAvatarChannel, the common case is the 3.7% row.
///
/// 3. GROUPING BY CHANNEL IS FREE MONEY. Sorting entries by channel and emitting one [ch][count]
///    header per run, instead of repeating [ch] on every entry, is -13.9% wire bytes on the delta
///    path and -1.1% on keyframes, at identical or better throughput. It removes bytes LZ4 was
///    otherwise being paid to rediscover.
///
/// 4. TRANSPOSITION IS BIMODAL — DO NOT APPLY IT BLINDLY. Column-transposing a group is a large
///    win on deltas (short, field-aligned, correlated across players) but a LOSS on keyframes
///    (+0.9% measured), because idle players emit near-identical whole payloads and transposing
///    shatters exactly the long matches LZ4 was living on. Delta group only.
///
/// 5. DERIVING BODY LENGTHS FROM THE CHANNEL IS NOT WORTH IT. Omitting the length wherever the
///    channel implies it (Group+dT+der below) is worth 1.6pp on keyframes and EXACTLY ZERO on
///    deltas — delta bodies are genuinely variable and keep their 2-byte length either way, so
///    every byte of the -13.9% is already captured without it. The price would be a decoder that
///    reproduces the keyframe serializer's exact geometry, including the per-entry
///    [size][messageIndex] framing v43 added to AdditionalAvatarData, where a one-byte
///    disagreement desyncs every bundle. The safe variant keeps [len:2] on every entry and gives
///    up ~1.5pp on the path that is not the steady state. That is the trade this took.
///
/// ── Corpus fidelity ───────────────────────────────────────────────────────────────────────────
///
/// The "idle" keyframe case lands at 0.828 against production's ~0.87, which is what makes the
/// rest of the table worth reading. Getting there required modelling the redundancy structurally
/// (see Pose): a first cut built on DeltaTestSupport.MakeRealisticPayload measured 1.005 — pure
/// literal-run overhead, no matches at all — because that builder fills position and the tail
/// with random bytes, and random bytes are not what a crowd standing in a room looks like.
///
/// Run with <c>--logger "console;verbosity=detailed"</c> to see the table. Nothing is asserted
/// beyond self-consistency: this is a measurement, not a regression gate.
/// </summary>
public class BundleCompressionExperiment
{
    private readonly ITestOutputHelper _out;
    public BundleCompressionExperiment(ITestOutputHelper o) => _out = o;

    /// <summary>Raw bytes per chunk. ~MTU / the 0.85 initial ratio guess, i.e. what PickChunkEnd targets.</summary>
    const int TargetRawBytes = 1400;
    const int Chunks = 400;
    const int TimedPasses = 60;
    const int TimedReps = 5;

    /// <summary>Distance ladder as a receiver actually sees it: a few near players, a long VeryLow tail.</summary>
    static readonly BitQuality[] QualityMix =
    {
        BitQuality.VeryLow, BitQuality.VeryLow, BitQuality.VeryLow, BitQuality.VeryLow,
        BitQuality.Low, BitQuality.Low, BitQuality.Low,
        BitQuality.Medium, BitQuality.Medium,
        BitQuality.High,
    };

    /// <summary>One pending avatar message: the bytes that would go out on <see cref="Channel"/> alone.</summary>
    readonly struct Entry
    {
        public readonly byte Channel;
        public readonly byte[] Body;   // [id:1|2][interval:1][payload][additional?]
        public readonly int AddlSize;  // 0 when this channel carries no AdditionalAvatarData
        public Entry(byte channel, byte[] body, int addlSize)
        {
            Channel = channel; Body = body; AddlSize = addlSize;
        }
    }

    /// <summary>
    /// How much length information a channel actually has to put on the wire. This is the whole
    /// point of the exercise, so it is modelled exactly rather than assumed away:
    ///
    ///   Derived  even quality channel — id size, interval and payload size all follow from the
    ///            channel number, so the body needs no length at all.
    ///   AddlByte odd quality channel — same, plus AdditionalAvatarData. Its size is a BYTE on the
    ///            message (AdditionalAvatarDataSize), so one byte covers it, not two.
    ///   Len16    DeltaAvatarChannel — genuinely variable, keeps a 2-byte length.
    /// </summary>
    enum LenClass { Derived, AddlByte, Len16 }

    // Note the parity of "odd offset" flips between the two ranges (byte-id base 6, large base 41),
    // so this goes through the existing helper rather than testing channel & 1.
    static LenClass ClassOf(byte channel) =>
        channel == BasisNetworkCommons.DeltaAvatarChannel ? LenClass.Len16
        : BasisNetworkCommons.ChannelHasAdditionalData(channel) ? LenClass.AddlByte
        : LenClass.Derived;

    /// <summary>Writes whatever length bytes <paramref name="e"/>'s channel class requires.</summary>
    static void WriteLen(List<byte> o, in Entry e)
    {
        switch (ClassOf(e.Channel))
        {
            case LenClass.Derived: break;
            case LenClass.AddlByte: o.Add((byte)e.AddlSize); break;
            default: o.Add((byte)e.Body.Length); o.Add((byte)(e.Body.Length >> 8)); break;
        }
    }

    // ── payload sources ───────────────────────────────────────────────────────────────────────

    static void WriteInt24(byte[] a, int off, int v)
    {
        a[off] = (byte)v; a[off + 1] = (byte)(v >> 8); a[off + 2] = (byte)(v >> 16);
    }

    /// <summary>
    /// idle   — realistic: a crowd sharing one room, most of them at rest.
    /// moving — ceiling: every player mid-motion with no shared structure, i.e. the existing
    ///          DeltaTestSupport builder, whose position/tail are literally random bytes.
    ///
    /// The realistic case is not "random with jitter". The redundancy the production 0.87 comes
    /// from is structural: everyone is in the same room so the int24-millimetre position shares
    /// its high bytes; everyone is standing so the low-bits-per-component slots at VeryLow/Low
    /// quantize to the SAME value across players; and avatar scale comes from a small set. Those
    /// are the matches LZ4 finds. Random bytes destroy all three, which is why the first cut of
    /// this experiment measured 1.005 (pure literal-run overhead, no matches at all).
    /// </summary>
    static byte[] Pose(BitQuality q, string kind, Random rng, int player)
    {
        if (kind == "moving") return DeltaTestSupport.MakeRealisticPayload(q, rng);

        var arr = new byte[DeltaTestSupport.PayloadSize(q)];
        // Restlessness: most of a crowd is standing still, a minority is actively moving.
        double activity = rng.NextDouble() < 0.7 ? 0.01 : 0.35;

        // Position: int24 mm inside a ~30 m room on a flat floor. Top byte is constant across
        // the crowd, middle byte takes a few dozen values.
        WriteInt24(arr, 0, (int)((rng.NextDouble() * 2 - 1) * 15000));
        WriteInt24(arr, 3, (int)(rng.NextDouble() * 150));
        WriteInt24(arr, 6, (int)((rng.NextDouble() * 2 - 1) * 15000));

        // Bones: a canonical rest pose per slot, perturbed by the player's activity. At low
        // bits-per-component an idle perturbation quantizes away entirely — which is exactly
        // why the far-distance VeryLow tail of a bundle compresses and the near High players do not.
        var bpc = DeltaTestSupport.Bpc(q);
        var offs = DeltaTestSupport.BoneBitOffsets(q);
        for (int slot = 0; slot < DeltaTestSupport.WireBoneSlots; slot++)
        {
            var rest = new Random(4000 + slot);
            var (rx, ry, rz, rw) = DeltaTestSupport.RandomQuat(rest);
            float x = rx + (float)((rng.NextDouble() * 2 - 1) * activity);
            float y = ry + (float)((rng.NextDouble() * 2 - 1) * activity);
            float z = rz + (float)((rng.NextDouble() * 2 - 1) * activity);
            float w = rw + (float)((rng.NextDouble() * 2 - 1) * activity);
            float len = MathF.Sqrt(x * x + y * y + z * z + w * w);
            if (len < 1e-6f) { x = 0; y = 0; z = 0; w = 1; len = 1; }
            ulong packed = BasisBoneRotationCompression.EncodeSmallestThree(
                x / len, y / len, z / len, w / len, bpc[slot], BasisBoneRotationCompression.MAX_COMPONENT[slot]);
            BasisBoneRotationCompression.WriteBits(arr, DeltaTestSupport.BoneBaseBit(q) + offs[slot], packed, 2 + 3 * bpc[slot]);
        }

        // Fingers: relaxed curl, so the quantized pairs repeat heavily across a resting crowd.
        int curlBits = BasisBoneRotationCompression.CurlBits(q);
        int splayBits = BasisBoneRotationCompression.SplayBits(q);
        for (int f = 0; f < BasisBoneRotationCompression.FingerChannelCount; f++)
        {
            uint curl = BasisBoneRotationCompression.EncodeSignedUnit((float)(-0.4 + rng.NextDouble() * activity), curlBits);
            uint splay = BasisBoneRotationCompression.EncodeSignedUnit((float)(rng.NextDouble() * activity - activity / 2), splayBits);
            int b = DeltaTestSupport.BoneBaseBit(q) + offs[DeltaTestSupport.WireBoneSlots + f];
            BasisBoneRotationCompression.WriteBits(arr, b, curl, curlBits);
            BasisBoneRotationCompression.WriteBits(arr, b + curlBits, splay, splayBits);
        }

        // Tail. Scale comes from a handful of common avatar heights; hips delta is ~zero for a
        // standing player; body/hips rotation is upright with a yaw the player picked on spawn.
        int scaleOff = DeltaTestSupport.ScaleOffset(q);
        ushort scale = (ushort)(60000 + (player % 6) * 400);
        arr[scaleOff] = (byte)scale; arr[scaleOff + 1] = (byte)(scale >> 8);

        float yaw = (float)(rng.Next(16) / 16.0 * Math.PI * 2);
        ulong upright = BasisBoneRotationCompression.EncodeSmallestThree(
            0f, MathF.Sin(yaw / 2), 0f, MathF.Cos(yaw / 2), 9, 1f);
        for (int i = 0; i < 7; i++)
        {
            arr[DeltaTestSupport.BodyRotOffset(q) + i] = (byte)(upright >> (i * 8));
            arr[DeltaTestSupport.HipsRotOffset(q) + i] = (byte)(upright >> (i * 8));
        }
        // HipsDelta left at zero — a standing player's hips sit at TPose, which is the whole
        // reason the field is two's complement (see BasisAvatarBitPacking.WriteHipsDelta).

        if (DeltaTestSupport.EndEffectorBytes(q) > 0)
        {
            // Hands rest near the hips: same structure as the body, not random bytes.
            int off = DeltaTestSupport.EndEffectorOffset(q);
            for (int i = 0; i < DeltaTestSupport.EndEffectorBytes(q); i++)
                arr[off + i] = (byte)(upright >> ((i % 7) * 8));
        }
        return arr;
    }

    /// <summary>
    /// Builds one chunk's worth of entries, filling to <see cref="TargetRawBytes"/> the way
    /// PickChunkEnd does. <paramref name="delta"/> selects the steady-state case (real deltas
    /// against a keyframe on DeltaAvatarChannel) over the keyframe case.
    /// </summary>
    static List<Entry> BuildChunk(string kind, bool delta, Random rng, ref int player)
    {
        var entries = new List<Entry>();
        int raw = 0;
        while (raw < TargetRawBytes)
        {
            BitQuality q = QualityMix[rng.Next(QualityMix.Length)];
            bool largeId = rng.Next(4) == 0;             // >255 player ids exist but are the minority
            int idBytes = largeId ? 2 : 1;
            player++;

            // AdditionalAvatarData (face blendshapes, behaviour params) rides the odd channels.
            // StripAdditionalDataAtLowQuality drops it below Medium, so only the near tiers carry it.
            bool hasAddl = q >= BitQuality.Medium && rng.NextDouble() < 0.35;
            int addlSize = hasAddl ? 12 + rng.Next(24) : 0;

            byte[] body;
            byte channel;
            if (delta)
            {
                byte[] kf = Pose(q, kind, new Random(player), player);
                byte[] cur = (byte[])kf.Clone();
                // Steady state: a handful of fields moved since the keyframe.
                var widths = DeltaTestSupport.RotationFieldWidths(q);
                int moved = kind == "moving" ? 10 : 2;
                for (int k = 0; k < moved; k++) DeltaTestSupport.FlipBone(cur, q, rng.Next(widths.Length));

                var dst = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(q)];
                int len = BasisAvatarDeltaCompression.BuildDelta(kf, cur, q, dst, 0);
                if (len <= 0) continue;

                channel = BasisNetworkCommons.DeltaAvatarChannel;
                body = new byte[idBytes + 1 + len];
                Array.Copy(dst, 0, body, idBytes + 1, len);
            }
            else
            {
                byte[] payload = Pose(q, kind, rng, player);
                channel = largeId
                    ? BasisNetworkCommons.GetPlayerAvatarLargeChannelForQuality((int)q, hasAddl)
                    : BasisNetworkCommons.GetPlayerAvatarChannelForQuality((int)q, hasAddl);
                body = new byte[idBytes + 1 + payload.Length + addlSize];
                Array.Copy(payload, 0, body, idBytes + 1, payload.Length);

                // Blendshape weights: a resting face sits near neutral, so these repeat across the
                // crowd the same way rest poses do. A moving face is unstructured.
                for (int k = 0; k < addlSize; k++)
                    body[idBytes + 1 + payload.Length + k] =
                        kind == "moving" ? (byte)rng.Next(256) : (byte)(rng.Next(4) == 0 ? rng.Next(256) : 0);
            }

            // [id][interval] prefix, as the send loop writes it.
            body[0] = (byte)player;
            if (idBytes == 2) body[1] = (byte)(player >> 8);
            body[idBytes] = (byte)rng.Next(256);

            entries.Add(new Entry(channel, body, delta ? 0 : addlSize));
            raw += 3 + body.Length;
        }
        return entries;
    }

    // ── packings ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Today's wire format: [ch][len:2-LE][body] per entry.</summary>
    static byte[] PackCurrent(List<Entry> es)
    {
        var o = new List<byte>(TargetRawBytes + 64);
        foreach (var e in es)
        {
            o.Add(e.Channel);
            o.Add((byte)e.Body.Length);
            o.Add((byte)(e.Body.Length >> 8));
            o.AddRange(e.Body);
        }
        return o.ToArray();
    }

    /// <summary>Per-entry channel kept, but only the length bytes the channel actually needs.</summary>
    static byte[] PackNoLen(List<Entry> es)
    {
        var o = new List<byte>(TargetRawBytes + 64);
        foreach (var e in es)
        {
            o.Add(e.Channel);
            WriteLen(o, e);
            o.AddRange(e.Body);
        }
        return o.ToArray();
    }

    /// <summary>
    /// Sorted by channel, one [ch][count] header per run, then that run's lengths and bodies.
    /// <paramref name="transposeDelta"/> column-transposes the DeltaAvatarChannel group only —
    /// transposing the fixed-size quality groups measurably HURTS, because idle players emit
    /// near-identical whole payloads there.
    ///
    /// <paramref name="deriveLen"/> selects whether lengths are omitted where the channel implies
    /// them. That is the expensive half of the idea: it makes the decoder depend on reproducing
    /// the keyframe serializer's exact byte geometry (id width + interval + payload size + the
    /// per-entry [size][messageIndex] framing v43 added to AdditionalAvatarData), and a
    /// one-byte disagreement desyncs every bundle. Measure what it is actually worth before
    /// taking that on.
    /// </summary>
    static byte[] PackGrouped(List<Entry> es, bool transposeDelta, bool deriveLen)
    {
        var o = new List<byte>(TargetRawBytes + 64);
        foreach (var g in es.GroupBy(e => e.Channel).OrderBy(g => g.Key))
        {
            var items = g.ToList();
            o.Add(g.Key);
            o.Add((byte)items.Count);
            foreach (var e in items)
            {
                if (deriveLen) WriteLen(o, e);
                else { o.Add((byte)e.Body.Length); o.Add((byte)(e.Body.Length >> 8)); }
            }

            if (!(transposeDelta && g.Key == BasisNetworkCommons.DeltaAvatarChannel))
            {
                foreach (var e in items) o.AddRange(e.Body);
                continue;
            }

            // Column-transpose: byte j of every body, then byte j+1 of every body, ...
            // Puts the same field across players adjacent, which is where the correlation is.
            int maxLen = items.Max(e => e.Body.Length);
            for (int j = 0; j < maxLen; j++)
                foreach (var e in items)
                    if (j < e.Body.Length) o.Add(e.Body[j]);
        }
        return o.ToArray();
    }

    // ── measurement ───────────────────────────────────────────────────────────────────────────

    sealed class Stat
    {
        public long Raw, Comp;
        public double EncodeMs;
        public double Ratio => Raw > 0 ? (double)Comp / Raw : 0;
        /// <summary>Encode throughput in MB/s of INPUT, single-threaded — the figure to compare to ~225.</summary>
        public double MBps => EncodeMs > 0 ? Raw / (EncodeMs / 1000.0) / (1024 * 1024) : 0;
    }

    /// <summary>
    /// One-time global JIT warm-up. Without it the very first variant measured pays for tiering up
    /// every LZ4 code path and reads ~78 MB/s against ~540 for the identical corpus one row later —
    /// which looks exactly like a real finding and is not one.
    /// </summary>
    static bool _warm;
    static void WarmUpOnce()
    {
        if (_warm) return;
        var rng = new Random(1);
        var src = new byte[TargetRawBytes];
        var dst = new byte[LZ4Codec.MaximumOutputSize(src.Length)];
        for (int i = 0; i < 20000; i++)
        {
            // Alternate compressible and incompressible input so both branches tier up.
            if ((i & 1) == 0) rng.NextBytes(src); else Array.Fill(src, (byte)i);
            LZ4Codec.Encode(src, dst, LZ4Level.L00_FAST);
        }
        _warm = true;
    }

    static Stat Measure(List<byte[]> chunks)
    {
        WarmUpOnce();
        var s = new Stat();
        int maxRaw = chunks.Max(c => c.Length);
        var dst = new byte[LZ4Codec.MaximumOutputSize(maxRaw)];

        // Settle this corpus in cache before timing.
        for (int w = 0; w < 3; w++)
            foreach (var c in chunks) LZ4Codec.Encode(c, dst, LZ4Level.L00_FAST);

        foreach (var c in chunks)
        {
            s.Raw += c.Length;
            s.Comp += LZ4Codec.Encode(c, dst, LZ4Level.L00_FAST);
        }

        // Corpus construction allocates hundreds of arrays per variant, so without settling the
        // heap first a gen2 collection lands inside whichever variant is measured first and shows
        // up as a 4x throughput difference on byte-identical input. Take the min over several
        // reps as well — it is the rep least disturbed by whatever else the machine is doing.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        double best = double.MaxValue;
        for (int rep = 0; rep < TimedReps; rep++)
        {
            var sw = Stopwatch.StartNew();
            for (int p = 0; p < TimedPasses; p++)
                foreach (var c in chunks) LZ4Codec.Encode(c, dst, LZ4Level.L00_FAST);
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / TimedPasses);
        }
        s.EncodeMs = best;
        return s;
    }

    [Fact]
    public void BundlePackingAndCompressionEconomics()
    {
        _out.WriteLine($"chunks={Chunks} targetRaw={TargetRawBytes}B timedPasses={TimedPasses}");
        _out.WriteLine($"server-config: InitialBundleRatioGuess=0.85 AvatarBundleMaxRatio={BasisServerReductionSystemEvents.AvatarBundleMaxRatio}");
        _out.WriteLine("");

        foreach (bool delta in new[] { false, true })
        foreach (string kind in new[] { "idle", "moving" })
        {
            var rng = new Random(20260806);
            int player = 0;
            var raw = new List<List<Entry>>();
            for (int i = 0; i < Chunks; i++) raw.Add(BuildChunk(kind, delta, rng, ref player));

            int entries = raw.Sum(c => c.Count);
            _out.WriteLine($"── {(delta ? "delta" : "keyframe")} / {kind} — {entries} entries over {Chunks} chunks ({entries / (double)Chunks:F1} per bundle)");
            _out.WriteLine($"{"packing",-14}{"raw B",10}{"wire B",10}{"ratio",8}{"vs current",12}{"MB/s",10}");

            var variants = new (string name, Func<List<Entry>, byte[]> pack)[]
            {
                ("Current",      es => PackCurrent(es)),
                ("NoLen",        es => PackNoLen(es)),
                // Group + transpose only. Keeps [len:2] on every entry, so the decoder never has
                // to reproduce the serializer's geometry. This is the cheap, safe half.
                ("Group+dT",     es => PackGrouped(es, true,  deriveLen: false)),
                // ...and the same thing with derived lengths, to price the risky half.
                ("Group+dT+der", es => PackGrouped(es, true,  deriveLen: true)),
            };

            long currentWire = 0;
            long uncompressedCurrent = 0;
            foreach (var (name, pack) in variants)
            {
                var chunks = raw.Select(pack).ToList();
                var s = Measure(chunks);
                if (name == "Current") { currentWire = s.Comp; uncompressedCurrent = s.Raw; }
                double vs = currentWire > 0 ? (double)s.Comp / currentWire - 1.0 : 0;
                _out.WriteLine($"{name,-14}{s.Raw,10}{s.Comp,10}{s.Ratio,8:F3}{vs,11:P1}{s.MBps,10:F0}");
            }

            // The alternative to compressing at all: coalesce the entries into one datagram and
            // send them uncompressed. Zero encode cost, and the reference the 13% is worth beating.
            _out.WriteLine($"{"(no LZ4)",-14}{uncompressedCurrent,10}{uncompressedCurrent,10}{1.0,8:F3}" +
                           $"{(currentWire > 0 ? (double)uncompressedCurrent / currentWire - 1.0 : 0),11:P1}{"∞",10}");
            _out.WriteLine("");
        }
    }
}
