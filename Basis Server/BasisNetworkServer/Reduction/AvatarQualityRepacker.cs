using Basis.Network.Core.Compression;
using System;
using System.Runtime.CompilerServices;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    /// <summary>
    /// Server-side repacker that converts HIGH quality bone rotation data
    /// into medium/low/very-low quality by re-quantizing each bone's
    /// smallest-three components at a lower bits-per-component (BPC).
    /// </summary>
    public static class AvatarQualityRepacker
    {
        static readonly int BoneSlots = BasisBoneRotationCompression.WireBoneSlotCount;   // 21
        static readonly int FingerSlots = BasisBoneRotationCompression.FingerChannelCount; // 10

        // Cache BPC tables for each quality
        static readonly byte[] HighBpc  = BasisBoneRotationCompression.BPC_HIGH;
        static readonly byte[] MedBpc   = BasisBoneRotationCompression.BPC_MEDIUM;
        static readonly byte[] LowBpc   = BasisBoneRotationCompression.BPC_LOW;
        static readonly byte[] VLowBpc  = BasisBoneRotationCompression.BPC_VERY_LOW;

        // Cache byte counts (via MuscleBytes which now routes to RotationBytes)
        static readonly int HighRotBytes = MuscleBytes(BitQuality.High);
        static readonly int MedRotBytes  = MuscleBytes(BitQuality.Medium);
        static readonly int LowRotBytes  = MuscleBytes(BitQuality.Low);
        static readonly int VLowRotBytes = MuscleBytes(BitQuality.VeryLow);

        // Cache payload sizes. Every tier carries int24-mm position (9B), so the field is a
        // straight copy rather than a transcode.
        static readonly int PosBytes = WritePosition;
        static readonly int HighPayloadSize = PosBytes + HighRotBytes + TailBytes;
        static readonly int MedPayloadSize  = PosBytes + MedRotBytes  + TailBytes;
        static readonly int LowPayloadSize  = PosBytes + LowRotBytes  + TailBytes;
        static readonly int VLowPayloadSize = PosBytes + VLowRotBytes + TailBytes;

        // Cache per-bone bit offsets for each quality
        static readonly int[] HighOffs = BuildBitOffsets(BitQuality.High);
        static readonly int[] MedOffs  = BuildBitOffsets(BitQuality.Medium);
        static readonly int[] LowOffs  = BuildBitOffsets(BitQuality.Low);
        static readonly int[] VLowOffs = BuildBitOffsets(BitQuality.VeryLow);

        static int[] BuildBitOffsets(BitQuality q)
        {
            var offs = new int[BasisBoneRotationCompression.RotationFieldCount];
            BasisBoneRotationCompression.BuildRotationFieldOffsets(q, offs);
            return offs;
        }

        public static void BuildAllLowerFromHighInto(
            in SerializableBasis.LocalAvatarSyncMessage srcHigh,
            ref SerializableBasis.LocalAvatarSyncMessage medium,
            ref SerializableBasis.LocalAvatarSyncMessage low,
            ref SerializableBasis.LocalAvatarSyncMessage veryLow)
        {
            if (srcHigh.array == null)
                throw new ArgumentNullException(nameof(srcHigh.array));

            if (srcHigh.array.Length < HighPayloadSize)
                throw new ArgumentException($"High payload too small. Need >= {HighPayloadSize}, got {srcHigh.array.Length}");

            EnsureBuffer(ref medium, BitQuality.Medium, MedPayloadSize);
            EnsureBuffer(ref low, BitQuality.Low, LowPayloadSize);
            EnsureBuffer(ref veryLow, BitQuality.VeryLow, VLowPayloadSize);

            // Position: int24-mm at every tier, so it copies across untouched
            Buffer.BlockCopy(srcHigh.array, 0, medium.array, 0, PosBytes);
            Buffer.BlockCopy(srcHigh.array, 0, low.array, 0, PosBytes);
            Buffer.BlockCopy(srcHigh.array, 0, veryLow.array, 0, PosBytes);

            int srcRotBase = PosBytes;
            int rotBase = PosBytes;

            // Clear rotation regions (BitWriter ORs into bytes)
            Array.Clear(medium.array, rotBase, MedRotBytes);
            Array.Clear(low.array, rotBase, LowRotBytes);
            Array.Clear(veryLow.array, rotBase, VLowRotBytes);

            // Repack each explicit bone. 3-DOF slots: read smallest-three at HIGH BPC, rescale
            // to lower BPC. Restricted slots (v52): one or two uniformly quantized angles whose
            // ranges are quality-invariant, so they rescale on the same integer ladder as fingers.
            for (int slot = 0; slot < BoneSlots; slot++)
            {
                if (BasisBoneRotationCompression.BONE_DOF[slot] == 3)
                {
                    int bpcSrc = HighBpc[slot];
                    int totalBitsSrc = 2 + 3 * bpcSrc;

                    // Read the full packed bone (index + 3 components) as raw bits
                    ulong raw = BitReader.ReadBitsU64(srcHigh.array, srcRotBase, HighOffs[slot], totalBitsSrc);

                    // Extract the 2-bit index (which component was dropped)
                    uint idx = (uint)(raw & 3UL);

                    // Extract 3 components at source BPC
                    uint maskSrc = (uint)((1 << bpcSrc) - 1);
                    uint qa = (uint)((raw >> 2) & maskSrc);
                    uint qb = (uint)((raw >> (2 + bpcSrc)) & maskSrc);
                    uint qc = (uint)((raw >> (2 + 2 * bpcSrc)) & maskSrc);

                    // Rescale and write for each target quality
                    RepackBone(medium.array, rotBase, MedOffs[slot], MedBpc[slot], idx, qa, qb, qc, bpcSrc);
                    RepackBone(low.array, rotBase, LowOffs[slot], LowBpc[slot], idx, qa, qb, qc, bpcSrc);
                    RepackBone(veryLow.array, rotBase, VLowOffs[slot], VLowBpc[slot], idx, qa, qb, qc, bpcSrc);
                }
                else
                {
                    RepackRestrictedBone(srcHigh.array, srcRotBase, slot, medium.array, rotBase, MedOffs[slot], BitQuality.Medium);
                    RepackRestrictedBone(srcHigh.array, srcRotBase, slot, low.array, rotBase, LowOffs[slot], BitQuality.Low);
                    RepackRestrictedBone(srcHigh.array, srcRotBase, slot, veryLow.array, rotBase, VLowOffs[slot], BitQuality.VeryLow);
                }
            }

            // Finger channels: two independent signed-unit scalars per finger, so they rescale with
            // the same integer ladder the quaternion components use rather than being re-encoded
            // through a float round trip.
            int srcCurl = BasisBoneRotationCompression.CurlBits(BitQuality.High);
            int srcSplay = BasisBoneRotationCompression.SplayBits(BitQuality.High);
            for (int finger = 0; finger < FingerSlots; finger++)
            {
                int field = BoneSlots + finger;
                int srcBit = HighOffs[field];
                uint curl = (uint)BitReader.ReadBitsU64(srcHigh.array, srcRotBase, srcBit, srcCurl);
                uint splay = (uint)BitReader.ReadBitsU64(srcHigh.array, srcRotBase, srcBit + srcCurl, srcSplay);

                RepackFinger(medium.array, rotBase, MedOffs[field], BitQuality.Medium, curl, splay, srcCurl, srcSplay);
                RepackFinger(low.array, rotBase, LowOffs[field], BitQuality.Low, curl, splay, srcCurl, srcSplay);
                RepackFinger(veryLow.array, rotBase, VLowOffs[field], BitQuality.VeryLow, curl, splay, srcCurl, srcSplay);
            }

            // Copy tail (scale + body rotation)
            int srcTailOffset = PosBytes + HighRotBytes;
            Buffer.BlockCopy(srcHigh.array, srcTailOffset, medium.array, rotBase + MedRotBytes, TailBytes);
            Buffer.BlockCopy(srcHigh.array, srcTailOffset, low.array, rotBase + LowRotBytes, TailBytes);
            Buffer.BlockCopy(srcHigh.array, srcTailOffset, veryLow.array, rotBase + VLowRotBytes, TailBytes);
        }

        /// <summary>Rescales a restricted (1/2-DOF) bone's angle field(s) from High to a lower tier.
        /// Ranges are identical across tiers, so pure integer rescaling is exact.</summary>
        static void RepackRestrictedBone(byte[] src, int srcRotBase, int slot,
            byte[] dst, int dstRotBase, int dstBitOffset, BitQuality dstQuality)
        {
            int srcBit = HighOffs[slot];
            if (BasisBoneRotationCompression.BONE_DOF[slot] == 1)
            {
                int srcBits = BasisBoneRotationCompression.SingleAxisBits(BitQuality.High);
                int dstBits = BasisBoneRotationCompression.SingleAxisBits(dstQuality);
                uint v = (uint)BitReader.ReadBitsU64(src, srcRotBase, srcBit, srcBits);
                BitWriter.WriteBitsU64(dst, dstRotBase, dstBitOffset, RescaleQuant(v, srcBits, dstBits), dstBits);
                return;
            }

            int srcHinge = BasisBoneRotationCompression.HingeBits(BitQuality.High);
            int srcTwist = BasisBoneRotationCompression.TwistBits(BitQuality.High);
            int dstHinge = BasisBoneRotationCompression.HingeBits(dstQuality);
            int dstTwist = BasisBoneRotationCompression.TwistBits(dstQuality);
            uint hinge = (uint)BitReader.ReadBitsU64(src, srcRotBase, srcBit, srcHinge);
            uint twist = (uint)BitReader.ReadBitsU64(src, srcRotBase, srcBit + srcHinge, srcTwist);
            ulong packed = RescaleQuant(hinge, srcHinge, dstHinge)
                | ((ulong)RescaleQuant(twist, srcTwist, dstTwist) << dstHinge);
            BitWriter.WriteBitsU64(dst, dstRotBase, dstBitOffset, packed, dstHinge + dstTwist);
        }

        static void RepackFinger(byte[] dst, int baseByteOffset, int bitOffset, BitQuality dstQuality,
            uint curl, uint splay, int srcCurlBits, int srcSplayBits)
        {
            int dstCurlBits = BasisBoneRotationCompression.CurlBits(dstQuality);
            int dstSplayBits = BasisBoneRotationCompression.SplayBits(dstQuality);
            uint dstCurl = RescaleQuant(curl, srcCurlBits, dstCurlBits);
            uint dstSplay = RescaleQuant(splay, srcSplayBits, dstSplayBits);

            ulong packed = dstCurl | ((ulong)dstSplay << dstCurlBits);
            BitWriter.WriteBitsU64(dst, baseByteOffset, bitOffset, packed, dstCurlBits + dstSplayBits);
        }

        static void RepackBone(byte[] dst, int baseByteOffset, int bitOffset, int bpcDst,
            uint idx, uint qa, uint qb, uint qc, int bpcSrc)
        {
            // Rescale each component from source BPC to destination BPC
            uint da = RescaleQuant(qa, bpcSrc, bpcDst);
            uint db = RescaleQuant(qb, bpcSrc, bpcDst);
            uint dc = RescaleQuant(qc, bpcSrc, bpcDst);

            // Pack: [idx:2][da:bpcDst][db:bpcDst][dc:bpcDst]
            ulong packed = (ulong)idx
                | ((ulong)da << 2)
                | ((ulong)db << (2 + bpcDst))
                | ((ulong)dc << (2 + 2 * bpcDst));

            int totalBits = 2 + 3 * bpcDst;
            BitWriter.WriteBitsU64(dst, baseByteOffset, bitOffset, packed, totalBits);
        }

        static void EnsureBuffer(ref SerializableBasis.LocalAvatarSyncMessage msg, BitQuality q, int size)
        {
            msg.DataQualityLevel = (byte)q;
            if (msg.array != null && msg.array.Length >= size)
                return;
            msg.array = new byte[size];
        }

        public static (SerializableBasis.LocalAvatarSyncMessage medium,
                       SerializableBasis.LocalAvatarSyncMessage low,
                       SerializableBasis.LocalAvatarSyncMessage veryLow)
            BuildAllLowerFromHigh(in SerializableBasis.LocalAvatarSyncMessage srcHigh)
        {
            var med = new SerializableBasis.LocalAvatarSyncMessage();
            var low = new SerializableBasis.LocalAvatarSyncMessage();
            var vlow = new SerializableBasis.LocalAvatarSyncMessage();
            BuildAllLowerFromHighInto(srcHigh, ref med, ref low, ref vlow);
            return (med, low, vlow);
        }

        /// <summary>
        /// Rescales one quantized value from <paramref name="bSrc"/> bits to <paramref name="bDst"/>
        /// bits, rounding to nearest.
        ///
        /// <para>Every 3-DOF bone repacks three components into three lower tiers and every finger
        /// two channels into three, so one avatar frame runs this ~250 times — and it used to end in
        /// a 64-bit hardware divide, which is the slowest integer instruction on x86 by a wide margin
        /// (tens of cycles, and not pipelined, so they cannot overlap). The divisor is
        /// <c>2^bSrc - 1</c>: it depends only on the layout, never on the data, so it is known before
        /// any player connects and the divide can be a multiply by a fixed reciprocal instead.</para>
        ///
        /// <para><b>Exact by exhaustive check, not by argument.</b> <see cref="QuantRescaleTable"/>
        /// verifies each reciprocal against the real division across the divisor's ENTIRE input
        /// domain at static init, and refuses to install one that disagrees anywhere. A pair that
        /// fails falls back to a 32-bit divide — still cheaper than the 64-bit one this replaced,
        /// because the operands provably fit (see the bound in the table).</para>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint RescaleQuant(uint qSrc, int bSrc, int bDst)
        {
            if (bSrc == bDst) return qSrc;
            if (bDst <= 0) return 0;
            return QuantRescaleTable.Rescale(qSrc, bSrc, bDst);
        }

        /// <summary>
        /// The repacker addresses bits relative to the rotation region's base byte; the shared codec
        /// takes an absolute bit position. Kept as thin named shims so the call sites below still
        /// read as "read this field of that region".
        /// </summary>
        static class BitReader
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ulong ReadBitsU64(byte[] src, int baseByteOffset, int bitPos, int bitCount)
                => BasisBitCodec.Read(src, (baseByteOffset << 3) + bitPos, bitCount);
        }

        static class BitWriter
        {
            // The destination rotation region is Array.Clear'd before any of this runs, so OR is
            // the correct and cheaper form.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void WriteBitsU64(byte[] dst, int baseByteOffset, int bitPos, ulong value, int bitCount)
                => BasisBitCodec.Or(dst, (baseByteOffset << 3) + bitPos, value, bitCount);
        }
    }
}
