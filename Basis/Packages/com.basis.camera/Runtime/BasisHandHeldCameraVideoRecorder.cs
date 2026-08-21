using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Basis;
using Basis.BasisUI;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.XR;

/// <summary>
/// Video-file recording for the handheld camera: the same paced capture as the GIF recorder,
/// saved as Motion-JPEG in an AVI beside the photos. MJPEG is the one video codec this project
/// can carry embedded and permissive — every frame is an ordinary JPEG from the engine's own
/// encoder, no native codec, and the file opens in any player or editor timeline. The cost is
/// size: it is a capture format to hand to an editor or a quick share, not a distribution codec.
/// </summary>
public partial class BasisHandHeldCamera
{
    public const int MinVideoFrameRate = 10;
    public const int MaxVideoFrameRate = 60;
    public const float MinVideoDurationSeconds = 1f;
    public const float MaxVideoDurationSeconds = 120f;
    public const int MinVideoWidth = 320;
    public const int MaxVideoWidth = 3840;
    public const int MinVideoQuality = 30;
    public const int MaxVideoQuality = 95;

    /// <summary>Widths the panel offers. The height always follows the photo aspect.</summary>
    public static readonly int[] VideoWidthPresets = { 1280, 1920, 2560, 3840 };

    /// <summary>
    /// On, the picked length is where a clip ends — the recording with it, or only that clip
    /// when <see cref="VideoContinuousClips"/> is set; off, one clip runs until stopped by hand.
    /// MJPEG is big — roughly megabytes per second — so an unlimited recording's ceiling is the
    /// disk, and one that fills it ends as a failed recording rather than a saved one.
    /// </summary>
    [NonSerialized]
    public bool VideoRecordingTimeLimit = true;

    /// <summary>
    /// With a time limit set, on: reaching it closes the clip and opens the next one in the same
    /// frame, so a long session lands as a numbered run of files and keeps going until it is
    /// stopped by hand. Nothing falls between them — the join costs one frame interval, the same
    /// gap as any two frames inside a clip. Off, the limit ends the recording.
    ///
    /// <para>Which is the point of it: MJPEG at a megabyte a second gives one unbounded recording
    /// a single enormous file that a player has to read to the end, and loses the lot if the disk
    /// fills. A run of clips is the same footage in pieces an editor can open.</para>
    /// </summary>
    [NonSerialized]
    public bool VideoContinuousClips;

    private int videoFrameRate = 30;
    private float videoDurationSeconds = 30f;
    private int videoWidth = 1920;
    private int videoQuality = 80;

    private readonly BasisCameraFrameRecorder videoRecorder = new BasisCameraFrameRecorder("Video");
    private BasisVideoAudioTap videoAudioTap;

    private static readonly List<XRDisplaySubsystem> ratePinDisplays = new List<XRDisplaySubsystem>();
    private static int ratePinHolders;
    private static bool ratePinSavedLimit;
    private static float ratePinSavedHz;
    private static float ratePinAppliedHz;
    private bool ratePinHeld;

    public static bool IsRenderRatePinnedByRecording => ratePinHolders > 0;

    // The geometry and naming every clip of one run shares. Held for the roll, which has to
    // open its file without the feed's dimensions being free to change underneath it.
    private int videoSegmentWidth;
    private int videoSegmentHeight;
    private string videoSegmentStamp;
    private int videoSegmentIndex;
    private bool videoSegmentNumbered;

    public int VideoRecordingFrameRate => videoFrameRate;
    public float VideoRecordingDurationSeconds => videoDurationSeconds;
    public int VideoRecordingWidth => videoWidth;
    public int VideoRecordingQuality => videoQuality;

    public void SetVideoRecordingFrameRate(int framesPerSecond) =>
        videoFrameRate = Mathf.Clamp(framesPerSecond, MinVideoFrameRate, MaxVideoFrameRate);

    public void SetVideoRecordingDuration(float seconds) =>
        videoDurationSeconds = Mathf.Clamp(seconds, MinVideoDurationSeconds, MaxVideoDurationSeconds);

    public void SetVideoRecordingWidth(int width) =>
        videoWidth = Mathf.Clamp(width, MinVideoWidth, MaxVideoWidth);

    public void SetVideoRecordingQuality(int quality) =>
        videoQuality = Mathf.Clamp(quality, MinVideoQuality, MaxVideoQuality);

    public BasisCameraRecordingState VideoRecordingState => videoRecorder.State;

