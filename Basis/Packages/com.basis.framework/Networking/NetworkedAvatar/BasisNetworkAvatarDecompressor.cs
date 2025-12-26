using Basis.Scripts.Networking.Compression;
using Basis.Scripts.Networking.Receivers;
using System;
using Unity.Collections;
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
            int length = data.Length;
            if (length >= LocalAvatarSyncMessage.AvatarSyncSize)
            {
                int offset = 0;
                double Interval = (double)BasisNetworkManagement.ServerMetaDataMessage.SyncInterval;// Interval + syncMessage.interval
                BasisAvatarBuffer avatarBuffer = CreateAvatarBuffer(data, ref offset, (double)(Interval + (double)syncMessage.interval) / 1000f);
                EnqueueAndProcessAdditionalData(baseReceiver, avatarBuffer, syncMessage.avatarSerialization);
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

        private static BasisAvatarBuffer CreateAvatarBuffer(byte[] data, ref int offset, double SecondsInterval)
        {
            BasisAvatarBuffer Buffer = BasisAvatarBufferPool.Get();
            Buffer.Position = BasisUnityBitPackerExtensionsUnsafe.ReadPosition(ref data, ref offset);
            if (!IsFinite(Buffer.Position))
            {
                // If position broke, keep last known good (0 if none)
                Buffer.Position = new Unity.Mathematics.float3(0, 0, 0);
            }
            Buffer.Rotation = SanitizeRotation(BasisUnityBitPackerExtensionsUnsafe.ReadQuaternionFromBytes(ref data, BasisNetworkPlayer.RotationCompression, ref offset));
            BasisOrderedDataSet.DecompressAvatarMuscles_NoLoop(data, ref Buffer.Muscles, ref offset);
            Buffer.Scale = MuscleDecompress(BasisUnityBitPackerExtensionsUnsafe.ReadUShort(ref data, ref offset), MinimumValueSupported, MaximumValueSupported);

            // Seconds interval — clamp to a sane minimum so interpolation works
            if (!math.isfinite((float)SecondsInterval) || SecondsInterval <= 0)
            {
                Buffer.SecondsInterval = 1.0 / 60.0;
            }
            else
            {
                Buffer.SecondsInterval = SecondsInterval;
            }
            return Buffer;
        }
        private static quaternion SanitizeRotation(quaternion q)
        {
            // If not finite or nearly zero length, use identity
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

            // Queue the avatar buffer
            baseReceiver.EnQueueAvatarBuffer(avatarBuffer);

            // Process additional avatar data
            if (message.AdditionalAvatarDataSize > 0 && message.AdditionalAvatarDatas != null)
            {
                bool isDifferentAvatar = message.LinkedAvatarIndex != baseReceiver.LastLinkedAvatarIndex;
                if (isDifferentAvatar)
                {
                    return;
                }

                for (int Index = 0; Index < message.AdditionalAvatarDataSize; Index++)
                {
                    AdditionalAvatarData data = message.AdditionalAvatarDatas[Index];

                    if (data.messageIndex < baseReceiver.NetworkBehaviourCount)
                    {
                        baseReceiver.NetworkBehaviours[data.messageIndex].OnNetworkMessageServerReductionSystem(data.array);
                    }
                }
            }
        }
    }
}
