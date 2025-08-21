using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Profiler;
using Basis.Scripts.TransformBinders.BoneControl;
using Basis.Scripts.UI.NamePlate;
using System;
using System.Collections.Concurrent;
using Unity.Mathematics;
using UnityEngine;
using static Basis.Scripts.Networking.NetworkedAvatar.BasisNetworkAvatarDecompressor;
using static SerializableBasis;

namespace Basis.Scripts.Networking.Receivers
{
    [DefaultExecutionOrder(15001)]
    [System.Serializable]
    public class BasisNetworkReceiver : BasisNetworkPlayer
    {
        // ---- Constants ----
        private const int EyesAndMouthOffset = 15;
        private const int EyesAndMouthCount = 6;
        public const int BufferCapacityBeforeCleanup = 3;
        public const float MinCutoff = 0.001f;
        public const float Beta = 5f;
        public const float DerivativeCutoff = 1.0f;
        public const int EyeAndMouthSize = EyesAndMouthOffset * sizeof(float);
        public const int EyeAndMouthcount = EyesAndMouthCount * sizeof(float);

        // ---- Public Fields ----
        public BasisRemoteBoneControl MouthBone;
        [SerializeField] public BasisAudioReceiver AudioReceiverModule = new BasisAudioReceiver();
        [SerializeField] public ConcurrentQueue<BasisAvatarBuffer> PayloadQueue = new ConcurrentQueue<BasisAvatarBuffer>();
        public BasisRemotePlayer RemotePlayer;
        public bool HasEvents = false;
        public quaternion OutputRotation;

        [SerializeField] public BasisAvatarBuffer First;
        [SerializeField] public BasisAvatarBuffer Last;

        public float interpolationTime;
        public double TimeBeforeCompletion;
        public double TimeInThePast;
        public bool HasAvatarQueue;
        public bool LogFirstError = false;

        /// <summary>
        /// first 4 are eyes
        /// 5 is mouth closed when 1
        /// 6 is mouth left right
        /// </summary>
        public float[] EyesAndMouth = new float[] { 0, 0, 0, 0, 1, 0 };
        public Vector3 SafeScale;
        public Vector3 SafePosition;
        public float[] Muscles = new float[95];

        public void Compute(double TimeAsDouble)
        {
            if (!HasAvatarQueue) return;

            InterpolateBuffers(TimeAsDouble);

            if (!First.Equals(default) && !Last.Equals(default))
            {
                BasisRemoteNetworkDriver.SetPlayerInputs(
                    this,
                    First.Position, Last.Position,
                    First.Scale, Last.Scale,
                    First.rotation, Last.rotation,
                    interpolationTime,
                    First.Muscles, Last.Muscles
                );
            }
        }

        public void Apply(double TimeAsDouble)
        {
            if (!HasAvatarQueue || First.Equals(default) || Last.Equals(default)) return;

            if (BasisRemoteNetworkDriver.GetPlayerOutputs(this, out float3 pos, out float3 scale, out quaternion rot, ref Muscles))
            {
                SafePosition = pos;
                SafeScale = scale;
                OutputRotation = rot;

                ApplyComputedData();
            }

            // Move to next buffer if interpolation finished
            if (interpolationTime >= 1f && PayloadQueue.TryDequeue(out BasisAvatarBuffer next))
            {
                BasisAvatarBufferPool.Release(ref First); // release old buffer
                First = Last;
                Last = next;

                TimeBeforeCompletion = Last.SecondsInterval;
                TimeInThePast = TimeAsDouble;
            }
        }

