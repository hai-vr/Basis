using LiteNetLib.Utils;

public static partial class SerializableBasis
{
    public struct LocalAvatarSyncMessage
    {
        public const int StoredBones = 89;
        public const int AvatarSyncSize = 204 + 2;

        public byte[] array; // 178 (muscles) + 12 (position) + 14 (rotation) = 204
        public AdditionalAvatarData[] AdditionalAvatarDatas;
        public byte AdditionalAvatarDataSize;
        public byte LinkedAvatarIndex;

        public void Deserialize(NetDataReader reader)
        {
            int availableBytes = reader.AvailableBytes;
            if (availableBytes < AvatarSyncSize)
            {
                BNL.LogError($"Insufficient bytes for LocalAvatarSyncMessage. Available: {availableBytes}, Required: {AvatarSyncSize}");
                return;
            }

            array ??= new byte[AvatarSyncSize];
            reader.GetBytes(array, AvatarSyncSize);

            if (!reader.TryGetByte(out AdditionalAvatarDataSize))
            {
                BNL.LogError("Missing AdditionalAvatarDataSize byte.");
                return;
            }

            if (AdditionalAvatarDataSize == 0)
            {
                AdditionalAvatarDatas = null;
                return;
            }

            if (!reader.TryGetByte(out LinkedAvatarIndex))
            {
                BNL.LogError("Missing LinkedAvatarIndex byte.");
                return;
            }

            AdditionalAvatarDatas = new AdditionalAvatarData[AdditionalAvatarDataSize];
            for (int i = 0; i < AdditionalAvatarDataSize; i++)
            {
                var data = new AdditionalAvatarData();
                data.Deserialize(reader); // assumes this internally handles failure
                AdditionalAvatarDatas[i] = data;
            }

            // BNL.Log($"Deserialized {AdditionalAvatarDataSize} additional avatar entries.");
        }

        public void Serialize(NetDataWriter writer)
        {
            if (array == null || array.Length != AvatarSyncSize)
            {
                BNL.LogError("Avatar sync array is null or of incorrect size.");
                return;
            }

            writer.Put(array);

            if (AdditionalAvatarDatas == null || AdditionalAvatarDatas.Length == 0)
            {
                writer.Put((byte)0);
                return;
            }

            int count = AdditionalAvatarDatas.Length;
            if (count > 255)
            {
                BNL.LogError("AdditionalAvatarDatas length exceeds byte limit (255). Truncating.");
                count = 255;
            }

            AdditionalAvatarDataSize = (byte)count;
            writer.Put(AdditionalAvatarDataSize);
            writer.Put(LinkedAvatarIndex);

            for (int i = 0; i < count; i++)
            {
                AdditionalAvatarDatas[i].Serialize(writer);
            }

            // BNL.Log($"Serialized {AdditionalAvatarDataSize} additional avatar entries.");
        }
    }
}
