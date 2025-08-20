using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Profiler;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Jobs;
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
        public const int EyeAndMouthSize = EyesAndMouthOffset* sizeof(float);
        public const int EyeAndMouthcount = EyesAndMouthCount * sizeof(float);

        // ---- Public Fields ----
        public BasisRemoteBoneControl MouthBone;
        [SerializeField] public BasisAudioReceiver AudioReceiverModule = new BasisAudioReceiver();
        [SerializeField] public ConcurrentQueue<BasisAvatarBuffer> PayloadQueue = new ConcurrentQueue<BasisAvatarBuffer>();
        public BasisRemotePlayer RemotePlayer;
        public bool HasEvents = false;

        // ---- Internal Data ----
        private NativeArray<float3> OutputVectors;
        private NativeArray<float3> TargetVectors;
        private NativeArray<float> targetMuscles;
        private NativeArray<float> EuroValuesOutput;
        private NativeArray<float2> positionFilters;
        private NativeArray<float2> derivativeFilters;

        public JobHandle musclesHandle;
        public JobHandle AvatarHandle;
        public UpdateAvatarMusclesJob musclesJob = new UpdateAvatarMusclesJob();
        public UpdateAvatarJob AvatarJob = new UpdateAvatarJob();
        public quaternion OutputRotation;
        public BasisAvatarBuffer First;
        public BasisAvatarBuffer Last;
        public float interpolationTime;
        public double TimeBeforeCompletion;
        public double TimeInThePast;
        public bool HasAvatarQueue;

        public BasisOneEuroFilterParallelJob oneEuroFilterJob;
        public JobHandle EuroFilterHandle;
        public bool LogFirstError = false;
        /// <summary>
        /// first 4 are eyes
        /// 5 is mouth closed when 1
        /// 6 is mouth left right
        /// </summary>
        public float[] EyesAndMouth = new float[] { 0, 0, 0, 0, 1, 0 };
        public Vector3 SafeScale;
        public Vector3 SafePosition;

        /// <summary>
        /// Perform computations to interpolate and update avatar state.
        /// </summary>
        public void Compute(double TimeAsDouble)
        {
            if (!EuroFilterHandle.IsCompleted)
                EuroFilterHandle.Complete();

            if (!HasAvatarQueue) return;

            InterpolateBuffers(TimeAsDouble);
            ScheduleJobs();
        }

        public void Apply(double TimeAsDouble)
        {
            if (PoseHandler == null || First == null || Last == null || !HasAvatarQueue) return;

            OutputRotation = math.slerp(First.rotation, Last.rotation, interpolationTime);

            try
            {
                EuroFilterHandle.Complete();
                SafeScale = OutputVectors[1];
                SafePosition = OutputVectors[0];
                ApplyComputedData();
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }

            if (interpolationTime >= 1 && PayloadQueue.TryDequeue(out BasisAvatarBuffer result))
            {
                FloatPool.Return(First.Muscles);
                First = Last;
                Last = result;

                if (Last != null)
                    TimeBeforeCompletion = Last.SecondsInterval;

                TimeInThePast = TimeAsDouble;
            }
        }

        private void InterpolateBuffers(double TimeAsDouble)
        {
            if (TimeBeforeCompletion <= double.Epsilon)
            {
                interpolationTime = 1f;
            }
            else
            {
                double rawT = (TimeAsDouble - TimeInThePast) / TimeBeforeCompletion;
                interpolationTime = Mathf.SmoothStep(0f, 1f, (float)rawT);
            }

            if (float.IsNaN(interpolationTime))
            {
                BasisDebug.LogError("IsNaN on Interpolation Time");
                interpolationTime = 0f;
            }

            if (First == null)
            {
                if (Last != null)
                {
                    First = Last;
                    PayloadQueue.TryDequeue(out Last);
                    BasisDebug.LogError("Last != null filled in gap", BasisDebug.LogTag.Networking);
                }
                else
                {
                    PayloadQueue.TryDequeue(out First);
                    BasisDebug.LogError("Last and first are null replacing First!", BasisDebug.LogTag.Networking);
                }
            }

            if (Last == null)
            {
                PayloadQueue.TryDequeue(out Last);
            }

            if (First != null)
            {
                OutputVectors[0] = First.Position;
                OutputVectors[1] = First.Scale;
                EuroValuesOutput.CopyFrom(First.Muscles);
            }

            if (Last != null)
            {
                TargetVectors[0] = Last.Position;
                TargetVectors[1] = Last.Scale;
                targetMuscles.CopyFrom(Last.Muscles);
            }

            AvatarJob.Time = interpolationTime;
            musclesJob.Time = interpolationTime;
        }

        private void ScheduleJobs()
        {
            AvatarHandle = AvatarJob.Schedule();
            musclesHandle = musclesJob.Schedule(MuscleCount, 64, AvatarHandle);
            oneEuroFilterJob.DeltaTime = interpolationTime;
            EuroFilterHandle = oneEuroFilterJob.Schedule(MuscleCount, 64, musclesHandle);
        }

        public void ApplyComputedData()
        {
            ApplyPoseData(Player.AvatarTransform, Player.BasisAvatar.AnimatorHumanScale, SafeScale, SafePosition, OutputRotation, EuroValuesOutput);

            PoseHandler.SetHumanPose(ref HumanPose);
            RemotePlayer.RemoteBoneDriver.SimulateAndApplyRemote(SafeScale);

            if (AudioReceiverModule.HasTransform)
            {
                AudioReceiverModule.MoveAudio(RemotePlayer.RemoteBoneDriver.Mouth.OutGoingData);
            }
            if (RemotePlayer.HasRemoteNamePlate)
            {
                RemotePlayer.RemoteNamePlate.Simulate();
            }
        }

        public void HandleException(Exception ex)
        {
            if (LogFirstError) return;

            BasisDebug.LogError($"Error in Apply: {ex.Message}\nStack Trace:\n{ex.StackTrace}");
            if (ex.InnerException != null)
                BasisDebug.LogError($"Inner Exception: {ex.InnerException.Message}\nStack Trace:\n{ex.InnerException.StackTrace}");

            LogFirstError = true;
        }

        public void EnQueueAvatarBuffer(ref BasisAvatarBuffer avatarBuffer)
        {
            if (avatarBuffer == null)
            {
                BasisDebug.LogError("Missing Avatar Buffer!");
                return;
            }

            if (HasAvatarQueue)
            {
                PayloadQueue.Enqueue(avatarBuffer);
                while (PayloadQueue.Count > BufferCapacityBeforeCleanup && PayloadQueue.TryDequeue(out BasisAvatarBuffer buffer))
                {
                    FloatPool.Return(buffer.Muscles);
                }
            }
            else
            {
                First = avatarBuffer;
                Last = avatarBuffer;
                HasAvatarQueue = true;
            }
        }

        public void ApplyPoseData(Transform AnimatorsTransform, Vector3 Scaling, float3 Scale, float3 Position, Quaternion Rotation, NativeArray<float> Muscles)
        {
            Scaling = SafeDivide(Scaling, Scale);
            Vector3 ScaledPosition = Vector3.Scale(Position, Scaling);
            HumanPose.bodyPosition = ScaledPosition;
            HumanPose.bodyRotation = Rotation;
            Muscles.CopyTo(HumanPose.muscles);
            Buffer.BlockCopy(EyesAndMouth, 0, HumanPose.muscles, EyeAndMouthSize, EyeAndMouthcount);
            AnimatorsTransform.localScale = Scale;
        }

        public static float3 SafeDivide(float3 a, float3 b, float epsilon = 1e-5f)
        {
            return new float3(
                math.abs(b.x) > epsilon ? a.x / b.x : a.x,
                math.abs(b.y) > epsilon ? a.y / b.y : a.y,
                math.abs(b.z) > epsilon ? a.z / b.z : a.z
            );
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

        public override void Initialize()
        {
            if (!OutputVectors.IsCreated)
                OutputVectors = new NativeArray<float3>(2, Allocator.Persistent);
            if (!TargetVectors.IsCreated)
                TargetVectors = new NativeArray<float3>(2, Allocator.Persistent);
            if (!targetMuscles.IsCreated)
                targetMuscles = new NativeArray<float>(MuscleCount, Allocator.Persistent);
            if (!EuroValuesOutput.IsCreated)
                EuroValuesOutput = new NativeArray<float>(MuscleCount, Allocator.Persistent);
            if (!positionFilters.IsCreated)
                positionFilters = new NativeArray<float2>(MuscleCount, Allocator.Persistent);
            if (!derivativeFilters.IsCreated)
                derivativeFilters = new NativeArray<float2>(MuscleCount, Allocator.Persistent);

            musclesJob = new UpdateAvatarMusclesJob { Outputmuscles = EuroValuesOutput, targetMuscles = targetMuscles };
            AvatarJob = new UpdateAvatarJob { OutputVector = OutputVectors, TargetVector = TargetVectors };

            ForceUpdateFilters();

            RemotePlayer = (BasisRemotePlayer)Player;
            AudioReceiverModule.Initalize(this);
            if (!HasEvents)
            {
                RemotePlayer.RemoteAvatarDriver.CalibrationComplete += OnCalibration;
                HasEvents = true;
            }
        }

        public void ForceUpdateFilters()
        {
            for (int Index = 0; Index < MuscleCount; Index++)
            {
                positionFilters[Index] = new float2(0, 0);
                derivativeFilters[Index] = new float2(0, 0);
            }

            oneEuroFilterJob = new BasisOneEuroFilterParallelJob
            {
                Values = EuroValuesOutput,
                DeltaTime = interpolationTime,
                MinCutoff = MinCutoff,
                Beta = Beta,
                DerivativeCutoff = DerivativeCutoff,
                PositionFilters = positionFilters,
                DerivativeFilters = derivativeFilters,
            };
        }

        public void OnCalibration()
        {
            Player.BasisAvatar.AnimatorHumanScale = Vector3.one / Player.BasisAvatar.Animator.humanScale;
            AudioReceiverModule.AvatarChanged(this);
        }

        public override void DeInitialize()
        {
            EuroFilterHandle.Complete();
            AvatarHandle.Complete();
            musclesHandle.Complete();

            if (OutputVectors.IsCreated) OutputVectors.Dispose();
            if (TargetVectors.IsCreated) TargetVectors.Dispose();
            if (targetMuscles.IsCreated) targetMuscles.Dispose();
            if (EuroValuesOutput.IsCreated) EuroValuesOutput.Dispose();
            if (positionFilters.IsCreated) positionFilters.Dispose();
            if (derivativeFilters.IsCreated) derivativeFilters.Dispose();

            if (RemotePlayer != null && HasEvents && RemotePlayer.RemoteAvatarDriver != null)
            {
                RemotePlayer.RemoteAvatarDriver.CalibrationComplete -= OnCalibration;
                HasEvents = false;
            }

            AudioReceiverModule?.OnDestroy();
        }

        public BasisNetworkReceiver(ushort PlayerID)
        {
            PlayerIDMessage.playerID = PlayerID;
            hasID = true;
        }
    }
}
