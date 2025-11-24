using Basis.Network.Core;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
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
public class BasisTransmissionResults
{
    public BasisDistanceJob distanceJob;
    public JobHandle distanceJobHandle;

    public ushort[] HearingIndexToId;
    public ushort[] LastHearingIndexToId;

    public int LastIndexLength = -1;
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
    public NetDataWriter AudioRecipientswriter = new NetDataWriter(true, 0);
    public bool CanDoSimulate(float previousInterval,out BasisAvatar BasisAvatar)
    {
        var player = BasisNetworkTransmitter.Player;
        if (player != null)
        {
            BasisAvatar = player.BasisAvatar;
        }
        else
        {
            BasisAvatar = null;
        }
        if (BasisAvatar == null)
        {
            BasisDebug.LogError("Missing Basis Avatar. Cannot send network update.", BasisDebug.LogTag.System);
            timer -= previousInterval;
            return false;
        }
        return true;
    }
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
        if (CanDoSimulate(previousInterval,out BasisAvatar avatar) == false)
        {
            return;
        }
        // Schedule job to compute all distance info
        distanceJob.SquaredAvatarDistance = SMModuleDistanceBasedReductions.AvatarRange;
        distanceJob.SquaredHearingDistance = SMModuleDistanceBasedReductions.HearingRange;
        distanceJob.SquaredVoiceDistance = SMModuleDistanceBasedReductions.MicrophoneRange;
        distanceJob.referencePosition = MouthBone.OutgoingWorldData.position;

        int receiverCount = BasisNetworkPlayers.ReceiverCount;
        var snapshot = BasisNetworkPlayers.ReceiversSnapshot;
        bool DifferentLengths = LastIndexLength != receiverCount;
        if (DifferentLengths)
        {
            ResizeOrCreateArrayData(receiverCount);//resets arrays and resizes
        }
        // Fill target positions and ID map aligned to snapshot order
        for (int index = 0; index < receiverCount; index++)
        {
            BasisNetworkReceiver remote = snapshot[index];
            if (remote == null)
            {
                BasisDebug.LogError("this shouldnt occur remote was out of bounds!", BasisDebug.LogTag.Networking);
                //target just becomes infinite
                HearingIndexToId[index] = 0;
                distanceJob.targetPositions[index] = math.INFINITY;
                continue;
            }
            RemoteBoneJobSystem.GetOutGoingMouth(remote.playerId, out float3 outgoing);
            distanceJob.targetPositions[index] = outgoing;
            HearingIndexToId[index] = remote.playerId;
        }
        distanceJobHandle = distanceJob.Schedule();
        // Compress avatar state (doesn't touch mouth bone used as input)
        BasisNetworkAvatarCompressor.Compress(BasisNetworkTransmitter, avatar.Animator);
        // Complete job, consume results, update send interval, send recipients
        distanceJobHandle.Complete();

        // Cache current as previous for next hysteresis step
        distanceJob.DistanceInside.CopyTo(distanceJob.PrevDistanceInside);
        distanceJob.HearingInside.CopyTo(distanceJob.PrevHearingInside);
        distanceJob.AvatarInside.CopyTo(distanceJob.PrevAvatarInside);

