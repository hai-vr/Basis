#ifndef BASIS_RTAO_COMMON_INCLUDED
#define BASIS_RTAO_COMMON_INCLUDED

float2 BasisRtaoOctWrap(float2 v)
{
    return (1.0 - abs(v.yx)) * float2(v.x >= 0.0 ? 1.0 : -1.0, v.y >= 0.0 ? 1.0 : -1.0);
}

float2 BasisRtaoEncodeNormal(float3 n)
{
    n /= max(1e-6, abs(n.x) + abs(n.y) + abs(n.z));
    n.xy = n.z >= 0.0 ? n.xy : BasisRtaoOctWrap(n.xy);
    return n.xy;
}

float3 BasisRtaoDecodeNormal(float2 f)
{
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = saturate(-n.z);
    n.xy += float2(n.x >= 0.0 ? -t : t, n.y >= 0.0 ? -t : t);
    return normalize(n);
}

uint BasisRtaoHash(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

uint BasisRtaoHashCell(int3 cell, uint seed)
{
    uint h = uint(cell.x) * 73856093u ^ uint(cell.y) * 19349663u ^ uint(cell.z) * 83492791u;
    return BasisRtaoHash(h ^ BasisRtaoHash(seed));
}

float BasisRtaoUnitFloat(uint u)
{
    return float(u & 0x00ffffffu) * (1.0 / 16777216.0);
}

float2 BasisRtaoHammersley(uint index, uint count, float2 offset)
{
    uint bits = index;
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xaaaaaaaau) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xccccccccu) >> 2u);
    bits = ((bits & 0x0f0f0f0fu) << 4u) | ((bits & 0xf0f0f0f0u) >> 4u);
    bits = ((bits & 0x00ff00ffu) << 8u) | ((bits & 0xff00ff00u) >> 8u);
    return frac(float2(float(index) / float(count), float(bits) * 2.3283064365386963e-10) + offset);
}

float3 BasisRtaoCosineHemisphere(float2 u, float3 normalWS)
{
    float z = 1.0 - 2.0 * u.x;
    z = clamp(z, -0.99999, 0.99999);
    float r = sqrt(1.0 - z * z);
    float phi = 6.2831853071795864 * u.y;
    float3 sphere = float3(r * cos(phi), r * sin(phi), z);
    float3 dir = normalWS + sphere;
    float lenSq = dot(dir, dir);
    return lenSq < 1e-6 ? normalWS : dir * rsqrt(lenSq);
}

float2 BasisRtaoProjectToScreenUV(float4x4 viewProj, float3 positionWS, out float clipW)
{
    float4 clip = mul(viewProj, float4(positionWS, 1.0));
    clipW = clip.w;
    return clip.xy / max(1e-6, clip.w) * 0.5 + 0.5;
}

float BasisRtaoViewDepth(float3 positionWS, float3 referencePositionWS, float3 viewForwardWS)
{
    return dot(positionWS - referencePositionWS, viewForwardWS);
}

#endif
