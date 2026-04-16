using System;
using Basis.BasisUI;
using Basis.Scripts.Networking;
using UnityEngine;

namespace Basis.Streaming
{
    /// <summary>
    /// Owns the <see cref="BasisStreamingMetaServer"/> lifecycle. Subscribes to
    /// <see cref="BasisSettingsDefaults.EnableStreamingMeta"/> so the listener
    /// starts/stops the moment the user flips the toggle.
    /// </summary>
    public sealed class BasisStreamingMetaRuntime : MonoBehaviour
    {
        public const string Host = "127.0.0.1";
        public const int Port = 9080;

        private static BasisStreamingMetaRuntime instance;

        private BasisStreamingMetaServer server;
        private float smoothedDelta;
        private bool subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
            {
                return;
            }

            GameObject go = new GameObject(nameof(BasisStreamingMetaRuntime));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BasisStreamingMetaRuntime>();
        }

        private void OnEnable()
        {
            if (!subscribed)
            {
                BasisSettingsDefaults.EnableStreamingMeta.OnChanged += HandleSettingChanged;
                subscribed = true;
            }

            ApplyCurrentSetting();
        }

        private void OnDisable()
        {
            if (subscribed)
            {
                BasisSettingsDefaults.EnableStreamingMeta.OnChanged -= HandleSettingChanged;
                subscribed = false;
            }

            StopServer();
        }

        private void OnDestroy()
        {
            StopServer();
            if (instance == this)
            {
                instance = null;
            }
        }

        private void HandleSettingChanged(bool _) => ApplyCurrentSetting();

        private void ApplyCurrentSetting()
        {
            if (BasisSettingsDefaults.EnableStreamingMeta.RawValue)
            {
                StartServer();
            }
            else
            {
                StopServer();
            }
        }

        private void StartServer()
        {
            if (server != null)
            {
                return;
            }

            try
            {
                server = new BasisStreamingMetaServer(Host, Port);
                Debug.Log($"[BasisStreamingMeta] overlay available at {server.Prefix}overlay.html");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BasisStreamingMeta] failed to bind http://{Host}:{Port}: {ex.Message}");
                server = null;
            }
        }

        private void StopServer()
        {
            if (server == null)
            {
                return;
            }

            server.Dispose();
            server = null;
        }

        private void Update()
        {
            if (server == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            smoothedDelta += (dt - smoothedDelta) * 0.1f;
            float fps = smoothedDelta > 0f ? 1f / smoothedDelta : 0f;

            var snapshot = new BasisStreamingMetaServer.Snapshot
            {
                Fps = fps,
                TimeUtc = DateTimeOffset.UtcNow,
            };

            var peer = BasisNetworkConnection.LocalPlayerPeer;
            if (peer != null)
            {
                snapshot.Connected = true;
                snapshot.Ccu = BasisNetworkPlayers.ReceiverCount + 1;
                snapshot.PeerLimit = BasisNetworkManagement.ServerMetaDataMessage.PeerLimit;
                snapshot.RoundTripMs = peer.RoundTripTime;
                snapshot.PingMs = peer.Ping;
            }

            server.PublishSnapshot(snapshot);
        }
    }
}
