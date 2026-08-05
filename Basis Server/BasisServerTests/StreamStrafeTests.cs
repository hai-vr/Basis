using Basis.Network.Core.Compression;
using Xunit;
using Xunit.Abstractions;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;
using S = BasisServerTests.DeltaTestSupport;

namespace BasisServerTests;

/// <summary>
/// Lateral translation (strafing) through both codecs, checking for VELOCITY ripple as distinct from
/// positional error. A constant-speed strafe has zero true acceleration, so any frame-to-frame
/// variation in the reconstructed step size is introduced by the pipeline — and a reconstruction can
/// be perfectly accurate in position while still stuttering in speed, which is what a viewer notices.
///
/// Position is 3 x signed int24 millimetres, so the quantizer's own floor is 1 mm; that is the only
/// ripple that should survive, and it should never exceed one step.
/// </summary>
public class StreamStrafeTests
{
    private readonly ITestOutputHelper _out;
    public StreamStrafeTests(ITestOutputHelper output) => _out = output;

    const BitQuality Q = BitQuality.High;

    sealed class Strafe
    {
        public int InexactFrames;
        public double MaxStepMm, MinStepMm, MeanStepMm;
        public double MaxStepDeviationMm;   // worst departure from the constant true step
        public double AvgBytes;
        public int RawModeFrames;           // frames where the position field fell back to verbatim
    }

    /// <summary>Runs a constant-velocity lateral slide through the stream codec.</summary>
    static Strafe RunStream(double metresPerSecond, double hz, int frames)
    {
        var rng = new Random(3);
        byte[] pose = S.MakeRealisticPayload(Q, rng);
        var tx = new BasisAvatarStreamState(Q);
        var rx = new BasisAvatarStreamState(Q);

        BasisAvatarBitPacking.EncodePosition(0f, 1.6f, 0f, pose, 0);
        tx.SeedFrom(pose); rx.SeedFrom(pose);

        var scratch = new byte[BasisAvatarStreamCodec.MaxFrameSize(Q)];
        var outFull = new byte[BasisAvatarDeltaCompression.PayloadSize(Q)];
        int payload = BasisAvatarDeltaCompression.PayloadSize(Q);

        double perFrame = metresPerSecond / hz;
        var r = new Strafe { MinStepMm = double.MaxValue };
        double prevX = 0; long byteSum = 0; int n = 0; double stepSum = 0;

        for (int f = 1; f <= frames; f++)
        {
            float x = (float)(perFrame * f);
            BasisAvatarBitPacking.EncodePosition(x, 1.6f, 0f, pose, 0);

            int len = BasisAvatarStreamCodec.Encode(tx, pose, (byte)f, scratch, 0);
            byteSum += len;
            Assert.True(BasisAvatarStreamCodec.Decode(rx, scratch, 0, len, (byte)f, outFull));

            if (!outFull.AsSpan(0, payload).SequenceEqual(pose.AsSpan(0, payload))) r.InexactFrames++;

            BasisAvatarBitPacking.DecodePosition(outFull, 0, out float gx, out _, out _);
            if (f > 1)
            {
                double stepMm = (gx - prevX) * 1000.0;
                r.MaxStepMm = Math.Max(r.MaxStepMm, stepMm);
                r.MinStepMm = Math.Min(r.MinStepMm, stepMm);
                r.MaxStepDeviationMm = Math.Max(r.MaxStepDeviationMm, Math.Abs(stepMm - perFrame * 1000.0));
                stepSum += stepMm; n++;
            }
            prevX = gx;
        }
        r.MeanStepMm = stepSum / n;
        r.AvgBytes = (double)byteSum / frames;
        return r;
    }

    /// <summary>Same slide through the keyframe-relative delta codec (the downlink path).</summary>
    static Strafe RunDelta(double metresPerSecond, double hz, int frames, int keyframeEvery)
    {
        var rng = new Random(3);
        byte[] pose = S.MakeRealisticPayload(Q, rng);
        BasisAvatarBitPacking.EncodePosition(0f, 1.6f, 0f, pose, 0);
        byte[] baseline = (byte[])pose.Clone();

        var dst = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(Q)];
        var outFull = new byte[BasisAvatarDeltaCompression.PayloadSize(Q)];
        int payload = BasisAvatarDeltaCompression.PayloadSize(Q);

