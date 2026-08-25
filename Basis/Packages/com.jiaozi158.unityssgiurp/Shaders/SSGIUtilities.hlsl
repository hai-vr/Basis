#ifndef URP_SCREEN_SPACE_GLOBAL_ILLUMINATION_UTILITIES_HLSL
#define URP_SCREEN_SPACE_GLOBAL_ILLUMINATION_UTILITIES_HLSL

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

#if UNITY_VERSION >= 202310
#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
#include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"

void SSGIEvaluateAdaptiveProbeVolume(in float3 posWS, in half3 normalWS, in half3 viewDir, in float2 positionSS, in uint renderingLayer,
    out half3 bakeDiffuseLighting, out half4 probeOcclusion)
{
    bakeDiffuseLighting = half3(0.0, 0.0, 0.0);

#if UNITY_VERSION >= 202330
    posWS = AddNoiseToSamplingPosition(posWS, positionSS, viewDir);
#else
    posWS = AddNoiseToSamplingPosition(posWS, positionSS);
#endif

#if UNITY_VERSION >= 600000
    APVSample apvSample = SampleAPV(posWS, normalWS, renderingLayer, viewDir);
#else
    APVSample apvSample = SampleAPV(posWS, normalWS, viewDir);
#endif
    
#ifdef USE_APV_PROBE_OCCLUSION
    probeOcclusion = apvSample.probeOcclusion;
#else
    probeOcclusion = 1;
#endif

    EvaluateAdaptiveProbeVolume(apvSample, normalWS, bakeDiffuseLighting);
}

#endif
#endif

#include "./SSGIConfig.hlsl"
#include "./SSGIInput.hlsl"

void UpdateAmbientSH()
{
    unity_SHAr = ssgi_SHAr;
    unity_SHAg = ssgi_SHAg;
    unity_SHAb = ssgi_SHAb;
    unity_SHBr = ssgi_SHBr;
    unity_SHBg = ssgi_SHBg;
    unity_SHBb = ssgi_SHBb;
    unity_SHC = ssgi_SHC;
}

half3 SSGIEvaluateAmbientProbe(half3 normalWS)
{
    // Linear + constant polynomial terms
    half3 res = SHEvalLinearL0L1(normalWS, ssgi_SHAr, ssgi_SHAg, ssgi_SHAb);

    // Quadratic polynomials
    res += SHEvalLinearL2(normalWS, ssgi_SHBr, ssgi_SHBg, ssgi_SHBb, ssgi_SHC);

    return res;
}

half3 SSGISampleProbeVolumePixel(in float3 absolutePositionWS, in float3 normalWS, in float3 viewDir, in float2 screenUV, out half4 probeOcclusion)
{
    probeOcclusion = 1.0;

#if defined(EVALUATE_SH_VERTEX) || defined(EVALUATE_SH_MIXED)
    return half3(0.0, 0.0, 0.0);
#elif defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    half3 bakedGI;
    if (_EnableProbeVolumes)
    {
        // TODO: get the actual rendering layer
        uint meshRenderingLayer = 0xFFFFFFFF; // RenderingLayerMask.Everything

        SSGIEvaluateAdaptiveProbeVolume(absolutePositionWS, normalWS, viewDir, screenUV * _ScreenSize.xy, meshRenderingLayer, bakedGI, probeOcclusion);
    }
    else
    {
        bakedGI = SSGIEvaluateAmbientProbe(normalWS);
    }
#ifdef UNITY_COLORSPACE_GAMMA
    bakedGI = LinearToSRGB(bakedGI);
#endif
    return bakedGI;
#else
    return half3(0, 0, 0);
#endif
}

half3 SSGIEvaluateAmbientProbeSRGB(half3 normalWS)
{
    half3 res = SSGIEvaluateAmbientProbe(normalWS);
#ifdef UNITY_COLORSPACE_GAMMA
    res = LinearToSRGB(res);
#endif
    return res;
}

#ifndef kDieletricSpec
#define kDieletricSpec half4(0.04, 0.04, 0.04, 1.0 - 0.04) // standard dielectric reflectivity coef at incident angle (= 4%)
#endif

#include "./SSGIFallback.hlsl" // Reflection Probes Sampling

// position  : world space ray origin
// direction : world space ray direction
struct Ray
{
    float3 position;
    half3  direction;
};

