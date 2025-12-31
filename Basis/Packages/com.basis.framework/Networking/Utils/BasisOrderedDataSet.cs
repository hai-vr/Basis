using Basis.Network.Core.Compression;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class BasisOrderedDataSet : MonoBehaviour
{
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
            if(i == 36 || i == 28)//toe joint up down. 
            {
                MinMuscle[i] = -20;
                MaxMuscle[i] = 20;
                RangeMuscle[i] = MaxMuscle[i] - MinMuscle[i];
            }
            else
            {
                MinMuscle[i] = HumanTrait.GetMuscleDefaultMin(i);
                MaxMuscle[i] = HumanTrait.GetMuscleDefaultMax(i);
                RangeMuscle[i] = MaxMuscle[i] - MinMuscle[i];
            }
        }
    }
    // =======================================================
    // Bitstream decompression (no managed "ToArray" hop needed,
    // but keeping your pattern: NativeArray<float> as output).
    // =======================================================

    public static void DecompressAvatarMuscles_BitPacked(byte[] data, ref NativeArray<float> outputArray, ref int offsetBytes)
    {
        int bitPos = offsetBytes << 3; // bits
        int slots = BasisBitPackingConstants.WRITE_ORDER.Length;

        // You can avoid a managed array by writing directly into outputArray,
        // but outputArray is a NativeArray<float> and you might want it Burst-safe elsewhere.
        // We'll write directly to outputArray here.
        for (int slot = 0; slot < slots; slot++)
        {
            int muscleIndex = BasisBitPackingConstants.WRITE_ORDER[slot];
            int bits = BasisBitPackingConstants.BITS_PER_SLOT[slot];

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
