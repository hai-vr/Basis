using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace GatorDragonGames.JigglePhysics {

[BurstCompile]
public struct JiggleJobBulkTransformReadReset : IJobParallelForTransform {
    public NativeArray<JiggleTransform> restPoseTransforms;

    [ReadOnly] public NativeArray<JiggleTransform> previousLocalTransforms;

    public NativeArray<JiggleTransform> simulateInputPoses;
    [NativeDisableContainerSafetyRestriction] public NativeArray<int> nonFiniteStages;

    public JiggleJobBulkTransformReadReset(JiggleMemoryBus bus) {
        restPoseTransforms = bus.restPoseTransforms;
        previousLocalTransforms = bus.previousLocalRestPoseTransforms;
        simulateInputPoses = bus.inputPosesCurrent;
        nonFiniteStages = bus.nonFiniteStages;
    }

    public void UpdateArrays(JiggleMemoryBus bus) {
        restPoseTransforms = bus.restPoseTransforms;
        previousLocalTransforms = bus.previousLocalRestPoseTransforms;
        simulateInputPoses = bus.inputPosesCurrent;
        nonFiniteStages = bus.nonFiniteStages;
    }

    [Flags]
    private enum ChangeFlags {
        None = 0,
        Position = 1,
        Rotation = 2,
        PositionAndRotation = 3,
    }

    private static ChangeFlags GetChangedFlags(float3 oldPosition, Vector3 newPosition, quaternion oldRotation, Quaternion newRotation) {
        ChangeFlags changed = ChangeFlags.None;
        changed |= (newPosition == (Vector3)oldPosition ? ChangeFlags.None : ChangeFlags.Position);
        changed |= (newRotation == (Quaternion)oldRotation ? ChangeFlags.None : ChangeFlags.Rotation);
        return changed;
    }

    private static bool GetIsFinite(float3 position, quaternion rotation) {
        return math.all(math.isfinite(position)) && math.all(math.isfinite(rotation.value));
    }

    public void Execute(int index, TransformAccess transform) {
        if (!transform.isValid) {
            return;
        }

        var localTransform = previousLocalTransforms[index];
        if (!localTransform.isVirtual) {
            transform.GetLocalPositionAndRotation(out var localPosition, out var localRotation);
            var restTransform = restPoseTransforms[index];
            var restIsFinite = GetIsFinite(restTransform.position, restTransform.rotation);

            if (!GetIsFinite(localPosition, localRotation)) {
                nonFiniteStages[JiggleMemoryBus.NonFiniteStageResetLocalPose] = 1;
                if (restIsFinite) {
                    transform.SetLocalPositionAndRotation(restTransform.position, restTransform.rotation);
                }
            } else {
                switch (GetChangedFlags(localTransform.position, localPosition, localTransform.rotation, localRotation)) {
                    case ChangeFlags.Position:
                        if (restIsFinite) {
                            transform.localRotation = restTransform.rotation;
                        }
                        restTransform.position = localPosition;
                        restPoseTransforms[index] = restTransform;
                        break;
                    case ChangeFlags.Rotation:
                        if (restIsFinite) {
                            transform.localPosition = restTransform.position;
                        }
                        restTransform.rotation = localRotation;
                        restPoseTransforms[index] = restTransform;
                        break;
                    case ChangeFlags.PositionAndRotation:
                        restTransform.position = localPosition;
                        restTransform.rotation = localRotation;
                        restPoseTransforms[index] = restTransform;
                        break;
                    case ChangeFlags.None:
                        if (restIsFinite) {
                            transform.SetLocalPositionAndRotation(restTransform.position, restTransform.rotation);
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(ChangeFlags), "Unknown ChangeFlags (JiggleJobBulkTransformReadReset), this should never happen.");
                }
            }
        }

        var jiggleTransform = simulateInputPoses[index];
        if (jiggleTransform.isVirtual) {
            return;
        }
        transform.GetPositionAndRotation(out var position, out var rotation);
        if (!GetIsFinite(position, rotation)) {
            nonFiniteStages[JiggleMemoryBus.NonFiniteStageMergedRead] = 1;
            return;
        }
        jiggleTransform.position = position;
        jiggleTransform.rotation = rotation;
        jiggleTransform.scale = transform.localToWorldMatrix.lossyScale;
        simulateInputPoses[index] = jiggleTransform;
    }

}

}
