Shader "Basis/NamePlate/Panel"
{
    // Unlit, vertex-color, depth-tested transparent panel for the global single-draw nameplate
    // system. The panel color is the per-plate talk-mode color, carried in the vertex color
    // (set from CurrentColor each frame); the shader just renders it with a subtle top sheen.
    // Procedural (no texture fetches), one material across every plate, VR single-pass safe.
    Properties
    {
        _Sheen ("Top Sheen", Range(0,0.4)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        Fog { Mode Off }
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float3 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv    : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _Sheen;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);

                // CurrentColor (talk-mode color) arrives as an sRGB vertex color. In a linear
                // project Unity doesn't auto-convert vertex colors the way it does a material
                // _BaseColor, so convert here or the grey (and every mode color) renders ~2x too
                // bright. Guarded so it's a no-op in gamma projects.
                float3 c = v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                    c = GammaToLinearSpace(c);
                #endif
                o.color = fixed4(c, v.color.a);

                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Per-plate talk-mode color with a soft top-down sheen.
                float3 body = i.color.rgb * lerp(1.0 - _Sheen, 1.0 + _Sheen, i.uv.y);
                return fixed4(saturate(body), i.color.a);
            }

            ENDHLSL
        }
    }
}
