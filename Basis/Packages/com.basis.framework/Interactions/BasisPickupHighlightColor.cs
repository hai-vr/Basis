using System.Collections.Generic;
using Basis.BasisUI;
using Basis.Scripts.Settings;
using UnityEngine;

namespace Basis.Scripts.BasisSdk.Interactions
{
    /// <summary>
    /// Tints every pickup collider highlight mesh with the user-chosen colour
    /// (<see cref="BasisSettingsDefaults.PickupHighlightColor"/>) via a property block,
    /// so the shared highlight material asset is never modified. An unset colour leaves
    /// the material's own colour. Highlight renderers register on creation and the
    /// colour is re-applied on every settings change.
    /// </summary>
    public static class BasisPickupHighlightColor
    {
        private static readonly int ColorID = Shader.PropertyToID("_Color");
        private static readonly List<MeshRenderer> Renderers = new();
        private static MaterialPropertyBlock _block;

        [RuntimeInitializeOnLoadMethod]
        private static void Initialize()
        {
            BasisSettingsSystem.OnSettingsFinishedChanges += ApplyToAll;
        }

        public static void Register(MeshRenderer renderer)
        {
            if (renderer == null || Renderers.Contains(renderer))
            {
                return;
            }
            Renderers.Add(renderer);
            ApplyTo(renderer);
        }

        public static void ApplyTo(MeshRenderer renderer)
        {
            Apply(renderer, ParseColor(BasisSettingsDefaults.PickupHighlightColor.RawValue));
        }

        public static void Unregister(MeshRenderer renderer)
        {
            Renderers.Remove(renderer);
        }

        public static void ApplyToAll()
        {
            ApplyToAll(ParseColor(BasisSettingsDefaults.PickupHighlightColor.RawValue));
        }

        public static void PreviewColor(Color? color)
        {
            ApplyToAll(color);
        }

        public static Color? ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return null;
            }
            if (!hex.StartsWith("#"))
            {
                hex = "#" + hex;
            }
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : (Color?)null;
        }

        private static void ApplyToAll(Color? color)
        {
            for (int Index = Renderers.Count - 1; Index >= 0; Index--)
            {
                MeshRenderer renderer = Renderers[Index];
                if (renderer == null)
                {
                    Renderers.RemoveAt(Index);
                    continue;
                }
                Apply(renderer, color);
            }
        }

        private static void Apply(MeshRenderer renderer, Color? color)
        {
            if (renderer == null)
            {
                return;
            }
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }
            renderer.GetPropertyBlock(_block);
            if (color.HasValue)
            {
                _block.SetColor(ColorID, color.Value);
            }
            else
            {
                Material material = renderer.sharedMaterial;
                if (material == null || !material.HasProperty(ColorID))
                {
                    renderer.SetPropertyBlock(null);
                    return;
                }
                _block.SetColor(ColorID, material.GetColor(ColorID));
            }
            renderer.SetPropertyBlock(_block);
        }
    }
}
