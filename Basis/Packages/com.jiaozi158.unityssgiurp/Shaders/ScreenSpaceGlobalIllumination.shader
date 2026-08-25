Shader "Hidden/Lighting/ScreenSpaceGlobalIllumination"
{
    Properties
    {
        [HideInInspector] _SSGIBlueNoise ("Blue Noise", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Pass 0: Prepare (camera colour, ambient light, surface normal + validity, albedo + metallic)
        // Pass 1: SSGI
        // Pass 2: Temporal Reprojection
        // Pass 3: Edge-Avoiding Spatial Denoise
        // Pass 4: Temporal Stabilization
        // Pass 5: Copy History Depth
        // Pass 6: Combine GI (multiply by the ambient removal factor, blended per MSAA sample)
        // Pass 7: [Editor only] Camera Motion Vectors
        // Pass 8: Poisson Disk Recurrent Denoise
        // Pass 9: Blit Color Texture
        // Pass 10: Combine GI Add (upscale + add, blended per MSAA sample; debug views)
        // Pass 11: Prime Depth (copies the camera depth into a depth attachment so the forward GBuffer pass gets early Z)

        Pass
        {
            Name "Prepare"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            Blend One Zero

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

        #if UNITY_VERSION >= 202310
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
        #endif

            #include "./SSGI.hlsl"

            // Everything the rest of the effect needs about the surface at this pixel, resolved once.
            // RT-1: the camera colour, which the combine passes rebuild from.
            // RT-2: ambient light at the pixel for its normal (adaptive probe volume or ambient probe): the term SSGI replaces.
            // RT-3: world normal, with the GBuffer-belongs-to-this-surface bit in alpha.
            // RT-4: albedo and metallic, already put through the fallback for surfaces with no GBuffer data.
            void frag(Varyings input, out half4 cameraColor : SV_Target0, out half3 ambientLighting : SV_Target1,
                out half4 surfaceNormal : SV_Target2, out half4 surfaceAlbedo : SV_Target3)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;

                float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, screenUV, 0).r;

            #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
            #endif

                cameraColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, my_point_clamp_sampler, screenUV, 0).rgba;
                ambientLighting = half3(0.0, 0.0, 0.0);
                surfaceNormal = half4(0.0, 0.0, 0.0, 0.0);
                surfaceAlbedo = half4(0.0, 0.0, 0.0, 0.0);

                // If the current pixel is sky
                bool isBackground = abs(depth - UNITY_RAW_FAR_CLIP_VALUE) < RAW_FAR_CLIP_THRESHOLD;

                if (isBackground)
                    return;

                UpdateAmbientSH();

                bool hasGBuffer;
                half3 normalWS = SSGISampleNormalWS(screenUV, hasGBuffer);
                float3 positionWS = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
                ambientLighting = SSGIEvaluateAmbientLighting(screenUV, positionWS, normalWS);

                half3 albedo;
                half metallic;
                SSGISampleAlbedoMetallic(screenUV, hasGBuffer, cameraColor.rgb, ambientLighting, albedo, metallic);

                surfaceNormal = half4(normalWS, hasGBuffer ? SSGI_SURFACE_HAS_GBUFFER : SSGI_SURFACE_NO_GBUFFER);
                surfaceAlbedo = half4(albedo, metallic);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Screen Space Global Illumination"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            #pragma multi_compile_local_fragment _ _FP_REFL_PROBE_ATLAS
            #pragma multi_compile_local_fragment _ _BACKFACE_TEXTURES
            #pragma multi_compile_local_fragment _ _RAYMARCHING_FALLBACK_SKY
            #pragma multi_compile_local_fragment _ _RAYMARCHING_FALLBACK_REFLECTION_PROBES
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION

        #if UNITY_VERSION >= 202310
            #pragma multi_compile_fragment _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_local_fragment _ _APV_LIGHTING_BUFFER
        #endif

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            // The reflection probe atlas fallback uses URP's own cluster iteration and probe declarations,
            // which Core.hlsl only compiles in when the cluster light loop keyword is set.
        #if defined(_FP_REFL_PROBE_ATLAS) && !defined(_CLUSTER_LIGHT_LOOP)
            #define _CLUSTER_LIGHT_LOOP 1
        #endif

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #include "./SSGIDenoise.hlsl"
            #include "./SSGI.hlsl"

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;
                half4 lightingDistance = half4(0.0, 0.0, 0.0, 0.0); // indirectDiffuse.rgb + distance.a

                float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, screenUV, 0).r;

                // If the current pixel is sky
                bool isBackground = abs(depth - UNITY_RAW_FAR_CLIP_VALUE) < RAW_FAR_CLIP_THRESHOLD;

                // Don't clear the render target, we use them to fill in the border gaps when rendering low resolution GI.
                if (isBackground)
                    discard;

            #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
            #endif

                // Start denoising before tracing SSGI
                // If the history sample for a pixel will be invalid, we increase the number of samples and reduce ray marching quality.

                float3 positionWS = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
                float3 cameraPositionWS = GetCameraPositionWS();
                half3 viewDirectionWS = IsPerspectiveProjection() ? normalize(cameraPositionWS - positionWS) : normalize(UNITY_MATRIX_V[2].xyz);

                half2 velocity = SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, my_linear_clamp_sampler, screenUV, 0).xy;
                float2 prevUV = screenUV - velocity;

                half3 normalWS = SSGIReadSurfaceNormal(screenUV);

                half maxRadius = ComputeMaxReprojectionWorldRadius(positionWS, viewDirectionWS, normalWS, _PixelSpreadAngleTangent);
                float prevDeviceDepth = SAMPLE_TEXTURE2D_X_LOD(_SSGIHistoryDepthTexture, my_point_clamp_sampler, prevUV, 0).r;

            #if !UNITY_REVERSED_Z
                prevDeviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, prevDeviceDepth);
            #endif

                float3 prevPositionWS = ComputeWorldSpacePosition(prevUV, prevDeviceDepth, SSGI_PREV_INV_VIEW_PROJ_MATRIX);
                half radius = length(prevPositionWS - positionWS) / maxRadius;

                bool canBeReprojected = (prevUV.x <= 1.0 && prevUV.x >= 0.0 && prevUV.y <= 1.0 && prevUV.y >= 0.0 && radius <= 1.0 && _HistoryTextureValid);

                Ray ray;
                ray.position = cameraPositionWS;
                ray.direction = -viewDirectionWS; // viewDirectionWS points to the camera.

                // Calculate screenHit data only once
                RayHit screenHit = InitializeRayHit();
                screenHit.distance = length(cameraPositionWS - positionWS);
                screenHit.position = positionWS;
                screenHit.normal = normalWS;

                // If reprojection fails, we increase the number of samples and reduce ray marching quality.
                if (!canBeReprojected && _TemporalIntensity != 0.0)
                {
                    MAX_STEP = 8;
                    MAX_SMALL_STEP = 0;
                    MAX_MEDIUM_STEP = 4;
                    STEP_SIZE = 0.6;
                    MEDIUM_STEP_SIZE = 0.075;
                    RAY_COUNT = max(4, RAY_COUNT);
                }

                // Blue noise per pixel, stepped through the R2 sequence per ray and per frame: the sampling error is high frequency
                // in space and well distributed in time, which the spatial and temporal denoisers remove far better than white noise.
                uint2 noisePixel = uint2(input.positionCS.xy);
                float3 blueNoise = float3(SSGIBlueNoise(noisePixel), SSGIBlueNoise(noisePixel + uint2(29, 47)), SSGIBlueNoise(noisePixel + uint2(53, 11)));
                half dither = (frac(blueNoise.z + _FrameIndex * 0.6180339887) * 0.3 - 0.15);

                half sampleWeight = rcp(RAY_COUNT);

                for (int i = 0; i < RAY_COUNT; i++)
                {
                    RayHit rayHit = screenHit;

                    // Generate a new sample direction
                    float sampleIndex = _FrameIndex * RAY_COUNT + i;
                    ray.direction = SampleHemisphereCosine(frac(blueNoise.x + sampleIndex * 0.7548776662), frac(blueNoise.y + sampleIndex * 0.5698402910), rayHit.normal);
                    ray.position = rayHit.position;

                    // Find the intersection of the ray with scene geometries
                    rayHit = RayMarching(ray, screenUV, dither, viewDirectionWS);

                    bool hitSuccessful = rayHit.distance > REAL_EPS;

                    half3 rayRadiance = half3(0.0, 0.0, 0.0);

                    UNITY_BRANCH
                    if (hitSuccessful)
                    {
                        rayRadiance = rayHit.emission;
                        lightingDistance.a += rayHit.distance * sampleWeight;
                    }
                    else
                    {
                        rayRadiance = SampleReflectionProbes(ray.direction, positionWS, 1.0h, screenUV);
                        lightingDistance.a += sampleWeight; // 1.0 * sampleWeight
                    }

                    // Clamping the mean instead of each ray lets one outlier lift the average and then scales every
                    // correct ray down with it, so the surface loses light the outlier never contributed. Clamping the
                    // ray keeps the rest of the estimate intact. Scaling the colour preserves hue and saturation.
                    half rayMaxChannel = Max3(rayRadiance.x, rayRadiance.y, rayRadiance.z);
                    rayRadiance *= rayMaxChannel > _MaxBrightness ? _MaxBrightness * rcp(rayMaxChannel) : 1.0;

                    lightingDistance.rgb += rayRadiance * sampleWeight;
                }

                // Set it to negative to pass "canBeReprojected" to the denoising pass
                lightingDistance.w = canBeReprojected ? lightingDistance.w : -lightingDistance.w;

                return lightingDistance;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Temporal Reprojection"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            Blend One Zero

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "./SSGI.hlsl"

            void frag(Varyings input, out half4 denoiseOutput : SV_Target0, out half currentSample : SV_Target1, out half4 normalOutput : SV_Target2)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;

                // Normals at this resolution for the denoisers, so a tap is a single fetch instead of a decode.
                normalOutput = half4(SSGIReadSurfaceNormal(screenUV), 0.0);

                half2 velocity = SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_LinearClamp, screenUV, 0).xy;

                float2 prevUV = screenUV - velocity;

                float deviceDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, screenUV, 0).r;

                // Fetch the current and history values and apply the exposition to it.
                half4 currentColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, screenUV, 0).rgba;

                half historySample = SAMPLE_TEXTURE2D_X_LOD(_SSGIHistorySampleTexture, my_point_clamp_sampler, prevUV, 0).r;

                // Extract the "canBeReprojected" variable.
                bool canBeReprojected = FastSign(currentColor.a) == 1.0;
                currentColor.a = abs(currentColor.a);

                bool isSky = abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) < RAW_FAR_CLIP_THRESHOLD;
                canBeReprojected = isSky || historySample == 0.0 ? false : canBeReprojected;

                // Re-projected color from last frame.
                half3 prevColor = SAMPLE_TEXTURE2D_X_LOD(_HistoryIndirectDiffuseTexture, sampler_LinearClamp, prevUV, 0).rgb;

                half accumulationFactor = (historySample >= MAX_ACCUM_FRAME_NUM ? _TemporalIntensity : (historySample / (historySample + 1.0)));

                half sampleCount = clamp(historySample + 1.0, 0.0, MAX_ACCUM_FRAME_NUM);

                half3 result;

                UNITY_BRANCH
                if (canBeReprojected)
                {
                    result = (currentColor.rgb * (1.0 - accumulationFactor) + prevColor.rgb * accumulationFactor);
                }
                else if (_AggressiveDenoise)
                {
                    // Performance cost here can be reduced by removing less important operations.

                    // Color Variance
                    half3 boxMax = currentColor.rgb;
                    half3 boxMin = currentColor.rgb;
                    half3 moment1 = currentColor.rgb;
                    half3 moment2 = currentColor.rgb * currentColor.rgb;

                    // adjacent pixels
                    AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, 0.0, -1.0);
                    AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, -1.0, 0.0);
                    AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, 1.0, 0.0);
                    AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, 0.0, 1.0);

                    /*
                    // remaining pixels in a 9x9 square (excluding center)
                    AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, -1.0, -1.0);
                    AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, 1.0, -1.0);
                    AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, -1.0, 1.0);
                    AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, 1.0, 1.0);
                    */

                    prevColor = ClipToVarianceBox(prevColor, boxMin, boxMax, moment1, moment2, 5.0);

                    // We still try to reuse (clamped) history samples even if they are invalid
                    result = (currentColor.rgb * (1.0 - accumulationFactor) + prevColor.rgb * accumulationFactor);
                }
                else
                {
                    result = currentColor.rgb;
                    sampleCount = 1.0;
                }

                denoiseOutput = half4(result, currentColor.a);
                //denoiseOutput = half4(historySample.xxx * rcp(MAX_ACCUM_FRAME_NUM) - rcp(MAX_ACCUM_FRAME_NUM), currentColor.a); // debug sample count
                currentSample = sampleCount;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Edge-Avoiding Spatial Denoise"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            Blend One Zero

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"

            #include "./SSGI.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            half4 frag(Varyings input) : SV_Target
            {
                // Edge-Avoiding A-TrousWavelet Transform for denoising
                // Modified from "https://www.shadertoy.com/view/ldKBzG"
                // feel free to use it

                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;

                // Depth and normals come from the copies made at this resolution, so every tap is a single fetch.
                float centerDepth = SAMPLE_TEXTURE2D_X_LOD(_SSGIDepthTexture, my_point_clamp_sampler, screenUV, 0).r;

                // If the current pixel is sky
                bool isBackground = abs(centerDepth - UNITY_RAW_FAR_CLIP_VALUE) < RAW_FAR_CLIP_THRESHOLD;

                if (isBackground)
                    discard;

            #if !UNITY_REVERSED_Z
                centerDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, centerDepth);
            #endif

                centerDepth = ConvertLinearEyeDepth(centerDepth);

                half4 colorDistance = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, my_point_clamp_sampler, screenUV, 0).rgba;
                half3 centerColor = colorDistance.rgb;
                half hitDistance = colorDistance.a;

                // Dynamic dilation rate
                // This reduces repetitive artifacts of A-Trous filtering.

                // Reduce blur intensity if the hit distance is small, but never below what a pixel that has just been
                // disoccluded needs: it holds a single noisy sample and the temporal pass has nothing to hide it with.
                half noisiness = SSGIHistoryNoisiness(screenUV);
                half blurAmount = hitDistance < 1.0 && _HistoryTextureValid ? 0.05 : 1.0;
                blurAmount = max(blurAmount, noisiness);

                half minRange = max(2.0 * _DownSample, 2.0);
                half maxRange = max(5.0 * _DownSample, minRange + 4.0);
                maxRange *= lerp(1.0, SSGI_NOISY_RADIUS_BOOST, noisiness);

                half random = SSGIBlueNoise(uint2(input.positionCS.xy));
                float2 intensity = floor(lerp(minRange, maxRange, random)) * _BlitTexture_TexelSize.xy;

                // 3x3 gaussian kernel texel offset, excluding center
                const half2 offset[8] =
                {
                    half2(-1.0, -1.0), half2(0.0, -1.0), half2(1.0, -1.0),  // offset[0]..[2]
                    half2(-1.0, 0.0), /*half2(0.0, 0.0),*/ half2(1.0, 0.0), // offset[3]..[5], excluding center
                    half2(-1.0, 1.0), half2(0.0, 1.0), half2(1.0, 1.0)      // offset[6]..[8]
                };

                // 3x3 approximate gaussian kernel, excluding center
                const half kernel[8] =
                {
                    half(0.0625), half(0.125), half(0.0625),  // kernel[0]..[2]
                    half(0.125), /*half(0.25),*/ half(0.125), // kernel[3]..[5], excluding center
                    half(0.0625), half(0.125), half(0.0625)   // kernel[6]..[8]
                };

                half3 centerNormal = SAMPLE_TEXTURE2D_X_LOD(_SSGINormalTexture, my_point_clamp_sampler, screenUV, 0).xyz;

                // Add the center weight
                half sumWeight = 0.25;
                half3 sumColor = centerColor * sumWeight;

                // 3x3, excluding center
                for (uint i = 0; i < 8; i++)
                {
                    float2 uv = saturate(screenUV + offset[i] * intensity);

                    half3 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, my_point_clamp_sampler, uv, 0).rgb;
                    half3 normal = SAMPLE_TEXTURE2D_X_LOD(_SSGINormalTexture, my_point_clamp_sampler, uv, 0).xyz;

                    half3 diff = centerNormal - normal;
                    half distance = max(dot(diff, diff), 0.0);
                    half normalWeight = min(exp(-distance * 20.0), 1.0);

                    float depth = SAMPLE_TEXTURE2D_X_LOD(_SSGIDepthTexture, my_point_clamp_sampler, uv, 0).r;

                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif

                    depth = ConvertLinearEyeDepth(depth);

                    diff.x = centerDepth - depth;
                    distance = dot(diff.x, diff.x);
                    half depthWeight = min(exp(-distance), 1.0);

                    half weight = normalWeight * depthWeight * kernel[i];

                    sumColor += color * weight;
                    sumWeight += weight;
                }

                return half4(lerp(centerColor, sumColor * rcp(sumWeight), blurAmount), hitDistance);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Temporal Stabilization"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            Blend One Zero

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"

            #include "./SSGI.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;

                half4 indirectDiffuse = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, my_point_clamp_sampler, screenUV, 0).xyzw;
                half3 colorCenter = indirectDiffuse.xyz;

                // Unity motion vectors are forward motion vectors in screen UV space
                half2 velocity = SAMPLE_TEXTURE2D_X_LOD(_MotionVectorTexture, sampler_LinearClamp, screenUV, 0).xy;
                float2 prevUV = screenUV - velocity;

                float deviceDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, screenUV, 0).r;

                bool isSky = abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) < RAW_FAR_CLIP_THRESHOLD;

                if (isSky || prevUV.x > 1.0 || prevUV.x < 0.0 || prevUV.y > 1.0 || prevUV.y < 0.0)
                    discard;

            #if !UNITY_REVERSED_Z
                deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, deviceDepth);
            #endif

                // Performance cost here can be reduced by removing less important operations.

                // Color Variance
                half3 boxMax = colorCenter;
                half3 boxMin = colorCenter;
                half3 moment1 = colorCenter;
                half3 moment2 = colorCenter * colorCenter;

                // adjacent pixels
                AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, 0.0, -1.0);
                AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, -1.0, 0.0);
                AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, 1.0, 0.0);
                AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, 0.0, 1.0);

                /*
                // remaining pixels in a 9x9 square (excluding center)
                AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, -1.0, -1.0);
                AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, 1.0, -1.0);
                AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, -1.0, 1.0);
                AdjustColorBox(boxMin, boxMax, moment1, moment2, screenUV, 1.0, 1.0);
                */

                // Re-projected color from last frame.
                half3 prevColor = SAMPLE_TEXTURE2D_X_LOD(_HistoryIndirectDiffuseTexture, sampler_LinearClamp, prevUV, 0).rgb;

                prevColor = ClipToVarianceBox(prevColor, boxMin, boxMax, moment1, moment2, 5.0);

                half intensity = saturate(min(_TemporalIntensity - (abs(velocity.x)) * _TemporalIntensity, _TemporalIntensity - (abs(velocity.y)) * _TemporalIntensity));

                half3 finalColor = lerp(colorCenter, prevColor, intensity * _HistoryTextureValid);

                return half4(finalColor, indirectDiffuse.w);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Copy History Depth"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            Blend One Zero
            //ZWrite On

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            // URP pre-defined the following variable on 2023.2+.
        #if UNITY_VERSION < 202320
            float4 _BlitTexture_TexelSize;
        #endif

            TEXTURE2D_X(_CameraDepthTexture);
            SAMPLER(my_point_clamp_sampler);

            //float frag(Varyings input, out float outDepth : SV_Depth) : SV_Target
            float frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;

                float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, screenUV, 0).r;
                //float depth = LOAD_TEXTURE2D_X(_CameraDepthTexture, uint2(screenUV * _ScreenSize.xy)).r; // This should be a bit faster

                //outDepth = depth;

                return depth;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Combine GI"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            // camera colour *= ambient removal factor, blended per MSAA sample
            Blend Zero SrcColor

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            #pragma multi_compile_local_fragment _ _USE_RENDERING_LAYERS

        #if defined(_USE_RENDERING_LAYERS)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareRenderingLayerTexture.hlsl"
        #endif

            #include "./SSGI.hlsl"

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;

                float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, screenUV, 0).r;

                // If the current pixel is sky
                bool isBackground = abs(depth - UNITY_RAW_FAR_CLIP_VALUE) < RAW_FAR_CLIP_THRESHOLD;

                if (isBackground)
                    discard;

            #if defined(_USE_RENDERING_LAYERS)
                uint meshRenderingLayers = LoadSceneRenderingLayer(uint2(input.positionCS.xy));
                if(!IsMatchingLightLayer(_IndirectDiffuseRenderingLayers, meshRenderingLayers))
                    discard;
            #endif

                // Debug views replace the image: clear it here, the add pass then writes the buffer being inspected.
                if (_SSGIDebugView != 0.0)
                    return half4(0.0, 0.0, 0.0, 1.0);

                if (_OverrideAmbientLighting == 0.0)
                    return half4(1.0, 1.0, 1.0, 1.0);

                half3 cameraColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, my_point_clamp_sampler, screenUV, 0).rgb;
                half3 ambientLighting = SAMPLE_TEXTURE2D_X_LOD(_SSGIAmbientLightingTexture, my_point_clamp_sampler, screenUV, 0).rgb;
                half3 albedo;
                half metallic;
                SSGIReadSurfaceAlbedoMetallic(screenUV, albedo, metallic);

                // Unlit surfaces carry a normal but no albedo in the GBuffer: nothing to remove (and the add pass adds nothing).
                if (!any(albedo))
                    return half4(1.0, 1.0, 1.0, 1.0);

                return half4(SSGIAmbientRemovalFactor(cameraColor, ambientLighting, albedo, metallic), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Scene View Camera Motion Vectors"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            Blend One Zero
            ZTest Always
            ZWrite Off

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            #include "./SSGI.hlsl"

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;

                float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, screenUV, 0).r;

            #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
            #endif

                // Reconstruct world position
                float3 posWS = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);

                // Multiply with current and previous non-jittered view projection
                float4 posCS = mul(_NonJitteredViewProjMatrix, float4(posWS.xyz, 1.0));
                float4 prevPosCS = mul(_PrevViewProjMatrix, float4(posWS.xyz, 1.0));

                // Non-uniform raster needs to keep the posNDC values in float to avoid additional conversions
                // since uv remap functions use floats
                float2 posNDC = posCS.xy * rcp(posCS.w);
                float2 prevPosNDC = prevPosCS.xy * rcp(prevPosCS.w);

                // Calculate forward velocity
                half2 velocity = (posNDC - prevPosNDC);

                // TODO: test that velocity.y is correct
            #if UNITY_UV_STARTS_AT_TOP
                velocity.y = -velocity.y;
            #endif

                // Convert velocity from NDC space (-1..1) to screen UV 0..1 space
                // Note: It doesn't mean we don't have negative values, we store negative or positive offset in the UV space.
                // Note: ((posNDC * 0.5 + 0.5) - (prevPosNDC * 0.5 + 0.5)) = (velocity * 0.5)
                velocity.xy *= 0.5;

                return float4(velocity, 0, 0);
            }
            ENDHLSL
        }

        Pass
        {
            // Modified from HDRP's ReBLUR denoiser
            Name "Poisson Disk Recurrent Denoise"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            Blend One Zero

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"

            #include "./SSGI.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;

                // Depth and normals come from the copies made at this resolution, so every tap is a single fetch.
                float centerDepth = SAMPLE_TEXTURE2D_X_LOD(_SSGIDepthTexture, my_point_clamp_sampler, screenUV, 0).r;

            #if !UNITY_REVERSED_Z
                centerDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, centerDepth);
            #endif

                // If the current pixel is sky
                bool isBackground = abs(centerDepth - UNITY_RAW_FAR_CLIP_VALUE) < RAW_FAR_CLIP_THRESHOLD;

                if (isBackground)
                    discard;

                float3 positionWS = ComputeWorldSpacePosition(screenUV, centerDepth, UNITY_MATRIX_I_VP);
                centerDepth = Linear01Depth(centerDepth, _ZBufferParams);

                // Center Signal
                half4 centerSignal = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, my_point_clamp_sampler, screenUV, 0);

                // Evaluate the position and view vectors
                float3 cameraPositionWS = GetCameraPositionWS();
                half3 viewDirectionWS = IsPerspectiveProjection() ? normalize(cameraPositionWS - positionWS) : normalize(UNITY_MATRIX_V[2].xyz);

                half3 centerNormal = SAMPLE_TEXTURE2D_X_LOD(_SSGINormalTexture, my_point_clamp_sampler, screenUV, 0).xyz;

                // Convert both directions to view space
                //half NdotV = abs(dot(centerNormal, viewDirectionWS));

                // Get the dominant direction
                half4 dominantWS = GetSpecularDominantDirection(centerNormal, viewDirectionWS);

                // Evaluate the blur radius
                //float distanceToCamera = length(positionWS - cameraPositionWS);
                //half blurRadius = ComputeBlurRadius(1.0, BLUR_MAX_RADIUS) * _ReBlurDenoiserRadius;
                // A pixel with a full history is already clean and only loses detail to a wide kernel; one with a
                // single sample needs every tap it can get. The radius follows how converged the estimate is.
                half blurRadius = _ReBlurDenoiserRadius * lerp(1.0, SSGI_NOISY_RADIUS_BOOST, SSGIHistoryNoisiness(screenUV)); // * BLUR_MAX_RADIUS;
                //blurRadius *= max(1.0 - saturate(accumulationFactor / MAX_ACCUM_FRAME_NUM), 1.0);
                //blurRadius *= HitDistanceAttenuation(centerRoughness, distanceToCamera, centerSignal.w);
                //blurRadius *= lerp(saturate((distanceToCamera - MIN_BLUR_DISTANCE) / BLUR_OUT_RANGE), 0.0, 1.0);

                // Evalute the local basis
                half2x3 TvBv = GetKernelBasis(dominantWS.xyz, centerNormal);
                TvBv[0] *= blurRadius;
                TvBv[1] *= blurRadius;

                // Loop through the samples
                float4 signalSum = 0.0; // requires full precision float
                float sumWeight = 0.0;
                for (int sampleIndex = 0; sampleIndex < POISSON_SAMPLE_COUNT; ++sampleIndex)
                {
                    // Pick the next sample value
                    half3 offset = k_PoissonDiskSamples[sampleIndex];

                    // Evaluate the tap uv
                    float2 uv = GetKernelSampleCoordinates(offset, positionWS, TvBv[0], TvBv[1], _ReBlurBlurRotator);

                    // Is the target pixel on the screen?
                    bool isInScreen = uv.x <= 1.0 && uv.x >= 0.0 && uv.y <= 1.0 && uv.y >= 0.0;
                    if (!isInScreen)
                        continue;

                    // Sample weights
                    half depthWeight = 1.0;
                    half normalWeight = 1.0;
                    half planeWeight = 1.0;

                    float sampleDepth = SAMPLE_TEXTURE2D_X_LOD(_SSGIDepthTexture, my_point_clamp_sampler, uv, 0).r;

                #if !UNITY_REVERSED_Z
                    sampleDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, sampleDepth);
                #endif

                    // The depth belongs to the texel centre, so the position is reconstructed there to stay on the surface.
                    float2 texelUV = (clamp(floor(uv * _BlitTexture_TexelSize.zw), 0.0, _BlitTexture_TexelSize.zw - 1.0) + 0.5) * _BlitTexture_TexelSize.xy;
                    float3 samplePositionWS = ComputeWorldSpacePosition(texelUV, sampleDepth, UNITY_MATRIX_I_VP);

                    sampleDepth = Linear01Depth(sampleDepth, _ZBufferParams);

                    half3 sampleNormal = SAMPLE_TEXTURE2D_X_LOD(_SSGINormalTexture, my_point_clamp_sampler, uv, 0).xyz;

                    depthWeight = max(0.0, 1.0 - abs(sampleDepth - centerDepth));

                    const half normalCloseness = sqr(sqr(max(0.0, dot(sampleNormal, centerNormal))));
                    const half normalError = 1.0 - normalCloseness;
                    normalWeight = max(0.0, (1.0 - normalError));

                    // Change in position in camera space
                    const half3 dq = positionWS - samplePositionWS;

                    // How far away is this point from the original sample
                    // in camera space? (Max value is unbounded)
                    const half distance2 = dot(dq, dq);

                    // How far off the expected plane (on the perpendicular) is this point? Max value is unbounded.
                    const half planeError = max(abs(dot(dq, sampleNormal)), abs(dot(dq, centerNormal)));

                    planeWeight = (distance2 < 0.0001) ? 1.0 :
                    pow(max(0.0, 1.0 - 2.0 * planeError / sqrt(distance2)), 2.0);

                    half w = k_GaussianWeight[sampleIndex]; //GetGaussianWeight(offset.z);
                    w *= depthWeight * normalWeight * planeWeight;
                    w = (sampleDepth != 1.0) && isInScreen ? w : 0.0;

                    // Fetch the full resolution depth
                    float4 tapSignal = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, my_point_clamp_sampler, uv, 0);
                    w = Luminance(tapSignal.xyz) > 0.0 ? w : 0.0;
                    tapSignal = w ? tapSignal : 0.0;

                    // Accumulate
                    signalSum += tapSignal * w;
                    sumWeight += w;
                }

                // Normalize the samples (or the central one if we didn't get any valid samples)
                signalSum = sumWeight != 0.0 ? signalSum / sumWeight : centerSignal;

                // Normalize the result
                return max(signalSum, half4(0.0, 0.0, 0.0, 0.0));
            }
            ENDHLSL
        }

        Pass
        {
            Name "Blit Color Texture"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            Blend One Zero

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5
            
            // URP pre-defined the following variable on 2023.2+.
        #if UNITY_VERSION < 202320
            float4 _BlitTexture_TexelSize;
        #endif

            SAMPLER(my_linear_clamp_sampler);

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;

                // This writes the colour history that every ray hit reads back, at the traced resolution. Point
                // sampling threw away three pixels in four, and the aliasing that left was temporally unstable: small
                // bright details popped in and out of the bounce as the camera moved. A bilinear tap at the centre of
                // a half resolution texel covers exactly the four pixels it stands for.
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, my_linear_clamp_sampler, screenUV, 0).rgba;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Combine GI Add"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            // camera colour += bounce, blended per MSAA sample
            Blend One One

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #pragma multi_compile_local_fragment _ _USE_RENDERING_LAYERS
            #pragma multi_compile_local_fragment _ _DEPTH_NORMALS_UPSCALE

        #if defined(_USE_RENDERING_LAYERS)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareRenderingLayerTexture.hlsl"
        #endif

            #include "./SSGI.hlsl"
            #include "./SSGICombine.hlsl"

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.texcoord;

                float depth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, screenUV, 0).r;

                // If the current pixel is sky
                bool isBackground = abs(depth - UNITY_RAW_FAR_CLIP_VALUE) < RAW_FAR_CLIP_THRESHOLD;

                if (isBackground)
                    discard;

            #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
            #endif

            #if defined(_USE_RENDERING_LAYERS)
                uint meshRenderingLayers = LoadSceneRenderingLayer(uint2(input.positionCS.xy));
                if(!IsMatchingLightLayer(_IndirectDiffuseRenderingLayers, meshRenderingLayers))
                    discard;
            #endif

                half3 cameraColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, my_point_clamp_sampler, screenUV, 0).rgb;
                half3 ambientLighting = SAMPLE_TEXTURE2D_X_LOD(_SSGIAmbientLightingTexture, my_point_clamp_sampler, screenUV, 0).rgb;
                bool hasGBuffer = SSGIReadSurfaceHasGBuffer(screenUV);
                half3 albedo;
                half metallic;
                SSGIReadSurfaceAlbedoMetallic(screenUV, albedo, metallic);

                half3 indirectLighting = SSGIResolveIndirectLighting(screenUV, depth);

                // Apply the indirect lighting multiplier, then bound what a guessed albedo is allowed to do to the pixel.
                half3 giContribution = indirectLighting * albedo * (1.0 - metallic) * _IndirectDiffuseLightingMultiplier;
                giContribution = SSGIClampFallbackContribution(giContribution, cameraColor, ambientLighting, hasGBuffer);

                UNITY_BRANCH
                if (_SSGIDebugView != 0.0)
                    return half4(SSGIDebugColor(screenUV, indirectLighting, giContribution), 0.0);

                return half4(giContribution, 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Prime Depth"
            Tags { "LightMode" = "Screen Space Global Illumination" }

            // The forward GBuffer pass cannot share a multisampled camera depth attachment, so without this it draws
            // every opaque surface against a cleared depth buffer and pays for all of the overdraw. Writing the
            // camera's own resolved depth here lets the depth test reject whatever the camera already knows is hidden.
            ColorMask 0
            ZWrite On
            ZTest Always

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // The Blit.hlsl file provides the vertex shader (Vert),
            // input structure (Attributes) and output structure (Varyings)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma target 3.5

            TEXTURE2D_X_FLOAT(_CameraDepthTexture);
            SAMPLER(my_point_clamp_sampler);

            void frag(Varyings input, out float outDepth : SV_Depth)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                outDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, input.texcoord, 0).r;
            }
            ENDHLSL
        }
    }
}
