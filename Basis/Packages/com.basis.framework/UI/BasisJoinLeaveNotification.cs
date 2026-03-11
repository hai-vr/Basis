using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.Scripts.UI
{
    /// <summary>
    /// Displays join/leave notifications on the player's HUD.
    /// Auto-creates a screen-space overlay Canvas with vertically stacked messages
    /// that fade out after a configurable duration.
    /// </summary>
    public class BasisJoinLeaveNotification : MonoBehaviour
    {
        /// <summary>How long each notification stays visible (seconds).</summary>
        public float MessageDuration = 5f;

        /// <summary>Maximum number of simultaneous notifications.</summary>
        public int MaxMessages = 5;

        /// <summary>Time (seconds) after which the notification begins fading out.</summary>
        public float FadeStartTime = 3.5f;

        /// <summary>Font size for notification text.</summary>
        public float FontSize = 20f;

        private Transform messageContainer;
        private readonly List<NotificationEntry> activeMessages = new List<NotificationEntry>();
        private static BasisJoinLeaveNotification instance;

        private struct NotificationEntry
        {
            public GameObject GameObject;
            public CanvasGroup Group;
            public double SpawnTime;
        }

        /// <summary>
        /// Call once to create and attach the notification system.
        /// Typically invoked from BasisNetworkManagement or scene setup.
        /// </summary>
        public static BasisJoinLeaveNotification Create()
        {
            if (instance != null)
            {
                return instance;
            }

            GameObject root = new GameObject("BasisJoinLeaveNotification");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<BasisJoinLeaveNotification>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            BuildCanvas();
        }

        private void BuildCanvas()
        {
            // Create overlay canvas
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            // Container anchored to bottom-left
            GameObject container = new GameObject("NotificationContainer");
            container.transform.SetParent(transform, false);

            RectTransform containerRect = container.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0, 0);
            containerRect.anchorMax = new Vector2(0, 0);
            containerRect.pivot = new Vector2(0, 0);
            containerRect.anchoredPosition = new Vector2(20, 20);
            containerRect.sizeDelta = new Vector2(500, 400);

            VerticalLayoutGroup layout = container.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4;
            layout.padding = new RectOffset(0, 0, 0, 0);

            ContentSizeFitter fitter = container.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            messageContainer = container.transform;
        }

        private void OnEnable()
        {
            BasisNetworkPlayer.OnRemotePlayerJoined += OnRemotePlayerJoined;
            BasisNetworkPlayer.OnRemotePlayerLeft += OnRemotePlayerLeft;
        }

        private void OnDisable()
        {
            BasisNetworkPlayer.OnRemotePlayerJoined -= OnRemotePlayerJoined;
            BasisNetworkPlayer.OnRemotePlayerLeft -= OnRemotePlayerLeft;
        }

        private void OnRemotePlayerJoined(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            string name = networkPlayer.displayName;
            if (string.IsNullOrEmpty(name))
            {
                name = "Unknown";
            }
            ShowNotification(name + " joined", new Color(0.55f, 1f, 0.55f));
        }

        private void OnRemotePlayerLeft(BasisNetworkPlayer networkPlayer, BasisRemotePlayer remotePlayer)
        {
            string name = networkPlayer.displayName;
            if (string.IsNullOrEmpty(name))
            {
                name = "Unknown";
            }
            ShowNotification(name + " left", new Color(1f, 0.55f, 0.55f));
        }

        private void ShowNotification(string message, Color color)
        {
            // Remove oldest if at capacity
            while (activeMessages.Count >= MaxMessages)
            {
                RemoveMessage(0);
            }

            GameObject msgObj = new GameObject("Notification");
            msgObj.transform.SetParent(messageContainer, false);

            CanvasGroup group = msgObj.AddComponent<CanvasGroup>();

            // Background image for readability
            Image bg = msgObj.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.45f);

            // Horizontal layout to add padding around text
            HorizontalLayoutGroup hlg = msgObj.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 10, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            LayoutElement msgLayout = msgObj.AddComponent<LayoutElement>();
            msgLayout.preferredHeight = 32;

            // Text child
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(msgObj.transform, false);

            TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = message;
            text.fontSize = FontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Left;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Truncate;

            activeMessages.Add(new NotificationEntry
            {
                GameObject = msgObj,
                Group = group,
                SpawnTime = Time.timeAsDouble,
            });
        }

        private void Update()
        {
            double now = Time.timeAsDouble;

            for (int i = activeMessages.Count - 1; i >= 0; i--)
            {
                NotificationEntry entry = activeMessages[i];
                double elapsed = now - entry.SpawnTime;

                if (elapsed >= MessageDuration)
                {
                    RemoveMessage(i);
                    continue;
                }

                // Fade out after FadeStartTime
                if (elapsed >= FadeStartTime)
                {
                    float fadeProgress = (float)(elapsed - FadeStartTime) / (MessageDuration - FadeStartTime);
                    entry.Group.alpha = 1f - fadeProgress;
                }
            }
        }

        private void RemoveMessage(int index)
        {
            if (index < 0 || index >= activeMessages.Count)
            {
                return;
            }

            NotificationEntry entry = activeMessages[index];
            if (entry.GameObject != null)
            {
                Destroy(entry.GameObject);
            }
            activeMessages.RemoveAt(index);
        }

        private void OnDestroy()
        {
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
