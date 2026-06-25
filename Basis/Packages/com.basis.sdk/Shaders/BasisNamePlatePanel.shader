Shader "Basis/NamePlate/Panel"
{
    // Unlit, vertex-color, depth-tested transparent panel for the global single-draw nameplate
    // system, styled after basisvr.org: an animated diagonal brand gradient (red -> purple ->
    // blue -> pink) with a bright glass edge. The per-plate talk-state color/alpha rides in the
    // vertex color; _BrandMix dials between that functional color (0) and the brand gradient (1),
    // and the talk color always re-asserts as an edge glow so speaking state stays readable.
    // Procedural (no texture fetches), one material across every plate, VR single-pass safe.
    Properties
    {
        _BrandMix ("Brand Gradient Mix", Range(0,1)) = 0.8
        _GradientSpread ("Gradient Spread", Range(0,2)) = 0.4
        _GradientSpeed ("Gradient Animation Speed", Range(0,1)) = 0.08
        _TopLight ("Vertical Light", Range(0,1)) = 0.12
        _CenterGlow ("Center Lift", Range(0,1)) = 0.05
        _RimWidth ("Talk Rim Width", Range(0,0.5)) = 0.18
        _RimStrength ("Talk Rim Strength", Range(0,1)) = 0.45
        _RimAlpha ("Talk Rim Opacity", Range(0,1)) = 0.15
        _EdgeWidth ("Glass Edge Width", Range(0,0.3)) = 0.06
        _EdgeBright ("Glass Edge Brightness", Range(0,1)) = 0.45
        _EdgeAlpha ("Glass Edge Opacity", Range(0,1)) = 0.30
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

            float _BrandMix;
            float _GradientSpread;
            float _GradientSpeed;
            float _TopLight;
            float _CenterGlow;
            float _RimWidth;
            float _RimStrength;
            float _RimAlpha;
            float _EdgeWidth;
            float _EdgeBright;
            float _EdgeAlpha;

            // basisvr.org .animated-gradient stops, cyclic: red -> purple -> blue -> pink -> red.
            float3 BrandGradient(float t)
            {
                float3 cRed    = float3(0.9373, 0.0706, 0.2157); // #ef1237
                float3 cPurple = float3(0.5765, 0.2000, 0.9176); // #9333ea
                float3 cBlue   = float3(0.2314, 0.5098, 0.9647); // #3b82f6
                float3 cPink   = float3(0.9569, 0.2471, 0.3686); // #f43f5e

                float x = frac(t) * 4.0;
                float3 col = lerp(cRed, cPurple, saturate(x));
                col = lerp(col, cBlue, saturate(x - 1.0));
                col = lerp(col, cPink, saturate(x - 2.0));
                col = lerp(col, cRed,  saturate(x - 3.0));
                return col;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                fixed4 vcol = i.color;

                // Animated diagonal brand gradient (-45deg, slow loop like the site hero).
                float diag = (uv.x + (1.0 - uv.y)) * 0.5;
                float phase = diag * _GradientSpread - _Time.y * _GradientSpeed;
                float3 brand = BrandGradient(phase);

                // Functional talk-state color blended toward the brand gradient.
                float3 fill = lerp(vcol.rgb, brand, _BrandMix);

                // Glass shaping: light from above + a gentle center lift.
                fill *= lerp(1.0 - _TopLight, 1.0 + _TopLight, uv.y);
                float2 c = uv - 0.5;
                fill += saturate(1.0 - dot(c, c) * 3.0) * _CenterGlow;

                // Edges: soft talk-colored glow, then a bright glass stroke at the very border.
                float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                float glow = 1.0 - smoothstep(0.0, _RimWidth, edge);
                fill = lerp(fill, vcol.rgb, glow * _RimStrength);
                float stroke = 1.0 - smoothstep(0.0, _EdgeWidth, edge);
                fill += stroke * _EdgeBright;

                float a = saturate(vcol.a + glow * _RimAlpha + stroke * _EdgeAlpha);
                return fixed4(saturate(fill), a);
            }

            ENDHLSL
        }
    }
}
