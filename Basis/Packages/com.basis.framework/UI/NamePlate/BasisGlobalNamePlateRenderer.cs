using System.Collections.Generic;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.Scripts.UI.NamePlate
{
    /// <summary>
    /// Merges every remote nameplate into a single panel mesh plus one text mesh per font atlas,
    /// so the whole lobby's name labels cost ~2 draw calls instead of ~2 per player.
    ///
    /// The merged geometry (topology + all vertex channels) is built with CombineMeshes only when
    /// the baked-plate set changes — a setup-time op, never per frame. Each vertex caches its
    /// plate-local position and an index into the plate snapshot. Every frame we just transform
    /// those local positions by the plates' current (already-billboarded) matrices and push the
    /// position buffer with revalidation disabled — no CombineMeshes, no reallocation. Hidden
    /// plates collapse to a degenerate point via a zero matrix, so visibility needs no rebuild.
    /// </summary>
    public static class BasisGlobalNamePlateRenderer
    {
        private const float NoCullExtent = 100000f;

        private static GameObject root;
        private static int layer = 5;
        private static Material panelMaterial;
        private static bool initialized;
        private static bool dirty;

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
        private static Matrix4x4[] matrices = System.Array.Empty<Matrix4x4>();
        private static Color32[] plateColors = System.Array.Empty<Color32>();

        private static Layer panel;
        private static readonly Dictionary<Material, Layer> textLayers = new();
        private static readonly List<Layer> textLayerList = new();

        private const MeshUpdateFlags FastUpdate =
            MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontValidateIndices |
            MeshUpdateFlags.DontNotifyMeshUsers;

        private sealed class Layer
        {
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Vector3[] LocalPos;
            public int[] PlateIdx;
            public Vector3[] WorldPos;
            public Color32[] ColorBuf; // panel only
            public int VertexCount;

            // Reused during a topology rebuild.
            public readonly List<CombineInstance> Combine = new(64);
            public readonly List<int> SrcPlate = new(64);
            private CombineInstance[] combineArray;

            public CombineInstance[] CombineExact()
            {
                int n = Combine.Count;
                if (combineArray == null || combineArray.Length != n) combineArray = new CombineInstance[n];
                for (int i = 0; i < n; i++) combineArray[i] = Combine[i];
                return combineArray;
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
                return;
            }

            root = new GameObject("BasisGlobalNamePlates");
            root.transform.SetParent(BasisDeviceManagement.Instance.transform, false);
            root.layer = layer;

            panel = NewLayer("Panels", panelMaterial);
            panel.ColorBuf = System.Array.Empty<Color32>();

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
            if (matrices.Length < plateCount) matrices = new Matrix4x4[plateCount];
            if (plateColors.Length < plateCount) plateColors = new Color32[plateCount];

            // Panel layer: one quad per plate.
            panel.Combine.Clear();
            panel.SrcPlate.Clear();
            for (int gi = 0; gi < plateCount; gi++)
            {
                panel.Combine.Add(new CombineInstance { mesh = snapshot[gi].GlobalPanelMesh });
                panel.SrcPlate.Add(gi);
            }
            BuildLayerGeometry(panel, true);

            // Text layers: bucket every plate's per-atlas meshes by material.
            for (int i = 0; i < textLayerList.Count; i++)
            {
                textLayerList[i].Combine.Clear();
                textLayerList[i].SrcPlate.Clear();
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
                }
            }
            for (int i = 0; i < textLayerList.Count; i++)
            {
                BuildLayerGeometry(textLayerList[i], false);
            }
        }

        /// <summary>
        /// Runs CombineMeshes once to produce the merged channels + topology, then caches each
        /// vertex's local position and owning plate index for the per-frame transform.
        /// </summary>
        private static void BuildLayerGeometry(Layer l, bool isPanel)
        {
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
            if (l.LocalPos == null || l.LocalPos.Length < vc)
            {
                l.LocalPos = new Vector3[vc];
                l.PlateIdx = new int[vc];
                l.WorldPos = new Vector3[vc];
                if (isPanel) l.ColorBuf = new Color32[vc];
            }
            l.Mesh.GetVertices(localScratch);
            for (int v = 0; v < vc; v++) l.LocalPos[v] = localScratch[v];

            // Fill per-vertex plate index from each combine entry's vertex span.
            int offset = 0;
            for (int e = 0; e < l.Combine.Count; e++)
            {
                int evc = l.Combine[e].mesh.vertexCount;
                int gi = l.SrcPlate[e];
                for (int j = 0; j < evc; j++) l.PlateIdx[offset + j] = gi;
                offset += evc;
            }

            l.Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * NoCullExtent);
        }

        private static readonly List<Vector3> localScratch = new(4096);

        private static void UpdateFrame()
        {
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

            UpdateLayer(panel, true);
            for (int i = 0; i < textLayerList.Count; i++) UpdateLayer(textLayerList[i], false);
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

        private static void UpdateLayer(Layer l, bool isPanel)
        {
            int vc = l.VertexCount;
            if (vc == 0)
            {
                SetLayerEnabled(l, false);
                return;
            }

            Vector3[] local = l.LocalPos;
            Vector3[] world = l.WorldPos;
            int[] idx = l.PlateIdx;

            if (isPanel)
            {
                Color32[] colors = l.ColorBuf;
                for (int v = 0; v < vc; v++)
                {
                    int gi = idx[v];
                    world[v] = matrices[gi].MultiplyPoint3x4(local[v]);
                    colors[v] = plateColors[gi];
                }
                l.Mesh.SetVertices(world, 0, vc, FastUpdate);
                l.Mesh.SetColors(colors, 0, vc, FastUpdate);
            }
            else
            {
                for (int v = 0; v < vc; v++)
                {
                    world[v] = matrices[idx[v]].MultiplyPoint3x4(local[v]);
                }
                l.Mesh.SetVertices(world, 0, vc, FastUpdate);
            }

            SetLayerEnabled(l, true);
        }

        private static readonly Matrix4x4 ZeroMatrix = new Matrix4x4(
            Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero);

        private static Layer GetTextLayer(Material mat)
        {
            if (textLayers.TryGetValue(mat, out Layer existing)) return existing;

            // Keep the panel just below the text so text paints on top within the transparent queue.
            if (panelMaterial != null && panelMaterial.renderQueue >= mat.renderQueue)
                panelMaterial.renderQueue = mat.renderQueue - 1;

            Layer l = NewLayer("Text", mat);
            textLayers.Add(mat, l);
            textLayerList.Add(l);
            return l;
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

        public static void Dispose()
        {
            if (!initialized) return;

            if (panel != null && panel.Mesh != null) Object.Destroy(panel.Mesh);
            foreach (Layer l in textLayerList)
            {
                if (l.Mesh != null) Object.Destroy(l.Mesh);
            }
            if (root != null) Object.Destroy(root);

            panel = null;
            textLayers.Clear();
            textLayerList.Clear();
            snapshot.Clear();
            localScratch.Clear();
            matrices = System.Array.Empty<Matrix4x4>();
            plateColors = System.Array.Empty<Color32>();
            panelMaterial = null;
            initialized = false;
            dirty = false;
        }
    }
}
