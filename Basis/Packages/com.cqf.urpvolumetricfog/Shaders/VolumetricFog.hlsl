#ifndef VOLUMETRIC_FOG_INCLUDED
#define VOLUMETRIC_FOG_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#if UNITY_VERSION >= 202310 && _APV_CONTRIBUTION_ENABLED
    #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
        #include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"
    #endif
#endif
#include "./DeclareDownsampledDepthTexture.hlsl"
#include "./VolumetricShadows.hlsl"
#include "./ProjectionUtils.hlsl"

// Optional LTCGI (area lights: screens, video) integration. Off by default so the fog compiles
// standalone when the LTCGI package is absent. To enable it, install the LTCGI package
// (at.pimaker.ltcgi) and uncomment the define below.
// #define VF_LTCGI

#if defined(VF_LTCGI)
    #define LTCGI_ALWAYS_LTC_DIFFUSE
    #define LTCGI_SPECULAR_OFF
    #include "Packages/at.pimaker.ltcgi/Shaders/LTCGI_URP_Compat.hlsl"
    #include "Packages/at.pimaker.ltcgi/Shaders/LTCGI.cginc"
#endif

// Stop marching once the medium is dense enough that the remaining steps are imperceptible.
#define VF_MIN_TRANSMITTANCE 0.003
// Distance-adaptive stepping: ratio of the last (far) to the first (near) step length. Steps are
// distributed geometrically so samples concentrate near the camera, where detail matters most.
#define VF_STEP_GROWTH_RATIO 8.0
// Skip steps whose density falls below this fraction of the configured maximum; the faint top-of-band
// sliver they cover is imperceptible but still pays for shadow, APV and LTCGI sampling.
#define VF_MIN_DENSITY_FRACTION 0.01

float _Distance;
float _BaseHeight;
float _MaximumHeight;
float _GroundHeight;
float _Density;
float _Absortion;
float _APVContributionWeight;
float3 _Tint;
int _MaxSteps;

float _MainLightAnisotropy;
float _MainLightScattering;

#if defined(VF_LTCGI)
float _LTCGIScattering;
#endif

// Blue-noise texture for raymarch jitter.
TEXTURE2D(_BlueNoiseTexture);
float4 _BlueNoiseParams; // xy = 1 / textureSize, zw = per-frame scroll offset

// Computes the ray origin, direction, and returns the reconstructed world position for orthographic projection.
float3 ComputeOrthographicParams(float2 uv, float depth, out float3 ro, out float3 rd)
{
    float4x4 viewMatrix = UNITY_MATRIX_V;
    float2 ndc = uv * 2.0 - 1.0;

    rd = normalize(-viewMatrix[2].xyz);
    float3 rightOffset = normalize(viewMatrix[0].xyz) * (ndc.x * unity_OrthoParams.x);
    float3 upOffset = normalize(viewMatrix[1].xyz) * (ndc.y * unity_OrthoParams.y);
    float3 fwdOffset = rd * depth;

    float3 posWs = GetCameraPositionWS() + fwdOffset + rightOffset + upOffset;
    ro = posWs - fwdOffset;

    return posWs;
}

// Calculates the initial raymarching parameters.
void CalculateRaymarchingParams(float2 uv, out float3 ro, out float3 rd, out float iniOffsetToNearPlane, out float offsetLength, out float3 rdPhase)
{
    float depth = SampleDownsampledSceneDepth(uv);
    float3 posWS;

    UNITY_BRANCH
    if (unity_OrthoParams.w <= 0)
    {
        ro = GetCameraPositionWS();
#if !UNITY_REVERSED_Z
        depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
#endif
        posWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
        float3 offset = posWS - ro;
        offsetLength = length(offset);
        rd = offset / offsetLength;
        rdPhase = rd;

        // In perspective, ray direction should vary in length depending on which fragment we are at.
        float3 camFwd = normalize(-UNITY_MATRIX_V[2].xyz);
        float cos = dot(camFwd, rd);
        float fragElongation = 1.0 / cos;
        iniOffsetToNearPlane = fragElongation * _ProjectionParams.y;
    }
    else
    {
        depth = LinearEyeDepthOrthographic(depth);
        posWS = ComputeOrthographicParams(uv, depth, ro, rd);
        offsetLength = depth;

        // Fake the ray direction that will be used to calculate the phase, so we can still use anisotropy in orthographic mode.
        rdPhase = normalize(posWS - GetCameraPositionWS());
        iniOffsetToNearPlane = _ProjectionParams.y;
    }
}

// Per-pixel raymarch start jitter from a blue-noise texture (better-distributed than interleaved
// gradient noise, so banding is less visible at the same step count).
float GetRaymarchJitter(float2 positionCS)
{
    float2 uv = positionCS * _BlueNoiseParams.xy + _BlueNoiseParams.zw;
    return SAMPLE_TEXTURE2D_LOD(_BlueNoiseTexture, sampler_PointRepeat, uv, 0.0).r;
}

