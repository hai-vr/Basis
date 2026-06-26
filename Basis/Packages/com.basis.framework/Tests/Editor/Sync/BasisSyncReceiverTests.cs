using System;
using Basis.Scripts.Networking.Sync;
using NUnit.Framework;
using static Basis.Tests.Sync.BasisSyncTestSupport;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Behavioural tests for <see cref="BasisSyncReceiver"/> (the playback / jitter buffer), driven with
    /// hand-crafted packet sequences: keyframe gating, delta-on-baseline, stale / duplicate rejection,
    /// sequence wraparound, teleport snapping, reset, keyframe recovery, and decode robustness.
    /// </summary>
    public class BasisSyncReceiverTests
    {
        [Test]
        public void Receiver_HasNoData_BeforeAnyPacket()
        {
            var (s, _) = Single(BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);
            Assert.IsFalse(r.HasData);
            r.Advance(Dt);
            Assert.IsFalse(r.HasData);
        }

        [Test]
        public void Receiver_DeltaBeforeKeyframe_IsIgnored()
        {
            var (s, v) = Single(BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);

            v.Cont[0] = 9f; FeedDelta(r, s, v, 1, new[] { 0 });
            r.Advance(Dt);
            Assert.IsFalse(r.HasData, "a delta arriving before any keyframe must be dropped");

            v.Cont[0] = 5f; FeedKeyframe(r, s, v, 2);
            r.Advance(Dt);
            Assert.IsTrue(r.HasData);
            Assert.AreEqual(5f, r.NextValues.Cont[0], 1e-3f);
        }

        [Test]
        public void Receiver_Keyframe_ThenDelta_AppliesOnBaseline()
        {
            var s = Schema(BasisSyncFieldType.Float, BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);
            var v = Values(s);

            v.Cont[0] = 1f; v.Cont[1] = 2f; FeedKeyframe(r, s, v, 1); r.Advance(Dt);
            v.Cont[0] = 9f; FeedDelta(r, s, v, 2, new[] { 0 }); r.Advance(Dt);

            Assert.AreEqual(9f, r.NextValues.Cont[0], 1e-3f, "delta field updated");
            Assert.AreEqual(2f, r.NextValues.Cont[1], 1e-3f, "untouched field carried from baseline");
        }

        [Test]
        public void Receiver_StalePacket_IsIgnored()
        {
            var (s, v) = Single(BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);

            v.Cont[0] = 100f; FeedKeyframe(r, s, v, 10); r.Advance(Dt);
            v.Cont[0] = 110f; FeedKeyframe(r, s, v, 11); r.Advance(Dt);
            Assert.AreEqual(110f, r.NextValues.Cont[0], 1e-3f);

            v.Cont[0] = 90f; FeedKeyframe(r, s, v, 9); r.Advance(Dt); // seq 9 is behind 11
            Assert.AreEqual(110f, r.NextValues.Cont[0], 1e-3f, "stale packet must not overwrite newer state");
        }

        [Test]
        public void Receiver_DuplicatePacket_IsDeduped()
        {
            var (s, v) = Single(BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);

            v.Cont[0] = 100f; FeedKeyframe(r, s, v, 10); r.Advance(Dt);
            v.Cont[0] = 110f; FeedKeyframe(r, s, v, 11); r.Advance(Dt);
            v.Cont[0] = 110f; FeedKeyframe(r, s, v, 11); // duplicate seq
            Assert.DoesNotThrow(() => r.Advance(Dt));
            Assert.AreEqual(110f, r.NextValues.Cont[0], 1e-3f);
        }

        [Test]
        public void Receiver_SeqWraparound_KeepsAccepting()
        {
            var (s, v) = Single(BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);

            byte seq = 0;
            for (int i = 1; i <= 280; i++) // wraps the byte sequence past 255
            {
                seq++;
                v.Cont[0] = i;
                FeedKeyframe(r, s, v, seq);
                r.Advance(Dt);
            }
            Advance(r, 300); // drain the playback buffer to the newest frame

            Assert.IsTrue(r.HasData);
            Assert.AreEqual(280f, r.NextValues.Cont[0], 1e-3f, "must keep accepting the newest packet across the seq wrap");
        }

        [Test]
        public void Receiver_TeleportThreshold_SnapsBeyond_InterpolatesBelow()
        {
            var (s, v) = Single(BasisSyncFieldType.Position);
            var r = new BasisSyncReceiver(s);
            r.Configure(false, 0.2, true, 9f, 0, 3); // threshold 3m (sq 9) over the position axes

            v.Cont[0] = 0f; v.Cont[1] = 0f; v.Cont[2] = 0f; FeedKeyframe(r, s, v, 1); r.Advance(Dt);

            v.Cont[0] = 50f; FeedKeyframe(r, s, v, 2); r.Advance(Dt); // jump beyond threshold
            Assert.AreEqual(50f, r.CurrentValues.Cont[0], 0.1f, "a jump beyond the threshold should snap immediately");
            Assert.AreEqual(0f, r.InterpTime, 1e-4f, "snap clears the interpolation window");

            v.Cont[0] = 50.5f; FeedKeyframe(r, s, v, 3); r.Advance(Dt); // small move
            Assert.AreEqual(50f, r.CurrentValues.Cont[0], 0.1f, "a below-threshold move must not snap");
            Assert.Greater(r.InterpTime, 0f, "a below-threshold move should interpolate");
        }

        [Test]
        public void Receiver_Reset_ClearsState()
        {
            var (s, v) = Single(BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);

            v.Cont[0] = 7f; FeedKeyframe(r, s, v, 1); r.Advance(Dt);
            Assert.IsTrue(r.HasData);

            r.Reset();
            Assert.IsFalse(r.HasData, "reset clears the current frame");

            v.Cont[0] = 8f; FeedDelta(r, s, v, 2, new[] { 0 }); r.Advance(Dt);
            Assert.IsFalse(r.HasData, "after reset a delta needs a fresh keyframe");

            v.Cont[0] = 9f; FeedKeyframe(r, s, v, 3); r.Advance(Dt);
            Assert.IsTrue(r.HasData);
            Assert.AreEqual(9f, r.NextValues.Cont[0], 1e-3f);
        }

        [Test]
        public void Receiver_RecoversFromDeltaLoss_ViaNextKeyframe()
        {
            var s = Schema(BasisSyncFieldType.Float, BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);
            var v = Values(s);

            v.Cont[0] = 1f; v.Cont[1] = 1f; FeedKeyframe(r, s, v, 1); r.Advance(Dt);
            // seq 2 delta is "lost" (never fed)
            v.Cont[0] = 3f; v.Cont[1] = 3f; FeedKeyframe(r, s, v, 3); r.Advance(Dt);

            Assert.AreEqual(3f, r.NextValues.Cont[0], 1e-3f);
            Assert.AreEqual(3f, r.NextValues.Cont[1], 1e-3f, "a keyframe restores full state despite the missing delta");
        }

        [Test]
        public void Receiver_DoesNotThrow_OnArbitraryFixedSizeBytes()
        {
            // Fixed-size fields only (no varint) -> decode reads bounded offsets and cannot overrun.
            var s = Schema(BasisSyncFieldType.Position, BasisSyncFieldType.Rotation, BasisSyncFieldType.Color);
            var r = new BasisSyncReceiver(s);
            var rng = new System.Random(99);
            int max = BasisSyncCodec.MaxSerializedSize(s);

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 64; i++)
                {
                    var garbage = new byte[max];
                    rng.NextBytes(garbage);
                    r.OnPacket(garbage, max);
                    r.Advance(Dt);
                }
            });
        }

        [Test]
        public void Receiver_DropsCorruptedPacket_RecoversViaKeyframe()
        {
            var (s, v) = Single(BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);
            r.Configure(false, 0.2, false, 0f, 0, 0, verifyChecksum: true);

            v.Cont[0] = 1f; FeedKeyframe(r, s, v, 1, checksum: true); r.Advance(Dt);
            Assert.AreEqual(1f, r.NextValues.Cont[0], 1e-3f);

            // A corrupted delta whose sequence is also poisoned far ahead: if trusted it would apply garbage AND
            // make the next keyframe (seq 2) look stale (fwd >= 128). The checksum must drop it first.
            v.Cont[0] = 999f;
            byte[] bad = SerializeDelta(s, v, 2, new[] { 0 }, out int badLen, checksum: true);
            bad[0] = 120;                                  // poison the sequence number far ahead
            bad[BasisSyncCodec.HeaderSize] ^= 0xFF;        // corrupt the value
            r.OnPacket(bad, badLen);
            r.Advance(Dt);
            Assert.AreEqual(1f, r.NextValues.Cont[0], 1e-3f, "corrupted packet dropped, state unchanged");

            v.Cont[0] = 5f; FeedKeyframe(r, s, v, 2, checksum: true); r.Advance(Dt);
            Assert.AreEqual(5f, r.NextValues.Cont[0], 1e-3f, "clean keyframe applies; the corrupted sequence did not poison the buffer");
        }

        [Test]
        public void Receiver_Extrapolation_PredictsBeyondWindow_OrClampsAtIt()
        {
            var (s, v) = Single(BasisSyncFieldType.Float);
            var noExtrap = new BasisSyncReceiver(s);
            var extrap = new BasisSyncReceiver(s);
            noExtrap.Configure(false, 0.1, false, 0f, 0, 0);
            extrap.Configure(true, 0.1, false, 0f, 0, 0);

            // Two keyframes 50 ms apart, then starve: no more packets ever arrive.
            foreach (BasisSyncReceiver r in new[] { noExtrap, extrap })
            {
                v.Cont[0] = 1f; FeedKeyframe(r, s, v, 1, ms: 50);
                v.Cont[0] = 2f; FeedKeyframe(r, s, v, 2, ms: 50);
                for (int i = 0; i < 40; i++) r.Advance(Dt);
            }

            Assert.AreEqual(1f, noExtrap.InterpTime, 1e-3f, "a non-extrapolating receiver holds at the window end (t = 1)");
            Assert.Greater(extrap.InterpTime, 1f, "an extrapolating receiver predicts past the last frame");
            float cap = 1f + 0.1f / 0.05f; // maxExtrapolation / window
            Assert.LessOrEqual(extrap.InterpTime, cap + 1e-3f, "extrapolation never runs past the max-extrapolation cap");
        }

        [Test]
        public void Receiver_AdaptivePlayback_DrainsFasterWhenOverBuffered()
        {
            var (s, v) = Single(BasisSyncFieldType.Float);
            var r = new BasisSyncReceiver(s);
            r.Configure(false, 0.1, false, 0f, 0, 0);

            for (int i = 1; i <= 24; i++) { v.Cont[0] = i; FeedKeyframe(r, s, v, (byte)i, ms: 50); }
            r.Advance(Dt); // stage the burst, take current+next
            int buffered0 = r.BufferedFrameCount;
            Assert.GreaterOrEqual(buffered0, 20, "a burst should leave a deep buffer");

            // 0.5 s of wall time, no new frames. At the nominal 1x rate that consumes 0.5 / 0.05 = 10 frames;
            // an over-buffered receiver speeds up (catch-up) and consumes more.
            for (int i = 0; i < 36; i++) r.Advance(Dt);

            int consumed = buffered0 - r.BufferedFrameCount;
            Assert.Greater(consumed, 10, "an over-buffered receiver catches up faster than the 1x playback rate");
        }
    }
}
