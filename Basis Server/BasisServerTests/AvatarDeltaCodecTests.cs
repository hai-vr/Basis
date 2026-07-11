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
        // quantize to the same bits (unchanged), which is the common in-session case.
        var rng = new Random(3000 + (int)q);
        var bpc = S.Bpc(q);
        var offs = S.BoneBitOffsets(q);
        for (int iter = 0; iter < 100; iter++)
        {
            byte[] kf = S.MakeRealisticPayload(q, rng);
            byte[] cur = (byte[])kf.Clone();
            for (int s = 0; s < bpc.Length; s++)
            {
                var (x, y, z, w) = S.RandomQuat(rng);
                float nudge = 0.02f;
                float nx = x + (float)(rng.NextDouble() * 2 - 1) * nudge;
                float ny = y + (float)(rng.NextDouble() * 2 - 1) * nudge;
                float nz = z + (float)(rng.NextDouble() * 2 - 1) * nudge;
                float nw = w + (float)(rng.NextDouble() * 2 - 1) * nudge;
                ulong packed = BasisBoneRotationCompression.EncodeSmallestThree(nx, ny, nz, nw, bpc[s], BasisBoneRotationCompression.MAX_COMPONENT[s]);
                BasisBoneRotationCompression.WriteBits(cur, S.BoneBaseBit + offs[s], packed, 2 + 3 * bpc[s]);
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
        Assert.Equal(112, S.PayloadSize(BitQuality.VeryLow));
        Assert.Equal(131, S.PayloadSize(BitQuality.Low));
        Assert.Equal(156, S.PayloadSize(BitQuality.Medium));
        Assert.Equal(182, S.PayloadSize(BitQuality.High));
        Assert.Equal(7, BasisAvatarDeltaCompression.DirtyMaskBytes);
        Assert.Equal(56, BasisAvatarDeltaCompression.FieldCount);
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
        // The bound is exactly mask + full payload.
        Assert.Equal(BasisAvatarDeltaCompression.DirtyMaskBytes + S.PayloadSize(q), max);
    }
}
