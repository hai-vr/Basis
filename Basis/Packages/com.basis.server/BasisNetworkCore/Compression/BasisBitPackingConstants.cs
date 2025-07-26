using static SerializableBasis;
namespace Basis.Network.Core.Compression
{
    public static class BasisBitPackingConstants
    {
        public const int FloatSize = sizeof(float);
        public const int UShortSize = sizeof(ushort);
        public const int Vector3Size = 3 * FloatSize;
        public const int QuaternionSize = 3 * FloatSize + UShortSize;
        public const float Precision = 1f / 16f;
        public const int Scale = 16;
        public const int PositionDelta = 6;
        public static readonly int LengthUshortBytes = LocalAvatarSyncMessage.StoredBones * UShortSize;
    }
}
