// Internal conversion shader for BasisI420FrameRenderer. Not intended for use
// on a user material directly — BasisI420FrameRenderer blits through this into
// an internal sRGB RenderTexture, then binds that RenderTexture to the user's
// chosen material slot (typically _BaseMap).
//
// Output is BT.709 limited-range YCbCr decoded to full-range sRGB. In linear
// color-space projects the result is gamma-decoded once before write so that
// the surrounding sRGB RenderTexture re-encodes correctly and downstream
// shaders see the right colors after auto-decode.
//
// V is flipped here because libvpx hands rows top-down while Unity uploads
// SetPixelData bottom-up.
Shader "Basis/VideoPlayer/Yuv420ToRgb"
{
    Properties
    {
        _YPlane ("Y", 2D) = "black" {}
        _UPlane ("U", 2D) = "gray"  {}
        _VPlane ("V", 2D) = "gray"  {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Yuv420ToRgbBlit"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_YPlane); SAMPLER(sampler_YPlane);
            TEXTURE2D(_UPlane); SAMPLER(sampler_UPlane);
            TEXTURE2D(_VPlane); SAMPLER(sampler_VPlane);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = float2(IN.uv.x, 1.0 - IN.uv.y);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float y = SAMPLE_TEXTURE2D(_YPlane, sampler_YPlane, IN.uv).r;
                float u = SAMPLE_TEXTURE2D(_UPlane, sampler_UPlane, IN.uv).r - 0.5;
                float v = SAMPLE_TEXTURE2D(_VPlane, sampler_VPlane, IN.uv).r - 0.5;

                // BT.709, limited range (16..235 Y, 16..240 UV) -> full-range sRGB-encoded RGB.
                float yLin = (y - 16.0 / 255.0) * 1.16438356;
                float r = yLin + 1.79274107 * v;
                float g = yLin - 0.21324861 * u - 0.53290933 * v;
                float b = yLin + 2.11240179 * u;

                float3 srgb = saturate(float3(r, g, b));

            #if defined(UNITY_COLORSPACE_GAMMA)
                // Gamma project: shader output is treated as gamma already; pass through.
                return half4(srgb, 1.0);
            #else
                // Linear project: write linear so the sRGB RenderTexture re-encodes once
                // and downstream sampling auto-decodes back to linear correctly.
                return half4(SRGBToLinear(srgb), 1.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
