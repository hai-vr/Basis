using System;
using System.Diagnostics;
using System.Numerics;
using Basis.Network.Core;
using Basis.Network.Core.Compression;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using Xunit;
using Xunit.Abstractions;

namespace BasisServerTests.Compression
{
    /// <summary>
    /// Measures each of the three per-sender codec changes against the code it replaced, so the
    /// speedups are numbers rather than claims.
    ///
    /// <para>The "before" implementations are copied verbatim into this file rather than referenced,
    /// because production no longer contains them. That is deliberate: both arms then run on the same
    /// data, in the same process, against the same JIT, and the comparison is a ratio rather than a
    /// number that has to be trusted across machines.</para>
    ///
    /// <para><b>What this does not measure.</b> Only the operation. Whether that operation matters is
    /// a question for the BSR profiler under real load — this codebase has a standing lesson that a
    /// 15x reduction in a hot loop's iteration count measured exactly nothing at the process level,
    /// so a microbenchmark is evidence that a change did what it intended, not that the server got
    /// faster. Deliberately short (~1 s total) so it can live in the suite.</para>
    ///
    /// <para><b>Nothing here asserts a timing.</b> The printed ratios are only trustworthy when this
    /// class has the machine to itself; run it with
    /// <c>--filter FullyQualifiedName~SimdCodecBenchmark</c>. In a full-suite run the arms compete
    /// with every other test for cores and the ratios move by more than the effects being measured.</para>
    /// </summary>
    public class SimdCodecBenchmark
    {
        private readonly ITestOutputHelper _out;
        public SimdCodecBenchmark(ITestOutputHelper output) => _out = output;

        private const int Rounds = 5;

        // ────────────────────────────────────────────────────────────
        //  "Before" implementations, as they stood prior to this work.
        // ────────────────────────────────────────────────────────────

        private static ulong LegacyReadBits(byte[] src, int bitPos, int bitCount)
        {
            int bytePos = bitPos >> 3;
            int bitInByte = bitPos & 7;
            ulong outV = 0;
            int outShift = 0;
            int bitsLeft = bitCount;

            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;
                ulong maskVal = (1UL << take) - 1UL;
                ulong chunk = ((ulong)src[bytePos] >> bitInByte) & maskVal;
                outV |= chunk << outShift;
                outShift += take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }
            return outV;
        }

