using System;
using System.Collections.Generic;
using System.IO;

namespace Basis
{
    /// <summary>
    /// Streams a Motion-JPEG AVI: RIFF headers up front, one JPEG per frame as it arrives, then
    /// the index and the real sizes backpatched on <see cref="Finish"/> — which is why the stream
    /// must be seekable. MJPEG because it is the only video codec this project can carry without
    /// a native encoder: every frame is an ordinary JPEG, so the whole file is pure C# plus the
    /// engine's own JPEG encode, and every player from VLC to an editor timeline reads it.
    /// Pure data — safe to drive from the encode worker.
    /// </summary>
    public sealed class BasisMjpegAviWriter
    {
        private readonly Stream stream;
        private readonly int width;
        private readonly int height;
        private readonly int nominalFrameRate;

        private readonly List<int> frameOffsets = new List<int>();
        private readonly List<int> frameSizes = new List<int>();
        private int largestFrame;
        private bool finished;

        private long riffSizeAt;
        private long microSecPerFrameAt;
        private long maxBytesPerSecAt;
        private long totalFramesAt;
        private long headerSuggestedBufferAt;
        private long rateAt;
        private long lengthAt;
        private long streamSuggestedBufferAt;
        private long moviSizeAt;
        private long moviFourccAt;

        public int FrameCount => frameOffsets.Count;

        public BasisMjpegAviWriter(Stream stream, int width, int height, int nominalFrameRate)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("AVI needs a seekable stream — sizes are patched at the end.", nameof(stream));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            this.width = width;
            this.height = height;
            this.nominalFrameRate = Math.Max(1, nominalFrameRate);

            WriteHeaders();
        }

        /// <summary>Appends one frame. The buffer may be pooled — only the first <paramref name="length"/> bytes are read.</summary>
        public void WriteFrame(byte[] jpeg, int length)
        {
            if (finished) throw new InvalidOperationException("Writer already finished.");
            if (jpeg == null) throw new ArgumentNullException(nameof(jpeg));
            if (length <= 0 || length > jpeg.Length) throw new ArgumentOutOfRangeException(nameof(length));

            // Index offsets are counted from the 'movi' fourcc, first chunk at 4 — the layout
            // players expect from every common muxer.
            frameOffsets.Add((int)(stream.Position - moviFourccAt));
            frameSizes.Add(length);
            if (length > largestFrame) largestFrame = length;

            WriteFourcc("00dc");
            WriteInt(length);
            stream.Write(jpeg, 0, length);
            if ((length & 1) != 0) stream.WriteByte(0);
        }

        /// <summary>
        /// Writes the index and patches every size and count. <paramref name="measuredFrameRate"/>
        /// is the rate the frames were actually captured at; AVI plays at one fixed rate, so using
        /// the measured average keeps the clip's wall-clock length true even when capture skipped
        /// frames. Pass null to keep the nominal rate. The stream stays open — its owner closes it.
        /// </summary>
        public void Finish(double? measuredFrameRate = null)
        {
            if (finished) return;
            finished = true;

            long moviEnd = stream.Position;
            WriteFourcc("idx1");
            WriteInt(FrameCount * 16);
            for (int Frame = 0; Frame < FrameCount; Frame++)
            {
                WriteFourcc("00dc");
                WriteInt(0x10);
                WriteInt(frameOffsets[Frame]);
                WriteInt(frameSizes[Frame]);
            }
            long fileEnd = stream.Position;

            double frameRate = measuredFrameRate.HasValue && measuredFrameRate.Value > 0.5
                ? measuredFrameRate.Value
                : nominalFrameRate;

            // dwRate over a fixed dwScale of 1000 carries fractional rates without float headers.
            int rate = Math.Max(1, (int)Math.Round(frameRate * 1000.0));
            int microSecPerFrame = Math.Max(1, (int)Math.Round(1_000_000.0 * 1000.0 / rate));
            int bytesPerSecond = (int)Math.Min(int.MaxValue, (long)Math.Ceiling(largestFrame * frameRate));
            int suggestedBuffer = largestFrame;

            Patch(riffSizeAt, (int)(fileEnd - 8));
            Patch(microSecPerFrameAt, microSecPerFrame);
            Patch(maxBytesPerSecAt, bytesPerSecond);
            Patch(totalFramesAt, FrameCount);
            Patch(headerSuggestedBufferAt, suggestedBuffer);
            Patch(rateAt, rate);
            Patch(lengthAt, FrameCount);
            Patch(streamSuggestedBufferAt, suggestedBuffer);
            Patch(moviSizeAt, (int)(moviEnd - moviFourccAt));

            stream.Seek(fileEnd, SeekOrigin.Begin);
            stream.Flush();
        }

