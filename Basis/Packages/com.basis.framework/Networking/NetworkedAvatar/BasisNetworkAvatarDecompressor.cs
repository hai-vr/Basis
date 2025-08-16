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
            float[] muscles = GenerateMuscleArray(ref data, ref baseReceiver.CopyData, ref offset);
            float scale = MuscleDecompress(BasisUnityBitPackerExtensionsUnsafe.ReadUShort(ref data, ref offset),MinimumValueSupported,MaximumValueSupported);

            return new BasisAvatarBuffer
            {
                Position = position,
                rotation = rotation,
                Muscles = muscles,
                Scale = scale,
                SecondsInterval = SecondsInterval
            };
        }

        private static float[] GenerateMuscleArray(ref byte[] data, ref ushort[] copyData, ref int offset)
        {
            int nonFingerCount = 55;
            int fingerCount = 34;

            // Read first 55 muscles as ushorts
            BasisUnityBitPackerExtensionsUnsafe.ReadMusclesFromBytes(ref data, ref copyData, ref offset, nonFingerCount);

            // Read next 34 muscles as bytes
            byte[] fingerData = new byte[fingerCount];
            BasisUnityBitPackerExtensionsUnsafe.ReadBytes(ref data, ref fingerData, ref offset, fingerCount);

            float[] muscles = new float[LocalAvatarSyncMessage.StoredBones];

            // Decompress first 55 muscles
            for (int i = 0; i < nonFingerCount; i++)
            {
                muscles[i] = MuscleDecompress(copyData[i], BasisAvatarMuscleRange.MinMuscle[i], BasisAvatarMuscleRange.MaxMuscle[i]);
            }

            // Decompress next 34 finger muscles
            for (int i = 0; i < fingerCount; i++)
            {
                muscles[nonFingerCount + i] = FingerDecompress(fingerData[i], BasisAvatarMuscleRange.MinMuscle[nonFingerCount + i], BasisAvatarMuscleRange.MaxMuscle[nonFingerCount + i]);
            }

            return muscles;
        }

        public static float MuscleDecompress(ushort value, float minValue, float maxValue)
        {
            float normalized = value / FloatRangeDifference;
            return normalized * (maxValue - minValue) + minValue;
        }
        private const float ByteRangeDifference = byte.MaxValue;
        public static float FingerDecompress(byte value, float minValue, float maxValue)
        {
            float normalized = value / (float)ByteRangeDifference;
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
