using NUnit.Framework;
using UnityEngine.Animations.Rigging;

namespace Basis.Tests.IK
{
    /// <summary>
    /// "Chest twists/hunches when I look down" -- the CCD half of the regression, the companion to
    /// <see cref="BasisSpineLookDownHunchTests"/> (the pre-bend half). Zeroing DistributeSpineBend's lean
    /// weights stops the PRE-bend from hunching, but the head-reach CCD (SolveSequentialSpineIK) aims the
    /// head BONE at the HMD target -- which swings forward of the neck on a look-down -- so a hips-anchored
    /// reach pitched the whole hips->head chain, the tracked chest included, forward and re-introduced the
    /// hunch/twist on a path with no suppression. <see cref="BasisSpineReachCore.LastJoint"/> anchors the
    /// reach at a tracked chest; these gate that the chest (and the spine/hips below it) is never in the
    /// rotated range. The chain is tip->root [head, neck, (upperChest), chest, spine, hips], so the chest
    /// sits at chainLen-3.
    /// </summary>
    public class BasisSpineReachCoreTests
    {
        const int FirstJoint = 1;

        [Test]
        public void NoChestTracker_ReachRunsDownToTheSpine()
        {
            // Without a chest tracker the head estimates the whole torso, so the reach reaches the spine
            // (chainLen-2): index 4 on a 6-bone chain, 3 on a 5-bone chain.
            Assert.AreEqual(4, BasisSpineReachCore.LastJoint(6, FirstJoint, false));
            Assert.AreEqual(3, BasisSpineReachCore.LastJoint(5, FirstJoint, false));
        }

        [Test]
        public void ChestTracker_ReachStopsStrictlyAboveTheChest()
        {
            // The chest (chainLen-3) and everything below it must be outside the rotated range, so the head
            // reach can never rotate the tracked chest or the spine to chase a look-down.
            Assert.AreEqual(2, BasisSpineReachCore.LastJoint(6, FirstJoint, true)); // upperChest is the lowest
            Assert.Less(BasisSpineReachCore.LastJoint(6, FirstJoint, true), 6 - 3);
            Assert.Less(BasisSpineReachCore.LastJoint(5, FirstJoint, true), 5 - 3);
        }

        [Test]
        public void ChestTracker_NeverDropsBelowTheFirstJoint()
        {
            // Short/degenerate chains must still yield an in-range loop bound (>= firstJoint).
            Assert.GreaterOrEqual(BasisSpineReachCore.LastJoint(5, FirstJoint, true), FirstJoint);
            Assert.GreaterOrEqual(BasisSpineReachCore.LastJoint(4, FirstJoint, true), FirstJoint);
            Assert.GreaterOrEqual(BasisSpineReachCore.LastJoint(3, FirstJoint, true), FirstJoint);
        }
    }
}
