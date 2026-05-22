using Basis.Network.Core;

public static partial class SerializableBasis
{
    public struct BasisP2PSignalMessage
    {
        public const int MaxTokenLength = 64;
        public const int PublicKeySize = 32;

        public ushort otherPlayerId;
        public string sessionToken;
        /// <summary>
        /// X25519 ephemeral public key of the sender, relayed by the server so the two
        /// peers can derive a per-pair key and always encrypt the direct (P2P) link.
        /// </summary>
        public byte[] ephemeralPublicKey;

        public void Deserialize(NetDataReader reader)
        {
            otherPlayerId = reader.GetUShort();
            sessionToken = reader.GetString(MaxTokenLength);
            byte hasKey = reader.GetByte();
            if (hasKey == 1 && reader.AvailableBytes >= PublicKeySize)
            {
                ephemeralPublicKey = new byte[PublicKeySize];
                reader.GetBytes(ephemeralPublicKey, PublicKeySize);
            }
            else
            {
                ephemeralPublicKey = null;
            }
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(otherPlayerId);
            writer.Put(sessionToken ?? string.Empty, MaxTokenLength);
            if (ephemeralPublicKey != null && ephemeralPublicKey.Length == PublicKeySize)
            {
                writer.Put((byte)1);
                writer.Put(ephemeralPublicKey);
            }
            else
            {
                writer.Put((byte)0);
            }
        }
    }
}
