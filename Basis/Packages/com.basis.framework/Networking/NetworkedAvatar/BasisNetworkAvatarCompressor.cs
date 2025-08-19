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
        public static void Compress(BasisNetworkTransmitter transmitter, Animator animator)
        {
            EnsureTransmitterIsInitialized(transmitter, animator);

            // Get current pose from Animator
            transmitter.PoseHandler.GetHumanPose(ref transmitter.HumanPose);
            CompressAvatarData(transmitter.storedAvatarData, transmitter.HumanPose, animator);
            transmitter.storedAvatarData.LASM.AdditionalAvatarDatas = transmitter.SendingOutAvatarData.Count == 0 ? null : transmitter.SendingOutAvatarData.Values.ToArray();
            transmitter.storedAvatarData.LASM.Serialize(transmitter.AvatarSendWriter);
            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.LocalAvatarSync, transmitter.AvatarSendWriter.Length);
            BasisNetworkManagement.LocalPlayerPeer.Send(transmitter.AvatarSendWriter, BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced);
            transmitter.AvatarSendWriter.Reset();
            transmitter.ClearAdditional();
        }

        public static void InitalAvatarData(Animator animator, out StoredAvatarData StoredAvatarData)
        {
            var poseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
            var humanPose = new HumanPose();
            poseHandler.GetHumanPose(ref humanPose);
            StoredAvatarData = new StoredAvatarData();
            CompressAvatarData(StoredAvatarData, humanPose, animator);
        }
        [BurstCompile]
        public static void CompressAvatarData(StoredAvatarData AvatarData, HumanPose pose, Animator animator)
        {
            int offset = 0;
            // Compress Position 3*4 = 12 bytes
            BasisUnityBitPackerExtensionsUnsafe.WritePosition(animator.bodyPosition, ref AvatarData.LASM.array, ref offset);

            // Compress Rotation 3*4 = 12 + 2 = 14 bytes
            BasisUnityBitPackerExtensionsUnsafe.WriteQuaternionToBytes(animator.bodyRotation, ref AvatarData.LASM.array, ref offset, BasisNetworkPlayer.RotationCompression);

            // Compress Muscles totals 137 bytes
            CompressAvatarMuscles_NoLoop(ref pose.muscles, ref AvatarData.LASM, ref offset);

            // Compress Scale 2 bytes
            CompressScale(animator.transform.localScale.y, ref AvatarData.LASM, ref offset);
            //28
            // 12 + 14 + 137 + 2 = 165 bytes
        }
        const int muscleCount = 95 - 6;//we remove the stuff we dont even put on the network first.
        public static byte[][] CBytes = new byte[muscleCount][];
        public static void CompressAvatarMuscles_NoLoop(ref float[] floatArray, ref LocalAvatarSyncMessage message, ref int offset)
        {
            BasisDebug.Log($"Count was {floatArray.Length}");

            SetCompressedUshort(ref CBytes, ref floatArray, 0, 0, false);  // Spine Front-Back: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 1, 1, false);  // Spine Left-Right: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 2, 2, false);  // Spine Twist Left-Right: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 3, 3, false);  // Chest Front-Back: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 4, 4, false);  // Chest Left-Right: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 5, 5, false);  // Chest Twist Left-Right: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 6, 6, false);  // UpperChest Front-Back: Range 40
            SetCompressedUshort(ref CBytes, ref floatArray, 7, 7, false);  // UpperChest Left-Right: Range 40
            SetCompressedUshort(ref CBytes, ref floatArray, 8, 8, false);  // UpperChest Twist Left-Right: Range 40
            SetCompressedUshort(ref CBytes, ref floatArray, 9, 9, false);  // Neck Nod Down-Up: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 10, 10, false); // Neck Tilt Left-Right: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 11, 11, false); // Neck Turn Left-Right: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 12, 12, false); // Head Nod Down-Up: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 13, 13, false); // Head Tilt Left-Right: Range 80
            SetCompressedUshort(ref CBytes, ref floatArray, 14, 14, false); // Head Turn Left-Right: Range 80

            // no need to put this data on the network! 6 in total (saves between 6 and 16 bytes)
            // SetCompressedUshort(ref floatArray, 15, true); // Left Eye Down-Up: Range 25 byteable
            // SetCompressedUshort(ref floatArray, 16, true); // Left Eye In-Out: Range 40 byteable
            // SetCompressedUshort(ref floatArray, 17, true); // Right Eye Down-Up: Range 25 byteable
            // SetCompressedUshort(ref floatArray, 18, true); // Right Eye In-Out: Range 40 byteable
            // SetCompressedUshort(ref floatArray, 19, true); // Jaw Close: Range 20 byteable
            // SetCompressedUshort(ref floatArray, 20, true); // Jaw Left-Right: Range 20 byteable

            // Left Leg
            SetCompressedUshort(ref CBytes, ref floatArray, 21, 15, false); // Left Upper Leg Front-Back: Range 140
            SetCompressedUshort(ref CBytes, ref floatArray, 22, 16, false); // Left Upper Leg In-Out: Range 120
            SetCompressedUshort(ref CBytes, ref floatArray, 23, 17, false); // Left Upper Leg Twist In-Out: Range 120
            SetCompressedUshort(ref CBytes, ref floatArray, 24, 18, false); // Left Lower Leg Stretch: Range 160
            SetCompressedUshort(ref CBytes, ref floatArray, 25, 19, false); // Left Lower Leg Twist In-Out: Range 180
            SetCompressedUshort(ref CBytes, ref floatArray, 26, 20, false); // Left Foot Up-Down: Range 100
            SetCompressedUshort(ref CBytes, ref floatArray, 27, 21, true); // Left Foot Twist In-Out: Range 60 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 28, 22, true); // Left Toes Up-Down: Range 100 byteable

            // Right Leg
            SetCompressedUshort(ref CBytes, ref floatArray, 29, 23, false); // Right Upper Leg Front-Back: Range 140
            SetCompressedUshort(ref CBytes, ref floatArray, 30, 24, false); // Right Upper Leg In-Out: Range 120
            SetCompressedUshort(ref CBytes, ref floatArray, 31, 25, false); // Right Upper Leg Twist In-Out: Range 120
            SetCompressedUshort(ref CBytes, ref floatArray, 32, 26, false); // Right Lower Leg Stretch: Range 160
            SetCompressedUshort(ref CBytes, ref floatArray, 33, 27, false); // Right Lower Leg Twist In-Out: Range 180
            SetCompressedUshort(ref CBytes, ref floatArray, 34, 28, false); // Right Foot Up-Down: Range 100
            SetCompressedUshort(ref CBytes, ref floatArray, 35, 29, true); // Right Foot Twist In-Out: Range 60 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 36, 30, true); // Right Toes Up-Down: Range 100 byteable

            // Left Arm
            SetCompressedUshort(ref CBytes, ref floatArray, 37, 31, true); // Left Shoulder Down-Up: Range 45 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 38, 32, true); // Left Shoulder Front-Back: Range 30 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 39, 33, false); // Left Arm Down-Up: Range 160
            SetCompressedUshort(ref CBytes, ref floatArray, 40, 34, false); // Left Arm Front-Back: Range 200
            SetCompressedUshort(ref CBytes, ref floatArray, 41, 35, false); // Left Arm Twist In-Out: Range 180
            SetCompressedUshort(ref CBytes, ref floatArray, 42, 36, false); // Left Forearm Stretch: Range 160
            SetCompressedUshort(ref CBytes, ref floatArray, 43, 37, false); // Left Forearm Twist In-Out: Range 180
            SetCompressedUshort(ref CBytes, ref floatArray, 44, 38, false); // Left Hand Down-Up: Range 160
            SetCompressedUshort(ref CBytes, ref floatArray, 45, 39, false); // Left Hand In-Out: Range 80

            // Right Arm
            SetCompressedUshort(ref CBytes, ref floatArray, 46, 40, true); // Right Shoulder Down-Up: Range 45 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 47, 41, true); // Right Shoulder Front-Back: Range 30 byteable

            SetCompressedUshort(ref CBytes, ref floatArray, 48, 42, false); // Right Arm Down-Up: Range 160
            SetCompressedUshort(ref CBytes, ref floatArray, 49, 43, false); // Right Arm Front-Back: Range 200
            SetCompressedUshort(ref CBytes, ref floatArray, 50, 44, false); // Right Arm Twist In-Out: Range 180
            SetCompressedUshort(ref CBytes, ref floatArray, 51, 45, false); // Right Forearm Stretch: Range 160
            SetCompressedUshort(ref CBytes, ref floatArray, 52, 46, false); // Right Forearm Twist In-Out: Range 180
            SetCompressedUshort(ref CBytes, ref floatArray, 53, 47, false); // Right Hand Down-Up: Range 160
            SetCompressedUshort(ref CBytes, ref floatArray, 54, 48, false); // Right Hand In-Out: Range 80

            // Left Hand Fingers
            SetCompressedUshort(ref CBytes, ref floatArray, 55, 49, true); // Left Thumb 1 Stretched: Range 40 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 56, 50, true); // Left Thumb Spread: Range 50 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 57, 51, true); // Left Thumb 2 Stretched: Range 75 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 58, 52, true); // Left Thumb 3 Stretched: Range 75 byteable

            SetCompressedUshort(ref CBytes, ref floatArray, 59, 53, true); // Left Index 1 Stretched: Range 100 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 60, 54, true); // Left Index Spread: Range 40 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 61, 55, true); // Left Index 2 Stretched: Range 90 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 62, 56, true); // Left Index 3 Stretched: Range 90 byteable

            SetCompressedUshort(ref CBytes, ref floatArray, 63, 57, true); // Left Middle 1 Stretched: Range 100 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 64, 58, true); // Left Middle Spread: Range 15 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 65, 59, true); // Left Middle 2 Stretched: Range 90 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 66, 60, true); // Left Middle 3 Stretched: Range 90 byteable

            SetCompressedUshort(ref CBytes, ref floatArray, 67, 61, true); // Left Ring 1 Stretched: Range 100 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 68, 62, true); // Left Ring Spread: Range 15 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 69, 63, true); // Left Ring 2 Stretched: Range 90 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 70, 64, true); // Left Ring 3 Stretched: Range 90 byteable

            SetCompressedUshort(ref CBytes, ref floatArray, 71, 65, true); // Left Little 1 Stretched: Range 100 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 72, 66, true); // Left Little Spread: Range 40 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 73, 67, true); // Left Little 2 Stretched: Range 90 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 74, 68, true); // Left Little 3 Stretched: Range 90 byteable

            // Right Hand Fingers
            SetCompressedUshort(ref CBytes, ref floatArray, 75, 69, true); // Right Thumb 1 Stretched: Range 40 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 76, 70, true); // Right Thumb Spread: Range 50 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 77, 71, true); // Right Thumb 2 Stretched: Range 75 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 78, 72, true); // Right Thumb 3 Stretched: Range 75 byteable

            SetCompressedUshort(ref CBytes, ref floatArray, 79, 73, true); // Right Index 1 Stretched: Range 100 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 80, 74, true); // Right Index Spread: Range 40 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 81, 75, true); // Right Index 2 Stretched: Range 90 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 82, 76, true); // Right Index 3 Stretched: Range 90 byteable

            SetCompressedUshort(ref CBytes, ref floatArray, 83, 77, true); // Right Middle 1 Stretched: Range 100 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 84, 78, true); // Right Middle Spread: Range 15 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 85, 79, true); // Right Middle 2 Stretched: Range 90 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 86, 80, true); // Right Middle 3 Stretched: Range 90 byteable

            SetCompressedUshort(ref CBytes, ref floatArray, 87, 81, true); // Right Ring 1 Stretched: Range 100 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 88, 82, true); // Right Ring Spread: Range 15 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 89, 83, true); // Right Ring 2 Stretched: Range 90 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 90, 84, true); // Right Ring 3 Stretched: Range 90 byteable

            SetCompressedUshort(ref CBytes, ref floatArray, 91, 85, true); // Right Little 1 Stretched: Range 100 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 92, 86, true); // Right Little Spread: Range 40 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 93, 87, true); // Right Little 2 Stretched: Range 90 byteable
            SetCompressedUshort(ref CBytes, ref floatArray, 94, 88, true); // Right Little 3 Stretched: Range 90 byteable


            int totalLength = 0;
            for (int Index = 0; Index < muscleCount; Index++)
            {
                totalLength += CBytes[Index].Length;
            }
            byte[] combined = new byte[totalLength];
            int pos = 0;
            for (int Index = 0; Index < muscleCount; Index++)
            {
                Array.Copy(CBytes[Index], 0, combined, pos, CBytes[Index].Length);
                pos += CBytes[Index].Length;
            }
            BasisDebug.Log($"compressed size was {combined.Length}");
            // Write to message
            Array.Copy(combined, 0, message.array, offset, combined.Length);
            offset += combined.Length;
        }
        public static void SetCompressedUshort(ref byte[][] compressedBytes, ref float[] value, int index, int compressedindex, bool asUshort)
        {
            float clamped = math.clamp(value[index], BasisMuscleRange.MinMuscle[index], BasisMuscleRange.MaxMuscle[index]);
            float normalized = (clamped - BasisMuscleRange.MinMuscle[index]) / BasisMuscleRange.RangeMuscle[index];

            int requiredLength = asUshort ? 2 : 1;

            // Allocate if null or wrong length
            if (compressedBytes[compressedindex] == null || compressedBytes[compressedindex].Length != requiredLength)
            {
                compressedBytes[compressedindex] = new byte[requiredLength];
            }

            if (asUshort)
            {
                ushort compressed = (ushort)(normalized * BasisMuscleRange.UShortRangeDifference);
                compressedBytes[compressedindex][0] = (byte)(compressed & 0xFF); // little endian
                compressedBytes[compressedindex][1] = (byte)(compressed >> 8);
            }
            else
            {
                byte compressed = (byte)(normalized * 255f);
                compressedBytes[compressedindex][0] = compressed;
            }
        }
        public static void CompressScale(float scale, ref LocalAvatarSyncMessage message, ref int offset)
        {
            const float Min = 0.005f;
            const float Max = 150f;
            const float range = Max - Min;

            float clamped = math.clamp(scale, Min, Max);
            float normalized = (clamped - Min) / range;

            ushort compressed = (ushort)(normalized * BasisMuscleRange.UShortRangeDifference);
            BasisUnityBitPackerExtensionsUnsafe.WriteUShort(compressed, ref message.array, ref offset);
        }

        private static void EnsureTransmitterIsInitialized(BasisNetworkTransmitter transmitter, Animator animator)
        {

            if (transmitter.PoseHandler == null)
                transmitter.PoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
        }
    }
}
