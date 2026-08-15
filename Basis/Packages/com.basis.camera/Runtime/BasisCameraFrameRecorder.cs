using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis
{
    /// <summary>Where a clip recording is in its life. Saving means frames are still draining into the file.</summary>
    public enum BasisCameraRecordingState
    {
        Idle,
        Recording,
        Saving,
    }

    /// <summary>
    /// One recording's encode side, owned by a worker thread: takes raw frames off the main
    /// thread, writes a finished file, and reports progress through counters the panel polls.
    /// </summary>
    public interface IBasisFrameRecorderSession
    {
        /// <summary>Main thread. Copies one readback and queues it; false when full or failed.</summary>
        bool TryAddFrame(NativeArray<byte> rgba, double timestamp);

        /// <summary>Main thread. No more frames are coming; the worker finalises once the queue drains.</summary>
        void CompleteAdding();

        int FramesQueued { get; }
        int FramesEncoded { get; }
        bool IsFinished { get; }
        string FailureMessage { get; }
        string FinalPath { get; }
    }

    /// <summary>
    /// The capture half every clip recorder shares: a paced, aspect-cropped blit of the camera
    /// feed into its own small target, GPU readbacks polled oldest-first (they complete in order,
    /// so frames can never reach the file out of order), and a bounded hand-off into a session.
    /// The GIF and video recorders differ only in the session they plug in and whether the blit
    /// flips — GIF wants rows top-down, JPEG reads the readback's bottom-up rows upright.
    /// </summary>
    public sealed class BasisCameraFrameRecorder
    {
        /// <summary>
        /// Raw bytes allowed in flight — pending readbacks plus frames the encoder has not
        /// consumed. When the encoder falls behind, capture skips ticks instead of queueing
        /// without bound; the skipped time is carried by the frame timestamps, so playback
        /// speed stays true. A byte budget rather than a frame count, because one 4K video
        /// frame outweighs sixteen GIF frames.
        /// </summary>
        private const long MaxPendingBytes = 64L * 1024 * 1024;

        private struct PendingReadback
        {
            public AsyncGPUReadbackRequest Request;
            public double Timestamp;
        }

        private readonly string label;
        private readonly List<PendingReadback> pendingReadbacks = new List<PendingReadback>();
        private RenderTexture target;
        private BasisRenderRateLimiter pacing;
        private double deadline;
        private int frameRate;
        private int maxPendingFrames;
        private bool flipVertically;
        private bool completeSignalled;

        public BasisCameraFrameRecorder(string label)
        {
            this.label = label;
        }

        public BasisCameraRecordingState State { get; private set; } = BasisCameraRecordingState.Idle;
        public IBasisFrameRecorderSession Session { get; private set; }

        public bool IsRecording => State == BasisCameraRecordingState.Recording;

        /// <summary>Capture rate of the running recording, for the camera's render-rate floor.</summary>
        public int FrameRate => State != BasisCameraRecordingState.Idle ? frameRate : 0;

        /// <summary>Frames handed to the GPU for readback this recording.</summary>
        public int FramesCaptured { get; private set; }

        /// <summary>Frames the worker has finished encoding into the file.</summary>
        public int FramesEncoded => Session != null ? Session.FramesEncoded : 0;

        /// <summary>Seconds of recording time left, for the panel's stop-button label.</summary>
        public float SecondsRemaining => State == BasisCameraRecordingState.Recording
            ? Mathf.Max(0f, (float)(deadline - Time.unscaledTimeAsDouble))
            : 0f;

        /// <summary>Filename of the last clip this recorder saved, or null.</summary>
        public string LastFileName { get; private set; }

        /// <summary>Why the last recording failed, or null. Cleared when a new one starts.</summary>
        public string LastFailure { get; private set; }

        /// <summary>Adopts a started session and begins capturing into it.</summary>
        public bool Start(IBasisFrameRecorderSession session, int width, int height, int framesPerSecond, float durationSeconds, bool flip)
        {
            if (State != BasisCameraRecordingState.Idle || session == null) return false;

            target = new RenderTexture(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { sRGB = true })
            {
                name = $"Basis{label}Capture"
            };
            target.Create();

            Session = session;
            frameRate = framesPerSecond;
            flipVertically = flip;
            maxPendingFrames = (int)Mathf.Clamp(MaxPendingBytes / (width * height * 4L), 2, 16);
            pacing = default;
            pendingReadbacks.Clear();
            deadline = Time.unscaledTimeAsDouble + durationSeconds;
            FramesCaptured = 0;
            completeSignalled = false;
            LastFileName = null;
            LastFailure = null;
            State = BasisCameraRecordingState.Recording;
            return true;
        }

        /// <summary>
        /// Ends the capture phase and lets the frames already taken drain into the file. Reached
        /// by the stop button, the duration running out, and a capture lock landing mid-recording.
        /// </summary>
        public void Stop()
        {
            if (State != BasisCameraRecordingState.Recording) return;
            State = BasisCameraRecordingState.Saving;
        }

        /// <summary>Per-frame upkeep, run from the camera's render-phase tick.</summary>
        public void Tick(RenderTexture source, bool captureBlocked)
        {
            if (State == BasisCameraRecordingState.Idle) return;

            if (Session == null)
            {
                pendingReadbacks.Clear();
                ReleaseTarget();
                State = BasisCameraRecordingState.Idle;
                return;
            }

            if (State == BasisCameraRecordingState.Recording)
            {
                if (captureBlocked
                    || Time.unscaledTimeAsDouble >= deadline
                    || Session.FailureMessage != null)
                {
                    Stop();
                }
                else
                {
                    CaptureFrameIfDue(source);
                }
            }

            DrainReadbacks(blocking: false);

            if (State == BasisCameraRecordingState.Saving)
            {
                if (pendingReadbacks.Count == 0 && !completeSignalled)
                {
                    Session.CompleteAdding();
                    completeSignalled = true;
                }
                if (Session.IsFinished) Finish();
            }
        }

        /// <summary>
        /// Teardown for a camera that closes mid-recording. The frames already read back are
        /// handed over synchronously and the worker finishes the file on its own thread — it
        /// holds no engine objects, so the clip still lands even though the camera is gone.
        /// </summary>
        public void Shutdown()
        {
            if (State == BasisCameraRecordingState.Idle) return;

            if (Session != null)
            {
                DrainReadbacks(blocking: true);
                if (!completeSignalled) Session.CompleteAdding();
            }

            pendingReadbacks.Clear();
            Session = null;
            completeSignalled = false;
            ReleaseTarget();
            State = BasisCameraRecordingState.Idle;
        }

        private void CaptureFrameIfDue(RenderTexture source)
        {
            if (source == null || target == null) return;
            if (!pacing.AllowThisFrame(Time.unscaledDeltaTime, frameRate, true)) return;
            if (Session.FramesQueued + pendingReadbacks.Count >= maxPendingFrames) return;

            BasisHandHeldCamera.GetStreamBlitCrop(source, target, out Vector2 scale, out Vector2 offset);
            if (flipVertically)
            {
                // Readback rows come back bottom-up; a format that wants them top-down gets the
                // crop flipped — negate its scale and move the offset to the far edge of the band.
                Graphics.Blit(source, target, new Vector2(scale.x, -scale.y), new Vector2(offset.x, offset.y + scale.y));
            }
            else
            {
                Graphics.Blit(source, target, scale, offset);
            }

            pendingReadbacks.Add(new PendingReadback
            {
                Request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32),
                Timestamp = Time.unscaledTimeAsDouble,
            });
            FramesCaptured++;
        }

        private void DrainReadbacks(bool blocking)
        {
            while (pendingReadbacks.Count > 0)
            {
                PendingReadback pending = pendingReadbacks[0];
                if (blocking) pending.Request.WaitForCompletion();
                if (!pending.Request.done) break;

                pendingReadbacks.RemoveAt(0);
                if (pending.Request.hasError) continue;

                Session?.TryAddFrame(pending.Request.GetData<byte>(), pending.Timestamp);
            }
        }

        private void Finish()
        {
            LastFailure = Session.FailureMessage;
            LastFileName = LastFailure == null ? System.IO.Path.GetFileName(Session.FinalPath) : null;

            if (LastFailure != null)
            {
                BasisDebug.LogError($"{label} recording failed: {LastFailure}", BasisDebug.LogTag.Camera);
            }
            else
            {
                BasisDebug.Log($"{label} saved: {Session.FinalPath} ({Session.FramesEncoded} frames).", BasisDebug.LogTag.Camera);
            }

            Session = null;
            ReleaseTarget();
            State = BasisCameraRecordingState.Idle;
        }

        private void ReleaseTarget()
        {
            if (target == null) return;
            target.Release();
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
            target = null;
        }
    }
}
