// Draws a renderer into the screen space global illumination GBuffer using only the material's common properties
// (_MainTex, _Color, _BumpMap, _Cutoff). Used as an override shader for materials whose shader has neither a
// "UniversalGBuffer" nor an "SSGIGBuffer" pass, so those surfaces receive GI with their real albedo and normals
// without touching the shader itself. Skinned meshes are skinned by Unity before this vertex shader like any other.
Shader "Hidden/Lighting/ScreenSpaceGlobalIlluminationGBufferOverride"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _Cutoff ("Alpha Cutoff", Range(0, 1.001)) = 0.5
        _Mode ("Rendering Preset (Poiyomi)", Float) = 0
        _AlphaClip ("Alpha Clip (URP)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SSGIGBufferOverride"
            Tags { "LightMode" = "SSGIGBuffer" }

            Cull Off
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _BumpScale;
            half _Cutoff;
            half _Mode;
            half _AlphaClip;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = half3(normalInputs.normalWS);
                output.tangentWS = half4(half3(normalInputs.tangentWS), half(input.tangentOS.w * GetOddNegativeScale()));
                return output;
            }

            void Frag(Varyings input, bool isFrontFace : SV_IsFrontFace,
                out half4 outGBuffer0 : SV_Target0,
                out half4 outGBuffer1 : SV_Target1,
                out half4 outGBuffer2 : SV_Target2)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;

                // Poiyomi "Cutout" (1) and "TransClipping" (9) presets, URP alpha clip.
                bool alphaClip = _Mode == 1.0 || _Mode == 9.0 || _AlphaClip > 0.5;
                if (alphaClip)
                    clip(albedo.a - _Cutoff);

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS));
                normalWS = NormalizeNormalPerPixel(normalWS);
                if (!isFrontFace)
                    normalWS = -normalWS;

            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                half3 packedNormalWS = half3(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)));
            #else
                half3 packedNormalWS = normalWS;
            #endif

                // URP GBuffer layout, first three targets: albedo + material flags, metallic + occlusion, normal + smoothness.
                outGBuffer0 = half4(albedo.rgb, 0.0);
                outGBuffer1 = half4(0.0, 0.0, 0.0, 1.0);
                outGBuffer2 = half4(packedNormalWS, 0.5);
            }
            ENDHLSL
        }
    }
}
