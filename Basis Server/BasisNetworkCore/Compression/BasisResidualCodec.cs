using System;
using System.Runtime.CompilerServices;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Bit-level primitives used by the avatar delta codec: a bounds-checked LSB-first bit cursor and
    /// zig-zag Exponential-Golomb.
    ///
    /// <para><b>Exp-Golomb</b> spends bits in proportion to magnitude — 1 bit for zero, 3 for ±1, 5 for
    /// ±2..3, and two more per octave after that. A pose field that did not move therefore costs a
    /// single bit instead of its full width, which is what makes per-component granularity affordable:
    /// the old codec had to spend a whole bone (38 bits at High) when one of its three components
    /// moved by one step.</para>
    ///
    /// <para><b>Residuals are never approximated.</b> A previous revision companded them above a small
    /// linear zone to cap the code length on fast motion. It was removed: the approximation produced
    /// up to a <b>180° single-frame bone error whenever a smallest-three index flipped</b>, and the
    /// codec already caps code length correctly by falling back to a verbatim field when residual
    /// coding would be longer. Exactness costs about a byte a frame and removes a whole class of
    /// artefact.</para>
    /// </summary>
    public static class BasisResidualCodec
    {
        /// <summary>Widest channel the codec addresses (the int24 position axes).</summary>
        public const int MaxWidth = 24;

        /// <summary>
        /// Reduces a difference modulo 2^width into the signed range [-2^(width-1), 2^(width-1)).
        /// Reconstruction masks back to the width, so this always round-trips exactly — and picking
        /// the short way round the ring keeps the code small when a signed field crosses zero.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WrapSigned(int diff, int width)
        {
            if (width >= 32) return diff;
            int shift = 32 - width;
            return (diff << shift) >> shift;
        }

        // ────────────────────────────────────────────────────────────
        //  Bit cursors (LSB-first within each byte, matching BasisBoneRotationCompression)
        // ────────────────────────────────────────────────────────────

        /// <summary>Overwriting LSB-first bit writer. Does not require a pre-cleared buffer.</summary>
        public struct BitWriter
        {
            private readonly byte[] _buf;
            private int _bit;

            public BitWriter(byte[] buffer, int startBit) { _buf = buffer; _bit = startBit; }

            public int BitPosition => _bit;

            public void WriteBits(ulong value, int count)
            {
                int bytePos = _bit >> 3, inByte = _bit & 7, left = count;
                _bit += count;
                while (left > 0)
                {
                    int room = 8 - inByte;
                    int take = left < room ? left : room;
                    int lowMask = (1 << take) - 1;
                    int clear = lowMask << inByte;
                    byte chunk = (byte)(((int)(value & (ulong)lowMask)) << inByte);
                    _buf[bytePos] = (byte)((_buf[bytePos] & ~clear) | chunk);
                    value >>= take;
                    left -= take;
                    bytePos++;
                    inByte = 0;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void WriteBit(int b) => WriteBits((ulong)(b & 1), 1);

            /// <summary>Zig-zag then unsigned Exp-Golomb: 1 bit for 0, 3 for ±1, 5 for ±2..3, and so on.</summary>
            public void WriteSignedEg(int value)
            {
                uint zz = (uint)((value << 1) ^ (value >> 31));   // 0,-1,1,-2,2 -> 0,1,2,3,4
                uint m = zz + 1;
                int numBits = BitLength(m);
                if (numBits > 1) WriteBits(0UL, numBits - 1);     // prefix zeros
                WriteBit(1);                                       // the leading 1 of m
                if (numBits > 1) WriteBitsMsbFirst(m, numBits - 1);
            }

            // Emits the low (count) bits of value, most significant first, so the decoder can
            // rebuild m by shifting in from the top after it has counted the prefix.
            private void WriteBitsMsbFirst(uint value, int count)
            {
                for (int i = count - 1; i >= 0; i--) WriteBit((int)((value >> i) & 1u));
            }
        }

        /// <summary>
        /// Bounds-checked LSB-first bit reader. Every read past <see cref="EndBit"/> latches
        /// <see cref="Failed"/> and yields zero instead of throwing — the delta receive path parses
        /// attacker-reachable bytes, and the fuzz suite requires it never throws.
        /// </summary>
        public struct BitReader
        {
            private readonly byte[] _buf;
            private int _bit;
            private readonly int _end;
            private bool _failed;

            public BitReader(byte[] buffer, int startBit, int endBit)
            {
                _buf = buffer;
                _bit = startBit;
                _end = endBit;
                _failed = false;
            }

            public int BitPosition => _bit;
            public int EndBit => _end;
            public bool Failed => _failed;

            public ulong ReadBits(int count)
            {
                if (_failed || count < 0 || _bit + count > _end) { _failed = true; return 0; }
                int bytePos = _bit >> 3, inByte = _bit & 7, left = count, shift = 0;
                ulong outV = 0;
                _bit += count;
                while (left > 0)
                {
                    int room = 8 - inByte;
                    int take = left < room ? left : room;
                    ulong maskVal = (1UL << take) - 1UL;
                    outV |= (((ulong)_buf[bytePos] >> inByte) & maskVal) << shift;
                    shift += take;
                    left -= take;
                    bytePos++;
                    inByte = 0;
                }
                return outV;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int ReadBit() => (int)ReadBits(1);

            public int ReadSignedEg()
            {
                int zeros = 0;
                while (true)
                {
                    if (_failed || _bit >= _end) { _failed = true; return 0; }
                    if (ReadBit() == 1) break;
                    zeros++;
                    // A valid code never has more prefix zeros than a uint has bits; a corrupt frame
                    // otherwise spins to the end of the buffer on every field.
                    if (zeros > 32) { _failed = true; return 0; }
                }
                uint m = 1;
                for (int i = 0; i < zeros; i++) m = (m << 1) | (uint)ReadBit();
                if (_failed) return 0;
                uint zz = m - 1;
                return (int)((zz >> 1) ^ (uint)(-(int)(zz & 1)));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int BitLength(uint v)
        {
            int n = 0;
            while (v != 0) { n++; v >>= 1; }
            return n;
        }

        /// <summary>Exact bit cost of <see cref="BitWriter.WriteSignedEg"/>, without writing anything.</summary>
        public static int SignedEgBits(int value)
        {
            uint zz = (uint)((value << 1) ^ (value >> 31));
            return 2 * BitLength(zz + 1) - 1;
        }
    }
}
