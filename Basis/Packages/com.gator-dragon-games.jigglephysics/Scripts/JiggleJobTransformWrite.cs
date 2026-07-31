using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace GatorDragonGames.JigglePhysics {

[BurstCompile]
public struct JiggleJobTransformWrite : IJobParallelForTransform {
    public NativeArray<JiggleTransform> previousLocalPoses;
    [ReadOnly] public NativeArray<JiggleTransform> inputInterpolatedPoses;
    [NativeDisableContainerSafetyRestriction] public NativeArray<int> nonFiniteStages;

    public JiggleJobTransformWrite(JiggleMemoryBus bus) {
        previousLocalPoses = bus.previousLocalRestPoseTransforms;
        inputInterpolatedPoses = bus.interpolationOutputPoses;
        nonFiniteStages = bus.nonFiniteStages;
    }

    public void UpdateArrays(JiggleMemoryBus bus) {
        previousLocalPoses = bus.previousLocalRestPoseTransforms;
        inputInterpolatedPoses = bus.interpolationOutputPoses;
        nonFiniteStages = bus.nonFiniteStages;
    }

    public void Execute(int index, TransformAccess transform) {
        if (!transform.isValid) {
            return;
        }

        var pose = inputInterpolatedPoses[index];
        if (pose.isVirtual) {
            return;
        }

        if (!math.all(math.isfinite(pose.position)) || !math.all(math.isfinite(pose.rotation.value))) {
            nonFiniteStages[JiggleMemoryBus.NonFiniteStageTransformWrite] = 1;
            return;
        }

        transform.SetPositionAndRotation(pose.position, pose.rotation);
        transform.GetLocalPositionAndRotation(out var localPosition, out var localRotation);

        var previousLocalPose = previousLocalPoses[index];
        previousLocalPose.position = localPosition;
        previousLocalPose.rotation = localRotation;
        previousLocalPoses[index] = previousLocalPose;
    }
}

}