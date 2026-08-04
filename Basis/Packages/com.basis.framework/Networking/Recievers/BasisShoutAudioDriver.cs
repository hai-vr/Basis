using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.VoiceRecording;
#if !UNITY_SERVER
#endif
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static SerializableBasis;

namespace Basis.Scripts.Networking.Receivers
{
    /// <summary>
    /// Global driver for shout-mode audio sources.
    /// Shout audio sources are NOT parented to remote players and are NOT
    /// affected by distance culling, LOD, avatar unloading, or spatialization.
    /// Each shouting player gets one non-spatialized (2D) AudioSource parented
    /// to BasisDeviceManagement.Instance so it persists across scene loads.
    /// </summary>
    public static class BasisShoutAudioDriver
    {
        /// <summary>
        /// Per-player shout audio state.
        /// </summary>
        private class ShoutAudioEntry
        {
            public ushort PlayerId;
            public BasisAudioReceiver Receiver;
            public AudioSource AudioSource;
            public BasisRemoteAudioDriver Driver;
            public GameObject Root;

            /// <summary>
            /// The player's own viseme driver, borrowed for the duration of the shout. Held here
            /// so teardown can hand it back without a receiver lookup that may already be gone.
            /// </summary>
            public BasisAudioAndVisemeDriver VisemeDriver;
        }

        private static readonly Dictionary<ushort, ShoutAudioEntry> _entries = new Dictionary<ushort, ShoutAudioEntry>();

        /// <summary>
        /// Enables shout mode for a player. Creates a non-spatialized audio source
        /// on BasisDeviceManagement.Instance, independent of the remote player hierarchy.
        /// </summary>
        public static void EnableShoutMode(ushort playerId)
        {
#if UNITY_SERVER
            BasisDebug.LogWarning($"Ignoring shout audio enable for player {playerId} on server/headless build.");
            return;
#else
            if (_entries.ContainsKey(playerId))
            {
                return; // already active
            }

            if (BasisDeviceManagement.Instance == null)
            {
                BasisDebug.LogError("BasisDeviceManagement.Instance is null, cannot create shout audio source.");
                return;
            }

            var entry = new ShoutAudioEntry();
            entry.PlayerId = playerId;

            // Create a new BasisAudioReceiver for the shout channel
            entry.Receiver = new BasisAudioReceiver();

            // Initialize the decoder
            BasisAudioReceiver.outputSampleRate = AudioSettings.outputSampleRate;
            BasisAudioReceiver.silentData ??= new float[RemoteOpusSettings.MaxFrameSize];

#if UNITY_IOS && !UNITY_EDITOR
            entry.Receiver.decoder = new OpusSharp.Core.Static.OpusDecoder(RemoteOpusSettings.NetworkSampleRate, RemoteOpusSettings.Channels);
#else
            entry.Receiver.decoder = new OpusSharp.Core.Dynamic.OpusDecoder(RemoteOpusSettings.NetworkSampleRate, RemoteOpusSettings.Channels);
#endif

            // Own GameObject per shouter: OnAudioFilterRead scripts run for every
            // AudioSource on the same GameObject, so shared hosting breaks with
            // multiple simultaneous shouters.
            entry.Root = new GameObject($"Shout Audio {playerId}");
            entry.Root.transform.SetParent(BasisDeviceManagement.Instance.transform, false);
            entry.AudioSource = entry.Root.AddComponent<AudioSource>();
            entry.AudioSource.clip = BasisAudioClipPool.Get(playerId);
            entry.AudioSource.loop = true;

            // Non-spatialized settings: pure 2D audio
            entry.AudioSource.spatialBlend = 0f;
            entry.AudioSource.spatialize = false;
            entry.AudioSource.spatializePostEffects = false;
            entry.AudioSource.dopplerLevel = 0f;
            entry.AudioSource.spread = 0f;
            entry.AudioSource.minDistance = 0f;
            entry.AudioSource.maxDistance = float.MaxValue;
            entry.AudioSource.rolloffMode = AudioRolloffMode.Linear;
            entry.AudioSource.volume = 1f;

            // Wire up the audio driver so OnAudioFilterRead fires
            entry.Driver = entry.Root.AddComponent<BasisRemoteAudioDriver>();
            entry.Driver.BasisAudioReceiver = entry.Receiver;

            // This source, not the player's silent spatial one, feeds lip-sync for the duration
            // of the shout. See BasisRemoteAudioDriver.OwnsVisemeTap.
            entry.Driver.IsShoutSource = true;

            entry.Receiver.audioSource = entry.AudioSource;
            entry.Receiver.AudioSourceTransform = entry.Root.transform;
            entry.Receiver.DirectionalDampeningMultiplier = 1f;

            // Initialize audio processing buffers BEFORE setting HasAudioSource.
            // Without this, OnAudioFilterRead runs with null scratch buffers = buzzing.
            entry.Receiver.InitializeForPlayback();

            // Now safe to enable - OnAudioFilterRead can process correctly
            entry.Receiver.HasAudioSource = true;

            // Wire up the player's existing viseme driver so lip-sync works during shout mode
            if (BasisNetworkPlayers.RemotePlayerReceivers.TryGetValue(playerId, out BasisNetworkReceiver receiver))
            {
                BasisAudioAndVisemeDriver viseme = receiver.AudioReceiverModule.visemeDriver;
                entry.VisemeDriver = viseme;

                // Order matters here. By the time a shout starts, the normal path has usually
                // already retired this driver: the viseme distance cutoff drops it out of
                // ActiveDrivers, and going out of hearing range pools the player's spatial
                // AudioSource, whose ResetForPool unregisters the driver and releases its
                // OpenLipSync context outright. So flag it first (SetVisemeRange honours the flag
                // and stops the distance pass fighting us), force it back in range, and only then
                // Initialize — which re-registers it when the pool return had dropped it, and adds
                // it to ActiveDrivers because InVisemeRange is true again by that point.
                viseme.ShoutActive = true;
                BasisRemoteAudioDriver.SetVisemeRange(viseme, true);
                entry.Driver.Initialize(viseme);
            }
            else
            {
                entry.Driver.Initialized = true;
            }

            entry.AudioSource.Play();

            _entries[playerId] = entry;
            BasisVoiceRecording.OnShoutReceiverCreated(playerId, entry.Receiver);
            BasisDebug.Log($"Shout audio enabled for player {playerId}");
#endif
        }

