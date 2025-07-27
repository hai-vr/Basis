using Basis.Network.Core;
using Basis.Scripts.Networking.Compression;
using Basis.Scripts.Networking.Transmitters;
using Basis.Scripts.Profiler;
using LiteNetLib;
using System;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static SerializableBasis;

namespace Basis.Scripts.Networking.NetworkedAvatar
{
    public static class BasisNetworkAvatarCompressor
    {
        private const ushort UShortMin = ushort.MinValue;
        private const ushort UShortMax = ushort.MaxValue;
        private const ushort UShortRangeDifference = UShortMax - UShortMin;

        public static void Compress(BasisNetworkTransmitter transmitter, Animator animator)
        {
            EnsureTransmitterIsInitialized(transmitter, animator);

            // Get current pose from Animator
            transmitter.PoseHandler.GetHumanPose(ref transmitter.HumanPose);

            CompressAvatarData(ref transmitter.SequenceNumber, ref transmitter.FloatArray,ref transmitter.UshortArray,ref transmitter.LASM,transmitter.HumanPose,animator);

            transmitter.LASM.AdditionalAvatarDatas = transmitter.SendingOutAvatarData.Count == 0
                ? null
                : transmitter.SendingOutAvatarData.Values.ToArray();

            transmitter.AvatarSendWriter.Put(BasisNetworkCommons.PlayerAvatarChannel);
            transmitter.LASM.Serialize(transmitter.AvatarSendWriter);
            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.LocalAvatarSync, transmitter.AvatarSendWriter.Length);

            BasisNetworkManagement.LocalPlayerPeer.Send(transmitter.AvatarSendWriter,BasisNetworkCommons.FallChannel,DeliveryMethod.Unreliable);

            transmitter.AvatarSendWriter.Reset();
            transmitter.ClearAdditional();
        }

        public static void InitalAvatarData(Animator animator, out LocalAvatarSyncMessage message)
        {
            var poseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            var humanPose = new HumanPose();
            poseHandler.GetHumanPose(ref humanPose);

            float[] floatArray = new float[LocalAvatarSyncMessage.StoredBones];
            ushort[] ushortArray = new ushort[LocalAvatarSyncMessage.StoredBones];

            message = new LocalAvatarSyncMessage(new byte[LocalAvatarSyncMessage.AvatarSyncSize]);

            CompressAvatarData(ref message.SequenceNumber, ref floatArray, ref ushortArray, ref message,humanPose, animator);
        }

        [BurstCompile]
        public static void CompressAvatarData(ref byte SequenceNumber, ref float[] floatArray, ref ushort[] networkSend, ref LocalAvatarSyncMessage message, HumanPose pose, Animator animator)
        {
            int offset = 0;

            // Copy muscles to float array
            Array.Copy(pose.muscles, 0, floatArray, 0, BasisAvatarMuscleRange.FirstBuffer);
            Array.Copy(pose.muscles, BasisAvatarMuscleRange.SecondBuffer, floatArray, BasisAvatarMuscleRange.FirstBuffer, BasisAvatarMuscleRange.SizeAfterGap);

            SequenceNumber = (byte)((SequenceNumber + 1) % 256);
            message.SequenceNumber = SequenceNumber;
            // Track and log byte size written by each compress operation
            //  int prevOffset;
            // Compress Position
            //   prevOffset = offset;
            BasisUnityBitPackerExtensionsUnsafe.WritePosition(animator.bodyPosition, ref message.array, ref offset);
            //   BasisDebug.Log($"CompressPosition: wrote {offset - prevOffset} bytes (offset now {offset})", BasisDebug.LogTag.Networking);

            // Compress Rotation
            //  prevOffset = offset;
            BasisUnityBitPackerExtensionsUnsafe.WriteQuaternionToBytes(animator.bodyRotation, ref message.array, ref offset, BasisNetworkPlayer.RotationCompression);
            // BasisDebug.Log($"WriteQuaternionToBytes: wrote {offset - prevOffset} bytes (offset now {offset})", BasisDebug.LogTag.Networking);

            // Compress Muscles
            //   prevOffset = offset;
            CompressAvatarMuscles(ref networkSend, ref floatArray, ref message, ref offset);
            //  BasisDebug.Log($"CompressAvatarMuscles: wrote {offset - prevOffset} bytes (offset now {offset})", BasisDebug.LogTag.Networking);

            // Compress Scale
            // prevOffset = offset;
            CompressScale(animator.transform.localScale.y, ref message, ref offset);
            //  BasisDebug.Log($"CompressScale: wrote {offset - prevOffset} bytes (offset now {offset})", BasisDebug.LogTag.Networking);
        }
        public static void CompressAvatarMuscles(ref ushort[] networkOutData,ref float[] floatArray,ref LocalAvatarSyncMessage message,ref int offset)
        {
            using var floatArrayNative = new NativeArray<float>(floatArray, Allocator.TempJob);
            using var minMuscleNative = new NativeArray<float>(BasisAvatarMuscleRange.MinMuscle, Allocator.TempJob);
            using var maxMuscleNative = new NativeArray<float>(BasisAvatarMuscleRange.MaxMuscle, Allocator.TempJob);
            using var rangeMuscleNative = new NativeArray<float>(BasisAvatarMuscleRange.RangeMuscle, Allocator.TempJob);
            using var networkSendNative = new NativeArray<ushort>(LocalAvatarSyncMessage.StoredBones, Allocator.TempJob);

            var muscleJob = new CompressMusclesJob
            {
                ValueArray = floatArrayNative,
                MinMuscle = minMuscleNative,
                MaxMuscle = maxMuscleNative,
                valueDiffence = rangeMuscleNative,
                NetworkSend = networkSendNative
            };

            muscleJob.Schedule(LocalAvatarSyncMessage.StoredBones, 64).Complete();

            networkSendNative.CopyTo(networkOutData);
            BasisUnityBitPackerExtensionsUnsafe.WriteUShortsToBytes(networkOutData, ref message.array, ref offset);
        }

        public static void CompressScale(float scale, ref LocalAvatarSyncMessage message, ref int offset)
        {
            const float Min = 0.005f;
            const float Max = 150f;
            const float range = Max - Min;

            float clamped = math.clamp(scale, Min, Max);
            float normalized = (clamped - Min) / range;

            ushort compressed = (ushort)(normalized * UShortRangeDifference);
            BasisUnityBitPackerExtensionsUnsafe.WriteUShort(compressed, ref message.array, ref offset);
        }

        [BurstCompile]
        public struct CompressMusclesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> ValueArray;
            [ReadOnly] public NativeArray<float> MinMuscle;
            [ReadOnly] public NativeArray<float> MaxMuscle;
            [ReadOnly] public NativeArray<float> valueDiffence;
            [WriteOnly] public NativeArray<ushort> NetworkSend;

            public void Execute(int index)
            {
                float clamped = math.clamp(ValueArray[index], MinMuscle[index], MaxMuscle[index]);
                float normalized = (clamped - MinMuscle[index]) / valueDiffence[index];
                NetworkSend[index] = (ushort)(normalized * UShortRangeDifference);
            }
        }

        private static void EnsureTransmitterIsInitialized(BasisNetworkTransmitter transmitter, Animator animator)
        {
            if (transmitter.UshortArray == null)
                transmitter.UshortArray = new ushort[LocalAvatarSyncMessage.StoredBones];

            if (transmitter.FloatArray == null)
                transmitter.FloatArray = new float[LocalAvatarSyncMessage.StoredBones];

            if (transmitter.PoseHandler == null)
                transmitter.PoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
        }
    }
}
