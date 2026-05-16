using Basis.Network.Core;

public static partial class SerializableBasis
{
    public struct BasisP2PSignalMessage
    {
        public const int MaxTokenLength = 64;

        public ushort otherPlayerId;
        public string sessionToken;

        public void Deserialize(NetDataReader reader)
        {
            otherPlayerId = reader.GetUShort();
            sessionToken = reader.GetString(MaxTokenLength);
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(otherPlayerId);
            writer.Put(sessionToken ?? string.Empty, MaxTokenLength);
        }
    }
}
