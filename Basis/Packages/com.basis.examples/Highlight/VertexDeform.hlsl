#ifndef BASIS_VERTEX_DEFORM_INCLUDED
#define BASIS_VERTEX_DEFORM_INCLUDED

// Shared vertex deformation used by BOTH the visible material and its highlight
// mask. Keeping the math in one place guarantees the mask silhouette displaces
// identically to the rendered geometry, so the highlight outline lines up.
//
// positionOS / normalOS are object space. amount = amplitude, frequency =
// spatial frequency along object Y, speed = animation rate. Drive both materials
// with the same property values and the deform is identical.
//
// Requires _Time, which URP's Core.hlsl declares; include Core.hlsl before this.
float3 BasisApplyVertexDeform(float3 positionOS, float3 normalOS, float amount, float frequency, float speed)
{
    float wave = sin(positionOS.y * frequency + _Time.y * speed);
    return positionOS + normalOS * (wave * amount);
}

#endif
