Shader "Hidden/Basis/DirectToScreen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Feed", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        _PaperWhite ("Paper White (nits)", Float) = 160
        _MaxNits ("Max Nits", Float) = 1000
        _HDRColorspace ("HDR Colorspace", Integer) = 0
        _HDREncoding ("HDR Encoding", Integer) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ HDR_COLORSPACE_CONVERSION_AND_ENCODING

            #define BASIS_HDRCOLORSPACE_REC709 0
            #define BASIS_HDRCOLORSPACE_REC2020 1
            #define BASIS_HDRCOLORSPACE_P3D65 2
            #define BASIS_HDRENCODING_S_RGB 0
            #define BASIS_HDRENCODING_PQ 2
            #define BASIS_HDRENCODING_LINEAR 3
            #define BASIS_HDRENCODING_GAMMA22 4
            #define BASIS_SDR_REF_WHITE 80.0
            #define BASIS_MAX_PQ_NITS 10000.0

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _PaperWhite;
            float _MaxNits;
            int _HDRColorspace;
            int _HDREncoding;

            float3 RotateRec709ToOutputSpace(float3 c)
            {
                if (_HDRColorspace == BASIS_HDRCOLORSPACE_REC2020)
                {
                    return mul(float3x3(0.627402, 0.329292, 0.043306, 0.069095, 0.919544, 0.011360, 0.016394, 0.088028, 0.895578), c);
                }
                if (_HDRColorspace == BASIS_HDRCOLORSPACE_P3D65)
                {
                    return mul(float3x3(0.822462, 0.177538, 0.000000, 0.033194, 0.966806, 0.000000, 0.017083, 0.072397, 0.910520), c);
                }
                return c;
            }

            float3 LinearToPQ(float3 nits)
            {
                float3 y = max(nits / BASIS_MAX_PQ_NITS, 0.0);
                float3 ym1 = pow(y, 2610.0 / 4096.0 / 4.0);
                float3 n = 3424.0 / 4096.0 + (2413.0 / 4096.0 * 32.0) * ym1;
                float3 d = 1.0 + (2392.0 / 4096.0 * 32.0) * ym1;
                return pow(n / d, 2523.0 / 4096.0 * 128.0);
            }

            float3 EncodeForDisplay(float3 nits)
            {
                if (_HDREncoding == BASIS_HDRENCODING_LINEAR) return nits / BASIS_SDR_REF_WHITE;
                if (_HDREncoding == BASIS_HDRENCODING_PQ) return LinearToPQ(nits);
                if (_HDREncoding == BASIS_HDRENCODING_GAMMA22) return pow(max(nits / _MaxNits, 0.0), 1.0 / 2.2);
                return nits / _MaxNits;
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            float4 frag(v2f IN) : SV_Target
            {
                float4 color = IN.color * (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef HDR_COLORSPACE_CONVERSION_AND_ENCODING
                color.rgb = EncodeForDisplay(RotateRec709ToOutputSpace(color.rgb) * _PaperWhite);
                #endif

                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}
