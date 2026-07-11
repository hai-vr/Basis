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
        public const int WritePosition = 12;
        public const int WriteScale = 2;
        public const int WriteRotation = 7;
        // Hips local-position delta vs TPose, sent so seated/IK-driven hips poses
        // reach remotes (3 ushorts at fixed range, see HipsDeltaRange below).
        public const int WriteHipsDelta = 6;
        // Hips local-rotation delta vs TPose. Hips is excluded from the bone
        // packet (BONE_WRITE_ORDER), so without this slot the remote hips would
        // sit at calibration rotation forever. 7 bytes = same smallest-three
        // encoding used for the root rotation.
        public const int WriteHipsRotation = 7;
        // Per-axis ±1m envelope. With ushort precision this is ≈30 µm/axis,
        // far below visual jitter and large enough to cover squat/seated
        // overrides without clipping.
        public const float HipsDeltaRange = 1f;

        public const int TailBytes = WriteScale + WriteRotation + WriteHipsDelta + WriteHipsRotation; // 22

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

        public static int ConvertToSize(BitQuality q)
        {
            // Position (12) + BoneRotations (variable) + Posit16 Scale (2) + Rotation (7) + hips tail.
            return BasisBoneRotationCompression.ConvertToSize(q);
        }
    }
}
