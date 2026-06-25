using Basis.Scripts.Networking.Compression;
using Unity.Mathematics;

namespace Basis.Scripts.Networking.Sync
{
    /// <summary>
    /// Wire (de)serialization for a <see cref="BasisSyncSchema"/>. Supports full keyframes (every
    /// field) and dirty-mask deltas (changed fields only). Rotations use Basis smallest-three
    /// quaternion packing; positions/scale/floats are full float32 or optionally quantized.
    /// </summary>
    public static class BasisSyncCodec
    {
        public const byte FlagKeyframe = 1 << 0;
        public const int HeaderSize = 4; // seq(1) + flags(1) + intervalMs(2)

        // Quantization ranges (used only when a field opts into Quantize).
        private const float PosMin = -1024f, PosMax = 1024f, PosSpan = PosMax - PosMin;
        private const float ScaleMin = 0f, ScaleMax = 64f, ScaleSpan = ScaleMax - ScaleMin;

        public static int MaxSerializedSize(BasisSyncSchema schema)
        {
            int size = HeaderSize + schema.DirtyMaskBytes;
            for (int i = 0; i < schema.FieldCount; i++)
            {
                size += MaxFieldSize(schema.GetField(i));
            }
            return size;
        }

        private static int MaxFieldSize(in BasisSyncField f)
        {
            switch (f.Type)
            {
                case BasisSyncFieldType.Position:
                case BasisSyncFieldType.Scale: return f.Quantize ? 6 : 12;
                case BasisSyncFieldType.Float:
                case BasisSyncFieldType.Angle: return f.Quantize ? 2 : 4;
                case BasisSyncFieldType.Vector2: return f.Quantize ? 4 : 8;
                case BasisSyncFieldType.Vector4: return f.Quantize ? 8 : 16;
                case BasisSyncFieldType.Color: return f.Quantize ? 4 : 16;
                case BasisSyncFieldType.Rotation: return 4;
                case BasisSyncFieldType.Int:
                case BasisSyncFieldType.UInt: return 5;
                case BasisSyncFieldType.UShort: return 2;
                case BasisSyncFieldType.Bool:
                case BasisSyncFieldType.Byte: return 1;
                default: return 0;
            }
        }

        public static int Serialize(BasisSyncSchema schema, BasisSyncValues values, bool keyframe, byte[] dirtyMask, byte seq, ushort intervalMs, byte[] outBuf)
        {
            int o = 0;
            outBuf[o++] = seq;
            outBuf[o++] = keyframe ? FlagKeyframe : (byte)0;
            WriteUShort(outBuf, ref o, intervalMs);

            if (!keyframe)
            {
                for (int i = 0; i < schema.DirtyMaskBytes; i++) outBuf[o++] = dirtyMask[i];
            }

            int n = schema.FieldCount;
            for (int fi = 0; fi < n; fi++)
            {
                if (!keyframe && (dirtyMask[fi >> 3] & (1 << (fi & 7))) == 0) continue;
                EncodeField(schema.GetField(fi), values, outBuf, ref o);
            }
            return o;
        }

        public static bool Deserialize(BasisSyncSchema schema, byte[] buf, int length, BasisSyncValues baseline, out byte seq, out bool keyframe, out ushort intervalMs)
        {
            seq = 0;
            keyframe = false;
            intervalMs = 0;
            if (buf == null || length < HeaderSize) return false;

            int o = 0;
            seq = buf[o++];
            byte flags = buf[o++];
            keyframe = (flags & FlagKeyframe) != 0;
            intervalMs = ReadUShort(buf, ref o);

            if (keyframe)
            {
                for (int fi = 0; fi < schema.FieldCount; fi++)
                {
                    DecodeField(schema.GetField(fi), baseline, buf, ref o);
                }
            }
            else
            {
                int maskStart = o;
                o += schema.DirtyMaskBytes;
                if (o > length) return false;
                for (int fi = 0; fi < schema.FieldCount; fi++)
                {
                    if ((buf[maskStart + (fi >> 3)] & (1 << (fi & 7))) != 0)
                    {
                        DecodeField(schema.GetField(fi), baseline, buf, ref o);
                    }
                }
            }
            return true;
        }

