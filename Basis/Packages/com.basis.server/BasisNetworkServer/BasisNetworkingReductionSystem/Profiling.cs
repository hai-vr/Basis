using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    /// <summary>
    /// One closed profiling window. Raw window totals only; derived figures are left to the caller
    /// so every consumer computes them the same way.
    /// </summary>
    public sealed class BSRProfilerSnapshot
    {
        public DateTimeOffset CapturedUtc;
        public long Ticks;
        public long Messages;
        public long Sends;
        public long PreSerializations;
        public long PreSerializationsSkipped;
        public double DrainMs;
        public double ProcessMs;
        public double DistanceMs;
        public double UpdateMs;
        public double TriggerMs;
        public double TotalMs;
        public long BundlesEmitted;
        public long BundleMessages;
        public long BundleRawBytes;
        public long BundleCompressedBytes;
        public double BundleDeflateMs;
        public long BundleRetries;
        public long BundleFallbacks;
        public long BundleTailUncompressed;
    }

    /// <summary>
    /// One worker thread's counter block. The send loop is a Parallel.For over receivers, so
    /// every counter below used to be an Interlocked op contended by every core at once —
    /// bundleFallbacks alone ran ~130K/s at 500 players. Each thread now accumulates into its
    /// own block and <see cref="BSRProfiler.TryPrint"/> sums the registered blocks once per
    /// window, so the hot path is a plain non-atomic increment.
    ///
    /// Padded to keep two threads' blocks off the same cache line; without it the counters are
    /// contended by false sharing even though nothing is shared logically.
    /// </summary>
    public sealed class BSRThreadCounters
    {
#pragma warning disable CS0169 // padding fields are never read by design
        private long _padHead0, _padHead1, _padHead2, _padHead3, _padHead4, _padHead5, _padHead6, _padHead7;
#pragma warning restore CS0169

        public long Sends;
        public long PreSerializations;
        public long PreSerializationsSkipped;
        public long BundlesEmitted;
        public long BundleMessages;
        public long BundleRawBytes;
        public long BundleCompressedBytes;
        public long BundleDeflateTicks;
        public long BundleRetries;
        public long BundleFallbacks;
        public long BundleTailUncompressed;

#pragma warning disable CS0169
        private long _padTail0, _padTail1, _padTail2, _padTail3, _padTail4, _padTail5, _padTail6, _padTail7;
#pragma warning restore CS0169
    }

    /// <summary>
    /// Lock-free, low-overhead profiler for the BSR tick loop.
    /// Disabled by default — enable via EnableBSRProfiling in config.xml or env var.
    /// When disabled, all methods are no-ops (volatile bool check, no branches taken).
    /// Collects a window every 5 seconds, publishes it to <see cref="Latest"/>, and resets counters.
    /// </summary>
    public static class BSRProfiler
    {
        public static volatile bool Enabled;

        [ThreadStatic] private static BSRThreadCounters _threadCounters;

        // Every block handed out, so a window can sum them. Held weakly: the send loop runs on
        // ThreadPool workers, and the pool retires idle threads and mints new ones as load
        // oscillates — with strong references this list grew by one padded block per thread the
        // process ever saw and never shrank. The thread-static keeps a live thread's block
        // strongly rooted; a dead thread's block is collected and its entry pruned on the next
        // window drain. Counts a dying thread had not yet drained are lost with it — diagnostics,
        // not accounting.
        private static readonly List<WeakReference<BSRThreadCounters>> _allCounters = new();
        private static readonly object _countersLock = new();

        /// <summary>
        /// This thread's counter block, created on first use. Hoist it into a local once per
        /// receiver rather than calling this per counter — the thread-static read is cheap but
        /// not free.
        /// </summary>
        public static BSRThreadCounters Local
        {
            get
            {
                var c = _threadCounters;
                if (c == null)
                {
                    c = new BSRThreadCounters();
                    _threadCounters = c;
                    lock (_countersLock) _allCounters.Add(new WeakReference<BSRThreadCounters>(c));
                }
                return c;
            }
        }

        /// <summary>
        /// Drains every thread block into the static totals. Called under the window close, which
        /// is the only reader. A worker mid-increment just lands in the next window — these are
        /// diagnostics, and paying for exactness here would reintroduce the contention this exists
        /// to remove.
        /// </summary>
        private static void DrainThreadCounters()
        {
            lock (_countersLock)
            {
                for (int i = _allCounters.Count - 1; i >= 0; i--)
                {
                    if (!_allCounters[i].TryGetTarget(out var c))
                    {
                        _allCounters.RemoveAt(i);
                        continue;
                    }
                    SendCount += Interlocked.Exchange(ref c.Sends, 0);
                    _preSerializations += Interlocked.Exchange(ref c.PreSerializations, 0);
                    _preSerializationsSkipped += Interlocked.Exchange(ref c.PreSerializationsSkipped, 0);
                    bundlesEmitted += Interlocked.Exchange(ref c.BundlesEmitted, 0);
                    bundleMessages += Interlocked.Exchange(ref c.BundleMessages, 0);
                    bundleRawBytes += Interlocked.Exchange(ref c.BundleRawBytes, 0);
                    bundleCompressedBytes += Interlocked.Exchange(ref c.BundleCompressedBytes, 0);
                    bundleDeflateTicks += Interlocked.Exchange(ref c.BundleDeflateTicks, 0);
                    bundleRetries += Interlocked.Exchange(ref c.BundleRetries, 0);
                    bundleFallbacks += Interlocked.Exchange(ref c.BundleFallbacks, 0);
                    bundleTailUncompressed += Interlocked.Exchange(ref c.BundleTailUncompressed, 0);
                }
            }
        }

        /// <summary>Whether a closed window is written to the log. Collection is driven by <see cref="Enabled"/> alone.</summary>
        public static volatile bool WriteToLog;

        private static volatile BSRProfilerSnapshot _latest;

        /// <summary>Most recently closed window, or null until the first one completes.</summary>
        public static BSRProfilerSnapshot Latest => _latest;

        private static readonly double MsToTick = Stopwatch.Frequency / 1000.0;
        private static readonly long PrintIntervalTicks = (long)(5000 * MsToTick);
        private static long _lastPrintTick = Stopwatch.GetTimestamp();

        // Phase timings (accumulated ticks, reset each print interval)
        public static long drainTicks;
        public static long processTicks;
        public static long distanceTicks;
        public static long updateTicks;
        public static long triggerTicks;

        // Counters (reset each print interval)
        public static long tickCount;
        public static long messagesProcessed;
        // Public so thread-local counters can aggregate via Interlocked.Add after Parallel.For
        public static long SendCount;
        private static long _preSerializations;
        private static long _preSerializationsSkipped;

        // Compressed-avatar-bundle metrics. Public so the reduction system can Interlocked.Add
        // from the parallel send loop. All only touched when Enabled is true.
        public static long bundlesEmitted;
        public static long bundleMessages;
        public static long bundleRawBytes;
        public static long bundleCompressedBytes;
        public static long bundleDeflateTicks;
        public static long bundleRetries;
        public static long bundleFallbacks;
        public static long bundleTailUncompressed;

        public static void IncrementPreSerializations()
        {
            if (!Enabled) return;
            Local.PreSerializations++;
        }

        public static void IncrementPreSerializationsSkipped()
        {
            if (!Enabled) return;
            Local.PreSerializationsSkipped++;
        }

        /// <summary>Test seam (InternalsVisibleTo): closes the current window immediately instead of waiting out the interval.</summary>
        internal static void FlushWindowForTests()
        {
            Volatile.Write(ref _lastPrintTick, Stopwatch.GetTimestamp() - PrintIntervalTicks - 1);
            TryPrint();
        }

        /// <summary>Test seam (InternalsVisibleTo): clears every counter, the published window, and both flags.</summary>
        internal static void ResetForTests()
        {
            Enabled = false;
            WriteToLog = false;
            _latest = null;
            lock (_countersLock)
            {
                for (int i = _allCounters.Count - 1; i >= 0; i--)
                {
                    if (!_allCounters[i].TryGetTarget(out var c))
                    {
                        _allCounters.RemoveAt(i);
                        continue;
                    }
                    c.Sends = 0;
                    c.PreSerializations = 0;
                    c.PreSerializationsSkipped = 0;
                    c.BundlesEmitted = 0;
                    c.BundleMessages = 0;
                    c.BundleRawBytes = 0;
                    c.BundleCompressedBytes = 0;
                    c.BundleDeflateTicks = 0;
                    c.BundleRetries = 0;
                    c.BundleFallbacks = 0;
                    c.BundleTailUncompressed = 0;
                }
            }
            tickCount = 0;
            messagesProcessed = 0;
            SendCount = 0;
            _preSerializations = 0;
            _preSerializationsSkipped = 0;
            drainTicks = 0;
            processTicks = 0;
            distanceTicks = 0;
            updateTicks = 0;
            triggerTicks = 0;
            bundlesEmitted = 0;
            bundleMessages = 0;
            bundleRawBytes = 0;
            bundleCompressedBytes = 0;
            bundleDeflateTicks = 0;
            bundleRetries = 0;
            bundleFallbacks = 0;
            bundleTailUncompressed = 0;
        }

        public static void TryPrint()
        {
            if (!Enabled) return;

            long now = Stopwatch.GetTimestamp();
            if (now - Volatile.Read(ref _lastPrintTick) < PrintIntervalTicks) return;
            Volatile.Write(ref _lastPrintTick, now);

            // Fold per-thread blocks in before anything below reads the totals.
            DrainThreadCounters();

            long ticks = Interlocked.Exchange(ref tickCount, 0);
            if (ticks == 0) return;

            long msgs = Interlocked.Exchange(ref messagesProcessed, 0);
            long sends = Interlocked.Exchange(ref SendCount, 0);
            long preSer = Interlocked.Exchange(ref _preSerializations, 0);
            long preSkip = Interlocked.Exchange(ref _preSerializationsSkipped, 0);

            double drain = Interlocked.Exchange(ref drainTicks, 0) / MsToTick;
            double process = Interlocked.Exchange(ref processTicks, 0) / MsToTick;
            double distance = Interlocked.Exchange(ref distanceTicks, 0) / MsToTick;
            double update = Interlocked.Exchange(ref updateTicks, 0) / MsToTick;
            double trigger = Interlocked.Exchange(ref triggerTicks, 0) / MsToTick;

            double total = drain + process + distance + update + trigger;

            // Bundle metrics — exchange even if zero so a flag flip is reflected immediately
            long bEmit = Interlocked.Exchange(ref bundlesEmitted, 0);
            long bMsg = Interlocked.Exchange(ref bundleMessages, 0);
            long bRaw = Interlocked.Exchange(ref bundleRawBytes, 0);
            long bComp = Interlocked.Exchange(ref bundleCompressedBytes, 0);
            long bDeflate = Interlocked.Exchange(ref bundleDeflateTicks, 0);
            long bRetry = Interlocked.Exchange(ref bundleRetries, 0);
            long bFallback = Interlocked.Exchange(ref bundleFallbacks, 0);
            long bTail = Interlocked.Exchange(ref bundleTailUncompressed, 0);

            _latest = new BSRProfilerSnapshot
            {
                CapturedUtc = DateTimeOffset.UtcNow,
                Ticks = ticks,
                Messages = msgs,
                Sends = sends,
                PreSerializations = preSer,
                PreSerializationsSkipped = preSkip,
                DrainMs = drain,
                ProcessMs = process,
                DistanceMs = distance,
                UpdateMs = update,
                TriggerMs = trigger,
                TotalMs = total,
                BundlesEmitted = bEmit,
                BundleMessages = bMsg,
                BundleRawBytes = bRaw,
                BundleCompressedBytes = bComp,
                BundleDeflateMs = bDeflate / MsToTick,
                BundleRetries = bRetry,
                BundleFallbacks = bFallback,
                BundleTailUncompressed = bTail,
            };

            if (!WriteToLog) return;

            // One line, not eleven. A window carries the same 26 figures it always did, but every
            // BNL.Log costs a console lock, a DateTime.Now, an open/write/close on the log file and
            // a fistful of console attribute syscalls — the cost of reporting a window was set by
            // the number of calls, not by anything being measured. Percentages are dropped to whole
            // numbers where the ms/tick figure they derive from sits beside them.
            double pctOfTotal = total > 0 ? 100.0 / total : 0;

            StringBuilder line = new StringBuilder(288);
            AppendInv(line, $"[BSR] {ticks}t {total / ticks:F3}ms/t");
            AppendPhase(line, "drain", drain, ticks, pctOfTotal);
            AppendPhase(line, "proc", process, ticks, pctOfTotal);
            AppendPhase(line, "dist", distance, ticks, pctOfTotal);
            AppendPhase(line, "upd", update, ticks, pctOfTotal);
            AppendPhase(line, "trig", trigger, ticks, pctOfTotal);
            AppendInv(line, $" | {msgs}msg {sends}send preser {preSer}/{preSer + preSkip}");

            if (bEmit > 0 || bTail > 0 || bFallback > 0)
            {
                double ratio = bRaw > 0 ? (double)bComp / bRaw : 0;
                double savedPct = bRaw > 0 ? (ratio - 1) * 100.0 : 0; // negative: bundled bytes shrank
                double deflateMs = bDeflate / MsToTick;
                double perBundle = bEmit > 0 ? 1.0 / bEmit : 0;       // 0 collapses every per-bundle average to 0

                AppendInv(line, $" | bundles {bEmit} {bEmit / (double)ticks:F2}/t {bMsg}msg {bTail}tail {bFallback}fb");
                AppendInv(line, $" {bMsg * perBundle:F1}msg/b {bRaw * perBundle:F0}→{bComp * perBundle:F0}B {ratio:F3} {savedPct:F1}% {(bRaw - bComp) / 1024.0:F1}KB");
                AppendInv(line, $" deflate {deflateMs / ticks:F3}ms/t {deflateMs * pctOfTotal:F1}% {deflateMs * 1000.0 * perBundle:F1}µs/b retry {bRetry} {bRetry * perBundle * 100.0:F1}%");
            }

            BNL.Log(line.ToString());
        }

        /// <summary>
        /// Invariant culture so a comma-decimal host still emits numbers a log scraper can read —
        /// the same reason the health endpoint pins it.
        /// </summary>
        private static void AppendInv(StringBuilder sb, FormattableString text) =>
            sb.Append(FormattableString.Invariant(text));

        private static void AppendPhase(StringBuilder sb, string name, double ms, long ticks, double pctOfTotal) =>
            AppendInv(sb, $" {name} {ms / ticks:F3} {ms * pctOfTotal:F0}%");
    }
}
