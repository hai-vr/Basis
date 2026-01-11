using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using static BasisNetworkPrimitiveCompression;
namespace Basis.Scripts.Networking.Compression
{
    public static class BasisUnityBitPackerExtensionsUnsafe
    {
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
        public unsafe static void WriteQuaternionToBytes(quaternion q,ref byte[] bytes,ref int offset)
        {
            fixed (byte* ptr = &bytes[offset])
            {
                float* f = (float*)ptr;
                f[0] = float.IsNaN(q.value.x) ? 0f : q.value.x;
                f[1] = float.IsNaN(q.value.y) ? 0f : q.value.y;
                f[2] = float.IsNaN(q.value.z) ? 0f : q.value.z;
                f[3] = float.IsNaN(q.value.w) ? 1f : q.value.w;
            }
            offset += 16;
        }

        public unsafe static quaternion ReadQuaternionFromBytes(ref byte[] bytes, ref int offset)
        {
            float x, y, z, w;
            fixed (byte* ptr = &bytes[offset])
            {
                x = *((float*)ptr);
                y = *((float*)(ptr + 4));
                z = *((float*)(ptr + 8));
                w = *((float*)(ptr + 12));
            }
            offset += 16;

            if (float.IsNaN(x)) x = 0f;
            if (float.IsNaN(y)) y = 0f;
            if (float.IsNaN(z)) z = 0f;
            if (float.IsNaN(w)) w = 1f;

            return new quaternion(x, y, z, w);
        }
        static void WriteULongLE(ulong v, ref byte[] bytes, ref int offset)
        {
            // little-endian
            bytes[offset++] = (byte)(v);
            bytes[offset++] = (byte)(v >> 8);
            bytes[offset++] = (byte)(v >> 16);
            bytes[offset++] = (byte)(v >> 24);
            bytes[offset++] = (byte)(v >> 32);
            bytes[offset++] = (byte)(v >> 40);
            bytes[offset++] = (byte)(v >> 48);
            bytes[offset++] = (byte)(v >> 56);
        }

        static ulong ReadULongLE(ref byte[] bytes, ref int offset)
        {
            ulong v =
                (ulong)bytes[offset] |
                ((ulong)bytes[offset + 1] << 8) |
                ((ulong)bytes[offset + 2] << 16) |
                ((ulong)bytes[offset + 3] << 24) |
                ((ulong)bytes[offset + 4] << 32) |
                ((ulong)bytes[offset + 5] << 40) |
                ((ulong)bytes[offset + 6] << 48) |
                ((ulong)bytes[offset + 7] << 56);

            offset += 8;
            return v;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WritePosition(UnityEngine.Vector3 position, ref byte[] buffer, ref int offset)
        {
            unsafe
            {
                fixed (byte* dst = &buffer[offset])
                {
                    float* fDst = (float*)dst;
                    fDst[0] = position.x;
                    fDst[1] = position.y;
                    fDst[2] = position.z;
                }
            }

            offset += 12;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UnityEngine.Vector3 ReadPosition(ref byte[] buffer, ref int offset)
        {
            UnityEngine.Vector3 result;
            unsafe
            {
                fixed (byte* src = &buffer[offset])
                {
                    float* fSrc = (float*)src;
                    result.x = float.IsNaN(fSrc[0]) ? 0f : fSrc[0];
                    result.y = float.IsNaN(fSrc[1]) ? 0f : fSrc[1];
                    result.z = float.IsNaN(fSrc[2]) ? 0f : fSrc[2];
                }
            }

            offset += 12;
            return result;
        }
    }
}
