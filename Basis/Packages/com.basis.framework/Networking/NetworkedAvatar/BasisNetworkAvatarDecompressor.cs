using Basis.Scripts.Networking.Compression;
using Basis.Scripts.Networking.Receivers;
using Basis.Scripts.Profiler;
using System;
using Unity.Mathematics;
using static SerializableBasis;

namespace Basis.Scripts.Networking.NetworkedAvatar
{
    public static class BasisNetworkAvatarDecompressor
    {
        private const float MinimumValueSupported = 0.005f;
        private const float MaximumValueSupported = 150f;
        private const ushort UShortMin = ushort.MinValue;
        private const ushort UShortMax = ushort.MaxValue;
        private const float FloatRangeDifference = UShortMax - UShortMin;

        public static void DecompressAndProcessAvatar(BasisNetworkReceiver baseReceiver, ServerSideSyncPlayerMessage syncMessage)
        {
            if (syncMessage.avatarSerialization.array == null)
            {
                throw new ArgumentException("Cannot serialize avatar data.");
            }
            byte[] data = syncMessage.avatarSerialization.array;
            int offset = 0;
            int length = data.Length;
            if (length >= LocalAvatarSyncMessage.AvatarSyncSize)
            {
                double Interval = (double)BasisNetworkManagement.ServerMetaDataMessage.SyncInterval;// Interval + syncMessage.interval
                BasisAvatarBuffer avatarBuffer = CreateAvatarBuffer(data, ref offset, baseReceiver, (double)(Interval + (double)syncMessage.interval) / 1000f);
                EnqueueAndProcessAdditionalData(baseReceiver, ref avatarBuffer, syncMessage.avatarSerialization, length);
            }
            else
            {
                BasisDebug.LogError("Data did not have enough for AvatarsyncMessage", BasisDebug.LogTag.Networking);
            }
        }
        /// <summary>
        /// tied to initalization
        /// </summary>
        /// <param name="baseReceiver"></param>
        /// <param name="avatarSerialization"></param>
        /// <exception cref="ArgumentException"></exception>
        public static void DecompressAndProcessAvatar(BasisNetworkReceiver baseReceiver, LocalAvatarSyncMessage avatarSerialization)
        {
            if (avatarSerialization.array == null)
            {
                throw new ArgumentException("Cannot serialize inital avatar data.");
            }
            byte[] data = avatarSerialization.array;
            int offset = 0;
            int length = data.Length;
            if (length >= LocalAvatarSyncMessage.AvatarSyncSize)
            {
                BasisAvatarBuffer avatarBuffer = CreateAvatarBuffer(data, ref offset, baseReceiver, 0.01f);
                EnqueueAndProcessAdditionalData(baseReceiver, ref avatarBuffer, avatarSerialization, length);
            }
            else
            {
                BasisDebug.LogError("Data did not have enough for AvatarsyncMessage", BasisDebug.LogTag.Networking);
            }
        }

        private static BasisAvatarBuffer CreateAvatarBuffer(byte[] data, ref int offset, BasisNetworkReceiver baseReceiver, double SecondsInterval)
        {
            var position = BasisUnityBitPackerExtensionsUnsafe.ReadPosition(ref data, ref offset);
            var rotation = BasisUnityBitPackerExtensionsUnsafe.ReadQuaternionFromBytes(ref data, BasisNetworkPlayer.RotationCompression, ref offset);
            DecompressAvatarMuscles_NoLoop(data, ref baseReceiver.muscles, ref offset);
            float scale = MuscleDecompress(BasisUnityBitPackerExtensionsUnsafe.ReadUShort(ref data, ref offset),MinimumValueSupported,MaximumValueSupported);

            return new BasisAvatarBuffer
            {
                Position = position,
                rotation = rotation,
                Muscles = baseReceiver.muscles,
                Scale = scale,
                SecondsInterval = SecondsInterval
            };
        }

