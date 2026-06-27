using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Scripts.Networking.Sync
{
    public enum BasisSyncedParameterType : byte
    {
        Float = 0,
        Angle = 1,
        Vector2 = 2,
        Vector3 = 3,
        Vector4 = 4,
        Color = 5,
        Rotation = 6,
        Int = 7,
        UInt = 8,
        UShort = 9,
        Bool = 10,
        Byte = 11,
    }

    /// <summary>Per-axis wire compression for one continuous component of a parameter.</summary>
    [Serializable]
    public struct BasisAxisCompression
    {
        public BasisQuantMode Mode;
        public float Min;
        public float Max;
        [Range(1, 31)] public int Bits;

        public BasisQuantSpec ToSpec()
        {
            switch (Mode)
            {
                case BasisQuantMode.Half: return BasisQuantSpec.Half;
                case BasisQuantMode.Ranged: return BasisQuantSpec.Ranged(Min, Max, Bits < 1 ? 1 : Bits);
                default: return BasisQuantSpec.Raw;
            }
        }

        public static BasisAxisCompression Default => new BasisAxisCompression { Mode = BasisQuantMode.Raw, Min = 0f, Max = 1f, Bits = 16 };
    }

    [Serializable]
    public class BasisSyncedParameter
    {
        public string Name = "Parameter";
        public BasisSyncedParameterType Type = BasisSyncedParameterType.Float;
        public bool Interpolate = true;
        public BasisAxisCompression AxisX = BasisAxisCompression.Default;
        public BasisAxisCompression AxisY = BasisAxisCompression.Default;
        public BasisAxisCompression AxisZ = BasisAxisCompression.Default;
        public BasisAxisCompression AxisW = BasisAxisCompression.Default;
    }

    /// <summary>
    /// Data-driven value sync. Build a list of named parameters in the inspector (each with per-axis
    /// compression), then on the owner call Set*(name, value) and on every other client read Get*(name)
    /// — values arrive smoothed through the same playback buffer as <see cref="BasisSyncedObject"/>.
    /// Owner-authoritative (TakeOwnership to become the authority).
    /// </summary>
    public class BasisSyncedParameters : BasisSyncedObject
    {
        public List<BasisSyncedParameter> Parameters = new List<BasisSyncedParameter>();

        private struct Entry
        {
            public BasisSyncHandle Handle;
            public BasisSyncedParameterType Type;
        }

        private readonly Dictionary<string, int> _idByName = new Dictionary<string, int>();
        private readonly List<Entry> _entries = new List<Entry>();
        private bool _mapBuilt;

        protected virtual void Awake()
        {
            BuildParameterMap();
        }

        /// <summary>
        /// Resolves every declared parameter into a synced field. Runs once (from Awake); only call it manually
        /// if you populated <see cref="Parameters"/> from code before the object goes network-ready. Resolve a
        /// name to an id once with <see cref="GetParameterId"/>, then drive it by id to skip the name lookup.
        /// </summary>
        public void BuildParameterMap()
        {
            if (_mapBuilt) return;
            _mapBuilt = true;
            _idByName.Clear();
            _entries.Clear();
            if (Parameters == null) return;

            for (int i = 0; i < Parameters.Count; i++)
            {
                BasisSyncedParameter p = Parameters[i];
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                if (_idByName.ContainsKey(p.Name))
                {
                    BasisDebug.LogError($"BasisSyncedParameters on {name}: duplicate parameter '{p.Name}' ignored.");
                    continue;
                }
                _idByName[p.Name] = _entries.Count;
                _entries.Add(new Entry { Handle = RegisterParameter(p), Type = p.Type });
            }
        }

        private BasisSyncHandle RegisterParameter(BasisSyncedParameter p)
        {
            BasisSyncFieldType fieldType = ToFieldType(p.Type);
            int comps = ComponentCount(p.Type);
            if (comps == 0) return Register(fieldType, p.Interpolate, null);

            var specs = new BasisQuantSpec[comps];
            for (int c = 0; c < comps; c++) specs[c] = AxisAt(p, c).ToSpec();
            return Register(fieldType, p.Interpolate, specs);
        }

        // ── Named writes (owner) / reads (any client; remote-interpolated) ──
        public bool Has(string name) => _idByName.ContainsKey(name);

        /// <summary>
        /// Resolves a parameter name to a stable id once (like Animator.StringToHash). Cache it and use the
        /// id-based Set*/Get* overloads to drive the parameter without the per-call name lookup. Returns -1 if
        /// the name isn't declared.
        /// </summary>
        public int GetParameterId(string name) => _idByName.TryGetValue(name, out int id) ? id : -1;

        public void SetFloat(string name, float value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public float GetFloat(string name) => TryHandle(name, out var h) ? GetFloat(h) : 0f;

        public void SetAngle(string name, float degrees) { if (TryHandle(name, out var h)) LocalSet(h, degrees); }
        public float GetAngle(string name) => GetFloat(name);

        public void SetVector2(string name, Vector2 value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public Vector2 GetVector2(string name) => TryHandle(name, out var h) ? GetVector2(h) : Vector2.zero;

        public void SetVector3(string name, Vector3 value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public Vector3 GetVector3(string name) => TryHandle(name, out var h) ? GetVector3(h) : Vector3.zero;

        public void SetVector4(string name, Vector4 value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public Vector4 GetVector4(string name) => TryHandle(name, out var h) ? GetVector4(h) : Vector4.zero;

        public void SetColor(string name, Color value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public Color GetColor(string name) => TryHandle(name, out var h) ? GetColor(h) : Color.clear;

        public void SetRotation(string name, Quaternion value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public Quaternion GetRotation(string name) => TryHandle(name, out var h) ? GetQuaternion(h) : Quaternion.identity;

        public void SetInt(string name, int value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public int GetInt(string name) => TryHandle(name, out var h) ? GetInt(h) : 0;

        public void SetUInt(string name, uint value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public uint GetUInt(string name) => TryHandle(name, out var h) ? GetUInt(h) : 0u;

        public void SetUShort(string name, ushort value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public ushort GetUShort(string name) => TryHandle(name, out var h) ? GetUShort(h) : (ushort)0;

        public void SetBool(string name, bool value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public bool GetBool(string name) => TryHandle(name, out var h) && GetBool(h);

        public void SetByte(string name, byte value) { if (TryHandle(name, out var h)) LocalSet(h, value); }
        public byte GetByte(string name) => TryHandle(name, out var h) ? GetByte(h) : (byte)0;

        // ── Id-based writes / reads — resolve the id once with GetParameterId, then no per-call name lookup ──
        public void SetFloat(int id, float value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public float GetFloat(int id) => TryHandle(id, out var h) ? GetFloat(h) : 0f;

        public void SetAngle(int id, float degrees) { if (TryHandle(id, out var h)) LocalSet(h, degrees); }
        public float GetAngle(int id) => GetFloat(id);

        public void SetVector2(int id, Vector2 value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public Vector2 GetVector2(int id) => TryHandle(id, out var h) ? GetVector2(h) : Vector2.zero;

        public void SetVector3(int id, Vector3 value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public Vector3 GetVector3(int id) => TryHandle(id, out var h) ? GetVector3(h) : Vector3.zero;

        public void SetVector4(int id, Vector4 value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public Vector4 GetVector4(int id) => TryHandle(id, out var h) ? GetVector4(h) : Vector4.zero;

        public void SetColor(int id, Color value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public Color GetColor(int id) => TryHandle(id, out var h) ? GetColor(h) : Color.clear;

        public void SetRotation(int id, Quaternion value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public Quaternion GetRotation(int id) => TryHandle(id, out var h) ? GetQuaternion(h) : Quaternion.identity;

        public void SetInt(int id, int value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public int GetInt(int id) => TryHandle(id, out var h) ? GetInt(h) : 0;

        public void SetUInt(int id, uint value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public uint GetUInt(int id) => TryHandle(id, out var h) ? GetUInt(h) : 0u;

        public void SetUShort(int id, ushort value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public ushort GetUShort(int id) => TryHandle(id, out var h) ? GetUShort(h) : (ushort)0;

        public void SetBool(int id, bool value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public bool GetBool(int id) => TryHandle(id, out var h) && GetBool(h);

        public void SetByte(int id, byte value) { if (TryHandle(id, out var h)) LocalSet(h, value); }
        public byte GetByte(int id) => TryHandle(id, out var h) ? GetByte(h) : (byte)0;

        private bool TryHandle(string name, out BasisSyncHandle handle)
        {
            if (_idByName.TryGetValue(name, out int id)) { handle = _entries[id].Handle; return true; }
            handle = BasisSyncHandle.Invalid;
            return false;
        }

        private bool TryHandle(int id, out BasisSyncHandle handle)
        {
            if ((uint)id < (uint)_entries.Count) { handle = _entries[id].Handle; return true; }
            handle = BasisSyncHandle.Invalid;
            return false;
        }

        // ── Type mapping ──
        public static int ComponentCount(BasisSyncedParameterType t)
        {
            switch (t)
            {
                case BasisSyncedParameterType.Float:
                case BasisSyncedParameterType.Angle: return 1;
                case BasisSyncedParameterType.Vector2: return 2;
                case BasisSyncedParameterType.Vector3: return 3;
                case BasisSyncedParameterType.Vector4:
                case BasisSyncedParameterType.Color: return 4;
                default: return 0;
            }
        }

        public static BasisSyncFieldType ToFieldType(BasisSyncedParameterType t)
        {
            switch (t)
            {
                case BasisSyncedParameterType.Float: return BasisSyncFieldType.Float;
                case BasisSyncedParameterType.Angle: return BasisSyncFieldType.Angle;
                case BasisSyncedParameterType.Vector2: return BasisSyncFieldType.Vector2;
                case BasisSyncedParameterType.Vector3: return BasisSyncFieldType.Position;
                case BasisSyncedParameterType.Vector4: return BasisSyncFieldType.Vector4;
                case BasisSyncedParameterType.Color: return BasisSyncFieldType.Color;
                case BasisSyncedParameterType.Rotation: return BasisSyncFieldType.Rotation;
                case BasisSyncedParameterType.Int: return BasisSyncFieldType.Int;
                case BasisSyncedParameterType.UInt: return BasisSyncFieldType.UInt;
                case BasisSyncedParameterType.UShort: return BasisSyncFieldType.UShort;
                case BasisSyncedParameterType.Bool: return BasisSyncFieldType.Bool;
                case BasisSyncedParameterType.Byte: return BasisSyncFieldType.Byte;
                default: return BasisSyncFieldType.Float;
            }
        }

        public static BasisAxisCompression AxisAt(BasisSyncedParameter p, int component)
        {
            switch (component)
            {
                case 0: return p.AxisX;
                case 1: return p.AxisY;
                case 2: return p.AxisZ;
                default: return p.AxisW;
            }
        }
    }
}
