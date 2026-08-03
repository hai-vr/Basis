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
        private const string PropSri = "_BasisSri";
        private const string PropCenterLR = "_BasisVrsCenterLR";
        private const string PropParams = "_BasisVrsParams";
        private const string PropTile = "_BasisVrsTile";
        private const string PropRates = "_BasisVrsRates";

        private static readonly Vector4 Rates = new Vector4(Encode(0, 0), Encode(1, 1), Encode(2, 2), 0f);

        private readonly ComputeShader _buildShader;
        private readonly int _kernel;

        private float _gazeProjectDistance;
        private bool _yFlip;

        public BasisVariableRateShadingPass(ComputeShader buildShader)
        {
            _buildShader = buildShader;
            if (_buildShader != null)
                _kernel = _buildShader.FindKernel("CSMain");
            profilingSampler = new ProfilingSampler("BasisVariableRateShading");
        }

        public void Configure(float gazeProjectDistance, bool yFlip)
        {
            _gazeProjectDistance = gazeProjectDistance;
            _yFlip = yFlip;
        }

        private static int Encode(int log2X, int log2Y) => ((log2X & 3) << 2) | (log2Y & 3);

        private class BuildPassData
        {
            public ComputeShader cs;
            public int kernel;
            public TextureHandle sri;
            public Vector4 centers;
            public Vector4 parms;
            public Vector2Int tiles;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_buildShader == null)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.cameraType != CameraType.Game)
                return;

            if (!ReferenceEquals(cameraData.camera, BasisLocalCameraDriver.CameraInstance))
                return;

            if (!BasisVariableRateShadingFeature.IsSupported)
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

            Vector4 centers = ComputeFovealCenters(cameraData);
            float aspect = (float)camDesc.width / Mathf.Max(1, camDesc.height);
            // The graphics quality level tightens the sharp region rather than writing the
            // player's sliders — a smaller foveal radius leaves more of the frame at the coarse
            // shading rate. Clamping here instead of overwriting the setting keeps the slider
            // showing what the player chose, the same way shadows and HDR clamp themselves.
            float fovealScale = FovealScaleForTier(BasisQualityTier.Current);
            float inner = BasisSettingsDefaults.VrsFovealInnerRadius.RawValue * fovealScale;
            float outer = BasisSettingsDefaults.VrsFovealOuterRadius.RawValue * fovealScale;

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

            using (var builder = renderGraph.AddComputePass<BuildPassData>("BasisVRS Build", out BuildPassData data))
            {
                data.cs = _buildShader;
                data.kernel = _kernel;
                data.sri = sri;
                data.centers = centers;
                data.parms = new Vector4(inner, outer, aspect, 0f);
                data.tiles = tiles;

                builder.UseTexture(sri, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((BuildPassData d, ComputeGraphContext ctx) =>
                {
                    ctx.cmd.SetComputeTextureParam(d.cs, d.kernel, PropSri, d.sri);
                    ctx.cmd.SetComputeVectorParam(d.cs, PropCenterLR, d.centers);
                    ctx.cmd.SetComputeVectorParam(d.cs, PropParams, d.parms);
                    ctx.cmd.SetComputeVectorParam(d.cs, PropTile, new Vector4(d.tiles.x, d.tiles.y, 0f, 0f));
                    ctx.cmd.SetComputeVectorParam(d.cs, PropRates, Rates);
                    int groupsX = (d.tiles.x + 7) / 8;
                    int groupsY = (d.tiles.y + 7) / 8;
                    ctx.cmd.DispatchCompute(d.cs, d.kernel, groupsX, groupsY, 1);
                });
            }

            UniversalShadingRateData vrsData = frameData.GetOrCreate<UniversalShadingRateData>();
            vrsData.shadingRateImage = sri;
            vrsData.isValid = true;
        }

        /// <summary>
        /// How far the graphics quality level shrinks the foveal radii. Medium and above leave
        /// the player's sliders alone; the two low tiers pull the sharp region in so more of
        /// the frame shades at the coarse rate.
        /// </summary>
        private static float FovealScaleForTier(int tier)
        {
            switch (tier)
            {
                case BasisQualityTier.VeryLow: return 0.5f;
                case BasisQualityTier.Low: return 0.7f;
                default: return 1f;
            }
        }

        private Vector4 ComputeFovealCenters(UniversalCameraData cameraData)
        {
            Vector2 fallback = new Vector2(0.5f, 0.5f);
            if (!BasisLocalCameraDriver.HasInstance || BasisLocalCameraDriver.CameraInstance == null || !BasisLocalCameraDriver.HasEyeGaze)
                return new Vector4(fallback.x, fallback.y, fallback.x, fallback.y);

            Vector3 focal = BasisLocalCameraDriver.GazeOrigin + BasisLocalCameraDriver.GazeDirection * _gazeProjectDistance;
            int rightEye = cameraData.xr.enabled && cameraData.xr.singlePassEnabled ? 1 : 0;

            Matrix4x4 viewLeft = cameraData.GetViewMatrix(0);
            Matrix4x4 viewRight = cameraData.GetViewMatrix(rightEye);
            Matrix4x4 projLeft = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(0), false);
            Matrix4x4 projRight = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(rightEye), false);

            Vector2 uvLeft = ProjectToUV(focal, viewLeft, projLeft, fallback);
            Vector2 uvRight = ProjectToUV(focal, viewRight, projRight, fallback);

            if (_yFlip)
            {
                uvLeft.y = 1f - uvLeft.y;
                uvRight.y = 1f - uvRight.y;
            }
            return new Vector4(uvLeft.x, uvLeft.y, uvRight.x, uvRight.y);
        }

        private static Vector2 ProjectToUV(Vector3 worldPoint, Matrix4x4 view, Matrix4x4 proj, Vector2 fallback)
        {
            Vector4 clip = proj * (view * new Vector4(worldPoint.x, worldPoint.y, worldPoint.z, 1f));
            if (clip.w <= 1e-5f)
                return fallback;
            float u = 0.5f * (clip.x / clip.w) + 0.5f;
            float v = 0.5f * (clip.y / clip.w) + 0.5f;
            return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
        }
    }

    /// <summary>
    /// Debug overlay: paints the produced shading rate image over the final frame so the
    /// foveal regions are visible. Enqueued only when the feature's Debug Visualize flag is on.
    /// </summary>
    internal class BasisVariableRateShadingDebugPass : ScriptableRenderPass
    {
        public BasisVariableRateShadingDebugPass()
        {
            profilingSampler = new ProfilingSampler("BasisVariableRateShading Debug");
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!frameData.Contains<UniversalShadingRateData>())
                return;

            UniversalShadingRateData vrsData = frameData.Get<UniversalShadingRateData>();
            if (!vrsData.isValid || !vrsData.shadingRateImage.IsValid())
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            Vrs.ShadingRateImageToColorMaskTexture(renderGraph, vrsData.shadingRateImage, resourceData.activeColorTexture);
        }
    }
}
