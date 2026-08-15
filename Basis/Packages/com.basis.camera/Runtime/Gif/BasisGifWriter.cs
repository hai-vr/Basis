using System;
using System.IO;

namespace Basis
{
    /// <summary>
    /// Streams an animated GIF89a: the header and looping extension once, then one image per
    /// frame — its own local colour table, display delay and GIF-flavoured LZW data — and a
    /// trailer on <see cref="Finish"/>. Frames go straight to the stream as they arrive, so a
    /// recording never holds more than the frame being encoded. Pure C# and pure data — safe to
    /// drive from the encode worker.
    /// </summary>
    public sealed class BasisGifWriter
    {
        /// <summary>
        /// Shortest frame delay written, in hundredths of a second. Browsers quietly treat
        /// anything under this as 10cs — the historic 0-delay convention — which would turn a
        /// fast frame into a slow one.
        /// </summary>
        public const int MinDelayCentiseconds = 2;

        private readonly Stream stream;
        private readonly int width;
        private readonly int height;
        private bool finished;

        public int FrameCount { get; private set; }

        public BasisGifWriter(Stream stream, int width, int height, bool loop)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (width <= 0 || width > ushort.MaxValue || height <= 0 || height > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width), $"GIF size {width}x{height} is outside 1-65535.");
            this.width = width;
            this.height = height;

            stream.Write(HeaderBytes, 0, HeaderBytes.Length);
            WriteUShort(width);
            WriteUShort(height);
            // No global colour table; colour resolution advertised as 8 bits per channel.
            stream.WriteByte(0x70);
            stream.WriteByte(0);
            stream.WriteByte(0);

