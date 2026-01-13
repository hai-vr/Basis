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

    // 10% hysteresis (exit threshold multiplier)
    public float HysteresisPercent; // set to 0.10f (or leave 0 and it defaults to 0.10f below)

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
        float smallestDistance = float.PositiveInfinity;
        int length = targetPositions.Length;

        bool anyMicChanged = false;
        bool anyHearChanged = false;
        bool anyAvatarChanged = false;

        float h = (HysteresisPercent > 0f) ? HysteresisPercent : 0.10f;
        float exitMul = 1f + h;

        // Enter thresholds (base)
        float voiceEnter = SquaredVoiceDistance;
        float hearEnter = SquaredHearingDistance;
        float avEnter = SquaredAvatarDistance;

        // Exit thresholds (looser, so you stay "in" until you go 10% farther)
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

            // Hysteresis logic:
            // - If you were OUT, you only enter when d2 < enterThreshold.
            // - If you were IN, you only exit when d2 >= exitThreshold.
            bool voice = prevVoice ? (d2 < voiceExit) : (d2 < voiceEnter);
            bool hearing = prevHearing ? (d2 < hearExit) : (d2 < hearEnter);
            bool avatar = prevAvatar ? (d2 < avExit) : (d2 < avEnter);

            MicrophoneRange[i] = voice;
            hearingRange[i] = hearing;
            AvatarRange[i] = avatar;

            if (voice != prevVoice) anyMicChanged = true;
            if (hearing != prevHearing) anyHearChanged = true;
            if (avatar != prevAvatar) anyAvatarChanged = true;

            smallestDistance = math.min(smallestDistance, d2);
        }

        SMD[0] = smallestDistance;
        AnyChangedArray[0] = anyMicChanged;
        AnyChangedArray[1] = anyHearChanged;
        AnyChangedArray[2] = anyAvatarChanged;
    }
}
