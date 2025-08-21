using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Profiler;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Concurrent;
using Unity.Mathematics;
using UnityEngine;
using static SerializableBasis;

namespace Basis.Scripts.Networking.Receivers
{
    [DefaultExecutionOrder(15001)]
    [System.Serializable]
    public class BasisNetworkReceiver : BasisNetworkPlayer
    {
        // ---------- Status / Diagnostics ----------
        public enum ReceiverStatus
        {
            Idle,
            WaitingForFirst,
            WaitingForLast,
            Bootstrapped,
            Ready,
            Interpolating,
            Advancing,
            Applying,
            QueueStarved,
            QueueOverrun,
            ErrorMissingData,
            ErrorNaNInterpolation,
            ErrorInvalidInterval
        }

        [SerializeField] public ReceiverStatus Status = ReceiverStatus.Idle;
        [SerializeField] public string StatusReason = "";
        [SerializeField] public double StatusChangedAt;

        private void SetStatus(ReceiverStatus next, string reason = null)
        {
            if (Status != next || (reason != null && !string.Equals(StatusReason, reason)))
            {
                Status = next;
                StatusReason = reason ?? "";
                StatusChangedAt = Time.timeAsDouble;
                // BasisDebug.Log($"[ReceiverStatus] {Status} :: {StatusReason}");
            }
        }

        public void ReportStatus()
        {
            BasisDebug.Log(
                $"[ReceiverStatus] {Status} :: {StatusReason} | " +
                $"HasFirst:{BufferHolder.HasFirst} HasLast:{BufferHolder.HasLast} " +
                $"HasAvatarQueue:{HasAvatarQueue} Queue:{PayloadQueue.Count} " +
                $"interp:{interpolationTime:0.000} dt:{(Time.timeAsDouble - StatusChangedAt):0.000}s");
        }

        // ---------- Constants ----------
        private const int EyesAndMouthOffset = 15;
        private const int EyesAndMouthCount = 6;
        public int BufferCapacityBeforeCleanup = 5;
        public const int EyeAndMouthSize = EyesAndMouthOffset * sizeof(float);
        public const int EyeAndMouthcount = EyesAndMouthCount * sizeof(float);

        // ---------- Playback / jitter tuning (50 ms granularity) ----------
        [Header("Playback/Jitter Settings (Quantized)")]
        [Tooltip("How many buffers we try to keep queued ahead.")]
        public int TargetQueueDepth = 2;

        [Tooltip("Lower bound on accepted segment duration (seconds).")]
        public double MinSegmentDuration = 0.015; // ~66 Hz

        [Tooltip("Upper bound on accepted segment duration (seconds).")]
        public double MaxSegmentDuration = 0.100; // ~10 Hz

        [Tooltip("EMA alpha for SecondsInterval smoothing. 0=no smoothing, 1=freeze.")]
        [Range(0f, 1f)]
        public float IntervalEmaAlpha = 0.15f;

        [Tooltip("Base playout lead (seconds), quantized; min 0.05s as per constraint.")]
        public double BaseLeadSeconds = 0.050;

        [Tooltip("Lead quantization step (seconds). Smallest allowed nudge is 50 ms).")]
        public double LeadQuantumSeconds = 0.050;

        [Tooltip("Maximum playout lead (seconds).")]
        public double MaxLeadSeconds = 0.200;

        [Tooltip("How strongly queue deficit increases the desired lead (before quantization).")]
        public double AdaptiveLeadGain = 0.25;

        // Internal smoothed cadence
        private double _smoothedInterval = 0.033;

        // Prevent double-advance within the SAME frame (Compute/Apply)
        private int _advanceFrameStamp = -1;

        // ---------- Anti-jitter state ----------
        // Lead pinned per-segment; we don't modify it mid-segment.
        private double _appliedLead = 0.050;

        // Stable time anchor for the current segment; t=0 at this instant and only moves forward.
        private double _anchorStart = 0.0;

        // Monotonic guard
        private double _lastRawT = 0.0;

        // ---------- Public Fields ----------
        public BasisRemoteBoneControl MouthBone;

        [SerializeField] public BasisAudioReceiver AudioReceiverModule = new BasisAudioReceiver();
        [SerializeField] public ConcurrentQueue<BasisAvatarBuffer> PayloadQueue = new ConcurrentQueue<BasisAvatarBuffer>();

        public BasisRemotePlayer RemotePlayer;
        public bool HasEvents = false;

        [SerializeField] public BasisRemoteAvatarBufferHolder BufferHolder = new BasisRemoteAvatarBufferHolder();

