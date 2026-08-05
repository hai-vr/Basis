using System;
using System.Runtime.CompilerServices;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Avatar delta compression against a keyframe baseline. A keyframe is the full fixed-size payload
    /// (<see cref="BasisBoneRotationCompression.ConvertToSize"/>). A delta encodes only the fields that
    /// changed since the last keyframe, preceded by a per-field dirty mask. Reconstructing a full
    /// payload = copy the baseline keyframe, then rewrite the dirty fields. Pure C#; shared by the
    /// server (encode) and client (decode), and used on the uplink in both directions.
    ///
    /// Fields: [0]=position, [1..31]=the rotation region's 31 wire fields (21 explicit bone rotations
    /// then 10 finger curl/splay channels), then scale, body rotation, hips delta, hips rotation and
    /// the end-effector block.
    ///
    /// <para><b>Deltas reference the keyframe, never the previous frame.</b> That is the property the
    /// whole fan-out depends on: the reduction system throttles each receiver to its own distance-derived
    /// send rate, so one sender's frames reach different receivers on different subsampling schedules.
    /// A single delta must therefore reconstruct the full pose on its own, regardless of which
    /// intermediate frames a given receiver was never sent or lost.</para>
    ///
    /// <para><b>Body encoding — per-channel residuals.</b> Each dirty field is decomposed into its
    /// channels (<see cref="BasisAvatarChannelMap"/>) and each quantized channel is sent as the
    /// zig-zag Exponential-Golomb code of its difference from the baseline. Unchanged channels cost one
    /// bit, small motion three to seven. This is what buys per-component granularity: previously a bone
    /// whose single dominant axis moved by one quantization step cost its entire 38-bit field, and
    /// hinge joints — elbows, knees, every finger — move on one axis nearly all the time.</para>
    ///
    /// <para>Residuals are EXACT here, never companded. There is no closed loop against a keyframe
    /// baseline to absorb approximation error, so the reconstruction is bit-identical to the sender's
    /// payload. Exponential-Golomb can exceed a narrow channel's raw width, so every field also carries
    /// a mode bit and the encoder picks whichever encoding is shorter — bounding a field at its own
    /// verbatim size plus one bit, and with it the whole delta.</para>
    ///
    /// Delta body layout:
    ///   [dirtyMask : DirtyMaskBytes][bitstream, LSB-first]
    /// and within the bitstream, for each dirty field in field order:
    ///   [1 bit mode][raw: every channel verbatim | residual: Raw channels verbatim, Delta channels as se(v)]
    /// The body is byte-padded at the end. Unlike the fixed-width scheme this replaces, the body length
    /// is not derivable from the mask alone — <see cref="DeltaBodyLength"/> parses to find it.
    /// </summary>
    public static class BasisAvatarDeltaCompression
    {
        public const int BoneFieldStart = 1;
        public const int FieldCount = 1 + BasisBoneRotationCompression.RotationFieldCount + 5; // 37 (incl. end-effector)
        public const int DirtyMaskBytes = (FieldCount + 7) >> 3;                               // 5

        private const int ModeResidual = 0;
        private const int ModeRaw = 1;

        private sealed class QualityGeometry
        {
            public BasisAvatarChannelLayout Layout;
            public int PayloadSize;
            public int MaxDeltaSize;
        }

        private static readonly QualityGeometry[] Geo = new QualityGeometry[4];

        static BasisAvatarDeltaCompression()
        {
            for (int qi = 0; qi < 4; qi++)
            {
                var q = (BasisAvatarBitPacking.BitQuality)qi;
                var layout = BasisAvatarChannelMap.For(q);
                // Raw mode caps every field at its own verbatim width, so the worst case is one mode
                // bit per field plus the whole payload — five bytes over the old fixed bound.
                int maxBodyBits = FieldCount + layout.TotalChannelBits;
                Geo[qi] = new QualityGeometry
                {
                    Layout = layout,
                    PayloadSize = layout.PayloadBytes,
                    MaxDeltaSize = DirtyMaskBytes + ((maxBodyBits + 7) >> 3),
                };
            }
        }

        public static int PayloadSize(BasisAvatarBitPacking.BitQuality q) => Geo[(int)q].PayloadSize;

        /// <summary>
        /// Worst-case delta body length for a quality. Callers size scratch buffers with this.
        /// </summary>
        public static int MaxDeltaSize(BasisAvatarBitPacking.BitQuality q) => Geo[(int)q].MaxDeltaSize;

        /// <summary>
        /// Builds a delta of <paramref name="current"/> against <paramref name="keyframe"/> into
        /// <paramref name="dst"/> at <paramref name="dstStart"/>. Both payloads must be at least
        /// PayloadSize(q) bytes; dst must have room for MaxDeltaSize(q) from dstStart. Returns the delta
        /// body length written, or -1 on bad input. The caller compares the returned length to
        /// PayloadSize(q) to decide keyframe promotion.
        /// </summary>
        public static int BuildDelta(byte[] keyframe, byte[] current, BasisAvatarBitPacking.BitQuality q, byte[] dst, int dstStart)
        {
            var g = Geo[(int)q];
            if (keyframe == null || current == null || dst == null) return -1;
            if (keyframe.Length < g.PayloadSize || current.Length < g.PayloadSize) return -1;
            if (dstStart < 0 || dst.Length - dstStart < g.MaxDeltaSize) return -1;

            var layout = g.Layout;
            var channels = layout.Channels;

            Span<byte> mask = stackalloc byte[DirtyMaskBytes];
            mask.Clear();

            for (int f = 0; f < FieldCount; f++)
            {
                int start = layout.FieldChannelStart(f), end = layout.FieldChannelEnd(f);
                for (int c = start; c < end; c++)
                {
                    if (ReadChannel(current, channels[c]) != ReadChannel(keyframe, channels[c]))
                    {
                        SetBit(mask, f);
                        break;
                    }
                }
            }

            for (int i = 0; i < DirtyMaskBytes; i++) dst[dstStart + i] = mask[i];

            var w = new BasisResidualCodec.BitWriter(dst, (dstStart + DirtyMaskBytes) * 8);
            int bodyStartBit = w.BitPosition;

            for (int f = 0; f < FieldCount; f++)
            {
                if (!GetBit(mask, f)) continue;
                int start = layout.FieldChannelStart(f), end = layout.FieldChannelEnd(f);

                int residualBits = 0, rawBits = 0;
                for (int c = start; c < end; c++)
                {
                    var ch = channels[c];
                    rawBits += ch.Width;
                    if (ch.Kind == BasisChannelKind.Raw) { residualBits += ch.Width; continue; }
                    int diff = BasisResidualCodec.WrapSigned(
                        (int)ReadChannel(current, ch) - (int)ReadChannel(keyframe, ch), ch.Width);
                    residualBits += BasisResidualCodec.SignedEgBits(diff);
                }

                bool raw = rawBits < residualBits;
                w.WriteBit(raw ? ModeRaw : ModeResidual);

                for (int c = start; c < end; c++)
                {
                    var ch = channels[c];
                    uint cur = ReadChannel(current, ch);
                    if (raw || ch.Kind == BasisChannelKind.Raw)
                    {
                        w.WriteBits(cur, ch.Width);
                        continue;
                    }
                    int diff = BasisResidualCodec.WrapSigned((int)cur - (int)ReadChannel(keyframe, ch), ch.Width);
                    w.WriteSignedEg(diff);
                }
            }

            // Zero the unused bits of the final partial byte: the body is compared and hashed
            // elsewhere, and dst is a reused scratch buffer that is not guaranteed clean.
            int bodyBits = w.BitPosition - bodyStartBit;
            int pad = (8 - (bodyBits & 7)) & 7;
            if (pad > 0) w.WriteBits(0UL, pad);

            return DirtyMaskBytes + ((bodyBits + 7) >> 3);
        }

        /// <summary>
        /// Reconstructs the full payload from <paramref name="baseline"/> (last keyframe) plus the delta
        /// body in <paramref name="delta"/>[deltaStart, deltaStart+deltaLen). Writes PayloadSize(q) bytes
        /// into <paramref name="outFull"/>. Returns false if the delta is malformed/truncated. Never
        /// mutates the baseline.
        /// </summary>
        public static bool TryApplyDelta(byte[] baseline, byte[] delta, int deltaStart, int deltaLen, BasisAvatarBitPacking.BitQuality q, byte[] outFull)
        {
            var g = Geo[(int)q];
            if (baseline == null || baseline.Length < g.PayloadSize) return false;
            if (outFull == null || outFull.Length < g.PayloadSize) return false;
            if (delta == null || deltaLen < DirtyMaskBytes) return false;
            if (deltaStart < 0 || deltaLen < 0 || deltaStart + deltaLen > delta.Length) return false;

            var layout = g.Layout;
            var channels = layout.Channels;
            var mask = new ReadOnlySpan<byte>(delta, deltaStart, DirtyMaskBytes);

            Buffer.BlockCopy(baseline, 0, outFull, 0, g.PayloadSize);

            var r = new BasisResidualCodec.BitReader(delta,
                (deltaStart + DirtyMaskBytes) * 8, (deltaStart + deltaLen) * 8);

            for (int f = 0; f < FieldCount; f++)
            {
                if (!GetBit(mask, f)) continue;
                bool raw = r.ReadBit() == ModeRaw;
                int start = layout.FieldChannelStart(f), end = layout.FieldChannelEnd(f);
                for (int c = start; c < end; c++)
                {
                    var ch = channels[c];
                    if (raw || ch.Kind == BasisChannelKind.Raw)
                    {
                        uint v = (uint)r.ReadBits(ch.Width);
                        if (r.Failed) return false;
                        WriteChannel(outFull, ch, v);
                        continue;
                    }
                    int diff = r.ReadSignedEg();
                    if (r.Failed) return false;
                    WriteChannel(outFull, ch, (uint)((int)ReadChannel(baseline, ch) + diff) & ch.Mask);
                }
            }
            if (r.Failed) return false;

            // The body must occupy exactly the bytes it was given: a length that disagrees with what
            // the mask and codes describe is a corrupt or mis-split frame, not a decodable one.
            int consumed = r.BitPosition - (deltaStart + DirtyMaskBytes) * 8;
            return DirtyMaskBytes + ((consumed + 7) >> 3) == deltaLen;
        }

        /// <summary>
        /// Reads the dirty mask and codes at the start of a delta body and returns the total body length
        /// in bytes (mask + encoded fields), or -1 if the body is truncated or malformed. The receive
        /// path uses this to split a delta frame's body from any trailing additional-data section.
        /// </summary>
        public static int DeltaBodyLength(byte[] delta, int start, int available, BasisAvatarBitPacking.BitQuality q)
        {
            if (delta == null || available < DirtyMaskBytes || start < 0) return -1;
            if (start + DirtyMaskBytes > delta.Length) return -1;

            int limit = Math.Min(available, delta.Length - start);
            var g = Geo[(int)q];
            var layout = g.Layout;
            var channels = layout.Channels;
            var mask = new ReadOnlySpan<byte>(delta, start, DirtyMaskBytes);

            var r = new BasisResidualCodec.BitReader(delta, (start + DirtyMaskBytes) * 8, (start + limit) * 8);

            for (int f = 0; f < FieldCount; f++)
            {
                if (!GetBit(mask, f)) continue;
                bool raw = r.ReadBit() == ModeRaw;
                int cs = layout.FieldChannelStart(f), ce = layout.FieldChannelEnd(f);
                for (int c = cs; c < ce; c++)
                {
                    var ch = channels[c];
                    if (raw || ch.Kind == BasisChannelKind.Raw) r.ReadBits(ch.Width);
                    else r.ReadSignedEg();
                    if (r.Failed) return -1;
                }
            }
            if (r.Failed) return -1;

            int consumed = r.BitPosition - (start + DirtyMaskBytes) * 8;
            return DirtyMaskBytes + ((consumed + 7) >> 3);
        }

        // ────────────────────────────────────────────────────────────
        //  Channel access
        // ────────────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint ReadChannel(byte[] payload, in BasisAvatarChannel ch)
        {
            int bit = ch.BitOffset;
            return (uint)BasisBoneRotationCompression.ReadBits(payload, ref bit, ch.Width);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteChannel(byte[] payload, in BasisAvatarChannel ch, uint value)
        {
            int bitPos = ch.BitOffset, bytePos = bitPos >> 3, inByte = bitPos & 7, left = ch.Width;
            uint v = value;
            while (left > 0)
            {
                int room = 8 - inByte;
                int take = left < room ? left : room;
                int lowMask = (1 << take) - 1;
                int clear = lowMask << inByte;
                byte chunk = (byte)(((int)(v & (uint)lowMask)) << inByte);
                payload[bytePos] = (byte)((payload[bytePos] & ~clear) | chunk);
                v >>= take;
                left -= take;
                bytePos++;
                inByte = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetBit(Span<byte> mask, int field) => mask[field >> 3] |= (byte)(1 << (field & 7));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool GetBit(ReadOnlySpan<byte> mask, int field) => (mask[field >> 3] & (1 << (field & 7))) != 0;
    }
}
