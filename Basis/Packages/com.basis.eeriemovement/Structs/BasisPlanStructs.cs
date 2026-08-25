namespace Basis.IK
{
    public struct BasisEerieFrameFacts
    {
        public bool hipsTracked, chestTracked, prone, leftLegTracked, rightLegTracked, leftFootTracked, rightFootTracked, leftKneeTracked, rightKneeTracked;
        public bool leftElbowTracked, rightElbowTracked, leftShoulderTracked, rightShoulderTracked, leftToeTracked, rightToeTracked, footSimReady;
        public bool leftSimFootRotation, rightSimFootRotation;
        public float leftHandWeight, rightHandWeight, leftFootSim, rightFootSim, leftKneeAssist, rightKneeAssist;
    }
    public struct BasisEerieArmPlan
    {
        public bool has, upperTwist, lowerTwist, solve, trackerHint;
        public float weight;
    }
    public struct BasisEerieLegPlan
    {
        public bool has, solve, tracked, kneeTracked, preserveTip, modelHint;
        public float weight, hintWeight;
        public BasisEerieSource target, hint;
    }
    public struct BasisEeriePlan
    {
        public bool hasHips, hasSpine, hasChest, hasUpperChest, hasNeck, hasHead, hasSpineChain, hasChestJoint, hasSpineRestFrames, hasSpineBend;
        public bool hasBodyRight, hasTorso, hasLegFrame, hasChestRef, hasLeftShoulder, hasRightShoulder, hasLeftToe, hasRightToe;
        public bool hasChestSpring, hasSwingState, hasArmState, hasLegState, hasLegDiagnostics;
        public int chestIdx;
        public uint boundSlots;
        public BasisBoneHandle torsoFrom, torsoTo, legFrameTo, chestRef;
        public bool prone, hipsTracked, chestTracked, chestChain, headChain, crouchOffset, gaitPelvis;
        public bool leftShoulderTracked, rightShoulderTracked, leftToeTracked, rightToeTracked;
        public BasisEerieArmPlan leftArm, rightArm;
        public BasisEerieLegPlan leftLeg, rightLeg;
    }
}