        /// <summary>
        /// Disables shout mode for a player. Destroys their audio components.
        /// </summary>
        public static void DisableShoutMode(ushort playerId)
        {
            if (!_entries.TryGetValue(playerId, out var entry))
            {
                return;
            }

            entry.Receiver.HasAudioSource = false;

#if !UNITY_SERVER
            if (entry.Receiver.decoder != null)
            {
                entry.Receiver.decoder.Dispose();
                entry.Receiver.decoder = null;
            }
#endif

            if (entry.AudioSource != null)
            {
                entry.AudioSource.Stop();
                if (entry.AudioSource.clip != null)
                {
                    BasisAudioClipPool.Return(entry.AudioSource.clip);
                }
                Object.Destroy(entry.AudioSource);
            }

            if (entry.VisemeDriver != null)
            {
                // Hand the driver back to the distance rule; the next transmission tick recomputes
                // InVisemeRange and retires it if they really are too far to read.
                entry.VisemeDriver.ShoutActive = false;

                // If the player's own spatial AudioSource is not currently holding this driver —
                // the out-of-range shouter, whose source was pooled — then the shout path was its
                // only owner and it has to be retired here, or it dangles in the static registry
                // being ticked every frame with nothing left to feed it.
                bool spatialPathOwnsIt =
                    BasisNetworkPlayers.RemotePlayerReceivers.TryGetValue(playerId, out BasisNetworkReceiver receiver)
                    && receiver.AudioReceiverModule != null
                    && receiver.AudioReceiverModule.HasAudioSource;

                if (!spatialPathOwnsIt)
                {
                    BasisRemoteAudioDriver.SetVisemeRange(entry.VisemeDriver, false);
                    BasisRemoteAudioDriver.UnregisterDriver(entry.VisemeDriver);
                }
                entry.VisemeDriver = null;
            }

            if (entry.Driver != null)
            {
                // Detach the shared viseme driver before destroying so OnDestroy
                // doesn't clean up the player's viseme driver
                entry.Driver.BasisAudioAndVisemeDriver = null;
                entry.Driver.Initialized = false;
                Object.Destroy(entry.Driver);
            }

            if (entry.Root != null)
            {
                Object.Destroy(entry.Root);
            }

            _entries.Remove(playerId);
            BasisDebug.Log($"Shout audio disabled for player {playerId}");
        }

        /// <summary>
        /// Returns true if a player currently has an active shout audio source.
        /// </summary>
        public static bool IsInShoutMode(ushort playerId)
        {
            return _entries.ContainsKey(playerId);
        }

        /// <summary>
        /// Exposes a shouting player's receiver so the voice-recording tap can follow the
        /// shout audio path. Returns false when the player is not currently shouting.
        /// </summary>
        internal static bool TryGetReceiver(ushort playerId, out BasisAudioReceiver receiver)
        {
            if (_entries.TryGetValue(playerId, out ShoutAudioEntry entry))
            {
                receiver = entry.Receiver;
                return true;
            }
            receiver = null;
            return false;
        }

        /// <summary>
        /// Inserts an audio segment into the shout receiver's jitter buffer.
        /// Auto-enables shout mode if not already active.
        /// </summary>
        public static void ReceiveShoutAudio(ushort playerId, AudioSegmentDataMessage audioData)
        {
            if (!_entries.TryGetValue(playerId, out var entry))
            {
                // Auto-enable when we receive shout audio (handles late joiners)
                EnableShoutMode(playerId);
                if (!_entries.TryGetValue(playerId, out entry))
                {
                    return; // failed to create
                }
            }

            entry.Receiver.Insert(audioData);

            // Notify the player's nameplate that audio was received so it shows the talking state
            if (BasisNetworkPlayers.RemotePlayerReceivers.TryGetValue(playerId, out BasisNetworkReceiver receiver))
            {
                receiver.Player.AudioReceived?.Invoke();
            }
        }

        /// <summary>
        /// Must be called each frame to drain jitter buffers and decode audio.
        /// </summary>
        public static void DrainAll()
        {
            foreach (var kvp in _entries)
            {
                var receiver = kvp.Value.Receiver;
                if (!receiver.IsAudioActive || receiver.VoiceBuffer.DecodedFrameCount == 0)
                {
                    receiver.DrainAndDecodeThreadSafe();
                }
                receiver.ApplyAudioState();
            }
        }

        /// <summary>
        /// Cleans up a player's shout state (call on disconnect).
        /// </summary>
        public static void RemovePlayer(ushort playerId)
        {
            DisableShoutMode(playerId);
        }

        /// <summary>
        /// Cleans up all shout audio sources and resets local shout state (call on disconnect from server).
        /// </summary>
        public static void DeInitialize()
        {
            var keys = new List<ushort>(_entries.Keys);
            foreach (var key in keys)
            {
                DisableShoutMode(key);
            }

            // Reset local player shout state
            Basis.Scripts.Networking.Transmitters.BasisAudioTransmission.IsInShoutMode = false;
        }
    }
}
