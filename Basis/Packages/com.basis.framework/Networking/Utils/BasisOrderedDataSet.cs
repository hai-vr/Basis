using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class BasisOrderedDataSet : MonoBehaviour
{
    // slot -> muscle index (exactly your existing order, skipping 15..20)
    public static readonly int[] WRITE_ORDER = new int[]
    {
        // 0..14
        0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,

        // Left Leg
        21,22,23,24,25,26,27,28,

        // Right Leg
        29,30,31,32,33,34,35,36,

        // Left Arm
        37,38,39,40,41,42,43,44,45,

        // Right Arm
        46,47,48,49,50,51,52,53,54,

        // Left Hand Fingers
        55,56,57,58,59,60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,

        // Right Hand Fingers
        75,76,77,78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,
    };
    public static readonly byte[] BITS_PER_SLOT = new byte[]
    {
    // ----------------------
    // Spine/Chest/Head (slots 0..14 -> muscles 0..14)
    // Range 80 -> 15 bits (step ~0.0024)
    // Range 40 -> 14 bits (step ~0.0024)
    // ----------------------
    15,15,15,   // 0 Spine FB, 1 Spine LR, 2 Spine Twist (80)
    15,15,15,   // 3 Chest FB, 4 Chest LR, 5 Chest Twist (80)
    14,14,14,   // 6 UpperChest FB, 7 UpperChest LR, 8 UpperChest Twist (40)
    15,15,15,   // 9 Neck Nod, 10 Neck Tilt, 11 Neck Turn (80)
    15,15,15,   // 12 Head Nod, 13 Head Tilt, 14 Head Turn (80)

    // ----------------------
    // Left Leg (slots 15..22 -> muscles 21..28)
    // Big leg joints: keep fine-ish.
    // ----------------------
    15,15,15,   // 21 UpperLeg FB (140), 22 UpperLeg InOut (120), 23 UpperLeg Twist (120)
    15,16,15,   // 24 LowerLeg Stretch (160), 25 LowerLeg Twist (180), 26 Foot UpDown (100)
    13,8,      // 27 Foot Twist (60)  -> 13 (step ~0.0073) MUCH better than 8-bit
                // 28 Toes UpDown (100) -> 15 (step ~0.0031)

    // ----------------------
    // Right Leg (slots 23..30 -> muscles 29..36)
    // ----------------------
    15,15,15,   // 29 UpperLeg FB (140), 30 UpperLeg InOut (120), 31 UpperLeg Twist (120)
    15,16,15,   // 32 LowerLeg Stretch (160), 33 LowerLeg Twist (180), 34 Foot UpDown (100)
    13,8,      // 35 Foot Twist (60) -> 13, 36 Toes UpDown (100) -> 15

    // ----------------------
    // Left Arm (slots 31..39 -> muscles 37..45)
    // Arms have some huge ranges (160..200). Keep those higher.
    // Shoulder ranges are smaller (45/30) so we can drop.
    // ----------------------
    12,12,      // 37 Shoulder DownUp (45), 38 Shoulder FrontBack (30)
    16,16,16,   // 39 Arm DownUp (160), 40 Arm FrontBack (200), 41 Arm Twist (180)
    15,16,      // 42 Forearm Stretch (160), 43 Forearm Twist (180)
    15,14,      // 44 Hand DownUp (160), 45 Hand InOut (80)

    // ----------------------
    // Right Arm (slots 40..48 -> muscles 46..54)
    // ----------------------
    12,12,      // 46 Shoulder DownUp (45), 47 Shoulder FrontBack (30)
    16,16,16,   // 48 Arm DownUp (160), 49 Arm FrontBack (200), 50 Arm Twist (180)
    15,16,      // 51 Forearm Stretch (160), 52 Forearm Twist (180)
    15,14,      // 53 Hand DownUp (160), 54 Hand InOut (80)

    // ----------------------
    // Left Hand Fingers (slots 49..68 -> muscles 55..74)
    //
    // especially on fast networked motion. Here we push bends to 13–14 and spreads to 11–12.
    //
    // Thumb: ranges 40,50,75,75
    // Index/Middle/Ring/Little: 1 stretch ~100, spreads 15 or 40, 2/3 stretches ~90
    // ----------------------
    8,13,8,8,    // 55 Thumb1 (40), 56 ThumbSpread (50), 57 Thumb2 (75), 58 Thumb3 (75)
    8,12,8,8,    // 59 Index1 (100), 60 IndexSpread (40), 61 Index2 (90), 62 Index3 (90)
    8,11,8,8,    // 63 Middle1 (100), 64 MiddleSpread (15), 65 Middle2 (90), 66 Middle3 (90)
    8,11,8,8,    // 67 Ring1 (100), 68 RingSpread (15), 69 Ring2 (90), 70 Ring3 (90)
    8,12,8,8,    // 71 Little1 (100), 72 LittleSpread (40), 73 Little2 (90), 74 Little3 (90)

    // ----------------------
    // Right Hand Fingers (slots 69..88 -> muscles 75..94)
    // Mirror of left.
    // ----------------------
    8,13,8,8,    // 75 Thumb1 (40), 76 ThumbSpread (50), 77 Thumb2 (75), 78 Thumb3 (75)
    8,12,8,8,    // 79 Index1 (100), 80 IndexSpread (40), 81 Index2 (90), 82 Index3 (90)
    8,11,8,8,    // 83 Middle1 (100), 84 MiddleSpread (15), 85 Middle2 (90), 86 Middle3 (90)
    8,11,8,8,    // 87 Ring1 (100), 88 RingSpread (15), 89 Ring2 (90), 90 Ring3 (90)
    8,12,8,8,    // 91 Little1 (100), 92 LittleSpread (40), 93 Little2 (90), 94 Little3 (90)
    };
    public static float[] MinMuscle;
    public static float[] MaxMuscle;
    public static float[] RangeMuscle;
    public static int TotalMuscles;

    public const ushort UShortMin = ushort.MinValue;
    public const ushort UShortMax = ushort.MaxValue;
    public const ushort UShortRangeDifference = UShortMax - UShortMin;

    public static void Initalize()
    {
        TotalMuscles = HumanTrait.MuscleName.Length; // 95
        MinMuscle = new float[TotalMuscles];
        MaxMuscle = new float[TotalMuscles];
        RangeMuscle = new float[TotalMuscles];

        for (int i = 0; i < TotalMuscles; i++)
        {
            MinMuscle[i] = HumanTrait.GetMuscleDefaultMin(i);
            MaxMuscle[i] = HumanTrait.GetMuscleDefaultMax(i);
            RangeMuscle[i] = MaxMuscle[i] - MinMuscle[i];
        }
    }
    // =======================================================
    // Bitstream decompression (no managed "ToArray" hop needed,
    // but keeping your pattern: NativeArray<float> as output).
    // =======================================================

    public static void DecompressAvatarMuscles_BitPacked(byte[] data, ref NativeArray<float> outputArray, ref int offsetBytes)
    {
        int bitPos = offsetBytes << 3; // bits
        int slots = WRITE_ORDER.Length;

        // You can avoid a managed array by writing directly into outputArray,
        // but outputArray is a NativeArray<float> and you might want it Burst-safe elsewhere.
        // We'll write directly to outputArray here.
        for (int slot = 0; slot < slots; slot++)
        {
            int muscleIndex = WRITE_ORDER[slot];
            int bits = BITS_PER_SLOT[slot];

            uint q = BitReader.ReadBits(data, ref bitPos, bits);

            // dequantize to 0..1
            uint maxQ = (bits >= 32) ? 0xFFFFFFFFu : ((1u << bits) - 1u);
            float norm = (maxQ == 0u) ? 0f : (q / (float)maxQ);

            float min = MinMuscle[muscleIndex];
            float max = MaxMuscle[muscleIndex];
            float range = RangeMuscle[muscleIndex];

            float value = min + norm * range;
            outputArray[muscleIndex] = math.clamp(value, min, max);
        }

        offsetBytes = (bitPos + 7) >> 3; // advance to next whole byte
    }
    // -------------------------------------------------------
    // Small bit reader helper (LSB-first within the stream).
    // Matches the writer below in the compressor.
    // -------------------------------------------------------
    static class BitReader
    {
        public static uint ReadBits(byte[] src, ref int bitPos, int bitCount)
        {
            int bytePos = bitPos >> 3;
            int bitInByte = bitPos & 7;

            uint outV = 0;
            int outShift = 0;

            int bitsLeft = bitCount;
            while (bitsLeft > 0)
            {
                int room = 8 - bitInByte;
                int take = bitsLeft < room ? bitsLeft : room;

                uint mask = (uint)((1 << take) - 1);
                uint chunk = (uint)(src[bytePos] >> bitInByte) & mask;

                outV |= (chunk << outShift);

                outShift += take;
                bitsLeft -= take;
                bytePos++;
                bitInByte = 0;
            }

            bitPos += bitCount;
            return outV;
        }
    }
    /*
    // ----------------------
    // Section: Spine/Chest/Head
    // ----------------------
    private static void DecompressSpineChestHead(byte[] data, ref int dataPos, ref float[] floatArray)
    {
        ReadCompressed(data, ref dataPos, 0, false, floatArray); // Spine Front-Back: Range 80
        ReadCompressed(data, ref dataPos, 1, false, floatArray); // Spine Left-Right: Range 80
        ReadCompressed(data, ref dataPos, 2, false, floatArray); // Spine Twist Left-Right: Range 80
        ReadCompressed(data, ref dataPos, 3, false, floatArray); // Chest Front-Back: Range 80
        ReadCompressed(data, ref dataPos, 4, false, floatArray); // Chest Left-Right: Range 80
        ReadCompressed(data, ref dataPos, 5, false, floatArray); // Chest Twist Left-Right: Range 80
        ReadCompressed(data, ref dataPos, 6, false, floatArray); // UpperChest Front-Back: Range 40
        ReadCompressed(data, ref dataPos, 7, false, floatArray); // UpperChest Left-Right: Range 40
        ReadCompressed(data, ref dataPos, 8, false, floatArray); // UpperChest Twist Left-Right: Range 40
        ReadCompressed(data, ref dataPos, 9, false, floatArray); // Neck Nod Down-Up: Range 80
        ReadCompressed(data, ref dataPos, 10, false, floatArray); // Neck Tilt Left-Right: Range 80
        ReadCompressed(data, ref dataPos, 11, false, floatArray); // Neck Turn Left-Right: Range 80
        ReadCompressed(data, ref dataPos, 12, false, floatArray); // Head Nod Down-Up: Range 80
        ReadCompressed(data, ref dataPos, 13, false, floatArray); // Head Tilt Left-Right: Range 80
        ReadCompressed(data, ref dataPos, 14, false, floatArray); // Head Turn Left-Right: Range 80
    }

    // ----------------------
    // Section: Eyes/Jaw (Skipped like original)
    // ----------------------
    private static void DecompressEyesJaw(byte[] data, ref int dataPos, float[] floatArray)
    {
        // no need to put this data on the network! 6 in total (saves between 6 and 16 bytes)
        // ReadCompressed(data, ref dataPos, 15, true,  floatArray); // Left Eye Down-Up: Range 25 byteable
        // ReadCompressed(data, ref dataPos, 16, true,  floatArray); // Left Eye In-Out: Range 40 byteable
        // ReadCompressed(data, ref dataPos, 17, true,  floatArray); // Right Eye Down-Up: Range 25 byteable
        // ReadCompressed(data, ref dataPos, 18, true,  floatArray); // Right Eye In-Out: Range 40 byteable
        // ReadCompressed(data, ref dataPos, 19, true,  floatArray); // Jaw Close: Range 20 byteable
        // ReadCompressed(data, ref dataPos, 20, true,  floatArray); // Jaw Left-Right: Range 20 byteable
    }

    // ----------------------
    // Section: Left Leg
    // ----------------------
    private static void DecompressLeftLeg(byte[] data, ref int dataPos, float[] floatArray)
    {
        ReadCompressed(data, ref dataPos, 21, false, floatArray); // Left Upper Leg Front-Back: Range 140
        ReadCompressed(data, ref dataPos, 22, false, floatArray); // Left Upper Leg In-Out: Range 120
        ReadCompressed(data, ref dataPos, 23, false, floatArray); // Left Upper Leg Twist In-Out: Range 120
        ReadCompressed(data, ref dataPos, 24, false, floatArray); // Left Lower Leg Stretch: Range 160
        ReadCompressed(data, ref dataPos, 25, false, floatArray); // Left Lower Leg Twist In-Out: Range 180
        ReadCompressed(data, ref dataPos, 26, false, floatArray); // Left Foot Up-Down: Range 100
        ReadCompressed(data, ref dataPos, 27, true, floatArray); // Left Foot Twist In-Out: Range 60 byteable
        ReadCompressed(data, ref dataPos, 28, false, floatArray); // Left Toes Up-Down: Range 100 byteable
    }

    // ----------------------
    // Section: Right Leg
    // ----------------------
    private static void DecompressRightLeg(byte[] data, ref int dataPos, float[] floatArray)
    {
        ReadCompressed(data, ref dataPos, 29, false, floatArray); // Right Upper Leg Front-Back: Range 140
        ReadCompressed(data, ref dataPos, 30, false, floatArray); // Right Upper Leg In-Out: Range 120
        ReadCompressed(data, ref dataPos, 31, false, floatArray); // Right Upper Leg Twist In-Out: Range 120
        ReadCompressed(data, ref dataPos, 32, false, floatArray); // Right Lower Leg Stretch: Range 160
        ReadCompressed(data, ref dataPos, 33, false, floatArray); // Right Lower Leg Twist In-Out: Range 180
        ReadCompressed(data, ref dataPos, 34, false, floatArray); // Right Foot Up-Down: Range 100
        ReadCompressed(data, ref dataPos, 35, true, floatArray); // Right Foot Twist In-Out: Range 60 byteable
        ReadCompressed(data, ref dataPos, 36, false, floatArray); // Right Toes Up-Down: Range 100 byteable
    }

    // ----------------------
    // Section: Left Arm
    // ----------------------
    private static void DecompressLeftArm(byte[] data, ref int dataPos, float[] floatArray)
    {
        ReadCompressed(data, ref dataPos, 37, false, floatArray); // Left Shoulder Down-Up: Range 45 byteable
        ReadCompressed(data, ref dataPos, 38, false, floatArray); // Left Shoulder Front-Back: Range 30 byteable
        ReadCompressed(data, ref dataPos, 39, false, floatArray); // Left Arm Down-Up: Range 160
        ReadCompressed(data, ref dataPos, 40, false, floatArray); // Left Arm Front-Back: Range 200
        ReadCompressed(data, ref dataPos, 41, false, floatArray); // Left Arm Twist In-Out: Range 180
        ReadCompressed(data, ref dataPos, 42, false, floatArray); // Left Forearm Stretch: Range 160
        ReadCompressed(data, ref dataPos, 43, false, floatArray); // Left Forearm Twist In-Out: Range 180
        ReadCompressed(data, ref dataPos, 44, false, floatArray); // Left Hand Down-Up: Range 160
        ReadCompressed(data, ref dataPos, 45, false, floatArray); // Left Hand In-Out: Range 80
    }

    // ----------------------
    // Section: Right Arm
    // ----------------------
    private static void DecompressRightArm(byte[] data, ref int dataPos, float[] floatArray)
    {
        ReadCompressed(data, ref dataPos, 46, false, floatArray); // Right Shoulder Down-Up: Range 45 byteable
        ReadCompressed(data, ref dataPos, 47, false, floatArray); // Right Shoulder Front-Back: Range 30 byteable
        ReadCompressed(data, ref dataPos, 48, false, floatArray); // Right Arm Down-Up: Range 160
        ReadCompressed(data, ref dataPos, 49, false, floatArray); // Right Arm Front-Back: Range 200
        ReadCompressed(data, ref dataPos, 50, false, floatArray); // Right Arm Twist In-Out: Range 180
        ReadCompressed(data, ref dataPos, 51, false, floatArray); // Right Forearm Stretch: Range 160
        ReadCompressed(data, ref dataPos, 52, false, floatArray); // Right Forearm Twist In-Out: Range 180
        ReadCompressed(data, ref dataPos, 53, false, floatArray); // Right Hand Down-Up: Range 160
        ReadCompressed(data, ref dataPos, 54, false, floatArray); // Right Hand In-Out: Range 80
    }

    // ----------------------
    // Section: Left Hand Fingers
    // ----------------------
    private static void DecompressLeftHandFingers(byte[] data, ref int dataPos, float[] floatArray)
    {
        ReadCompressed(data, ref dataPos, 55, false, floatArray); // Left Thumb 1 Stretched: Range 40 byteable
        ReadCompressed(data, ref dataPos, 56, false, floatArray); // Left Thumb Spread: Range 50 byteable
        ReadCompressed(data, ref dataPos, 57, true, floatArray); // Left Thumb 2 Stretched: Range 75 byteable
        ReadCompressed(data, ref dataPos, 58, true, floatArray); // Left Thumb 3 Stretched: Range 75 byteable

        ReadCompressed(data, ref dataPos, 59, false, floatArray); // Left Index 1 Stretched: Range 100 byteable
        ReadCompressed(data, ref dataPos, 60, true, floatArray); // Left Index Spread: Range 40 byteable
        ReadCompressed(data, ref dataPos, 61, true, floatArray); // Left Index 2 Stretched: Range 90 byteable
        ReadCompressed(data, ref dataPos, 62, true, floatArray); // Left Index 3 Stretched: Range 90 byteable

        ReadCompressed(data, ref dataPos, 63, false, floatArray); // Left Middle 1 Stretched: Range 100 byteable
        ReadCompressed(data, ref dataPos, 64, true, floatArray); // Left Middle Spread: Range 15 byteable
        ReadCompressed(data, ref dataPos, 65, true, floatArray); // Left Middle 2 Stretched: Range 90 byteable
        ReadCompressed(data, ref dataPos, 66, true, floatArray); // Left Middle 3 Stretched: Range 90 byteable

        ReadCompressed(data, ref dataPos, 67, false, floatArray); // Left Ring 1 Stretched: Range 100 byteable
        ReadCompressed(data, ref dataPos, 68, true, floatArray); // Left Ring Spread: Range 15 byteable
        ReadCompressed(data, ref dataPos, 69, true, floatArray); // Left Ring 2 Stretched: Range 90 byteable
        ReadCompressed(data, ref dataPos, 70, true, floatArray); // Left Ring 3 Stretched: Range 90 byteable

        ReadCompressed(data, ref dataPos, 71, false, floatArray); // Left Little 1 Stretched: Range 100 byteable
        ReadCompressed(data, ref dataPos, 72, true, floatArray); // Left Little Spread: Range 40 byteable
        ReadCompressed(data, ref dataPos, 73, true, floatArray); // Left Little 2 Stretched: Range 90 byteable
        ReadCompressed(data, ref dataPos, 74, true, floatArray); // Left Little 3 Stretched: Range 90 byteable
    }

    // ----------------------
    // Section: Right Hand Fingers
    // ----------------------
    private static void DecompressRightHandFingers(byte[] data, ref int dataPos, float[] floatArray)
    {
        ReadCompressed(data, ref dataPos, 75, false, floatArray); // Right Thumb 1 Stretched: Range 40 byteable
        ReadCompressed(data, ref dataPos, 76, false, floatArray); // Right Thumb Spread: Range 50 byteable
        ReadCompressed(data, ref dataPos, 77, true, floatArray); // Right Thumb 2 Stretched: Range 75 byteable
        ReadCompressed(data, ref dataPos, 78, true, floatArray); // Right Thumb 3 Stretched: Range 75 byteable

        ReadCompressed(data, ref dataPos, 79, false, floatArray); // Right Index 1 Stretched: Range 100 byteable
        ReadCompressed(data, ref dataPos, 80, true, floatArray); // Right Index Spread: Range 40 byteable
        ReadCompressed(data, ref dataPos, 81, true, floatArray); // Right Index 2 Stretched: Range 90 byteable
        ReadCompressed(data, ref dataPos, 82, true, floatArray); // Right Index 3 Stretched: Range 90 byteable

        ReadCompressed(data, ref dataPos, 83, false, floatArray); // Right Middle 1 Stretched: Range 100 byteable
        ReadCompressed(data, ref dataPos, 84, true, floatArray); // Right Middle Spread: Range 15 byteable
        ReadCompressed(data, ref dataPos, 85, true, floatArray); // Right Middle 2 Stretched: Range 90 byteable
        ReadCompressed(data, ref dataPos, 86, true, floatArray); // Right Middle 3 Stretched: Range 90 byteable

        ReadCompressed(data, ref dataPos, 87, false, floatArray); // Right Ring 1 Stretched: Range 100 byteable
        ReadCompressed(data, ref dataPos, 88, true, floatArray); // Right Ring Spread: Range 15 byteable
        ReadCompressed(data, ref dataPos, 89, true, floatArray); // Right Ring 2 Stretched: Range 90 byteable
        ReadCompressed(data, ref dataPos, 90, true, floatArray); // Right Ring 3 Stretched: Range 90 byteable

        ReadCompressed(data, ref dataPos, 91, false, floatArray); // Right Little 1 Stretched: Range 100 byteable
        ReadCompressed(data, ref dataPos, 92, true, floatArray); // Right Little Spread: Range 40 byteable
        ReadCompressed(data, ref dataPos, 93, true, floatArray); // Right Little 2 Stretched: Range 90 byteable
        ReadCompressed(data, ref dataPos, 94, true, floatArray); // Right Little 3 Stretched: Range 90 byteable
    }
    private static void ReadCompressed(byte[] data, ref int dataPos, int index, bool asByte, float[] floatArray)
    {
        float normalized;
        if (asByte)
        {
            byte compressed = data[dataPos];
            normalized = compressed / 255f;
            dataPos += 1;
        }
        else
        {
            // Little-endian ushort from two bytes
            int lo = data[dataPos];
            int hi = data[dataPos + 1];
            ushort compressed = (ushort)(lo | (hi << 8));
            normalized = compressed / 65535f;
            dataPos += 2;
        }

        float min = BasisOrderedDataSet.MinMuscle[index];
        float range = BasisOrderedDataSet.RangeMuscle[index];
        float max = BasisOrderedDataSet.MaxMuscle[index];

        float value = min + normalized * range;
        floatArray[index] = math.clamp(value, min, max);
    }
        */
}
