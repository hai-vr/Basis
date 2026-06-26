using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace Basis.Scripts.Networking.Sync
{
    /// <summary>
    /// Interpolates every remote synced object in parallel. One index per object slot; each slot
    /// owns contiguous ranges in the shared SoA pools. Continuous → lerp, rotation → nlerp,
    /// discrete → snap to newest.
    /// </summary>
    [BurstCompile]
    public struct InterpolateSyncObjectsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Active;
        [ReadOnly] public NativeArray<float> T;

        [ReadOnly] public NativeArray<int> ContBase;
        [ReadOnly] public NativeArray<int> ContCount;
        [ReadOnly] public NativeArray<int> RotBase;
        [ReadOnly] public NativeArray<int> RotCount;
        [ReadOnly] public NativeArray<int> DiscBase;
        [ReadOnly] public NativeArray<int> DiscCount;

        [ReadOnly] public NativeArray<float> ContCur;
        [ReadOnly] public NativeArray<float> ContNext;
        [ReadOnly] public NativeArray<byte> ContMode;
        [ReadOnly] public NativeArray<quaternion> RotCur;
        [ReadOnly] public NativeArray<quaternion> RotNext;
        [ReadOnly] public NativeArray<byte> RotMode;
        [ReadOnly] public NativeArray<int> DiscNext;

        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> ContOut;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<quaternion> RotOut;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> DiscOut;

        public void Execute(int slot)
        {
            if (Active[slot] == 0) return;
            float t = T[slot];

            int cb = ContBase[slot];
            int cc = ContCount[slot];
            for (int i = 0; i < cc; i++)
            {
                int idx = cb + i;
                byte mode = ContMode[idx];
                if (mode == 0)
                {
                    ContOut[idx] = ContNext[idx];
                }
                else if (mode == 2)
                {
                    float a = ContCur[idx];
                    float delta = math.fmod(ContNext[idx] - a + 540f, 360f) - 180f;
                    ContOut[idx] = a + delta * t;
                }
                else
                {
                    ContOut[idx] = math.lerp(ContCur[idx], ContNext[idx], t);
                }
            }

            int rb = RotBase[slot];
            int rc = RotCount[slot];
            for (int i = 0; i < rc; i++)
            {
                int idx = rb + i;
                if (RotMode[idx] == 0)
                {
                    RotOut[idx] = RotNext[idx];
                    continue;
                }
                quaternion a = RotCur[idx];
                quaternion b = RotNext[idx];
                if (math.dot(a.value, b.value) < 0f) b.value = -b.value;
                RotOut[idx] = math.nlerp(a, b, t);
            }

            int db = DiscBase[slot];
            int dc = DiscCount[slot];
            for (int i = 0; i < dc; i++)
            {
                int idx = db + i;
                DiscOut[idx] = DiscNext[idx];
            }
        }
    }

    /// <summary>
    /// Writes interpolated position/rotation/scale onto bound transforms. A binding may drive any
    /// subset of the three; unbound components (-1) are left untouched.
    /// </summary>
    [BurstCompile]
    public struct ApplySyncTransformsJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<float> ContOut;
        [ReadOnly] public NativeArray<quaternion> RotOut;
        [ReadOnly] public NativeArray<int> PosIdx;
        [ReadOnly] public NativeArray<int> RotIdx;
        [ReadOnly] public NativeArray<int> ScaleIdx;
        [ReadOnly] public NativeArray<byte> WorldSpace;
        [ReadOnly] public NativeArray<byte> Active;
        [ReadOnly] public NativeArray<int> BindSlot;

        public void Execute(int index, TransformAccess transform)
        {
            if (!transform.isValid) return;
            if (Active[BindSlot[index]] == 0) return;

            int pi = PosIdx[index];
            int ri = RotIdx[index];
            int si = ScaleIdx[index];
            bool world = WorldSpace[index] == 1;

            if (pi >= 0 && ri >= 0)
            {
                float3 p = new float3(ContOut[pi], ContOut[pi + 1], ContOut[pi + 2]);
                quaternion r = RotOut[ri];
                if (world) transform.SetPositionAndRotation(p, r);
                else transform.SetLocalPositionAndRotation(p, r);
            }
            else if (pi >= 0)
            {
                float3 p = new float3(ContOut[pi], ContOut[pi + 1], ContOut[pi + 2]);
                if (world) transform.position = p;
                else transform.localPosition = p;
            }
            else if (ri >= 0)
            {
                quaternion r = RotOut[ri];
                if (world) transform.rotation = r;
                else transform.localRotation = r;
            }

            if (si >= 0)
            {
                transform.localScale = new float3(ContOut[si], ContOut[si + 1], ContOut[si + 2]);
            }
        }
    }
}