// position  : world space hit position
// distance  : distance that ray travels
// ...       : surfaceData of hit position
struct RayHit
{
    float3 position;
    float  distance;
    half3  normal;
    half3  emission;
};

// position : the intersection between Ray and Scene.
// distance : the distance from Ray's starting position to intersection.
// normal   : the normal direction of the intersection.
// ...      : material information from GBuffer.
RayHit InitializeRayHit()
{
    RayHit rayHit;
    rayHit.position = float3(0.0, 0.0, 0.0);
    rayHit.distance = REAL_EPS;
    rayHit.normal = half3(0.0, 0.0, 0.0);
    rayHit.emission = half3(0.0, 0.0, 0.0);
    return rayHit;
}

uint UnpackMaterialFlags(float packedMaterialFlags)
{
    return uint((packedMaterialFlags * 255.0h) + 0.5h);
}

// One blue noise value per pixel. A constant offset gives a second field that is uncorrelated with the first.
half SSGIBlueNoise(uint2 pixel)
{
    return LOAD_TEXTURE2D(_SSGIBlueNoise, pixel & 63).r;
}

// Supports perspective and orthographic projections
float ConvertLinearEyeDepth(float deviceDepth)
{
    UNITY_BRANCH
    if (IsPerspectiveProjection())
        return LinearEyeDepth(deviceDepth, _ZBufferParams);
    else
    {
    #if UNITY_REVERSED_Z
        deviceDepth = 1.0 - deviceDepth;
    #endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, deviceDepth);
    }

}

// The forward GBuffer pass clears its targets, so a pixel whose shader has no "UniversalGBuffer" pass reads back all zeros.
bool SSGIHasGBuffer(half4 rawGBuffer2)
{
    return dot(rawGBuffer2.rgb, rawGBuffer2.rgb) > 1e-4;
}

// Non-zero GBuffer data is not enough: the GBuffer pass draws ONLY shaders that have a GBuffer pass, so a surface without one
// (a tree card, a toon avatar, anything out of an existing asset bundle) never occludes anything there and the pixel keeps the
// data of whatever surface lies BEHIND it. Shading the near surface with the far one's albedo and normal is what turns dark
// alpha-tested foliage into bright cards lit as though they were the wall behind them. Comparing the GBuffer's own depth against
// the camera depth rejects that case, so those pixels fall through to the fallback they were always meant to use.
bool SSGIGBufferMatchesSurface(float2 screenUV)
{
    float gBufferDepth = SAMPLE_TEXTURE2D_X_LOD(_GBufferDepthTexture, my_point_clamp_sampler, screenUV, 0).r;
    float cameraDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, screenUV, 0).r;

#if !UNITY_REVERSED_Z
    gBufferDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, gBufferDepth);
    cameraDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, cameraDepth);
#endif

    float gBufferEyeDepth = ConvertLinearEyeDepth(gBufferDepth);
    float cameraEyeDepth = ConvertLinearEyeDepth(cameraDepth);

    // Both buffers rasterize the same triangles, so a match is exact up to precision; the tolerance is relative because depth
    // precision falls off with distance.
    return abs(gBufferEyeDepth - cameraEyeDepth) <= max(0.01, cameraEyeDepth * 0.01);
}

half3 SSGIDecodeNormal(half3 packedNormal)
{
#if defined(_GBUFFER_NORMALS_OCT)
    half2 remappedOctNormalWS = half2(Unpack888ToFloat2(packedNormal));            // values between [ 0, +1]
    half2 octNormalWS = remappedOctNormalWS.xy * half(2.0) - half(1.0);            // values between [-1, +1]
    return half3(UnpackNormalOctQuadEncode(octNormalWS));                          // values between [-1, +1]
#else
    return packedNormal;
#endif
}

// Normal reconstructed from the depth buffer for surfaces whose shader wrote no GBuffer.
// On each axis the neighbour closer in depth is used, so the normal does not smear across silhouettes.
// This needs nothing but the depth texture, so it works with MSAA depth priming where a depth normals prepass cannot.
half3 SSGIReconstructNormalWS(float2 screenUV)
{
    float2 texel = _ScreenSize.zw;
    float2 uvL = screenUV - float2(texel.x, 0.0);
    float2 uvR = screenUV + float2(texel.x, 0.0);
    float2 uvD = screenUV - float2(0.0, texel.y);
    float2 uvU = screenUV + float2(0.0, texel.y);

    float d0 = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, screenUV, 0).r;
    float dL = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uvL, 0).r;
    float dR = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uvR, 0).r;
    float dD = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uvD, 0).r;
    float dU = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, uvU, 0).r;

