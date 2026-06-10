using Basis.Network.Core;
public static partial class SerializableBasis
{
    /// <summary>
    /// Client→server request to change a flag on an already-spawned resource, and the
    /// server→client broadcast that applies the change on every client. Currently carries
    /// the "Static" flag (pickup disabled + frozen in place for props, locked out for vehicles).
    /// </summary>
    public struct ModifyResource
    {
        /// <summary>Unique network id of the spawned resource to modify.</summary>
        public string LoadedNetID;
        /// <summary>0 = GameObject, 1 = Scene (matches <see cref="LocalLoadResource.Mode"/>).</summary>
        public byte Mode;
        /// <summary>Desired static state.</summary>
        public bool Static;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(LoadedNetID);
            writer.Put(Mode);
            writer.Put(Static);
        }
        public void Deserialize(NetDataReader reader)
        {
            LoadedNetID = reader.GetString();
            Mode = reader.GetByte();
            Static = reader.GetBool();
        }
    }
}
