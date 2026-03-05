using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public static class Profiling
    {
        // ConcurrentBag per key so concurrent Add calls from parallel tasks are safe.
        public static readonly ConcurrentDictionary<string, ConcurrentBag<long>> Timings = new();
        private static long _lastPrintTicks = Stopwatch.GetTimestamp();
        private static readonly double MsToTick = Stopwatch.Frequency / 1000.0;
        private static readonly long PrintIntervalTicks = (long)(5000 * MsToTick);

        public static void StartTimer(string key, out long startTicks)
        {
            startTicks = Stopwatch.GetTimestamp();
        }

        public static void EndTimer(string key, long startTicks)
        {
            long duration = Stopwatch.GetTimestamp() - startTicks;
            Timings.GetOrAdd(key, _ => new ConcurrentBag<long>()).Add(duration);
        }

        public static void TryPrint()
        {
            long now = Stopwatch.GetTimestamp();
            if (now - Interlocked.Read(ref _lastPrintTicks) < PrintIntervalTicks) return;
            Interlocked.Exchange(ref _lastPrintTicks, now);

            BNL.Log("\n[BSR Profiling Summary]");
            var drain = new List<long>(1024);
            foreach (var kvp in Timings)
            {
                drain.Clear();
                while (kvp.Value.TryTake(out long t)) drain.Add(t);
                if (drain.Count == 0) continue;

                double sum = 0;
                for (int i = 0; i < drain.Count; i++) sum += drain[i];
                double avgMs = sum / drain.Count / MsToTick;
                BNL.Log($"{kvp.Key}: {avgMs:F3} ms over {drain.Count} runs");
            }
        }
    }
}
