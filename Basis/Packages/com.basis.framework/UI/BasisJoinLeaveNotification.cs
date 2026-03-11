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

        private readonly List<NotificationEntry> activeMessages = new List<NotificationEntry>();
        private static BasisJoinLeaveNotification instance;
        private MaterialPropertyBlock mpb;

        private struct NotificationEntry
        {
            public GameObject Root;
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
            while (activeMessages.Count >= MaxMessages)
            {
                RemoveMessage(0);
            }

            GameObject root = new GameObject("Notification");
            root.transform.SetParent(transform, false);

            // Background: MeshFilter + MeshRenderer, same as nameplate chat bubble
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(root.transform, false);
            bgObj.transform.localPosition = Vector3.zero;
            bgObj.transform.localRotation = Quaternion.identity;
            bgObj.transform.localScale = Vector3.one;

            MeshFilter bgFilter = bgObj.AddComponent<MeshFilter>();
            MeshRenderer bgRenderer = bgObj.AddComponent<MeshRenderer>();

            if (BasisRemoteNamePlateDriver.Instance != null)
            {
                bgRenderer.material = BasisRemoteNamePlateDriver.Instance.SelectedNamePlateMaterial;
            }
            bgRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bgRenderer.receiveShadows = false;
            bgRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            // Text: TextMeshPro matching nameplate chat text style
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(root.transform, false);
            textObj.transform.localPosition = new Vector3(0f, 0f, ZOffset - 0.02f);
            textObj.transform.localRotation = Quaternion.Euler(0, 180, 0);
            textObj.transform.localScale = Vector3.one;

            TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
            tmp.text = message;
            tmp.fontSize = FontSize;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = FontSizeMin;
            tmp.fontSizeMax = FontSizeMax;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.sortingOrder = 1;

            RectTransform textRect = tmp.GetComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(TextRectWidth, TextRectHeight);

            // Size the background mesh to fit text, same as GenerateChatBubble
            tmp.ForceMeshUpdate();
            Vector2 textSize = tmp.GetRenderedValues(true);

            float halfWidth = (textSize.x / 2f) + BackgroundPadding;
            float halfHeight = (textSize.y / 2f) + BackgroundPadding;
            halfWidth = Mathf.Max(halfWidth, MinHalfWidth);
            halfHeight = Mathf.Max(halfHeight, MinHalfHeight);

            bgFilter.sharedMesh = GenerateRoundedQuad(halfWidth, halfHeight);

            activeMessages.Add(new NotificationEntry
            {
                Root = root,
                Text = tmp,
                BgRenderer = bgRenderer,
                BgFilter = bgFilter,
                TextColor = color,
                SpawnTime = Time.timeAsDouble,
            });

            RepositionAll();
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
            for (int i = activeMessages.Count - 1; i >= 0; i--)
            {
                NotificationEntry entry = activeMessages[i];
                y -= LineSpacing;
                entry.Root.transform.localPosition = new Vector3(0f, y, 0f);
            }
        }

        private void Update()
        {
            double now = Time.timeAsDouble;
            bool removed = false;
            int colorId = Shader.PropertyToID("_BaseColor");

            for (int i = activeMessages.Count - 1; i >= 0; i--)
            {
                NotificationEntry entry = activeMessages[i];
                double elapsed = now - entry.SpawnTime;

                if (elapsed >= MessageDuration)
                {
                    RemoveMessage(i);
                    removed = true;
                    continue;
                }

                if (elapsed >= FadeStartTime)
                {
                    float alpha = 1f - (float)(elapsed - FadeStartTime) / (MessageDuration - FadeStartTime);

                    Color tc = entry.TextColor;
                    tc.a = alpha;
                    entry.Text.color = tc;

                    // Fade background via MaterialPropertyBlock (same as nameplate SetPlateColor)
                    entry.BgRenderer.GetPropertyBlock(mpb, 0);
                    mpb.SetColor(colorId, new Color(1f, 1f, 1f, alpha));
                    entry.BgRenderer.SetPropertyBlock(mpb, 0);
                }
            }

            if (removed)
            {
                RepositionAll();
            }
        }

        private void RemoveMessage(int index)
        {
            if (index < 0 || index >= activeMessages.Count)
            {
                return;
            }

            NotificationEntry entry = activeMessages[index];
            if (entry.BgFilter != null && entry.BgFilter.sharedMesh != null)
            {
                Destroy(entry.BgFilter.sharedMesh);
            }
            if (entry.Root != null)
            {
                Destroy(entry.Root);
            }
            activeMessages.RemoveAt(index);
        }

        private void OnDestroy()
        {
            BasisLocalCameraDriver.InstanceExists -= AttachToCamera;

            for (int i = activeMessages.Count - 1; i >= 0; i--)
            {
                RemoveMessage(i);
            }

            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
