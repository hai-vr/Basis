using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace Basis.Scripts.Rendering
{
    /// <summary>
    /// Bounds refresh for every entry that has a root transform. The write is scattered — dense row
    /// to database slot — so the parallel-for restriction has to come off; it stays safe because
    /// <c>DenseToSlot</c> is a bijection, no two rows ever address the same slot.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public struct BasisVisibilityBoundsJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<int> DenseToSlot;
        [ReadOnly][NativeDisableParallelForRestriction] public NativeArray<float3> BaseExtents;
        [NativeDisableParallelForRestriction] public NativeArray<float3> Centers;
        [NativeDisableParallelForRestriction] public NativeArray<float3> Extents;

        public void Execute(int index, TransformAccess transform)
        {
            int slot = DenseToSlot[index];
            if (slot < 0)
            {
                return;
            }
            Centers[slot] = transform.position;
            Extents[slot] = BaseExtents[slot] * BasisVisibilityMath.MaxAxisScale(transform.localToWorldMatrix);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public struct BasisVisibilityFrustumJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Centers;
        [ReadOnly] public NativeArray<float3> Extents;
        [ReadOnly] public NativeArray<uint> Flags;
        [ReadOnly] public NativeArray<BasisVisibilityCamera> Cameras;

        public int CameraCount;
        public float Margin;

        [WriteOnly] public NativeArray<uint> VisibleMask;

        public void Execute(int index)
        {
            uint flags = Flags[index];

            if ((flags & (uint)BasisVisibilityFlags.Active) == 0)
            {
                VisibleMask[index] = 0u;
                return;
            }

            bool forced = (flags & (uint)BasisVisibilityFlags.AlwaysVisible) != 0
                || (flags & (uint)BasisVisibilityFlags.CullEligible) == 0;

            if (forced || CameraCount <= 0)
            {
                VisibleMask[index] = uint.MaxValue;
                return;
            }

            float3 center = Centers[index];
            float3 extents = Extents[index] + Margin;

            uint mask = 0u;
            for (int camera = 0; camera < CameraCount; camera++)
            {
                if (TestAabb(Cameras[camera], center, extents))
                {
                    mask |= 1u << camera;
                }
            }
            VisibleMask[index] = mask;
        }

        private static bool TestAabb(BasisVisibilityCamera camera, float3 center, float3 extents)
        {
            return Inside(camera.Plane0, center, extents)
                && Inside(camera.Plane1, center, extents)
                && Inside(camera.Plane2, center, extents)
                && Inside(camera.Plane3, center, extents)
                && Inside(camera.Plane4, center, extents)
                && Inside(camera.Plane5, center, extents);
        }

        private static bool Inside(float4 plane, float3 center, float3 extents)
        {
            float3 normal = plane.xyz;
            float radius = math.dot(extents, math.abs(normal));
            float distance = math.dot(normal, center) + plane.w;
            return distance + radius >= 0f;
        }
    }

    [BurstCompile]
    public struct BasisVisibilityChangeJob : IJob
    {
        [ReadOnly] public NativeArray<uint> VisibleMask;
        [ReadOnly] public NativeArray<byte> AppliedVisible;
        [ReadOnly] public NativeArray<uint> Flags;

        public int Count;
        public NativeList<int> Changed;

        public void Execute()
        {
            Changed.Clear();
            for (int index = 0; index < Count; index++)
            {
                if ((Flags[index] & (uint)BasisVisibilityFlags.Active) == 0)
                {
                    continue;
                }
                byte wanted = VisibleMask[index] != 0u ? (byte)1 : (byte)0;
                if (wanted != AppliedVisible[index])
                {
                    Changed.Add(index);
                }
            }
        }
    }
}