        if (DifferentLengths || HasMicrophoneStateChanged())
        {
            if (TalkingPoints.Capacity < receiverCount)
            {
                TalkingPoints.Capacity = receiverCount;
            }
            TalkingPoints.Clear();
            for (int index = 0; index < receiverCount; index++)
            {
                if (distanceJob.HearingInside[index])
                {
                    TalkingPoints.Add(HearingIndexToId[index]);
                }
            }
            BasisNetworkTransmitter.HasReasonToSendAudio = TalkingPoints.Count != 0;

            Array.Copy(HearingIndexToId, LastHearingIndexToId, receiverCount);

            // Send to server
            VoiceReceiversMessage VoiceReceiversMessage = new VoiceReceiversMessage
            {
                users = TalkingPoints.ToArray()
            };

            AudioRecipientswriter.Reset();
            VoiceReceiversMessage.Serialize(AudioRecipientswriter);
            //BasisDebug.Log($"Sending Microphone Check Data ({AudioRecipientswriter.Length})", BasisDebug.LogTag.Voice);
            BasisNetworkConnection.LocalPlayerPeer.Send(AudioRecipientswriter, BasisNetworkCommons.AudioRecipientsChannel, DeliveryMethod.ReliableOrdered);

            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.AudioRecipients, AudioRecipientswriter.Length);
            ApplyLocalChanges(receiverCount, snapshot);
        }
        // Update rate control based on minimum distance
        SmallestDistanceToAnotherPlayer = distanceJob.outMin; // still squared

        if (!float.IsFinite(SmallestDistanceToAnotherPlayer))
        {
            // No receivers or all failed positions => treat as zero for rate calc
            SmallestDistanceToAnotherPlayer = 0f;
        }

        ServerMetaDataMessage ServerMetaDataMessage = BasisNetworkManagement.ServerMetaDataMessage;
        DefaultInterval = ServerMetaDataMessage.SyncInterval / 1000f;

        // Keep squared inside; apply sqrt at boundary where human-tuned params likely expect meters.
        float minLinear = math.sqrt(math.max(0f, SmallestDistanceToAnotherPlayer));

        float calculatedIntervalBase = ServerMetaDataMessage.BaseMultiplier + (minLinear * ServerMetaDataMessage.IncreaseRate);
        UnClampedInterval = DefaultInterval * calculatedIntervalBase;

        intervalSeconds = Mathf.Clamp(UnClampedInterval, DefaultInterval, ServerMetaDataMessage.SlowestSendRate);

        if (BasisAvatarRecorder.IsRecording)
        {
            BasisAvatarRecorder.StoreData(intervalSeconds, avatar.Animator.bodyRotation, avatar.Animator.bodyPosition, BasisNetworkTransmitter.HumanPose.muscles, avatar.Animator.transform.localScale.y);
        }
        // account for overshoot using the interval that actually accumulated
        timer -= previousInterval;
        LastIndexLength = receiverCount;
    }
    public void ApplyLocalChanges(int receiverCount, BasisNetworkReceiver[] snapshot)
    {
        float activeTime = Time.time;
        for (int index = 0; index < receiverCount; index++)
        {
            try
            {
                var receiver = snapshot[index];
                var remote = receiver.RemotePlayer;

                // Avatar in-range toggling
                if (remote.InAvatarRange != distanceJob.AvatarInside[index])
                {
                    remote.InAvatarRange = distanceJob.AvatarInside[index];
                    remote.ReloadAvatar();
                }

                // Distance-based mesh LOD
                remote.ChangeMeshLOD(distanceJob.distanceSq[index], SMModuleDistanceBasedReductions.MeshLod);

                // Voice start/stop (note: HearingIndex describes "we can hear them")
                bool canHear = distanceJob.HearingInside[index];
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
    private bool HasMicrophoneStateChanged()
    {
        for (int index = 0; index < LastIndexLength; index++)
        {
            if (distanceJob.DistanceInside[index] != distanceJob.PrevDistanceInside[index])
            {
                return true;
            }
            if (HearingIndexToId[index] != LastHearingIndexToId[index])
            {
                return true;
            }
            if (distanceJob.HearingInside[index] != distanceJob.PrevHearingInside[index])
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Allocate / reallocate all NativeArrays.
    /// </summary>
    public void ResizeOrCreateArrayData(int receiverCount)
    {
        ReleaseResults();

        // wire job views
        distanceJob.distanceSq = new NativeArray<float>(receiverCount, Allocator.Persistent);
        distanceJob.DistanceInside = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        distanceJob.HearingInside = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        distanceJob.AvatarInside = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        distanceJob.PrevDistanceInside = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        distanceJob.PrevHearingInside = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        distanceJob.PrevAvatarInside = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        distanceJob.targetPositions = new NativeArray<float3>(receiverCount, Allocator.Persistent);

        // managed mirrors
        HearingIndexToId = new ushort[receiverCount];
        LastHearingIndexToId = new ushort[receiverCount];
    }

    /// <summary>
    /// Dispose NativeArrays and complete outstanding jobs.
    /// </summary>
    public void ReleaseResults()
    {
        // wait for in-flight jobs
        if (!distanceJobHandle.IsCompleted) { distanceJobHandle.Complete(); }

        // dispose old
        if (distanceJob.targetPositions.IsCreated) distanceJob.targetPositions.Dispose();
        if (distanceJob.distanceSq.IsCreated) distanceJob.distanceSq.Dispose();
        if (distanceJob.DistanceInside.IsCreated) distanceJob.DistanceInside.Dispose();
        if (distanceJob.HearingInside.IsCreated) distanceJob.HearingInside.Dispose();
        if (distanceJob.AvatarInside.IsCreated) distanceJob.AvatarInside.Dispose();
        if (distanceJob.PrevDistanceInside.IsCreated) distanceJob.PrevDistanceInside.Dispose();
        if (distanceJob.PrevHearingInside.IsCreated) distanceJob.PrevHearingInside.Dispose();
        if (distanceJob.PrevAvatarInside.IsCreated) distanceJob.PrevAvatarInside.Dispose();
    }
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public struct BasisDistanceJob : IJob
    {
        public float SquaredVoiceDistance;
        public float SquaredHearingDistance;
        public float SquaredAvatarDistance;

        public const float HysteresisMargin = 0.05f;

        [ReadOnly] public float3 referencePosition;
        [ReadOnly] public NativeArray<float3> targetPositions;

        [ReadOnly] public NativeArray<bool> PrevDistanceInside;
        [ReadOnly] public NativeArray<bool> PrevHearingInside;
        [ReadOnly] public NativeArray<bool> PrevAvatarInside;

        [WriteOnly] public NativeArray<float> distanceSq;  // d^2 per target
        [WriteOnly] public NativeArray<bool> DistanceInside;
        [WriteOnly] public NativeArray<bool> HearingInside;
        [WriteOnly] public NativeArray<bool> AvatarInside;

        public float outMin;

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
            float SmallestDistance = float.PositiveInfinity;
            for (int i = 0; i < targetPositions.Length; i++)
            {
                float3 diff = targetPositions[i] - refPos;
                float d2 = math.lengthsq(diff);
                distanceSq[i] = d2;

                DistanceInside[i] = Hysteresis(PrevDistanceInside[i], d2, SquaredVoiceDistance, HysteresisMargin);
                HearingInside[i] = Hysteresis(PrevHearingInside[i], d2, SquaredHearingDistance, HysteresisMargin);
                AvatarInside[i] = Hysteresis(PrevAvatarInside[i], d2, SquaredAvatarDistance, HysteresisMargin);

                SmallestDistance = math.min(SmallestDistance, d2);
            }
            outMin = SmallestDistance;
        }
    }
}
