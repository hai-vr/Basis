using Basis.Network.Core;
public static partial class SerializableBasis
{
    public struct ServerSideSyncPlayerMessage
    {
        public PlayerIdMessage playerIdMessage;
        public byte interval;
        public byte sequence;
        public LocalAvatarSyncMessage avatarSerialization;
        public void Deserialize(NetDataReader Writer)
        {
            playerIdMessage.Deserialize(Writer);//2bytes
            Writer.Get(out interval);//1 byte
            Writer.Get(out sequence);//1 byte
            avatarSerialization.Deserialize(Writer);
        }
        public void Serialize(NetDataWriter Writer)
        {
            playerIdMessage.Serialize(Writer);
            Writer.Put(interval);
            Writer.Put(sequence);
            avatarSerialization.Serialize(Writer, (Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality)avatarSerialization.DataQualityLevel);
        }
    }
}
