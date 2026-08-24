Shader "Hidden/Basis/DirectToScreen"
{
    Properties
    {
        _MainTex ("Feed", 2D) = "black" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _PaperWhite ("Paper White (nits)", Float) = 160
        _MaxNits ("Max Nits", Float) = 1000
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "BasisDirectToScreen"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local_fragment _ HDR_COLORSPACE_CONVERSION_AND_ENCODING

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/HDROutput.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _Color;
            float _PaperWhite;
            float _MaxNits;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _Color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;

                #if defined(HDR_COLORSPACE_CONVERSION_AND_ENCODING)
                color.rgb = OETF(RotateRec709ToOutputSpace(color.rgb) * _PaperWhite, _MaxNits);
                #endif

                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