        private void WriteHeaders()
        {
            WriteFourcc("RIFF");
            riffSizeAt = Reserve();
            WriteFourcc("AVI ");

            WriteFourcc("LIST");
            long headerListSizeAt = Reserve();
            long headerListStart = stream.Position;
            WriteFourcc("hdrl");

            WriteFourcc("avih");
            WriteInt(56);
            microSecPerFrameAt = Reserve();
            maxBytesPerSecAt = Reserve();
            WriteInt(0);
            WriteInt(0x10);                       // AVIF_HASINDEX
            totalFramesAt = Reserve();
            WriteInt(0);
            WriteInt(1);
            headerSuggestedBufferAt = Reserve();
            WriteInt(width);
            WriteInt(height);
            WriteInt(0); WriteInt(0); WriteInt(0); WriteInt(0);

            WriteFourcc("LIST");
            long streamListSizeAt = Reserve();
            long streamListStart = stream.Position;
            WriteFourcc("strl");

            WriteFourcc("strh");
            WriteInt(56);
            WriteFourcc("vids");
            WriteFourcc("MJPG");
            WriteInt(0);
            WriteShort(0);
            WriteShort(0);
            WriteInt(0);
            WriteInt(1000);                       // dwScale — dwRate/1000 fps, patched at Finish
            rateAt = Reserve();
            WriteInt(0);
            lengthAt = Reserve();
            streamSuggestedBufferAt = Reserve();
            WriteInt(-1);                         // dwQuality: default
            WriteInt(0);
            WriteShort(0); WriteShort(0);         // rcFrame left, top
            WriteShort((short)width);
            WriteShort((short)height);

            WriteFourcc("strf");
            WriteInt(40);
            WriteInt(40);                         // BITMAPINFOHEADER biSize
            WriteInt(width);
            WriteInt(height);
            WriteShort(1);
            WriteShort(24);
            WriteFourcc("MJPG");
            WriteInt(width * height * 3);
            WriteInt(0); WriteInt(0); WriteInt(0); WriteInt(0);

            long afterStreamList = stream.Position;
            Patch(streamListSizeAt, (int)(afterStreamList - streamListStart));
            Patch(headerListSizeAt, (int)(afterStreamList - headerListStart));
            stream.Seek(afterStreamList, SeekOrigin.Begin);

            WriteFourcc("LIST");
            moviSizeAt = Reserve();
            moviFourccAt = stream.Position;
            WriteFourcc("movi");
        }

        /// <summary>Writes a size placeholder and returns where it lives, for patching at Finish.</summary>
        private long Reserve()
        {
            long at = stream.Position;
            WriteInt(0);
            return at;
        }

        private void Patch(long at, int value)
        {
            stream.Seek(at, SeekOrigin.Begin);
            WriteInt(value);
        }

        private void WriteFourcc(string fourcc)
        {
            for (int Index = 0; Index < 4; Index++) stream.WriteByte((byte)fourcc[Index]);
        }

        private void WriteInt(int value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 24) & 0xFF));
        }

        private void WriteShort(short value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }
    }
}
