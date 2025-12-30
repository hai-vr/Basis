using Basis.Network.Core;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
using Basis.Scripts.Networking.Transmitters;
using Basis.Scripts.Profiler;
using Basis.Scripts.TransformBinders.BoneControl;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static SerializableBasis;
[System.Serializable]
public partial class BasisTransmissionResults
{
    public BasisDistanceJob distanceJob;
    public JobHandle distanceJobHandle;

    public int LengthOfArrays = -1;
    public List<ushort> TalkingPoints = new List<ushort>(128);
    public float intervalSeconds = 0.05f;
    public float timer = 0f;
    public float SquaredSmallestDistance;
    public float UnClampedInterval;
    public float DefaultInterval;

    public bool AnyMicrophoneRangeChanged;
    public bool AnyHearingRangeChanged;
    public bool AnyAvatarRangeChanged;
    public bool AnyIdOrderOrLengthChanged;

    [SerializeReference]
    public BasisLocalBoneControl MouthBone;
    [SerializeReference]
    public BasisNetworkTransmitter BasisNetworkTransmitter;
    public NetDataWriter VRMWriter = new NetDataWriter(true, 0);
    private NativeArray<float> distanceSq;
    private NativeArray<bool> hearingRange;
    private NativeArray<float3> targetPositions;
    public NativeArray<bool> MicrophoneRange;
    public NativeArray<bool> AvatarRange;
    public NativeArray<bool> PrevInMicrophoneRange;
    public NativeArray<bool> PrevInHearingRange;
    public NativeArray<bool> PrevInAvatarRange;
    public NativeArray<ushort> IndexToPlayerId;
    public NativeArray<ushort> LastIndexToPlayerId;
    public VoiceReceiversMessage VRM = new VoiceReceiversMessage();
    /// <summary>
    /// Called each frame; drives scheduling of distance job and network sync.
    /// </summary>
    public void Simulate()
    {
        float deltaTime = Time.deltaTime;
        timer += deltaTime;

        if (timer <= intervalSeconds)
        {
            return;
        }

        // Use the actual accumulated interval (handles overshoot)
        float previousInterval = intervalSeconds;
        if (CanDoSimulate(previousInterval, out BasisAvatar avatar) == false)
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
        if (LengthOfArrays != receiverCount)
        {
            ResizeOrCreateArrayData(receiverCount);//resets arrays and resizes
        }
        // Fill target positions and ID map aligned to snapshot order
        for (int index = 0; index < receiverCount; index++)
        {
            BasisNetworkReceiver remote = snapshot[index];
            ushort ID = remote.playerId;
            if (RemoteBoneJobSystem.GetOutGoingMouth(ID, out float3 outgoing))
            {
                targetPositions[index] = outgoing;
            }
            else
            {
                BasisDebug.LogError("Bad TargetPosition Inserted");
                targetPositions[index] = outgoing;
            }
            IndexToPlayerId[index] = ID;
        }
        distanceJobHandle = distanceJob.Schedule();
        // Compress avatar state (doesn't touch mouth bone used as input)
        BasisNetworkAvatarCompressor.Compress(BasisNetworkTransmitter, avatar.Animator);
        // Complete job, consume results, update send interval, send recipients
        distanceJobHandle.Complete();

        /// AnyMicrophoneRangeChanged AnyHearingRangeChanged AnyAvatarRangeChanged AnyIdOrderOrLengthChanged;
        AnyMicrophoneRangeChanged = distanceJob.AnyChangedArray[0];
        AnyHearingRangeChanged = distanceJob.AnyChangedArray[1];
        AnyAvatarRangeChanged = distanceJob.AnyChangedArray[2];
        AnyIdOrderOrLengthChanged = distanceJob.AnyChangedArray[3];

        SquaredSmallestDistance = distanceJob.SMD[0];

        bool MicrophoneChange = AnyIdOrderOrLengthChanged || AnyMicrophoneRangeChanged;
        bool HearingChange = AnyIdOrderOrLengthChanged || AnyHearingRangeChanged;
        bool AvatarChange = AnyIdOrderOrLengthChanged || AnyAvatarRangeChanged;

        if (HearingChange)
        {
            for (int index = 0; index < receiverCount; index++)
            {
                var receiver = snapshot[index];
                bool canHear = hearingRange[index];
                if (receiver.AudioReceiverModule.HasAudioSource != canHear)
                {
                    if (canHear)
                    {
                        receiver.AudioReceiverModule.StartAudio();
                        receiver.RemotePlayer.OutOfRangeFromLocal = false;
                    }
                    else
                    {
                        receiver.AudioReceiverModule.StopAudio();
                        receiver.RemotePlayer.OutOfRangeFromLocal = true;
                    }
                }
            }
        }
        if (AvatarChange)
        {
            for (int index = 0; index < receiverCount; index++)
            {
                var receiver = snapshot[index];
                var remote = receiver.RemotePlayer;
                if (remote.IsLoadingAnAvatar == false && remote.InAvatarRange != AvatarRange[index])
                {
                    remote.InAvatarRange = AvatarRange[index];
                    remote.ReloadAvatar();
                }
            }
        }
        float MeshLodMulitplier = SMModuleDistanceBasedReductions.MeshLod;
        for (int index = 0; index < receiverCount; index++)
        {
            var receiver = snapshot[index];
            var remote = receiver.RemotePlayer;
            // Distance-based mesh LOD
            remote.ChangeMeshLOD(distanceSq[index], MeshLodMulitplier);
        }
        // Cache current as previous for next hysteresis step
        MicrophoneRange.CopyTo(PrevInMicrophoneRange);
        hearingRange.CopyTo(PrevInHearingRange);
        AvatarRange.CopyTo(PrevInAvatarRange);
        IndexToPlayerId.CopyTo(LastIndexToPlayerId);

        //update the server with who we are talking to
        if (MicrophoneChange)
        {
            if (TalkingPoints.Capacity < receiverCount)
            {
                TalkingPoints.Capacity = receiverCount;
            }
            TalkingPoints.Clear();
            for (int index = 0; index < receiverCount; index++)
            {
                if (MicrophoneRange[index])
                {
                    TalkingPoints.Add(IndexToPlayerId[index]);
                }
            }
            BasisNetworkTransmitter.HasReasonToSendAudio = TalkingPoints.Count != 0;
            VRM.Users = TalkingPoints.ToArray();
            VRMWriter.Reset();
            VRM.Serialize(VRMWriter);
            //BasisDebug.Log($"Sending Microphone Check Data ({AudioRecipientswriter.Length})", BasisDebug.LogTag.Voice);
            BasisNetworkConnection.LocalPlayerPeer.Send(VRMWriter, BasisNetworkCommons.AudioRecipientsChannel, DeliveryMethod.ReliableOrdered);

            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.AudioRecipients, VRMWriter.Length);
        }
        if (!float.IsFinite(SquaredSmallestDistance))
        {
            // No receivers or all failed positions => treat as zero for rate calc
            SquaredSmallestDistance = 0f;
        }

