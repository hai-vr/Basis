using System.Runtime.CompilerServices;
using Basis.Scripts.Common;
using Unity.Collections;
using UnityEngine;
namespace Basis.IK
{
    public partial struct BasisEerieMovement
    {
        void SolveSpinePass() => SolveSpine();
        public void SolveSpine()
        {
            BasisEerieMarkers.SpineHipsPlacement.Begin();

            Quaternion chestDesired = targetRotationChest * offsetRotationChest;

            if (plan.prone)
            {
                if (plan.hasHips && plan.hasHead)
                {
                    ApplyProneBodyYaw();
                }
            }
            else
            {
                ResetSpineChainToRest();
                Vector3 headTargetPos = targetPositionHead, hipsTargetPos = targetPositionHips;
                Quaternion headTargetRot = targetRotationHead, hipsTargetRot = targetRotationHips;
                Quaternion offsetHips = offsetRotationHips, hipDesired = hipsTargetRot * offsetHips;
                float restDist = restChordHeadHips > epsilon ? restChordHeadHips : minHeadSpineHeight;
                BasisIKLockMode lockMode = ikLockMode;
                Vector3 up = playerUp;

                switch (lockMode)
                {
                    case BasisIKLockMode.LockHips: break;

                    case BasisIKLockMode.LockHead:
                        if (!plan.hipsTracked)
                        {
                            Vector3 headToHips = hipsTargetPos - headTargetPos;
                            float spineLen = headToHips.magnitude;
                            if (spineLen < restDist)
                            {
                                Vector3 spineDir = spineLen > epsilon ? headToHips / spineLen : hipsTargetRot * Vector3.down;
                                hipsTargetPos = headTargetPos + spineDir * restDist;
                            }
                        }
                        break;

                    default: hipsTargetPos = AntiContortionist(headTargetPos, headTargetRot, hipsTargetPos, hipsTargetRot, restDist);
                        hipsTargetPos = MitigateSpineBuckling(headTargetPos, hipsTargetRot, hipsTargetPos, restDist, up);
                        float MaxBendDeg = maxBendDeg;
                        hipsTargetPos = EnforceSpineBendLimit(headTargetPos, hipsTargetPos, MaxBendDeg, up);
                        hipsTargetPos = ClampHipsAroundHead(headTargetPos, hipsTargetPos, restDist, minFactor, maxFactor, up);
                        break;
                }
                Vector3 neckCue = ComputeNeckCue(headTargetPos);
                float crouchFade = 1f;
                if (!plan.hipsTracked)
                {
                    BasisTrunkCounterbalanceCore.Solve(hipsTargetPos, neckCue, up, trunkCounterbalance, trunkCounterbalanceMaxSpineFrac * restDist, out hipsTargetPos, out float flexionFrac, out _);
                    crouchFade = 1f - flexionFrac;
                }
                if (plan.crouchOffset)
                {
                    hipsTargetPos = ApplyCrouchBodyOffset(headTargetPos, hipsTargetPos, hipDesired, up, crouchFade, restDist);
                }
                targetPositionHips = hipsTargetPos;
                if (!plan.hipsTracked)
                {
                    BasisHipHingeCore.Solve(neckCue, hipsTargetPos, hipDesired, up, hipHingeStartDeg, hipHingeMaxAddDeg, out hipDesired, out _, out _);
                }

                if (plan.hasHips)
                {
                    poseStream.SetPosition(handleHips, hipsTargetPos);
                    poseStream.SetRotation(handleHips, hipDesired);
                    YieldHipsToReach(headTargetPos);
                }
            }
            BasisEerieMarkers.SpineHipsPlacement.End();
            if (plan.chestChain)
            {
                BasisEerieMarkers.SpineChainPrep.Begin();

                float Value = maxChestDeltaDeg;
                Quaternion clampedChestRot = chestDesired;
                if (plan.hasNeck)
                {
                    clampedChestRot = ClampRotation(clampedChestRot, poseStream.GetRotation(handleNeck), Value);
                }
                if (plan.hasSpine)
                {
                    clampedChestRot = ClampRotation(clampedChestRot, poseStream.GetRotation(handleSpine), Value);
                }

                clampedChestRot = ClampTrackedChest(clampedChestRot);
                chestTrackedRot = clampedChestRot;
                poseStream.SetRotation(handleChest, clampedChestRot);

                Vector3 headPos = targetPositionHead;

                DistributeSpineBend(headPos);
                BiasSpineTowardChest();
                poseStream.SetRotation(handleChest, clampedChestRot);
                SolveLordosisPass();
                GuardSpineChain();
                BasisEerieMarkers.SpineChainPrep.End();
                BasisEerieMarkers.SpineSequentialIK.Begin();
                SolveSequentialSpineIK(headPos, targetRotationHead);
                BasisEerieMarkers.SpineSequentialIK.End();
            }
            else if (plan.headChain)
            {
                Vector3 headPos = targetPositionHead;

                BasisEerieMarkers.SpineChainPrep.Begin();
                DistributeSpineBend(headPos);
                if (plan.armSwingChestFollow) ApplyArmSwingChestFollow();
                SolveLordosisPass();
                GuardSpineChain();
                BasisEerieMarkers.SpineChainPrep.End();
                BasisEerieMarkers.SpineSequentialIK.Begin();
                SolveSequentialSpineIK(headPos, targetRotationHead);
                BasisEerieMarkers.SpineSequentialIK.End();
            }
        }
        void SolveLordosisPass()
        {
            if (!plan.lordosis)
            {
                return;
            }
            BasisEerieMarkers.SpineLordosis.Begin();
            ApplyCervicalLordosis();
            BasisEerieMarkers.SpineLordosis.End();
        }
        void ResetSpineChainToRest()
        {
            restChordHeadHips = restChordHeadLumbar = restChordHeadUpper = restReachHeadLumbar = 0f;
            if (!plan.hasSpineChain)
            {
                return;
            }
            int chainLen = chainHeadToSpine.Length;
            for (int i = chainLen - 1; i >= 0; i--)
            {
                poseStream.ResetToRest(chainHeadToSpine[i]);
            }
            Vector3 tip = poseStream.GetPosition(chainHeadToSpine[0]);
            restChordHeadHips = (tip - poseStream.GetPosition(chainHeadToSpine[chainLen - 1])).magnitude;
            restChordHeadLumbar = (tip - poseStream.GetPosition(chainHeadToSpine[chainLen - 2])).magnitude;
            restChordHeadUpper = plan.hasChestJoint && plan.chestIdx >= 2 ? (tip - poseStream.GetPosition(chainHeadToSpine[plan.chestIdx - 1])).magnitude : 0f;
            restReachHeadLumbar = SpineChainReach(chainLen - 2);
            chestRestFromHead = plan.hasChestJoint ? BasisSpineAnatomyCore.Conj(poseStream.GetRotation(chainHeadToSpine[0])) * poseStream.GetRotation(chainHeadToSpine[plan.chestIdx]) : Quaternion.identity;
        }
        const float chestFromHeadMaxDeg = 80f;
        Quaternion ClampTrackedChest(Quaternion chestRot)
        {
            if (!plan.hipsTracked || !plan.hasChestJoint)
            {
                return chestRot;
            }
            if (plan.hasHead)
            {
                chestRot = ClampRotation(chestRot, targetRotationHead * offsetRotationHead * chestRestFromHead, chestFromHeadMaxDeg);
            }
            if (!plan.spineRom)
            {
                return chestRot;
            }
            int chestIdx = plan.chestIdx, parentIdx = chestIdx + 1, chainLen = chainHeadToSpine.Length;
            BasisSpineRestFrame frame = chainSpineRestFrames[chestIdx];
            if (!frame.Valid)
            {
                return chestRot;
            }
            BasisSpineRom rom = BasisSpineAnatomy.Rom(frame.Segment);
            if (parentIdx < chainLen - 1)
            {
                BasisSpineRestFrame parentFrame = chainSpineRestFrames[parentIdx];
                if (!parentFrame.Valid)
                {
                    return chestRot;
                }
                BasisSpineRom parentRom = BasisSpineAnatomy.Rom(parentFrame.Segment);
                frame.Right = parentFrame.RestLocalRot * frame.Right;
                frame.Up = parentFrame.RestLocalRot * frame.Up;
                frame.Forward = parentFrame.RestLocalRot * frame.Forward;
                frame.RestLocalRot = parentFrame.RestLocalRot * frame.RestLocalRot;
                rom = new BasisSpineRom(rom.FlexDeg + parentRom.FlexDeg, rom.ExtDeg + parentRom.ExtDeg, rom.LatDeg + parentRom.LatDeg, rom.AxialDeg + parentRom.AxialDeg);
            }
            Quaternion baseRot = poseStream.GetRotation(chainHeadToSpine[parentIdx < chainLen - 1 ? chainLen - 1 : parentIdx]);
            Quaternion clamped = BasisSpineAnatomyCore.Clamp(BasisSpineAnatomyCore.Conj(baseRot) * chestRot, frame, rom, out BasisSpineClampInfo info);
            return info.Touched ? baseRot * clamped : chestRot;
        }
        void YieldHipsToReach(Vector3 headPos)
        {
            bool headAnchored = !plan.hipsTracked || ikLockMode != BasisIKLockMode.LockHips;
            if (!headAnchored || !plan.hasSpineChain || !plan.hasHips || !(restReachHeadLumbar > epsilon))
            {
                return;
            }
            float reach = restReachHeadLumbar * (plan.hipsTracked ? 1f + Mathf.Max(0f, spineStretchMax) : 1f);
            Vector3 lumbarPos = poseStream.GetPosition(chainHeadToSpine[chainHeadToSpine.Length - 2]), toHead = headPos - lumbarPos;
            float dist = toHead.magnitude, excess = dist - reach;
            if (!(excess > 0f) || dist < epsilon)
            {
                return;
            }
            Vector3 hipsPos = poseStream.GetPosition(handleHips) + toHead * (excess / dist);
            targetPositionHips = hipsPos;
            poseStream.SetPosition(handleHips, hipsPos);
        }
        float SpineChainReach(int rootIdx)
        {
            float reach = 0f;
            for (int i = 0; i < rootIdx; i++)
            {
                reach += (poseStream.GetPosition(chainHeadToSpine[i]) - poseStream.GetPosition(chainHeadToSpine[i + 1])).magnitude;
            }
            return reach;
        }
        void ApplyProneBodyYaw()
        {
            Vector3 up = playerUp, hipsPos = poseStream.GetPosition(handleHips);
            Vector3 headPos = poseStream.GetPosition(handleHead), bodyFwd = headPos - hipsPos;
            bodyFwd -= up * Vector3.Dot(bodyFwd, up);
            Vector3 desiredFwd = targetRotationHips * Vector3.forward;
            desiredFwd -= up * Vector3.Dot(desiredFwd, up);
            if (bodyFwd.sqrMagnitude < sqrEpsilon || desiredFwd.sqrMagnitude < sqrEpsilon)
            {
                return;
            }

            float deltaYaw = Vector3.SignedAngle(bodyFwd, desiredFwd, up);
            Quaternion swing = Quaternion.AngleAxis(deltaYaw, up);
            Vector3 toTarget = targetPositionHead - headPos;
            toTarget -= up * Vector3.Dot(toTarget, up);
            poseStream.SetPosition(handleHips, headPos + swing * (hipsPos - headPos) + toTarget);
            poseStream.SetRotation(handleHips, swing * poseStream.GetRotation(handleHips));
        }
        public void SolveSequentialSpineIK(Vector3 headTargetPos, Quaternion headTargetRot)
        {
            if (!plan.hasSpineChain)
                return;

            int chainLen = chainHeadToSpine.Length, chestIdx = plan.chestIdx;
            const int tipIdx = 0, firstJoint = 1;
            bool chestSplit = plan.chestTracked && plan.hasChestJoint;
            int maxIters = Mathf.Max(1, spineMaxIterations);
            float tolerance = Mathf.Max(0f, spineTolerance), tolSqr = tolerance * tolerance;
            Quaternion hipsTwistRot = (plan.hasHips ? poseStream.GetRotation(handleHips) : Quaternion.identity) * Quaternion.Inverse(offsetRotationHips);
            Vector3 ccdUp = hipsTwistRot * Vector3.up, ccdRight = hipsTwistRot * Vector3.right;
            if (ccdUp.sqrMagnitude < sqrEpsilon) ccdUp = playerUp;
            if (chestSplit)
            {
                SolveChestTarget(chestIdx, chainLen - 2, ccdUp, tolSqr);
                poseStream.SetRotation(chainHeadToSpine[chestIdx], chestTrackedRot);
                SweepHead(headTargetPos, firstJoint, chestIdx - 1, restChordHeadUpper, false, ccdUp, ccdRight, tolerance, maxIters);
            }
            if (!chestSplit)
            {
                SweepHead(headTargetPos, firstJoint, chainLen - 2, restChordHeadLumbar, plan.hipsTracked, ccdUp, ccdRight, tolerance, maxIters);
            }
            else
            {
                Vector3 tip = poseStream.GetPosition(chainHeadToSpine[tipIdx]), toHead = headTargetPos - tip;
                float relax = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((toHead.magnitude - tolerance) / Mathf.Max(tolerance * relaxEaseToleranceFactor, epsilon)));
                if (relax > 0f)
                {
                    SweepHead(tip + toHead * relax, firstJoint, chainLen - 2, restChordHeadLumbar, plan.hipsTracked, ccdUp, ccdRight, tolerance, maxIters);
                }
            }
            Quaternion finalHeadRot = headTargetRot * offsetRotationHead;
            ShareNeckYaw(finalHeadRot);
            poseStream.SetRotation(chainHeadToSpine[tipIdx], finalHeadRot);
        }
        void SweepHead(Vector3 headTargetPos, int firstJoint, int lastJoint, float restChord, bool measuredRoot, Vector3 ccdUp, Vector3 ccdRight, float tolerance, int maxIters)
        {
            if (lastJoint < firstJoint)
            {
                return;
            }
            int chestIdx = plan.chestIdx;
            float jointSpan = Mathf.Max(1, lastJoint - firstJoint), shareTotal = 0f, tolSqr = tolerance * tolerance;
            for (int i = firstJoint; i <= lastJoint; i++)
            {
                shareTotal += JointShare(i);
            }
            headTargetPos = CommandHeadTarget(headTargetPos, lastJoint, restChord, measuredRoot, out float chainReach);
            for (int k = 0; k < seedPasses; k++)
            {
                SeedBow(firstJoint, lastJoint, headTargetPos, chainReach, shareTotal, ccdRight);
                ReachHeadJoint(lastJoint, headTargetPos, firstJoint, chestIdx, jointSpan, ccdUp, 1f, true);
            }
            float dampSqr = tolerance * aimDampToleranceFactor;
            dampSqr *= dampSqr;
            for (int iter = 0; iter < maxIters; iter++)
            {
                float errSqr = (headTargetPos - poseStream.GetPosition(chainHeadToSpine[0])).sqrMagnitude;
                if (errSqr < tolSqr)
                    break;

                bool damp = errSqr < dampSqr;
                float remaining = shareTotal;
                for (int i = firstJoint; i <= lastJoint; i++)
                {
                    float share = JointShare(i);
                    ReachHeadJoint(i, headTargetPos, firstJoint, chestIdx, jointSpan, ccdUp, remaining > epsilon ? share / remaining : 1f, !damp || i == lastJoint);
                    remaining -= share;
                }
            }
        }
        const float aimDeadbandDeg = 1f, seedPlaneBlendSin = 0.139f, aimDampToleranceFactor = 4f, relaxEaseToleranceFactor = 8f;
        const int seedPasses = 3;
        float JointShare(int i)
        {
            bool unset = !(spineBendPitch > 0f) && !(chestBendPitch > 0f) && !(upperChestBendPitch > 0f);
            float lumbar = unset ? 0.40f : Mathf.Max(0f, spineBendPitch), chest = unset ? 0.20f : Mathf.Max(0f, chestBendPitch), upper = unset ? 0.15f : Mathf.Max(0f, upperChestBendPitch);
            int h = chainHeadToSpine[i].IndexPlusOne;
            if (h == handleSpine.IndexPlusOne) return lumbar;
            if (h == handleChest.IndexPlusOne) return chest;
            if (h == handleUpperChest.IndexPlusOne) return upper;
            if (h == handleNeck.IndexPlusOne) return Mathf.Max(0.05f, 1f - lumbar - chest - upper);
            return 0.2f;
        }
        void SeedBow(int firstJoint, int lastJoint, Vector3 commandedTarget, float chainReach, float shareTotal, Vector3 ccdRight)
        {
            Vector3 rootPos = poseStream.GetPosition(chainHeadToSpine[lastJoint]), chord = poseStream.GetPosition(chainHeadToSpine[0]) - rootPos, toTarget = commandedTarget - rootPos;
            float chordNow = chord.magnitude, targetDist = toTarget.magnitude, need = chordNow - targetDist;
            if (!(chainReach > epsilon) || !(targetDist > epsilon) || !(chordNow > epsilon) || lastJoint < firstJoint)
            {
                return;
            }
            if (need < 0f)
            {
                float unfold = Mathf.Clamp01(-need / Mathf.Max(chainReach - chordNow, epsilon));
                Vector3 dir = toTarget / targetDist;
                for (int i = lastJoint; i >= firstJoint; i--)
                {
                    Vector3 seg = poseStream.GetPosition(chainHeadToSpine[i - 1]) - poseStream.GetPosition(chainHeadToSpine[i]);
                    if (seg.sqrMagnitude < sqrEpsilon)
                    {
                        continue;
                    }
                    poseStream.SetRotation(chainHeadToSpine[i], Quaternion.Slerp(Quaternion.identity, BasisQuaternionExt.FromToRotation(seg, dir), unfold) * poseStream.GetRotation(chainHeadToSpine[i]));
                    GuardSpineJoint(i);
                }
                return;
            }
            float bowDeg = BasisSpineBendCore.BowAngleDeg(need / chainReach);
            if (!(bowDeg > 0f))
            {
                return;
            }
            Vector3 chordN = chord / chordNow, sagittal = ccdRight - chordN * Vector3.Dot(ccdRight, chordN);
            if (sagittal.sqrMagnitude < sqrEpsilon)
            {
                sagittal = Vector3.Cross(chordN, Mathf.Abs(chordN.y) < 0.9f ? Vector3.up : Vector3.forward);
            }
            if (sagittal.sqrMagnitude < sqrEpsilon)
            {
                return;
            }
            sagittal.Normalize();
            Vector3 toward = Vector3.Cross(chord, toTarget);
            float towardMag = toward.magnitude, w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(towardMag / (chordNow * targetDist * seedPlaneBlendSin)));
            if (w > 0f)
            {
                toward /= towardMag;
            }
            int joints = lastJoint - firstJoint + 1;
            for (int i = firstJoint; i <= lastJoint; i++)
            {
                float part = bowDeg * (shareTotal > epsilon ? JointShare(i) / shareTotal : 1f / joints);
                Quaternion bow = Quaternion.AngleAxis(part * (1f - w), sagittal);
                if (w > 0f)
                {
                    bow = Quaternion.AngleAxis(part * w, toward) * bow;
                }
                poseStream.SetRotation(chainHeadToSpine[i], bow * poseStream.GetRotation(chainHeadToSpine[i]));
            }
        }
        Vector3 CommandHeadTarget(Vector3 headTargetPos, int lastJoint, float restChord, bool measuredRoot, out float chainReach)
        {
            Vector3 rootPos = poseStream.GetPosition(chainHeadToSpine[lastJoint]), rootToTarget = headTargetPos - rootPos;
            float targetDist = rootToTarget.magnitude;
            chainReach = SpineChainReach(lastJoint);
            if (!(targetDist > epsilon) || !(chainReach > epsilon))
            {
                return headTargetPos;
            }
            if (measuredRoot && targetDist > chainReach && spineStretchMax > 0f)
            {
                float stretch = Mathf.Min(targetDist / chainReach, 1f + spineStretchMax);
                for (int i = 0; i < lastJoint; i++)
                {
                    int index = chainHeadToSpine[i].Index;
                    poseStream.LocalPosition[index] = poseStream.LocalPosition[index] * stretch;
                }
                poseStream.InvalidateWorldCache();
                chainReach *= stretch;
            }
            if (!(restChord > epsilon))
            {
                restChord = minHeadSpineHeight;
            }
            if (!(restChord > epsilon) || restChord > chainReach)
            {
                restChord = chainReach;
            }
            float compression = restChord - targetDist, commandedDist;
            if (compression > 0f)
            {
                float bandFull = spineTautBandFrac * chainReach, band = bandFull > epsilon ? bandFull * Mathf.Clamp01(1f - (chainReach - restChord) / bandFull) : 0f;
                float denom = compression * compression + band * band;
                commandedDist = denom > 0f ? restChord - compression * compression * compression / denom : targetDist;
            }
            else
            {
                commandedDist = Mathf.Min(targetDist, chainReach);
            }
            return rootPos + rootToTarget * (commandedDist / targetDist);
        }
        void ShareNeckYaw(Quaternion finalHeadRot)
        {
            float share = Mathf.Clamp01(neckYawShare);
            if (share <= 0f || !plan.hasNeck || !plan.hasHead || !poseStream.RestLocalRotation.IsCreated || chainHeadToSpine[1].IndexPlusOne != handleNeck.IndexPlusOne)
            {
                return;
            }
            Vector3 neckPos = poseStream.GetPosition(handleNeck), axis = poseStream.GetPosition(handleHead) - neckPos;
            if (axis.sqrMagnitude < sqrEpsilon)
            {
                return;
            }
            axis.Normalize();
            Quaternion neckRot = poseStream.GetRotation(handleNeck), restHead = neckRot * (Quaternion)poseStream.RestLocalRotation[handleHead.Index];
            float twistDeg = BasisTwistSolveCore.SignedTwistAngleDeg(finalHeadRot * Quaternion.Inverse(restHead), axis);
            if (Mathf.Abs(twistDeg) < 1e-3f)
            {
                return;
            }
            poseStream.SetRotation(handleNeck, Quaternion.AngleAxis(share * twistDeg, axis) * neckRot);
            GuardSpineJoint(1);
        }
        void ReachHeadJoint(int i, Vector3 headTargetPos, int firstJoint, int chestIdx, float jointSpan, Vector3 ccdUp, float gain, bool closer)
        {
            const int tipIdx = 0;
            Vector3 jointPos = poseStream.GetPosition(chainHeadToSpine[i]);
            Vector3 curTipPos = poseStream.GetPosition(chainHeadToSpine[tipIdx]), cur = curTipPos - jointPos;
            Vector3 tgt = headTargetPos - jointPos;
            if (cur.sqrMagnitude < sqrEpsilon || tgt.sqrMagnitude < sqrEpsilon)
                return;

            Quaternion delta = BasisQuaternionExt.FromToRotation(cur, tgt);
            float t = (i - firstJoint) / jointSpan, jointTwistKeep = Mathf.Lerp(spineNeckTwistKeep, spineTwistKeep, t);
            bool guarded = plan.spineRom && chainSpineRestFrames[i].Valid;
            Vector3 twistAxis = guarded ? poseStream.GetRotation(chainHeadToSpine[i + 1]) * chainSpineRestFrames[i].Up : ccdUp;
            delta = BasisTwistSolveCore.ShapeReachStep(delta, twistAxis, jointTwistKeep, 1f);
            float angleDeg = Quaternion.Angle(Quaternion.identity, delta), soft = closer ? 1f : angleDeg / (angleDeg + aimDeadbandDeg);
            delta = Quaternion.Slerp(Quaternion.identity, delta, Mathf.Clamp01(spineCCDRelax * gain * soft));
            poseStream.SetRotation(chainHeadToSpine[i], delta * poseStream.GetRotation(chainHeadToSpine[i]));

            if (!guarded)
            {
                if (i == firstJoint)
                {
                    ClampNeckCone(i, neckMaxConeDeg);
                }
                else if (i == chestIdx && !plan.chestTracked)
                {
                    ClampChestCone(i, maxChestDeltaDeg);
                }
            }

            GuardSpineJoint(i);
        }
        void SolveChestTarget(int chestBoneIdx, int lastJoint, Vector3 ccdUp, float tolSqr)
        {
            if (!plan.chestTarget)
                return;

            Vector3 chestTargetPos = targetPositionChest;
            if ((chestTargetPos - poseStream.GetPosition(chainHeadToSpine[chestBoneIdx])).sqrMagnitude > (chestPullMaxDist * chestPullMaxDist))
                return;

            float weight = Mathf.Clamp01(spineCCDRelax * chestIkWeight);
            for (int citer = 0; citer < chestIkIterations; citer++)
            {
                Vector3 spinePos = poseStream.GetPosition(chainHeadToSpine[lastJoint]), chestNow = poseStream.GetPosition(chainHeadToSpine[chestBoneIdx]);
                if ((chestTargetPos - chestNow).sqrMagnitude < tolSqr)
                {
                    break;
                }

                Vector3 cCur = chestNow - spinePos, cTgt = chestTargetPos - spinePos;
                if (cCur.sqrMagnitude < sqrEpsilon || cTgt.sqrMagnitude < sqrEpsilon)
                {
                    break;
                }
                Quaternion cDelta = BasisQuaternionExt.FromToRotation(cCur, cTgt);
                cDelta = BasisTwistSolveCore.ShapeReachStep(cDelta, ccdUp, spineTwistKeep, 1f);
                cDelta = Quaternion.Slerp(Quaternion.identity, cDelta, weight);
                poseStream.SetRotation(chainHeadToSpine[lastJoint], cDelta * poseStream.GetRotation(chainHeadToSpine[lastJoint]));
                GuardSpineJoint(lastJoint);
            }
        }
        void GuardSpineJoint(int i)
        {
            if (!plan.spineRom || (plan.chestTracked && i == plan.chestIdx))
            {
                return;
            }

            BasisSpineRestFrame frame = chainSpineRestFrames[i];
            if (!frame.Valid)
            {
                return;
            }

            int parent = i + 1;
            Quaternion parentRot = poseStream.GetRotation(chainHeadToSpine[parent]);
            Quaternion boneRot = poseStream.GetRotation(chainHeadToSpine[i]);
            Quaternion local = BasisSpineAnatomyCore.Conj(parentRot) * boneRot;
            Quaternion clamped = BasisSpineAnatomyCore.Clamp(local, frame, BasisSpineAnatomy.Rom(frame.Segment), out BasisSpineClampInfo info);
            if (!info.Touched)
            {
                return;
            }

            poseStream.SetRotation(chainHeadToSpine[i], parentRot * clamped);
        }
        void GuardSpineChain()
        {
            if (!plan.hasSpineChain)
            {
                return;
            }
            for (int i = 1; i <= chainHeadToSpine.Length - 2; i++)
            {
                if (plan.chestTracked && i == plan.chestIdx)
                {
                    continue;
                }
                GuardSpineJoint(i);
            }
        }
        void ClampNeckCone(int neckIdx, float maxConeDeg)
        {
            Vector3 chestPos = poseStream.GetPosition(chainHeadToSpine[neckIdx + 1]);
            Vector3 neckPos = poseStream.GetPosition(chainHeadToSpine[neckIdx]);
            Vector3 headPos = poseStream.GetPosition(chainHeadToSpine[0]), parentDir = neckPos - chestPos;
            Vector3 boneDir = headPos - neckPos;
            if (parentDir.sqrMagnitude < sqrEpsilon || boneDir.sqrMagnitude < sqrEpsilon)
            {
                return;
            }

            float ang = Vector3.Angle(parentDir, boneDir);
            if (ang <= maxConeDeg)
            {
                return;
            }

            Vector3 axis = Vector3.Cross(boneDir, parentDir);
            if (axis.sqrMagnitude < sqrEpsilon)
            {
                return;
            }

            axis.Normalize();
            Quaternion correction = Quaternion.AngleAxis(ang - maxConeDeg, axis);
            poseStream.SetRotation(chainHeadToSpine[neckIdx], correction * poseStream.GetRotation(chainHeadToSpine[neckIdx]));
        }
        void ClampChestCone(int chestIdx, float maxConeDeg)
        {
            Vector3 spinePos = poseStream.GetPosition(chainHeadToSpine[chestIdx + 1]);
            Vector3 chestPos = poseStream.GetPosition(chainHeadToSpine[chestIdx]);
            Vector3 childPos = poseStream.GetPosition(chainHeadToSpine[chestIdx - 1]), parentDir = chestPos - spinePos;
            Vector3 boneDir = childPos - chestPos;
            if (parentDir.sqrMagnitude < sqrEpsilon || boneDir.sqrMagnitude < sqrEpsilon)
                return;

            float ang = Vector3.Angle(parentDir, boneDir);
            if (ang <= maxConeDeg)
                return;

            Vector3 axis = Vector3.Cross(boneDir, parentDir);
            if (axis.sqrMagnitude < sqrEpsilon)
                return;

            axis.Normalize();
            Quaternion correction = Quaternion.AngleAxis(ang - maxConeDeg, axis);
            poseStream.SetRotation(chainHeadToSpine[chestIdx], correction * poseStream.GetRotation(chainHeadToSpine[chestIdx]));
        }
        void BiasSpineTowardChest()
        {
            if (!plan.hasSpine || !plan.hasChest)
                return;

            Vector3 chestTargetPos = targetPositionChest, spinePos = poseStream.GetPosition(handleSpine);
            Vector3 chestPos = poseStream.GetPosition(handleChest);

            if ((chestTargetPos - chestPos).sqrMagnitude > (chestPullMaxDist * chestPullMaxDist))
                return;

            Vector3 cur = chestPos - spinePos, tgt = chestTargetPos - spinePos;
            if (cur.sqrMagnitude < sqrEpsilon || tgt.sqrMagnitude < sqrEpsilon)
                return;

            Quaternion pull = ClampRotation(BasisQuaternionExt.FromToRotation(cur, tgt), Quaternion.identity, chestPosPullMaxDeg);
            poseStream.SetRotation(handleSpine, pull * poseStream.GetRotation(handleSpine));
        }
        Vector3 ComputeNeckCue(Vector3 headTargetPos)
        {
            return BasisNeckCueCore.Solve(headTargetPos, targetRotationHead * offsetRotationHead, tposeHeadToNeckLocal, playerUp, neckExtensionDamp, neckFlexionDamp);
        }
        public void DistributeSpineBend(Vector3 headTargetPos)
        {
            if (!plan.hasSpineBend)
            {
                return;
            }

            bool hasSpine = plan.hasSpine, hasUpper = plan.hasUpperChest;
            Quaternion hipsRot = poseStream.GetRotation(handleHips);
            Vector3 neckCue = ComputeNeckCue(headTargetPos);
            Vector3 spineCue = Vector3.Lerp(neckCue, headTargetPos, Mathf.Clamp01(spineGazeFollow));
            Quaternion hipsBind = offsetRotationHips;
            BasisSpineBendInput input;
            input.HipsRot = hipsRot;
            input.HipsPos = poseStream.GetPosition(handleHips);
            input.ChestPos = poseStream.GetPosition(handleChest);
            input.SmoothedHead = ApplyChestSpring(spineCue);
            input.HipsBind = hipsBind;
            input.HeadTargetRot = targetRotationHead;
            input.SpineMaxForwardDeg = spineMaxForwardDeg;
            input.SpineMaxBackwardDeg = spineMaxBackwardDeg;
            input.SpineMaxLateralDeg = spineMaxLateralDeg;
            input.SpineBendPitch = spineBendPitch;
            input.SpineBendYaw = spineBendYaw;
            input.SpineBendRoll = spineBendRoll;
            input.UpperBendPitch = upperChestBendPitch;
            input.UpperBendYaw = upperChestBendYaw;
            input.UpperBendRoll = upperChestBendRoll;
            input.AnatDifferentialStiffness = anatDifferentialStiffness;
            input.AnatPelvicTwistRouting = anatPelvicTwistRouting;
            input.SquishBoost = spineSquishBoost;
            input.RestLen = tposeLengthNeckToHips.magnitude;
            input.BendTwistCoupling = bendTwistCoupling;
            input.HasSpine = hasSpine;
            input.HasUpper = hasUpper;

            if (plan.chestTracked)
            {
                input.SpineBendPitch = 0f;
                input.SpineBendRoll = 0f;
                input.UpperBendPitch = 0f;
                input.UpperBendRoll = 0f;
            }

            BasisSpineBendChestInput chest;
            chest.ChestBendPitch = chestBendPitch;
            chest.ChestBendYaw = chestBendYaw;
            chest.ChestBendRoll = chestBendRoll;
            chest.TautBandFrac = spineTautBandFrac;
            chest.NeckCue = neckCue;
            chest.HasChest = !plan.chestTracked;
            chest.HasNeckCue = true;
            BasisSpineBendCore.Solve(input, chest, out BasisSpineBendResult r);
            if (r.EarlyOut)
            {
                return;
            }

            Quaternion hipsAnat = hipsRot * Quaternion.Inverse(hipsBind), invHipsAnat = Quaternion.Inverse(hipsAnat);
            if (r.WriteSpine)
            {
                Quaternion deltaWorld = hipsAnat * BasisSpineBendCore.Compose(r.SpineEuler) * invHipsAnat;
                poseStream.SetRotation(handleSpine, deltaWorld * poseStream.GetRotation(handleSpine));
            }
            if (r.WriteChest)
            {
                Quaternion deltaWorld = hipsAnat * BasisSpineBendCore.Compose(r.ChestEuler) * invHipsAnat;
                poseStream.SetRotation(handleChest, deltaWorld * poseStream.GetRotation(handleChest));
            }
            if (r.WriteUpper)
            {
                Quaternion deltaWorld = hipsAnat * BasisSpineBendCore.Compose(r.UpperEuler) * invHipsAnat;
                poseStream.SetRotation(handleUpperChest, deltaWorld * poseStream.GetRotation(handleUpperChest));
            }
        }
        Vector3 ApplyChestSpring(Vector3 headTargetPos)
        {
            if (!plan.hasChestSpring)
            {
                return headTargetPos;
            }

            ref BasisChestSpringState spring = ref Ref(chestSpring, 0);
            float hz = chestSpringHz;
            if (hz <= 0f || !spring.Seeded)
            {
                spring.Pos = headTargetPos;
                spring.Vel = Vector3.zero;
                spring.Seeded = true;
                return headTargetPos;
            }

            float dt = poseStream.deltaTime;
            if (dt <= 0f)
                return spring.Pos;

            BasisChestSpringCore.Step(spring.Pos, spring.Vel, headTargetPos, dt, hz, chestSpringDamping, out Vector3 newPos, out Vector3 newVel);

            if (!IsFinite(newPos) || !IsFinite(newVel))
            {
                spring.Pos = headTargetPos;
                spring.Vel = Vector3.zero;
                return headTargetPos;
            }

            spring.Pos = newPos;
            spring.Vel = newVel;
            return newPos;
        }
        static bool IsFinite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        Vector3 ApplyCrouchBodyOffset(Vector3 headTargetPos, Vector3 hipsPos, Quaternion hipsRot, Vector3 playerUpDir, float fade, float restDist)
        {
            BasisCrouchOffsetInput input;
            input.HeadTargetPos = headTargetPos;
            input.HipsPos = hipsPos;
            input.HipsRot = hipsRot;
            input.Bind = offsetRotationHips;
            input.PlayerUp = playerUpDir;
            input.Factor = moveBodyBackWhenCrouching;
            input.RestDist = restDist;
            input.CrouchDepth = crouchDepth;
            input.StandingHeadHeight = standingHeadHeight;
            input.Fade = fade;
            BasisCrouchOffsetCore.Solve(input, out BasisCrouchOffsetResult result);
            return result.HipsPos;
        }
        public void ApplyCervicalLordosis()
        {
            if (!plan.hasNeck)
            {
                return;
            }

            Vector3 referenceUp;
            if (plan.hasChest)
            {
                Vector3 chestToNeck = poseStream.GetPosition(handleNeck) - poseStream.GetPosition(handleChest);
                referenceUp = chestToNeck.sqrMagnitude > sqrEpsilon ? chestToNeck.normalized : poseStream.GetRotation(handleChest) * Vector3.up;
            }
            else
            {
                referenceUp = playerUp;
            }

            BasisCervicalInput input;
            input.BaseDeg = lordosisBaseDeg;
            input.NeckShare = Mathf.Clamp01(lordosisNeckShare);
            input.MaxHeadPitchDeg = lordosisMaxHeadPitchDeg;
            input.ExtremeStartDeg = lordosisExtremeStartDeg;
            input.ExtremeFullDeg = lordosisExtremeFullDeg;
            input.ExtremeRollForwardMaxDeg = lordosisExtremeRollForwardMaxDeg;
            input.ExtremeRollBackwardMaxDeg = lordosisExtremeRollBackwardMaxDeg;
            input.ExtremeHipsHorizontalMax = lordosisExtremeHipsHorizontalMax;
            input.ExtremeChestHorizontalMax = lordosisExtremeChestHorizontalMax;
            input.ExtremeHipsHorizontalLookUp = lordosisExtremeHipsHorizontalLookUp;
            input.ExtremeChestHorizontalLookUp = lordosisExtremeChestHorizontalLookUp;
            input.ExtremeHipsDownMax = lordosisExtremeHipsDownMax;
            input.ExtremeChestDownMax = lordosisExtremeChestDownMax;
            input.ExtremeHipsDownLookUp = lordosisExtremeHipsDownLookUp;
            input.ExtremeChestDownLookUp = lordosisExtremeChestDownLookUp;
            input.PitchGainDeg = Mathf.Max(0f, lordosisPitchGainDeg);
            input.ReferenceUp = referenceUp;
            input.HeadTargetRot = targetRotationHead;
            input.HasUpperChest = plan.hasUpperChest;

            BasisCervicalSolveCore.Solve(input, out BasisCervicalResult result);
            if (result.EarlyOut)
            {
                return;
            }

            Vector3 shoulderRight = plan.hasBodyRight ? poseStream.GetPosition(handleRightUpperArm) - poseStream.GetPosition(handleLeftUpperArm) : Vector3.zero;
            bool hasShoulderRight = shoulderRight.sqrMagnitude > sqrEpsilon;
            if (hasShoulderRight)
            {
                shoulderRight.Normalize();
            }

            if (plan.hasChestRef && result.BhDeg != 0f && !(plan.chestTracked && plan.chestRef.IndexPlusOne == handleChest.IndexPlusOne))
            {
                Quaternion bhRot = poseStream.GetRotation(plan.chestRef);
                Vector3 bhAxis = hasShoulderRight ? shoulderRight : bhRot * Vector3.right;
                poseStream.SetRotation(plan.chestRef, Quaternion.AngleAxis(result.BhDeg, bhAxis) * bhRot);
            }

            if (result.HasExtreme)
            {
                Quaternion refRot = plan.hasHips ? poseStream.GetRotation(handleHips) * Quaternion.Inverse(offsetRotationHips) : (plan.hasChest ? poseStream.GetRotation(handleChest) : Quaternion.identity);
                Vector3 refForward = refRot * Vector3.forward, refDown = -(refRot * Vector3.up);

                if (plan.hasHips && !plan.hipsTracked)
                {
                    poseStream.SetPosition(handleHips, poseStream.GetPosition(handleHips) + refForward * result.HipsForwardAmount + refDown * result.HipsDownAmount);
                    YieldHipsToReach(targetPositionHead);
                }

                if (plan.hasChest && plan.hasSpine && !plan.chestTracked)
                {
                    Vector3 chestOffset = refForward * result.ChestForwardAmount + refDown * result.ChestDownAmount;
                    Vector3 spinePos = poseStream.GetPosition(handleSpine), cur = poseStream.GetPosition(handleChest) - spinePos, tgt = cur + chestOffset;
                    if (cur.sqrMagnitude > sqrEpsilon && tgt.sqrMagnitude > sqrEpsilon)
                    {
                        poseStream.SetRotation(handleSpine, BasisQuaternionExt.FromToRotation(cur, tgt) * poseStream.GetRotation(handleSpine));
                    }
                }
            }
            float extraNeckDeg = Mathf.Clamp01(neckGazeFollow) * neckGazeFollowMaxDeg * result.LookDownFrac;
            float totalNeckDeg = result.NeckDeg + extraNeckDeg;
            if (totalNeckDeg != 0f)
            {
                Quaternion neckRotCurrent = poseStream.GetRotation(handleNeck);
                Vector3 neckAxis = hasShoulderRight ? shoulderRight : neckRotCurrent * Vector3.right;
                poseStream.SetRotation(handleNeck, Quaternion.AngleAxis(totalNeckDeg, neckAxis) * neckRotCurrent);
            }
        }
        public static Vector3 ClampHipsAroundHead(Vector3 headPos, Vector3 hipsPos, float restDistance, float minFactor, float maxFactor, Vector3 playerUp)
        {
            Vector3 headToHips = hipsPos - headPos;
            float dist = headToHips.magnitude, minD = restDistance * minFactor, maxD = restDistance * maxFactor;
            if (dist < epsilon)
            {
                return headPos - minD * playerUp;
            }

            Vector3 dir = headToHips / dist;
            float upDot = Vector3.Dot(dir, playerUp);
            if (upDot > 0f)
            {
                Vector3 horiz = dir - playerUp * upDot;
                dir = horiz.sqrMagnitude > sqrEpsilon ? horiz.normalized : -playerUp;
            }

            return headPos + dir * Mathf.Clamp(dist, minD, maxD);
        }
        public static Vector3 EnforceSpineBendLimit(Vector3 headPos, Vector3 hipsPos, float maxBendDeg, Vector3 playerUp)
        {
            if (maxBendDeg <= 0f)
            {
                return hipsPos;
            }

            Vector3 diff = hipsPos - headPos;
            if (diff.sqrMagnitude < minMag)
            {
                return hipsPos;
            }

            Vector3 up = playerUp;
            float down = Vector3.Dot(diff, -up);
            Vector3 lateral = diff + up * down;
            float lateralLen = lateral.magnitude, coneTan = Mathf.Tan(Mathf.Min(maxBendDeg, 89.9f) * Mathf.Deg2Rad);
            float minDown = lateralLen / Mathf.Max(coneTan, minMag);
            if (down >= minDown)
            {
                return hipsPos;
            }

            return headPos - up * minDown + lateral;
        }
        public static Vector3 AntiContortionist(Vector3 headPos, Quaternion headRot, Vector3 hipsPos, Quaternion hipsRot, float restDistance)
        {
            Vector3 headFwd = headRot * Vector3.forward, hipsFwd = hipsRot * Vector3.forward;
            float facingSimilarity = Vector3.Dot(headFwd, hipsFwd);
            float minDistFactor = Mathf.Lerp(0.2f, 0.85f, Mathf.Clamp01((facingSimilarity + 1f) * 0.5f));
            float minDist = restDistance * minDistFactor;
            Vector3 diff = hipsPos - headPos;
            float currentDist = diff.magnitude;

            if (currentDist < minDist && currentDist > epsilon)
            {
                return headPos + diff * (minDist / currentDist);
            }
            return hipsPos;
        }
        public static Vector3 MitigateSpineBuckling(Vector3 headPos, Quaternion hipsRot, Vector3 hipsPos, float restDistance, Vector3 playerUp)
        {
            Vector3 diff = hipsPos - headPos;
            float currentDist = diff.magnitude;

            if (currentDist >= restDistance || currentDist < epsilon)
                return hipsPos;

            Vector3 hipsUp = hipsRot * Vector3.up, spineDir = (headPos - hipsPos).normalized;
            float tension = Mathf.Clamp01(Vector3.Dot(hipsUp, spineDir));
            float compression = 1f - (currentDist / restDistance);
            float pushAmount = compression * tension * restDistance * 0.5f;
            return hipsPos - playerUp * pushAmount;
        }
    }
}
