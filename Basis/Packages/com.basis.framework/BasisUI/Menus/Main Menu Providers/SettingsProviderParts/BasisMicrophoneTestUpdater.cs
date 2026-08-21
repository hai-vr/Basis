#if !BASIS_DISABLE_MICROPHONE
using System;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using UnityEngine;

namespace Basis.BasisUI
{
    public class BasisMicrophoneTestUpdater : MonoBehaviour
    {
        public PanelElementDescriptor StatusField;
        public BasisWaveformGraphic Waveform;
        public PanelButton RecordButton;
        public PanelButton PlayButton;
        public PanelButton SaveButton;
        public PanelButton LiveButton;

        private const float MinDb = -50f;
        private const float StatusInterval = 0.1f;
        private const int FreezeAfterUpdates = 3;
        private const int Columns = BasisMicrophoneTestTap.Columns;

        private readonly BasisMicrophoneTestTap tap = new BasisMicrophoneTestTap();
        private readonly float[] peakScratch = new float[Columns];
        private readonly float[] rmsScratch = new float[Columns];
        private readonly float[] peakEnvelope = new float[Columns];
        private readonly float[] rmsEnvelope = new float[Columns];

        private AudioSource playbackSource;
        private AudioClip playbackClip;
        private bool holdingCapture;
        private bool bypassingMute;
        private bool wasRecording;
        private bool wasPlaying;
        private bool envelopeReady;
        private bool showingLivePreview;
        private bool holdingLocalOnly;
        private bool unmutedForTest;
        private bool richTextDisabled;
        private bool layoutFrozen;
        private int statusUpdates;
        private float statusTimer;
        private string lastStatus;
        private string saveMessage;
        private float saveMessageUntil;

        public void Bind(PanelElementDescriptor statusField, BasisWaveformGraphic waveform, PanelButton recordButton, PanelButton playButton, PanelButton saveButton, PanelButton liveButton)
        {
            StatusField = statusField;
            Waveform = waveform;
            RecordButton = recordButton;
            PlayButton = playButton;
            SaveButton = saveButton;
            LiveButton = liveButton;

            if (RecordButton != null) RecordButton.OnClicked += ToggleRecording;
            if (PlayButton != null) PlayButton.OnClicked += TogglePlayback;
            if (SaveButton != null) SaveButton.OnClicked += SaveRecording;
            if (LiveButton != null) LiveButton.OnClicked += ShowLivePreview;

            RefreshButtons();
        }

        private void OnEnable()
        {
            tap.ClearLevels();
            tap.Attach();

            if (!holdingCapture)
            {
                holdingCapture = true;
                BasisLocalMicrophoneDriver.AddCaptureHold();
            }

            BasisFrameClock.AddRequest();
            BasisFrameClock.OnTick += OnFrameTick;

            RefreshButtons();
        }

        private void OnDisable()
        {
            BasisFrameClock.OnTick -= OnFrameTick;
            BasisFrameClock.RemoveRequest();

            EndRecording();
            StopPlayback();
            tap.Detach();

            if (holdingCapture)
            {
                holdingCapture = false;
                BasisLocalMicrophoneDriver.ReleaseCaptureHold();
            }

            ReleaseLocalOnly();
        }

        private void OnDestroy()
        {
            tap.ClearRecording();
            DestroyPlaybackClip();
            if (playbackSource != null) Destroy(playbackSource.gameObject);
            ReleaseLocalOnly();
        }

        private void OnFrameTick()
        {
            bool recording = tap.IsRecording;
            if (wasRecording && !recording)
            {
                EndRecording();
                BuildEnvelope();
                BeginPlayback();
                if (!IsPlaying) ReleaseLocalOnly();
                RefreshButtons();
            }

            bool playing = IsPlaying;
            if (wasPlaying && !playing)
            {
                wasPlaying = false;
                if (Waveform != null) Waveform.SetPlayhead(-1f);
                ReleaseLocalOnly();
                RefreshButtons();
            }

            UpdateWaveform(recording, playing);
            UpdateStatus(recording, playing);
        }

        private bool IsPlaying => playbackSource != null && playbackSource.isPlaying;

        private void ToggleRecording()
        {
            if (tap.IsRecording)
            {
                EndRecording();
                BuildEnvelope();
                BeginPlayback();
                if (!IsPlaying) ReleaseLocalOnly();
                RefreshButtons();
                return;
            }

            StopPlayback();
            DestroyPlaybackClip();
            tap.ClearRecording();
            envelopeReady = false;
            showingLivePreview = false;

            if (!bypassingMute)
            {
                bypassingMute = true;
                BasisLocalMicrophoneDriver.AddMuteBypass();
            }

            HoldLocalOnly();
            ForceUnmute();
            tap.StartRecording(LocalOpusSettings.MicrophoneSampleRate);
            wasRecording = true;
            RefreshButtons();
        }

        private void EndRecording()
        {
            tap.StopRecording();
            wasRecording = false;

            if (!bypassingMute) return;
            bypassingMute = false;
            BasisLocalMicrophoneDriver.ReleaseMuteBypass();
        }