    /// <summary>True while frames are still being captured — the phase that needs the feed live.</summary>
    public bool IsVideoRecording => videoRecorder.IsRecording;

    /// <summary>Filename of the last video saved by this camera, or null.</summary>
    public string LastVideoFileName => videoRecorder.LastFileName;

    /// <summary>Why the last recording failed, or null. Cleared when a new one starts.</summary>
    public string LastVideoFailure => videoRecorder.LastFailure;

    /// <summary>Frames handed to the GPU for readback this recording.</summary>
    public int VideoFramesCaptured => videoRecorder.FramesCaptured;

    /// <summary>Frames the worker has finished encoding into the file.</summary>
    public int VideoFramesEncoded => videoRecorder.FramesEncoded;

    /// <summary>Seconds of recording time left, for the panel's stop-button label.</summary>
    public float VideoSecondsRemaining => videoRecorder.SecondsRemaining;

    /// <summary>Which clip of a run is recording, counting from one, or zero for a single clip.</summary>
    public int VideoClipNumber => videoRecorder.SegmentNumber;

    /// <summary>
    /// Starts a recording with the current video settings. Refused while one is already running
    /// or saving, and while an admin has locked capture — the same gate photos go through.
    /// </summary>
    public bool StartVideoRecording()
    {
        if (videoRecorder.State != BasisCameraRecordingState.Idle) return false;
        if (!TryBeginClipRecording("Video", videoWidth, MinVideoWidth, MaxVideoWidth, out int width, out int height, out string timestamp)) return false;

        bool continuous = VideoRecordingTimeLimit && VideoContinuousClips;
        videoSegmentWidth = width;
        videoSegmentHeight = height;
        videoSegmentStamp = timestamp;
        videoSegmentIndex = 0;
        videoSegmentNumbered = continuous;

        IBasisFrameRecorderSession session = BeginVideoSegment();
        if (session == null)
        {
            BasisVideoAudioTap.Detach(ref videoAudioTap);
            return false;
        }

        // No flip: the readback's bottom-up rows are exactly what the JPEG encoder reads as
        // upright — the same reason the MJPEG web stream never flips.
        float duration = VideoRecordingTimeLimit
            ? Mathf.Clamp(videoDurationSeconds, MinVideoDurationSeconds, MaxVideoDurationSeconds)
            : float.PositiveInfinity;
        videoRecorder.Start(session, width, height, videoFrameRate, duration, flip: false,
            continuous ? BeginVideoSegment : (Func<IBasisFrameRecorderSession>)null);
        SyncRenderRatePin(true);
        UpdateRenderGate();
        AnnounceClipRecording();

        string length = !VideoRecordingTimeLimit ? "until stopped."
            : continuous ? $"in {videoDurationSeconds:0.#}s clips until stopped."
            : $"for up to {videoDurationSeconds:0.#}s.";
        BasisDebug.Log($"Video recording started: {width}x{height} @ {videoFrameRate}fps, {length}", BasisDebug.LogTag.Camera);
        return true;
    }

    /// <summary>
    /// Opens a clip's file and encoder: the first one at the start of a recording, and one more
    /// at every roll. Returns null when the encoder will not start, which ends the run.
    ///
    /// <para>The listener tap is retargeted rather than detached and re-attached, so the audio
    /// thread always has somewhere to write. A callback that read the old target just as the
    /// swap landed still reaches the clip it belonged to: that clip's worker is told no more
    /// frames are coming a tick or more later, and drains everything buffered before it closes
    /// the file.</para>
    /// </summary>
    private IBasisFrameRecorderSession BeginVideoSegment()
    {
        videoSegmentIndex++;
        string fileName = videoSegmentNumbered
            ? $"Video_{videoSegmentStamp}_{videoSegmentWidth}x{videoSegmentHeight}_part{videoSegmentIndex:000}.avi"
            : $"Video_{videoSegmentStamp}_{videoSegmentWidth}x{videoSegmentHeight}.avi";

        // The recording hears what the player hears: the listener mix, from the camera's own
        // position when Hear From Camera has moved the listener there. No listener to tap —
        // batch tests, headless — records a silent file rather than refusing.
        var audioBuffer = new BasisRecordingAudioBuffer(AudioSettings.outputSampleRate);
        if (videoAudioTap == null) videoAudioTap = BasisVideoAudioTap.Attach(audioBuffer);
        else videoAudioTap.Target = audioBuffer;

        var session = new BasisVideoRecorderSession(videoSegmentWidth, videoSegmentHeight, videoQuality, videoFrameRate,
            GetSavePath(fileName), videoAudioTap != null ? audioBuffer : null, Time.unscaledTimeAsDouble);
        return session.Start() ? session : null;
    }

