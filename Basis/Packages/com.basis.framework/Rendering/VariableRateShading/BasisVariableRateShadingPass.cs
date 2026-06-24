using Basis.BasisUI;
using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Basis.Scripts.Rendering
{
    internal class BasisVariableRateShadingPass : ScriptableRenderPass
    {
        private static readonly int PropCenter = Shader.PropertyToID("_BasisVrsCenter");
        private static readonly int PropAspect = Shader.PropertyToID("_BasisVrsAspect");
        private static readonly int PropColorNear = Shader.PropertyToID("_BasisVrsColorNear");
        private static readonly int PropColorMid = Shader.PropertyToID("_BasisVrsColorMid");
        private static readonly int PropColorFar = Shader.PropertyToID("_BasisVrsColorFar");

        // Conversion-LUT band colors from the VRS runtime resources asset (pure primaries,
        // matched nearest-color in linear space): red=1x1, green=2x2, blue=4x4.
        private static readonly Vector4 ColorNear = new Vector4(1f, 0f, 0f, 1f); // 1x1
        private static readonly Vector4 ColorMid = new Vector4(0f, 1f, 0f, 1f);  // 2x2
        private static readonly Vector4 ColorFar = new Vector4(0f, 0f, 1f, 1f);  // 4x4

        private static bool _loggedSupport;

        private readonly Material _maskMaterial;

        private float _gazeProjectDistance;
        private bool _yFlip;

        public BasisVariableRateShadingPass(Material maskMaterial)
        {
            _maskMaterial = maskMaterial;
            profilingSampler = new ProfilingSampler("BasisVariableRateShading");
        }

        public void Configure(float gazeProjectDistance, bool yFlip)
        {
            _gazeProjectDistance = gazeProjectDistance;
            _yFlip = yFlip;
        }

        private class MaskPassData
        {
            public Material material;
            public TextureHandle source;
            public Vector4 center;
            public float aspect;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!_loggedSupport)
            {
                _loggedSupport = true;
                BasisDebug.Log($"[BasisVRS] supported={Vrs.IsColorMaskTextureConversionSupported()} " +
                          $"perImageTile={ShadingRateInfo.supportsPerImageTile} " +
                          $"compute={SystemInfo.supportsComputeShaders} " +
                          $"api={SystemInfo.graphicsDeviceType} material={(_maskMaterial != null)}", BasisDebug.LogTag.Rendering);
            }

            if (_maskMaterial == null)
                return;

            if (!Vrs.IsColorMaskTextureConversionSupported())
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.cameraType != CameraType.Game)
                return;

            bool enabled = cameraData.xr.enabled
                ? BasisSettingsDefaults.DevVariableRateShading.RawValue
                : BasisSettingsDefaults.DevVariableRateShadingDesktop.RawValue;
            if (!enabled)
                return;

            RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;
            if (camDesc.width <= 0 || camDesc.height <= 0)
                return;

            Vector2Int tiles = ShadingRateImage.GetAllocTileSize(camDesc.width, camDesc.height);
            if (tiles.x <= 0 || tiles.y <= 0)
                return;

            Vector2 center = ComputeFovealCenter();
            float aspect = (float)camDesc.width / Mathf.Max(1, camDesc.height);
            float innerRadius = BasisSettingsDefaults.VrsFovealInnerRadius.RawValue;
            float outerRadius = BasisSettingsDefaults.VrsFovealOuterRadius.RawValue;

            RenderTextureDescriptor maskDesc = new RenderTextureDescriptor(tiles.x, tiles.y, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None, 0)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                dimension = TextureDimension.Tex2D,
            };
            TextureHandle colorMask = UniversalRenderer.CreateRenderGraphTexture(renderGraph, maskDesc, "_BasisVrsColorMask", clear: false);

            RenderTextureDescriptor sriDesc = new RenderTextureDescriptor(tiles.x, tiles.y, ShadingRateInfo.graphicsFormat, GraphicsFormat.None, 0)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                dimension = TextureDimension.Tex2D,
                enableRandomWrite = true,
                enableShadingRate = true,
            };
            TextureHandle sri = UniversalRenderer.CreateRenderGraphTexture(renderGraph, sriDesc, "_BasisShadingRateImage", clear: false);

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<MaskPassData>("BasisVRS Mask", out MaskPassData data, profilingSampler))
            {
                data.material = _maskMaterial;
                data.source = renderGraph.defaultResources.blackTexture;
                data.center = new Vector4(center.x, center.y, innerRadius, outerRadius);
                data.aspect = aspect;

                builder.UseTexture(data.source, AccessFlags.Read);
                builder.SetRenderAttachment(colorMask, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((MaskPassData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalVector(PropCenter, d.center);
                    ctx.cmd.SetGlobalFloat(PropAspect, d.aspect);
                    ctx.cmd.SetGlobalVector(PropColorNear, ColorNear);
                    ctx.cmd.SetGlobalVector(PropColorMid, ColorMid);
                    ctx.cmd.SetGlobalVector(PropColorFar, ColorFar);
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                });
            }

            Vrs.ColorMaskTextureToShadingRateImage(renderGraph, sri, colorMask, TextureDimension.Tex2D, _yFlip);

            UniversalShadingRateData vrsData = frameData.GetOrCreate<UniversalShadingRateData>();
            vrsData.shadingRateImage = sri;
            vrsData.colorMask = colorMask;
            vrsData.isValid = true;
        }

        private Vector2 ComputeFovealCenter()
        {
            Vector2 center = new Vector2(0.5f, 0.5f);

            Camera cam = BasisLocalCameraDriver.CameraInstance;
            if (BasisLocalCameraDriver.HasInstance && cam != null && BasisLocalCameraDriver.HasEyeGaze)
            {
                Vector3 worldPoint = BasisLocalCameraDriver.GazeOrigin + BasisLocalCameraDriver.GazeDirection * _gazeProjectDistance;
                Vector3 vp = cam.WorldToViewportPoint(worldPoint);
                if (vp.z > 0f)
                    center = new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
            }

            return center;
        }
    }

    /// <summary>
    /// Debug overlay: paints the produced shading rate image over the final frame so the
    /// foveal regions are visible (red = 1x1 sharp, green = 2x2, dark blue = 4x4). Enqueued
    /// only when the feature's Debug Visualize flag is on.
    /// </summary>
    internal class BasisVariableRateShadingDebugPass : ScriptableRenderPass
    {
        private class CopyPassData
        {
            public TextureHandle source;
        }

        // true = show the raw color mask (pre-conversion); false = show the converted SRI.
        public bool showColorMask;

        public BasisVariableRateShadingDebugPass()
        {
            profilingSampler = new ProfilingSampler("BasisVariableRateShading Debug");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!frameData.Contains<UniversalShadingRateData>())
                return;

            UniversalShadingRateData vrsData = frameData.Get<UniversalShadingRateData>();
            if (!vrsData.isValid)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            if (showColorMask)
            {
                if (!vrsData.colorMask.IsValid())
                    return;

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<CopyPassData>("BasisVRS Debug ColorMask", out CopyPassData data, profilingSampler);
                data.source = vrsData.colorMask;
                builder.UseTexture(vrsData.colorMask, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((CopyPassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), 0, false);
                });
                return;
            }

            if (!vrsData.shadingRateImage.IsValid())
                return;

            Vrs.ShadingRateImageToColorMaskTexture(renderGraph, vrsData.shadingRateImage, resourceData.activeColorTexture);
        }
    }
}
