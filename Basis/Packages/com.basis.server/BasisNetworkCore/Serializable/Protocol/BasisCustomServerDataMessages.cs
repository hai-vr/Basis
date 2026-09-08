using System;
using Basis.Network.Core;

public static partial class SerializableBasis
{
    [Serializable]
    public struct CustomServerDataSubscribeRequest
    {
        public string ChannelPattern;
        public Guid RequestID;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChannelPattern);
            writer.Put(RequestID);
        }

        public bool Deserialize(NetDataReader reader)
        {
            if (reader.TryGetString(out ChannelPattern) && reader.AvailableBytes >= 16)
            {
                RequestID = reader.GetGuid();
                return true;
            }

            return false;
        }
    }

    [Serializable]
    public struct CustomServerDataUnsubscribeRequest
    {
        public string ChannelPattern;
        public Guid RequestID;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChannelPattern);
            writer.Put(RequestID);
        }

        public bool Deserialize(NetDataReader reader)
        {
            if (reader.TryGetString(out ChannelPattern) && reader.AvailableBytes >= 16)
            {
                RequestID = reader.GetGuid();
                return true;
            }

            return false;
        }
    }

    [Serializable]
    public struct CustomServerDataProvideChannelId
    {
        public string ChannelName;
        public ushort ChannelId;
        public Guid RequestID;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChannelName);
            writer.Put(ChannelId);
            writer.Put(RequestID);
        }

        public bool Deserialize(NetDataReader reader)
        {
            if (reader.TryGetString(out ChannelName) && reader.TryGetUShort(out ChannelId) && reader.AvailableBytes >= 16)
            {
                RequestID = reader.GetGuid();
                return true;
            }

            return false;
        }
    }

    [Serializable]
    public struct CustomServerDataMessage
    {
        public ushort ChannelId;
        public byte[] Data;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChannelId);
            writer.PutBytesWithLength(Data);
        }

        public bool Deserialize(NetDataReader reader)
        {
            return reader.TryGetUShort(out ChannelId) && reader.TryGetBytesWithLength(out Data);
        }
    }

    [Serializable]
    public struct CustomServerDataInitialState
    {
        public ushort ChannelId;
        public byte[] Data;
        public Guid RequestID;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(ChannelId);
            writer.PutBytesWithLength(Data);
            writer.Put(RequestID);
        }

        public bool Deserialize(NetDataReader reader)
        {
            if (reader.TryGetUShort(out ChannelId) && reader.TryGetBytesWithLength(out Data) && reader.AvailableBytes >= 16)
            {
                RequestID = reader.GetGuid();
                return true;
            }

            return false;
        }
    }
}