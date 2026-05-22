using Basis.Network.Core;
using System;
using System.Text;

public static partial class SerializableBasis
{
    /// <summary>
    /// Client-to-server chat message. Contains UTF-8 encoded text.
    /// </summary>
    public struct ChatMessage
    {
        /// <summary>
        /// Maximum allowed message length in bytes.
        /// </summary>
        public const int MaxPayloadBytes = 512;

        /// <summary>
        /// The UTF-8 encoded chat message bytes.
        /// </summary>
        public byte[] payload;

        /// <summary>
        /// Length of the payload in bytes.
        /// </summary>
        public ushort payloadSize;

        /// <summary>
        /// Whether receivers should play their chat notification sound.
        /// </summary>
        public bool playNotificationSound;

        public void Deserialize(NetDataReader reader)
        {
            playNotificationSound = true;
            int payloadSizeWire = reader.GetUShort();
            int readSize = Math.Min(payloadSizeWire, MaxPayloadBytes);

            if (payloadSizeWire == 0)
            {
                payload = Array.Empty<byte>();
                payloadSize = 0;
            }
            else if (reader.AvailableBytes < readSize)
            {
                payload = Array.Empty<byte>();
                payloadSize = 0;
                reader.SkipBytes(Math.Min(payloadSizeWire, reader.AvailableBytes));
                return;
            }
            else
            {
                payloadSize = (ushort)readSize;
                payload = new byte[readSize];
                reader.GetBytes(payload, readSize);

                int excessSize = payloadSizeWire - readSize;
                if (excessSize > 0)
                {
                    reader.SkipBytes(Math.Min(excessSize, reader.AvailableBytes));
                }
            }

            if (reader.AvailableBytes > 0)
            {
                playNotificationSound = reader.GetBool();
            }
        }

        public void Serialize(NetDataWriter writer)
        {
            if (payload == null || payload.Length == 0)
            {
                writer.Put((ushort)0);
                writer.Put(playNotificationSound);
                return;
            }
            payloadSize = (ushort)Math.Min(payload.Length, MaxPayloadBytes);
            writer.Put(payloadSize);
            writer.Put(payload, 0, payloadSize);
            writer.Put(playNotificationSound);
        }
    }
}
