using System;

namespace Basis
{
    /// <summary>
    /// The hand-off between the audio thread and the encode worker: the listener tap writes the
    /// mixed output in whatever channel layout the device runs, this converts it to interleaved
    /// stereo 16-bit and holds it until the worker drains it into the file. The lock is held only
    /// to move indices and samples — never for allocation or IO — which is the discipline the
    /// audio thread demands. Pure C#, so the conversion maths is testable without an audio device.
    /// </summary>
    public sealed class BasisRecordingAudioBuffer
    {
        /// <summary>The recording is always stereo: wider device layouts keep their front pair, mono is doubled.</summary>
        public const int Channels = 2;

        public const int BytesPerSampleFrame = Channels * 2;

        private readonly object gate = new object();
        private readonly short[] ring;
        private int readAt;
        private int count;

        /// <summary>
        /// Sample frames refused because the worker fell far enough behind to fill the ring.
        /// Replayed as silence once the ring drains, so the recording keeps its duration even
        /// though the audio in the gap is gone.
        /// </summary>
        private long droppedSampleFrames;

        public int SampleRate { get; }

        public BasisRecordingAudioBuffer(int sampleRate, float capacitySeconds = 4f)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            SampleRate = sampleRate;
            ring = new short[Math.Max(1, (int)(sampleRate * capacitySeconds)) * Channels];
        }

        /// <summary>
        /// Audio thread. Interleaved floats in the device layout; converts to stereo 16-bit and
        /// stores. Layouts wider than stereo contribute their first two channels — the front
        /// left/right pair in every Unity speaker mode.
        /// </summary>
        public void Write(float[] interleaved, int sourceChannels)
        {
            if (interleaved == null || sourceChannels <= 0) return;
            int sampleFrames = interleaved.Length / sourceChannels;
            if (sampleFrames <= 0) return;

            lock (gate)
            {
                for (int Frame = 0; Frame < sampleFrames; Frame++)
                {
                    if (count + Channels > ring.Length)
                    {
                        droppedSampleFrames += sampleFrames - Frame;
                        return;
                    }

                    int source = Frame * sourceChannels;
                    float left = interleaved[source];
                    float right = sourceChannels >= 2 ? interleaved[source + 1] : left;

                    int writeAt = (readAt + count) % ring.Length;
                    ring[writeAt] = ToPcm16(left);
                    ring[writeAt + 1 == ring.Length ? 0 : writeAt + 1] = ToPcm16(right);
                    count += Channels;
                }
            }
        }

        /// <summary>
        /// Worker thread. Fills little-endian stereo 16-bit bytes and returns how many were
        /// written — zero when nothing is buffered. Dropped stretches come back as silence after
        /// the ring drains, keeping the stream's length honest.
        /// </summary>
        public int Read(byte[] destination)
        {
            if (destination == null) return 0;
            int maxSamples = destination.Length / 2;

            lock (gate)
            {
                int written = 0;
                while (written < maxSamples && count > 0)
                {
                    short sample = ring[readAt];
                    readAt = readAt + 1 == ring.Length ? 0 : readAt + 1;
                    count--;

                    destination[written * 2] = (byte)(sample & 0xFF);
                    destination[written * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                    written++;
                }

                if (count == 0)
                {
                    while (written + Channels <= maxSamples && droppedSampleFrames > 0)
                    {
                        destination[written * 2] = 0;
                        destination[written * 2 + 1] = 0;
                        destination[written * 2 + 2] = 0;
                        destination[written * 2 + 3] = 0;
                        written += Channels;
                        droppedSampleFrames--;
                    }
                }

                return written * 2;
            }
        }

        /// <summary>Forgets everything buffered and any silence debt.</summary>
        public void Clear()
        {
            lock (gate)
            {
                readAt = 0;
                count = 0;
                droppedSampleFrames = 0;
            }
        }

        private static short ToPcm16(float sample)
        {
            if (sample > 1f) sample = 1f;
            else if (sample < -1f) sample = -1f;
            return (short)(sample * short.MaxValue);
        }
    }
}