            if (loop)
            {
                stream.Write(NetscapeLoopForever, 0, NetscapeLoopForever.Length);
            }
        }

        private static readonly byte[] HeaderBytes = { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' };

        /// <summary>Application extension declaring infinite repeat, the form every decoder honours.</summary>
        private static readonly byte[] NetscapeLoopForever =
        {
            0x21, 0xFF, 0x0B,
            (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E',
            (byte)'2', (byte)'.', (byte)'0',
            0x03, 0x01, 0x00, 0x00,
            0x00,
        };

        /// <summary>
        /// Appends one full frame. <paramref name="indices"/> is one palette index per pixel,
        /// row-major, top row first; <paramref name="paletteRgb"/> holds
        /// <paramref name="paletteCount"/> RGB triplets. The delay is how long this frame shows.
        /// </summary>
        public void WriteFrame(byte[] indices, byte[] paletteRgb, int paletteCount, int delayCentiseconds)
        {
            if (finished) throw new InvalidOperationException("Writer already finished.");
            if (indices == null || paletteRgb == null) throw new ArgumentNullException(indices == null ? nameof(indices) : nameof(paletteRgb));
            int pixelCount = width * height;
            if (indices.Length < pixelCount) throw new ArgumentException("Fewer indices than pixels.", nameof(indices));
            if (paletteCount < 1 || paletteCount > 256 || paletteRgb.Length < paletteCount * 3)
                throw new ArgumentOutOfRangeException(nameof(paletteCount));

            // The table is stored at a power-of-two size; the spec's floor is two entries.
            int tableBits = 1;
            while ((1 << tableBits) < paletteCount) tableBits++;
            int tableSize = 1 << tableBits;

            int delay = Math.Min(Math.Max(delayCentiseconds, MinDelayCentiseconds), ushort.MaxValue);

            // Graphic control: keep the frame up (disposal 1), no transparency.
            stream.WriteByte(0x21);
            stream.WriteByte(0xF9);
            stream.WriteByte(0x04);
            stream.WriteByte(0x04);
            WriteUShort(delay);
            stream.WriteByte(0);
            stream.WriteByte(0);

            // Image descriptor with a local colour table.
            stream.WriteByte(0x2C);
            WriteUShort(0);
            WriteUShort(0);
            WriteUShort(width);
            WriteUShort(height);
            stream.WriteByte((byte)(0x80 | (tableBits - 1)));

            stream.Write(paletteRgb, 0, paletteCount * 3);
            for (int Pad = paletteCount * 3; Pad < tableSize * 3; Pad++) stream.WriteByte(0);

            int minCodeSize = Math.Max(2, tableBits);
            stream.WriteByte((byte)minCodeSize);
            EncodePixels(indices, pixelCount, minCodeSize);
            stream.WriteByte(0);

            FrameCount++;
        }

        /// <summary>Writes the trailer. The stream itself stays open — its owner closes it.</summary>
        public void Finish()
        {
            if (finished) return;
            finished = true;
            stream.WriteByte(0x3B);
        }

        private void WriteUShort(int value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }

        // ---- GIF LZW ---------------------------------------------------------------------
        // The classic compress() shape: an open-addressed hash of (prefix, pixel) pairs, codes
        // packed least-significant-bit first, the code width growing as the table fills and a
        // clear code resetting it when it is full. The width bookkeeping runs inside EmitCode,
        // keyed off nextCode at the moment of emit, which is the convention every decoder
        // mirrors — moving it anywhere else desynchronises the two.

        private const int MaxCodeBits = 12;
        private const int CodeLimit = 1 << MaxCodeBits;
        private const int HashSize = 5003;
        private const int HashShift = 4;

        private readonly int[] hashKeys = new int[HashSize];
        private readonly int[] hashCodes = new int[HashSize];
        private readonly byte[] block = new byte[255];
        private int blockLength;
        private int bitBuffer;
        private int bitCount;

        private int codeBits;
        private int maxCode;
        private int nextCode;
        private int clearCode;
        private int initialCodeBits;
        private bool clearPending;

        private void EncodePixels(byte[] indices, int pixelCount, int minCodeSize)
        {
            clearCode = 1 << minCodeSize;
            int endCode = clearCode + 1;
            initialCodeBits = minCodeSize + 1;
            codeBits = initialCodeBits;
            maxCode = (1 << codeBits) - 1;
            nextCode = clearCode + 2;
            clearPending = false;
            bitBuffer = 0;
            bitCount = 0;
            blockLength = 0;
            Array.Fill(hashKeys, -1);

            EmitCode(clearCode);

            int prefix = indices[0];
            for (int Pixel = 1; Pixel < pixelCount; Pixel++)
            {
                int pixel = indices[Pixel];
                int packed = (pixel << MaxCodeBits) + prefix;
                int slot = (pixel << HashShift) ^ prefix;

                bool found = false;
                if (hashKeys[slot] == packed)
                {
                    found = true;
                }
                else if (hashKeys[slot] >= 0)
                {
                    int stride = slot == 0 ? 1 : HashSize - slot;
                    do
                    {
                        slot -= stride;
                        if (slot < 0) slot += HashSize;
                        if (hashKeys[slot] == packed)
                        {
                            found = true;
                            break;
                        }
                    }
                    while (hashKeys[slot] >= 0);
                }

                if (found)
                {
                    prefix = hashCodes[slot];
                    continue;
                }

                EmitCode(prefix);

                if (nextCode < CodeLimit)
                {
                    hashCodes[slot] = nextCode++;
                    hashKeys[slot] = packed;
                }
                else
                {
                    Array.Fill(hashKeys, -1);
                    nextCode = clearCode + 2;
                    clearPending = true;
                    EmitCode(clearCode);
                }

                prefix = pixel;
            }

            EmitCode(prefix);
            EmitCode(endCode);

            while (bitCount > 0)
            {
                EmitByte((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitCount -= 8;
            }
            FlushBlock();
        }

        private void EmitCode(int code)
        {
            bitBuffer |= code << bitCount;
            bitCount += codeBits;
            while (bitCount >= 8)
            {
                EmitByte((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitCount -= 8;
            }

            if (nextCode > maxCode || clearPending)
            {
                if (clearPending)
                {
                    codeBits = initialCodeBits;
                    maxCode = (1 << codeBits) - 1;
                    clearPending = false;
                }
                else
                {
                    codeBits++;
                    maxCode = codeBits == MaxCodeBits ? CodeLimit : (1 << codeBits) - 1;
                }
            }
        }

        private void EmitByte(byte value)
        {
            block[blockLength++] = value;
            if (blockLength == 255) FlushBlock();
        }

        private void FlushBlock()
        {
            if (blockLength == 0) return;
            stream.WriteByte((byte)blockLength);
            stream.Write(block, 0, blockLength);
            blockLength = 0;
        }
    }
}
