using Basis.Network.Core;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Transmitters;
using Basis.Scripts.Profiler;
using Basis.Scripts.TransformBinders.BoneControl;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static SerializableBasis;
[System.Serializable]
public class BasisTransmissionResults : IDisposable
{
    public NativeArray<float3> targetPositions;
    public NativeArray<float> distances;
    public NativeArray<bool> PrevDistanceResults;
    public NativeArray<bool> PrevHearingResults;
    public NativeArray<bool> PrevAvatarResults;
    public NativeArray<bool> DistanceResults;
    public NativeArray<bool> HearingResults;
    public NativeArray<bool> AvatarResults;
    public NativeArray<float> smallestDistance;
    public BasisDistanceJob distanceJob;
    public JobHandle distanceJobHandle;
    public bool[] MicrophoneRangeIndex;
    public bool[] LastMicrophoneRangeIndex;
    public bool[] HearingIndex;
    public bool[] AvatarIndex;
    public ushort[] HearingIndexToId;
    public ushort[] LastHearingIndexToId;
    public float[] CalculatedDistances;
    public int IndexLength = -1;
    public bool requiresRebuild = false;
    public List<ushort> TalkingPoints = new List<ushort>(128);
    public float intervalSeconds = 0.5f;
    public float timer = 0f;
    public float SmallestDistanceToAnotherPlayer; // squared distance
    public float UnClampedInterval;
    public float DefaultInterval;
    [SerializeReference]
    public BasisLocalBoneControl MouthBone;
    [SerializeReference]
    public BasisNetworkTransmitter BasisNetworkTransmitter;

    /// <summary>
    /// Called each frame; drives scheduling of distance job and network sync.
    /// </summary>
    public void Simulate()
    {
        timer += Time.deltaTime;

        if (timer <= intervalSeconds)
        {
            return;
        }

        // Use the actual accumulated interval (handles overshoot)
        float previousInterval = intervalSeconds;

        if (BasisNetworkTransmitter == null)
        {
            BasisDebug.LogError("BasisNetworkTransmitter is null; cannot send network update.", BasisDebug.LogTag.System);
            timer -= previousInterval;
            return;
        }

        var player = BasisNetworkTransmitter.Player;
        var avatar = player != null ? player.BasisAvatar : null;

        if (avatar == null)
        {
            BasisDebug.LogError("Missing Basis Avatar. Cannot send network update.", BasisDebug.LogTag.System);
            timer -= previousInterval;
            return;
        }

        if (MouthBone == null)
        {
            BasisDebug.LogError("MouthBone is null; cannot schedule distance job.", BasisDebug.LogTag.System);
            timer -= previousInterval;
            return;
        }

        // Schedule job to compute all distance info
        ScheduleCheck(MouthBone);

        // Compress avatar state (doesn't touch mouth bone used as input)
        BasisNetworkAvatarCompressor.Compress(BasisNetworkTransmitter, avatar.Animator);

        // Complete job, consume results, update send interval, send recipients
        distanceJobHandle.Complete();

        if (BasisNetworkPlayers.RemotePlayers.Count != 0 && (!AreNativeResultsValid() || !AreManagedMirrorsValid()))
        {
            BasisDebug.LogError("Missing Results!");
            return;
        }

        int n = DistanceResults.Length;
        int indexLength = IndexLength >= 0 ? IndexLength : n;

        // Copy job outputs -> managed mirrors
        DistanceResults.CopyTo(MicrophoneRangeIndex);
        HearingResults.CopyTo(HearingIndex);
        AvatarResults.CopyTo(AvatarIndex);
        distances.CopyTo(CalculatedDistances);

        // Cache current as previous for next hysteresis step
        DistanceResults.CopyTo(PrevDistanceResults);
        HearingResults.CopyTo(PrevHearingResults);
        AvatarResults.CopyTo(PrevAvatarResults);

        // Handle per-receiver visual/audio state
        IndexLength = indexLength;
        MicrophoneOutputCheck();
        IterationOverRemotePlayers();

        // Update rate control based on minimum distance
        UpdateRateControl();

        // account for overshoot using the interval that actually accumulated
        timer -= previousInterval;
    }

