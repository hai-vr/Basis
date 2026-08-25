#ifndef URP_SCREEN_SPACE_GLOBAL_ILLUMINATION_COMBINE_HLSL
#define URP_SCREEN_SPACE_GLOBAL_ILLUMINATION_COMBINE_HLSL

#include "./SSGIUtilities.hlsl"

// Shared by both combine passes, so the upscale and the debug views exist once rather than once per blend mode.

// Nearest-depth upscaling
// Refer to "https://developer.download.nvidia.com/assets/gamedev/files/sdk/11/OpacityMappingSDKWhitePaper.pdf".
half3 SSGIDepthNormalsUpscale(float2 screenUV, float deviceDepth)
{
    float2 offsetUV = screenUV;
    offsetUV.y -= _IndirectDiffuseTexture_TexelSize.y;

    // The prepare pass already resolved the normal at every pixel, including the depth reconstruction for surfaces
    // with no GBuffer data. Re-deriving it here, five times over, was the most expensive thing in the effect.
    half3 centerNormal = SSGIReadSurfaceNormal(screenUV);
    float centerDepth = ConvertLinearEyeDepth(deviceDepth);

    float2 uv0 = offsetUV + float2(0.0, _IndirectDiffuseTexture_TexelSize.y);
    float2 uv1 = offsetUV + _IndirectDiffuseTexture_TexelSize.xy;
    float2 uv2 = offsetUV + float2(_IndirectDiffuseTexture_TexelSize.x, 0.0);
    float2 uv3 = offsetUV + float2(0.0, 0.0);

    // We can use a gather here but that requires shader model 5.0
    float4 neighborDepth = float4(
        SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uv0, 0).x,
        SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uv1, 0).x,
        SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uv2, 0).x,
        SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uv3, 0).x);

#if !UNITY_REVERSED_Z
    neighborDepth = lerp(UNITY_NEAR_CLIP_VALUE.xxxx, float4(1.0, 1.0, 1.0, 1.0), neighborDepth);
#endif

    neighborDepth = float4(
        ConvertLinearEyeDepth(neighborDepth.x),
        ConvertLinearEyeDepth(neighborDepth.y),
        ConvertLinearEyeDepth(neighborDepth.z),
        ConvertLinearEyeDepth(neighborDepth.w));

    half3 normal0 = SSGIReadSurfaceNormal(uv0);
    half3 normal1 = SSGIReadSurfaceNormal(uv1);
    half3 normal2 = SSGIReadSurfaceNormal(uv2);
    half3 normal3 = SSGIReadSurfaceNormal(uv3);

    half4 distances;
    distances.x = distance(neighborDepth.x, centerDepth);
    distances.y = distance(neighborDepth.y, centerDepth);
    distances.z = distance(neighborDepth.z, centerDepth);
    distances.w = distance(neighborDepth.w, centerDepth);

    distances.x *= (1 - saturate(dot(normal0, centerNormal)));
    distances.y *= (1 - saturate(dot(normal1, centerNormal)));
    distances.z *= (1 - saturate(dot(normal2, centerNormal)));
    distances.w *= (1 - saturate(dot(normal3, centerNormal)));

    half bestDistance = min(min(min(distances.x, distances.y), distances.z), distances.w);

    float2 bestUV = bestDistance == distances.x ? uv0 : bestDistance == distances.y ? uv1 : bestDistance == distances.z ? uv2 : uv3;

    return SAMPLE_TEXTURE2D_X_LOD(_IndirectDiffuseTexture, my_linear_clamp_sampler, bestUV, 0).xyz;
}