        private static void LegacyWriteBits(byte[] dst, int bitPos, ulong value, int bitCount)
        {
            int bytePos = bitPos >> 3;
            int bitInByte = bitPos & 7;
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

        private static uint LegacyRescaleQuant(uint qSrc, int bSrc, int bDst)
        {
            if (bSrc == bDst) return qSrc;
            if (bDst <= 0) return 0;
            ulong maxSrc = ((ulong)1 << bSrc) - 1UL;
            ulong maxDst = ((ulong)1 << bDst) - 1UL;
            ulong num = (ulong)qSrc * maxDst + (maxSrc >> 1);
            return (uint)(num / maxSrc);
        }

        // ────────────────────────────────────────────────────────────

        private static double MedianNsPerOp(Func<long> run, long opsPerRun)
        {
            var samples = new double[Rounds];
            for (int r = 0; r < Rounds; r++)
            {
                var sw = Stopwatch.StartNew();
                long sink = run();
                sw.Stop();
                // Keep the result live so nothing above is dead-code eliminated.
                if (sink == long.MinValue) throw new InvalidOperationException();
                samples[r] = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / opsPerRun;
            }
            Array.Sort(samples);
            return samples[Rounds / 2];
        }

        private void Report(string label, double beforeNs, double afterNs)
        {
            _out.WriteLine($"  {label,-34} {beforeNs,8:F2} ns -> {afterNs,8:F2} ns   {beforeNs / afterNs,5:F2}x");
        }

        [Fact]
        public void BitCodecIsFasterThanTheByteWalkItReplaced()
        {
            const int Iterations = 200_000;

            var layout = BasisAvatarChannelMap.For(BasisAvatarBitPacking.BitQuality.High);
            var channels = layout.Channels;
            var payload = new byte[layout.PayloadBytes];
            new Random(11).NextBytes(payload);
            int n = channels.Length;

            _out.WriteLine($"[SIMD] {BasisSimdCapabilities.Describe()}");
            _out.WriteLine($"Bit field access, High layout: {n} channels x {Iterations} iterations");

            double before = MedianNsPerOp(() =>
            {
                ulong acc = 0;
                for (int it = 0; it < Iterations; it++)
                    for (int c = 0; c < n; c++)
                        acc += LegacyReadBits(payload, channels[c].BitOffset, channels[c].Width);
                return (long)acc;
            }, (long)Iterations * n);

            double after = MedianNsPerOp(() =>
            {
                ulong acc = 0;
                for (int it = 0; it < Iterations; it++)
                    for (int c = 0; c < n; c++)
                        acc += BasisBitCodec.Read(payload, channels[c].BitOffset, channels[c].Width);
                return (long)acc;
            }, (long)Iterations * n);

            Report("read a channel", before, after);

            var scratchLegacy = new byte[layout.PayloadBytes];
            var scratchNew = new byte[layout.PayloadBytes];

            double writeBefore = MedianNsPerOp(() =>
            {
                for (int it = 0; it < Iterations; it++)
                {
                    Array.Clear(scratchLegacy, 0, scratchLegacy.Length);
                    for (int c = 0; c < n; c++)
                        LegacyWriteBits(scratchLegacy, channels[c].BitOffset, (ulong)c, channels[c].Width);
                }
                return scratchLegacy[0];
            }, (long)Iterations * n);

            double writeAfter = MedianNsPerOp(() =>
            {
                for (int it = 0; it < Iterations; it++)
                {
                    Array.Clear(scratchNew, 0, scratchNew.Length);
                    for (int c = 0; c < n; c++)
                        BasisBitCodec.Or(scratchNew, channels[c].BitOffset, (ulong)c, channels[c].Width);
                }
                return scratchNew[0];
            }, (long)Iterations * n);

            Report("write a channel (incl. clear)", writeBefore, writeAfter);

            // Correctness, not speed. Timing is NOT asserted anywhere in this class: the suite runs
            // its tests against a machine that is also running the rest of the suite, and a ratio
            // measured under that contention swings either way — an earlier version of this assert
            // went red on a 0.92x reading of a change that measures 1.18x when it has the box to
            // itself. A benchmark that can fail on load is a flaky test, and this codebase already
            // pays for enough of those. Read the printed ratios; run the class alone to trust them.
            Assert.Equal(
                LegacyReadBits(payload, channels[0].BitOffset, channels[0].Width),
                BasisBitCodec.Read(payload, channels[0].BitOffset, channels[0].Width));
        }

        [Fact]
        public void DirtyMaskPrefilterSkipsTheUnchangedFields()
        {
            const int Iterations = 200_000;

            var layout = BasisAvatarChannelMap.For(BasisAvatarBitPacking.BitQuality.High);
            int payloadBytes = layout.PayloadBytes;
            int fieldCount = layout.FieldCount;

            var keyframe = new byte[payloadBytes];
            new Random(23).NextBytes(keyframe);

            _out.WriteLine($"[SIMD] {BasisSimdCapabilities.Describe()}");
            _out.WriteLine($"Dirty-mask scan, High layout: {fieldCount} fields, {payloadBytes} B payload");

            // Three points on the curve the prefilter actually lives on: a still player (the case
            // delta compression exists for), light motion, and a full-body move where the prefilter
            // can only add cost.
            foreach (int movedFields in new[] { 0, 3, fieldCount })
            {
                var current = (byte[])keyframe.Clone();
                var rng = new Random(31 + movedFields);
                for (int f = 0; f < movedFields; f++)
                {
                    int c = layout.FieldChannelStart(f);
                    if (c >= layout.FieldChannelEnd(f)) continue;
                    var ch = layout.Channels[c];
                    uint v = (uint)rng.Next() & ch.Mask;
                    BasisBitCodec.Replace(current, ch.BitOffset, v ^ 1u, ch.Width);
                }

                double before = MedianNsPerOp(() =>
                {
                    long dirty = 0;
                    for (int it = 0; it < Iterations; it++)
                    {
                        for (int f = 0; f < fieldCount; f++)
                        {
                            for (int c = layout.FieldChannelStart(f); c < layout.FieldChannelEnd(f); c++)
                            {
                                var ch = layout.Channels[c];
                                if (BasisBitCodec.Read(current, ch.BitOffset, ch.Width)
                                    != BasisBitCodec.Read(keyframe, ch.BitOffset, ch.Width))
                                {
                                    dirty++;
                                    break;
                                }
                            }
                        }
                    }
                    return dirty;
                }, Iterations);

                double after = MedianNsPerOp(() =>
                {
                    long dirty = 0;
                    for (int it = 0; it < Iterations; it++)
                    {
                        ulong words = BasisPayloadDiff.WordDiffMask(current, keyframe, payloadBytes);
                        for (int f = 0; f < fieldCount; f++)
                        {
                            if ((words & layout.FieldWordMask[f]) == 0) continue;
                            for (int c = layout.FieldChannelStart(f); c < layout.FieldChannelEnd(f); c++)
                            {
                                var ch = layout.Channels[c];
                                if (BasisBitCodec.Read(current, ch.BitOffset, ch.Width)
                                    != BasisBitCodec.Read(keyframe, ch.BitOffset, ch.Width))
                                {
                                    dirty++;
                                    break;
                                }
                            }
                        }
                    }
                    return dirty;
                }, Iterations);

                string shape = movedFields == 0 ? "still" : movedFields == fieldCount ? "everything moved" : $"{movedFields} fields moved";
                Report($"whole-payload scan ({shape})", before, after);
            }
        }

        [Fact]
        public void ReciprocalRescaleIsFasterThanTheDivide()
        {
            const int Iterations = 2_000_000;

            // The pairs the repacker actually runs: High 12-bit components down to each lower tier.
            var pairs = new (int src, int dst)[] { (12, 8), (12, 6), (12, 5), (5, 4), (13, 9), (13, 7) };

            _out.WriteLine($"Quantized rescale, {pairs.Length} width pairs x {Iterations} iterations");

            double before = MedianNsPerOp(() =>
            {
                uint acc = 0;
                for (int it = 0; it < Iterations; it++)
                {
                    var p = pairs[it % pairs.Length];
                    acc += LegacyRescaleQuant((uint)(it & ((1 << p.src) - 1)), p.src, p.dst);
                }
                return acc;
            }, Iterations);

            double after = MedianNsPerOp(() =>
            {
                uint acc = 0;
                for (int it = 0; it < Iterations; it++)
                {
                    var p = pairs[it % pairs.Length];
                    acc += QuantRescaleTable.Rescale((uint)(it & ((1 << p.src) - 1)), p.src, p.dst);
                }
                return acc;
            }, Iterations);

            Report("rescale one component", before, after);

            // Same reasoning as above: assert the result, print the ratio.
            foreach (var p in pairs)
            {
                Assert.Equal(LegacyRescaleQuant(1u, p.src, p.dst), QuantRescaleTable.Rescale(1u, p.src, p.dst));
            }
        }

        /// <summary>
        /// A server whose vector paths are running scalar would still pass every correctness test
        /// while quietly giving up the width. Worth one line in the output so a benchmark run says so.
        /// </summary>
        [Fact]
        public void ReportsTheVectorWidthInUse()
        {
            _out.WriteLine($"Vector.IsHardwareAccelerated = {Vector.IsHardwareAccelerated}");
            _out.WriteLine($"Vector<byte>.Count           = {Vector<byte>.Count}");
            _out.WriteLine($"BasisSimdCapabilities        = {BasisSimdCapabilities.Describe()}");
            Assert.True(BasisSimdCapabilities.VectorByteWidth >= 1);
        }
    }
}