#if !UNITY_REVERSED_Z
    d0 = lerp(UNITY_NEAR_CLIP_VALUE, 1, d0);
    dL = lerp(UNITY_NEAR_CLIP_VALUE, 1, dL);
    dR = lerp(UNITY_NEAR_CLIP_VALUE, 1, dR);
    dD = lerp(UNITY_NEAR_CLIP_VALUE, 1, dD);
    dU = lerp(UNITY_NEAR_CLIP_VALUE, 1, dU);
#endif

    float3 P0 = ComputeWorldSpacePosition(screenUV, d0, UNITY_MATRIX_I_VP);
    float3 PL = ComputeWorldSpacePosition(uvL, dL, UNITY_MATRIX_I_VP);
    float3 PR = ComputeWorldSpacePosition(uvR, dR, UNITY_MATRIX_I_VP);
    float3 PD = ComputeWorldSpacePosition(uvD, dD, UNITY_MATRIX_I_VP);
    float3 PU = ComputeWorldSpacePosition(uvU, dU, UNITY_MATRIX_I_VP);

    float e0 = ConvertLinearEyeDepth(d0);
    float3 dX = abs(ConvertLinearEyeDepth(dL) - e0) < abs(ConvertLinearEyeDepth(dR) - e0) ? P0 - PL : PR - P0;
    float3 dY = abs(ConvertLinearEyeDepth(dD) - e0) < abs(ConvertLinearEyeDepth(dU) - e0) ? P0 - PD : PU - P0;

    half3 normalWS = half3(normalize(cross(dY, dX)));

    // Visible opaque surfaces face the camera; fix the winding accordingly.
    half3 viewDirectionWS = IsPerspectiveProjection() ? half3(normalize(GetCameraPositionWS() - P0)) : half3(normalize(UNITY_MATRIX_V[2].xyz));
    if (dot(normalWS, viewDirectionWS) < 0.0)
        normalWS = -normalWS;

    return normalWS;
}

half3 SSGISampleNormalWS(float2 screenUV, out bool hasGBuffer)
{
    half4 gbuffer2 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, my_point_clamp_sampler, screenUV, 0);
    hasGBuffer = SSGIHasGBuffer(gbuffer2) && SSGIGBufferMatchesSurface(screenUV);

    // Single exit: with an early return in the branch, FXC fails the pass with
    // "use of potentially uninitialized variable (SSGISampleNormalWS)".
    half3 normalWS = SSGIDecodeNormal(gbuffer2.xyz);

    UNITY_BRANCH
    if (!hasGBuffer && _SSGIGBufferFallback != 0.0)
        normalWS = SSGIReconstructNormalWS(screenUV);

    return normalWS;
}

half3 SSGISampleNormalWS(float2 screenUV)
{
    bool hasGBuffer;
    return SSGISampleNormalWS(screenUV, hasGBuffer);
}

bool SSGIHasGBuffer(float2 screenUV)
{
    return SSGIHasGBuffer(SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, my_point_clamp_sampler, screenUV, 0)) && SSGIGBufferMatchesSurface(screenUV);
}

// Ambient light reaching the pixel for its normal: the adaptive probe volume when the project has one, else the ambient probe.
// This is the term the combine passes remove from the camera colour and replace with the traced bounce.
half3 SSGIEvaluateAmbientLighting(float2 screenUV, float3 positionWS, half3 normalWS)
{
#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
    half3 viewDirectionWS = IsPerspectiveProjection() ? normalize(GetCameraPositionWS() - positionWS) : normalize(UNITY_MATRIX_V[2].xyz);
    half4 probeOcclusion = half4(1.0, 1.0, 1.0, 1.0);
    half3 ambientLighting = SSGISampleProbeVolumePixel(positionWS, normalWS, viewDirectionWS, screenUV, probeOcclusion);
    return ambientLighting * probeOcclusion.rgb;
#else
    return SSGIEvaluateAmbientProbeSRGB(normalWS);
#endif
}

