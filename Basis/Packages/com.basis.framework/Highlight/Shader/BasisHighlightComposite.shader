Shader "Basis/Highlight/Composite"
{
    // Composites the highlight effect onto the camera color target.
    //
    //   _BlitTexture            = blurred glow field (gaussian of dilated mask)
    //   _BasisHighlightMask     = raw silhouette mask
    //   _BasisHighlightDilated  = max-filtered (dilated) silhouette mask
    //
    // Ring = dilated - raw   (constant-width band exactly following the contour,
    //                         including concave corners — no gaussian-iso-line
    //                         skipping over nub junctions).
    // Glow = blurred * outside  (soft halo trailing outward).
    // Interior = optional faint tint over the silhouette itself.

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // NOTE: render-graph transient textures don't carry import settings,
            // so the `SAMPLER(sampler_FooBar)` auto-sampler comes through unbound
            // and reads zero. Always sample these with the predefined global
            // sampler_LinearClamp (same as _BlitTexture / URP's Bloom).
            TEXTURE2D_X(_BasisHighlightMask);
            TEXTURE2D_X(_BasisHighlightDilated);

            half4 _BasisHighlightColor;
            float _BasisHighlightSoftness;
            float _BasisHighlightGlow;
            float _BasisHighlightInteriorFill;
            float _BasisHighlightDebugMode;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half raw     = SAMPLE_TEXTURE2D_X(_BasisHighlightMask,    sampler_LinearClamp, uv).r;
                half dilated = SAMPLE_TEXTURE2D_X(_BasisHighlightDilated, sampler_LinearClamp, uv).r;
                half blurred = SAMPLE_TEXTURE2D_X(_BlitTexture,           sampler_LinearClamp, uv).r;

                // ---- Debug visualizations ----
                // Each branch returns a fully-opaque solid color so the input
                // signal is visible against the scene. Use to determine which
                // stage of the pipeline is producing zero output.
                int dbg = (int)_BasisHighlightDebugMode;
                if (dbg == 1) return half4(raw,     raw,     raw,     1); // ShowRaw
                if (dbg == 2) return half4(dilated, dilated, dilated, 1); // ShowDilated
                if (dbg == 3) return half4(blurred, blurred, blurred, 1); // ShowBlurred
                if (dbg == 5) {
                    // Raw dilated-minus-raw band, no AA, full opacity.
                    half band = saturate(dilated - raw);
                    return half4(band, band, band, 1);
                }

                // AA the boundaries. softness shifts the step centers around 0.5.
                // The `dilated` input is pre-softened by a small gaussian pass in
                // BasisHighlightPass (RingSoften), so it carries a real 0..1
                // gradient across a few pixels — that gradient is what makes
                // softness produce a visible feathered band rather than a hard
                // step. `raw` stays binary, so its smoothstep gives only
                // sub-pixel AA at the silhouette edge.
                half s = max(_BasisHighlightSoftness, 0.001);
                half dilatedAA = smoothstep(0.5 - s, 0.5 + s, dilated);
                half rawAA     = smoothstep(0.5 - s, 0.5 + s, raw);

                // Ring is the band between the dilated edge and the raw edge.
                // This is solid (alpha 1) so it always reads as a distinct line.
                half ring = saturate(dilatedAA - rawAA);

                // Glow lives only *outside* the dilation band. This (a) prevents
                // the bright inner part of the gaussian field from drowning out
                // the ring and (b) makes the inner edge of the glow land on the
                // outer (AA'd) edge of the ring instead of the hard silhouette
                // edge — no more aliasing against the object.
                half outsideDilation = saturate(1.0 - dilatedAA);
                half glow = blurred * _BasisHighlightGlow * outsideDilation;

                // Optional faint interior tint.
                half interior = rawAA * _BasisHighlightInteriorFill;

                if (dbg == 4) {
                    // ShowRingOnly: composite only the ring contribution.
                    return half4(_BasisHighlightColor.rgb, ring * _BasisHighlightColor.a);
                }

                half alpha = saturate(max(max(ring, glow), interior));
                return half4(_BasisHighlightColor.rgb, alpha * _BasisHighlightColor.a);
            }
            ENDHLSL
        }
    }
}
