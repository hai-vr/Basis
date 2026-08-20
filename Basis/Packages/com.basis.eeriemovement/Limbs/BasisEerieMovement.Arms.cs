using System.Runtime.CompilerServices;
using Basis.Scripts.Common;
using Unity.Collections;
using UnityEngine;
namespace Basis.IK
{
    public partial struct BasisEerieMovement
    {
        void SolveShoulderPass(BasisPoseStream stream)
        {
            if (shoulderSolveEnabled)
            {
                SolveShoulder(stream, true);
                SolveShoulder(stream, false);
            }
            else
            {
                ApplyRotation(stream, enabledLeftShoulder, handleLeftShoulder, targetRotationLeftShoulder, targetOffsetLeftShoulder);
                ApplyRotation(stream, enabledRightShoulder, handleRightShoulder, targetRotationRightShoulder, targetOffsetRightShoulder);
            }
            if (anatShoulderSlide)
            {
                ApplyShoulderSlide(stream);
            }
        }

        void SolveArmPass(BasisPoseStream stream)
        {
            SolveHand(stream, true);
            SolveHand(stream, false);

            if (enabledLeftHand > 0f)
            {
                ApplySwingContinuity(stream, k_SwingLeftElbow, handleLeftUpperArm, handleLeftLowerArm, handleLeftHand, targetPositionLeftHand);
            }

            if (enabledRightHand > 0f)
            {
                ApplySwingContinuity(stream, k_SwingRightElbow, handleRightUpperArm, handleRightLowerArm, handleRightHand, targetPositionRightHand);
            }

            SolveArmTwist(stream, handleLeftLowerArm, handleLeftHand, handleLeftLowerArmTwist, lowerArmTwistFraction, tposeLeftLowerArmChildBind, tposeLeftLowerArmTwistBind);
            SolveArmTwist(stream, handleRightLowerArm, handleRightHand, handleRightLowerArmTwist, lowerArmTwistFraction, tposeRightLowerArmChildBind, tposeRightLowerArmTwistBind);
            SolveArmTwist(stream, handleLeftUpperArm, handleLeftLowerArm, handleLeftUpperArmTwist, upperArmTwistFraction, tposeLeftUpperArmChildBind, tposeLeftUpperArmTwistBind);
            SolveArmTwist(stream, handleRightUpperArm, handleRightLowerArm, handleRightUpperArmTwist, upperArmTwistFraction, tposeRightUpperArmChildBind, tposeRightUpperArmTwistBind);
        }

