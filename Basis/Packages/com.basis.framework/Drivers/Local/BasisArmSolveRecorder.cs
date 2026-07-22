using System;
using System.Globalization;
using System.IO;
using System.Text;
using Basis.IK;
using UnityEngine;

namespace Basis.Scripts.Drivers
{
    /// <summary>
    /// ⭐ THE ARM CHANNEL. Per arm, per frame, in a REAL headset session, streamed to CSV.
    ///
    /// ================================================================================================
    /// WHY THIS EXISTS, AND WHY IT IS THE HIGHEST-LEVERAGE INSTRUMENT AVAILABLE.
    ///
    /// BasisArmSolveResult publishes HumeralTwistDeg, HumeralTwistGuardDeg, WristTwistDeg, WristReliefDeg
    /// and ForearmRollDeg. Until this file, NOT ONE of them was recorded anywhere -- no plausibility
    /// channel, no CSV, no sink. The consequences are on the record: three investigations aimed at the
    /// WRONG JOINT before a test caught it, one agent that had to invalidate two of its own three probes,
    /// and a shipped guard whose "0.000 mm" safety claim came from a gate structurally incapable of failing.
    ///
    /// ⚠️ AND THE CORPUS CANNOT SUBSTITUTE FOR IT. Two things a user capture can settle that CMU cannot:
    ///   * CLAVICLE MOTION. CMU carries none at all -- the RightShoulder channel's range is 0/0/0 in every
    ///     clip -- so the clavicle->humerus junction has literally no reference there.
    ///   * AXIAL ROLL NEAR FULL EXTENSION. The corpus's own solver hits the same singularity ours does; a
    ///     band read p95 148.7 deg. It cannot referee the region where the user actually lives.
    /// And the flat HumeralTwistSoftDeg = 120 / HardDeg = 150 is a WHOLE-COMPLEX bound chosen because this
    /// rig's girdle does not move. Clinical glenohumeral arc is position-dependent (~48/71/40 deg at
    /// 45/90/135 abduction) -- but a corpus-derived elevation spec was REFUTED by measurement this week
    /// (+/-81 deg at 45 would clip 49 deg inside the fat of the most populated band, and occupied arc falls
    /// monotonically 211/183/162/97 rather than peaking at 90). Real user data is the only referee left.
    /// ================================================================================================
    ///
    /// HOUSE SHAPE, deliberately: this is <see cref="BasisLegSwivelDebug"/>'s arm sibling. Rows are
    /// <see cref="BasisArmDiagnostics"/> verbatim -- its own Header/ToRow, never a second copy of the field
    /// list, so the two cannot drift. The gate is read ONCE at <see cref="Begin"/> and every Record after
    /// that is a single bool test, exactly as <see cref="BasisCalibrationDebugRecorder"/> and
    /// <see cref="BasisDeviceStreamRecorder"/> do, so the instrumentation is free to leave wired in.
    ///
    /// ⚠️ IT DOES NOT ACCUMULATE ROWS IN MEMORY. BasisLegSwivelDebug buffers into a StringBuilder and caps
    /// at 16000 rows, which drops the END of a long session -- the wrong half, because the artifact you are
    /// chasing is the reason you are still recording. This streams: a fixed StringBuilder is flushed to an
    /// open StreamWriter every <see cref="FlushEveryRows"/> rows and cleared, so the resident cost is O(1)
    /// in session length and nothing is lost.
    ///
    /// AND IT DOES THE SEGMENTATION ONLINE, IN FIXED MEMORY. Occupied-arc percentiles per elevation x
    /// extension cell come out of integer histograms sized at Begin, so the answer to "which joint leaves
    /// human range, in which band, and by how much" is available in-headset at StopAndDump without opening
    /// the CSV at all. The CSV is for the frame the number goes bad.
    /// </summary>
    public static class BasisArmSolveRecorder
    {
        /// <summary>
        /// Developer gate. FALSE BY DEFAULT and must stay that way -- this writes a few MB per minute.
        ///
        /// A plain static rather than a settings binding for the same reason
        /// <see cref="BasisDeviceStreamRecorder.DeveloperEnabled"/> is: adding one means editing
        /// BasisSettingsDefaults, which this change does not touch. The intended wiring is a sibling of
        /// DumpCalibrationCsv:
        ///     public static BasisSettingsBinding&lt;bool&gt; DumpArmSolveCsv =
        ///         new("devdumparmsolvecsv", new BasisPlatformDefault&lt;bool&gt;(false));
        /// read here in <see cref="Begin"/> the way DumpCalibrationCsv is read in
        /// BasisCalibrationDebugRecorder.Begin. Until then a developer sets this from the console or a
        /// debug menu before calling Begin.
        /// </summary>
        public static bool DeveloperEnabled;