    /// <summary>Ends the capture phase and lets the frames already taken drain into the file.</summary>
    public void StopVideoRecording()
    {
        videoRecorder.Stop();
        SyncRenderRatePin(false);
        UpdateRenderGate();
    }

    private void SyncRenderRatePin(bool recording)
    {
        if (recording == ratePinHeld) return;
        ratePinHeld = recording;

        if (recording)
        {
            if (ratePinHolders++ != 0) return;

            ratePinSavedLimit = BasisSettingsDefaults.LimitHandHeldCameraRate.RawValue;
            ratePinSavedHz = BasisSettingsDefaults.HandHeldCameraRenderHz.RawValue;
            ratePinAppliedHz = Mathf.Clamp(DisplayRefreshHz(), MinHandHeldRenderHz, MaxHandHeldRenderHz);
            BasisSettingsDefaults.HandHeldCameraRenderHz.SetValueWithoutNotify(ratePinAppliedHz);
            BasisSettingsDefaults.LimitHandHeldCameraRate.SetValueWithoutNotify(true);
            return;
        }

        if (--ratePinHolders > 0) return;

        BasisSettingsDefaults.LimitHandHeldCameraRate.SetValueWithoutNotify(ratePinSavedLimit);
        if (Mathf.Approximately(BasisSettingsDefaults.HandHeldCameraRenderHz.RawValue, ratePinAppliedHz))
        {
            BasisSettingsDefaults.HandHeldCameraRenderHz.SetValueWithoutNotify(ratePinSavedHz);
        }
    }

    private static float DisplayRefreshHz()
    {
        if (XRSettings.isDeviceActive)
        {
            SubsystemManager.GetSubsystems(ratePinDisplays);
            for (int Index = 0; Index < ratePinDisplays.Count; Index++)
            {
                XRDisplaySubsystem display = ratePinDisplays[Index];
                if (display == null || !display.running) continue;
                if (display.TryGetDisplayRefreshRate(out float headsetHz) && headsetHz > 0f) return headsetHz;
            }
        }

        double screenHz = Screen.currentResolution.refreshRateRatio.value;
        return screenHz > 0.0 ? (float)screenHz : 60f;
    }

    /// <summary>Per-frame recorder upkeep, run from <see cref="SimulateLate"/>.</summary>
    private void TickVideoRecorder()
    {
        videoRecorder.Tick(renderTexture, BasisNetworkModeration.CameraCaptureBlockedLocally);
        SyncRenderRatePin(videoRecorder.IsRecording);

        // The tap outlives the capture phase on purpose — the worker is still draining its
        // buffer while Saving — and comes off the listener the moment the file is done.
        if (videoAudioTap != null && videoRecorder.State == BasisCameraRecordingState.Idle)
        {
            BasisVideoAudioTap.Detach(ref videoAudioTap);
        }
    }

    private void ShutdownVideoRecorder()
    {
        SyncRenderRatePin(false);
        BasisVideoAudioTap.Detach(ref videoAudioTap);
        videoRecorder.Shutdown();
    }
}

