using System;
using UnityEngine;

// 64x64 tileable blue noise (void-and-cluster, every one of the 256 values appears 16 times), embedded so the effect needs no texture asset.
public static class ScreenSpaceGlobalIlluminationBlueNoise
{
    public const int Size = 64;

    private const string k_Data =
        "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";

    private static Texture2D s_Texture;

    public static Texture2D Texture
    {
        get
        {
            if (s_Texture == null)
            {
                s_Texture = new Texture2D(Size, Size, TextureFormat.R8, false, true)
                {
                    name = "_SSGIBlueNoise",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                s_Texture.LoadRawTextureData(Convert.FromBase64String(k_Data));
                s_Texture.Apply(false, false);
            }
            return s_Texture;
        }
    }
}
