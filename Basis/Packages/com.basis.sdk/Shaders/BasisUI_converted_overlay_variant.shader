Shader "Basis/UI/Background"
{
    Properties
    {
        _BaseTex ("_BaseTex", 2D) = "white" {}
        _AccentTex1 ("_AccentTex1", 2D) = "white" {}
        _AccentTex2 ("_AccentTex2", 2D) = "white" {}
        _AccentTex3 ("_AccentTex3", 2D) = "white" {}
        _BlendFactor ("_BlendFactor", Float) = 0.2
        _OffsetMultiples ("_OffsetMultiples", Vector) = (0.1, 0.2, 0.3, 0)
        _MaxDistance ("_MaxDistance", Float) = 3
        _TimeScale ("Time Scale", Float) = 1.0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Overlay"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            sampler2D _BaseTex;
            sampler2D _AccentTex1;
            sampler2D _AccentTex2;
            sampler2D _AccentTex3;

            float _BlendFactor;
            float4 _OffsetMultiples;
            float _MaxDistance;
            float3 _CursorPos;
            float _TimeScale;

            struct appdata
            {
                float3 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float time = _Time.y * _TimeScale;

                float4 baseCol = tex2D(_BaseTex, i.uv);

                float3 viewCursor = mul(UNITY_MATRIX_V, float4(_CursorPos, 1)).xyz;
                float3 clampedOffset = clamp(viewCursor, -_MaxDistance, _MaxDistance);

                float2 offset1 = clampedOffset.xy * _OffsetMultiples.x + float2(sin(time * 1.2), cos(time * 1.2)) * 0.05;
                float2 offset2 = clampedOffset.xy * _OffsetMultiples.y + float2(cos(time * 0.9), sin(time * 0.9)) * 0.07;
                float2 offset3 = clampedOffset.xy * _OffsetMultiples.z + float2(sin(time * 0.7), cos(time * 0.7)) * 0.09;

                float4 acc1 = tex2D(_AccentTex1, i.uv + offset1);
                float4 acc2 = tex2D(_AccentTex2, i.uv + offset2);
                float4 acc3 = tex2D(_AccentTex3, i.uv + offset3);

                float4 blended = lerp(baseCol, acc1, acc1.a);
                blended = lerp(blended, acc2, acc2.a);
                blended = lerp(blended, acc3, acc3.a);

                blended = lerp(baseCol, blended, _BlendFactor);

                return blended;
            }

            ENDHLSL
        }
    }
}