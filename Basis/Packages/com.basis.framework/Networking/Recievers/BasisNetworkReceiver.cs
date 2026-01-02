using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Profiler;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static SerializableBasis;

namespace Basis.Scripts.Networking.Receivers
{
    /// <summary>
    /// Receives networked avatar state for a remote player, stages and interpolates frames,
    /// and applies a posed result to the avatar each frame. Also brokers remote audio.
    /// </summary>
    [DefaultExecutionOrder(15001)]
    [Serializable]
    public class BasisNetworkReceiver : BasisNetworkPlayer
    {
        private const int EyesAndMouthOffset = 15; // L/R up/down, L/R left/right, mouth open/smile
        private const int EyesAndMouthCount = 6;
        public const int EyeAndMouthSize = EyesAndMouthOffset * sizeof(float);
        public const int EyeAndMouthCountInBytes = EyesAndMouthCount * sizeof(float);

        /// <summary>
        /// If staging backlog exceeds this, older frames are dropped to reduce latency.
        /// </summary>
        public static int BufferCapacityBeforeCleanup = 5;

        [SerializeReference] public BasisAudioReceiver AudioReceiverModule = new BasisAudioReceiver();
        [SerializeField] public ConcurrentQueue<BasisAvatarBuffer> PayloadQueue = new ConcurrentQueue<BasisAvatarBuffer>();
        public BasisRemotePlayer RemotePlayer;
        [SerializeReference] public BasisRemoteAvatarBufferHolder BufferHolder = new BasisRemoteAvatarBufferHolder();

        public bool hasEvents = false;
        public float[] EyesAndMouth = new float[] { 0, 0, 0, 0, 1, 0 }; // default neutral eyes, mouth open=1 for breathing

        public quaternion ApplyingRotation;
        public float3 ApplyingScale;

        private float interpolationTime = 0f; // 0..1 over current->next window

        public bool HasBufferHolds;
        public bool PassedSimulate = false;

        // ---------------- staging (ring buffer) ----------------
        private const int MaxStage = 64;
        public int StagedCount;

        // Main-thread-only jitter buffer. Bounded. Overwrites oldest when full.
        private BasisRingBuffer<BasisAvatarBuffer> _stagedRing;

        public Transform LastAvatarsTransform;
        public bool DidLastAvatarTransformChanged;

        // ---------- NEW: time-warp parameters ----------
        // Playback rate control: catches up smoothly when backlog grows.
        private const int TargetJitterDepth = 2;          // desired staged depth cushion
        private const float CatchupGain = 0.15f;          // 0.05..0.25 tune
        private const float MinPlaybackRate = 0.85f;
        private const float MaxPlaybackRate = 1.35f;

        /// <summary>
        /// Main-thread simulation step. Pulls packets, maintains interpolation window,
        /// computes interpolationTime, and feeds inputs to the network driver.
        /// </summary>
        public void Compute(float unscaledDeltaTime)
        {
            if (Player.BasisAvatar == null) return; // expected briefly on join

            if (LastAvatarsTransform != Player.AvatarTransform)
            {
                LastAvatarsTransform = Player.AvatarTransform;
                DidLastAvatarTransformChanged = true;
            }

            if (Player.BasisAvatar.Animator == null)
            {
                BasisDebug.LogError($"Animator for {Player.DisplayName} lost", BasisDebug.LogTag.Remote);
                return;
            }

            if (Player.AvatarTransform == null)
            {
                BasisDebug.LogError($"AvatarTransform for {Player.DisplayName} lost", BasisDebug.LogTag.Remote);
                return;
            }

            // 1) Pull network packets to main-thread staging ring (bounded)
            while (PayloadQueue.TryDequeue(out var buffer))
            {
                _stagedRing.EnqueueOverwriteOldest(buffer, onOverwrite: BasisAvatarBufferPool.Release);
            }
            StagedCount = _stagedRing.Count;

            // 2) Ensure we have a valid interpolation window (Current -> Next)
            if (!BufferHolder.HasCurrentBuffer)
                TrySeedFirstFromStaging();   // patched: only takes ONE oldest

            if (!BufferHolder.HasNextBuffer)
                TrySetLastFromStaging();     // patched: only takes ONE next-oldest

            HasBufferHolds = BufferHolder.HasCurrentBuffer && BufferHolder.HasNextBuffer;
            if (!HasBufferHolds) return;

            // 2b) Advance window while consumed and we have staged frames
            while (interpolationTime >= 1f && _stagedRing.Count != 0)
            {
                BufferHolder.NextBecomesCurrent();
                interpolationTime = 0f;
                BufferHolder.HasNextBuffer = false;
                TrySetLastFromStaging();

                HasBufferHolds = BufferHolder.HasCurrentBuffer && BufferHolder.HasNextBuffer;
                if (!HasBufferHolds) break;
            }

            // 2c) Latency clamp: drop oldest if backlog too large (optional safety)
            StagedCount = _stagedRing.Count;
            if (StagedCount > BufferCapacityBeforeCleanup)
            {
                int drop = StagedCount - BufferCapacityBeforeCleanup;
                DropOldestFromStaging(drop);
            }

            HasBufferHolds = BufferHolder.HasCurrentBuffer && BufferHolder.HasNextBuffer;

            // 3) If we have a window, compute interpolation fraction and feed the driver
            if (HasBufferHolds)
            {
                var first = BufferHolder.Current;
                var last = BufferHolder.Next;

                double windowDuration =
                    last.SecondsInterval > 0 ? last.SecondsInterval :
                    first.SecondsInterval > 0 ? first.SecondsInterval :
                    (1.0 / 60.0);

                if (!double.IsFinite(windowDuration) || windowDuration <= 1e-6)
                    windowDuration = 1e-3;

                double step = Math.Max(unscaledDeltaTime, 0.0);

                // ---------- NEW: time-warp playback rate based on backlog ----------
                int depth = _stagedRing.Count;
                float rate = 1f + CatchupGain * (depth - TargetJitterDepth);
                rate = Mathf.Clamp(rate, MinPlaybackRate, MaxPlaybackRate);

                interpolationTime += (float)((step / windowDuration) * rate);

                // ---------- NEW: send REAL deltaTime seconds to driver ----------
                // Here we use unscaledDeltaTime as the filter dt.
                // If you prefer, use (float)windowDuration for a more "network-sampling" dt.
                float dtSeconds = Mathf.Max(unscaledDeltaTime, 1e-3f);

                PassedSimulate = BasisRemoteNetworkDriver.SetFrameTiming(playerId, interpolationTime, dtSeconds);

                if (PassedSimulate && BufferHolder.SentLatest)
                {
                    BasisRemoteNetworkDriver.SetFrameInputs(
                        playerId,
                        Player.BasisAvatar.HumanScale,
                        first.Position, last.Position,
                        first.Scale, last.Scale,
                        first.Rotation, last.Rotation
                    );

                    BasisRemoteNetworkDriver.SetMuscleWindow(playerId, first.Muscles, last.Muscles);
                    BufferHolder.SentLatest = false;
                }
            }
        }