// Gets the fog density at the given world height.
float GetFogDensity(float posWSy)
{
    float t = saturate((posWSy - _BaseHeight) / (_MaximumHeight - _BaseHeight));
    t = 1.0 - t;
    t = lerp(t, 0.0, posWSy < _GroundHeight);

    return _Density * t;
}

// Baked APV in-scatter volume: a world-space 3D texture of pre-integrated APV irradiance (RGB). When
// _APV_BAKED is set, the per-step APV evaluation becomes a single trilinear tap into this texture
// instead of a full SH reconstruction - and it needs no live APV (PROBE_VOLUMES) data, so it works
// even with Unity's APV runtime disabled.
TEXTURE3D(_BakedAPVFogVolume);
float3 _BakedAPVVolumeBoundsMin;   // world-space min corner of the baked volume
float3 _BakedAPVVolumeInvSize;     // 1 / world-space size; maps a world position into [0,1] uvw

// Evaluates the adaptive probe volume irradiance (weighted, WITHOUT density) at a world position.
// real is half on mobile (e.g. Adreno / Steam Frame) and float on desktop. The caller multiplies by
// the step density. Called once per march step - APV is the fog's lighting and varies along the ray,
// so it can't be skipped/held without artifacts; reduce cost via the volume's Max Steps instead (or
// switch to the baked mode below, where each step is a cheap trilinear tap).
real3 EvaluateWeightedAPV(float2 uv, float3 posWS)
{
    float3 apvDiffuseGI = float3(0.0, 0.0, 0.0);

#if _APV_CONTRIBUTION_ENABLED
    #if _APV_BAKED
        // Static path: sample the pre-baked world-space in-scatter volume. sampler_LinearClamp pins
        // out-of-bounds samples to the edge voxel instead of wrapping. RGB = APV irradiance, A = probe
        // coverage (1 inside valid APV data, 0 outside) - multiplying by it fades the baked ambient out
        // of uncovered space instead of showing APV's flat ambient fallback there.
        float3 uvw = (posWS - _BakedAPVVolumeBoundsMin) * _BakedAPVVolumeInvSize;
        float4 baked = SAMPLE_TEXTURE3D_LOD(_BakedAPVFogVolume, sampler_LinearClamp, uvw, 0.0);
        apvDiffuseGI = baked.rgb * (baked.a * _APVContributionWeight);
    #elif UNITY_VERSION >= 202310
        // Live path: evaluate Unity's APV once per step (dynamic, most expensive).
        #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
            EvaluateAdaptiveProbeVolume(posWS, uv * _ScreenSize.xy, apvDiffuseGI);
            apvDiffuseGI *= _APVContributionWeight;
        #endif
    #endif
#endif

    return apvDiffuseGI;
}

// Gets the main light color at one raymarch step, using the per-ray hoisted constant term
// (light colour * tint * phase * scattering). Only shadow, cookie and density vary per step.
real3 GetStepMainLightColor(float3 currPosWS, real3 mainLightConst, float density)
{
#if _MAIN_LIGHT_CONTRIBUTION_DISABLED
    return real3(0.0, 0.0, 0.0);
#else
    float4 shadowCoord = TransformWorldToShadowCoord(currPosWS);
    real shadow = VolumetricMainLightRealtimeShadow(shadowCoord);
    real3 color = mainLightConst * (shadow * density);
#if _LIGHT_COOKIES
    color *= SampleMainLightCookie(currPosWS);
#endif
    return color;
#endif
}

