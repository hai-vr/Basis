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
    /// Uses TextMeshPro and SpriteRenderer (no Canvas) parented under
    /// BasisLocalCameraDriver.ParentOfUI for VR and desktop compatibility.
    /// </summary>
    public class BasisJoinLeaveNotification : MonoBehaviour
    {
        public float MessageDuration = 5f;
        public int MaxMessages = 5;
        public float FadeStartTime = 3.5f;
        public float FontSize = 6f;
        public float LineHeight = 1.6f;
        public float BackgroundAlpha = 0.55f;
        public float BackgroundPadding = 0.4f;
        public Vector3 LocalPosition = new Vector3(0f, -1.2f, 0f);
        public Vector3 LocalScale = new Vector3(1f, 1f, 1f);

        private readonly List<NotificationEntry> activeMessages = new List<NotificationEntry>();
        private static BasisJoinLeaveNotification instance;
        private static Sprite whiteSprite;

        private struct NotificationEntry
        {
            public GameObject Root;
            public TextMeshPro Text;
            public SpriteRenderer Background;
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
            EnsureWhiteSprite();
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

        private static void EnsureWhiteSprite()
        {
            if (whiteSprite != null)
            {
                return;
            }

            Texture2D tex = new Texture2D(4, 4);
            Color[] pixels = new Color[16];
            for (int i = 0; i < 16; i++)
            {
                pixels[i] = Color.white;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
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

            // Root for this notification
            GameObject root = new GameObject("Notification");
            root.transform.SetParent(transform, false);

            // Background sprite
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(root.transform, false);

            SpriteRenderer bg = bgObj.AddComponent<SpriteRenderer>();
            bg.sprite = whiteSprite;
            bg.color = new Color(0f, 0f, 0f, BackgroundAlpha);
            bg.sortingOrder = 0;

            // Text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(root.transform, false);
            textObj.transform.localPosition = new Vector3(0f, 0f, -0.01f);

            TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
            tmp.text = message;
            tmp.fontSize = FontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode =  TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.sortingOrder = 1;

            // Size the background to fit the text
            tmp.ForceMeshUpdate();
            Vector2 textSize = tmp.GetRenderedValues(true);
            float bgWidth = textSize.x + BackgroundPadding * 2f;
            float bgHeight = textSize.y + BackgroundPadding * 2f;
            bg.size = new Vector2(bgWidth, bgHeight);
            bg.drawMode = SpriteDrawMode.Sliced;

            activeMessages.Add(new NotificationEntry
            {
                Root = root,
                Text = tmp,
                Background = bg,
                TextColor = color,
                SpawnTime = Time.timeAsDouble,
            });

            RepositionAll();
        }

        private void RepositionAll()
        {
            float y = 0f;
            for (int i = activeMessages.Count - 1; i >= 0; i--)
            {
                NotificationEntry entry = activeMessages[i];
                y -= LineHeight;
                entry.Root.transform.localPosition = new Vector3(0f, y, 0f);
            }
        }

        private void Update()
        {
            double now = Time.timeAsDouble;
            bool removed = false;

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

                    Color bc = entry.Background.color;
                    bc.a = BackgroundAlpha * alpha;
                    entry.Background.color = bc;
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
