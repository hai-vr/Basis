#if !BASIS_DISABLE_MICROPHONE
using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Minimal 16-bit PCM WAV writer for the microphone test recording. Deliberately not a general
/// audio exporter — it only has to produce a file every player and editor on the planet opens.
/// </summary>
public static class BasisWavFile
{
    public const int BitsPerSample = 16;

    public static string OutputDirectory => Path.Combine(Application.persistentDataPath, "MicrophoneTest");

    public static string BuildPath(DateTime stamp) =>
        Path.Combine(OutputDirectory, $"MicrophoneTest-{stamp:yyyy-MM-dd_HH-mm-ss}.wav");

    public static bool TryWrite(string path, float[] samples, int count, int sampleRate, int channels, out string error)
    {
        error = null;

        if (samples == null || count <= 0)
        {
            error = "no samples";
            return false;
        }

        if (count > samples.Length) count = samples.Length;
        if (sampleRate <= 0) sampleRate = 48000;
        if (channels <= 0) channels = 1;

        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            int blockAlign = channels * (BitsPerSample / 8);
            int dataBytes = count * (BitsPerSample / 8);

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(new char[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new char[] { 'W', 'A', 'V', 'E' });

                writer.Write(new char[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * blockAlign);
                writer.Write((short)blockAlign);
                writer.Write((short)BitsPerSample);

                writer.Write(new char[] { 'd', 'a', 't', 'a' });
                writer.Write(dataBytes);

                for (int i = 0; i < count; i++)
                {
                    float value = samples[i];
                    if (value > 1f) value = 1f;
                    else if (value < -1f) value = -1f;
                    writer.Write((short)Mathf.RoundToInt(value * short.MaxValue));
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
#endif
