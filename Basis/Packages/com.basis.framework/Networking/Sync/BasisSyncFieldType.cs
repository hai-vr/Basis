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


    /// <summary>
    /// Per-component compression for a continuous field. Ranged maps [Min, Max] onto Bits bits
    /// (step = (Max-Min)/(2^Bits-1)); pick Bits from the precision the allowed distance needs.
    /// </summary>
    public struct BasisQuantSpec
    {
        public BasisQuantMode Mode;
        public float Min;
        public float Max;
        public byte Bits;

        public static BasisQuantSpec Raw => new BasisQuantSpec { Mode = BasisQuantMode.Raw };
        public static BasisQuantSpec Half => new BasisQuantSpec { Mode = BasisQuantMode.Half };
        public static BasisQuantSpec Ranged(float min, float max, int bits) => new BasisQuantSpec
        {
            Mode = BasisQuantMode.Ranged,
            Min = min,
            Max = max,
            Bits = (byte)math.clamp(bits, 1, 31),
        };

        /// <summary>Wire size in bits for this spec.</summary>
        public int BitCount => Mode switch
        {
            BasisQuantMode.Raw => 32,
            BasisQuantMode.Half => 16,
            _ => math.clamp((int)Bits, 1, 31),
        };

        /// <summary>Quantization step (worst-case resolution) for Ranged; 0 for lossless-ish modes.</summary>
        public float Step => Mode == BasisQuantMode.Ranged && Max > Min
            ? (Max - Min) / ((1u << math.clamp((int)Bits, 1, 31)) - 1u)
            : 0f;
    }

    public struct BasisSyncField
    {
        public BasisSyncFieldType Type;
        public BasisSyncPool Pool;
        public int Offset;
        public int ContComponents;
        public bool Interpolate;
        public bool Quantize;
        public int RotBits;
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
        private readonly List<BasisQuantMode> _qMode = new List<BasisQuantMode>();
        private readonly List<float> _qMin = new List<float>();
        private readonly List<float> _qMax = new List<float>();
        private readonly List<byte> _qBits = new List<byte>();

        public int ContCount { get; private set; }
        public int RotCount { get; private set; }
        public int DiscCount { get; private set; }
        public int FieldCount => _fields.Count;
        public int DirtyMaskBytes => (_fields.Count + 7) >> 3;
        public bool Locked { get; private set; }

        public BasisSyncField GetField(int index) => _fields[index];

        // Per-continuous-component compression, indexed the same as BasisSyncValues.Cont.
        public BasisQuantMode QuantMode(int contIndex) => (uint)contIndex < (uint)_qMode.Count ? _qMode[contIndex] : BasisQuantMode.Raw;
        public float QuantMin(int contIndex) => (uint)contIndex < (uint)_qMin.Count ? _qMin[contIndex] : 0f;
        public float QuantMax(int contIndex) => (uint)contIndex < (uint)_qMax.Count ? _qMax[contIndex] : 0f;
        public byte QuantBits(int contIndex) => (uint)contIndex < (uint)_qBits.Count ? _qBits[contIndex] : (byte)0;

        public int AddField(BasisSyncFieldType type, bool interpolate = true, bool quantize = false)
            => AddFieldCore(type, interpolate, LegacySpecs(type, quantize));

        /// <summary>Declare a field with explicit per-component compression. specs length should match the field's component count; missing/extra entries default to Raw and are ignored for discrete/rotation fields.</summary>
        public int AddField(BasisSyncFieldType type, bool interpolate, BasisQuantSpec[] componentSpecs)
            => AddFieldCore(type, interpolate, componentSpecs);

        /// <summary>Declare a smallest-three rotation field with a magnitude-bit budget per component (9 = a 32-bit packet).</summary>
        public int AddRotation(bool interpolate, int magnitudeBits)
            => AddFieldCore(BasisSyncFieldType.Rotation, interpolate, null, magnitudeBits);

        private int AddFieldCore(BasisSyncFieldType type, bool interpolate, BasisQuantSpec[] specs, int rotBits = 9)
        {
            if (Locked)
                throw new InvalidOperationException("BasisSyncSchema is locked. Declare synced fields before the object is network-ready (in Awake).");
            if (_fields.Count >= 255)
                throw new InvalidOperationException("BasisSyncSchema supports at most 255 fields.");

            var f = new BasisSyncField { Type = type, Interpolate = interpolate };
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
                    f.RotBits = math.clamp(rotBits, 2, 18);
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

            bool anyQuant = false;
            if (f.Pool == BasisSyncPool.Continuous)
            {
                for (int c = 0; c < f.ContComponents; c++)
                {
                    BasisQuantSpec spec = (specs != null && c < specs.Length) ? specs[c] : BasisQuantSpec.Raw;
                    _qMode.Add(spec.Mode);
                    _qMin.Add(spec.Min);
                    _qMax.Add(spec.Max);
                    _qBits.Add(spec.Mode == BasisQuantMode.Ranged ? (byte)math.clamp((int)spec.Bits, 1, 31) : (byte)0);
                    if (spec.Mode != BasisQuantMode.Raw) anyQuant = true;
                }
            }
            f.Quantize = anyQuant;

            _fields.Add(f);
            return _fields.Count - 1;
        }

        private static BasisQuantSpec[] LegacySpecs(BasisSyncFieldType type, bool quantize)
        {
            switch (type)
            {
                case BasisSyncFieldType.Position: return Fill(3, quantize ? BasisQuantSpec.Ranged(-1024f, 1024f, 16) : BasisQuantSpec.Raw);
                case BasisSyncFieldType.Scale: return Fill(3, quantize ? BasisQuantSpec.Ranged(0f, 64f, 16) : BasisQuantSpec.Raw);
                case BasisSyncFieldType.Float:
                case BasisSyncFieldType.Angle: return Fill(1, quantize ? BasisQuantSpec.Half : BasisQuantSpec.Raw);
                case BasisSyncFieldType.Vector2: return Fill(2, quantize ? BasisQuantSpec.Half : BasisQuantSpec.Raw);
                case BasisSyncFieldType.Vector4: return Fill(4, quantize ? BasisQuantSpec.Half : BasisQuantSpec.Raw);
                case BasisSyncFieldType.Color: return Fill(4, quantize ? BasisQuantSpec.Ranged(0f, 1f, 8) : BasisQuantSpec.Raw);
                default: return null;
            }
        }

        private static BasisQuantSpec[] Fill(int n, BasisQuantSpec spec)
        {
            var a = new BasisQuantSpec[n];
            for (int i = 0; i < n; i++) a[i] = spec;
            return a;
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
