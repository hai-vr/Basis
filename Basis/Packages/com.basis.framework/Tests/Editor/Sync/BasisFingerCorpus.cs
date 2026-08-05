using System.Collections.Generic;
using UnityEngine;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Named finger poses in the twenty-scalar space every Basis hand input reduces to
    /// (<see cref="BasisFingerPose"/>: five curl/splay pairs per hand, each component in [-1, 1]).
    ///
    /// CURL POLARITY: negative is CURLED, positive is OPEN. This is easy to get backwards — the
    /// field is called a percentage and the shapes read as "how closed is the hand" — but every
    /// producer agrees on it: BasisInput.Remap01ToMinus1To1 is (0.75 - v) * 2 - 0.75, a DECREASING
    /// map, so a fully pressed trigger arrives as -1.25; and MediaPipeHandConverter.Curl returns
    /// 1 - curl01 * 2, so a fully curled landmark chain arrives as -1. Unity's muscle space agrees
    /// in turn: "Stretched" at +1 is an extended finger.
    ///
    /// That controller range is also worth knowing when reading these: a trigger spans
    /// [-1.25, +0.75], so it overshoots on the curl side and never reaches full extension. The
    /// sampler's clamp is what keeps the overshoot from wrapping onto another finger's cells.
    ///
    /// Fidelity, continuity and cross-rig tests all replay this rather than inventing their own
    /// inputs, so a tolerance argument made in one place holds in the others. The hand shapes are
    /// the ones that carry meaning to a viewer — a pinch that misses is worse than a fist that is
    /// a degree off — and the boundary entries sit deliberately on quantiser step edges, which is
    /// where a rounding-mode mistake shows up and nowhere else.
    ///
    /// A recording from a real session (com.basis.developer.recorder) can be appended as further
    /// cases; the shapes below are what a synthetic corpus can honestly cover.
    /// </summary>
    public static class BasisFingerCorpus
    {
        public readonly struct Pose
        {
            public readonly string Name;
            /// <summary>Ten curl/splay pairs, ordered L thumb→little then R thumb→little.</summary>
            public readonly Vector2[] Fingers;

            public Pose(string name, Vector2[] fingers)
            {
                Name = name;
                Fingers = fingers;
            }

            public override string ToString() => Name;
        }

        public const int FingerCount = 10;

        static Vector2[] Both(Vector2 thumb, Vector2 index, Vector2 middle, Vector2 ring, Vector2 little)
            => new[] { thumb, index, middle, ring, little, thumb, index, middle, ring, little };

        static Vector2[] Uniform(float curl, float splay = 0f)
            => Both(new Vector2(curl, splay), new Vector2(curl, splay), new Vector2(curl, splay),
                    new Vector2(curl, splay), new Vector2(curl, splay));

        /// <summary>Hand shapes a viewer reads as meaning something.</summary>
        public static IEnumerable<Pose> Expressive()
        {
            yield return new Pose("rest", Uniform(0f));
            yield return new Pose("relaxed", Uniform(-0.15f));
            yield return new Pose("flat-open", Uniform(1f));
            yield return new Pose("fist", Uniform(-1f));

            yield return new Pose("point", Both(
                new Vector2(-0.4f, 0f), new Vector2(1f, 0f), new Vector2(-1f, 0f),
                new Vector2(-1f, 0f), new Vector2(-1f, 0f)));

            yield return new Pose("peace", Both(
                new Vector2(-0.6f, 0f), new Vector2(1f, 0.5f), new Vector2(1f, -0.5f),
                new Vector2(-1f, 0f), new Vector2(-1f, 0f)));

            yield return new Pose("thumbs-up", Both(
                new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(-1f, 0f),
                new Vector2(-1f, 0f), new Vector2(-1f, 0f)));

            yield return new Pose("finger-gun", Both(
                new Vector2(1f, 0.3f), new Vector2(1f, 0f), new Vector2(-1f, 0f),
                new Vector2(-1f, 0f), new Vector2(-1f, 0f)));

            // The precision case: thumb and index tips are supposed to meet. Two chains reconstructed
            // independently, so their errors do not cancel at the contact point.
            yield return new Pose("pinch", Both(
                new Vector2(-0.62f, -0.4f), new Vector2(-0.68f, -0.3f), new Vector2(0.2f, 0f),
                new Vector2(0.2f, 0f), new Vector2(0.2f, 0f)));

            yield return new Pose("splay-wide", Uniform(0.8f, 1f));
            yield return new Pose("splay-tight", Uniform(0.8f, -1f));

            // The range a controller trigger actually spans, which is neither symmetric nor [-1, 1].
            yield return new Pose("trigger-released", Uniform(0.75f));
            yield return new Pose("trigger-pressed", Uniform(-1.25f));

            // Asymmetric: catches a left/right mirroring mistake that a symmetric pose hides.
            yield return new Pose("asymmetric", new[]
            {
                new Vector2(-1f, 0f), new Vector2(-0.5f, 0.25f), new Vector2(0f, 0f),
                new Vector2(0.5f, -0.25f), new Vector2(1f, 0f),
                new Vector2(1f, 0f), new Vector2(0.5f, -0.25f), new Vector2(0f, 0f),
                new Vector2(-0.5f, 0.25f), new Vector2(-1f, 0f),
            });
        }

        /// <summary>Values on and around quantiser step edges, where rounding mistakes surface.</summary>
        public static IEnumerable<Pose> Boundaries()
        {
            yield return new Pose("min", Uniform(-1f, -1f));
            yield return new Pose("max", Uniform(1f, 1f));
            yield return new Pose("zero", Uniform(0f, 0f));

            // Exact 8-bit and 6-bit step centres, and a half-step either side of one.
            foreach (int bits in new[] { 6, 8 })
            {
                int steps = (1 << bits) - 1;
                float half = 1f / steps;
                yield return new Pose($"step-{bits}b-lo", Uniform(-1f + 2f * half, -1f + 2f * half));
                yield return new Pose($"step-{bits}b-hi", Uniform(1f - 2f * half, 1f - 2f * half));
                yield return new Pose($"step-{bits}b-edge", Uniform(half, -half));
            }

            // Just outside the legal range: encoders must clamp, not wrap.
            yield return new Pose("overrange", Uniform(1.5f, -1.5f));
        }

        /// <summary>Deterministic spread over the whole square, for distribution statistics.</summary>
        public static IEnumerable<Pose> Sweep(int count = 64)
        {
            uint state = 0x9E3779B9u;
            float Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state / (float)uint.MaxValue) * 2f - 1f;
            }

            for (int i = 0; i < count; i++)
            {
                var fingers = new Vector2[FingerCount];
                for (int f = 0; f < FingerCount; f++) fingers[f] = new Vector2(Next(), Next());
                yield return new Pose($"sweep-{i:D2}", fingers);
            }
        }

        public static IEnumerable<Pose> All()
        {
            foreach (Pose p in Expressive()) yield return p;
            foreach (Pose p in Boundaries()) yield return p;
            foreach (Pose p in Sweep()) yield return p;
        }
    }
}
