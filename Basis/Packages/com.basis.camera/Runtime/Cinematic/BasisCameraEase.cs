using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>The shape of an ease. Each is offered as both an ease in and an ease out.</summary>
    public enum BasisCameraEase
    {
        Linear = 0,
        Sine = 1,
        Quad = 2,
        Cubic = 3,
        Quart = 4,
        Quint = 5,
        Expo = 6,
        Circ = 7,
        Back = 8,
        Elastic = 9,
        Bounce = 10,
    }

    /// <summary>
    /// The standard ease curves, as pure functions of a 0..1 input.
    ///
    /// <para><see cref="In"/> starts at 0 and arrives at 1; <see cref="Out"/> is its mirror, so an
    /// ease named the same way looks the same coming and going. Back and Elastic leave the 0..1
    /// range on purpose — that overshoot is the whole character of those two — so a caller that
    /// cannot take a value outside the range has to clamp it.</para>
    /// </summary>
    public static class BasisCameraEasing
    {
        public const int Count = 11;

        private const float BackOvershoot = 1.70158f;
        private const float ElasticPeriod = 2f * Mathf.PI / 3f;

        public static float In(BasisCameraEase ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case BasisCameraEase.Sine: return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case BasisCameraEase.Quad: return t * t;
                case BasisCameraEase.Cubic: return t * t * t;
                case BasisCameraEase.Quart: return t * t * t * t;
                case BasisCameraEase.Quint: return t * t * t * t * t;
                case BasisCameraEase.Expo: return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
                case BasisCameraEase.Circ: return 1f - Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
                case BasisCameraEase.Back: return (BackOvershoot + 1f) * t * t * t - BackOvershoot * t * t;
                case BasisCameraEase.Elastic:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return -Mathf.Pow(2f, 10f * t - 10f) * Mathf.Sin((t * 10f - 10.75f) * ElasticPeriod);
                case BasisCameraEase.Bounce: return 1f - BounceOut(1f - t);
                default: return t;
            }
        }

        public static float Out(BasisCameraEase ease, float t) => 1f - In(ease, 1f - Mathf.Clamp01(t));

        /// <summary>Whether the enum value names a curve, for settings arriving off disk or the wire.</summary>
        public static bool IsDefined(BasisCameraEase ease) => (int)ease >= 0 && (int)ease < Count;

        private static float BounceOut(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1)
            {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }
            if (t < 2.5f / d1)
            {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }
}
