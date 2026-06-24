using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// One-shot render-graph compute pass that bakes Unity's APV irradiance into a world-space 3D texture
/// (the persistent target RT owned by the renderer feature). It runs inside a camera render so URP's
/// APV runtime resources are bound. The feature publishes the RT to <see cref="VolumetricFogAPVBaker"/>
/// when it sets this pass up; this pass only fills the GPU texture.
/// </summary>
public sealed class VolumetricFogAPVBakePass : ScriptableRenderPass
{
    private class BakePassData
    {
        public ComputeShader computeShader;
        public int kernelIndex;
        public TextureHandle target;
        public Vector3 boundsMin;
        public Vector3 boundsSize;
        public Vector3Int resolution;
    }

    private static readonly int BakeResultId = Shader.PropertyToID("_BakeResult");
    private static readonly int BakeBoundsMinId = Shader.PropertyToID("_BakeBoundsMin");
    private static readonly int BakeBoundsSizeId = Shader.PropertyToID("_BakeBoundsSize");
    private static readonly int BakeResolutionId = Shader.PropertyToID("_BakeResolution");

    // Configured by the renderer feature before the pass is enqueued.
    public ComputeShader computeShader;
    public int kernelIndex = -1;
    public RTHandle targetRT;
    public Vector3 boundsMin;
    public Vector3 boundsSize;
    public Vector3Int resolution;

    // When true (and the GPU supports it), the bake is scheduled on the async compute queue. The fog draw
    // that consumes the RT is not render-graph-tracked, so the graph inserts no fence: a same-frame torn
    // read is possible while the bake is still re-running during the APV settle window (brief fog flicker
    // on world load). Steady state is safe since baking stops once settled and the RT stays resident.
    public bool useAsyncCompute;

    private readonly ProfilingSampler bakeProfilingSampler = new ProfilingSampler("Bake APV Fog Volume");

    public VolumetricFogAPVBakePass()
    {
        // Bake early: after APV runtime resources are bound for the frame, but before the fog samples
        // them (the fog runs at BeforeRenderingPostProcessing by default). Synchronous compute on
        // DX11/Vulkan keeps this ordered ahead of the fog draw in the same frame.
        renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (computeShader == null || kernelIndex < 0 || targetRT == null || targetRT.rt == null)
            return;
        if (resolution.x <= 0 || resolution.y <= 0 || resolution.z <= 0)
            return;

        TextureHandle target = renderGraph.ImportTexture(targetRT);

        // VALIDATE: AddComputePass / IComputeRenderGraphBuilder / ComputeGraphContext are the Unity 6
        // (URP 17.x) render-graph compute API; confirm the exact names compile against your SRP version.
        using (IComputeRenderGraphBuilder builder = renderGraph.AddComputePass("Bake APV Fog Volume", out BakePassData data, bakeProfilingSampler))
        {
            data.computeShader = computeShader;
            data.kernelIndex = kernelIndex;
            data.target = target;
            data.boundsMin = boundsMin;
            data.boundsSize = boundsSize;
            data.resolution = resolution;

            builder.UseTexture(target, AccessFlags.Write);
            // The result is consumed via the imported RT in a later pass/frame (the fog binds it as a
            // plain material texture, outside this graph's tracking), so the pass would otherwise be culled.
            builder.AllowPassCulling(false);
            builder.EnableAsyncCompute(useAsyncCompute && SystemInfo.supportsAsyncCompute);
            builder.SetRenderFunc((BakePassData d, ComputeGraphContext context) => ExecuteBakePass(d, context));
        }
    }

    private static void ExecuteBakePass(BakePassData data, ComputeGraphContext context)
    {
        ComputeCommandBuffer cmd = context.cmd;

        cmd.SetComputeTextureParam(data.computeShader, data.kernelIndex, BakeResultId, data.target);
        cmd.SetComputeVectorParam(data.computeShader, BakeBoundsMinId, data.boundsMin);
        cmd.SetComputeVectorParam(data.computeShader, BakeBoundsSizeId, data.boundsSize);
        cmd.SetComputeIntParams(data.computeShader, BakeResolutionId, data.resolution.x, data.resolution.y, data.resolution.z);

        // Kernel is [numthreads(4,4,4)].
        int groupsX = Mathf.CeilToInt(data.resolution.x / 4.0f);
        int groupsY = Mathf.CeilToInt(data.resolution.y / 4.0f);
        int groupsZ = Mathf.CeilToInt(data.resolution.z / 4.0f);
        cmd.DispatchCompute(data.computeShader, data.kernelIndex, groupsX, groupsY, groupsZ);
    }
}
