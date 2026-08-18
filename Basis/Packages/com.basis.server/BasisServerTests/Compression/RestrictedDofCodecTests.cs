using Basis.Network.Core.Compression;
using Xunit;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;

namespace BasisServerTests;

/// <summary>
/// The v52 restricted-DOF bone codec: 2-DOF joints (lower arms/legs, shoulders, hands, feet)
/// carry a hinge+twist angle pair and 1-DOF toes a single angle, instead of a smallest-three
/// quaternion. These tests pin the factorization, the quantization error bounds, and the
/// DOF tables themselves.
/// </summary>
public class RestrictedDofCodecTests
{
    private static readonly BitQuality[] AllQualities =
        { BitQuality.VeryLow, BitQuality.Low, BitQuality.Medium, BitQuality.High };

    private static (float x, float y, float z, float w) AxisAngle(int axis, float angle)
    {
        float s = MathF.Sin(angle * 0.5f), c = MathF.Cos(angle * 0.5f);
        return (axis == 0 ? s : 0f, axis == 1 ? s : 0f, axis == 2 ? s : 0f, c);
    }

    private static (float x, float y, float z, float w) Mul(
        (float x, float y, float z, float w) a, (float x, float y, float z, float w) b)
    {
        return (
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
    }

    /// <summary>Relative rotation angle between two unit quaternions, in radians.
    /// Uses 2*atan2(|v|,|w|) of the relative rotation — acos(|dot|) loses precision near 1.</summary>
    private static float AngleBetween(
        (float x, float y, float z, float w) a, (float x, float y, float z, float w) b)
    {
        var conjA = (-a.x, -a.y, -a.z, a.w);
        var r = Mul(conjA, b);
        float v = MathF.Sqrt(r.Item1 * r.Item1 + r.Item2 * r.Item2 + r.Item3 * r.Item3);
        return 2f * MathF.Atan2(v, MathF.Abs(r.Item4));
    }

    [Fact]
    public void DofTables_AreConsistent()
    {
        Assert.Equal(BasisBoneRotationCompression.WireBoneSlotCount, BasisBoneRotationCompression.BONE_DOF.Length);
        Assert.Equal(BasisBoneRotationCompression.WireBoneSlotCount, BasisBoneRotationCompression.BONE_AXIS_A.Length);
        Assert.Equal(BasisBoneRotationCompression.WireBoneSlotCount, BasisBoneRotationCompression.BONE_AXIS_B.Length);
        Assert.Equal(BasisBoneRotationCompression.WireBoneSlotCount, BasisBoneRotationCompression.BONE_RANGE_A.Length);
        Assert.Equal(BasisBoneRotationCompression.WireBoneSlotCount, BasisBoneRotationCompression.BONE_RANGE_B.Length);

        for (int slot = 0; slot < BasisBoneRotationCompression.WireBoneSlotCount; slot++)
        {
            byte dof = BasisBoneRotationCompression.BONE_DOF[slot];
            Assert.InRange(dof, (byte)1, (byte)3);
            if (dof < 3)
            {
                Assert.InRange(BasisBoneRotationCompression.BONE_RANGE_A[slot], 0.1f, MathF.PI);
                Assert.InRange(BasisBoneRotationCompression.BONE_AXIS_A[slot], (byte)0, (byte)2);
            }
            if (dof == 2)
            {
                Assert.InRange(BasisBoneRotationCompression.BONE_RANGE_B[slot], 0.1f, MathF.PI);
                // A hinge/twist pair about the same axis would be degenerate.
                Assert.NotEqual(BasisBoneRotationCompression.BONE_AXIS_A[slot],
                    BasisBoneRotationCompression.BONE_AXIS_B[slot]);
            }
        }
    }

    [Fact]
    public void HingeTwistFactorization_IsExact_ForTwoAxisRotations()
    {
        // Any rotation genuinely of the form R_A(a) * R_B(b) must extract those angles exactly.
        foreach (int axisA in new[] { 0, 1, 2 })
        {
            foreach (int axisB in new[] { 0, 1, 2 })
            {
                if (axisA == axisB) continue;
                for (float a = -2.6f; a <= 2.6f; a += 0.37f)
                {
                    for (float b = -1.5f; b <= 1.5f; b += 0.23f)
                    {
                        var q = Mul(AxisAngle(axisA, a), AxisAngle(axisB, b));
                        BasisBoneRotationCompression.ExtractHingeTwist(q.x, q.y, q.z, q.w,
                            axisA, axisB, out float ea, out float eb);
                        Assert.Equal(a, ea, 1e-3f);
                        Assert.Equal(b, eb, 1e-3f);

                        BasisBoneRotationCompression.ComposeHingeTwist(axisA, ea, axisB, eb,
                            out float rx, out float ry, out float rz, out float rw);
                        Assert.True(AngleBetween(q, (rx, ry, rz, rw)) < 1e-3f);
                    }
                }
            }
        }
    }

    [Fact]
    public void RestrictedCodec_RoundTrip_WithinQuantizationStep()
    {
        foreach (var quality in AllQualities)
        {
            for (int slot = 9; slot < BasisBoneRotationCompression.WireBoneSlotCount; slot++)
            {
                int dof = BasisBoneRotationCompression.BONE_DOF[slot];
                int axisA = BasisBoneRotationCompression.BONE_AXIS_A[slot];
                int axisB = BasisBoneRotationCompression.BONE_AXIS_B[slot];
                float rangeA = BasisBoneRotationCompression.BONE_RANGE_A[slot];
                float rangeB = BasisBoneRotationCompression.BONE_RANGE_B[slot];

                int bitsA = dof == 1
                    ? BasisBoneRotationCompression.SingleAxisBits(quality)
                    : BasisBoneRotationCompression.HingeBits(quality);
                float stepA = 2f * rangeA / ((1 << bitsA) - 1);
                float stepB = dof == 2
                    ? 2f * rangeB / ((1 << BasisBoneRotationCompression.TwistBits(quality)) - 1)
                    : 0f;
                // Half a step per angle, plus float slack.
                float bound = 0.5f * (stepA + stepB) + 1e-3f;

                var rng = new Random(slot * 31 + (int)quality);
                for (int iter = 0; iter < 200; iter++)
                {
                    float a = ((float)rng.NextDouble() * 2f - 1f) * rangeA * 0.98f;
                    float b = dof == 2 ? ((float)rng.NextDouble() * 2f - 1f) * rangeB * 0.98f : 0f;
                    var q = dof == 2
                        ? Mul(AxisAngle(axisA, a), AxisAngle(axisB, b))
                        : AxisAngle(axisA, a);

                    ulong packed = BasisBoneRotationCompression.EncodeRestricted(q.x, q.y, q.z, q.w, slot, quality);
                    Assert.True(packed < 1UL << BasisBoneRotationCompression.BoneFieldWidth(quality, slot));

                    BasisBoneRotationCompression.DecodeRestricted(packed, slot, quality,
                        out float dx, out float dy, out float dz, out float dw);
                    float err = AngleBetween(q, (dx, dy, dz, dw));
                    Assert.True(err <= bound,
                        $"slot {slot} {quality}: error {err} > bound {bound} (a={a}, b={b})");
                }
            }
        }
    }

    [Fact]
    public void OffAxisContent_IsProjectedAway()
    {
        // A rotation with content on a joint's impossible axis decodes to the nearest
        // representable two-axis rotation — bounded by the off-axis contamination, never garbage.
        int slot = 9; // left lower arm: hinge Y, twist X, dropped Z
        var quality = BitQuality.High;
        var pure = Mul(AxisAngle(1, 1.1f), AxisAngle(0, 0.6f));
        var contaminated = Mul(pure, AxisAngle(2, 0.1f)); // ~5.7° of impossible motion

        ulong packed = BasisBoneRotationCompression.EncodeRestricted(
            contaminated.x, contaminated.y, contaminated.z, contaminated.w, slot, quality);
        BasisBoneRotationCompression.DecodeRestricted(packed, slot, quality,
            out float dx, out float dy, out float dz, out float dw);

        // Decoded pose stays close to the anatomically-possible part of the input.
        Assert.True(AngleBetween(pure, (dx, dy, dz, dw)) < 0.12f);
    }

    [Fact]
    public void NonFiniteInput_EncodesToMidpointNotGarbage()
    {
        foreach (var quality in AllQualities)
        {
            ulong packed = BasisBoneRotationCompression.EncodeRestricted(
                float.NaN, float.NaN, float.NaN, float.NaN, 19, quality);
            Assert.True(packed < 1UL << BasisBoneRotationCompression.BoneFieldWidth(quality, 19));
            BasisBoneRotationCompression.DecodeRestricted(packed, 19, quality,
                out float dx, out float dy, out float dz, out float dw);
            Assert.True(float.IsFinite(dx) && float.IsFinite(dy) && float.IsFinite(dz) && float.IsFinite(dw));
        }
    }
}
