using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking.NetworkedAvatar;
using OpusSharp.Core;
using OpusSharp.Core.Extensions;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace Basis.Scripts.Networking.Receivers
{
    [System.Serializable]
    public class BasisAudioReceiver
    {
        public BasisRemoteAudioDriver BasisRemoteVisemeAudioDriver = null;
        public AudioSource audioSource;
        public BasisAudioAndVisemeDriver visemeDriver = new BasisAudioAndVisemeDriver();
        public BasisVoiceRingBuffer InOrderRead = new BasisVoiceRingBuffer();
        public bool IsPlaying = false;
        public float[] pcmBuffer = new float[RemoteOpusSettings.SampleLength];
        public int pcmLength;
        public byte lastReadIndex = 0;
        public Transform AudioSourceTransform;
        public float[] resampledSegment;
        public volatile bool HasTransform = false;
        public BasisNetworkReceiver BasisNetworkReceiver;
        //everything can safely share the same silent data as we only copy it.
        public static float[] silentData;
        public static int outputSampleRate;
        public OpusDecoder decoder = new OpusDecoder(RemoteOpusSettings.NetworkSampleRate, RemoteOpusSettings.Channels);
        public void OnDecode(byte[] data, int length)
        {
            if (HasTransform)
            {
                pcmLength = decoder.Decode(data, length, pcmBuffer, RemoteOpusSettings.NetworkSampleRate, false);
                InOrderRead.Add(pcmBuffer, pcmLength, true);
                AudioSourceSet();
            }
        }
        public void AudioSourceSet()
        {
            BasisDeviceManagement.EnqueueOnMainThread(() =>
            {
                if (HasTransform)
                {
                    if (InOrderRead.HasRealAudio)
                    {
                        if (audioSource.enabled == false)
                        {
                            audioSource.enabled = true;
                        }
                    }
                    else
                    {
                        if (audioSource.enabled)
                        {
                            audioSource.enabled = false;
                        }
                    }
                }
            });
        }
        public void OnDecodeSilence()
        {
            if (HasTransform)
            {
                InOrderRead.Add(silentData, RemoteOpusSettings.FrameSize, false);
                AudioSourceSet();
            }
        }
        public async void LoadAudioSource(BasisNetworkPlayer networkedPlayer,Transform MouthParent)
        {
            if (AudioSourceTransform == null)
            {
                AudioSourceTransform = BasisAudioRemoteSource.RequestAudio(MouthParent).transform;
                AudioSourceTransform.name = $"[Audio] {BasisNetworkReceiver.Player.DisplayName}";
                if (audioSource == null)
                {
                    audioSource = BasisHelpers.GetOrAddComponent<AudioSource>(AudioSourceTransform.gameObject);
                    audioSource.loop = true;
                    // Initialize settings and audio source
                    audioSource.clip = BasisAudioClipPool.Get(networkedPlayer.playerId);
                }
                audioSource.Play();
                HasTransform= true;
            }
            IsPlaying = true;
            AvatarChanged(networkedPlayer);
            BasisPlayerSettingsData BasisPlayerSettingsData = await BasisPlayerSettingsManager.RequestPlayerSettings(networkedPlayer.Player.UUID);
            ChangeRemotePlayersVolumeSettings(BasisPlayerSettingsData.VolumeLevel);
        }
        public void UnloadAudioSource()
        {
            HasTransform = false;
            if (audioSource != null && audioSource.clip != null)
            {
                audioSource.Stop();
                BasisAudioClipPool.Return(audioSource.clip);
            }
            if (AudioSourceTransform != null)
            {
                BasisAudioRemoteSource.Return(AudioSourceTransform.gameObject);
                AudioSourceTransform = null;
                BasisRemoteVisemeAudioDriver = null;
            }
            IsPlaying = false;
        }
        public void Initalize(BasisNetworkReceiver networkedPlayer)
        {
#if UNITY_SERVER
       return;
#endif
            outputSampleRate = UnityEngine.AudioSettings.outputSampleRate;
            if (silentData == null)
            {
                silentData = new float[RemoteOpusSettings.FrameSize];
            }
            BasisNetworkReceiver = networkedPlayer;
        }
        public void OnDestroy()
        {
            // Unsubscribe from events on destroy
            if (decoder != null)
            {
                decoder.Dispose();
                decoder = null;
            }
            UnloadAudioSource();
        }
        public void AvatarChanged(BasisNetworkPlayer networkedPlayer)
        {
#if UNITY_SERVER
       return;
#endif
            if (audioSource != null)
            {
                // Ensure viseme driver is initialized for audio processing
                visemeDriver.TryInitialize(networkedPlayer.Player);
                if (BasisRemoteVisemeAudioDriver == null)
                {
                    BasisRemoteVisemeAudioDriver = BasisHelpers.GetOrAddComponent<BasisRemoteAudioDriver>(audioSource.gameObject);
                }
                BasisRemoteVisemeAudioDriver.BasisAudioReceiver = this;
                BasisRemoteVisemeAudioDriver.Initalize(visemeDriver);
            }
        }
        public void StopAudio()
        {
#if UNITY_SERVER
       return;
#endif
            UnloadAudioSource();
        }
        public void StartAudio()
        {
            // Conservative initial sizes; will grow once and then reuse.
            _inputScratch = new float[1024];
            _resampleScratch = new float[1024];
            _cachedOutputRate = outputSampleRate;
            _resampleRatio = (float)RemoteOpusSettings.NetworkSampleRate / _cachedOutputRate;
#if UNITY_SERVER
       return;
#endif
            if (BasisNetworkReceiver != null)
            {
                LoadAudioSource(BasisNetworkReceiver, BasisNetworkReceiver.RemotePlayer.MouthTransform);
            }
        }
        public void ChangeRemotePlayersVolumeSettings(float volume = 1.0f,float dopplerLevel = 0,float spatialBlend = 1.0f,bool spatialize = true,bool spatializePostEffects = true)
        {
            // Safety check for audio source
            if (audioSource == null)
            {
                Debug.LogWarning("AudioSource is null. Cannot apply volume settings.");
                return;
            }

            // Apply spatial audio settings
            audioSource.spatialize = spatialize;
            audioSource.spatializePostEffects = spatializePostEffects;
            audioSource.spatialBlend = Mathf.Clamp01(spatialBlend);
            audioSource.dopplerLevel = Mathf.Max(0f, dopplerLevel); // Doppler should not be negative

            // Determine gain value
            short gain;

            if (volume <= 0f)
            {
                audioSource.volume = 0f;
                gain = 256; // Mute gain for Opus (e.g., 256 = silence)
            }
            else if (volume <= 1f)
            {
                audioSource.volume = volume;
                gain = 1024; // Normal gain
            }
            else
            {
                audioSource.volume = 1f;
                gain = (short)Mathf.Clamp(volume * 1024f, 1024f, short.MaxValue); // Prevent overflow
            }

            // Log for debugging if needed
            //Debug.Log($"[AudioDebug] Set Volume: {volume}, UnityVolume: {audioSource.volume}, Gain: {gain}, Spatialize: {spatialize}");

            // Apply gain to decoder
            if (decoder != null)
            {
                OpusDecoderExtensions.SetGain(decoder, gain);
            }
            else
            {
                BasisDebug.LogWarning("Decoder is null. Cannot apply gain.");
            }
        }
        private float[] _inputScratch;      // big enough for the largest chunk we pull
        private int _cachedOutputRate = -1;
        private float _resampleRatio = 1f;
        private float[] _resampleScratch;   // big enough for the largest 'frames' we output
        public void OnAudioFilterRead(float[] data, int channels, int length)
        {
            // Unity’s official signature is (float[] data, int channels); you’ve added 'length'.
            // We'll trust 'length' == data.Length for your setup.
            int frames = length / channels;

            if (InOrderRead.IsEmpty)
            {
                // No voice data: write silence without allocating
                Array.Clear(data, 0, length);
                return;
            }

            // Recompute ratio only when sample rate changes (avoids division each callback)
            if (_cachedOutputRate != outputSampleRate)
            {
                _cachedOutputRate = outputSampleRate;
                _resampleRatio = (float)RemoteOpusSettings.NetworkSampleRate / _cachedOutputRate;
            }

            if (RemoteOpusSettings.NetworkSampleRate == _cachedOutputRate)
            {
                ProcessNoResample(data, frames, channels);
            }
            else
            {
                ProcessResample(data, frames, channels);
            }
        }

        private void EnsureCapacity(ref float[] buf, int needed)
        {
            if (buf.Length < needed)
            {
                // grow to next power-of-two-ish to avoid repeated resizes
                int newSize = 1;
                while (newSize < needed) newSize <<= 1;
                buf = new float[newSize];
            }
        }

        private void ProcessNoResample(float[] data, int frames, int channels)
        {
            // Pull 'frames' mono samples
            EnsureCapacity(ref _inputScratch, frames);
            InOrderRead.Remove(frames, out float[] segment);

            // Copy into our scratch if the InOrderRead returns a pooled array we must return immediately
            // If Remove already gives us exactly the buffer to use, you can avoid this copy:
            //   var mono = segment;
            // Here I'll copy into _inputScratch to keep the code safe and predictable.
            Buffer.BlockCopy(segment, 0, _inputScratch, 0, frames * sizeof(float));

            int idx = 0;
            for (int f = 0; f < frames; f++)
            {
                float sample = _inputScratch[f];
                // Multiply into each channel sample in-place
                for (int c = 0; c < channels; c++)
                {
                    float v = data[idx] * sample;
                    data[idx++] = FastClamp(v);
                }
            }

            InOrderRead.BufferedReturn.Enqueue(segment);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float FastClamp(float x)  // cheaper than Math.Clamp in tight loops
        {
            if (x > 1f) return 1f;
            if (x < -1f) return -1f;
            return x;
        }
        private void ProcessResample(float[] data, int frames, int channels)
        {
            // How many network frames we need to cover these output frames
            float ratio = _resampleRatio; // network / output
            int neededFrames = (int)Mathf.Ceil(frames * ratio);

            EnsureCapacity(ref _inputScratch, neededFrames);
            EnsureCapacity(ref _resampleScratch, frames);

            InOrderRead.Remove(neededFrames, out float[] inputSegment);

            // Copy to scratch (see note in ProcessNoResample)
            Buffer.BlockCopy(inputSegment, 0, _inputScratch, 0, neededFrames * sizeof(float));

            // Linear interpolation via phase accumulator
            // phase advances in "network samples" per output sample
            double phase = 0.0;
            double step = ratio;
            int maxIndex = neededFrames - 1; // last valid low index
            for (int f = 0; f < frames; f++)
            {
                int iLow = (int)phase;                // floor
                double frac = phase - iLow;           // [0,1)
                int iHigh = iLow + 1;

                // Bound the high index once; no branch if inside range
                float sLow = _inputScratch[iLow];
                float sHigh = (iHigh <= maxIndex) ? _inputScratch[iHigh] : 0f;

                // Manual lerp: sLow + frac*(sHigh - sLow)
                _resampleScratch[f] = (float)(sLow + frac * (sHigh - sLow));

                phase += step;
            }

            // Apply to output interleaved buffer
            int idx = 0;
            for (int f = 0; f < frames; f++)
            {
                float sample = _resampleScratch[f];
                for (int c = 0; c < channels; c++)
                {
                    float v = data[idx] * sample;
                    data[idx++] = FastClamp(v);
                }
            }

            InOrderRead.BufferedReturn.Enqueue(inputSegment);
        }
    }
}
