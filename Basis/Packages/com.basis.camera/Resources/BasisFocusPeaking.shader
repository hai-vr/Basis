Shader "Hidden/Basis/FocusPeaking"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _PeakColor ("Peak Colour", Color) = (1, 0, 0, 1)
        _PeakThreshold ("Threshold", Float) = 0.12
        _PeakDesaturate ("Desaturate Base", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "BasisFocusPeaking"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            float4 _PeakColor;
            float _PeakThreshold;
            float _PeakDesaturate;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // The gradient is taken on a display-referred signal, not the linear values the render
            // texture holds: a linear luma puts almost all of its range in the highlights, so an
            // edge in a dark part of the frame reads as a fraction of the same edge in a bright
            // one and only lit subjects would ever peak. sqrt is the cheap stand-in for the sRGB
            // curve and is close enough for a threshold.
            float Detail(float2 uv)
            {
                float3 colour = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                return sqrt(saturate(dot(colour, float3(0.2126, 0.7152, 0.0722))));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                float2 texel = _MainTex_TexelSize.xy;

                float topLeft = Detail(input.uv + float2(-texel.x, texel.y));
                float top = Detail(input.uv + float2(0.0, texel.y));
                float topRight = Detail(input.uv + float2(texel.x, texel.y));
                float left = Detail(input.uv + float2(-texel.x, 0.0));
                float right = Detail(input.uv + float2(texel.x, 0.0));
                float bottomLeft = Detail(input.uv + float2(-texel.x, -texel.y));
                float bottom = Detail(input.uv + float2(0.0, -texel.y));
                float bottomRight = Detail(input.uv + float2(texel.x, -texel.y));

                float horizontal = (topRight + 2.0 * right + bottomRight) - (topLeft + 2.0 * left + bottomLeft);
                float vertical = (bottomLeft + 2.0 * bottom + bottomRight) - (topLeft + 2.0 * top + topRight);
                float edge = sqrt(horizontal * horizontal + vertical * vertical);

                // Ramped rather than cut: a hard step makes the overlay crawl and flicker frame to
                // frame on anything near the threshold, which reads as noise rather than as focus.
                float peak = smoothstep(_PeakThreshold, _PeakThreshold * 2.0, edge);

                float luma = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
                float3 base = lerp(source.rgb, luma.xxx, _PeakDesaturate);

                return float4(lerp(base, _PeakColor.rgb, peak), source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
