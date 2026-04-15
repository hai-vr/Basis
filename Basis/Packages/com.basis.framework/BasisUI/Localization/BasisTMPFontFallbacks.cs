using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Builds TMP font assets from OS-installed fonts and wires them up as
    /// global fallbacks so any TextMeshPro label falls through to a system
    /// font when its primary font doesn't have a glyph. This is how we get
    /// broad Unicode coverage (CJK, Cyrillic, Arabic, Devanagari, Thai…)
    /// without having to ship every glyph inside a baked static atlas.
    ///
    /// Runs once before the first scene loads. Define
    /// <c>BASIS_DISABLE_TMP_FALLBACKS</c> to opt out.
    /// </summary>
    public static class BasisTMPFontFallbacks
    {
        private const int DefaultSamplingPointSize = 90;

        private static bool _installed;

        /// <summary>
        /// Ordered candidates for each fallback slot. Names are OS font
        /// family names and are fed one-by-one to
        /// <see cref="TMP_FontAsset.CreateFontAsset(string, string, int)"/>,
        /// which asks the underlying FontEngine to resolve the font file on
        /// the host OS. The first name that resolves wins. The list
        /// intentionally overlaps Windows / macOS / Linux / Android so a
        /// single array serves every platform.
        /// </summary>
        private static readonly (string Label, string[] Candidates)[] FallbackGroups = new[]
        {
            ("Basis Fallback - CJK", new[]
            {
                // Windows 10/11 (ships by default, including English SKUs)
                "Yu Gothic UI",
                "Yu Gothic",
                "Meiryo UI",
                "Meiryo",
                "MS UI Gothic",
                "MS Gothic",
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "Microsoft JhengHei UI",
                "Microsoft JhengHei",
                "Malgun Gothic",
                "SimSun",
                "SimHei",
                // macOS
                "Hiragino Sans",
                "Hiragino Kaku Gothic ProN",
                "PingFang SC",
                "PingFang TC",
                "Apple SD Gothic Neo",
                // Linux / Noto
                "Noto Sans CJK JP",
                "Noto Sans CJK SC",
                "Noto Sans CJK KR",
                "Noto Sans JP",
                "Noto Sans SC",
                "Noto Sans KR",
                "Source Han Sans",
                // Android
                "Droid Sans Fallback",
                // Legacy / universal
                "Arial Unicode MS",
            }),
            ("Basis Fallback - Unicode", new[]
            {
                "Segoe UI",
                "Segoe UI Symbol",
                "Tahoma",
                "Arial",
                "Helvetica",
                "Helvetica Neue",
                "DejaVu Sans",
                "Liberation Sans",
                "FreeSans",
                "Noto Sans",
                "Roboto",
            }),
        };

#if !BASIS_DISABLE_TMP_FALLBACKS
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInstall()
        {
            InstallFallbacks();
        }
#endif

        /// <summary>
        /// Idempotent. Builds any missing dynamic-OS TMP font assets and
        /// appends them to <see cref="TMP_Settings.fallbackFontAssets"/>.
        /// Safe to call more than once — fallbacks already registered by name
        /// are skipped.
        /// </summary>
        public static void InstallFallbacks()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;

            List<TMP_FontAsset> fallbacks = TMP_Settings.fallbackFontAssets;
            if (fallbacks == null)
            {
                BasisDebug.LogError("[BasisTMPFontFallbacks] TMP_Settings.fallbackFontAssets is null — skipping install.");
                return;
            }

            for (int i = 0; i < FallbackGroups.Length; i++)
            {
                var group = FallbackGroups[i];
                if (ContainsByName(fallbacks, group.Label))
                {
                    continue;
                }

                TMP_FontAsset tmpFont = TryCreateDynamicOSFallback(group.Label, group.Candidates);
                if (tmpFont != null)
                {
                    fallbacks.Add(tmpFont);
                    BasisDebug.Log($"[BasisTMPFontFallbacks] Installed {group.Label} using OS font family '{tmpFont.faceInfo.familyName}'.");
                }
                else
                {
                    BasisDebug.LogError($"[BasisTMPFontFallbacks] None of the candidates for {group.Label} could be resolved on this OS. Candidates tried: {string.Join(", ", group.Candidates)}");
                }
            }
        }

        private static bool ContainsByName(List<TMP_FontAsset> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].name == name)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Walks the candidate list and returns the first TMP font asset
        /// that TMP can create from an OS font family. This uses the
        /// family-name overload of <see cref="TMP_FontAsset.CreateFontAsset(string, string, int)"/>,
        /// which internally calls FontEngine.TryGetSystemFontReference and
        /// correctly sets the atlas population mode to DynamicOS — that's
        /// the only path that makes the asset actually pull glyphs from the
        /// host OS font file at runtime.
        /// </summary>
        private static TMP_FontAsset TryCreateDynamicOSFallback(string label, string[] candidates)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                string family = candidates[i];
                TMP_FontAsset tmpFont;
                try
                {
                    tmpFont = TMP_FontAsset.CreateFontAsset(family, "Regular", DefaultSamplingPointSize);
                }
                catch (Exception e)
                {
                    BasisDebug.LogError($"[BasisTMPFontFallbacks] CreateFontAsset threw for '{family}': {e.Message}");
                    continue;
                }

                if (tmpFont == null)
                {
                    continue;
                }

                tmpFont.name = label;
                return tmpFont;
            }

            return null;
        }
    }
}
