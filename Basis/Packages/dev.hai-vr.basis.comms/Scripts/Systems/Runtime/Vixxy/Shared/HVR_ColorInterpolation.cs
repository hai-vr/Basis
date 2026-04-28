using UnityEngine;
// ReSharper disable SuggestVarOrType_BuiltInTypes
// ReSharper disable InconsistentNaming

namespace HVR.Vixxy
{
#if HVR_VIXXY_IS_IN_BASIS
    public static class HVR_ColorInterpolation
    {
        // Note:
        // Interpolation using OkLab may not be possible with HDR
        // https://blog.selfshadow.com/publications/s2025-shading-course/pdi/s2025_pbs_pdi_slides.pdf
        // page 141
        //
        // (Basis Discord) https://discord.com/channels/1239242259392757822/1288184592930570293/1498540178157867018
        public static Color OklabLerp(Color a, Color b, float t)
        {
            var labA = RGBToOklab(a.linear);
            var labB = RGBToOklab(b.linear);

            var lab = Vector3.Lerp(labA, labB, t);
            var alpha = Mathf.Lerp(a.a, b.a, t);

            return OklabToRGB(lab, alpha);
        }

        private static Vector3 RGBToOklab(Color rgb)
        {
            float r = rgb.r;
            float g = rgb.g;
            float b = rgb.b;

            float l = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
            float m = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
            float s = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;

            l = Mathf.Pow(l, 1.0f / 3.0f);
            m = Mathf.Pow(m, 1.0f / 3.0f);
            s = Mathf.Pow(s, 1.0f / 3.0f);

            float L = 0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s;
            float A = 1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s;
            float B = 0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s;

            return new Vector3(L, A, B);
        }

        private static Color OklabToRGB(Vector3 lab, float alpha)
        {
            float L = lab.x;
            float A = lab.y;
            float B = lab.z;

            float l = L + 0.3963377774f * A + 0.2158037573f * B;
            float m = L - 0.1055613458f * A - 0.0638541728f * B;
            float s = L - 0.0894841775f * A - 1.2914855480f * B;

            l = l * l * l;
            m = m * m * m;
            s = s * s * s;

            float r = 4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
            float g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
            float b = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

            return new Color(r, g, b, alpha);
        }
    }
#endif
}
