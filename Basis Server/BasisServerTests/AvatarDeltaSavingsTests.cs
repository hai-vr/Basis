using Basis.Network.Core.Compression;
using Xunit;
using Xunit.Abstractions;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;
using S = BasisServerTests.DeltaTestSupport;

namespace BasisServerTests;

/// <summary>
/// Bandwidth-savings characterization at different levels of data similarity. Wire sizes model the
/// common byte-id case:
///   keyframe wire = 1(id) + 1(interval) + 1(seq) + payload
///   delta wire    = 1(header) + 1(id) + 1(interval) + 1(seq) + 1(baseSeq) + deltaBody
///
/// <see cref="PrintSavingsTable"/> also prints what the previous fixed-width delta codec would have
/// spent on the same poses, which is the measurement that justifies the change: the two schemes agree
/// when a field is untouched or fully re-randomized, and diverge exactly in the middle — real motion,
/// where a joint moves by a few quantization steps.
/// </summary>
public class AvatarDeltaSavingsTests
{
    private readonly ITestOutputHelper _out;
    public AvatarDeltaSavingsTests(ITestOutputHelper output) => _out = output;

    private const int Mask = BasisAvatarDeltaCompression.DirtyMaskBytes;

    private static int KeyframeWire(BitQuality q) => 3 + S.PayloadSize(q);
    private static int DeltaWire(int body) => 5 + body;
    private static double Savings(BitQuality q, int body) => 1.0 - (double)DeltaWire(body) / KeyframeWire(q);

    private static int BodyFor(byte[] kf, byte[] cur, BitQuality q)
    {
        var dst = new byte[BasisAvatarDeltaCompression.MaxDeltaSize(q)];
        return BasisAvatarDeltaCompression.BuildDelta(kf, cur, q, dst, 0);
    }

