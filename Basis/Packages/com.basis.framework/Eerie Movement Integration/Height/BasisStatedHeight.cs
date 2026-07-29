using System.Globalization;
using UnityEngine;

/// <summary>
/// The player's own answer to "how tall are you", and the single most reliable measurement in the
/// whole calibration pipeline — a tape measure beats anything inferred from a headset.
///
/// It earns its keep in three places. It is the ONLY body measurement available to a player who
/// never stands up (seated mode substitutes a virtual standing eye, so their real height is
/// unobservable), it fills the gap before any observation has settled, and — most valuable — it acts
/// as a prior that throws out readings anatomy cannot produce, which is the one defence the
/// high-water estimator does not otherwise have against a play-space shift.
///
/// Stored as full body height in metres, because that is the number people actually know about
/// themselves; everything downstream wants eye height, which is <see cref="ImpliedEyeHeight"/>.
/// </summary>
public static class BasisStatedHeight
{
    /// <summary>Plausible band for a stated height. Deliberately wider than the measurement band —
    /// people are entitled to be unusual, and this is them telling us directly.</summary>
    public const float MinMeters = 1.0f;
    public const float MaxMeters = 2.4f;

    /// <summary>
    /// How far a measured eye height may sit from the stated one before it is rejected. Covers
    /// posture, hair, headset fit and rounding, but not a shifted play space.
    /// </summary>
    public const float EyeTolerance = 0.08f;
    /// <summary>
    /// The same for arm span, wider because ape index genuinely varies — a long-armed player is real
    /// where an eye height 15% off their own stated height is not.
    /// </summary>
    public const float SpanTolerance = 0.12f;

    public static float Meters => Sanitize(Basis.BasisUI.BasisSettingsDefaults.StatedBodyHeight.RawValue);

    public static bool IsSet => Meters > 0f;

    /// <summary>Standing eye height implied by the stated body height (eyes sit ~7% below the crown).</summary>
    public static float ImpliedEyeHeight => IsSet ? Meters * BasisCalibrationMath.EyeToHeightRatio : 0f;

    /// <summary>Arm span implied by the stated body height (ape index ≈ 1).</summary>
    public static float ImpliedArmSpan => IsSet ? Meters * BasisCalibrationMath.SpanToHeightRatio : 0f;

