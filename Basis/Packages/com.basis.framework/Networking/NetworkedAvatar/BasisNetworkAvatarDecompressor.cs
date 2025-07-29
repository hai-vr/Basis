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
                int Interval = BasisNetworkManagement.ServerMetaDataMessage.SyncInterval;
                BasisAvatarBuffer avatarBuffer = CreateAvatarBuffer(data, ref offset, baseReceiver, (Interval + syncMessage.interval) / 1000f);
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

            float scale = Decompress(
                BasisUnityBitPackerExtensionsUnsafe.ReadUShort(ref data, ref offset),
                MinimumValueSupported,
                MaximumValueSupported
            );

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
            BasisUnityBitPackerExtensionsUnsafe.ReadMusclesFromBytes(ref data, ref copyData, ref offset);
            float[] muscles = new float[LocalAvatarSyncMessage.StoredBones];

            for (int Index = 0; Index < LocalAvatarSyncMessage.StoredBones; Index++)
            {
                muscles[Index] = Decompress(copyData[Index], BasisAvatarMuscleRange.MinMuscle[Index], BasisAvatarMuscleRange.MaxMuscle[Index]);
            }

            return muscles;
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
        public static float Decompress(ushort value, float minValue, float maxValue)
        {
            float normalized = value / FloatRangeDifference;
            return normalized * (maxValue - minValue) + minValue;
        }
    }
}
