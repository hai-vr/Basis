using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Jobs;
[BurstCompile]
public struct BasisEyeApplyJob : IJobParallelForTransform
{
    [ReadOnly] public NativeArray<BasisEyeState> state;
    public void Execute(int index, TransformAccess transform)
    {
        BasisEyeState s = state[0];
        quaternion baseRot = transform.localRotation;
        quaternion offset = index == 0 ? s.leftOffset : s.rightOffset;
        transform.localRotation = math.mul(baseRot, offset);
    }
}
