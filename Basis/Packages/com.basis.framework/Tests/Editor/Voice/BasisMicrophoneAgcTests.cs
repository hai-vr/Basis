using NUnit.Framework;
using System;
using UnityEngine;

namespace Basis.Tests.Voice
{
    public class BasisMicrophoneAgcTests
    {
        private const int SampleRate = 48000;
        private const int FrameSize = 960;
        private const float FrameSeconds = FrameSize / (float)SampleRate;
        private const float TargetRms = 0.1f;

        private const int SpeakFrames = 125;
        private const int PauseFrames = 35;

        private static BasisMicrophoneAgc.Settings DefaultSettings()
        {
            return new BasisMicrophoneAgc.Settings
            {
                TargetRms = TargetRms,
                MaxBoostDb = 24f,
                Attack01 = 0.75f,
                Release01 = 0.85f,
                Headroom = 0.95f,
            };
        }

        private static float Db(float value, float reference)
        {
            return 20f * Mathf.Log10(Mathf.Max(1e-9f, value) / Mathf.Max(1e-9f, reference));
        }

        private sealed class Talker
        {
            private static readonly float ReferenceRms = MeasureReferenceRms();
            private readonly System.Random _rng;
            private readonly float _noiseRms;
            private readonly float _speechAmp;

            public Talker(float speechRms, float noiseRms, int seed)
            {
                _rng = new System.Random(seed);
                _noiseRms = noiseRms;
                _speechAmp = speechRms / ReferenceRms;
            }

            private static void Voice(float[] frame, int frameIndex)
            {
                for (int i = 0; i < frame.Length; i++)
                {
                    double t = (frameIndex * (long)FrameSize + i) / (double)SampleRate;
                    double syllable = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * 4.0 * t));
                    double word = (t % 1.6) < 1.25 ? 1.0 : 0.0;
                    double tone = Math.Sin(2.0 * Math.PI * 130.0 * t)
                                + 0.5 * Math.Sin(2.0 * Math.PI * 260.0 * t)
                                + 0.25 * Math.Sin(2.0 * Math.PI * 390.0 * t);
                    frame[i] = (float)(tone * syllable * syllable * word);
                }
            }

            private static float MeasureReferenceRms()
            {
                float[] scratch = new float[FrameSize];
                double sumSq = 0.0;
                for (int f = 0; f < 50; f++)
                {
                    Voice(scratch, f);
                    for (int i = 0; i < FrameSize; i++) sumSq += (double)scratch[i] * scratch[i];
                }
                return Mathf.Sqrt((float)(sumSq / (50.0 * FrameSize)));
            }

            public void Fill(float[] frame, int frameIndex, bool speaking)
            {
                Fill(frame, frameIndex, speaking, 1f);
            }

