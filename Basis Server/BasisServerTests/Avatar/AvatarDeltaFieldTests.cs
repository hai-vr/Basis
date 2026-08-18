using Basis.Network.Core.Compression;
using Xunit;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;
using S = BasisServerTests.DeltaTestSupport;

namespace BasisServerTests;

/// <summary>
/// Per-field dirty-mask coverage: changing exactly one field must send that field and nothing else,
/// and every field / combination must round-trip byte-exactly.
///
/// Sizes are asserted as UPPER BOUNDS rather than exact values. Under residual coding a field's cost
/// depends on how far it moved, not merely on whether it moved, so "flip every bit of this field"
/// (the worst case these tests construct) is bounded by the field's verbatim width plus its mode bit
/// — which is precisely the guarantee worth locking down, since it is what stops the codec from ever
/// being worse than the fixed-width scheme it replaced.
/// </summary>
public class AvatarDeltaFieldTests
{
    private const int Mask = BasisAvatarDeltaCompression.DirtyMaskBytes;   // 5

    /// <summary>Mask + a whole-field worst case: its channels verbatim plus the one mode bit.</summary>
    private static int MaxBodyFor(BitQuality q, params int[] fields)
    {
        var layout = S.Layout(q);
        int bits = 0;
        foreach (int f in fields) bits += layout.FieldRawBits(f) + 1;
        return Mask + ((bits + 7) >> 3);
    }

