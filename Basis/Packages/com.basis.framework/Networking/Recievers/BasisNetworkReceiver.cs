using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Profiler;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static SerializableBasis;

namespace Basis.Scripts.Networking.Receivers
{
    [DefaultExecutionOrder(15001)]
    [System.Serializable]
    public class BasisNetworkReceiver : BasisNetworkPlayer
    {
        // ---------- Constants ----------
        private const int EyesAndMouthOffset = 15;
        private const int EyesAndMouthCount = 6;
        public const int EyeAndMouthSize = EyesAndMouthOffset * sizeof(float);
        public const int EyeAndMouthcount = EyesAndMouthCount * sizeof(float);

        [Tooltip("If more than this many frames are queued, old frames will be dropped to catch up.")]
        public int BufferCapacityBeforeCleanup = 5;

        // ---------- Serialized / External ----------
        public BasisRemoteBoneControl MouthBone;

        [SerializeField] public BasisAudioReceiver AudioReceiverModule = new BasisAudioReceiver();
        [SerializeField] public ConcurrentQueue<BasisAvatarBuffer> PayloadQueue = new ConcurrentQueue<BasisAvatarBuffer>();

        public BasisRemotePlayer RemotePlayer;

        [SerializeField] public BasisRemoteAvatarBufferHolder BufferHolder = new BasisRemoteAvatarBufferHolder();

        [Serializable]
        public class BasisRemoteAvatarBufferHolder
        {
            public bool HasFirst = false;
            public bool HasLast = false;

            [SerializeField] public BasisAvatarBuffer First;
            [SerializeField] public BasisAvatarBuffer Last;

            public void ClearAndRelease()
            {
                if (HasFirst)
                {
                    BasisAvatarBufferPool.Release(ref First);
                    HasFirst = false;
                }
                if (HasLast)
                {
                    BasisAvatarBufferPool.Release(ref Last);
                    HasLast = false;
                }
            }
        }

        // ---------- State ----------
        public bool HasEvents = false;
        public bool HasAvatarQueue;

        public bool LogFirstError = false;

        public float[] EyesAndMouth = new float[] { 0, 0, 0, 0, 1, 0 };
        public float[] Muscles = new float[95];

        // Computed by driver
        public quaternion ApplyingRotation;
        public float3 ApplyingPosition;
        public float3 ApplyingScale;

        // Interpolation timing
        private float interpolationTime = 0f;

        // Main-thread staging for dequeued packets
        private readonly List<BasisAvatarBuffer> _staged = new List<BasisAvatarBuffer>(16);

        // Shared zero array for safety
        private static readonly float[] ZeroMuscles = new float[95];

        // ---------- Compute / Apply ----------
        // This is called from your network simulation (main thread).
        // It pulls data from the off-thread queue, builds an interpolation window,
        // computes the fraction using SecondsInterval, and pushes inputs to the driver.
        public void Compute(double timeNow)
        {
            // 1) Pull network packets to main-thread staging
            PumpQueueToStaging();

            // 2) Ensure we have a valid interpolation window (First -> Last)
            BuildOrAdvanceWindow();

            // 3) If we have a window, compute interpolation fraction and feed the compute phase
            if (BufferHolder.HasFirst && BufferHolder.HasLast)
            {
                ComputeInterpolationFraction();

                var first = BufferHolder.First;
                var last = BufferHolder.Last;

                // Ensure muscles are non-null
                var prevMuscles = first.Muscles;
                var targetMuscles = last.Muscles;
                if (prevMuscles == null || prevMuscles.Length < 95) prevMuscles = ZeroMuscles;
                if (targetMuscles == null || targetMuscles.Length < 95) targetMuscles = ZeroMuscles;

                BasisRemoteNetworkDriver.SetInputs(
                    playerId,
                    first.Position, last.Position,
                    first.Scale, last.Scale,
                    first.rotation, last.rotation,
                    interpolationTime,
                    prevMuscles, targetMuscles
                );
            }
        }