        /// <summary>
        /// THE SINGLE BOOL. This is the only thing the IK path tests when recording is off, and it is why
        /// the instrumentation costs nothing to leave in: no allocation, no NativeArray read, no capture,
        /// no branch beyond this one -- and it is a static bool that stays in cache and predicts perfectly
        /// because it does not change within a session.
        /// </summary>
        public static bool Active { get; private set; }

        public static string OutputDirectory => Path.Combine(Application.persistentDataPath, "ArmSolve");

        /// <summary>Rows buffered before the writer is flushed. Bounds the resident StringBuilder.</summary>
        public const int FlushEveryRows = 256;

        /// <summary>
        /// Hard ceiling on captured FRAMES (not rows -- each frame writes both arms). At 90 Hz this is a
        /// little over 60 minutes. A recorder left running cannot fill a disk unnoticed.
        /// </summary>
        public const int MaxFrames = 330000;

        public const int ArmLeft = 0;
        public const int ArmRight = 1;
        const int k_Arms = 2;

        // Histogram resolution: 1 degree over 0..180. The quantities are principal angles read as
        // magnitudes, so 181 buckets covers them exactly with no clamping at either end.
        const int k_Buckets = 181;
        const int k_QuantHumeralTwist = 0;
        const int k_QuantChainAxial = 1;
        const int k_Quantities = 2;

        const int k_GuardTwist = 0;
        const int k_GuardForearmRoll = 1;
        const int k_GuardRelief = 2;
        const int k_GuardElbow = 3;
        const int k_Guards = 4;

        /// <summary>
        /// The envelope <see cref="BasisArmDiagnostics.ChainAxialDeg"/> is FLAGGED against -- flagged,
        /// never gated, and never used to change the solve.
        ///
        /// Anatomical forearm pronation/supination is ~80 deg each way from the thumbs-up neutral, which is
        /// already this codebase's own number (BasisArmSolveCore.WristRollComfortDeg = 80). The radiocarpal
        /// joint has essentially NO independent axial degree of freedom, so the chain total past the
        /// humerus should not meaningfully exceed the forearm's own arc. 90 is that 80 plus a 10 deg
        /// allowance for an arbitrary rig's bind neutral not sitting exactly at anatomical neutral.
        ///
        /// ⚠️ IT IS A REPORTING THRESHOLD AND NOTHING ELSE. The whole point of a user capture is that we do
        /// not currently know what this number should be; a breach count is a QUESTION raised, not a defect
        /// proven. Read it next to the occupied-arc percentiles for the same cell.
        /// </summary>
        public const float ChainAxialEnvelopeDeg = 90f;

        static readonly StringBuilder _sb = new StringBuilder(FlushEveryRows * 260);
        static StreamWriter _writer;
        static string _path = string.Empty;
        static string _sessionLabel = string.Empty;

        static int _frame;
        static int _rowsWritten;

        // All fixed-size, all allocated once at Begin. Nothing here grows with session length.
        static int[] _hist;          // [arm, cell, quantity, bucket]
        static int[] _cellFrames;    // [arm, cell]
        static int[] _cellBreach;    // [arm, cell]  frames past ChainAxialEnvelopeDeg
        static int[] _guardFired;    // [arm, cell, guard]
        static int[] _guardAvail;    // [arm, cell, guard]
        static int[] _armFrames;
        static int[] _armUncelled;   // band declined (non-finite or out-of-range elevation / reach)
        static int[] _armNonFinite;
        static int[] _armTorsoInvalid;