        void ApplyShoulderSlide(BasisPoseStream stream)
        {
            if (!handleHips.IsValid(stream) || !handleChest.IsValid(stream))
            {
                return;
            }

            // Bind-cancelled like its siblings: on a rolled hips bind (Blender -90X) the raw bone frame
            // reads a lateral lean as twist and applies the counter-yaw as shoulder ROLL.
            Quaternion hipsRot = handleHips.GetRotation(stream) * Quaternion.Inverse(offsetRotationHips);
            Quaternion chestRot = handleChest.GetRotation(stream);
            Quaternion chestLocal = Quaternion.Inverse(hipsRot) * chestRot;
            float chestYaw = BasisTwistSolveCore.SignedTwistAngleDeg(chestLocal, Vector3.up);

            float excess = Mathf.Abs(chestYaw) - shoulderSlideStartDeg;
            if (excess <= 0f)
                return;

            float counterYaw = -Mathf.Sign(chestYaw) * Mathf.Min(excess * shoulderSlideFraction, shoulderSlideMaxDeg);
            ApplyShoulderYaw(stream, handleLeftShoulder, hipsRot, counterYaw);
            ApplyShoulderYaw(stream, handleRightShoulder, hipsRot, counterYaw);
        }
        void ApplyShoulderYaw(BasisPoseStream stream, BasisBoneHandle shoulder, Quaternion hipsRot, float yawDeg)
        {
            if (!shoulder.IsValid(stream))
                return;
            Quaternion delta = hipsRot * Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.Inverse(hipsRot);
            shoulder.SetRotation(stream, delta * shoulder.GetRotation(stream));
        }
        void ApplyArmSwingChestFollow(BasisPoseStream stream)
        {
            float factor = chestArmSwingFactor;
            if (factor <= 0f)
            {
                return;
            }

            if (!handleHips.IsValid(stream) || !handleChest.IsValid(stream))
            {
                return;
            }

            bool leftEnabled = enabledLeftHand > 0f;
            bool rightEnabled = enabledRightHand > 0f;
            if (!leftEnabled && !rightEnabled)
            {
                return;
            }

            Vector3 leftPos = leftEnabled ? targetPositionLeftHand : Vector3.zero;
            Vector3 rightPos = rightEnabled ? targetPositionRightHand : Vector3.zero;
            Vector3 handMid = leftEnabled && rightEnabled ? (leftPos + rightPos) * 0.5f : leftEnabled ? leftPos : rightPos;
            Vector3 hipsPos = handleHips.GetPosition(stream);
            Quaternion hipsAnat = handleHips.GetRotation(stream) * Quaternion.Inverse(offsetRotationHips);
            Quaternion invHipsAnat = Quaternion.Inverse(hipsAnat);
            Vector3 localMid = invHipsAnat * (handMid - hipsPos);

            float forwardDist = Mathf.Max(0.1f, Mathf.Abs(localMid.z));
            float yawDeg = Mathf.Atan2(localMid.x, forwardDist) * Mathf.Rad2Deg * factor;

            Vector3 localMidChest = invHipsAnat * (handMid - handleChest.GetPosition(stream));
            float pitchDeg = Mathf.Atan2(-localMidChest.y, forwardDist) * Mathf.Rad2Deg * factor;

            float maxDeg = chestArmSwingMaxDeg;
            if (maxDeg > 0f)
            {
                yawDeg = Mathf.Clamp(yawDeg, -maxDeg, maxDeg);
                pitchDeg = Mathf.Clamp(pitchDeg, -maxDeg, maxDeg);
            }

            Quaternion local = Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.AngleAxis(pitchDeg, Vector3.right);
            Quaternion deltaWorld = hipsAnat * local * invHipsAnat;

            if (handleUpperChest.IsValid(stream))
            {
                Quaternion chestPart = Quaternion.Slerp(Quaternion.identity, deltaWorld, chestFollowChestShare);
                Quaternion upperPart = Quaternion.Slerp(Quaternion.identity, deltaWorld, 1f - chestFollowChestShare);
                handleChest.SetRotation(stream, chestPart * handleChest.GetRotation(stream));
                handleUpperChest.SetRotation(stream, upperPart * handleUpperChest.GetRotation(stream));
            }
            else
            {
                handleChest.SetRotation(stream, deltaWorld * handleChest.GetRotation(stream));
            }
        }
        // Bind-cancelled like its siblings: childBind / twistBind are the T-pose rotations of the driving
        // child and of the helper itself, both in this parent's frame. Without them a rig that authors
        // either one off-axis from the arm bone -- palm-down hands, exported roll on the helper -- carries
        // a constant twist that is already there in a clean T-pose.
        void SolveArmTwist(BasisPoseStream stream, BasisBoneHandle parent, BasisBoneHandle child, BasisBoneHandle twist, float fraction, Quaternion childBind, Quaternion twistBind)
        {
            if (!twist.IsValid(stream) || fraction <= 0f)
                return;
            if (!parent.IsValid(stream) || !child.IsValid(stream))
                return;

            Vector3 parentPos = parent.GetPosition(stream);
            Vector3 childPos = child.GetPosition(stream);
            float positionFraction = BasisTwistSolveCore.SegmentPositionFraction(parentPos, childPos, twist.GetPosition(stream));

            if (BasisTwistSolveCore.Solve(parent.GetRotation(stream), child.GetRotation(stream), childPos - parentPos,
                    positionFraction * fraction, childBind, twistBind, out Quaternion twistWorld, out _, out _))
            {
                twist.SetRotation(stream, twistWorld);
            }
        }
        public void SolveShoulder(BasisPoseStream stream, bool isLeft)
        {
            BasisBoneHandle shoulderHandle = isLeft ? handleLeftShoulder : handleRightShoulder;
            if (!shoulderHandle.IsValid(stream))
            {
                return;
            }

            BasisShoulderSolveInput input;
            input.ShoulderPos = shoulderHandle.GetPosition(stream);
            input.HandTargetPos = isLeft ? targetPositionLeftHand : targetPositionRightHand;
            input.ElbowPos = isLeft ? hintPositionLeftHand : hintPositionRightHand;
            input.HasElbow = isLeft ? hintWeightLeftHand : hintWeightRightHand;
            input.HasShoulderTracker = isLeft ? enabledLeftShoulder : enabledRightShoulder;
            // The clavicle's parent is the UpperChest when one exists, and the spine pass writes the
            // UpperChest separately (routed twist + arm-swing follow) -- reading the Chest solves the
            // shoulder in a frame up to ~30 deg away from the bone it actually parents.
            input.ChestRot = handleUpperChest.IsValid(stream) ? handleUpperChest.GetRotation(stream)
                : handleChest.IsValid(stream) ? handleChest.GetRotation(stream) : Quaternion.identity;
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
            input.TrackerFinal = isLeft
                ? targetRotationLeftShoulder * targetOffsetLeftShoulder
                : targetRotationRightShoulder * targetOffsetRightShoulder;
            input.IsLeft = isLeft;

            BasisShoulderSolveCore.Solve(input, out BasisShoulderSolveResult result);
            if (result.Apply)
            {
                shoulderHandle.SetRotation(stream, result.ShoulderRotation);
            }
        }
        public void SolveTwoBoneIKArms(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, BasisAffineTransform target, BasisAffineTransform hint, bool hintWeight, bool hintIsTracker, Quaternion targetOffset, int swingSlot)
        {
            BasisArmSolveInput input = default;
            root.GetPositionAndRotation(stream, out Vector3 shoulderPos, out Quaternion shoulderRot);
            mid.GetPositionAndRotation(stream, out Vector3 elbowPos, out Quaternion elbowRot);
            tip.GetPositionAndRotation(stream, out Vector3 handPos, out Quaternion handRot);
            input.Shoulder = shoulderPos;
            input.Elbow = elbowPos;
            input.Hand = handPos;
            input.RootRotation = shoulderRot;
            input.MidRotation = elbowRot;
            input.TargetPosition = target.translation;
            input.TargetRotation = target.rotation;
            input.HintPosition = hint.translation;
            input.HintWeight = hintWeight;
            input.TargetOffset = targetOffset;
            input.PlayerUp = playerUp;
            input.HintIsTracker = hintIsTracker;
            input.HintMaxStepDeg = float.MaxValue;
            input.TipRotation = handRot;
            input.HintRotation = hintIsTracker ? hint.rotation : default;
            input.ForearmFollowWeight = 1f;

            Vector3 bodyRight = (handleLeftUpperArm.IsValid(stream) && handleRightUpperArm.IsValid(stream))
                ? handleRightUpperArm.GetPosition(stream) - handleLeftUpperArm.GetPosition(stream)
                : Vector3.zero;
            input.ElbowLateralOut = swingSlot == k_SwingLeftElbow ? -bodyRight : bodyRight;

            BasisBoneHandle torsoFrom = handleChest.IsValid(stream) ? handleChest
                : handleSpine.IsValid(stream) ? handleSpine : handleHips;
            BasisBoneHandle torsoTo = handleNeck.IsValid(stream) ? handleNeck : handleHead;
            input.TorsoUp = torsoFrom.IsValid(stream) && torsoTo.IsValid(stream)
                ? torsoTo.GetPosition(stream) - torsoFrom.GetPosition(stream)
                : Vector3.zero;

            bool slotOk = (uint)swingSlot < (uint)k_SwingCount;
            if (slotOk && swingGuardSide.IsCreated && (uint)swingSlot < (uint)swingGuardSide.Length)
            {
                input.PrevGuardSide = swingGuardSide[swingSlot];
            }

            bool anchorSlot = hintIsTracker && slotOk
                && swingPoleAnchor.IsCreated && swingPoleAnchorRot.IsCreated && swingPoleAnchorInit.IsCreated
                && (uint)swingSlot < (uint)swingPoleAnchor.Length
                && (uint)swingSlot < (uint)swingPoleAnchorRot.Length
                && (uint)swingSlot < (uint)swingPoleAnchorInit.Length;
            if (anchorSlot && swingPoleAnchorInit[swingSlot] != 0)
            {
                input.PrevPoleDir = swingPoleAnchor[swingSlot];
                input.PrevHintRotation = swingPoleAnchorRot[swingSlot];
                input.HasPrevPole = true;
            }

            BasisArmSolveCore.Solve(input, out BasisArmSolveResult result);

            if (slotOk && swingGuardSide.IsCreated && (uint)swingSlot < (uint)swingGuardSide.Length)
            {
                swingGuardSide[swingSlot] = result.GuardSideUsed;
            }
            if (anchorSlot)
            {
                if (result.PoleAnchorValid)
                {
                    swingPoleAnchor[swingSlot] = result.PoleDirUsed;
                    swingPoleAnchorRot[swingSlot] = result.PoleRotUsed;
                    swingPoleAnchorInit[swingSlot] = 1;
                }
            }
            else if (slotOk && swingPoleAnchorInit.IsCreated && (uint)swingSlot < (uint)swingPoleAnchorInit.Length)
            {
                swingPoleAnchorInit[swingSlot] = 0;
            }

            mid.SetRotation(stream, result.MidDelta * mid.GetRotation(stream));
            root.SetRotation(stream, result.RootDelta * root.GetRotation(stream));
            root.SetRotation(stream, result.HintDelta * root.GetRotation(stream));
            mid.SetRotation(stream, result.MidPostRoll * mid.GetRotation(stream));
            tip.SetRotation(stream, result.TipRotation);
        }

