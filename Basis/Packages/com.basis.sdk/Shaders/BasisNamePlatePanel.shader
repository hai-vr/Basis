Shader "Basis/NamePlate/Panel"
{
    // Unlit, vertex-color, depth-tested transparent panel used by the global single-draw
    // nameplate system. Every plate's rounded-quad background is merged into one world-space
    // mesh and rendered with this material; per-plate tint/alpha rides in the vertex color so
    // no MaterialPropertyBlock is needed (MPBs break batching and would force one draw per plate).
    // Matches the look of the old TransParentNamePlateMaterial (queue 3000, ZWrite off,
    // SrcAlpha/OneMinusSrcAlpha, occluded by opaque geometry) minus the wasted Lit shading.
    Properties
    {
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
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }

            ENDHLSL
        }
    }
}