        /// <summary>Path of the CSV currently being written, or the last one written. Empty until a Begin.</summary>
        public static string LastWrittenPath => _path;

        /// <summary>Frames captured so far in the active session.</summary>
        public static int CapturedFrames => _frame;

        /// <summary>Rows handed to the writer so far. Two per captured frame when both arms are solved.</summary>
        public static int RowsWritten => _rowsWritten;

        /// <summary>
        /// Characters currently held in the buffer. The bounded-memory proof reads this: it must stay under
        /// one flush window's worth no matter how many frames have gone through, which is the difference
        /// between streaming and the accumulate-then-cap shape.
        /// </summary>
        public static int BufferedChars => _sb.Length;

        /// <summary>
        /// Starts a capture. Reads the developer gate; when off this and every later Record is a no-op
        /// until the next Begin. Opens the CSV immediately so a session that ends in a crash still leaves
        /// everything flushed up to the last window.
        /// </summary>
        public static void Begin(string sessionLabel)
        {
            Stop();

            Active = DeveloperEnabled;
            if (!Active)
            {
                return;
            }

            _sessionLabel = string.IsNullOrEmpty(sessionLabel) ? "session" : sessionLabel;
            _frame = 0;
            _rowsWritten = 0;
            _sb.Clear();

            _hist = new int[k_Arms * BasisArmDiagnosticsCore.CellCount * k_Quantities * k_Buckets];
            _cellFrames = new int[k_Arms * BasisArmDiagnosticsCore.CellCount];
            _cellBreach = new int[k_Arms * BasisArmDiagnosticsCore.CellCount];
            _guardFired = new int[k_Arms * BasisArmDiagnosticsCore.CellCount * k_Guards];
            _guardAvail = new int[k_Arms * BasisArmDiagnosticsCore.CellCount * k_Guards];
            _armFrames = new int[k_Arms];
            _armUncelled = new int[k_Arms];
            _armNonFinite = new int[k_Arms];
            _armTorsoInvalid = new int[k_Arms];

            try
            {
                Directory.CreateDirectory(OutputDirectory);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                _path = Path.Combine(OutputDirectory, $"armsolve_{Sanitize(_sessionLabel)}_{stamp}.csv");
                _writer = new StreamWriter(_path, false, Encoding.UTF8);
                _writer.Write(BasisArmDiagnostics.Header);
                _writer.Write('\n');
            }
            catch (Exception e)
            {
                BasisDebug.LogError($"[ArmSolve] could not open '{_path}': {e}");
                Stop();
                return;
            }

            BasisDebug.Log(
                $"[ArmSolve] capturing '{_sessionLabel}' -> {_path}. Move BOTH arms through their full range: " +
                "overhead, behind the back, across the chest, at full stretch, and rolled palm-up and " +
                "palm-down at each. The bands you do not visit are the bands this capture cannot referee.",
                BasisDebug.LogTag.IK);
        }

