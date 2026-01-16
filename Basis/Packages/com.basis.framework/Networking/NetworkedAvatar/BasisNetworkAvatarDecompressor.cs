using Basis.Network.Core.Compression;
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
            {
                throw new ArgumentException("Cannot serialize avatar data.");
            }

            byte[] data = syncMessage.avatarSerialization.array;
            int length = data.Length;

            BasisAvatarBitPacking.BitQuality q = (BasisAvatarBitPacking.BitQuality)syncMessage.avatarSerialization.DataQualityLevel;
            int expected = BasisAvatarBitPacking.ConvertToSize(q);

            if (length >= expected)
            {
                int offset = 0;
                double interval = (double)BasisNetworkManagement.ServerMetaDataMessage.SyncInterval;
                if (TryCreateAvatarBuffer(data, ref offset, (interval + (double)syncMessage.interval) / 1000.0, q, out BasisAvatarBuffer avatarBuffer))
                {
                    EnqueueAndProcessAdditionalData(baseReceiver, avatarBuffer, syncMessage.avatarSerialization);
                }
            }
            else
            {
                BasisDebug.LogError("Data did not have enough for AvatarsyncMessage", BasisDebug.LogTag.Networking);
            }
        }

        public static void DecompressAndProcessAvatar(BasisNetworkReceiver baseReceiver, LocalAvatarSyncMessage avatarSerialization)
        {
            if (avatarSerialization.array == null)
            {
                throw new ArgumentException("Cannot serialize initial avatar data.");
            }

            byte[] data = avatarSerialization.array;
            int length = data.Length;

            BasisAvatarBitPacking.BitQuality q = (BasisAvatarBitPacking.BitQuality)avatarSerialization.DataQualityLevel;
            int expected = BasisAvatarBitPacking.ConvertToSize(q);

            if (length >= expected)
            {
                int offset = 0;
                if (TryCreateAvatarBuffer(data, ref offset, 0.01f, q, out BasisAvatarBuffer avatarBuffer))
                {
                    EnqueueAndProcessAdditionalData(baseReceiver, avatarBuffer, avatarSerialization);
                }
            }
            else
            {
                BasisDebug.LogError("Data did not have enough for AvatarsyncMessage", BasisDebug.LogTag.Networking);
            }
        }
        /// <summary>
        /// Creates A Avatar Buffer, removes NaN and infinite data
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="secondsInterval"></param>
        /// <param name="basisAvatarBuffer"></param>
        /// <returns></returns>
        private static bool TryCreateAvatarBuffer(byte[] data, ref int offset, double secondsInterval, BasisAvatarBitPacking.BitQuality quality, out BasisAvatarBuffer basisAvatarBuffer)
        {
            basisAvatarBuffer = null;
            int startOffset = offset;

            // Be tolerant: clamp instead of failing hard (unless you *know* it's corrupt).
            if (!math.isfinite(secondsInterval) || secondsInterval <= 0.0)
                goto Fail;

            // If your server truly never exceeds 1s, keep the cap but clamp instead of failing.
            secondsInterval = math.clamp(secondsInterval, 1e-3, 1.0);

            basisAvatarBuffer = BasisAvatarBufferPool.Get();

            // Position
            if (!BasisUnityBitPackerExtensionsUnsafe.TryReadPosition(ref data, ref offset, out basisAvatarBuffer.Position))
                goto Fail;

            BasisOrderedDataSet.DecompressAvatarMuscles_BitPacked(data, quality, ref basisAvatarBuffer.Muscles, ref offset);

            // Scale
            if (!BasisUnityBitPackerExtensionsUnsafe.TryReadUShort(ref data, ref offset, out ushort uScale))
                goto Fail;

            // Rotation
            if (!BasisUnityBitPackerExtensionsUnsafe.TryReadQuaternionFromBytes(ref data, ref offset, out basisAvatarBuffer.Rotation))
                goto Fail;

            basisAvatarBuffer.Scale = MuscleDecompress(uScale, MinimumValueSupported, MaximumValueSupported);
            basisAvatarBuffer.SecondsInterval = secondsInterval;
            return true;

        Fail:
            offset = startOffset;
            if (basisAvatarBuffer != null)
            {
                BasisAvatarBufferPool.Release(basisAvatarBuffer);
                basisAvatarBuffer = null;
            }
            BasisDebug.LogError($"non finite data found in Decompression Stage, bailing.", BasisDebug.LogTag.Remote);
            return false;
        }

        /// <summary>
        /// cant generate a nan unless min,max or floatrangedifference go bad (const cant)
        /// </summary>
        /// <param name="value"></param>
        /// <param name="minValue"></param>
        /// <param name="maxValue"></param>
        /// <returns></returns>
        public static float MuscleDecompress(ushort value, float minValue, float maxValue)
        {
            float normalized = value / FloatRangeDifference;
            return normalized * (maxValue - minValue) + minValue;
        }

        private static void EnqueueAndProcessAdditionalData(BasisNetworkReceiver baseReceiver, BasisAvatarBuffer avatarBuffer, LocalAvatarSyncMessage message)
        {
            baseReceiver.EnQueueAvatarBuffer(avatarBuffer);

            // (rest unchanged)
            if (message.AdditionalAvatarDataSize > 0 && message.AdditionalAvatarDatas != null)
            {
                bool isDifferentAvatar = message.LinkedAvatarIndex != baseReceiver.LastLinkedAvatarIndex;
                if (isDifferentAvatar) return;

                for (int Index = 0; Index < message.AdditionalAvatarDataSize; Index++)
                {
                    AdditionalAvatarData data = message.AdditionalAvatarDatas[Index];
                    if (data.messageIndex < baseReceiver.NetworkBehaviourCount)
                        baseReceiver.NetworkBehaviours[data.messageIndex].OnNetworkMessageServerReductionSystem(data.array);
                }
            }
        }

    }
}
