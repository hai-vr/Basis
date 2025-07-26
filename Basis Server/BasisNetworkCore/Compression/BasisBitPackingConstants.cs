using static SerializableBasis;
namespace Basis.Network.Core.Compression
{
    public static class BasisBitPackingConstants
    {
        public const int FloatSize = sizeof(float);
        public const int UShortSize = sizeof(ushort);
        public const int Vector3Size = 3 * FloatSize;
        public const int QuaternionSize = 3 * FloatSize + UShortSize;
        public const float Precision = 1f / 128f; // = 0.0078125
        public const int Scale = 128;             // Multiply value by this when encoding
        public const int PositionDelta = 9;       // 3 axes × 3 bytes
        public static readonly int LengthUshortBytes = LocalAvatarSyncMessage.StoredBones * UShortSize;
    }
}
