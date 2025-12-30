using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
public struct BasisDistanceJob : IJob
{
    public float SquaredVoiceDistance;
    public float SquaredHearingDistance;
    public float SquaredAvatarDistance;

    [ReadOnly] public float3 referencePosition;
    [ReadOnly] public NativeArray<float3> targetPositions;

    [ReadOnly] public NativeArray<bool> PrevInMicrophoneRange;
    [ReadOnly] public NativeArray<bool> PrevInHearingRange;
    [ReadOnly] public NativeArray<bool> PrevInAvatarRange;

    [WriteOnly] public NativeArray<float> distanceSq;
    [WriteOnly] public NativeArray<bool> MicrophoneRange;
    [WriteOnly] public NativeArray<bool> hearingRange;
    [WriteOnly] public NativeArray<bool> AvatarRange;

    /// <summary>
    /// AnyMicrophoneRangeChanged AnyHearingRangeChanged AnyAvatarRangeChanged AnyIdOrderOrLengthChanged;
    /// </summary>
    [WriteOnly] public NativeArray<bool> AnyChangedArray;
    [WriteOnly] public NativeArray<float> SMD;
    public void Execute()
    {
        float3 refPos = referencePosition;
        float SmallestDistance = float.PositiveInfinity;
        int length = targetPositions.Length;

        bool AnyMicrophoneRangeChanged = false;
        bool AnyHearingRangeChanged = false;
        bool AnyAvatarRangeChanged = false;

        for (int Index = 0; Index < length; Index++)
        {
            float3 diff = targetPositions[Index] - refPos;
            float d2 = math.lengthsq(diff);
            distanceSq[Index] = d2;

            bool prevDist = PrevInMicrophoneRange[Index];
            bool prevHear = PrevInHearingRange[Index];
            bool prevAvatar = PrevInAvatarRange[Index];

            bool Voice = d2 < SquaredVoiceDistance;
            bool Hearing = d2 < SquaredHearingDistance;
            bool Avatar = d2 < SquaredAvatarDistance;

            MicrophoneRange[Index] = Voice;
            hearingRange[Index] = Hearing;
            AvatarRange[Index] = Avatar;

            if (Voice != prevDist)
            {
                AnyMicrophoneRangeChanged = true;
            }
            if (Hearing != prevHear)
            {
                AnyHearingRangeChanged = true;
            }
            if (Avatar != prevAvatar)
            {
                AnyAvatarRangeChanged = true;
            }
            SmallestDistance = math.min(SmallestDistance, d2);
        }
        SMD[0] = SmallestDistance;
        AnyChangedArray[0] = AnyMicrophoneRangeChanged;
        AnyChangedArray[1] = AnyHearingRangeChanged;
        AnyChangedArray[2] = AnyAvatarRangeChanged;
    }
}