        private static void EncodeField(in BasisSyncField f, BasisSyncValues v, byte[] b, ref int o)
        {
            switch (f.Type)
            {
                case BasisSyncFieldType.Position:
                {
                    int i = f.Offset;
                    if (f.Quantize)
                    {
                        WriteQuant(b, ref o, v.Cont[i], PosMin, PosSpan);
                        WriteQuant(b, ref o, v.Cont[i + 1], PosMin, PosSpan);
                        WriteQuant(b, ref o, v.Cont[i + 2], PosMin, PosSpan);
                    }
                    else
                    {
                        WriteFloat(b, ref o, v.Cont[i]);
                        WriteFloat(b, ref o, v.Cont[i + 1]);
                        WriteFloat(b, ref o, v.Cont[i + 2]);
                    }
                    break;
                }
                case BasisSyncFieldType.Scale:
                {
                    int i = f.Offset;
                    if (f.Quantize)
                    {
                        WriteQuant(b, ref o, v.Cont[i], ScaleMin, ScaleSpan);
                        WriteQuant(b, ref o, v.Cont[i + 1], ScaleMin, ScaleSpan);
                        WriteQuant(b, ref o, v.Cont[i + 2], ScaleMin, ScaleSpan);
                    }
                    else
                    {
                        WriteFloat(b, ref o, v.Cont[i]);
                        WriteFloat(b, ref o, v.Cont[i + 1]);
                        WriteFloat(b, ref o, v.Cont[i + 2]);
                    }
                    break;
                }
                case BasisSyncFieldType.Float:
                case BasisSyncFieldType.Angle:
                {
                    float fv = v.Cont[f.Offset];
                    if (f.Quantize) WriteUShort(b, ref o, (ushort)math.f32tof16(fv));
                    else WriteFloat(b, ref o, fv);
                    break;
                }
                case BasisSyncFieldType.Rotation:
                {
                    quaternion q = v.Rot[f.Offset];
                    UnityEngine.Quaternion uq = new UnityEngine.Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
                    uint packed = BasisCompression.QuaternionCompressor.CompressQuaternion(ref uq);
                    WriteUInt(b, ref o, packed);
                    break;
                }
                case BasisSyncFieldType.Int:
                    WriteVarInt(b, ref o, v.Disc[f.Offset]);
                    break;
                case BasisSyncFieldType.UShort:
                    WriteUShort(b, ref o, (ushort)v.Disc[f.Offset]);
                    break;
                case BasisSyncFieldType.Vector2:
                {
                    int i = f.Offset;
                    if (f.Quantize) { WriteHalf(b, ref o, v.Cont[i]); WriteHalf(b, ref o, v.Cont[i + 1]); }
                    else { WriteFloat(b, ref o, v.Cont[i]); WriteFloat(b, ref o, v.Cont[i + 1]); }
                    break;
                }
                case BasisSyncFieldType.Vector4:
                {
                    int i = f.Offset;
                    for (int c = 0; c < 4; c++)
                    {
                        if (f.Quantize) WriteHalf(b, ref o, v.Cont[i + c]);
                        else WriteFloat(b, ref o, v.Cont[i + c]);
                    }
                    break;
                }
                case BasisSyncFieldType.Color:
                {
                    int i = f.Offset;
                    for (int c = 0; c < 4; c++)
                    {
                        if (f.Quantize) WriteUnitByte(b, ref o, v.Cont[i + c]);
                        else WriteFloat(b, ref o, v.Cont[i + c]);
                    }
                    break;
                }
                case BasisSyncFieldType.Bool:
                    b[o++] = (byte)(v.Disc[f.Offset] != 0 ? 1 : 0);
                    break;
                case BasisSyncFieldType.Byte:
                    b[o++] = (byte)v.Disc[f.Offset];
                    break;
                case BasisSyncFieldType.UInt:
                    WriteVarUInt(b, ref o, (uint)v.Disc[f.Offset]);
                    break;
            }
        }

