using Basis.Network.Core.Compression;
using Xunit;
using Xunit.Abstractions;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;
using S = BasisServerTests.DeltaTestSupport;

namespace BasisServerTests;

/// <summary>
/// Fast-rotation fidelity through the stream codec — the regression that caught the snapping.
///
/// A bone rotating quickly crosses smallest-three INDEX FLIPS: the 2-bit index selects which
/// quaternion component was dropped, so when it changes, the other three change meaning entirely.
/// While residuals were companded, a residual measured across a flip was measured against a stale
/// mapping and produced up to a <b>180° single-frame error</b> — the bone pointing backwards for one
/// frame, at rotation rates as low as 30°/s, settling over 2–3 frames. That is what read as snapping,
/// and as jitter when the amplitude was smaller.
///
/// Residuals are exact now and every field falls back to verbatim when that is shorter, so the
/// reconstruction is bit-identical regardless of speed. These tests hold it there.
/// </summary>
public class StreamFastRotationTests
{
    private readonly ITestOutputHelper _out;
    public StreamFastRotationTests(ITestOutputHelper output) => _out = output;

    const BitQuality Q = BitQuality.High;
    const int Slot = 5;   // an upper arm: 12 bits per component, full InvSqrt2 range

    static (float x, float y, float z, float w) AxisAngle(float ax, float ay, float az, double deg)
    {
        double h = deg * Math.PI / 180.0 * 0.5;
        double s = Math.Sin(h);
        return ((float)(ax * s), (float)(ay * s), (float)(az * s), (float)Math.Cos(h));
    }

    /// <summary>Angle between two unit quaternions in degrees, via 2*atan2(|v|,|w|) on the relative
    /// rotation — acos(|dot|) loses half its digits near dot≈1 and reports conditioning noise.</summary>
    static double AngleBetween((float x, float y, float z, float w) a, (float x, float y, float z, float w) b)
    {
        double rw = (double)a.w * b.w + a.x * b.x + a.y * b.y + a.z * b.z;
        double rx = (double)a.w * b.x - a.x * b.w - a.y * b.z + a.z * b.y;
        double ry = (double)a.w * b.y + a.x * b.z - a.y * b.w - a.z * b.x;
        double rz = (double)a.w * b.z - a.x * b.y + a.y * b.x - a.z * b.w;
        return 2.0 * Math.Atan2(Math.Sqrt(rx * rx + ry * ry + rz * rz), Math.Abs(rw)) * 180.0 / Math.PI;
    }

    sealed class Run
    {
        public double MaxErr, MeanErr, MaxStepJitter;
        public int Flips, Frames;
        public double AvgBytes;
        /// <summary>Frames whose decoded payload was not byte-identical to the sender's.</summary>
        public int InexactFrames;
        public int FirstInexactFrame = -1;
    }

