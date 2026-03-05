using System;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// XOR-based chunk delta compression for avatar byte payloads.
    /// Divides the payload into 8-byte chunks, emits a uint32 bitmask indicating
    /// which chunks changed, followed by only the XOR'd bytes of changed chunks.
    /// </summary>
    public static class AvatarDeltaCompression
    {
        public const int ChunkSize = 8;
        public const int BitmaskSize = 4; // uint32

        /// <summary>
        /// Encodes delta between current and baseline into deltaBuffer.
        /// Format: [bitmask:4][changed_chunk_0:8][changed_chunk_1:8]...
        /// Returns total bytes written. Returns BitmaskSize (4) if nothing changed.
        /// </summary>
        public static int EncodeDelta(byte[] current, byte[] baseline, byte[] deltaBuffer)
        {
            int payloadLen = current.Length;
            int numChunks = (payloadLen + ChunkSize - 1) / ChunkSize;

            uint bitmask = 0;
            int writePos = BitmaskSize; // skip bitmask header

            for (int chunk = 0; chunk < numChunks; chunk++)
            {
                int start = chunk * ChunkSize;
                int end = Math.Min(start + ChunkSize, payloadLen);

                bool changed = false;
                for (int i = start; i < end; i++)
                {
                    if (current[i] != baseline[i])
                    {
                        changed = true;
                        break;
                    }
                }

                if (changed)
                {
                    bitmask |= (1u << chunk);
                    for (int b = 0; b < ChunkSize; b++)
                    {
                        int i = start + b;
                        deltaBuffer[writePos++] = (i < payloadLen)
                            ? (byte)(current[i] ^ baseline[i])
                            : (byte)0; // pad partial last chunk
                    }
                }
            }

            // Write bitmask little-endian at start
            deltaBuffer[0] = (byte)(bitmask);
            deltaBuffer[1] = (byte)(bitmask >> 8);
            deltaBuffer[2] = (byte)(bitmask >> 16);
            deltaBuffer[3] = (byte)(bitmask >> 24);

            return writePos;
        }

        /// <summary>
        /// Maximum encoded delta size for a given payload length.
        /// </summary>
        public static int MaxDeltaSize(int payloadLength)
        {
            int numChunks = (payloadLength + ChunkSize - 1) / ChunkSize;
            return BitmaskSize + numChunks * ChunkSize;
        }

        /// <summary>
        /// Decodes a delta from raw bytes, XOR'ing with baseline into output.
        /// deltaData starts at deltaOffset: [bitmask:4][changed_chunks...]
        /// output must be at least baseline.Length.
        /// </summary>
        public static void DecodeDelta(byte[] deltaData, int deltaOffset, byte[] baseline, byte[] output)
        {
            int payloadLen = baseline.Length;
            Buffer.BlockCopy(baseline, 0, output, 0, payloadLen);

            uint bitmask = (uint)deltaData[deltaOffset]
                         | ((uint)deltaData[deltaOffset + 1] << 8)
                         | ((uint)deltaData[deltaOffset + 2] << 16)
                         | ((uint)deltaData[deltaOffset + 3] << 24);

            int readPos = deltaOffset + BitmaskSize;

            for (int chunk = 0; chunk < 32 && bitmask != 0; chunk++)
            {
                if ((bitmask & (1u << chunk)) == 0)
                    continue;

                int start = chunk * ChunkSize;
                for (int b = 0; b < ChunkSize; b++)
                {
                    byte xorByte = deltaData[readPos++];
                    int i = start + b;
                    if (i < payloadLen)
                        output[i] = (byte)(baseline[i] ^ xorByte);
                }
            }
        }

        /// <summary>
        /// Decodes a delta directly from a NetDataReader.
        /// Reads: [bitmask:4][changed_chunks:N*8]
        /// Applies XOR to baseline, writes result into output.
        /// </summary>
        public static void DecodeDelta(NetDataReader reader, byte[] baseline, byte[] output)
        {
            int payloadLen = baseline.Length;
            Buffer.BlockCopy(baseline, 0, output, 0, payloadLen);

            uint bitmask = reader.GetUInt();

            for (int chunk = 0; chunk < 32 && bitmask != 0; chunk++)
            {
                if ((bitmask & (1u << chunk)) == 0)
                    continue;

                int start = chunk * ChunkSize;
                for (int b = 0; b < ChunkSize; b++)
                {
                    byte xorByte = reader.GetByte();
                    int i = start + b;
                    if (i < payloadLen)
                        output[i] = (byte)(baseline[i] ^ xorByte);
                }
            }
        }
    }
}
