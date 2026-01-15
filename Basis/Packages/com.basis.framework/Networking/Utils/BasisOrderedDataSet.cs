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
            if (i == 36 || i == 28)//toe joint up down. 
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
    public static void DecompressAvatarMuscles_BitPacked(
     byte[] data,
     BasisAvatarBitPacking.BitQuality quality,
     ref NativeArray<float> outputArray,
     ref int offsetBytes)
    {
        int bitPos = offsetBytes << 3;
        int slots = BasisAvatarBitPacking.WRITE_ORDER.Length;
        byte[] bitsPerSlot = BasisAvatarBitPacking.GetBitsPerSlot(quality);

        for (int slot = 0; slot < slots; slot++)
        {
            int muscleIndex = BasisAvatarBitPacking.WRITE_ORDER[slot];
            int bits = bitsPerSlot[slot];

            uint q = BitReader.ReadBits(data, ref bitPos, bits);

            uint maxQ = (bits >= 32) ? 0xFFFFFFFFu : ((1u << bits) - 1u);
            float norm = (maxQ == 0u) ? 0f : (q / (float)maxQ);

            float min = MinMuscle[muscleIndex];
            float max = MaxMuscle[muscleIndex];
            float range = RangeMuscle[muscleIndex];

            float value = min + norm * range;
            if (!math.isfinite(value)) value = min;

            outputArray[muscleIndex] = math.clamp(value, min, max);
        }

        offsetBytes = (bitPos + 7) >> 3;
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
}
