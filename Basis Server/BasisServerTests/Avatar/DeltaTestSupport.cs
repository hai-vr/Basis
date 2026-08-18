using Basis.Network.Core.Compression;
using Xunit;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;

namespace BasisServerTests;

/// <summary>
/// Shared helpers for the avatar delta codec tests: payload construction (random and realistic),
/// field geometry accessors, and build/apply round-trip assertions.
///
/// "Bone" here means a ROTATION WIRE FIELD, of which there are
/// <see cref="BasisBoneRotationCompression.RotationFieldCount"/> = 31: the 21 explicit bone slots
/// followed by the 10 finger curl/splay channels. It does NOT mean one of the 51 legacy bone slots —
/// since v47 the wire has not carried thirty of those as rotations at all, and building payloads
/// against the old 51-slot geometry wrote past the end of every sub-High payload.
/// </summary>
public static class DeltaTestSupport
{
    public static readonly BitQuality[] AllQualities =
        { BitQuality.VeryLow, BitQuality.Low, BitQuality.Medium, BitQuality.High };

    /// <summary>Number of addressable rotation wire fields (21 bone slots + 10 finger channels).</summary>
    public const int BoneCount = BasisBoneRotationCompression.RotationFieldCount; // 31
    /// <summary>Of those, how many are true bone slots carrying a smallest-three rotation.</summary>
    public const int WireBoneSlots = BasisBoneRotationCompression.WireBoneSlotCount; // 21

    public static int PosBytes(BitQuality q) => BasisAvatarBitPacking.PositionBytes(q);
    public static int BoneBaseBit(BitQuality q) => PosBytes(q) * 8;

    public static int PayloadSize(BitQuality q) => BasisAvatarBitPacking.ConvertToSize(q);
    public static int RotBytes(BitQuality q) => BasisBoneRotationCompression.RotationBytes(q);
    public static int TailStart(BitQuality q) => PosBytes(q) + RotBytes(q);
    public static int ScaleOffset(BitQuality q) => TailStart(q);
    public static int BodyRotOffset(BitQuality q) => TailStart(q) + BasisBoneRotationCompression.WriteScale;
    public static int HipsDeltaOffset(BitQuality q) => BodyRotOffset(q) + BasisBoneRotationCompression.WriteRotation;
    public static int HipsRotOffset(BitQuality q) => HipsDeltaOffset(q) + BasisBoneRotationCompression.WriteHipsDelta;
    public static int EndEffectorOffset(BitQuality q) => TailStart(q) + BasisBoneRotationCompression.TailBytes;
    public static int EndEffectorBytes(BitQuality q) => BasisBoneRotationCompression.EndEffectorBytes(q);

    public static BasisAvatarChannelLayout Layout(BitQuality q) => BasisAvatarChannelMap.For(q);

    /// <summary>Flip every byte of the end-effector block (High only), guaranteeing it differs.</summary>
    public static void FlipEndEffector(byte[] payload, BitQuality q)
    {
        int off = EndEffectorOffset(q), n = EndEffectorBytes(q);
        for (int i = 0; i < n; i++) payload[off + i] ^= 0xFF;
    }

    /// <summary>Raw per-slot bits-per-component table (51 entries; only the first 21 reach the wire).</summary>
    public static byte[] Bpc(BitQuality q) => BasisBoneRotationCompression.GetBpcTable(q);

    /// <summary>Bit width of rotation wire field <paramref name="field"/> (0..30).</summary>
    public static int BoneWidth(BitQuality q, int field) => RotationFieldWidths(q)[field];

    public static int[] RotationFieldWidths(BitQuality q) => BasisBoneRotationCompression.BuildRotationFieldWidths(q);

    /// <summary>Start bit of each rotation wire field, relative to the rotation region.</summary>
    public static int[] BoneBitOffsets(BitQuality q)
    {
        var offs = new int[BasisBoneRotationCompression.RotationFieldCount];
        BasisBoneRotationCompression.BuildRotationFieldOffsets(q, offs);
        return offs;
    }

    public static ulong GetBone(byte[] payload, BitQuality q, int field)
    {
        int pos = BoneBaseBit(q) + BoneBitOffsets(q)[field];
        return BasisBoneRotationCompression.ReadBits(payload, ref pos, BoneWidth(q, field));
    }

    public static void SetBone(byte[] payload, BitQuality q, int field, ulong value)
    {
        int offset = BoneBaseBit(q) + BoneBitOffsets(q)[field];
        int width = BoneWidth(q, field);
        for (int i = 0; i < width; i++)
        {
            int b = offset + i, bytePos = b >> 3, bit = b & 7;
            if (((value >> i) & 1UL) != 0) payload[bytePos] |= (byte)(1 << bit);
            else payload[bytePos] &= (byte)~(1 << bit);
        }
    }

    /// <summary>Flip every bit of a rotation field, guaranteeing it differs from its current value.</summary>
    public static void FlipBone(byte[] payload, BitQuality q, int field)
    {
        ulong maxv = (1UL << BoneWidth(q, field)) - 1UL;
        SetBone(payload, q, field, GetBone(payload, q, field) ^ maxv);
    }

