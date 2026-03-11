using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.UI.NamePlate;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.Scripts.UI
{
    /// <summary>
    /// Displays join/leave notifications on the player's HUD.
    /// Auto-registers via RuntimeInitializeOnLoadMethod using the same pattern as SettingsProvider.
    /// Creates a screen-space overlay Canvas with vertically stacked messages
    /// that fade out over time. Colors are pulled from BasisRemoteNamePlateDriver
    /// to match the existing nameplate visual language.
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
        public float FontSize = 18f;

        /// <summary>Background alpha for notification pills.</summary>
        public float BackgroundAlpha = 0.55f;

        private Transform messageContainer;
        private readonly List<NotificationEntry> activeMessages = new List<NotificationEntry>();
        private static BasisJoinLeaveNotification instance;

        private struct NotificationEntry
        {
            public GameObject GameObject;
            public CanvasGroup Group;
            public double SpawnTime;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
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
            BuildCanvas();
        }

        private void BuildCanvas()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

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
            layout.childForceExpandWidth = false;
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

            // Use the IsTalkingColor from the nameplate driver (green) to signal a positive event,
            // falling back to the PanelToggle OnColor convention if the driver isn't loaded yet.
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

            // Use the OutOfRangeColor from the nameplate driver (muted/warm) for a disconnect event,
            // falling back to a soft red if the driver isn't loaded yet.
            Color leaveColor = BasisRemoteNamePlateDriver.Instance != null
                ? BasisRemoteNamePlateDriver.StaticOutOfRangeColor
                : new Color(0.85f, 0.35f, 0.35f, 1f);

            ShowNotification(name + " left", leaveColor);
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

            // Semi-transparent dark background matching the nameplate material style
            Image bg = msgObj.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, BackgroundAlpha);

            // Padding around the text
            HorizontalLayoutGroup hlg = msgObj.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 12, 6, 6);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            ContentSizeFitter msgFitter = msgObj.AddComponent<ContentSizeFitter>();
            msgFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            msgFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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
