using Basis.Scripts.Networking.Compression;
using Basis.Scripts.Networking.Receivers;
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
                throw new ArgumentException("Cannot serialize avatar data.");

            byte[] data = syncMessage.avatarSerialization.array;
            int length = data.Length;

            if (length >= LocalAvatarSyncMessage.AvatarSyncSize)
            {
                int offset = 0;
                double interval = (double)BasisNetworkManagement.ServerMetaDataMessage.SyncInterval;
                BasisAvatarBuffer avatarBuffer = CreateAvatarBuffer(data, ref offset, (interval + (double)syncMessage.interval) / 1000.0);
                EnqueueAndProcessAdditionalData(baseReceiver, avatarBuffer, syncMessage.avatarSerialization);
            }
            else
            {
                BasisDebug.LogError("Data did not have enough for AvatarsyncMessage", BasisDebug.LogTag.Networking);
            }
        }

        public static void DecompressAndProcessAvatar(BasisNetworkReceiver baseReceiver, LocalAvatarSyncMessage avatarSerialization)
        {
            if (avatarSerialization.array == null)
                throw new ArgumentException("Cannot serialize initial avatar data.");

            byte[] data = avatarSerialization.array;
            int length = data.Length;

            if (length >= LocalAvatarSyncMessage.AvatarSyncSize)
            {
                int offset = 0;
                BasisAvatarBuffer avatarBuffer = CreateAvatarBuffer(data, ref offset, 0.01f);
                EnqueueAndProcessAdditionalData(baseReceiver, avatarBuffer, avatarSerialization);
            }
            else
            {
                BasisDebug.LogError("Data did not have enough for AvatarsyncMessage", BasisDebug.LogTag.Networking);
            }
        }

        private static BasisAvatarBuffer CreateAvatarBuffer(byte[] data, ref int offset, double secondsInterval)
        {
            BasisAvatarBuffer buffer = BasisAvatarBufferPool.Get();

            buffer.Position = BasisUnityBitPackerExtensionsUnsafe.ReadPosition(ref data, ref offset);
            if (!IsFinite(buffer.Position))
                buffer.Position = new Unity.Mathematics.float3(0, 0, 0);

            buffer.Rotation = SanitizeRotation(
                BasisUnityBitPackerExtensionsUnsafe.ReadQuaternionFromBytes(ref data, BasisNetworkPlayer.RotationCompression, ref offset)
            );

            // MUSCLES: bitpacked decode
            BasisOrderedDataSet.DecompressAvatarMuscles_BitPacked(data, ref buffer.Muscles, ref offset);

            buffer.Scale = MuscleDecompress(BasisUnityBitPackerExtensionsUnsafe.ReadUShort(ref data, ref offset), MinimumValueSupported, MaximumValueSupported);

            if (!math.isfinite((float)secondsInterval) || secondsInterval <= 0)
                buffer.SecondsInterval = 1.0 / 60.0;
            else
                buffer.SecondsInterval = secondsInterval;

            return buffer;
        }

        private static quaternion SanitizeRotation(quaternion q)
        {
            if (!math.isfinite(q.value.x) || !math.isfinite(q.value.y) || !math.isfinite(q.value.z) || !math.isfinite(q.value.w))
                return quaternion.identity;

            float magSq = q.value.x * q.value.x + q.value.y * q.value.y + q.value.z * q.value.z + q.value.w * q.value.w;
            if (magSq < 1e-8f) return quaternion.identity;

            return math.normalize(q);
        }

        private static bool IsFinite(Unity.Mathematics.float3 v) => math.isfinite(v.x) && math.isfinite(v.y) && math.isfinite(v.z);

        public static float MuscleDecompress(ushort value, float minValue, float maxValue)
        {
            float normalized = value / FloatRangeDifference;
            return normalized * (maxValue - minValue) + minValue;
        }

        private static void EnqueueAndProcessAdditionalData(BasisNetworkReceiver baseReceiver, BasisAvatarBuffer avatarBuffer, LocalAvatarSyncMessage message)
        {
            baseReceiver.EnQueueAvatarBuffer(avatarBuffer);

            if (message.AdditionalAvatarDataSize > 0 && message.AdditionalAvatarDatas != null)
            {
                bool isDifferentAvatar = message.LinkedAvatarIndex != baseReceiver.LastLinkedAvatarIndex;
                if (isDifferentAvatar) return;

                for (int i = 0; i < message.AdditionalAvatarDataSize; i++)
                {
                    AdditionalAvatarData data = message.AdditionalAvatarDatas[i];
                    if (data.messageIndex < baseReceiver.NetworkBehaviourCount)
                        baseReceiver.NetworkBehaviours[data.messageIndex].OnNetworkMessageServerReductionSystem(data.array);
                }
            }
        }
    }
}