        [Serializable]
        public class BasisRemoteAvatarBufferHolder
        {
            public bool HasFirst = false;
            public bool HasLast = false;

            [SerializeField] public BasisAvatarBuffer First;
            [SerializeField] public BasisAvatarBuffer Last;

            public void ClearAndRelease()
            {
                if (HasFirst)
                {
                    BasisAvatarBufferPool.Release(ref First);
                    HasFirst = false;
                }
                if (HasLast)
                {
                    BasisAvatarBufferPool.Release(ref Last);
                    HasLast = false;
                }
            }
        }

        public float interpolationTime;
        public double TimeBeforeCompletion; // effective [First->Last] duration
        public double TimeInThePast;        // local start time for current segment (see anchor logic)
        public bool HasAvatarQueue;

        public bool LogFirstError = false;

        public float[] EyesAndMouth = new float[] { 0, 0, 0, 0, 1, 0 };
        public float[] Muscles = new float[95];

        public quaternion ApplyingRotation;
        public float3 ApplyingPosition;
        public float3 ApplyingScale;

        public bool HasApplyFrameEarlyExit = false;

        // ---------- Core Loop ----------
        public bool BufferHoldMeetsCriteria = false;

        public void Compute(double timeNow)
        {
            // NOTE: do NOT stamp _advanceFrameStamp here; only stamp when we actually advance.
            PrimeBuffersIfPossible();
            TryBootstrapIfOnlyFirst();

            if (!HasAvatarQueue)
            {
                SetStatus(BufferHolder.HasFirst ? ReceiverStatus.WaitingForLast : ReceiverStatus.WaitingForFirst, "Compute: waiting to become ready");
                return;
            }

            InterpolateBuffersAndCatchUp(timeNow);

            BufferHoldMeetsCriteria = BufferHolder.HasFirst && BufferHolder.HasLast;

            if (BufferHoldMeetsCriteria)
            {
                var first = BufferHolder.First;
                var last = BufferHolder.Last;

                BasisRemoteNetworkDriver.SetInputs(
                    playerId,
                    first.Position, last.Position,
                    first.Scale, last.Scale,
                    first.rotation, last.rotation,
                    interpolationTime,
                    first.Muscles, last.Muscles
                );
            }
        }

        public void Apply(double timeNow)
        {
            if (!HasAvatarQueue || !BufferHolder.HasFirst || !BufferHolder.HasLast)
            {
                PrimeBuffersIfPossible();
                TryBootstrapIfOnlyFirst();

                HasApplyFrameEarlyExit = !HasAvatarQueue || !BufferHolder.HasFirst || !BufferHolder.HasLast;
                if (HasApplyFrameEarlyExit)
                {
                    SetStatus(BufferHolder.HasFirst ? ReceiverStatus.WaitingForLast : ReceiverStatus.WaitingForFirst, "Apply: not ready after re-prime");
                    return;
                }
            }

            InterpolateBuffersAndCatchUp(timeNow);

            if (BasisRemoteNetworkDriver.GetOutputs(playerId, out ApplyingPosition, out ApplyingScale, out ApplyingRotation, ref Muscles))
            {
                SetStatus(ReceiverStatus.Applying, "Applying outputs");
                ApplyComputedData();
            }

            // NOTE: Advancement is centralized in InterpolateBuffersAndCatchUp to avoid double-advance.
        }

        // ---------- Helpers ----------

        private void PrimeBuffersIfPossible()
        {
            int pulledCount = 0;
            int safety = 32;
            while (safety-- > 0 && (!BufferHolder.HasFirst || !BufferHolder.HasLast) && PayloadQueue.TryDequeue(out var pulled))
            {
                pulledCount++;
                if (!BufferHolder.HasFirst)
                {
                    BufferHolder.First = pulled;
                    BufferHolder.HasFirst = true;
                    SetStatus(ReceiverStatus.WaitingForLast, "Primed First");
                }
                else if (!BufferHolder.HasLast)
                {
                    BufferHolder.Last = pulled;
                    BufferHolder.HasLast = true;
                    SetStatus(ReceiverStatus.Ready, "Primed Last");
                }
            }

            if (!HasAvatarQueue && BufferHolder.HasFirst && BufferHolder.HasLast)
            {
                _smoothedInterval = ClampInterval(BufferHolder.Last.SecondsInterval, MinSegmentDuration, MaxSegmentDuration);
                TimeBeforeCompletion = _smoothedInterval;

                // Quantize an initial lead and pin it for this segment
                _appliedLead = math.clamp(QuantizeUp(BaseLeadSeconds, LeadQuantumSeconds), 0.0, MaxLeadSeconds);

                // Stable anchor scheme: start t at 0 and let it grow
                double now = Time.timeAsDouble;
                _anchorStart = now;                 // t=0 at this moment
                TimeInThePast = now + _appliedLead; // semantic bookkeeping; not used in t calc directly

                _lastRawT = 0.0;
                interpolationTime = 0f;
                HasAvatarQueue = true;

                SetStatus(ReceiverStatus.Ready, $"AvatarQueue READY (pulled {pulledCount})");
            }
            else if (!BufferHolder.HasFirst && !BufferHolder.HasLast && PayloadQueue.IsEmpty)
            {
                SetStatus(ReceiverStatus.WaitingForFirst, "Prime: queue empty");
            }
        }

