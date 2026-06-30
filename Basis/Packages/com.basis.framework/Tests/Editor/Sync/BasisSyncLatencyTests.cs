using Basis.Scripts.Networking.Sync.Testing;
using NUnit.Framework;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Latency characterization for the generic value-sync path. Where the codec/receiver/convergence
    /// tests ask "is the value correct", these ask "how late is it" — the axis behind the in-practice
    /// lag. They drive the REAL BasisSyncReceiver through BasisSyncLatency.Run and pin both the absolute
    /// floor and the relative effect of each lever (send rate, distance reduction, jitter buffer), so a
    /// future latency fix has a green/red signal and a regression has a tripwire.
    /// </summary>
    public class BasisSyncLatencyTests
    {
        static BasisSyncLatencyScenario Default() => new BasisSyncLatencyScenario
        {
            Name = "default", SendHz = 20, DistanceMeters = 0, DistanceReduction = false,
            BaseLatencyMs = 10, JitterMs = 0, Seed = 12345, RenderHz = 72,
            DurationSeconds = 12, WarmupSeconds = 2,
        };

        // ── The distance-reduction curve math (mirrors BasisSyncedObject.TransmitIfDue / server config) ──

        [Test]
        public void EffectiveSendInterval_ZeroDistance_IsBase()
        {
            double baseInterval = 0.05;
            Assert.AreEqual(baseInterval, BasisSyncLatency.EffectiveSendInterval(baseInterval, 0), 1e-9);
        }

        [Test]
        public void EffectiveSendInterval_GrowsQuadraticallyWithDistance()
        {
            double b = 0.05;
            // base * (1 + d^2 * 0.005): 10 m -> *1.5, 20 m -> *3.0
            Assert.AreEqual(b * 1.5, BasisSyncLatency.EffectiveSendInterval(b, 10), 1e-6);
            Assert.AreEqual(b * 3.0, BasisSyncLatency.EffectiveSendInterval(b, 20), 1e-6);
        }

        [Test]
        public void EffectiveSendInterval_ClampsToSlowestSendRate()
        {
            // d=200 -> base*(1 + 40000*0.005) = base*201, far past the 2.55 s floor.
            Assert.AreEqual(BasisSyncLatency.DistSlowestSendRate, BasisSyncLatency.EffectiveSendInterval(0.05, 200), 1e-4);
        }

        // ── Absolute floor: even point-blank on a clean wire, the default config is well behind ──

        [Test]
        public void Baseline_PointBlank_Default_HasSubstantialLatency()
        {
            BasisSyncLatencyResult r = BasisSyncLatency.Run(Default());
            Assert.Greater(r.Samples, 100, "expected a populated sample window");
            // Measured ~165 ms (jitter buffer ~2.75 intervals + 50 ms send granularity). Wide band so the
            // test pins the order of magnitude (this IS laggy) without being brittle to small interp changes.
            Assert.That(r.MeanLatencyMs, Is.InRange(110f, 260f),
                $"point-blank default latency was {r.MeanLatencyMs:0} ms");
        }

        [Test]
        public void Baseline_JitterBufferIsTheDominantShare()
        {
            BasisSyncLatencyResult r = BasisSyncLatency.Run(Default());
            // The staged jitter buffer should account for most of the latency at point-blank.
            Assert.Greater(r.MeanBufferMs, 80f, $"buffer share only {r.MeanBufferMs:0} ms");
            Assert.Greater(r.MeanBufferMs, 0.5f * r.MeanLatencyMs,
                $"buffer {r.MeanBufferMs:0} ms vs total {r.MeanLatencyMs:0} ms — expected buffer to dominate");
        }

        // ── Send rate: faster owner cadence cuts latency monotonically ──

        [Test]
        public void HigherSendRate_LowersLatency()
        {
            float l20 = Run(20);
            float l30 = Run(30);
            float l60 = Run(60);
            Assert.Greater(l20, l30, $"20 Hz ({l20:0}) should be laggier than 30 Hz ({l30:0})");
            Assert.Greater(l30, l60, $"30 Hz ({l30:0}) should be laggier than 60 Hz ({l60:0})");

            float Run(double hz)
            {
                BasisSyncLatencyScenario s = Default();
                s.SendHz = hz;
                return BasisSyncLatency.Run(s).MeanLatencyMs;
            }
        }

        // ── Distance reduction (default ON): the practical killer — latency climbs with viewer distance ──

        [Test]
        public void DistanceReduction_RaisesLatencyWithDistance()
        {
            float l0 = RunDist(0);
            float l10 = RunDist(10);
            float l20 = RunDist(20);
            float l30 = RunDist(30);

            Assert.Greater(l10, l0, "10 m should be laggier than point-blank");
            Assert.Greater(l20, l10, "20 m should be laggier than 10 m");
            Assert.Greater(l30, l20, "30 m should be laggier than 20 m");
            // 20 m measured ~446 ms vs ~165 ms point-blank — comfortably over 1.8x.
            Assert.Greater(l20, 1.8f * l0, $"distance reduction barely moved latency: {l0:0} -> {l20:0} ms");

            float RunDist(double d)
            {
                BasisSyncLatencyScenario s = Default();
                s.DistanceReduction = true;
                s.DistanceMeters = d;
                return BasisSyncLatency.Run(s).MeanLatencyMs;
            }
        }

        [Test]
        public void NoReductionControl_LatencyFlatAcrossDistance()
        {
            // With reduction OFF the send rate ignores distance, so latency must not move.
            float near = RunDist(0);
            float far = RunDist(30);
            Assert.That(far, Is.EqualTo(near).Within(15f),
                $"reduction-off latency should be flat: {near:0} ms vs {far:0} ms");

            float RunDist(double d)
            {
                BasisSyncLatencyScenario s = Default();
                s.DistanceReduction = false;
                s.DistanceMeters = d;
                return BasisSyncLatency.Run(s).MeanLatencyMs;
            }
        }

        // ── Fix 1: a shallower jitter buffer cuts the fixed latency floor ──

        [Test]
        public void ShallowerJitterBuffer_LowersLatency()
        {
            float depth2 = RunDepth(2f);
            float depth1 = RunDepth(1f);
            Assert.Less(depth1, depth2, $"depth-1 ({depth1:0}) should be lower than depth-2 ({depth2:0})");
            // One fewer staged interval ≈ one send interval (50 ms at 20 Hz). Expect a meaningful cut.
            Assert.Greater(depth2 - depth1, 25f, $"buffer-depth cut only saved {depth2 - depth1:0} ms");

            float RunDepth(float d)
            {
                BasisSyncLatencyScenario s = Default();
                s.JitterBufferDepth = d;
                return BasisSyncLatency.Run(s).MeanLatencyMs;
            }
        }

        // ── Fix 2: suppressing reduction while held restores full rate (flat with distance) ──

        [Test]
        public void SuppressReduction_FlattensDistanceLatency()
        {
            float throttled = RunAt(20, suppress: false); // distance reduction ON
            float held = RunAt(20, suppress: true);        // full-rate-while-held

            float pointBlank = RunAt(0, suppress: false);
            Assert.Less(held, throttled, $"held full-rate ({held:0}) should beat throttled ({throttled:0})");
            Assert.That(held, Is.EqualTo(pointBlank).Within(15f),
                $"held at 20 m ({held:0}) should match point-blank ({pointBlank:0})");

            float RunAt(double d, bool suppress)
            {
                BasisSyncLatencyScenario s = Default();
                s.DistanceReduction = true;
                s.DistanceMeters = d;
                s.SuppressDistanceReduction = suppress;
                return BasisSyncLatency.Run(s).MeanLatencyMs;
            }
        }

        [Test]
        public void BothFixes_LargeReductionAtDistance()
        {
            BasisSyncLatencyScenario before = Default();
            before.DistanceReduction = true; before.DistanceMeters = 20;

            BasisSyncLatencyScenario after = Default();
            after.DistanceReduction = true; after.DistanceMeters = 20;
            after.SuppressDistanceReduction = true; after.JitterBufferDepth = 1f;

            float b = BasisSyncLatency.Run(before).MeanLatencyMs;
            float a = BasisSyncLatency.Run(after).MeanLatencyMs;
            // ~446 ms -> well under half once the throttle is off and the buffer is shallow.
            Assert.Less(a, 0.5f * b, $"combined fixes only got {b:0} -> {a:0} ms");
        }

        // ── Reproducibility ──

        [Test]
        public void Latency_IsDeterministicForFixedSeed()
        {
            float a = BasisSyncLatency.Run(Default()).MeanLatencyMs;
            float b = BasisSyncLatency.Run(Default()).MeanLatencyMs;
            Assert.AreEqual(a, b, 1e-3f, "same scenario + seed should reproduce the same latency");
        }
    }
}
