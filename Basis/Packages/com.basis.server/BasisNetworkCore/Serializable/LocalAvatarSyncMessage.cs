using Basis.Network.Core;
using Basis.Network.Core.Compression;
using static Basis.Network.Core.Compression.BasisBitPackingConstants;

public static partial class SerializableBasis
{
    public struct LocalAvatarSyncMessage
    {
        public byte DataQualityLevel;     // 0=Low, 1=Medium, 2=High
        public byte[] array;              // payload: position->scale->rotation->muscles

        // Only this struct can set it.
        public ushort PayloadLength { get; private set; }

        public AdditionalAvatarData[] AdditionalAvatarDatas;
        public byte AdditionalAvatarDataSize;
        public byte LinkedAvatarIndex;

        public LocalAvatarSyncMessage(byte[] array) : this()
        {
            this.array = array;
        }

        // Single source of truth for payload length.
        void RefreshPayloadLength()
        {
            var q = (BitQuality)DataQualityLevel;
            if (!BasisBitPackingConstants.IsValidQuality(q))
            {
                PayloadLength = 0;
                return;
            }

            PayloadLength = (ushort)BasisBitPackingConstants.ConvertToSize(q);
        }

        public void Deserialize(NetDataReader reader)
        {
            if (!reader.TryGetByte(out DataQualityLevel))
            {
                BNL.LogError("Missing DataQualityLevel!");
                return;
            }

            BitQuality q = (BitQuality)DataQualityLevel;
            if (!BasisBitPackingConstants.IsValidQuality(q))
            {
                BNL.LogError($"Invalid DataQualityLevel={DataQualityLevel}");
                return;
            }

            // Read length from stream, but store it only after validation.
            if (!reader.TryGetUShort(out ushort payloadLengthFromWire))
            {
                BNL.LogError("Missing PayloadLength!");
                return;
            }

            ushort expected = (ushort)BasisBitPackingConstants.ConvertToSize(q);
            if (payloadLengthFromWire != expected)
            {
                BNL.LogError($"PayloadLength mismatch. Got {payloadLengthFromWire}, expected {expected} for quality {q}.");
                return;
            }

            PayloadLength = expected;

            if (reader.AvailableBytes < PayloadLength)
            {
                BNL.LogError($"Unable to read avatar payload. Need {PayloadLength}, have {reader.AvailableBytes}.");
                return;
            }

            array ??= new byte[PayloadLength];
            if (array.Length != PayloadLength) array = new byte[PayloadLength];

            reader.GetBytes(array, PayloadLength);

            if (!reader.TryGetByte(out AdditionalAvatarDataSize))
            {
                BNL.LogError("Missing AdditionalAvatarDataSize!");
                return;
            }

            if (AdditionalAvatarDataSize == 0)
                return;

            if (!reader.TryGetByte(out LinkedAvatarIndex))
            {
                BNL.LogError("Missing LinkedAvatarIndex!");
                return;
            }

            AdditionalAvatarDatas = new AdditionalAvatarData[AdditionalAvatarDataSize];
            for (int i = 0; i < AdditionalAvatarDataSize; i++)
            {
                AdditionalAvatarDatas[i] = new AdditionalAvatarData();
                AdditionalAvatarDatas[i].Deserialize(reader);
            }
        }

        public void Serialize(NetDataWriter writer)
        {
            // Compute length here (no external code sets it)
            RefreshPayloadLength();

            writer.Put(DataQualityLevel);

            if (array == null || PayloadLength == 0)
            {
                BNL.LogError("array was null or PayloadLength was 0!!");
                writer.Put((ushort)0);
                writer.Put((byte)0); // AdditionalAvatarDataSize
                return;
            }

            if (PayloadLength > array.Length)
            {
                BNL.LogError($"PayloadLength ({PayloadLength}) > array.Length ({array.Length})");
                writer.Put((ushort)0);
                writer.Put((byte)0);
                return;
            }

            writer.Put(PayloadLength);

            // Write only the bytes we claim to have
            writer.Put(array, 0, PayloadLength);

            if (AdditionalAvatarDatas == null || AdditionalAvatarDatas.Length == 0 || AdditionalAvatarDatas.Length > 256)
            {
                writer.Put((byte)0);
                return;
            }

            AdditionalAvatarDataSize = (byte)AdditionalAvatarDatas.Length;
            writer.Put(AdditionalAvatarDataSize);
            writer.Put(LinkedAvatarIndex);

            for (int i = 0; i < AdditionalAvatarDataSize; i++)
                AdditionalAvatarDatas[i].Serialize(writer);
        }
    }
}