    /// <summary>Valid quantized payload: random position/tail bytes + random rotation-field bits.</summary>
    public static byte[] MakePayload(BitQuality q, Random rng)
    {
        var arr = new byte[PayloadSize(q)];
        FillNonRotation(arr, q, rng);
        var widths = RotationFieldWidths(q);
        var offs = BoneBitOffsets(q);
        for (int f = 0; f < widths.Length; f++)
        {
            ulong maxv = widths[f] >= 64 ? ulong.MaxValue : (1UL << widths[f]) - 1UL;
            BasisBoneRotationCompression.WriteBits(arr, BoneBaseBit(q) + offs[f], (ulong)rng.NextInt64() & maxv, widths[f]);
        }
        return arr;
    }

    /// <summary>
    /// Realistic payload: the 21 bone slots are true smallest-three encodings of random unit
    /// quaternions, the 10 finger channels are quantized curl/splay pairs.
    /// </summary>
    public static byte[] MakeRealisticPayload(BitQuality q, Random rng)
    {
        var arr = new byte[PayloadSize(q)];
        FillNonRotation(arr, q, rng);
        var bpc = Bpc(q);
        var offs = BoneBitOffsets(q);

        for (int slot = 0; slot < WireBoneSlots; slot++)
        {
            var (x, y, z, w) = RandomQuat(rng);
            ulong packed = BasisBoneRotationCompression.EncodeSmallestThree(
                x, y, z, w, bpc[slot], BasisBoneRotationCompression.MAX_COMPONENT[slot]);
            BasisBoneRotationCompression.WriteBits(arr, BoneBaseBit(q) + offs[slot], packed, 2 + 3 * bpc[slot]);
        }

        int curlBits = BasisBoneRotationCompression.CurlBits(q);
        int splayBits = BasisBoneRotationCompression.SplayBits(q);
        for (int f = 0; f < BasisBoneRotationCompression.FingerChannelCount; f++)
        {
            uint curl = BasisBoneRotationCompression.EncodeSignedUnit((float)(rng.NextDouble() * 2 - 1), curlBits);
            uint splay = BasisBoneRotationCompression.EncodeSignedUnit((float)(rng.NextDouble() * 2 - 1), splayBits);
            int b = BoneBaseBit(q) + offs[WireBoneSlots + f];
            BasisBoneRotationCompression.WriteBits(arr, b, curl, curlBits);
            BasisBoneRotationCompression.WriteBits(arr, b + curlBits, splay, splayBits);
        }
        return arr;
    }

    // Position and the tail (scale / body rot / hips delta / hips rot / effector block) are opaque
    // byte regions to these tests; random bytes exercise the codec just as well as real ones and
    // keep the builders independent of the tail's internal encodings.
    private static void FillNonRotation(byte[] arr, BitQuality q, Random rng)
    {
        rng.NextBytes(new Span<byte>(arr, 0, PosBytes(q)));
        rng.NextBytes(new Span<byte>(arr, TailStart(q), BasisBoneRotationCompression.TailBytes));
        if (EndEffectorBytes(q) > 0) rng.NextBytes(new Span<byte>(arr, EndEffectorOffset(q), EndEffectorBytes(q)));
    }

    public static (float x, float y, float z, float w) RandomQuat(Random rng)
    {
        float x = (float)(rng.NextDouble() * 2 - 1);
        float y = (float)(rng.NextDouble() * 2 - 1);
        float z = (float)(rng.NextDouble() * 2 - 1);
        float w = (float)(rng.NextDouble() * 2 - 1);
        float len = MathF.Sqrt(x * x + y * y + z * z + w * w);
        return len < 1e-6f ? (0f, 0f, 0f, 1f) : (x / len, y / len, z / len, w / len);
    }

    /// <summary>Builds a delta, verifies the length probe agrees and the bound holds, then applies it.</summary>
    public static (int len, byte[] recon) BuildApply(byte[] kf, byte[] cur, BitQuality q)
    {
        var dst = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(q)];
        int len = BasisAvatarDeltaCompression.BuildDelta(kf, cur, q, dst, 0);
        Assert.True(len > 0, "BuildDelta returned non-positive length");
        Assert.True(len <= BasisAvatarDeltaCompression.MaxDeltaSize(q), "delta exceeded MaxDeltaSize");
        Assert.Equal(len, BasisAvatarDeltaCompression.DeltaBodyLength(dst, 0, len, q));
        var recon = new byte[PayloadSize(q)];
        Assert.True(BasisAvatarDeltaCompression.TryApplyDelta(kf, dst, 0, len, q, recon), "TryApplyDelta rejected a valid delta");
        return (len, recon);
    }

    public static void AssertRoundTrip(byte[] kf, byte[] cur, BitQuality q)
    {
        var (_, recon) = BuildApply(kf, cur, q);
        Assert.Equal(cur.AsSpan(0, PayloadSize(q)).ToArray(), recon.AsSpan(0, PayloadSize(q)).ToArray());
    }
}
