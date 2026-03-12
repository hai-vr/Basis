using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using System.Collections.Generic;
using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Scripts.UI.NamePlate
{
    public class BasisRemoteNamePlateDriver : MonoBehaviour
    {
        public static BasisRemoteNamePlateDriver Instance;

        public Color NormalColor;
        public Color IsTalkingColor;
        public Color OutOfRangeColor;

        [SerializeField] public static float transitionDuration = 0.3f;
        [SerializeField] public static float returnDelay = 0.4f;

        public static Color StaticNormalColor;
        public static Color StaticIsTalkingColor;
        public static Color StaticOutOfRangeColor;

        public TextMeshPro Text;

        public Material TransParentNamePlateMaterial;
        public Material OpaqueNamePlateMaterial;

        [HideInInspector] public Material SelectedNamePlateMaterial;
        [HideInInspector] public Mesh RoundedCornersMesh;

        [Range(0f, 1f)] public float RoundEdges = 0.5f;
        public int CornerVertexCount = 8;
        public float zOffset = 0.06f;

        public static bool NamePlateEnabled = true;
        public static bool NamePlateMenuOnly = false;
        public static float NamePlateHalfWidth = 30f;
        public static float NamePlateSize = 1f;
        public static float NamePlateTransparency = 0.45f;
        private static bool lastMenuOpenState;

        public void Awake()
        {
            Instance = this;

            SelectedNamePlateMaterial = BasisDeviceManagement.IsMobileHardware()
                ? OpaqueNamePlateMaterial
                : TransParentNamePlateMaterial;

            NamePlateEnabled = BasisSettingsDefaults.NPEnabled.RawValue;
            NamePlateMenuOnly = BasisSettingsDefaults.NPMenuOnly.RawValue;
            NamePlateHalfWidth = BasisSettingsDefaults.NPWidth.RawValue;
            NamePlateSize = BasisSettingsDefaults.NPSize.RawValue;
            NamePlateTransparency = BasisSettingsDefaults.NPTransparency.RawValue;
            lastMenuOpenState = BasisMainMenu.Instance != null;

            StaticNormalColor = new Color(NormalColor.r, NormalColor.g, NormalColor.b, NamePlateTransparency);
            StaticIsTalkingColor = new Color(IsTalkingColor.r, IsTalkingColor.g, IsTalkingColor.b, NamePlateTransparency);
            StaticOutOfRangeColor = new Color(OutOfRangeColor.r, OutOfRangeColor.g, OutOfRangeColor.b, NamePlateTransparency);

            RoundedCornersMesh = GenerateRoundedQuad();
        }

        /// <summary>
        /// Returns whether a given plate should currently be active, considering
        /// the enabled toggle, menu-only mode, and per-plate face visibility.
        /// </summary>
        public static bool ShouldPlateBeActive(BasisRemoteNamePlate plate)
        {
            if (!NamePlateEnabled) return false;
            if (!plate.IsVisible) return false;
            if (NamePlateMenuOnly && BasisMainMenu.Instance == null) return false;
            return true;
        }

        /// <summary>
        /// Updates gameObject.SetActive on all plates based on current visibility state.
        /// Call only on state transitions, not every frame.
        /// </summary>
        private static void SetAllPlateVisibility()
        {
            for (int i = 0; i < plates.Count; i++)
            {
                var plate = plates[i];
                if (plate != null)
                    plate.gameObject.SetActive(ShouldPlateBeActive(plate));
            }
        }

        /// <summary>
        /// Called by SettingsProviderNamePlate when nameplate settings change.
        /// Re-reads settings and applies width, size, and transparency to all active plates.
        /// </summary>
        public void ApplyNamePlateSettingsFromUI()
        {
            bool enabled = BasisSettingsDefaults.NPEnabled.RawValue;
            bool menuOnly = BasisSettingsDefaults.NPMenuOnly.RawValue;
            float newWidth = BasisSettingsDefaults.NPWidth.RawValue;
            float newSize = BasisSettingsDefaults.NPSize.RawValue;
            float newTransparency = BasisSettingsDefaults.NPTransparency.RawValue;

            bool meshChanged = !Mathf.Approximately(NamePlateHalfWidth, newWidth);

            NamePlateEnabled = enabled;
            NamePlateMenuOnly = menuOnly;
            NamePlateHalfWidth = newWidth;
            NamePlateSize = newSize;
            NamePlateTransparency = newTransparency;

            StaticNormalColor = new Color(NormalColor.r, NormalColor.g, NormalColor.b, newTransparency);
            StaticIsTalkingColor = new Color(IsTalkingColor.r, IsTalkingColor.g, IsTalkingColor.b, newTransparency);
            StaticOutOfRangeColor = new Color(OutOfRangeColor.r, OutOfRangeColor.g, OutOfRangeColor.b, newTransparency);

            if (meshChanged)
            {
                RoundedCornersMesh = GenerateRoundedQuad();
            }

            Vector3 scale = new Vector3(0.02f, 0.02f, 0.02f) * newSize;
            for (int i = 0; i < plates.Count; i++)
            {
                var plate = plates[i];
                if (plate == null) continue;

                if (meshChanged && plate.bakedMesh != null)
                {
                    RebuildPlateMesh(plate);
                }

                if (plate.Self != null)
                {
                    plate.Self.localScale = scale;
                }

                plate.ApplyColorFromJob(StaticNormalColor);
            }

            SetAllPlateVisibility();
        }

        private void RebuildPlateMesh(BasisRemoteNamePlate namePlate)
        {
            CombineInstance[] combine = new CombineInstance[2];
            combine[0] = new CombineInstance { mesh = RoundedCornersMesh, transform = Matrix4x4.identity };
            combine[1] = new CombineInstance { mesh = namePlate.bakedMesh, transform = Matrix4x4.identity };

            Mesh combinedMesh = new Mesh { name = "CombinedNameplateMesh" };
            combinedMesh.CombineMeshes(combine, false);

            namePlate.Filter.sharedMesh = combinedMesh;
        }

        // ===========================
        // Existing text bake path (unchanged)
        // ===========================

        public void GenerateTextFactory(BasisRemotePlayer remotePlayer, BasisRemoteNamePlate namePlate)
        {
            Text.gameObject.SetActive(true);
            Text.text = remotePlayer.DisplayName;
            Text.ForceMeshUpdate();

            Mesh textMesh = Instantiate(Text.mesh);
            FlipMesh(textMesh);

            namePlate.bakedMesh = textMesh;
            namePlate.Filter.sharedMesh = textMesh;

            CreateFinalMesh(namePlate);
            Text.gameObject.SetActive(false);
        }

        private void CreateFinalMesh(BasisRemoteNamePlate namePlate)
        {
            CombineInstance[] combine = new CombineInstance[2];

            combine[0] = new CombineInstance { mesh = RoundedCornersMesh, transform = Matrix4x4.identity };
            combine[1] = new CombineInstance { mesh = namePlate.bakedMesh, transform = Matrix4x4.identity };

            Mesh combinedMesh = new Mesh { name = "CombinedNameplateMesh" };
            combinedMesh.CombineMeshes(combine, false);

            namePlate.Filter.sharedMesh = combinedMesh;

            // Keep same behavior: 2 submeshes => [background, text]
            // NOTE: This allocates; ideally cache materials array on plate, but left as-is for compatibility.
            namePlate.Renderer.materials = new Material[]
            {
                SelectedNamePlateMaterial,
                namePlate.Renderer.material
            };
        }

        private static void FlipMesh(Mesh mesh)
        {
            // Mirror vertices along X axis so text reads correctly from the opposite side.
            // Negating X implicitly reverses triangle winding, which makes the mesh
            // face the opposite direction - no explicit winding reversal needed.
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].x = -vertices[i].x;
            }
            mesh.vertices = vertices;

            // Flip normals to match the new facing direction
            Vector3[] normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = -normals[i];
            }
            mesh.normals = normals;
        }

        public Mesh GenerateRoundedQuad()
        {
            int cornerCount = Mathf.Max(3, CornerVertexCount);
            int ringVertexCount = cornerCount * 4;
            int vertexCount = ringVertexCount + 1;
            int triangleCount = ringVertexCount;

            Vector3[] v = new Vector3[vertexCount];
            Vector3[] n = new Vector3[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            int[] t = new int[triangleCount * 3];

            float halfWidth = NamePlateHalfWidth;
            float halfHeight = 4.5f;
            float width = halfWidth * 2f;
            float height = halfHeight * 2f;

            float maxRadius = Mathf.Min(halfWidth, halfHeight);
            float radius = Mathf.Clamp01(RoundEdges) * maxRadius;

            float angleStep = Mathf.PI * 0.5f / (cornerCount - 1);
            Vector2 uvOffset = new Vector2(0.5f, 0.5f);
            Vector2 uvScale = new Vector2(1f / width, 1f / height);

            v[0] = new Vector3(0, 0, zOffset);
            uv[0] = uvOffset;
            n[0] = Vector3.forward;

            for (int ci = 0; ci < cornerCount; ci++)
            {
                float angle = ci * angleStep;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                Vector2 tl = new Vector2(-halfWidth + (1f - cos) * radius, halfHeight - (1f - sin) * radius);
                Vector2 tr = new Vector2(halfWidth - (1f - sin) * radius, halfHeight - (1f - cos) * radius);
                Vector2 br = new Vector2(halfWidth - (1f - cos) * radius, -halfHeight + (1f - sin) * radius);
                Vector2 bl = new Vector2(-halfWidth + (1f - sin) * radius, -halfHeight + (1f - cos) * radius);

                int baseIndex = 1 + ci;

                v[baseIndex] = new Vector3(tl.x, tl.y, zOffset);
                v[baseIndex + cornerCount] = new Vector3(tr.x, tr.y, zOffset);
                v[baseIndex + cornerCount * 2] = new Vector3(br.x, br.y, zOffset);
                v[baseIndex + cornerCount * 3] = new Vector3(bl.x, bl.y, zOffset);

                uv[baseIndex] = tl * uvScale + uvOffset;
                uv[baseIndex + cornerCount] = tr * uvScale + uvOffset;
                uv[baseIndex + cornerCount * 2] = br * uvScale + uvOffset;
                uv[baseIndex + cornerCount * 3] = bl * uvScale + uvOffset;

                n[baseIndex] = Vector3.forward;
                n[baseIndex + cornerCount] = Vector3.forward;
                n[baseIndex + cornerCount * 2] = Vector3.forward;
                n[baseIndex + cornerCount * 3] = Vector3.forward;
            }

            for (int i = 0; i < ringVertexCount; i++)
            {
                int tri = i * 3;
                t[tri] = 0;
                t[tri + 1] = 1 + ((i + 1) % ringVertexCount);
                t[tri + 2] = 1 + i;
            }

            return new Mesh
            {
                name = "Rounded NamePlate Quad",
                vertices = v,
                normals = n,
                uv = uv,
                triangles = t
            };
        }

        // ===========================
        // Chat bubble mesh generation
        // ===========================

        /// <summary>
        /// Generates a rounded quad background mesh for the chat bubble,
        /// sized to fit the current chat text.
        /// </summary>
        public void GenerateChatBubble(BasisRemoteNamePlate namePlate)
        {
            if (namePlate.ChatText == null || namePlate.ChatBubbleFilter == null) return;

            namePlate.ChatText.ForceMeshUpdate();
            Vector2 textSize = namePlate.ChatText.GetRenderedValues(true);

            float padding = 2f;
            float halfWidth = (textSize.x / 2f) + padding;
            float halfHeight = (textSize.y / 2f) + padding;

            // Clamp minimum size
            halfWidth = Mathf.Max(halfWidth, 6f);
            halfHeight = Mathf.Max(halfHeight, 3f);

            Mesh bubbleMesh = GenerateChatBubbleQuad(halfWidth, halfHeight);
            namePlate.ChatBubbleFilter.sharedMesh = bubbleMesh;

            if (namePlate.ChatBubbleRenderer.sharedMaterial == null)
            {
                namePlate.ChatBubbleRenderer.material = SelectedNamePlateMaterial;
            }
        }

        private Mesh GenerateChatBubbleQuad(float halfWidth, float halfHeight)
        {
            int cornerCount = Mathf.Max(3, CornerVertexCount);
            int ringVertexCount = cornerCount * 4;
            int vertexCount = ringVertexCount + 1;
            int triangleCount = ringVertexCount;

            Vector3[] v = new Vector3[vertexCount];
            Vector3[] n = new Vector3[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            int[] t = new int[triangleCount * 3];

            float width = halfWidth * 2f;
            float height = halfHeight * 2f;

            float maxRadius = Mathf.Min(halfWidth, halfHeight);
            float radius = Mathf.Clamp01(RoundEdges) * maxRadius;

            float angleStep = Mathf.PI * 0.5f / (cornerCount - 1);
            Vector2 uvOffset = new Vector2(0.5f, 0.5f);
            Vector2 uvScale = new Vector2(1f / width, 1f / height);

            v[0] = new Vector3(0, 0, zOffset);
            uv[0] = uvOffset;
            n[0] = Vector3.forward;

            for (int ci = 0; ci < cornerCount; ci++)
            {
                float angle = ci * angleStep;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                Vector2 tl = new Vector2(-halfWidth + (1f - cos) * radius, halfHeight - (1f - sin) * radius);
                Vector2 tr = new Vector2(halfWidth - (1f - sin) * radius, halfHeight - (1f - cos) * radius);
                Vector2 br = new Vector2(halfWidth - (1f - cos) * radius, -halfHeight + (1f - sin) * radius);
                Vector2 bl = new Vector2(-halfWidth + (1f - sin) * radius, -halfHeight + (1f - cos) * radius);

                int baseIndex = 1 + ci;

                v[baseIndex] = new Vector3(tl.x, tl.y, zOffset);
                v[baseIndex + cornerCount] = new Vector3(tr.x, tr.y, zOffset);
                v[baseIndex + cornerCount * 2] = new Vector3(br.x, br.y, zOffset);
                v[baseIndex + cornerCount * 3] = new Vector3(bl.x, bl.y, zOffset);

                uv[baseIndex] = tl * uvScale + uvOffset;
                uv[baseIndex + cornerCount] = tr * uvScale + uvOffset;
                uv[baseIndex + cornerCount * 2] = br * uvScale + uvOffset;
                uv[baseIndex + cornerCount * 3] = bl * uvScale + uvOffset;

                n[baseIndex] = Vector3.forward;
                n[baseIndex + cornerCount] = Vector3.forward;
                n[baseIndex + cornerCount * 2] = Vector3.forward;
                n[baseIndex + cornerCount * 3] = Vector3.forward;
            }

            for (int i = 0; i < ringVertexCount; i++)
            {
                int tri = i * 3;
                t[tri] = 0;
                t[tri + 1] = 1 + ((i + 1) % ringVertexCount);
                t[tri + 2] = 1 + i;
            }

            return new Mesh
            {
                name = "Chat Bubble Quad",
                vertices = v,
                normals = n,
                uv = uv,
                triangles = t
            };
        }

        // =========================================================
        // Optimized job system (double-buffered + safe structural changes)
        // =========================================================

        private static readonly List<BasisRemoteNamePlate> plates = new(256);
        private static readonly Dictionary<BasisRemoteNamePlate, int> indexOf = new(256);

        private static readonly List<BasisRemoteNamePlate> pendingAdd = new(64);
        private static readonly List<BasisRemoteNamePlate> pendingRemove = new(64);

        // Double-buffer inputs so we don't need Complete() at the start.
        private static NativeArray<PlateInput> inputA;
        private static NativeArray<PlateInput> inputB;

        // Double-buffer outputs (optional but keeps everything symmetric)
        private static NativeArray<PlateOutput> outputA;
        private static NativeArray<PlateOutput> outputB;

        private static int writeBuffer; // 0 => A, 1 => B
        private static bool allocated;
        private static int capacity;

        public static JobHandle handle;
        public static int count;
        private static bool jobScheduled;

        public static void Register(BasisRemoteNamePlate p)
        {
            if (p == null) return;
            pendingAdd.Add(p);
        }

        public static void Unregister(BasisRemoteNamePlate p)
        {
            if (p == null) return;
            pendingRemove.Add(p);
        }

        public static void Dispose()
        {
            if (jobScheduled)
            {
                handle.Complete();
                jobScheduled = false;
            }

            DisposeArrays();

            plates.Clear();
            indexOf.Clear();
            pendingAdd.Clear();
            pendingRemove.Clear();

            allocated = false;
            capacity = 0;
            count = 0;
        }

        private static void DisposeArrays()
        {
            if (!allocated) return;

            if (inputA.IsCreated) inputA.Dispose();
            if (inputB.IsCreated) inputB.Dispose();
            if (outputA.IsCreated) outputA.Dispose();
            if (outputB.IsCreated) outputB.Dispose();
        }

        /// <summary>
        /// Call in Update. Does NOT stall at the beginning.
        /// Safe: if previous job is still running, it simply won't schedule a new one this frame.
        /// </summary>
        public static void ScheduleSimulate(double now)
        {
            if (!ShouldRunJobs())
                return;

            ScheduleSimulate(now, returnDelay, transitionDuration, StaticNormalColor);
        }

        private static bool ShouldRunJobs()
        {
            if (!NamePlateEnabled) return false;
            if (NamePlateMenuOnly && BasisMainMenu.Instance == null) return false;
            return true;
        }

        public static void ScheduleSimulate(double now, float hold, float fade, Color normalUnityColor)
        {
            // If a job is still running, don't stomp buffers.
            if (jobScheduled && !handle.IsCompleted)
                return;

            // If it finished, complete it now so we can apply structural changes/resizes safely.
            if (jobScheduled)
            {
                handle.Complete();
                jobScheduled = false;
            }

            ApplyPendingStructuralChanges();

            count = plates.Count;
            if (count == 0)
                return;

            EnsureCapacity(count);

            // Flip buffers (write into one, job reads that one, apply reads outputs of that one)
            writeBuffer ^= 1;

            var inBuf = (writeBuffer == 0) ? inputA : inputB;
            var outBuf = (writeBuffer == 0) ? outputA : outputB;

            // Gather inputs (still managed reads, but NO stall and tight struct write)
            for (int i = 0; i < count; i++)
            {
                var p = plates[i];

                // Keep exactly your current semantics
                var tc = p.GetTalkColorForJob();

                inBuf[i] = new PlateInput
                {
                    isVisible = (ushort)(p.IsVisible ? 1 : 0),
                    isPulsing = (ushort)(p.GetIsPulsingForJob() ? 1 : 0),
                    startTime = p.GetTalkStartTimeForJob(),
                    talkColor = new float4(tc.r, tc.g, tc.b, tc.a)
                };

                // Optional: clear only the flag fields; job overwrites anyway
                outBuf[i] = default;
            }

            float4 normal = new float4(normalUnityColor.r, normalUnityColor.g, normalUnityColor.b, normalUnityColor.a);

            var job = new NamePlatePulseJob
            {
                now = now,
                hold = hold,
                fade = fade,
                normalColor = normal,
                inputs = inBuf,
                outputs = outBuf
            };

            handle = job.Schedule(count, 64);
            jobScheduled = true;
        }

        /// <summary>
        /// Call in LateUpdate (or end-of-frame). This is where we sync once.
        /// </summary>
        public static void CompleteNamePlates()
        {
            // Detect menu open/close transitions for menu-only mode
            if (NamePlateMenuOnly && NamePlateEnabled)
            {
                bool menuOpen = BasisMainMenu.Instance != null;
                if (menuOpen != lastMenuOpenState)
                {
                    lastMenuOpenState = menuOpen;
                    SetAllPlateVisibility();
                }
            }

            if (!jobScheduled || count == 0)
                return;

            handle.Complete();
            jobScheduled = false;

            var outBuf = (writeBuffer == 0) ? outputA : outputB;

            for (int i = 0; i < count; i++)
            {
                var p = plates[i];

                if (outBuf[i].stopPulsing != 0)
                    p.StopPulseFromJob();

                if (outBuf[i].hasChange != 0)
                {
                    float4 c = outBuf[i].color;
                    p.ApplyColorFromJob(new Color(c.x, c.y, c.z, c.w));
                }

                // Update chat message timeout
                p.UpdateChatTimeout();
            }
        }

        private static void ApplyPendingStructuralChanges()
        {
            // Remove first (swap-back)
            for (int r = 0; r < pendingRemove.Count; r++)
            {
                var p = pendingRemove[r];
                if (p == null) continue;
                if (!indexOf.TryGetValue(p, out int idx)) continue;

                int last = plates.Count - 1;
                var lastPlate = plates[last];

                plates[idx] = lastPlate;
                plates.RemoveAt(last);

                indexOf[lastPlate] = idx;
                indexOf.Remove(p);
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
            }
            pendingAdd.Clear();
        }

        private static void EnsureCapacity(int countNeeded)
        {
            if (allocated && capacity >= countNeeded)
                return;

            int newCap = math.max(64, math.ceilpow2(countNeeded));

            var newInA = new NativeArray<PlateInput>(newCap, Allocator.Persistent);
            var newInB = new NativeArray<PlateInput>(newCap, Allocator.Persistent);
            var newOutA = new NativeArray<PlateOutput>(newCap, Allocator.Persistent);
            var newOutB = new NativeArray<PlateOutput>(newCap, Allocator.Persistent);

            if (allocated)
            {
                int copy = math.min(capacity, newCap);
                NativeArray<PlateInput>.Copy(inputA, newInA, copy);
                NativeArray<PlateInput>.Copy(inputB, newInB, copy);
                NativeArray<PlateOutput>.Copy(outputA, newOutA, copy);
                NativeArray<PlateOutput>.Copy(outputB, newOutB, copy);

                DisposeArrays();
            }

            inputA = newInA;
            inputB = newInB;
            outputA = newOutA;
            outputB = newOutB;

            capacity = newCap;
            allocated = true;
        }

        public struct PlateInput
        {
            public ushort isPulsing; // 0/1
            public ushort isVisible; // 0/1
            public double startTime;
            public float4 talkColor;
        }

        public struct PlateOutput
        {
            public float4 color;
            public ushort hasChange;   // 0/1
            public ushort stopPulsing; // 0/1
        }

        [BurstCompile]
        public struct NamePlatePulseJob : IJobParallelFor
        {
            public double now;
            public float hold;
            public float fade;

            public float4 normalColor;

            [ReadOnly] public NativeArray<PlateInput> inputs;
            public NativeArray<PlateOutput> outputs;

            public void Execute(int i)
            {
                // Fast clear
                var o = new PlateOutput
                {
                    hasChange = 0,
                    stopPulsing = 0,
                    color = 0
                };

                var st = inputs[i];

                if (st.isPulsing == 0)
                {
                    outputs[i] = o;
                    return;
                }

                if (st.isVisible == 0)
                {
                    o.stopPulsing = 1;
                    outputs[i] = o;
                    return;
                }

                double elapsed = now - st.startTime;

                if (elapsed < hold)
                {
                    outputs[i] = o;
                    return;
                }

                float t = (float)((elapsed - hold) / fade);

                if (t >= 1f)
                {
                    o.color = normalColor;
                    o.hasChange = 1;
                    o.stopPulsing = 1;
                    outputs[i] = o;
                    return;
                }

                t = math.saturate(t);
                o.color = math.lerp(st.talkColor, normalColor, t);
                o.hasChange = 1;
                outputs[i] = o;
            }
        }
    }
}
