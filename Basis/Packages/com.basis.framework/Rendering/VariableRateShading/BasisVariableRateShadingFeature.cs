using Basis.BasisUI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Basis.Scripts.Rendering
{
    /// <summary>
    /// Gaze-driven Variable Rate Shading for desktop (DirectX 12). Builds a per-frame
    /// shading rate image that keeps each eye's foveal region sharp and coarsens the
    /// periphery, then binds it onto URP's depth, depth-normals, opaque and transparent
    /// passes. Desktop only; Quest keeps its native OpenXR foveation.
    /// </summary>
    [DisallowMultipleRendererFeature("BasisVariableRateShading")]
    public class BasisVariableRateShadingFeature : ScriptableRendererFeature
    {
        [SerializeField] private ComputeShader buildShader;
        [SerializeField] private float gazeProjectDistance = 3f;
        [SerializeField] private bool yFlip = true;
        [SerializeField] private bool debugVisualize;

        private BasisVariableRateShadingPass _pass;
        private BasisVariableRateShadingDebugPass _debugPass;

        public override void Create()
        {
            _pass = new BasisVariableRateShadingPass(buildShader)
            {
                renderPassEvent = RenderPassEvent.BeforeRendering,
            };
            _pass.Configure(gazeProjectDistance, yFlip);

            _debugPass = new BasisVariableRateShadingDebugPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || buildShader == null)
                return;

            if (!BasisSettingsDefaults.DevVariableRateShading.RawValue
                && !BasisSettingsDefaults.DevVariableRateShadingDesktop.RawValue)
                return;

            _pass.Configure(gazeProjectDistance, yFlip);
            renderer.EnqueuePass(_pass);

            if (debugVisualize && _debugPass != null)
                renderer.EnqueuePass(_debugPass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass = null;
            _debugPass = null;
        }
    }
}
