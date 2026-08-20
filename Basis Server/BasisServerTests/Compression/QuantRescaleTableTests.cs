using Basis.Network.Core.Compression;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using Xunit;

namespace BasisServerTests.Compression
{
    /// <summary>
    /// The reciprocal table replaces a hardware divide in the repacker's innermost operation, so
    /// "close enough" is not a thing it can be: a single off-by-one would show as a bone that is
    /// subtly wrong at Medium and right at High, on one machine's worth of players.
    ///
    /// <para>The width pairs are drawn from the wire layout and every input is a bitfield of a known
    /// width, so the entire input domain is small enough to enumerate — which is what these do. The
    /// production table picks its shift from a closed-form bound; this checks that bound against the
    /// values it is supposed to be a statement about.</para>
    /// </summary>
    public class QuantRescaleTableTests
    {
        [Fact]
        public void ReciprocalMatchesDivisionAcrossEveryWidthPairAndEveryInput()
        {
            for (int bSrc = 1; bSrc <= QuantRescaleTable.MaxBits; bSrc++)
            {
                for (int bDst = 1; bDst <= QuantRescaleTable.MaxBits; bDst++)
                {
                    if (bSrc == bDst) continue;

                    uint maxSrc = (1u << bSrc) - 1u;
                    for (uint q = 0; q <= maxSrc; q++)
                    {
                        uint expected = QuantRescaleTable.RescaleExact(q, bSrc, bDst);
                        uint actual = QuantRescaleTable.Rescale(q, bSrc, bDst);
                        Assert.True(expected == actual,
                            $"{bSrc}->{bDst} bits, q={q}: expected {expected}, got {actual}");
                    }
                }
            }
        }

        /// <summary>
        /// Every width pair the wire layout can actually ask for must have taken the multiply path.
        /// Without this the tests above would still pass with every pair silently falling back to a
        /// divide, and the optimisation would be gone with nothing to show it.
        /// </summary>
        [Fact]
        public void EveryWidthPairTheLayoutUsesHasAReciprocal()
        {
            var qualities = new[]
            {
                BasisAvatarBitPacking.BitQuality.VeryLow,
                BasisAvatarBitPacking.BitQuality.Low,
                BasisAvatarBitPacking.BitQuality.Medium,
            };

            byte[] highBpc = BasisBoneRotationCompression.BPC_HIGH;

            foreach (var quality in qualities)
            {
                byte[] dstBpc = BasisBoneRotationCompression.GetBpcTable(quality);
                for (int slot = 0; slot < BasisBoneRotationCompression.WireBoneSlotCount; slot++)
                {
                    if (BasisBoneRotationCompression.BONE_DOF[slot] != 3) continue;
                    AssertPair(highBpc[slot], dstBpc[slot]);
                }

                AssertPair(BasisBoneRotationCompression.CurlBits(BasisAvatarBitPacking.BitQuality.High),
                           BasisBoneRotationCompression.CurlBits(quality));
                AssertPair(BasisBoneRotationCompression.SplayBits(BasisAvatarBitPacking.BitQuality.High),
                           BasisBoneRotationCompression.SplayBits(quality));
                AssertPair(BasisBoneRotationCompression.HingeBits(BasisAvatarBitPacking.BitQuality.High),
                           BasisBoneRotationCompression.HingeBits(quality));
                AssertPair(BasisBoneRotationCompression.TwistBits(BasisAvatarBitPacking.BitQuality.High),
                           BasisBoneRotationCompression.TwistBits(quality));
                AssertPair(BasisBoneRotationCompression.SingleAxisBits(BasisAvatarBitPacking.BitQuality.High),
                           BasisBoneRotationCompression.SingleAxisBits(quality));
            }

            static void AssertPair(int bSrc, int bDst)
            {
                if (bSrc == bDst) return;   // identity short-circuits before the table
                Assert.True(QuantRescaleTable.HasReciprocal(bSrc, bDst),
                    $"{bSrc}->{bDst} bits fell back to a divide; the repacker uses this pair every frame");
            }
        }

        /// <summary>
        /// Inputs wider than their stated width are outside the 32-bit arithmetic's safe range and
        /// must route to the exact path rather than wrapping. No caller does this today — every
        /// value arrives straight out of a bitfield — but the fast path's correctness depends on it,
        /// so the guard is pinned rather than left as a comment.
        /// </summary>
        [Fact]
        public void OutOfDomainInputsStillMatchTheExactResult()
        {
            uint[] rogue = { 0xFFFFu, 0x1_0000u, 0x00FF_FFFFu, uint.MaxValue / 2, uint.MaxValue };

            foreach (uint q in rogue)
            {
                for (int bSrc = 4; bSrc <= 13; bSrc++)
                {
                    for (int bDst = 4; bDst <= 13; bDst++)
                    {
                        if (bSrc == bDst) continue;
                        Assert.Equal(QuantRescaleTable.RescaleExact(q, bSrc, bDst),
                                     QuantRescaleTable.Rescale(q, bSrc, bDst));
                    }
                }
            }
        }

        /// <summary>Boundary values keep their meaning: zero stays zero and full scale stays full scale.</summary>
        [Fact]
        public void EndpointsArePreserved()
        {
            for (int bSrc = 1; bSrc <= 16; bSrc++)
            {
                for (int bDst = 1; bDst <= 16; bDst++)
                {
                    if (bSrc == bDst) continue;
                    Assert.Equal(0u, QuantRescaleTable.Rescale(0u, bSrc, bDst));
                    Assert.Equal((1u << bDst) - 1u, QuantRescaleTable.Rescale((1u << bSrc) - 1u, bSrc, bDst));
                }
            }
        }
    }
}