        /// <summary>
        /// Records one arm's frame. Called once per arm per frame from the rig driver, AFTER IKJob.Run() --
        /// the diagnostics are written inside the job, so reading them before Run() reads last frame.
        /// Single bool test when inactive.
        /// </summary>
        public static void Record(int arm, float timeSeconds, in BasisArmDiagnostics diagnostics)
        {
            if (!Active || _writer == null || (uint)arm >= (uint)k_Arms)
            {
                return;
            }

            BasisArmDiagnostics d = diagnostics;
            d.FrameIndex = _frame;
            d.TimeSeconds = timeSeconds;

            _armFrames[arm]++;
            if (d.NonFinite != 0f) _armNonFinite[arm]++;
            if (d.TorsoUpValid == 0f) _armTorsoInvalid[arm]++;

            // ⚠️ A NON-FINITE FRAME IS NOT EVIDENCE ABOUT ANY BAND. It is counted and then excluded from
            // every percentile, rather than being filed into whichever bucket a NaN comparison happens to
            // fall through into. Same for a frame whose elevation or reach lands outside the banded range:
            // the band function returns -1 for both, on purpose, so this is one test and not three.
            int cell = d.NonFinite != 0f ? -1 : BasisArmDiagnosticsCore.CellOf(in d);
            if (cell < 0)
            {
                _armUncelled[arm]++;
            }
            else
            {
                int cellBase = arm * BasisArmDiagnosticsCore.CellCount + cell;
                _cellFrames[cellBase]++;

                Bucket(arm, cell, k_QuantHumeralTwist, d.HumeralTwistDeg);
                Bucket(arm, cell, k_QuantChainAxial, d.ChainAxialDeg);

                if (Abs(d.ChainAxialDeg) > ChainAxialEnvelopeDeg)
                {
                    _cellBreach[cellBase]++;
                }

                // ⭐ THE DENOMINATOR OF A FIRING RATE IS THE FRAMES THE GUARD COULD HAVE FIRED, NOT ALL
                // FRAMES. A guard that is structurally declined for a whole session divides into zero, and
                // a rate computed over all frames would report it as a beautifully quiet 0.0% instead of as
                // the absence it is. This is the 0-of-22032 defect turned into arithmetic.
                Guard(arm, cell, k_GuardTwist, d.TwistGuardAvailable, d.TwistGuardFired);
                Guard(arm, cell, k_GuardForearmRoll,
                    (d.TrackerRollAvailable != 0f || d.NoTrackerRollAvailable != 0f) ? 1f : 0f, d.ForearmRollFired);
                Guard(arm, cell, k_GuardRelief, d.TipAvailable, d.WristReliefFired);
                Guard(arm, cell, k_GuardElbow, d.HintWeight, d.ElbowGuardFired);
            }

            _sb.Append(d.ToRow(arm == ArmLeft ? "L" : "R")).Append('\n');
            _rowsWritten++;

            if (_rowsWritten % FlushEveryRows == 0)
            {
                FlushBuffer();
            }
        }

        /// <summary>Closes the frame. Advances the counter and auto-stops at <see cref="MaxFrames"/>.</summary>
        public static void EndFrame()
        {
            if (!Active)
            {
                return;
            }
            _frame++;
            if (_frame >= MaxFrames)
            {
                BasisDebug.LogWarning($"[ArmSolve] hit the {MaxFrames} frame ceiling; dumping.", BasisDebug.LogTag.IK);
                StopAndDump();
            }
        }

        /// <summary>Flushes, closes and prints the segmented summary. Safe to call when never started.</summary>
        public static void StopAndDump()
        {
            if (!Active)
            {
                BasisDebug.LogWarning("[ArmSolve] never started -- set DeveloperEnabled and call Begin first.", BasisDebug.LogTag.IK);
                return;
            }

            string summary = Summary();
            string path = _path;
            int rows = _rowsWritten;

            FlushBuffer();
            Stop();

            BasisDebug.Log($"[ArmSolve] {rows} rows -> {path}\n{summary}", BasisDebug.LogTag.IK);
        }

