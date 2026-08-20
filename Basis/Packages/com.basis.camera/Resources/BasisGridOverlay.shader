Shader "Hidden/Basis/GridOverlay"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _GridColor ("Line Colour", Color) = (1, 1, 1, 1)
        _GridOpacity ("Opacity", Range(0, 1)) = 0.6
        _GridThickness ("Thickness", Float) = 1
        _GridPattern ("Pattern", Float) = 0
        _GridDivisions ("Divisions", Float) = 3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "BasisGridOverlay"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define BASIS_GRID_EVEN     0
            #define BASIS_GRID_GOLDEN   1
            #define BASIS_GRID_DIAGONAL 2
            #define BASIS_GRID_CENTRE   3

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            float4 _GridColor;
            float _GridOpacity;
            float _GridThickness;
            float _GridPattern;
            float _GridDivisions;

            // 1 - 1/phi and 1/phi. The pair the golden ratio puts the lines at, which is the
            // classical placement the rule of thirds is the round-numbered approximation of.
            static const float GoldenLow = 0.381966;
            static const float GoldenHigh = 0.618034;

            // Stands in for "no line on this axis", far enough out that no ramp reaches it.
            static const float FarFromAnyLine = 1e6;

            // The dark edge carried either side of the white. Without it a white line vanishes
            // against a sky or a lit wall, which is exactly the frame an alignment grid is for.
            static const float HaloWidth = 1.0;
            static const float HaloStrength = 0.6;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // Distances come back in pixels rather than in UV so that a line is the same width
            // whichever axis it runs along on a frame that is not square, and so that the ramp
            // either side of it can be one pixel wide regardless of the feed's resolution.
            float EvenAxisDistance(float coord, float divisions, float axisPixels)
            {
                float scaled = coord * divisions;
                float nearest = round(scaled);

                // The frame's own edges are not grid lines: half of one falls outside the picture,
                // and the viewfinder's border already draws that boundary.
                if (nearest < 0.5 || nearest > divisions - 0.5) return FarFromAnyLine;

                return abs(scaled - nearest) * (axisPixels / max(divisions, 1.0));
            }

            float PairAxisDistance(float coord, float low, float high, float axisPixels)
            {
                return min(abs(coord - low), abs(coord - high)) * axisPixels;
            }

            // Perpendicular distance to whichever corner-to-corner line is nearer, worked in pixel
            // space: a diagonal measured in UV would thin out on the long axis of a wide frame.
            float DiagonalDistance(float2 uv, float2 pixels)
            {
                float scale = (pixels.x * pixels.y) / max(length(pixels), 1e-5);
                return min(abs(uv.x - uv.y), abs(1.0 - uv.x - uv.y)) * scale;
            }

            float NearestLineDistance(float2 uv, float2 pixels)
            {
                int pattern = (int)_GridPattern;

                if (pattern == BASIS_GRID_GOLDEN)
                {
                    return min(PairAxisDistance(uv.x, GoldenLow, GoldenHigh, pixels.x),
                               PairAxisDistance(uv.y, GoldenLow, GoldenHigh, pixels.y));
                }

                if (pattern == BASIS_GRID_DIAGONAL)
                {
                    return DiagonalDistance(uv, pixels);
                }

                if (pattern == BASIS_GRID_CENTRE)
                {
                    return min(abs(uv.x - 0.5) * pixels.x, abs(uv.y - 0.5) * pixels.y);
                }

                return min(EvenAxisDistance(uv.x, _GridDivisions, pixels.x),
                           EvenAxisDistance(uv.y, _GridDivisions, pixels.y));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                float2 pixels = _MainTex_TexelSize.zw;
                float halfWidth = max(_GridThickness, 1.0) * 0.5;
                float lineDistance = NearestLineDistance(input.uv, pixels);

                // Coverage rather than a cut, so a line that lands between two pixels stays one
                // line instead of alternating between two hard ones as the camera is panned.
                float core = 1.0 - smoothstep(halfWidth - 0.5, halfWidth + 0.5, lineDistance);
                float outer = 1.0 - smoothstep(halfWidth + HaloWidth - 0.5, halfWidth + HaloWidth + 0.5, lineDistance);
                float halo = saturate(outer - core) * HaloStrength;

                float opacity = saturate(_GridOpacity);

                float3 colour = lerp(source.rgb, float3(0.0, 0.0, 0.0), halo * opacity);
                colour = lerp(colour, _GridColor.rgb, core * opacity);

                return float4(colour, source.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
