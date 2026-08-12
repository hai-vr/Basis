using System;
using System.Collections.Generic;
using System.IO;

namespace Basis
{
    /// <summary>
    /// Streams a Motion-JPEG AVI, optionally with a 16-bit PCM audio stream interleaved: RIFF
    /// headers up front, chunks as they arrive, then the index and the real sizes backpatched on
    /// <see cref="Finish"/> — which is why the stream must be seekable. MJPEG because it is the
    /// only video codec this project can carry without a native encoder: every frame is an
    /// ordinary JPEG, so the whole file is pure C# plus the engine's own JPEG encode, and every
    /// player from VLC to an editor timeline reads it. Pure data — safe to drive from the encode
    /// worker.
    /// </summary>
    public sealed class BasisMjpegAviWriter
    {
        private readonly Stream stream;
        private readonly int width;
        private readonly int height;
        private readonly int nominalFrameRate;
        private readonly int audioSampleRate;
        private readonly int audioChannels;
        private readonly int audioBlockAlign;

        private struct IndexEntry
        {
            public bool IsAudio;
            public int Offset;
            public int Size;
        }

        private readonly List<IndexEntry> index = new List<IndexEntry>();
        private int largestVideoChunk;
        private int largestAudioChunk;
        private long audioBytesWritten;
        private bool finished;

        private long riffSizeAt;
        private long microSecPerFrameAt;
        private long maxBytesPerSecAt;
        private long totalFramesAt;
        private long headerSuggestedBufferAt;
        private long videoRateAt;
        private long videoLengthAt;
        private long videoSuggestedBufferAt;
        private long audioLengthAt;
        private long audioSuggestedBufferAt;
        private long moviSizeAt;
        private long moviFourccAt;

        public int FrameCount { get; private set; }

        public bool HasAudio => audioSampleRate > 0;

        public BasisMjpegAviWriter(Stream stream, int width, int height, int nominalFrameRate,
            int audioSampleRate = 0, int audioChannels = 0)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("AVI needs a seekable stream — sizes are patched at the end.", nameof(stream));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (audioSampleRate > 0 && (audioChannels < 1 || audioChannels > 8)) throw new ArgumentOutOfRangeException(nameof(audioChannels));
            this.width = width;
            this.height = height;
            this.nominalFrameRate = Math.Max(1, nominalFrameRate);
            this.audioSampleRate = Math.Max(0, audioSampleRate);
            this.audioChannels = audioChannels;
            audioBlockAlign = audioChannels * 2;

            WriteHeaders();
        }

        /// <summary>Appends one video frame. The buffer may be pooled — only the first <paramref name="length"/> bytes are read.</summary>
        public void WriteFrame(byte[] jpeg, int length)
        {
            if (finished) throw new InvalidOperationException("Writer already finished.");
            if (jpeg == null) throw new ArgumentNullException(nameof(jpeg));
            if (length <= 0 || length > jpeg.Length) throw new ArgumentOutOfRangeException(nameof(length));

            if (length > largestVideoChunk) largestVideoChunk = length;
            WriteChunk("00dc", jpeg, 0, length, isAudio: false);
            FrameCount++;
        }

        /// <summary>Appends interleaved 16-bit PCM. Lengths that shear a sample block are trimmed to whole blocks.</summary>
        public void WriteAudio(byte[] pcm, int offset, int length)
        {
            if (finished) throw new InvalidOperationException("Writer already finished.");
            if (!HasAudio) throw new InvalidOperationException("Writer was created without an audio stream.");
            if (pcm == null) throw new ArgumentNullException(nameof(pcm));
            if (offset < 0 || offset > pcm.Length) throw new ArgumentOutOfRangeException(nameof(offset));

            length = Math.Min(length, pcm.Length - offset);
            length -= length % audioBlockAlign;
            if (length <= 0) return;

            if (length > largestAudioChunk) largestAudioChunk = length;
            audioBytesWritten += length;
            WriteChunk("01wb", pcm, offset, length, isAudio: true);
        }

