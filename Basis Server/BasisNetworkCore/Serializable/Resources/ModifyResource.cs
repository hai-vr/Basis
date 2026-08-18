using Basis.Network.Core;
public static partial class SerializableBasis
{
    /// <summary>
    /// Client→server request to change a flag on an already-spawned resource, and the
    /// server→client broadcast that applies the change on every client. Carries the static-lock
    /// state: <see cref="Static"/> freezes the item for everyone; <see cref="StaticAdminLocked"/>
    /// marks it as the admin tier (only a moderator, not the creator, may change or clear it).
    /// </summary>
    public struct ModifyResource
    {
        /// <summary>Unique network id of the spawned resource to modify.</summary>
        public string LoadedNetID;
        /// <summary>0 = GameObject, 1 = Scene (matches <see cref="LocalLoadResource.Mode"/>).</summary>
        public byte Mode;
        /// <summary>Desired frozen state (pickup disabled + frozen for props, locked out for vehicles).</summary>
        public bool Static;
        /// <summary>Desired admin tier: when true only a moderator may change it, and it implies Static.</summary>
        public bool StaticAdminLocked;

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(LoadedNetID);
            writer.Put(Mode);
            writer.Put(Static);
            writer.Put(StaticAdminLocked);
        }
        public void Deserialize(NetDataReader reader)
        {
            LoadedNetID = reader.GetString();
            Mode = reader.GetByte();
            Static = reader.GetBool();
            StaticAdminLocked = reader.GetBool();
        }
    }
}
