using Basis.Network.Core;
public static partial class SerializableBasis
{
    public struct PlayerIdMessage
    {
        private ushort playerid;

        public void Deserialize(NetDataReader Writer)
        {
            Writer.Get(out playerid); // Read the entire ushort value
        }
        public void Serialize(NetDataWriter Writer)
        {
            Writer.Put(playerid); // Write the entire ushort value
        }
    }
}
