using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace GatorDragonGames.JigglePhysics {

[BurstCompile]
public struct JiggleJobBulkTransformRead : IJobParallelForTransform {
    public NativeArray<JiggleTransform> simulateInputPoses;
    [NativeDisableContainerSafetyRestriction] public NativeArray<int> nonFiniteStages;

    public JiggleJobBulkTransformRead(JiggleMemoryBus bus) {
        simulateInputPoses = bus.inputPosesCurrent;
        nonFiniteStages = bus.nonFiniteStages;
    }

    public void UpdateArrays(JiggleMemoryBus bus) {
        simulateInputPoses = bus.inputPosesCurrent;
        nonFiniteStages = bus.nonFiniteStages;
    }
    
    public void Execute(int index, TransformAccess transform) {
        var jiggleTransform = simulateInputPoses[index];
        if (!transform.isValid || jiggleTransform.isVirtual) {
            return;
        }
        transform.GetPositionAndRotation(out var position, out var rotation);
        if (!math.all(math.isfinite((float3)position)) || !math.all(math.isfinite(((quaternion)rotation).value))) {
            nonFiniteStages[JiggleMemoryBus.NonFiniteStageRead] = 1;
            return;
        }
        jiggleTransform.position = position;
        jiggleTransform.rotation = rotation;
        var scale = transform.localToWorldMatrix.lossyScale;
        //if (scale is { x: 0, y: 0, z: 0 }) {
            //scale = new float3(0.00001f);
        //}
        jiggleTransform.scale = scale;
        simulateInputPoses[index] = jiggleTransform;
    }

}

}