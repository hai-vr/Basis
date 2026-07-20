using NUnit.Framework;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Pins the coarseness of the network-gizmo label change-keys (BasisNetworkGizmoLabelCore).
    /// The interp fraction cycles 0→1 every network keyframe, so if these keys ever become
    /// fine-grained again, every debug label re-tessellates its TMP mesh every frame and the
    /// gizmo system falls back over at high player counts. Each test states the bucket
    /// contract the consumers rely on: values inside one bucket → same key (no rebuild),
    /// crossing a bucket → new key (one rebuild).
    /// </summary>
    public class BasisGizmoLabelKeyTests
    {
        private static int PlayerKey(float t = 0.3f, float rate = 1f, int staged = 2, float bps = 1000f, float pps = 20f, bool state = true, bool bw = true, int id = 7)
        {
            return BasisNetworkGizmoLabelCore.PlayerLabelKey(id, t, rate, staged, bps, pps, state, bw);
        }

        // ── Interp-t: 10% buckets ───────────────────────────────────────────

        [Test]
        public void PlayerKey_InterpWithinBucket_SameKey()
        {
            Assert.AreEqual(PlayerKey(t: 0.31f), PlayerKey(t: 0.34f));
            Assert.AreEqual(PlayerKey(t: 0.06f), PlayerKey(t: 0.14f));
        }

        [Test]
        public void PlayerKey_InterpCrossingBucket_ChangesKey()
        {
            Assert.AreNotEqual(PlayerKey(t: 0.34f), PlayerKey(t: 0.36f));
        }

        [Test]
        public void PlayerKey_InterpClamped_OutOfRangeMatchesEdge()
        {
            Assert.AreEqual(PlayerKey(t: 1f), PlayerKey(t: 1.7f));
            Assert.AreEqual(PlayerKey(t: 0f), PlayerKey(t: -0.5f));
        }

        // ── Playback rate: 5% buckets ───────────────────────────────────────

        [Test]
        public void PlayerKey_RateWithinBucket_SameKey()
        {
            Assert.AreEqual(PlayerKey(rate: 1.00f), PlayerKey(rate: 1.02f));
        }

        [Test]
        public void PlayerKey_RateCrossingBucket_ChangesKey()
        {
            Assert.AreNotEqual(PlayerKey(rate: 1.00f), PlayerKey(rate: 1.08f));
        }

        // ── Bandwidth: 128 B/s buckets ──────────────────────────────────────

        [Test]
        public void PlayerKey_BandwidthWithinBucket_SameKey()
        {
            Assert.AreEqual(PlayerKey(bps: 1000f), PlayerKey(bps: 1020f));
        }

        [Test]
        public void PlayerKey_BandwidthCrossingBucket_ChangesKey()
        {
            Assert.AreNotEqual(PlayerKey(bps: 1000f), PlayerKey(bps: 1200f));
        }

        // ── Non-bucketed inputs must always change the key ──────────────────

        [Test]
        public void PlayerKey_StagedCountChange_ChangesKey()
        {
            Assert.AreNotEqual(PlayerKey(staged: 2), PlayerKey(staged: 3));
        }

        [Test]
        public void PlayerKey_DifferentPlayers_DifferentKeys()
        {
            Assert.AreNotEqual(PlayerKey(id: 7), PlayerKey(id: 8));
        }

        [Test]
        public void PlayerKey_ModeFlags_ChangeKey()
        {
            Assert.AreNotEqual(PlayerKey(state: true, bw: true), PlayerKey(state: true, bw: false));
            Assert.AreNotEqual(PlayerKey(state: true, bw: true), PlayerKey(state: false, bw: true));
        }

        [Test]
        public void PlayerKey_DisabledSections_IgnoreTheirInputs()
        {
            // With state off, interp/rate/staged must not affect the key (no pointless rebuilds).
            Assert.AreEqual(
                PlayerKey(state: false, t: 0.1f, rate: 0.9f, staged: 1),
                PlayerKey(state: false, t: 0.9f, rate: 1.3f, staged: 9));
            // With bandwidth off, byte/packet rates must not affect the key.
            Assert.AreEqual(
                PlayerKey(bw: false, bps: 100f, pps: 5f),
                PlayerKey(bw: false, bps: 90000f, pps: 50f));
        }

        // ── Sync-object key ─────────────────────────────────────────────────

        private static int SyncKey(float t = 0.4f, int depth = 3, bool extrap = false, float bps = 500f, float pps = 10f, bool state = true, bool bw = true, int id = 42)
        {
            return BasisNetworkGizmoLabelCore.SyncLabelKey(id, t, depth, extrap, bps, pps, state, bw);
        }

        [Test]
        public void SyncKey_InterpWithinBucket_SameKey()
        {
            Assert.AreEqual(SyncKey(t: 0.41f), SyncKey(t: 0.44f));
        }

        [Test]
        public void SyncKey_InterpCrossingBucket_ChangesKey()
        {
            Assert.AreNotEqual(SyncKey(t: 0.44f), SyncKey(t: 0.46f));
        }

        [Test]
        public void SyncKey_OvershootClampsAtTenX()
        {
            // The sync job lerps unclamped (overshoot is a real state), but the key clamps
            // at 9.99 so a runaway extrapolation doesn't churn the label forever.
            Assert.AreEqual(SyncKey(t: 9.99f), SyncKey(t: 25f));
        }

        [Test]
        public void SyncKey_ExtrapolationFlip_ChangesKey()
        {
            Assert.AreNotEqual(SyncKey(extrap: false), SyncKey(extrap: true));
        }

        [Test]
        public void SyncKey_BufferDepthChange_ChangesKey()
        {
            Assert.AreNotEqual(SyncKey(depth: 3), SyncKey(depth: 4));
        }

        [Test]
        public void SyncKey_IsDeterministic()
        {
            Assert.AreEqual(SyncKey(), SyncKey());
            Assert.AreEqual(PlayerKey(), PlayerKey());
        }
    }
}
