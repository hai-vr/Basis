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

// Comment out a define below to compile out that integration if its package is removed.
#define VF_LTCGI
#define VF_VRSL

#if defined(VF_LTCGI)
    #define LTCGI_ALWAYS_LTC_DIFFUSE
    #define LTCGI_SPECULAR_OFF
    #include "Packages/at.pimaker.ltcgi/Shaders/LTCGI_URP_Compat.hlsl"
    #include "Packages/at.pimaker.ltcgi/Shaders/LTCGI.cginc"
#endif

#if defined(VF_VRSL)
    #include "Packages/net.towneh.vrsl-urp/Runtime/Shaders/Shared/VRSLLightingLibrary.hlsl"
    StructuredBuffer<VRSLLightData> _VRSLLights;
    uint _VRSLLightCount;
#endif

int _FrameCount;
uint _CustomAdditionalLightsCount;
float _Distance;
float _BaseHeight;
float _MaximumHeight;
float _GroundHeight;
float _Density;
float _Absortion;
float _APVContributionWeight;
float3 _Tint;
int _MaxSteps;

float _Anisotropies[MAX_VISIBLE_LIGHTS + 1];
float _Scatterings[MAX_VISIBLE_LIGHTS + 1];
float _RadiiSq[MAX_VISIBLE_LIGHTS];

float _LTCGIScattering;
float _VRSLScattering;
float _VRSLAnisotropy;
float _VRSLSpotConeSharpness;
float _VRSLPointConeSharpness;
float _VRSLSourceDistance;
float3 _VRSLPointBeamAxis;

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

// Gets the main light phase function.
float GetMainLightPhase(float3 rd)
{
#if _MAIN_LIGHT_CONTRIBUTION_DISABLED
    return 0.0;
#else
    return CornetteShanksPhaseFunction(_Anisotropies[_CustomAdditionalLightsCount], dot(rd, GetMainLight().direction));
#endif
}

// Gets the fog density at the given world height.
float GetFogDensity(float posWSy)
{
    float t = saturate((posWSy - _BaseHeight) / (_MaximumHeight - _BaseHeight));
    t = 1.0 - t;
    t = lerp(t, 0.0, posWSy < _GroundHeight);

    return _Density * t;
}

// Gets the GI evaluation from the adaptive probe volume at one raymarch step.
float3 GetStepAdaptiveProbeVolumeEvaluation(float2 uv, float3 posWS, float density)
{
    float3 apvDiffuseGI = float3(0.0, 0.0, 0.0);
    
#if UNITY_VERSION >= 202310 && _APV_CONTRIBUTION_ENABLED
    #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
        EvaluateAdaptiveProbeVolume(posWS, uv * _ScreenSize.xy, apvDiffuseGI);
        apvDiffuseGI = apvDiffuseGI * _APVContributionWeight * density;
    #endif
#endif
 
    return apvDiffuseGI;
}

// Gets the main light color at one raymarch step.
float3 GetStepMainLightColor(float3 currPosWS, float phaseMainLight, float density)
{
#if _MAIN_LIGHT_CONTRIBUTION_DISABLED
    return float3(0.0, 0.0, 0.0);
#endif
    Light mainLight = GetMainLight();
    float4 shadowCoord = TransformWorldToShadowCoord(currPosWS);
    mainLight.shadowAttenuation = VolumetricMainLightRealtimeShadow(shadowCoord);
#if _LIGHT_COOKIES
    mainLight.color *= SampleMainLightCookie(currPosWS);
#endif
    return (mainLight.color * _Tint) * (mainLight.shadowAttenuation * phaseMainLight * density * _Scatterings[_CustomAdditionalLightsCount]);
}

