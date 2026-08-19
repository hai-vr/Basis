using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// One implementation of "read/write N bits at bit offset P in a byte[]", LSB-first, for every
    /// bitstream in the avatar pipeline.
    ///
    /// <para>The codecs each grew their own copy of the same byte-at-a-time loop — walk one byte,
    /// take the bits that fit, shift, repeat. That loop costs one iteration per byte SPANNED rather
    /// than one per field, so a 12-bit component straddling a byte boundary pays two or three
    /// iterations with a dependent shift chain through each. Every field in the payload is at most
    /// 24 bits wide, so all of them fit inside a single unaligned 64-bit load regardless of where
    /// they start: the whole loop collapses to load / shift / mask.</para>
    ///
    /// <para>This is deliberately NOT vectorised. A bitfield extract is one instruction's worth of
    /// work once it is expressed as a word operation, and the surrounding code consumes the result
    /// scalar-ly; the win here is not doing eight at a time, it is not doing three iterations to do
    /// one. <see cref="BasisPayloadDiff"/> is where the vector width earns its keep.</para>
    ///
    /// <para><b>⚠️ Reads go wide; writes deliberately do not.</b> The same trick applied to writes
    /// measured SLOWER — 3.67 ns/field against the byte loop's 2.57 — and the reason generalises:
    /// a write is a read-modify-write, encoders emit fields in increasing bit order, so each wide
    /// access reloads bytes the previous one has just stored. The overlap is partial, which is the
    /// case store-to-load forwarding cannot satisfy, and the stall costs more than the iterations it
    /// saved. Byte-at-a-time stores overlap exactly or not at all and forward cleanly. Narrower
    /// windows were measured too (4-byte 3.70 ns, 2-byte 2.95) and none beat the byte loop, so
    /// <see cref="Or"/> and <see cref="Replace"/> keep it. Reads have no such hazard — nothing was
    /// just stored there — and take the single load, which is where the 1.8x comes from.
    /// <c>SimdCodecBenchmark</c> re-measures both directions; a write path that "improves" past the
    /// byte loop here means the access pattern changed and should be re-derived, not assumed.</para>
    ///
    /// <para><b>The narrow read path is not dead code.</b> A field close enough to the end of the
    /// buffer that a 64-bit load would run past it still has to be served, and payload buffers are
    /// exact-sized (and often rented, so reading past the logical end would read another tenant's
    /// bytes). <c>BasisBitCodecTests</c> pins every path against an independent bit-at-a-time oracle
    /// across every offset and width the layouts can produce.</para>
    /// </summary>
    public static class BasisBitCodec
    {
        /// <summary>
        /// Widest field the single-load path can serve. A 64-bit load starting at the containing byte
        /// covers bits [0, 64) from that byte, and the field can begin up to 7 bits into it.
        /// </summary>
        public const int MaxWideBits = 57;

        /// <summary>Bytes a wide access reads or writes; the buffer must have this many left.</summary>
        private const int WordBytes = 8;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong LoadWord(byte[] buffer, int bytePos)
        {
            ulong word = Unsafe.ReadUnaligned<ulong>(ref buffer[bytePos]);
            // JIT-time constant, so exactly one of these survives codegen.
            return BitConverter.IsLittleEndian ? word : BinaryPrimitives.ReverseEndianness(word);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong LowMask(int bitCount) => bitCount >= 64 ? ulong.MaxValue : (1UL << bitCount) - 1UL;

        /// <summary>
        /// Reads <paramref name="bitCount"/> bits starting at <paramref name="bitPos"/>. Bits above
        /// the count are zero in the result.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Read(byte[] src, int bitPos, int bitCount)
        {
            int bytePos = bitPos >> 3;
            int bitInByte = bitPos & 7;

            if (bitCount <= MaxWideBits && bytePos + WordBytes <= src.Length)
            {
                return (LoadWord(src, bytePos) >> bitInByte) & LowMask(bitCount);
            }
            return ReadNarrow(src, bytePos, bitInByte, bitCount);
        }

        /// <summary>
        /// ORs <paramref name="value"/>'s low <paramref name="bitCount"/> bits into the buffer. The
        /// destination range must already be zero — this is the "write into a cleared region" form the
        /// bone/repack encoders use, and it is kept OR-only so a caller cannot silently start relying
        /// on it to overwrite. Use <see cref="Replace"/> when the destination may be dirty.
        ///
        /// <para>Byte-at-a-time on purpose; see the store-forwarding note on the type.</para>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Or(byte[] dst, int bitPos, ulong value, int bitCount)
        {
            OrNarrow(dst, bitPos >> 3, bitPos & 7, value, bitCount);
        }

        /// <summary>
        /// Overwrites the bit range with <paramref name="value"/>'s low <paramref name="bitCount"/>
        /// bits, clearing whatever was there. Bits outside the range are untouched.
        ///
        /// <para>Byte-at-a-time on purpose; see the store-forwarding note on the type.</para>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Replace(byte[] dst, int bitPos, ulong value, int bitCount)
        {
            ReplaceNarrow(dst, bitPos >> 3, bitPos & 7, value, bitCount);
        }

        // ────────────────────────────────────────────────────────────
        //  Byte-at-a-time paths. The write side uses these exclusively
        //  (measured faster); the read side falls back to one for fields
        //  too close to the end of the buffer to load a whole word over.
        // ────────────────────────────────────────────────────────────

        private static ulong ReadNarrow(byte[] src, int bytePos, int bitInByte, int bitCount)
        {
            ulong result = 0;
            int outShift = 0;
            int bitsLeft = bitCount;

            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;
                ulong chunk = ((ulong)src[bytePos] >> bitInByte) & ((1UL << take) - 1UL);
                result |= chunk << outShift;
                outShift += take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }
            return result;
        }

        private static void OrNarrow(byte[] dst, int bytePos, int bitInByte, ulong value, int bitCount)
        {
            int bitsLeft = bitCount;
            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;
                byte chunk = (byte)(value & ((1UL << take) - 1UL));
                dst[bytePos] = (byte)(dst[bytePos] | (chunk << bitInByte));
                value >>= take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }
        }

        private static void ReplaceNarrow(byte[] dst, int bytePos, int bitInByte, ulong value, int bitCount)
        {
            int bitsLeft = bitCount;
            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;
                int lowMask = (1 << take) - 1;
                int clear = lowMask << bitInByte;
                byte chunk = (byte)(((int)(value & (uint)lowMask)) << bitInByte);
                dst[bytePos] = (byte)((dst[bytePos] & ~clear) | chunk);
                value >>= take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }
        }
    }
}
