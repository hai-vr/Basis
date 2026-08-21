#if !BASIS_DISABLE_MICROPHONE
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Voice
{
    public class BasisMicrophoneTestTapTests
    {
        private const int SampleRate = 48000;
        private const int FrameSize = 960;

        private static float[] Frame(float amplitude)
        {
            float[] frame = new float[FrameSize];
            for (int i = 0; i < FrameSize; i++)
            {
                frame[i] = amplitude * Mathf.Sin(i * 0.05f);
            }
            return frame;
        }

        private static void PushFrames(BasisMicrophoneTestTap tap, float amplitude, int frames)
        {
            float[] frame = Frame(amplitude);
            for (int i = 0; i < frames; i++)
            {
                tap.PushFrame(frame, FrameSize);
            }
        }

        [Test]
        public void LevelRingKeepsNewestColumnLast()
        {
            BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();
            PushFrames(tap, 0.1f, BasisMicrophoneTestTap.Columns);
            PushFrames(tap, 0.8f, 1);

            float[] peaks = new float[BasisMicrophoneTestTap.Columns];
            float[] rms = new float[BasisMicrophoneTestTap.Columns];
            int count = tap.CopyLevels(peaks, rms);

            Assert.AreEqual(BasisMicrophoneTestTap.Columns, count);
            Assert.Greater(peaks[count - 1], 0.7f, "newest column should hold the loud frame");
            Assert.Less(peaks[count - 2], 0.2f, "older columns should still hold the quiet frames");
            Assert.Greater(rms[count - 1], rms[count - 2]);
        }

        [Test]
        public void LevelRingStartsEmptySoTheStripFillsFromTheRight()
        {
            BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();
            PushFrames(tap, 0.5f, 3);

            float[] peaks = new float[BasisMicrophoneTestTap.Columns];
            float[] rms = new float[BasisMicrophoneTestTap.Columns];
            int count = tap.CopyLevels(peaks, rms);

            Assert.AreEqual(0f, peaks[0]);
            Assert.AreEqual(0f, peaks[count - 4]);
            Assert.Greater(peaks[count - 1], 0.4f);
        }

        [Test]
        public void RecordingCollectsSamplesUntilStopped()
        {
            BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();
            Assert.IsFalse(tap.HasRecording);

            tap.StartRecording(SampleRate);
            PushFrames(tap, 0.5f, 50);
            tap.StopRecording();
            PushFrames(tap, 0.5f, 10);

            Assert.IsFalse(tap.IsRecording);
            Assert.AreEqual(50 * FrameSize, tap.RecordedSamples);
            Assert.AreEqual(50 * FrameSize / (float)SampleRate, tap.RecordedSeconds, 1e-4f);
        }

        [Test]
        public void RecordingStopsItselfAtTheLengthLimit()
        {
            BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();
            int capacity = SampleRate * BasisMicrophoneTestTap.MaxRecordSeconds;

            tap.StartRecording(SampleRate);
            PushFrames(tap, 0.5f, capacity / FrameSize + 20);

            Assert.IsFalse(tap.IsRecording, "recorder should latch off once the buffer is full");
            Assert.AreEqual(capacity, tap.RecordedSamples);
        }

        [Test]
        public void CopiedRecordingMatchesWhatWasPushed()
        {
            BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();
            float[] frame = Frame(0.6f);

            tap.StartRecording(SampleRate);
            tap.PushFrame(frame, FrameSize);
            tap.PushFrame(frame, FrameSize);
            tap.StopRecording();

            Assert.IsTrue(tap.TryCopyRecording(out float[] samples, out int sampleRate));
            Assert.AreEqual(SampleRate, sampleRate);
            Assert.AreEqual(FrameSize * 2, samples.Length);
            for (int i = 0; i < FrameSize; i++)
            {
                Assert.AreEqual(frame[i], samples[i], 1e-6f);
                Assert.AreEqual(frame[i], samples[FrameSize + i], 1e-6f);
            }
        }

        [Test]
        public void TakeColumnsFillProgressivelyAcrossTheRecordingWindow()
        {
            BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();
            float[] peaks = new float[BasisMicrophoneTestTap.Columns];
            float[] rms = new float[BasisMicrophoneTestTap.Columns];

            tap.StartRecording(SampleRate);
            PushFrames(tap, 0.5f, 25);
            tap.CopyTake(peaks, rms);

            Assert.Greater(peaks[0], 0.4f, "the take should draw from the left edge");
            Assert.AreEqual(0f, peaks[BasisMicrophoneTestTap.Columns - 1], "the unrecorded tail stays empty");
        }

        [Test]
        public void EnvelopeSpansTheWholeTakeOnceRecordingStops()
        {
            BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();
            float[] peaks = new float[BasisMicrophoneTestTap.Columns];
            float[] rms = new float[BasisMicrophoneTestTap.Columns];

            tap.StartRecording(SampleRate);
            PushFrames(tap, 0.5f, 25);
            tap.StopRecording();

            int columns = tap.BuildRecordedEnvelope(peaks, rms, BasisMicrophoneTestTap.Columns);

            Assert.AreEqual(BasisMicrophoneTestTap.Columns, columns);
            for (int i = 0; i < columns; i++)
            {
                Assert.Greater(peaks[i], 0.1f, "column " + i + " should carry signal");
                Assert.LessOrEqual(peaks[i], 0.51f);
                Assert.Greater(peaks[i], rms[i]);
            }
        }

        [Test]
        public void EnvelopeIsEmptyWithoutARecording()
        {
            BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();
            float[] peaks = new float[BasisMicrophoneTestTap.Columns];
            float[] rms = new float[BasisMicrophoneTestTap.Columns];

            Assert.AreEqual(0, tap.BuildRecordedEnvelope(peaks, rms, BasisMicrophoneTestTap.Columns));
            Assert.IsFalse(tap.TryCopyRecording(out _, out _));
        }

        [Test]
        public void ClearRecordingDropsTheTakeButKeepsMetering()
        {
            BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();

            tap.StartRecording(SampleRate);
            PushFrames(tap, 0.5f, 5);
            tap.ClearRecording();

            Assert.IsFalse(tap.IsRecording);
            Assert.IsFalse(tap.HasRecording);
            Assert.AreEqual(0, tap.RecordedSamples);

            PushFrames(tap, 0.5f, 1);
            Assert.AreEqual(6, tap.FramesPushed);
        }

        [Test]
        public void MuteBypassIsRefCountedAndClearsWhenReleased()
        {
            Assert.IsFalse(BasisLocalMicrophoneDriver.MuteBypassed, "suite started with a leaked bypass");

            BasisLocalMicrophoneDriver.AddMuteBypass();
            Assert.IsTrue(BasisLocalMicrophoneDriver.MuteBypassed);
            Assert.IsTrue(BasisLocalMicrophoneDriver.HasCaptureHold, "a bypass must also hold the capture device open");

            BasisLocalMicrophoneDriver.AddMuteBypass();
            BasisLocalMicrophoneDriver.ReleaseMuteBypass();
            Assert.IsTrue(BasisLocalMicrophoneDriver.MuteBypassed, "nested bypass should survive one release");

            BasisLocalMicrophoneDriver.ReleaseMuteBypass();
            Assert.IsFalse(BasisLocalMicrophoneDriver.MuteBypassed);
            Assert.IsFalse(BasisLocalMicrophoneDriver.HasCaptureHold);
        }

        [Test]
        public void UnbalancedReleaseCannotDriveTheCountersNegative()
        {
            BasisLocalMicrophoneDriver.ReleaseMuteBypass();
            BasisLocalMicrophoneDriver.ReleaseCaptureHold();

            Assert.IsFalse(BasisLocalMicrophoneDriver.MuteBypassed);
            Assert.IsFalse(BasisLocalMicrophoneDriver.HasCaptureHold);

            BasisLocalMicrophoneDriver.AddMuteBypass();
            Assert.IsTrue(BasisLocalMicrophoneDriver.MuteBypassed, "a stray release must not swallow the next bypass");
            BasisLocalMicrophoneDriver.ReleaseMuteBypass();
            Assert.IsFalse(BasisLocalMicrophoneDriver.MuteBypassed);
        }

        [Test]
        public void ShortFrameDoesNotReadPastTheSuppliedCount()
        {
            BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();
            float[] frame = new float[FrameSize];
            for (int i = 0; i < FrameSize; i++)
            {
                frame[i] = i < 100 ? 0.25f : 1f;
            }

            tap.StartRecording(SampleRate);
            tap.PushFrame(frame, 100);
            tap.StopRecording();

            Assert.AreEqual(100, tap.RecordedSamples);

            float[] peaks = new float[BasisMicrophoneTestTap.Columns];
            float[] rms = new float[BasisMicrophoneTestTap.Columns];
            tap.CopyLevels(peaks, rms);
            Assert.AreEqual(0.25f, peaks[BasisMicrophoneTestTap.Columns - 1], 1e-6f);
        }
    }
}
#endif