        public void ApplyComputedData()
        {
            ApplyPoseData(Player.AvatarTransform, Player.BasisAvatar.AnimatorHumanScale, SafeScale, SafePosition, OutputRotation, Muscles);

            PoseHandler.SetHumanPose(ref HumanPose);
            RemotePlayer.RemoteBoneDriver.SimulateAndApplyRemote(SafeScale);

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
        public void ApplyPoseData(Transform AnimatorsTransform, Vector3 Scaling, float3 Scale, float3 Position, Quaternion Rotation, float[] Muscles)
        {
            Scaling = SafeDivide(Scaling, Scale); Vector3 ScaledPosition = Vector3.Scale(Position, Scaling);
            HumanPose.bodyPosition = ScaledPosition; HumanPose.bodyRotation = Rotation; Array.Copy(Muscles,
                HumanPose.muscles, 95)
                ; Buffer.BlockCopy(EyesAndMouth, 0, HumanPose.muscles, EyeAndMouthSize, EyeAndMouthcount);
            AnimatorsTransform.localScale = Scale;
        }
        public static float3 SafeDivide(float3 a, float3 b, float epsilon = 1e-5f)
        {
            return new float3(
                math.abs(b.x) > epsilon ? a.x / b.x :a.x,
                math.abs(b.y) > epsilon ? a.y / b.y : a.y,
                math.abs(b.z) > epsilon ? a.z / b.z : a.z);
        }
        private void InterpolateBuffers(double TimeAsDouble)
        {
            if (First.Equals(default) || Last.Equals(default))
            {
                interpolationTime = 0f;
                return;
            }

            if (TimeBeforeCompletion <= double.Epsilon)
            {
                interpolationTime = 1f;
                return;
            }

            double rawT = (TimeAsDouble - TimeInThePast) / TimeBeforeCompletion;
            interpolationTime = Mathf.SmoothStep(0f, 1f, (float)rawT);

            if (float.IsNaN(interpolationTime))
            {
                BasisDebug.LogError("IsNaN on Interpolation Time");
                interpolationTime = 0f;
            }
        }

        public void EnQueueAvatarBuffer(ref BasisAvatarBuffer avatarBuffer)
        {
            if (avatarBuffer.Equals(default))
            {
                BasisDebug.LogError("Missing Avatar Buffer!");
                return;
            }

            PayloadQueue.Enqueue(avatarBuffer);

            // Limit queue size to prevent memory bloat
            while (PayloadQueue.Count > BufferCapacityBeforeCleanup && PayloadQueue.TryDequeue(out BasisAvatarBuffer buffer))
            {
                BasisAvatarBufferPool.Release(ref buffer);
            }

            // Initialize First/Last if empty
            if (!HasAvatarQueue)
            {
                if (PayloadQueue.TryDequeue(out BasisAvatarBuffer first)) First = first;
                if (PayloadQueue.TryDequeue(out BasisAvatarBuffer last)) Last = last;
                HasAvatarQueue = true;
                TimeBeforeCompletion = Last.SecondsInterval;
                TimeInThePast = Time.timeAsDouble; // you need a reference time here
            }
        }
        public override void Initialize() { RemotePlayer = (BasisRemotePlayer)Player; AudioReceiverModule.Initalize(this); if (!HasEvents) { RemotePlayer.RemoteAvatarDriver.CalibrationComplete += OnCalibration; HasEvents = true; } BasisRemoteNetworkDriver.AddPlayer(this); }
        public void OnCalibration() { Player.BasisAvatar.AnimatorHumanScale = Vector3.one / Player.BasisAvatar.Animator.humanScale; AudioReceiverModule.AvatarChanged(this); }
        public override void DeInitialize()
        {
            BasisRemoteNetworkDriver.RemovePlayer(this);

            if (!First.Equals(default)) BasisAvatarBufferPool.Release(ref First);
            if (!Last.Equals(default)) BasisAvatarBufferPool.Release(ref Last);

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
        public async void ReceiveAvatarChangeRequest(ServerAvatarChangeMessage ServerAvatarChangeMessage)
        {
            RemotePlayer.CACM = ServerAvatarChangeMessage.clientAvatarChangeMessage;
            BasisLoadableBundle BasisLoadableBundle = BasisBundleConversionNetwork.ConvertNetworkBytesToBasisLoadableBundle(ServerAvatarChangeMessage.clientAvatarChangeMessage.byteArray);

            await RemotePlayer.CreateAvatar(ServerAvatarChangeMessage.clientAvatarChangeMessage.loadMode, BasisLoadableBundle);
        }
        public BasisNetworkReceiver(ushort PlayerID)
        {
            PlayerIDMessage.playerID = PlayerID;
            hasID = true;
        }
    }
}
