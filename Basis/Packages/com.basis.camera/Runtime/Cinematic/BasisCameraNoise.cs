using System;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>Named noise characters, so a shot picks a feel rather than six raw numbers.</summary>
    public enum BasisCameraNoiseProfile
    {
        Off = 0,
        Handheld = 1,
        Documentary = 2,
        Drone = 3,
        Shaky = 4,
        Custom = 5,
    }

    /// <summary>
    /// Six-channel noise for a shot: three positional, three rotational, each with its own
    /// amplitude and rate.
    /// </summary>
    [Serializable]
    public struct BasisCameraNoiseSettings
    {
        public BasisCameraNoiseProfile profile;

        [Tooltip("Positional wander in metres.")]
        public Vector3 positionAmplitude;
        [Tooltip("Positional wander rate in cycles per second.")]
        public Vector3 positionFrequency;

        [Tooltip("Rotational wander in degrees.")]
        public Vector3 rotationAmplitude;
        [Tooltip("Rotational wander rate in cycles per second.")]
        public Vector3 rotationFrequency;

        [Tooltip("Scales the whole profile. 0 is still, 1 is as authored.")]
        [Range(0f, 3f)] public float amplitudeGain;
        [Range(0f, 3f)] public float frequencyGain;

        public static BasisCameraNoiseSettings ForProfile(BasisCameraNoiseProfile profile)
        {
            switch (profile)
            {
                case BasisCameraNoiseProfile.Handheld:
                    return Build(profile, new Vector3(0.012f, 0.016f, 0.008f), new Vector3(0.5f, 0.4f, 0.3f),
                        new Vector3(0.6f, 0.7f, 0.35f), new Vector3(0.4f, 0.35f, 0.25f));

                case BasisCameraNoiseProfile.Documentary:
                    return Build(profile, new Vector3(0.02f, 0.03f, 0.012f), new Vector3(0.8f, 0.7f, 0.5f),
                        new Vector3(1.1f, 1.3f, 0.6f), new Vector3(0.7f, 0.6f, 0.4f));

                case BasisCameraNoiseProfile.Drone:
                    return Build(profile, new Vector3(0.05f, 0.07f, 0.05f), new Vector3(0.18f, 0.14f, 0.16f),
                        new Vector3(0.35f, 0.5f, 0.2f), new Vector3(0.15f, 0.12f, 0.1f));

                case BasisCameraNoiseProfile.Shaky:
                    return Build(profile, new Vector3(0.03f, 0.04f, 0.02f), new Vector3(2.2f, 1.9f, 1.4f),
                        new Vector3(2.0f, 2.4f, 1.2f), new Vector3(1.8f, 1.6f, 1.1f));

                case BasisCameraNoiseProfile.Custom:
                    return Build(profile, new Vector3(0.015f, 0.02f, 0.01f), new Vector3(0.6f, 0.5f, 0.4f),
                        new Vector3(0.7f, 0.9f, 0.4f), new Vector3(0.5f, 0.45f, 0.3f));

                default:
                    return Build(BasisCameraNoiseProfile.Off, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero);
            }
        }

        private static BasisCameraNoiseSettings Build(BasisCameraNoiseProfile profile,
            Vector3 posAmp, Vector3 posFreq, Vector3 rotAmp, Vector3 rotFreq)
            => new BasisCameraNoiseSettings
            {
                profile = profile,
                positionAmplitude = posAmp,
                positionFrequency = posFreq,
                rotationAmplitude = rotAmp,
                rotationFrequency = rotFreq,
                amplitudeGain = 1f,
                frequencyGain = 1f,
            };

        public static BasisCameraNoiseSettings Default => ForProfile(BasisCameraNoiseProfile.Off);
    }

    /// <summary>
    /// Perlin-based camera noise and event impulses. Perlin rather than white noise because it is
    /// continuous — sampling it at any framerate gives the same motion, and it never steps.
    /// </summary>
    public static class BasisCameraNoise
    {
        // Deliberately non-integer and never zero. Perlin noise is exactly zero on its integer
        // lattice, so a channel seeded at 0 samples the weakest line through the field and comes out
        // quieter than the amplitude it was given.
        private static readonly Vector3 PositionSeeds = new Vector3(11.3f, 137.7f, 311.1f);
        private static readonly Vector3 RotationSeeds = new Vector3(523.5f, 701.9f, 911.3f);

        /// <summary>One noise channel in -amplitude..+amplitude.</summary>
        public static float SampleChannel(float time, float amplitude, float frequency, float seed)
        {
            if (amplitude == 0f || frequency == 0f)
            {
                return 0f;
            }
            return (Mathf.PerlinNoise(time * frequency + seed, seed * 0.37f + 0.5f) - 0.5f) * 2f * amplitude;
        }

        public static Vector3 SamplePosition(float time, in BasisCameraNoiseSettings settings)
            => Sample(time, settings.positionAmplitude, settings.positionFrequency,
                PositionSeeds, settings.amplitudeGain, settings.frequencyGain);

        public static Vector3 SampleRotation(float time, in BasisCameraNoiseSettings settings)
            => Sample(time, settings.rotationAmplitude, settings.rotationFrequency,
                RotationSeeds, settings.amplitudeGain, settings.frequencyGain);

        private static Vector3 Sample(float time, Vector3 amplitude, Vector3 frequency, Vector3 seeds,
            float amplitudeGain, float frequencyGain)
        {
            if (amplitudeGain <= 0f || frequencyGain <= 0f ||
                amplitude == Vector3.zero || frequency == Vector3.zero)
            {
                return Vector3.zero;
            }

            return new Vector3(
                SampleChannel(time, amplitude.x * amplitudeGain, frequency.x * frequencyGain, seeds.x),
                SampleChannel(time, amplitude.y * amplitudeGain, frequency.y * frequencyGain, seeds.y),
                SampleChannel(time, amplitude.z * amplitudeGain, frequency.z * frequencyGain, seeds.z));
        }

        /// <summary>
        /// Impulse strength over its lifetime: ramp in over <paramref name="attack"/>, hold for
        /// <paramref name="sustain"/>, fall off over <paramref name="decay"/>. Returns 0 once spent.
        /// </summary>
        public static float ImpulseEnvelope(float elapsed, float attack, float sustain, float decay)
        {
            if (elapsed < 0f)
            {
                return 0f;
            }

            if (attack > 0f && elapsed < attack)
            {
                return Mathf.SmoothStep(0f, 1f, elapsed / attack);
            }

            float afterAttack = elapsed - Mathf.Max(0f, attack);
            if (afterAttack < sustain)
            {
                return 1f;
            }

            if (decay <= 0f)
            {
                return 0f;
            }

            float intoDecay = afterAttack - sustain;
            return intoDecay >= decay ? 0f : Mathf.SmoothStep(1f, 0f, intoDecay / decay);
        }

        /// <summary>
        /// How much of an impulse reaches a camera <paramref name="distance"/> away. Full strength
        /// inside the radius, then falling to nothing at <paramref name="radius"/> +
        /// <paramref name="falloff"/>.
        /// </summary>
        public static float DistanceAttenuation(float distance, float radius, float falloff)
        {
            if (distance <= radius)
            {
                return 1f;
            }
            if (falloff <= 0f)
            {
                return 0f;
            }
            return Mathf.Clamp01(1f - (distance - radius) / falloff);
        }

        public static float ImpulseTotal(float elapsed, float attack, float sustain, float decay,
            float distance, float radius, float falloff)
            => ImpulseEnvelope(elapsed, attack, sustain, decay) * DistanceAttenuation(distance, radius, falloff);
    }
}
