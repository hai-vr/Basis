using System;
using Basis.BasisUI;

/// <summary>
/// Ordinal view of the Graphics tab's quality dropdown, cheapest tier first.
///
/// <para>Shadows and HDR keep their own dropdowns, and those modules stay the only writer for
/// their fields. Instead of the quality level becoming a second writer and fighting them, they
/// clamp themselves against this: the quality level acts as a ceiling, so picking Low can only
/// ever land somewhere cheaper than the dedicated dropdown asked for, never more expensive.</para>
/// </summary>
public static class BasisQualityTier
{
    public const int VeryLow = 0;
    public const int Low = 1;
    public const int Medium = 2;
    public const int High = 3;
    public const int Ultra = 4;

    /// <summary>
    /// Tier index for a dropdown value. Values reach the settings modules lowercased by
    /// <c>BasisSettingsBase.TOLowerValidSettingsChange</c>, but the raw binding keeps its
    /// display casing, so the match is deliberately case-insensitive. An unrecognised string
    /// falls back to <see cref="Ultra"/> — an unexpected value must never silently downgrade
    /// what the player sees.
    /// </summary>
    public static int FromOption(string optionValue)
    {
        if (string.IsNullOrEmpty(optionValue))
        {
            return Ultra;
        }

        if (string.Equals(optionValue, "very low", StringComparison.OrdinalIgnoreCase)) return VeryLow;
        if (string.Equals(optionValue, "low", StringComparison.OrdinalIgnoreCase)) return Low;
        if (string.Equals(optionValue, "medium", StringComparison.OrdinalIgnoreCase)) return Medium;
        if (string.Equals(optionValue, "high", StringComparison.OrdinalIgnoreCase)) return High;
        return Ultra;
    }

    /// <summary>Tier the graphics quality dropdown currently sits at.</summary>
    public static int Current => FromOption(BasisSettingsDefaults.QualityLevel.RawValue);
}