    /// <summary>
    /// Call whenever the receiver list changes.
    /// </summary>
    public void OnPlayerJoinedOrleaved()
    {
        requiresRebuild = true;
    }

    /// <summary>
    /// Drives LOD, avatar active state, and eye/blink simulation.
    /// </summary>
    public void IterationOverRemotePlayers()
    {
        if (IndexLength <= 0)
        {
            BasisDebug.LogError("IndexLength as less then or equal to zero");
            return;
        }

        var snapshot = BasisNetworkPlayers.ReceiversSnapshot;
        if (snapshot == null)
        {
            BasisDebug.LogError("No Remote Players!");
            return;
        }

        int safeLength = math.min(IndexLength,math.min(snapshot.Length,
                math.min(
                    AvatarIndex?.Length ?? 0,
                    CalculatedDistances?.Length ?? 0)));

        if (safeLength <= 0)
        {
            BasisDebug.LogError("safeLength <= 0");
            return;
        }

        float activeTime = Time.time;

        for (int index = 0; index < safeLength; index++)
        {
            try
            {
                var receiver = snapshot[index];
                if (receiver == null)
                {
                    BasisDebug.LogError("Empty Receiver!");
                    continue;
                }

                var remote = receiver.RemotePlayer;

                // Avatar in-range toggling
                if (remote.InAvatarRange != AvatarIndex[index])
                {
                    remote.InAvatarRange = AvatarIndex[index];
                    remote.ReloadAvatar();
                }

                // Distance-based mesh LOD
                remote.ChangeMeshLOD(CalculatedDistances[index], SMModuleDistanceBasedReductions.MeshLod);

                // Voice start/stop (note: HearingIndex describes "we can hear them")
                bool canHear = HearingIndex[index];
                if (receiver.AudioReceiverModule.HasAudioSource != canHear)
                {
                    if (canHear)
                    {
                        receiver.AudioReceiverModule.StartAudio();
                        remote.OutOfRangeFromLocal = false;
                    }
                    else
                    {
                        receiver.AudioReceiverModule.StopAudio();
                        remote.OutOfRangeFromLocal = true;
                    }
                }

                // Small anim drivers
                remote.RemoteEyeDriver.Simulate();
                remote.FacialBlinkDriver.Simulate(activeTime);
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"{ex} {ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Lets the server know who can currently hear us (voice recipients).
    /// </summary>
    public void MicrophoneOutputCheck()
    {
        if (BasisNetworkTransmitter == null)
        {
            BasisDebug.LogError("BasisNetworkTransmitter is null in MicrophoneOutputCheck.", BasisDebug.LogTag.Voice);
            return;
        }

        if (!ValidateMicrophoneArrays())
        {
            BasisNetworkTransmitter.HasReasonToSendAudio = false;
            SendEmptyRecipientsIfNeeded(BasisNetworkTransmitter);
            return;
        }

        if (!HasMicrophoneStateChanged())
            return;

        RebuildTalkingPoints();

        BasisNetworkTransmitter.HasReasonToSendAudio = TalkingPoints.Count != 0;

        // Copy current -> last for next tick (both mask and IDs)
        Array.Copy(MicrophoneRangeIndex, LastMicrophoneRangeIndex, IndexLength);
        Array.Copy(HearingIndexToId, LastHearingIndexToId, IndexLength);

        // Send to server
        var vrm = new VoiceReceiversMessage
        {
            users = TalkingPoints.ToArray()
        };

        NetDataWriter microphoneWriter = new NetDataWriter(true, 0);
        SendVoiceRecipients(microphoneWriter, vrm);

        BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.AudioRecipients, microphoneWriter.Length);
    }

    /// <summary>
    /// Schedule job that computes distance bands & minimum distance.
    /// </summary>
    public void ScheduleCheck(BasisLocalBoneControl mouthBone)
    {
        if (mouthBone == null)
            return;

        distanceJob.SquaredAvatarDistance = SMModuleDistanceBasedReductions.AvatarRange;
        distanceJob.SquaredHearingDistance = SMModuleDistanceBasedReductions.HearingRange;
        distanceJob.SquaredVoiceDistance = SMModuleDistanceBasedReductions.MicrophoneRange;
        distanceJob.HysteresisMargin = 0.05f; // clamped inside job
        distanceJob.referencePosition = mouthBone.OutgoingWorldData.position;

        int receiverCount = BasisNetworkPlayers.ReceiverCount;

        if (receiverCount < 0)
            receiverCount = 0;

        if (IndexLength != receiverCount || requiresRebuild)
        {
            ResizeOrCreateArrayData(receiverCount);

            // managed mirrors
            LastMicrophoneRangeIndex = new bool[receiverCount];
            MicrophoneRangeIndex = new bool[receiverCount];
            HearingIndex = new bool[receiverCount];
            AvatarIndex = new bool[receiverCount];
            CalculatedDistances = new float[receiverCount];
            HearingIndexToId = new ushort[receiverCount];
            LastHearingIndexToId = new ushort[receiverCount];

            IndexLength = receiverCount;
            requiresRebuild = false;
        }

        var snapshot = BasisNetworkPlayers.ReceiversSnapshot;
        if (snapshot == null)
            return;

        int safeLength = math.min(receiverCount, snapshot.Length);

        // Fill target positions and ID map aligned to snapshot order
        for (int index = 0; index < safeLength; index++)
        {
            var remote = snapshot[index];

            if (remote != null)
            {
                ushort rid = remote.playerId;

                float3 outgoing;
                bool hasMouth = RemoteBoneJobSystem.GetOutGoingMouth(remote.playerId, out outgoing);

                if (hasMouth)
                {
                    targetPositions[index] = outgoing;
                }
                else
                {
                    // Fallback: use reference position so distance is 0
                    targetPositions[index] = distanceJob.referencePosition;
                    BasisDebug.LogError($"Missing Mouth for {remote.playerId}");
                }

                // Always treat a valid remote as a valid receiver
                HearingIndexToId[index] = rid;
            }
            else
            {
                // Only truly invalid when we have no Remote at all.
                targetPositions[index] = distanceJob.referencePosition;
                HearingIndexToId[index] = 0;
            }
        }

        // If receiverCount > snapshot.Length, fill remaining entries as invalid
        for (int index = safeLength; index < receiverCount; index++)
        {
            targetPositions[index] = distanceJob.referencePosition;
            HearingIndexToId[index] = 0;
        }

        // reduction output
        distanceJob.outMin = smallestDistance;

        distanceJobHandle = distanceJob.Schedule();
    }

    /// <summary>
    /// Allocate / reallocate all NativeArrays.
    /// </summary>
    public void ResizeOrCreateArrayData(int totalUserCount)
    {
        ReleaseResults();

        // (re)create
        smallestDistance = new NativeArray<float>(1, Allocator.Persistent);
        smallestDistance[0] = float.PositiveInfinity;

        targetPositions = new NativeArray<float3>(totalUserCount, Allocator.Persistent);
        distances = new NativeArray<float>(totalUserCount, Allocator.Persistent);

        // outputs
        DistanceResults = new NativeArray<bool>(totalUserCount, Allocator.Persistent);
        HearingResults = new NativeArray<bool>(totalUserCount, Allocator.Persistent);
        AvatarResults = new NativeArray<bool>(totalUserCount, Allocator.Persistent);

        // prevs (start false)
        PrevDistanceResults = new NativeArray<bool>(totalUserCount, Allocator.Persistent);
        PrevHearingResults = new NativeArray<bool>(totalUserCount, Allocator.Persistent);
        PrevAvatarResults = new NativeArray<bool>(totalUserCount, Allocator.Persistent);

        // wire job views
        distanceJob.distanceSq = distances;
        distanceJob.DistanceInside = DistanceResults;
        distanceJob.HearingInside = HearingResults;
        distanceJob.AvatarInside = AvatarResults;
        distanceJob.PrevDistanceInside = PrevDistanceResults;
        distanceJob.PrevHearingInside = PrevHearingResults;
        distanceJob.PrevAvatarInside = PrevAvatarResults;
        distanceJob.targetPositions = targetPositions;
    }

    /// <summary>
    /// Dispose NativeArrays and complete outstanding jobs.
    /// </summary>
    public void ReleaseResults()
    {
        // wait for in-flight jobs
        if (!distanceJobHandle.Equals(default(JobHandle)) && !distanceJobHandle.IsCompleted)
            distanceJobHandle.Complete();

        // dispose old
        if (targetPositions.IsCreated) targetPositions.Dispose();
        if (distances.IsCreated) distances.Dispose();
        if (smallestDistance.IsCreated) smallestDistance.Dispose();
        if (DistanceResults.IsCreated) DistanceResults.Dispose();
        if (HearingResults.IsCreated) HearingResults.Dispose();
        if (AvatarResults.IsCreated) AvatarResults.Dispose();
        if (PrevDistanceResults.IsCreated) PrevDistanceResults.Dispose();
        if (PrevHearingResults.IsCreated) PrevHearingResults.Dispose();
        if (PrevAvatarResults.IsCreated) PrevAvatarResults.Dispose();
    }

    public void Dispose()
    {
        ReleaseResults();
    }
    private bool AreNativeResultsValid()
    {
        return distances.IsCreated &&
               DistanceResults.IsCreated &&
               HearingResults.IsCreated &&
               AvatarResults.IsCreated &&
               smallestDistance.IsCreated &&
               smallestDistance.Length == 1;
    }

    private bool AreManagedMirrorsValid()
    {
        if (DistanceResults.Length == 0)
            return false;

        if (MicrophoneRangeIndex == null ||
            HearingIndex == null ||
            AvatarIndex == null ||
            CalculatedDistances == null)
            return false;

        int n = DistanceResults.Length;

        return MicrophoneRangeIndex.Length == n &&
               HearingIndex.Length == n &&
               AvatarIndex.Length == n &&
               CalculatedDistances.Length == n;
    }

    private bool ValidateMicrophoneArrays()
    {
        if (MicrophoneRangeIndex == null ||
            LastMicrophoneRangeIndex == null ||
            HearingIndexToId == null ||
            LastHearingIndexToId == null)
        {
            return false;
        }

        if (IndexLength < 0)
        {
            return false;
        }

        if (MicrophoneRangeIndex.Length != IndexLength ||
            LastMicrophoneRangeIndex.Length != IndexLength ||
            HearingIndexToId.Length != IndexLength ||
            LastHearingIndexToId.Length != IndexLength)
        {
            BasisDebug.LogError("MicrophoneOutputCheck: length mismatch.", BasisDebug.LogTag.Voice);
            return false;
        }

        return true;
    }

    private bool HasMicrophoneStateChanged()
    {
        for (int index = 0; index < IndexLength; index++)
        {
            if (MicrophoneRangeIndex[index] != LastMicrophoneRangeIndex[index] ||
                HearingIndexToId[index] != LastHearingIndexToId[index])
            {
                return true;
            }
        }

        return false;
    }

    private void RebuildTalkingPoints()
    {
        if (TalkingPoints.Capacity < IndexLength)
            TalkingPoints.Capacity = IndexLength;

        TalkingPoints.Clear();

        for (int index = 0; index < IndexLength; index++)
        {
            // Skip invalid IDs (0 = no valid receiver)
            if (MicrophoneRangeIndex[index] && HearingIndexToId[index] != 0)
            {
                TalkingPoints.Add(HearingIndexToId[index]); // IDs aligned to snapshot order
            }
        }
    }

    private void UpdateRateControl()
    {
        SmallestDistanceToAnotherPlayer = smallestDistance[0]; // still squared

        if (!float.IsFinite(SmallestDistanceToAnotherPlayer))
        {
            // No receivers or all failed positions => treat as zero for rate calc
            SmallestDistanceToAnotherPlayer = 0f;
        }

        ServerMetaDataMessage message = BasisNetworkManagement.ServerMetaDataMessage;
        DefaultInterval = message.SyncInterval / 1000f;

        // Keep squared inside; apply sqrt at boundary where human-tuned params likely expect meters.
        float minLinear = math.sqrt(math.max(0f, SmallestDistanceToAnotherPlayer));

        float calculatedIntervalBase = message.BaseMultiplier + (minLinear * message.IncreaseRate);
        UnClampedInterval = DefaultInterval * calculatedIntervalBase;

        intervalSeconds = Mathf.Clamp(UnClampedInterval, DefaultInterval, message.SlowestSendRate);
    }

    private void SendEmptyRecipientsIfNeeded(BasisNetworkTransmitter transmitter)
    {
        if (transmitter == null)
            return;

        // If server already thinks we have no recipients, nothing to do.
        if (!transmitter.HasReasonToSendAudio)
        {
            return;
        }

        TalkingPoints.Clear();
        var vrm = new VoiceReceiversMessage
        {
            users = Array.Empty<ushort>()
        };

        NetDataWriter writer = new NetDataWriter(true, 0);
        BasisDebug.Log("Sending empty Microphone Check Data (flush)", BasisDebug.LogTag.Voice);
        SendVoiceRecipients(writer, vrm);

        BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.AudioRecipients, writer.Length);

        // Mark state as flushed
        transmitter.HasReasonToSendAudio = false;
    }

    private void SendVoiceRecipients(NetDataWriter writer, VoiceReceiversMessage message)
    {
        message.Serialize(writer);

        if (BasisNetworkConnection.LocalPlayerPeer == null)
        {
            BasisDebug.LogError("LocalPlayerPeer is null; cannot send voice recipients.", BasisDebug.LogTag.Voice);
            return;
        }

        BasisDebug.Log($"Sending Microphone Check Data ({writer.Length})", BasisDebug.LogTag.Voice);
        BasisNetworkConnection.LocalPlayerPeer.Send(writer, BasisNetworkCommons.AudioRecipientsChannel, DeliveryMethod.ReliableOrdered);
    }
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public struct BasisDistanceJob : IJob
    {
        public float SquaredVoiceDistance;
        public float SquaredHearingDistance;
        public float SquaredAvatarDistance;
        public float HysteresisMargin; // 0..0.49 (clamped)

        [ReadOnly] public float3 referencePosition;
        [ReadOnly] public NativeArray<float3> targetPositions;

        [ReadOnly] public NativeArray<bool> PrevDistanceInside;
        [ReadOnly] public NativeArray<bool> PrevHearingInside;
        [ReadOnly] public NativeArray<bool> PrevAvatarInside;

        [WriteOnly] public NativeArray<float> distanceSq;  // d^2 per target
        [WriteOnly] public NativeArray<bool> DistanceInside;
        [WriteOnly] public NativeArray<bool> HearingInside;
        [WriteOnly] public NativeArray<bool> AvatarInside;

        // length = 1
        public NativeArray<float> outMin;

        [BurstCompile]
        private static bool Hysteresis(bool wasInside, float d2, float thr2, float margin)
        {
            margin = math.clamp(margin, 0f, 0.49f);
            float insideFactor = 1f + margin;
            float outsideFactor = 1f - margin;
            float factor = math.select(outsideFactor, insideFactor, wasInside);
            return d2 < thr2 * factor;
        }

        public void Execute()
        {
            float3 refPos = referencePosition;
            float minD2 = float.PositiveInfinity;

            for (int i = 0; i < targetPositions.Length; i++)
            {
                float3 diff = targetPositions[i] - refPos;
                float d2 = math.lengthsq(diff);
                distanceSq[i] = d2;

                DistanceInside[i] = Hysteresis(PrevDistanceInside[i], d2, SquaredVoiceDistance, HysteresisMargin);
                HearingInside[i] = Hysteresis(PrevHearingInside[i], d2, SquaredHearingDistance, HysteresisMargin);
                AvatarInside[i] = Hysteresis(PrevAvatarInside[i], d2, SquaredAvatarDistance, HysteresisMargin);

                minD2 = math.min(minD2, d2);
            }

            if (outMin.IsCreated)
            {
                outMin[0] = minD2;
            }
        }
    }
}
