using UnityEngine;

namespace Basis.Rendering.RTAO
{
    public static class BasisRTAOSettingsMap
    {
        public const string ModeAuto = "Auto";
        public const string ModeRayTraced = "Ray Traced";
        public const string ModeScreenSpace = "Screen Space";

        public static BasisRTAOTracingMode ReadMode(string value)
        {
            switch (Normalise(value))
            {
                case "raytraced":
                case "ray traced":
                case "raytracedonly":
                    return BasisRTAOTracingMode.RayTracedOnly;
                case "screenspace":
                case "screen space":
                    return BasisRTAOTracingMode.ScreenSpace;
                case "computebvh":
                case "compute bvh":
                    return BasisRTAOTracingMode.ComputeBvh;
                default:
                    return BasisRTAOTracingMode.Auto;
            }
        }

        public static string WriteMode(BasisRTAOTracingMode mode)
        {
            switch (mode)
            {
                case BasisRTAOTracingMode.RayTracedOnly: return ModeRayTraced;
                case BasisRTAOTracingMode.ScreenSpace: return ModeScreenSpace;
                default: return ModeAuto;
            }
        }

        public static BasisRTAOQuality ReadQuality(string value)
        {
            switch (Normalise(value))
            {
                case "low": return BasisRTAOQuality.Low;
                case "high": return BasisRTAOQuality.High;
                case "ultra": return BasisRTAOQuality.Ultra;
                default: return BasisRTAOQuality.Medium;
            }
        }

        public static string WriteQuality(BasisRTAOQuality quality)
        {
            switch (quality)
            {
                case BasisRTAOQuality.Low: return "Low";
                case BasisRTAOQuality.High: return "High";
                case BasisRTAOQuality.Ultra: return "Ultra";
                default: return "Medium";
            }
        }

        public const string DenoiseOff = "Off";
        public const string DenoiseStandard = "Standard";
        public const string DenoiseHigh = "High";
        public const string DenoiseMaximum = "Maximum";

        public static int ReadDenoisePasses(string value)
        {
            switch (Normalise(value))
            {
                case "off": return 0;
                case "standard": return 1;
                case "high": return 2;
                case "maximum": return 3;
                default: return 2;
            }
        }

        public static string WriteDenoisePasses(int passes)
        {
            switch (Mathf.Clamp(passes, 0, 3))
            {
                case 0: return DenoiseOff;
                case 1: return DenoiseStandard;
                case 3: return DenoiseMaximum;
                default: return DenoiseHigh;
            }
        }

        public const string ApplyLighting = "Lighting";
        public const string ApplyFinalImage = "Final Image";

        public static readonly string[] DebugStageNames =
        {
            "Final", "Raw", "Temporal", "Denoised", "Position", "Normal"
        };

        public static BasisRTAODebugStage ReadDebugStage(string value)
        {
            switch (Normalise(value))
            {
                case "raw": return BasisRTAODebugStage.Raw;
                case "temporal": return BasisRTAODebugStage.Temporal;
                case "denoised": return BasisRTAODebugStage.Denoised;
                case "position": return BasisRTAODebugStage.Position;
                case "normal": return BasisRTAODebugStage.Normal;
                default: return BasisRTAODebugStage.Final;
            }
        }

        public static string WriteDebugStage(BasisRTAODebugStage stage)
        {
            int index = (int)stage;
            return index >= 0 && index < DebugStageNames.Length ? DebugStageNames[index] : DebugStageNames[0];
        }

        public static BasisRTAOApplyMode ReadApplyMode(string value)
        {
            switch (Normalise(value))
            {
                case "final image":
                case "finalimage":
                case "afteropaque":
                case "after opaque": return BasisRTAOApplyMode.AfterOpaque;
                default: return BasisRTAOApplyMode.Lighting;
            }
        }

        public static string WriteApplyMode(BasisRTAOApplyMode mode)
        {
            return mode == BasisRTAOApplyMode.AfterOpaque ? ApplyFinalImage : ApplyLighting;
        }

        public static BasisRTAOSkinnedMode ReadSkinnedMode(string value)
        {
            switch (Normalise(value))
            {
                case "static": return BasisRTAOSkinnedMode.Static;
                case "dynamic": return BasisRTAOSkinnedMode.Dynamic;
                default: return BasisRTAOSkinnedMode.Off;
            }
        }

        public static string WriteSkinnedMode(BasisRTAOSkinnedMode mode)
        {
            switch (mode)
            {
                case BasisRTAOSkinnedMode.Static: return "Static";
                case BasisRTAOSkinnedMode.Dynamic: return "Dynamic";
                default: return "Off";
            }
        }

        private static string Normalise(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }
    }
}