        private void WriteChunk(string fourcc, byte[] payload, int offset, int length, bool isAudio)
        {
            // Index offsets are counted from the 'movi' fourcc, first chunk at 4 — the layout
            // players expect from every common muxer.
            index.Add(new IndexEntry { IsAudio = isAudio, Offset = (int)(stream.Position - moviFourccAt), Size = length });

            WriteFourcc(fourcc);
            WriteInt(length);
            stream.Write(payload, offset, length);
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
            WriteInt(index.Count * 16);
            for (int Entry = 0; Entry < index.Count; Entry++)
            {
                WriteFourcc(index[Entry].IsAudio ? "01wb" : "00dc");
                WriteInt(0x10);
                WriteInt(index[Entry].Offset);
                WriteInt(index[Entry].Size);
            }
            long fileEnd = stream.Position;

            double frameRate = measuredFrameRate.HasValue && measuredFrameRate.Value > 0.5
                ? measuredFrameRate.Value
                : nominalFrameRate;

            // dwRate over a fixed dwScale of 1000 carries fractional rates without float headers.
            int rate = Math.Max(1, (int)Math.Round(frameRate * 1000.0));
            int microSecPerFrame = Math.Max(1, (int)Math.Round(1_000_000.0 * 1000.0 / rate));
            long videoBytesPerSecond = (long)Math.Ceiling(largestVideoChunk * frameRate);
            long audioBytesPerSecond = HasAudio ? audioSampleRate * (long)audioBlockAlign : 0;
            int bytesPerSecond = (int)Math.Min(int.MaxValue, videoBytesPerSecond + audioBytesPerSecond);

            Patch(riffSizeAt, (int)(fileEnd - 8));
            Patch(microSecPerFrameAt, microSecPerFrame);
            Patch(maxBytesPerSecAt, bytesPerSecond);
            Patch(totalFramesAt, FrameCount);
            Patch(headerSuggestedBufferAt, Math.Max(largestVideoChunk, largestAudioChunk));
            Patch(videoRateAt, rate);
            Patch(videoLengthAt, FrameCount);
            Patch(videoSuggestedBufferAt, largestVideoChunk);
            if (HasAudio)
            {
                Patch(audioLengthAt, (int)(audioBytesWritten / audioBlockAlign));
                Patch(audioSuggestedBufferAt, largestAudioChunk);
            }
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
            WriteInt(HasAudio ? 2 : 1);
            headerSuggestedBufferAt = Reserve();
            WriteInt(width);
            WriteInt(height);
            WriteInt(0); WriteInt(0); WriteInt(0); WriteInt(0);

            WriteVideoStreamList();
            if (HasAudio) WriteAudioStreamList();

            long afterHeaderList = stream.Position;
            Patch(headerListSizeAt, (int)(afterHeaderList - headerListStart));
            stream.Seek(afterHeaderList, SeekOrigin.Begin);

            WriteFourcc("LIST");
            moviSizeAt = Reserve();
            moviFourccAt = stream.Position;
            WriteFourcc("movi");
        }

        private void WriteVideoStreamList()
        {
            WriteFourcc("LIST");
            long listSizeAt = Reserve();
            long listStart = stream.Position;
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
            videoRateAt = Reserve();
            WriteInt(0);
            videoLengthAt = Reserve();
            videoSuggestedBufferAt = Reserve();
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

            long listEnd = stream.Position;
            Patch(listSizeAt, (int)(listEnd - listStart));
            stream.Seek(listEnd, SeekOrigin.Begin);
        }

        /// <summary>
        /// PCM stream headers, in the units players expect for uncompressed audio: the sample
        /// size, scale and length all speak block-aligned sample frames, so rate over scale is
        /// samples per second.
        /// </summary>
        private void WriteAudioStreamList()
        {
            int avgBytesPerSecond = audioSampleRate * audioBlockAlign;

            WriteFourcc("LIST");
            long listSizeAt = Reserve();
            long listStart = stream.Position;
            WriteFourcc("strl");

            WriteFourcc("strh");
            WriteInt(56);
            WriteFourcc("auds");
            WriteInt(0);
            WriteInt(0);
            WriteShort(0);
            WriteShort(0);
            WriteInt(0);
            WriteInt(audioBlockAlign);            // dwScale
            WriteInt(avgBytesPerSecond);          // dwRate — rate/scale = samples per second
            WriteInt(0);
            audioLengthAt = Reserve();            // dwLength in sample frames, patched at Finish
            audioSuggestedBufferAt = Reserve();
            WriteInt(-1);
            WriteInt(audioBlockAlign);            // dwSampleSize
            WriteShort(0); WriteShort(0);
            WriteShort(0); WriteShort(0);

            WriteFourcc("strf");
            WriteInt(18);                         // WAVEFORMATEX
            WriteShort(1);                        // PCM
            WriteShort((short)audioChannels);
            WriteInt(audioSampleRate);
            WriteInt(avgBytesPerSecond);
            WriteShort((short)audioBlockAlign);
            WriteShort(16);
            WriteShort(0);                        // cbSize

            long listEnd = stream.Position;
            Patch(listSizeAt, (int)(listEnd - listStart));
            stream.Seek(listEnd, SeekOrigin.Begin);
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
