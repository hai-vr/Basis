Shader "Hidden/Basis/VrsMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "BasisVrsMask"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // xy = foveal center UV, z = inner radius, w = outer radius (all in aspect-corrected UV)
            float4 _BasisVrsCenter;
            float  _BasisVrsAspect;
            float4 _BasisVrsColorNear; // 1x1 (linear)
            float4 _BasisVrsColorMid;  // 2x2 (linear)
            float4 _BasisVrsColorFar;  // 4x4 (linear)

            half4 Frag(Varyings input) : SV_Target
            {
                float2 d = input.texcoord - _BasisVrsCenter.xy;
                d.x *= _BasisVrsAspect;
                float dist = length(d);

                float4 col = _BasisVrsColorFar;
                if (dist < _BasisVrsCenter.z)
                    col = _BasisVrsColorNear;
                else if (dist < _BasisVrsCenter.w)
                    col = _BasisVrsColorMid;

                return half4(col.rgb, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