half3 SSGIDepthUpscale(float2 screenUV, float deviceDepth)
{
    float2 offsetUV = screenUV;
    offsetUV.y -= _IndirectDiffuseTexture_TexelSize.y;

    float centerDepth = Linear01Depth(deviceDepth, _ZBufferParams);

    half3 resultColor = half3(0.0, 0.0, 0.0);

    float2 uv0 = offsetUV + float2(0.0, _IndirectDiffuseTexture_TexelSize.y);
    float2 uv1 = offsetUV + _IndirectDiffuseTexture_TexelSize.xy;
    float2 uv2 = offsetUV + float2(_IndirectDiffuseTexture_TexelSize.x, 0.0);
    float2 uv3 = offsetUV + float2(0.0, 0.0);

    // We can use a gather here but that requires shader model 5.0
    float4 neighborDepth = float4(
        SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uv0, 0).x,
        SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uv1, 0).x,
        SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uv2, 0).x,
        SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uv3, 0).x);

#if !UNITY_REVERSED_Z
    neighborDepth = lerp(UNITY_NEAR_CLIP_VALUE.xxxx, float4(1.0, 1.0, 1.0, 1.0), neighborDepth);
#endif

    neighborDepth = float4(
        Linear01Depth(neighborDepth.x, _ZBufferParams),
        Linear01Depth(neighborDepth.y, _ZBufferParams),
        Linear01Depth(neighborDepth.z, _ZBufferParams),
        Linear01Depth(neighborDepth.w, _ZBufferParams));

    half4 distances;
    distances.x = abs(neighborDepth.x - centerDepth);
    distances.y = abs(neighborDepth.y - centerDepth);
    distances.z = abs(neighborDepth.z - centerDepth);
    distances.w = abs(neighborDepth.w - centerDepth);

    half bestDistance = min(min(min(distances.x, distances.y), distances.z), distances.w);

    float2 bestUV = bestDistance == distances.x ? uv0 : bestDistance == distances.y ? uv1 : bestDistance == distances.z ? uv2 : uv3;

    const half depthThreshold = 0.01;

    if (distances.x < depthThreshold && distances.y < depthThreshold && distances.z < depthThreshold && distances.w < depthThreshold)
        resultColor = SAMPLE_TEXTURE2D_X_LOD(_IndirectDiffuseTexture, my_linear_clamp_sampler, bestUV, 0).xyz;
    else
        resultColor = SAMPLE_TEXTURE2D_X_LOD(_IndirectDiffuseTexture, my_point_clamp_sampler, screenUV, 0).xyz;

    return resultColor;
}

half3 SSGIResolveIndirectLighting(float2 screenUV, float deviceDepth)
{
    half3 indirectLighting = half3(0.0, 0.0, 0.0);

    UNITY_BRANCH
    if (_DownSample == 1.0)
        indirectLighting = SAMPLE_TEXTURE2D_X_LOD(_IndirectDiffuseTexture, my_point_clamp_sampler, screenUV, 0).rgb;
    else
    #ifdef _DEPTH_NORMALS_UPSCALE
        indirectLighting = SSGIDepthNormalsUpscale(screenUV, deviceDepth);
    #else
        indirectLighting = SSGIDepthUpscale(screenUV, deviceDepth);
    #endif

    return indirectLighting;
}

half3 SSGIDebugColor(float2 screenUV, half3 indirectLighting, half3 giContribution)
{
    half3 result = half3(0.0, 0.0, 0.0);

    if (_SSGIDebugView == 1.0)
        result = indirectLighting * _IndirectDiffuseLightingMultiplier;
    else if (_SSGIDebugView == 2.0)
        result = giContribution;
    // The GBuffer views show what the forward GBuffer pass wrote: black surfaces have no "UniversalGBuffer" pass.
    else if (_SSGIDebugView == 3.0)
        result = SAMPLE_TEXTURE2D_X_LOD(_GBuffer0, my_point_clamp_sampler, screenUV, 0).rgb;
    else if (_SSGIDebugView == 5.0)
        result = SSGISampleEmission(screenUV);
    else
        result = SSGIDecodeNormal(SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, my_point_clamp_sampler, screenUV, 0).xyz) * 0.5 + 0.5;

    return result;
}

#endif
