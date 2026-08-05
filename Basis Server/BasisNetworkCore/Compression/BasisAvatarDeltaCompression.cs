using System;
using System.Runtime.CompilerServices;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Server→client avatar delta compression. A keyframe is the full fixed-size payload
    /// (<see cref="BasisBoneRotationCompression.ConvertToSize"/>). A delta encodes only the fields
    /// that changed since the last keyframe, each carrying its absolute value, preceded by a
    /// per-field dirty mask. Reconstructing a full payload = copy the baseline keyframe, then
    /// overwrite the dirty fields. Pure C#; shared by the server (encode) and client (decode).
    ///
    /// Fields: [0]=position, [1..31]=the rotation region's 31 wire fields (21 explicit bone
    /// rotations then 10 finger curl/splay channels, all bit-packed), then scale, body rotation,
    /// hips delta, hips rotation and the end-effector block.
    ///
    /// Delta body layout:
    ///   [dirtyMask:DirtyMaskBytes][changed byte-fields in field order][changed rotation sub-bitstream]
    /// The byte-aligned fields (position/scale/rots) are copied verbatim; the dirty rotation fields
    /// are packed contiguously into a byte-aligned sub-bitstream via the existing WriteBits.
    ///
    /// Giving each finger its own field is what makes a still hand free in a delta: the ten channels
    /// are independent, so a player moving one finger pays for one.
    /// </summary>
    public static class BasisAvatarDeltaCompression
    {
        public const int BoneFieldStart = 1;
        public const int FieldCount = 1 + BasisBoneRotationCompression.RotationFieldCount + 5; // 37 (incl. end-effector block)
        public const int DirtyMaskBytes = (FieldCount + 7) >> 3;                               // 5

        private const int FieldPosition = 0;
        private const int FieldScale = 1 + BasisBoneRotationCompression.RotationFieldCount; // 32
        private const int FieldBodyRot = FieldScale + 1;                                    // 33
        private const int FieldHipsDelta = FieldScale + 2;                                  // 34
        private const int FieldHipsRot = FieldScale + 3;                                    // 35
        private const int FieldEndEffector = FieldScale + 4;                                // 36 (High only)

        private sealed class QualityGeometry
        {
            public int PosBytes;          // 12 on High (float32), 9 otherwise (int24 mm)
            public int RotBytes;
            public int PayloadSize;
            public int ScaleOffset;
            public int BodyRotOffset;
            public int HipsDeltaOffset;
            public int HipsRotOffset;
            public int EndEffectorOffset;
            public int EndEffectorBytes;  // 39 on High, 0 otherwise
            public int[] BoneBitOffset;   // RotationFieldCount, relative to the rotation region start
            public int[] BoneWidth;       // RotationFieldCount
        }

        private static readonly QualityGeometry[] Geo = new QualityGeometry[4];

        static BasisAvatarDeltaCompression()
        {
            for (int qi = 0; qi < 4; qi++)
            {
                var q = (BasisAvatarBitPacking.BitQuality)qi;
                int n = BasisBoneRotationCompression.RotationFieldCount;
                int[] offsets = new int[n];
                BasisBoneRotationCompression.BuildRotationFieldOffsets(q, offsets);
                int[] widths = BasisBoneRotationCompression.BuildRotationFieldWidths(q);

                int posBytes = BasisAvatarBitPacking.PositionBytes(q);
                int rotBytes = BasisBoneRotationCompression.RotationBytes(q);
                int tailStart = posBytes + rotBytes;
                Geo[qi] = new QualityGeometry
                {
                    PosBytes = posBytes,
                    RotBytes = rotBytes,
                    PayloadSize = BasisBoneRotationCompression.ConvertToSize(q),
                    ScaleOffset = tailStart,
                    BodyRotOffset = tailStart + BasisBoneRotationCompression.WriteScale,
                    HipsDeltaOffset = tailStart + BasisBoneRotationCompression.WriteScale + BasisBoneRotationCompression.WriteRotation,
                    HipsRotOffset = tailStart + BasisBoneRotationCompression.WriteScale + BasisBoneRotationCompression.WriteRotation + BasisBoneRotationCompression.WriteHipsDelta,
                    EndEffectorOffset = tailStart + BasisBoneRotationCompression.TailBytes,   // right after the 22B tail
                    EndEffectorBytes = BasisBoneRotationCompression.EndEffectorBytes(q),
                    BoneBitOffset = offsets,
                    BoneWidth = widths,
                };
            }
        }

        public static int PayloadSize(BasisAvatarBitPacking.BitQuality q) => Geo[(int)q].PayloadSize;

        /// <summary>
        /// Worst-case delta body length for a quality (full mask). Callers size scratch buffers with this.
        /// </summary>
        public static int MaxDeltaSize(BasisAvatarBitPacking.BitQuality q) => DirtyMaskBytes + Geo[(int)q].PayloadSize;

        /// <summary>
        /// Builds a delta of <paramref name="current"/> against <paramref name="keyframe"/> into
        /// <paramref name="dst"/> at <paramref name="dstStart"/>. Both payloads must be at least
        /// PayloadSize(q) bytes; dst must have room for MaxDeltaSize(q) from dstStart. Returns the delta
        /// body length written, or -1 on bad input. The caller compares the returned length to
        /// PayloadSize(q) to decide keyframe promotion.
        /// </summary>
        public static int BuildDelta(byte[] keyframe, byte[] current, BasisAvatarBitPacking.BitQuality q, byte[] dst, int dstStart)
        {
            var g = Geo[(int)q];
            if (keyframe == null || current == null || dst == null) return -1;
            if (keyframe.Length < g.PayloadSize || current.Length < g.PayloadSize) return -1;
            if (dst.Length - dstStart < DirtyMaskBytes + g.PayloadSize) return -1;

            Span<byte> mask = stackalloc byte[DirtyMaskBytes];
            mask.Clear();

            if (!SpanEqual(current, 0, keyframe, 0, g.PosBytes))
                SetBit(mask, FieldPosition);

            int boneBaseBit = g.PosBytes * 8;
            for (int s = 0; s < g.BoneBitOffset.Length; s++)
            {
                int a = boneBaseBit + g.BoneBitOffset[s];
                int b = a;
                ulong cur = BasisBoneRotationCompression.ReadBits(current, ref a, g.BoneWidth[s]);
                ulong kf = BasisBoneRotationCompression.ReadBits(keyframe, ref b, g.BoneWidth[s]);
                if (cur != kf) SetBit(mask, BoneFieldStart + s);
            }

            if (!SpanEqual(current, g.ScaleOffset, keyframe, g.ScaleOffset, BasisBoneRotationCompression.WriteScale))
                SetBit(mask, FieldScale);
            if (!SpanEqual(current, g.BodyRotOffset, keyframe, g.BodyRotOffset, BasisBoneRotationCompression.WriteRotation))
                SetBit(mask, FieldBodyRot);
            if (!SpanEqual(current, g.HipsDeltaOffset, keyframe, g.HipsDeltaOffset, BasisBoneRotationCompression.WriteHipsDelta))
                SetBit(mask, FieldHipsDelta);
            if (!SpanEqual(current, g.HipsRotOffset, keyframe, g.HipsRotOffset, BasisBoneRotationCompression.WriteHipsRotation))
                SetBit(mask, FieldHipsRot);
            if (g.EndEffectorBytes > 0 && !SpanEqual(current, g.EndEffectorOffset, keyframe, g.EndEffectorOffset, g.EndEffectorBytes))
                SetBit(mask, FieldEndEffector);

            int o = dstStart;
            for (int i = 0; i < DirtyMaskBytes; i++) dst[o++] = mask[i];

            if (GetBit(mask, FieldPosition)) { Buffer.BlockCopy(current, 0, dst, o, g.PosBytes); o += g.PosBytes; }
            if (GetBit(mask, FieldScale)) { Buffer.BlockCopy(current, g.ScaleOffset, dst, o, BasisBoneRotationCompression.WriteScale); o += BasisBoneRotationCompression.WriteScale; }
            if (GetBit(mask, FieldBodyRot)) { Buffer.BlockCopy(current, g.BodyRotOffset, dst, o, BasisBoneRotationCompression.WriteRotation); o += BasisBoneRotationCompression.WriteRotation; }
            if (GetBit(mask, FieldHipsDelta)) { Buffer.BlockCopy(current, g.HipsDeltaOffset, dst, o, BasisBoneRotationCompression.WriteHipsDelta); o += BasisBoneRotationCompression.WriteHipsDelta; }
            if (GetBit(mask, FieldHipsRot)) { Buffer.BlockCopy(current, g.HipsRotOffset, dst, o, BasisBoneRotationCompression.WriteHipsRotation); o += BasisBoneRotationCompression.WriteHipsRotation; }
            if (GetBit(mask, FieldEndEffector)) { Buffer.BlockCopy(current, g.EndEffectorOffset, dst, o, g.EndEffectorBytes); o += g.EndEffectorBytes; }

            int boneBits = 0;
            for (int s = 0; s < g.BoneWidth.Length; s++)
                if (GetBit(mask, BoneFieldStart + s)) boneBits += g.BoneWidth[s];
            int boneBytes = (boneBits + 7) >> 3;
            for (int i = 0; i < boneBytes; i++) dst[o + i] = 0; // WriteBits ORs — clear first

            int subBit = o * 8;
            for (int s = 0; s < g.BoneWidth.Length; s++)
            {
                if (!GetBit(mask, BoneFieldStart + s)) continue;
                int rp = boneBaseBit + g.BoneBitOffset[s];
                ulong val = BasisBoneRotationCompression.ReadBits(current, ref rp, g.BoneWidth[s]);
                BasisBoneRotationCompression.WriteBits(dst, subBit, val, g.BoneWidth[s]);
                subBit += g.BoneWidth[s];
            }
            o += boneBytes;

            return o - dstStart;
        }

        /// <summary>
        /// Reconstructs the full payload from <paramref name="baseline"/> (last keyframe) plus the delta
        /// body in <paramref name="delta"/>[deltaStart, deltaStart+deltaLen). Writes PayloadSize(q) bytes
        /// into <paramref name="outFull"/>. Returns false if the delta is malformed/truncated. Never
        /// mutates the baseline.
        /// </summary>
        public static bool TryApplyDelta(byte[] baseline, byte[] delta, int deltaStart, int deltaLen, BasisAvatarBitPacking.BitQuality q, byte[] outFull)
        {
            var g = Geo[(int)q];
            if (baseline == null || baseline.Length < g.PayloadSize) return false;
            if (outFull == null || outFull.Length < g.PayloadSize) return false;
            if (delta == null || deltaLen < DirtyMaskBytes) return false;
            if (deltaStart < 0 || deltaStart + deltaLen > delta.Length) return false;

            var mask = new ReadOnlySpan<byte>(delta, deltaStart, DirtyMaskBytes);

            int expected = ExpectedBodyLength(mask, g);
            if (deltaLen != expected) return false;

            Buffer.BlockCopy(baseline, 0, outFull, 0, g.PayloadSize);

            int o = deltaStart + DirtyMaskBytes;
            if (GetBit(mask, FieldPosition)) { Buffer.BlockCopy(delta, o, outFull, 0, g.PosBytes); o += g.PosBytes; }
            if (GetBit(mask, FieldScale)) { Buffer.BlockCopy(delta, o, outFull, g.ScaleOffset, BasisBoneRotationCompression.WriteScale); o += BasisBoneRotationCompression.WriteScale; }
            if (GetBit(mask, FieldBodyRot)) { Buffer.BlockCopy(delta, o, outFull, g.BodyRotOffset, BasisBoneRotationCompression.WriteRotation); o += BasisBoneRotationCompression.WriteRotation; }
            if (GetBit(mask, FieldHipsDelta)) { Buffer.BlockCopy(delta, o, outFull, g.HipsDeltaOffset, BasisBoneRotationCompression.WriteHipsDelta); o += BasisBoneRotationCompression.WriteHipsDelta; }
            if (GetBit(mask, FieldHipsRot)) { Buffer.BlockCopy(delta, o, outFull, g.HipsRotOffset, BasisBoneRotationCompression.WriteHipsRotation); o += BasisBoneRotationCompression.WriteHipsRotation; }
            if (GetBit(mask, FieldEndEffector)) { Buffer.BlockCopy(delta, o, outFull, g.EndEffectorOffset, g.EndEffectorBytes); o += g.EndEffectorBytes; }

            int boneBaseBit = g.PosBytes * 8;
            int subBit = o * 8;
            for (int s = 0; s < g.BoneWidth.Length; s++)
            {
                if (!GetBit(mask, BoneFieldStart + s)) continue;
                ulong val = BasisBoneRotationCompression.ReadBits(delta, ref subBit, g.BoneWidth[s]);
                WriteBitsOverwrite(outFull, boneBaseBit + g.BoneBitOffset[s], val, g.BoneWidth[s]);
            }
            return true;
        }

        /// <summary>
        /// Reads the dirty mask at the start of a delta body and returns the total body length in bytes
        /// (mask + changed fields), or -1 if fewer than DirtyMaskBytes are available. The receive path
        /// uses this to split a delta frame's body from any trailing additional-data section.
        /// </summary>
        public static int DeltaBodyLength(byte[] delta, int start, int available, BasisAvatarBitPacking.BitQuality q)
        {
            if (delta == null || available < DirtyMaskBytes || start < 0 || start + DirtyMaskBytes > delta.Length)
                return -1;
            var mask = new ReadOnlySpan<byte>(delta, start, DirtyMaskBytes);
            return ExpectedBodyLength(mask, Geo[(int)q]);
        }

        private static int ExpectedBodyLength(ReadOnlySpan<byte> mask, QualityGeometry g)
        {
            int expected = DirtyMaskBytes;
            if (GetBit(mask, FieldPosition)) expected += g.PosBytes;
            if (GetBit(mask, FieldScale)) expected += BasisBoneRotationCompression.WriteScale;
            if (GetBit(mask, FieldBodyRot)) expected += BasisBoneRotationCompression.WriteRotation;
            if (GetBit(mask, FieldHipsDelta)) expected += BasisBoneRotationCompression.WriteHipsDelta;
            if (GetBit(mask, FieldHipsRot)) expected += BasisBoneRotationCompression.WriteHipsRotation;
            if (GetBit(mask, FieldEndEffector)) expected += g.EndEffectorBytes;
            int boneBits = 0;
            for (int s = 0; s < g.BoneWidth.Length; s++)
                if (GetBit(mask, BoneFieldStart + s)) boneBits += g.BoneWidth[s];
            expected += (boneBits + 7) >> 3;
            return expected;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetBit(Span<byte> mask, int field) => mask[field >> 3] |= (byte)(1 << (field & 7));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool GetBit(ReadOnlySpan<byte> mask, int field) => (mask[field >> 3] & (1 << (field & 7))) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SpanEqual(byte[] a, int aOff, byte[] b, int bOff, int len)
        {
            for (int i = 0; i < len; i++) if (a[aOff + i] != b[bOff + i]) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteBitsOverwrite(byte[] dst, int bitPos, ulong value, int bitCount)
        {
            int bytePos = bitPos >> 3;
            int bitInByte = bitPos & 7;
            ulong v = value;
            int bitsLeft = bitCount;
            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;
                int lowMask = (1 << take) - 1;
                int clearMask = lowMask << bitInByte;
                byte chunk = (byte)((int)(v & (ulong)lowMask) << bitInByte);
                dst[bytePos] = (byte)((dst[bytePos] & ~clearMask) | chunk);
                v >>= take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }
        }
    }
}
