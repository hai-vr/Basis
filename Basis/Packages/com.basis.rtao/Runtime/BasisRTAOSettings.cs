using System;
using UnityEngine;

namespace Basis.Rendering.RTAO
{
    public enum BasisRTAOQuality
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3
    }

    [Serializable]
    public struct BasisRTAOSettings
    {
        [Range(1, 16)] public int raysPerPixel;
        [Min(1)] public int resolutionDivider;
        [Min(0.01f)] public float radius;
        [Range(0f, 8f)] public float distanceFalloff;
        [Range(0f, 4f)] public float intensity;
        [Range(0.25f, 4f)] public float power;
        [Range(0f, 1f)] public float directLightingStrength;
        [Min(0f)] public float fadeStart;
        [Min(0f)] public float fadeEnd;
        [Min(0f)] public float normalBias;
        [Min(0f)] public float distanceBias;
        public bool stereoCoherentNoise;
        [Min(0.0001f)] public float noiseCellSize;
        [Range(1, 64)] public int temporalFrames;
        [Range(0f, 1f)] public float temporalMinAlpha;
        [Range(0.001f, 0.5f)] public float temporalDepthTolerance;
        [Range(0f, 1f)] public float temporalNormalTolerance;
        [Range(0, 4)] public int denoisePasses;
        [Range(0, 8)] public int blurMaxRadius;
        [Range(0, 8)] public int blurMinRadius;
        [Range(0.001f, 1f)] public float blurDepthSigma;
        [Range(1f, 64f)] public float blurNormalPower;

        public static BasisRTAOSettings Default => FromQuality(BasisRTAOQuality.Medium);

        public static BasisRTAOSettings FromQuality(BasisRTAOQuality quality)
        {
            BasisRTAOSettings settings = new BasisRTAOSettings
            {
                raysPerPixel = 2,
                resolutionDivider = 2,
                radius = 0.1f,
                distanceFalloff = 1f,
                intensity = 1f,
                power = 1f,
                directLightingStrength = 0.5f,
                fadeStart = 40f,
                fadeEnd = 60f,
                normalBias = 0.005f,
                distanceBias = 0.0005f,
                stereoCoherentNoise = true,
                noiseCellSize = 0.004f,
                temporalFrames = 24,
                temporalMinAlpha = 0.05f,
                temporalDepthTolerance = 0.03f,
                temporalNormalTolerance = 0.9f,
                denoisePasses = 2,
                blurMaxRadius = 4,
                blurMinRadius = 1,
                blurDepthSigma = 0.05f,
                blurNormalPower = 16f
            };

            switch (quality)
            {
                case BasisRTAOQuality.Low:
                    settings.raysPerPixel = 1;
                    settings.resolutionDivider = 2;
                    settings.temporalFrames = 32;
                    settings.blurMaxRadius = 5;
                    settings.denoisePasses = 3;
                    break;
                case BasisRTAOQuality.High:
                    settings.raysPerPixel = 4;
                    settings.resolutionDivider = 2;
                    settings.temporalFrames = 20;
                    settings.blurMaxRadius = 3;
                    settings.denoisePasses = 2;
                    break;
                case BasisRTAOQuality.Ultra:
                    settings.raysPerPixel = 6;
                    settings.resolutionDivider = 1;
                    settings.temporalFrames = 16;
                    settings.blurMaxRadius = 2;
                    settings.blurMinRadius = 0;
                    settings.denoisePasses = 1;
                    break;
            }

            return settings;
        }

        // Occlusion Quality is a performance tier: it decides how many rays are cast and how hard the result
        // is filtered. It has no business deciding how dark the shading looks. Keeping the two apart is what
        // lets a renderer author the look once and still let the player pick a cost.
        public BasisRTAOSettings WithCostFrom(in BasisRTAOSettings preset)
        {
            BasisRTAOSettings copy = this;
            copy.raysPerPixel = preset.raysPerPixel;
            copy.resolutionDivider = preset.resolutionDivider;
            copy.temporalFrames = preset.temporalFrames;
            copy.denoisePasses = preset.denoisePasses;
            copy.blurMaxRadius = preset.blurMaxRadius;
            copy.blurMinRadius = preset.blurMinRadius;
            return copy;
        }

        public BasisRTAOSettings Validated()
        {
            BasisRTAOSettings copy = this;
            copy.raysPerPixel = Mathf.Clamp(copy.raysPerPixel, 1, 16);
            copy.resolutionDivider = Mathf.Clamp(copy.resolutionDivider, 1, 4);
            copy.radius = Mathf.Max(0.01f, copy.radius);
            copy.distanceFalloff = Mathf.Clamp(copy.distanceFalloff, 0f, 8f);
            copy.intensity = Mathf.Clamp(copy.intensity, 0f, 4f);
            copy.power = Mathf.Clamp(copy.power, 0.25f, 4f);
            copy.directLightingStrength = Mathf.Clamp01(copy.directLightingStrength);
            copy.fadeStart = Mathf.Max(0f, copy.fadeStart);
            copy.fadeEnd = Mathf.Max(copy.fadeStart + 0.01f, copy.fadeEnd);
            copy.normalBias = Mathf.Max(0f, copy.normalBias);
            copy.distanceBias = Mathf.Max(0f, copy.distanceBias);
            copy.noiseCellSize = Mathf.Max(0.0001f, copy.noiseCellSize);
            copy.temporalFrames = Mathf.Clamp(copy.temporalFrames, 1, 64);
            copy.temporalMinAlpha = Mathf.Clamp01(copy.temporalMinAlpha);
            copy.temporalDepthTolerance = Mathf.Clamp(copy.temporalDepthTolerance, 0.001f, 0.5f);
            copy.temporalNormalTolerance = Mathf.Clamp01(copy.temporalNormalTolerance);
            copy.denoisePasses = Mathf.Clamp(copy.denoisePasses, 0, 4);
            copy.blurMaxRadius = Mathf.Clamp(copy.blurMaxRadius, 0, 8);
            copy.blurMinRadius = Mathf.Clamp(copy.blurMinRadius, 0, copy.blurMaxRadius);
            copy.blurDepthSigma = Mathf.Clamp(copy.blurDepthSigma, 0.001f, 1f);
            copy.blurNormalPower = Mathf.Clamp(copy.blurNormalPower, 1f, 64f);
            return copy;
        }
    }
}
