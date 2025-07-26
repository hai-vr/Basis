using Basis.Scripts.Networking.Compression;
using System;
using System.Runtime.CompilerServices;
using static BasisNetworkPrimitiveCompression;
using static SerializableBasis;
namespace Basis.Network.Core.Compression
{
    public static class BasisNetworkCompressionExtensions
    {
        public static BasisRangedUshortFloatData BasisRangedUshortFloatData = new BasisRangedUshortFloatData(-1f, 1f, 0.001f);
        public static int LengthSize = 90;
        public static int LengthBytes = LengthSize * 4; // Initialize LengthBytes first
        public static byte[] StoredBytes = new byte[LengthBytes];
        /// <summary>
        /// Single API to handle all avatar decompression tasks.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 DecompressAndProcessAvatarFaster(LocalAvatarSyncMessage syncMessage)
        {
            var buffer = syncMessage.array;

            EnsureReadable(buffer, BasisBitPackingConstants.PositionDelta);
            float x = DecompressAxis(ref buffer, 0);
            float y = DecompressAxis(ref buffer, 3);
            float z = DecompressAxis(ref buffer, 6);
            return new Vector3(x, y, z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DecompressAxis(ref byte[] buffer, int offset)
        {
            int value = (buffer[offset] << 16) | (buffer[offset + 1] << 8) | buffer[offset + 2];
            value = (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
            return value * BasisBitPackingConstants.Precision;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureReadable(byte[] bytes, int requiredSize)
        {
            if (bytes == null || bytes.Length < requiredSize)
                throw new ArgumentException($"Byte array too small. Required: {requiredSize}, Got: {bytes?.Length ?? 0}");
        }
        // Ensure the byte array is large enough to hold the data
        public static void EnsureWritable(ref byte[] bytes, int requiredSize)
        {
            if (bytes == null || bytes.Length < requiredSize)
            {
                Array.Resize(ref bytes, requiredSize);
            }
        }
    }
}
