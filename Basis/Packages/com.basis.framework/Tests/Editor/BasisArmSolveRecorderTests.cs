using System.Collections.Generic;
using System.IO;
using Basis.IK;
using Basis.Scripts.Drivers;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    /// <summary>
    /// ================================================================================================
    /// ⭐ THE RECORDER'S OWN PROMISES, AND THE MUTANT FOR EACH ONE.
    ///
    /// Four claims are made for BasisArmSolveRecorder, and all four are the kind that are easy to believe
    /// and expensive to be wrong about:
    ///
    ///   1. FREE WHEN OFF          -- one bool, no file, no allocation, no state touched.
    ///   2. BOUNDED IN MEMORY      -- it STREAMS, so the resident buffer does not grow with session length.
    ///                               The mutant is the accumulate-then-cap shape it replaces, and the gate
    ///                               is arranged so that shape would fail it.
    ///   3. AN EMPTY CELL IS NaN, NEVER ZERO -- a band the user never visited must not be able to report a
    ///                               comfortable "0 deg". That is how "this band is fine" gets said about a
    ///                               band with no data in it, which is the failure mode this whole capture
    ///                               exists to prevent.
    ///   4. A FIRING RATE'S DENOMINATOR IS AVAILABILITY, NOT FRAMES -- a guard that was structurally
    ///                               declined all session must read "n/a", never "0.0%". Those two strings
    ///                               mean opposite things and one of them shipped a 0-of-22032 defect.
    ///
    /// ⚠️ THE TESTS WRITE REAL FILES, ON PURPOSE, and delete them in TearDown. The file open is part of
    /// what Begin promises and a test that stubbed it out would not be testing the recorder.
    /// ================================================================================================
    /// </summary>
    public class BasisArmSolveRecorderTests
    {
        readonly List<string> m_Created = new List<string>();

        /// <summary>
        /// Every Begin in this fixture goes through here so TearDown can delete EVERY file the run made,
        /// not just the last one. Reading LastWrittenPath at teardown misses the intermediate sessions in
        /// the tests that Begin more than once, and those files then accumulate in the developer's real
        /// persistentDataPath one CI run at a time.
        /// </summary>
        void Begin(string label)
        {
            BasisArmSolveRecorder.Begin(label);
            string path = BasisArmSolveRecorder.LastWrittenPath;
            if (!string.IsNullOrEmpty(path) && !m_Created.Contains(path))
            {
                m_Created.Add(path);
            }
        }

        [TearDown]
        public void TearDown()
        {
            BasisArmSolveRecorder.Stop();
            BasisArmSolveRecorder.DeveloperEnabled = false;
            foreach (string path in m_Created)
            {
                if (File.Exists(path))
                {
                    try { File.Delete(path); } catch { /* a leaked temp file is not a test failure */ }
                }
            }
            m_Created.Clear();
        }

        // ============================================================================================
        // 1. FREE WHEN OFF
        // ============================================================================================

        /// <summary>
        /// The gate is read ONCE at Begin, exactly as BasisCalibrationDebugRecorder's is, so that the
        /// per-frame cost of leaving the instrumentation wired in is a single static bool test. This
        /// asserts the observable half of that: gated off, Begin creates nothing and Record changes
        /// nothing, so there is no state for a hot path to have to check.
        /// </summary>
        [Test]
        public void Recorder_GatedOff_CreatesNothingAndRecordsNothing()
        {
            BasisArmSolveRecorder.DeveloperEnabled = false;
            Begin("gate-off");

            Assert.That(BasisArmSolveRecorder.Active, Is.False, "the gate was off and the recorder started anyway");

            BasisArmDiagnostics d = MakeRow(45f, 0.9f, 30f, 20f);
            for (int k = 0; k < 500; k++)
            {
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmLeft, k * 0.011f, in d);
                BasisArmSolveRecorder.EndFrame();
            }

            Assert.That(BasisArmSolveRecorder.RowsWritten, Is.EqualTo(0), "a gated-off recorder wrote rows");
            Assert.That(BasisArmSolveRecorder.CapturedFrames, Is.EqualTo(0), "a gated-off recorder advanced its frame counter");
            Assert.That(BasisArmSolveRecorder.BufferedChars, Is.EqualTo(0), "a gated-off recorder buffered characters");

            // Non-vacuity: with the gate ON the very same calls must do all three of those things, or the
            // assertions above are about a recorder that never works rather than about the gate.
            BasisArmSolveRecorder.DeveloperEnabled = true;
            Begin("gate-on");
            Assert.That(BasisArmSolveRecorder.Active, Is.True, "the gate was on and the recorder refused to start");
            BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmLeft, 0f, in d);
            BasisArmSolveRecorder.EndFrame();
            Assert.That(BasisArmSolveRecorder.RowsWritten, Is.EqualTo(1),
                "with the gate ON the recorder still wrote nothing, so the gated-off assertions prove nothing.");
            Assert.That(BasisArmSolveRecorder.CapturedFrames, Is.EqualTo(1));
        }

        /// <summary>An out-of-range arm index must be dropped, not indexed. A recorder that reads past its
        /// own arrays on a bad slot turns a diagnostic into a crash in the one build that has it on.</summary>
        [Test]
        public void Recorder_RejectsAnOutOfRangeArmSlot()
        {
            BasisArmSolveRecorder.DeveloperEnabled = true;
            Begin("bad-slot");

            BasisArmDiagnostics d = MakeRow(45f, 0.9f, 30f, 20f);
            Assert.DoesNotThrow(() => BasisArmSolveRecorder.Record(2, 0f, in d));
            Assert.DoesNotThrow(() => BasisArmSolveRecorder.Record(-1, 0f, in d));
            Assert.DoesNotThrow(() => BasisArmSolveRecorder.Record(int.MaxValue, 0f, in d));
            Assert.That(BasisArmSolveRecorder.RowsWritten, Is.EqualTo(0), "an out-of-range slot was recorded");

            BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmRight, 0f, in d);
            Assert.That(BasisArmSolveRecorder.RowsWritten, Is.EqualTo(1), "a valid slot was also rejected");
        }

        // ============================================================================================
        // 2. BOUNDED IN MEMORY
        // ============================================================================================

        /// <summary>
        /// ⭐ BOUNDED MEANS BOUNDED IN SESSION LENGTH, WHICH IS A STATEMENT ABOUT THE SHAPE AND NOT ABOUT
        /// A CAP.
        ///
        /// BasisLegSwivelDebug accumulates into a StringBuilder and stops at 16000 rows. That IS bounded,
        /// and it is the wrong bound: it drops the END of a long session, which is where the artifact you
        /// are still recording for lives. This recorder streams instead, and the difference is visible
        /// exactly here -- the resident buffer stays inside one flush window no matter how many rows have
        /// gone through, while the file keeps growing.
        ///
        /// THE MUTANT IS THE SHAPE IT REPLACES: the gate compares the resident buffer against the TOTAL
        /// bytes written, and demands the total be many times larger. An accumulating implementation has
        /// those two numbers equal by construction and fails on the spot.
        /// </summary>
        [Test]
        public void Recorder_Streams_SoResidentMemoryDoesNotGrowWithTheSession()
        {
            BasisArmSolveRecorder.DeveloperEnabled = true;
            Begin("bounded");
            string path = BasisArmSolveRecorder.LastWrittenPath;
            Assert.That(File.Exists(path), Is.True, "Begin did not open the CSV, so nothing below is streaming anywhere");

            const int frames = 4000;
            int worstBuffered = 0;
            for (int k = 0; k < frames; k++)
            {
                BasisArmDiagnostics l = MakeRow(20f + (k % 140), 0.5f + 0.4f * ((k % 100) / 100f), k % 170, (k % 90) - 45);
                BasisArmDiagnostics r = MakeRow(15f + (k % 150), 0.4f + 0.5f * ((k % 77) / 77f), k % 160, (k % 80) - 40);
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmLeft, k * 0.011f, in l);
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmRight, k * 0.011f, in r);
                BasisArmSolveRecorder.EndFrame();
                worstBuffered = Mathf.Max(worstBuffered, BasisArmSolveRecorder.BufferedChars);
            }

            Assert.That(BasisArmSolveRecorder.RowsWritten, Is.EqualTo(frames * 2));
            Assert.That(BasisArmSolveRecorder.CapturedFrames, Is.EqualTo(frames));

            // One flush window's worth of rows, generously sized. Nothing about this bound scales with
            // `frames`, which is the whole claim.
            int bound = BasisArmSolveRecorder.FlushEveryRows * 400;
            Assert.That(worstBuffered, Is.LessThan(bound),
                $"the resident buffer reached {worstBuffered} chars over {frames} frames, past the fixed bound {bound}. " +
                "This recorder is accumulating, not streaming, and a long session will grow without limit.");

            BasisArmSolveRecorder.StopAndDump();

            var info = new FileInfo(path);
            Assert.That(info.Exists, Is.True);
            Assert.That(info.Length, Is.GreaterThan((long)worstBuffered * 4L),
                $"the file is only {info.Length} bytes against a peak resident buffer of {worstBuffered} chars. " +
                "Those two being comparable is the signature of the accumulate-then-write shape, in which the " +
                "'bound' above is satisfied trivially because the session was short.");

            // NOTHING WAS LOST. A cap-and-drop recorder fails here; a streaming one cannot.
            int lines = File.ReadAllLines(path).Length;
            Assert.That(lines, Is.EqualTo(frames * 2 + 1),
                $"the CSV has {lines} lines against {frames * 2} rows plus a header. Rows were dropped, which is " +
                "exactly the failure mode streaming was chosen to avoid.");
        }

        // ============================================================================================
        // 3. AN EMPTY CELL IS NaN, NEVER ZERO
        // ============================================================================================

        /// <summary>
        /// ⭐ THE MOST DANGEROUS NUMBER THIS INSTRUMENT COULD REPORT IS A CONFIDENT ZERO FOR A BAND NOBODY
        /// VISITED.
        ///
        /// "p95 humeral twist in the 120-150 elevation band: 0 deg" reads as evidence that the band is
        /// safe. If the user never raised their arm that high, it is evidence of nothing at all, and the
        /// difference is invisible unless the empty case is explicitly not-a-number. Percentile returns NaN
        /// and the summary prints "not visited"; both are asserted here.
        /// </summary>
        [Test]
        public void Recorder_AnUnvisitedCellReportsNaN_NotAComfortableZero()
        {
            BasisArmSolveRecorder.DeveloperEnabled = true;
            Begin("empty-cells");

            // Occupy exactly ONE cell: elevation band 1 (30-60), extension band 3 (0.95-0.99).
            const int occupiedElev = 1, occupiedExt = 3;
            BasisArmDiagnostics d = MakeRow(45f, 0.97f, 30f, 20f);
            Assert.That(BasisArmDiagnosticsCore.ElevationBand(d.ElevationDeg), Is.EqualTo(occupiedElev),
                "the fixture row does not land in the band this test believes it does");
            Assert.That(BasisArmDiagnosticsCore.ExtensionBand(d.ReachRatio), Is.EqualTo(occupiedExt));

            for (int k = 0; k < 200; k++)
            {
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmLeft, k * 0.011f, in d);
                BasisArmSolveRecorder.EndFrame();
            }

            int occupied = BasisArmDiagnosticsCore.Cell(occupiedElev, occupiedExt);
            Assert.That(BasisArmSolveRecorder.CellFrames(BasisArmSolveRecorder.ArmLeft, occupied), Is.EqualTo(200));
            Assert.That(float.IsNaN(BasisArmSolveRecorder.Percentile(BasisArmSolveRecorder.ArmLeft, occupied, 0, 0.5f)),
                Is.False, "the OCCUPIED cell reported NaN, so the NaN below carries no information");

            int emptyCells = 0;
            for (int e = 0; e < BasisArmDiagnosticsCore.ElevationBands; e++)
            {
                for (int x = 0; x < BasisArmDiagnosticsCore.ExtensionBands; x++)
                {
                    int cell = BasisArmDiagnosticsCore.Cell(e, x);
                    if (cell == occupied)
                    {
                        continue;
                    }
                    emptyCells++;
                    Assert.That(BasisArmSolveRecorder.CellFrames(BasisArmSolveRecorder.ArmLeft, cell), Is.EqualTo(0));
                    float p = BasisArmSolveRecorder.Percentile(BasisArmSolveRecorder.ArmLeft, cell, 0, 0.95f);
                    Assert.That(float.IsNaN(p), Is.True,
                        $"cell (elev {e}, ext {x}) has NO FRAMES and reported {p} instead of NaN. A band nobody " +
                        "visited must not be able to report a number that reads as a clean bill of health.");
                }
            }
            Assert.That(emptyCells, Is.EqualTo(BasisArmDiagnosticsCore.CellCount - 1));

            string summary = BasisArmSolveRecorder.Summary();
            Assert.That(summary.Contains("not visited"), Is.True,
                "the summary does not name its unvisited bands, so a reader cannot tell the edge of the evidence " +
                "from the evidence.");
            Assert.That(summary.Contains("CANNOT referee"), Is.True);
        }

        /// <summary>
        /// The percentiles come out of the histogram rather than out of a sample list, so they have to be
        /// checked against a distribution whose answers are known by construction. Uniform 0..100 in 1 deg
        /// steps: p50 = 50, p95 = 95, max = 100, to within the 1 deg bucket.
        /// </summary>
        [Test]
        public void Recorder_PercentilesComeOutOfTheHistogramCorrectly()
        {
            BasisArmSolveRecorder.DeveloperEnabled = true;
            Begin("percentiles");

            for (int deg = 0; deg <= 100; deg++)
            {
                BasisArmDiagnostics d = MakeRow(45f, 0.97f, deg, 0f);
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmRight, deg * 0.011f, in d);
                BasisArmSolveRecorder.EndFrame();
            }

            int cell = BasisArmDiagnosticsCore.Cell(1, 3);
            Assert.That(BasisArmSolveRecorder.CellFrames(BasisArmSolveRecorder.ArmRight, cell), Is.EqualTo(101));

            float p50 = BasisArmSolveRecorder.Percentile(BasisArmSolveRecorder.ArmRight, cell, 0, 0.50f);
            float p95 = BasisArmSolveRecorder.Percentile(BasisArmSolveRecorder.ArmRight, cell, 0, 0.95f);
            float max = BasisArmSolveRecorder.Percentile(BasisArmSolveRecorder.ArmRight, cell, 0, 1.00f);

            Assert.That(p50, Is.EqualTo(50f).Within(1.5f), $"p50 came out at {p50} on a uniform 0..100 distribution");
            Assert.That(p95, Is.EqualTo(95f).Within(1.5f), $"p95 came out at {p95}");
            Assert.That(max, Is.EqualTo(100f).Within(1.5f), $"max came out at {max}");

            // Non-vacuity: the three must be DISTINCT, or a stub returning one constant would pass.
            Assert.That(p50, Is.Not.EqualTo(p95));
            Assert.That(p95, Is.Not.EqualTo(max));

            // The SIGN must not matter -- occupied arc is a magnitude. A -80 and a +80 belong in one bucket.
            Begin("percentiles-signed");
            for (int k = 0; k < 50; k++)
            {
                BasisArmDiagnostics neg = MakeRow(45f, 0.97f, -80f, 0f);
                BasisArmDiagnostics pos = MakeRow(45f, 0.97f, 80f, 0f);
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmRight, k * 0.011f, in neg);
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmRight, k * 0.011f, in pos);
                BasisArmSolveRecorder.EndFrame();
            }
            float signedP50 = BasisArmSolveRecorder.Percentile(BasisArmSolveRecorder.ArmRight, cell, 0, 0.5f);
            Assert.That(signedP50, Is.EqualTo(80f).Within(1.5f),
                $"a 50/50 mix of -80 and +80 deg reported p50 {signedP50}. The histogram is being fed the SIGNED " +
                "value, so equal and opposite excursions cancel instead of both counting as occupied arc.");
        }

        // ============================================================================================
        // 4. THE FIRING-RATE DENOMINATOR
        // ============================================================================================

        /// <summary>
        /// ⭐ "0.0%" AND "n/a" MEAN OPPOSITE THINGS, AND THE DIFFERENCE IS A SHIPPED DEFECT.
        ///
        /// A guard that fires 0 times out of 22032 frames it COULD have fired in is a tuned guard doing
        /// nothing, which is a defect. A guard that fires 0 times out of 0 frames it could have fired in
        /// is a guard that was never wired up, which is a different defect with a different fix. Divide by
        /// total frames and the second one prints as the reassuring first one.
        ///
        /// So the denominator is availability. This feeds a session in which the twist guard is never
        /// available and asserts the report says so, then feeds one in which it is available and quiet and
        /// asserts THAT prints as a rate -- because if both printed "n/a" the distinction would be lost the
        /// other way round.
        /// </summary>
        [Test]
        public void Recorder_FiringRateDenominatorIsAvailability_SoADeadGuardCannotReadAsAQuietOne()
        {
            // --- session A: the guard is never available.
            BasisArmSolveRecorder.DeveloperEnabled = true;
            Begin("guard-dead");
            for (int k = 0; k < 300; k++)
            {
                BasisArmDiagnostics d = MakeRow(45f, 0.97f, 10f, 5f);
                d.TwistGuardAvailable = 0f;
                d.TwistGuardFired = 0f;
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmLeft, k * 0.011f, in d);
                BasisArmSolveRecorder.EndFrame();
            }
            string dead = BasisArmSolveRecorder.Summary();
            Assert.That(dead.Contains("n/a"), Is.True,
                "a guard that was NEVER AVAILABLE for 300 frames did not report 'n/a'. If it reported a " +
                "percentage, a structurally dead guard is indistinguishable from a quiet one -- which is " +
                "precisely how a guard that fired 0 of 22032 times read as healthy.");

            // --- session B: available the whole time, and quiet. This MUST print a rate, not 'n/a'.
            BasisArmSolveRecorder.Stop();
            BasisArmSolveRecorder.DeveloperEnabled = true;
            Begin("guard-quiet");
            for (int k = 0; k < 300; k++)
            {
                BasisArmDiagnostics d = MakeRow(45f, 0.97f, 10f, 5f);
                d.TwistGuardAvailable = 1f;
                d.TwistGuardFired = k < 6 ? 1f : 0f;   // 2.0%, inside the house 3% precedent
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmLeft, k * 0.011f, in d);
                BasisArmSolveRecorder.EndFrame();
            }
            string quiet = BasisArmSolveRecorder.Summary();
            Assert.That(quiet.Contains("2.0%"), Is.True,
                "an AVAILABLE guard firing 6 of 300 frames did not report 2.0%. Either the denominator is " +
                "wrong or availability is not being tracked, and both make the rate uninterpretable.");
            Assert.That(quiet, Is.Not.EqualTo(dead),
                "the dead-guard and quiet-guard sessions produced identical reports, so the distinction this " +
                "test exists to preserve is not actually in the output.");
        }

        /// <summary>
        /// A non-finite row is counted and then EXCLUDED. Filing it into whichever bucket a NaN comparison
        /// falls through into would put a frame that is not evidence about anything into a percentile that
        /// somebody is going to quote.
        /// </summary>
        [Test]
        public void Recorder_NonFiniteFramesAreCountedAndExcluded_NotFiled()
        {
            BasisArmSolveRecorder.DeveloperEnabled = true;
            Begin("nonfinite");

            int cell = BasisArmDiagnosticsCore.Cell(1, 3);

            for (int k = 0; k < 100; k++)
            {
                BasisArmDiagnostics good = MakeRow(45f, 0.97f, 40f, 10f);
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmLeft, k * 0.011f, in good);

                BasisArmDiagnostics bad = MakeRow(45f, 0.97f, 40f, 10f);
                bad.ChainAxialDeg = float.NaN;
                bad.NonFinite = 1f;
                BasisArmSolveRecorder.Record(BasisArmSolveRecorder.ArmLeft, k * 0.011f, in bad);

                BasisArmSolveRecorder.EndFrame();
            }

            Assert.That(BasisArmSolveRecorder.RowsWritten, Is.EqualTo(200),
                "the non-finite rows must still reach the CSV -- excluding them from the STATISTICS is not the " +
                "same as hiding them, and the frame they happened on is the one worth looking at.");
            Assert.That(BasisArmSolveRecorder.CellFrames(BasisArmSolveRecorder.ArmLeft, cell), Is.EqualTo(100),
                "a non-finite row was counted into a band's sample size, so every percentile in that band is now " +
                "computed over a denominator that includes frames carrying no measurement.");

            string summary = BasisArmSolveRecorder.Summary();
            Assert.That(summary.Contains("non-finite 100"), Is.True,
                "the non-finite frames were dropped SILENTLY. They have to be counted where a reader sees them, " +
                "or a capture that was 40% NaN looks exactly like one that was clean.");
        }

        /// <summary>The summary must survive being asked for before any data exists, and must say so rather
        /// than emit a table of zeroes that reads like a result.</summary>
        [Test]
        public void Recorder_SummaryOnAnEmptySessionSaysSo()
        {
            BasisArmSolveRecorder.DeveloperEnabled = false;
            BasisArmSolveRecorder.Stop();
            string s = BasisArmSolveRecorder.Summary();
            Assert.That(s.Contains("no data"), Is.True, $"an empty session summarised as: {s}");
        }

        // ============================================================================================
        // helpers
        // ============================================================================================

        /// <summary>A row with just enough set to land in a known cell with known magnitudes. Everything
        /// else is left at the struct default, which is the recorder's own decline path.</summary>
        static BasisArmDiagnostics MakeRow(float elevationDeg, float reach, float humeralTwistDeg, float chainAxialDeg)
        {
            BasisArmDiagnostics d = default;
            d.Side = 1f;
            d.ElevationDeg = elevationDeg;
            d.ReachRatio = reach;
            d.HumeralTwistDeg = humeralTwistDeg;
            d.ChainAxialDeg = chainAxialDeg;
            d.TwistGuardAvailable = 1f;
            d.TipAvailable = 1f;
            d.HintWeight = 1f;
            d.TorsoUpValid = 1f;
            return d;
        }
    }
}
