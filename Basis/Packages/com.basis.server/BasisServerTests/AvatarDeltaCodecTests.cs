using Basis.Network.Core.Compression;
using Xunit;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;
using S = BasisServerTests.DeltaTestSupport;

namespace BasisServerTests;

/// <summary>Core BuildDelta / TryApplyDelta / DeltaBodyLength round-trip behavior.</summary>
public class AvatarDeltaCodecTests
{
    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void RoundTrip_RandomPayloads(BitQuality q)
    {
        var rng = new Random(1000 + (int)q);
        for (int i = 0; i < 500; i++)
            S.AssertRoundTrip(S.MakePayload(q, rng), S.MakePayload(q, rng), q);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void RoundTrip_RealisticQuaternionPayloads(BitQuality q)
    {
        var rng = new Random(2000 + (int)q);
        for (int i = 0; i < 500; i++)
            S.AssertRoundTrip(S.MakeRealisticPayload(q, rng), S.MakeRealisticPayload(q, rng), q);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void RoundTrip_RealisticSmallMotion(BitQuality q)
    {
        // A small pose nudge: re-encode each bone from a slightly rotated quaternion. Many bones
        // quantize to the same bits (unchanged), which is the common in-session case — and the
        // ones that do move mostly move by a step or two, which is what the residual coding is for.
        var rng = new Random(3000 + (int)q);
        var bpc = S.Bpc(q);
        for (int iter = 0; iter < 100; iter++)
        {
            byte[] kf = S.MakeRealisticPayload(q, rng);
            byte[] cur = (byte[])kf.Clone();
            for (int s = 0; s < S.WireBoneSlots; s++)
            {
                // Decode what the keyframe actually holds, rotate it slightly, re-encode. Writing a
                // fresh random quaternion instead (as this test used to) makes every bone a full
                // change, which is the opposite of the case being covered.
                BasisBoneRotationCompression.DecodeSmallestThree(
                    S.GetBone(kf, q, s), bpc[s], out float x, out float y, out float z, out float w,
                    BasisBoneRotationCompression.MAX_COMPONENT[s]);
                float nudge = 0.002f;
                float nx = x + (float)(rng.NextDouble() * 2 - 1) * nudge;
                float ny = y + (float)(rng.NextDouble() * 2 - 1) * nudge;
                float nz = z + (float)(rng.NextDouble() * 2 - 1) * nudge;
                float nw = w + (float)(rng.NextDouble() * 2 - 1) * nudge;
                ulong packed = BasisBoneRotationCompression.EncodeSmallestThree(nx, ny, nz, nw, bpc[s], BasisBoneRotationCompression.MAX_COMPONENT[s]);
                S.SetBone(cur, q, s, packed);
            }
            S.AssertRoundTrip(kf, cur, q);
        }
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void BuildDelta_IsDeterministic(BitQuality q)
    {
        var rng = new Random(42);
        byte[] kf = S.MakePayload(q, rng);
        byte[] cur = S.MakePayload(q, rng);
        var a = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(q)];
        var b = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(q)];
        int la = BasisAvatarDeltaCompression.BuildDelta(kf, cur, q, a, 0);
        int lb = BasisAvatarDeltaCompression.BuildDelta(kf, cur, q, b, 0);
        Assert.Equal(la, lb);
        Assert.Equal(a.AsSpan(0, la).ToArray(), b.AsSpan(0, lb).ToArray());
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void BuildDelta_HonorsDstStartOffset(BitQuality q)
    {
        var rng = new Random(7);
        byte[] kf = S.MakePayload(q, rng);
        byte[] cur = S.MakePayload(q, rng);
        const int start = 37;
        var dst = new byte[start + BasisAvatarDeltaCompression.MaxDeltaSize(q)];
        for (int i = 0; i < start; i++) dst[i] = 0xEE; // sentinel that must survive
        int len = BasisAvatarDeltaCompression.BuildDelta(kf, cur, q, dst, start);
        Assert.True(len > 0);
        for (int i = 0; i < start; i++) Assert.Equal((byte)0xEE, dst[i]);
        Assert.Equal(len, BasisAvatarDeltaCompression.DeltaBodyLength(dst, start, len, q));
        var recon = new byte[S.PayloadSize(q)];
        Assert.True(BasisAvatarDeltaCompression.TryApplyDelta(kf, dst, start, len, q, recon));
        Assert.Equal(cur, recon);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void TryApplyDelta_IsIdempotent(BitQuality q)
    {
        var rng = new Random(11);
        byte[] kf = S.MakePayload(q, rng);
        byte[] cur = S.MakePayload(q, rng);
        var dst = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(q)];
        int len = BasisAvatarDeltaCompression.BuildDelta(kf, cur, q, dst, 0);
        var r1 = new byte[S.PayloadSize(q)];
        var r2 = new byte[S.PayloadSize(q)];
        Assert.True(BasisAvatarDeltaCompression.TryApplyDelta(kf, dst, 0, len, q, r1));
        Assert.True(BasisAvatarDeltaCompression.TryApplyDelta(kf, dst, 0, len, q, r2));
        Assert.Equal(r1, r2);
        Assert.Equal(cur, r1);
    }

    [Fact]
    public void PayloadSizes_MatchExpectedLadder()
    {
        // Position is int24 mm (9B) at every tier and the hips tail is 21B (13-bit hips delta).
        Assert.Equal(82, S.PayloadSize(BitQuality.VeryLow));  // 9 pos + 52 rot + 21 tail
        Assert.Equal(92, S.PayloadSize(BitQuality.Low));      // 9 pos + 62 rot + 21 tail
        Assert.Equal(108, S.PayloadSize(BitQuality.Medium));  // 9 pos + 78 rot + 21 tail
        Assert.Equal(177, S.PayloadSize(BitQuality.High));    // 9 pos + 112 rot + 21 tail + 35 effector
        Assert.Equal(5, BasisAvatarDeltaCompression.DirtyMaskBytes);
        Assert.Equal(37, BasisAvatarDeltaCompression.FieldCount);
    }

    [Fact]
    public void QuantizedPosition_RoundTripsWithinHalfMillimetre()
    {
        var buf = new byte[BasisAvatarBitPacking.WritePosition];
        foreach (float v in new[] { 0f, 0.001f, -0.001f, 1.2345f, -987.654f, 8000f, -8000f })
        {
            BasisAvatarBitPacking.EncodeAxisMm(v, buf, 0);
            Assert.Equal(v, BasisAvatarBitPacking.DecodeAxisMm(buf, 0), 0.0006f);
        }

        // Out-of-range and non-finite inputs clamp instead of wrapping.
        BasisAvatarBitPacking.EncodeAxisMm(99999f, buf, 0);
        Assert.Equal(8388.607f, BasisAvatarBitPacking.DecodeAxisMm(buf, 0), 0.001f);
        BasisAvatarBitPacking.EncodeAxisMm(-99999f, buf, 0);
        Assert.Equal(-8388.607f, BasisAvatarBitPacking.DecodeAxisMm(buf, 0), 0.001f);
        BasisAvatarBitPacking.EncodeAxisMm(float.NaN, buf, 0);
        Assert.Equal(0f, BasisAvatarBitPacking.DecodeAxisMm(buf, 0), 0.0001f);

        // The whole-block helpers lay the three axes out at 0/3/6 and read them back the same way.
        var block = new byte[BasisAvatarBitPacking.WritePosition];
        BasisAvatarBitPacking.EncodePosition(1.5f, -2.25f, 300.125f, block, 0);
        BasisAvatarBitPacking.DecodePosition(block, 0, out float px, out float py, out float pz);
        Assert.Equal(1.5f, px, 0.0006f);
        Assert.Equal(-2.25f, py, 0.0006f);
        Assert.Equal(300.125f, pz, 0.0006f);
        Assert.Equal(1.5f, BasisAvatarBitPacking.DecodeAxisMm(block, 0), 0.0006f);
        Assert.Equal(-2.25f, BasisAvatarBitPacking.DecodeAxisMm(block, 3), 0.0006f);
        Assert.Equal(300.125f, BasisAvatarBitPacking.DecodeAxisMm(block, 6), 0.0006f);
    }

    [Fact]
    public void HipsDelta_RoundTripsWithinAQuarterMillimetre()
    {
        var buf = new byte[BasisAvatarBitPacking.WriteHipsDelta];
        foreach (var (x, y, z) in new[]
        {
            (0f, 0f, 0f), (0.001f, -0.001f, 0.5f), (-0.25f, 0.75f, -0.999f),
            (1f, -1f, 0f), (0.3333f, -0.6667f, 0.1234f),
        })
        {
            BasisAvatarBitPacking.EncodeHipsDelta(x, y, z, buf, 0);
            BasisAvatarBitPacking.DecodeHipsDelta(buf, 0, out float ox, out float oy, out float oz);
            Assert.Equal(x, ox, 0.00025f);
            Assert.Equal(y, oy, 0.00025f);
            Assert.Equal(z, oz, 0.00025f);
        }

        // An all-zero field must decode to a zero delta — the console test client leaves it unwritten.
        Array.Clear(buf);
        BasisAvatarBitPacking.DecodeHipsDelta(buf, 0, out float zx, out float zy, out float zz);
        Assert.Equal(0f, zx);
        Assert.Equal(0f, zy);
        Assert.Equal(0f, zz);

        // Out-of-range and non-finite inputs clamp to the envelope rather than wrapping.
        BasisAvatarBitPacking.EncodeHipsDelta(99f, -99f, float.NaN, buf, 0);
        BasisAvatarBitPacking.DecodeHipsDelta(buf, 0, out float cx, out float cy, out float cz);
        Assert.Equal(BasisAvatarBitPacking.HipsDeltaRange, cx, 0.00025f);
        Assert.Equal(-BasisAvatarBitPacking.HipsDeltaRange, cy, 0.00025f);
        Assert.Equal(0f, cz);

        // Encoding overwrites the whole field: no residue survives from a previous value.
        BasisAvatarBitPacking.EncodeHipsDelta(0.9f, -0.9f, 0.9f, buf, 0);
        BasisAvatarBitPacking.EncodeHipsDelta(0f, 0f, 0f, buf, 0);
        Assert.All(buf, b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void MaxDeltaSize_IsUpperBound(BitQuality q)
    {
        var rng = new Random(555 + (int)q);
        int max = BasisAvatarDeltaCompression.MaxDeltaSize(q);
        var dst = new byte[max];
        for (int i = 0; i < 300; i++)
        {
            int len = BasisAvatarDeltaCompression.BuildDelta(S.MakePayload(q, rng), S.MakePayload(q, rng), q, dst, 0);
            Assert.InRange(len, BasisAvatarDeltaCompression.DirtyMaskBytes, max);
        }
        // Raw mode caps each field at its own verbatim width, so the worst case is the mask plus one
        // mode bit per field plus the payload itself — five bytes over the old fixed-width bound.
        int expected = BasisAvatarDeltaCompression.DirtyMaskBytes
                     + ((BasisAvatarDeltaCompression.FieldCount + S.PayloadSize(q) * 8 + 7) >> 3);
        Assert.Equal(expected, max);
        Assert.InRange(max - S.PayloadSize(q), 0, BasisAvatarDeltaCompression.DirtyMaskBytes + 5);
    }
}
