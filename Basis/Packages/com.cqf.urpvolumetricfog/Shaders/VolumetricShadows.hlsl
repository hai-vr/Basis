
#ifndef VOLUMETRIC_SHADOWS_INCLUDED
#define VOLUMETRIC_SHADOWS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

// Copied and modified from SampleShadowmap from Shadows.hlsl. 
real VolumetricSampleShadowmap(TEXTURE2D_SHADOW_PARAM( ShadowMap, sampler_ShadowMap), float4 shadowCoord,ShadowSamplingData ShadowSamplingDatasamplingData,half4 shadowParams, bool isPerspectiveProjection = true)
{
    if (isPerspectiveProjection)
        shadowCoord.xyz /= max(0.00001, shadowCoord.w);

real attenuation = real(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz));
real shadowStrength = shadowParams.x;

    attenuation = LerpWhiteTo(attenuation, shadowStrength);
                
    return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 :
attenuation;
}

// Copied and modified from MainLightRealTimeShadow from Shadows.hlsl. 
half VolumetricMainLightRealtimeShadow(float4 shadowCoord)
{
#if !defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    return half(1.0);
#elif defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
    return SampleScreenSpaceShadowmap(shadowCoord);
#else
    ShadowSamplingData shadowSamplingData = GetMainLightShadowSamplingData();
    half4 shadowParams = GetMainLightShadowParams();
    return VolumetricSampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare), shadowCoord, shadowSamplingData, shadowParams, false);
#endif
}
#endif