        public static void DecompressAvatarMuscles_NoLoop(byte[] data, ref float[] floatArray, ref int offset)
        {
            int dataPos = offset;

            void ReadCompressed(int index, bool AsByte,ref float[] floatArray)
            {
                float normalized;
                if (AsByte)
                {
                    byte compressed = data[dataPos];
                    normalized = compressed / 255f;
                    dataPos += 1;
                }
                else
                {
                    ushort compressed = (ushort)(data[dataPos] | (data[dataPos + 1] << 8));
                    normalized = compressed / 65535f;
                    dataPos += 2;
                }

                float value = BasisMuscleRange.MinMuscle[index] + normalized * BasisMuscleRange.RangeMuscle[index];
                floatArray[index] = math.clamp(value, BasisMuscleRange.MinMuscle[index], BasisMuscleRange.MaxMuscle[index]);
            }
            ReadCompressed(0, false,ref floatArray);// Spine Front-Back: Range 80
            ReadCompressed(1, false, ref floatArray);// Spine Left-Right: Range 80
            ReadCompressed(2, false, ref floatArray);// Spine Twist Left-Right: Range 80
            ReadCompressed(3, false, ref floatArray);// Chest Front-Back: Range 80
            ReadCompressed(4, false, ref floatArray);// Chest Left-Right: Range 80
            ReadCompressed(5, false, ref floatArray); // Chest Twist Left-Right: Range 80
            ReadCompressed(6, false, ref floatArray); // UpperChest Front-Back: Range 40
            ReadCompressed(7, false, ref floatArray);// UpperChest Left-Right: Range 40
            ReadCompressed(8, false, ref floatArray); // UpperChest Twist Left-Right: Range 40
            ReadCompressed(9, false, ref floatArray);// Neck Nod Down-Up: Range 80
            ReadCompressed(10, false, ref floatArray);// Neck Tilt Left-Right: Range 80
            ReadCompressed(11, false, ref floatArray);// Neck Turn Left-Right: Range 80
            ReadCompressed(12, false, ref floatArray);// Head Nod Down-Up: Range 80
            ReadCompressed(13, false, ref floatArray);// Head Tilt Left-Right: Range 80
            ReadCompressed(14, false, ref floatArray); // Head Turn Left-Right: Range 80

            // no need to put this data on the network! 6 in total (saves between 6 and 16 bytes)
            // ReadCompressed(ref floatArray, 15, true); // Left Eye Down-Up: Range 25 byteable
            // ReadCompressed(ref floatArray, 16, true); // Left Eye In-Out: Range 40 byteable
            // ReadCompressed(ref floatArray, 17, true); // Right Eye Down-Up: Range 25 byteable
            // ReadCompressed(ref floatArray, 18, true); // Right Eye In-Out: Range 40 byteable
            // ReadCompressed(ref floatArray, 19, true); // Jaw Close: Range 20 byteable
            // ReadCompressed(ref floatArray, 20, true); // Jaw Left-Right: Range 20 byteable

            // Left Leg
            ReadCompressed(21, false, ref floatArray);// Left Upper Leg Front-Back: Range 140
            ReadCompressed(22, false, ref floatArray);// Left Upper Leg In-Out: Range 120
            ReadCompressed(23, false, ref floatArray);// Left Upper Leg Twist In-Out: Range 120
            ReadCompressed(24, false, ref floatArray);// Left Lower Leg Stretch: Range 160
            ReadCompressed(25, false, ref floatArray);// Left Lower Leg Twist In-Out: Range 180
            ReadCompressed(26, false, ref floatArray);// Left Foot Up-Down: Range 100

            ReadCompressed(27, true, ref floatArray);// Left Foot Twist In-Out: Range 60 byteable
            ReadCompressed(28, true, ref floatArray);// Left Toes Up-Down: Range 100 byteable

            // Right Leg
            ReadCompressed(29, false, ref floatArray);// Right Upper Leg Front-Back: Range 140
            ReadCompressed(30, false, ref floatArray); // Right Upper Leg In-Out: Range 120
            ReadCompressed(31, false, ref floatArray);// Right Upper Leg Twist In-Out: Range 120
            ReadCompressed(32, false, ref floatArray);// Right Lower Leg Stretch: Range 160
            ReadCompressed(33, false, ref floatArray);// Right Lower Leg Twist In-Out: Range 180
            ReadCompressed(34, false, ref floatArray);// Right Foot Up-Down: Range 100

            ReadCompressed(35, true, ref floatArray);// Right Foot Twist In-Out: Range 60 byteable
            ReadCompressed(36, true, ref floatArray);// Right Toes Up-Down: Range 100 byteable

            // Left Arm
            ReadCompressed(37, true, ref floatArray);// Left Shoulder Down-Up: Range 45 byteable
            ReadCompressed(38, true, ref floatArray); // Left Shoulder Front-Back: Range 30 byteable

            ReadCompressed(39, false, ref floatArray);// Left Arm Down-Up: Range 160
            ReadCompressed(40, false, ref floatArray);// Left Arm Front-Back: Range 200
            ReadCompressed(41, false, ref floatArray);// Left Arm Twist In-Out: Range 180
            ReadCompressed(42, false, ref floatArray);// Left Forearm Stretch: Range 160
            ReadCompressed(43, false, ref floatArray);// Left Forearm Twist In-Out: Range 180
            ReadCompressed(44, false, ref floatArray);// Left Hand Down-Up: Range 160
            ReadCompressed(45, false, ref floatArray);// Left Hand In-Out: Range 80

            // Right Arm
            ReadCompressed(46, true, ref floatArray);// Right Shoulder Down-Up: Range 45 byteable
            ReadCompressed(47, true, ref floatArray);// Right Shoulder Front-Back: Range 30 byteable

            ReadCompressed(48, false, ref floatArray);// Right Arm Down-Up: Range 160
            ReadCompressed(49, false, ref floatArray);// Right Arm Front-Back: Range 200
            ReadCompressed(50, false, ref floatArray);// Right Arm Twist In-Out: Range 180
            ReadCompressed(51, false, ref floatArray);// Right Forearm Stretch: Range 160
            ReadCompressed(52, false, ref floatArray);// Right Forearm Twist In-Out: Range 180
            ReadCompressed(53, false, ref floatArray);// Right Hand Down-Up: Range 160
            ReadCompressed(54, false, ref floatArray);// Right Hand In-Out: Range 80

            // Left Hand Fingers
            ReadCompressed(55, true, ref floatArray);// Left Thumb 1 Stretched: Range 40 byteable
            ReadCompressed(56, true, ref floatArray);// Left Thumb Spread: Range 50 byteable
            ReadCompressed(57, true, ref floatArray);// Left Thumb 2 Stretched: Range 75 byteable
            ReadCompressed(58, true, ref floatArray);// Left Thumb 3 Stretched: Range 75 byteable

            ReadCompressed(59, true, ref floatArray);// Left Index 1 Stretched: Range 100 byteable
            ReadCompressed(60, true, ref floatArray);// Left Index Spread: Range 40 byteable
            ReadCompressed(61, true, ref floatArray);// Left Index 2 Stretched: Range 90 byteable
            ReadCompressed(62, true, ref floatArray);// Left Index 3 Stretched: Range 90 byteable

            ReadCompressed(63, true, ref floatArray);// Left Middle 1 Stretched: Range 100 byteable
            ReadCompressed(64, true, ref floatArray);// Left Middle Spread: Range 15 byteable
            ReadCompressed(65, true, ref floatArray);// Left Middle 2 Stretched: Range 90 byteable
            ReadCompressed(66, true, ref floatArray);// Left Middle 3 Stretched: Range 90 byteable

            ReadCompressed(67, true, ref floatArray); // Left Ring 1 Stretched: Range 100 byteable
            ReadCompressed(68, true, ref floatArray);// Left Ring Spread: Range 15 byteable
            ReadCompressed(69, true, ref floatArray);// Left Ring 2 Stretched: Range 90 byteable
            ReadCompressed(70, true, ref floatArray); // Left Ring 3 Stretched: Range 90 byteable

            ReadCompressed(71, true, ref floatArray);// Left Little 1 Stretched: Range 100 byteable
            ReadCompressed(72, true, ref floatArray);// Left Little Spread: Range 40 byteable
            ReadCompressed(73, true, ref floatArray);// Left Little 2 Stretched: Range 90 byteable
            ReadCompressed(74, true, ref floatArray);// Left Little 3 Stretched: Range 90 byteable

            // Right Hand Fingers
            ReadCompressed(75, true, ref floatArray);// Right Thumb 1 Stretched: Range 40 byteable
            ReadCompressed(76, true, ref floatArray);// Right Thumb Spread: Range 50 byteable
            ReadCompressed(77, true, ref floatArray);// Right Thumb 2 Stretched: Range 75 byteable
            ReadCompressed(78, true, ref floatArray);// Right Thumb 3 Stretched: Range 75 byteable

            ReadCompressed(79, true, ref floatArray);// Right Index 1 Stretched: Range 100 byteable
            ReadCompressed(80, true, ref floatArray);// Right Index Spread: Range 40 byteable
            ReadCompressed(81, true, ref floatArray);// Right Index 2 Stretched: Range 90 byteable
            ReadCompressed(82, true, ref floatArray);// Right Index 3 Stretched: Range 90 byteable

            ReadCompressed(83, true, ref floatArray);// Right Middle 1 Stretched: Range 100 byteable
            ReadCompressed(84, true, ref floatArray);// Right Middle Spread: Range 15 byteable
            ReadCompressed(85, true, ref floatArray);// Right Middle 2 Stretched: Range 90 byteable
            ReadCompressed(86, true, ref floatArray);// Right Middle 3 Stretched: Range 90 byteable

            ReadCompressed(87, true, ref floatArray);// Right Ring 1 Stretched: Range 100 byteable
            ReadCompressed(88, true, ref floatArray);// Right Ring Spread: Range 15 byteable
            ReadCompressed(89, true, ref floatArray); // Right Ring 2 Stretched: Range 90 byteable
            ReadCompressed(90, true, ref floatArray);// Right Ring 3 Stretched: Range 90 byteable

            ReadCompressed(91, true, ref floatArray);// Right Little 1 Stretched: Range 100 byteable
            ReadCompressed(92, true, ref floatArray);// Right Little Spread: Range 40 byteable
            ReadCompressed(93, true, ref floatArray);// Right Little 2 Stretched: Range 90 byteable
            ReadCompressed(94, true, ref floatArray); // Right Little 3 Stretched: Range 90 byteable

            offset = dataPos;
        }
        public static float MuscleDecompress(ushort value, float minValue, float maxValue)
        {
            float normalized = value / FloatRangeDifference;
            return normalized * (maxValue - minValue) + minValue;
        }
        private static void EnqueueAndProcessAdditionalData(BasisNetworkReceiver baseReceiver, ref BasisAvatarBuffer avatarBuffer, LocalAvatarSyncMessage message, int dataLength)
        {
            // Add to profiler
            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerSideSyncPlayer, dataLength);

            // Queue the avatar buffer
            baseReceiver.EnQueueAvatarBuffer(ref avatarBuffer);

            // Process additional avatar data
            if (message.AdditionalAvatarDataSize > 0 && message.AdditionalAvatarDatas != null)
            {
                bool isDifferentAvatar = message.LinkedAvatarIndex != baseReceiver.LastLinkedAvatarIndex;

                for (int Index = 0; Index < message.AdditionalAvatarDataSize; Index++)
                {
                    AdditionalAvatarData data = message.AdditionalAvatarDatas[Index];

                    if (data.messageIndex < baseReceiver.NetworkBehaviourCount)
                    {
                        baseReceiver.NetworkBehaviours[data.messageIndex].OnNetworkMessageServerReductionSystem(data.array, isDifferentAvatar);
                    }
                }
            }
        }
    }
}
