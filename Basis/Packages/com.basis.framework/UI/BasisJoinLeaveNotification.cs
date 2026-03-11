using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.UI.NamePlate;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Basis.Scripts.UI
{
    /// <summary>
    /// Displays join/leave notifications below the microphone icon on the player's HUD.
    /// Uses TextMeshPro + MeshFilter/MeshRenderer with a generated rounded-corner quad
    /// for the background, matching the nameplate chat bubble approach.
    /// Parented under BasisLocalCameraDriver.ParentOfUI for VR and desktop.
    ///
    /// Optimizations:
    /// - Object pool: pre-allocates MaxMessages slots, reuses GameObjects instead of Destroy/Instantiate.
    /// - Mesh cache: rounded-corner quads are cached by quantized dimensions, avoiding repeated generation.
    /// - Shader property ID and material are cached once.
    /// </summary>
    public class BasisJoinLeaveNotification : MonoBehaviour
    {
        public float MessageDuration = 5f;
        public int MaxMessages = 5;
        public float FadeStartTime = 3.5f;
        public float FontSize = 28f;
        public float FontSizeMin = 14f;
        public float FontSizeMax = 28f;
        public float BackgroundPadding = 2f;
        public float MinHalfWidth = 6f;
        public float MinHalfHeight = 3f;
        public float LineSpacing = 12f;
        public float TextRectWidth = 58f;
        public float TextRectHeight = 10f;
        public float RoundEdges = 0.5f;
        public int CornerVertexCount = 8;
        public float ZOffset = 0.06f;
        public Vector3 LocalPosition = new Vector3(0f, -12f, 0f);
        public Vector3 LocalScale = new Vector3(1f, 1f, 1f);

        private readonly List<NotificationSlot> activeSlots = new List<NotificationSlot>();
        private readonly Stack<NotificationSlot> pool = new Stack<NotificationSlot>();
        private readonly Dictionary<long, Mesh> meshCache = new Dictionary<long, Mesh>();
        private static BasisJoinLeaveNotification instance;
        private MaterialPropertyBlock mpb;
        private Material cachedMaterial;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color FullAlpha = new Color(1f, 1f, 1f, 1f);

        private class NotificationSlot
        {
            public GameObject Root;
            public GameObject BgObj;
            public GameObject TextObj;
            public TextMeshPro Text;
            public MeshRenderer BgRenderer;
            public MeshFilter BgFilter;
            public Color TextColor;
            public double SpawnTime;
        }

        public static void Create()
        {
            if (instance != null)
            {
                return;
            }

            GameObject root = new GameObject("BasisJoinLeaveNotification");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<BasisJoinLeaveNotification>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            mpb = new MaterialPropertyBlock();
            PrewarmPool();
        }

        private void OnEnable()
        {
            BasisNetworkPlayer.OnRemotePlayerJoined += OnRemotePlayerJoined;
            BasisNetworkPlayer.OnRemotePlayerLeft += OnRemotePlayerLeft;

            if (BasisLocalCameraDriver.HasInstance)
            {
                AttachToCamera();
            }
            BasisLocalCameraDriver.InstanceExists += AttachToCamera;
        }

        private void OnDisable()
        {
            BasisNetworkPlayer.OnRemotePlayerJoined -= OnRemotePlayerJoined;
            BasisNetworkPlayer.OnRemotePlayerLeft -= OnRemotePlayerLeft;
            BasisLocalCameraDriver.InstanceExists -= AttachToCamera;
        }

        private void AttachToCamera()
        {
            if (BasisLocalCameraDriver.Instance == null)
            {
                return;
            }

            transform.SetParent(BasisLocalCameraDriver.Instance.ParentOfUI, false);
            transform.SetLocalPositionAndRotation(LocalPosition, Quaternion.identity);
            transform.localScale = LocalScale;
        }

        private void CacheMaterial()
        {
            if (cachedMaterial != null)
            {
                return;
            }
            if (BasisRemoteNamePlateDriver.Instance != null)
            {
                cachedMaterial = BasisRemoteNamePlateDriver.Instance.SelectedNamePlateMaterial;
            }
        }

        private void PrewarmPool()
        {
            for (int i = 0; i < MaxMessages; i++)
            {
                pool.Push(CreateSlot());
            }
        }

        private NotificationSlot CreateSlot()
        {
            NotificationSlot slot = new NotificationSlot();

            slot.Root = new GameObject("Notification");
            slot.Root.transform.SetParent(transform, false);

            slot.BgObj = new GameObject("Background");
            slot.BgObj.transform.SetParent(slot.Root.transform, false);
            slot.BgObj.transform.localPosition = Vector3.zero;
            slot.BgObj.transform.localRotation = Quaternion.identity;
            slot.BgObj.transform.localScale = Vector3.one;

            slot.BgFilter = slot.BgObj.AddComponent<MeshFilter>();
            slot.BgRenderer = slot.BgObj.AddComponent<MeshRenderer>();
            slot.BgRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            slot.BgRenderer.receiveShadows = false;
            slot.BgRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            slot.TextObj = new GameObject("Text");
            slot.TextObj.transform.SetParent(slot.Root.transform, false);
            slot.TextObj.transform.localPosition = new Vector3(0f, 0f, ZOffset - 0.02f);
            slot.TextObj.transform.localRotation = Quaternion.Euler(0, 180, 0);
            slot.TextObj.transform.localScale = Vector3.one;

            slot.Text = slot.TextObj.AddComponent<TextMeshPro>();
            slot.Text.fontSize = FontSize;
            slot.Text.enableAutoSizing = true;
            slot.Text.fontSizeMin = FontSizeMin;
            slot.Text.fontSizeMax = FontSizeMax;
            slot.Text.alignment = TextAlignmentOptions.Center;
            slot.Text.textWrappingMode = TextWrappingModes.Normal;
            slot.Text.overflowMode = TextOverflowModes.Truncate;
            slot.Text.sortingOrder = 1;

            RectTransform textRect = slot.Text.GetComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(TextRectWidth, TextRectHeight);

            slot.Root.SetActive(false);
            return slot;
        }

        private NotificationSlot AcquireSlot()
        {
            if (pool.Count > 0)
            {
                return pool.Pop();
            }
            return CreateSlot();
        }

        private void ReleaseSlot(NotificationSlot slot)
        {
            slot.Root.SetActive(false);
            // Reset background alpha
            slot.BgRenderer.GetPropertyBlock(mpb, 0);
            mpb.SetColor(BaseColorId, FullAlpha);
            slot.BgRenderer.SetPropertyBlock(mpb, 0);
            pool.Push(slot);
        }

        private void OnRemotePlayerJoined(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            string name = networkPlayer.displayName;
            if (string.IsNullOrEmpty(name))
            {
                name = "Unknown";
            }

            Color joinColor = BasisRemoteNamePlateDriver.Instance != null
                ? BasisRemoteNamePlateDriver.StaticIsTalkingColor
                : new Color(0.2f, 0.8f, 0.4f, 1f);

            ShowNotification(name + " joined", joinColor);
        }

        private void OnRemotePlayerLeft(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            string name = networkPlayer.displayName;
            if (string.IsNullOrEmpty(name))
            {
                name = "Unknown";
            }

            Color leaveColor = BasisRemoteNamePlateDriver.Instance != null
                ? BasisRemoteNamePlateDriver.StaticOutOfRangeColor
                : new Color(0.85f, 0.35f, 0.35f, 1f);

            ShowNotification(name + " left", leaveColor);
        }

        private void ShowNotification(string message, Color color)
        {
            // Evict oldest if at capacity
            while (activeSlots.Count >= MaxMessages)
            {
                ReleaseSlot(activeSlots[0]);
                activeSlots.RemoveAt(0);
            }

            CacheMaterial();

            NotificationSlot slot = AcquireSlot();

            // Assign material (may have been null at pool creation time)
            if (cachedMaterial != null && slot.BgRenderer.sharedMaterial != cachedMaterial)
            {
                slot.BgRenderer.sharedMaterial = cachedMaterial;
            }

            // Configure text
            slot.Text.text = message;
            slot.Text.color = color;
            slot.TextColor = color;
            slot.SpawnTime = Time.timeAsDouble;

            // Size background to fit text
            slot.Text.ForceMeshUpdate();
            Vector2 textSize = slot.Text.GetRenderedValues(true);

            float halfWidth = (textSize.x / 2f) + BackgroundPadding;
            float halfHeight = (textSize.y / 2f) + BackgroundPadding;
            halfWidth = Mathf.Max(halfWidth, MinHalfWidth);
            halfHeight = Mathf.Max(halfHeight, MinHalfHeight);

            slot.BgFilter.sharedMesh = GetOrCreateMesh(halfWidth, halfHeight);
            slot.Root.SetActive(true);

            activeSlots.Add(slot);
            RepositionAll();
        }

        /// <summary>
        /// Returns a cached mesh for the given dimensions, quantized to 0.5-unit steps
        /// to maximize cache hits while keeping visuals accurate.
        /// </summary>
        private Mesh GetOrCreateMesh(float halfWidth, float halfHeight)
        {
            // Quantize to 0.5 units (pack two ints into one long for the key)
            int qw = Mathf.CeilToInt(halfWidth * 2f);
            int qh = Mathf.CeilToInt(halfHeight * 2f);
            long key = ((long)qw << 32) | (uint)qh;

            if (meshCache.TryGetValue(key, out Mesh cached))
            {
                return cached;
            }

            // Generate at quantized size
            float actualHW = qw * 0.5f;
            float actualHH = qh * 0.5f;
            Mesh mesh = GenerateRoundedQuad(actualHW, actualHH);
            meshCache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// Generates a rounded-corner quad mesh, same algorithm as
        /// BasisRemoteNamePlateDriver.GenerateChatBubbleQuad.
        /// </summary>
        private Mesh GenerateRoundedQuad(float halfWidth, float halfHeight)
        {
            int cornerCount = Mathf.Max(3, CornerVertexCount);
            int ringVertexCount = cornerCount * 4;
            int vertexCount = ringVertexCount + 1;

            Vector3[] v = new Vector3[vertexCount];
            Vector3[] n = new Vector3[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            int[] t = new int[ringVertexCount * 3];

            float width = halfWidth * 2f;
            float height = halfHeight * 2f;

            float maxRadius = Mathf.Min(halfWidth, halfHeight);
            float radius = Mathf.Clamp01(RoundEdges) * maxRadius;

            float angleStep = Mathf.PI * 0.5f / (cornerCount - 1);
            Vector2 uvOff = new Vector2(0.5f, 0.5f);
            Vector2 uvScale = new Vector2(1f / width, 1f / height);

            v[0] = new Vector3(0, 0, ZOffset);
            uv[0] = uvOff;
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

                int b = 1 + ci;
                v[b] = new Vector3(tl.x, tl.y, ZOffset);
                v[b + cornerCount] = new Vector3(tr.x, tr.y, ZOffset);
                v[b + cornerCount * 2] = new Vector3(br.x, br.y, ZOffset);
                v[b + cornerCount * 3] = new Vector3(bl.x, bl.y, ZOffset);

                uv[b] = tl * uvScale + uvOff;
                uv[b + cornerCount] = tr * uvScale + uvOff;
                uv[b + cornerCount * 2] = br * uvScale + uvOff;
                uv[b + cornerCount * 3] = bl * uvScale + uvOff;

                n[b] = Vector3.forward;
                n[b + cornerCount] = Vector3.forward;
                n[b + cornerCount * 2] = Vector3.forward;
                n[b + cornerCount * 3] = Vector3.forward;
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
                name = "Notification Quad",
                vertices = v,
                normals = n,
                uv = uv,
                triangles = t
            };
        }

        private void RepositionAll()
        {
            float y = 0f;
            for (int i = activeSlots.Count - 1; i >= 0; i--)
            {
                y -= LineSpacing;
                activeSlots[i].Root.transform.localPosition = new Vector3(0f, y, 0f);
            }
        }

        private void Update()
        {
            if (activeSlots.Count == 0)
            {
                return;
            }

            double now = Time.timeAsDouble;
            bool removed = false;

            for (int i = activeSlots.Count - 1; i >= 0; i--)
            {
                NotificationSlot slot = activeSlots[i];
                double elapsed = now - slot.SpawnTime;

                if (elapsed >= MessageDuration)
                {
                    ReleaseSlot(slot);
                    activeSlots.RemoveAt(i);
                    removed = true;
                    continue;
                }

                if (elapsed >= FadeStartTime)
                {
                    float alpha = 1f - (float)(elapsed - FadeStartTime) / (MessageDuration - FadeStartTime);

                    Color tc = slot.TextColor;
                    tc.a = alpha;
                    slot.Text.color = tc;

                    slot.BgRenderer.GetPropertyBlock(mpb, 0);
                    mpb.SetColor(BaseColorId, new Color(1f, 1f, 1f, alpha));
                    slot.BgRenderer.SetPropertyBlock(mpb, 0);
                }
            }

            if (removed)
            {
                RepositionAll();
            }
        }

        private void OnDestroy()
        {
            BasisLocalCameraDriver.InstanceExists -= AttachToCamera;

            // Return active slots (no Destroy needed, they're children of this GO)
            activeSlots.Clear();
            pool.Clear();

            // Destroy cached meshes
            foreach (Mesh mesh in meshCache.Values)
            {
                Destroy(mesh);
            }
            meshCache.Clear();

            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