// Guards the division below. The result is bounded by the assumed albedo anyway, so this only avoids dividing by zero.
#define SSGI_IMPLIED_ALBEDO_MIN_AMBIENT 1e-4

// Most a guessed bounce may add to a pixel, as a multiple of the pixel's own colour. A surface lit by the ambient alone shows
// albedo * ambient, so a bounce of the same size adds about the pixel's colour again; anything far beyond that means the albedo
// guess or the traced hemisphere was wrong, and on a dark surface it reads as a blown-out card rather than as bounce light.
#define SSGI_FALLBACK_MAX_GAIN 1.0

// Albedo of a surface without GBuffer data, implied by its colour: a surface lit by the ambient alone shows albedo * ambient,
// so the ratio recovers the albedo. It is only an UPPER bound - a surface lit directly as well is brighter than the ambient
// alone could make it, and nothing here can separate the two - so it is capped by the assumed albedo rather than by 1. Capping
// at 1 handed every directly lit surface the full traced irradiance, which is what lit dark foliage up like paper.
// The cap also keeps albedo * ambient at or below the pixel's colour, so the removal never has to clamp at zero: removing and
// re-adding with the same albedo then leaves a pixel exactly unchanged when the traced bounce equals the ambient it replaces.
half3 SSGIImpliedAlbedo(half3 color, half3 ambientLighting)
{
    half3 implied = color * rcp(max(ambientLighting, half3(SSGI_IMPLIED_ALBEDO_MIN_AMBIENT, SSGI_IMPLIED_ALBEDO_MIN_AMBIENT, SSGI_IMPLIED_ALBEDO_MIN_AMBIENT)));
    return min(implied, half3(_SSGIFallbackAlbedo, _SSGIFallbackAlbedo, _SSGIFallbackAlbedo));
}

// Bounds what a guessed albedo can do to a pixel. GBuffer surfaces carry a real albedo and are left alone; fallback surfaces
// only ever had an estimate, so the light they gain is limited relative to the light they already show. Identity is unaffected:
// when the bounce equals the ambient it replaces the contribution is at most the pixel's colour, which is below the cap.
half3 SSGIClampFallbackContribution(half3 giContribution, half3 color, bool hasGBuffer)
{
    UNITY_BRANCH
    if (hasGBuffer)
        return giContribution;

    return min(giContribution, color * SSGI_FALLBACK_MAX_GAIN);
}

// Albedo and metallic of the surface at the pixel: from the GBuffer, or implied by the pixel colour for surfaces without one.
void SSGISampleAlbedoMetallic(float2 screenUV, bool hasGBuffer, half3 color, half3 ambientLighting, out half3 albedo, out half metallic)
{
    half4 gbuffer0 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer0, my_point_clamp_sampler, screenUV, 0);
    half4 gbuffer1 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, my_point_clamp_sampler, screenUV, 0);
    albedo = gbuffer0.rgb;
    metallic = (gbuffer0.a == kMaterialFlagSpecularSetup) ? MetallicFromReflectivity(ReflectivitySpecular(gbuffer1.rgb)) : gbuffer1.r;

    UNITY_BRANCH
    if (!hasGBuffer && _SSGIGBufferFallback != 0.0)
    {
        albedo = SSGIImpliedAlbedo(color, ambientLighting);
        metallic = 0.0;
    }
}

// Fraction of the camera colour that is not ambient light, per channel: the combine pass multiplies the camera target by it
// before the traced bounce is added in place of the ambient. Pixels lit by little more than ambient hold only precision noise
// after the subtraction, so they are treated as pure ambient; the threshold is relative to the pixel so dim (night) scenes keep
// their direct light instead of losing everything below a fixed luminance.
half3 SSGIAmbientRemovalFactor(half3 color, half3 ambientLighting, half3 albedo, half metallic)
{
    half3 removed = max(color - ambientLighting * albedo * (1.0 - metallic), half3(0.0, 0.0, 0.0));
    half luminanceFactor = saturate(Luminance(removed) * rcp(max(Luminance(color) * 0.04, 1e-4)));
    removed = lerp(half3(0.0, 0.0, 0.0), removed, luminanceFactor);
    return saturate(removed / max(color, half3(1e-4, 1e-4, 1e-4)));
}

#endif