        private void TryBootstrapIfOnlyFirst()
        {
            if (!HasAvatarQueue && BufferHolder.HasFirst && !BufferHolder.HasLast && PayloadQueue.IsEmpty)
            {
                BufferHolder.Last = BufferHolder.First;
                BufferHolder.HasLast = true;

                TimeBeforeCompletion = 0.0; // zero-length segment

                // Pin initial lead and anchor
                _appliedLead = math.clamp(QuantizeUp(BaseLeadSeconds, LeadQuantumSeconds), 0.0, MaxLeadSeconds);
                double now = Time.timeAsDouble;
                _anchorStart = now;                 // start at t=0
                TimeInThePast = now + _appliedLead; // bookkeeping

                _lastRawT = 0.0;
                interpolationTime = 0f;
                HasAvatarQueue = true;

                SetStatus(ReceiverStatus.Bootstrapped, "Bootstrap: mirrored First -> Last (zero interval)");
            }
        }

        private void InterpolateBuffersAndCatchUp(double now)
        {
            if (!BufferHolder.HasFirst || !BufferHolder.HasLast)
            {
                interpolationTime = 0f;
                SetStatus(ReceiverStatus.ErrorMissingData, "Interpolate: missing First/Last");
                return;
            }

            // Effective (smoothed + clamped) segment duration
            double incomingInterval = ClampInterval(BufferHolder.Last.SecondsInterval, MinSegmentDuration, MaxSegmentDuration);
            _smoothedInterval = Ema(_smoothedInterval, incomingInterval, IntervalEmaAlpha);
            TimeBeforeCompletion = _smoothedInterval;

            // Compute desired lead from queue deficit (for NEXT segment decisioning only)
            // We DO NOT change lead mid-segment to avoid backward jumps.
            // (Keeping this calc if you want to log/inspect or prewarm next segment choice.)
            // int qCount = Math.Max(0, PayloadQueue.Count);
            // int deficit = TargetQueueDepth - qCount;
            // double desiredLead = BaseLeadSeconds + deficit * _smoothedInterval * AdaptiveLeadGain;
            // desiredLead = math.clamp(desiredLead, 0.0, MaxLeadSeconds);
            // double adaptiveLead = QuantizeUp(desiredLead, LeadQuantumSeconds);

            // Zero/invalid interval => instant transition; advance centrally here
            if (!(TimeBeforeCompletion > 0.0) || double.IsNaN(TimeBeforeCompletion))
            {
                if (double.IsNaN(TimeBeforeCompletion))
                {
                    SetStatus(ReceiverStatus.ErrorInvalidInterval, "Interpolate: NaN interval treated as zero");
                }
                interpolationTime = 1f;
                TryAdvanceWhilePossible(now);
                if (PayloadQueue.IsEmpty)
                {
                    SetStatus(ReceiverStatus.QueueStarved, "Interpolate: waiting for next buffer after zero interval");
                }
                return;
            }

            // Stable, monotonic t against a fixed anchor
            double rawT = (now - _anchorStart) / TimeBeforeCompletion;

            if (rawT >= 1.0)
            {
                SetStatus(ReceiverStatus.Advancing, "Interpolate: overshoot catch-up");
                TryAdvanceWhilePossible(now);

                // Recompute after advancing (new anchorStart set in AdvanceToNext)
                rawT = (now - _anchorStart) / Math.Max(TimeBeforeCompletion, MinSegmentDuration);
            }

            // Ensure t never goes backwards within a segment
            if (rawT < _lastRawT)
                rawT = _lastRawT;

            _lastRawT = rawT;

            float tClamped = Mathf.Clamp01((float)rawT);
            interpolationTime = Mathf.SmoothStep(0f, 1f, tClamped);

            if (float.IsNaN(interpolationTime))
            {
                SetStatus(ReceiverStatus.ErrorNaNInterpolation, "Interpolate: NaN interpolation time");
                interpolationTime = 0f;
                return;
            }

            SetStatus(ReceiverStatus.Interpolating, $"Interpolate: t={interpolationTime:0.000}");
        }

