using System;
using Basis.Network.Core;

public static partial class SerializableBasis
{
    [Serializable]
    public struct PubSubSubscribeRequest
    {
        public string ChannelName;
        public Guid RequestID;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChannelName);
            writer.Put(RequestID);
        }

        public bool Deserialize(NetDataReader reader)
        {
            if (reader.TryGetString(out ChannelName) && reader.AvailableBytes >= 16)
            {
                RequestID = reader.GetGuid();
                return true;
            }
            return false;
        }
    }

    [Serializable]
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

    [Serializable]
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

    [Serializable]
    public struct PubSubInitialState
    {
        public string ChannelName;
        public byte[] Data;
        public Guid RequestID;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChannelName);
            writer.PutBytesWithLength(Data);
            writer.Put(RequestID);
        }

        public bool Deserialize(NetDataReader reader)
        {
            if (reader.TryGetString(out ChannelName) && reader.TryGetBytesWithLength(out Data) && reader.AvailableBytes >= 16)
            {
                RequestID = reader.GetGuid();
                return true;
            }
            return false;
        }
    }
}
