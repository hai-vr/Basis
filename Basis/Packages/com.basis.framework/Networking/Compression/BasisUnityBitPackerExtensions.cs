using System;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;
using Unity.Mathematics;
using static BasisNetworkPrimitiveCompression;
using static SerializableBasis;
using Basis.Network.Core.Compression;
namespace Basis.Scripts.Networking.Compression
{
    public static class BasisUnityBitPackerExtensionsUnsafe
    {
        private static readonly BasisSimpleObjectPool<byte[]> byteArrayPool = new(() => new byte[BasisBitPackingConstants.LengthUshortBytes]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUShort(ushort value, ref byte[] bytes, ref int offset)
        {
            bytes[offset++] = (byte)value;
            bytes[offset++] = (byte)(value >> 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadUShort(ref byte[] bytes, ref int offset)
        {
            ushort result = (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
            offset += 2;
            return result;
        }

        public unsafe static void WriteQuaternionToBytes(quaternion q, ref byte[] bytes, ref int offset, BasisRangedUshortFloatData compressor)
        {
            fixed (byte* ptr = &bytes[offset])
            {
                *((float*)ptr) = q.value.x;
                *((float*)(ptr + 4)) = q.value.y;
                *((float*)(ptr + 8)) = q.value.z;
            }
            offset += 12;
            ushort compressedW = compressor.Compress(q.value.w);
            WriteUShort(compressedW, ref bytes, ref offset);
        }

        public unsafe static quaternion ReadQuaternionFromBytes(ref byte[] bytes, BasisRangedUshortFloatData compressor, ref int offset)
        {
            float x, y, z;
            fixed (byte* ptr = &bytes[offset])
            {
                x = *((float*)ptr);
                y = *((float*)(ptr + 4));
                z = *((float*)(ptr + 8));
            }
            offset += 12;
            ushort compressedW = ReadUShort(ref bytes, ref offset);
            return new quaternion(x, y, z, compressor.Decompress(compressedW));
        }

        public unsafe static void WriteUShortsToBytes(ushort[] values, ref byte[] bytes, ref int offset)
        {
            fixed (byte* dst = &bytes[offset])
            fixed (ushort* src = values)
            {
                Buffer.MemoryCopy(src, dst, BasisBitPackingConstants.LengthUshortBytes, BasisBitPackingConstants.LengthUshortBytes);
            }
            offset += BasisBitPackingConstants.LengthUshortBytes;
        }

        public unsafe static void ReadMusclesFromBytes(ref byte[] bytes, ref ushort[] muscles, ref int offset)
        {
            if (muscles == null || muscles.Length != LocalAvatarSyncMessage.StoredBones)
            {
                muscles = new ushort[LocalAvatarSyncMessage.StoredBones];
            }

            fixed (byte* src = &bytes[offset])
            fixed (ushort* dst = muscles)
            {
                Buffer.MemoryCopy(src, dst, BasisBitPackingConstants.LengthUshortBytes, BasisBitPackingConstants.LengthUshortBytes);
            }
            offset += BasisBitPackingConstants.LengthUshortBytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CompressPosition(UnityEngine.Vector3 position, ref byte[] buffer, ref int offset)
        {
            CompressAxis(position.x, buffer, offset);
            CompressAxis(position.y, buffer, offset + 3);
            CompressAxis(position.z, buffer, offset + 6);
            offset += 9; // Actually 3 axes * 3 bytes each
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UnityEngine.Vector3 DecompressPosition(ref byte[] buffer, ref int offset)
        {
            float x = DecompressAxis(buffer, offset);
            float y = DecompressAxis(buffer, offset + 3);
            float z = DecompressAxis(buffer, offset + 6);
            offset += 9;
            return new UnityEngine.Vector3(x, y, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DecompressAxis(byte[] buffer, int offset)
        {
            int value = (buffer[offset] << 16) | (buffer[offset + 1] << 8) | buffer[offset + 2];
            value = (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
            return value * BasisBitPackingConstants.Precision;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CompressAxis(float value, byte[] buffer, int offset)
        {
            int scaled = (int)(value * BasisBitPackingConstants.Scale + 0.5f);
            scaled = scaled > 8388607 ? 8388607 : (scaled < -8388608 ? -8388608 : scaled);

            buffer[offset] = (byte)((scaled >> 16) & 0xFF);
            buffer[offset + 1] = (byte)((scaled >> 8) & 0xFF);
            buffer[offset + 2] = (byte)(scaled & 0xFF);
        }
    }
}
