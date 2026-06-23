Shader "Basis/Test/VertexDeformTestMask"
{
    // Highlight mask matching "Basis/Test/VertexDeformTest". Same vertex deform,
    // but renders a solid R=1 silhouette (ColorMask R, ZWrite Off) so the
    // BasisHighlight feature's outline follows the displaced geometry. Assign as
    // the maskMaterial on a BasisHighlightOverride (OverrideType = Material) and
    // mirror the deform property values from the visible material.

    Properties
    {
        _DeformAmount ("Deform Amount", Float) = 0.1
        _DeformFrequency ("Deform Frequency", Float) = 6
        _DeformSpeed ("Deform Speed", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"
        }
        LOD 100

        Pass
        {
            Name "VertexDeformMask"
            Tags
            {
                "LightMode" = "BasisHighlightMask"
            }

            ZWrite Off
            ZTest LEqual
            Cull Back
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VertexDeform.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _DeformAmount;
                float _DeformFrequency;
                float _DeformSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
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

                float3 deformed = BasisApplyVertexDeform(
                    input.positionOS.xyz, input.normalOS,
                    _DeformAmount, _DeformFrequency, _DeformSpeed);
                output.positionCS = TransformObjectToHClip(deformed);
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
