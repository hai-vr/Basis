using Basis.BTween;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Shared accent tinting for panel elements — used to show at a glance that a control is in a
    /// raised state (Performance Mode is cutting, a volume is boosted past normal, and so on).
    ///
    /// <para>The element's own colours are captured once up front and every tint is a blend toward
    /// the accent, so the styling the prefab shipped with is never lost and clearing always lands
    /// back exactly where it started. Transitions run through the tween system.</para>
    /// </summary>
    /// <summary>
    /// How much attention a live readout deserves. Panels that grade themselves — the frame
    /// bottleneck, avatar cost, cache fill, connection quality — map their own numbers onto this
    /// so the whole settings menu reads with one colour vocabulary.
    /// </summary>
    public enum BasisPanelSeverity
    {
        None,
        Calm,
        Caution,
        Hot
    }

    public static class BasisPanelTint
    {
        public const float Strength = 0.28f;
        public const float Duration = 0.25f;

        public static readonly Color Calm = new Color(0.45f, 0.82f, 0.55f, 1f);
        public static readonly Color Caution = new Color(1f, 0.78f, 0.35f, 1f);
        public static readonly Color Hot = new Color(1f, 0.48f, 0.35f, 1f);

        public static Color AccentFor(BasisPanelSeverity severity)
        {
            switch (severity)
            {
                case BasisPanelSeverity.Calm: return Calm;
                case BasisPanelSeverity.Caution: return Caution;
                case BasisPanelSeverity.Hot: return Hot;
                default: return Color.white;
            }
        }

        /// <summary>Fraction of a threshold a value must fall back under before the grade relaxes.</summary>
        public const double GradeReleaseFraction = 0.85;

        /// <summary>
        /// Grades a rising stat, holding <paramref name="previous"/> until the value has fallen a clear
        /// margin back under the threshold it crossed. Live stats like ping and packet loss jitter
        /// across a boundary constantly, and without the margin the card would retint — and replay its
        /// fade — several times a second. Pass <see cref="BasisPanelSeverity.None"/> for a fresh grade.
        /// </summary>
        public static BasisPanelSeverity Grade(double value, double caution, double hot, BasisPanelSeverity previous)
        {
            if (value >= hot)
            {
                return BasisPanelSeverity.Hot;
            }

            if (value >= caution)
            {
                return previous == BasisPanelSeverity.Hot && value >= hot * GradeReleaseFraction
                    ? BasisPanelSeverity.Hot
                    : BasisPanelSeverity.Caution;
            }

            if (value >= caution * GradeReleaseFraction && previous != BasisPanelSeverity.Calm)
            {
                return BasisPanelSeverity.Caution;
            }

            return BasisPanelSeverity.Calm;
        }

        /// <summary>
        /// Tints for a graded state, or clears back to plain when the state is
        /// <see cref="BasisPanelSeverity.None"/>.
        /// </summary>
        public static void Apply(Handle handle, BasisPanelSeverity severity, bool animate = true)
        {
            if (severity == BasisPanelSeverity.None)
            {
                Clear(handle, animate);
                return;
            }

            Apply(handle, AccentFor(severity), animate);
        }

        public sealed class Handle
        {
            internal PanelElementDescriptor Element;
            internal Color PlainBackground;
            internal Color PlainTitle;
            internal bool HasBackground;
            internal bool HasTitle;
            internal bool Tinted;
            internal Color Accent;
            internal TweenGraphicColor BackgroundTween;
            internal TweenGraphicColor TitleTween;
        }

        /// <summary>
        /// Snapshots an element's untinted colours. Call once, before any tint is applied.
        /// </summary>
        public static Handle Capture(PanelElementDescriptor element)
        {
            Handle handle = new Handle { Element = element };
            if (element == null)
            {
                return handle;
            }

            handle.HasBackground = element.HasElementBaseImage;
            handle.HasTitle = element.HasTitle;
            handle.PlainBackground = handle.HasBackground ? element.ElementBaseImage.color : Color.white;
            handle.PlainTitle = handle.HasTitle ? element.TitleLabel.color : Color.white;
            return handle;
        }

        /// <summary>
        /// Blends the element toward <paramref name="accent"/>. Repeat calls with the same accent
        /// are ignored, so this is safe to drive from a slider that fires every frame.
        /// </summary>
        public static void Apply(Handle handle, Color accent, bool animate = true)
        {
            if (handle?.Element == null)
            {
                return;
            }

            if (handle.Tinted && handle.Accent == accent)
            {
                return;
            }

            handle.Tinted = true;
            handle.Accent = accent;

            Color background = Color.Lerp(handle.PlainBackground, accent, Strength);
            background.a = handle.PlainBackground.a;

            Color title = accent;
            title.a = handle.PlainTitle.a;

            Transition(handle, background, title, animate);
        }

        /// <summary>
        /// Returns the element to the colours <see cref="Capture"/> recorded.
        /// </summary>
        public static void Clear(Handle handle, bool animate = true)
        {
            if (handle?.Element == null || !handle.Tinted)
            {
                return;
            }

            handle.Tinted = false;
            Transition(handle, handle.PlainBackground, handle.PlainTitle, animate);
        }

        private static void Transition(Handle handle, Color background, Color title, bool animate)
        {
            PanelElementDescriptor element = handle.Element;

            if (handle.HasBackground && element.ElementBaseImage != null)
            {
                if (handle.BackgroundTween && handle.BackgroundTween.Active
                    && handle.BackgroundTween.Target == element.ElementBaseImage)
                {
                    handle.BackgroundTween.Reset();
                }

                if (animate && Application.isPlaying)
                {
                    handle.BackgroundTween = element.ElementBaseImage
                        .TweenColor(Duration, element.ElementBaseImage.color, background)
                        .SetEase(Easing.OutCubic);
                }
                else
                {
                    element.ElementBaseImage.color = background;
                }
            }

            if (handle.HasTitle && element.TitleLabel != null)
            {
                if (handle.TitleTween && handle.TitleTween.Active
                    && handle.TitleTween.Target == element.TitleLabel)
                {
                    handle.TitleTween.Reset();
                }

                if (animate && Application.isPlaying)
                {
                    handle.TitleTween = element.TitleLabel
                        .TweenColor(Duration, element.TitleLabel.color, title)
                        .SetEase(Easing.OutCubic);
                }
                else
                {
                    element.TitleLabel.color = title;
                }
            }
        }
    }
}
