using System;
using System.Runtime.CompilerServices;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// What a channel's bits mean, and therefore what may be done to them arithmetically.
    /// </summary>
    public enum BasisChannelKind : byte
    {
        /// <summary>
        /// A uniformly quantized scalar. Adjacent integers are adjacent values, so the difference
        /// between two of them is meaningful (residual coding) and a reflected Gray code of the
        /// stored pattern differs in one bit between neighbours (bit-plane sweeping).
        /// </summary>
        Delta = 0,

        /// <summary>
        /// Categorical or opaque bits — a smallest-three "largest component" index, the effector
        /// mask, the posit16 scale, structural padding. Arithmetic on these is meaningless, so they
        /// are only ever carried verbatim.
        /// </summary>
        Raw = 1,
    }

    /// <summary>One channel: a contiguous run of bits in the payload with a single interpretation.</summary>
    public readonly struct BasisAvatarChannel
    {
        public readonly int BitOffset;
        public readonly byte Width;
        public readonly BasisChannelKind Kind;

        public BasisAvatarChannel(int bitOffset, int width, BasisChannelKind kind)
        {
            BitOffset = bitOffset;
            Width = (byte)width;
            Kind = kind;
        }

        /// <summary>Low-bit mask for this channel's width.</summary>
        public uint Mask
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Width >= 32 ? uint.MaxValue : (1u << Width) - 1u;
        }
    }

    /// <summary>
    /// The channel decomposition of one quality's avatar payload, grouped into the same dirty-mask
    /// fields <see cref="BasisAvatarDeltaCompression"/> has always used.
    ///
    /// The channel list is a TOTAL PARTITION of the payload: every bit belongs to exactly one
    /// channel, structural padding included. That is what lets both codecs rebuild a payload
    /// byte-for-byte from channel values alone, rather than having to fall back on copying
    /// unmodelled regions.
    /// </summary>
    public sealed class BasisAvatarChannelLayout
    {
        /// <summary>All channels, ordered by field then by bit offset (which is also payload order).</summary>
        public readonly BasisAvatarChannel[] Channels;

        /// <summary>Prefix bounds: field f owns Channels[FieldFirstChannel[f] .. FieldFirstChannel[f+1]).</summary>
        public readonly int[] FieldFirstChannel;

        public readonly int PayloadBytes;
        public readonly int PayloadBits;

        /// <summary>Total bits if every channel were written verbatim — the raw-mode worst case.</summary>
        public readonly int TotalChannelBits;

        /// <summary>Widest channel in the layout; the Gray sweep needs this many frames for a full pass.</summary>
        public readonly int MaxChannelWidth;

        public int FieldCount => FieldFirstChannel.Length - 1;

        internal BasisAvatarChannelLayout(BasisAvatarChannel[] channels, int[] fieldFirstChannel, int payloadBytes)
        {
            Channels = channels;
            FieldFirstChannel = fieldFirstChannel;
            PayloadBytes = payloadBytes;
            PayloadBits = payloadBytes * 8;
            int total = 0, widest = 0;
            for (int i = 0; i < channels.Length; i++)
            {
                total += channels[i].Width;
                if (channels[i].Width > widest) widest = channels[i].Width;
            }
            TotalChannelBits = total;
            MaxChannelWidth = widest;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FieldChannelStart(int field) => FieldFirstChannel[field];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FieldChannelEnd(int field) => FieldFirstChannel[field + 1];

        /// <summary>Sum of the widths of one field's channels — its verbatim cost.</summary>
        public int FieldRawBits(int field)
        {
            int bits = 0;
            for (int c = FieldFirstChannel[field]; c < FieldFirstChannel[field + 1]; c++) bits += Channels[c].Width;
            return bits;
        }
    }

    /// <summary>
    /// Builds the per-quality channel decomposition of an avatar payload.
    ///
    /// Payload geometry (see <see cref="BasisAvatarBitPacking"/> / <see cref="BasisBoneRotationCompression"/>):
    ///   [position      9 B]  3 x signed int24 millimetres, byte-aligned little-endian
    ///   [rotation    var B]  bit-packed LSB-first: 21 bone slots (2-bit largest index + 3 x BPC
    ///                        components) then 10 finger channels (curl + splay), then padding
    ///   [scale         2 B]  posit16
    ///   [body rot      7 B]  1 B largest index + 3 x uint16
    ///   [hips delta    5 B]  3 x signed 13-bit + 1 spare bit
    ///   [hips rot      7 B]  1 B largest index + 3 x uint16
    ///   [effectors    35 B]  High only: 8-bit mask + 4 x (3 x 12-bit position, 2-bit index, 3 x 10-bit rotation)
    ///
    /// Field indices match <see cref="BasisAvatarDeltaCompression"/>'s dirty-mask fields exactly, so
    /// the mask's meaning is unchanged from the verbatim-copy codec that preceded this.
    /// </summary>
    public static class BasisAvatarChannelMap
    {
        // Effector block geometry. Mirrored from BasisAvatarEndEffectors, which lives in
        // com.basis.framework and so cannot be referenced from the server core. EffectorBlockBits
        // is asserted against BasisBoneRotationCompression.EndEffectorBlockBytes at static init.
        private const int EffectorCount = 4;
        private const int EffectorPosBits = 12;
        private const int EffectorRotBpc = 10;
        private const int EffectorMaskBits = 8;
        private const int EffectorStride = 3 * EffectorPosBits + 2 + 3 * EffectorRotBpc;   // 68
        private const int EffectorBlockBits = EffectorMaskBits + EffectorCount * EffectorStride; // 280

        private static readonly BasisAvatarChannelLayout[] Layouts = new BasisAvatarChannelLayout[4];

        static BasisAvatarChannelMap()
        {
            for (int qi = 0; qi < 4; qi++)
                Layouts[qi] = Build((BasisAvatarBitPacking.BitQuality)qi);
        }

        public static BasisAvatarChannelLayout For(BasisAvatarBitPacking.BitQuality q) => Layouts[(int)q];

        private static BasisAvatarChannelLayout Build(BasisAvatarBitPacking.BitQuality q)
        {
            int posBytes = BasisAvatarBitPacking.PositionBytes(q);
            int rotBytes = BasisBoneRotationCompression.RotationBytes(q);
            int rotBits = BasisBoneRotationCompression.RotationBits(q);
            int payloadBytes = BasisBoneRotationCompression.ConvertToSize(q);
            int effBytes = BasisBoneRotationCompression.EndEffectorBytes(q);

            if (effBytes > 0 && effBytes * 8 != EffectorBlockBits)
                throw new InvalidOperationException($"Effector block geometry drifted: {effBytes} B vs {EffectorBlockBits} bits modelled.");

            int fieldCount = BasisAvatarDeltaCompression.FieldCount;
            var channels = new System.Collections.Generic.List<BasisAvatarChannel>(256);
            var fieldFirst = new int[fieldCount + 1];

            // ── Field 0: hips world position, 3 x signed int24 millimetres ──
            fieldFirst[0] = 0;
            for (int a = 0; a < 3; a++)
                channels.Add(new BasisAvatarChannel(a * 24, 24, BasisChannelKind.Delta));

            // ── Fields 1..31: the rotation region ──
            int rotBase = posBytes * 8;
            byte[] bpc = BasisBoneRotationCompression.GetBpcTable(q);
            int[] fieldOffsets = new int[BasisBoneRotationCompression.RotationFieldCount];
            BasisBoneRotationCompression.BuildRotationFieldOffsets(q, fieldOffsets);
            int curlBits = BasisBoneRotationCompression.CurlBits(q);
            int splayBits = BasisBoneRotationCompression.SplayBits(q);

            for (int slot = 0; slot < BasisBoneRotationCompression.WireBoneSlotCount; slot++)
            {
                fieldFirst[BasisAvatarDeltaCompression.BoneFieldStart + slot] = channels.Count;
                int b = rotBase + fieldOffsets[slot];
                // Smallest-three: the 2-bit index selects which component was dropped, so it changes
                // what the other three MEAN. Differencing across an index change is nonsense — Raw.
                channels.Add(new BasisAvatarChannel(b, 2, BasisChannelKind.Raw));
                for (int c = 0; c < 3; c++)
                    channels.Add(new BasisAvatarChannel(b + 2 + c * bpc[slot], bpc[slot], BasisChannelKind.Delta));
            }

            for (int f = 0; f < BasisBoneRotationCompression.FingerChannelCount; f++)
            {
                int field = BasisAvatarDeltaCompression.BoneFieldStart + BasisBoneRotationCompression.WireBoneSlotCount + f;
                fieldFirst[field] = channels.Count;
                int b = rotBase + fieldOffsets[BasisBoneRotationCompression.WireBoneSlotCount + f];
                channels.Add(new BasisAvatarChannel(b, curlBits, BasisChannelKind.Delta));
                channels.Add(new BasisAvatarChannel(b + curlBits, splayBits, BasisChannelKind.Delta));
            }

            // Rotation-region tail padding (VeryLow rounds up by 3 bits). Parked on the last finger
            // field so the partition stays total; it is always zero in practice and costs nothing.
            int rotPad = rotBytes * 8 - rotBits;
            if (rotPad > 0)
                channels.Add(new BasisAvatarChannel(rotBase + rotBits, rotPad, BasisChannelKind.Raw));

            // ── Fields 32..35: the tail ──
            int tailStart = posBytes + rotBytes;
            int scaleOff = tailStart;
            int bodyRotOff = scaleOff + BasisBoneRotationCompression.WriteScale;
            int hipsDeltaOff = bodyRotOff + BasisBoneRotationCompression.WriteRotation;
            int hipsRotOff = hipsDeltaOff + BasisBoneRotationCompression.WriteHipsDelta;

            // Scale: posit16. Monotone in value but non-uniformly spaced, and a scale change is a
            // rare one-off jump rather than a tracked motion — verbatim is both simpler and smaller.
            fieldFirst[FieldScale] = channels.Count;
            channels.Add(new BasisAvatarChannel(scaleOff * 8, 16, BasisChannelKind.Raw));

            fieldFirst[FieldBodyRot] = channels.Count;
            AddByteAlignedQuaternion(channels, bodyRotOff * 8);

            fieldFirst[FieldHipsDelta] = channels.Count;
            for (int a = 0; a < 3; a++)
                channels.Add(new BasisAvatarChannel(hipsDeltaOff * 8 + a * BasisAvatarBitPacking.HipsDeltaBits,
                                                   BasisAvatarBitPacking.HipsDeltaBits, BasisChannelKind.Delta));
            // The 40th bit of the 5-byte hips-delta field is spare and asserted zero; carry it so the
            // partition stays total.
            int hipsUsed = 3 * BasisAvatarBitPacking.HipsDeltaBits;
            if (hipsUsed < BasisBoneRotationCompression.WriteHipsDelta * 8)
                channels.Add(new BasisAvatarChannel(hipsDeltaOff * 8 + hipsUsed,
                                                   BasisBoneRotationCompression.WriteHipsDelta * 8 - hipsUsed,
                                                   BasisChannelKind.Raw));

            fieldFirst[FieldHipsRot] = channels.Count;
            AddByteAlignedQuaternion(channels, hipsRotOff * 8);

            // ── Field 36: end-effector block (High only) ──
            fieldFirst[FieldEndEffector] = channels.Count;
            if (effBytes > 0)
            {
                int eBase = (tailStart + BasisBoneRotationCompression.TailBytes) * 8;
                channels.Add(new BasisAvatarChannel(eBase, EffectorMaskBits, BasisChannelKind.Raw));
                for (int e = 0; e < EffectorCount; e++)
                {
                    int b = eBase + EffectorMaskBits + e * EffectorStride;
                    for (int a = 0; a < 3; a++)
                        channels.Add(new BasisAvatarChannel(b + a * EffectorPosBits, EffectorPosBits, BasisChannelKind.Delta));
                    int r = b + 3 * EffectorPosBits;
                    channels.Add(new BasisAvatarChannel(r, 2, BasisChannelKind.Raw));
                    for (int c = 0; c < 3; c++)
                        channels.Add(new BasisAvatarChannel(r + 2 + c * EffectorRotBpc, EffectorRotBpc, BasisChannelKind.Delta));
                }
            }

            fieldFirst[fieldCount] = channels.Count;
            return new BasisAvatarChannelLayout(channels.ToArray(), fieldFirst, payloadBytes);
        }

        /// <summary>
        /// The 7-byte compressed quaternion written by BasisUnityBitPackerExtensions: a whole byte of
        /// "largest component" index followed by three little-endian uint16 components. Byte-aligned,
        /// not bit-packed, unlike the bone block.
        /// </summary>
        private static void AddByteAlignedQuaternion(System.Collections.Generic.List<BasisAvatarChannel> channels, int bitOffset)
        {
            channels.Add(new BasisAvatarChannel(bitOffset, 8, BasisChannelKind.Raw));
            for (int c = 0; c < 3; c++)
                channels.Add(new BasisAvatarChannel(bitOffset + 8 + c * 16, 16, BasisChannelKind.Delta));
        }

        private const int FieldScale = 1 + BasisBoneRotationCompression.RotationFieldCount; // 32
        private const int FieldBodyRot = FieldScale + 1;                                    // 33
        private const int FieldHipsDelta = FieldScale + 2;                                  // 34
        private const int FieldHipsRot = FieldScale + 3;                                    // 35
        private const int FieldEndEffector = FieldScale + 4;                                // 36
    }
}
