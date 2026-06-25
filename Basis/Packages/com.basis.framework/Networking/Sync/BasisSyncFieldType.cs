using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Basis.Scripts.Networking.Sync
{
    public enum BasisSyncFieldType : byte
    {
        Position = 0,
        Rotation = 1,
        Scale = 2,
        Float = 3,
        Int = 4,
        UShort = 5,
        Vector2 = 6,
        Vector4 = 7,
        Color = 8,
        Bool = 9,
        Byte = 10,
        UInt = 11,
        Angle = 12,
    }

    public enum BasisSyncPool : byte
    {
        Continuous = 0,
        Rotation = 1,
        Discrete = 2,
    }

    public struct BasisSyncField
    {
        public BasisSyncFieldType Type;
        public BasisSyncPool Pool;
        public int Offset;
        public int ContComponents;
        public bool Interpolate;
        public bool Quantize;
    }

    public readonly struct BasisSyncHandle
    {
        public readonly int FieldIndex;
        public readonly BasisSyncFieldType Type;

        public BasisSyncHandle(int fieldIndex, BasisSyncFieldType type)
        {
            FieldIndex = fieldIndex;
            Type = type;
        }

        public bool IsValid => FieldIndex >= 0;
        public static readonly BasisSyncHandle Invalid = new BasisSyncHandle(-1, 0);
    }

    /// <summary>
    /// Ordered set of typed fields a synced object carries on the wire. Declare every field
    /// before the object goes network-ready (e.g. in Awake); the layout is locked afterwards.
    /// </summary>
    public sealed class BasisSyncSchema
    {
        private readonly List<BasisSyncField> _fields = new List<BasisSyncField>();

        public int ContCount { get; private set; }
        public int RotCount { get; private set; }
        public int DiscCount { get; private set; }
        public int FieldCount => _fields.Count;
        public int DirtyMaskBytes => (_fields.Count + 7) >> 3;
        public bool Locked { get; private set; }

        public BasisSyncField GetField(int index) => _fields[index];

        public int AddField(BasisSyncFieldType type, bool interpolate = true, bool quantize = false)
        {
            if (Locked)
                throw new InvalidOperationException("BasisSyncSchema is locked. Declare synced fields before the object is network-ready (in Awake).");
            if (_fields.Count >= 255)
                throw new InvalidOperationException("BasisSyncSchema supports at most 255 fields.");

            var f = new BasisSyncField { Type = type, Interpolate = interpolate, Quantize = quantize };
            switch (type)
            {
                case BasisSyncFieldType.Position:
                case BasisSyncFieldType.Scale:
                    f.Pool = BasisSyncPool.Continuous;
                    f.ContComponents = 3;
                    f.Interpolate = true;
                    f.Offset = ContCount;
                    ContCount += 3;
                    break;
                case BasisSyncFieldType.Float:
                case BasisSyncFieldType.Angle:
                    f.Pool = BasisSyncPool.Continuous;
                    f.ContComponents = 1;
                    f.Offset = ContCount;
                    ContCount += 1;
                    break;
                case BasisSyncFieldType.Vector2:
                    f.Pool = BasisSyncPool.Continuous;
                    f.ContComponents = 2;
                    f.Offset = ContCount;
                    ContCount += 2;
                    break;
                case BasisSyncFieldType.Vector4:
                case BasisSyncFieldType.Color:
                    f.Pool = BasisSyncPool.Continuous;
                    f.ContComponents = 4;
                    f.Offset = ContCount;
                    ContCount += 4;
                    break;
                case BasisSyncFieldType.Rotation:
                    f.Pool = BasisSyncPool.Rotation;
                    f.Offset = RotCount;
                    RotCount += 1;
                    break;
                case BasisSyncFieldType.Int:
                case BasisSyncFieldType.UShort:
                case BasisSyncFieldType.Bool:
                case BasisSyncFieldType.Byte:
                case BasisSyncFieldType.UInt:
                    f.Pool = BasisSyncPool.Discrete;
                    f.Offset = DiscCount;
                    DiscCount += 1;
                    break;
            }

            _fields.Add(f);
            return _fields.Count - 1;
        }

        public void Lock() => Locked = true;
    }

    /// <summary>
    /// One snapshot of a synced object's values, split by interpolation pool.
    /// Continuous = lerp, Rotation = nlerp, Discrete = snap.
    /// </summary>
    public sealed class BasisSyncValues
    {
        public float[] Cont = Array.Empty<float>();
        public quaternion[] Rot = Array.Empty<quaternion>();
        public int[] Disc = Array.Empty<int>();

        public void Allocate(BasisSyncSchema schema)
        {
            Cont = schema.ContCount > 0 ? new float[schema.ContCount] : Array.Empty<float>();
            Rot = schema.RotCount > 0 ? new quaternion[schema.RotCount] : Array.Empty<quaternion>();
            for (int i = 0; i < Rot.Length; i++) Rot[i] = quaternion.identity;
            Disc = schema.DiscCount > 0 ? new int[schema.DiscCount] : Array.Empty<int>();
        }

        public void CopyFrom(BasisSyncValues other)
        {
            Array.Copy(other.Cont, Cont, Cont.Length);
            Array.Copy(other.Rot, Rot, Rot.Length);
            Array.Copy(other.Disc, Disc, Disc.Length);
        }
    }
}
