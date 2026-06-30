using System;
using Basis.Scripts.Networking.Sync;
using NUnit.Framework;
using static Basis.Tests.Sync.BasisSyncTestSupport;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Direct unit coverage for the configurable jitter-buffer depth on <see cref="BasisSyncReceiver"/>
    /// (the <c>bufferDepth</c> argument to <see cref="BasisSyncReceiver.Configure"/>). The latency harness
    /// exercises this end-to-end; these pin the receiver mechanism itself: the floor is seeded and clamped,
    /// the default argument preserves the historical depth-2 behaviour byte-for-byte, a lower floor settles
    /// to a shallower steady buffer (the latency win), and the depth still adapts upward under starvation.
    /// </summary>
    public class BasisSyncReceiverDepthTests
    {
        // Mirrors BasisSyncReceiver's private MinJitterDepth/MaxJitterDepth.
        const float FloorMin = 1f;
        const float FloorMax = 4f;
        const float LegacyDepth = 2f;

        static BasisSyncReceiver Configured(BasisSyncSchema s, float bufferDepth)
        {
            var r = new BasisSyncReceiver(s);
            r.Configure(false, 0.2, false, 0f, 0, 0, false, bufferDepth);
            return r;
        }

        [Test]
        public void Configure_Default_SeedsLegacyDepthTwo()
        {
            var (s, _) = Single(BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);
            r.Configure(false, 0.2, false, 0f, 0, 0); // no bufferDepth arg => legacy default
            Assert.AreEqual(LegacyDepth, r.DynamicDepth, 1e-3f, "the default buffer depth must stay at the historical 2");
        }

        [Test]
        public void Configure_LowBufferDepth_SeedsDepthOne()
        {
            var (s, _) = Single(BasisSyncFieldType.Float);
            BasisSyncReceiver r = Configured(s, 1f);
            Assert.AreEqual(1f, r.DynamicDepth, 1e-3f, "a fresh receiver should start playback at the configured floor");
        }

        [Test]
        public void Configure_BufferDepth_ClampedToValidRange()
        {
            var (s, _) = Single(BasisSyncFieldType.Float);
            Assert.AreEqual(FloorMin, Configured(s, 0f).DynamicDepth, 1e-3f, "below-range depth clamps up to the minimum");
            Assert.AreEqual(FloorMin, Configured(s, -5f).DynamicDepth, 1e-3f, "negative depth clamps up to the minimum");
            Assert.AreEqual(FloorMax, Configured(s, 99f).DynamicDepth, 1e-3f, "above-range depth clamps down to the maximum");
        }

        [Test]
        public void Configure_DefaultArg_MatchesExplicitDepthTwo_OverAStream()
        {
            // The default 8th argument must reproduce the pre-change wire/playback behaviour exactly, so
            // existing content and the convergence matrix don't move.
            var s = Schema(BasisSyncFieldType.Float, BasisSyncFieldType.Float);
            var legacy = new BasisSyncReceiver(s);
            legacy.Configure(false, 0.2, false, 0f, 0, 0);            // 6-arg legacy call
            BasisSyncReceiver explicitTwo = Configured(s, LegacyDepth); // explicit depth 2

            var v = Values(s);
            byte seq = 0;
            for (int i = 1; i <= 30; i++)
            {
                seq++;
                v.Cont[0] = i; v.Cont[1] = i * 2;
                bool keyframe = (i % 5) == 1;
                if (keyframe) { FeedKeyframe(legacy, s, v, seq, ms: 50); FeedKeyframe(explicitTwo, s, v, seq, ms: 50); }
                else { FeedDelta(legacy, s, v, seq, new[] { 0 }, ms: 50); FeedDelta(explicitTwo, s, v, seq, new[] { 0 }, ms: 50); }
                legacy.Advance(Dt); explicitTwo.Advance(Dt);
            }

            Assert.AreEqual(legacy.DynamicDepth, explicitTwo.DynamicDepth, 1e-4f, "depth target diverged from legacy");
            Assert.AreEqual(legacy.BufferedFrameCount, explicitTwo.BufferedFrameCount, "buffer occupancy diverged from legacy");
            Assert.AreEqual(legacy.NextValues.Cont[0], explicitTwo.NextValues.Cont[0], 1e-4f, "output diverged from legacy");
            Assert.AreEqual(legacy.InterpTime, explicitTwo.InterpTime, 1e-4f, "interp fraction diverged from legacy");
        }

        [Test]
        public void LowBufferDepth_SettlesToShallowerSteadyBuffer()
        {
            // Feed both receivers a real-time stream (window == advance dt, one frame per step) so the
            // adaptive playback rate drives the staged buffer to each receiver's target. The depth-1
            // receiver must settle both a lower depth target and a shallower (or equal) staged buffer.
            var (s, v) = Single(BasisSyncFieldType.Float);
            BasisSyncReceiver shallow = Configured(s, 1f);
            BasisSyncReceiver deep = Configured(s, 2f);

            const float dt = 0.05f;     // == the 50 ms window below, so playback consumes ~1 frame/step
            byte seq = 0;
            for (int i = 0; i < 400; i++)
            {
                seq++;
                v.Cont[0] = i;
                FeedKeyframe(shallow, s, v, seq, ms: 50);
                FeedKeyframe(deep, s, v, seq, ms: 50);
                shallow.Advance(dt);
                deep.Advance(dt);
            }

            Assert.Less(shallow.DynamicDepth, deep.DynamicDepth,
                $"depth-1 ({shallow.DynamicDepth:0.00}) should settle below depth-2 ({deep.DynamicDepth:0.00})");
            Assert.LessOrEqual(shallow.BufferedFrameCount, deep.BufferedFrameCount,
                "a lower depth target should hold no more staged frames than a higher one");
        }

        [Test]
        public void LowBufferDepth_StillAdaptsUpwardOnStarvation()
        {
            // Lowering the floor must not disable the upward adaptation: a starved receiver still climbs
            // its depth target above the floor to rebuild tolerance.
            var (s, v) = Single(BasisSyncFieldType.Float);
            BasisSyncReceiver r = Configured(s, 1f);
            Assert.AreEqual(1f, r.DynamicDepth, 1e-3f);

            v.Cont[0] = 1f; FeedKeyframe(r, s, v, 1, ms: 50);
            v.Cont[0] = 2f; FeedKeyframe(r, s, v, 2, ms: 50);
            r.Advance(Dt);                 // take current + next, staged now empty
            Advance(r, 40);                // starve: repeated underruns

            Assert.Greater(r.DynamicDepth, 1f, "a starved receiver must climb above the floor");
        }
    }
}