            /// <summary>
            /// <paramref name="speechScale"/> attenuates the voice only; room noise stays
            /// where it is, so a value below 1 is a talker trailing off at the end of a
            /// sentence rather than the whole signal fading.
            /// </summary>
            public void Fill(float[] frame, int frameIndex, bool speaking, float speechScale)
            {
                if (speaking)
                {
                    Voice(frame, frameIndex);
                    float amp = _speechAmp * speechScale;
                    for (int i = 0; i < frame.Length; i++) frame[i] *= amp;
                }
                else
                {
                    Array.Clear(frame, 0, frame.Length);
                }

                for (int i = 0; i < frame.Length; i++)
                {
                    frame[i] += (float)(_rng.NextDouble() * 2.0 - 1.0) * _noiseRms * 1.732f;
                }
            }
        }

        private sealed class Result
        {
            public float SpeechOutRms;
            public float PeakOut;
        }

        private static Result Run(
            BasisMicrophoneAgc agc,
            Talker talker,
            BasisMicrophoneAgc.Settings settings,
            int frames,
            int onFrames,
            int offFrames,
            int measureFromFrame,
            int startFrame = 0)
        {
            float[] frame = new float[FrameSize];
            double measuredSumSq = 0.0;
            long measuredCount = 0;
            float peakOut = 0f;

            for (int f = startFrame; f < startFrame + frames; f++)
            {
                int local = f - startFrame;
                int period = onFrames + offFrames;
                bool speaking = period <= 0 || (local % period) < onFrames;

                talker.Fill(frame, f, speaking);

                double sumSq = 0.0;
                float peak = 0f;
                for (int i = 0; i < FrameSize; i++)
                {
                    float v = frame[i];
                    sumSq += (double)v * v;
                    float magnitude = v < 0f ? -v : v;
                    if (magnitude > peak) peak = magnitude;
                }

                float frameRms = Mathf.Sqrt((float)(sumSq / FrameSize));
                float amp = agc.Process(frameRms, peak, settings, FrameSeconds);

                float outPeak = peak * amp;
                if (outPeak > peakOut) peakOut = outPeak;

                if (speaking && local >= measureFromFrame)
                {
                    float outRms = frameRms * amp;
                    measuredSumSq += (double)outRms * outRms;
                    measuredCount++;
                }
            }

            return new Result
            {
                SpeechOutRms = measuredCount > 0 ? Mathf.Sqrt((float)(measuredSumSq / measuredCount)) : 0f,
                PeakOut = peakOut,
            };
        }

        private static Result Converge(float speechRms, float noiseRms, int seed, out BasisMicrophoneAgc agc)
        {
            agc = new BasisMicrophoneAgc();
            var talker = new Talker(speechRms, noiseRms, seed);
            return Run(agc, talker, DefaultSettings(), 750, SpeakFrames, PauseFrames, 500);
        }

        [Test]
        public void TalkersAcross34dBOfInputLevel_LandOnTheSameOutputLevel()
        {
            float[] inputLevels = { 0.010f, 0.040f, 0.150f, 0.400f };
            float[] outputs = new float[inputLevels.Length];

            for (int i = 0; i < inputLevels.Length; i++)
            {
                outputs[i] = Converge(inputLevels[i], inputLevels[i] * 0.02f, i + 1, out _).SpeechOutRms;
            }

            float quietest = Mathf.Min(Mathf.Min(outputs[0], outputs[1]), Mathf.Min(outputs[2], outputs[3]));
            for (int i = 0; i < outputs.Length; i++)
            {
                Assert.Less(Mathf.Abs(Db(outputs[i], quietest)), 1.5f,
                    $"input {inputLevels[i]} settled at {outputs[i]:F4}, others at {quietest:F4}");
            }
        }

        [Test]
        public void SameVoiceInNoisierRooms_StillLandsOnTheSameOutputLevel()
        {
            float[] noiseFractions = { 0.005f, 0.02f, 0.08f };
            float[] outputs = new float[noiseFractions.Length];

            for (int i = 0; i < noiseFractions.Length; i++)
            {
                outputs[i] = Converge(0.04f, 0.04f * noiseFractions[i], 20 + i, out _).SpeechOutRms;
            }

            float quietest = Mathf.Min(outputs[0], Mathf.Min(outputs[1], outputs[2]));
            for (int i = 0; i < outputs.Length; i++)
            {
                Assert.Less(Mathf.Abs(Db(outputs[i], quietest)), 1.5f,
                    $"noise {noiseFractions[i]:P1} settled at {outputs[i]:F4}, others at {quietest:F4}");
            }
        }

        [Test]
        public void ConvergedOutput_SitsNearTheConfiguredTarget()
        {
            Result result = Converge(0.04f, 0.04f * 0.02f, 4, out _);
            float offset = Db(result.SpeechOutRms, TargetRms);

            Assert.Greater(offset, -8f, $"output {result.SpeechOutRms:F4} too far below target {TargetRms}");
            Assert.Less(offset, 2f, $"output {result.SpeechOutRms:F4} above target {TargetRms}");
        }

        [Test]
        public void QuietTalker_IsBoostedUpToTheCeiling()
        {
            Converge(0.010f, 0.010f * 0.02f, 5, out BasisMicrophoneAgc agc);

            Assert.Greater(agc.GainDb, 12f, "quiet talker was not boosted");
            Assert.LessOrEqual(agc.GainDb, 24.01f, "boost exceeded the configured ceiling");
        }

        [Test]
        public void LoudTalker_IsBroughtDownAndStaysUnderHeadroom()
        {
            Result result = Converge(0.400f, 0.400f * 0.02f, 6, out BasisMicrophoneAgc agc);

            Assert.Less(agc.GainDb, -6f, "loud talker was not attenuated");
            Assert.LessOrEqual(result.PeakOut, 0.951f, "output exceeded headroom");
        }

        [Test]
        public void GainHoldsAcrossAPause_SoTheNextUtteranceStartsAtLevel()
        {
            var agc = new BasisMicrophoneAgc();
            var talker = new Talker(0.010f, 0.010f * 0.02f, 7);
            var settings = DefaultSettings();

            Result converged = Run(agc, talker, settings, 400, SpeakFrames, PauseFrames, 300);
            float gainBeforePause = agc.GainDb;

            Run(agc, talker, settings, 150, 0, 150, 0, startFrame: 400);
            float gainAfterPause = agc.GainDb;

            Assert.Less(Mathf.Abs(gainAfterPause - gainBeforePause), 0.5f,
                $"gain drifted across a 3s pause: {gainBeforePause:F2} dB -> {gainAfterPause:F2} dB");

            Result resumed = Run(agc, talker, settings, 100, SpeakFrames, PauseFrames, 0, startFrame: 550);

            Assert.Less(Mathf.Abs(Db(resumed.SpeechOutRms, converged.SpeechOutRms)), 1.5f,
                $"resumed at {resumed.SpeechOutRms:F4} after converging to {converged.SpeechOutRms:F4}");
        }

        [Test]
        public void SteadyRoomNoise_NeverMovesTheGain()
        {
            var agc = new BasisMicrophoneAgc();
            var talker = new Talker(1f, 0.004f, 8);

            Run(agc, talker, DefaultSettings(), 500, 0, 500, 0);

            Assert.IsFalse(agc.HasSpeechEstimate, "noise was mistaken for speech");
            Assert.AreEqual(0f, agc.GainDb, 0.01f, "noise-only input moved the gain");
        }

        [Test]
        public void TalkerWhoLeansBack_IsBroughtBackUp()
        {
            var agc = new BasisMicrophoneAgc();
            var settings = DefaultSettings();
            var near = new Talker(0.080f, 0.080f * 0.02f, 11);
            var far = new Talker(0.012f, 0.080f * 0.02f, 11);

            Result nearResult = Run(agc, near, settings, 400, SpeakFrames, PauseFrames, 300);
            Result farResult = Run(agc, far, settings, 500, SpeakFrames, PauseFrames, 400, startFrame: 400);

            Assert.Less(Mathf.Abs(Db(farResult.SpeechOutRms, nearResult.SpeechOutRms)), 2.5f,
                $"after leaning back the talker settled at {farResult.SpeechOutRms:F4}, was {nearResult.SpeechOutRms:F4}");
        }

        [Test]
        public void Reset_DropsTheHeldGain()
        {
            Converge(0.010f, 0.010f * 0.02f, 9, out BasisMicrophoneAgc agc);
            Assert.Greater(agc.GainDb, 6f);

            agc.Reset();

            Assert.AreEqual(0f, agc.GainDb, 0.001f);
            Assert.IsFalse(agc.HasSpeechEstimate);
        }

        [Test]
        public void NoiseFloorTracker_FindsTheRoomFloorAndIsNotDraggedUpBySpeech()
        {
            var tracker = new BasisNoiseFloorTracker();
            var talker = new Talker(0.05f, 0.002f, 30);
            float[] frame = new float[FrameSize];

            for (int f = 0; f < 500; f++)
            {
                bool speaking = (f % (SpeakFrames + PauseFrames)) < SpeakFrames;
                talker.Fill(frame, f, speaking);

                double sumSq = 0.0;
                for (int i = 0; i < FrameSize; i++) sumSq += (double)frame[i] * frame[i];

                tracker.Update(Mathf.Sqrt((float)(sumSq / FrameSize)), FrameSeconds);
            }

            Assert.Less(Mathf.Abs(Db(tracker.NoiseFloor, 0.002f)), 4f,
                $"tracked floor {tracker.NoiseFloor:F5} vs actual room noise 0.002");
        }

        [Test]
        public void NoiseFloorTracker_RisesWhenTheRoomGetsLouder()
        {
            var tracker = new BasisNoiseFloorTracker();

            for (int f = 0; f < 100; f++) tracker.Update(0.001f, FrameSeconds);
            float quiet = tracker.NoiseFloor;

            for (int f = 0; f < 200; f++) tracker.Update(0.02f, FrameSeconds);

            Assert.Less(quiet, 0.002f);
            Assert.Greater(tracker.NoiseFloor, 0.01f, "floor never followed the room getting louder");
        }

        [TestCase(1024, 48000)]
        [TestCase(512, 48000)]
        [TestCase(1024, 44100)]
        [TestCase(256, 16000)]
        public void ConvergesAtReceiverCallbackSizes(int callbackFrames, int sampleRate)
        {
            var agc = new BasisMicrophoneAgc();
            var settings = DefaultSettings();
            var talker = new Talker(0.02f, 0.02f * 0.02f, 40);

            float callbackSeconds = callbackFrames / (float)sampleRate;
            int totalCallbacks = Mathf.CeilToInt(15f / callbackSeconds);
            int measureFrom = Mathf.CeilToInt(10f / callbackSeconds);

            float[] frame = new float[FrameSize];
            double measuredSumSq = 0.0;
            long measuredCount = 0;

            for (int c = 0; c < totalCallbacks; c++)
            {
                float t = c * callbackSeconds;
                bool speaking = (t % 3.2f) < 2.5f;
                talker.Fill(frame, c, speaking);

                double sumSq = 0.0;
                float peak = 0f;
                for (int i = 0; i < FrameSize; i++)
                {
                    float v = frame[i];
                    sumSq += (double)v * v;
                    float magnitude = v < 0f ? -v : v;
                    if (magnitude > peak) peak = magnitude;
                }

                float frameRms = Mathf.Sqrt((float)(sumSq / FrameSize));
                float amp = agc.Process(frameRms, peak, settings, callbackSeconds);

                if (speaking && c >= measureFrom)
                {
                    float outRms = frameRms * amp;
                    measuredSumSq += (double)outRms * outRms;
                    measuredCount++;
                }
            }

            float settled = measuredCount > 0 ? Mathf.Sqrt((float)(measuredSumSq / measuredCount)) : 0f;
            float offset = Db(settled, TargetRms);

            Assert.Greater(offset, -9f, $"{callbackFrames}@{sampleRate} settled at {settled:F4}");
            Assert.Less(offset, 2f, $"{callbackFrames}@{sampleRate} settled at {settled:F4}");
        }

        [Test]
        public void PlayerSettingsUpgrade_TurnsNormalizationOffForOlderRecords()
        {
            // v6/v7 forced normalisation on for everyone; the v8 upgrade clears it again.
            var legacy = new BasisPlayerSettingsData { UUID = "abc", VolumeLevel = 1f, Version = 7, NormalizeLoudness = true };
            legacy.UpgradeSchema();

            Assert.IsFalse(legacy.NormalizeLoudness, "upgraded record kept the old forced-on normalization");
            Assert.AreEqual(BasisPlayerSettingsData.CurrentVersion, legacy.Version);

            var current = new BasisPlayerSettingsData { UUID = "abc", VolumeLevel = 1f, Version = BasisPlayerSettingsData.CurrentVersion, NormalizeLoudness = true };
            current.UpgradeSchema();

            Assert.IsTrue(current.NormalizeLoudness, "a current-version opt-in was overwritten");
        }

        [Test]
        public void PlayerSettingsDefaults_LeaveNormalizationOff()
        {
            var fresh = new BasisPlayerSettingsData("abc", 1.0f, true, true);

            Assert.IsFalse(fresh.NormalizeLoudness, "a new player record opted in to normalization");
            Assert.IsFalse(BasisPlayerSettingsData.Default.NormalizeLoudness, "the default record opted in to normalization");
        }

        [Test]
        public void ZeroBoostCeiling_LeavesAQuietTalkerAlone()
        {
            var agc = new BasisMicrophoneAgc();
            var talker = new Talker(0.010f, 0.010f * 0.02f, 10);

            var settings = DefaultSettings();
            settings.MaxBoostDb = 0f;

            Run(agc, talker, settings, 400, SpeakFrames, PauseFrames, 300);

            Assert.IsTrue(agc.HasSpeechEstimate, "speech was never detected");
            Assert.LessOrEqual(agc.GainDb, 0.01f);
        }

        private const int OnsetFrames = 15;

        /// <summary>
        /// Steps whole utterances and returns, for each one after
        /// <paramref name="skipUtterances"/>, how much hotter the gain was over the
        /// utterance's first 0.3s than over its body.
        /// </summary>
        private static float WorstOnsetExcessDb(
            BasisMicrophoneAgc agc,
            Talker talker,
            BasisMicrophoneAgc.Settings settings,
            int utterances,
            int onFrames,
            int offFrames,
            int skipUtterances,
            int trailOffFrames = 0)
        {
            float[] frame = new float[FrameSize];
            int frameIndex = 0;
            float worst = float.NegativeInfinity;

            for (int u = 0; u < utterances; u++)
            {
                float onsetGain = float.NegativeInfinity;
                double bodySum = 0.0;
                int bodyCount = 0;

                for (int p = 0; p < onFrames; p++)
                {
                    int intoTail = p - (onFrames - trailOffFrames);
                    float scale = intoTail <= 0
                        ? 1f
                        : Mathf.Pow(10f, -2f * intoTail / Mathf.Max(1, trailOffFrames));

                    StepOne(agc, talker, settings, frame, frameIndex++, true, scale);

                    if (p < OnsetFrames)
                    {
                        if (agc.GainDb > onsetGain) onsetGain = agc.GainDb;
                    }
                    else if (intoTail <= 0)
                    {
                        bodySum += agc.GainDb;
                        bodyCount++;
                    }
                }

                for (int p = 0; p < offFrames; p++)
                {
                    StepOne(agc, talker, settings, frame, frameIndex++, false);
                }

                if (u >= skipUtterances && bodyCount > 0)
                {
                    float excess = onsetGain - (float)(bodySum / bodyCount);
                    if (excess > worst) worst = excess;
                }
            }

            return worst;
        }

        private static void StepOne(
            BasisMicrophoneAgc agc,
            Talker talker,
            BasisMicrophoneAgc.Settings settings,
            float[] frame,
            int frameIndex,
            bool speaking,
            float speechScale = 1f)
        {
            talker.Fill(frame, frameIndex, speaking, speechScale);

            double sumSq = 0.0;
            float peak = 0f;
            for (int i = 0; i < FrameSize; i++)
            {
                float v = frame[i];
                sumSq += (double)v * v;
                float magnitude = v < 0f ? -v : v;
                if (magnitude > peak) peak = magnitude;
            }

            agc.Process(Mathf.Sqrt((float)(sumSq / FrameSize)), peak, settings, FrameSeconds);
        }

        [Test]
        public void ColdStart_DoesNotBoostOffTheAttackOfTheFirstWord()
        {
            var agc = new BasisMicrophoneAgc();
            var talker = new Talker(0.010f, 0.010f * 0.02f, 50);
            var settings = DefaultSettings();
            float[] frame = new float[FrameSize];

            for (int f = 0; f < 400 && !agc.HasSpeechEstimate; f++)
            {
                StepOne(agc, talker, settings, frame, f, true);
            }

            Assert.IsTrue(agc.HasSpeechEstimate, "speech was never detected");
            Assert.LessOrEqual(agc.GainDb, 0.01f,
                $"cold start boosted to {agc.GainDb:F2} dB off a single onset frame");
        }

        [Test]
        public void FirstUtterance_NeverOpensLouderThanItSettles()
        {
            var agc = new BasisMicrophoneAgc();
            var talker = new Talker(0.010f, 0.010f * 0.02f, 51);

            float excess = WorstOnsetExcessDb(agc, talker, DefaultSettings(), 1, SpeakFrames, PauseFrames, 0);

            Assert.Less(excess, 2f, $"the first utterance opened {excess:F2} dB above its own body");
        }

        [Test]
        public void UtteranceOnsets_DoNotOpenHotterThanTheUtteranceBody()
        {
            var agc = new BasisMicrophoneAgc();
            var talker = new Talker(0.030f, 0.030f * 0.02f, 52);

            float excess = WorstOnsetExcessDb(agc, talker, DefaultSettings(), 8, SpeakFrames, PauseFrames, 2);

            Assert.Less(excess, 2f, $"an utterance opened {excess:F2} dB above its own body");
        }

        [Test]
        public void SentencesThatTrailOff_DoNotMakeTheNextOneOpenHot()
        {
            var agc = new BasisMicrophoneAgc();
            var talker = new Talker(0.030f, 0.030f * 0.02f, 54);

            float excess = WorstOnsetExcessDb(agc, talker, DefaultSettings(), 8, SpeakFrames, PauseFrames, 2,
                trailOffFrames: 12);

            Assert.Less(excess, 2f,
                $"after a sentence trailed off the next one opened {excess:F2} dB above its own body");
        }

        [Test]
        public void ASilentGapNeverRaisesTheGain()
        {
            var agc = new BasisMicrophoneAgc();
            var talker = new Talker(0.030f, 0.030f * 0.02f, 53);
            var settings = DefaultSettings();
            float[] frame = new float[FrameSize];
            int frameIndex = 0;

            for (int f = 0; f < 400; f++) StepOne(agc, talker, settings, frame, frameIndex++, true);
            float before = agc.GainDb;

            float highest = before;
            for (int f = 0; f < 100; f++)
            {
                StepOne(agc, talker, settings, frame, frameIndex++, false);
                if (agc.GainDb > highest) highest = agc.GainDb;
            }

            Assert.Less(highest - before, 0.05f,
                $"gain climbed {highest - before:F2} dB during a 2s gap ({before:F2} -> {highest:F2})");
        }
    }
}
