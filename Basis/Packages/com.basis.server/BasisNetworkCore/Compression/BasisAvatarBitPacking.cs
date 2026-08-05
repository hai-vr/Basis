namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Avatar payload geometry (position / scale / rotation / hips tail sizes) and the quality
    /// ladder. The actual bone-rotation bitstream is produced by <see cref="BasisBoneRotationCompression"/>;
    /// the size helpers here delegate to it. (The legacy per-muscle quantization tables that used to
    /// live here were removed — the smallest-three quaternion codec superseded them.)
    /// </summary>
    public static class BasisAvatarBitPacking
    {
        /// <summary>
        /// Hips world position: 3 × signed int24 millimetres (±8388 m, 1 mm steps) at EVERY
        /// quality, High included.
        ///
        /// High used to keep 3 × float32. Nothing needed that precision — the position deadband
        /// is 2 mm, twice the step this gives — and float32 cost bandwidth twice over. It is
        /// 3 bytes wider, and, worse, the delta codec compares this field as raw bytes:
        /// sub-millimetre mantissa churn on a player who is standing still marked it dirty on
        /// every frame, so every delta carried 12 bytes of position it did not need to. At 1 mm
        /// steps a still player's position field goes quiet and drops out of the delta entirely.
        /// It also turns the server's per-tier repack of this field into a plain copy.
        /// </summary>
        public const int WritePosition = 9;
        private const int PositionMmLimit = (1 << 23) - 1;
        public const int WriteScale = 2;
        public const int WriteRotation = 7;
        /// <summary>
        /// Hips local-position delta vs TPose, sent so seated/IK-driven hips poses reach remotes.
        /// 3 × signed 13-bit at ±<see cref="HipsDeltaRange"/> packed into 5 bytes (the spare bit
        /// stays zero). Two's complement, so an all-zero field still decodes to a zero delta —
        /// the same property the previous 3 × int16 form had, and which the console test client
        /// relies on to leave the field unwritten.
        /// </summary>
        public const int WriteHipsDelta = 5;
        // Hips local-rotation delta vs TPose. Hips is excluded from the bone
        // packet (BONE_WRITE_ORDER), so without this slot the remote hips would
        // sit at calibration rotation forever. 7 bytes = same smallest-three
        // encoding used for the root rotation.
        public const int WriteHipsRotation = 7;
        /// <summary>
        /// Per-axis ±1 m envelope. At 13 bits that is ≈0.24 mm/axis — six times finer than the
        /// 1.5 mm hips-delta deadband that already decides what is worth sending, and large
        /// enough to cover squat/seated overrides without clipping. (The previous int16 form
        /// spent 30 µm/axis, fifty times finer than the deadband it was measured against.)
        /// </summary>
        public const float HipsDeltaRange = 1f;

        public const int HipsDeltaBits = 13;
        private const int HipsDeltaMaxQ = (1 << (HipsDeltaBits - 1)) - 1;   // 4095
        private const uint HipsDeltaMask = (1u << HipsDeltaBits) - 1u;      // 0x1FFF

        public const int TailBytes = WriteScale + WriteRotation + WriteHipsDelta + WriteHipsRotation; // 21

        // Expanded ladder (anchors preserved: Low/Medium/High)
        public enum BitQuality : byte
        {
            VeryLow = 0,
            Low = 1,
            Medium = 2,
            High = 3,
        }
        public static bool IsValidQuality(BitQuality q) => q == BitQuality.VeryLow || q == BitQuality.Low || q == BitQuality.Medium || q == BitQuality.High;

        /// <summary>
        /// Returns the byte count for the bone rotation bitstream at the given quality.
        /// Named MuscleBytes for backward compatibility with server code.
        /// </summary>
        public static int MuscleBytes(BitQuality q) => BasisBoneRotationCompression.RotationBytes(q);

        /// <summary>Position field size. Uniform across the ladder now, but kept as a per-quality
        /// query so it reads like RotationBytes/EndEffectorBytes at the call sites.</summary>
        public static int PositionBytes(BitQuality q) => WritePosition;

        public static int ConvertToSize(BitQuality q)
        {
            // Position (9) + BoneRotations (variable) + Posit16 Scale (2) + Rotation (7) + hips tail.
            return BasisBoneRotationCompression.ConvertToSize(q);
        }

        /// <summary>Encodes one world-space axis value (metres) as signed int24 millimetres, little-endian.</summary>
        public static void EncodeAxisMm(float meters, byte[] dst, int offset)
        {
            float mmF = meters * 1000f;
            int mm = float.IsNaN(mmF) ? 0
                : mmF >= PositionMmLimit ? PositionMmLimit
                : mmF <= -PositionMmLimit ? -PositionMmLimit
                : (int)System.Math.Round(mmF);
            dst[offset] = (byte)mm;
            dst[offset + 1] = (byte)(mm >> 8);
            dst[offset + 2] = (byte)(mm >> 16);
        }

        /// <summary>Decodes one signed int24 millimetre axis back to metres.</summary>
        public static float DecodeAxisMm(byte[] src, int offset)
        {
            int mm = src[offset] | (src[offset + 1] << 8) | (src[offset + 2] << 16);
            // Sign-extend from 24 bits.
            mm = (mm << 8) >> 8;
            return mm * 0.001f;
        }

        /// <summary>Writes the whole 3-axis position block (metres) as int24 millimetres.</summary>
        public static void EncodePosition(float x, float y, float z, byte[] dst, int offset)
        {
            EncodeAxisMm(x, dst, offset);
            EncodeAxisMm(y, dst, offset + 3);
            EncodeAxisMm(z, dst, offset + 6);
        }

        /// <summary>Reads the whole 3-axis position block back into metres.</summary>
        public static void DecodePosition(byte[] src, int offset, out float x, out float y, out float z)
        {
            x = DecodeAxisMm(src, offset);
            y = DecodeAxisMm(src, offset + 3);
            z = DecodeAxisMm(src, offset + 6);
        }

        /// <summary>
        /// Packs a hips local-position delta (metres) into <see cref="WriteHipsDelta"/> bytes as
        /// three signed 13-bit axes. Overwrites the whole field, so callers need not pre-clear it.
        /// </summary>
        public static void EncodeHipsDelta(float x, float y, float z, byte[] dst, int offset)
        {
            ulong packed = QuantizeHipsAxis(x)
                | ((ulong)QuantizeHipsAxis(y) << HipsDeltaBits)
                | ((ulong)QuantizeHipsAxis(z) << (2 * HipsDeltaBits));
            dst[offset] = (byte)packed;
            dst[offset + 1] = (byte)(packed >> 8);
            dst[offset + 2] = (byte)(packed >> 16);
            dst[offset + 3] = (byte)(packed >> 24);
            dst[offset + 4] = (byte)(packed >> 32);
        }

        /// <summary>Unpacks the three signed 13-bit axes written by <see cref="EncodeHipsDelta"/>.</summary>
        public static void DecodeHipsDelta(byte[] src, int offset, out float x, out float y, out float z)
        {
            ulong packed = src[offset]
                | ((ulong)src[offset + 1] << 8)
                | ((ulong)src[offset + 2] << 16)
                | ((ulong)src[offset + 3] << 24)
                | ((ulong)src[offset + 4] << 32);
            x = DequantizeHipsAxis((uint)(packed & HipsDeltaMask));
            y = DequantizeHipsAxis((uint)((packed >> HipsDeltaBits) & HipsDeltaMask));
            z = DequantizeHipsAxis((uint)((packed >> (2 * HipsDeltaBits)) & HipsDeltaMask));
        }

        private static uint QuantizeHipsAxis(float meters)
        {
            float scaled = meters * (HipsDeltaMaxQ / HipsDeltaRange);
            int q = float.IsNaN(scaled) ? 0
                : scaled >= HipsDeltaMaxQ ? HipsDeltaMaxQ
                : scaled <= -HipsDeltaMaxQ ? -HipsDeltaMaxQ
                : (int)System.Math.Round(scaled);
            return (uint)q & HipsDeltaMask;
        }

        private static float DequantizeHipsAxis(uint q)
        {
            // Sign-extend from 13 bits.
            int s = (int)(q << (32 - HipsDeltaBits)) >> (32 - HipsDeltaBits);
            return s * (HipsDeltaRange / HipsDeltaMaxQ);
        }
    }
}
