using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP only honours a camera target texture's own MSAA level while the pipeline asset itself has
/// MSAA enabled (<c>camera.allowMSAA &amp;&amp; asset.msaaSampleCount > 1</c> in InitializeCameraData), so
/// with the Antialiasing setting Off the camera target descriptor reports one sample while the
/// texture is really multisampled. An offscreen camera with no post-processing then gets no
/// intermediate colour attachment — the multisampled sample count was the only thing asking for one
/// — and URP renders straight into the multisampled surface without ever running the explicit
/// resolve the Editor and desktop players require, leaving the surface shaders sample blank.
/// Size every camera target texture through <see cref="Clamp"/>.
/// </summary>
public static class BasisCameraTargetMsaa
{
    /// <summary>MSAA sample count the active pipeline asset will actually honour; at least 1.</summary>
    public static int PipelineSampleCount()
    {
        UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
        return asset != null ? Mathf.Max(1, asset.msaaSampleCount) : 1;
    }

    /// <summary>The requested sample count, dropped to 1 when the pipeline would discard it.</summary>
    public static int Clamp(int requestedSamples)
    {
        int requested = Mathf.Max(1, requestedSamples);
        return PipelineSampleCount() > 1 ? requested : 1;
    }
}
