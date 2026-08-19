using System.Runtime.CompilerServices;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    /// <summary>
    /// Fixed reciprocals for the only division the avatar repacker performs, so it can be a multiply.
    ///
    /// <para>Rescaling a quantized value from <c>bSrc</c> bits to <c>bDst</c> bits divides by
    /// <c>2^bSrc - 1</c>. Both widths come from the wire layout, so the divisor is one of a handful of
    /// compile-time-ish constants and is fixed before any player connects — but written as
    /// <c>num / maxSrc</c> the JIT cannot see that and emits a hardware divide, the slowest integer
    /// instruction on x86 and one that does not pipeline. The repacker runs this ~250 times per avatar
    /// frame across three quality tiers, so those divides serialise a meaningful slice of the
    /// per-sender path.</para>
    ///
    /// <para><b>The operands fit in 32 bits, which is what makes this safe.</b> Every layout width is
    /// at most 16 bits (13 in the current tables), so
    /// <c>num = qSrc*maxDst + maxSrc/2 &lt; 2^16 * 2^16 = 2^32</c>. The old code computed in 64-bit
    /// purely because the expression was written that way, paying for a 64-bit divide it never needed.
    /// Even the fallback here — a 32-bit divide — is materially cheaper than what it replaces.</para>
    ///
    /// <para><b>Exactness.</b> With <c>M = ceil(2^S / d)</c> and <c>e = M*d - 2^S</c>, the identity
    /// <c>floor(n*M / 2^S) == floor(n / d)</c> holds for every <c>n &lt; N</c> exactly when
    /// <c>(N-1)*e &lt; 2^S</c> — the standard reciprocal bound, and the reason the shift is searched
    /// rather than fixed. A pair whose bound cannot be met at any usable shift installs no reciprocal
    /// and divides. <c>QuantRescaleTableTests</c> re-checks every installed reciprocal against real
    /// division across the divisor's entire input domain, so the closed form above is proven against
    /// the values rather than merely asserted.</para>
    /// </summary>
    public static class QuantRescaleTable
    {
        /// <summary>Widest field the wire layout can produce. Current tables peak at 13.</summary>
        public const int MaxBits = 16;

        private const int Stride = MaxBits + 1;

        // Multiplier[0] == a pair with no usable reciprocal, which divides instead.
        private static readonly ulong[] Multiplier = new ulong[Stride * Stride];
        private static readonly byte[] ShiftFor = new byte[Stride * Stride];

        static QuantRescaleTable()
        {
            for (int bSrc = 1; bSrc <= MaxBits; bSrc++)
            {
                for (int bDst = 1; bDst <= MaxBits; bDst++)
                {
                    if (bSrc == bDst) continue;   // identity; never reaches the table

                    ulong maxSrc = (1UL << bSrc) - 1UL;
                    ulong maxDst = (1UL << bDst) - 1UL;
                    ulong numMax = maxSrc * maxDst + (maxSrc >> 1);

                    // High shifts first: a larger shift shrinks the reciprocal's error term, so the
                    // first one that both fits and satisfies the bound is the most robust available.
                    for (int shift = 62; shift >= 32; shift--)
                    {
                        ulong pow = 1UL << shift;
                        ulong m = pow / maxSrc + 1UL;          // ceil(2^S / d) for non-dividing d
                        if (maxSrc == 1UL) m = pow;            // d == 1 divides exactly; ceil would overshoot

                        // The product must stay inside 64 bits for every input in the domain.
                        if (m != 0 && numMax != 0 && m > ulong.MaxValue / numMax) continue;

                        ulong error = m * maxSrc - pow;
                        if (numMax != 0 && error != 0 && error > (pow - 1) / numMax) continue;

                        Multiplier[bSrc * Stride + bDst] = m;
                        ShiftFor[bSrc * Stride + bDst] = (byte)shift;
                        break;
                    }
                }
            }
        }

        /// <summary>True when this width pair rescales by multiply rather than by divide.</summary>
        public static bool HasReciprocal(int bSrc, int bDst) =>
            (uint)bSrc <= MaxBits && (uint)bDst <= MaxBits && Multiplier[bSrc * Stride + bDst] != 0;

        /// <summary>
        /// The exact scalar this is a fast path for: round-to-nearest rescale of
        /// <paramref name="qSrc"/> from <paramref name="bSrc"/> bits to <paramref name="bDst"/> bits.
        /// Kept as the reference the tests compare against.
        /// </summary>
        public static uint RescaleExact(uint qSrc, int bSrc, int bDst)
        {
            ulong maxSrc = ((ulong)1 << bSrc) - 1UL;
            ulong maxDst = ((ulong)1 << bDst) - 1UL;
            return (uint)((qSrc * maxDst + (maxSrc >> 1)) / maxSrc);
        }

        /// <summary>
        /// Rescales <paramref name="qSrc"/>, which must already be masked to <paramref name="bSrc"/>
        /// bits (every caller reads it straight out of a bitfield of that width). Anything outside
        /// that domain, or outside the modelled width range, takes the exact 64-bit path — the 32-bit
        /// arithmetic below is only valid inside it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Rescale(uint qSrc, int bSrc, int bDst)
        {
            if ((uint)bSrc > MaxBits || (uint)bDst > MaxBits) return RescaleExact(qSrc, bSrc, bDst);

            uint maxSrc = (1u << bSrc) - 1u;
            if (qSrc > maxSrc) return RescaleExact(qSrc, bSrc, bDst);

            uint maxDst = (1u << bDst) - 1u;
            uint num = qSrc * maxDst + (maxSrc >> 1);

            int slot = bSrc * Stride + bDst;
            ulong m = Multiplier[slot];
            if (m == 0) return num / maxSrc;
            return (uint)((num * m) >> ShiftFor[slot]);
        }
    }
}
