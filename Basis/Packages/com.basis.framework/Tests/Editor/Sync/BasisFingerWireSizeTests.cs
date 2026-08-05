using Basis.Network.Core.Compression;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using Q = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Pins the exact geometry of the rotation bitstream.
    ///
    /// Every number here was recomputed from the tables rather than copied from a comment, because
    /// the comments had already drifted once: BPC_HIGH claimed 1182 bits / 148 rotation bytes / a
    /// 169-byte packet against a then-real 1302 / 163 / 232, and all four tiers understated the
    /// packet by the hips-rotation tail.
    ///
    /// v47 split the region in two. Slots 0..20 still carry smallest-three rotations; the thirty
    /// finger joints that used to follow them are gone, replaced by ten curl/splay channels. The
    /// share those fingers used to cost — 41.9% of the High stream — is the budget this was measured
    /// against, so the before/after figures are asserted rather than described.
    /// </summary>
    public class BasisFingerWireSizeTests
    {
        const int FirstFingerSlot = 21;
        const int FingerSlotCount = 30;
        const int FirstFingerBone = (int)HumanBodyBones.LeftThumbProximal;   // 24
        const int LastFingerBone = (int)HumanBodyBones.RightLittleDistal;    // 53

        static int BoneBits(byte[] table)
        {
            int bits = 0;
            for (int slot = 0; slot < BasisBoneRotationCompression.WireBoneSlotCount; slot++)
                bits += 2 + 3 * table[slot];
            return bits;
        }

        // ────────────────────────────────────────────────────────────
        //  Table shape
        // ────────────────────────────────────────────────────────────

        [Test]
        public void SyncBoneCount_MatchesWriteOrder()
        {
            Assert.AreEqual(51, BasisBoneRotationCompression.SyncBoneCount);
            Assert.AreEqual(BasisBoneRotationCompression.SyncBoneCount,
                BasisBoneRotationCompression.BONE_WRITE_ORDER.Length);
        }

        [Test]
        public void WireLayout_IsTwentyOneBoneSlotsAndTenFingerChannels()
        {
            Assert.AreEqual(21, BasisBoneRotationCompression.WireBoneSlotCount);
            Assert.AreEqual(10, BasisBoneRotationCompression.FingerChannelCount);
            Assert.AreEqual(31, BasisBoneRotationCompression.RotationFieldCount);
            Assert.AreEqual(FirstFingerSlot, BasisBoneRotationCompression.WireBoneSlotCount,
                "the explicit slots must stop exactly where the finger joints begin");
        }

        [TestCase(Q.High)]
        [TestCase(Q.Medium)]
        [TestCase(Q.Low)]
        [TestCase(Q.VeryLow)]
        public void BpcTable_IsOneEntryPerSlot(Q quality)
        {
            Assert.AreEqual(BasisBoneRotationCompression.SyncBoneCount,
                BasisBoneRotationCompression.GetBpcTable(quality).Length);
        }

        [Test]
        public void MaxComponentTable_IsOneEntryPerSlot()
        {
            Assert.AreEqual(BasisBoneRotationCompression.SyncBoneCount,
                BasisBoneRotationCompression.MAX_COMPONENT.Length);
        }

        [Test]
        public void MaxComponent_StaysWithinSmallestThreeBound()
        {
            float[] max = BasisBoneRotationCompression.MAX_COMPONENT;
            for (int slot = 0; slot < max.Length; slot++)
            {
                Assert.Greater(max[slot], 0f, $"slot {slot} has a non-positive component range");
                Assert.LessOrEqual(max[slot], BasisBoneRotationCompression.InvSqrt2 + 1e-6f,
                    $"slot {slot} exceeds 1/sqrt(2), which no non-dropped component can reach");
            }
        }

        [TestCase(Q.High)]
        [TestCase(Q.Medium)]
        [TestCase(Q.Low)]
        [TestCase(Q.VeryLow)]
        public void EveryFieldWidth_FitsTheSixtyFourBitPackedWord(Q quality)
        {
            int[] widths = BasisBoneRotationCompression.BuildRotationFieldWidths(quality);
            Assert.AreEqual(BasisBoneRotationCompression.RotationFieldCount, widths.Length);
            for (int i = 0; i < widths.Length; i++)
            {
                Assert.Greater(widths[i], 0, $"field {i} is zero-width");
                Assert.LessOrEqual(widths[i], 64, $"field {i} packs past the ulong the codec returns");
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Exact sizes
        // ────────────────────────────────────────────────────────────

        [TestCase(Q.High, 896, 112)]
        [TestCase(Q.Medium, 624, 78)]
        [TestCase(Q.Low, 496, 62)]
        [TestCase(Q.VeryLow, 413, 52)]
        public void RotationStream_IsExactlyThisManyBits(Q quality, int expectedBits, int expectedBytes)
        {
            Assert.AreEqual(expectedBits, BasisBoneRotationCompression.RotationBits(quality));
            Assert.AreEqual(expectedBytes, BasisBoneRotationCompression.RotationBytes(quality));
            Assert.AreEqual(expectedBytes, BasisAvatarBitPacking.MuscleBytes(quality));
        }

        [TestCase(Q.High, 177)]
        [TestCase(Q.Medium, 108)]
        [TestCase(Q.Low, 92)]
        [TestCase(Q.VeryLow, 82)]
        public void Packet_IsExactlyThisManyBytes(Q quality, int expected)
        {
            Assert.AreEqual(expected, BasisAvatarBitPacking.ConvertToSize(quality));
            Assert.AreEqual(expected, BasisBoneRotationCompression.ConvertToSize(quality));
        }

        [TestCase(Q.High, 9, 35)]
        [TestCase(Q.Medium, 9, 0)]
        [TestCase(Q.Low, 9, 0)]
        [TestCase(Q.VeryLow, 9, 0)]
        public void Packet_IsPositionPlusRotationPlusTailPlusEffectors(Q quality, int posBytes, int effectorBytes)
        {
            Assert.AreEqual(posBytes, BasisAvatarBitPacking.PositionBytes(quality));
            Assert.AreEqual(effectorBytes, BasisBoneRotationCompression.EndEffectorBytes(quality));
            Assert.AreEqual(21, BasisAvatarBitPacking.TailBytes);

            int sum = posBytes + BasisBoneRotationCompression.RotationBytes(quality)
                + BasisAvatarBitPacking.TailBytes + effectorBytes;
            Assert.AreEqual(sum, BasisAvatarBitPacking.ConvertToSize(quality));
        }

        // ────────────────────────────────────────────────────────────
        //  What the finger block cost, and what it costs now
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// The pre-v47 cost, still computable from the retained BPC entries. If someone reintroduces
        /// explicit finger rotations these are the numbers they would be paying again.
        /// </summary>
        [TestCase(Q.High, 546, 756)]
        [TestCase(Q.Medium, 468, 504)]
        [TestCase(Q.Low, 378, 396)]
        [TestCase(Q.VeryLow, 288, 333)]
        public void ExplicitFingerRotations_WouldHaveCostThis(Q quality, int expectedFingerBits, int expectedBoneBits)
        {
            byte[] table = BasisBoneRotationCompression.GetBpcTable(quality);
            int fingerBits = 0;
            for (int slot = FirstFingerSlot; slot < FirstFingerSlot + FingerSlotCount; slot++)
                fingerBits += 2 + 3 * table[slot];

            Assert.AreEqual(expectedFingerBits, fingerBits);
            Assert.AreEqual(expectedBoneBits, BoneBits(table));
        }

        [TestCase(Q.High, 8, 6, 140)]
        [TestCase(Q.Medium, 7, 5, 120)]
        [TestCase(Q.Low, 6, 4, 100)]
        [TestCase(Q.VeryLow, 5, 3, 80)]
        public void FingerBlock_CostsThisInstead(Q quality, int curlBits, int splayBits, int totalBits)
        {
            Assert.AreEqual(curlBits, BasisBoneRotationCompression.CurlBits(quality));
            Assert.AreEqual(splayBits, BasisBoneRotationCompression.SplayBits(quality));
            Assert.AreEqual(curlBits + splayBits, BasisBoneRotationCompression.FingerFieldWidth(quality));
            Assert.AreEqual(totalBits,
                BasisBoneRotationCompression.FingerChannelCount * BasisBoneRotationCompression.FingerFieldWidth(quality));

            Assert.AreEqual(BoneBits(BasisBoneRotationCompression.GetBpcTable(quality)) + totalBits,
                BasisBoneRotationCompression.RotationBits(quality));
        }

        [Test]
        public void FingerSlots_AreTheThirtyFingerBones()
        {
            int[] order = BasisBoneRotationCompression.BONE_WRITE_ORDER;

            for (int slot = 0; slot < FirstFingerSlot; slot++)
            {
                Assert.IsFalse(order[slot] >= FirstFingerBone && order[slot] <= LastFingerBone,
                    $"slot {slot} holds finger bone {(HumanBodyBones)order[slot]} but sits in the explicit range");
            }

            var seen = new HashSet<int>();
            for (int slot = FirstFingerSlot; slot < FirstFingerSlot + FingerSlotCount; slot++)
            {
                int bone = order[slot];
                Assert.GreaterOrEqual(bone, FirstFingerBone, $"slot {slot} is not a finger bone");
                Assert.LessOrEqual(bone, LastFingerBone, $"slot {slot} is not a finger bone");
                Assert.IsTrue(seen.Add(bone), $"bone {(HumanBodyBones)bone} appears twice");
            }

            Assert.AreEqual(FingerSlotCount, seen.Count);
            Assert.AreEqual(FirstFingerSlot + FingerSlotCount, BasisBoneRotationCompression.SyncBoneCount);
        }

        /// <summary>
        /// The expansion path indexes slots as <c>21 + joint*10 + finger</c>. That only holds because
        /// BONE_WRITE_ORDER groups the finger slots by joint tier rather than by finger, so assert
        /// the ordering the arithmetic depends on instead of trusting it.
        /// </summary>
        [Test]
        public void FingerSlots_AreGroupedByJointTierNotByFinger()
        {
            int[] order = BasisBoneRotationCompression.BONE_WRITE_ORDER;
            for (int joint = 0; joint < 3; joint++)
            {
                for (int finger = 0; finger < 10; finger++)
                {
                    int slot = FirstFingerSlot + joint * 10 + finger;
                    int expectedBone = FirstFingerBone + finger * 3 + joint;
                    Assert.AreEqual(expectedBone, order[slot],
                        $"slot {slot} should be {(HumanBodyBones)expectedBone} (finger {finger}, joint {joint})");
                }
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Slot mapping
        // ────────────────────────────────────────────────────────────

        [Test]
        public void WriteOrder_CoversEveryBoneExceptHipsEyesAndJaw()
        {
            var excluded = new HashSet<int>
            {
                (int)HumanBodyBones.Hips,
                (int)HumanBodyBones.LeftEye,
                (int)HumanBodyBones.RightEye,
                (int)HumanBodyBones.Jaw,
            };

            var carried = new HashSet<int>(BasisBoneRotationCompression.BONE_WRITE_ORDER);
            Assert.AreEqual(BasisBoneRotationCompression.SyncBoneCount, carried.Count,
                "BONE_WRITE_ORDER carries a duplicate");

            for (int bone = 0; bone <= (int)HumanBodyBones.UpperChest; bone++)
            {
                bool expected = !excluded.Contains(bone);
                Assert.AreEqual(expected, carried.Contains(bone),
                    $"{(HumanBodyBones)bone} is {(expected ? "missing from" : "present in")} the write order");
            }
        }

        [Test]
        public void BoneToSlot_RoundTripsTheWriteOrder()
        {
            int[] order = BasisBoneRotationCompression.BONE_WRITE_ORDER;
            int[] toSlot = BasisBoneRotationCompression.BONE_TO_SLOT;

            Assert.AreEqual(55, toSlot.Length);
            for (int slot = 0; slot < order.Length; slot++)
            {
                Assert.AreEqual(slot, toSlot[order[slot]],
                    $"{(HumanBodyBones)order[slot]} does not map back to slot {slot}");
            }

            Assert.AreEqual(-1, toSlot[(int)HumanBodyBones.Hips]);
            Assert.AreEqual(-1, toSlot[(int)HumanBodyBones.LeftEye]);
            Assert.AreEqual(-1, toSlot[(int)HumanBodyBones.RightEye]);
            Assert.AreEqual(-1, toSlot[(int)HumanBodyBones.Jaw]);
        }

        // ────────────────────────────────────────────────────────────
        //  Bit offsets
        // ────────────────────────────────────────────────────────────

        [TestCase(Q.High)]
        [TestCase(Q.Medium)]
        [TestCase(Q.Low)]
        [TestCase(Q.VeryLow)]
        public void FieldOffsets_MatchARunningSumOfWidths(Q quality)
        {
            int[] widths = BasisBoneRotationCompression.BuildRotationFieldWidths(quality);
            var offsets = new int[BasisBoneRotationCompression.RotationFieldCount];
            int total = BasisBoneRotationCompression.BuildRotationFieldOffsets(quality, offsets);

            int running = 0;
            for (int i = 0; i < widths.Length; i++)
            {
                Assert.AreEqual(running, offsets[i], $"field {i} starts at the wrong bit");
                running += widths[i];
            }

            Assert.AreEqual(running, total);
            Assert.AreEqual(BasisBoneRotationCompression.RotationBits(quality), total);
            Assert.LessOrEqual(total, BasisBoneRotationCompression.RotationBytes(quality) * 8,
                "the stream overruns the byte count it reports");
        }
    }
}