    static float Sanitize(float meters)
    {
        if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < MinMeters || meters > MaxMeters)
        {
            return 0f;
        }
        return meters;
    }

    /// <summary>
    /// True when a measured eye height is compatible with what the player told us. A reading outside
    /// this is not a posture — it is a bad measurement, and the whole point of holding a stated height
    /// is to be able to say so. Always true when no height has been stated.
    /// </summary>
    public static bool IsPlausibleEye(float eyeMeters)
    {
        if (!IsSet || eyeMeters <= 0f)
        {
            return true;
        }
        float expected = ImpliedEyeHeight;
        return Mathf.Abs(eyeMeters - expected) <= expected * EyeTolerance;
    }

    /// <summary>As <see cref="IsPlausibleEye"/>, for arm span. Under-measurement is NOT rejected — a
    /// short span just means the player has not reached out yet, which is normal and harmless to a
    /// high-water estimator. Only implausibly LONG spans are refused.</summary>
    public static bool IsPlausibleSpan(float spanMeters)
    {
        if (!IsSet || spanMeters <= 0f)
        {
            return true;
        }
        return spanMeters <= ImpliedArmSpan * (1f + SpanTolerance);
    }

    /// <summary>
    /// Parses whatever the player typed. Accepts metric ("178", "178cm", "1.78", "1.78 m") and
    /// imperial ("5'10", "5' 10\"", "5ft 10in", "5 10"). A bare number is read as centimetres above 3
    /// and metres below it, which separates the two ranges no human falls between.
    /// </summary>
    public static bool TryParse(string text, out float meters)
    {
        meters = 0f;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string s = text.Trim().ToLowerInvariant()
            .Replace("\"", " ").Replace("''", " ").Replace("’", "'")
            .Replace("feet", "'").Replace("foot", "'").Replace("ft", "'")
            .Replace("inches", " ").Replace("inch", " ").Replace("in", " ");

        // An explicit metric unit settles it outright. Checked before the feet-and-inches marks so a
        // string carrying both — "178 cm (5' 10\")", which is exactly what the readout shows — is read
        // the way it was written rather than as 178 feet.
        bool saysMetric = s.Contains("cm") || s.Contains("m");
        bool imperial = !saysMetric && s.Contains("'");
        if (imperial)
        {
            int tick = s.IndexOf('\'');
            string feetPart = s.Substring(0, tick);
            string inchPart = s.Substring(tick + 1);
            if (!TryNumber(feetPart, out float feet))
            {
                return false;
            }
            TryNumber(inchPart, out float inches); // inches are optional: "6'" is a height
            meters = (feet * 12f + inches) * 0.0254f;
            return Validate(ref meters);
        }

        // "5 10" with no marks at all is still feet and inches — two numbers cannot be one height.
        string[] parts = s.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && TryNumber(parts[0], out float f2) && TryNumber(parts[1], out float i2)
            && f2 >= 3f && f2 <= 8f && i2 >= 0f && i2 < 12f)
        {
            meters = (f2 * 12f + i2) * 0.0254f;
            return Validate(ref meters);
        }

        // Only the FIRST number counts: the readout form carries a parenthesised imperial echo after it,
        // and gluing every digit together would read "178 cm (5' 10")" as 178510.
        if (!TryLeadingNumber(s, out float value))
        {
            return false;
        }
        bool saidCm = s.Contains("cm");
        bool saidM = !saidCm && s.Contains("m");
        meters = saidCm ? value * 0.01f
               : saidM ? value
               : (value > 3f ? value * 0.01f : value);
        return Validate(ref meters);
    }

    static bool Validate(ref float meters)
    {
        if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < MinMeters || meters > MaxMeters)
        {
            meters = 0f;
            return false;
        }
        return true;
    }

    /// <summary>Reads the first number in the string and stops there, ignoring anything after it.</summary>
    static bool TryLeadingNumber(string s, out float value)
    {
        value = 0f;
        var digits = new System.Text.StringBuilder(s.Length);
        bool started = false;
        foreach (char c in s)
        {
            bool part = char.IsDigit(c) || c == '.' || c == ',' || (!started && c == '-');
            if (part)
            {
                started = true;
                digits.Append(c == ',' ? '.' : c);
            }
            else if (started)
            {
                break;
            }
        }
        return started && float.TryParse(digits.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    static bool TryNumber(string s, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }
        var digits = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (char.IsDigit(c) || c == '.' || c == ',' || c == '-')
            {
                digits.Append(c == ',' ? '.' : c);
            }
        }
        return float.TryParse(digits.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// A single, unambiguous unit — what goes back into an editable field, where the player may well
    /// select it and type over part of it.
    /// </summary>
    public static string FormatCompact(float meters)
    {
        return meters > 0f ? $"{Mathf.RoundToInt(meters * 100f)} cm" : string.Empty;
    }

    /// <summary>
    /// Formats a height in both systems ("178 cm (5' 10\")"), which sidesteps needing a units
    /// preference at all and reads naturally to either audience. For display; use
    /// <see cref="FormatCompact"/> for anything the player edits.
    /// </summary>
    public static string Format(float meters)
    {
        if (meters <= 0f)
        {
            return "-";
        }
        int cm = Mathf.RoundToInt(meters * 100f);
        int totalInches = Mathf.RoundToInt(meters / 0.0254f);
        int feet = totalInches / 12;
        int inches = totalInches % 12;
        return $"{cm} cm ({feet}' {inches}\")";
    }
}
