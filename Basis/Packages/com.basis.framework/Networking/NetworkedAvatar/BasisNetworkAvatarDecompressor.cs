using Basis.Scripts.Networking.Compression;
using Basis.Scripts.Networking.Receivers;
using Basis.Scripts.Profiler;
using System;
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

            float ReadCompressed(int index, bool asUshort)
            {
                float normalized;
                if (asUshort)
                {
                    ushort compressed = (ushort)(data[dataPos] | (data[dataPos + 1] << 8));
                    normalized = compressed / (float)BasisMuscleRange.UShortRangeDifference;
                    dataPos += 2;
                }
                else
                {
                    byte compressed = data[dataPos];
                    normalized = compressed / 255f;
                    dataPos += 1;
                }
                return BasisMuscleRange.MinMuscle[index] + normalized * BasisMuscleRange.RangeMuscle[index];
            }
            floatArray[0] = ReadCompressed(0, false);// Spine Front-Back: Range 80
            floatArray[1] = ReadCompressed(1, false);// Spine Left-Right: Range 80
            floatArray[2] = ReadCompressed(2, false);// Spine Twist Left-Right: Range 80
            floatArray[3] = ReadCompressed(3, false);// Chest Front-Back: Range 80
            floatArray[4] = ReadCompressed(4, false);// Chest Left-Right: Range 80
            floatArray[5] = ReadCompressed(5, false); // Chest Twist Left-Right: Range 80
            floatArray[6] = ReadCompressed(6, false); // UpperChest Front-Back: Range 40
            floatArray[7] = ReadCompressed(7, false);// UpperChest Left-Right: Range 40
            floatArray[8] = ReadCompressed(8, false); // UpperChest Twist Left-Right: Range 40
            floatArray[9] = ReadCompressed(9, false);// Neck Nod Down-Up: Range 80
            floatArray[10] = ReadCompressed(10, false);// Neck Tilt Left-Right: Range 80
            floatArray[11] = ReadCompressed(11, false);// Neck Turn Left-Right: Range 80
            floatArray[12] = ReadCompressed(12, false);// Head Nod Down-Up: Range 80
            floatArray[13] = ReadCompressed(13, false);// Head Tilt Left-Right: Range 80
            floatArray[14] = ReadCompressed(14, false); // Head Turn Left-Right: Range 80

            // no need to put this data on the network! 6 in total (saves between 6 and 16 bytes)
            // ReadCompressed(ref floatArray, 15, true); // Left Eye Down-Up: Range 25 byteable
            // ReadCompressed(ref floatArray, 16, true); // Left Eye In-Out: Range 40 byteable
            // ReadCompressed(ref floatArray, 17, true); // Right Eye Down-Up: Range 25 byteable
            // ReadCompressed(ref floatArray, 18, true); // Right Eye In-Out: Range 40 byteable
            // ReadCompressed(ref floatArray, 19, true); // Jaw Close: Range 20 byteable
            // ReadCompressed(ref floatArray, 20, true); // Jaw Left-Right: Range 20 byteable
            // Left Leg
            floatArray[21] = ReadCompressed(15, false);// Left Upper Leg Front-Back: Range 140
            floatArray[22] = ReadCompressed(16, false);// Left Upper Leg In-Out: Range 120
            floatArray[23] = ReadCompressed(17, false);// Left Upper Leg Twist In-Out: Range 120
            floatArray[24] = ReadCompressed(18, false);// Left Lower Leg Stretch: Range 160
            floatArray[25] = ReadCompressed(19, false);// Left Lower Leg Twist In-Out: Range 180
            floatArray[26] = ReadCompressed(20, false);// Left Foot Up-Down: Range 100
            floatArray[27] = ReadCompressed(21, true);// Left Foot Twist In-Out: Range 60 byteable
            floatArray[28] = ReadCompressed(22, true);// Left Toes Up-Down: Range 100 byteable

            // Right Leg
            floatArray[29] = ReadCompressed(23, false);// Right Upper Leg Front-Back: Range 140
            floatArray[30] = ReadCompressed(24, false); // Right Upper Leg In-Out: Range 120
            floatArray[31] = ReadCompressed(25, false);// Right Upper Leg Twist In-Out: Range 120
            floatArray[32] = ReadCompressed(26, false);// Right Lower Leg Stretch: Range 160
            floatArray[33] = ReadCompressed(27, false);// Right Lower Leg Twist In-Out: Range 180
            floatArray[34] = ReadCompressed(28, false);// Right Foot Up-Down: Range 100
            floatArray[35] = ReadCompressed(29, true);// Right Foot Twist In-Out: Range 60 byteable
            floatArray[36] = ReadCompressed(30, true);// Right Toes Up-Down: Range 100 byteable

            // Left Arm
            floatArray[37] = ReadCompressed(31, true);// Left Shoulder Down-Up: Range 45 byteable
            floatArray[38] = ReadCompressed(32, true); // Left Shoulder Front-Back: Range 30 byteable
            floatArray[39] = ReadCompressed(33, false);// Left Arm Down-Up: Range 160
            floatArray[40] = ReadCompressed(34, false);// Left Arm Front-Back: Range 200
            floatArray[41] = ReadCompressed(35, false);// Left Arm Twist In-Out: Range 180
            floatArray[42] = ReadCompressed(36, false);// Left Forearm Stretch: Range 160
            floatArray[43] = ReadCompressed(37, false);// Left Forearm Twist In-Out: Range 180
            floatArray[44] = ReadCompressed(38, false);// Left Hand Down-Up: Range 160
            floatArray[45] = ReadCompressed(39, false);// Left Hand In-Out: Range 80

            // Right Arm
            floatArray[46] = ReadCompressed(40, true);// Right Shoulder Down-Up: Range 45 byteable
            floatArray[47] = ReadCompressed(41, true);// Right Shoulder Front-Back: Range 30 byteable
            floatArray[48] = ReadCompressed(42, false);// Right Arm Down-Up: Range 160
            floatArray[49] = ReadCompressed(43, false);// Right Arm Front-Back: Range 200
            floatArray[50] = ReadCompressed(44, false);// Right Arm Twist In-Out: Range 180
            floatArray[51] = ReadCompressed(45, false);// Right Forearm Stretch: Range 160
            floatArray[52] = ReadCompressed(46, false);// Right Forearm Twist In-Out: Range 180
            floatArray[53] = ReadCompressed(47, false);// Right Hand Down-Up: Range 160
            floatArray[54] = ReadCompressed(48, false);// Right Hand In-Out: Range 80

            // Left Hand Fingers
            floatArray[55] = ReadCompressed(49, true);// Left Thumb 1 Stretched: Range 40 byteable
            floatArray[56] = ReadCompressed(50, true);// Left Thumb Spread: Range 50 byteable
            floatArray[57] = ReadCompressed(51, true);// Left Thumb 2 Stretched: Range 75 byteable
            floatArray[58] = ReadCompressed(52, true);// Left Thumb 3 Stretched: Range 75 byteable

            floatArray[59] = ReadCompressed(53, true);// Left Index 1 Stretched: Range 100 byteable
            floatArray[60] = ReadCompressed(54, true);// Left Index Spread: Range 40 byteable
            floatArray[61] = ReadCompressed(55, true);// Left Index 2 Stretched: Range 90 byteable
            floatArray[62] = ReadCompressed(56, true);// Left Index 3 Stretched: Range 90 byteable

            floatArray[63] = ReadCompressed(57, true);// Left Middle 1 Stretched: Range 100 byteable
            floatArray[64] = ReadCompressed(58, true);// Left Middle Spread: Range 15 byteable
            floatArray[65] = ReadCompressed(59, true);// Left Middle 2 Stretched: Range 90 byteable
            floatArray[66] = ReadCompressed(60, true);// Left Middle 3 Stretched: Range 90 byteable

            floatArray[67] = ReadCompressed(61, true); // Left Ring 1 Stretched: Range 100 byteable
            floatArray[68] = ReadCompressed(62, true);// Left Ring Spread: Range 15 byteable
            floatArray[69] = ReadCompressed(63, true);// Left Ring 2 Stretched: Range 90 byteable
            floatArray[70] = ReadCompressed(64, true); // Left Ring 3 Stretched: Range 90 byteable

            floatArray[71] = ReadCompressed(65, true);// Left Little 1 Stretched: Range 100 byteable
            floatArray[72] = ReadCompressed(66, true);// Left Little Spread: Range 40 byteable
            floatArray[73] = ReadCompressed(67, true);// Left Little 2 Stretched: Range 90 byteable
            floatArray[74] = ReadCompressed(68, true);// Left Little 3 Stretched: Range 90 byteable

            // Right Hand Fingers
            floatArray[75] = ReadCompressed(69, true);// Right Thumb 1 Stretched: Range 40 byteable
            floatArray[76] = ReadCompressed(70, true);// Right Thumb Spread: Range 50 byteable
            floatArray[77] = ReadCompressed(71, true);// Right Thumb 2 Stretched: Range 75 byteable
            floatArray[78] = ReadCompressed(72, true);// Right Thumb 3 Stretched: Range 75 byteable

            floatArray[79] = ReadCompressed(73, true);// Right Index 1 Stretched: Range 100 byteable
            floatArray[80] = ReadCompressed(74, true);// Right Index Spread: Range 40 byteable
            floatArray[81] = ReadCompressed(75, true);// Right Index 2 Stretched: Range 90 byteable
            floatArray[82] = ReadCompressed(76, true);// Right Index 3 Stretched: Range 90 byteable

            floatArray[83] = ReadCompressed(77, true);// Right Middle 1 Stretched: Range 100 byteable
            floatArray[84] = ReadCompressed(78, true);// Right Middle Spread: Range 15 byteable
            floatArray[85] = ReadCompressed(79, true);// Right Middle 2 Stretched: Range 90 byteable
            floatArray[86] = ReadCompressed(80, true);// Right Middle 3 Stretched: Range 90 byteable

            floatArray[87] = ReadCompressed(81, true);// Right Ring 1 Stretched: Range 100 byteable
            floatArray[88] = ReadCompressed(82, true);// Right Ring Spread: Range 15 byteable
            floatArray[89] = ReadCompressed(83, true); // Right Ring 2 Stretched: Range 90 byteable
            floatArray[90] = ReadCompressed(84, true);// Right Ring 3 Stretched: Range 90 byteable

            floatArray[91] = ReadCompressed(85, true);// Right Little 1 Stretched: Range 100 byteable
            floatArray[92] = ReadCompressed(86, true);// Right Little Spread: Range 40 byteable
            floatArray[93] = ReadCompressed(87, true);// Right Little 2 Stretched: Range 90 byteable
            floatArray[94] = ReadCompressed(88, true); // Right Little 3 Stretched: Range 90 byteable

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