        BasisSwivelFrame BuildArmFrame(BasisPoseStream stream)
        {
            if (!handleLeftUpperArm.IsValid(stream) || !handleRightUpperArm.IsValid(stream))
            {
                return default;
            }

            BasisBoneHandle upFrom = handleChest.IsValid(stream) ? handleChest
                : handleSpine.IsValid(stream) ? handleSpine : handleHips;
            BasisBoneHandle upTo = handleNeck.IsValid(stream) ? handleNeck : handleHead;
            if (!upFrom.IsValid(stream) || !upTo.IsValid(stream))
            {
                return default;
            }

            return BasisSwivelHintCore.BuildFrame(
                handleLeftUpperArm.GetPosition(stream), handleRightUpperArm.GetPosition(stream),
                upFrom.GetPosition(stream), upTo.GetPosition(stream));
        }
        public static void SwingElbowAroundAC(BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 desiredB)
        {
            Vector3 A = root.GetPosition(stream);
            Vector3 C = tip.GetPosition(stream);
            Vector3 B = mid.GetPosition(stream);

            Vector3 AC = C - A;
            float acSqr = Vector3.Dot(AC, AC);
            if (acSqr <= k_SqrEpsilon) return;

            Vector3 n = AC / Mathf.Sqrt(acSqr);
            Vector3 v1 = B - A; v1 -= n * Vector3.Dot(v1, n);
            Vector3 v2 = desiredB - A; v2 -= n * Vector3.Dot(v2, n);

            float v1Sqr = Vector3.Dot(v1, v1);
            float v2Sqr = Vector3.Dot(v2, v2);
            if (v1Sqr <= k_SqrEpsilon || v2Sqr <= k_SqrEpsilon) return;

            v1 /= Mathf.Sqrt(v1Sqr);
            v2 /= Mathf.Sqrt(v2Sqr);

            float dot = Mathf.Clamp(Vector3.Dot(v1, v2), -1f, 1f);
            float ang = Mathf.Acos(dot);
            Vector3 cross = Vector3.Cross(v1, v2);
            float dir = Mathf.Sign(Vector3.Dot(cross, n));
            Quaternion swing = Quaternion.AngleAxis(ang * dir * Mathf.Rad2Deg, n);

            root.SetRotation(stream, swing * root.GetRotation(stream));
        }
        bool SwingHintStateReady(int slot)
        {
            return swingHintInit.IsCreated && swingHintBend.IsCreated && swingHintAxis.IsCreated
                && swingHintDrag.IsCreated && swingHintBodyRot.IsCreated && swingHintReach.IsCreated
                && (uint)slot < (uint)swingHintInit.Length && (uint)slot < (uint)swingHintBend.Length
                && (uint)slot < (uint)swingHintAxis.Length && (uint)slot < (uint)swingHintDrag.Length
                && (uint)slot < (uint)swingHintBodyRot.Length && (uint)slot < (uint)swingHintReach.Length;
        }

