#if !BASIS_DISABLE_MICROPHONE
using System;

public sealed class BasisMicrophoneTestTap
{
    public const int MaxRecordSeconds = 10;
    public const int Columns = 192;

    private readonly object gate = new object();
    private readonly float[] slotPeaks = new float[Columns];
    private readonly float[] slotRms = new float[Columns];
    private readonly float[] takePeaks = new float[Columns];
    private readonly float[] takeRms = new float[Columns];
    private int slotWrite;
    private long framesPushed;

    private float[] recordBuffer;
    private int recordedSamples;
    private int recordSampleRate = 48000;
    private bool recording;
    private bool attached;

    public bool IsRecording { get { lock (gate) { return recording; } } }
    public bool HasRecording { get { lock (gate) { return recordedSamples > 0; } } }
    public int RecordedSamples { get { lock (gate) { return recordedSamples; } } }
    public int RecordSampleRate { get { lock (gate) { return recordSampleRate; } } }
    public long FramesPushed { get { lock (gate) { return framesPushed; } } }

    public float RecordedSeconds
    {
        get
        {
            lock (gate)
            {
                return recordSampleRate > 0 ? recordedSamples / (float)recordSampleRate : 0f;
            }
        }
    }

    public void Attach()
    {
        if (attached) return;
        attached = true;
        BasisLocalMicrophoneDriver.OnHasAudio += OnMicrophoneFrame;
        BasisLocalMicrophoneDriver.OnHasSilence += OnMicrophoneFrame;
    }

    public void Detach()
    {
        if (!attached) return;
        attached = false;
        BasisLocalMicrophoneDriver.OnHasAudio -= OnMicrophoneFrame;
        BasisLocalMicrophoneDriver.OnHasSilence -= OnMicrophoneFrame;
    }

    private void OnMicrophoneFrame()
    {
        float[] frame = BasisLocalMicrophoneDriver.processBufferArray;
        if (frame == null) return;

        int count = BasisLocalMicrophoneDriver.ProcessFrameSize;
        if (count > frame.Length) count = frame.Length;
        PushFrame(frame, count);
    }

    public void PushFrame(float[] samples, int count)
    {
        if (samples == null) return;
        if (count > samples.Length) count = samples.Length;
        if (count <= 0) return;

        float peak = 0f;
        double square = 0d;
        for (int i = 0; i < count; i++)
        {
            float value = samples[i];
            float magnitude = value < 0f ? -value : value;
            if (magnitude > peak) peak = magnitude;
            square += (double)value * value;
        }

        float rms = (float)Math.Sqrt(square / count);

        lock (gate)
        {
            slotPeaks[slotWrite] = peak;
            slotRms[slotWrite] = rms;
            slotWrite = slotWrite + 1 == Columns ? 0 : slotWrite + 1;
            framesPushed++;

            if (!recording || recordBuffer == null) return;

            int column = (int)((long)recordedSamples * Columns / recordBuffer.Length);
            if (column < 0) column = 0;
            else if (column >= Columns) column = Columns - 1;
            if (peak > takePeaks[column]) takePeaks[column] = peak;
            if (rms > takeRms[column]) takeRms[column] = rms;

            int room = recordBuffer.Length - recordedSamples;
            int copy = count < room ? count : room;
            if (copy > 0)
            {
                Array.Copy(samples, 0, recordBuffer, recordedSamples, copy);
                recordedSamples += copy;
            }

            if (recordedSamples >= recordBuffer.Length) recording = false;
        }
    }

    public void StartRecording(int sampleRate)
    {
        lock (gate)
        {
            recordSampleRate = sampleRate > 0 ? sampleRate : 48000;
            int capacity = recordSampleRate * MaxRecordSeconds;
            if (recordBuffer == null || recordBuffer.Length != capacity) recordBuffer = new float[capacity];
            Array.Clear(takePeaks, 0, Columns);
            Array.Clear(takeRms, 0, Columns);
            recordedSamples = 0;
            recording = true;
        }
    }

    public void StopRecording()
    {
        lock (gate)
        {
            recording = false;
        }
    }

    public void ClearRecording()
    {
        lock (gate)
        {
            recording = false;
            recordedSamples = 0;
            recordBuffer = null;
            Array.Clear(takePeaks, 0, Columns);
            Array.Clear(takeRms, 0, Columns);
        }
    }

    public void ClearLevels()
    {
        lock (gate)
        {
            Array.Clear(slotPeaks, 0, Columns);
            Array.Clear(slotRms, 0, Columns);
            slotWrite = 0;
        }
    }

    public int CopyLevels(float[] peakDestination, float[] rmsDestination)
    {
        if (peakDestination == null || rmsDestination == null) return 0;

        int count = Math.Min(Columns, Math.Min(peakDestination.Length, rmsDestination.Length));
        if (count <= 0) return 0;

        lock (gate)
        {
            int oldest = slotWrite + Columns - count;
            for (int i = 0; i < count; i++)
            {
                int slot = (oldest + i) % Columns;
                peakDestination[i] = slotPeaks[slot];
                rmsDestination[i] = slotRms[slot];
            }
        }

        return count;
    }

    public int CopyTake(float[] peakDestination, float[] rmsDestination)
    {
        if (peakDestination == null || rmsDestination == null) return 0;

        int count = Math.Min(Columns, Math.Min(peakDestination.Length, rmsDestination.Length));
        if (count <= 0) return 0;

        lock (gate)
        {
            Array.Copy(takePeaks, 0, peakDestination, 0, count);
            Array.Copy(takeRms, 0, rmsDestination, 0, count);
        }

        return count;
    }

    public bool TryCopyRecording(out float[] samples, out int sampleRate)
    {
        lock (gate)
        {
            sampleRate = recordSampleRate;
            if (recordBuffer == null || recordedSamples <= 0)
            {
                samples = null;
                return false;
            }

            samples = new float[recordedSamples];
            Array.Copy(recordBuffer, 0, samples, 0, recordedSamples);
            return true;
        }
    }

    public int BuildRecordedEnvelope(float[] peakDestination, float[] rmsDestination, int columns)
    {
        if (peakDestination == null || rmsDestination == null) return 0;
        if (columns > peakDestination.Length) columns = peakDestination.Length;
        if (columns > rmsDestination.Length) columns = rmsDestination.Length;
        if (columns <= 0) return 0;

        lock (gate)
        {
            if (recordBuffer == null || recordedSamples <= 0) return 0;

            for (int column = 0; column < columns; column++)
            {
                int start = (int)((long)recordedSamples * column / columns);
                int end = (int)((long)recordedSamples * (column + 1) / columns);
                if (start >= recordedSamples)
                {
                    peakDestination[column] = 0f;
                    rmsDestination[column] = 0f;
                    continue;
                }
                if (end <= start) end = start + 1;
                if (end > recordedSamples) end = recordedSamples;

                float peak = 0f;
                double square = 0d;
                for (int i = start; i < end; i++)
                {
                    float value = recordBuffer[i];
                    float magnitude = value < 0f ? -value : value;
                    if (magnitude > peak) peak = magnitude;
                    square += (double)value * value;
                }

                peakDestination[column] = peak;
                rmsDestination[column] = (float)Math.Sqrt(square / (end - start));
            }
        }

        return columns;
    }
}
#endif
