using Basis.Network.Core;

public static partial class SerializableBasis
{
    [System.Serializable]
    public struct PubSubSubscribeRequest
    {
        public string ChannelName;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChannelName);
        }

        public bool Deserialize(NetDataReader reader)
        {
            return reader.TryGetString(out ChannelName);
        }
    }

    [System.Serializable]
    public struct PubSubUnsubscribeRequest
    {
        public string ChannelName;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChannelName);
        }

        public bool Deserialize(NetDataReader reader)
        {
            return reader.TryGetString(out ChannelName);
        }
    }

    [System.Serializable]
    public struct PubSubMessage
    {
        public string ChannelName;
        public byte[] Data;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChannelName);
            writer.PutBytesWithLength(Data);
        }

        public bool Deserialize(NetDataReader reader)
        {
            return reader.TryGetString(out ChannelName) && reader.TryGetBytesWithLength(out Data);
        }
    }
}
