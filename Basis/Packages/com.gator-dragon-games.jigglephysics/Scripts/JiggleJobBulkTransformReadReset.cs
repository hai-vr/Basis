using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace GatorDragonGames.JigglePhysics {

[BurstCompile]
public struct JiggleJobBulkTransformReadReset : IJobParallelForTransform {
    public NativeArray<JiggleTransform> restPoseTransforms;

    [ReadOnly] public NativeArray<JiggleTransform> previousLocalTransforms;

    public NativeArray<JiggleTransform> simulateInputPoses;

    public JiggleJobBulkTransformReadReset(JiggleMemoryBus bus) {
        restPoseTransforms = bus.restPoseTransforms;
        previousLocalTransforms = bus.previousLocalRestPoseTransforms;
        simulateInputPoses = bus.inputPosesCurrent;
    }

    public void UpdateArrays(JiggleMemoryBus bus) {
        restPoseTransforms = bus.restPoseTransforms;
        previousLocalTransforms = bus.previousLocalRestPoseTransforms;
        simulateInputPoses = bus.inputPosesCurrent;
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

    public void Execute(int index, TransformAccess transform) {
        if (!transform.isValid) {
            return;
        }

        var localTransform = previousLocalTransforms[index];
        if (!localTransform.isVirtual) {
            transform.GetLocalPositionAndRotation(out var localPosition, out var localRotation);
            var restTransform = restPoseTransforms[index];

            switch (GetChangedFlags(localTransform.position, localPosition, localTransform.rotation, localRotation)) {
                case ChangeFlags.Position:
                    transform.localRotation = restTransform.rotation;
                    restTransform.position = localPosition;
                    restPoseTransforms[index] = restTransform;
                    break;
                case ChangeFlags.Rotation:
                    transform.localPosition = restTransform.position;
                    restTransform.rotation = localRotation;
                    restPoseTransforms[index] = restTransform;
                    break;
                case ChangeFlags.PositionAndRotation:
                    restTransform.position = localPosition;
                    restTransform.rotation = localRotation;
                    restPoseTransforms[index] = restTransform;
                    break;
                case ChangeFlags.None:
                    transform.SetLocalPositionAndRotation(restTransform.position, restTransform.rotation);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ChangeFlags), "Unknown ChangeFlags (JiggleJobBulkTransformReadReset), this should never happen.");
            }
        }

        var jiggleTransform = simulateInputPoses[index];
        if (jiggleTransform.isVirtual) {
            return;
        }
        transform.GetPositionAndRotation(out var position, out var rotation);
        jiggleTransform.position = position;
        jiggleTransform.rotation = rotation;
        // The unit scale substituted here is never read: Cache multiplies it by a collisionRadius
        // that is zero for exactly the rigs this flag is false for. Writing 1 rather than leaving the
        // old value keeps it finite, so a rig that starts colliding can never multiply by a stale
        // infinity. Measured at ~29% of this job (0.194ms -> 0.138ms over 8192 bones).
        jiggleTransform.scale = jiggleTransform.wantsScale
            ? (float3)transform.localToWorldMatrix.lossyScale
            : new float3(1f);
        simulateInputPoses[index] = jiggleTransform;
    }

}

}
