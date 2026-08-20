using System.Runtime.CompilerServices;
using Basis.Scripts.Common;
using Unity.Collections;
using UnityEngine;
namespace Basis.IK
{
    public partial struct BasisEerieMovement
    {
        void SolveShoulderPass()
        {
            if (shoulderSolveEnabled)
            {
                SolveShoulder(true);
                SolveShoulder(false);
            }
            else
            {
                ApplyRotation(enabledLeftShoulder, handleLeftShoulder, targetRotationLeftShoulder, offsetRotationLeftShoulder);
                ApplyRotation(enabledRightShoulder, handleRightShoulder, targetRotationRightShoulder, offsetRotationRightShoulder);
            }
            if (anatShoulderSlide)
            {
                ApplyShoulderSlide();
            }
        }
        void SolveArmPass()
        {
            SolveHand(true);
            SolveHand(false);

            if (enabledLeftHand > 0f)
            {
                ApplySwingContinuity(swingLeftElbow, handleLeftUpperArm, handleLeftLowerArm, handleLeftHand, targetPositionLeftHand);
            }

            if (enabledRightHand > 0f)
            {
                ApplySwingContinuity(swingRightElbow, handleRightUpperArm, handleRightLowerArm, handleRightHand, targetPositionRightHand);
            }

            SolveArmTwist(handleLeftLowerArm, handleLeftHand, handleLeftLowerArmTwist, lowerArmTwistFraction, tposeLeftLowerArmChildBind, tposeLeftLowerArmTwistBind);
            SolveArmTwist(handleRightLowerArm, handleRightHand, handleRightLowerArmTwist, lowerArmTwistFraction, tposeRightLowerArmChildBind, tposeRightLowerArmTwistBind);
            SolveArmTwist(handleLeftUpperArm, handleLeftLowerArm, handleLeftUpperArmTwist, upperArmTwistFraction, tposeLeftUpperArmChildBind, tposeLeftUpperArmTwistBind);
            SolveArmTwist(handleRightUpperArm, handleRightLowerArm, handleRightUpperArmTwist, upperArmTwistFraction, tposeRightUpperArmChildBind, tposeRightUpperArmTwistBind);
        }
        void ApplyShoulderSlide()
        {
            if (!poseStream.IsValid(handleHips) || !poseStream.IsValid(handleChest))
            {
                return;
            }

            Quaternion hipsRot = poseStream.GetRotation(handleHips) * Quaternion.Inverse(offsetRotationHips);
            Quaternion chestRot = poseStream.GetRotation(handleChest);
            Quaternion chestLocal = Quaternion.Inverse(hipsRot) * chestRot;
            float chestYaw = BasisTwistSolveCore.SignedTwistAngleDeg(chestLocal, Vector3.up);
            float excess = Mathf.Abs(chestYaw) - shoulderSlideStartDeg;
            if (excess <= 0f)
                return;

            float counterYaw = -Mathf.Sign(chestYaw) * Mathf.Min(excess * shoulderSlideFraction, shoulderSlideMaxDeg);
            ApplyShoulderYaw(handleLeftShoulder, hipsRot, counterYaw);
            ApplyShoulderYaw(handleRightShoulder, hipsRot, counterYaw);
        }
        void ApplyShoulderYaw(BasisBoneHandle shoulder, Quaternion hipsRot, float yawDeg)
        {
            if (!poseStream.IsValid(shoulder))
                return;
            Quaternion delta = hipsRot * Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.Inverse(hipsRot);
            poseStream.SetRotation(shoulder, delta * poseStream.GetRotation(shoulder));
        }
        void ApplyArmSwingChestFollow()
        {
            float factor = chestArmSwingFactor;
            if (factor <= 0f)
            {
                return;
            }

            if (!poseStream.IsValid(handleHips) || !poseStream.IsValid(handleChest))
            {
                return;
            }

            bool leftEnabled = enabledLeftHand > 0f, rightEnabled = enabledRightHand > 0f;
            if (!leftEnabled && !rightEnabled)
            {
                return;
            }

            Vector3 leftPos = leftEnabled ? targetPositionLeftHand : Vector3.zero;
            Vector3 rightPos = rightEnabled ? targetPositionRightHand : Vector3.zero;
            Vector3 handMid = leftEnabled && rightEnabled ? (leftPos + rightPos) * 0.5f : leftEnabled ? leftPos : rightPos;
            Vector3 hipsPos = poseStream.GetPosition(handleHips);
            Quaternion hipsAnat = poseStream.GetRotation(handleHips) * Quaternion.Inverse(offsetRotationHips);
            Quaternion invHipsAnat = Quaternion.Inverse(hipsAnat);
            Vector3 localMid = invHipsAnat * (handMid - hipsPos);
            float forwardDist = Mathf.Max(0.1f, Mathf.Abs(localMid.z));
            float yawDeg = Mathf.Atan2(localMid.x, forwardDist) * Mathf.Rad2Deg * factor;
            Vector3 localMidChest = invHipsAnat * (handMid - poseStream.GetPosition(handleChest));
            float pitchDeg = Mathf.Atan2(-localMidChest.y, forwardDist) * Mathf.Rad2Deg * factor;
            float maxDeg = chestArmSwingMaxDeg;
            if (maxDeg > 0f)
            {
                yawDeg = Mathf.Clamp(yawDeg, -maxDeg, maxDeg);
                pitchDeg = Mathf.Clamp(pitchDeg, -maxDeg, maxDeg);
            }

            Quaternion local = Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.AngleAxis(pitchDeg, Vector3.right);
            Quaternion deltaWorld = hipsAnat * local * invHipsAnat;

            if (poseStream.IsValid(handleUpperChest))
            {
                Quaternion chestPart = Quaternion.Slerp(Quaternion.identity, deltaWorld, chestFollowChestShare);
                Quaternion upperPart = Quaternion.Slerp(Quaternion.identity, deltaWorld, 1f - chestFollowChestShare);
                poseStream.SetRotation(handleChest, chestPart * poseStream.GetRotation(handleChest));
                poseStream.SetRotation(handleUpperChest, upperPart * poseStream.GetRotation(handleUpperChest));
            }
            else
            {
                poseStream.SetRotation(handleChest, deltaWorld * poseStream.GetRotation(handleChest));
            }
        }
        void SolveArmTwist(BasisBoneHandle parent, BasisBoneHandle child, BasisBoneHandle twist, float fraction, Quaternion childBind, Quaternion twistBind)
        {
            if (!poseStream.IsValid(twist) || fraction <= 0f)
                return;
            if (!poseStream.IsValid(parent) || !poseStream.IsValid(child))
                return;

            Vector3 parentPos = poseStream.GetPosition(parent), childPos = poseStream.GetPosition(child);
            float positionFraction = BasisTwistSolveCore.SegmentPositionFraction(parentPos, childPos, poseStream.GetPosition(twist));

            if (BasisTwistSolveCore.Solve(poseStream.GetRotation(parent), poseStream.GetRotation(child), childPos - parentPos, positionFraction * fraction, childBind, twistBind, out Quaternion twistWorld, out _, out _))
            {
                poseStream.SetRotation(twist, twistWorld);
            }
        }
        public void SolveShoulder(bool isLeft)
        {
            BasisBoneHandle shoulderHandle = isLeft ? handleLeftShoulder : handleRightShoulder;
            if (!poseStream.IsValid(shoulderHandle))
            {
                return;
            }

            BasisShoulderSolveInput input;
            input.ShoulderPos = poseStream.GetPosition(shoulderHandle);
            input.HandTargetPos = isLeft ? targetPositionLeftHand : targetPositionRightHand;
            input.ElbowPos = isLeft ? hintPositionLeftHand : hintPositionRightHand;
            input.HasElbow = isLeft ? hintWeightLeftHand : hintWeightRightHand;
            input.HasShoulderTracker = isLeft ? enabledLeftShoulder : enabledRightShoulder;

            input.ChestRot = poseStream.IsValid(handleUpperChest) ? poseStream.GetRotation(handleUpperChest) : poseStream.IsValid(handleChest) ? poseStream.GetRotation(handleChest) : Quaternion.identity;
            input.TposeChestRot = tposeChestRot;
            input.TposeShoulderRot = isLeft ? tposeLeftShoulderRot : tposeRightShoulderRot;
            input.TposeArmDirWorld = isLeft ? tposeLeftShoulderLocalDir : tposeRightShoulderLocalDir;
            input.TposeArmLength = isLeft ? tposeShoulderToHandLeft : tposeShoulderToHandRight;
            input.TposeClavicleLength = isLeft ? tposeClavicleLenLeft : tposeClavicleLenRight;
            input.TposeElbowLength = isLeft ? tposeShoulderToElbowLeft : tposeShoulderToElbowRight;
            input.ShrugEnabled = shoulderShrugEnabled;
            input.ElevationFactor = shoulderElevationFactor;
            input.ProtractionFactor = shoulderProtractionFactor;
            input.CoupleRatio = shoulderCoupleRatio;
            input.MaxShoulderDeg = shoulderMaxDeg;
            input.TrackerFinal = isLeft ? targetRotationLeftShoulder * offsetRotationLeftShoulder : targetRotationRightShoulder * offsetRotationRightShoulder;
            input.IsLeft = isLeft;

            BasisShoulderSolveCore.Solve(input, out BasisShoulderSolveResult result);
            if (result.Apply)
            {
                poseStream.SetRotation(shoulderHandle, result.ShoulderRotation);
            }
        }
        BasisSwivelFrame BuildArmFrame()
        {
            if (!poseStream.IsValid(handleLeftUpperArm) || !poseStream.IsValid(handleRightUpperArm))
            {
                return default;
            }

            BasisBoneHandle upFrom = poseStream.IsValid(handleChest) ? handleChest : poseStream.IsValid(handleSpine) ? handleSpine : handleHips;
            BasisBoneHandle upTo = poseStream.IsValid(handleNeck) ? handleNeck : handleHead;
            if (!poseStream.IsValid(upFrom) || !poseStream.IsValid(upTo))
            {
                return default;
            }

            return BasisSwivelHintCore.BuildFrame( poseStream.GetPosition(handleLeftUpperArm), poseStream.GetPosition(handleRightUpperArm), poseStream.GetPosition(upFrom), poseStream.GetPosition(upTo));
        }
        public void SwingElbowAroundAC(BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 desiredB)
        {
            Vector3 A = poseStream.GetPosition(root), C = poseStream.GetPosition(tip), B = poseStream.GetPosition(mid);
            Vector3 AC = C - A;
            float acSqr = Vector3.Dot(AC, AC);
            if (acSqr <= sqrEpsilon) return;

            Vector3 n = AC / Mathf.Sqrt(acSqr);
            Vector3 v1 = B - A; v1 -= n * Vector3.Dot(v1, n);
            Vector3 v2 = desiredB - A; v2 -= n * Vector3.Dot(v2, n);

            float v1Sqr = Vector3.Dot(v1, v1), v2Sqr = Vector3.Dot(v2, v2);
            if (v1Sqr <= sqrEpsilon || v2Sqr <= sqrEpsilon) return;

            v1 /= Mathf.Sqrt(v1Sqr);
            v2 /= Mathf.Sqrt(v2Sqr);

            float dot = Mathf.Clamp(Vector3.Dot(v1, v2), -1f, 1f), ang = Mathf.Acos(dot);
            Vector3 cross = Vector3.Cross(v1, v2);
            float dir = Mathf.Sign(Vector3.Dot(cross, n));
            Quaternion swing = Quaternion.AngleAxis(ang * dir * Mathf.Rad2Deg, n);

            poseStream.SetRotation(root, swing * poseStream.GetRotation(root));
        }
        void ApplySwingContinuity(int slot, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPos)
        {
            if (!swingContinuity.IsCreated || (uint)slot >= (uint)swingContinuity.Length || !poseStream.IsValid(root) || !poseStream.IsValid(mid) || !poseStream.IsValid(tip))
            {
                return;
            }

            Vector3 a = poseStream.GetPosition(root), c = poseStream.GetPosition(tip), b = poseStream.GetPosition(mid);

            ref BasisSwingContinuityState state = ref Ref(swingContinuity, slot);
            int collided = armState.IsCreated && (uint)slot < (uint)armState.Length ? armState[slot].Collided : 0;

            if (!BasisSwingContinuityCore.Step(ref state, a, b, c, targetPos, collided, swingSmoothRateDeg, poseStream.deltaTime, out bool applySwing, out Vector3 newDir))
            {
                return;
            }

            if (applySwing)
            {
                Quaternion preservedHandRot = poseStream.GetRotation(tip);
                SwingElbowAroundAC(root, mid, tip, a + newDir);
                poseStream.SetPosition(tip, c);
                poseStream.SetRotation(tip, preservedHandRot);
            }
            state.Seeded = true;
        }
        public void SolveHand(bool isLeft)
        {
            float weight = isLeft ? enabledLeftHand : enabledRightHand;
            if (!(weight > 0f))
            {
                return;
            }
            BasisBoneHandle root = isLeft ? handleLeftUpperArm : handleRightUpperArm;
            BasisBoneHandle mid = isLeft ? handleLeftLowerArm : handleRightLowerArm;
            BasisBoneHandle tip = isLeft ? handleLeftHand : handleRightHand;
            if (!(poseStream.IsValid(root) && poseStream.IsValid(mid) && poseStream.IsValid(tip)))
            {
                return;
            }
            Vector3 tgtPos = isLeft ? targetPositionLeftHand : targetPositionRightHand;
            Quaternion tgtRot = isLeft ? targetRotationLeftHand : targetRotationRightHand;
            Vector3 hintPos = isLeft ? hintPositionLeftHand : hintPositionRightHand;
            Quaternion hintRot = isLeft ? hintRotationLeftHand : hintRotationRightHand;
            bool hasHint = isLeft ? hintWeightLeftHand : hintWeightRightHand;
            Quaternion targetOffset = isLeft ? offsetRotationLeftHand : offsetRotationRightHand;
            int swingSlot = isLeft ? swingLeftElbow : swingRightElbow;
            bool slotOk = armState.IsCreated && (uint)swingSlot < (uint)armState.Length;
            Quaternion origRootRot = poseStream.GetRotation(root), origMidRot = poseStream.GetRotation(mid);
            Quaternion origTipRot = poseStream.GetRotation(tip);
            Vector3 bodyRight = (poseStream.IsValid(handleLeftUpperArm) && poseStream.IsValid(handleRightUpperArm)) ? poseStream.GetPosition(handleRightUpperArm) - poseStream.GetPosition(handleLeftUpperArm) : Vector3.zero;
            bool usedModel = false;

            if (!hasHint)
            {
                BasisSwivelFrame frame = BuildArmFrame();
                Vector3 shoulderPos = poseStream.GetPosition(root);
                float upperLen = (poseStream.GetPosition(mid) - shoulderPos).magnitude;
                float lowerLen = (poseStream.GetPosition(tip) - poseStream.GetPosition(mid)).magnitude;
                float armLen = upperLen + lowerLen;
                if (BasisSwivelHintCore.ArmHint(frame, shoulderPos, tgtPos, armLen, isLeft, out Vector3 modelHint, out float poleConditioning))
                {
                    Vector3 curAxisV = tgtPos - shoulderPos, rawBendV = modelHint - shoulderPos;
                    float axLen = curAxisV.magnitude, rbLen = rawBendV.magnitude;
                    if (axLen > 1e-5f && rbLen > 1e-5f && slotOk)
                    {
                        ref BasisArmSlotState arm = ref Ref(armState, swingSlot);
                        Vector3 curAxis = curAxisV / axLen, rawBend = rawBendV / rbLen;
                        bool seeded = arm.HintSeeded;
                        float curReach = axLen / armLen;
                        float armDt = Mathf.Min(poseStream.deltaTime, BasisElbowSwingCapCore.MaxSlewBudgetDt);
                        Vector3 cappedBend = seeded ? (Vector3)BasisElbowSwingCapCore.Apply(arm.HintBend, arm.HintAxis, curAxis, rawBend, BasisElbowSwingCapCore.MaxGain, curReach - arm.HintReach, poleConditioning, BasisElbowSwingCapCore.SlewCapRad(poseStream.deltaTime)) : rawBend;
                        arm.HintBend = cappedBend;
                        arm.HintAxis = curAxis;
                        arm.HintReach = curReach;

                        Quaternion bodyRot = frame.Valid ? Quaternion.LookRotation(frame.Forward, frame.Up) : poseStream.IsValid(handleHips) ? poseStream.GetRotation(handleHips) : Quaternion.identity;
                        Vector3 outBend = cappedBend;
                        if (elbowDragEnabled && seeded)
                        {
                            Quaternion bodyDelta = bodyRot * Quaternion.Inverse(arm.HintBodyRot);
                            outBend = (Vector3)BasisElbowDragCore.Apply(arm.HintDrag, bodyDelta, curAxis, cappedBend, BasisElbowDragCore.Alpha(elbowDragHz, armDt));
                        }
                        arm.HintDrag = outBend;
                        arm.HintBodyRot = bodyRot;
                        arm.HintSeeded = true;
                        modelHint = shoulderPos + 0.5f * armLen * outBend;
                    }

                    hintPos = modelHint;
                    hasHint = true;
                    usedModel = true;
                }
            }
            if (!usedModel && slotOk)
            {
                Ref(armState, swingSlot).HintSeeded = false;
            }

            bool hintIsTracker = hasHint && !usedModel;
            BasisArmSolveInput input = default;
            poseStream.GetPositionAndRotation(root, out Vector3 rootPos, out Quaternion rootRot);
            poseStream.GetPositionAndRotation(mid, out Vector3 elbowPos, out Quaternion elbowRot);
            poseStream.GetPositionAndRotation(tip, out Vector3 handPos, out Quaternion handRot);
            input.Shoulder = rootPos;
            input.Elbow = elbowPos;
            input.Hand = handPos;
            input.RootRotation = rootRot;
            input.MidRotation = elbowRot;
            input.TargetPosition = tgtPos;
            input.TargetRotation = tgtRot;
            input.HintPosition = hintPos;
            input.HintWeight = hasHint;
            input.TargetOffset = targetOffset;
            input.PlayerUp = playerUp;
            input.HintIsTracker = hintIsTracker;
            input.HintMaxStepDeg = float.MaxValue;
            input.TipRotation = handRot;
            input.HintRotation = hintIsTracker ? hintRot : default;
            input.ForearmFollowWeight = 1f;
            input.ElbowLateralOut = isLeft ? -bodyRight : bodyRight;

            BasisBoneHandle torsoFrom = poseStream.IsValid(handleChest) ? handleChest : poseStream.IsValid(handleSpine) ? handleSpine : handleHips;
            BasisBoneHandle torsoTo = poseStream.IsValid(handleNeck) ? handleNeck : handleHead;
            input.TorsoUp = poseStream.IsValid(torsoFrom) && poseStream.IsValid(torsoTo) ? poseStream.GetPosition(torsoTo) - poseStream.GetPosition(torsoFrom) : Vector3.zero;

            bool anchorSlot = hintIsTracker && slotOk;
            if (slotOk)
            {
                BasisArmSlotState arm = armState[swingSlot];
                input.PrevGuardSide = arm.GuardSide;
                if (anchorSlot && arm.PoleValid)
                {
                    input.PrevPoleDir = arm.PoleDir;
                    input.PrevHintRotation = arm.PoleRot;
                    input.HasPrevPole = true;
                }
            }

            BasisArmSolveCore.Solve(input, out BasisArmSolveResult result);

            if (slotOk)
            {
                ref BasisArmSlotState arm = ref Ref(armState, swingSlot);
                arm.GuardSide = result.GuardSideUsed;
                if (anchorSlot)
                {
                    if (result.PoleAnchorValid)
                    {
                        arm.PoleDir = result.PoleDirUsed;
                        arm.PoleRot = result.PoleRotUsed;
                        arm.PoleValid = true;
                    }
                }
                else
                {
                    arm.PoleValid = false;
                }
            }

            poseStream.SetRotation(mid, result.MidDelta * poseStream.GetRotation(mid));
            poseStream.SetRotation(root, result.RootDelta * poseStream.GetRotation(root));
            poseStream.SetRotation(root, result.HintDelta * poseStream.GetRotation(root));
            poseStream.SetRotation(mid, result.MidPostRoll * poseStream.GetRotation(mid));
            poseStream.SetRotation(tip, result.TipRotation);

            int collisionState = 0;
            bool doCollisions = collisionsEnabled && poseStream.IsValid(handleChest) && poseStream.IsValid(handleNeck);
            if (doCollisions && protectElbow && (!hintIsTracker || collideTrackedElbow))
            {
                BasisElbowProtectInput epi = default;
                epi.Shoulder = poseStream.GetPosition(root);
                epi.Elbow = poseStream.GetPosition(mid);
                epi.Hand = poseStream.GetPosition(tip);
                epi.HasHips = poseStream.IsValid(handleHips);
                epi.HasSpine = poseStream.IsValid(handleSpine);
                epi.HipsPos = epi.HasHips ? poseStream.GetPosition(handleHips) : Vector3.zero;
                epi.SpinePos = epi.HasSpine ? poseStream.GetPosition(handleSpine) : Vector3.zero;
                epi.ChestPos = poseStream.GetPosition(handleChest);
                epi.NeckPos = poseStream.GetPosition(handleNeck);
                epi.ChestRadiusBase = chestRadius;
                epi.CollisionSkin = collisionSkin;
                epi.HandRadius = handRadius;
                epi.HandSkin = handSkin;
                epi.PlayerUp = playerUp;
                epi.BodyRight = bodyRight;

                BasisElbowProtectCore.Solve(epi, out BasisElbowProtectResult epr);
                if (epr.Engaged)
                {
                    poseStream.GetPositionAndRotation(tip, out Vector3 preservedHandPos, out Quaternion preservedHandRot);
                    SwingElbowAroundAC(root, mid, tip, epr.DesiredElbow);
                    poseStream.SetPosition(tip, preservedHandPos);
                    poseStream.SetRotation(tip, preservedHandRot);
                }
                collisionState = epr.CollisionState;
            }

            if (slotOk)
            {
                Ref(armState, swingSlot).Collided = collisionState;
            }

            if (weight < 1f)
            {
                poseStream.SetRotation(root, Quaternion.Slerp(origRootRot, poseStream.GetRotation(root), weight));
                poseStream.SetRotation(mid, Quaternion.Slerp(origMidRot, poseStream.GetRotation(mid), weight));
                poseStream.SetRotation(tip, Quaternion.Slerp(origTipRot, poseStream.GetRotation(tip), weight));
            }
        }
    }
}