    static Run Rotate(double degPerSecond, double hz, int frames)
    {
        var rng = new Random(7);
        byte[] pose = S.MakeRealisticPayload(Q, rng);
        var bpc = S.Bpc(Q);
        float maxComp = BasisBoneRotationCompression.MAX_COMPONENT[Slot];

        var tx = new BasisAvatarStreamState(Q);
        var rx = new BasisAvatarStreamState(Q);
        var start = AxisAngle(0, 1, 0, 0);
        S.SetBone(pose, Q, Slot, BasisBoneRotationCompression.EncodeSmallestThree(
            start.x, start.y, start.z, start.w, bpc[Slot], maxComp));
        tx.SeedFrom(pose); rx.SeedFrom(pose);

        var scratch = new byte[BasisAvatarStreamCodec.MaxFrameSize(Q)];
        var outFull = new byte[BasisAvatarDeltaCompression.PayloadSize(Q)];
        var layout = S.Layout(Q);
        int field = BasisAvatarDeltaCompression.BoneFieldStart + Slot;
        var comp0 = layout.Channels[layout.FieldChannelStart(field) + 1];

        double degPerFrame = degPerSecond / hz, errSum = 0;
        var r = new Run { Frames = frames };
        ulong prevPacked = S.GetBone(pose, Q, Slot);
        long byteSum = 0;
        int prevStepErr = 0;

        for (int f = 1; f <= frames; f++)
        {
            var truth = AxisAngle(0, 1, 0, degPerFrame * f);
            ulong packed = BasisBoneRotationCompression.EncodeSmallestThree(
                truth.x, truth.y, truth.z, truth.w, bpc[Slot], maxComp);
            S.SetBone(pose, Q, Slot, packed);
            if ((packed & 3UL) != (prevPacked & 3UL)) r.Flips++;
            prevPacked = packed;

            int len = BasisAvatarStreamCodec.Encode(tx, pose, (byte)f, scratch, 0);
            byteSum += len;
            Assert.True(BasisAvatarStreamCodec.Decode(rx, scratch, 0, len, (byte)f, outFull));

            // The property that matters: the receiver reconstructed the sender's payload exactly.
            int payload = BasisAvatarDeltaCompression.PayloadSize(Q);
            if (!outFull.AsSpan(0, payload).SequenceEqual(pose.AsSpan(0, payload)))
            {
                r.InexactFrames++;
                if (r.FirstInexactFrame < 0) r.FirstInexactFrame = f;
            }

            BasisBoneRotationCompression.DecodeSmallestThree(S.GetBone(outFull, Q, Slot), bpc[Slot],
                out float gx, out float gy, out float gz, out float gw, maxComp);
            BasisBoneRotationCompression.DecodeSmallestThree(packed, bpc[Slot],
                out float qx, out float qy, out float qz, out float qw, maxComp);

            double err = AngleBetween((qx, qy, qz, qw), (gx, gy, gz, gw));
            errSum += err;
            if (err > r.MaxErr) r.MaxErr = err;

            // Jitter: a constant-rate rotation has zero true jerk, so any frame-to-frame movement in
            // the reconstruction error is codec noise.
            int stepErr = (int)BasisAvatarDeltaCompression.ReadChannel(outFull, comp0)
                        - (int)BasisAvatarDeltaCompression.ReadChannel(pose, comp0);
            if (f > 1) r.MaxStepJitter = Math.Max(r.MaxStepJitter, Math.Abs(stepErr - prevStepErr));
            prevStepErr = stepErr;
        }
        r.MeanErr = errSum / frames;
        r.AvgBytes = (double)byteSum / frames;
        return r;
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(360)]
    [InlineData(720)]
    [InlineData(1080)]
    public void FastRotation_ReconstructsExactly_AcrossIndexFlips(double degPerSecond)
    {
        var r = Rotate(degPerSecond, 11.0, 120);

        // The test is only meaningful if it actually crosses index flips.
        Assert.True(r.Flips > 0, $"{degPerSecond} deg/s produced no smallest-three index flips");

        Assert.True(r.InexactFrames == 0,
            $"{degPerSecond} deg/s: {r.InexactFrames} of {r.Frames} frames were not byte-exact " +
            $"(first at frame {r.FirstInexactFrame}), over {r.Flips} index flips. Max angular error " +
            $"{r.MaxErr:E3}°. The stream codec must reconstruct the sender's payload exactly.");
        Assert.Equal(0.0, r.MaxStepJitter);
    }

    [Fact]
    public void PrintFidelityTable()
    {
        _out.WriteLine("One upper-arm bone rotating about Y at 11 Hz, High quality (12 bits/component).");
        _out.WriteLine("Angular error is measured against the QUANTIZED truth, so the quantizer's own");
        _out.WriteLine("floor (~0.04°/step) is excluded — this is codec error alone.");
        _out.WriteLine("");
        _out.WriteLine("  deg/s | deg/frame | index flips | mean err | max err | max jitter | bytes/frame");
        foreach (double sp in new double[] { 30, 90, 180, 360, 720, 1080 })
        {
            var r = Rotate(sp, 11.0, 120);
            _out.WriteLine($"  {sp,5:F0} | {sp / 11.0,9:F2} | {r.Flips,11} | {r.MeanErr,8:F3} | " +
                           $"{r.MaxErr,7:F3} | {r.MaxStepJitter,10:F0} | {r.AvgBytes,11:F1}");
        }
        _out.WriteLine("");
        _out.WriteLine("Before residuals were made exact, the same runs measured up to 180.0° of");
        _out.WriteLine("single-frame error on every index flip, settling over 2-3 frames.");
    }
}