        private static void DecodeField(in BasisSyncField f, BasisSyncValues v, byte[] b, ref int o)
        {
            switch (f.Type)
            {
                case BasisSyncFieldType.Position:
                {
                    int i = f.Offset;
                    if (f.Quantize)
                    {
                        v.Cont[i] = ReadQuant(b, ref o, PosMin, PosSpan);
                        v.Cont[i + 1] = ReadQuant(b, ref o, PosMin, PosSpan);
                        v.Cont[i + 2] = ReadQuant(b, ref o, PosMin, PosSpan);
                    }
                    else
                    {
                        v.Cont[i] = ReadFloat(b, ref o);
                        v.Cont[i + 1] = ReadFloat(b, ref o);
                        v.Cont[i + 2] = ReadFloat(b, ref o);
                    }
                    break;
                }
                case BasisSyncFieldType.Scale:
                {
                    int i = f.Offset;
                    if (f.Quantize)
                    {
                        v.Cont[i] = ReadQuant(b, ref o, ScaleMin, ScaleSpan);
                        v.Cont[i + 1] = ReadQuant(b, ref o, ScaleMin, ScaleSpan);
                        v.Cont[i + 2] = ReadQuant(b, ref o, ScaleMin, ScaleSpan);
                    }
                    else
                    {
                        v.Cont[i] = ReadFloat(b, ref o);
                        v.Cont[i + 1] = ReadFloat(b, ref o);
                        v.Cont[i + 2] = ReadFloat(b, ref o);
                    }
                    break;
                }
                case BasisSyncFieldType.Float:
                case BasisSyncFieldType.Angle:
                    v.Cont[f.Offset] = f.Quantize ? math.f16tof32(ReadUShort(b, ref o)) : ReadFloat(b, ref o);
                    break;
                case BasisSyncFieldType.Rotation:
                {
                    uint packed = ReadUInt(b, ref o);
                    UnityEngine.Quaternion uq = BasisCompression.QuaternionCompressor.DecompressQuaternion(packed);
                    v.Rot[f.Offset] = new quaternion(uq.x, uq.y, uq.z, uq.w);
                    break;
                }
                case BasisSyncFieldType.Int:
                    v.Disc[f.Offset] = ReadVarInt(b, ref o);
                    break;
                case BasisSyncFieldType.UShort:
                    v.Disc[f.Offset] = ReadUShort(b, ref o);
                    break;
                case BasisSyncFieldType.Vector2:
                {
                    int i = f.Offset;
                    if (f.Quantize) { v.Cont[i] = ReadHalf(b, ref o); v.Cont[i + 1] = ReadHalf(b, ref o); }
                    else { v.Cont[i] = ReadFloat(b, ref o); v.Cont[i + 1] = ReadFloat(b, ref o); }
                    break;
                }
                case BasisSyncFieldType.Vector4:
                {
                    int i = f.Offset;
                    for (int c = 0; c < 4; c++)
                        v.Cont[i + c] = f.Quantize ? ReadHalf(b, ref o) : ReadFloat(b, ref o);
                    break;
                }
                case BasisSyncFieldType.Color:
                {
                    int i = f.Offset;
                    for (int c = 0; c < 4; c++)
                        v.Cont[i + c] = f.Quantize ? ReadUnitByte(b, ref o) : ReadFloat(b, ref o);
                    break;
                }
                case BasisSyncFieldType.Bool:
                    v.Disc[f.Offset] = b[o++] != 0 ? 1 : 0;
                    break;
                case BasisSyncFieldType.Byte:
                    v.Disc[f.Offset] = b[o++];
                    break;
                case BasisSyncFieldType.UInt:
                    v.Disc[f.Offset] = (int)ReadVarUInt(b, ref o);
                    break;
            }
        }

        private static void WriteQuant(byte[] b, ref int o, float value, float min, float span)
        {
            float t = (value - min) / span;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            WriteUShort(b, ref o, (ushort)(t * 65535f + 0.5f));
        }

        private static float ReadQuant(byte[] b, ref int o, float min, float span)
        {
            ushort q = ReadUShort(b, ref o);
            return min + (q / 65535f) * span;
        }

        private static unsafe void WriteFloat(byte[] b, ref int o, float v)
        {
            uint u = *(uint*)&v;
            WriteUInt(b, ref o, u);
        }

        private static unsafe float ReadFloat(byte[] b, ref int o)
        {
            uint u = ReadUInt(b, ref o);
            return *(float*)&u;
        }

        private static void WriteUInt(byte[] b, ref int o, uint v)
        {
            b[o++] = (byte)v;
            b[o++] = (byte)(v >> 8);
            b[o++] = (byte)(v >> 16);
            b[o++] = (byte)(v >> 24);
        }

        private static uint ReadUInt(byte[] b, ref int o)
        {
            uint v = (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
            o += 4;
            return v;
        }

        private static void WriteUShort(byte[] b, ref int o, ushort v)
        {
            b[o++] = (byte)v;
            b[o++] = (byte)(v >> 8);
        }

        private static ushort ReadUShort(byte[] b, ref int o)
        {
            ushort v = (ushort)(b[o] | (b[o + 1] << 8));
            o += 2;
            return v;
        }

        private static void WriteHalf(byte[] b, ref int o, float v)
        {
            WriteUShort(b, ref o, (ushort)math.f32tof16(v));
        }

        private static float ReadHalf(byte[] b, ref int o)
        {
            return math.f16tof32(ReadUShort(b, ref o));
        }

        private static void WriteUnitByte(byte[] b, ref int o, float v)
        {
            float c = v < 0f ? 0f : (v > 1f ? 1f : v);
            b[o++] = (byte)(c * 255f + 0.5f);
        }

        private static float ReadUnitByte(byte[] b, ref int o)
        {
            return b[o++] / 255f;
        }

        private static void WriteVarInt(byte[] b, ref int o, int value)
        {
            WriteVarUInt(b, ref o, (uint)((value << 1) ^ (value >> 31)));
        }

        private static int ReadVarInt(byte[] b, ref int o)
        {
            uint zig = ReadVarUInt(b, ref o);
            return (int)(zig >> 1) ^ -(int)(zig & 1);
        }

        private static void WriteVarUInt(byte[] b, ref int o, uint v)
        {
            while (v >= 0x80)
            {
                b[o++] = (byte)(v | 0x80);
                v >>= 7;
            }
            b[o++] = (byte)v;
        }

        private static uint ReadVarUInt(byte[] b, ref int o)
        {
            uint val = 0;
            int shift = 0;
            while (true)
            {
                byte bb = b[o++];
                val |= (uint)(bb & 0x7F) << shift;
                if ((bb & 0x80) == 0) break;
                shift += 7;
            }
            return val;
        }
    }
}
