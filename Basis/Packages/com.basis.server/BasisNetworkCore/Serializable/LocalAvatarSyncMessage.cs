using Basis.Network.Core;
using Basis.Network.Core.Compression;
using static Basis.Network.Core.Compression.BasisBitPackingConstants;

public static partial class SerializableBasis
{
    public struct LocalAvatarSyncMessage
    {
        public byte DataQualityLevel; // 0=Low, 1=Medium, 2=High (BitQuality enum)
        public byte[] array;          // position -> scale -> rotation -> muscles(bitpacked)

        public AdditionalAvatarData[] AdditionalAvatarDatas;
        public byte AdditionalAvatarDataSize;
        public byte LinkedAvatarIndex;

        public LocalAvatarSyncMessage(byte[] array) : this()
        {
            this.array = array;
        }

        public void Deserialize(NetDataReader reader)
        {
            // Read quality first
            if (!reader.TryGetByte(out DataQualityLevel))
            {
                BNL.LogError("Missing DataQualityLevel!");
                return;
            }

            // Validate enum range
            BitQuality q = (BitQuality)DataQualityLevel;
            if (!BasisBitPackingConstants.IsValidQuality(q))
            {
                BNL.LogError($"Invalid DataQualityLevel={DataQualityLevel}");
                return;
            }

            int avatarSyncSize = BasisBitPackingConstants.ConvertToSize(q);

            // AvailableBytes NOW is *after* reading DataQualityLevel
            int bytesRemaining = reader.AvailableBytes;
            if (bytesRemaining < avatarSyncSize)
            {
                BNL.LogError($"Unable to read avatar payload. Need {avatarSyncSize}, have {bytesRemaining}.");
                return;
            }

            array ??= new byte[avatarSyncSize];
            reader.GetBytes(array, avatarSyncSize);

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
            // Always write quality byte first
            writer.Put(DataQualityLevel);

            if (array == null)
            {
                BNL.LogError("array was null!!");
                writer.Put((byte)0);
                return;
            }

            writer.Put(array);

            if (AdditionalAvatarDatas == null || AdditionalAvatarDatas.Length == 0 || AdditionalAvatarDatas.Length > 256)
            {
                writer.Put((byte)0);
                return;
            }

            AdditionalAvatarDataSize = (byte)AdditionalAvatarDatas.Length;
            writer.Put(AdditionalAvatarDataSize);

            // only include linked avatar if there is additional data
            writer.Put(LinkedAvatarIndex);

            for (int i = 0; i < AdditionalAvatarDataSize; i++)
            {
                AdditionalAvatarDatas[i].Serialize(writer);
            }
        }
    }
}
