using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking.NetworkedAvatar;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Basis.Scripts.UI
{
    /// <summary>
    /// Displays join/leave notifications below the microphone icon on the player's HUD.
    /// Uses TextMeshPro + SpriteRenderer for the background.
    /// Parented under BasisLocalCameraDriver.ParentOfUI for VR and desktop.
    ///
    /// Fully static – driven by BasisEventDriver.LateUpdate via Simulate().
    /// Object pool pre-allocates MaxMessages slots, reuses GameObjects.
    /// </summary>
    public static class BasisJoinLeaveNotification
    {
        public static float MessageDuration = 5f;
        public static int MaxMessages = 5;
        public static float FadeStartTime = 3.5f;
        public static float FontSize = 28f;
        public static float FontSizeMin = 14f;
        public static float FontSizeMax = 28f;
        public static float BackgroundPadding = 4f;
        public static float MinHalfWidth = 10f;
        public static float MinHalfHeight = 1.5f;
        public static float LineSpacing = 4f;
        public static float TextRectWidth = 58f;
        public static float TextRectHeight = 10f;
        public static Vector3 LocalPosition = new Vector3(0f, -4f, 0f);
        public static Color BackgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);

        private static readonly List<NotificationSlot> activeSlots = new List<NotificationSlot>();
        private static readonly Stack<NotificationSlot> pool = new Stack<NotificationSlot>();
        private static Transform root;
        private static bool initialized;
        private static Sprite whiteSprite;
        private static Texture2D whiteTexture;

        private class NotificationSlot
        {
            public GameObject Root;
            public GameObject BgObj;
            public GameObject TextObj;
            public TextMeshPro Text;
            public SpriteRenderer BgSprite;
            public Color TextColor;
            public double SpawnTime;
        }

        public static void Create()
        {
            if (initialized)
            {
                return;
            }

            GameObject go = new GameObject("BasisJoinLeaveNotification");
            Object.DontDestroyOnLoad(go);
            root = go.transform;
            initialized = true;

            CreateWhiteSprite();
            PrewarmPool();

            BasisNetworkPlayer.OnRemotePlayerJoined += OnRemotePlayerJoined;
            BasisNetworkPlayer.OnRemotePlayerLeft += OnRemotePlayerLeft;

            if (BasisLocalCameraDriver.HasInstance)
            {
                AttachToCamera();
            }
            BasisLocalCameraDriver.InstanceExists += AttachToCamera;
        }

        public static void Shutdown()
        {
            if (!initialized)
            {
                return;
            }

            BasisNetworkPlayer.OnRemotePlayerJoined -= OnRemotePlayerJoined;
            BasisNetworkPlayer.OnRemotePlayerLeft -= OnRemotePlayerLeft;
            BasisLocalCameraDriver.InstanceExists -= AttachToCamera;

            activeSlots.Clear();
            pool.Clear();

            if (root != null)
            {
                Object.Destroy(root.gameObject);
                root = null;
            }

            if (whiteTexture != null)
            {
                Object.Destroy(whiteTexture);
                whiteTexture = null;
            }
            whiteSprite = null;
            initialized = false;
        }

        /// <summary>
        /// Per-frame update driven by BasisEventDriver.LateUpdate.
        /// Handles fade-out and expiry of active notifications.
        /// </summary>
        public static void Simulate(double now)
        {
            if (!initialized || activeSlots.Count == 0)
            {
                return;
            }

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

                    Color bg = BackgroundColor;
                    bg.a = alpha;
                    slot.BgSprite.color = bg;
                }
            }

            if (removed)
            {
                RepositionAll();
            }
        }

        private static void AttachToCamera()
        {
            if (root == null || BasisLocalCameraDriver.Instance == null)
            {
                return;
            }

            root.SetParent(BasisLocalCameraDriver.Instance.ParentOfUI, false);
            root.SetLocalPositionAndRotation(LocalPosition, Quaternion.identity);
            root.localScale = Vector3.one;
        }

        private static void CreateWhiteSprite()
        {
            whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
            whiteSprite = Sprite.Create(whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        private static void PrewarmPool()
        {
            for (int i = 0; i < MaxMessages; i++)
            {
                pool.Push(CreateSlot());
            }
        }

        private static NotificationSlot CreateSlot()
        {
            NotificationSlot slot = new NotificationSlot();

            slot.Root = new GameObject("Notification");
            slot.Root.transform.SetParent(root, false);

            slot.BgObj = new GameObject("Background");
            slot.BgObj.transform.SetParent(slot.Root.transform, false);
            slot.BgObj.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            slot.BgObj.transform.localScale = Vector3.one;

            slot.BgSprite = slot.BgObj.AddComponent<SpriteRenderer>();
            slot.BgSprite.sprite = whiteSprite;
            slot.BgSprite.drawMode = SpriteDrawMode.Sliced;
            slot.BgSprite.size = Vector2.one;
            slot.BgSprite.color = BackgroundColor;

            slot.TextObj = new GameObject("Text");
            slot.TextObj.transform.SetParent(slot.Root.transform, false);
            slot.TextObj.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
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

            if (slot.Text.TryGetComponent<RectTransform>(out RectTransform textRect))
            {
                textRect.sizeDelta = new Vector2(TextRectWidth, TextRectHeight);
            }

            slot.Root.SetActive(false);
            return slot;
        }

        private static NotificationSlot AcquireSlot()
        {
            if (pool.Count > 0)
            {
                return pool.Pop();
            }
            return CreateSlot();
        }

        private static void ReleaseSlot(NotificationSlot slot)
        {
            slot.Root.SetActive(false);
            slot.BgSprite.color = BackgroundColor;
            pool.Push(slot);
        }

        private static void OnRemotePlayerJoined(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            string name = networkPlayer.displayName;
            if (string.IsNullOrEmpty(name))
            {
                name = "Unknown";
            }

            ShowNotification(name + " joined", Color.white);
        }

        private static void OnRemotePlayerLeft(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            string name = networkPlayer.displayName;
            if (string.IsNullOrEmpty(name))
            {
                name = "Unknown";
            }

            ShowNotification(name + " left", Color.white);
        }

        private static void ShowNotification(string message, Color color)
        {
            if (!initialized)
            {
                return;
            }

            while (activeSlots.Count >= MaxMessages)
            {
                ReleaseSlot(activeSlots[0]);
                activeSlots.RemoveAt(0);
            }

            NotificationSlot slot = AcquireSlot();

            slot.Text.text = message;
            slot.Text.color = color;
            slot.TextColor = color;
            slot.SpawnTime = Time.timeAsDouble;

            slot.Text.ForceMeshUpdate();
            Vector2 textSize = slot.Text.GetRenderedValues(true);

            float bgWidth = Mathf.Max(textSize.x + BackgroundPadding * 2f, MinHalfWidth * 2f);
            float bgHeight = Mathf.Max(textSize.y / 2f + BackgroundPadding, MinHalfHeight * 2f);

            slot.BgSprite.size = new Vector2(bgWidth, bgHeight);
            slot.BgSprite.color = BackgroundColor;
            slot.Root.SetActive(true);

            activeSlots.Add(slot);
            RepositionAll();
        }

        private static void RepositionAll()
        {
            float y = 0f;
            for (int i = activeSlots.Count - 1; i >= 0; i--)
            {
                activeSlots[i].Root.transform.localPosition = new Vector3(0f, y, 0f);
                y -= LineSpacing;
            }
        }
    }
}