        private void AdvanceToNext(ref BasisAvatarBuffer next, double now)
        {
            if (BufferHolder.HasFirst)
            {
                BasisAvatarBufferPool.Release(ref BufferHolder.First);
                BufferHolder.HasFirst = false;
            }

            if (BufferHolder.HasLast)
            {
                BufferHolder.First = BufferHolder.Last;
                BufferHolder.HasFirst = true;
            }

            BufferHolder.Last = next;
            BufferHolder.HasLast = true;

            double candidate = ClampInterval(BufferHolder.Last.SecondsInterval, MinSegmentDuration, MaxSegmentDuration);
            _smoothedInterval = Ema(_smoothedInterval, candidate, IntervalEmaAlpha);
            TimeBeforeCompletion = _smoothedInterval;

            // Decide and pin lead for THIS new segment
            int qCount = Math.Max(0, PayloadQueue.Count);
            int deficit = TargetQueueDepth - qCount;
            double desiredLead = BaseLeadSeconds + deficit * _smoothedInterval * AdaptiveLeadGain;
            desiredLead = math.clamp(desiredLead, 0.0, MaxLeadSeconds);
            _appliedLead = QuantizeUp(desiredLead, LeadQuantumSeconds);

            // Stable anchor: t restarts at 0
            _anchorStart = now;                 // t=0 now
            TimeInThePast = now + _appliedLead; // bookkeeping only
            _lastRawT = 0.0;

            interpolationTime = 0f;
            SetStatus(ReceiverStatus.Ready, $"Advance: interval={TimeBeforeCompletion:0.000###}s lead={_appliedLead:0.000}s");
        }

        private void TryAdvanceWhilePossible(double now)
        {
            // Guard: advance at most once per frame (across Compute/Apply)
            if (_advanceFrameStamp == Time.frameCount)
            {
                return;
            }

            int safety = 32;
            bool advancedAny = false;
            while (safety-- > 0 && PayloadQueue.TryDequeue(out var next))
            {
                AdvanceToNext(ref next, now);
                advancedAny = true;

                if (TimeBeforeCompletion > 0.0 && !double.IsNaN(TimeBeforeCompletion))
                    break; // stop if the new segment has a real duration
            }

            if (advancedAny)
            {
                _advanceFrameStamp = Time.frameCount; // mark we advanced this frame
            }
            else
            {
                SetStatus(ReceiverStatus.QueueStarved, "Advance: no buffers to advance into");
            }
        }

        public void ApplyComputedData()
        {
            ApplyPoseData(Player.AvatarTransform, Player.BasisAvatar.AnimatorHumanScale, ApplyingScale, ApplyingPosition, ApplyingRotation, Muscles);

            PoseHandler.SetHumanPose(ref HumanPose);
            RemotePlayer.RemoteBoneDriver.SimulateAndApplyRemote(ApplyingScale);

            if (AudioReceiverModule.HasTransform)
            {
                var outgoing = RemotePlayer.RemoteBoneDriver.Mouth.OutGoingData;
                AudioReceiverModule.AudioSourceTransform.SetPositionAndRotation(outgoing.position, outgoing.rotation);
            }

            if (RemotePlayer.HasRemoteNamePlate)
            {
                RemotePlayer.RemoteNamePlate.Simulate();
            }
        }

        public void ApplyPoseData(Transform AnimatorsTransform, float3 Scaling, float3 Scale, float3 Position, Quaternion Rotation, float[] Muscles)
        {
            Scaling = SafeDivide(Scaling, Scale);
            HumanPose.bodyPosition = Vector3.Scale(Position, Scaling);
            HumanPose.bodyRotation = Rotation;
            Array.Copy(Muscles, HumanPose.muscles, 95);
            Buffer.BlockCopy(EyesAndMouth, 0, HumanPose.muscles, EyeAndMouthSize, EyeAndMouthcount);
            AnimatorsTransform.localScale = Scale;
        }

        public static float3 SafeDivide(float3 a, float3 b, float epsilon = 1e-5f)
        {
            return new float3(
                math.abs(b.x) > epsilon ? a.x / b.x : a.x,
                math.abs(b.y) > epsilon ? a.y / b.y : a.y,
                math.abs(b.z) > epsilon ? a.z / b.z : a.z);
        }

