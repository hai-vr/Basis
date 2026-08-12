using System;
using System.Collections.Generic;
using System.IO;
using Basis;
using NUnit.Framework;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The AVI muxer against an independently written RIFF reader: header fields, frame payloads
    /// with their odd-length padding, the idx1 index pointing at the real chunk positions, and
    /// the backpatched sizes a streaming writer can only know at the end.
    /// </summary>
    public class BasisMjpegAviWriterTests
    {
        [Test]
        public void HeadersDescribeTheStreamAndTheSizesAddUp()
        {
            var stream = new MemoryStream();
            var writer = new BasisMjpegAviWriter(stream, 320, 180, 30);
            writer.WriteFrame(FakeJpeg(100, 1), 100);
            writer.Finish();

            byte[] data = stream.ToArray();
            ParsedAvi parsed = ParsedAvi.Parse(data);

            Assert.That(parsed.RiffSize, Is.EqualTo(data.Length - 8), "RIFF size must cover the whole file.");
            Assert.That(parsed.Width, Is.EqualTo(320));
            Assert.That(parsed.Height, Is.EqualTo(180));
            Assert.That(parsed.StreamType, Is.EqualTo("vids"));
            Assert.That(parsed.Handler, Is.EqualTo("MJPG"));
            Assert.That(parsed.Compression, Is.EqualTo("MJPG"));
            Assert.That(parsed.HasIndexFlag, Is.True);
        }

        [Test]
        public void FramesRoundTripWithPaddingAndTheIndexPointsAtThem()
        {
            var stream = new MemoryStream();
            var writer = new BasisMjpegAviWriter(stream, 64, 48, 24);

            byte[] odd = FakeJpeg(101, 7);
            byte[] even = FakeJpeg(200, 8);
            byte[] big = FakeJpeg(4321, 9);
            writer.WriteFrame(odd, odd.Length);
            writer.WriteFrame(even, even.Length);
            writer.WriteFrame(big, big.Length);
            writer.Finish();

            ParsedAvi parsed = ParsedAvi.Parse(stream.ToArray());

            Assert.That(parsed.Frames.Count, Is.EqualTo(3));
            Assert.That(parsed.Frames[0], Is.EqualTo(odd));
            Assert.That(parsed.Frames[1], Is.EqualTo(even));
            Assert.That(parsed.Frames[2], Is.EqualTo(big));

            Assert.That(parsed.TotalFrames, Is.EqualTo(3));
            Assert.That(parsed.StreamLength, Is.EqualTo(3));
            Assert.That(parsed.SuggestedBufferSize, Is.EqualTo(big.Length), "The buffer hint is the largest frame.");

            Assert.That(parsed.IndexEntries.Count, Is.EqualTo(3));
            for (int Frame = 0; Frame < 3; Frame++)
            {
                Assert.That(parsed.IndexEntries[Frame].Offset, Is.EqualTo(parsed.FrameChunkOffsets[Frame]),
                    $"idx1 entry {Frame} does not point at its chunk.");
                Assert.That(parsed.IndexEntries[Frame].Size, Is.EqualTo(parsed.Frames[Frame].Length));
            }
        }

        [Test]
        public void MeasuredFrameRateIsPatchedIn()
        {
            var stream = new MemoryStream();
            var writer = new BasisMjpegAviWriter(stream, 32, 32, 30);
            writer.WriteFrame(FakeJpeg(64, 2), 64);
            writer.WriteFrame(FakeJpeg(64, 3), 64);
            writer.Finish(24.5);

            ParsedAvi parsed = ParsedAvi.Parse(stream.ToArray());
            Assert.That(parsed.Scale, Is.EqualTo(1000));
            Assert.That(parsed.Rate, Is.EqualTo(24500));
            Assert.That(parsed.MicroSecPerFrame, Is.EqualTo((int)Math.Round(1_000_000_000.0 / 24500)));
        }

        [Test]
        public void NominalRateHoldsWhenNothingWasMeasured()
        {
            var stream = new MemoryStream();
            var writer = new BasisMjpegAviWriter(stream, 32, 32, 30);
            writer.WriteFrame(FakeJpeg(64, 4), 64);
            writer.Finish(null);

            ParsedAvi parsed = ParsedAvi.Parse(stream.ToArray());
            Assert.That(parsed.Rate, Is.EqualTo(30000));
        }

        [Test]
        public void WriterRefusesAStreamItCannotSeek()
        {
            Assert.Throws<ArgumentException>(() => new BasisMjpegAviWriter(new ForwardOnlyStream(), 16, 16, 30));
        }

        /// <summary>Recognisably JPEG-shaped payload: SOI marker in, EOI marker out, noise between.</summary>
        private static byte[] FakeJpeg(int length, int seed)
        {
            var random = new System.Random(seed);
            byte[] data = new byte[Math.Max(4, length)];
            random.NextBytes(data);
            data[0] = 0xFF; data[1] = 0xD8;
            data[data.Length - 2] = 0xFF; data[data.Length - 1] = 0xD9;
            return data;
        }

        private sealed class ForwardOnlyStream : MemoryStream
        {
            public override bool CanSeek => false;
        }

        /// <summary>
        /// A deliberately independent RIFF walker for the fields the muxer claims to write.
        /// Reads the format from the reader's side, so the writer is checked against what a
        /// player parses rather than against itself.
        /// </summary>
        public sealed class ParsedAvi
        {
            public int RiffSize;
            public int Width;
            public int Height;
            public string StreamType;
            public string Handler;
            public string Compression;
            public bool HasIndexFlag;
            public int MicroSecPerFrame;
            public int TotalFrames;
            public int SuggestedBufferSize;
            public int Scale;
            public int Rate;
            public int StreamLength;
            public readonly List<byte[]> Frames = new List<byte[]>();
            public readonly List<int> FrameChunkOffsets = new List<int>();
            public readonly List<(int Offset, int Size)> IndexEntries = new List<(int, int)>();

            public static ParsedAvi Parse(byte[] data)
            {
                var parsed = new ParsedAvi();
                Assert.That(Fourcc(data, 0), Is.EqualTo("RIFF"));
                parsed.RiffSize = Int(data, 4);
                Assert.That(Fourcc(data, 8), Is.EqualTo("AVI "));

                int moviFourccAt = -1;
                int pos = 12;
                while (pos < data.Length)
                {
                    string chunk = Fourcc(data, pos);
                    int size = Int(data, pos + 4);
                    int body = pos + 8;

                    if (chunk == "LIST")
                    {
                        string listType = Fourcc(data, body);
                        if (listType == "hdrl")
                        {
                            ParseHeaderList(data, body + 4, body + size, parsed);
                        }
                        else if (listType == "movi")
                        {
                            moviFourccAt = body;
                            ParseMovi(data, body + 4, body + size, moviFourccAt, parsed);
                        }
                    }
                    else if (chunk == "idx1")
                    {
                        for (int Entry = body; Entry < body + size; Entry += 16)
                        {
                            Assert.That(Fourcc(data, Entry), Is.EqualTo("00dc"));
                            parsed.IndexEntries.Add((Int(data, Entry + 8), Int(data, Entry + 12)));
                        }
                    }

                    pos = body + size + (size & 1);
                }

                Assert.That(moviFourccAt, Is.GreaterThan(0), "No movi list found.");
                return parsed;
            }

            private static void ParseHeaderList(byte[] data, int pos, int end, ParsedAvi parsed)
            {
                while (pos < end)
                {
                    string chunk = Fourcc(data, pos);
                    int size = Int(data, pos + 4);
                    int body = pos + 8;

                    if (chunk == "avih")
                    {
                        parsed.MicroSecPerFrame = Int(data, body);
                        parsed.HasIndexFlag = (Int(data, body + 12) & 0x10) != 0;
                        parsed.TotalFrames = Int(data, body + 16);
                        parsed.SuggestedBufferSize = Int(data, body + 28);
                        parsed.Width = Int(data, body + 32);
                        parsed.Height = Int(data, body + 36);
                    }
                    else if (chunk == "LIST" && Fourcc(data, body) == "strl")
                    {
                        ParseStreamList(data, body + 4, body + size, parsed);
                    }

                    pos = body + size + (size & 1);
                }
            }

            private static void ParseStreamList(byte[] data, int pos, int end, ParsedAvi parsed)
            {
                while (pos < end)
                {
                    string chunk = Fourcc(data, pos);
                    int size = Int(data, pos + 4);
                    int body = pos + 8;

                    if (chunk == "strh")
                    {
                        parsed.StreamType = Fourcc(data, body);
                        parsed.Handler = Fourcc(data, body + 4);
                        parsed.Scale = Int(data, body + 20);
                        parsed.Rate = Int(data, body + 24);
                        parsed.StreamLength = Int(data, body + 32);
                    }
                    else if (chunk == "strf")
                    {
                        parsed.Compression = Fourcc(data, body + 16);
                    }

                    pos = body + size + (size & 1);
                }
            }

            private static void ParseMovi(byte[] data, int pos, int end, int moviFourccAt, ParsedAvi parsed)
            {
                while (pos < end)
                {
                    string chunk = Fourcc(data, pos);
                    int size = Int(data, pos + 4);
                    Assert.That(chunk, Is.EqualTo("00dc"), $"Unexpected chunk '{chunk}' inside movi.");

                    parsed.FrameChunkOffsets.Add(pos - moviFourccAt);
                    byte[] frame = new byte[size];
                    Array.Copy(data, pos + 8, frame, 0, size);
                    parsed.Frames.Add(frame);

                    pos += 8 + size + (size & 1);
                }
            }

            private static string Fourcc(byte[] data, int at) =>
                System.Text.Encoding.ASCII.GetString(data, at, 4);

            private static int Int(byte[] data, int at) =>
                data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
        }
    }
}