    private static int FieldPosition => 0;
    private static int FieldScale => 1 + BasisBoneRotationCompression.RotationFieldCount;
    private static int FieldBodyRot => FieldScale + 1;
    private static int FieldHipsDelta => FieldScale + 2;
    private static int FieldHipsRot => FieldScale + 3;
    private static int FieldEndEffector => FieldScale + 4;
    private static int BoneField(int slot) => BasisAvatarDeltaCompression.BoneFieldStart + slot;

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void NoChange_SendsMaskOnly(BitQuality q)
    {
        var rng = new Random((int)q + 1);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        byte[] cur = (byte[])kf.Clone();
        var (len, recon) = S.BuildApply(kf, cur, q);
        Assert.Equal(Mask, len);
        Assert.Equal(cur, recon);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void PositionOnly(BitQuality q) => AssertByteFieldOnly(q, 0, FieldPosition);

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void ScaleOnly(BitQuality q) => AssertByteFieldOnly(q, S.ScaleOffset(q), FieldScale);

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void BodyRotationOnly(BitQuality q) => AssertByteFieldOnly(q, S.BodyRotOffset(q), FieldBodyRot);

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void HipsDeltaOnly(BitQuality q) => AssertByteFieldOnly(q, S.HipsDeltaOffset(q), FieldHipsDelta);

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void HipsRotationOnly(BitQuality q) => AssertByteFieldOnly(q, S.HipsRotOffset(q), FieldHipsRot);

    private static void AssertByteFieldOnly(BitQuality q, int fieldOffset, int fieldIndex)
    {
        var rng = new Random(fieldOffset * 31 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        byte[] cur = (byte[])kf.Clone();
        cur[fieldOffset] ^= 0xFF; // flip one byte inside the field
        var (len, recon) = S.BuildApply(kf, cur, q);
        Assert.InRange(len, Mask + 1, MaxBodyFor(q, fieldIndex));
        Assert.Equal(cur, recon);
    }

    [Fact]
    public void EverySingleRotationField_AllQualities_BoundedAndRoundTrips()
    {
        var rng = new Random(9001);
        foreach (var q in S.AllQualities)
        {
            for (int slot = 0; slot < S.BoneCount; slot++)
            {
                byte[] kf = S.MakeRealisticPayload(q, rng);
                byte[] cur = (byte[])kf.Clone();
                S.FlipBone(cur, q, slot);
                var (len, recon) = S.BuildApply(kf, cur, q);
                Assert.InRange(len, Mask + 1, MaxBodyFor(q, BoneField(slot)));
                Assert.Equal(cur, recon);
            }
        }
    }

    /// <summary>
    /// A single quantization step on one component — the case the old codec charged a whole bone for
    /// and the entire reason this codec exists. One step must cost dramatically less than the field.
    /// </summary>
    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void SingleComponentStep_CostsFarLessThanTheField(BitQuality q)
    {
        var rng = new Random(1234 + (int)q);
        var layout = S.Layout(q);
        var bpc = S.Bpc(q);

        for (int slot = 0; slot < S.WireBoneSlots; slot++)
        {
            byte[] kf = S.MakeRealisticPayload(q, rng);
            byte[] cur = (byte[])kf.Clone();

            // Nudge the first component of this bone by exactly one step.
            int field = BoneField(slot);
            var ch = layout.Channels[layout.FieldChannelStart(field) + 1];   // [0] is the 2-bit index
            uint v = BasisAvatarDeltaCompression.ReadChannel(cur, ch);
            BasisAvatarDeltaCompression.WriteChannel(cur, ch, (v + 1) & ch.Mask);
            if (BasisAvatarDeltaCompression.ReadChannel(cur, ch) == BasisAvatarDeltaCompression.ReadChannel(kf, ch)) continue;

            var (len, recon) = S.BuildApply(kf, cur, q);
            Assert.Equal(cur, recon);

            // Mask + mode bit + 2 index bits + three EG codes, one of which is +-1 (3 bits) and two
            // of which are zero (1 bit each): 8 bits of body, so at most one byte past the mask.
            Assert.True(len <= Mask + 1,
                $"{q} slot {slot}: one-step change cost {len - Mask} body bytes, expected 1 " +
                $"(field is {bpc[slot] * 3 + 2} bits verbatim)");
        }
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void AllRotationFieldsChanged_TailStable(BitQuality q)
    {
        var rng = new Random(4242 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        byte[] cur = (byte[])kf.Clone();
        for (int s = 0; s < S.BoneCount; s++) S.FlipBone(cur, q, s);
        var (len, recon) = S.BuildApply(kf, cur, q);
        // Bounded by the whole rotation region verbatim plus one mode bit per rotation field.
        Assert.InRange(len, Mask + 1, Mask + ((S.BoneCount + S.RotBytes(q) * 8 + 7) >> 3));
        Assert.Equal(cur, recon);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void AllByteFieldsChanged_RotationStable(BitQuality q)
    {
        var rng = new Random(4343 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        byte[] cur = (byte[])kf.Clone();
        cur[0] ^= 0xFF;                       // position
        cur[S.ScaleOffset(q)] ^= 0xFF;        // scale
        cur[S.BodyRotOffset(q)] ^= 0xFF;      // body rot
        cur[S.HipsDeltaOffset(q)] ^= 0xFF;    // hips delta
        cur[S.HipsRotOffset(q)] ^= 0xFF;      // hips rot
        if (S.EndEffectorBytes(q) > 0) cur[S.EndEffectorOffset(q)] ^= 0xFF;   // effector block (High only)

        var fields = S.EndEffectorBytes(q) > 0
            ? new[] { FieldPosition, FieldScale, FieldBodyRot, FieldHipsDelta, FieldHipsRot, FieldEndEffector }
            : new[] { FieldPosition, FieldScale, FieldBodyRot, FieldHipsDelta, FieldHipsRot };

        var (len, recon) = S.BuildApply(kf, cur, q);
        Assert.InRange(len, Mask + 1, MaxBodyFor(q, fields));
        Assert.Equal(cur, recon);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void EverythingChanged_StaysUnderMaxDeltaSize(BitQuality q)
    {
        var rng = new Random(4444 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        byte[] cur = (byte[])kf.Clone();
        cur[0] ^= 0xFF;
        cur[S.ScaleOffset(q)] ^= 0xFF;
        cur[S.BodyRotOffset(q)] ^= 0xFF;
        cur[S.HipsDeltaOffset(q)] ^= 0xFF;
        cur[S.HipsRotOffset(q)] ^= 0xFF;
        for (int s = 0; s < S.BoneCount; s++) S.FlipBone(cur, q, s);
        if (S.EndEffectorBytes(q) > 0) S.FlipEndEffector(cur, q);
        var (len, recon) = S.BuildApply(kf, cur, q);
        Assert.InRange(len, Mask + 1, BasisAvatarDeltaCompression.MaxDeltaSize(q));
        Assert.Equal(cur, recon);
    }

    [Theory]
    [InlineData(BitQuality.High, 1)]
    [InlineData(BitQuality.High, 3)]
    [InlineData(BitQuality.High, 7)]
    [InlineData(BitQuality.Medium, 5)]
    [InlineData(BitQuality.Low, 10)]
    [InlineData(BitQuality.VeryLow, 20)]
    public void KRotationFieldsChanged_ContiguousPacking(BitQuality q, int k)
    {
        var rng = new Random(k * 101 + (int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        byte[] cur = (byte[])kf.Clone();
        var slots = new HashSet<int>();
        while (slots.Count < k) slots.Add(rng.Next(S.BoneCount));
        foreach (int s in slots) S.FlipBone(cur, q, s);
        var (len, recon) = S.BuildApply(kf, cur, q);
        Assert.InRange(len, Mask + 1, MaxBodyFor(q, slots.Select(BoneField).ToArray()));
        Assert.Equal(cur, recon);
    }
}
