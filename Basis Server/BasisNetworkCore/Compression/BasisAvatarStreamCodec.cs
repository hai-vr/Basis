using System;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Per-stream reconstruction state: one estimate per channel of the payload. The sender keeps one
    /// modelling what a lossless receiver holds; each receiver keeps its own.
    /// </summary>
    public sealed class BasisAvatarStreamState
    {
        public readonly BasisAvatarBitPacking.BitQuality Quality;
        public readonly uint[] Estimate;
        internal readonly BasisAvatarChannelLayout Layout;

        /// <summary>False until a keyframe has seeded the estimates; frames are meaningless before that.</summary>
        public bool HasBaseline;

        public BasisAvatarStreamState(BasisAvatarBitPacking.BitQuality q)
        {
            Quality = q;
            Layout = BasisAvatarChannelMap.For(q);
            Estimate = new uint[Layout.Channels.Length];
        }

        /// <summary>Adopts a full payload as the current estimate. Both ends do this on a keyframe.</summary>
        public void SeedFrom(byte[] payload)
        {
            var ch = Layout.Channels;
            for (int i = 0; i < ch.Length; i++)
                Estimate[i] = BasisAvatarDeltaCompression.ReadChannel(payload, ch[i]);
            HasBaseline = true;
        }

        /// <summary>Materialises the current estimate as a full payload.</summary>
        public void WriteTo(byte[] outFull)
        {
            var ch = Layout.Channels;
            for (int i = 0; i < ch.Length; i++)
                BasisAvatarDeltaCompression.WriteChannel(outFull, ch[i], Estimate[i]);
        }

        public void Reset()
        {
            Array.Clear(Estimate, 0, Estimate.Length);
            HasBaseline = false;
        }
    }

    /// <summary>
    /// Continuous-stream avatar codec: closed-loop predictive coding plus a Gray-code bit-plane sweep.
    /// Used where there is exactly ONE stream from a sender that every receiver sees in full — the
    /// client uplink and the P2P direct path. It is NOT usable on the server's downlink fan-out, and
    /// that is a property of the transport rather than of this code: the reduction system throttles
    /// each receiver to its own distance-derived send rate, so a given receiver sees an arbitrary
    /// subsequence of a sender's frames. A predictive chain cannot be decimated — every frame depends
    /// on the reconstruction the previous one produced — so the downlink keeps
    /// <see cref="BasisAvatarDeltaCompression"/>, whose deltas are absolute against a shared keyframe.
    ///
    /// <para><b>Two independent mechanisms per frame.</b></para>
    ///
    /// <para><b>1. The residual.</b> Every channel is coded against <em>what the sender believes the
    /// receiver currently holds</em>, not against the sender's previous value. The sender runs the
    /// decoder's arithmetic itself and feeds the result back, so its model is exactly the payload a
    /// lossless receiver reconstructs — which is what lets the next frame's residual be measured
    /// against something real rather than against the sender's own history.</para>
    ///
    /// <para><b>2. The sweep.</b> Each frame also carries one bit per channel: bit
    /// <see cref="BasisResidualCodec.SweepBitIndex"/> of the Gray code of the channel's TRUE value.
    /// The receiver overwrites that one bit of its own estimate's Gray code. Over one pass — as many
    /// frames as the channel is wide — the receiver has been told every bit of the true value, so it
    /// converges exactly regardless of what it missed. It is a keyframe smeared thinly across time
    /// instead of delivered as a burst, and it costs less: about 19 bytes per frame at High against a
    /// 177-byte keyframe every half second.</para>
    ///
    /// <para>The sweep is why a lost frame needs no acknowledgement, no retransmission and no keyframe
    /// request. Loss makes the receiver's estimate differ from the sender's model by a constant
    /// offset, which the residual loop preserves exactly (both sides add the same decoded value); the
    /// sweep then drives that offset to zero one bit at a time.</para>
    ///
    /// <para><b>Gray coding is what makes a single bit safe to send.</b> Adjacent integers differ in
    /// exactly one Gray bit, so correcting one bit of a nearly-right estimate moves it by a bounded
    /// amount. In plain binary the same operation is catastrophic — one wrong high bit across a carry
    /// boundary is half the range. Categorical channels (the smallest-three "largest component"
    /// indices, the effector mask, padding) are swept as plain binary instead: their values are labels
    /// rather than magnitudes, so Gray adjacency means nothing, but bit-patching still resyncs them
    /// within their width.</para>
    ///
    /// Frame layout — one LSB-first bitstream, byte-padded at the end:
    ///   [1 bit masked?][if masked: FieldCount bits of dirty mask]
    ///   for each coded field: [1 bit mode] then
    ///        residual mode -> Delta: se(exact residual);  Raw: [1 bit changed][value if changed]
    ///        raw mode      -> every channel verbatim
    ///   sweep: one bit per channel, over ALL channels in channel order.
    ///
    /// A field is dirty when any of its channels differs from the estimate. Carrying a mask makes an
    /// agreed-upon field free instead of one bit per channel, which matters enormously when a player
    /// is nearly still; but during real motion almost every field is dirty and the mask is dead weight.
    /// Since a clean channel costs exactly one bit either way, the encoder can compare the two exactly
    /// rather than guess, and spends one bit saying which it chose.
    ///
    /// <para><b>Residuals are EXACT — nothing is approximated.</b> An earlier revision companded them
    /// above a small linear zone, on the theory that the closed loop would absorb the error. It does
    /// not absorb it fast enough to be invisible, and worse, it interacts catastrophically with the
    /// smallest-three encoding: the 2-bit index selects WHICH quaternion component was dropped, so
    /// when it flips, the other three change meaning entirely and a residual measured across that flip
    /// is measured against a stale mapping. Measured, that produced up to a <b>180° single-frame error
    /// on every index flip</b> — a bone pointing backwards for one frame, at rotation rates as low as
    /// 30°/s — which reads as snapping, and as jitter when it is smaller. The raw mode below is what
    /// bounds it: a field whose residuals are large or whose mapping just changed costs more as
    /// residuals than verbatim, so the encoder sends it verbatim and the reconstruction is exact.</para>
    ///
    /// <para>Because every field is now carried exactly, the sender's model equals what a lossless
    /// receiver holds <em>bit for bit</em>, so the sweep is a no-op whenever nothing was lost. It has
    /// gone from something that perturbs a perpetually-approximate estimate to a pure loss-recovery
    /// mechanism, which is all it was ever meant to be.</para>
    ///
    /// The sweep covers every channel unconditionally, masked or not: a field can be clean from the
    /// SENDER's point of view and still be wrong on a receiver that missed the frame which set it, and
    /// repairing exactly that is what the sweep is for.
    /// </summary>
    public static class BasisAvatarStreamCodec
    {
        private const int ModeUnmasked = 0;
        private const int ModeMasked = 1;
        private const int FieldResidual = 0;
        private const int FieldRaw = 1;

        private static readonly int[] MaxFrameBytes = new int[4];

        static BasisAvatarStreamCodec()
        {
            for (int qi = 0; qi < 4; qi++)
            {
                var layout = BasisAvatarChannelMap.For((BasisAvatarBitPacking.BitQuality)qi);
                // Raw mode caps every field at its own verbatim width, so the worst case is the mask,
                // one mode bit per field, the payload itself, and the sweep.
                int bits = 1
                         + BasisAvatarDeltaCompression.FieldCount            // dirty mask
                         + BasisAvatarDeltaCompression.FieldCount            // per-field mode bits
                         + layout.TotalChannelBits                           // every field verbatim
                         + layout.Channels.Length;                           // sweep
                MaxFrameBytes[qi] = (bits + 7) >> 3;
            }
        }

        /// <summary>
        /// Worst-case frame size. Larger than the payload — a frame in which every channel jumps the
        /// full range costs more than sending the pose outright — so callers size scratch buffers with
        /// this and promote to a keyframe when a frame comes out no smaller than the payload.
        /// </summary>
        public static int MaxFrameSize(BasisAvatarBitPacking.BitQuality q) => MaxFrameBytes[(int)q];

        /// <summary>How many consecutive received frames a full sweep pass takes.</summary>
        public static int SweepPeriod(BasisAvatarBitPacking.BitQuality q) => BasisAvatarChannelMap.For(q).MaxChannelWidth;

        /// <summary>
        /// Encodes <paramref name="current"/> against <paramref name="state"/> and advances the state to
        /// what a receiver that gets this frame will hold. Returns bytes written, or -1 on bad input.
        /// The caller must have seeded the state from a keyframe first.
        /// </summary>
        public static int Encode(BasisAvatarStreamState state, byte[] current, byte sequence, byte[] dst, int dstStart)
        {
            if (state == null || current == null || dst == null) return -1;
            var layout = state.Layout;
            if (current.Length < layout.PayloadBytes) return -1;
            int max = MaxFrameBytes[(int)state.Quality];
            if (dstStart < 0 || dst.Length - dstStart < max) return -1;

            var channels = layout.Channels;
            var est = state.Estimate;
            int fieldCount = BasisAvatarDeltaCompression.FieldCount;

            // Dirty scan, and with it the exact cost of carrying a mask: every channel of a clean
            // field would cost exactly one bit unmasked (an Exp-Golomb zero, or an unchanged flag),
            // and the mask itself costs exactly FieldCount bits.
            Span<bool> dirty = stackalloc bool[fieldCount];
            int cleanChannelBits = 0;
            for (int f = 0; f < fieldCount; f++)
            {
                int cs = layout.FieldChannelStart(f), ce = layout.FieldChannelEnd(f);
                bool d = false;
                for (int c = cs; c < ce; c++)
                {
                    if (BasisAvatarDeltaCompression.ReadChannel(current, channels[c]) != est[c]) { d = true; break; }
                }
                dirty[f] = d;
                if (!d) cleanChannelBits += ce - cs;
            }
            bool masked = cleanChannelBits > fieldCount;

            var w = new BasisResidualCodec.BitWriter(dst, dstStart * 8);
            int startBit = w.BitPosition;
            w.WriteBit(masked ? ModeMasked : ModeUnmasked);
            if (masked)
                for (int f = 0; f < fieldCount; f++) w.WriteBit(dirty[f] ? 1 : 0);

            // Pass 1 — per field, whichever of exact-residual or verbatim is shorter. Both are exact,
            // so this is purely a size choice; it is also what makes a smallest-three index flip safe,
            // since the huge residuals a flip produces always lose to verbatim.
            for (int f = 0; f < fieldCount; f++)
            {
                if (masked && !dirty[f]) continue;
                int cs = layout.FieldChannelStart(f), ce = layout.FieldChannelEnd(f);

                int residualBits = 0, rawBits = 0;
                for (int i = cs; i < ce; i++)
                {
                    var ch = channels[i];
                    rawBits += ch.Width;
                    uint cur = BasisAvatarDeltaCompression.ReadChannel(current, ch);
                    if (ch.Kind == BasisChannelKind.Raw)
                    {
                        residualBits += cur == est[i] ? 1 : 1 + ch.Width;
                        continue;
                    }
                    residualBits += BasisResidualCodec.SignedEgBits(
                        BasisResidualCodec.WrapSigned((int)cur - (int)est[i], ch.Width));
                }

                bool raw = rawBits < residualBits;
                w.WriteBit(raw ? FieldRaw : FieldResidual);

                for (int i = cs; i < ce; i++)
                {
                    var ch = channels[i];
                    uint cur = BasisAvatarDeltaCompression.ReadChannel(current, ch);
                    if (raw)
                    {
                        w.WriteBits(cur, ch.Width);
                        est[i] = cur;
                        continue;
                    }
                    if (ch.Kind == BasisChannelKind.Raw)
                    {
                        if (cur == est[i]) { w.WriteBit(0); continue; }
                        w.WriteBit(1);
                        w.WriteBits(cur, ch.Width);
                        est[i] = cur;
                        continue;
                    }
                    w.WriteSignedEg(BasisResidualCodec.WrapSigned((int)cur - (int)est[i], ch.Width));
                    est[i] = cur;   // exact: (est + (cur - est)) & mask == cur
                }
            }

            // Pass 2 — the sweep. The sender applies the same patch a lossless receiver will, so its
            // model stays exactly what that receiver holds and the next frame's residuals are honest.
            for (int i = 0; i < channels.Length; i++)
            {
                var ch = channels[i];
                uint cur = BasisAvatarDeltaCompression.ReadChannel(current, ch);
                int idx = BasisResidualCodec.SweepBitIndex(sequence, ch.Width);
                uint bit = SweepBitOf(cur, ch.Kind, idx);
                w.WriteBit((int)bit);
                est[i] = ApplySweepBit(est[i], ch, idx, bit);
            }

            int bodyBits = w.BitPosition - startBit;
            int pad = (8 - (bodyBits & 7)) & 7;
            if (pad > 0) w.WriteBits(0UL, pad);
            return (bodyBits + 7) >> 3;
        }

        /// <summary>
        /// Applies one encoded frame to <paramref name="state"/> and materialises the result into
        /// <paramref name="outFull"/>. Returns false if the frame is truncated or malformed, in which
        /// case the state is left untouched.
        /// </summary>
        public static bool Decode(BasisAvatarStreamState state, byte[] src, int start, int len, byte sequence, byte[] outFull)
        {
            if (state == null || src == null || outFull == null) return false;
            var layout = state.Layout;
            if (outFull.Length < layout.PayloadBytes) return false;
            if (start < 0 || len <= 0 || start + len > src.Length) return false;

            var channels = layout.Channels;
            int fieldCount = BasisAvatarDeltaCompression.FieldCount;
            // Decode into scratch first: a frame that turns out to be truncated must not leave the
            // state half-advanced, or every subsequent frame decodes against a corrupt reference.
            uint[] next = RentScratch(channels.Length);
            Array.Copy(state.Estimate, next, channels.Length);

            var r = new BasisResidualCodec.BitReader(src, start * 8, (start + len) * 8);
            bool masked = r.ReadBit() == ModeMasked;
            Span<bool> dirty = stackalloc bool[fieldCount];
            for (int f = 0; f < fieldCount; f++) dirty[f] = masked ? r.ReadBit() != 0 : true;
            if (r.Failed) return false;

            for (int f = 0; f < fieldCount; f++)
            {
                if (!dirty[f]) continue;
                bool raw = r.ReadBit() == FieldRaw;
                if (r.Failed) return false;
                for (int i = layout.FieldChannelStart(f); i < layout.FieldChannelEnd(f); i++)
                {
                    var ch = channels[i];
                    if (raw)
                    {
                        next[i] = (uint)r.ReadBits(ch.Width) & ch.Mask;
                        if (r.Failed) return false;
                        continue;
                    }
                    if (ch.Kind == BasisChannelKind.Raw)
                    {
                        if (r.ReadBit() != 0) next[i] = (uint)r.ReadBits(ch.Width) & ch.Mask;
                        if (r.Failed) return false;
                        continue;
                    }
                    int residual = r.ReadSignedEg();
                    if (r.Failed) return false;
                    next[i] = (uint)((int)next[i] + residual) & ch.Mask;
                }
            }

            for (int i = 0; i < channels.Length; i++)
            {
                var ch = channels[i];
                uint bit = (uint)r.ReadBit();
                if (r.Failed) return false;
                next[i] = ApplySweepBit(next[i], ch, BasisResidualCodec.SweepBitIndex(sequence, ch.Width), bit);
            }
            if (r.Failed) return false;

            int consumed = r.BitPosition - start * 8;
            if (((consumed + 7) >> 3) != len) return false;

            Array.Copy(next, state.Estimate, channels.Length);
            state.HasBaseline = true;
            state.WriteTo(outFull);
            return true;
        }

        /// <summary>
        /// Parses a frame far enough to find its length in bytes, or -1 if truncated/malformed. The
        /// receive path uses this to split the frame from any trailing additional-data section.
        /// </summary>
        public static int FrameLength(byte[] src, int start, int available, BasisAvatarBitPacking.BitQuality q)
        {
            if (src == null || start < 0 || available <= 0 || start >= src.Length) return -1;

            var layout = BasisAvatarChannelMap.For(q);
            var channels = layout.Channels;
            int fieldCount = BasisAvatarDeltaCompression.FieldCount;
            int limit = Math.Min(available, src.Length - start);
            var r = new BasisResidualCodec.BitReader(src, start * 8, (start + limit) * 8);

            bool masked = r.ReadBit() == ModeMasked;
            Span<bool> dirty = stackalloc bool[fieldCount];
            for (int f = 0; f < fieldCount; f++) dirty[f] = masked ? r.ReadBit() != 0 : true;
            if (r.Failed) return -1;

            for (int f = 0; f < fieldCount; f++)
            {
                if (!dirty[f]) continue;
                bool raw = r.ReadBit() == FieldRaw;
                if (r.Failed) return -1;
                for (int i = layout.FieldChannelStart(f); i < layout.FieldChannelEnd(f); i++)
                {
                    var ch = channels[i];
                    if (raw) r.ReadBits(ch.Width);
                    else if (ch.Kind == BasisChannelKind.Raw) { if (r.ReadBit() != 0) r.ReadBits(ch.Width); }
                    else r.ReadSignedEg();
                    if (r.Failed) return -1;
                }
            }
            for (int i = 0; i < channels.Length; i++) { r.ReadBit(); if (r.Failed) return -1; }
            if (r.Failed) return -1;

            return (r.BitPosition - start * 8 + 7) >> 3;
        }

        private static uint SweepBitOf(uint value, BasisChannelKind kind, int idx)
        {
            uint coded = kind == BasisChannelKind.Raw ? value : BasisResidualCodec.ToGray(value);
            return (coded >> idx) & 1u;
        }

        private static uint ApplySweepBit(uint estimate, in BasisAvatarChannel ch, int idx, uint bit)
        {
            uint mask = ch.Mask;
            if (ch.Kind == BasisChannelKind.Raw)
                return ((estimate & ~(1u << idx)) | (bit << idx)) & mask;

            uint g = BasisResidualCodec.ToGray(estimate);
            g = (g & ~(1u << idx)) | (bit << idx);
            return BasisResidualCodec.FromGray(g) & mask;
        }

        [ThreadStatic] private static uint[] _scratch;
        private static uint[] RentScratch(int n)
        {
            var s = _scratch;
            if (s == null || s.Length < n) { s = new uint[n < 256 ? 256 : n]; _scratch = s; }
            return s;
        }
    }
}
