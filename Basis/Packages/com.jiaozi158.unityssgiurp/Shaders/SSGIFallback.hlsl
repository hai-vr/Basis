#ifndef URP_SCREEN_SPACE_GLOBAL_ILLUMINATION_FALLBACK_HLSL
#define URP_SCREEN_SPACE_GLOBAL_ILLUMINATION_FALLBACK_HLSL

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "./SSGIUtilities.hlsl"

// Forward+ / Deferred+ reflection probe atlas.
// The SSGI pass defines "_CLUSTER_LIGHT_LOOP" together with "_FP_REFL_PROBE_ATLAS" before including Core.hlsl,
// so URP's own reflection probe declarations (Input.hlsl) and cluster iteration (Clustering.hlsl) are used here,
// including rotated probes, instead of a copy that drifts from the installed URP version.
#if defined(_FP_REFL_PROBE_ATLAS)

// used by Forward+
half3 SampleReflectionProbesAtlas(half3 reflectVector, float3 positionWS, half mipLevel, float2 normalizedScreenSpaceUV)
{
    half3 irradiance = half3(0.0h, 0.0h, 0.0h);

    float totalWeight = 0.0f;

#if defined(_RAYMARCHING_FALLBACK_REFLECTION_PROBES)
    uint probeIndex;
    ClusterIterator it = ClusterInit(normalizedScreenSpaceUV, positionWS, 1);
    [loop] while (ClusterNext(it, probeIndex) && totalWeight < 0.99f)
    {
        probeIndex -= URP_FP_PROBES_BEGIN;

    #if defined(REFLECTION_PROBE_ROTATION)
        // Rotate the position into the probe's frame so the influence volume and box projection can be treated as axis aligned.
        float3 probeCenterPosWS = GetReflectionProbeCenter(urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
        float3 rotPosWS = GetRotatedPoint(probeCenterPosWS, urp_ReflProbes_Rotation[probeIndex], positionWS);
    #else
        float3 rotPosWS = positionWS;
    #endif

        float weight = CalculateProbeWeight(rotPosWS, urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
        weight = min(weight, 1.0f - totalWeight);

        // Box projection is decided per probe (ProbePosition.w > 0), so the projection helper is always used.
    #if defined(REFLECTION_PROBE_ROTATION)
        half3 sampleVector = BoxProjectedCubemapDirection(urp_ReflProbes_Rotation[probeIndex], reflectVector, rotPosWS, urp_ReflProbes_ProbePosition[probeIndex], urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
    #else
        half3 sampleVector = BoxProjectedCubemapDirection(reflectVector, rotPosWS, urp_ReflProbes_ProbePosition[probeIndex], urp_ReflProbes_BoxMin[probeIndex], urp_ReflProbes_BoxMax[probeIndex]);
    #endif

        uint maxMip = (uint)abs(urp_ReflProbes_ProbePosition[probeIndex].w) - 1;
        half probeMip = min(mipLevel, maxMip);
        float2 uv = saturate(PackNormalOctQuadEncode(sampleVector) * 0.5 + 0.5);

        float mip0 = floor(probeMip);
        float mip1 = mip0 + 1;
        float mipBlend = probeMip - mip0;
        float4 scaleOffset0 = urp_ReflProbes_MipScaleOffset[probeIndex * 7 + (uint)mip0];
        float4 scaleOffset1 = urp_ReflProbes_MipScaleOffset[probeIndex * 7 + (uint)mip1];

        half3 encodedIrradiance0 = half3(SAMPLE_TEXTURE2D_LOD(urp_ReflProbes_Atlas, sampler_LinearClamp, uv * scaleOffset0.xy + scaleOffset0.zw, 0.0).rgb);
        half3 encodedIrradiance1 = half3(SAMPLE_TEXTURE2D_LOD(urp_ReflProbes_Atlas, sampler_LinearClamp, uv * scaleOffset1.xy + scaleOffset1.zw, 0.0).rgb);
        irradiance += weight * lerp(encodedIrradiance0, encodedIrradiance1, mipBlend);
        totalWeight += weight;
    }
#endif

#if defined(_RAYMARCHING_FALLBACK_SKY)
    if (totalWeight < 1.0f)
    {
        UpdateAmbientSH();
    #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
        #if defined(_APV_LIGHTING_BUFFER)
        irradiance += SAMPLE_TEXTURE2D_X_LOD(_SSGIAmbientLightingTexture, my_point_clamp_sampler, normalizedScreenSpaceUV, 0).rgb * (1.0 - totalWeight);
        #else
        half3 viewDirectionWS = IsPerspectiveProjection() ? normalize(GetCameraPositionWS() - positionWS) : normalize(UNITY_MATRIX_V[2].xyz);
        half4 probeOcclusion = half4(1.0, 1.0, 1.0, 1.0);
        half3 ambientLighting = SSGISampleProbeVolumePixel(positionWS, reflectVector, viewDirectionWS, normalizedScreenSpaceUV, probeOcclusion);
        irradiance += ambientLighting * probeOcclusion.rgb * (1.0 - totalWeight);
        #endif

    #else
        irradiance += SSGIEvaluateAmbientProbeSRGB(reflectVector) * (1.0 - totalWeight);
    #endif
    }
#endif

    return irradiance;
}

#else // (_FP_REFL_PROBE_ATLAS)

// used by Forward or Deferred
half3 SampleReflectionProbesCubemap(half3 reflectVector, float3 positionWS, half mipLevel, float2 normalizedScreenSpaceUV)
{
    half3 color = half3(0.0, 0.0, 0.0);
    bool probeSampled = false;

    // Check if the reflection probes are correctly set.
    // We don't support probe blending in Forward & Deferred path yet.
#if defined(_RAYMARCHING_FALLBACK_REFLECTION_PROBES)
    if (_ProbeSet)
    {
        half3 uvw = reflectVector;

        if (_SpecCube0_ProbePosition.w > 0.0) // Box Projection Probe
        {
            float3 factors = ((reflectVector > 0 ? _SpecCube0_BoxMax.xyz : _SpecCube0_BoxMin.xyz) - positionWS) * rcp(reflectVector);
            float scalar = min(min(factors.x, factors.y), factors.z);
            uvw = reflectVector * scalar + (positionWS - _SpecCube0_ProbePosition.xyz);
        }

        color = DecodeHDREnvironment(SAMPLE_TEXTURECUBE_LOD(_SpecCube0, sampler_SpecCube0, uvw, mipLevel), _SpecCube0_HDR).rgb;

        // TODO: Implement a better reflection probe blending for Forward & Deferred path
        probeSampled = true;
    }
#endif

    // Single exit: an early return inside the branch above makes FXC fail the pass with
    // "use of potentially uninitialized variable (SampleReflectionProbesCubemap)".
#if defined(_RAYMARCHING_FALLBACK_SKY)
    if (!probeSampled)
    {
        UpdateAmbientSH();
    #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
        #if defined(_APV_LIGHTING_BUFFER)
        color = SAMPLE_TEXTURE2D_X_LOD(_SSGIAmbientLightingTexture, my_point_clamp_sampler, normalizedScreenSpaceUV, 0).rgb;
        #else
        half3 viewDirectionWS = IsPerspectiveProjection() ? normalize(GetCameraPositionWS() - positionWS) : normalize(UNITY_MATRIX_V[2].xyz);
        half4 probeOcclusion = half4(1.0, 1.0, 1.0, 1.0);
        half3 ambientLighting = SSGISampleProbeVolumePixel(positionWS, reflectVector, viewDirectionWS, normalizedScreenSpaceUV, probeOcclusion);
        color = ambientLighting * probeOcclusion.rgb;
        #endif
    #else
        color = SSGIEvaluateAmbientProbeSRGB(reflectVector.xyz);
    #endif
    }
#endif
    return color;
}
#endif

half3 SampleReflectionProbes(half3 reflectVector, float3 positionWS, half mipLevel, float2 normalizedScreenSpaceUV)
{
    half3 color = half3(0.0, 0.0, 0.0);

    #if defined(_FP_REFL_PROBE_ATLAS)
        color = ClampToFloat16Max(SampleReflectionProbesAtlas(reflectVector, positionWS, mipLevel, normalizedScreenSpaceUV));
    #else
        color = SampleReflectionProbesCubemap(reflectVector, positionWS, mipLevel, normalizedScreenSpaceUV);
    #endif

    // Limit the intensity of SSGI results accumulated in reflection probe
    return _IsProbeCamera ? color * 0.3 : color;
}
#endif
