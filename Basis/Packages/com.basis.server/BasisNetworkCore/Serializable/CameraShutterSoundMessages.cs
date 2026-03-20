using Basis.Network.Core;

public static partial class SerializableBasis
{
    /// <summary>
    /// Server -> clients: a player took a photo. Includes position for spatial audio.
    /// Serialized after the EventType byte has already been written by the event router.
    /// </summary>
    public struct CameraShutterSoundMessage
    {
        public ushort PlayerID;
        public float PositionX;
        public float PositionY;
        public float PositionZ;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(PlayerID);
            writer.Put(PositionX);
            writer.Put(PositionY);
            writer.Put(PositionZ);
        }

        public void Deserialize(NetDataReader reader)
        {
            PlayerID = reader.GetUShort();
            PositionX = reader.GetFloat();
            PositionY = reader.GetFloat();
            PositionZ = reader.GetFloat();
        }
    }

    /// <summary>
    /// Client -> server: local player took a photo. Position for spatial audio on other clients.
    /// Serialized after the EventType byte has already been written by the sender.
    /// </summary>
    public struct ClientCameraShutterSoundMessage
    {
        public float PositionX;
        public float PositionY;
        public float PositionZ;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(PositionX);
            writer.Put(PositionY);
            writer.Put(PositionZ);
        }

        public void Deserialize(NetDataReader reader)
        {
            PositionX = reader.GetFloat();
            PositionY = reader.GetFloat();
            PositionZ = reader.GetFloat();
        }
    }
}
