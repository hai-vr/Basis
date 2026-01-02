using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Profiler;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
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
        public const int EyeAndMouthCountInBytes = EyesAndMouthCount * sizeof(float);

        /// <summary>
        /// If staging backlog exceeds this, older frames are dropped to reduce latency.
        /// </summary>
        public static int BufferCapacityBeforeCleanup = 12;

        [SerializeReference]
        public BasisAudioReceiver AudioReceiverModule = new BasisAudioReceiver();
        [SerializeField]
        public ConcurrentQueue<BasisAvatarBuffer> PayloadQueue = new ConcurrentQueue<BasisAvatarBuffer>();
        public BasisRemotePlayer RemotePlayer;

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

        // Playback rate control: catches up smoothly when backlog grows.
        private const int TargetJitterDepth = 3;          // desired staged depth cushion
        private const float CatchupGain = 0.12f;          // 0.05..0.25 tune
        private const float MinPlaybackRate = 0.85f;
        private const float MaxPlaybackRate = 1.35f;

        public bool HasCurrentBuffer = false;
        public bool HasNextBuffer = false;
        public bool SentLatest = false;
        public BasisAvatarBuffer Current { get; private set; }
        public BasisAvatarBuffer Next { get; private set; }

        private void AssertInitialized()
        {
            //  Assert.IsTrue(hasID, "BasisNetworkReceiver: hasID must be true (playerId not set?)");
            // Assert.IsTrue(playerId != 0 || hasID, "BasisNetworkReceiver: playerId looks invalid.");
            //  Assert.IsNotNull(Player, "BasisNetworkReceiver: Player is null (Initialize not called / wiring broken).");
            //  Assert.IsNotNull(_stagedRing, "BasisNetworkReceiver: _stagedRing is null (Initialize not called?).");
            //  Assert.IsNotNull(AudioReceiverModule, "BasisNetworkReceiver: AudioReceiverModule is null.");
        }

        private void AssertAvatarReady()
        {
            //  Assert.IsNotNull(Player.BasisAvatar, "BasisNetworkReceiver: BasisAvatar is null unexpectedly.");
            //  Assert.IsNotNull(Player.BasisAvatar.Animator, "BasisNetworkReceiver: Animator is null unexpectedly.");
            //  Assert.IsNotNull(Player.AvatarTransform, "BasisNetworkReceiver: AvatarTransform is null unexpectedly.");
        }

        private void AssertBuffersConsistent()
        {
            //  if (HasCurrentBuffer) Assert.IsNotNull(Current, "HasCurrentBuffer true but Current is null.");
            //   if (HasNextBuffer) Assert.IsNotNull(Next, "HasNextBuffer true but Next is null.");
            //   if (!HasCurrentBuffer) Assert.IsNull(Current, "HasCurrentBuffer false but Current is non-null.");
            //  if (!HasNextBuffer) Assert.IsNull(Next, "HasNextBuffer false but Next is non-null.");

            // Assert.IsTrue(interpolationTime >= 0f, $"interpolationTime went negative: {interpolationTime}");
            // It can exceed 1 (you consume windows in a loop), but it should never blow up.
            //  Assert.IsTrue(interpolationTime < 1000f, $"interpolationTime is absurd: {interpolationTime}");
        }

        /// <summary>
        /// Main-thread simulation step. Pulls packets, maintains interpolation window,
        /// computes interpolationTime, and feeds inputs to the network driver.
        /// </summary>
        public void Compute(float unscaledDeltaTime)
        {
            // Basic invariants
            // AssertInitialized();

            // expected briefly on join
            if (Player.BasisAvatar == null) return;

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

            // After early outs, these should be true.
            //   AssertAvatarReady();

            // Timing sanity
            //  Assert.IsTrue(float.IsFinite(unscaledDeltaTime), $"unscaledDeltaTime not finite: {unscaledDeltaTime}");
            //  Assert.IsTrue(unscaledDeltaTime >= 0f, $"unscaledDeltaTime negative: {unscaledDeltaTime}");

            // 1) Pull network packets to main-thread staging ring (bounded)
            while (PayloadQueue.TryDequeue(out var buffer))
            {
                //  Assert.IsNotNull(buffer, "PayloadQueue contained a null BasisAvatarBuffer.");
                _stagedRing.EnqueueOverwriteOldest(buffer, onOverwrite: BasisAvatarBufferPool.Release);
            }
            StagedCount = _stagedRing.Count;

            // Assert.IsTrue(StagedCount >= 0 && StagedCount <= MaxStage, $"StagedCount out of range: {StagedCount} (MaxStage {MaxStage})");

            // 2) Ensure we have a valid interpolation window (Current -> Next)
            if (!HasCurrentBuffer)
            {
                TrySeedFirstFromStaging();   // only takes ONE oldest
            }

            if (!HasNextBuffer)
            {
                TrySetLastFromStaging();     // only takes ONE next-oldest
            }

            //  AssertBuffersConsistent();

            HasBufferHolds = HasCurrentBuffer && HasNextBuffer;
            if (!HasBufferHolds)
            {
                // It's valid to be here if we haven't received enough frames yet.
                return;
            }

            // 2b) Advance window while consumed and we have staged frames
            while (interpolationTime >= 1f && _stagedRing.Count != 0)
            {
                if (HasCurrentBuffer)
                {
                    ReleaseCurrent();
                }

                // If we had holds, Next must be non-null here.
                //  Assert.IsTrue(HasNextBuffer && Next != null, "Advancing window but Next missing.");

                Current = Next;
                HasCurrentBuffer = true;

                HasNextBuffer = false;
                Next = null;

                interpolationTime = 0f;

                TrySetLastFromStaging();

                HasBufferHolds = HasCurrentBuffer && HasNextBuffer;
                if (!HasBufferHolds)
                {
                    break;
                }
            }

            StagedCount = _stagedRing.Count;

            while (_stagedRing.Count > BufferCapacityBeforeCleanup)
            {
                if (_stagedRing.TryDequeueOldest(out var buf))
                {
                    //   Assert.IsNotNull(buf, "Staging ring returned null buffer on dequeue.");
                    BasisAvatarBufferPool.Release(buf);
                }
                else
                {
                    break;
                }
            }
            StagedCount = _stagedRing.Count;

            HasBufferHolds = HasCurrentBuffer && HasNextBuffer;

            // 3) If we have a window, compute interpolation fraction and feed the driver
            if (HasBufferHolds)
            {
                //  Assert.IsNotNull(Current, "HasBufferHolds but Current is null.");
                //  Assert.IsNotNull(Next, "HasBufferHolds but Next is null.");

                var first = Current;
                var last = Next;

                double windowDuration =
                    last.SecondsInterval > 0 ? last.SecondsInterval :
                    first.SecondsInterval > 0 ? first.SecondsInterval :
                    (1.0 / 60.0);

                if (!double.IsFinite(windowDuration) || windowDuration <= 1e-6)
                {
                    windowDuration = 1e-3;
                }

                double step = Math.Max(unscaledDeltaTime, 0.0);

                int depth = _stagedRing.Count;
                //   Assert.IsTrue(depth >= 0 && depth <= MaxStage, $"Ring depth out of range: {depth}");

                float rate = 1f + CatchupGain * (depth - TargetJitterDepth);
                rate = Mathf.Clamp(rate, MinPlaybackRate, MaxPlaybackRate);
                //    Assert.IsTrue(rate >= MinPlaybackRate - 1e-3f && rate <= MaxPlaybackRate + 1e-3f, $"Playback rate out of clamp: {rate}");

                interpolationTime += (float)((step / windowDuration) * rate);
                //  Assert.IsTrue(dtSeconds > 0f && float.IsFinite(dtSeconds), $"Bad dtSeconds: {dtSeconds}");

                PassedSimulate = BasisRemoteNetworkDriver.SetFrameTiming(playerId, interpolationTime, unscaledDeltaTime);

                if (PassedSimulate && SentLatest)
                {

                    BasisRemoteNetworkDriver.SetFrameInputs(
                        playerId,
                        Player.BasisAvatar.HumanScale,
                        first.Position, last.Position,
                        first.Scale, last.Scale,
                        first.Rotation, last.Rotation
                    );

                    BasisRemoteNetworkDriver.SetMuscleWindow(playerId, first.Muscles, last.Muscles);
                    SentLatest = false;
                }
            }
        }

        /// <summary>
        /// Main-thread application step. Pulls posed outputs from the driver and applies
        /// body position/rotation/muscles to the avatar via PoseHandler.
        /// </summary>
        public void Apply()
        {
            //  AssertInitialized();

            if (PassedSimulate)
            {
                // These outputs should be stable when simulate passed.
                BasisRemoteNetworkDriver.GetOutputs_NoAlloc(playerId, out bool outscale, out ApplyingRotation, out float3 scaledBody);

                //  Assert.IsNotNull(EyesAndMouth, "EyesAndMouth array is null.");
                //  Assert.IsTrue(EyesAndMouth.Length >= EyesAndMouthCount, $"EyesAndMouth length too small: {EyesAndMouth.Length}");
                // Assert.IsTrue(EyesAndMouthOffset >= 0, $"EyesAndMouthOffset negative: {EyesAndMouthOffset}");

                BasisRemoteNetworkDriver.GetMuscleArray(
                    playerId,
                    ref HumanPose,
                    EyesAndMouth,
                    EyesAndMouthOffset,
                    EyeAndMouthCountInBytes
                );

                HumanPose.bodyPosition = scaledBody;
                HumanPose.bodyRotation = ApplyingRotation;

                // Assert.IsNotNull(PoseHandler, "PoseHandler is null when applying pose.");
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
                    //   Assert.IsNotNull(RemotePlayer, "RemotePlayer is null while HasOverridenDestination.");
                    //  Assert.IsNotNull(RemotePlayer.RemoteAvatarDriver, "RemoteAvatarDriver null while HasOverridenDestination.");
                    var References = RemotePlayer.RemoteAvatarDriver.References;
                    //   Assert.IsNotNull(References.Hips, "References.Hips is null while HasOverridenDestination.");
                    References.Hips.transform.SetPositionAndRotation(OverridenPosition, OverridenRotation);
                }

                PassedSimulate = false;
            }
            else
            {
                if (Player.BasisAvatar == null) return;
                if (Player.BasisAvatar.Animator == null) return;
                if (Player.AvatarTransform == null) return;

                if (PoseHandler != null)
                {
                    PoseHandler.SetHumanPose(ref HumanPose);
                }

                if (HasOverridenDestination)
                {
                    //   Assert.IsNotNull(RemotePlayer, "RemotePlayer is null while HasOverridenDestination.");
                    //  Assert.IsNotNull(RemotePlayer.RemoteAvatarDriver, "RemoteAvatarDriver null while HasOverridenDestination.");
                    var References = RemotePlayer.RemoteAvatarDriver.References;
                    //   Assert.IsNotNull(References.Hips, "References.Hips is null while HasOverridenDestination.");
                    References.Hips.transform.SetPositionAndRotation(OverridenPosition, OverridenRotation);
                }
            }
        }

        public void ApplyScale()
        {
            //  AssertInitialized();
            //   Assert.IsNotNull(Player.AvatarTransform, "AvatarTransform null in ApplyScale.");
            BasisRemoteNetworkDriver.GetScaleOutput(playerId, out ApplyingScale);
            Player.AvatarTransform.localScale = ApplyingScale;
        }

        public void EnQueueAvatarBuffer(BasisAvatarBuffer avatarBuffer)
        {
            //  Assert.IsNotNull(avatarBuffer, "EnQueueAvatarBuffer called with null avatarBuffer.");
            PayloadQueue.Enqueue(avatarBuffer);
        }

        public override void Initialize()
        {
            //  Assert.IsNotNull(Player, "Initialize called but Player is null.");
            RemotePlayer = (BasisRemotePlayer)Player;
            //  Assert.IsNotNull(RemotePlayer, "RemotePlayer cast failed in Initialize.");

            // Assert.IsNotNull(RemotePlayer.RemoteAvatarDriver, "RemotePlayer.RemoteAvatarDriver missing during Initialize.");

            //  Assert.IsNotNull(AudioReceiverModule, "AudioReceiverModule missing during Initialize.");
            AudioReceiverModule.Initalize(this);

            // Reset staging
            _stagedRing = new BasisRingBuffer<BasisAvatarBuffer>(MaxStage);
            //  Assert.IsNotNull(_stagedRing, "Failed to allocate _stagedRing.");

            StagedCount = 0;
            ClearAndRelease();
            interpolationTime = 0f;
            PassedSimulate = false;

            // Clear any packets that arrived before init (rare, but safe)
            while (PayloadQueue.TryDequeue(out var buf))
            {
                Assert.IsNotNull(buf, "PayloadQueue contained null buffer during Initialize flush.");
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
            // AssertInitialized();
            // Assert.IsNotNull(RemotePlayer, "OnCalibration: RemotePlayer null.");
            // Assert.IsNotNull(RemotePlayer.RemoteAvatarDriver, "OnCalibration: RemoteAvatarDriver null.");

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
            // _stagedRing can be null if Initialize never completed, so don't Assert here—guard.
            if (_stagedRing != null)
            {
                while (_stagedRing.TryDequeueOldest(out var buf))
                {
                    //  Assert.IsNotNull(buf, "Staging ring returned null buffer during DeInitialize.");
                    BasisAvatarBufferPool.Release(buf);
                }
                StagedCount = 0;
            }

            while (PayloadQueue.TryDequeue(out var buffer))
            {
                //  Assert.IsNotNull(buffer, "PayloadQueue contained null buffer during DeInitialize.");
                BasisAvatarBufferPool.Release(buffer);
            }

            ClearAndRelease();

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
            //  AssertInitialized();
            //   Assert.IsNotNull(AudioReceiverModule, "Missing Audio Receiver for remote player!");

            int serverSilentUnits = msg.audioSegmentData.TotalPlayedInSilence; // 20ms units

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

            //   Assert.IsTrue(msg.audioSegmentData.LengthUsed >= 0, $"Audio LengthUsed negative: {msg.audioSegmentData.LengthUsed}");
            //   Assert.IsNotNull(msg.audioSegmentData.buffer, "Audio buffer is null.");

            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerAudioSegment, msg.audioSegmentData.LengthUsed);
            AudioReceiverModule.OnDecode(msg.audioSegmentData.buffer, msg.audioSegmentData.LengthUsed);
            Player.AudioReceived?.Invoke();
        }

        public async void ReceiveAvatarChangeRequest(ServerAvatarChangeMessage SACM)
        {
            // Assert.IsNotNull(RemotePlayer, "ReceiveAvatarChangeRequest: RemotePlayer is null.");
            //  Assert.IsNotNull(SACM.clientAvatarChangeMessage.byteArray, "ReceiveAvatarChangeRequest: byteArray is null.");

            RemotePlayer.CACM = SACM.clientAvatarChangeMessage;
            BasisLoadableBundle bundle = BasisBundleConversionNetwork.ConvertNetworkBytesToBasisLoadableBundle(SACM.clientAvatarChangeMessage.byteArray);
            await RemotePlayer.CreateAvatar(SACM.clientAvatarChangeMessage.loadMode, bundle);
        }

        public BasisNetworkReceiver(ushort PlayerID)
        {
            playerId = PlayerID;
            hasID = true;
        }

        private void TrySeedFirstFromStaging()
        {
            if (HasCurrentBuffer) return;
            //  Assert.IsNotNull(_stagedRing, "TrySeedFirstFromStaging called before Initialize (no _stagedRing).");

            if (_stagedRing.TryDequeueOldest(out var first))
            {
                //  Assert.IsNotNull(first, "TrySeedFirstFromStaging dequeued a null buffer.");
                Current = first;
                SentLatest = true;
                HasCurrentBuffer = true;
            }

            StagedCount = _stagedRing.Count;
        }

        // Seed Next with ONE next-oldest staged frame (do NOT drain staging)
        private void TrySetLastFromStaging()
        {
            if (!HasCurrentBuffer || HasNextBuffer) return;
            //  Assert.IsNotNull(_stagedRing, "TrySetLastFromStaging called before Initialize (no _stagedRing).");

            if (_stagedRing.TryDequeueOldest(out var next))
            {
                //  Assert.IsNotNull(next, "TrySetLastFromStaging dequeued a null buffer.");
                Next = next;
                SentLatest = true;
                HasNextBuffer = true;
            }

            StagedCount = _stagedRing.Count;
        }

        public void ClearAndRelease()
        {
            ReleaseCurrent();
            if (HasNextBuffer)
            {
                // Assert.IsNotNull(Next, "ClearAndRelease: HasNextBuffer true but Next is null.");
                BasisAvatarBufferPool.Release(Next);
                Next = null;
                HasNextBuffer = false;
            }
        }

        public void ReleaseCurrent()
        {
            if (HasCurrentBuffer)
            {
                //  Assert.IsNotNull(Current, "ReleaseCurrent: HasCurrentBuffer true but Current is null.");
                BasisAvatarBufferPool.Release(Current);
                Current = null;
                HasCurrentBuffer = false;
            }
        }
    }
}
