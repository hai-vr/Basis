Shader "Basis/Highlight/GenericMask"
{
    // Vanilla object-to-clip silhouette. Used by BasisHighlightFeature as the
    // fallback mask material for any renderer that doesn't ship with its own
    // BasisHighlightMaterialOverride. Renders solid R=1 over the silhouette.

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"
        }
        LOD 100

        Pass
        {
            Name "GenericMask"
            Tags
            {
                "LightMode" = "BasisHighlightMask"
            }

            ZWrite Off
            ZTest LEqual
            Cull Off
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(1, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