namespace Basis
{
    /// <summary>
    /// One video recording: a worker thread that JPEG-encodes raw frames and muxes them into an
    /// MJPEG AVI as they arrive, temp file renamed into place when the last frame lands. The
    /// container plays at one fixed rate, so Finish patches in the rate the frames were actually
    /// captured at — a recording that skipped frames under load still plays at wall-clock speed.
    /// With an audio buffer attached, the listener mix is drained beside each frame into a PCM
    /// stream; audio buffered before the first frame's capture moment is skipped, so the two
    /// streams start together.
    /// </summary>
    public sealed class BasisVideoRecorderSession : IBasisFrameRecorderSession
    {
        /// <summary>
        /// Parallel JPEG encodes in flight. This is the pipeline's bottleneck — a large frame
        /// costs tens of milliseconds — and frames are independent, so a few pool threads carry
        /// a resolution and rate one thread cannot. Half the machine, capped, so the encode
        /// never starves the render loop of cores mid-session.
        /// </summary>
        private static readonly int EncodeThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));

        /// <summary>One in-flight frame: the raw input and the JPEG that comes back.</summary>
        private sealed class EncodeSlot
        {
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public byte[] Rgba;
            public byte[] Jpeg;
            public double Timestamp;
            public volatile string Error;
        }

        private readonly ConcurrentStack<EncodeSlot> slotPool = new ConcurrentStack<EncodeSlot>();

        private readonly int width;
        private readonly int height;
        private readonly int quality;
        private readonly int nominalFrameRate;
        private readonly string temporaryPath;
        private readonly BasisRecordingAudioBuffer audio;
        private readonly double audioStartTime;
        private long audioSkipBytes;
        private byte[] audioScratch;

        private readonly ConcurrentQueue<QueuedFrame> pendingFrames = new ConcurrentQueue<QueuedFrame>();
        private readonly ConcurrentStack<byte[]> bufferPool = new ConcurrentStack<byte[]>();
        private readonly AutoResetEvent frameReady = new AutoResetEvent(false);

        private Thread worker;
        private volatile bool completeAdding;
        private int framesQueued;
        private int framesEncoded;
        private volatile bool finished;
        private volatile string failureMessage;

        private struct QueuedFrame
        {
            public byte[] Rgba;
            public double Timestamp;
        }

        public string FinalPath { get; }
        public int FramesQueued => Volatile.Read(ref framesQueued);
        public int FramesEncoded => Volatile.Read(ref framesEncoded);
        public bool IsFinished => finished;
        public string FailureMessage => failureMessage;

        public BasisVideoRecorderSession(int width, int height, int quality, int frameRate, string finalPath,
            BasisRecordingAudioBuffer audio = null, double audioStartTime = 0)
        {
            this.width = width;
            this.height = height;
            this.quality = quality;
            nominalFrameRate = frameRate;
            FinalPath = finalPath;
            temporaryPath = finalPath + ".tmp";
            this.audio = audio;
            this.audioStartTime = audioStartTime;
        }

        public bool Start()
        {
            try
            {
                worker = new Thread(WorkerLoop) { IsBackground = true, Name = "BasisVideoEncoder" };
                worker.Start();
                return true;
            }
            catch (Exception e)
            {
                failureMessage = $"Could not start the encode worker ({e.GetType().Name}: {e.Message})";
                finished = true;
                return false;
            }
        }

        /// <summary>Main thread. Copies one readback into a pooled buffer and queues it for encoding.</summary>
        public bool TryAddFrame(NativeArray<byte> rgba, double timestamp)
        {
            if (completeAdding || failureMessage != null) return false;
            if (rgba.Length != width * height * 4) return false;

            if (!bufferPool.TryPop(out byte[] buffer) || buffer.Length != rgba.Length)
            {
                buffer = new byte[rgba.Length];
            }
            rgba.CopyTo(buffer);

            pendingFrames.Enqueue(new QueuedFrame { Rgba = buffer, Timestamp = timestamp });
            Interlocked.Increment(ref framesQueued);
            frameReady.Set();
            return true;
        }

        /// <summary>Main thread. No more frames are coming; the worker finalises once the queue drains.</summary>
        public void CompleteAdding()
        {
            completeAdding = true;
            frameReady.Set();
        }

        private void WorkerLoop()
        {
            FileStream file = null;
            bool success = false;
            var inFlight = new Queue<EncodeSlot>();
            try
            {
                file = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var writer = new BasisMjpegAviWriter(file, width, height, nominalFrameRate,
                    audio != null ? audio.SampleRate : 0,
                    audio != null ? BasisRecordingAudioBuffer.Channels : 0);
                if (audio != null)
                {
                    audioScratch = new byte[audio.SampleRate * BasisRecordingAudioBuffer.BytesPerSampleFrame];
                }

                double firstTimestamp = 0;
                double lastTimestamp = 0;

                while (failureMessage == null)
                {
                    bool dispatched = false;
                    while (inFlight.Count < EncodeThreads && pendingFrames.TryDequeue(out QueuedFrame frame))
                    {
                        Interlocked.Decrement(ref framesQueued);
                        inFlight.Enqueue(DispatchEncode(frame));
                        dispatched = true;
                    }

                    if (inFlight.Count == 0)
                    {
                        if (completeAdding && pendingFrames.IsEmpty) break;
                        if (!dispatched) frameReady.WaitOne(100);
                        continue;
                    }

                    // Results are taken oldest-first, so however the pool schedules the
                    // encodes, the file gets its frames in capture order.
                    EncodeSlot ready = inFlight.Dequeue();
                    ready.Done.Wait();
                    if (ready.Error != null)
                    {
                        failureMessage = ready.Error;
                        break;
                    }

                    if (writer.FrameCount == 0)
                    {
                        firstTimestamp = ready.Timestamp;
                        // The tap starts buffering when the recording starts, up to a tick
                        // before the first frame is captured; skipping that lead keeps the
                        // sound and the picture starting on the same moment.
                        if (audio != null)
                        {
                            long skipFrames = (long)Math.Max(0, Math.Round((ready.Timestamp - audioStartTime) * audio.SampleRate));
                            audioSkipBytes = skipFrames * BasisRecordingAudioBuffer.BytesPerSampleFrame;
                        }
                    }
                    lastTimestamp = ready.Timestamp;

                    writer.WriteFrame(ready.Jpeg, ready.Jpeg.Length);
                    Interlocked.Increment(ref framesEncoded);
                    RecycleSlot(ready);
                    DrainAudio(writer);
                }

                DrainAudio(writer);

                if (failureMessage == null)
                {
                    double? measuredFps = writer.FrameCount >= 2 && lastTimestamp > firstTimestamp
                        ? (writer.FrameCount - 1) / (lastTimestamp - firstTimestamp)
                        : (double?)null;
                    writer.Finish(measuredFps);
                    file.Flush();
                    file.Dispose();
                    file = null;

                    if (writer.FrameCount == 0)
                    {
                        failureMessage = "No frames were captured.";
                    }
                    else
                    {
                        File.Move(temporaryPath, FinalPath);
                        success = true;
                    }
                }
            }
            catch (Exception e)
            {
                failureMessage = $"{e.GetType().Name}: {e.Message}";
            }
            finally
            {
                // The pool callbacks write into the slots; every one must land before teardown.
                while (inFlight.Count > 0) inFlight.Dequeue().Done.Wait();
                try { file?.Dispose(); } catch (Exception) { }
                if (!success)
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (Exception) { }
                }
                while (pendingFrames.TryDequeue(out _)) { }
                finished = true;
            }
        }

        private EncodeSlot DispatchEncode(QueuedFrame frame)
        {
            if (!slotPool.TryPop(out EncodeSlot slot)) slot = new EncodeSlot();
            slot.Rgba = frame.Rgba;
            slot.Timestamp = frame.Timestamp;
            slot.Jpeg = null;
            slot.Error = null;
            slot.Done.Reset();
            ThreadPool.QueueUserWorkItem(EncodeSlotWork, slot);
            return slot;
        }

        /// <summary>
        /// Pool thread. The engine's JPEG encode runs off the main thread — the MJPEG web
        /// stream has leaned on that for months. It allocates the output; at a recording's
        /// frame rate that is acceptable churn on background threads.
        /// </summary>
        private void EncodeSlotWork(object state)
        {
            var slot = (EncodeSlot)state;
            try
            {
                slot.Jpeg = ImageConversion.EncodeArrayToJPG(
                    slot.Rgba, GraphicsFormat.R8G8B8A8_SRGB, (uint)width, (uint)height, 0, quality);
                if (slot.Jpeg == null || slot.Jpeg.Length == 0)
                {
                    slot.Error = "JPEG encode returned nothing.";
                }
            }
            catch (Exception e)
            {
                slot.Error = $"{e.GetType().Name}: {e.Message}";
            }
            finally
            {
                slot.Done.Set();
            }
        }

        private void RecycleSlot(EncodeSlot slot)
        {
            bufferPool.Push(slot.Rgba);
            slot.Rgba = null;
            slot.Jpeg = null;
            slotPool.Push(slot);
        }

        /// <summary>
        /// Moves everything the listener tap has buffered into the file, minus the lead-in
        /// being skipped for alignment. Called beside every video frame and once at the end,
        /// so audio never pools more than a frame behind the picture.
        /// </summary>
        private void DrainAudio(BasisMjpegAviWriter writer)
        {
            if (audio == null) return;

            int bytes;
            while ((bytes = audio.Read(audioScratch)) > 0)
            {
                int offset = 0;
                if (audioSkipBytes > 0)
                {
                    int skipped = (int)Math.Min(audioSkipBytes, bytes);
                    audioSkipBytes -= skipped;
                    offset = skipped;
                }
                if (bytes - offset > 0)
                {
                    writer.WriteAudio(audioScratch, offset, bytes - offset);
                }
            }
        }
    }
}
