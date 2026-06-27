using System.Collections.Generic;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.Scripts.UI.NamePlate
{
    /// <summary>
    /// Merges every remote nameplate into a single panel mesh plus one text mesh per font atlas,
    /// so the whole lobby's name labels cost ~2 draw calls instead of ~2 per player.
    ///
    /// The merged geometry (topology + all vertex channels) is built with CombineMeshes only when
    /// the baked-plate set changes — a setup-time op, never per frame. Each layer is billboarded one
    /// of two ways:
    ///  - GPU (<see cref="GpuBillboardPanel"/> / <see cref="GpuBillboardText"/>): plate-local
    ///    positions + a per-vertex plate id are uploaded once, and each frame the CPU pushes only the
    ///    small per-plate matrix (+ panel color) buffers — the vertex shader does the transform.
    ///  - CPU (fallback): a Burst job transforms the cached local positions by the plates' current
    ///    matrices and pushes the position buffer with revalidation disabled.
    /// Hidden plates collapse to a degenerate point via a zero matrix, so visibility needs no rebuild.
    /// </summary>
    public static class BasisGlobalNamePlateRenderer
    {
        private const float NoCullExtent = 100000f;

        // Below this total vertex count the CPU per-vertex transform runs inline (still Burst-compiled
        // via .Run) — scheduling overhead would dominate the trivial work for tiny lobbies.
        private const int ParallelVertexThreshold = 2048;

        /// <summary>
        /// GPU-billboard the panel layer in its vertex shader instead of transforming + re-uploading
        /// its vertices on the CPU every frame. Flip to A/B against the CPU path (rebuilds next frame).
        /// </summary>
        public static bool GpuBillboardPanel = true;
        /// <summary>
        /// GPU-billboard the text layers (forked TMP SDF shader). Flip to A/B against the CPU path;
        /// the text layers are torn down + rebuilt on change (GPU/CPU use different materials).
        /// </summary>
        public static bool GpuBillboardText = true;

        private const string GpuKeyword = "BASIS_NAMEPLATE_GPU";
        private const string TextBillboardShaderName = "Basis/NamePlate/Text";
        private static readonly int PlateMatricesId = Shader.PropertyToID("_PlateMatrices");
        private static readonly int PlateColorsId = Shader.PropertyToID("_PlateColors");
        private static Shader textBillboardShader;

        private static GameObject root;
        private static int layer = 5;
        private static Material panelMaterial;
        private static bool initialized;
        private static bool dirty;
        private static bool panelKeywordApplied;
        private static bool textGpuApplied;

        // ---- Per-frame visibility culling (culled plates collapse to a degenerate point) ----
        /// <summary>Cull plates farther than this many metres from the viewer. 0 disables.</summary>
        public static float MaxDistance = 0f;
        /// <summary>Cull plates behind the viewer (and far off to the sides).</summary>
        public static bool CullBehind = true;
        /// <summary>dot(camForward, dirToPlate) below this is culled (-0.25 ≈ 105° off-forward).</summary>
        public static float BehindDotThreshold = -0.25f;
        /// <summary>Cull plates the viewer can't see through walls (linecast on <see cref="OcclusionMask"/>).</summary>
        public static bool CullOccluded = true;
        /// <summary>Environment layers for through-wall occlusion. Empty (0) disables it — set to your world/scenery layers.</summary>
        public static LayerMask OcclusionMask;

        // Per-frame plate data, indexed by snapshot order.
        private static readonly List<BasisRemoteNamePlate> snapshot = new(64);
        private static NativeArray<Matrix4x4> matrices;
        private static NativeArray<Color> plateColors;
        private static int plateCapacity;

        // Shared GPU per-plate buffers (matrices stored as 4 float4 columns each).
        private static GraphicsBuffer plateMatrixBuffer;
        private static GraphicsBuffer plateColorBuffer;
        private static int gpuBufferCapacity;

        private static Layer panel;
        private static readonly Dictionary<Material, Layer> textLayers = new();
        private static readonly List<Layer> textLayerList = new();
        // TMP atlas material -> our billboard clone (forked shader, same props/keywords/atlas).
        private static readonly Dictionary<Material, Material> textBillboardMats = new();

        private const MeshUpdateFlags FastUpdate =
            MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontValidateIndices |
            MeshUpdateFlags.DontNotifyMeshUsers;

        private sealed class Layer
        {
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public bool Gpu;
            public NativeArray<Vector3> LocalPos;
            public NativeArray<int> PlateIdx;
            public NativeArray<Vector3> WorldPos;
            public NativeArray<Color> ColorBuf; // CPU panel path only
            public int VertexCount;
            public int Capacity;

            // Reused during a topology rebuild.
            public readonly List<CombineInstance> Combine = new(64);
            public readonly List<int> SrcPlate = new(64);
            public readonly List<int> SrcVertexCount = new(64);
            private CombineInstance[] combineArray;

            public CombineInstance[] CombineExact()
            {
                int n = Combine.Count;
                if (combineArray == null || combineArray.Length != n) combineArray = new CombineInstance[n];
                for (int i = 0; i < n; i++) combineArray[i] = Combine[i];
                return combineArray;
            }

            public void EnsureCapacity(int vc, bool withColor)
            {
                if (LocalPos.IsCreated && Capacity >= vc) return;
                DisposeBuffers();
                Capacity = vc;
                LocalPos = new NativeArray<Vector3>(vc, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                PlateIdx = new NativeArray<int>(vc, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                WorldPos = new NativeArray<Vector3>(vc, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                if (withColor) ColorBuf = new NativeArray<Color>(vc, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            public void DisposeBuffers()
            {
                if (LocalPos.IsCreated) LocalPos.Dispose();
                if (PlateIdx.IsCreated) PlateIdx.Dispose();
                if (WorldPos.IsCreated) WorldPos.Dispose();
                if (ColorBuf.IsCreated) ColorBuf.Dispose();
                Capacity = 0;
            }
        }

        public static bool IsInitialized => initialized;

        /// <summary>Flags the merged geometry for a rebuild next frame (plate baked / removed).</summary>
        public static void MarkDirty() => dirty = true;

        public static void EnsureInitialized(Material panelMat, int plateLayer)
        {
            panelMaterial = panelMat;
            layer = plateLayer;

            if (initialized)
            {
                if (panel != null && panel.Renderer != null && panel.Renderer.sharedMaterial != panelMaterial)
                    panel.Renderer.sharedMaterial = panelMaterial;
                ApplyPanelKeyword();
                return;
            }

            if (GpuBillboardText && textBillboardShader == null)
            {
                textBillboardShader = Shader.Find(TextBillboardShaderName);
                if (textBillboardShader == null) GpuBillboardText = false; // shader stripped -> CPU text
            }
            textGpuApplied = GpuBillboardText;

            root = new GameObject("BasisGlobalNamePlates");
            root.transform.SetParent(BasisDeviceManagement.Instance.transform, false);
            root.layer = layer;

            panel = NewLayer("Panels", panelMaterial);
            ApplyPanelKeyword();

            dirty = true;
            initialized = true;
        }

        /// <summary>
        /// Rebuilds topology if the plate set changed, then transforms positions for the frame.
        /// Call once per frame after the plate transforms and colors are final.
        /// </summary>
        public static void Rebuild(BasisRemoteNamePlate[] plates, int count)
        {
            if (!initialized || panelMaterial == null) return;

            if (panelKeywordApplied != GpuBillboardPanel)
            {
                ApplyPanelKeyword();
                dirty = true;
            }
            if (textGpuApplied != GpuBillboardText)
            {
                TearDownTextLayers(); // GPU/CPU text use different materials; recreate them
                textGpuApplied = GpuBillboardText;
                dirty = true;
            }

            if (dirty)
            {
                RebuildTopology(plates, count);
                dirty = false;
            }

            if (snapshot.Count == 0)
            {
                SetLayerEnabled(panel, false);
                for (int i = 0; i < textLayerList.Count; i++) SetLayerEnabled(textLayerList[i], false);
                return;
            }

            UpdateFrame();
        }

        private static void RebuildTopology(BasisRemoteNamePlate[] plates, int count)
        {
            snapshot.Clear();
            for (int i = 0; i < count; i++)
            {
                BasisRemoteNamePlate p = plates[i];
                if (p != null && p.HasGlobalParts && p.GlobalPanelMesh != null) snapshot.Add(p);
            }

            int plateCount = snapshot.Count;
            EnsurePlateCapacity(plateCount);

            // Panel layer: one quad per plate.
            panel.Combine.Clear();
            panel.SrcPlate.Clear();
            panel.SrcVertexCount.Clear();
            for (int gi = 0; gi < plateCount; gi++)
            {
                Mesh m = snapshot[gi].GlobalPanelMesh;
                panel.Combine.Add(new CombineInstance { mesh = m });
                panel.SrcPlate.Add(gi);
                panel.SrcVertexCount.Add(m.vertexCount);
            }
            BuildLayerGeometry(panel, true);

            // Text layers: bucket every plate's per-atlas meshes by material.
            for (int i = 0; i < textLayerList.Count; i++)
            {
                textLayerList[i].Combine.Clear();
                textLayerList[i].SrcPlate.Clear();
                textLayerList[i].SrcVertexCount.Clear();
            }
            for (int gi = 0; gi < plateCount; gi++)
            {
                BasisRemoteNamePlate p = snapshot[gi];
                Mesh[] meshes = p.GlobalTextMeshes;
                Material[] mats = p.GlobalTextMaterials;
                if (meshes == null || mats == null) continue;

                int parts = Mathf.Min(meshes.Length, mats.Length);
                for (int k = 0; k < parts; k++)
                {
                    if (meshes[k] == null || mats[k] == null) continue;
                    Layer textLayer = GetTextLayer(mats[k]);
                    textLayer.Combine.Add(new CombineInstance { mesh = meshes[k] });
                    textLayer.SrcPlate.Add(gi);
                    textLayer.SrcVertexCount.Add(meshes[k].vertexCount);
                }
            }
            for (int i = 0; i < textLayerList.Count; i++)
            {
                BuildLayerGeometry(textLayerList[i], false);
            }
        }

        /// <summary>
        /// Runs CombineMeshes once to produce the merged channels + topology. For the GPU path it
        /// writes the owning plate id into a UV channel and leaves positions plate-local; for the CPU
        /// path it caches each vertex's local position and plate index for the per-frame transform.
        /// </summary>
        private static void BuildLayerGeometry(Layer l, bool isPanel)
        {
            l.Gpu = isPanel ? GpuBillboardPanel : (GpuBillboardText && textBillboardShader != null);

            if (l.Combine.Count == 0)
            {
                l.VertexCount = 0;
                SetLayerEnabled(l, false);
                return;
            }

            // useMatrices:false keeps each plate's geometry in its own local space.
            l.Mesh.Clear();
            l.Mesh.CombineMeshes(l.CombineExact(), true, false);

            int vc = l.Mesh.vertexCount;
            l.VertexCount = vc;

            if (l.Gpu)
            {
                // Positions stay plate-local in the mesh; only the per-vertex plate id is needed.
                // Panel uses UV1 (mesh has UV0 only); text uses UV2 (TMP already uses UV0 + UV1).
                int channel = isPanel ? 1 : 2;
                var ids = new NativeArray<Vector2>(vc, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                int o = 0;
                for (int e = 0; e < l.SrcPlate.Count; e++)
                {
                    int evc = l.SrcVertexCount[e];
                    float gi = l.SrcPlate[e];
                    for (int j = 0; j < evc; j++) ids[o + j] = new Vector2(gi, 0f);
                    o += evc;
                }
                l.Mesh.SetUVs(channel, ids);
                ids.Dispose();
            }
            else
            {
                l.EnsureCapacity(vc, isPanel);

                // Read the combined local positions straight into the native buffer — no List indexer.
                using (Mesh.MeshDataArray mda = Mesh.AcquireReadOnlyMeshData(l.Mesh))
                {
                    mda[0].GetVertices(l.LocalPos.GetSubArray(0, vc));
                }

                // Fill per-vertex plate index from each combine entry's cached vertex span.
                int offset = 0;
                NativeArray<int> idx = l.PlateIdx;
                int entries = l.SrcPlate.Count;
                for (int e = 0; e < entries; e++)
                {
                    int evc = l.SrcVertexCount[e];
                    int gi = l.SrcPlate[e];
                    for (int j = 0; j < evc; j++) idx[offset + j] = gi;
                    offset += evc;
                }
            }

            l.Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * NoCullExtent);
        }

        private static void UpdateFrame()
        {
            if (!matrices.IsCreated) return;

            Matrix4x4 worldToLocal = root.transform.worldToLocalMatrix;
            int plateCount = snapshot.Count;

            bool canCull = BasisLocalCameraDriver.CameraInstance != null;
            Vector3 camPos = canCull ? BasisLocalCameraDriver.Position : Vector3.zero;
            Vector3 camFwd = canCull ? BasisLocalCameraDriver.Forward().normalized : Vector3.forward;
            float maxDistSqr = (canCull && MaxDistance > 0f) ? MaxDistance * MaxDistance : float.MaxValue;
            bool cullBehind = canCull && CullBehind;
            bool cullOccluded = canCull && CullOccluded && OcclusionMask.value != 0;

            for (int gi = 0; gi < plateCount; gi++)
            {
                BasisRemoteNamePlate p = snapshot[gi];
                if (p != null && p.IsGloballyRenderable &&
                    IsPlateVisible(p, camPos, camFwd, maxDistSqr, cullBehind, cullOccluded))
                {
                    matrices[gi] = worldToLocal * p.Self.localToWorldMatrix;
                    plateColors[gi] = p.CurrentColor;
                }
                else
                {
                    // Collapse hidden / culled / destroyed plates to a point (zero-area, not rasterized).
                    matrices[gi] = ZeroMatrix;
                }
            }

            // Any GPU layer? Upload the shared per-plate buffers once (before scheduling CPU jobs that
            // read the same matrices on worker threads).
            bool anyGpu = panel.Gpu;
            for (int i = 0; i < textLayerList.Count; i++) anyGpu |= textLayerList[i].Gpu;
            if (anyGpu)
            {
                EnsureGpuBuffers(plateCapacity);
                // Matrix4x4 is column-major in memory, so reinterpreting to float4 yields the 4 columns
                // per plate the shader expects. Colors are Color (== float4 layout).
                plateMatrixBuffer.SetData(matrices.Reinterpret<float4>(UnsafeUtility.SizeOf<Matrix4x4>()), 0, 0, plateCount * 4);
                if (panel.Gpu) plateColorBuffer.SetData(plateColors.Reinterpret<float4>(), 0, 0, plateCount);
            }

            NativeArray<float4x4> mtx = matrices.Reinterpret<float4x4>();

            // CPU transform for any layer not GPU-billboarded.
            int total = panel.Gpu ? 0 : panel.VertexCount;
            for (int i = 0; i < textLayerList.Count; i++)
                if (!textLayerList[i].Gpu) total += textLayerList[i].VertexCount;

            if (total >= ParallelVertexThreshold)
            {
                JobHandle deps = default;
                if (!panel.Gpu) deps = ScheduleLayer(panel, true, mtx, deps);
                for (int i = 0; i < textLayerList.Count; i++)
                    if (!textLayerList[i].Gpu) deps = ScheduleLayer(textLayerList[i], false, mtx, deps);
                deps.Complete();
            }
            else
            {
                if (!panel.Gpu) RunLayer(panel, true, mtx);
                for (int i = 0; i < textLayerList.Count; i++)
                    if (!textLayerList[i].Gpu) RunLayer(textLayerList[i], false, mtx);
            }

            FinalizeLayer(panel, true);
            for (int i = 0; i < textLayerList.Count; i++) FinalizeLayer(textLayerList[i], false);
        }

        /// <summary>GPU layers just toggle their renderer; CPU layers push the transformed buffers.</summary>
        private static void FinalizeLayer(Layer l, bool isPanel)
        {
            if (l.Gpu) SetLayerEnabled(l, l.VertexCount > 0);
            else PushLayer(l, isPanel);
        }

        private static JobHandle ScheduleLayer(Layer l, bool isPanel, NativeArray<float4x4> mtx, JobHandle dep)
        {
            int vc = l.VertexCount;
            if (vc == 0) return dep;

            NativeArray<float3> local = l.LocalPos.Reinterpret<float3>();
            NativeArray<float3> world = l.WorldPos.Reinterpret<float3>();

            JobHandle h;
            if (isPanel)
            {
                h = new TransformColorJob
                {
                    Matrices = mtx,
                    PlateIdx = l.PlateIdx,
                    Local = local,
                    World = world,
                    PlateColors = plateColors.Reinterpret<float4>(),
                    Colors = l.ColorBuf.Reinterpret<float4>()
                }.Schedule(vc, 512);
            }
            else
            {
                h = new TransformJob
                {
                    Matrices = mtx,
                    PlateIdx = l.PlateIdx,
                    Local = local,
                    World = world
                }.Schedule(vc, 512);
            }
            return JobHandle.CombineDependencies(dep, h);
        }

        private static void RunLayer(Layer l, bool isPanel, NativeArray<float4x4> mtx)
        {
            int vc = l.VertexCount;
            if (vc == 0) return;

            NativeArray<float3> local = l.LocalPos.Reinterpret<float3>();
            NativeArray<float3> world = l.WorldPos.Reinterpret<float3>();

            if (isPanel)
            {
                new TransformColorJob
                {
                    Matrices = mtx,
                    PlateIdx = l.PlateIdx,
                    Local = local,
                    World = world,
                    PlateColors = plateColors.Reinterpret<float4>(),
                    Colors = l.ColorBuf.Reinterpret<float4>()
                }.Run(vc);
            }
            else
            {
                new TransformJob
                {
                    Matrices = mtx,
                    PlateIdx = l.PlateIdx,
                    Local = local,
                    World = world
                }.Run(vc);
            }
        }

        private static void PushLayer(Layer l, bool isPanel)
        {
            int vc = l.VertexCount;
            if (vc == 0)
            {
                SetLayerEnabled(l, false);
                return;
            }

            l.Mesh.SetVertices(l.WorldPos, 0, vc, FastUpdate);
            if (isPanel) l.Mesh.SetColors(l.ColorBuf, 0, vc, FastUpdate);
            SetLayerEnabled(l, true);
        }

        private static bool IsPlateVisible(BasisRemoteNamePlate p, Vector3 camPos, Vector3 camFwd,
            float maxDistSqr, bool cullBehind, bool cullOccluded)
        {
            Vector3 platePos = p.Self.position;
            Vector3 toPlate = platePos - camPos;

            float distSqr = toPlate.sqrMagnitude;
            if (distSqr > maxDistSqr) return false; // too far

            if (cullBehind && distSqr > 1e-6f)
            {
                // cos(angle to plate) = dot / dist; cull when below threshold (behind / far to the side).
                float along = Vector3.Dot(camFwd, toPlate);
                if (along < BehindDotThreshold * Mathf.Sqrt(distSqr)) return false;
            }

            if (cullOccluded && Physics.Linecast(camPos, platePos, OcclusionMask, QueryTriggerInteraction.Ignore))
                return false; // blocked by a wall

            return true;
        }

        private static readonly Matrix4x4 ZeroMatrix = new Matrix4x4(
            Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero);

        private static void EnsurePlateCapacity(int n)
        {
            if (matrices.IsCreated && plateCapacity >= n) return;
            if (matrices.IsCreated) matrices.Dispose();
            if (plateColors.IsCreated) plateColors.Dispose();
            plateCapacity = Mathf.Max(16, Mathf.NextPowerOfTwo(n));
            matrices = new NativeArray<Matrix4x4>(plateCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            plateColors = new NativeArray<Color>(plateCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureGpuBuffers(int n)
        {
            if (plateMatrixBuffer != null && gpuBufferCapacity >= n) return;
            plateMatrixBuffer?.Dispose();
            plateColorBuffer?.Dispose();
            gpuBufferCapacity = Mathf.Max(16, Mathf.NextPowerOfTwo(n));
            int f4 = UnsafeUtility.SizeOf<float4>();
            plateMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, gpuBufferCapacity * 4, f4);
            plateColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, gpuBufferCapacity, f4);
            RebindGpuBuffers();
        }

        private static void RebindGpuBuffers()
        {
            if (plateMatrixBuffer == null) return;
            if (panelMaterial != null)
            {
                panelMaterial.SetBuffer(PlateMatricesId, plateMatrixBuffer);
                panelMaterial.SetBuffer(PlateColorsId, plateColorBuffer);
            }
            foreach (Material m in textBillboardMats.Values)
                if (m != null) m.SetBuffer(PlateMatricesId, plateMatrixBuffer);
        }

        private static void ApplyPanelKeyword()
        {
            panelKeywordApplied = GpuBillboardPanel;
            if (panelMaterial == null) return;
            if (GpuBillboardPanel) panelMaterial.EnableKeyword(GpuKeyword);
            else panelMaterial.DisableKeyword(GpuKeyword);
        }

        private static Layer GetTextLayer(Material tmpMat)
        {
            if (textLayers.TryGetValue(tmpMat, out Layer existing)) return existing;

            Material renderMat = (GpuBillboardText && textBillboardShader != null)
                ? GetTextBillboardMaterial(tmpMat)
                : tmpMat;

            // Keep the panel just below the text so text paints on top within the transparent queue.
            if (panelMaterial != null && panelMaterial.renderQueue >= renderMat.renderQueue)
                panelMaterial.renderQueue = renderMat.renderQueue - 1;

            Layer l = NewLayer("Text", renderMat);
            textLayers.Add(tmpMat, l);
            textLayerList.Add(l);
            return l;
        }

        /// <summary>Clones a TMP atlas material onto the billboard shader (same props/keywords/atlas).</summary>
        private static Material GetTextBillboardMaterial(Material tmpMat)
        {
            if (textBillboardMats.TryGetValue(tmpMat, out Material existing) && existing != null)
                return existing;

            var bm = new Material(tmpMat) { name = tmpMat.name + " (NamePlate GPU)" };
            bm.shader = textBillboardShader;
            if (plateMatrixBuffer != null) bm.SetBuffer(PlateMatricesId, plateMatrixBuffer);
            textBillboardMats[tmpMat] = bm;
            return bm;
        }

        private static Layer NewLayer(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.layer = layer;

            var mesh = new Mesh { name = "Global" + name + "Mesh", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            mr.enabled = false;

            return new Layer { Renderer = mr, Mesh = mesh };
        }

        private static void SetLayerEnabled(Layer l, bool on)
        {
            if (l != null && l.Renderer != null && l.Renderer.enabled != on) l.Renderer.enabled = on;
        }

        /// <summary>Destroys the text layer GameObjects/meshes so the next rebuild recreates them with
        /// the materials matching the current <see cref="GpuBillboardText"/> mode.</summary>
        private static void TearDownTextLayers()
        {
            for (int i = 0; i < textLayerList.Count; i++)
            {
                Layer l = textLayerList[i];
                l.DisposeBuffers();
                if (l.Mesh != null) Object.Destroy(l.Mesh);
                if (l.Renderer != null) Object.Destroy(l.Renderer.gameObject);
            }
            textLayerList.Clear();
            textLayers.Clear();
        }

        public static void Dispose()
        {
            if (!initialized) return;

            if (panel != null)
            {
                if (panel.Mesh != null) Object.Destroy(panel.Mesh);
                panel.DisposeBuffers();
            }
            foreach (Layer l in textLayerList)
            {
                if (l.Mesh != null) Object.Destroy(l.Mesh);
                l.DisposeBuffers();
            }
            if (root != null) Object.Destroy(root);

            if (matrices.IsCreated) matrices.Dispose();
            if (plateColors.IsCreated) plateColors.Dispose();
            plateCapacity = 0;

            plateMatrixBuffer?.Dispose();
            plateMatrixBuffer = null;
            plateColorBuffer?.Dispose();
            plateColorBuffer = null;
            gpuBufferCapacity = 0;

            foreach (Material m in textBillboardMats.Values)
                if (m != null) Object.Destroy(m);
            textBillboardMats.Clear();

            panel = null;
            textLayers.Clear();
            textLayerList.Clear();
            snapshot.Clear();
            panelMaterial = null;
            panelKeywordApplied = false;
            initialized = false;
            dirty = false;
        }

        [BurstCompile]
        private struct TransformJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4x4> Matrices;
            [ReadOnly] public NativeArray<int> PlateIdx;
            [ReadOnly] public NativeArray<float3> Local;
            [WriteOnly] public NativeArray<float3> World;

            public void Execute(int v)
            {
                World[v] = math.transform(Matrices[PlateIdx[v]], Local[v]);
            }
        }

        [BurstCompile]
        private struct TransformColorJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4x4> Matrices;
            [ReadOnly] public NativeArray<int> PlateIdx;
            [ReadOnly] public NativeArray<float3> Local;
            [WriteOnly] public NativeArray<float3> World;
            [ReadOnly] public NativeArray<float4> PlateColors;
            [WriteOnly] public NativeArray<float4> Colors;

            public void Execute(int v)
            {
                int gi = PlateIdx[v];
                World[v] = math.transform(Matrices[gi], Local[v]);
                Colors[v] = PlateColors[gi];
            }
        }
    }
}
