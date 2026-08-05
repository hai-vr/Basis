using Basis.Network.Core.Compression;
using BasisNetworkServer.BasisNetworkingReductionSystem;
using Xunit;
using BitQuality = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;
using S = BasisServerTests.DeltaTestSupport;

namespace BasisServerTests;

/// <summary>
/// Direct coverage of <see cref="AvatarQualityRepacker.BuildAllLowerFromHighInto"/>: position is
/// int24-mm at 0..9 in every tier, so it copies across untouched and the rotation bitstream starts
/// at the same base on both sides. A wrong base offset here corrupts every reduced avatar frame,
/// so the layout is asserted bit-exactly.
/// </summary>
public class AvatarRepackerTests
{
    private static readonly BitQuality[] LowerQualities = { BitQuality.Medium, BitQuality.Low, BitQuality.VeryLow };

    private static uint Rescale(uint qSrc, int bSrc, int bDst)
    {
        if (bSrc == bDst) return qSrc;
        ulong maxSrc = ((ulong)1 << bSrc) - 1UL;
        ulong maxDst = ((ulong)1 << bDst) - 1UL;
        return (uint)(((ulong)qSrc * maxDst + (maxSrc >> 1)) / maxSrc);
    }

    /// <summary>
    /// Start bit of every rotation field relative to the rotation region — the WIRE geometry
    /// (21 explicit bone slots then 10 finger curl/splay channels), which is what the repacker
    /// reads and writes. Not the same as walking all 51 BPC entries as if they were bones.
    /// </summary>
    private static int[] RotationFieldOffsets(BitQuality q)
    {
        var offs = new int[BasisBoneRotationCompression.RotationFieldCount];
        BasisBoneRotationCompression.BuildRotationFieldOffsets(q, offs);
        return offs;
    }

    /// <summary>
    /// A High payload laid out the way the wire actually carries one: real smallest-three
    /// encodings in the 21 bone slots, random curl/splay in the 10 finger channels, plausible
    /// world coordinates in the int24-mm position, and random tail/effector bytes.
    /// </summary>
    private static byte[] MakeWireHighPayload(Random rng)
    {
        var q = BitQuality.High;
        var arr = new byte[BasisAvatarBitPacking.ConvertToSize(q)];

        BasisAvatarBitPacking.EncodePosition(
            (float)(rng.NextDouble() * 2000.0 - 1000.0),
            (float)(rng.NextDouble() * 2000.0 - 1000.0),
            (float)(rng.NextDouble() * 2000.0 - 1000.0),
            arr, 0);
        rng.NextBytes(new Span<byte>(arr, S.TailStart(q), BasisBoneRotationCompression.TailBytes));
        rng.NextBytes(new Span<byte>(arr, S.EndEffectorOffset(q), S.EndEffectorBytes(q)));

        byte[] bpc = BasisBoneRotationCompression.BPC_HIGH;
        int[] offs = RotationFieldOffsets(q);
        int baseBit = S.BoneBaseBit(q);
        for (int slot = 0; slot < BasisBoneRotationCompression.WireBoneSlotCount; slot++)
        {
            var (x, y, z, w) = S.RandomQuat(rng);
            ulong packed = BasisBoneRotationCompression.EncodeSmallestThree(
                x, y, z, w, bpc[slot], BasisBoneRotationCompression.MAX_COMPONENT[slot]);
            BasisBoneRotationCompression.WriteBits(arr, baseBit + offs[slot], packed, 2 + 3 * bpc[slot]);
        }

        int fingerWidth = BasisBoneRotationCompression.FingerFieldWidth(q);
        for (int finger = 0; finger < BasisBoneRotationCompression.FingerChannelCount; finger++)
        {
            int field = BasisBoneRotationCompression.WireBoneSlotCount + finger;
            ulong maxv = (1UL << fingerWidth) - 1UL;
            BasisBoneRotationCompression.WriteBits(
                arr, baseBit + offs[field], (ulong)rng.NextInt64() & maxv, fingerWidth);
        }
        return arr;
    }