        double perFrame = metresPerSecond / hz;
        var r = new Strafe { MinStepMm = double.MaxValue };
        double prevX = 0; long byteSum = 0; int n = 0; double stepSum = 0;

        for (int f = 1; f <= frames; f++)
        {
            float x = (float)(perFrame * f);
            BasisAvatarBitPacking.EncodePosition(x, 1.6f, 0f, pose, 0);

            if (f % keyframeEvery == 0)
            {
                baseline = (byte[])pose.Clone();
                Array.Copy(pose, outFull, payload);
                byteSum += payload;
            }
            else
            {
                int len = BasisAvatarDeltaCompression.BuildDelta(baseline, pose, Q, dst, 0);
                byteSum += len;
                Assert.True(BasisAvatarDeltaCompression.TryApplyDelta(baseline, dst, 0, len, Q, outFull));
            }
            if (!outFull.AsSpan(0, payload).SequenceEqual(pose.AsSpan(0, payload))) r.InexactFrames++;

            BasisAvatarBitPacking.DecodePosition(outFull, 0, out float gx, out _, out _);
            if (f > 1)
            {
                double stepMm = (gx - prevX) * 1000.0;
                r.MaxStepMm = Math.Max(r.MaxStepMm, stepMm);
                r.MinStepMm = Math.Min(r.MinStepMm, stepMm);
                r.MaxStepDeviationMm = Math.Max(r.MaxStepDeviationMm, Math.Abs(stepMm - perFrame * 1000.0));
                stepSum += stepMm; n++;
            }
            prevX = gx;
        }
        r.MeanStepMm = stepSum / n;
        r.AvgBytes = (double)byteSum / frames;
        return r;
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(3.0)]
    [InlineData(6.0)]
    public void Strafe_IsBitExact_AndHasNoVelocityRipple_Stream(double mps)
    {
        var r = RunStream(mps, 11.0, 200);
        Assert.Equal(0, r.InexactFrames);
        // The only ripple that may survive is the position field's own 1 mm quantization.
        Assert.True(r.MaxStepDeviationMm <= 1.0,
            $"{mps} m/s: reconstructed step varied by up to {r.MaxStepDeviationMm:F3} mm from the " +
            $"constant true step — more than the 1 mm position quantum");
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(1.5)]
    [InlineData(6.0)]
    public void Strafe_IsBitExact_AndHasNoVelocityRipple_Delta(double mps)
    {
        var r = RunDelta(mps, 11.0, 200, keyframeEvery: 6);
        Assert.Equal(0, r.InexactFrames);
        Assert.True(r.MaxStepDeviationMm <= 1.0,
            $"{mps} m/s: reconstructed step varied by up to {r.MaxStepDeviationMm:F3} mm");
    }

    [Fact]
    public void PrintStrafeTable()
    {
        _out.WriteLine("Constant-velocity lateral slide at 11 Hz, High quality.");
        _out.WriteLine("True acceleration is zero, so any spread between min and max step is pipeline ripple.");
        _out.WriteLine("Position is signed int24 millimetres, so a 1 mm spread is the quantizer floor.");
        _out.WriteLine("");
        _out.WriteLine("  m/s  | true step | min step | max step | worst dev | inexact | bytes");
        foreach (double mps in new[] { 0.3, 1.0, 1.5, 3.0, 6.0 })
        {
            var r = RunStream(mps, 11.0, 200);
            _out.WriteLine($"  {mps,4:F1} | {mps / 11.0 * 1000,9:F2} | {r.MinStepMm,8:F2} | {r.MaxStepMm,8:F2} | " +
                           $"{r.MaxStepDeviationMm,9:F3} | {r.InexactFrames,7} | {r.AvgBytes,5:F1}");
        }
    }
}
