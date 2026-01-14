using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Basis.Scripts.Networking.Compression;
using BasisNetworkClientConsole;
using static BasisNetworkPrimitiveCompression;
using static SerializableBasis;

namespace Basis.Network
{
    public static class MovementSender
    {
        public static Quaternion Rotation = new Quaternion(0, 0, 0, 1);

        private const ushort UShortMin = ushort.MinValue;   // 0
        private const ushort UShortMax = ushort.MaxValue;   // 65535
        private const ushort UShortRangeDifference = UShortMax - UShortMin;

        public static Vector3[] PlayersCurrentPosition;
        public static PlayerData[] ActivePlayerData;

        public struct PlayerData
        {
            public NetDataWriter Writer;
            public LocalAvatarSyncMessage Message;
        }

        // Precompute compressed scale once; reused for all messages.
        private static readonly ushort CompressedScale = CompressScaleOnce(1);

        public static void Initialize(int clientCount)
        {
            PlayersCurrentPosition = new Vector3[clientCount];
            ActivePlayerData = new PlayerData[clientCount];

            for (int Index = 0; Index < clientCount; Index++)
            {
                PlayersCurrentPosition[Index] = Randomizer.GetRandomOffset();
                ActivePlayerData[Index] = Generate();
            }
        }
        public static PlayerData Generate()
        {
            var pd = new PlayerData
            {
                Writer = new NetDataWriter(),
                Message = new LocalAvatarSyncMessage
                {
                    AdditionalAvatarDatas = null,
                    AdditionalAvatarDataSize = 0,
                    LinkedAvatarIndex = 0,
                    array = new byte[BasisBitPackingConstants.AvatarSyncSize],
                }
            };

            int offset = 0;
            var message = pd.Message;
            // Position (12 bytes)
            WritePosition(Randomizer.GetRandomOffset(), ref message.array, ref offset);//12

            // Rotation xyz (12 bytes) + compressed w (2 bytes)
            WriteQuaternionToBytes(Rotation, ref message.array, ref offset);//16

            // Scale (2 bytes) at the end
            int scaleOffset = BasisBitPackingConstants.AvatarSyncSize - 2;
            WriteUShort(CompressedScale, ref message.array, ref scaleOffset);//2
            pd.Message = message;
            return pd;
        }

        public static void ProcessSingle(NetPeer peer, int index)
        {
            if (peer == null) return;

            // Update position
            PlayersCurrentPosition[index] += Randomizer.GetRandomOffset();

            // Overwrite just the position region in the message buffer
            int offset = 0;
            var Message = ActivePlayerData[index].Message;
            WritePosition(PlayersCurrentPosition[index], ref Message.array, ref offset);
            var Writer = ActivePlayerData[index].Writer;
            // Reset writer before (re)serialization
            Writer.Reset();
            Message.Serialize(Writer);


            peer.Send(Writer, BasisNetworkCommons.PlayerAvatarChannel, DeliveryMethod.Sequenced);

            ActivePlayerData[index].Message = Message;
        }

        public static void WritePosition(Scripts.Networking.Compression.Vector3 position, ref byte[] buffer, ref int offset)
        {
            unsafe
            {
                fixed (byte* dst = &buffer[offset])
                {
                    float* f = (float*)dst;
                    f[0] = position.x;
                    f[1] = position.y;
                    f[2] = position.z;
                }
            }
            offset += 12;
        }

        public unsafe static void WriteQuaternionToBytes(Quaternion q, ref byte[] bytes, ref int offset)
        {
            fixed (byte* ptr = &bytes[offset])
            {
                *((float*)ptr) = float.IsNaN(q.value.x) ? 0f : q.value.x;
                *((float*)(ptr + 4)) = float.IsNaN(q.value.y) ? 0f : q.value.y;
                *((float*)(ptr + 8)) = float.IsNaN(q.value.z) ? 0f : q.value.z;
                *((float*)(ptr + 12)) = float.IsNaN(q.value.w) ? 1f : q.value.w;
            }

            offset += 16; // 4 floats = 16 bytes
        }

        private static ushort CompressScaleOnce(float scale)
        {
            const float Min = 0.005f;
            const float Max = 150f;
            const float Range = Max - Min;
            float clamped = scale;//math.clamp(scale, Min, Max);
            // Normalized value of a uniform scale of 1.0 within [Min, Max]
            float normalized = (clamped - Min) / Range;
            ushort compressed = (ushort)(normalized * UShortRangeDifference);

            return compressed;
        }

        public static void WriteUShort(ushort value, ref byte[] bytes, ref int offset)
        {
            bytes[offset++] = (byte)value;
            bytes[offset++] = (byte)(value >> 8);
        }
    }
}
