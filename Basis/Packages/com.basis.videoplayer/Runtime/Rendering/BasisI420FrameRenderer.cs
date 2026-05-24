using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

// Decodes I420 frames into an internal sRGB RenderTexture via a blit through
// the bundled Basis/VideoPlayer/Yuv420ToRgb shader. The RenderTexture is
// exposed as OutputTexture so consumers (BasisVideoMaterialOutput,
// BasisVideoDisplay) can rebind on resize via OnOutputTextureChanged.
//
// The shader is loaded once on first use via Addressables (key below). The load
// is forced synchronous via WaitForCompletion so callers don't have to deal with
// an async init step; the actual wait is milliseconds for a single shader.
//
// Three R8 plane textures are reused frame-to-frame; the RenderTexture is
// recreated only when frame dimensions change. SetPixelData with
// sourceDataStartIndex slices BasisVideoFrame.Data in place — no per-frame byte
// copies on the C# side.
public sealed class BasisI420FrameRenderer : IBasisFrameRenderer
{
    // Address the shader is registered under in Addressables.
    // The shader asset must be marked Addressable with this address; the
    // package ships it at Runtime/Shaders/BasisYuv420ToRgb.shader.
    public const string ShaderAddress = "BasisYuv420ToRgb";

    // Fallback shader name used by Shader.Find if Addressables can't resolve
    // the address (e.g., Addressables isn't initialized in the consumer project).
    private const string ShaderName = "Basis/VideoPlayer/Yuv420ToRgb";

    private static readonly int YPlaneId = Shader.PropertyToID("_YPlane");
    private static readonly int UPlaneId = Shader.PropertyToID("_UPlane");
    private static readonly int VPlaneId = Shader.PropertyToID("_VPlane");

    private Texture2D yPlane;
    private Texture2D uPlane;
    private Texture2D vPlane;
    private int currentWidth;
    private int currentHeight;

    private Material conversionMaterial;
    private RenderTexture outputRt;
    private AsyncOperationHandle<Shader> shaderHandle;
    private bool shaderHandleValid;
    private bool shaderMissingLogged;

    public Texture OutputTexture => outputRt;

    public event Action<Texture> OnOutputTextureChanged;

    public bool SupportsFormat(BasisVideoFrameFormat format) => format == BasisVideoFrameFormat.I420;

    public bool Present(BasisVideoFrame frame)
    {
        if (frame?.Data == null) return false;
        if (!EnsureConversionMaterial()) return false;
        EnsurePlanes(frame.Width, frame.Height);
        EnsureOutputTexture(frame.Width, frame.Height);

        int w = frame.Width;
        int h = frame.Height;
        int ySize = w * h;
        int uSize = (w / 2) * (h / 2);

        yPlane.SetPixelData(frame.Data, 0, 0);
        yPlane.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        uPlane.SetPixelData(frame.Data, 0, ySize);
        uPlane.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        vPlane.SetPixelData(frame.Data, 0, ySize + uSize);
        vPlane.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        conversionMaterial.SetTexture(YPlaneId, yPlane);
        conversionMaterial.SetTexture(UPlaneId, uPlane);
        conversionMaterial.SetTexture(VPlaneId, vPlane);

        // Pass yPlane as source so Blit has a valid source binding; the shader
        // ignores _MainTex and reads _YPlane/_UPlane/_VPlane explicitly.
        Graphics.Blit(yPlane, outputRt, conversionMaterial);
        return true;
    }

    public void Dispose()
    {
        DisposeTex(ref yPlane);
        DisposeTex(ref uPlane);
        DisposeTex(ref vPlane);
        if (outputRt != null)
        {
            outputRt.Release();
            DestroyObject(outputRt);
            outputRt = null;
            OnOutputTextureChanged?.Invoke(null);
        }
        if (conversionMaterial != null)
        {
            DestroyObject(conversionMaterial);
            conversionMaterial = null;
        }
        if (shaderHandleValid)
        {
            try { Addressables.Release(shaderHandle); } catch { }
            shaderHandleValid = false;
        }
        currentWidth = 0;
        currentHeight = 0;
    }

    private bool EnsureConversionMaterial()
    {
        if (conversionMaterial != null) return true;

        Shader shader = LoadShaderViaAddressables();
        // Fallback: if Addressables isn't initialized in this project or the
        // address isn't registered, Shader.Find still works as long as the
        // shader was pulled into the build by some other reference (e.g.,
        // Always Included Shaders, or an inspector reference).
        if (shader == null) shader = Shader.Find(ShaderName);

        if (shader == null)
        {
            if (!shaderMissingLogged)
            {
                BasisDebug.LogError(
                    $"BasisI420FrameRenderer: could not load shader '{ShaderName}'. " +
                    $"Mark com.basis.videoplayer/Runtime/Shaders/BasisYuv420ToRgb.shader as Addressable with address '{ShaderAddress}', " +
                    "or add the shader to Project Settings > Graphics > Always Included Shaders.",
                    BasisDebug.LogTag.Video);
                shaderMissingLogged = true;
            }
            return false;
        }

        conversionMaterial = new Material(shader)
        {
            name = "BasisYuv420ToRgbConversion",
            hideFlags = HideFlags.HideAndDontSave,
        };
        return true;
    }

    private Shader LoadShaderViaAddressables()
    {
        try
        {
            shaderHandle = Addressables.LoadAssetAsync<Shader>(ShaderAddress);
            shaderHandleValid = shaderHandle.IsValid();
            Shader shader = shaderHandle.WaitForCompletion();
            if (shader == null && shaderHandleValid)
            {
                Addressables.Release(shaderHandle);
                shaderHandleValid = false;
            }
            return shader;
        }
        catch (Exception)
        {
            if (shaderHandleValid)
            {
                try { Addressables.Release(shaderHandle); } catch { }
                shaderHandleValid = false;
            }
            return null;
        }
    }

    private void EnsurePlanes(int w, int h)
    {
        if (yPlane != null && currentWidth == w && currentHeight == h) return;
        DisposeTex(ref yPlane);
        DisposeTex(ref uPlane);
        DisposeTex(ref vPlane);
        yPlane = MakePlane(w, h, "BasisVideoPlayer_Y");
        uPlane = MakePlane(w / 2, h / 2, "BasisVideoPlayer_U");
        vPlane = MakePlane(w / 2, h / 2, "BasisVideoPlayer_V");
        currentWidth = w;
        currentHeight = h;
    }

    private void EnsureOutputTexture(int w, int h)
    {
        if (outputRt != null && outputRt.width == w && outputRt.height == h) return;
        if (outputRt != null)
        {
            outputRt.Release();
            DestroyObject(outputRt);
        }
        outputRt = new RenderTexture(w, h, depth: 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = "BasisVideoPlayerOutput",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
            autoGenerateMips = false,
        };
        outputRt.Create();
        OnOutputTextureChanged?.Invoke(outputRt);
    }

    private static Texture2D MakePlane(int w, int h, string name)
    {
        return new Texture2D(w, h, TextureFormat.R8, mipChain: false, linear: true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = name,
        };
    }

    private static void DisposeTex(ref Texture2D t)
    {
        if (t == null) return;
        DestroyObject(t);
        t = null;
    }

    private static void DestroyObject(UnityEngine.Object o)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(o);
        else UnityEngine.Object.DestroyImmediate(o);
    }
}