        /// <summary>
        /// Ends the session and releases everything without printing. Safe at any time.
        ///
        /// ⚠️ IT CLEARS THE STATISTICS AS WELL AS THE WRITER, AND THAT IS NOT TIDINESS. A stopped recorder
        /// that still answers CapturedFrames, RowsWritten and Summary() from the LAST session reports stale
        /// numbers as current ones -- and a stale segmented table is the single most misleading artifact
        /// this instrument could produce, because it is indistinguishable from a fresh one. Caught by
        /// BasisArmSolveRecorderTests: a gated-off Begin reported 300 rows it had not written, and an empty
        /// session summarised as the previous 4000-frame capture.
        ///
        /// <see cref="StopAndDump"/> takes its summary BEFORE calling this, so nothing is lost. LastWrittenPath
        /// deliberately survives: it names the file on disk, which outlives the session by design.
        /// </summary>
        public static void Stop()
        {
            if (_writer != null)
            {
                try
                {
                    if (_sb.Length > 0)
                    {
                        _writer.Write(_sb.ToString());
                    }
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch (Exception e)
                {
                    BasisDebug.LogError($"[ArmSolve] flush/close failed: {e}");
                }
                _writer = null;
            }
            _sb.Clear();
            Active = false;

            _frame = 0;
            _rowsWritten = 0;
            _hist = null;
            _cellFrames = null;
            _cellBreach = null;
            _guardFired = null;
            _guardAvail = null;
            _armFrames = null;
            _armUncelled = null;
            _armNonFinite = null;
            _armTorsoInvalid = null;
        }

        static void FlushBuffer()
        {
            if (_writer == null || _sb.Length == 0)
            {
                return;
            }
            try
            {
                _writer.Write(_sb.ToString());
                _writer.Flush();
            }
            catch (Exception e)
            {
                BasisDebug.LogError($"[ArmSolve] write failed, stopping: {e}");
                Stop();
                return;
            }
            _sb.Clear();
        }

        static float Abs(float v) => v < 0f ? -v : v;

        static void Bucket(int arm, int cell, int quantity, float signedDeg)
        {
            float mag = Abs(signedDeg);
            if (!(mag >= 0f))
            {
                return;   // reject-unless-good: a NaN that survived the NonFinite screen lands here, not in a bucket
            }
            int b = (int)(mag + 0.5f);
            if (b < 0) b = 0;
            if (b >= k_Buckets) b = k_Buckets - 1;
            _hist[HistIndex(arm, cell, quantity, b)]++;
        }

        static void Guard(int arm, int cell, int guard, float available, float fired)
        {
            int idx = (arm * BasisArmDiagnosticsCore.CellCount + cell) * k_Guards + guard;
            if (available != 0f) _guardAvail[idx]++;
            if (fired != 0f) _guardFired[idx]++;
        }

        static int HistIndex(int arm, int cell, int quantity, int bucket) =>
            ((arm * BasisArmDiagnosticsCore.CellCount + cell) * k_Quantities + quantity) * k_Buckets + bucket;

        /// <summary>
        /// The occupied arc at percentile <paramref name="f"/> for one quantity in one cell, read straight
        /// out of the histogram. Returns NaN when the cell has no samples -- an EMPTY CELL IS NOT A ZERO,
        /// and reporting it as one is how "this band is fine" gets said about a band nobody visited.
        /// </summary>
        public static float Percentile(int arm, int cell, int quantity, float f)
        {
            if (_hist == null || (uint)arm >= (uint)k_Arms || (uint)cell >= (uint)BasisArmDiagnosticsCore.CellCount)
            {
                return float.NaN;
            }
            int total = _cellFrames[arm * BasisArmDiagnosticsCore.CellCount + cell];
            if (total <= 0)
            {
                return float.NaN;
            }

            int want = Mathf.Clamp(Mathf.RoundToInt(f * (total - 1)), 0, total - 1);
            int seen = 0;
            for (int b = 0; b < k_Buckets; b++)
            {
                seen += _hist[HistIndex(arm, cell, quantity, b)];
                if (seen > want)
                {
                    return b;
                }
            }
            return k_Buckets - 1;
        }

        public static int CellFrames(int arm, int cell)
        {
            if (_cellFrames == null || (uint)arm >= (uint)k_Arms || (uint)cell >= (uint)BasisArmDiagnosticsCore.CellCount)
            {
                return 0;
            }
            return _cellFrames[arm * BasisArmDiagnosticsCore.CellCount + cell];
        }

        /// <summary>
        /// ⭐ THE SEGMENTED REPORT. NEVER POOLED, AND THE EMPTY CELLS ARE PRINTED AS EMPTY.
        ///
        /// A cell the user never visited cannot referee anything, and the single most useful line in this
        /// report is the list of cells with too few frames to speak: that is the honest boundary of what
        /// the capture proves. Pooling across cells is what hid a band-specific breach twice this week.
        /// </summary>
        public static string Summary()
        {
            var sb = new StringBuilder(4096);
            if (_cellFrames == null)
            {
                return "[ArmSolve] no data.";
            }

            sb.Append("  ARM SOLVE CAPTURE -- segmented by elevation x extension, NEVER pooled.\n");
            sb.Append("  elevation: 0=arm at side, 90=horizontal, 180=overhead. extension: reach/armLength.\n\n");

            for (int arm = 0; arm < k_Arms; arm++)
            {
                if (_armFrames[arm] == 0)
                {
                    sb.Append(arm == ArmLeft ? "  LEFT" : "  RIGHT").Append(": no frames -- that arm's IK is off or its weight is 0.\n");
                    continue;
                }

                sb.Append(arm == ArmLeft ? "  LEFT ARM" : "  RIGHT ARM")
                  .Append("  frames ").Append(_armFrames[arm])
                  .Append(", unbanded ").Append(_armUncelled[arm])
                  .Append(", non-finite ").Append(_armNonFinite[arm])
                  .Append(", torso frame UNAVAILABLE on ").Append(_armTorsoInvalid[arm])
                  .Append(" (those rows' elevation/plane are measured against the ROOT's up and must be excluded)\n");

                sb.Append("    elev      ext        n   |humTwist| p50/p95/max   |chain| p50/p95/max   past").Append(ChainAxialEnvelopeDeg.ToString("F0")).Append("   twistGuard  elbowGuard\n");

                for (int e = 0; e < BasisArmDiagnosticsCore.ElevationBands; e++)
                {
                    for (int x = 0; x < BasisArmDiagnosticsCore.ExtensionBands; x++)
                    {
                        int cell = BasisArmDiagnosticsCore.Cell(e, x);
                        int n = CellFrames(arm, cell);
                        sb.Append("    ").Append(ElevLabel(e).PadRight(10)).Append(ExtLabel(x).PadRight(11));
                        if (n == 0)
                        {
                            sb.Append("        -   (not visited -- this capture CANNOT referee this band)\n");
                            continue;
                        }

                        int cellBase = arm * BasisArmDiagnosticsCore.CellCount + cell;
                        sb.Append(n.ToString().PadLeft(7)).Append("   ");
                        Trip(sb, arm, cell, k_QuantHumeralTwist);
                        sb.Append("   ");
                        Trip(sb, arm, cell, k_QuantChainAxial);
                        sb.Append("   ").Append(Pct(_cellBreach[cellBase], n).PadLeft(6));
                        sb.Append("   ").Append(Rate(arm, cell, k_GuardTwist).PadLeft(10));
                        sb.Append("  ").Append(Rate(arm, cell, k_GuardElbow).PadLeft(10));
                        sb.Append('\n');
                    }
                }
                sb.Append('\n');
            }

            sb.Append("  Guard columns are FIRED / AVAILABLE, not fired / all frames: a guard that was\n");
            sb.Append("  structurally declined all session reads 'n/a' here rather than a quiet 0.0%.\n");
            sb.Append("  A cell marked 'not visited' is the honest edge of what this capture proves. The\n");
            sb.Append("  flat 120/150 humeral envelope can only be re-argued from cells with real frames in\n");
            sb.Append("  them; everywhere else the capture is silent and saying otherwise is invention.\n");
            return sb.ToString();
        }

        static void Trip(StringBuilder sb, int arm, int cell, int quantity)
        {
            float p50 = Percentile(arm, cell, quantity, 0.50f);
            float p95 = Percentile(arm, cell, quantity, 0.95f);
            float max = Percentile(arm, cell, quantity, 1.00f);
            sb.Append(p50.ToString("F0").PadLeft(5)).Append('/')
              .Append(p95.ToString("F0").PadLeft(4)).Append('/')
              .Append(max.ToString("F0").PadLeft(4));
        }

        static string Rate(int arm, int cell, int guard)
        {
            int idx = (arm * BasisArmDiagnosticsCore.CellCount + cell) * k_Guards + guard;
            int avail = _guardAvail[idx];
            if (avail == 0)
            {
                return "n/a";
            }
            return Pct(_guardFired[idx], avail);
        }

        static string Pct(int num, int den) =>
            den <= 0 ? "n/a" : (100f * num / den).ToString("F1", CultureInfo.InvariantCulture) + "%";

        static string ElevLabel(int band) => $"{band * 30}-{(band + 1) * 30}";

        static string ExtLabel(int band)
        {
            switch (band)
            {
                case 0: return "<0.70";
                case 1: return "0.70-0.85";
                case 2: return "0.85-0.95";
                case 3: return "0.95-0.99";
                default: return ">=0.99";
            }
        }

        static string Sanitize(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