        public void Apply()
        {
            if (BufferHolder.HasFirst && BufferHolder.HasLast)
            {
                if (BasisRemoteNetworkDriver.GetOutputs(playerId, out ApplyingPosition, out ApplyingScale, out ApplyingRotation, ref Muscles))
                {
                    ApplyComputedData();
                }
            }
        }
        public void ApplyComputedData()
        {
            ApplyPoseData(Player.AvatarTransform, Player.BasisAvatar.AnimatorHumanScale, ApplyingScale, ApplyingPosition, ApplyingRotation, Muscles);

            PoseHandler.SetHumanPose(ref HumanPose);
            RemotePlayer.RemoteBoneDriver.SimulateAndApplyRemote(ApplyingScale);

            if (AudioReceiverModule.HasTransform)
            {
                var outgoing = RemotePlayer.RemoteBoneDriver.Mouth.OutGoingData;
                AudioReceiverModule.AudioSourceTransform.SetPositionAndRotation(outgoing.position, outgoing.rotation);
            }

            if (RemotePlayer.HasRemoteNamePlate)
            {
                RemotePlayer.RemoteNamePlate.Simulate();
            }
        }

        public void ApplyPoseData(Transform AnimatorsTransform, float3 Scaling, float3 Scale, float3 Position, Quaternion Rotation, float[] Muscles)
        {
            Scaling = SafeDivide(Scaling, Scale);
            HumanPose.bodyPosition = Vector3.Scale(Position, Scaling);
            HumanPose.bodyRotation = Rotation;
            Array.Copy(Muscles, HumanPose.muscles, 95);
            Buffer.BlockCopy(EyesAndMouth, 0, HumanPose.muscles, EyeAndMouthSize, EyeAndMouthcount);
            AnimatorsTransform.localScale = Scale;
        }

        public static float3 SafeDivide(float3 a, float3 b, float epsilon = 1e-5f)
        {
            return new float3(
                math.abs(b.x) > epsilon ? a.x / b.x : a.x,
                math.abs(b.y) > epsilon ? a.y / b.y : a.y, // fixed
                math.abs(b.z) > epsilon ? a.z / b.z : a.z);
        }

        /// <summary>
        /// Called from a background/network thread. Thread-safe.
        /// </summary>
        public void EnQueueAvatarBuffer(BasisAvatarBuffer avatarBuffer)
        {
            PayloadQueue.Enqueue(avatarBuffer);
        }

        // ---------- Lifecycle ----------
        public override void Initialize()
        {
            RemotePlayer = (BasisRemotePlayer)Player;
            AudioReceiverModule.Initalize(this);

            if (!HasEvents && RemotePlayer?.RemoteAvatarDriver != null)
            {
                RemotePlayer.RemoteAvatarDriver.CalibrationComplete += OnCalibration;
                HasEvents = true;
            }

            HasAvatarQueue = true;
            _staged.Clear();
            BufferHolder.ClearAndRelease();
            interpolationTime = 0f;
        }

        public void OnCalibration()
        {
            Player.BasisAvatar.AnimatorHumanScale = Vector3.one / Player.BasisAvatar.Animator.humanScale;
            AudioReceiverModule.AvatarChanged(this);
        }

        public override void DeInitialize()
        {
            BufferHolder.ClearAndRelease();

            for (int i = 0; i < _staged.Count; i++)
            {
                var b = _staged[i];
                BasisAvatarBufferPool.Release(ref b);
            }
            _staged.Clear();

            while (PayloadQueue.TryDequeue(out var buffer))
            {
                BasisAvatarBufferPool.Release(ref buffer);
            }

            if (RemotePlayer != null && HasEvents && RemotePlayer.RemoteAvatarDriver != null)
            {
                RemotePlayer.RemoteAvatarDriver.CalibrationComplete -= OnCalibration;
                HasEvents = false;
            }

            AudioReceiverModule?.OnDestroy();

            HasAvatarQueue = false;
        }
        public void ReceiveNetworkAudio(ServerAudioSegmentMessage audioSegment)
        {
            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerAudioSegment, audioSegment.audioSegmentData.LengthUsed);
            AudioReceiverModule.OnDecode(audioSegment.audioSegmentData.buffer, audioSegment.audioSegmentData.LengthUsed);
            Player.AudioReceived?.Invoke(true);
        }

