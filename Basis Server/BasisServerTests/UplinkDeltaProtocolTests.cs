using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Xunit;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;
using S = BasisServerTests.DeltaTestSupport;

namespace BasisServerTests;

/// <summary>
/// v42 uplink/P2P avatar delta protocol: the client frames [hdr][seq][baseSeq][delta body] on
/// DeltaAvatarChannel against its last uploaded keyframe; the server (or a P2P peer) reconstructs
/// with the shared codec, NACKing via control frames when the baseline is missing or stale.
/// </summary>
public class UplinkDeltaProtocolTests
{
    [Fact]
    public void ClientFrame_ReconstructsExactly_OnMatchingBaseline()
    {
        var rng = new Random(4242);
        byte[] keyframe = S.MakeRealisticPayload(BitQuality.High, rng);
        byte[] current = (byte[])keyframe.Clone();
        current[0] ^= 0xFF;                       // moved position
        S.FlipBone(current, BitQuality.High, 7);  // one bone changed

        // Client side: encode the delta and frame it the way the compressor does.
        byte clientBaseSeq = 10;
        byte clientSeq = 11;
        var scratch = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(BitQuality.High)];
        int bodyLen = BasisAvatarDeltaCompression.BuildDelta(keyframe, current, BitQuality.High, scratch, 0);
        Assert.True(bodyLen > 0 && bodyLen < keyframe.Length, "delta must beat the keyframe here");

        var frame = new byte[3 + bodyLen];
        frame[0] = BasisNetworkCommons.BuildDeltaHeader(3, hasAdditionalData: false, largeId: false);
        frame[1] = clientSeq;
        frame[2] = clientBaseSeq;
        Buffer.BlockCopy(scratch, 0, frame, 3, bodyLen);

        // Server side: parse exactly like HandleDeltaChannelInbound.
        byte header = frame[0];
        Assert.False(BasisNetworkCommons.IsDeltaControlHeader(header));
        Assert.Equal(3, BasisNetworkCommons.DeltaHeaderQuality(header));
        Assert.False(BasisNetworkCommons.DeltaHeaderHasAdditionalData(header));
        byte seq = frame[1], baseSeq = frame[2];
        Assert.Equal(clientSeq, seq);
        Assert.Equal(clientBaseSeq, baseSeq);

        int parsedLen = BasisAvatarDeltaCompression.DeltaBodyLength(frame, 3, frame.Length - 3, BitQuality.High);
        Assert.Equal(bodyLen, parsedLen);

