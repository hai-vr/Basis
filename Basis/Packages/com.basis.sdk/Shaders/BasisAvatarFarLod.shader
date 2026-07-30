Shader "Basis/AvatarFarLod"
{
    // Distance far LOD for remote avatars. One opaque pass, per-vertex lighting
    // (ambient SH + wrapped main light — the atlas was captured under flat white
    // ambient, so world lighting is applied here), fog, VR single-pass instanced
    // safe. DepthOnly pass included so forced depth priming keeps working.
    // No ShadowCaster on purpose: far LODs render past the shadow LOD cutoff.
    Properties
    {
        // [MainTexture]/[MainColor] make Material.mainTexture / .color target these —
        // without them Unity writes _MainTex/_Color, which this shader doesn't have.
        [MainTexture] _BaseMap ("Atlas", 2D) = "white" {}
        [MainColor] _Tint ("Tint", Color) = (1,1,1,1)
        // Measured from the avatar's own shaders at bake time: toon shaders floor and cap
        // their light term, and matching that keeps the far avatar from going pitch black or
        // blowing out where the real avatar wouldn't. 0 / 4 ≈ unclamped standard response.
        _MinBrightness ("Min Brightness", Range(0, 1)) = 0
        _MaxBrightness ("Max Brightness", Range(0.5, 4)) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // Adaptive Probe Volumes: without these variants an APV world lights far avatars
            // with the global sky ambient — glowing in dark interiors where real avatars don't.
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half _MinBrightness;
                half _MaxBrightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3 normalWS    : TEXCOORD1;
                half fogFactor    : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.positionWS = positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _Tint.rgb;

                // Per-pixel lighting: per-vertex on big decimated triangles reads as faceting.
                // Wrapped diffuse keeps the dark side readable — a hard terminator on low poly
                // reads as banding, not shading.
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half wrapped = saturate(dot(normalWS, mainLight.direction)) * 0.6h + 0.4h;

                // Ambient: Adaptive Probe Volumes when the world uses them (same sampling path
                // as URP's own shaders), classic SH otherwise.
                half3 ambient;
                #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
                if (_EnableProbeVolumes)
                {
                    float3 bakedGI;
                    EvaluateAdaptiveProbeVolume(input.positionWS, normalWS, GetWorldSpaceNormalizeViewDir(input.positionWS), (uint2)input.positionCS.xy, bakedGI);
                    ambient = (half3)bakedGI;
                }
                else
                {
                    ambient = SampleSH(normalWS);
                }
                #else
                ambient = SampleSH(normalWS);
                #endif

                half3 light = ambient + mainLight.color * wrapped;

                // Match the real avatar's measured lighting clamps: scale by luminance so the
                // light keeps its hue while its intensity is floored/capped like theirs.
                half lum = max(light.r, max(light.g, light.b));
                half clampedLum = clamp(lum, _MinBrightness, _MaxBrightness);
                light = lum > 1e-4h ? light * (clampedLum / lum) : _MinBrightness.xxx;

                half3 color = MixFog(albedo * light, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings depthVert(DepthAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half depthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
