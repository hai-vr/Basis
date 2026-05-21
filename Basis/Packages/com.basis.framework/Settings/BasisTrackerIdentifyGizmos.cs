using System;
using System.Collections.Generic;
using Basis.Scripts.Device_Management.Devices;
using UnityEngine;

namespace Basis.Scripts.Debugging
{
    public static class BasisTrackerIdentifyGizmos
    {
        private sealed class Entry
        {
            public int GizmoId;
            public Color Color;
        }

        private static readonly Color[] Palette =
        {
            new Color(1f, 0.15f, 0.15f),
            new Color(0.2f, 1f, 0.3f),
            new Color(0.3f, 0.55f, 1f),
            new Color(1f, 0.85f, 0.1f),
            new Color(1f, 0.35f, 1f),
            new Color(0.15f, 1f, 1f),
            new Color(1f, 0.5f, 0.1f),
            new Color(0.7f, 0.45f, 1f),
        };

        private const float BaseSize = 0.07f;
        private const float EmissionIntensity = 2.5f;

        private static readonly Dictionary<BasisInput, Entry> Active = new Dictionary<BasisInput, Entry>();
        private static readonly List<BasisInput> _stale = new List<BasisInput>();
        private static bool _registered;

        public static event Action OnIdentifyChanged;

        public static bool HasActive => Active.Count != 0;

        public static bool TryGetColor(BasisInput input, out Color color)
        {
            if (input != null && Active.TryGetValue(input, out Entry entry))
            {
                color = entry.Color;
                return true;
            }
            color = default;
            return false;
        }

        public static void Toggle(BasisInput input)
        {
            if (input == null) return;
            EnsureMasterToggleHook();

            if (Active.TryGetValue(input, out Entry existing))
            {
                BasisGizmoManager.DestroyGizmo(existing.GizmoId);
                Active.Remove(input);
                OnIdentifyChanged?.Invoke();
                return;
            }

            Color color = PickColor();
            Color glow = color * EmissionIntensity;
            glow.a = 1f;
            float size = BaseSize * HeightScale();
            string label = input.TryGetRole(out var role) ? role.ToString() : "Tracker";
            if (BasisGizmoManager.CreateSphereGizmo($"Identify_{label}", out int id, input.transform.position, size, glow))
            {
                Active[input] = new Entry { GizmoId = id, Color = color };
                OnIdentifyChanged?.Invoke();
            }
        }

        public static void Tick()
        {
            if (Active.Count == 0) return;

            Vector3 scale = Vector3.one * (BaseSize * HeightScale());

            foreach (KeyValuePair<BasisInput, Entry> kvp in Active)
            {
                BasisInput input = kvp.Key;
                if (input == null)
                {
                    _stale.Add(input);
                    continue;
                }
                BasisGizmoManager.UpdateSphereGizmo(kvp.Value.GizmoId, input.transform.position, scale);
            }

            if (_stale.Count == 0) return;

            for (int i = 0; i < _stale.Count; i++)
            {
                if (Active.TryGetValue(_stale[i], out Entry e))
                {
                    BasisGizmoManager.DestroyGizmo(e.GizmoId);
                    Active.Remove(_stale[i]);
                }
            }
            _stale.Clear();
            OnIdentifyChanged?.Invoke();
        }

        public static void ClearAll()
        {
            if (Active.Count == 0) return;
            foreach (KeyValuePair<BasisInput, Entry> kvp in Active)
            {
                BasisGizmoManager.DestroyGizmo(kvp.Value.GizmoId);
            }
            Active.Clear();
            OnIdentifyChanged?.Invoke();
        }

        private static Color PickColor()
        {
            for (int i = 0; i < Palette.Length; i++)
            {
                bool used = false;
                foreach (KeyValuePair<BasisInput, Entry> kvp in Active)
                {
                    if (kvp.Value.Color == Palette[i]) { used = true; break; }
                }
                if (!used) return Palette[i];
            }
            return Palette[Active.Count % Palette.Length];
        }

        private static float HeightScale()
        {
            float scale = BasisHeightDriver.ScaledToMatchValue;
            return scale > 0f ? scale : 1f;
        }

        private static void EnsureMasterToggleHook()
        {
            if (_registered) return;
            BasisGizmoManager.OnUseGizmosChanged += OnMasterToggleChanged;
            _registered = true;
        }

        private static void OnMasterToggleChanged(bool state)
        {
            if (state || Active.Count == 0) return;
            Active.Clear();
            OnIdentifyChanged?.Invoke();
        }
    }
}
