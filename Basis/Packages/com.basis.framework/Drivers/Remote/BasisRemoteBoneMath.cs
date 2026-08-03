using Unity.Mathematics;

/// <summary>
/// Pure remote-avatar bone math shared by BasisRemoteBoneJob and the editor Remote Bone sweep, so
/// the sweep exercises the real forward-kinematics chain rather than a copy. The head chain (neck →
/// chest → spine, plus derived eye/mouth) is FK off the networked head pose and the scaled T-pose
/// offsets — each child is parent + headRotation * scaledOffset.
/// </summary>
internal static class BasisRemoteBoneMath
{
    /// <summary>
    /// The rotation that carries an avatar-root-space direction (as authored at T-pose) into world
    /// space, given the head's current world rotation.
    ///
    /// Every offset in the head chain is authored root-relative — <c>AddRemotePlayer</c> builds them
    /// with <c>root.worldToLocalMatrix</c>, and <c>tposeHeadFromRoot</c> is the head's T-pose rotation
    /// in that same root frame (<c>BasisTransformMapping.TposeFromRoot</c>). Mapping one to the
    /// other is therefore <c>headWorld * inverse(tposeHeadFromRoot)</c>: at T-pose the head reads
    /// <c>rootWorld * tposeHeadFromRoot</c>, so the product collapses back to <c>rootWorld</c>, which
    /// is exactly the frame the offsets were measured in.
    ///
    /// The result doubles as the world rotation of the eye/mouth anchors. Those are authored as bare
    /// points with no rotation of their own, so the only meaningful orientation to hand them is the
    /// avatar's own frame — whose forward is the face direction the gaze selector dots against, and
    /// the direction the voice AudioSource is spatialised along. Reading the head bone's rotation
    /// straight off the transform would give neither, because a head bone's local forward is only the
    /// face direction on rigs whose head happens to be bound axis-aligned.
    ///
    /// Conjugate rather than inverse: both inputs are unit quaternions.
    /// </summary>
    internal static quaternion HeadWorldFrame(in quaternion headWorldRotation, in quaternion tposeHeadFromRoot)
    {
        return math.mul(headWorldRotation, math.conjugate(tposeHeadFromRoot));
    }

    /// <summary>
    /// The head-relative offset a face anchor is stored as at registration.
    ///
    /// Both operands must be in the avatar-root frame at T-pose, scale-normalised to model metres:
    /// <paramref name="authoredRootLocal"/> is the authored eye/mouth point unpacked from
    /// <c>BasisAvatar.AvatarEyePosition</c>, and <paramref name="tposeHeadFromRoot"/> is the head's
    /// position out of <c>BasisTransformMapping.TposeFromRoot</c>, which is built through the root's
    /// worldToLocalMatrix and so divides the root's scale back out. Mixing frames here is what made
    /// the anchors drift: a world-space head position produces a world-axes delta, and
    /// <see cref="HeadWorldFrame"/> then rotates it a second time.
    /// </summary>
    internal static float3 HeadAnchorOffset(in float3 authoredRootLocal, in float3 tposeHeadFromRoot)
    {
        return authoredRootLocal - tposeHeadFromRoot;
    }

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
