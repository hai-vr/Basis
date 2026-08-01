using System.Globalization;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Settings;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Remembers where the user dragged each provider panel. The stored value is a delta from the
    /// position the provider authored for that panel, not an absolute placement, so a later change
    /// to a provider's default still lands and the user's nudge is applied on top of it.
    /// <para>
    /// Desktop and VR keep separate deltas. The menu sits at a different distance and scale in each
    /// mode, and only VR can push a panel along Z at all, so a single shared value would drag a
    /// placement made in one mode into the other where it never belonged.
    /// </para>
    /// </summary>
    public static class BasisPanelOffsetMemory
    {
        private const string Prefix = "menu.panel.offset.";

        public const string DesktopPlatform = "desktop";
        public const string VRPlatform = "vr";

        // Backstop for values read back off disk, NOT the working limit — a corrupt or hand-edited
        // settings file must not be able to park a panel where it can't be reached, and nothing
        // else should notice these are here. Panel-local units: X/Y run at the menu's 0.0005 group
        // scale and Z at the 0.01 depth floor BasisMenuMover clamps to, both avatar-relative, so
        // this is roughly four metres sideways and -2..+6 metres of depth. Deliberately wider than
        // a VR drag can reach (the grab is capped at three metres along the pointing ray) so the
        // hand is what stops the panel, not an envelope the user cannot see.
        private const float MaxLateral = 8000f;
        private const float MinDepth = -200f;
        private const float MaxDepth = 600f;

        /// <summary>Which set of offsets applies right now — the user can switch modes mid-session.</summary>
        public static string ActivePlatform => BasisDeviceManagement.IsUserInDesktop() ? DesktopPlatform : VRPlatform;

        public static Vector3 Get(string panelKey) => Get(panelKey, ActivePlatform);

        public static Vector3 Get(string panelKey, string platform)
        {
            if (string.IsNullOrEmpty(panelKey))
            {
                return Vector3.zero;
            }

            string key = BuildKey(panelKey, platform);
            // HasSaveData first: LoadString persists its fallback, which would write a zero entry
            // for every provider the user has merely opened.
            if (!BasisSettingsSystem.HasSaveData(key))
            {
                return Vector3.zero;
            }

            return Clamp(Parse(BasisSettingsSystem.LoadString(key, string.Empty)));
        }

        public static void Set(string panelKey, Vector3 offset) => Set(panelKey, ActivePlatform, offset);

        public static void Set(string panelKey, string platform, Vector3 offset)
        {
            if (string.IsNullOrEmpty(panelKey))
            {
                return;
            }

            BasisSettingsSystem.SaveStringQuiet(BuildKey(panelKey, platform), Format(Clamp(offset)));
        }

        /// <summary>Puts a panel back where its provider asked for it, on the current platform only.</summary>
        public static void Clear(string panelKey) => Set(panelKey, Vector3.zero);

        private static string BuildKey(string panelKey, string platform) => Prefix + platform + "." + panelKey;

        private static string Format(Vector3 offset)
        {
            return offset.x.ToString(CultureInfo.InvariantCulture) + ","
                 + offset.y.ToString(CultureInfo.InvariantCulture) + ","
                 + offset.z.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads a stored placement. Six numbers is tolerated and its tail ignored: panels briefly
        /// stored a tilt alongside the position, and a save written in that window should still put
        /// the panel back where the user left it rather than being discarded whole.
        /// </summary>
        private static Vector3 Parse(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Vector3.zero;
            }

            string[] parts = value.Split(',');
            if (parts.Length != 3 && parts.Length != 6)
            {
                return Vector3.zero;
            }

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return Vector3.zero;
            }

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Trims an offset to the reachable envelope. Public so a live drag stops at the edge
        /// rather than running past it and being silently trimmed on save.
        /// </summary>
        public static Vector3 Clamp(Vector3 offset)
        {
            if (float.IsNaN(offset.x) || float.IsNaN(offset.y) || float.IsNaN(offset.z) ||
                float.IsInfinity(offset.x) || float.IsInfinity(offset.y) || float.IsInfinity(offset.z))
            {
                return Vector3.zero;
            }

            return new Vector3(
                Mathf.Clamp(offset.x, -MaxLateral, MaxLateral),
                Mathf.Clamp(offset.y, -MaxLateral, MaxLateral),
                Mathf.Clamp(offset.z, MinDepth, MaxDepth));
        }
    }
}