// Gets the accumulated color from LTCGI area lights (screens, video) at one raymarch step.
real3 GetStepLTCGIColor(float3 currPosWS, float3 viewToCam, float density)
{
#if defined(VF_LTCGI)
    UNITY_BRANCH
    if (_LTCGIScattering <= 0.0)
        return real3(0.0, 0.0, 0.0);

    // Fog has no surface normal, so a single normal hard-clips every screen in the opposite
    // hemisphere to zero (the "only lit when looking away from the screen" artifact). Evaluate
    // both hemispheres around the view ray and sum so screens light the fog from any view angle.
    // The view dir handed to LTCGI must be perpendicular to the normal or its tangent basis is
    // normalize(0) = NaN.
    float3 ltcgiN = -viewToCam;
    float3 ltcgiUp = abs(ltcgiN.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    float3 ltcgiView = normalize(cross(ltcgiUp, ltcgiN));

    half3 diffuse = half3(0.0, 0.0, 0.0);
    LTCGI_Contribution(currPosWS,  ltcgiN, ltcgiView, 1.0, float2(0.0, 0.0), diffuse);
    LTCGI_Contribution(currPosWS, -ltcgiN, ltcgiView, 1.0, float2(0.0, 0.0), diffuse);
    return diffuse * (_LTCGIScattering * density);
#else
    return real3(0.0, 0.0, 0.0);
#endif
}

// Calculates the volumetric fog. Returns the color in the RGB channels and transmittance in alpha.
float4 VolumetricFog(float2 uv, float2 positionCS)
{
    float3 ro;
    float3 rd;
    float iniOffsetToNearPlane;
    float offsetLength;
    float3 rdPhase;

    CalculateRaymarchingParams(uv, ro, rd, iniOffsetToNearPlane, offsetLength, rdPhase);

    // We treat the space between the camera and the near plane as if no fog existed there. It removes a
    // lot of noise in closed environments with darkening attenuation and certain near plane / fov / density
    // combinations, and looks much better when the near plane is above the minimum allowed.
    float3 roNearPlane = ro + rd * iniOffsetToNearPlane;
    float marchEnd = min(offsetLength, _Distance) - iniOffsetToNearPlane;

    // Empty-space skipping: density is only non-zero inside the world-Y band [_GroundHeight, _MaximumHeight].
    // Clip the march to where the ray actually crosses that band so no steps are wasted above or below it.
    float tEnter = 0.0;
    float tExit = marchEnd;

    UNITY_BRANCH
    if (abs(rd.y) > 1e-6)
    {
        float invRdy = 1.0 / rd.y;
        float tBandA = (_MaximumHeight - roNearPlane.y) * invRdy;
        float tBandB = (_GroundHeight - roNearPlane.y) * invRdy;
        tEnter = max(tEnter, min(tBandA, tBandB));
        tExit = min(tExit, max(tBandA, tBandB));
    }
    else if (roNearPlane.y > _MaximumHeight || roNearPlane.y < _GroundHeight)
    {
        tExit = tEnter; // Horizontal ray entirely outside the fog band.
    }

    UNITY_BRANCH
    if (tExit <= tEnter)
        return float4(0.0, 0.0, 0.0, 1.0);

    float marchLength = tExit - tEnter;

    // Cap the iteration count to the actual (slab-clipped) march length so the sample density (steps
    // per world unit) stays constant instead of always spending all _MaxSteps. Short crossings then
    // use far fewer iterations - and shadow samples - for the same quality.
    float fogSpan = max(1e-4, _Distance - iniOffsetToNearPlane);
    int stepCount = (int)clamp(ceil((float)_MaxSteps * (marchLength / fogSpan)), 1.0, (float)_MaxSteps);

    // Geometric step distribution over [tEnter, tExit]: ds grows by 'growth' each step so the far/near
    // step length ratio is VF_STEP_GROWTH_RATIO, while the lengths still sum exactly to marchLength.
    float growth = pow(VF_STEP_GROWTH_RATIO, 1.0 / (float)stepCount);
    float ds = marchLength * (growth - 1.0) / (VF_STEP_GROWTH_RATIO - 1.0);
    float jitterFrac = GetRaymarchJitter(positionCS);

    // Hoist the per-ray main-light terms out of the loop; only shadow, cookie and density vary per step.
    real3 mainLightConst = real3(0.0, 0.0, 0.0);
    bool sampleMainLight = false;
#if !_MAIN_LIGHT_CONTRIBUTION_DISABLED
    Light mainLight = GetMainLight();
    real phaseMainLight = CornetteShanksPhaseFunction(_MainLightAnisotropy, dot(rdPhase, mainLight.direction));
    mainLightConst = (mainLight.color * _Tint) * (phaseMainLight * _MainLightScattering);
    // When the main light can't contribute (black sun, zero scattering/tint), its per-step shadow sample is wasted work.
    sampleMainLight = any(mainLightConst > 0.0);
#endif

    float minDensity = _Density * VF_MIN_DENSITY_FRACTION;
    float3 volumetricFogColor = float3(0.0, 0.0, 0.0);
    float transmittance = 1.0;
    float t = tEnter;

    UNITY_LOOP
    for (int i = 0; i < stepCount; ++i)
    {
        float tSample = t + ds * jitterFrac;
        float3 currPosWS = roNearPlane + rd * tSample;
        float density = GetFogDensity(currPosWS.y);

        UNITY_BRANCH
        if (density > minDensity)
        {
            transmittance *= exp(-ds * _Absortion * density);

            real3 apvColor = real3(0.0, 0.0, 0.0);
#if _APV_CONTRIBUTION_ENABLED
            apvColor = EvaluateWeightedAPV(uv, currPosWS) * density;
#endif
            real3 mainLightColor = real3(0.0, 0.0, 0.0);
            UNITY_BRANCH
            if (sampleMainLight)
                mainLightColor = GetStepMainLightColor(currPosWS, mainLightConst, density);

            real3 ltcgiColor = GetStepLTCGIColor(currPosWS, -rd, density);

            real3 stepColor = apvColor + mainLightColor + ltcgiColor;
            volumetricFogColor += (stepColor * (transmittance * ds));

            // Early-out once the medium is effectively opaque: further steps contribute ~0 and, since
            // transmittance is already near zero, the composite occludes the scene as dense fog should.
            UNITY_BRANCH
            if (transmittance < VF_MIN_TRANSMITTANCE)
                break;
        }

        t += ds;
        ds *= growth;
    }

    return float4(volumetricFogColor, transmittance);
}

#endif
