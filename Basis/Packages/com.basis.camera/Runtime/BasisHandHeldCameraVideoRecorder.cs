using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Basis;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

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
    /// On, a recording stops itself after the picked length; off, it runs until stopped by
    /// hand. MJPEG is big — roughly megabytes per second — so an unlimited recording's ceiling
    /// is the disk, and one that fills it ends as a failed recording rather than a saved one.
    /// </summary>
    [NonSerialized]
    public bool VideoRecordingTimeLimit = true;

    private int videoFrameRate = 30;
    private float videoDurationSeconds = 30f;
    private int videoWidth = 1920;
    private int videoQuality = 80;

    private readonly BasisCameraFrameRecorder videoRecorder = new BasisCameraFrameRecorder("Video");

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

    /// <summary>
    /// Starts a recording with the current video settings. Refused while one is already running
    /// or saving, and while an admin has locked capture — the same gate photos go through.
    /// </summary>
    public bool StartVideoRecording()
    {
        if (videoRecorder.State != BasisCameraRecordingState.Idle) return false;
        if (!TryBeginClipRecording("Video", videoWidth, MinVideoWidth, MaxVideoWidth, out int width, out int height, out string timestamp)) return false;

        string finalPath = GetSavePath($"Video_{timestamp}_{width}x{height}.avi");
        var session = new BasisVideoRecorderSession(width, height, videoQuality, videoFrameRate, finalPath);
        if (!session.Start()) return false;

        // No flip: the readback's bottom-up rows are exactly what the JPEG encoder reads as
        // upright — the same reason the MJPEG web stream never flips.
        float duration = VideoRecordingTimeLimit
            ? Mathf.Clamp(videoDurationSeconds, MinVideoDurationSeconds, MaxVideoDurationSeconds)
            : float.PositiveInfinity;
        videoRecorder.Start(session, width, height, videoFrameRate, duration, flip: false);
        UpdateRenderGate();
        AnnounceClipRecording();

        BasisDebug.Log($"Video recording started: {width}x{height} @ {videoFrameRate}fps, " +
            (VideoRecordingTimeLimit ? $"for up to {videoDurationSeconds:0.#}s." : "until stopped."), BasisDebug.LogTag.Camera);
        return true;
    }

    /// <summary>Ends the capture phase and lets the frames already taken drain into the file.</summary>
    public void StopVideoRecording()
    {
        videoRecorder.Stop();
        UpdateRenderGate();
    }

    /// <summary>Per-frame recorder upkeep, run from <see cref="SimulateLate"/>.</summary>
    private void TickVideoRecorder() =>
        videoRecorder.Tick(renderTexture, BasisNetworkModeration.CameraCaptureBlockedLocally);

    private void ShutdownVideoRecorder() => videoRecorder.Shutdown();
}

namespace Basis
{
    /// <summary>
    /// One video recording: a worker thread that JPEG-encodes raw frames and muxes them into an
    /// MJPEG AVI as they arrive, temp file renamed into place when the last frame lands. The
    /// container plays at one fixed rate, so Finish patches in the rate the frames were actually
    /// captured at — a recording that skipped frames under load still plays at wall-clock speed.
    /// </summary>
    public sealed class BasisVideoRecorderSession : IBasisFrameRecorderSession
    {
        private readonly int width;
        private readonly int height;
        private readonly int quality;
        private readonly int nominalFrameRate;
        private readonly string temporaryPath;

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

        public BasisVideoRecorderSession(int width, int height, int quality, int frameRate, string finalPath)
        {
            this.width = width;
            this.height = height;
            this.quality = quality;
            nominalFrameRate = frameRate;
            FinalPath = finalPath;
            temporaryPath = finalPath + ".tmp";
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
            try
            {
                file = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var writer = new BasisMjpegAviWriter(file, width, height, nominalFrameRate);

                double firstTimestamp = 0;
                double lastTimestamp = 0;

                while (true)
                {
                    if (!pendingFrames.TryDequeue(out QueuedFrame frame))
                    {
                        if (completeAdding) break;
                        frameReady.WaitOne(100);
                        continue;
                    }
                    Interlocked.Decrement(ref framesQueued);

                    // The engine's JPEG encode runs off the main thread — the MJPEG web stream
                    // has leaned on that for months. It allocates the output; at a recording's
                    // frame rate that is acceptable churn on a background thread.
                    byte[] jpeg = ImageConversion.EncodeArrayToJPG(
                        frame.Rgba, GraphicsFormat.R8G8B8A8_SRGB, (uint)width, (uint)height, 0, quality);
                    bufferPool.Push(frame.Rgba);
                    if (jpeg == null || jpeg.Length == 0)
                    {
                        failureMessage = "JPEG encode returned nothing.";
                        break;
                    }

                    if (writer.FrameCount == 0) firstTimestamp = frame.Timestamp;
                    lastTimestamp = frame.Timestamp;

                    writer.WriteFrame(jpeg, jpeg.Length);
                    Interlocked.Increment(ref framesEncoded);
                }

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
                try { file?.Dispose(); } catch (Exception) { }
                if (!success)
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (Exception) { }
                }
                while (pendingFrames.TryDequeue(out _)) { }
                finished = true;
            }
        }
    }
}
