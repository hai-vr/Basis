using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace GatorDragonGames.JigglePhysics {

[BurstCompile]
public struct JiggleJobBulkReadRoots : IJobParallelForTransform {
    public NativeArray<float3> rootOutputPositions;
    [NativeDisableContainerSafetyRestriction] public NativeArray<int> nonFiniteStages;

    public JiggleJobBulkReadRoots(JiggleMemoryBus bus) {
        rootOutputPositions = bus.rootOutputPositions;
        nonFiniteStages = bus.nonFiniteStages;
    }

    public void UpdateArrays(JiggleMemoryBus bus) {
        rootOutputPositions = bus.rootOutputPositions;
        nonFiniteStages = bus.nonFiniteStages;
    }
    public void Execute(int index, TransformAccess transform) {
        if (!transform.isValid) {
            return;
        }

        float3 position = transform.position;
        if (!math.all(math.isfinite(position))) {
            nonFiniteStages[JiggleMemoryBus.NonFiniteStageRootRead] = 1;
            return;
        }

        rootOutputPositions[index] = position;
    }
}

}