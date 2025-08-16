using UnityEngine;
using static SerializableBasis;

public static class BasisAvatarMuscleRange
{
    public const int SizeAfterGap = 95 - SecondBuffer;
    public const int FirstBuffer = 15;
    public const int SecondBuffer = 21;

    public static float[] MinMuscle;
    public static float[] MaxMuscle;
    public static float[] RangeMuscle;
    public static string[] MusclesName;
    public static string[] NonFingerMuscles;
    public static string[] FingerMuscles;
    public static int splitIndex;
    public static void Initalize()
    {
        MinMuscle = new float[LocalAvatarSyncMessage.StoredBones];
        MaxMuscle = new float[LocalAvatarSyncMessage.StoredBones];
        RangeMuscle = new float[LocalAvatarSyncMessage.StoredBones];
        MusclesName = new string[LocalAvatarSyncMessage.StoredBones];

        splitIndex = -1;

        for (int Index = 0, MuscleIndex = 0; Index < LocalAvatarSyncMessage.StoredBones; Index++)
        {
            if (Index < FirstBuffer || Index > SecondBuffer)
            {
                MinMuscle[MuscleIndex] = HumanTrait.GetMuscleDefaultMin(Index);
                MaxMuscle[MuscleIndex] = HumanTrait.GetMuscleDefaultMax(Index);
                RangeMuscle[MuscleIndex] = MaxMuscle[MuscleIndex] - MinMuscle[MuscleIndex];
                MusclesName[MuscleIndex] = HumanTrait.MuscleName[MuscleIndex];

                if (MusclesName[MuscleIndex] == "Left Thumb 1 Stretched")
                    splitIndex = MuscleIndex;

                MuscleIndex++;
            }
        }

        // Now split into two arrays
        if (splitIndex >= 0)
        {
            NonFingerMuscles = new string[splitIndex];
            FingerMuscles = new string[MusclesName.Length - splitIndex];

            System.Array.Copy(MusclesName, 0, NonFingerMuscles, 0, splitIndex);
            System.Array.Copy(MusclesName, splitIndex, FingerMuscles, 0, FingerMuscles.Length);
        }

        // Print lengths
        //Debug.Log($"NonFingerMuscles: {NonFingerMuscles.Length}");
        // Debug.Log($"FingerMuscles: {FingerMuscles.Length}");
    }
}
