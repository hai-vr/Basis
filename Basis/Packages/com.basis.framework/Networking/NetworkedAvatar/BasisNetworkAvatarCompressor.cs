using Basis.Network.Core;
using Basis.Scripts.Networking.Compression;
using Basis.Scripts.Networking.Transmitters;
using Basis.Scripts.Profiler;
using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using static Basis.Scripts.Networking.Transmitters.BasisNetworkTransmitter;
using static SerializableBasis;

namespace Basis.Scripts.Networking.NetworkedAvatar
{
    public static class BasisNetworkAvatarCompressor
    {
        private const ushort UShortMin = ushort.MinValue;
        private const ushort UShortMax = ushort.MaxValue;
        private const ushort UShortRangeDifference = UShortMax - UShortMin;
        private const float ByteRangeDifference = byte.MaxValue;

        public static void Compress(BasisNetworkTransmitter transmitter, Animator animator)
        {
            EnsureTransmitterIsInitialized(transmitter, animator);

            // Get current pose from Animator
            transmitter.PoseHandler.GetHumanPose(ref transmitter.HumanPose);
            CompressAvatarData(transmitter.storedAvatarData,transmitter.HumanPose,animator);
            transmitter.storedAvatarData.LASM.AdditionalAvatarDatas = transmitter.SendingOutAvatarData.Count == 0? null: transmitter.SendingOutAvatarData.Values.ToArray();
            transmitter.storedAvatarData.LASM.Serialize(transmitter.AvatarSendWriter);
            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.LocalAvatarSync, transmitter.AvatarSendWriter.Length);
            BasisNetworkManagement.LocalPlayerPeer.Send(transmitter.AvatarSendWriter,BasisNetworkCommons.PlayerAvatarChannel,DeliveryMethod.Sequenced);
            transmitter.AvatarSendWriter.Reset();
            transmitter.ClearAdditional();
        }

        public static void InitalAvatarData(Animator animator,out StoredAvatarData StoredAvatarData)
        {
            var poseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            var humanPose = new HumanPose();
            poseHandler.GetHumanPose(ref humanPose);
            StoredAvatarData = new StoredAvatarData();
            CompressAvatarData(StoredAvatarData,humanPose, animator);
        }
        [BurstCompile]
        public static void CompressAvatarData(StoredAvatarData AvatarData, HumanPose pose, Animator animator)
        {
            int offset = 0;

            // Copy muscles to float array, we skip eyes and mouth.
            Array.Copy(pose.muscles, 0, AvatarData.FloatArray, 0, BasisAvatarMuscleRange.FirstBuffer);
            Array.Copy(pose.muscles, BasisAvatarMuscleRange.SecondBuffer, AvatarData.FloatArray, BasisAvatarMuscleRange.FirstBuffer, BasisAvatarMuscleRange.SizeAfterGap);

            // Compress Position 3*4 = 12 bytes
            BasisUnityBitPackerExtensionsUnsafe.WritePosition(animator.bodyPosition, ref AvatarData.LASM.array, ref offset);

            // Compress Rotation 3*4 = 12 + 2  14 bytes
            BasisUnityBitPackerExtensionsUnsafe.WriteQuaternionToBytes(animator.bodyRotation, ref AvatarData.LASM.array, ref offset, BasisNetworkPlayer.RotationCompression);

            // Compress Muscles totals 110 bytes
            CompressAvatarMuscles(ref AvatarData.FloatArray, ref AvatarData.LASM, ref offset);

            // Compress Muscles totals 34 bytes
            CompressAvatarMusclesBytes(ref AvatarData.FloatArray, ref AvatarData.LASM, ref offset);

            // Compress Scale 2 bytes
            CompressScale(animator.transform.localScale.y, ref AvatarData.LASM, ref offset);

            //12 + 14 + 110 + 34 + 2 = 172 bytes
        }
        /// <summary>
        /// uses 55 ushorts * 2 = 110 bytes
        /// </summary>
        /// <param name="floatArray"></param>
        /// <param name="message"></param>
        /// <param name="offset"></param>
        public static void CompressAvatarMuscles(ref float[] floatArray, ref LocalAvatarSyncMessage message, ref int offset)
        {
            var result = new List<ushort>(55); // non-finger muscles count
            for (int Index = 0; Index < 55; Index++)
            {
                float clamped = math.clamp(floatArray[Index], BasisAvatarMuscleRange.MinMuscle[Index], BasisAvatarMuscleRange.MaxMuscle[Index]);
                float normalized = (clamped - BasisAvatarMuscleRange.MinMuscle[Index]) / BasisAvatarMuscleRange.RangeMuscle[Index];
                ushort compressed = (ushort)(normalized * UShortRangeDifference);

                result.Add(compressed);
            }
            BasisDebug.Log($"CompressAvatarMuscles Size was {result.Count * 2}");

            BasisDebug.Log($"message.array Size was {message.array.Length}");
            BasisUnityBitPackerExtensionsUnsafe.WriteUShortsToBytes(result.ToArray(), ref message.array, ref offset,55);
        }
        /// <summary>
        /// uses 34 bytes
        /// </summary>
        /// <param name="floatArray"></param>
        /// <param name="message"></param>
        /// <param name="offset"></param>
        public static void CompressAvatarMusclesBytes(ref float[] floatArray, ref LocalAvatarSyncMessage message, ref int offset)
        {
            var result = new List<byte>(34); // finger muscles count
            for (int Index = 55; Index < 55 + 34; Index++)
            {
                float clamped = math.clamp(floatArray[Index], BasisAvatarMuscleRange.MinMuscle[Index], BasisAvatarMuscleRange.MaxMuscle[Index]);
                float normalized = (clamped - BasisAvatarMuscleRange.MinMuscle[Index]) / BasisAvatarMuscleRange.RangeMuscle[Index];
                byte compressed = (byte)(normalized * ByteRangeDifference);

                result.Add(compressed);
            }
            BasisDebug.Log($"CompressAvatarMusclesBytesSize was {result.Count}");
            BasisUnityBitPackerExtensionsUnsafe.WriteBytes(result.ToArray(), ref message.array, ref offset);
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

        private static void EnsureTransmitterIsInitialized(BasisNetworkTransmitter transmitter, Animator animator)
        {

            if (transmitter.PoseHandler == null)
                transmitter.PoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
        }
    }
}