        public void ReceiveSilentNetworkAudio(ServerAudioSegmentMessage audioSilentSegment)
        {
            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerAudioSegment, 1);
            AudioReceiverModule.OnDecodeSilence();
            Player.AudioReceived?.Invoke(false);
        }

        // ---------- Avatar switching ----------
        public async void ReceiveAvatarChangeRequest(ServerAvatarChangeMessage ServerAvatarChangeMessage)
        {
            RemotePlayer.CACM = ServerAvatarChangeMessage.clientAvatarChangeMessage;
            BasisLoadableBundle BasisLoadableBundle = BasisBundleConversionNetwork.ConvertNetworkBytesToBasisLoadableBundle(ServerAvatarChangeMessage.clientAvatarChangeMessage.byteArray);

            await RemotePlayer.CreateAvatar(ServerAvatarChangeMessage.clientAvatarChangeMessage.loadMode, BasisLoadableBundle);
        }

        // ---------- Ctor ----------
        public BasisNetworkReceiver(ushort PlayerID)
        {
            playerId = PlayerID;
            hasID = true;
        }
        /// <summary>
        /// Move packets from the concurrent queue to a main-thread staging list.
        /// </summary>
        private void PumpQueueToStaging()
        {
            while (PayloadQueue.TryDequeue(out var buffer))
            {
                _staged.Add(buffer);
            }

            const int MaxStage = 64;
            if (_staged.Count > MaxStage)
            {
                int drop = _staged.Count - MaxStage;
                DropOldestFromStaging(drop);
            }
        }

        /// <summary>
        /// Ensures we have a (First, Last) interpolation window and advances when consumed.
        /// </summary>
        private void BuildOrAdvanceWindow()
        {
            // Seed First if missing
            if (!BufferHolder.HasFirst)
            {
                TrySeedFirstFromStaging();
            }

            // Fill Last if missing
            if (!BufferHolder.HasLast)
            {
                TrySetLastFromStaging();
            }

            // If either still missing, bail; we'll try again next compute tick
            if (!BufferHolder.HasFirst || !BufferHolder.HasLast)
                return;

            // If we've consumed the current window, advance; repeat while we have more staged
            while (interpolationTime >= 1f && _staged.Count > 0)
            {
                // Release old First
                if (BufferHolder.HasFirst)
                {
                    BasisAvatarBufferPool.Release(ref BufferHolder.First);
                    BufferHolder.HasFirst = false;
                }

                // Promote Last -> First
                BufferHolder.First = BufferHolder.Last;
                BufferHolder.HasFirst = true;

                // Pull new Last
                BufferHolder.HasLast = false;

                interpolationTime = 0f;

                TrySetLastFromStaging();
            }

            // If staging backlog is large, drop old frames to reduce latency
            if (_staged.Count > BufferCapacityBeforeCleanup)
            {
                int drop = _staged.Count - BufferCapacityBeforeCleanup;
                DropOldestFromStaging(drop);
            }
        }

        private void TrySeedFirstFromStaging()
        {
            if (_staged.Count == 0) return;

            var first = _staged[0];
            _staged.RemoveAt(0);

            BufferHolder.First = first;
            BufferHolder.HasFirst = true;
        }

        private void TrySetLastFromStaging()
        {
            if (!BufferHolder.HasFirst) return;
            if (_staged.Count == 0) return;

            var last = _staged[0];
            _staged.RemoveAt(0);
            BufferHolder.Last = last;
            BufferHolder.HasLast = true;
        }

        private void ComputeInterpolationFraction()
        {
            var first = BufferHolder.First;
            var last = BufferHolder.Last;

            double windowDuration = last.SecondsInterval > 0 ? last.SecondsInterval :
                                    (first.SecondsInterval > 0 ? first.SecondsInterval : (1.0 / 60.0));

            if (windowDuration <= 1e-6) windowDuration = 1e-3;

            double step = Math.Max(Time.unscaledDeltaTime, 0.0);
            interpolationTime += (float)(step / windowDuration);
            if (interpolationTime > 1f) interpolationTime = 1f;
            if (interpolationTime < 0f) interpolationTime = 0f;
        }

        private void DropOldestFromStaging(int count)
        {
            count = Mathf.Min(count, _staged.Count);
            for (int i = 0; i < count; i++)
            {
                var b = _staged[i];
                BasisAvatarBufferPool.Release(ref b);
            }
            _staged.RemoveRange(0, count);
        }
    }
}
