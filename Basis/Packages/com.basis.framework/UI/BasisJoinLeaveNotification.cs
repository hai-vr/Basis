using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
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
    /// Uses a world-space Canvas parented under BasisLocalCameraDriver.ParentOfUI
    /// so it works correctly in both VR and desktop modes.
    /// Colors are pulled from BasisRemoteNamePlateDriver to match the nameplate style.
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

        /// <summary>Local position offset from ParentOfUI.</summary>
        public Vector3 LocalPosition = new Vector3(-10f, -3f, 0f);

        /// <summary>Local scale for the world-space canvas (world units per UI pixel).</summary>
        public Vector3 LocalScale = new Vector3(0.02f, 0.02f, 0.02f);

        private Canvas canvas;
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

        /// <summary>
        /// Parents this object under the camera driver's ParentOfUI transform
        /// and assigns the world camera, following the BasisUILoadingBar pattern.
        /// </summary>
        private void AttachToCamera()
        {
            if (BasisLocalCameraDriver.Instance == null)
            {
                return;
            }

            transform.SetParent(BasisLocalCameraDriver.Instance.ParentOfUI, false);
            transform.SetLocalPositionAndRotation(LocalPosition, Quaternion.identity);
            transform.localScale = LocalScale;

            if (canvas != null)
            {
                canvas.worldCamera = BasisLocalCameraDriver.Instance.Camera;
            }
        }

        private void BuildCanvas()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(500, 400);
            canvasRect.pivot = new Vector2(0, 0);

            // Container for vertical message stack
            GameObject container = new GameObject("NotificationContainer");
            container.transform.SetParent(transform, false);

            RectTransform containerRect = container.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0, 0);
            containerRect.anchorMax = new Vector2(1, 1);
            containerRect.offsetMin = Vector2.zero;
            containerRect.offsetMax = Vector2.zero;

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

            GameObject msgObj = new GameObject("Notification");
            msgObj.transform.SetParent(messageContainer, false);

            CanvasGroup group = msgObj.AddComponent<CanvasGroup>();

            Image bg = msgObj.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, BackgroundAlpha);

            HorizontalLayoutGroup hlg = msgObj.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 12, 6, 6);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            ContentSizeFitter msgFitter = msgObj.AddComponent<ContentSizeFitter>();
            msgFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            msgFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

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
