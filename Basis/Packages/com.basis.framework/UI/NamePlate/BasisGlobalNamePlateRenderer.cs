using System.Collections.Generic;
using Basis.Scripts.Device_Management;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.Scripts.UI.NamePlate
{
    /// <summary>
    /// Merges every visible remote nameplate into a single world-space panel mesh plus one
    /// text mesh per font atlas, so the whole lobby's name labels cost ~2 draw calls instead
    /// of ~2 per player. Rebuilt each frame from the plates' already-billboarded
    /// <see cref="BasisRemoteNamePlate.Self"/> transforms (driven by MappedNameplateApplyJob)
    /// after the remote bone jobs complete. Per-plate tint rides in the panel vertex color.
    /// </summary>
    public static class BasisGlobalNamePlateRenderer
    {
        private static GameObject root;
        private static int layer = 5;

        // Panel layer (single shared vertex-color material).
        private static MeshRenderer panelRenderer;
        private static Mesh panelMesh;
        private static readonly List<CombineInstance> panelCombines = new(64);
        private static readonly List<Color32> panelColorsPerPlate = new(64);
        private static CombineInstance[] panelArray;
        private static Color32[] panelColorBuffer = System.Array.Empty<Color32>();

        // One text layer per atlas material (usually just the Latin atlas).
        private static readonly Dictionary<Material, TextLayer> textLayers = new();
        private static readonly List<TextLayer> textLayerList = new();

        private static Material panelMaterial;
        private static bool initialized;

        private sealed class TextLayer
        {
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public readonly List<CombineInstance> Combines = new(64);
            private CombineInstance[] array;

            public void Clear() => Combines.Clear();

            public void Add(Mesh mesh, Matrix4x4 matrix)
            {
                Combines.Add(new CombineInstance { mesh = mesh, transform = matrix });
            }

            public void Build()
            {
                int n = Combines.Count;
                if (n == 0)
                {
                    if (Renderer.enabled) Renderer.enabled = false;
                    return;
                }

                if (array == null || array.Length != n) array = new CombineInstance[n];
                for (int i = 0; i < n; i++) array[i] = Combines[i];

                Mesh.Clear();
                Mesh.CombineMeshes(array, true, true);
                if (!Renderer.enabled) Renderer.enabled = true;
            }
        }

        public static bool IsInitialized => initialized;

        /// <summary>
        /// Lazily builds the persistent root + panel layer. <paramref name="panelMat"/> is the
        /// Basis/NamePlate/Panel material; if null the system can't render and callers should
        /// fall back to per-plate rendering.
        /// </summary>
        public static void EnsureInitialized(Material panelMat, int plateLayer)
        {
            panelMaterial = panelMat;
            layer = plateLayer;

            if (initialized)
            {
                if (panelRenderer != null && panelRenderer.sharedMaterial != panelMaterial)
                    panelRenderer.sharedMaterial = panelMaterial;
                return;
            }

            root = new GameObject("BasisGlobalNamePlates");
            root.transform.SetParent(BasisDeviceManagement.Instance.transform, false);
            root.layer = layer;

            panelMesh = NewDynamicMesh("GlobalNamePlatePanelMesh");
            panelRenderer = NewLayerRenderer("Panels", panelMesh, panelMaterial, out _);

            initialized = true;
        }

        /// <summary>
        /// Rebuilds the merged meshes from the live plate array. Call once per frame after the
        /// nameplate <see cref="BasisRemoteNamePlate.Self"/> transforms and per-plate colors are
        /// final (end of CompleteNamePlates).
        /// </summary>
        public static void Rebuild(BasisRemoteNamePlate[] plates, int count)
        {
            if (!initialized || panelMaterial == null) return;

            panelCombines.Clear();
            panelColorsPerPlate.Clear();
            for (int i = 0; i < textLayerList.Count; i++) textLayerList[i].Clear();

            // Bake plate world poses into the root's local space so the result is correct no
            // matter where the (BasisDeviceManagement-parented) root sits in the hierarchy.
            Matrix4x4 worldToLocal = root.transform.worldToLocalMatrix;

            for (int i = 0; i < count; i++)
            {
                BasisRemoteNamePlate p = plates[i];
                if (p == null || !p.IsGloballyRenderable) continue;

                Matrix4x4 matrix = worldToLocal * p.Self.localToWorldMatrix;

                panelCombines.Add(new CombineInstance { mesh = p.GlobalPanelMesh, transform = matrix });
                panelColorsPerPlate.Add(p.CurrentColor);

                Mesh[] textMeshes = p.GlobalTextMeshes;
                Material[] textMats = p.GlobalTextMaterials;
                if (textMeshes == null || textMats == null) continue;

                int parts = Mathf.Min(textMeshes.Length, textMats.Length);
                for (int k = 0; k < parts; k++)
                {
                    Mesh tm = textMeshes[k];
                    Material mat = textMats[k];
                    if (tm == null || mat == null) continue;
                    GetTextLayer(mat).Add(tm, matrix);
                }
            }

            BuildPanel();

            for (int i = 0; i < textLayerList.Count; i++) textLayerList[i].Build();
        }

        private static void BuildPanel()
        {
            int n = panelCombines.Count;
            if (n == 0)
            {
                if (panelRenderer.enabled) panelRenderer.enabled = false;
                return;
            }

            if (panelArray == null || panelArray.Length != n) panelArray = new CombineInstance[n];

            int totalVerts = 0;
            for (int i = 0; i < n; i++)
            {
                CombineInstance ci = panelCombines[i];
                panelArray[i] = ci;
                totalVerts += ci.mesh.vertexCount;
            }

            panelMesh.Clear();
            panelMesh.CombineMeshes(panelArray, true, true);

            // Expand per-plate tint over each plate's vertex range. Plate i occupies a
            // contiguous block because CombineMeshes concatenates instances in order.
            if (panelColorBuffer.Length < totalVerts) panelColorBuffer = new Color32[totalVerts];
            int offset = 0;
            for (int i = 0; i < n; i++)
            {
                int vc = panelArray[i].mesh.vertexCount;
                Color32 c = panelColorsPerPlate[i];
                for (int j = 0; j < vc; j++) panelColorBuffer[offset + j] = c;
                offset += vc;
            }
            panelMesh.SetColors(panelColorBuffer, 0, totalVerts);

            if (!panelRenderer.enabled) panelRenderer.enabled = true;
        }

        private static TextLayer GetTextLayer(Material mat)
        {
            if (textLayers.TryGetValue(mat, out TextLayer layerForMat)) return layerForMat;

            // Panels are one draw, text is another; same transparent queue would sort ambiguously
            // (both meshes span the world). Pin the panel just below the text so text paints on top,
            // matching the old single-mesh submesh order.
            if (panelMaterial != null && panelMaterial.renderQueue >= mat.renderQueue)
            {
                panelMaterial.renderQueue = mat.renderQueue - 1;
            }

            Mesh mesh = NewDynamicMesh("GlobalNamePlateTextMesh");
            MeshRenderer mr = NewLayerRenderer("Text", mesh, mat, out _);
            mr.enabled = false;

            layerForMat = new TextLayer { Renderer = mr, Mesh = mesh };
            textLayers.Add(mat, layerForMat);
            textLayerList.Add(layerForMat);
            return layerForMat;
        }

        private static Mesh NewDynamicMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            return mesh;
        }

        private static MeshRenderer NewLayerRenderer(string name, Mesh mesh, Material material, out MeshFilter filter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.layer = layer;

            filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return mr;
        }

        public static void Dispose()
        {
            if (!initialized) return;

            foreach (TextLayer textLayer in textLayerList)
            {
                if (textLayer.Mesh != null) Object.Destroy(textLayer.Mesh);
            }
            textLayers.Clear();
            textLayerList.Clear();

            if (panelMesh != null) Object.Destroy(panelMesh);
            if (root != null) Object.Destroy(root);

            panelMesh = null;
            panelRenderer = null;
            panelArray = null;
            panelColorBuffer = System.Array.Empty<Color32>();
            panelCombines.Clear();
            panelColorsPerPlate.Clear();
            panelMaterial = null;
            initialized = false;
        }
    }
}
