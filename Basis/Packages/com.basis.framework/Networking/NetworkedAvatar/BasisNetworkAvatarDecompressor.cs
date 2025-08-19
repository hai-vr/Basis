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

            // Helper local function to read one compressed value
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

            // Spine
            for (int i = 0; i <= 14; i++)
            {
                floatArray[i] = ReadCompressed(i, false);
            }

            // Skip 6 indices in floatArray (do not assign these values)
            for (int i = 0; i < 6; i++)
            {
                ReadCompressed(15 + i, false); // Still read data to advance dataPos
            }
            // Left Leg
            floatArray[15] = ReadCompressed(21, false);
            floatArray[16] = ReadCompressed(22, false);
            floatArray[17] = ReadCompressed(23, false);
            floatArray[18] = ReadCompressed(24, false);
            floatArray[19] = ReadCompressed(25, false);
            floatArray[20] = ReadCompressed(26, false);
            floatArray[21] = ReadCompressed(27, true);
            floatArray[22] = ReadCompressed(28, true);

            // Right Leg
            floatArray[23] = ReadCompressed(29, false);
            floatArray[24] = ReadCompressed(30, false);
            floatArray[25] = ReadCompressed(31, false);
            floatArray[26] = ReadCompressed(32, false);
            floatArray[27] = ReadCompressed(33, false);
            floatArray[28] = ReadCompressed(34, false);
            floatArray[29] = ReadCompressed(35, true);
            floatArray[30] = ReadCompressed(36, true);

            // Left Arm
            floatArray[31] = ReadCompressed(37, true);
            floatArray[32] = ReadCompressed(38, true);
            floatArray[33] = ReadCompressed(39, false);
            floatArray[34] = ReadCompressed(40, false);
            floatArray[35] = ReadCompressed(41, false);
            floatArray[36] = ReadCompressed(42, false);
            floatArray[37] = ReadCompressed(43, false);
            floatArray[38] = ReadCompressed(44, false);
            floatArray[39] = ReadCompressed(45, false);

            // Right Arm
            floatArray[40] = ReadCompressed(46, true);
            floatArray[41] = ReadCompressed(47, true);
            floatArray[42] = ReadCompressed(48, false);
            floatArray[43] = ReadCompressed(49, false);
            floatArray[44] = ReadCompressed(50, false);
            floatArray[45] = ReadCompressed(51, false);
            floatArray[46] = ReadCompressed(52, false);
            floatArray[47] = ReadCompressed(53, false);
            floatArray[48] = ReadCompressed(54, false);

            // Left Hand Fingers
            for (int i = 55; i <= 70; i++)
            {
                floatArray[i - 6] = ReadCompressed(i, true); // adjust index for skipped 6
            }
            // Right Hand Fingers
            for (int i = 75; i <= 90; i++)
            {
                floatArray[i - 6 - 10] = ReadCompressed(i, true); // adjust index for previous skips
            }
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
