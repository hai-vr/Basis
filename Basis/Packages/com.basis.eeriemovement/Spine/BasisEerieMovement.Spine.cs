using System.Runtime.CompilerServices;
using Basis.Scripts.Common;
using Unity.Collections;
using UnityEngine;
namespace Basis.IK
{
    public partial struct BasisEerieMovement
    {
        static readonly Unity.Profiling.ProfilerMarker sMarkerSpineHips = new Unity.Profiling.ProfilerMarker("BasisEerie.Spine.HipsPlacement");
        static readonly Unity.Profiling.ProfilerMarker sMarkerSpineChainPrep = new Unity.Profiling.ProfilerMarker("BasisEerie.Spine.ChainPrep");
        static readonly Unity.Profiling.ProfilerMarker sMarkerSpineSequential = new Unity.Profiling.ProfilerMarker("BasisEerie.Spine.SequentialIK");
        static readonly Unity.Profiling.ProfilerMarker sMarkerSpineLordosis = new Unity.Profiling.ProfilerMarker("BasisEerie.Spine.Lordosis");

        void SolveSpinePass(BasisPoseStream stream)
        {
            SolveSpine(stream);
            if (anatCervicalLordosis)
            {
                sMarkerSpineLordosis.Begin();
                ApplyCervicalLordosis(stream);
                sMarkerSpineLordosis.End();
            }
        }

