using Basis.BasisUI;
using Basis.Network.Core;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
using Basis.Scripts.Networking.Transmitters;
using Basis.Scripts.Profiler;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static SerializableBasis;
[System.Serializable]
public partial class BasisTransmissionResults
{
    // Jobs
    public BasisDistanceJobParallel distanceJob;
    public BasisDistanceReduceJob reduceJob;

    public JobHandle distanceJobHandle;
    public JobHandle reduceJobHandle;

    // Timing / interval control
    public float intervalSeconds = 0.05f;
    public float timer = 0f;
    public float SquaredSmallestDistance;
    public float UnClampedInterval;
    public float DefaultInterval;

    // Change flags (derived from mask)
    public bool AnyMicrophoneRangeChanged;
    public bool AnyHearingRangeChanged;
    public bool AnyAvatarRangeChanged;
    public bool AnyLodRangeChanged;

    // Network
    [SerializeReference] public BasisNetworkTransmitter BasisNetworkTransmitter;
    public NetDataWriter VRMWriter = new NetDataWriter(true, 0);

    // Recipients
    public List<ushort> TalkingPoints = new List<ushort>(128);

    // Capacity / length
    public int LengthOfArrays = -1;
    private int capacity = 0;

    /// <summary>
    /// Pre-computed per-index flag: true when the remote player currently has their
    /// real avatar loaded (InAvatarRange and not fallback). Filled in the positions
    /// loop so managed objects are never touched during sorting.
    /// </summary>
    private NativeArray<bool> hasRealAvatarLoaded;

    /// <summary>
    /// Scratch buffer for avatar-cap sorting. Sized to capacity, reused each tick.
    /// Sorted via NativeArray.Sort() which uses Burst-optimized introsort.
    /// </summary>
    private NativeArray<AvatarCapEntry> avatarCapEntries;

    // State
    public bool IndexChanged;

    // Arrays
    private NativeArray<float> distanceSq;
    private NativeArray<float3> targetPositions;

    public NativeArray<bool> MicrophoneRange;
    private NativeArray<bool> hearingRange;
    public NativeArray<bool> AvatarRange;

    public NativeArray<bool> PrevInMicrophoneRange;
    public NativeArray<bool> PrevInHearingRange;
    public NativeArray<bool> PrevInAvatarRange;

    public NativeArray<short> MeshLodLevel;
    public NativeArray<short> prevMeshLodLevel;
    public NativeArray<bool> MeshLodRange;

    // Scratch + reduced outputs
    private NativeArray<float> perIndexMinD2;
    private NativeArray<int> perIndexMask;

    private NativeArray<float> smallestD2; // length 1
    private NativeArray<int> changeMask;   // length 1

   public static float HysteresisPercent = 1.10f * 1.10f; // 10% hysteresis

