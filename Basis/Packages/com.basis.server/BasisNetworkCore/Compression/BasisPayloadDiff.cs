using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Finds which 8-byte words of an avatar payload differ from its keyframe, as a bitmap, so the
    /// delta encoder can rule fields out wholesale instead of unpacking every channel of every one.
    ///
    /// <para><b>Why this is the shape of the answer.</b> The dirty-mask pass used to decide each of
    /// the 37 fields by unpacking all of its channels out of BOTH payloads and comparing them —
    /// roughly 400 bitfield extracts per payload per quality, every one of which exists only to
    /// discover that nothing moved. Delta compression is premised on most fields being unchanged, so
    /// nearly all of that work is spent proving a negative. A byte that is equal in both payloads
    /// cannot contain a channel that changed, so a plain memory comparison answers the same question
    /// for every field at once, and only the words that actually differ need unpacking.</para>
    ///
    /// <para><b>The result is a conservative superset, and that is what keeps the output identical.</b>
    /// Fields are bit-packed, so one word can hold parts of several fields and a differing word does
    /// not prove any particular field changed. The caller therefore uses this only to SKIP — a field
    /// whose words are all clean is provably clean, because every bit it owns lies inside them — and
    /// runs the original exact per-channel comparison on whatever survives. The emitted delta is
    /// byte-for-byte what the scalar path produced; only the work done to reach it changes.</para>
    ///
    /// <para><b>Where the vector width earns its keep.</b> This is a pure bulk memory compare, which
    /// is exactly what <see cref="Vector{T}"/> is good at: one <see cref="Vector.EqualsAll{T}"/> clears
    /// 16, 32 or 64 bytes at a time on whatever the host actually has, and a still player clears the
    /// whole payload in three or four of them. Deliberately portable rather than hand-written AVX2 —
    /// the two measured identical on this codebase's own distance sweep, and portable covers ARM
    /// servers for free.</para>
    /// </summary>
    public static class BasisPayloadDiff
    {
        /// <summary>
        /// Largest payload this can describe: one bit per 8-byte word in a single <see cref="ulong"/>.
        /// Avatar payloads run ~90-175 bytes, so this is headroom rather than a constraint, but the
        /// layout builder checks it rather than trusting that to stay true.
        /// </summary>
        public const int MaxPayloadBytes = 64 * 8;

        /// <summary>
        /// Returns a bitmap whose bit <c>w</c> is set when bytes <c>[8w, 8w+8)</c> of the two buffers
        /// differ over the first <paramref name="length"/> bytes. Both buffers must have at least
        /// <paramref name="length"/> bytes; <paramref name="length"/> must not exceed
        /// <see cref="MaxPayloadBytes"/>.
        /// </summary>
        public static ulong WordDiffMask(byte[] current, byte[] baseline, int length)
        {
            ulong mask = 0;
            int i = 0;

            // Bulk scan. The point of the vector pass is the SKIP: a block that matches costs one
            // compare and one branch for all of its words, which is the common case by a wide margin.
            if (Vector.IsHardwareAccelerated && length >= Vector<byte>.Count)
            {
                int step = Vector<byte>.Count;
                for (; i + step <= length; i += step)
                {
                    if (Vector.EqualsAll(new Vector<byte>(current, i), new Vector<byte>(baseline, i)))
                    {
                        continue;
                    }
                    for (int p = i; p < i + step; p += 8) mask |= WordBit(current, baseline, p);
                }
            }

            for (; i + 8 <= length; i += 8) mask |= WordBit(current, baseline, i);
            if (i < length) mask |= TailBit(current, baseline, i, length);
            return mask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong WordBit(byte[] current, byte[] baseline, int bytePos)
        {
            ulong a = Unsafe.ReadUnaligned<ulong>(ref current[bytePos]);
            ulong b = Unsafe.ReadUnaligned<ulong>(ref baseline[bytePos]);
            // Endianness is irrelevant: this only ever asks whether the eight bytes are equal.
            return a == b ? 0UL : 1UL << (bytePos >> 3);
        }

        private static ulong TailBit(byte[] current, byte[] baseline, int bytePos, int length)
        {
            for (int k = bytePos; k < length; k++)
            {
                if (current[k] != baseline[k]) return 1UL << (bytePos >> 3);
            }
            return 0;
        }
    }
}