        ServerMetaDataMessage ServerMetaDataMessage = BasisNetworkManagement.ServerMetaDataMessage;
        DefaultInterval = ServerMetaDataMessage.SyncInterval / 1000f;


        float calculatedIntervalBase = ServerMetaDataMessage.BaseMultiplier + (SquaredSmallestDistance * ServerMetaDataMessage.IncreaseRate);
        UnClampedInterval = DefaultInterval * calculatedIntervalBase;

        intervalSeconds = Mathf.Clamp(UnClampedInterval, DefaultInterval, ServerMetaDataMessage.SlowestSendRate);

        if (BasisAvatarRecorder.IsRecording)
        {
            var Anim = avatar.Animator;
            BasisAvatarRecorder.StoreData(intervalSeconds, Anim.bodyRotation, Anim.bodyPosition, BasisNetworkTransmitter.HumanPose.muscles, Anim.transform.localScale.y);
        }
        // account for overshoot using the interval that actually accumulated
        timer -= previousInterval;
    }
    /// <summary>
    /// Allocate / reallocate all NativeArrays.
    /// </summary>
    public void ResizeOrCreateArrayData(int receiverCount)
    {
        ReleaseResults();

        LengthOfArrays = receiverCount;

        // wire job views
        distanceSq = new NativeArray<float>(receiverCount, Allocator.Persistent);
        MicrophoneRange = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        hearingRange = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        AvatarRange = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        PrevInMicrophoneRange = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        PrevInHearingRange = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        PrevInAvatarRange = new NativeArray<bool>(receiverCount, Allocator.Persistent);
        targetPositions = new NativeArray<float3>(receiverCount, Allocator.Persistent);
        IndexToPlayerId = new NativeArray<ushort>(receiverCount, Allocator.Persistent);
        LastIndexToPlayerId = new NativeArray<ushort>(receiverCount, Allocator.Persistent);
        for (int Index = 0; Index < receiverCount; Index++)
        {
            LastIndexToPlayerId[Index] = ushort.MaxValue;//this means that we cant support the full ushort max value but i dont think we will ever need 65535
        }

        distanceJob.distanceSq = distanceSq;
        distanceJob.MicrophoneRange = MicrophoneRange;
        distanceJob.hearingRange = hearingRange;
        distanceJob.AvatarRange = AvatarRange;
        distanceJob.PrevInMicrophoneRange = PrevInMicrophoneRange;
        distanceJob.PrevInHearingRange = PrevInHearingRange;
        distanceJob.PrevInAvatarRange = PrevInAvatarRange;
        distanceJob.targetPositions = targetPositions;
        distanceJob.IndexToPlayerId = IndexToPlayerId;
        distanceJob.LastIndexToPlayerId = LastIndexToPlayerId;
    }
    public bool CanDoSimulate(float previousInterval, out BasisAvatar BasisAvatar)
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
    public void Initalize()
    {
        distanceJob.AnyChangedArray = new NativeArray<bool>(4, Allocator.Persistent);
        distanceJob.SMD = new NativeArray<float>(1, Allocator.Persistent);
    }
    public void DeInitalize()
    {
        ReleaseResults();

        if (distanceJob.AnyChangedArray.IsCreated)
        {
            distanceJob.AnyChangedArray.Dispose();
        }

        if (distanceJob.SMD.IsCreated)
        {
            distanceJob.SMD.Dispose();
        }
    }
    /// <summary>
    /// Dispose NativeArrays and complete outstanding jobs.
    /// </summary>
    public void ReleaseResults()
    {
        // wait for in-flight jobs
        if (!distanceJobHandle.IsCompleted)
        {
            distanceJobHandle.Complete();
        }

        if (targetPositions.IsCreated) targetPositions.Dispose();
        if (distanceSq.IsCreated) distanceSq.Dispose();
        if (MicrophoneRange.IsCreated) MicrophoneRange.Dispose();
        if (hearingRange.IsCreated) hearingRange.Dispose();
        if (AvatarRange.IsCreated) AvatarRange.Dispose();
        if (PrevInMicrophoneRange.IsCreated) PrevInMicrophoneRange.Dispose();
        if (PrevInHearingRange.IsCreated) PrevInHearingRange.Dispose();
        if (PrevInAvatarRange.IsCreated) PrevInAvatarRange.Dispose();
        if (LastIndexToPlayerId.IsCreated) LastIndexToPlayerId.Dispose();
        if (IndexToPlayerId.IsCreated) IndexToPlayerId.Dispose();
    }
}
