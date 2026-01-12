using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Basis.Scripts.Networking.Compression
{
    public static class BasisUnityBitPackerExtensionsUnsafe
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool EnsureSpace(byte[] bytes, int offset, int size)
        {
            return (uint)offset <= (uint)bytes.Length && offset + size <= bytes.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(float v) => math.isfinite(v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite3(float a, float b, float c) =>
            IsFinite(a) & IsFinite(b) & IsFinite(c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite4(float a, float b, float c, float d) =>
            IsFinite(a) & IsFinite(b) & IsFinite(c) & IsFinite(d);

        /// <summary>
        /// Optional stricter validation: quaternions should be close-ish to unit length.
        /// Networking compression often expects normalized rotations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsReasonableQuaternion(float x, float y, float z, float w, float tolerance)
        {
            // length^2 should be near 1
            float lenSq = x * x + y * y + z * z + w * w;
            // Reject zeros / nonsense
            if (!(lenSq > 0f) || !IsFinite(lenSq)) return false;

            // Accept within tolerance (e.g. 0.01..0.05 depending on how noisy your source can be)
            return math.abs(lenSq - 1f) <= tolerance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadUShort(ref byte[] bytes, ref int offset, out ushort value)
        {
            value = default;
            if (!EnsureSpace(bytes, offset, 2)) return false;

            value = (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
            offset += 2;
            return true;
        }


        public unsafe static bool TryReadQuaternionFromBytes(
            ref byte[] bytes,
            ref int offset,
            out quaternion q,
            float unitLengthTolerance = 0.02f,
            bool requireUnitLength = true)
        {
            q = default;
            if (!EnsureSpace(bytes, offset, 16)) return false;

            float x, y, z, w;
            fixed (byte* ptr = &bytes[offset])
            {
                float* f = (float*)ptr;
                x = f[0];
                y = f[1];
                z = f[2];
                w = f[3];
            }

            // Validate without repairing
            if (!IsFinite4(x, y, z, w))
            {
                return false;
            }

            if (requireUnitLength && !IsReasonableQuaternion(x, y, z, w, unitLengthTolerance))
            {
                return false;
            }

            offset += 16;
            q = new quaternion(x, y, z, w);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadPosition(ref byte[] buffer, ref int offset, out Unity.Mathematics.float3 position)
        {
            position = default;
            if (!EnsureSpace(buffer, offset, 12)) return false;

            float x, y, z;
            unsafe
            {
                fixed (byte* src = &buffer[offset])
                {
                    float* fSrc = (float*)src;
                    x = fSrc[0];
                    y = fSrc[1];
                    z = fSrc[2];
                }
            }

            if (!IsFinite3(x, y, z)) return false;

            offset += 12;
            position = new Unity.Mathematics.float3(x, y, z);
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Sanitize(float v, float fallback)
        {
            // math.isfinite catches both NaN and ±Infinity.
            return math.isfinite(v) ? v : fallback;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUShort(ushort value, ref byte[] bytes, ref int offset)
        {
            EnsureSpace(bytes, offset, 2); bytes[offset++] = (byte)value; bytes[offset++] = (byte)(value >> 8);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadUShort(ref byte[] bytes, ref int offset)
        {
            EnsureSpace(bytes, offset, 2);
            ushort result = (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
            offset += 2;
            return result;
        }
        public unsafe static void WriteQuaternionToBytes(quaternion q, ref byte[] bytes, ref int offset)
        {
            EnsureSpace(bytes, offset, 16); fixed (byte* ptr = &bytes[offset])
            {
                float* f = (float*)ptr; f[0] = Sanitize(q.value.x, 0f);
                f[1] = Sanitize(q.value.y, 0f);
                f[2] = Sanitize(q.value.z, 0f);
                f[3] = Sanitize(q.value.w, 1f);
            }
            offset += 16;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WritePosition(UnityEngine.Vector3 position, ref byte[] buffer, ref int offset)
        {
            EnsureSpace(buffer, offset, 12);
            unsafe
            {
                fixed (byte* dst = &buffer[offset])
                {
                    float* fDst = (float*)dst;
                    fDst[0] = Sanitize(position.x, 0f);
                    fDst[1] = Sanitize(position.y, 0f);
                    fDst[2] = Sanitize(position.z, 0f);
                }
            }
            offset += 12;
        }
    }
}
