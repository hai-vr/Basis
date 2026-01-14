using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public struct BasisDistanceJob : IJob
{
    public float SquaredVoiceDistance;
    public float SquaredHearingDistance;
    public float SquaredAvatarDistance;

    public float HysteresisPercent;   // e.g. 0.10f
    public float ReductionMultiplier;

    [ReadOnly] public float3 referencePosition;
    [ReadOnly] public NativeArray<float3> targetPositions;

    [ReadOnly] public NativeArray<bool> PrevInMicrophoneRange;
    [ReadOnly] public NativeArray<bool> PrevInHearingRange;
    [ReadOnly] public NativeArray<bool> PrevInAvatarRange;

    // IMPORTANT: this must be readable (previous frame)
    [ReadOnly] public NativeArray<short> PrevMeshLodLevel;

    [WriteOnly] public NativeArray<float> distanceSq;
    [WriteOnly] public NativeArray<short> MeshLodLevel;

    [WriteOnly] public NativeArray<bool> MicrophoneRange;
    [WriteOnly] public NativeArray<bool> hearingRange;
    [WriteOnly] public NativeArray<bool> AvatarRange;
    [WriteOnly] public NativeArray<bool> MeshLodRange;
    /// <summary>
    /// [0]=AnyMicrophoneRangeChanged, [1]=AnyHearingRangeChanged, [2]=AnyAvatarRangeChanged, [3]=AnyLodChanged
    /// </summary>
    [WriteOnly] public NativeArray<bool> AnyChangedArray;

    [WriteOnly] public NativeArray<float> SMD;

    public void Execute()
    {
        float3 refPos = referencePosition;
        float smallestDistance = float.PositiveInfinity;
        int length = targetPositions.Length;

        bool anyMicChanged = false;
        bool anyHearChanged = false;
        bool anyAvatarChanged = false;
        bool anyLodChanged = false;

        float exitMul = HysteresisPercent;

        float voiceEnter = SquaredVoiceDistance;
        float hearEnter = SquaredHearingDistance;
        float avEnter = SquaredAvatarDistance;

        float voiceExit = voiceEnter * exitMul;
        float hearExit = hearEnter * exitMul;
        float avExit = avEnter * exitMul;

        for (int i = 0; i < length; i++)
        {
            float3 diff = targetPositions[i] - refPos;
            float d2 = math.lengthsq(diff);
            distanceSq[i] = d2;

            bool prevVoice = PrevInMicrophoneRange[i];
            bool prevHearing = PrevInHearingRange[i];
            bool prevAvatar = PrevInAvatarRange[i];

            bool voice = prevVoice ? (d2 < voiceExit) : (d2 < voiceEnter);
            bool hearing = prevHearing ? (d2 < hearExit) : (d2 < hearEnter);
            bool avatar = prevAvatar ? (d2 < avExit) : (d2 < avEnter);

            MicrophoneRange[i] = voice;
            hearingRange[i] = hearing;
            AvatarRange[i] = avatar;

            if (voice != prevVoice)
            {
                anyMicChanged = true;
            }

            if (hearing != prevHearing)
            {
                anyHearChanged = true;
            }

            if (avatar != prevAvatar)
            {
                anyAvatarChanged = true;
            }

            smallestDistance = math.min(smallestDistance, d2);

            // Normalize to [0,1] if ReductionMultiplier is set accordingly
            float normalized = d2 * ReductionMultiplier;

            // 0–3 LOD
            int lod = (int)math.floor(normalized * 4f);
            lod = math.clamp(lod, 0, 3);
            short newLod = (short)lod;

            // Detect change vs last run
            bool ChangedState = newLod != PrevMeshLodLevel[i];
            MeshLodRange[i] = ChangedState;
            if (ChangedState)
            {
                anyLodChanged = true;
            }

            MeshLodLevel[i] = newLod;
        }

        SMD[0] = smallestDistance;
        AnyChangedArray[0] = anyMicChanged;
        AnyChangedArray[1] = anyHearChanged;
        AnyChangedArray[2] = anyAvatarChanged;
        AnyChangedArray[3] = anyLodChanged;
    }
}
