using Basis.Network.Core.Compression;
using Xunit;
using Xunit.Abstractions;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;
using S = BasisServerTests.DeltaTestSupport;

namespace BasisServerTests;

/// <summary>
/// The continuous-stream codec: closed-loop residuals plus the Gray bit-plane sweep.
///
/// The load-bearing property is <see cref="ConvergesExactly_AfterTotalLinkLoss"/> — cut the link
/// entirely, restore it, and the receiver must return to the sender's exact pose with no
/// acknowledgement, no retransmission and no keyframe. That is the whole reason for the sweep, and
/// the only thing that justifies removing the periodic uplink keyframe.
/// </summary>
public class AvatarStreamCodecTests
{
    private readonly ITestOutputHelper _out;
    public AvatarStreamCodecTests(ITestOutputHelper output) => _out = output;

    /// <summary>Sender and receiver seeded from the same keyframe, as a real bootstrap does.</summary>
    private static (BasisAvatarStreamState tx, BasisAvatarStreamState rx, byte[] scratch, byte[] outFull)
        Pair(BitQuality q, byte[] keyframe)
    {
        var tx = new BasisAvatarStreamState(q);
        var rx = new BasisAvatarStreamState(q);
        tx.SeedFrom(keyframe);
        rx.SeedFrom(keyframe);
        return (tx, rx, new byte[BasisAvatarStreamCodec.MaxFrameSize(q)], new byte[S.PayloadSize(q)]);
    }

