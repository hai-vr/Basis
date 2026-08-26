#ifndef BASIS_RTAO_KERNEL_INCLUDED
#define BASIS_RTAO_KERNEL_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/UnifiedRayTracing/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/UnifiedRayTracing/TraceRayAndQueryHit.hlsl"
#include "Packages/com.basis.rtao/Shaders/BasisRTAOCommon.hlsl"

UNIFIED_RT_DECLARE_ACCEL_STRUCT(_BasisRtaoAccel);

Texture2DArray<float4> _BasisRtaoPositionTex;
Texture2DArray<float4> _BasisRtaoNormalTex;
RWTexture2DArray<float2> _BasisRtaoResultTex;

float4 _BasisRtaoReference;
float4 _BasisRtaoTrace;
float4 _BasisRtaoBias;
float4 _BasisRtaoSize;
int _BasisRtaoRayCount;
int _BasisRtaoViewCount;
int _BasisRtaoFrameIndex;
int _BasisRtaoStereoCoherent;

void RayGenExecute(UnifiedRT::DispatchInfo dispatchInfo)
{
    uint3 id = dispatchInfo.dispatchThreadID;
    if (id.x >= (uint)_BasisRtaoSize.x || id.y >= (uint)_BasisRtaoSize.y || id.z >= (uint)_BasisRtaoViewCount)
        return;

    float4 packed = _BasisRtaoPositionTex.Load(int4(id.xy, id.z, 0));
    if (packed.w < 0.5)
    {
        _BasisRtaoResultTex[id] = float2(1.0, 1.0);
        return;
    }

    float3 positionWS = packed.xyz + _BasisRtaoReference.xyz;
    float3 normalWS = BasisRtaoDecodeNormal(_BasisRtaoNormalTex.Load(int4(id.xy, id.z, 0)).xy);

    float viewDistance = length(packed.xyz);

    uint seed;
    if (_BasisRtaoStereoCoherent != 0)
    {
        // A fixed world grid is many pixels across up close and sub pixel far away, so it reads as blocky
        // patches on anything near the camera that quietly vanish with distance. Scaling the cell with view
        // distance keeps it roughly one screen pixel everywhere, and both eyes still see the same distance
        // for the same surface point, so they still agree on the seed.
        float noiseCell = max(1e-5, _BasisRtaoBias.z * max(0.05, viewDistance));
        int3 cell = int3(floor(positionWS / noiseCell));
        seed = BasisRtaoHashCell(cell, (uint)_BasisRtaoFrameIndex);
    }
    else
    {
        seed = BasisRtaoHash(id.x * 1973u + id.y * 9277u + id.z * 26699u + (uint)_BasisRtaoFrameIndex * 6151u);
    }
    float2 jitter = float2(BasisRtaoUnitFloat(seed), BasisRtaoUnitFloat(BasisRtaoHash(seed ^ 0x9e3779b9u)));

    // The distance term exists to clear the half float precision of the stored position, which is about
    // d/2048, so it has to grow with distance. But it must never grow into the search itself: at a 10 cm
    // radius an uncapped bias starts the ray a full radius above the surface by ~60 m out, and the occlusion
    // simply stops at a line. Cap it so three quarters of the radius always survives.
    float originBias = min(_BasisRtaoBias.x + _BasisRtaoBias.y * viewDistance, _BasisRtaoTrace.y * 0.25);

    UnifiedRT::RayTracingAccelStruct accelStruct = UNIFIED_RT_GET_ACCEL_STRUCT(_BasisRtaoAccel);

    UnifiedRT::Ray ray;
    ray.origin = OffsetRayOrigin(positionWS, normalWS, originBias);
    ray.tMin = 0.0;
    ray.tMax = _BasisRtaoTrace.y;

    uint rayCount = (uint)max(1, _BasisRtaoRayCount);
    float visibility = 0.0;
    float distanceSum = 0.0;

    if (_BasisRtaoTrace.z > 0.0)
    {
        for (uint i = 0; i < rayCount; ++i)
        {
            ray.direction = BasisRtaoCosineHemisphere(BasisRtaoHammersley(i, rayCount, jitter), normalWS);
            UnifiedRT::Hit hit = UnifiedRT::TraceRayClosestHit(dispatchInfo, accelStruct, 0xffffffff, ray, 0);
            if (hit.IsValid())
            {
                float normalized = saturate(hit.hitDistance / _BasisRtaoTrace.y);
                visibility += pow(normalized, _BasisRtaoTrace.z);
                distanceSum += normalized;
            }
            else
            {
                visibility += 1.0;
                distanceSum += 1.0;
            }
        }
    }
    else
    {
        for (uint i = 0; i < rayCount; ++i)
        {
            ray.direction = BasisRtaoCosineHemisphere(BasisRtaoHammersley(i, rayCount, jitter), normalWS);
            bool occluded = UnifiedRT::TraceRayAnyHit(dispatchInfo, accelStruct, 0xffffffff, ray, 0);
            visibility += occluded ? 0.0 : 1.0;
            distanceSum += occluded ? 0.0 : 1.0;
        }
    }

    float rcpCount = 1.0 / float(rayCount);
    _BasisRtaoResultTex[id] = float2(saturate(visibility * rcpCount), saturate(distanceSum * rcpCount));
}

#endif
