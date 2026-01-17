public static class BasisOrderedDataSet
{
    public static int TotalMuscles = 95;
    public const ushort UShortMin = ushort.MinValue;
    public const ushort UShortMax = ushort.MaxValue;
    public const ushort UShortRangeDifference = UShortMax - UShortMin;
    public static float[] MinMuscle = new float[]
    {
        -40f, -40f, -40f, -40f, -40f, -40f,
        -20f, -20f, -20f,
        -40f, -40f, -40f, -40f, -40f, -40f,
        -10f, -20f, -10f, -20f, -10f, -10f,
        -90f, -60f, -60f, -80f, -90f, -50f, -30f, -20f,
        -90f, -60f, -60f, -80f, -90f, -50f, -30f, -20f,
        -15f, -15f,
        -60f, -100f, -90f, -80f, -90f, -80f, -40f,
        -15f, -15f,
        -60f, -100f, -90f, -80f, -90f, -80f, -40f,
        -20f, -25f, -40f, -40f, -50f, -20f,
        -45f, -45f, -50f, -7.5f,
        -45f, -45f, -50f, -7.5f,
        -45f, -45f, -50f, -20f,
        -45f, -45f,
        -20f, -25f, -40f, -40f, -50f, -20f,
        -45f, -45f, -50f, -7.5f,
        -45f, -45f, -50f, -7.5f,
        -45f, -45f, -50f, -20f,
        -45f, -45f
    };

    public static float[] MaxMuscle = new float[]
    {
        40f, 40f, 40f, 40f, 40f, 40f,
        20f, 20f, 20f,
        40f, 40f, 40f, 40f, 40f, 40f,
        15f, 20f, 15f, 20f, 10f, 10f,
        50f, 60f, 60f, 80f, 90f, 50f, 30f, 20f,
        50f, 60f, 60f, 80f, 90f, 50f, 30f, 20f,
        30f, 15f,
        100f, 100f, 90f, 80f, 90f, 80f, 40f,
        30f, 15f,
        100f, 100f, 90f, 80f, 90f, 80f, 40f,
        20f, 25f, 35f, 35f, 50f, 20f,
        45f, 45f, 50f, 7.5f,
        45f, 45f, 50f, 7.5f,
        45f, 45f, 50f, 20f,
        45f, 45f,
        20f, 25f, 35f, 35f, 50f, 20f,
        45f, 45f, 50f, 7.5f,
        45f, 45f, 50f, 7.5f,
        45f, 45f, 50f, 20f,
        45f, 45f
    };

    public static float[] RangeMuscle = new float[]
    {
        80f, 80f, 80f, 80f, 80f, 80f,
        40f, 40f, 40f,
        80f, 80f, 80f, 80f, 80f, 80f,
        25f, 40f, 25f, 40f, 20f, 20f,
        140f, 120f, 120f, 160f, 180f, 100f, 60f, 40f,
        140f, 120f, 120f, 160f, 180f, 100f, 60f, 40f,
        45f, 30f,
        160f, 200f, 180f, 160f, 180f, 160f, 80f,
        45f, 30f,
        160f, 200f, 180f, 160f, 180f, 160f, 80f,
        40f, 50f, 75f, 75f, 100f, 40f,
        90f, 90f, 100f, 15f,
        90f, 90f, 100f, 15f,
        90f, 90f, 100f, 40f,
        90f, 90f,
        40f, 50f, 75f, 75f, 100f, 40f,
        90f, 90f, 100f, 15f,
        90f, 90f, 100f, 15f,
        90f, 90f, 100f, 40f,
        90f, 90f
    };
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
