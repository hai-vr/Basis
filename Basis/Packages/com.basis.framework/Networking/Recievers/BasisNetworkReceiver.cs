using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Profiler;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Generic;
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
        public BasisRemoteBoneControl MouthBone;
        [SerializeField]
        public BasisAudioReceiver AudioReceiverModule = new BasisAudioReceiver();
        [SerializeField]
        public Queue<BasisAvatarBuffer> PayloadQueue = new Queue<BasisAvatarBuffer>();
        public BasisRemotePlayer RemotePlayer;
        public bool HasEvents = false;

        private NativeArray<float3> OutputVectors;      // Merged positions and scales
        private NativeArray<float3> TargetVectors; // Merged target positions and scales
                                                   //  private NativeArray<float> musclesPreEuro;
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
        public const int BufferCapacityBeforeCleanup = 3;
        public float interpolationTime;
        public double TimeBeforeCompletion;
        public double TimeInThePast;
        public bool HasAvatarQueue;

        public BasisOneEuroFilterParallelJob oneEuroFilterJob;
        public const float MinCutoff = 0.001f;
        public const float Beta = 5f;
        public const float DerivativeCutoff = 1.0f;
        public JobHandle EuroFilterHandle;
        public bool LogFirstError = false;
        public float[] EyesAndMouth = new float[] {0,0,0,0,1,0 };
        public Vector3 SafeScale;
        public Vector3 SafePosition;
        /// <summary>
        /// Perform computations to interpolate and update avatar state.
        /// </summary>
        public void Compute(double TimeAsDouble)
        {
            if (EuroFilterHandle.IsCompleted == false)
            {
                EuroFilterHandle.Complete();//we always call complete so that way scheduling can occur.
            }
            if (HasAvatarQueue)
            {
                // Calculate interpolation time
                interpolationTime = Mathf.Clamp01((float)((TimeAsDouble - TimeInThePast) / TimeBeforeCompletion));
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
                    //not a error  BasisDebug.LogError("Last == null tried to dequeue", BasisDebug.LogTag.Networking);

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

                // Muscle interpolation job
                musclesJob.Time = interpolationTime;
                AvatarHandle = AvatarJob.Schedule();
                musclesHandle = musclesJob.Schedule(95, 64, AvatarHandle);
                oneEuroFilterJob.DeltaTime = interpolationTime;
                EuroFilterHandle = oneEuroFilterJob.Schedule(95, 64, musclesHandle);
            }
        }
        public void Apply(double TimeAsDouble)
        {
            if (PoseHandler == null)
            {
                return;
            }
            if (First == null)
            {
                return;
            }
            if (Last == null)
            {
                return;
            }
            if (HasAvatarQueue)
            {
                OutputRotation = math.slerp(First.rotation, Last.rotation, interpolationTime);
                try
                {
                    EuroFilterHandle.Complete();//we always call complete so that way scheduling can occur.
                    // Complete the jobs and apply the results
                    SafeScale = OutputVectors[1];
                    SafePosition = OutputVectors[0];
                    ApplyComputedData(true);
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }
            if (interpolationTime >= 1 && PayloadQueue.TryDequeue(out BasisAvatarBuffer result))
            {
                FloatPool.Return(First.Muscles);//first is no longer needed here.
                First = Last;
                Last = result;

                if (Last != null)
                {
                    TimeBeforeCompletion = Last.SecondsInterval;
                }
                TimeInThePast = TimeAsDouble;
            }
        }
        public void ApplyComputedData(bool ApplyMuscle)
        {
            Player.BasisAvatar.AnimatorHumanScale = Vector3.one / Player.BasisAvatar.Animator.humanScale;
            ApplyPoseData(Player.BasisAvatarTransform, Player.BasisAvatar.AnimatorHumanScale, SafeScale, SafePosition, OutputRotation, ApplyMuscle, EuroValuesOutput);
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
            if (LogFirstError == false)
            {
                // Log the full exception details, including stack trace
                BasisDebug.LogError($"Error in Apply: {ex.Message}\nStack Trace:\n{ex.StackTrace}");

                // If the exception has an inner exception, log it as well
                if (ex.InnerException != null)
                {
                    BasisDebug.LogError($"Inner Exception: {ex.InnerException.Message}\nStack Trace:\n{ex.InnerException.StackTrace}");
                }
                LogFirstError = true;
            }
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
                while (PayloadQueue.Count > BufferCapacityBeforeCleanup)
                {
                    PayloadQueue.TryDequeue(out BasisAvatarBuffer Buffer);
                    FloatPool.Return(Buffer.Muscles);
                }
            }
            else
            {
                First = avatarBuffer;
                Last = avatarBuffer;
                HasAvatarQueue = true;
            }
        }
        public void ApplyPoseData(Transform AnimatorsTransform, Vector3 Scaling, float3 Scale, float3 Position, Quaternion Rotation, bool HasMuscle, NativeArray<float> Muscles)
        {
            // Directly adjust scaling by applying the inverse of the AvatarHumanScale
            Scaling = Divide(Scaling, Scale);

            Vector3 ScaledPosition = Vector3.Scale(Position, Scaling);
            HumanPose.bodyPosition = ScaledPosition;
            HumanPose.bodyRotation = Rotation;

            if (HasMuscle)
            {
                Muscles.CopyTo(HumanPose.muscles);
                Array.Copy(EyesAndMouth, 0, HumanPose.muscles,15, 6);
            }

            AnimatorsTransform.localScale = Scale;
        }
        public static Vector3 Divide(Vector3 a, Vector3 b)
        {
            // Define a small epsilon to avoid division by zero, using a flexible value based on magnitude
            const float epsilon = 0.00001f;

            return new Vector3(
                Mathf.Abs(b.x) > epsilon ? a.x / b.x : a.x,  // Avoid scaling if b is too small
                Mathf.Abs(b.y) > epsilon ? a.y / b.y : a.y,  // Same for y-axis
                Mathf.Abs(b.z) > epsilon ? a.z / b.z : a.z   // Same for z-axis
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
            HumanPose.muscles = new float[95];
            OutputVectors = new NativeArray<float3>(2, Allocator.Persistent); // Index 0 = position, Index 1 = scale
            TargetVectors = new NativeArray<float3>(2, Allocator.Persistent); // Index 0 = target position, Index 1 = target scale
                                                                              //  musclesPreEuro = new NativeArray<float>(LocalAvatarSyncMessage.StoredBones, Allocator.Persistent);
            targetMuscles = new NativeArray<float>(95, Allocator.Persistent);
            EuroValuesOutput = new NativeArray<float>(95, Allocator.Persistent);

            positionFilters = new NativeArray<float2>(95, Allocator.Persistent);
            derivativeFilters = new NativeArray<float2>(95, Allocator.Persistent);

            musclesJob = new UpdateAvatarMusclesJob();
            AvatarJob = new UpdateAvatarJob();
            musclesJob.Outputmuscles = EuroValuesOutput;
            musclesJob.targetMuscles = targetMuscles;
            AvatarJob.OutputVector = OutputVectors;
            AvatarJob.TargetVector = TargetVectors;

            ForceUpdateFilters();

            RemotePlayer = (BasisRemotePlayer)Player;
            AudioReceiverModule.Initalize(this);
            if (HasEvents == false)
            {
                RemotePlayer.RemoteAvatarDriver.CalibrationComplete += OnCalibration;
                HasEvents = true;
            }
        }
        public void ForceUpdateFilters()
        {
            for (int Index = 0; Index < 95; Index++)
            {
                positionFilters[Index] = new float2(0, 0);
                derivativeFilters[Index] = new float2(0, 0);
            }

            oneEuroFilterJob = new BasisOneEuroFilterParallelJob
            {
                //  InputValues = musclesPreEuro,
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
            AudioReceiverModule.AvatarChanged(this);
        }
        public override void DeInitialize()
        {
            EuroFilterHandle.Complete();
            AvatarHandle.Complete();
            musclesHandle.Complete();
            // Dispose vector data if initialized
            if (OutputVectors != null && OutputVectors.IsCreated) OutputVectors.Dispose();
            if (TargetVectors != null && TargetVectors.IsCreated) TargetVectors.Dispose();
            //   if (musclesPreEuro != null && musclesPreEuro.IsCreated) musclesPreEuro.Dispose();
            if (targetMuscles != null && targetMuscles.IsCreated) targetMuscles.Dispose();
            if (EuroValuesOutput != null && EuroValuesOutput.IsCreated) EuroValuesOutput.Dispose();
            if (positionFilters != null && positionFilters.IsCreated) positionFilters.Dispose();
            if (derivativeFilters != null && derivativeFilters.IsCreated) derivativeFilters.Dispose();

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
            SetPlayerID = PlayerID;
            hasID = true;
        }
        [SerializeField]
        private ushort SetPlayerID;
    }
}
