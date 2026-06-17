namespace UnityEngine.Animations.Rigging
{
    // Stream-free joint-range rule for the head-reach CCD (BasisFullIKConstraintJob.SolveSequentialSpineIK).
    // The chain runs tip->root [head, neck, (upperChest), chest, spine, hips], so the chest is at chainLen-3
    // and the hips anchor at chainLen-1; the CCD rotates [firstJoint .. LastJoint] to slide the head bone
    // onto its target. With NO chest tracker the head estimates the whole torso, so the reach runs down to
    // the spine (chainLen-2). With a chest tracker the chest is authoritative -- the head reach must not be
    // allowed to rotate it (a look-down swings the HMD forward of the neck, so a hips-anchored reach pitches
    // the whole chain, chest included, forward and hunches/twists the tracked chest). Anchor the reach at the
    // chest instead (chainLen-4 = the joint just above it) so only the chest->head joints bend. Pure int math
    // kept out of the job so it has a unit gate even though the CCD itself runs over AnimationStream handles.
    public static class BasisSpineReachCore
    {
        public static int LastJoint(int chainLen, int firstJoint, bool chestAnchored)
        {
            int last = chestAnchored ? chainLen - 4 : chainLen - 2;
            return last < firstJoint ? firstJoint : last;
        }
    }
}