        /// <summary>
        /// Main-thread application step. Pulls posed outputs from the driver and applies
        /// body position/rotation/muscles to the avatar via PoseHandler.
        /// </summary>
        public void Apply()
        {
            if (!PassedSimulate) return;

            BasisRemoteNetworkDriver.GetOutputs_NoAlloc(playerId, out bool outscale, out ApplyingRotation, out float3 scaledBody);

            BasisRemoteNetworkDriver.GetMuscleArray(
                playerId,
                ref HumanPose,
                EyesAndMouth,
                EyesAndMouthOffset,
                EyeAndMouthCountInBytes
            );

            HumanPose.bodyPosition = scaledBody;
            HumanPose.bodyRotation = ApplyingRotation;

            PoseHandler.SetHumanPose(ref HumanPose);

            if (outscale)
            {
                ApplyScale();
            }
            else
            {
                if (DidLastAvatarTransformChanged)
                {
                    ApplyScale();
                    DidLastAvatarTransformChanged = false;
                }
            }

            if (HasOverridenDestination)
            {
                var References = RemotePlayer.RemoteAvatarDriver.References;
                References.Hips.transform.SetPositionAndRotation(OverridenPosition, OverridenRotation);
            }

            PassedSimulate = false;
        }

        public void ApplyScale()
        {
            BasisRemoteNetworkDriver.GetScaleOutput(playerId, out ApplyingScale);
            Player.AvatarTransform.localScale = ApplyingScale;
        }

        public void EnQueueAvatarBuffer(BasisAvatarBuffer avatarBuffer)
        {
            PayloadQueue.Enqueue(avatarBuffer);
        }

        public override void Initialize()
        {
            RemotePlayer = (BasisRemotePlayer)Player;
            if (RemotePlayer == null)
            {
                BasisDebug.LogError("Remote Player was not found During Initialization!!");
                return;
            }

            if (RemotePlayer.RemoteAvatarDriver == null)
            {
                BasisDebug.LogError("Remote Player RemoteAvatarDriver was not found During Initialization!!");
                return;
            }

            AudioReceiverModule.Initalize(this);

            // Reset staging
            _stagedRing = new BasisRingBuffer<BasisAvatarBuffer>(MaxStage);
            StagedCount = 0;
            BufferHolder.ClearAndRelease();
            interpolationTime = 0f;
            PassedSimulate = false;

            // Clear any packets that arrived before init (rare, but safe)
            while (PayloadQueue.TryDequeue(out var buf))
            {
                BasisAvatarBufferPool.Release(buf);
            }

            if (!hasEvents)
            {
                RemotePlayer.RemoteAvatarDriver.CalibrationComplete += OnCalibration;
                hasEvents = true;
            }
        }