    private static void AssertStatesEqual(BasisAvatarStreamState a, BasisAvatarStreamState b, string what)
    {
        for (int i = 0; i < a.Estimate.Length; i++)
            Assert.True(a.Estimate[i] == b.Estimate[i],
                $"{what}: channel {i} diverged — sender {a.Estimate[i]}, receiver {b.Estimate[i]}");
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void LosslessLink_ReceiverTracksSenderModelExactly(BitQuality q)
    {
        var rng = new Random(100 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        var (tx, rx, scratch, outFull) = Pair(q, kf);

        byte[] pose = (byte[])kf.Clone();
        for (int frame = 0; frame < 200; frame++)
        {
            pose = Nudge(pose, q, rng, 3);
            int len = BasisAvatarStreamCodec.Encode(tx, pose, (byte)frame, scratch, 0);
            Assert.True(len > 0);
            Assert.True(BasisAvatarStreamCodec.Decode(rx, scratch, 0, len, (byte)frame, outFull));
            AssertStatesEqual(tx, rx, $"frame {frame}");
        }
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void StaticPose_ConvergesToItExactly(BitQuality q)
    {
        var rng = new Random(200 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        byte[] pose = S.MakeRealisticPayload(q, rng);      // deliberately far from the seed
        var (tx, rx, scratch, outFull) = Pair(q, kf);

        int budget = 2 * BasisAvatarStreamCodec.SweepPeriod(q) + 16;
        for (int frame = 0; frame < budget; frame++)
        {
            int len = BasisAvatarStreamCodec.Encode(tx, pose, (byte)frame, scratch, 0);
            Assert.True(BasisAvatarStreamCodec.Decode(rx, scratch, 0, len, (byte)frame, outFull));
        }
        Assert.Equal(pose, outFull);
    }

    /// <summary>
    /// The reference implementation's "link broken" toggle. No feedback channel exists, so the sender
    /// cannot know anything was lost — recovery has to be a property of the encoding alone.
    /// </summary>
    [Theory]
    [InlineData(BitQuality.VeryLow, 30)]
    [InlineData(BitQuality.Low, 30)]
    [InlineData(BitQuality.Medium, 60)]
    [InlineData(BitQuality.High, 60)]
    [InlineData(BitQuality.High, 500)]
    public void ConvergesExactly_AfterTotalLinkLoss(BitQuality q, int outageFrames)
    {
        var rng = new Random(300 + (int)q + outageFrames);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        var (tx, rx, scratch, outFull) = Pair(q, kf);

        int frame = 0;
        byte[] pose = (byte[])kf.Clone();

        // Normal operation, then the link goes down: the sender keeps encoding (advancing its model
        // and the sequence) and nothing is delivered.
        for (int i = 0; i < 20; i++, frame++)
        {
            pose = Nudge(pose, q, rng, 4);
            int len = BasisAvatarStreamCodec.Encode(tx, pose, (byte)frame, scratch, 0);
            Assert.True(BasisAvatarStreamCodec.Decode(rx, scratch, 0, len, (byte)frame, outFull));
        }
        for (int i = 0; i < outageFrames; i++, frame++)
        {
            pose = Nudge(pose, q, rng, 4);
            BasisAvatarStreamCodec.Encode(tx, pose, (byte)frame, scratch, 0);   // dropped
        }
        Assert.NotEqual(pose, outFull);   // the receiver really is stale

        // Link restored, pose settles. Convergence must be exact within one sweep pass plus the few
        // frames the residual loop needs to bring the sender's model onto the true pose.
        int budget = 2 * BasisAvatarStreamCodec.SweepPeriod(q) + 16;
        for (int i = 0; i < budget; i++, frame++)
        {
            int len = BasisAvatarStreamCodec.Encode(tx, pose, (byte)frame, scratch, 0);
            Assert.True(BasisAvatarStreamCodec.Decode(rx, scratch, 0, len, (byte)frame, outFull));
        }
        Assert.Equal(pose, outFull);
    }

    [Theory]
    [InlineData(BitQuality.High)]
    [InlineData(BitQuality.Medium)]
    public void ConvergesExactly_UnderSustainedRandomLoss(BitQuality q)
    {
        var rng = new Random(400 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        var (tx, rx, scratch, outFull) = Pair(q, kf);
        byte[] pose = (byte[])kf.Clone();
        int frame = 0;

        // 30% loss while moving.
        for (int i = 0; i < 300; i++, frame++)
        {
            pose = Nudge(pose, q, rng, 3);
            int len = BasisAvatarStreamCodec.Encode(tx, pose, (byte)frame, scratch, 0);
            if (rng.NextDouble() >= 0.30)
                Assert.True(BasisAvatarStreamCodec.Decode(rx, scratch, 0, len, (byte)frame, outFull));
        }
        // Motion and loss both stop; it must settle exactly rather than to something merely close.
        for (int i = 0; i < 2 * BasisAvatarStreamCodec.SweepPeriod(q) + 16; i++, frame++)
        {
            int len = BasisAvatarStreamCodec.Encode(tx, pose, (byte)frame, scratch, 0);
            Assert.True(BasisAvatarStreamCodec.Decode(rx, scratch, 0, len, (byte)frame, outFull));
        }
        Assert.Equal(pose, outFull);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void FrameLength_AgreesWithEncode(BitQuality q)
    {
        var rng = new Random(500 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        var (tx, _, scratch, _) = Pair(q, kf);
        byte[] pose = (byte[])kf.Clone();
        for (int frame = 0; frame < 100; frame++)
        {
            pose = Nudge(pose, q, rng, 5);
            int len = BasisAvatarStreamCodec.Encode(tx, pose, (byte)frame, scratch, 0);
            Assert.Equal(len, BasisAvatarStreamCodec.FrameLength(scratch, 0, scratch.Length, q));
            Assert.Equal(len, BasisAvatarStreamCodec.FrameLength(scratch, 0, len, q));
        }
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void MaxFrameSize_IsAnUpperBound(BitQuality q)
    {
        var rng = new Random(600 + (int)q);
        int max = BasisAvatarStreamCodec.MaxFrameSize(q);
        var scratch = new byte[max];
        var tx = new BasisAvatarStreamState(q);
        tx.SeedFrom(S.MakePayload(q, rng));
        for (int i = 0; i < 500; i++)
        {
            // Uncorrelated poses every frame: the pathological case for a predictive codec.
            int len = BasisAvatarStreamCodec.Encode(tx, S.MakePayload(q, rng), (byte)i, scratch, 0);
            Assert.InRange(len, 1, max);
        }
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void Fuzz_GarbageFrames_NeverThrow_AndNeverCorruptState(BitQuality q)
    {
        var rng = new Random(700 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        var rx = new BasisAvatarStreamState(q);
        rx.SeedFrom(kf);
        var before = (uint[])rx.Estimate.Clone();
        var outFull = new byte[S.PayloadSize(q)];

        for (int i = 0; i < 20000; i++)
        {
            var garbage = new byte[rng.Next(0, BasisAvatarStreamCodec.MaxFrameSize(q) + 8)];
            rng.NextBytes(garbage);
            BasisAvatarStreamCodec.FrameLength(garbage, 0, garbage.Length, q);
            if (!BasisAvatarStreamCodec.Decode(rx, garbage, 0, garbage.Length, (byte)i, outFull))
                Assert.Equal(before, rx.Estimate);   // a rejected frame must not half-advance the state
            else
                before = (uint[])rx.Estimate.Clone();
        }
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void TruncatedFrames_AreRejected_WithoutThrowing(BitQuality q)
    {
        var rng = new Random(800 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        var (tx, rx, scratch, outFull) = Pair(q, kf);
        byte[] pose = (byte[])kf.Clone();
        for (int i = 0; i < 500; i++)
        {
            pose = Nudge(pose, q, rng, 4);
            int len = BasisAvatarStreamCodec.Encode(tx, pose, (byte)i, scratch, 0);
            int cut = rng.Next(0, len);
            var snapshot = (uint[])rx.Estimate.Clone();
            Assert.False(BasisAvatarStreamCodec.Decode(rx, scratch, 0, cut, (byte)i, outFull));
            Assert.Equal(snapshot, rx.Estimate);
        }
    }

    [Fact]
    public void PrintStreamBandwidthTable()
    {
        var rng = new Random(2025);
        _out.WriteLine("Stream codec frame size vs the keyframe+delta scheme it replaces on the uplink.");
        _out.WriteLine("keyframe wire = 3 + payload; the keyframe+delta column amortises one 0.5 s keyframe");
        _out.WriteLine("over 5.5 frames at the ~11 Hz client publish rate.");
        _out.WriteLine("");

        foreach (var q in S.AllQualities)
        {
            _out.WriteLine($"== {q}  (payload = {S.PayloadSize(q)} B, sweep period = {BasisAvatarStreamCodec.SweepPeriod(q)} frames) ==");
            _out.WriteLine("  motion/frame | stream B | keyframe+delta B | saving");
            foreach (int steps in new[] { 0, 1, 2, 4, 8, 32 })
            {
                byte[] kf = S.MakeRealisticPayload(q, rng);
                var tx = new BasisAvatarStreamState(q);
                tx.SeedFrom(kf);
                var scratch = new byte[BasisAvatarStreamCodec.MaxFrameSize(q)];
                var deltaDst = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(q)];

                byte[] pose = (byte[])kf.Clone();
                byte[] baseline = (byte[])kf.Clone();
                long streamSum = 0, kfDeltaSum = 0;
                const int frames = 220;
                for (int f = 0; f < frames; f++)
                {
                    pose = Nudge(pose, q, rng, steps);
                    streamSum += BasisAvatarStreamCodec.Encode(tx, pose, (byte)f, scratch, 0);

                    // Keyframe every 5.5 frames (0.5 s at 11 Hz), deltas against it in between.
                    if (f % 6 == 0) { baseline = (byte[])pose.Clone(); kfDeltaSum += S.PayloadSize(q); }
                    else
                    {
                        int d = BasisAvatarDeltaCompression.BuildDelta(baseline, pose, q, deltaDst, 0);
                        kfDeltaSum += Math.Min(d, S.PayloadSize(q));
                    }
                }
                double s = (double)streamSum / frames, k = (double)kfDeltaSum / frames;
                _out.WriteLine($"  +-{steps,-10} | {s,8:F1} | {k,16:F1} | {1.0 - s / k,6:P1}");
            }
            _out.WriteLine("");
        }
    }

    /// <summary>Perturbs every quantized channel by up to +-steps, leaving categorical channels alone.</summary>
    private static byte[] Nudge(byte[] payload, BitQuality q, Random rng, int steps)
    {
        var next = (byte[])payload.Clone();
        if (steps <= 0) return next;
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
