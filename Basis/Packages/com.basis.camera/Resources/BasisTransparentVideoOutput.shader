Shader "Hidden/Basis/Camera/TransparentVideoOutput"
{
    Properties
    {
        _MainTex("Source", 2D) = "black" {}
        _MaskTex("Mask", 2D) = "black" {}
        _ScaleOffset("Scale Offset", Vector) = (1, 1, 0, 0)
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _MaskTex;
            float4 _ScaleOffset;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv * _ScaleOffset.xy + _ScaleOffset.zw;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                half4 source = tex2D(_MainTex, input.uv);
                half alpha = tex2D(_MaskTex, input.uv).a;

                // Keep straight-alpha RGB for every visible subject pixel. The mask render uses the
                // subject layers only, so excluded world/default layers cannot contribute either
                // colour or alpha. ARGB32's smallest non-zero alpha is 1/255.
                source.rgb *= step(1.0 / 255.0, alpha);
                source.a = alpha;
                return source;
            }
            ENDCG
        }
    }
}
