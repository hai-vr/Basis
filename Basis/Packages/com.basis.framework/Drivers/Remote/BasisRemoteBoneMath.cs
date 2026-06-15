using Unity.Mathematics;

/// <summary>
/// Pure remote-avatar bone math shared by BasisRemoteBoneJob and the editor Remote Bone sweep, so
/// the sweep exercises the real forward-kinematics chain rather than a copy. The head chain (neck →
/// chest → spine, plus derived eye/mouth) is FK off the networked head pose and the scaled T-pose
/// offsets — each child is parent + headRotation * scaledOffset.
/// </summary>
internal static class BasisRemoteBoneMath
{
    internal static void ComposeHeadChain(
        in float3 headP, in quaternion headR,
        in float3 neckOffset, in float3 chestOffset, in float3 spineOffset, in float3 eyeOffset, in float3 mouthOffset,
        out float3 neckP, out float3 chestP, out float3 spineP, out float3 eyeP, out float3 mouthP)
    {
        neckP = headP + math.mul(headR, neckOffset);
        chestP = neckP + math.mul(headR, chestOffset);
        spineP = chestP + math.mul(headR, spineOffset);
        eyeP = headP + math.mul(headR, eyeOffset);
        mouthP = headP + math.mul(headR, mouthOffset);
    }
}