    [Fact]
    public void RepackedLowerTiers_MatchExpectedLayoutBitExactly()
    {
        var rng = new Random(777);
        for (int iter = 0; iter < 50; iter++)
        {
            byte[] highArr = MakeWireHighPayload(rng);
            var high = new SerializableBasis.LocalAvatarSyncMessage { array = highArr, DataQualityLevel = (byte)BitQuality.High };
            var med = new SerializableBasis.LocalAvatarSyncMessage();
            var low = new SerializableBasis.LocalAvatarSyncMessage();
            var vlow = new SerializableBasis.LocalAvatarSyncMessage();
            AvatarQualityRepacker.BuildAllLowerFromHighInto(high, ref med, ref low, ref vlow);

            var tiers = new[] { (BitQuality.Medium, med), (BitQuality.Low, low), (BitQuality.VeryLow, vlow) };
            byte[] highBpc = BasisBoneRotationCompression.BPC_HIGH;
            var highOffs = RotationFieldOffsets(BitQuality.High);

            foreach (var (q, msg) in tiers)
            {
                Assert.Equal((byte)q, msg.DataQualityLevel);
                Assert.True(msg.array.Length >= BasisAvatarBitPacking.ConvertToSize(q));

                int posBytes = BasisAvatarBitPacking.PositionBytes(q);
                Assert.Equal(BasisAvatarBitPacking.WritePosition, posBytes);

                // Position is the same encoding in both tiers, so it copies across byte-exactly.
                for (int i = 0; i < posBytes; i++)
                    Assert.True(highArr[i] == msg.array[i], $"{q} position byte {i} mismatch");

                // Every explicit bone is the High bone rescaled to the tier's BPC, written at the
                // 9-byte base. Only slots 0..WireBoneSlotCount carry rotations since v47; the
                // remaining rotation fields are the ten finger curl/splay channels, checked below.
                byte[] bpc = BasisBoneRotationCompression.GetBpcTable(q);
                var offs = RotationFieldOffsets(q);
                for (int slot = 0; slot < BasisBoneRotationCompression.WireBoneSlotCount; slot++)
                {
                    int srcPos = S.BoneBaseBit(BitQuality.High) + highOffs[slot];
                    ulong rawHigh = BasisBoneRotationCompression.ReadBits(highArr, ref srcPos, 2 + 3 * highBpc[slot]);
                    uint idx = (uint)(rawHigh & 3UL);
                    uint maskSrc = (uint)((1 << highBpc[slot]) - 1);
                    uint qa = (uint)((rawHigh >> 2) & maskSrc);
                    uint qb = (uint)((rawHigh >> (2 + highBpc[slot])) & maskSrc);
                    uint qc = (uint)((rawHigh >> (2 + 2 * highBpc[slot])) & maskSrc);

                    ulong expectedPacked = idx
                        | ((ulong)Rescale(qa, highBpc[slot], bpc[slot]) << 2)
                        | ((ulong)Rescale(qb, highBpc[slot], bpc[slot]) << (2 + bpc[slot]))
                        | ((ulong)Rescale(qc, highBpc[slot], bpc[slot]) << (2 + 2 * bpc[slot]));

                    int dstPos = posBytes * 8 + offs[slot];
                    ulong actualPacked = BasisBoneRotationCompression.ReadBits(msg.array, ref dstPos, 2 + 3 * bpc[slot]);
                    Assert.True(expectedPacked == actualPacked, $"{q} bone slot {slot} mismatch");
                }

                // Finger channels: curl and splay rescale independently on the same integer ladder.
                int srcCurlBits = BasisBoneRotationCompression.CurlBits(BitQuality.High);
                int srcSplayBits = BasisBoneRotationCompression.SplayBits(BitQuality.High);
                int dstCurlBits = BasisBoneRotationCompression.CurlBits(q);
                int dstSplayBits = BasisBoneRotationCompression.SplayBits(q);
                for (int finger = 0; finger < BasisBoneRotationCompression.FingerChannelCount; finger++)
                {
                    int field = BasisBoneRotationCompression.WireBoneSlotCount + finger;

                    int srcPos = S.BoneBaseBit(BitQuality.High) + highOffs[field];
                    uint curl = (uint)BasisBoneRotationCompression.ReadBits(highArr, ref srcPos, srcCurlBits);
                    uint splay = (uint)BasisBoneRotationCompression.ReadBits(highArr, ref srcPos, srcSplayBits);

                    ulong expectedPacked = Rescale(curl, srcCurlBits, dstCurlBits)
                        | ((ulong)Rescale(splay, srcSplayBits, dstSplayBits) << dstCurlBits);

                    int dstPos = posBytes * 8 + offs[field];
                    ulong actualPacked = BasisBoneRotationCompression.ReadBits(msg.array, ref dstPos, dstCurlBits + dstSplayBits);
                    Assert.True(expectedPacked == actualPacked, $"{q} finger channel {finger} mismatch");
                }

                // Tail is copied verbatim from the High source.
                int srcTail = S.TailStart(BitQuality.High);
                int dstTail = posBytes + BasisBoneRotationCompression.RotationBytes(q);
                for (int i = 0; i < BasisBoneRotationCompression.TailBytes; i++)
                    Assert.Equal(highArr[srcTail + i], msg.array[dstTail + i]);
            }
        }
    }

    [Fact]
    public void RepackedPayloads_RoundTripThroughDeltaCodec()
    {
        // The delta codec and the repacker must agree on the lower-tier layout: a delta built
        // from two repacked frames has to reconstruct the second exactly.
        var rng = new Random(778);
        byte[] a = S.MakeRealisticPayload(BitQuality.High, rng);
        byte[] b = (byte[])a.Clone();
        b[0] ^= 0xFF; // move position
        S.FlipBone(b, BitQuality.High, 4);

        var highA = new SerializableBasis.LocalAvatarSyncMessage { array = a, DataQualityLevel = (byte)BitQuality.High };
        var highB = new SerializableBasis.LocalAvatarSyncMessage { array = b, DataQualityLevel = (byte)BitQuality.High };
        var (medA, lowA, vlowA) = AvatarQualityRepacker.BuildAllLowerFromHigh(highA);
        var (medB, lowB, vlowB) = AvatarQualityRepacker.BuildAllLowerFromHigh(highB);

        S.AssertRoundTrip(medA.array, medB.array, BitQuality.Medium);
        S.AssertRoundTrip(lowA.array, lowB.array, BitQuality.Low);
        S.AssertRoundTrip(vlowA.array, vlowB.array, BitQuality.VeryLow);
    }
}