    /// <summary>
    /// What the superseded codec would have produced: the dirty mask, then every changed field
    /// verbatim (byte fields whole, rotation fields bit-packed contiguously).
    /// </summary>
    private static int LegacyBodyFor(byte[] kf, byte[] cur, BitQuality q)
    {
        var layout = S.Layout(q);
        int byteFieldBits = 0, rotFieldBits = 0;
        for (int f = 0; f < BasisAvatarDeltaCompression.FieldCount; f++)
        {
            bool dirty = false;
            for (int c = layout.FieldChannelStart(f); c < layout.FieldChannelEnd(f); c++)
            {
                var ch = layout.Channels[c];
                if (BasisAvatarDeltaCompression.ReadChannel(cur, ch) != BasisAvatarDeltaCompression.ReadChannel(kf, ch))
                { dirty = true; break; }
            }
            if (!dirty) continue;
            bool isRotation = f >= BasisAvatarDeltaCompression.BoneFieldStart
                           && f < BasisAvatarDeltaCompression.BoneFieldStart + BasisBoneRotationCompression.RotationFieldCount;
            if (isRotation) rotFieldBits += layout.FieldRawBits(f);
            else byteFieldBits += layout.FieldRawBits(f);
        }
        return Mask + (byteFieldBits >> 3) + ((rotFieldBits + 7) >> 3);
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void Idle_SavesOver88Percent(BitQuality q)
    {
        var rng = new Random((int)q);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        int body = BodyFor(kf, (byte[])kf.Clone(), q);
        Assert.Equal(Mask, body);
        Assert.True(Savings(q, body) >= 0.88, $"idle savings {Savings(q, body):P1} < 88% at {q}");
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void RootPositionOnly_SavesOver78Percent(BitQuality q)
    {
        var rng = new Random((int)q + 10);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        byte[] cur = (byte[])kf.Clone();
        cur[0] ^= 0xFF;
        int body = BodyFor(kf, cur, q);
        Assert.True(body <= Mask + S.PosBytes(q) + 1);
        Assert.True(Savings(q, body) >= 0.78, $"position-only savings {Savings(q, body):P1} < 78% at {q}");
    }

    /// <summary>
    /// The case this codec exists for: every joint moving slightly, which is what a person standing
    /// and talking produces. The previous scheme charged full field width for any movement at all and
    /// so saved essentially nothing here.
    /// </summary>
    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void SmallMotionEverywhere_BeatsLegacySubstantially(BitQuality q)
    {
        var rng = new Random((int)q + 55);
        long mine = 0, legacy = 0;
        for (int t = 0; t < 200; t++)
        {
            byte[] kf = S.MakeRealisticPayload(q, rng);
            byte[] cur = NudgeAllComponents(kf, q, rng, maxSteps: 2);
            mine += BodyFor(kf, cur, q);
            legacy += LegacyBodyFor(kf, cur, q);
        }
        double ratio = (double)mine / legacy;

        // The narrow tiers gain least: at VeryLow a component is 2-5 bits wide, so a two-step move is
        // a large fraction of its range and an Exp-Golomb code barely undercuts sending it verbatim.
        // High is where the bits actually are, and where the gain has to show up.
        double bound = q == BitQuality.High ? 0.45 : 0.75;
        Assert.True(ratio < bound,
            $"{q}: small-motion body is {ratio:P1} of the legacy scheme, expected under {bound:P0} " +
            $"({mine / 200.0:F1} B vs {legacy / 200.0:F1} B)");
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void AllRotationFields_StillSavesSomething(BitQuality q)
    {
        var rng = new Random((int)q + 20);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        byte[] cur = (byte[])kf.Clone();
        for (int s = 0; s < S.BoneCount; s++) S.FlipBone(cur, q, s);
        int body = BodyFor(kf, cur, q);
        Assert.True(body <= Mask + ((S.BoneCount + S.RotBytes(q) * 8 + 7) >> 3));
        Assert.True(Savings(q, body) > 0.05, $"all-rotation savings {Savings(q, body):P1} not positive enough at {q}");
    }

    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void UncorrelatedPoses_TriggerKeyframePromotion(BitQuality q)
    {
        // Two independent random poses: every channel differs by a large arbitrary amount, so no
        // residual can be short and every field falls back to raw. That is the worst case, and the
        // server's `body >= payload` guard has to fire on it or the delta is pure overhead.
        var rng = new Random((int)q + 30);
        for (int t = 0; t < 50; t++)
        {
            byte[] kf = S.MakePayload(q, rng);
            byte[] cur = S.MakePayload(q, rng);
            int body = BodyFor(kf, cur, q);
            Assert.True(body >= S.PayloadSize(q),
                $"expected promotion at {q}: body {body} < payload {S.PayloadSize(q)}");
            Assert.True(body <= BasisAvatarDeltaCompression.MaxDeltaSize(q));
        }
    }

    /// <summary>
    /// Flipping one byte of each byte-field and every bit of each rotation field used to be the
    /// "everything changed" case, and used to force promotion because the old codec charged full
    /// width for any change at all. It no longer does: a one-byte flip of a 24-bit position axis is a
    /// bounded residual, so the delta stays genuinely smaller and shipping it is correct.
    /// </summary>
    [Theory]
    [InlineData(BitQuality.VeryLow)]
    [InlineData(BitQuality.Low)]
    [InlineData(BitQuality.Medium)]
    [InlineData(BitQuality.High)]
    public void WholesalePoseChange_StaysUnderTheKeyframe(BitQuality q)
    {
        var rng = new Random((int)q + 31);
        byte[] kf = S.MakeRealisticPayload(q, rng);
        byte[] cur = (byte[])kf.Clone();
        cur[0] ^= 0xFF;
        cur[S.ScaleOffset(q)] ^= 0xFF;
        cur[S.BodyRotOffset(q)] ^= 0xFF;
        cur[S.HipsDeltaOffset(q)] ^= 0xFF;
        cur[S.HipsRotOffset(q)] ^= 0xFF;
        for (int s = 0; s < S.BoneCount; s++) S.FlipBone(cur, q, s);
        if (S.EndEffectorBytes(q) > 0) S.FlipEndEffector(cur, q);
        int body = BodyFor(kf, cur, q);
        int legacy = LegacyBodyFor(kf, cur, q);
        Assert.True(body < legacy, $"{q}: {body} B should undercut the legacy {legacy} B");
        S.AssertRoundTrip(kf, cur, q);
    }

    /// <summary>Perturbs every Delta channel by up to +-maxSteps quantization steps.</summary>
    private static byte[] NudgeAllComponents(byte[] kf, BitQuality q, Random rng, int maxSteps)
    {
        var cur = (byte[])kf.Clone();
        var layout = S.Layout(q);
        foreach (var ch in layout.Channels)
        {
            if (ch.Kind != BasisChannelKind.Delta) continue;
            int step = rng.Next(-maxSteps, maxSteps + 1);
            uint v = BasisAvatarDeltaCompression.ReadChannel(cur, ch);
            BasisAvatarDeltaCompression.WriteChannel(cur, ch, (uint)((int)v + step) & ch.Mask);
        }
        return cur;
    }

    [Fact]
    public void PrintSavingsTable()
    {
        var rng = new Random(2024);
        const int trials = 400;

        _out.WriteLine("Avatar delta bandwidth vs full keyframe (byte-id wire, averaged over realistic poses)");
        _out.WriteLine("'legacy' = the fixed-width delta codec this replaced, on the same poses.");
        _out.WriteLine("");

        foreach (var q in S.AllQualities)
        {
            _out.WriteLine($"== {q}  (keyframe wire = {KeyframeWire(q)} B, payload = {S.PayloadSize(q)} B) ==");
            _out.WriteLine("  scenario                | body B | legacy B | wire B | savings | vs legacy");

            void Row(string name, Func<byte[], BitQuality, Random, byte[]> mutate)
            {
                long mineSum = 0, legacySum = 0;
                for (int t = 0; t < trials; t++)
                {
                    byte[] kf = S.MakeRealisticPayload(q, rng);
                    byte[] cur = mutate(kf, q, rng);
                    mineSum += BodyFor(kf, cur, q);
                    legacySum += LegacyBodyFor(kf, cur, q);
                }
                double body = (double)mineSum / trials, legacy = (double)legacySum / trials;
                double wire = 5 + body;
                _out.WriteLine($"  {name,-23} | {body,6:F1} | {legacy,8:F1} | {wire,6:F1} | " +
                               $"{1.0 - wire / KeyframeWire(q),6:P1}  | {1.0 - body / legacy,6:P1}");
            }

            Row("idle", (kf, qq, r) => (byte[])kf.Clone());
            Row("position only", (kf, qq, r) => { var c = (byte[])kf.Clone(); c[0] ^= 0xFF; return c; });
            Row("micro motion (+-1)", (kf, qq, r) => NudgeAllComponents(kf, qq, r, 1));
            Row("small motion (+-2)", (kf, qq, r) => NudgeAllComponents(kf, qq, r, 2));
            Row("moderate motion (+-8)", (kf, qq, r) => NudgeAllComponents(kf, qq, r, 8));
            Row("large motion (+-64)", (kf, qq, r) => NudgeAllComponents(kf, qq, r, 64));
            Row("k=5 fields re-posed", (kf, qq, r) =>
            {
                var c = (byte[])kf.Clone();
                var slots = new HashSet<int>();
                while (slots.Count < 5) slots.Add(r.Next(S.BoneCount));
                foreach (int s in slots) S.FlipBone(c, qq, s);
                return c;
            });
            Row("everything re-randomized", (kf, qq, r) =>
            {
                var c = (byte[])kf.Clone();
                for (int s = 0; s < S.BoneCount; s++) S.FlipBone(c, qq, s);
                c[0] ^= 0xFF; c[S.ScaleOffset(qq)] ^= 0xFF; c[S.BodyRotOffset(qq)] ^= 0xFF;
                c[S.HipsDeltaOffset(qq)] ^= 0xFF; c[S.HipsRotOffset(qq)] ^= 0xFF;
                if (S.EndEffectorBytes(qq) > 0) S.FlipEndEffector(c, qq);
                return c;
            });
            _out.WriteLine("");
        }
    }
}