// Gets the accumulated color from additional lights at one raymarch step.
float3 GetStepAdditionalLightsColor(float2 uv, float3 currPosWS, float3 rd, float density)
{
#if _ADDITIONAL_LIGHTS_CONTRIBUTION_DISABLED
    return float3(0.0, 0.0, 0.0);
#endif
#if _CLUSTER_LIGHT_LOOP
    // Forward+ rendering path needs this data before the light loop.
    InputData inputData = (InputData)0;
    inputData.normalizedScreenSpaceUV = uv;
    inputData.positionWS = currPosWS;
#endif
    float3 additionalLightsColor = float3(0.0, 0.0, 0.0);
                
    // Loop differently through lights in Forward+ while considering Forward and Deferred too.
    LIGHT_LOOP_BEGIN(_CustomAdditionalLightsCount)
        // Read the per-light fog params once through a clamped index. Indexing these uniform arrays
        // with the raw loop index makes FXC mis-track the relative index in the Forward+ build
        // variants -> error X8000 "relative index temp register uninitialized" (issue #25).
        uint fogIndex = min(lightIndex, (uint)(MAX_VISIBLE_LIGHTS - 1));
        float fogScattering = _Scatterings[fogIndex];
        float fogRadiusSq = _RadiiSq[fogIndex];
        float fogAnisotropy = _Anisotropies[fogIndex];

        UNITY_BRANCH
        if (fogScattering <= 0.0)
            continue;

        Light additionalLight = GetAdditionalPerObjectLight(lightIndex, currPosWS);
        // Fog never samples additional-light shadows (the _ADDITIONAL_LIGHT_SHADOWS variant is not
        // compiled - it overflows FXC under _CLUSTER_LIGHT_LOOP, issue #25). Lights stay unshadowed.
        additionalLight.shadowAttenuation = 1.0;
#if _LIGHT_COOKIES
        additionalLight.color *= SampleAdditionalLightCookie(lightIndex, currPosWS);
#endif
        // See universal\ShaderLibrary\RealtimeLights.hlsl - GetAdditionalPerObjectLight.
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        float4 additionalLightPos = _AdditionalLightsBuffer[lightIndex].position;
#else
        float4 additionalLightPos = _AdditionalLightsPosition[lightIndex];
#endif
        // This is useful for both spotlights and pointlights. For the latter it is specially true when the point light is inside some geometry and casts shadows.
        // Gradually reduce additional lights scattering to zero at their origin to try to avoid flicker-aliasing.
        float3 distToPos = additionalLightPos.xyz - currPosWS;
        float distToPosMagnitudeSq = dot(distToPos, distToPos);
        float newScattering = smoothstep(0.0, fogRadiusSq, distToPosMagnitudeSq) ;
        newScattering *= newScattering;
        newScattering *= fogScattering;

        // If directional lights are also considered as additional lights when more than 1 is used, ignore the previous code when it is a directional light.
        // They store direction in additionalLightPos.xyz and have .w set to 0, while point and spotlights have it set to 1.
        // newScattering = lerp(1.0, newScattering, additionalLightPos.w);
    
        float phase = CornetteShanksPhaseFunction(fogAnisotropy, dot(rd, additionalLight.direction));
        additionalLightsColor += (additionalLight.color * (additionalLight.shadowAttenuation * additionalLight.distanceAttenuation * phase * density * newScattering));
    LIGHT_LOOP_END

    return additionalLightsColor;
}

// Gets the accumulated color from LTCGI area lights (screens, video) at one raymarch step.
float3 GetStepLTCGIColor(float3 currPosWS, float3 viewToCam, float density)
{
#if defined(VF_LTCGI)
    UNITY_BRANCH
    if (_LTCGIScattering <= 0.0)
        return float3(0.0, 0.0, 0.0);

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
    return float3(0.0, 0.0, 0.0);
#endif
}

