using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine.UIElements;
using static BasisNetworkPrimitiveCompression;
using static SerializableBasis;
namespace Basis.Scripts.Networking.Compression
{
    public static class BasisUnityBitPackerExtensionsUnsafe
    {
        public const int FloatSize = sizeof(float);
        public const int UShortSize = sizeof(ushort);
        public const int Vector3Size = 3 * FloatSize;
        public const int QuaternionSize = 3 * FloatSize + UShortSize;
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
                *((float*)ptr) = float.IsNaN(q.value.x) ? 0f : q.value.x;
                *((float*)(ptr + 4)) = float.IsNaN(q.value.y) ? 0f : q.value.y;
                *((float*)(ptr + 8)) = float.IsNaN(q.value.z) ? 0f : q.value.z;
            }
            offset += 12;

            float w = float.IsNaN(q.value.w) ? 1f : q.value.w;
            ushort compressedW = compressor.Compress(w);
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
            float w = compressor.Decompress(compressedW);

            // Sanitize potential NaNs
            if (float.IsNaN(x)) x = 0f;
            if (float.IsNaN(y)) y = 0f;
            if (float.IsNaN(z)) z = 0f;
            if (float.IsNaN(w)) w = 1f;

            return new quaternion(x, y, z, w);
        }
        const int BITS = 20;
        const uint MAX = (1u << BITS) - 1;
        const float INV_SQRT2 = 0.7071067811865475244f; // 1/sqrt(2)

        static uint Quantize(float v)
        {
            // map [-INV_SQRT2, +INV_SQRT2] -> [0, MAX]
            float t = (v + INV_SQRT2) / (2f * INV_SQRT2);
            t = math.clamp(t, 0f, 1f);
            return (uint)math.round(t * MAX);
        }

        static float Dequantize(uint q)
        {
            float t = (float)q / MAX;
            return t * (2f * INV_SQRT2) - INV_SQRT2;
        }

        public static void WriteQuaternionToBytes(quaternion q, ref byte[] bytes, ref int offset)
        {
            float4 v = q.value;
            if (!math.all(math.isfinite(v)))
                v = new float4(0, 0, 0, 1);

            // find index of largest abs component
            float4 av = math.abs(v);
            int largest = 0;
            float max = av.x;
            if (av.y > max) { max = av.y; largest = 1; }
            if (av.z > max) { max = av.z; largest = 2; }
            if (av.w > max) { largest = 3; }

            // make largest component positive (removes sign bit)
            if (v[largest] < 0f) v = -v;

            // store the other three (a,b,c)
            float a, b, c;
            switch (largest)
            {
                case 0: a = v.y; b = v.z; c = v.w; break;
                case 1: a = v.x; b = v.z; c = v.w; break;
                case 2: a = v.x; b = v.y; c = v.w; break;
                default: a = v.x; b = v.y; c = v.z; break;
            }

            uint qa = Quantize(a);
            uint qb = Quantize(b);
            uint qc = Quantize(c);

            // pack into 64-bit:
            // [largest:2][qa:20][qb:20][qc:20] = 62 bits used
            ulong packed = (ulong)largest | ((ulong)qa << 2) | ((ulong)qb << (2 + BITS))| ((ulong)qc << (2 + 2 * BITS));

            WriteULongLE(packed, ref bytes, ref offset);
        }

        public static quaternion ReadQuaternionFromBytes(ref byte[] bytes, ref int offset)
        {
            ulong packed = ReadULongLE(ref bytes, ref offset);

            int largest = (int)(packed & 0x3UL);

            uint qa = (uint)((packed >> 2) & MAX);
            uint qb = (uint)((packed >> (2 + BITS)) & MAX);
            uint qc = (uint)((packed >> (2 + 2 * BITS)) & MAX);

            float a = Dequantize(qa);
            float b = Dequantize(qb);
            float c = Dequantize(qc);

            // reconstruct missing (largest) component
            float missingSq = 1f - (a * a + b * b + c * c);
            float missing = math.sqrt(math.max(0f, missingSq)); // clamp for numeric safety

            float4 v;
            switch (largest)
            {
                case 0: v = new float4(missing, a, b, c); break;
                case 1: v = new float4(a, missing, b, c); break;
                case 2: v = new float4(a, b, missing, c); break;
                default: v = new float4(a, b, c, missing); break;
            }

            // optional: normalize to kill tiny drift
            v = math.normalize(v);
            return new quaternion(v);
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