        public void EnQueueAvatarBuffer(BasisAvatarBuffer avatarBuffer)
        {
            PayloadQueue.Enqueue(avatarBuffer);

            bool overrun = false;
            while (PayloadQueue.Count > BufferCapacityBeforeCleanup && PayloadQueue.TryDequeue(out BasisAvatarBuffer buffer))
            {
                overrun = true;
                BasisAvatarBufferPool.Release(ref buffer);
            }
            if (overrun)
            {
                SetStatus(ReceiverStatus.QueueOverrun, $"Enqueue: dropped to cap {BufferCapacityBeforeCleanup}");
            }

            // If EnQueue can be called off-thread, consider deferring priming to main thread.
            PrimeBuffersIfPossible();

            if (!HasAvatarQueue && BufferHolder.HasFirst && !BufferHolder.HasLast)
            {
                PrimeBuffersIfPossible();
                TryBootstrapIfOnlyFirst();
            }
        }

        // ---------- Lifecycle ----------
        public override void Initialize()
        {
            RemotePlayer = (BasisRemotePlayer)Player;
            AudioReceiverModule.Initalize(this);

            if (!HasEvents && RemotePlayer?.RemoteAvatarDriver != null)
            {
                RemotePlayer.RemoteAvatarDriver.CalibrationComplete += OnCalibration;
                HasEvents = true;
            }

            SetStatus(ReceiverStatus.WaitingForFirst, "Initialized");
        }

        public void OnCalibration()
        {
            Player.BasisAvatar.AnimatorHumanScale = Vector3.one / Player.BasisAvatar.Animator.humanScale;
            AudioReceiverModule.AvatarChanged(this);
        }

        public override void DeInitialize()
        {
            BufferHolder.ClearAndRelease();

            while (PayloadQueue.TryDequeue(out var buffer))
            {
                BasisAvatarBufferPool.Release(ref buffer);
            }

            if (RemotePlayer != null && HasEvents && RemotePlayer.RemoteAvatarDriver != null)
            {
                RemotePlayer.RemoteAvatarDriver.CalibrationComplete -= OnCalibration;
                HasEvents = false;
            }

            AudioReceiverModule?.OnDestroy();

            HasAvatarQueue = false;
            interpolationTime = 0f;
            TimeBeforeCompletion = 0;
            TimeInThePast = 0;

            // Reset anti-jitter state
            _appliedLead = BaseLeadSeconds;
            _anchorStart = 0.0;
            _lastRawT = 0.0;

            SetStatus(ReceiverStatus.Idle, "Deinitialized");
        }

        // ---------- Audio ----------
        public void ReceiveNetworkAudio(ServerAudioSegmentMessage audioSegment)
        {
            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerAudioSegment, audioSegment.audioSegmentData.LengthUsed);
            AudioReceiverModule.OnDecode(audioSegment.audioSegmentData.buffer, audioSegment.audioSegmentData.LengthUsed);
            Player.AudioReceived?.Invoke(true);
        }

        public void ReceiveSilentNetworkAudio(ServerAudioSegmentMessage audioSilentSegment)
        {
            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.ServerAudioSegment, 1);
            AudioReceiverModule.OnDecodeSilence();
            Player.AudioReceived?.Invoke(false);
        }

        // ---------- Avatar switching ----------
        public async void ReceiveAvatarChangeRequest(ServerAvatarChangeMessage ServerAvatarChangeMessage)
        {
            RemotePlayer.CACM = ServerAvatarChangeMessage.clientAvatarChangeMessage;
            BasisLoadableBundle BasisLoadableBundle = BasisBundleConversionNetwork.ConvertNetworkBytesToBasisLoadableBundle(ServerAvatarChangeMessage.clientAvatarChangeMessage.byteArray);

            await RemotePlayer.CreateAvatar(ServerAvatarChangeMessage.clientAvatarChangeMessage.loadMode, BasisLoadableBundle);
        }

        // ---------- Ctor ----------
        public BasisNetworkReceiver(ushort PlayerID)
        {
            PlayerIDMessage.playerID = PlayerID;
            hasID = true;
        }

        // ---------- Utils ----------
        private static double ClampInterval(double value, double min, double max)
        {
            if (double.IsNaN(value) || value <= 0.0) return min;
            return math.clamp(value, min, max);
        }

        private static double Ema(double prev, double current, float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            return alpha * prev + (1.0f - alpha) * current;
        }

        private static double QuantizeUp(double value, double quantum)
        {
            if (quantum <= 0.0) return value;
            double steps = Math.Ceiling(value / quantum);
            return steps <= 0 ? 0.0 : steps * quantum;
        }
    }
}