        public void SolveSpine(BasisPoseStream stream)
        {
            if (!enabledSpineIK)
            {
                return;
            }
            sMarkerSpineHips.Begin();

            Quaternion chestDesired = targetRotationChest * targetOffsetChest;

            // Prone: the locomotion animation owns the pelvis. Every placement stage below models an
            // upright body under the head (lock-mode restore, hips-under-head clamp, counterbalance,
            // hip hinge, crouch sit-back) and would fold a lying pose back under the camera, so the
            // hips stage stands down to a single yaw follow; the chain solve below still pins the
            // head to the gaze.
            if (proneBodyPose)
            {
                ApplyProneBodyYaw(stream);
            }
            else
            {
                Vector3 headTargetPos = targetPositionHead;
                Vector3 hipsTargetPos = targetPositionHips;

                Quaternion headTargetRot = targetRotationHead;
                Quaternion hipsTargetRot = targetRotationHips;
                Quaternion offsetHips = offsetRotationHips;

                Quaternion hipDesired = hipsTargetRot * offsetHips;

                float restDist = minHeadSpineHeight;
                BasisIKLockMode lockMode = ikLockMode;
                Vector3 up = playerUp;

                switch (lockMode)
                {
                    case BasisIKLockMode.LockHips:
                        break;

                    case BasisIKLockMode.LockHead:
                        {
                            Vector3 headToHips = hipsTargetPos - headTargetPos;
                            float spineLen = headToHips.magnitude;
                            if (spineLen < restDist)
                            {
                                Vector3 spineDir = spineLen > k_Epsilon ? headToHips / spineLen : hipsTargetRot * Vector3.down;
                                hipsTargetPos = headTargetPos + spineDir * restDist;
                            }

                            if (!hasHipsTracker)
                            {
                                hipsTargetPos = ClampHipsUnderHead(headTargetPos, hipsTargetPos, restDist * HipsUnderHeadMaxLeanFrac, up);
                            }
                        }
                        break;

                    default:
                        hipsTargetPos = AntiContortionist(headTargetPos, headTargetRot, hipsTargetPos, hipsTargetRot, restDist);
                        hipsTargetPos = MitigateSpineBuckling(headTargetPos, hipsTargetRot, hipsTargetPos, restDist, up);
                        float MaxBendDeg = maxBendDeg;
                        hipsTargetPos = EnforceSpineBendLimit(headTargetPos, hipsTargetPos, MaxBendDeg, up);
                        hipsTargetPos = ClampHipsAroundHead(headTargetPos, hipsTargetPos, restDist, minFactor, maxFactor, up);
                        break;
                }
                Vector3 neckCue = ComputeNeckCue(headTargetPos);
                float crouchFade = 1f;
                if (!hasHipsTracker)
                {
                    BasisTrunkCounterbalanceCore.Solve(hipsTargetPos, neckCue, up, trunkCounterbalance,
                        trunkCounterbalanceMaxSpineFrac * minHeadSpineHeight, out hipsTargetPos, out float flexionFrac, out _);
                    crouchFade = 1f - flexionFrac;
                }
                hipsTargetPos = ApplyCrouchBodyOffset(stream, headTargetPos, hipsTargetPos, hipDesired, up, crouchFade);
                targetPositionHips = hipsTargetPos;
                if (!hasHipsTracker)
                {
                    BasisHipHingeCore.Solve(neckCue, hipsTargetPos, hipDesired, up, hipHingeStartDeg, hipHingeMaxAddDeg,
                        out hipDesired, out _, out _);
                }

                if (handleHips.IsValid(stream))
                {
                    handleHips.SetPosition(stream, hipsTargetPos);
                    handleHips.SetRotation(stream, hipDesired);
                }
            }
            sMarkerSpineHips.End();
            if (hasChestTracker && handleChest.IsValid(stream))
            {
                sMarkerSpineChainPrep.Begin();

                float Value = maxChestDeltaDeg;

                Quaternion clampedChestRot = chestDesired;
                if (handleNeck.IsValid(stream))
                {
                    clampedChestRot = ClampRotation(clampedChestRot, handleNeck.GetRotation(stream), Value);
                }
                if (handleSpine.IsValid(stream))
                {
                    clampedChestRot = ClampRotation(clampedChestRot, handleSpine.GetRotation(stream), Value);
                }

                handleChest.SetRotation(stream, clampedChestRot);

                Vector3 headPos = targetPositionHead;
                Quaternion headRot = targetRotationHead;

                DistributeSpineBend(stream, headPos);
                BiasSpineTowardChest(stream);
                GuardSpineChain(stream);
                sMarkerSpineChainPrep.End();
                sMarkerSpineSequential.Begin();
                SolveSequentialSpineIK(stream, headPos, headRot);
                sMarkerSpineSequential.End();
            }
            else if (handleHead.IsValid(stream))
            {
                Vector3 headPos = targetPositionHead;
                Quaternion headRot = targetRotationHead;

                sMarkerSpineChainPrep.Begin();
                DistributeSpineBend(stream, headPos);
                ApplyArmSwingChestFollow(stream);
                GuardSpineChain(stream);
                sMarkerSpineChainPrep.End();
                sMarkerSpineSequential.Begin();
                SolveSequentialSpineIK(stream, headPos, headRot);
                sMarkerSpineSequential.End();
            }
        }
        void ApplyProneBodyYaw(BasisPoseStream stream)
        {
            if (!handleHips.IsValid(stream) || !handleHead.IsValid(stream))
            {
                return;
            }

            Vector3 up = playerUp;
            Vector3 hipsPos = handleHips.GetPosition(stream);
            Vector3 headPos = handleHead.GetPosition(stream);

            Vector3 bodyFwd = headPos - hipsPos;
            bodyFwd -= up * Vector3.Dot(bodyFwd, up);
            Vector3 desiredFwd = targetRotationHips * Vector3.forward;
            desiredFwd -= up * Vector3.Dot(desiredFwd, up);
            if (bodyFwd.sqrMagnitude < k_SqrEpsilon || desiredFwd.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            float deltaYaw = Vector3.SignedAngle(bodyFwd, desiredFwd, up);
            Quaternion swing = Quaternion.AngleAxis(deltaYaw, up);
            Vector3 toTarget = targetPositionHead - headPos;
            toTarget -= up * Vector3.Dot(toTarget, up);
            handleHips.SetPosition(stream, headPos + swing * (hipsPos - headPos) + toTarget);
            handleHips.SetRotation(stream, swing * handleHips.GetRotation(stream));
        }

        public void SolveSequentialSpineIK(BasisPoseStream stream, Vector3 headTargetPos, Quaternion headTargetRot)
        {
            if (!chainHeadToSpine.IsCreated || chainHeadToSpine.Length < 3)
                return;

            int chainLen = chainHeadToSpine.Length;
            const int tipIdx = 0;
            const int firstJoint = 1;
            int lastJoint = chainLen - 2;

            for (int i = 0; i < chainLen; i++)
            {
                if (!chainHeadToSpine[i].IsValid(stream))
                    return;
            }

            int maxIters = Mathf.Max(1, spineMaxIterations);
            float tolerance = Mathf.Max(0f, spineTolerance);
            float tolSqr = tolerance * tolerance;
            {
                Vector3 rootPos = chainHeadToSpine[chainLen - 1].GetPosition(stream);
                float chainReach = 0f;
                for (int i = 0; i < chainLen - 1; i++)
                {
                    chainReach += (chainHeadToSpine[i].GetPosition(stream) - chainHeadToSpine[i + 1].GetPosition(stream)).magnitude;
                }
                Vector3 rootToTarget = headTargetPos - rootPos;
                float targetDist = rootToTarget.magnitude;
                if (targetDist > k_Epsilon && chainReach > k_Epsilon)
                {
                    float compression = chainReach - targetDist;
                    float commandedDist;
                    if (compression > 0f)
                    {
                        float band = spineTautBandFrac * chainReach;
                        float denom = compression * compression + band * band;
                        commandedDist = denom > 0f
                            ? chainReach - compression * compression * compression / denom
                            : targetDist;
                    }
                    else
                    {
                        commandedDist = chainReach;
                    }
                    headTargetPos = rootPos + rootToTarget * (commandedDist / targetDist);
                }
            }

            Quaternion hipsTwistRot = handleHips.IsValid(stream) ? handleHips.GetRotation(stream) : Quaternion.identity;
            float hipsBindMagSq = offsetRotationHips.x * offsetRotationHips.x + offsetRotationHips.y * offsetRotationHips.y
                + offsetRotationHips.z * offsetRotationHips.z + offsetRotationHips.w * offsetRotationHips.w;
            if (hipsBindMagSq > k_SqrEpsilon)
            {
                hipsTwistRot *= Quaternion.Inverse(offsetRotationHips);
            }
            Vector3 ccdUp = hipsTwistRot * Vector3.up;
            if (ccdUp.sqrMagnitude < k_SqrEpsilon) ccdUp = playerUp;
            float jointSpan = Mathf.Max(1, lastJoint - firstJoint);
            int chestIdx = chainChestIdx != 0 ? chainChestIdx : (chainLen >= 5 ? chainLen - 3 : -1);
            Quaternion finalHeadRot = headTargetRot * targetOffsetHead;

            for (int iter = 0; iter < maxIters; iter++)
            {
                Vector3 tipPos = chainHeadToSpine[tipIdx].GetPosition(stream);
                if ((headTargetPos - tipPos).sqrMagnitude < tolSqr)
                    break;

                for (int i = lastJoint; i >= firstJoint; i--)
                {
                    ReachHeadJoint(stream, i, headTargetPos, firstJoint, chestIdx, jointSpan, ccdUp);
                }
            }

            SolveChestTarget(stream, headTargetPos, firstJoint, lastJoint, chestIdx, jointSpan, ccdUp, tolSqr);

            chainHeadToSpine[tipIdx].SetRotation(stream, finalHeadRot);
        }

        void ReachHeadJoint(BasisPoseStream stream, int i, Vector3 headTargetPos, int firstJoint, int chestIdx,
            float jointSpan, Vector3 ccdUp)
        {
            const int tipIdx = 0;
            Vector3 jointPos = chainHeadToSpine[i].GetPosition(stream);
            Vector3 curTipPos = chainHeadToSpine[tipIdx].GetPosition(stream);

            Vector3 cur = curTipPos - jointPos;
            Vector3 tgt = headTargetPos - jointPos;
            if (cur.sqrMagnitude < k_SqrEpsilon || tgt.sqrMagnitude < k_SqrEpsilon)
                return;

            Quaternion delta = BasisQuaternionExt.FromToRotation(cur, tgt);
            float t = (i - firstJoint) / jointSpan;
            float jointTwistKeep = Mathf.Lerp(spineNeckTwistKeep, spineTwistKeep, t);
            float jointSwingScale = 1f - thoracicBendStiffen * (1f - Mathf.Abs(2f * t - 1f));
            delta = BasisTwistSolveCore.ShapeReachStep(delta, ccdUp, jointTwistKeep, jointSwingScale);
            delta = Quaternion.Slerp(Quaternion.identity, delta, spineCCDRelax);
            chainHeadToSpine[i].SetRotation(stream, delta * chainHeadToSpine[i].GetRotation(stream));

            if (i == firstJoint)
            {
                ClampNeckCone(stream, i, neckMaxConeDeg);
            }
            else if (i == chestIdx)
            {
                ClampChestCone(stream, i, maxChestDeltaDeg);
            }

            GuardSpineJoint(stream, i);
        }
        void SolveChestTarget(BasisPoseStream stream, Vector3 headTargetPos, int firstJoint, int lastJoint,
            int chestBoneIdx, float jointSpan, Vector3 ccdUp, float tolSqr)
        {
            if (!chestIkTarget)
                return;

            if (chestBoneIdx < firstJoint || lastJoint <= firstJoint || lastJoint <= chestBoneIdx)
                return;

            Vector3 chestTargetPos = targetPositionChestRaw;
            Vector3 chestBonePos = chainHeadToSpine[chestBoneIdx].GetPosition(stream);

            if ((chestTargetPos - chestBonePos).sqrMagnitude > (chestPullMaxDist * chestPullMaxDist))
                return;

            float spineT = (lastJoint - firstJoint) / jointSpan;
            float chestTwistKeep = Mathf.Lerp(spineNeckTwistKeep, spineTwistKeep, spineT);
            float spineSwingScale = 1f - thoracicBendStiffen * (1f - Mathf.Abs(2f * spineT - 1f));

            for (int citer = 0; citer < chestIkIterations; citer++)
            {
                Vector3 spinePos = chainHeadToSpine[lastJoint].GetPosition(stream);
                Vector3 chestNow = chainHeadToSpine[chestBoneIdx].GetPosition(stream);

                if ((chestTargetPos - chestNow).sqrMagnitude < tolSqr
                    && (headTargetPos - chainHeadToSpine[0].GetPosition(stream)).sqrMagnitude < tolSqr)
                {
                    break;
                }

                Vector3 cCur = chestNow - spinePos;
                Vector3 cTgt = chestTargetPos - spinePos;
                if (cCur.sqrMagnitude > k_SqrEpsilon && cTgt.sqrMagnitude > k_SqrEpsilon)
                {
                    Quaternion cDelta = BasisQuaternionExt.FromToRotation(cCur, cTgt);
                    cDelta = BasisTwistSolveCore.ShapeReachStep(cDelta, ccdUp, chestTwistKeep, spineSwingScale);

                    cDelta = Quaternion.Slerp(Quaternion.identity, cDelta, spineCCDRelax * chestIkWeight);
                    chainHeadToSpine[lastJoint].SetRotation(stream, cDelta * chainHeadToSpine[lastJoint].GetRotation(stream));
                    GuardSpineJoint(stream, lastJoint);
                }

                for (int sweep = 0; sweep < chestIkHeadRestoreSweeps; sweep++)
                {
                    for (int i = lastJoint - 1; i >= firstJoint; i--)
                    {
                        ReachHeadJoint(stream, i, headTargetPos, firstJoint, chestBoneIdx, jointSpan, ccdUp);
                    }
                }
            }
        }

        void GuardSpineJoint(BasisPoseStream stream, int i)
        {
            if (!spineAnatomicalRom)
            {
                return;
            }
            if (!chainSpineRestFrames.IsCreated || i < 0 || i >= chainSpineRestFrames.Length)
            {
                return;
            }
            if (!chainSpineRoms.IsCreated || i >= chainSpineRoms.Length)
            {
                return;
            }

            BasisSpineRestFrame frame = chainSpineRestFrames[i];
            if (!frame.Valid)
            {
                return;
            }

            int parent = i + 1;
            if (parent >= chainHeadToSpine.Length || !chainHeadToSpine[parent].IsValid(stream) || !chainHeadToSpine[i].IsValid(stream))
            {
                return;
            }

            Quaternion parentRot = chainHeadToSpine[parent].GetRotation(stream);
            Quaternion boneRot = chainHeadToSpine[i].GetRotation(stream);
            Quaternion local = BasisSpineAnatomyCore.Conj(parentRot) * boneRot;

            Quaternion clamped = BasisSpineAnatomyCore.Clamp(local, frame, chainSpineRoms[i], out BasisSpineClampInfo info);
            if (!info.Touched)
            {
                return;
            }

            chainHeadToSpine[i].SetRotation(stream, parentRot * clamped);
        }

        void GuardSpineChain(BasisPoseStream stream)
        {
            if (!chainHeadToSpine.IsCreated || chainHeadToSpine.Length < 3)
            {
                return;
            }
            for (int i = 1; i <= chainHeadToSpine.Length - 2; i++)
            {
                GuardSpineJoint(stream, i);
            }
        }

        void ClampNeckCone(BasisPoseStream stream, int neckIdx, float maxConeDeg)
        {
            Vector3 chestPos = chainHeadToSpine[neckIdx + 1].GetPosition(stream);
            Vector3 neckPos = chainHeadToSpine[neckIdx].GetPosition(stream);
            Vector3 headPos = chainHeadToSpine[0].GetPosition(stream);

            Vector3 parentDir = neckPos - chestPos;
            Vector3 boneDir = headPos - neckPos;
            if (parentDir.sqrMagnitude < k_SqrEpsilon || boneDir.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            float ang = Vector3.Angle(parentDir, boneDir);
            if (ang <= maxConeDeg)
            {
                return;
            }

            Vector3 axis = Vector3.Cross(boneDir, parentDir);
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            axis.Normalize();
            Quaternion correction = Quaternion.AngleAxis(ang - maxConeDeg, axis);
            chainHeadToSpine[neckIdx].SetRotation(stream, correction * chainHeadToSpine[neckIdx].GetRotation(stream));
        }
        void ClampChestCone(BasisPoseStream stream, int chestIdx, float maxConeDeg)
        {
            Vector3 spinePos = chainHeadToSpine[chestIdx + 1].GetPosition(stream);
            Vector3 chestPos = chainHeadToSpine[chestIdx].GetPosition(stream);
            Vector3 childPos = chainHeadToSpine[chestIdx - 1].GetPosition(stream);

            Vector3 parentDir = chestPos - spinePos;
            Vector3 boneDir = childPos - chestPos;
            if (parentDir.sqrMagnitude < k_SqrEpsilon || boneDir.sqrMagnitude < k_SqrEpsilon)
                return;

            float ang = Vector3.Angle(parentDir, boneDir);
            if (ang <= maxConeDeg)
                return;

            Vector3 axis = Vector3.Cross(boneDir, parentDir);
            if (axis.sqrMagnitude < k_SqrEpsilon)
                return;

            axis.Normalize();
            Quaternion correction = Quaternion.AngleAxis(ang - maxConeDeg, axis);
            chainHeadToSpine[chestIdx].SetRotation(stream, correction * chainHeadToSpine[chestIdx].GetRotation(stream));
        }
        void BiasSpineTowardChest(BasisPoseStream stream)
        {
            if (!handleSpine.IsValid(stream) || !handleChest.IsValid(stream))
                return;

            Vector3 chestTargetPos = targetPositionChest;
            Vector3 spinePos = handleSpine.GetPosition(stream);
            Vector3 chestPos = handleChest.GetPosition(stream);

            if ((chestTargetPos - chestPos).sqrMagnitude > (chestPullMaxDist * chestPullMaxDist))
                return;

            Vector3 cur = chestPos - spinePos;
            Vector3 tgt = chestTargetPos - spinePos;
            if (cur.sqrMagnitude < k_SqrEpsilon || tgt.sqrMagnitude < k_SqrEpsilon)
                return;

            Quaternion pull = ClampRotation(BasisQuaternionExt.FromToRotation(cur, tgt), Quaternion.identity, chestPosPullMaxDeg);
            handleSpine.SetRotation(stream, pull * handleSpine.GetRotation(stream));
        }

        Vector3 ComputeNeckCue(Vector3 headTargetPos)
        {
            return BasisNeckCueCore.Solve(headTargetPos, targetRotationHead * targetOffsetHead,
                tposeHeadToNeckLocal, playerUp, neckExtensionDamp, neckFlexionDamp);
        }

        public void DistributeSpineBend(BasisPoseStream stream, Vector3 headTargetPos)
        {
            if (!handleHips.IsValid(stream) || !handleChest.IsValid(stream))
            {
                return;
            }

            bool hasSpine = handleSpine.IsValid(stream);
            bool hasUpper = handleUpperChest.IsValid(stream);
            if (!hasSpine && !hasUpper)
            {
                return;
            }

            Quaternion hipsRot = handleHips.GetRotation(stream);

            Vector3 neckCue = ComputeNeckCue(headTargetPos);

            Vector3 spineCue = Vector3.Lerp(neckCue, headTargetPos, Mathf.Clamp01(spineGazeFollow));

            Quaternion hipsBind = offsetRotationHips;

            BasisSpineBendInput input;
            input.HipsRot = hipsRot;
            input.HipsPos = handleHips.GetPosition(stream);
            input.ChestPos = handleChest.GetPosition(stream);
            input.SmoothedHead = ApplyChestSpring(stream, spineCue);
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

            if (hasChestTracker)
            {
                input.SpineBendPitch = 0f;
                input.SpineBendRoll = 0f;
                input.UpperBendPitch = 0f;
                input.UpperBendRoll = 0f;
            }

            BasisSpineBendCore.Solve(input, out BasisSpineBendResult r);
            if (r.EarlyOut)
            {
                return;
            }

            Quaternion hipsAnat = hipsRot * Quaternion.Inverse(hipsBind);
            Quaternion invHipsAnat = Quaternion.Inverse(hipsAnat);
            if (r.WriteSpine)
            {
                Quaternion deltaWorld = hipsAnat * BasisSpineBendCore.Compose(r.SpineEuler) * invHipsAnat;
                handleSpine.SetRotation(stream, deltaWorld * handleSpine.GetRotation(stream));
            }
            if (r.WriteUpper)
            {
                Quaternion deltaWorld = hipsAnat * BasisSpineBendCore.Compose(r.UpperEuler) * invHipsAnat;
                handleUpperChest.SetRotation(stream, deltaWorld * handleUpperChest.GetRotation(stream));
            }
        }

        Vector3 ApplyChestSpring(BasisPoseStream stream, Vector3 headTargetPos)
        {
            if (!chestSpringState.IsCreated || !chestSpringInit.IsCreated)
            {
                return headTargetPos;
            }

            float hz = chestSpringHz;
            if (hz <= 0f)
            {
                chestSpringState[0] = headTargetPos;
                chestSpringState[1] = Vector3.zero;
                chestSpringInit[0] = 1;
                return headTargetPos;
            }
            if (chestSpringInit[0] == 0)
            {
                chestSpringState[0] = headTargetPos;
                chestSpringState[1] = Vector3.zero;
                chestSpringInit[0] = 1;
                return headTargetPos;
            }

            float dt = stream.deltaTime;
            if (dt <= 0f)
                return chestSpringState[0];

            BasisChestSpringCore.Step(chestSpringState[0], chestSpringState[1], headTargetPos, dt, hz,
                chestSpringDamping, out Vector3 newPos, out Vector3 newVel);

            if (!IsFinite(newPos) || !IsFinite(newVel))
            {
                chestSpringState[0] = headTargetPos;
                chestSpringState[1] = Vector3.zero;
                return headTargetPos;
            }

            chestSpringState[0] = newPos;
            chestSpringState[1] = newVel;
            return newPos;
        }
        static bool IsFinite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);

        Vector3 ApplyCrouchBodyOffset(BasisPoseStream stream, Vector3 headTargetPos, Vector3 hipsPos, Quaternion hipsRot, Vector3 playerUpDir, float fade)
        {
            if (hasChestTracker || hasHipsTracker)
            {
                return hipsPos;
            }

            BasisCrouchOffsetInput input;
            input.HeadTargetPos = headTargetPos;
            input.HipsPos = hipsPos;
            input.HipsRot = hipsRot;
            input.Bind = offsetRotationHips;
            input.PlayerUp = playerUpDir;
            input.Factor = moveBodyBackWhenCrouching;
            input.RestDist = minHeadSpineHeight;
            input.CrouchDepth = crouchDepth;
            input.StandingHeadHeight = standingHeadHeight;
            input.Fade = fade;
            BasisCrouchOffsetCore.Solve(input, out BasisCrouchOffsetResult result);
            return result.HipsPos;
        }
        public void ApplyCervicalLordosis(BasisPoseStream stream)
        {
            if (!handleNeck.IsValid(stream))
            {
                return;
            }

            Vector3 referenceUp;
            if (handleChest.IsValid(stream))
            {
                Vector3 chestToNeck = handleNeck.GetPosition(stream) - handleChest.GetPosition(stream);
                referenceUp = chestToNeck.sqrMagnitude > k_SqrEpsilon
                    ? chestToNeck.normalized
                    : handleChest.GetRotation(stream) * Vector3.up;
            }
            else
            {
                Vector3 up = playerUp;
                referenceUp = up.sqrMagnitude < k_SqrEpsilon ? Vector3.up : up.normalized;
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
            input.HasUpperChest = handleUpperChest.IsValid(stream);

            BasisCervicalSolveCore.Solve(input, out BasisCervicalResult result);
            if (result.EarlyOut)
            {
                if (handleHead.IsValid(stream))
                {
                    handleHead.SetPosition(stream, targetPositionHead);
                    handleHead.SetRotation(stream, result.HeadRotClamped * targetOffsetHead);
                }
                return;
            }

            Vector3 shoulderRight = (handleLeftUpperArm.IsValid(stream) && handleRightUpperArm.IsValid(stream))
                ? handleRightUpperArm.GetPosition(stream) - handleLeftUpperArm.GetPosition(stream)
                : Vector3.zero;
            bool hasShoulderRight = shoulderRight.sqrMagnitude > k_SqrEpsilon;
            if (hasShoulderRight)
            {
                shoulderRight.Normalize();
            }

            BasisBoneHandle bendHandle = input.HasUpperChest ? handleUpperChest : handleChest;
            if (bendHandle.IsValid(stream) && result.BhDeg != 0f)
            {
                Quaternion bhRot = bendHandle.GetRotation(stream);
                Vector3 bhAxis = hasShoulderRight ? shoulderRight : bhRot * Vector3.right;
                bendHandle.SetRotation(stream, Quaternion.AngleAxis(result.BhDeg, bhAxis) * bhRot);
            }

            if (result.HasExtreme)
            {
                Quaternion refRot = handleHips.IsValid(stream)
                    ? handleHips.GetRotation(stream) * Quaternion.Inverse(offsetRotationHips)
                    : (handleChest.IsValid(stream) ? handleChest.GetRotation(stream) : Quaternion.identity);
                Vector3 refForward = refRot * Vector3.forward;
                Vector3 refDown = -(refRot * Vector3.up);

                if (handleHips.IsValid(stream))
                {
                    Vector3 hipsOffset = refForward * result.HipsForwardAmount + refDown * result.HipsDownAmount;
                    handleHips.SetPosition(stream, handleHips.GetPosition(stream) + hipsOffset);
                }

                if (handleChest.IsValid(stream))
                {
                    Vector3 chestOffset = refForward * result.ChestForwardAmount + refDown * result.ChestDownAmount;
                    handleChest.SetPosition(stream, handleChest.GetPosition(stream) + chestOffset);
                }
            }
            float extraNeckDeg = Mathf.Clamp01(neckGazeFollow) * neckGazeFollowMaxDeg * result.LookDownFrac;
            float totalNeckDeg = result.NeckDeg + extraNeckDeg;
            if (totalNeckDeg != 0f)
            {
                Quaternion neckRotCurrent = handleNeck.GetRotation(stream);
                Vector3 neckAxis = hasShoulderRight ? shoulderRight : neckRotCurrent * Vector3.right;
                handleNeck.SetRotation(stream, Quaternion.AngleAxis(totalNeckDeg, neckAxis) * neckRotCurrent);
            }

            if (handleHead.IsValid(stream))
            {
                handleHead.SetPosition(stream, targetPositionHead);
                handleHead.SetRotation(stream, result.HeadRotClamped * targetOffsetHead);
            }
        }
        public static Vector3 ClampHipsAroundHead(Vector3 headPos, Vector3 hipsPos, float restDistance, float minFactor, float maxFactor, Vector3 playerUp)
        {
            Vector3 headToHips = hipsPos - headPos;
            float dist = headToHips.magnitude;
            float minD = restDistance * minFactor;
            float maxD = restDistance * maxFactor;
            if (dist < k_Epsilon)
            {
                return headPos - minD * playerUp;
            }

            Vector3 dir = headToHips / dist;
            float upDot = Vector3.Dot(dir, playerUp);
            if (upDot > 0f)
            {
                Vector3 horiz = dir - playerUp * upDot;
                dir = horiz.sqrMagnitude > k_SqrEpsilon ? horiz.normalized : -playerUp;
            }

            return headPos + dir * Mathf.Clamp(dist, minD, maxD);
        }

        const float HipsUnderHeadMaxLeanFrac = 1.0f;

        public static Vector3 ClampHipsUnderHead(Vector3 headPos, Vector3 hipsPos, float maxHorizontal, Vector3 playerUp)
        {
            if (maxHorizontal <= 0f)
            {
                return hipsPos;
            }

            Vector3 up = playerUp.sqrMagnitude < k_SqrEpsilon ? Vector3.up : playerUp.normalized;
            Vector3 diff = hipsPos - headPos;
            Vector3 lateral = diff - up * Vector3.Dot(diff, up);
            float lateralLen = lateral.magnitude;
            if (lateralLen <= maxHorizontal || lateralLen < k_Epsilon)
            {
                return hipsPos;
            }

            return hipsPos - lateral * (1f - maxHorizontal / lateralLen);
        }

        public static Vector3 EnforceSpineBendLimit(Vector3 headPos, Vector3 hipsPos, float maxBendDeg, Vector3 playerUp)
        {
            if (maxBendDeg <= 0f)
            {
                return hipsPos;
            }

            Vector3 diff = hipsPos - headPos;
            if (diff.sqrMagnitude < k_MinMag)
            {
                return hipsPos;
            }

            Vector3 up = playerUp;

            float down = Vector3.Dot(diff, -up);
            Vector3 lateral = diff + up * down;
            float lateralLen = lateral.magnitude;
            float coneTan = Mathf.Tan(Mathf.Min(maxBendDeg, 89.9f) * Mathf.Deg2Rad);
            float minDown = lateralLen / Mathf.Max(coneTan, k_MinMag);
            if (down >= minDown)
            {
                return hipsPos;
            }

            return headPos - up * minDown + lateral;
        }
        public static Vector3 AntiContortionist(Vector3 headPos, Quaternion headRot, Vector3 hipsPos, Quaternion hipsRot, float restDistance)
        {
            Vector3 headFwd = headRot * Vector3.forward;
            Vector3 hipsFwd = hipsRot * Vector3.forward;
            float facingSimilarity = Vector3.Dot(headFwd, hipsFwd);

            float minDistFactor = Mathf.Lerp(0.2f, 0.85f, Mathf.Clamp01((facingSimilarity + 1f) * 0.5f));
            float minDist = restDistance * minDistFactor;

            Vector3 diff = hipsPos - headPos;
            float currentDist = diff.magnitude;

            if (currentDist < minDist && currentDist > k_Epsilon)
            {
                return headPos + diff * (minDist / currentDist);
            }
            return hipsPos;
        }
        public static Vector3 MitigateSpineBuckling(Vector3 headPos, Quaternion hipsRot, Vector3 hipsPos, float restDistance, Vector3 playerUp)
        {
            Vector3 diff = hipsPos - headPos;
            float currentDist = diff.magnitude;

            if (currentDist >= restDistance || currentDist < k_Epsilon)
                return hipsPos;

            Vector3 hipsUp = hipsRot * Vector3.up;
            Vector3 spineDir = (headPos - hipsPos).normalized;

            float tension = Mathf.Clamp01(Vector3.Dot(hipsUp, spineDir));
            float compression = 1f - (currentDist / restDistance);

            float pushAmount = compression * tension * restDistance * 0.5f;
            return hipsPos - playerUp * pushAmount;
        }
    }
}
