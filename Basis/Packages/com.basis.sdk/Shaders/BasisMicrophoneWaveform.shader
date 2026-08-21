Shader "Basis/UI/MicrophoneWaveform"
{
    // Blit-only shader for the live microphone waveform. Each time column arrives as one element
    // of a float4 ring (xy = left trough/peak, zw = right trough/peak) via SetVectorArray, the same
    // "push the samples into the material and let the GPU draw them" path AudioLink uses, so the
    // CPU neither rasterizes nor copies. _Oldest unwraps the ring so a new column costs one write.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "PreviewType"="Plane"
        }

        Cull Off
        Lighting Off
        Fog { Mode Off }
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            #define BASIS_WAVEFORM_COLUMNS 192

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            float4 _Columns[BASIS_WAVEFORM_COLUMNS];

            float4 _Background;
            float4 _Left;
            float4 _Right;
            float4 _Hot;
            float4 _CentreLine;
            float4 _GateLine;
            float4 _MutedColour;

            float _Oldest;
            float _Stereo;
            float _Scale;
            float _CentreHalf;
            float _LineHalf;
            float _MinimumBar;
            float _GateLevel;
            float _WarnLevel;
            float _Muted;
            float _Glow;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 SampleColumn(float position)
            {
                int index = (int)position;
                index = clamp(index, 0, BASIS_WAVEFORM_COLUMNS - 1);
                int ring = index + (int)_Oldest;
                ring = ring >= BASIS_WAVEFORM_COLUMNS ? ring - BASIS_WAVEFORM_COLUMNS : ring;
                return _Columns[ring];
            }

            // Coverage of a band [low, high] at signed level v, softened by the pixel footprint so
            // the envelope reads as a curve instead of a stack of hard boxes.
            float BandCoverage(float v, float low, float high, float softness)
            {
                float rises = smoothstep(low - softness, low + softness, v);
                float falls = 1.0 - smoothstep(high - softness, high + softness, v);
                return saturate(rises * falls);
            }

            float4 frag (v2f i) : SV_Target
            {
                // Interpolate between neighbouring columns rather than snapping to one, so the
                // envelope is continuous across the width instead of 192 visible steps.
                float position = i.uv.x * (BASIS_WAVEFORM_COLUMNS - 1);
                float blend = frac(position);
                blend = blend * blend * (3.0 - 2.0 * blend);

                float4 near = SampleColumn(floor(position));
                float4 far = SampleColumn(floor(position) + 1.0);
                float4 column = lerp(near, far, blend);

                float centred = (i.uv.y - 0.5) * 2.0;
                float v = centred / max(_Scale, 0.0001);
                float softness = max(fwidth(v) * 0.85, 0.0015);

                float2 left = clamp(column.xy, -1.0, 1.0);
                float2 right = clamp(column.zw, -1.0, 1.0);

                float leftCoverage = BandCoverage(v, min(left.x, -_MinimumBar), max(left.y, _MinimumBar), softness);
                float rightCoverage = _Stereo > 0.5 ? BandCoverage(v, min(right.x, -_MinimumBar), max(right.y, _MinimumBar), softness) : 0.0;

                float amplitude = max(max(-left.x, left.y), max(-right.x, right.y));
                float hot = saturate((amplitude - _WarnLevel) / max(1.0 - _WarnLevel, 0.0001));

                float3 leftColour = lerp(_Left.rgb, _Hot.rgb, hot);
                float3 rightColour = lerp(_Right.rgb, _Hot.rgb, hot);

                float3 colour = _Background.rgb;

                if (_GateLevel > 0.0)
                {
                    float gate = smoothstep(_LineHalf, 0.0, abs(abs(v) - _GateLevel));
                    colour = lerp(colour, _GateLine.rgb, gate);
                }

                float centreLine = smoothstep(_CentreHalf, 0.0, abs(centred));
                colour = lerp(colour, _CentreLine.rgb, centreLine);

                // A soft bloom just outside the envelope stops the fill ending on a flat cut.
                float glowCoverage = BandCoverage(v, min(left.x, -_MinimumBar) - _Glow, max(left.y, _MinimumBar) + _Glow, softness + _Glow);
                float glowRight = _Stereo > 0.5 ? BandCoverage(v, min(right.x, -_MinimumBar) - _Glow, max(right.y, _MinimumBar) + _Glow, softness + _Glow) : 0.0;
                float3 glowColour = leftColour * glowCoverage + rightColour * glowRight;
                float glowAmount = saturate(glowCoverage + glowRight);
                colour = lerp(colour, glowColour / max(glowAmount, 0.0001), glowAmount * 0.22);

                // Additive between the channels so an in-phase stereo signal reads as the blend of
                // both rather than whichever happened to be drawn last.
                float3 wave = leftColour * leftCoverage + rightColour * rightCoverage;
                float coverage = saturate(leftCoverage + rightCoverage);
                wave = saturate(wave / max(coverage, 0.0001));

                // Lift the middle of the fill slightly so a tall column has some shape to it.
                float shape = lerp(0.82, 1.12, saturate(1.0 - abs(v) / max(amplitude, 0.0001)));
                colour = lerp(colour, saturate(wave * shape), coverage);

                colour = lerp(colour, _MutedColour.rgb, _Muted * saturate(coverage + glowAmount * 0.22));

                return float4(colour, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