        private void TogglePlayback()
        {
            if (IsPlaying)
            {
                StopPlayback();
                ReleaseLocalOnly();
                RefreshButtons();
                return;
            }

            BeginPlayback();
            RefreshButtons();
        }

        private void BeginPlayback()
        {
            if (!tap.TryCopyRecording(out float[] samples, out int sampleRate)) return;

            EnsurePlaybackSource();
            DestroyPlaybackClip();

            playbackClip = AudioClip.Create("MicrophoneTest", samples.Length, 1, sampleRate, false);
            playbackClip.SetData(samples, 0);

            playbackSource.clip = playbackClip;
            playbackSource.volume = SMModuleAudio.Instance != null ? SMModuleAudio.ActiveMenusVolume : 1f;
            playbackSource.time = 0f;
            playbackSource.Play();
            wasPlaying = true;
            showingLivePreview = false;
        }

        private void StopPlayback()
        {
            if (playbackSource != null && playbackSource.isPlaying) playbackSource.Stop();
            wasPlaying = false;
            if (Waveform != null) Waveform.SetPlayhead(-1f);
        }

        private void EnsurePlaybackSource()
        {
            if (playbackSource != null) return;

            GameObject holder = new GameObject("Microphone Test Playback");
            holder.transform.SetParent(transform, false);
            playbackSource = holder.AddComponent<AudioSource>();
            playbackSource.playOnAwake = false;
            playbackSource.loop = false;
            playbackSource.spatialBlend = 0f;
            playbackSource.dopplerLevel = 0f;
            playbackSource.bypassReverbZones = true;
        }

        private void DestroyPlaybackClip()
        {
            if (playbackClip == null) return;
            if (playbackSource != null) playbackSource.clip = null;
            Destroy(playbackClip);
            playbackClip = null;
        }

        private void BuildEnvelope()
        {
            int count = tap.BuildRecordedEnvelope(peakEnvelope, rmsEnvelope, Columns);
            envelopeReady = count > 0;
            for (int i = 0; i < count; i++)
            {
                peakEnvelope[i] = ToUnit(peakEnvelope[i]);
                rmsEnvelope[i] = ToUnit(rmsEnvelope[i]);
            }
        }

        private void UpdateWaveform(bool recording, bool playing)
        {
            if (Waveform == null) return;

            if (recording)
            {
                int taken = tap.CopyTake(peakScratch, rmsScratch);
                ConvertScratch(taken);
                Waveform.SetBars(peakScratch, rmsScratch, taken);
                Waveform.SetPlayhead(tap.RecordedSeconds / BasisMicrophoneTestTap.MaxRecordSeconds);
                return;
            }

            if (playing && envelopeReady)
            {
                float length = playbackClip != null ? playbackClip.length : 0f;
                Waveform.SetBars(peakEnvelope, rmsEnvelope, Columns);
                Waveform.SetPlayhead(length > 0f ? playbackSource.time / length : 0f);
                return;
            }

            // Playback finished but the take is still loaded: keep it on screen. The live
            // monitor below has nothing to show once the capture hold is released, so falling
            // through to it blanks the graph until Play is pressed again.
            if (envelopeReady && tap.HasRecording && !showingLivePreview)
            {
                Waveform.SetBars(peakEnvelope, rmsEnvelope, Columns);
                Waveform.SetPlayhead(-1f);
                return;
            }

            int count = tap.CopyLevels(peakScratch, rmsScratch);
            ConvertScratch(count);
            Waveform.SetBars(peakScratch, rmsScratch, count);
            Waveform.SetPlayhead(-1f);
        }

        private void ConvertScratch(int count)
        {
            for (int i = 0; i < count; i++)
            {
                peakScratch[i] = ToUnit(peakScratch[i]);
                rmsScratch[i] = ToUnit(rmsScratch[i]);
            }
        }

        private void UpdateStatus(bool recording, bool playing)
        {
            if (StatusField == null) return;

            statusTimer += Time.unscaledDeltaTime;
            if (statusTimer < StatusInterval) return;
            statusTimer = 0f;

            if (!richTextDisabled)
            {
                richTextDisabled = true;
                StatusField.DisableRichText();
            }

            string text;
            if (saveMessage != null && Time.unscaledTime < saveMessageUntil)
            {
                text = saveMessage;
            }
            else if (recording)
            {
                text = BasisLocalization.Get(BasisLocalMicrophoneDriver.isPaused
                    ? "settings.microphone.test.status.recordingMuted"
                    : "settings.microphone.test.status.recordingLocal", tap.RecordedSeconds, BasisMicrophoneTestTap.MaxRecordSeconds);
            }
            else if (playing)
            {
                float length = playbackClip != null ? playbackClip.length : 0f;
                text = BasisLocalization.Get("settings.microphone.test.status.playing", playbackSource.time, length);
            }
            else if (!HasMicrophone())
            {
                text = BasisLocalization.Get("settings.microphone.test.status.noDevice");
            }
            else if (holdingLocalOnly)
            {
                text = BasisLocalization.Get("settings.microphone.test.status.localOnly", tap.RecordedSeconds);
            }
            else if (tap.HasRecording)
            {
                text = BasisLocalization.Get("settings.microphone.test.status.ready", tap.RecordedSeconds);
            }
            else if (tap.FramesPushed == 0)
            {
                text = BasisLocalization.Get("settings.microphone.test.status.waiting");
            }
            else if (BasisLocalMicrophoneDriver.isPaused)
            {
                text = BasisLocalization.Get("settings.microphone.test.status.muted");
            }
            else
            {
                text = BasisLocalization.Get("settings.microphone.test.status.idle");
            }

            if (text != lastStatus)
            {
                lastStatus = text;
                StatusField.SetDescription(text);
            }

            if (layoutFrozen) return;
            statusUpdates++;
            if (statusUpdates < FreezeAfterUpdates) return;
            layoutFrozen = true;
            StatusField.FreezeLayoutSize(230f);
        }