    public static float LastHearingRange = -1;
    public static bool RevaluteAudioRanges = false;
    public static float  ConvertedVoiceDistance;
    /// <summary>
    /// Called each frame; drives scheduling of distance job and network sync.
    /// </summary>
    public void Simulate()
    {
        float dt = Time.deltaTime;
        timer += dt;

        if (timer < intervalSeconds)
        {
            return;
        }

        float intervalUsedThisTick = intervalSeconds;

        if (!CanDoSimulate(intervalUsedThisTick, out BasisAvatar avatar))
        {
            return;
        }

        int receiverCount = BasisNetworkPlayers.ReceiverCount;
        var snapshot = BasisNetworkPlayers.ReceiversSnapshot;

        if (receiverCount <= 0)
        {
            // Still update interval pacing even with no receivers
            UpdateSendInterval(0f);
            timer = math.max(0f, timer - intervalUsedThisTick);
            IndexChanged = false;
            return;
        }

        EnsureCapacity(receiverCount);
        LengthOfArrays = receiverCount;

        // Fill target positions aligned to snapshot order.
        // Also pre-compute stickiness flags for the avatar cap so the
        // NativeArray sort never needs to touch managed objects.
        for (int Index = 0; Index < receiverCount; Index++)
        {
            BasisNetworkReceiver remote = snapshot[Index];
            ushort id = remote.playerId;

            if (RemoteBoneJobSystem.GetOutGoingMouth(id, out float3 outgoing))
            {
                targetPositions[Index] = outgoing;
            }
            else
            {
                targetPositions[Index] = BasisLocalCameraDriver.Position + new Vector3(900, 900, 900);//shove it way outside of our understanding.
            }

            var remotePlayer = remote.RemotePlayer;
            hasRealAvatarLoaded[Index] = remotePlayer.InAvatarRange && !remotePlayer.IsConsideredFallBackAvatar;
        }
        var CurrentHearingRange = SMModuleDistanceBasedReductions.HearingRange;
        if (LastHearingRange != CurrentHearingRange)
        {
            LastHearingRange = CurrentHearingRange;
            ConvertedVoiceDistance = Mathf.Sqrt(LastHearingRange);
            RevaluteAudioRanges = true;
        }
        else
        {
            RevaluteAudioRanges = false;
        }
        // Configure job inputs (only what changes per tick)
        distanceJob.SquaredAvatarDistance = SMModuleDistanceBasedReductions.AvatarRange;
        distanceJob.SquaredHearingDistance = SMModuleDistanceBasedReductions.HearingRange;
        distanceJob.SquaredVoiceDistance = SMModuleDistanceBasedReductions.MicrophoneRange;

        distanceJob.referencePosition = BasisLocalCameraDriver.Position;
        distanceJob.ReductionMultiplier = SMModuleDistanceBasedReductions.MeshLod;

        distanceJob.HysteresisPercent = HysteresisPercent;

        // Schedule distance job (parallel)
        distanceJobHandle = distanceJob.Schedule(receiverCount, 64);

        // Reduce depends on distance job
        reduceJobHandle = reduceJob.Schedule(distanceJobHandle);

        // Do work that doesn't depend on distance results
        BasisNetworkAvatarCompressor.Compress(BasisNetworkTransmitter, avatar.Animator);

        // Finish before consuming results
        reduceJobHandle.Complete();

        int mask = changeMask[0];
        AnyMicrophoneRangeChanged = (mask & 1) != 0;
        AnyHearingRangeChanged = (mask & 2) != 0;
        AnyAvatarRangeChanged = (mask & 4) != 0;
        AnyLodRangeChanged = (mask & 8) != 0;

        SquaredSmallestDistance = smallestD2[0];
        if (!float.IsFinite(SquaredSmallestDistance))
        {
            SquaredSmallestDistance = 0f;
        }

        // Enforce max-visible-avatars cap before checking for changes.
        // This may flip some AvatarRange[i] from true to false, which
        // the change detection below will pick up naturally.
        EnforceMaxVisibleAvatars(receiverCount);

        bool microphoneChange = IndexChanged || AnyMicrophoneRangeChanged;
        bool hearingChange = IndexChanged || AnyHearingRangeChanged;
        bool lodChange = IndexChanged || AnyLodRangeChanged;

        // Re-check avatar changes: the cap enforcement may have changed
        // AvatarRange entries beyond what the distance job flagged.
        bool avatarChange = IndexChanged || AnyAvatarRangeChanged;
        if (!avatarChange && SMModuleDistanceBasedReductions.MaxVisibleAvatars > 0)
        {
            for (int i = 0; i < receiverCount; i++)
            {
                if (AvatarRange[i] != snapshot[i].RemotePlayer.InAvatarRange)
                {
                    avatarChange = true;
                    break;
                }
            }
        }

        // Apply hearing toggles only when needed
        if (hearingChange)
        {
            for (int i = 0; i < receiverCount; i++)
            {
                var receiver = snapshot[i];
                bool canHear = hearingRange[i];
                if (receiver.AudioReceiverModule.HasAudioSource != canHear)
                {
                    if (canHear)
                    {
                        receiver.AudioReceiverModule.StartAudio(ConvertedVoiceDistance);
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
        if (RevaluteAudioRanges)
        {
            for (int i = 0; i < receiverCount; i++)
            {
                var receiver = snapshot[i];
                receiver.AudioReceiverModule.ApplyRangeData(ConvertedVoiceDistance);
            }
        }

        // Apply listener directional dampening
        ApplyListenerDirectionalDampening(snapshot, receiverCount);

        // Apply avatar load toggles only when needed
        if (avatarChange)
        {
            for (int Index = 0; Index < receiverCount; Index++)
            {
                var receiver = snapshot[Index];
                var remote = receiver.RemotePlayer;

                bool inRange = AvatarRange[Index];
                if (!remote.IsLoadingAnAvatar && remote.InAvatarRange != inRange)
                {
                    remote.InAvatarRange = inRange;
                    remote.ReloadAvatar();
                }
            }
        }

        // Apply mesh LOD only for changed indices (cheap micro-optimization)
        if (lodChange)
        {
            for (int i = 0; i < receiverCount; i++)
            {
                if (!MeshLodRange[i])
                {
                    continue;
                }

                var receiver = snapshot[i];
                receiver.RemotePlayer.ChangeMeshLOD(MeshLodLevel[i]);
            }
        }

        // Update who we are talking to (serialize without allocations)
        if (microphoneChange)
        {
            BuildAndSendTalkingPoints(snapshot, receiverCount);
        }

        UpdateSendInterval(SquaredSmallestDistance);

        // Recording hook
        if (BasisAvatarRecorder.IsRecording)
        {
            var anim = avatar.Animator;
            BasisAvatarRecorder.StoreData(
                intervalSeconds,
                anim.bodyRotation,
                anim.bodyPosition,
                BasisNetworkTransmitter.HumanPose.muscles,
                anim.transform.localScale.y);
        }

        // Swap buffers instead of CopyTo() each tick (avoid full-array memcopy on main thread)
        Swap(ref MicrophoneRange, ref PrevInMicrophoneRange);
        Swap(ref hearingRange, ref PrevInHearingRange);
        Swap(ref AvatarRange, ref PrevInAvatarRange);
        Swap(ref MeshLodLevel, ref prevMeshLodLevel);

        // Rebind swapped arrays to the job for next tick
        distanceJob.MicrophoneRange = MicrophoneRange;
        distanceJob.PrevInMicrophoneRange = PrevInMicrophoneRange;

        distanceJob.hearingRange = hearingRange;
        distanceJob.PrevInHearingRange = PrevInHearingRange;

        distanceJob.AvatarRange = AvatarRange;
        distanceJob.PrevInAvatarRange = PrevInAvatarRange;

        distanceJob.MeshLodLevel = MeshLodLevel;
        distanceJob.PrevMeshLodLevel = prevMeshLodLevel;

        IndexChanged = false;

        // Consume one interval worth of accumulated time (robust to overshoot)
        timer = math.max(0f, timer - intervalUsedThisTick);
    }

    private void BuildAndSendTalkingPoints(IReadOnlyList<BasisNetworkReceiver> snapshot, int receiverCount)
    {
        if (TalkingPoints.Capacity < receiverCount)
            TalkingPoints.Capacity = receiverCount;

        TalkingPoints.Clear();

        for (int i = 0; i < receiverCount; i++)
        {
            if (MicrophoneRange[i])
            {
                TalkingPoints.Add(snapshot[i].playerId);
            }
        }

        BasisNetworkTransmitter.HasReasonToSendAudio = TalkingPoints.Count != 0;

        // Serialize directly: [count][id0][id1]...
        VRMWriter.Reset();
        VRMWriter.Put((ushort)TalkingPoints.Count);
        for (int i = 0; i < TalkingPoints.Count; i++)
        {
            VRMWriter.Put(TalkingPoints[i]);
        }

        BasisNetworkConnection.LocalPlayerPeer.Send(
            VRMWriter,
            BasisNetworkCommons.AudioRecipientsChannel,
            DeliveryMethod.ReliableOrdered);

        BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.AudioRecipients, VRMWriter.Length);
    }

    /// <summary>
    /// After the distance job has determined who is in avatar range, this method
    /// enforces the MaxVisibleAvatars cap. If more players are in range than the cap
    /// allows, only the closest N keep AvatarRange[i]=true; the rest are set to false
    /// so they fall back to the default avatar.
    ///
    /// Uses a NativeArray with Burst-optimized introsort for 1000+ player scalability.
    /// Stickiness flags are pre-computed into hasRealAvatarLoaded during the positions
    /// loop so this method never touches managed objects during the sort.
    /// </summary>
    private void EnforceMaxVisibleAvatars(int receiverCount)
    {
        int maxVisible = SMModuleDistanceBasedReductions.MaxVisibleAvatars;
        if (maxVisible <= 0)
        {
            return;
        }

        // Build the candidate list into the pre-allocated NativeArray.
        // Apply stickiness: players with their real avatar loaded get a 25% distance
        // bonus (d² * 0.75 for ranking only), so a newcomer must be meaningfully
        // closer to displace a currently-visible avatar — prevents pulsing.
        const float StickinessBonus = 0.75f;
        int candidateCount = 0;

        for (int i = 0; i < receiverCount; i++)
        {
            if (!AvatarRange[i])
            {
                continue;
            }

            float d2 = distanceSq[i];
            float effective = hasRealAvatarLoaded[i] ? d2 * StickinessBonus : d2;

            avatarCapEntries[candidateCount] = new AvatarCapEntry
            {
                Index = i,
                EffectiveDistSq = effective,
            };
            candidateCount++;
        }

        if (candidateCount <= maxVisible)
        {
            return;
        }

        // Burst-optimized introsort on the filled sub-array (no GC, cache-friendly).
        avatarCapEntries.GetSubArray(0, candidateCount).Sort();

        // Everyone past the cap gets forced out of avatar range
        for (int i = maxVisible; i < candidateCount; i++)
        {
            AvatarRange[avatarCapEntries[i].Index] = false;
        }
    }

    private void UpdateSendInterval(float smallestD2)
    {
        ServerMetaDataMessage meta = BasisNetworkManagement.ServerMetaDataMessage;
        DefaultInterval = meta.SyncInterval / 1000f;

        float calculatedIntervalBase = meta.BaseMultiplier + (smallestD2 * meta.IncreaseRate);
        UnClampedInterval = DefaultInterval * calculatedIntervalBase;

        intervalSeconds = Mathf.Clamp(UnClampedInterval, DefaultInterval, meta.SlowestSendRate);
    }

    /// <summary>
    /// Capacity growth allocator; avoids dispose/realloc churn on player join/leave.
    /// </summary>
    private void EnsureCapacity(int receiverCount)
    {
        if (receiverCount <= capacity && distanceSq.IsCreated)
            return;

        int newCap = math.max(16, math.ceilpow2(receiverCount));
        Realloc(newCap);
        capacity = newCap;
    }

    private void Realloc(int newCap)
    {
        ReleaseResults();

        distanceSq = new NativeArray<float>(newCap, Allocator.Persistent);
        targetPositions = new NativeArray<float3>(newCap, Allocator.Persistent);

        MicrophoneRange = new NativeArray<bool>(newCap, Allocator.Persistent);
        hearingRange = new NativeArray<bool>(newCap, Allocator.Persistent);
        AvatarRange = new NativeArray<bool>(newCap, Allocator.Persistent);

        PrevInMicrophoneRange = new NativeArray<bool>(newCap, Allocator.Persistent);
        PrevInHearingRange = new NativeArray<bool>(newCap, Allocator.Persistent);
        PrevInAvatarRange = new NativeArray<bool>(newCap, Allocator.Persistent);

        MeshLodLevel = new NativeArray<short>(newCap, Allocator.Persistent);
        prevMeshLodLevel = new NativeArray<short>(newCap, Allocator.Persistent);
        MeshLodRange = new NativeArray<bool>(newCap, Allocator.Persistent);

        perIndexMinD2 = new NativeArray<float>(newCap, Allocator.Persistent);
        perIndexMask = new NativeArray<int>(newCap, Allocator.Persistent);

        hasRealAvatarLoaded = new NativeArray<bool>(newCap, Allocator.Persistent);
        avatarCapEntries = new NativeArray<AvatarCapEntry>(newCap, Allocator.Persistent);

        if (!smallestD2.IsCreated) smallestD2 = new NativeArray<float>(1, Allocator.Persistent);
        if (!changeMask.IsCreated) changeMask = new NativeArray<int>(1, Allocator.Persistent);

        // Bind constant array references to jobs (these remain valid until next Realloc)
        distanceJob.distanceSq = distanceSq;
        distanceJob.targetPositions = targetPositions;

        distanceJob.MicrophoneRange = MicrophoneRange;
        distanceJob.hearingRange = hearingRange;
        distanceJob.AvatarRange = AvatarRange;

        distanceJob.PrevInMicrophoneRange = PrevInMicrophoneRange;
        distanceJob.PrevInHearingRange = PrevInHearingRange;
        distanceJob.PrevInAvatarRange = PrevInAvatarRange;

        distanceJob.MeshLodLevel = MeshLodLevel;
        distanceJob.PrevMeshLodLevel = prevMeshLodLevel;
        distanceJob.MeshLodRange = MeshLodRange;

        distanceJob.PerIndexMinD2 = perIndexMinD2;
        distanceJob.PerIndexMask = perIndexMask;

        reduceJob.PerIndexMinD2 = perIndexMinD2;
        reduceJob.PerIndexMask = perIndexMask;
        reduceJob.SmallestD2 = smallestD2;
        reduceJob.ChangeMask = changeMask;

        LengthOfArrays = -1; // will be set on next Simulate call
    }

    public bool CanDoSimulate(float intervalUsed, out BasisAvatar basisAvatar)
    {
        var player = BasisNetworkTransmitter != null ? BasisNetworkTransmitter.Player : null;
        basisAvatar = player != null ? player.BasisAvatar : null;

        if (basisAvatar == null)
        {
            BasisDebug.LogError("Missing Basis Avatar. Cannot send network update.", BasisDebug.LogTag.System);
            timer = math.max(0f, timer - intervalUsed);
            return false;
        }

        return true;
    }

    public void Initalize()
    {
        // Track join/leave to force resync against index order changes
        BasisNetworkPlayer.OnRemotePlayerJoined += OnPlayerIndexChanged;
        BasisNetworkPlayer.OnRemotePlayerLeft += OnPlayerIndexChanged;
        capacity = 0;
        LengthOfArrays = -1;
    }

    public void DeInitalize()
    {
        BasisNetworkPlayer.OnRemotePlayerJoined -= OnPlayerIndexChanged;
        BasisNetworkPlayer.OnRemotePlayerLeft -= OnPlayerIndexChanged;

        ReleaseResults();

        if (smallestD2.IsCreated) smallestD2.Dispose();
        if (changeMask.IsCreated) changeMask.Dispose();
    }

    public void OnPlayerIndexChanged(BasisNetworkPlayer bnp, BasisRemotePlayer brp)
    {
        IndexChanged = true;
    }
    /// <summary>
    /// Dispose NativeArrays and complete outstanding jobs.
    /// </summary>
    public void ReleaseResults()
    {
        // Wait for in-flight jobs (both handles)
        if (!distanceJobHandle.IsCompleted) distanceJobHandle.Complete();
        if (!reduceJobHandle.IsCompleted) reduceJobHandle.Complete();

        if (targetPositions.IsCreated) targetPositions.Dispose();
        if (distanceSq.IsCreated) distanceSq.Dispose();

        if (MicrophoneRange.IsCreated) MicrophoneRange.Dispose();
        if (hearingRange.IsCreated) hearingRange.Dispose();
        if (AvatarRange.IsCreated) AvatarRange.Dispose();

        if (PrevInMicrophoneRange.IsCreated) PrevInMicrophoneRange.Dispose();
        if (PrevInHearingRange.IsCreated) PrevInHearingRange.Dispose();
        if (PrevInAvatarRange.IsCreated) PrevInAvatarRange.Dispose();

        if (MeshLodLevel.IsCreated) MeshLodLevel.Dispose();
        if (prevMeshLodLevel.IsCreated) prevMeshLodLevel.Dispose();
        if (MeshLodRange.IsCreated) MeshLodRange.Dispose();

        if (perIndexMinD2.IsCreated) perIndexMinD2.Dispose();
        if (perIndexMask.IsCreated) perIndexMask.Dispose();

        if (hasRealAvatarLoaded.IsCreated) hasRealAvatarLoaded.Dispose();
        if (avatarCapEntries.IsCreated) avatarCapEntries.Dispose();

        // Note: smallestD2/changeMask are 1-length arrays kept across reallocs; disposed in DeInitalize.
        capacity = 0;
        LengthOfArrays = -1;
    }

    private static void Swap<T>(ref NativeArray<T> a, ref NativeArray<T> b) where T : struct
    {
        NativeArray<T> tmp = a;
        a = b;
        b = tmp;
    }

    /// <summary>
    /// Reduces PCM volume for remote players outside the listener's forward cone.
    /// Sets <see cref="BasisAudioReceiver.DirectionalDampeningMultiplier"/> which is
    /// applied per-sample inside OnAudioFilterRead, bypassing AudioSource.volume
    /// (which Steam Audio's spatializer ignores for filter-written audio).
    /// </summary>
    private void ApplyListenerDirectionalDampening(IReadOnlyList<BasisNetworkReceiver> snapshot, int receiverCount)
    {
        float coneAngle = BasisSettingsDefaults.RAListenerConeAngle.RawValue;

        // 360 means no dampening
        if (coneAngle >= 360f)
        {
            for (int i = 0; i < receiverCount; i++)
            {
                var module = snapshot[i].AudioReceiverModule;
                module.DirectionalDampeningMultiplier = 1f;
            }
            return;
        }

        float dampenPercent = Mathf.Clamp(BasisSettingsDefaults.RAListenerDampenAmount.RawValue, 1f, 95f);
        float minVolume = 1f - (dampenPercent / 100f);

        float halfConeRad = (coneAngle * 0.5f) * Mathf.Deg2Rad;
        float cosHalfCone = Mathf.Cos(halfConeRad);
        float cosRange = cosHalfCone - (-1f); // cosHalfCone + 1

        Vector3 listenerPos = BasisLocalCameraDriver.Position;
        Vector3 listenerFwd = BasisLocalCameraDriver.Forward();

        for (int i = 0; i < receiverCount; i++)
        {
            var module = snapshot[i].AudioReceiverModule;

            Vector3 toSource = ((Vector3)targetPositions[i]) - listenerPos;
            float sqrMag = toSource.sqrMagnitude;

            if (sqrMag < 0.001f)
            {
                module.DirectionalDampeningMultiplier = 1f;
                continue;
            }

            float invMag = 1f / Mathf.Sqrt(sqrMag);
            Vector3 dir = toSource * invMag;
            float dot = Vector3.Dot(listenerFwd, dir);

            if (dot >= cosHalfCone)
            {
                module.DirectionalDampeningMultiplier = 1f;
            }
            else
            {
                float t = (cosHalfCone - dot) / cosRange;
                module.DirectionalDampeningMultiplier = Mathf.Lerp(1f, minVolume, t);
            }
        }
    }
}

/// <summary>
/// Sortable entry for the avatar visibility cap. Sorted by effective squared
/// distance (with stickiness bonus baked in) via NativeArray.Sort().
/// </summary>
public struct AvatarCapEntry : System.IComparable<AvatarCapEntry>
{
    public int Index;
    public float EffectiveDistSq;

    public int CompareTo(AvatarCapEntry other)
    {
        return EffectiveDistSq.CompareTo(other.EffectiveDistSq);
    }
}