        bool SwingContinuityStateReady(int slot)
        {
            return swingContinuityInit.IsCreated && swingLastDir.IsCreated && swingLastAxis.IsCreated
                && swingLastTarget.IsCreated && swingSmoothState.IsCreated
                && (uint)slot < (uint)swingContinuityInit.Length && (uint)slot < (uint)swingLastDir.Length
                && (uint)slot < (uint)swingLastAxis.Length && (uint)slot < (uint)swingLastTarget.Length
                && (uint)slot < (uint)swingSmoothState.Length;
        }

        void ApplySwingContinuity(BasisPoseStream stream, int slot, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPos)
        {
            if (!SwingContinuityStateReady(slot) || !root.IsValid(stream) || !mid.IsValid(stream) || !tip.IsValid(stream))
            {
                return;
            }

            Vector3 a = root.GetPosition(stream);
            Vector3 c = tip.GetPosition(stream);
            Vector3 b = mid.GetPosition(stream);

            BasisSwingContinuityState state;
            state.LastDir = swingLastDir[slot];
            state.LastAxis = swingLastAxis[slot];
            state.LastTarget = swingLastTarget[slot];
            state.SmoothState = swingSmoothState[slot];
            state.Seeded = swingContinuityInit[slot] != 0;
            int collided = swingCollided.IsCreated && (uint)slot < (uint)swingCollided.Length ? swingCollided[slot] : 0;

            if (!BasisSwingContinuityCore.Step(ref state, a, b, c, targetPos, collided, swingSmoothRateDeg, stream.deltaTime,
                    out bool applySwing, out Vector3 newDir))
            {
                return;
            }

            if (applySwing)
            {
                Quaternion preservedHandRot = tip.GetRotation(stream);
                SwingElbowAroundAC(stream, root, mid, tip, a + newDir);
                tip.SetPosition(stream, c);
                tip.SetRotation(stream, preservedHandRot);
            }

            swingLastDir[slot] = state.LastDir;
            swingLastAxis[slot] = state.LastAxis;
            swingLastTarget[slot] = state.LastTarget;
            swingSmoothState[slot] = state.SmoothState;
            swingContinuityInit[slot] = 1;
        }
        public void SolveHand(BasisPoseStream stream, bool isLeft)
        {
            float weight = isLeft ? enabledLeftHand : enabledRightHand;
            if (!(weight > 0f))
            {
                return;
            }
            BasisBoneHandle root = isLeft ? handleLeftUpperArm : handleRightUpperArm;
            BasisBoneHandle mid = isLeft ? handleLeftLowerArm : handleRightLowerArm;
            BasisBoneHandle tip = isLeft ? handleLeftHand : handleRightHand;
            if (!(root.IsValid(stream) && mid.IsValid(stream) && tip.IsValid(stream)))
            {
                return;
            }
            Vector3 tgtPos = isLeft ? targetPositionLeftHand : targetPositionRightHand;
            Quaternion tgtRot = isLeft ? targetRotationLeftHand : targetRotationRightHand;
            Vector3 hintPos = isLeft ? hintPositionLeftHand : hintPositionRightHand;
            Quaternion hintRot = isLeft ? hintRotationLeftHand : hintRotationRightHand;
            bool hasHint = isLeft ? hintWeightLeftHand : hintWeightRightHand;
            Quaternion targetOffset = isLeft ? targetOffsetLeftHand : targetOffsetRightHand;
            int swingSlot = isLeft ? k_SwingLeftElbow : k_SwingRightElbow;

            Quaternion origRootRot = root.GetRotation(stream);
            Quaternion origMidRot = mid.GetRotation(stream);
            Quaternion origTipRot = tip.GetRotation(stream);

            var target = new BasisAffineTransform(tgtPos, tgtRot);
            var hint = new BasisAffineTransform(hintPos, hintRot);
            bool usedModel = false;

            if (!hasHint)
            {
                BasisSwivelFrame frame = BuildArmFrame(stream);

                Vector3 shoulderPos = root.GetPosition(stream);
                float upperLen = (mid.GetPosition(stream) - shoulderPos).magnitude;
                float lowerLen = (tip.GetPosition(stream) - mid.GetPosition(stream)).magnitude;
                float armLen = upperLen + lowerLen;
                if (BasisSwivelHintCore.ArmHint(frame, shoulderPos, tgtPos, armLen, isLeft,
                                                out Vector3 modelHint, out float poleConditioning))
                {
                    Vector3 curAxisV = tgtPos - shoulderPos;
                    Vector3 rawBendV = modelHint - shoulderPos;
                    float axLen = curAxisV.magnitude;
                    float rbLen = rawBendV.magnitude;
                    if (axLen > 1e-5f && rbLen > 1e-5f && SwingHintStateReady(swingSlot))
                    {
                        Vector3 curAxis = curAxisV / axLen;
                        Vector3 rawBend = rawBendV / rbLen;
                        bool seeded = swingHintInit[swingSlot] != 0;
                        float curReach = axLen / armLen;
                        float armDt = Mathf.Min(stream.deltaTime, BasisElbowSwingCapCore.MaxSlewBudgetDt);
                        Vector3 cappedBend = seeded
                            ? (Vector3)BasisElbowSwingCapCore.Apply(swingHintBend[swingSlot], swingHintAxis[swingSlot],
                                                                    curAxis, rawBend, BasisElbowSwingCapCore.MaxGain,
                                                                    curReach - swingHintReach[swingSlot], poleConditioning,
                                                                    BasisElbowSwingCapCore.SlewCapRad(stream.deltaTime))
                            : rawBend;
                        swingHintBend[swingSlot] = cappedBend;
                        swingHintAxis[swingSlot] = curAxis;
                        swingHintReach[swingSlot] = curReach;
                        // The elbow field lives in the chest/shoulder-line frame; cancelling only the HIPS
                        // frame left chest-relative twist uncancelled (1.8-3.6 cm elbow lag on torso twist).
                        Quaternion bodyRot = frame.Valid
                            ? Quaternion.LookRotation(frame.Forward, frame.Up)
                            : handleHips.IsValid(stream) ? handleHips.GetRotation(stream) : Quaternion.identity;

                        Vector3 outBend = cappedBend;
                        if (elbowDragEnabled && seeded)
                        {
                            Quaternion bodyDelta = bodyRot * Quaternion.Inverse(swingHintBodyRot[swingSlot]);
                            outBend = (Vector3)BasisElbowDragCore.Apply(swingHintDrag[swingSlot], bodyDelta, curAxis, cappedBend,
                                                                       BasisElbowDragCore.Alpha(elbowDragHz, armDt));
                        }
                        swingHintDrag[swingSlot] = outBend;
                        swingHintBodyRot[swingSlot] = bodyRot;
                        swingHintInit[swingSlot] = 1;
                        modelHint = shoulderPos + 0.5f * armLen * outBend;
                    }

                    hint = new BasisAffineTransform(modelHint, hintRot);
                    hasHint = true;
                    usedModel = true;
                }
            }
            if (!usedModel && SwingHintStateReady(swingSlot))
            {
                swingHintInit[swingSlot] = 0;
            }
            SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hasHint, hasHint && !usedModel, targetOffset, swingSlot);
            int collisionState = 0;
            bool doCollisions = collisionsEnabled && handleChest.IsValid(stream) && handleNeck.IsValid(stream);
            bool elbowTrackerForced = hasHint && !usedModel;
            if (doCollisions && protectElbow && (!elbowTrackerForced || collideTrackedElbow))
            {
                Vector3 bodyRight = (handleLeftUpperArm.IsValid(stream) && handleRightUpperArm.IsValid(stream))
                    ? handleRightUpperArm.GetPosition(stream) - handleLeftUpperArm.GetPosition(stream)
                    : Vector3.zero;

                BasisElbowProtectInput epi = default;
                epi.Shoulder = root.GetPosition(stream);
                epi.Elbow = mid.GetPosition(stream);
                epi.Hand = tip.GetPosition(stream);
                epi.HasHips = handleHips.IsValid(stream);
                epi.HasSpine = handleSpine.IsValid(stream);
                epi.HipsPos = epi.HasHips ? handleHips.GetPosition(stream) : Vector3.zero;
                epi.SpinePos = epi.HasSpine ? handleSpine.GetPosition(stream) : Vector3.zero;
                epi.ChestPos = handleChest.GetPosition(stream);
                epi.NeckPos = handleNeck.GetPosition(stream);
                epi.ChestRadiusBase = chestRadius;
                epi.CollisionSkin = collisionSkin;
                epi.HandRadius = handRadius;
                epi.HandSkin = handSkin;
                epi.PlayerUp = playerUp;
                epi.BodyRight = bodyRight;

                BasisElbowProtectCore.Solve(epi, out BasisElbowProtectResult epr);
                if (epr.Engaged)
                {
                    tip.GetPositionAndRotation(stream, out Vector3 preservedHandPos, out Quaternion preservedHandRot);
                    SwingElbowAroundAC(stream, root, mid, tip, epr.DesiredElbow);
                    tip.SetPosition(stream, preservedHandPos);
                    tip.SetRotation(stream, preservedHandRot);
                }
                collisionState = epr.CollisionState;
            }

            if (swingCollided.IsCreated && (uint)swingSlot < (uint)swingCollided.Length)
            {
                swingCollided[swingSlot] = collisionState;
            }

            if (weight < 1f)
            {
                root.SetRotation(stream, Quaternion.Slerp(origRootRot, root.GetRotation(stream), weight));
                mid.SetRotation(stream, Quaternion.Slerp(origMidRot, mid.GetRotation(stream), weight));
                tip.SetRotation(stream, Quaternion.Slerp(origTipRot, tip.GetRotation(stream), weight));
            }
        }
    }
}