        private void RefreshButtons()
        {
            bool recording = tap.IsRecording;
            bool playing = IsPlaying;
            bool hasMicrophone = HasMicrophone();

            if (RecordButton != null)
            {
                RecordButton.Descriptor.SetTitle(BasisLocalization.Get(recording
                    ? "settings.microphone.test.stop"
                    : "settings.microphone.test.record"));
                RecordButton.SetInteractable(hasMicrophone, BasisLocalization.Get("settings.microphone.test.status.noDevice"));
            }

            if (PlayButton != null)
            {
                PlayButton.Descriptor.SetTitle(BasisLocalization.Get(playing
                    ? "settings.microphone.test.playStop"
                    : "settings.microphone.test.play"));
                PlayButton.SetInteractable(!recording && (playing || tap.HasRecording),
                    BasisLocalization.Get("settings.microphone.test.play.disabled"));
            }

            if (SaveButton != null)
            {
                SaveButton.SetInteractable(!recording && tap.HasRecording,
                    BasisLocalization.Get("settings.microphone.test.save.disabled"));
            }

            if (LiveButton != null)
            {
                LiveButton.SetInteractable(!recording && !playing && envelopeReady && tap.HasRecording && !showingLivePreview,
                    BasisLocalization.Get("settings.microphone.test.live.disabled"));
            }
        }

        // Display only. The microphone is handed back when playback ends, not here; this just
        // returns the graph to the live monitor, which otherwise never reappears once a take exists.
        // The release below is a safety net for the case where playback never started.
        private void HoldLocalOnly()
        {
            if (holdingLocalOnly) return;
            holdingLocalOnly = true;
            BasisTalkModeManager.AddLocalOnlyHold();
        }

        private void ReleaseLocalOnly()
        {
            RestoreMute();
            if (!holdingLocalOnly) return;
            holdingLocalOnly = false;
            BasisTalkModeManager.ReleaseLocalOnlyHold();
        }

        // Recording while muted would capture nothing useful, so take the mute off for the test and
        // hand it back on the way out. The talk mode itself is never touched, so "whatever mic mode
        // you were in" survives on its own.
        private void ForceUnmute()
        {
            if (unmutedForTest || !BasisLocalMicrophoneDriver.isPaused) return;
            unmutedForTest = true;
            BasisLocalMicrophoneDriver.ToggleIsPaused();
        }

        private void RestoreMute()
        {
            if (!unmutedForTest) return;
            unmutedForTest = false;
            if (!BasisLocalMicrophoneDriver.isPaused) BasisLocalMicrophoneDriver.ToggleIsPaused();
        }

        private void ShowLivePreview()
        {
            StopPlayback();
            ReleaseLocalOnly();
            showingLivePreview = true;
            tap.ClearLevels();
            RefreshButtons();
        }

        private void ShowSaveMessage(string text)
        {
            saveMessage = text;
            saveMessageUntil = Time.unscaledTime + 6f;
            statusTimer = StatusInterval;
        }

        private void SaveRecording()
        {
            if (!tap.TryCopyRecording(out float[] samples, out int sampleRate))
            {
                ShowSaveMessage(BasisLocalization.Get("settings.microphone.test.save.failed"));
                return;
            }

            string path = BasisWavFile.BuildPath(DateTime.Now);
            if (BasisWavFile.TryWrite(path, samples, samples.Length, sampleRate, 1, out string error))
            {
                ShowSaveMessage(string.Format(BasisLocalization.Get("settings.microphone.test.save.done"), path));
                BasisDebug.Log($"Saved microphone test recording to {path}");
            }
            else
            {
                ShowSaveMessage(BasisLocalization.Get("settings.microphone.test.save.failed"));
                BasisDebug.LogError($"Failed to save microphone test recording: {error}");
            }
        }

        private static bool HasMicrophone()
        {
            if (!BasisLocalMicrophoneDriver.IsInitialize) return false;
            string[] devices = SMDMicrophone.MicrophoneDevices;
            return devices != null && devices.Length > 0;
        }

        private static float ToUnit(float amplitude)
        {
            if (amplitude <= 0f) return 0f;
            float db = 20f * Mathf.Log10(Mathf.Max(1e-7f, amplitude));
            return Mathf.Clamp01(Mathf.InverseLerp(MinDb, 0f, db));
        }
    }
}
#endif
