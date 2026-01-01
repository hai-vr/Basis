using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using System.Collections.Generic;
using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Scripts.UI.NamePlate
{
    /// <summary>
    /// Handles the generation and management of remote player nameplates,
    /// including mesh creation, material selection, and text rendering.
    /// </summary>
    public class BasisRemoteNamePlateDriver : MonoBehaviour
    {
        /// <summary>
        /// Singleton instance of the <see cref="BasisRemoteNamePlateDriver"/>.
        /// </summary>
        public static BasisRemoteNamePlateDriver Instance;

        /// <summary>
        /// Default color for the nameplate when idle.
        /// </summary>
        public Color NormalColor;

        /// <summary>
        /// Color used when the player is talking.
        /// </summary>
        public Color IsTalkingColor;

        /// <summary>
        /// Color used when the player is out of range.
        /// </summary>
        public Color OutOfRangeColor;

        /// <summary>
        /// Duration of the color transition animation in seconds.
        /// </summary>
        [SerializeField]
        public static float transitionDuration = 0.3f;

        /// <summary>
        /// Delay before returning to the default color after an event.
        /// </summary>
        [SerializeField]
        public static float returnDelay = 0.4f;

        /// <summary>
        /// Cached static reference for the default color.
        /// </summary>
        public static Color StaticNormalColor;

        /// <summary>
        /// Cached static reference for the talking color.
        /// </summary>
        public static Color StaticIsTalkingColor;

        /// <summary>
        /// Cached static reference for the out-of-range color.
        /// </summary>
        public static Color StaticOutOfRangeColor;

        /// <summary>
        /// Reference to the text mesh used for displaying player names.
        /// </summary>
        public TextMeshPro Text;

        /// <summary>
        /// Transparent material used for nameplates.
        /// </summary>
        public Material TransParentNamePlateMaterial;

        /// <summary>
        /// Opaque material used for nameplates.
        /// </summary>
        public Material OpaqueNamePlateMaterial;

        /// <summary>
        /// The currently selected material for the nameplate,
        /// determined by device type (mobile vs. non-mobile).
        /// </summary>
        [HideInInspector]
        public Material SelectedNamePlateMaterial;

        /// <summary>
        /// The mesh with rounded corners for nameplates.
        /// </summary>
        [HideInInspector]
        public Mesh RoundedCornersMesh;

        /// <summary>
        /// Controls the curvature of the rounded corners (0–1 scale).
        /// 0 = sharp rectangle, 1 = maximum rounding given width/height.
        /// </summary>
        [Range(0f, 1f)]
        public float RoundEdges = 0.5f;

        /// <summary>
        /// Number of vertices used per rounded corner (must be greater than 2).
        /// </summary>
        public int CornerVertexCount = 8;

        /// <summary>
        /// Z-axis offset applied to the generated mesh.
        /// </summary>
        public float zOffset = 0.06f;

        /// <summary>
        /// Unity lifecycle method. Initializes the singleton,
        /// assigns materials, caches colors, and generates the rounded mesh.
        /// </summary>
        public void Awake()
        {
            Instance = this;

            if (BasisDeviceManagement.IsMobileHardware())
            {
                SelectedNamePlateMaterial = OpaqueNamePlateMaterial;
            }
            else
            {
                SelectedNamePlateMaterial = TransParentNamePlateMaterial;
            }

            StaticNormalColor = NormalColor;
            StaticIsTalkingColor = IsTalkingColor;
            StaticOutOfRangeColor = OutOfRangeColor;

            RoundedCornersMesh = GenerateRoundedQuad();
        }

        /// <summary>
        /// Generates the text mesh for a remote player's nameplate.
        /// </summary>
        /// <param name="remotePlayer">The remote player whose display name will be shown.</param>
        /// <param name="namePlate">The target nameplate object to assign the mesh to.</param>
        public void GenerateTextFactory(BasisRemotePlayer remotePlayer, BasisRemoteNamePlate namePlate)
        {
            Text.gameObject.SetActive(true);
            Text.text = remotePlayer.DisplayName;
            Text.ForceMeshUpdate();

            // Generate a new mesh from the text
            Mesh textMesh = Instantiate(Text.mesh);

            // Assign to nameplate
            namePlate.bakedMesh = textMesh;
            namePlate.Filter.sharedMesh = textMesh;

            // Combine meshes
            CreateFinalMesh(namePlate);
            Text.gameObject.SetActive(false);
        }

        /// <summary>
        /// Combines the rounded corner mesh and the text mesh
        /// into a single final mesh for rendering on the nameplate.
        /// </summary>
        /// <param name="namePlate">The nameplate object to update with the combined mesh.</param>
        private void CreateFinalMesh(BasisRemoteNamePlate namePlate)
        {
            CombineInstance[] combine = new CombineInstance[2];

            combine[0] = new CombineInstance
            {
                mesh = RoundedCornersMesh,
                transform = Matrix4x4.identity
            };

            combine[1] = new CombineInstance
            {
                mesh = namePlate.bakedMesh,
                transform = Matrix4x4.identity
            };

            Mesh combinedMesh = new Mesh
            {
                name = "CombinedNameplateMesh"
            };
            combinedMesh.CombineMeshes(combine, false); // false = keep submeshes

            namePlate.Filter.sharedMesh = combinedMesh;
            namePlate.Renderer.materials = new Material[]
            {
                SelectedNamePlateMaterial,
                namePlate.Renderer.material
            };
        }

        /// <summary>
        /// Generates a rectangular mesh with rounded corners for the nameplate background.
        /// </summary>
        /// <returns>A mesh with rounded edges and UV mapping applied.</returns>
        public Mesh GenerateRoundedQuad()
        {
            int cornerCount = Mathf.Max(3, CornerVertexCount); // safety clamp
            int ringVertexCount = cornerCount * 4;
            int vertexCount = ringVertexCount + 1;
            int triangleCount = ringVertexCount;

            Vector3[] m_Vertices = new Vector3[vertexCount];
            Vector3[] m_Normals = new Vector3[vertexCount];
            Vector2[] m_UV = new Vector2[vertexCount];
            int[] m_Triangles = new int[triangleCount * 3];

            // Base dimensions of the quad
            float halfWidth = 30f;
            float halfHeight = 4.5f;
            float width = halfWidth * 2f;
            float height = halfHeight * 2f;

            // Max possible radius before the rounded corners break the shape
            float maxRadius = Mathf.Min(halfWidth, halfHeight);

            // Interpret RoundEdges as a 0–1 slider
            float radius = Mathf.Clamp01(RoundEdges) * maxRadius;

            float angleStep = Mathf.PI * 0.5f / (cornerCount - 1);
            Vector2 uvOffset = new Vector2(0.5f, 0.5f);
            Vector2 uvScale = new Vector2(1f / width, 1f / height);

            // Center vertex
            m_Vertices[0] = new Vector3(0, 0, zOffset);
            m_UV[0] = uvOffset;
            m_Normals[0] = -Vector3.forward;

            for (int CornerIndex = 0; CornerIndex < cornerCount; CornerIndex++)
            {
                float angle = CornerIndex * angleStep;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                Vector2 tl = new Vector2(
                    -halfWidth + (1f - cos) * radius,
                    halfHeight - (1f - sin) * radius
                );

                Vector2 tr = new Vector2(
                    halfWidth - (1f - sin) * radius,
                    halfHeight - (1f - cos) * radius
                );

                Vector2 br = new Vector2(
                    halfWidth - (1f - cos) * radius,
                    -halfHeight + (1f - sin) * radius
                );

                Vector2 bl = new Vector2(
                    -halfWidth + (1f - sin) * radius,
                    -halfHeight + (1f - cos) * radius
                );

                int baseIndex = 1 + CornerIndex;

                m_Vertices[baseIndex] = new Vector3(tl.x, tl.y, zOffset);
                m_Vertices[baseIndex + cornerCount] = new Vector3(tr.x, tr.y, zOffset);
                m_Vertices[baseIndex + cornerCount * 2] = new Vector3(br.x, br.y, zOffset);
                m_Vertices[baseIndex + cornerCount * 3] = new Vector3(bl.x, bl.y, zOffset);

                m_UV[baseIndex] = tl * uvScale + uvOffset;
                m_UV[baseIndex + cornerCount] = tr * uvScale + uvOffset;
                m_UV[baseIndex + cornerCount * 2] = br * uvScale + uvOffset;
                m_UV[baseIndex + cornerCount * 3] = bl * uvScale + uvOffset;

                m_Normals[baseIndex] = -Vector3.forward;
                m_Normals[baseIndex + cornerCount] = -Vector3.forward;
                m_Normals[baseIndex + cornerCount * 2] = -Vector3.forward;
                m_Normals[baseIndex + cornerCount * 3] = -Vector3.forward;
            }

            // Triangle fan around center
            for (int i = 0; i < ringVertexCount; i++)
            {
                int triIndex = i * 3;
                m_Triangles[triIndex] = 0;
                m_Triangles[triIndex + 1] = 1 + i;
                m_Triangles[triIndex + 2] = 1 + ((i + 1) % ringVertexCount);
            }

            Mesh mesh = new Mesh
            {
                name = "Rounded NamePlate Quad",
                vertices = m_Vertices,
                normals = m_Normals,
                uv = m_UV,
                triangles = m_Triangles
            };

            return mesh;
        }

        // =========================================================
        // Jobified pulsing system (safe structural changes + JIT resize)
        // =========================================================

        // Plates and index mapping
        private static readonly List<BasisRemoteNamePlate> plates = new();
        private static readonly Dictionary<BasisRemoteNamePlate, int> indexOf = new();

        // Pending structural changes (safe to call any time on main thread)
        private static readonly List<BasisRemoteNamePlate> pendingAdd = new();
        private static readonly List<BasisRemoteNamePlate> pendingRemove = new();

        // Native state (persistent)
        private static NativeArray<ushort> isPulsing;
        private static NativeArray<ushort> isVisible;
        private static NativeArray<double> startTime;
        private static NativeArray<float4> talkColor;

        // Outputs
        private static NativeArray<float4> outColor;
        private static NativeArray<ushort> outHasChange;
        private static NativeArray<ushort> outStopPulsing;

        private static bool allocated;

        public static JobHandle handle;
        public static int count;

        /// <summary>
        /// Request registering a plate. The actual add occurs at a safe sync point (before the job schedules).
        /// </summary>
        public static void Register(BasisRemoteNamePlate p)
        {
            if (p == null) return;
            pendingAdd.Add(p);
        }

        /// <summary>
        /// Request unregistering a plate. The actual remove occurs at a safe sync point (before the job schedules).
        /// </summary>
        public static void Unregister(BasisRemoteNamePlate p)
        {
            if (p == null) return;
            pendingRemove.Add(p);
        }

        /// <summary>
        /// Applies pending adds/removes. Must only be called when no job is running (i.e., after handle.Complete()).
        /// </summary>
        private static void ApplyPendingStructuralChanges()
        {
            // Remove first
            for (int r = 0; r < pendingRemove.Count; r++)
            {
                var p = pendingRemove[r];
                if (!indexOf.TryGetValue(p, out int idx)) continue;

                int last = plates.Count - 1;
                var lastPlate = plates[last];

                plates[idx] = lastPlate;
                plates.RemoveAt(last);

                indexOf[lastPlate] = idx;
                indexOf.Remove(p);

                // Keep native arrays aligned (copy last -> idx)
                if (allocated && idx != last)
                {
                    isPulsing[idx] = isPulsing[last];
                    isVisible[idx] = isVisible[last];
                    startTime[idx] = startTime[last];
                    talkColor[idx] = talkColor[last];
                }
            }
            pendingRemove.Clear();

            // Add
            for (int a = 0; a < pendingAdd.Count; a++)
            {
                var p = pendingAdd[a];
                if (p == null) continue;
                if (indexOf.ContainsKey(p)) continue;

                int idx = plates.Count;
                plates.Add(p);
                indexOf[p] = idx;

                // Optional init
                if (allocated && idx < isPulsing.Length)
                {
                    isPulsing[idx] = 0;
                    isVisible[idx] = 0;
                    startTime[idx] = 0;
                    talkColor[idx] = 0;
                }
            }
            pendingAdd.Clear();
        }

        /// <summary>
        /// Ensures native buffers can hold at least 'count'. Preserves existing data.
        /// Must only be called when no job is running (i.e., after handle.Complete()).
        /// </summary>
        private static void EnsureCapacity(int countNeeded)
        {
            if (allocated && isPulsing.Length >= countNeeded) return;

            int newCap = math.max(64, math.ceilpow2(countNeeded));

            // Create new arrays
            var newIsPulsing = new NativeArray<ushort>(newCap, Allocator.Persistent);
            var newIsVisible = new NativeArray<ushort>(newCap, Allocator.Persistent);
            var newStartTime = new NativeArray<double>(newCap, Allocator.Persistent);
            var newTalkColor = new NativeArray<float4>(newCap, Allocator.Persistent);

            var newOutColor = new NativeArray<float4>(newCap, Allocator.Persistent);
            var newOutHasChange = new NativeArray<ushort>(newCap, Allocator.Persistent);
            var newOutStopPulsing = new NativeArray<ushort>(newCap, Allocator.Persistent);

            if (allocated)
            {
                int copy = math.min(isPulsing.Length, newCap);

                NativeArray<ushort>.Copy(isPulsing, newIsPulsing, copy);
                NativeArray<ushort>.Copy(isVisible, newIsVisible, copy);
                NativeArray<double>.Copy(startTime, newStartTime, copy);
                NativeArray<float4>.Copy(talkColor, newTalkColor, copy);

                NativeArray<float4>.Copy(outColor, newOutColor, copy);
                NativeArray<ushort>.Copy(outHasChange, newOutHasChange, copy);
                NativeArray<ushort>.Copy(outStopPulsing, newOutStopPulsing, copy);

                DisposeArrays(); // dispose old after copying
            }

            isPulsing = newIsPulsing;
            isVisible = newIsVisible;
            startTime = newStartTime;
            talkColor = newTalkColor;

            outColor = newOutColor;
            outHasChange = newOutHasChange;
            outStopPulsing = newOutStopPulsing;

            allocated = true;
        }

        public static void Dispose()
        {
            // Ensure no jobs are touching these arrays.
            handle.Complete();

            DisposeArrays();
            plates.Clear();
            indexOf.Clear();
            pendingAdd.Clear();
            pendingRemove.Clear();

            allocated = false;
            count = 0;
        }

        private static void DisposeArrays()
        {
            if (!allocated) return;

            if (isPulsing.IsCreated) isPulsing.Dispose();
            if (isVisible.IsCreated) isVisible.Dispose();
            if (startTime.IsCreated) startTime.Dispose();
            if (talkColor.IsCreated) talkColor.Dispose();

            if (outColor.IsCreated) outColor.Dispose();
            if (outHasChange.IsCreated) outHasChange.Dispose();
            if (outStopPulsing.IsCreated) outStopPulsing.Dispose();
        }

        public static void ScheduleSimulate(double timeAsDouble)
        {
            ScheduleSimulate(
                timeAsDouble,
                0.1f,
                0.1f,
                BasisRemoteNamePlateDriver.StaticNormalColor
            );
        }

        /// <summary>
        /// Call from your driver each frame/tick.
        /// SAFE: completes previous job, applies pending structural changes, resizes buffers, gathers, then schedules.
        /// </summary>
        public static unsafe void ScheduleSimulate(double now, float hold, float fade, Color normalUnityColor)
        {
            // Ensure previous job is not touching arrays before we overwrite gather buffers.
            // If you want overlap (faster), move this Complete() out to a single sync point per frame.
            handle.Complete();

            ApplyPendingStructuralChanges();

            count = plates.Count;
            if (count == 0) return;

            EnsureCapacity(count);

            // Convert once
            float4 normal = new float4(
                normalUnityColor.r,
                normalUnityColor.g,
                normalUnityColor.b,
                normalUnityColor.a
            );

            // --- UNSAFE POINTERS (skip NativeArray.SetItem) ---
            ushort* pIsPulsing = (ushort*)isPulsing.GetUnsafePtr();
            ushort* pIsVisible = (ushort*)isVisible.GetUnsafePtr();
            double* pStartTime = (double*)startTime.GetUnsafePtr();
            float4* pTalkColor = (float4*)talkColor.GetUnsafePtr();

            // Optional: if you also want to clear outputs here via pointers (not required)
            // float4* pOutColor = (float4*)outColor.GetUnsafePtr();
            // ushort* pOutHasChange = (ushort*)outHasChange.GetUnsafePtr();
            // ushort* pOutStopPulsing = (ushort*)outStopPulsing.GetUnsafePtr();

            // --- Gather phase (main thread) ---
            for (int Index = 0; Index < count; Index++)
            {
                BasisRemoteNamePlate p = plates[Index];

                pIsVisible[Index] = (ushort)(p.IsVisible ? 1 : 0);
                pIsPulsing[Index] = (ushort)(p.GetIsPulsingForJob() ? 1 : 0);

                pStartTime[Index] = p.GetTalkStartTimeForJob();

                Color tc = p.GetTalkColorForJob();
                pTalkColor[Index] = new float4(tc.r, tc.g, tc.b, tc.a);

                // If you want to eagerly clear outputs (job also clears per-index):
                // pOutHasChange[i] = 0;
                // pOutStopPulsing[i] = 0;
                // pOutColor[i] = normal; // or leave untouched
            }

            // --- Job phase ---
            var job = new NamePlatePulseJob
            {
                now = now,
                hold = hold,
                fade = fade,
                normalColor = normal,

                isPulsing = isPulsing,
                isVisible = isVisible,
                startTime = startTime,
                talkColor = talkColor,

                outColor = outColor,
                outHasChange = outHasChange,
                outStopPulsing = outStopPulsing,
            };

            handle = job.Schedule(count, 64);
        }

        /// <summary>
        /// Call after ScheduleSimulate to apply job output back onto plates.
        /// </summary>
        public static void CompleteNamePlates()
        {
            if (count == 0) return;

            handle.Complete();

            // --- Apply phase (main thread) ---
            for (int i = 0; i < count; i++)
            {
                var p = plates[i];

                if (outStopPulsing[i] != 0)
                {
                    p.StopPulseFromJob(); // must exist on plate
                }

                if (outHasChange[i] != 0)
                {
                    float4 c = outColor[i];
                    p.ApplyColorFromJob(new Color(c.x, c.y, c.z, c.w)); // must exist on plate
                }
            }
        }

        [BurstCompile]
        public struct NamePlatePulseJob : IJobParallelFor
        {
            public double now;
            public float hold;
            public float fade;

            public float4 normalColor;

            [ReadOnly] public NativeArray<ushort> isPulsing; // 0/1
            [ReadOnly] public NativeArray<ushort> isVisible; // 0/1
            [ReadOnly] public NativeArray<double> startTime;
            [ReadOnly] public NativeArray<float4> talkColor;

            public NativeArray<float4> outColor;
            public NativeArray<ushort> outHasChange;     // 0/1
            public NativeArray<ushort> outStopPulsing;   // 0/1

            public void Execute(int i)
            {
                outHasChange[i] = 0;
                outStopPulsing[i] = 0;

                if (isPulsing[i] == 0) return;

                if (isVisible[i] == 0)
                {
                    outStopPulsing[i] = 1;
                    return;
                }

                double elapsed = now - startTime[i];

                if (elapsed < hold)
                {
                    // Still holding talk color; don't spam updates.
                    return;
                }

                float fadeElapsed = (float)(elapsed - hold);
                float t = fadeElapsed / fade;

                if (t >= 1f)
                {
                    outColor[i] = normalColor;
                    outHasChange[i] = 1;
                    outStopPulsing[i] = 1;
                    return;
                }

                t = math.saturate(t);
                outColor[i] = math.lerp(talkColor[i], normalColor, t);
                outHasChange[i] = 1;
            }
        }
    }
}
