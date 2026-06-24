using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    // Basis fork addition. Carries a per-frame variable-rate-shading image produced
    // by BasisVariableRateShadingFeature into the forward passes that consume it.
    public class UniversalShadingRateData : ContextItem
    {
        public TextureHandle shadingRateImage;
        public TextureHandle colorMask;
        public bool isValid;

        public override void Reset()
        {
            shadingRateImage = TextureHandle.nullHandle;
            colorMask = TextureHandle.nullHandle;
            isValid = false;
        }
    }
}