// Gets the accumulated color from VRSL DMX/AudioLink stage lights at one raymarch step.
float3 GetStepVRSLColor(float3 currPosWS, float3 viewToCam, float density)
{
#if defined(VF_VRSL)
    UNITY_BRANCH
    if (_VRSLScattering <= 0.0)
        return float3(0.0, 0.0, 0.0);

    float3 vrslColor = float3(0.0, 0.0, 0.0);
    UNITY_LOOP
    for (uint i = 0; i < _VRSLLightCount; ++i)
    {
        VRSLLightData light = _VRSLLights[i];
        if (light.spotCosines.z < 0.5)
            continue;

        float3 toLight = light.positionAndRange.xyz - currPosWS;
        float distSq = dot(toLight, toLight);
        float range = light.positionAndRange.w;
        if (distSq > range * range)
            continue;

        // Distance falloff with a softened source so the fixture isn't a dense hotspot;
        // _VRSLSourceDistance pushes the falloff origin out from the source for a longer beam.
        float rangeRcp = 1.0 / max(range, 0.0001);
        float d2 = distSq * rangeRcp * rangeRcp;
        float f = saturate(1.0 - d2 * d2);
        float distAtten = (f * f) / (distSq + _VRSLSourceDistance * _VRSLSourceDistance + 0.0001);

        float3 toLightN = toLight * rsqrt(max(distSq, 0.0001));

        // Beam shaping per fixture type. Spots tighten their real cone; point fixtures have no
        // cone in VRSL's data, so synthesize a forward cone along the fixture's aim direction.
        float beam;
        if (light.directionAndType.w < 0.5)
        {
            beam = pow(saturate(VRSL_SpotAttenuation(light.directionAndType.xyz, toLight,
                light.spotCosines.x, light.spotCosines.y, light.spotCosines.w)), _VRSLSpotConeSharpness);
        }
        else
        {
            // Point fixtures have no usable per-fixture aim in VRSL's data, so they stay an
            // omnidirectional glow unless the user sets a fixed world beam axis (e.g. (0,-1,0)
            // to aim all point beams down); zero axis = omnidirectional.
            float axisLen = length(_VRSLPointBeamAxis);
            beam = axisLen > 0.0001
                ? pow(saturate(dot(_VRSLPointBeamAxis / axisLen, -toLightN)), _VRSLPointConeSharpness)
                : 1.0;
        }

        float phase = VRSL_HenyeyGreenstein(dot(viewToCam, toLightN), _VRSLAnisotropy);

        // VRSL_SpotAttenuation / normalize(dir) can NaN at a fixture apex or zero aim; flush it.
        float3 c = light.colorAndIntensity.xyz * light.colorAndIntensity.w * (distAtten * beam * phase);
        vrslColor += all(c == c) ? c : float3(0.0, 0.0, 0.0);
    }

    return vrslColor * (_VRSLScattering * density);
#else
    return float3(0.0, 0.0, 0.0);
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

    offsetLength -= iniOffsetToNearPlane;
    float3 roNearPlane = ro + rd * iniOffsetToNearPlane;
    float stepLength = (_Distance - iniOffsetToNearPlane) / (float)_MaxSteps;
    float jitter = stepLength * InterleavedGradientNoise(positionCS, _FrameCount);

    float phaseMainLight = GetMainLightPhase(rdPhase);
    float minusStepLengthTimesAbsortion = -stepLength * _Absortion;
                
    float3 volumetricFogColor = float3(0.0, 0.0, 0.0);
    float transmittance = 1.0;

    UNITY_LOOP
    for (int i = 0; i < _MaxSteps; ++i)
    {
        float dist = jitter + i * stepLength;
        
        UNITY_BRANCH
        if (dist >= offsetLength)
            break;

        // We are making the space between the camera position and the near plane "non existant", as if fog did not exist there.
        // However, it removes a lot of noise when in closed environments with an attenuation that makes the scene darker
        // and certain combinations of field of view, raymarching resolution and camera near plane.
        // In those edge cases, it looks so much better, specially when near plane is higher than the minimum (0.01) allowed.
        float3 currPosWS = roNearPlane + rd * dist;
        float density = GetFogDensity(currPosWS.y);
                    
        UNITY_BRANCH
        if (density <= 0.0)
            continue;

        float stepAttenuation = exp(minusStepLengthTimesAbsortion * density);
        transmittance *= stepAttenuation;

        float3 apvColor = GetStepAdaptiveProbeVolumeEvaluation(uv, currPosWS, density);
        float3 mainLightColor = GetStepMainLightColor(currPosWS, phaseMainLight, density);
        float3 ltcgiColor = GetStepLTCGIColor(currPosWS, -rd, density);
        float3 vrslColor = GetStepVRSLColor(currPosWS, -rd, density);
        
        // TODO: Additional contributions? Reflection probes, etc...
        float3 stepColor = apvColor + mainLightColor + ltcgiColor + vrslColor;
        volumetricFogColor += (stepColor * (transmittance * stepLength));
        
        // TODO: Break out when transmittance reaches low threshold and remap the transmittance when doing so.
        // It does not make sense right now because the fog does not properly support transparency, so having dense fog leads to issues.
    }

    return float4(volumetricFogColor, transmittance);
}

#endif
