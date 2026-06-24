using Basis.BasisUI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Basis.Scripts.Rendering
{
    /// <summary>
    /// Gaze-driven Variable Rate Shading for desktop (DirectX 12). Generates a per-frame
    /// shading rate image that keeps the foveal region sharp (1x1) and coarsens the
    /// periphery (2x2 then 4x4), then binds it onto URP's opaque and transparent passes.
    /// Add this only to the Desktop renderer; Quest keeps its native OpenXR foveation.
    /// </summary>
    [DisallowMultipleRendererFeature("BasisVariableRateShading")]
    public class BasisVariableRateShadingFeature : ScriptableRendererFeature
    {
        [SerializeField] private Shader maskShader;
        [SerializeField] private float gazeProjectDistance = 3f;
        [SerializeField] private bool yFlip = true;
        [SerializeField] private bool debugVisualize;
        [SerializeField] private bool debugShowColorMask;

        private Material _maskMaterial;
        private BasisVariableRateShadingPass _pass;
        private BasisVariableRateShadingDebugPass _debugPass;

        public override void Create()
        {
            if (maskShader == null)
                maskShader = Shader.Find("Hidden/Basis/VrsMask");

            if (maskShader != null && _maskMaterial == null)
                _maskMaterial = CoreUtils.CreateEngineMaterial(maskShader);

            _pass = new BasisVariableRateShadingPass(_maskMaterial)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques,
            };
            _pass.Configure(gazeProjectDistance, yFlip);

            _debugPass = new BasisVariableRateShadingDebugPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || _maskMaterial == null)
                return;

            if (!BasisSettingsDefaults.DevVariableRateShading.RawValue
                && !BasisSettingsDefaults.DevVariableRateShadingDesktop.RawValue)
                return;

            _pass.Configure(gazeProjectDistance, yFlip);
            renderer.EnqueuePass(_pass);

            if ((debugVisualize || debugShowColorMask) && _debugPass != null)
            {
                _debugPass.showColorMask = debugShowColorMask;
                renderer.EnqueuePass(_debugPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_maskMaterial);
            _maskMaterial = null;
            _pass = null;
        }
    }
}
