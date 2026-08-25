using UnityEngine;
namespace Basis.IK
{
    public static class BasisEeriePlanner
    {
        public static void Bind(ref BasisEerieMovement job)
        {
            ref BasisEeriePlan p = ref job.plan;
            p.hasHips = job.handleHips.IsBound;
            p.hasSpine = job.handleSpine.IsBound;
            p.hasChest = job.handleChest.IsBound;
            p.hasUpperChest = job.handleUpperChest.IsBound;
            p.hasNeck = job.handleNeck.IsBound;
            p.hasHead = job.handleHead.IsBound;
            int chainLen = job.chainHeadToSpine.IsCreated ? job.chainHeadToSpine.Length : 0;
            p.hasSpineChain = chainLen >= 3;
            for (int i = 0; i < chainLen; i++)
            {
                if (!job.chainHeadToSpine[i].IsBound)
                {
                    p.hasSpineChain = false;
                }
            }
            p.chestIdx = !p.hasSpineChain ? -1 : job.chainChestIdx != 0 ? job.chainChestIdx : chainLen >= 5 ? chainLen - 3 : -1;
            p.hasChestJoint = p.hasSpineChain && chainLen > 3 && p.chestIdx >= 1 && p.chestIdx < chainLen - 2;
            p.hasSpineRestFrames = p.hasSpineChain && job.chainSpineRestFrames.IsCreated && job.chainSpineRestFrames.Length >= chainLen;
            p.hasSpineBend = p.hasHips && p.hasChest && (p.hasSpine || p.hasUpperChest);
            p.hasBodyRight = job.handleLeftUpperArm.IsBound && job.handleRightUpperArm.IsBound;
            p.torsoFrom = p.hasChest ? job.handleChest : p.hasSpine ? job.handleSpine : job.handleHips;
            p.torsoTo = p.hasNeck ? job.handleNeck : job.handleHead;
            p.hasTorso = p.torsoFrom.IsBound && p.torsoTo.IsBound;
            p.legFrameTo = p.hasChest ? job.handleChest : p.hasSpine ? job.handleSpine : p.hasNeck ? job.handleNeck : job.handleHead;
            p.hasLegFrame = job.handleLeftUpperLeg.IsBound && job.handleRightUpperLeg.IsBound && p.hasHips && p.legFrameTo.IsBound;
            p.chestRef = p.hasUpperChest ? job.handleUpperChest : job.handleChest;
            p.hasChestRef = p.chestRef.IsBound;
            p.hasLeftShoulder = job.handleLeftShoulder.IsBound;
            p.hasRightShoulder = job.handleRightShoulder.IsBound;
            p.hasLeftToe = job.handleLeftToe.IsBound;
            p.hasRightToe = job.handleRightToe.IsBound;
            p.hasChestSpring = job.chestSpring.IsCreated && job.chestSpring.Length >= 1;
            p.hasSwingState = job.swingContinuity.IsCreated && job.swingContinuity.Length >= BasisEerieMovement.swingCount;
            p.hasArmState = job.armState.IsCreated && job.armState.Length >= BasisEerieMovement.swingCount;
            p.hasLegState = job.legState.IsCreated && job.legState.Length >= 2;
            p.hasLegDiagnostics = job.legDiagnostics.IsCreated && job.legDiagnostics.Length >= 2;
            p.boundSlots = 0;
            for (int i = 0; i < BasisEerieMovement.Count; i++)
            {
                if (job.SlotHandle(i).IsBound)
                {
                    p.boundSlots |= 1u << i;
                }
            }
            p.leftArm.has = job.handleLeftUpperArm.IsBound && job.handleLeftLowerArm.IsBound && job.handleLeftHand.IsBound;
            p.leftArm.upperTwist = job.handleLeftUpperArmTwist.IsBound && job.handleLeftUpperArm.IsBound && job.handleLeftLowerArm.IsBound;
            p.leftArm.lowerTwist = job.handleLeftLowerArmTwist.IsBound && job.handleLeftLowerArm.IsBound && job.handleLeftHand.IsBound;
            p.rightArm.has = job.handleRightUpperArm.IsBound && job.handleRightLowerArm.IsBound && job.handleRightHand.IsBound;
            p.rightArm.upperTwist = job.handleRightUpperArmTwist.IsBound && job.handleRightUpperArm.IsBound && job.handleRightLowerArm.IsBound;
            p.rightArm.lowerTwist = job.handleRightLowerArmTwist.IsBound && job.handleRightLowerArm.IsBound && job.handleRightHand.IsBound;
            p.leftLeg.has = job.handleLeftUpperLeg.IsBound && job.handleLeftLowerLeg.IsBound && job.handleLeftFoot.IsBound;
            p.rightLeg.has = job.handleRightUpperLeg.IsBound && job.handleRightLowerLeg.IsBound && job.handleRightFoot.IsBound;
        }
        public static void Frame(ref BasisEerieMovement job, in BasisEerieFrameFacts facts)
        {
            ref BasisEeriePlan p = ref job.plan;
            p.hipsTracked = facts.hipsTracked;
            p.chestTracked = facts.chestTracked;
            p.prone = facts.prone && !facts.hipsTracked;
            p.chestChain = facts.chestTracked && p.hasChest;
            p.headChain = !p.chestChain && p.hasHead;
            p.crouchOffset = !facts.chestTracked && !facts.hipsTracked;
            p.gaitPelvis = !facts.hipsTracked && facts.footSimReady && Mathf.Min(facts.leftFootSim, facts.rightFootSim) > 0.001f;
            p.leftShoulderTracked = facts.leftShoulderTracked;
            p.rightShoulderTracked = facts.rightShoulderTracked;
            p.leftToeTracked = facts.leftToeTracked;
            p.rightToeTracked = facts.rightToeTracked;
            Arm(ref p.leftArm, facts.leftHandWeight, facts.leftElbowTracked);
            Arm(ref p.rightArm, facts.rightHandWeight, facts.rightElbowTracked);
            Leg(ref p.leftLeg, p.hasLegFrame, facts.leftLegTracked, facts.leftKneeTracked, facts.footSimReady, facts.leftFootSim, facts.leftSimFootRotation, facts.leftKneeAssist);
            Leg(ref p.rightLeg, p.hasLegFrame, facts.rightLegTracked, facts.rightKneeTracked, facts.footSimReady, facts.rightFootSim, facts.rightSimFootRotation, facts.rightKneeAssist);
            job.offsetRotationHips = Unit(job.offsetRotationHips);
            job.offsetRotationHead = Unit(job.offsetRotationHead);
            job.offsetRotationChest = Unit(job.offsetRotationChest);
            job.offsetRotationLeftFoot = Unit(job.offsetRotationLeftFoot);
            job.offsetRotationRightFoot = Unit(job.offsetRotationRightFoot);
            job.offsetRotationLeftToe = Unit(job.offsetRotationLeftToe);
            job.offsetRotationRightToe = Unit(job.offsetRotationRightToe);
            job.offsetRotationLeftShoulder = Unit(job.offsetRotationLeftShoulder);
            job.offsetRotationRightShoulder = Unit(job.offsetRotationRightShoulder);
            job.offsetRotationLeftHand = Unit(job.offsetRotationLeftHand);
            job.offsetRotationRightHand = Unit(job.offsetRotationRightHand);
        }
        public static bool KneeAssistWanted(in BasisEerieFrameFacts facts, bool isLeft) => isLeft ? facts.leftFootTracked && !facts.leftKneeTracked : facts.rightFootTracked && !facts.rightKneeTracked;
        static void Arm(ref BasisEerieArmPlan arm, float weight, bool elbowTracked)
        {
            arm.weight = weight;
            arm.solve = weight > 0f && arm.has;
            arm.trackerHint = elbowTracked;
        }
        static void Leg(ref BasisEerieLegPlan leg, bool hasFrame, bool tracked, bool kneeTracked, bool simReady, float sim, bool simRotation, float assist)
        {
            bool simActive = simReady && sim > 0.001f;
            leg.tracked = tracked;
            leg.kneeTracked = kneeTracked;
            leg.target = tracked ? BasisEerieSource.Tracker : simActive ? BasisEerieSource.Sim : BasisEerieSource.None;
            leg.weight = tracked ? 1f : simActive ? sim : 0f;
            leg.solve = leg.weight > 0f && leg.has;
            leg.preserveTip = leg.target == BasisEerieSource.Sim && !simRotation;
            leg.hint = kneeTracked ? BasisEerieSource.Tracker : simActive ? BasisEerieSource.Sim : assist > 0f ? BasisEerieSource.Assist : BasisEerieSource.None;
            leg.hintWeight = kneeTracked ? 1f : simActive ? sim : assist > 0f ? assist : 0f;
            leg.modelHint = hasFrame && (leg.hint == BasisEerieSource.None || leg.hint == BasisEerieSource.Sim);
        }
        static Quaternion Unit(Quaternion q) => q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > BasisEerieMovement.sqrEpsilon ? q : Quaternion.identity;
    }
}
