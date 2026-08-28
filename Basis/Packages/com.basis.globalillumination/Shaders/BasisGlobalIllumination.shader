Shader "Hidden/Basis/GlobalIllumination"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "BasisGITrace"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_local_fragment _ _BASISGI_NORMALS_TEXTURE
            #pragma multi_compile_local_fragment _ _BASISGI_FALLBACK_SKY _BASISGI_FALLBACK_PROBE
            #pragma multi_compile_local_fragment _ _BASISGI_EMITTERS
            #pragma multi_compile_local_fragment _ _BASISGI_EMITTER_OCCLUSION
            #pragma multi_compile_local_fragment _ _BASISGI_RAY_REUSE
            #pragma multi_compile_local_fragment _ _BASISGI_HIT_NORMAL
            #pragma multi_compile_local_fragment _ _BASISGI_HIERARCHICAL_MARCH

            #include "./BasisGlobalIlluminationTrace.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return BasisGITrace(input.texcoord, input.positionCS.xy);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BasisGITemporal"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_local_fragment _ _BASISGI_NEIGHBOURHOOD_CLAMP
            #pragma multi_compile_local_fragment _ _BASISGI_MOTION_VECTORS

            #include "./BasisGlobalIlluminationDenoise.hlsl"

            BasisGITemporalOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return BasisGITemporal(input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BasisGIBlur"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "./BasisGlobalIlluminationDenoise.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return BasisGIBilateralBlur(input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BasisGIComposite"
            Blend DstColor Zero, Zero One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_local_fragment _ _BASISGI_BILATERAL_UPSAMPLE

            #include "./BasisGlobalIlluminationComposite.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return BasisGIComposite(input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BasisGIDebug"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_local_fragment _ _BASISGI_NORMALS_TEXTURE
            #pragma multi_compile_local_fragment _ _BASISGI_BILATERAL_UPSAMPLE

            #include "./BasisGlobalIlluminationComposite.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return BasisGIDebug(input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BasisGICopyColor"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "./BasisGlobalIlluminationCommon.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return float4(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, UnityStereoTransformScreenSpaceTex(input.texcoord), 0).rgb, 1.0);
            }
            ENDHLSL
        }

        // Pass 6, appended rather than inserted: the pass indices above are constants on
        // BasisGlobalIlluminationPass and reordering them silently repoints every stage.
        Pass
        {
            Name "BasisGISpecularUpsample"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_local_fragment _ _BASISGI_BILATERAL_UPSAMPLE

            #include "./BasisGlobalIlluminationComposite.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return BasisGISpecularResolve(input.texcoord);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BasisGICoarseSeed"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "./BasisGlobalIlluminationCommon.hlsl"

            /// Brings the full resolution depth buffer down to traced resolution, linearised, as the pair
            /// (closest, furthest) every consumer downstream reads. Sky is not clamped to the far plane: it
            /// writes the sentinel in r and zero in g, so a texel of open space can never be in front of a
            /// ray and no consumer needs a sky test of its own.
            ///
            /// This takes the ONE texel a point sample would have landed on rather than the minimum of the
            /// block, and the difference is not cosmetic. A minimum is the Hi-Z convention and it is
            /// conservative in the right direction for CULLING, where a maybe is resolved by looking properly
            /// afterwards. Nothing looks properly afterwards here - the march reads this and believes it - so
            /// taking the closest of a block instead swells every surface towards the camera by the depth
            /// spread of that block, and rays that passed cleanly in front of a silhouette begin crossing it.
            /// Measured on the contact bounce probe: the hierarchical march went from 1% above a converged run
            /// of the same estimator to 12% above it. The fine walk visits every texel and so meets every one
            /// of those invented crossings, where the uniform march it is measured against strides past most
            /// of them - which is why the error showed up in one and not the other. A representative texel is
            /// unbiased, and at Full resolution it is the identical texel the march used to read.
            float2 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int span = clamp((int)BASISGI_COARSE_SPAN, 1, 8);
                int2 limit = int2(BASISGI_COARSE_SOURCE_SIZE) - 1;
                // Where a point sample at the centre of this traced texel lands: floor((i + 0.5) * span).
                int2 coord = min(int2(input.positionCS.xy) * span + (span >> 1), limit);

                float raw = LOAD_TEXTURE2D_X(_CameraDepthTexture, coord).r;
                if (BasisGIIsSky(raw)) { return float2(BASISGI_SKY_DEPTH, 0.0); }

                float eye = BasisGILinearEyeDepth(raw);
                return float2(eye, eye);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BasisGICoarseReduce"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "./BasisGlobalIlluminationCommon.hlsl"

            /// Folds one level of the coarse summary into the next. Closest stays a minimum and furthest
            /// stays a maximum, so both remain true of every texel underneath however many times it folds.
            float2 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int span = clamp((int)BASISGI_COARSE_SPAN, 1, 8);
                int2 base = int2(input.positionCS.xy) * span;
                int2 limit = int2(BASISGI_COARSE_SOURCE_SIZE) - 1;

                float closest = BASISGI_SKY_DEPTH;
                float furthest = 0.0;

                UNITY_LOOP
                for (int y = 0; y < span; y++)
                {
                    UNITY_LOOP
                    for (int x = 0; x < span; x++)
                    {
                        float2 tap = LOAD_TEXTURE2D_X(_BasisGICoarseDepth, min(base + int2(x, y), limit)).rg;
                        closest = min(closest, tap.r);
                        furthest = max(furthest, tap.g);
                    }
                }

                return float2(closest, furthest);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
