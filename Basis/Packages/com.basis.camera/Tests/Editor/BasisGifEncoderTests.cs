using System;
using System.Collections.Generic;
using System.IO;
using Basis;
using NUnit.Framework;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The GIF encoder is bytes in, bytes out, so it is tested the only way that means anything:
    /// a decoder. The LZW round trip proves the variable-width code bookkeeping against an
    /// independent implementation of the decoder's rules — the two drift apart on the very first
    /// frame if either side grows its code width at the wrong moment.
    /// </summary>
    public class BasisGifEncoderTests
    {
        // ---- writer structure ----------------------------------------------------------

        [Test]
        public void HeaderTrailerAndLoopExtensionArePresent()
        {
            byte[] file = EncodeFrames(loop: true, MakeFrame(seed: 1));

            Assert.That(System.Text.Encoding.ASCII.GetString(file, 0, 6), Is.EqualTo("GIF89a"));
            Assert.That(file[file.Length - 1], Is.EqualTo(0x3B));

            ParsedGif parsed = ParsedGif.Parse(file);
            Assert.That(parsed.HasNetscapeLoop, Is.True);
        }

        [Test]
        public void NoLoopMeansNoNetscapeExtension()
        {
            byte[] file = EncodeFrames(loop: false, MakeFrame(seed: 2));
            Assert.That(ParsedGif.Parse(file).HasNetscapeLoop, Is.False);
        }

        [Test]
        public void EveryFrameSurvivesTheLzwRoundTrip()
        {
            byte[] frameA = MakeFrame(seed: 3);
            byte[] frameB = MakeFrame(seed: 4);
            byte[] file = EncodeFrames(loop: true, frameA, frameB);

            ParsedGif parsed = ParsedGif.Parse(file);
            Assert.That(parsed.FrameIndices.Count, Is.EqualTo(2));
            Assert.That(parsed.Width, Is.EqualTo(FrameWidth));
            Assert.That(parsed.Height, Is.EqualTo(FrameHeight));
            Assert.That(parsed.FrameIndices[0], Is.EqualTo(frameA));
            Assert.That(parsed.FrameIndices[1], Is.EqualTo(frameB));
        }

        [Test]
        public void RandomIndicesBigEnoughToForceATableClearRoundTrip()
        {
            // Random pairs almost never repeat, so the code table hits its 4096-entry limit and
            // the encoder has to emit a mid-stream clear — the path a short frame never takes.
            var random = new System.Random(99);
            byte[] indices = new byte[200 * 200];
            for (int Index = 0; Index < indices.Length; Index++) indices[Index] = (byte)random.Next(256);

            var stream = new MemoryStream();
            var writer = new BasisGifWriter(stream, 200, 200, false);
            writer.WriteFrame(indices, FullPalette(), 256, 5);
            writer.Finish();

            ParsedGif parsed = ParsedGif.Parse(stream.ToArray());
            Assert.That(parsed.FrameIndices[0], Is.EqualTo(indices));
        }

        [Test]
        public void TinyPalettesRoundTripAtTheMinimumCodeSize()
        {
            byte[] indices = new byte[FrameWidth * FrameHeight];
            for (int Index = 0; Index < indices.Length; Index++) indices[Index] = (byte)(Index % 2);

            var stream = new MemoryStream();
            var writer = new BasisGifWriter(stream, FrameWidth, FrameHeight, false);
            writer.WriteFrame(indices, FullPalette(), 2, 10);
            writer.Finish();

            ParsedGif parsed = ParsedGif.Parse(stream.ToArray());
            Assert.That(parsed.FrameIndices[0], Is.EqualTo(indices));
        }

        [Test]
        public void DelaysAreWrittenAndTheBrowserFloorIsApplied()
        {
            byte[] file = EncodeFrames(loop: false, new[] { 0, 12 }, MakeFrame(seed: 5), MakeFrame(seed: 6));

            ParsedGif parsed = ParsedGif.Parse(file);
            Assert.That(parsed.Delays[0], Is.EqualTo(BasisGifWriter.MinDelayCentiseconds),
                "A zero delay must be floored — browsers read tiny delays as ten times slower.");
            Assert.That(parsed.Delays[1], Is.EqualTo(12));
        }

        // ---- quantizer -----------------------------------------------------------------

        [Test]
        public void AFrameWithFewColorsReproducesThemExactly()
        {
            const int Width = 8, Height = 8;
            byte[] rgba = new byte[Width * Height * 4];
            for (int Pixel = 0; Pixel < Width * Height; Pixel++)
            {
                bool red = Pixel < Width * Height / 2;
                rgba[Pixel * 4] = (byte)(red ? 200 : 10);
                rgba[Pixel * 4 + 1] = 30;
                rgba[Pixel * 4 + 2] = (byte)(red ? 20 : 220);
                rgba[Pixel * 4 + 3] = 255;
            }

            var quantizer = new BasisGifQuantizer();
            byte[] indices = new byte[Width * Height];
            quantizer.Quantize(rgba, Width, Height, dither: false, indices);

            Assert.That(quantizer.PaletteCount, Is.EqualTo(2));
            for (int Pixel = 0; Pixel < Width * Height; Pixel++)
            {
                int index = indices[Pixel];
                Assert.That(quantizer.PaletteRgb[index * 3], Is.EqualTo(rgba[Pixel * 4]));
                Assert.That(quantizer.PaletteRgb[index * 3 + 1], Is.EqualTo(rgba[Pixel * 4 + 1]));
                Assert.That(quantizer.PaletteRgb[index * 3 + 2], Is.EqualTo(rgba[Pixel * 4 + 2]));
            }
        }

        [Test]
        public void NoisyFramesStayInsideTheGifLimits([Values(false, true)] bool dither)
        {
            const int Width = 64, Height = 64;
            var random = new System.Random(7);
            byte[] rgba = new byte[Width * Height * 4];
            random.NextBytes(rgba);

            var quantizer = new BasisGifQuantizer();
            byte[] indices = new byte[Width * Height];
            quantizer.Quantize(rgba, Width, Height, dither, indices);

            Assert.That(quantizer.PaletteCount, Is.LessThanOrEqualTo(BasisGifQuantizer.MaxColors));
            Assert.That(quantizer.PaletteCount, Is.GreaterThan(0));
            for (int Pixel = 0; Pixel < indices.Length; Pixel++)
            {
                Assert.That(indices[Pixel], Is.LessThan(quantizer.PaletteCount),
                    $"Pixel {Pixel} maps outside the palette.");
            }
        }

        [Test]
        public void ReusingTheQuantizerAcrossFramesDoesNotLeakTheOldPalette()
        {
            const int Width = 16, Height = 16;
            var quantizer = new BasisGifQuantizer();
            byte[] indices = new byte[Width * Height];

            byte[] greenish = SolidFrame(Width, Height, 20, 220, 40);
            quantizer.Quantize(greenish, Width, Height, dither: true, indices);

            byte[] reddish = SolidFrame(Width, Height, 230, 25, 25);
            quantizer.Quantize(reddish, Width, Height, dither: true, indices);

            int index = indices[0] * 3;
            Assert.That(quantizer.PaletteRgb[index], Is.EqualTo(230));
            Assert.That(quantizer.PaletteRgb[index + 1], Is.EqualTo(25));
            Assert.That(quantizer.PaletteRgb[index + 2], Is.EqualTo(25));
        }

        // ---- helpers -------------------------------------------------------------------

        private const int FrameWidth = 21;
        private const int FrameHeight = 13;

        private static byte[] MakeFrame(int seed)
        {
            var random = new System.Random(seed);
            byte[] indices = new byte[FrameWidth * FrameHeight];
            for (int Index = 0; Index < indices.Length; Index++) indices[Index] = (byte)random.Next(64);
            return indices;
        }

        private static byte[] FullPalette()
        {
            byte[] palette = new byte[256 * 3];
            for (int Index = 0; Index < 256; Index++)
            {
                palette[Index * 3] = (byte)Index;
                palette[Index * 3 + 1] = (byte)(255 - Index);
                palette[Index * 3 + 2] = (byte)(Index * 7);
            }
            return palette;
        }

        private static byte[] SolidFrame(int width, int height, byte r, byte g, byte b)
        {
            byte[] rgba = new byte[width * height * 4];
            for (int Pixel = 0; Pixel < width * height; Pixel++)
            {
                rgba[Pixel * 4] = r;
                rgba[Pixel * 4 + 1] = g;
                rgba[Pixel * 4 + 2] = b;
                rgba[Pixel * 4 + 3] = 255;
            }
            return rgba;
        }

        private static byte[] EncodeFrames(bool loop, params byte[][] frames) =>
            EncodeFrames(loop, null, frames);

        private static byte[] EncodeFrames(bool loop, int[] delays, params byte[][] frames)
        {
            var stream = new MemoryStream();
            var writer = new BasisGifWriter(stream, FrameWidth, FrameHeight, loop);
            for (int Frame = 0; Frame < frames.Length; Frame++)
            {
                writer.WriteFrame(frames[Frame], FullPalette(), 64, delays != null ? delays[Frame] : 4);
            }
            writer.Finish();
            return stream.ToArray();
        }

        /// <summary>
        /// A deliberately independent GIF reader: block walking, local colour tables, and a
        /// from-the-spec LZW decoder. Written from the decoder's side of the format so the
        /// encoder is checked against the rules a viewer applies, not against itself.
        /// </summary>
        public sealed class ParsedGif
        {
            public int Width;
            public int Height;
            public bool HasNetscapeLoop;
            public readonly List<int> Delays = new List<int>();
            public readonly List<byte[]> FrameIndices = new List<byte[]>();
            public readonly List<byte[]> Palettes = new List<byte[]>();

            public static ParsedGif Parse(byte[] data)
            {
                var parsed = new ParsedGif();
                Assert.That(System.Text.Encoding.ASCII.GetString(data, 0, 6), Is.EqualTo("GIF89a"));

                int pos = 6;
                parsed.Width = data[pos] | (data[pos + 1] << 8);
                parsed.Height = data[pos + 2] | (data[pos + 3] << 8);
                int packed = data[pos + 4];
                pos += 7;
                if ((packed & 0x80) != 0) pos += 3 * (1 << ((packed & 0x07) + 1));

                int pendingDelay = 0;
                while (true)
                {
                    byte block = data[pos++];
                    if (block == 0x3B) break;

                    if (block == 0x21)
                    {
                        byte label = data[pos++];
                        if (label == 0xF9)
                        {
                            int size = data[pos++];
                            Assert.That(size, Is.EqualTo(4));
                            pendingDelay = data[pos + 1] | (data[pos + 2] << 8);
                            pos += size;
                            Assert.That(data[pos++], Is.EqualTo(0), "Graphic control missing its terminator.");
                        }
                        else
                        {
                            bool first = true;
                            int size;
                            while ((size = data[pos++]) != 0)
                            {
                                if (first && label == 0xFF && size == 11 &&
                                    System.Text.Encoding.ASCII.GetString(data, pos, 11) == "NETSCAPE2.0")
                                {
                                    parsed.HasNetscapeLoop = true;
                                }
                                first = false;
                                pos += size;
                            }
                        }
                        continue;
                    }

                    Assert.That(block, Is.EqualTo(0x2C), $"Unknown block 0x{block:X2} at {pos - 1}.");
                    int frameWidth = data[pos + 4] | (data[pos + 5] << 8);
                    int frameHeight = data[pos + 6] | (data[pos + 7] << 8);
                    int framePacked = data[pos + 8];
                    pos += 9;
                    Assert.That(framePacked & 0x40, Is.Zero, "Interlaced frames are never written.");
                    if ((framePacked & 0x80) != 0)
                    {
                        int paletteBytes = 3 * (1 << ((framePacked & 0x07) + 1));
                        byte[] palette = new byte[paletteBytes];
                        Array.Copy(data, pos, palette, 0, paletteBytes);
                        parsed.Palettes.Add(palette);
                        pos += paletteBytes;
                    }
                    else
                    {
                        parsed.Palettes.Add(Array.Empty<byte>());
                    }

                    int minCodeSize = data[pos++];
                    var lzw = new MemoryStream();
                    int subBlock;
                    while ((subBlock = data[pos++]) != 0)
                    {
                        lzw.Write(data, pos, subBlock);
                        pos += subBlock;
                    }

                    parsed.Delays.Add(pendingDelay);
                    parsed.FrameIndices.Add(DecodeLzw(lzw.ToArray(), minCodeSize, frameWidth * frameHeight));
                }

                return parsed;
            }

            private static byte[] DecodeLzw(byte[] data, int minCodeSize, int expectedPixels)
            {
                int clearCode = 1 << minCodeSize;
                int endCode = clearCode + 1;
                int codeBits = minCodeSize + 1;
                int nextCode = endCode + 1;

                int[] prefixes = new int[4096];
                byte[] suffixes = new byte[4096];
                for (int Code = 0; Code < clearCode; Code++)
                {
                    prefixes[Code] = -1;
                    suffixes[Code] = (byte)Code;
                }

                var output = new List<byte>(expectedPixels);
                var stack = new Stack<byte>();
                int bitPos = 0;
                int previous = -1;

                int ReadCode(int bits)
                {
                    int value = 0;
                    for (int Bit = 0; Bit < bits; Bit++, bitPos++)
                    {
                        if ((data[bitPos >> 3] & (1 << (bitPos & 7))) != 0) value |= 1 << Bit;
                    }
                    return value;
                }

                byte FirstOf(int code)
                {
                    while (prefixes[code] >= 0) code = prefixes[code];
                    return suffixes[code];
                }

                void Emit(int code)
                {
                    while (code >= 0)
                    {
                        stack.Push(suffixes[code]);
                        code = prefixes[code];
                    }
                    while (stack.Count > 0) output.Add(stack.Pop());
                }

                while (true)
                {
                    int code = ReadCode(codeBits);
                    if (code == endCode) break;

                    if (code == clearCode)
                    {
                        codeBits = minCodeSize + 1;
                        nextCode = endCode + 1;
                        previous = -1;
                        continue;
                    }

                    if (previous < 0)
                    {
                        Assert.That(code, Is.LessThan(clearCode), "First code after a clear must be a root.");
                        output.Add(suffixes[code]);
                        previous = code;
                        continue;
                    }

                    if (code < nextCode)
                    {
                        Emit(code);
                    }
                    else
                    {
                        Assert.That(code, Is.EqualTo(nextCode), $"Code {code} references an entry that does not exist yet.");
                        Emit(previous);
                        output.Add(FirstOf(previous));
                    }

                    if (nextCode < 4096)
                    {
                        prefixes[nextCode] = previous;
                        suffixes[nextCode] = FirstOf(code < nextCode ? code : previous);
                        nextCode++;
                        if (nextCode == (1 << codeBits) && codeBits < 12) codeBits++;
                    }

                    previous = code;
                }

                Assert.That(output.Count, Is.EqualTo(expectedPixels),
                    "Decoded pixel count differs from the image descriptor.");
                return output.ToArray();
            }
        }
    }
}