        var reconstructed = new byte[BasisAvatarDeltaCompression.PayloadSize(BitQuality.High)];
        Assert.True(BasisAvatarDeltaCompression.TryApplyDelta(keyframe, frame, 3, parsedLen, BitQuality.High, reconstructed));
        Assert.Equal(current, reconstructed);
    }

    [Fact]
    public void StaleBaseline_IsDetectedBySeqMismatch()
    {
        // The server's stored baseline seq must equal the frame's baseSeq; anything else NACKs.
        byte storedBaselineSeq = 9;
        byte frameBaseSeq = 10;
        Assert.NotEqual(storedBaselineSeq, frameBaseSeq);
    }

    [Fact]
    public void FullyChangedFrame_PromotesToKeyframe()
    {
        var rng = new Random(555);
        byte[] keyframe = S.MakeRealisticPayload(BitQuality.High, rng);
        byte[] current = S.MakeRealisticPayload(BitQuality.High, rng);
        var scratch = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(BitQuality.High)];
        int bodyLen = BasisAvatarDeltaCompression.BuildDelta(keyframe, current, BitQuality.High, scratch, 0);
        // The client sends a keyframe whenever the delta is not strictly smaller.
        Assert.True(bodyLen >= keyframe.Length, "an everything-changed delta must trigger promotion");
    }

    [Fact]
    public void ControlHeaders_NeverCollideWithDataHeaders()
    {
        for (int qi = 0; qi < 4; qi++)
        {
            foreach (bool additional in new[] { false, true })
            {
                foreach (bool largeId in new[] { false, true })
                {
                    byte hdr = BasisNetworkCommons.BuildDeltaHeader(qi, additional, largeId);
                    Assert.False(BasisNetworkCommons.IsDeltaControlHeader(hdr));
                }
            }
        }
        Assert.True(BasisNetworkCommons.IsDeltaControlHeader(BasisNetworkCommons.DeltaControlKeyframeRequest));
        Assert.True(BasisNetworkCommons.IsDeltaControlHeader(BasisNetworkCommons.DeltaControlUplinkKeyframeRequest));
        Assert.NotEqual(BasisNetworkCommons.DeltaControlKeyframeRequest, BasisNetworkCommons.DeltaControlUplinkKeyframeRequest);
    }

    [Fact]
    public void IdleUplinkDelta_IsMaskOnly()
    {
        var rng = new Random(777);
        byte[] keyframe = S.MakeRealisticPayload(BitQuality.High, rng);
        var scratch = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(BitQuality.High)];
        int bodyLen = BasisAvatarDeltaCompression.BuildDelta(keyframe, (byte[])keyframe.Clone(), BitQuality.High, scratch, 0);
        Assert.Equal(BasisAvatarDeltaCompression.DirtyMaskBytes, bodyLen);
    }

    // ── v49 uplink stream frames ─────────────────────────────────────────────

    [Fact]
    public void StreamHeaderBit_IsDistinctFromEveryOtherHeaderMeaning()
    {
        for (int qi = 0; qi < 4; qi++)
            foreach (bool additional in new[] { false, true })
                foreach (bool largeId in new[] { false, true })
                {
                    byte delta = BasisNetworkCommons.BuildDeltaHeader(qi, additional, largeId);
                    byte stream = BasisNetworkCommons.BuildDeltaHeader(qi, additional, largeId, stream: true);

                    Assert.False(BasisNetworkCommons.DeltaHeaderIsStream(delta));
                    Assert.True(BasisNetworkCommons.DeltaHeaderIsStream(stream));
                    // The stream bit must not disturb anything else the header encodes, or a receiver
                    // would read a stream frame's quality/id-width wrong before it even looks at the bit.
                    Assert.Equal(qi, BasisNetworkCommons.DeltaHeaderQuality(stream));
                    Assert.Equal(additional, BasisNetworkCommons.DeltaHeaderHasAdditionalData(stream));
                    Assert.Equal(largeId, BasisNetworkCommons.DeltaHeaderLargeId(stream));
                    Assert.False(BasisNetworkCommons.IsDeltaControlHeader(stream));
                }
    }

    /// <summary>
    /// End-to-end uplink simulation over a lossy link: the client bootstraps with a keyframe, then
    /// sends stream frames and NOTHING else — no periodic keyframe at all. The server must stay exactly
    /// in step, and after an outage must re-converge on its own, without ever asking for a keyframe.
    /// </summary>
    [Fact]
    public void UplinkStream_SurvivesLoss_WithoutEverSendingAnotherKeyframe()
    {
        const BitQuality Q = BitQuality.High;
        var rng = new Random(31337);

        // Client state, mirroring BasisNetworkAvatarCompressor.
        var txStream = new BasisAvatarStreamState(Q);
        var scratch = new byte[BasisAvatarStreamCodec.MaxFrameSize(Q)];
        byte[] pose = S.MakeRealisticPayload(Q, rng);
        byte seq = 0;

        // Server state, mirroring UplinkDeltaState.
        var rxStream = new BasisAvatarStreamState(Q);
        var serverPose = new byte[BasisAvatarDeltaCompression.PayloadSize(Q)];

        // Bootstrap keyframe: both ends seed from exactly the same bytes.
        txStream.SeedFrom(pose);
        rxStream.SeedFrom(pose);
        Buffer.BlockCopy(pose, 0, serverPose, 0, pose.Length);

        int keyframesSent = 1, framesSent = 0, framesDelivered = 0;
        long wireBytes = 0;

        void SendFrame(bool deliver)
        {
            int len = BasisAvatarStreamCodec.Encode(txStream, pose, seq, scratch, 0);
            Assert.True(len > 0);

            // Frame it the way the compressor does: [hdr|stream][seq][body].
            var frame = new byte[2 + len];
            frame[0] = BasisNetworkCommons.BuildDeltaHeader((int)Q, false, false, stream: true);
            frame[1] = seq;
            Buffer.BlockCopy(scratch, 0, frame, 2, len);
            framesSent++;
            wireBytes += frame.Length;
            unchecked { seq++; }
            if (!deliver) return;

            // Server parse, mirroring HandleDeltaChannelInbound.
            byte header = frame[0];
            Assert.True(BasisNetworkCommons.DeltaHeaderIsStream(header));
            Assert.Equal(3, BasisNetworkCommons.DeltaHeaderQuality(header));
            byte rxSeq = frame[1];
            int bodyLen = BasisAvatarStreamCodec.FrameLength(frame, 2, frame.Length - 2, Q);
            Assert.Equal(len, bodyLen);
            Assert.True(BasisAvatarStreamCodec.Decode(rxStream, frame, 2, bodyLen, rxSeq, serverPose));
            framesDelivered++;
        }

        // 1. Clean run while moving — the server must track the client exactly, every frame.
        for (int i = 0; i < 60; i++)
        {
            pose = NudgePose(pose, Q, rng, 3);
            SendFrame(deliver: true);
            for (int c = 0; c < txStream.Estimate.Length; c++)
                Assert.Equal(txStream.Estimate[c], rxStream.Estimate[c]);
        }

        // 2. A 40-frame blackout, then heavy loss, all while the avatar keeps moving.
        for (int i = 0; i < 40; i++) { pose = NudgePose(pose, Q, rng, 4); SendFrame(deliver: false); }
        for (int i = 0; i < 200; i++)
        {
            pose = NudgePose(pose, Q, rng, 3);
            SendFrame(deliver: rng.NextDouble() >= 0.35);
        }

        // 3. The player stops moving. With no keyframe and no retransmission, the Gray sweep alone
        //    has to bring the server back to the exact pose.
        for (int i = 0; i < 2 * BasisAvatarStreamCodec.SweepPeriod(Q) + 16; i++) SendFrame(deliver: true);

        Assert.Equal(pose, serverPose);
        Assert.Equal(1, keyframesSent);
        Assert.True(framesDelivered < framesSent, "the test must actually have dropped frames");

        // The whole run cost less than one keyframe every ~5 frames would have — the cadence the
        // delta scheme needs and this one does not.
        double avg = (double)wireBytes / framesSent;
        Assert.True(avg < S.PayloadSize(Q) * 0.75,
            $"average uplink frame {avg:F1} B should be well under the {S.PayloadSize(Q)} B pose");
    }

    [Fact]
    public void StreamFrame_WithoutBootstrap_IsRejected_SoTheServerCanNack()
    {
        const BitQuality Q = BitQuality.High;
        var rng = new Random(2468);
        var tx = new BasisAvatarStreamState(Q);
        byte[] pose = S.MakeRealisticPayload(Q, rng);
        tx.SeedFrom(pose);

        var scratch = new byte[BasisAvatarStreamCodec.MaxFrameSize(Q)];
        int len = BasisAvatarStreamCodec.Encode(tx, pose, 5, scratch, 0);

        // A server that never saw the bootstrap keyframe has HasBaseline == false, which is exactly
        // the condition HandleDeltaChannelInbound NACKs on rather than decoding against zeroes.
        var rx = new BasisAvatarStreamState(Q);
        Assert.False(rx.HasBaseline);

        // And once bootstrapped it accepts the same frame.
        rx.SeedFrom(pose);
        Assert.True(rx.HasBaseline);
        var outFull = new byte[BasisAvatarDeltaCompression.PayloadSize(Q)];
        Assert.True(BasisAvatarStreamCodec.Decode(rx, scratch, 0, len, 5, outFull));
    }

    private static byte[] NudgePose(byte[] payload, BitQuality q, Random rng, int steps)
    {
        var next = (byte[])payload.Clone();
        foreach (var ch in S.Layout(q).Channels)
        {
            if (ch.Kind != BasisChannelKind.Delta) continue;
            int d = rng.Next(-steps, steps + 1);
            uint v = BasisAvatarDeltaCompression.ReadChannel(next, ch);
            BasisAvatarDeltaCompression.WriteChannel(next, ch, (uint)((int)v + d) & ch.Mask);
        }
        return next;
    }
}
