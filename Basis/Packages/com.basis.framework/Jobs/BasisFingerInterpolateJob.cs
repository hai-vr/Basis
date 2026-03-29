using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;

/// <summary>
/// Burst-compiled job that bilinearly interpolates finger target rotations
/// from the baked 2D pose grid. Runs one work item per finger (10 total).
/// </summary>
[BurstCompile]
public struct BasisFingerInterpolateJob : IJobParallelFor
{
    /// <summary>Baked grid: [gridIdx * 30 + fingerIdx * 3 + jointIdx].</summary>
    [ReadOnly] public NativeArray<quaternion> PoseGrid;

    /// <summary>Per-finger input percentages (10 entries, curl/spread in [-1,1]).</summary>
    [ReadOnly] public NativeArray<float2> Percentages;

    /// <summary>Per-finger output targets (30 entries: fingerIdx * 3 + jointIdx).</summary>
    [WriteOnly]
    [NativeDisableParallelForRestriction]
    public NativeArray<quaternion> Targets;

    public int GridWidth;
    public int GridHeight;
    public float Increment;

    public void Execute(int fingerIndex)
    {
        float2 pct = Percentages[fingerIndex];
        float fx = (pct.x + 1f) / Increment;
        float fy = (pct.y + 1f) / Increment;
        int x0 = math.clamp((int)math.floor(fx), 0, GridWidth - 2);
        int y0 = math.clamp((int)math.floor(fy), 0, GridHeight - 2);
        float tx = math.clamp(fx - x0, 0f, 1f);
        float ty = math.clamp(fy - y0, 0f, 1f);

        int g00 = (x0 * GridHeight + y0) * 30 + fingerIndex * 3;
        int g10 = ((x0 + 1) * GridHeight + y0) * 30 + fingerIndex * 3;
        int g01 = (x0 * GridHeight + y0 + 1) * 30 + fingerIndex * 3;
        int g11 = ((x0 + 1) * GridHeight + y0 + 1) * 30 + fingerIndex * 3;

        int outBase = fingerIndex * 3;
        for (int i = 0; i < 3; i++)
        {
            quaternion bottom = math.slerp(PoseGrid[g00 + i], PoseGrid[g10 + i], tx);
            quaternion top = math.slerp(PoseGrid[g01 + i], PoseGrid[g11 + i], tx);
            Targets[outBase + i] = math.slerp(bottom, top, ty);
        }
    }
}