        public void OnCalibration()
        {
            AudioReceiverModule.AvatarChanged(this, true);

            List<byte> keysToRemove = new List<byte>();
            foreach (KeyValuePair<byte, ServerAvatarDataMessageQueue> message in NextMessages)
            {
                ServerAvatarDataMessage avatarMessage = message.Value.ServerAvatarDataMessage;
                RemoteAvatarDataMessage Remote = avatarMessage.avatarDataMessage;
                PlayerIdMessage playerIdMessage = avatarMessage.playerIdMessage;

                bool isSameAvatar = Remote.AvatarLinkIndex == LastLinkedAvatarIndex;
                if (isSameAvatar)
                {
                    NetworkBehaviours[message.Key].OnNetworkMessageReceived(
                        playerIdMessage.playerID,
                        Remote.payload,
                        message.Value.Method
                    );
                    keysToRemove.Add(message.Key);
                }
                else
                {
                    bool isPastMessage = IsPastAvatar(Remote.AvatarLinkIndex, LastLinkedAvatarIndex);
                    if (isPastMessage)
                    {
                        BasisDebug.Log($"Discarding stale message with AvatarLinkIndex {Remote.AvatarLinkIndex}");
                        keysToRemove.Add(message.Key);
                    }
                }
            }

            foreach (byte key in keysToRemove)
                NextMessages.Remove(key);
        }

        private bool IsPastAvatar(byte messageIndex, byte currentIndex)
        {
            int diff = (currentIndex - messageIndex + 256) % 256;
            return diff > 0 && diff < 128;
        }

        public override void DeInitialize()
        {
            // Release anything staged in the ring
            if (_stagedRing != null)
            {
                while (_stagedRing.TryDequeueOldest(out var buf))
                    BasisAvatarBufferPool.Release(buf);

                StagedCount = 0;
            }

            // Release anything still waiting in the concurrent ingress queue
            while (PayloadQueue.TryDequeue(out var buffer))
                BasisAvatarBufferPool.Release(buffer);

            BufferHolder.ClearAndRelease();

            if (hasEvents && RemotePlayer != null && RemotePlayer.RemoteAvatarDriver != null)
            {
                RemotePlayer.RemoteAvatarDriver.CalibrationComplete -= OnCalibration;
                hasEvents = false;
            }

            AudioReceiverModule?.OnDestroy();
            PassedSimulate = false;
        }

        public void ReceiveNetworkAudio(ServerAudioSegmentMessage msg)
        {
            int serverSilentUnits = msg.audioSegmentData.TotalPlayedInSilence; // 20ms units
            if (AudioReceiverModule == null)
            {
                BasisDebug.LogError("Missing Audio Receiver for remote player!", BasisDebug.LogTag.Remote);
                return;
            }

            if (serverSilentUnits > 0)
            {
                int localUnits = System.Threading.Interlocked.Exchange(ref AudioReceiverModule._silentUnits20ms, 0);
                int missing = serverSilentUnits - localUnits;
                if (missing > 0)
                {
                    for (int Index = 0; Index < missing; Index++)
                        AudioReceiverModule.OnDecodeSilence();
                }
            }

            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerAudioSegment, msg.audioSegmentData.LengthUsed);
            AudioReceiverModule.OnDecode(msg.audioSegmentData.buffer, msg.audioSegmentData.LengthUsed);
            Player.AudioReceived?.Invoke();
        }

        public async void ReceiveAvatarChangeRequest(ServerAvatarChangeMessage SACM)
        {
            RemotePlayer.CACM = SACM.clientAvatarChangeMessage;
            BasisLoadableBundle bundle = BasisBundleConversionNetwork.ConvertNetworkBytesToBasisLoadableBundle(SACM.clientAvatarChangeMessage.byteArray);
            await RemotePlayer.CreateAvatar(SACM.clientAvatarChangeMessage.loadMode, bundle);
        }

        public BasisNetworkReceiver(ushort PlayerID)
        {
            playerId = PlayerID;
            hasID = true;
        }

        // -------------------- PATCHED staging helpers --------------------

        // Seed Current with ONE oldest staged frame (do NOT drain staging)
        private void TrySeedFirstFromStaging()
        {
            if (BufferHolder.HasCurrentBuffer) return;

            if (_stagedRing.TryDequeueOldest(out var first))
            {
                BufferHolder.SetCurrent(ref first);
                BufferHolder.HasCurrentBuffer = true;
            }

            StagedCount = _stagedRing.Count;
        }

        // Seed Next with ONE next-oldest staged frame (do NOT drain staging)
        private void TrySetLastFromStaging()
        {
            if (!BufferHolder.HasCurrentBuffer || BufferHolder.HasNextBuffer) return;

            if (_stagedRing.TryDequeueOldest(out var next))
            {
                BufferHolder.SetNext(ref next);
                BufferHolder.HasNextBuffer = true;
            }

            StagedCount = _stagedRing.Count;
        }

        private void DropOldestFromStaging(int count)
        {
            int n = Mathf.Min(count, _stagedRing.Count);
            for (int i = 0; i < n; i++)
            {
                if (_stagedRing.TryDequeueOldest(out var buf))
                    BasisAvatarBufferPool.Release(buf);
                else
                    break;
            }

            StagedCount = _stagedRing.Count;
        }
    }
}
