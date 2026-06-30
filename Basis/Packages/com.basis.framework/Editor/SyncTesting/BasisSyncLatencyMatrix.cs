using System;
using System.Collections.Generic;
using System.Text;

namespace Basis.Scripts.Networking.Sync.Testing
{
    /// <summary>
    /// Diagnostic sweeps for <see cref="BasisSyncLatency"/>. Each group isolates one latency lever so the
    /// CSV makes the contributor obvious:
    ///   - send-rate:   20/30/60 Hz at point-blank, perfect wire (the floor every object pays).
    ///   - distance:    distance reduction ON, 20 Hz, sweeping nearest-viewer distance (the practical killer).
    ///   - no-reduction: the same distances with reduction OFF (flat control — proves it's the throttle).
    ///   - jitter:      one-way jitter at 20 Hz point-blank (drives the dynamic-depth climb).
    ///   - extrapolate: on/off under jitter (shows extrapolation masks gaps but doesn't cut steady latency).
    /// </summary>
    public static class BasisSyncLatencyMatrix
    {
        public sealed class Options
        {
            public bool SendRate = true;
            public bool Distance = true;
            public bool NoReductionControl = true;
            public bool Jitter = true;
            public bool Extrapolate = true;
            public bool Fixes = true;
            public int Seed = 12345;
            public double RenderHz = 72;
        }

        public static List<BasisSyncLatencyScenario> Enumerate(Options o)
        {
            var list = new List<BasisSyncLatencyScenario>();

            if (o.SendRate)
                foreach (double hz in new[] { 20.0, 30.0, 60.0 })
                    list.Add(new BasisSyncLatencyScenario
                    {
                        Name = $"sendrate/{hz:0}Hz", SendHz = hz, DistanceMeters = 0, DistanceReduction = false,
                        BaseLatencyMs = 10, JitterMs = 0, Seed = o.Seed, RenderHz = o.RenderHz,
                    });

            if (o.Distance)
                foreach (double d in new[] { 0.0, 3.0, 5.0, 10.0, 20.0, 30.0 })
                    list.Add(new BasisSyncLatencyScenario
                    {
                        Name = $"distredux/{d:0}m", SendHz = 20, DistanceMeters = d, DistanceReduction = true,
                        BaseLatencyMs = 10, JitterMs = 0, Seed = o.Seed, RenderHz = o.RenderHz,
                    });

            if (o.NoReductionControl)
                foreach (double d in new[] { 10.0, 20.0, 30.0 })
                    list.Add(new BasisSyncLatencyScenario
                    {
                        Name = $"noredux/{d:0}m", SendHz = 20, DistanceMeters = d, DistanceReduction = false,
                        BaseLatencyMs = 10, JitterMs = 0, Seed = o.Seed, RenderHz = o.RenderHz,
                    });

            if (o.Jitter)
                foreach (double j in new[] { 0.0, 10.0, 30.0, 60.0 })
                    list.Add(new BasisSyncLatencyScenario
                    {
                        Name = $"jitter/{j:0}ms", SendHz = 20, DistanceMeters = 0, DistanceReduction = false,
                        BaseLatencyMs = 20, JitterMs = j, Seed = o.Seed, RenderHz = o.RenderHz,
                    });

            if (o.Extrapolate)
                foreach (bool ex in new[] { false, true })
                    list.Add(new BasisSyncLatencyScenario
                    {
                        Name = $"extrap/{(ex ? "on" : "off")}", SendHz = 20, DistanceMeters = 0, DistanceReduction = false,
                        BaseLatencyMs = 20, JitterMs = 30, Extrapolate = ex, Seed = o.Seed, RenderHz = o.RenderHz,
                    });

            if (o.Fixes)
            {
                // Before/after for the two fix levers. Buffer-depth 1 at point-blank; full-rate-while-held at 20 m.
                list.Add(new BasisSyncLatencyScenario { Name = "fix/before/0m", SendHz = 20, DistanceMeters = 0, DistanceReduction = false, BaseLatencyMs = 10, JitterMs = 0, JitterBufferDepth = 2, Seed = o.Seed, RenderHz = o.RenderHz });
                list.Add(new BasisSyncLatencyScenario { Name = "fix/buffer1/0m", SendHz = 20, DistanceMeters = 0, DistanceReduction = false, BaseLatencyMs = 10, JitterMs = 0, JitterBufferDepth = 1, Seed = o.Seed, RenderHz = o.RenderHz });
                list.Add(new BasisSyncLatencyScenario { Name = "fix/before/20m", SendHz = 20, DistanceMeters = 20, DistanceReduction = true, BaseLatencyMs = 10, JitterMs = 0, JitterBufferDepth = 2, Seed = o.Seed, RenderHz = o.RenderHz });
                list.Add(new BasisSyncLatencyScenario { Name = "fix/held/20m", SendHz = 20, DistanceMeters = 20, DistanceReduction = true, SuppressDistanceReduction = true, BaseLatencyMs = 10, JitterMs = 0, JitterBufferDepth = 2, Seed = o.Seed, RenderHz = o.RenderHz });
                list.Add(new BasisSyncLatencyScenario { Name = "fix/held+buffer1/20m", SendHz = 20, DistanceMeters = 20, DistanceReduction = true, SuppressDistanceReduction = true, BaseLatencyMs = 10, JitterMs = 0, JitterBufferDepth = 1, Seed = o.Seed, RenderHz = o.RenderHz });
            }

            return list;
        }

        public static List<BasisSyncLatencyResult> RunAll(Options o, Action<int, int, BasisSyncLatencyResult> onProgress = null, Func<bool> cancelled = null)
        {
            List<BasisSyncLatencyScenario> scenarios = Enumerate(o);
            var results = new List<BasisSyncLatencyResult>(scenarios.Count);
            for (int i = 0; i < scenarios.Count; i++)
            {
                if (cancelled != null && cancelled()) break;
                BasisSyncLatencyResult r = BasisSyncLatency.Run(scenarios[i]);
                results.Add(r);
                onProgress?.Invoke(i + 1, scenarios.Count, r);
            }
            return results;
        }

        public static string ToCsv(List<BasisSyncLatencyResult> results)
        {
            var sb = new StringBuilder();
            sb.Append(BasisSyncLatencyResult.CsvHeader).Append('\n');
            if (results != null)
                for (int i = 0; i < results.Count; i++)
                    sb.Append(results[i].ToCsvRow()).Append('\n');
            return sb.ToString();
        }
    }
}
