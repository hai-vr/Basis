using UnityEngine;
using System;
using System.Linq;
using Basis.Scripts.Device_Management;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
public static class BasisLocalMicrophoneDriver
{
    private static int head = 0;
    private static int bufferLength;
    public static bool HasEvents = false;
    public static int PacketSize;
    public static bool UseDenoiser = false;
    public static Action<bool> OnPausedAction;
    private static bool MicrophoneIsStarted = false;
    private static Thread processingThread;
    public static bool isRunning = true;
    private static ManualResetEvent processingEvent = new ManualResetEvent(false);
    private static object processingLock = new object();
    private static int position;
    private static NativeArray<float> PBA;
    private static BasisVolumeAdjustmentJob VAJ;
    private static JobHandle handle;
    public const string MicrophoneState = "MicrophoneState";
    public static Action OnHasAudio;
    public static Action OnHasSilence; // Event triggered when silence is detected
    public static AudioClip clip;
    public static bool IsInitialize = false;
    public static string MicrophoneDevice = null;
    public static float Volume = 1; // Volume adjustment factor, default to 1 (no adjustment)
    [HideInInspector]
    public static float[] microphoneBufferArray;
    [HideInInspector]
    public static float[] processBufferArray;
    [HideInInspector]
    public static float[] rmsValues;
    public static int rmsIndex = 0;
    public static float averageRms;
#if !UNITY_ANDROID && !UNITY_STANDALONE_LINUX
    public static RNNoise.NET.Denoiser Denoiser = new RNNoise.NET.Denoiser();
#endif
    public static int minFreq = 48000;
    public static int maxFreq = 48000;
    public static int SampleRate;
    private static bool ScheduleMainHasAudio;
    private static bool ScheduleMainHasSilence;
    public static Action MainThreadOnHasAudio;
    public static Action MainThreadOnHasSilence; // Event triggered when silence is detected
    private static readonly object _lock = new object();
    public static bool Initialize()
    {
        if (IsInitialize) return true;

        lock (_lock)
        {
            if (IsInitialize) return true;

            try
            {
                RegisterEvents();
                SMDMicrophone.LoadInMicrophoneData(BasisDeviceManagement.StaticCurrentMode);
                ResetMicrophones(SMDMicrophone.SelectedMicrophone);
                ConfigureDenoiser(SMDMicrophone.SelectedDenoiserMicrophone);
                StartProcessingThread();
                IsInitialize = true;
                return true;
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"Microphone Initialization Failed: {ex}");
                DeInitialize(); // Clean up any partial state
                return false;
            }
        }
    }

    public static void DeInitialize()
    {
        lock (_lock)
        {
            if (!IsInitialize) return;

            StopProcessingThread();
            UnregisterEvents();

            StopSelectedMicrophone();
            if (handle.IsCompleted == false)
            {
                handle.Complete();
            }
            if (VAJ.processBufferArray.IsCreated)
            {
                VAJ.processBufferArray.Dispose();
            }

#if !UNITY_ANDROID && !UNITY_STANDALONE_LINUX
            Denoiser?.Dispose();
#endif

            clip = null;
            microphoneBufferArray = null;
            processBufferArray = null;
            rmsValues = null;

            IsInitialize = false;
            BasisDebug.Log("Microphone Driver Deinitialized.");
        }
    }

    private static void RegisterEvents()
    {
        if (HasEvents) return;

        SMDMicrophone.OnMicrophoneChanged += ResetMicrophones;
        SMDMicrophone.OnMicrophoneVolumeChanged += ChangeMicrophoneVolume;
        SMDMicrophone.OnMicrophoneUseDenoiserChanged += ConfigureDenoiser;
        BasisDeviceManagement.OnBootModeChanged += OnBootModeChanged;

        HasEvents = true;
    }

    private static void UnregisterEvents()
    {
        if (!HasEvents) return;

        SMDMicrophone.OnMicrophoneChanged -= ResetMicrophones;
        SMDMicrophone.OnMicrophoneVolumeChanged -= ChangeMicrophoneVolume;
        SMDMicrophone.OnMicrophoneUseDenoiserChanged -= ConfigureDenoiser;
        BasisDeviceManagement.OnBootModeChanged -= OnBootModeChanged;

        HasEvents = false;
    }
    private static void ConfigureDenoiser(bool useDenoiser)
    {
        UseDenoiser = useDenoiser;
        BasisDebug.Log("Setting Denoiser To " + UseDenoiser);
    }
    private static void OnBootModeChanged(string mode)
    {
        ResetMicrophones(SMDMicrophone.SelectedMicrophone);
    }
    public static void ResetMicrophones(string newMicrophone)
    {
        if (string.IsNullOrEmpty(newMicrophone))
        {
            BasisDebug.LogError("Microphone was empty or null");
            return;
        }
        if (Microphone.devices.Length == 0)
        {
            BasisDebug.LogError("No Microphones found!");
            return;
        }
        if (!Microphone.devices.Contains(newMicrophone))
        {
            newMicrophone = Microphone.devices[0];//we have prechecked microphones
        }
        bool isRecording = Microphone.IsRecording(newMicrophone);
        //we should not be in a state where the new microphone is recording already.
        if (isRecording)
        {
            Microphone.End(newMicrophone);
        }
        //we stop the active microphone and also reset the data to the write state,
        //we dont check to see if we have already stopped the microphone that happens internally now.
        StopSelectedMicrophone();

        if (IsPaused)
        {
            BasisDebug.Log("Microphone Is Paused");
        }
        else
        {
            BasisDebug.Log("Starting Microphone :" + newMicrophone);

            Microphone.GetDeviceCaps(newMicrophone, out minFreq, out maxFreq);
            LocalOpusSettings.SetDeviceAudioConfig(maxFreq);

            clip = Microphone.Start(newMicrophone, true, LocalOpusSettings.RecordingFullLength, LocalOpusSettings.MicrophoneSampleRate);
            bufferLength = LocalOpusSettings.RecordingFullLength * LocalOpusSettings.MicrophoneSampleRate;
            LocalOpusSettings.CreateOrResizeArray(bufferLength, ref microphoneBufferArray);
            MicrophoneIsStarted = true;
            LocalOpusSettings.EnsureProcessBuffer(ref processBufferArray, out SampleRate);
            HandleBasisVolumeAdjustmentJob();
            LocalOpusSettings.CreateOrResizeArray(LocalOpusSettings.rmsWindowSize, ref rmsValues);
            PacketSize = SampleRate * 4;
            ChangeMicrophoneVolume(SMDMicrophone.SelectedVolumeMicrophone);
        }
        MicrophoneDevice = newMicrophone;
    }
    public static void HandleBasisVolumeAdjustmentJob()
    {
        if (PBA.IsCreated)
        {
            if (PBA.Length == processBufferArray.Length)
            {
                //dont need todo anything memory is good to use.
            }
            else
            {
                PBA.Dispose();
                PBA = new NativeArray<float>(processBufferArray, Allocator.Persistent);
            }
        }
        else
        {
            PBA = new NativeArray<float>(processBufferArray, Allocator.Persistent);
        }
        VAJ = new BasisVolumeAdjustmentJob
        {
            processBufferArray = PBA,
            Volume = Volume
        };
    }

    private static void StopSelectedMicrophone()
    {
        if (string.IsNullOrEmpty(MicrophoneDevice))
        {
            return;
        }
        bool isRecording = Microphone.IsRecording(MicrophoneDevice);
        if (isRecording)
        {
            Microphone.End(MicrophoneDevice);
            BasisDebug.Log("Stopped Microphone " + MicrophoneDevice);
        }
        MicrophoneDevice = null;
        MicrophoneIsStarted = false;
    }

    public static void ToggleIsPaused()
    {
        IsPaused = !IsPaused;
    }
    public static bool isPaused = false;
    private static bool IsPaused
    {
        get
        {
            return isPaused;
        }
        set
        {
            PlayerPrefs.SetInt(MicrophoneState, isPaused ? 1 : 0);
            isPaused = value;
            if (isPaused)
            {
                StopSelectedMicrophone();
            }
            else
            {
                ResetMicrophones(SMDMicrophone.SelectedMicrophone);
            }
            OnPausedAction?.Invoke(isPaused);
        }
    }
    public static void MicrophoneUpdate()
    {
        if (MicrophoneIsStarted)
        {
            position = Microphone.GetPosition(MicrophoneDevice);
            if (position == head)
            {
                // No new data has been recorded since the last update
                return;
            }

            clip.GetData(microphoneBufferArray, 0);

            // Signal the processing thread to start processing the audio data
            processingEvent.Set();
            lock (_lock)
            {
                if (ScheduleMainHasAudio)
                {
                    MainThreadOnHasAudio?.Invoke();
                    ScheduleMainHasAudio = false;
                }
                else
                {
                    if (ScheduleMainHasSilence)
                    {
                        MainThreadOnHasSilence?.Invoke();
                        ScheduleMainHasSilence = false;
                    }
                }
            }
        }
    }
    private static CancellationTokenSource processingTokenSource;
    static void StartProcessingThread()
    {
        processingTokenSource = new CancellationTokenSource();
        processingThread = new Thread(() =>
        {
            while (!processingTokenSource.IsCancellationRequested)
            {
                processingEvent.WaitOne();
                if (processingTokenSource.IsCancellationRequested) break;

                lock (processingLock)
                {
                    ProcessAudioData(position);
                }

                processingEvent.Reset();
            }
        });
        processingThread.Start();
    }

    public static void StopProcessingThread()
    {
        processingTokenSource?.Cancel();
        processingEvent?.Set();

        if (processingThread != null && processingThread.IsAlive)
        {
            processingThread.Join();
        }

        processingThread = null;
        processingTokenSource?.Dispose();
        processingTokenSource = null;
    }
    public static void ProcessAudioData(int position)
    {
        int dataLength = GetDataLength(bufferLength, head, position);

        while (dataLength >= SampleRate)
        {
            int remain = bufferLength - head;
            if (remain < SampleRate)
            {
                Array.Copy(microphoneBufferArray, head, processBufferArray, 0, remain);
                Array.Copy(microphoneBufferArray, 0, processBufferArray, remain, SampleRate - remain);
            }
            else
            {
                Array.Copy(microphoneBufferArray, head, processBufferArray, 0, SampleRate);
            }

            AdjustVolume();  // Adjust the volume of the audio data

            if (UseDenoiser)
            {
                ApplyDeNoise();  // Apply noise gate before processing the audio
            }

            RollingRMS();

            if (IsTransmitWorthy())
            {
                OnHasAudio?.Invoke();
                lock (_lock)
                {
                    ScheduleMainHasAudio = true;
                }
            }
            else
            {
                OnHasSilence?.Invoke();
                lock (_lock)
                {
                    ScheduleMainHasSilence = true;
                }
            }

            head = (head + SampleRate) % bufferLength;
            dataLength -= SampleRate;
        }
    }
    public static void AdjustVolume()
    {
        VAJ.processBufferArray.CopyFrom(processBufferArray);

        handle = VAJ.Schedule(processBufferArray.Length, 64);
        handle.Complete();

        VAJ.processBufferArray.CopyTo(processBufferArray);
    }
    public static float GetRMS()
    {
        // Use a double for the sum to avoid overflow and precision issues
        double sum = 0.0;

        for (int Index = 0; Index < SampleRate; Index++)
        {
            float value = processBufferArray[Index];
            sum += value * value;
        }

        return Mathf.Sqrt((float)(sum / SampleRate));
    }
    public static int GetDataLength(int bufferLength, int head, int position)
    {
        if (position < head)
        {
            return bufferLength - head + position;
        }
        else
        {
            return position - head;
        }
    }
    public static void ChangeMicrophoneVolume(float volume)
    {
        Volume = volume;
        // Create the job
        VAJ.Volume = Volume;
        BasisDebug.Log($"Set Microphone Volume To {Volume}");
    }
    public static void ApplyDeNoise()
    {
#if !UNITY_ANDROID && !UNITY_STANDALONE_LINUX
        Denoiser.Denoise(processBufferArray);
#endif
    }
    public static void RollingRMS()
    {
        float rms = GetRMS();
        rmsValues[rmsIndex] = rms;
        rmsIndex = (rmsIndex + 1) % LocalOpusSettings.rmsWindowSize;
        averageRms = rmsValues.Average();
    }
    public static bool IsTransmitWorthy()
    {
        return averageRms > LocalOpusSettings.silenceThreshold;
    }
}
