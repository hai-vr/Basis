using UnityEngine;
using static RalivIKSpine;
namespace Basis.IK
{
    public partial struct BasisEerieMovement
    {
        public void SolveSpinePass()
        {
            BasisEerieMarkers.SpineHipsPlacement.Begin();

            if (plan.prone)
            {
                if (plan.hasHips && plan.hasHead)
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
            }
            else
            {
                if (!plan.hasSpineChain)
                {
                    return;
                }
                for (int Index = chainHeadToSpine.Length - 1; Index >= 0; Index--)
                {
                    poseStream.ResetToRest(chainHeadToSpine[Index]);
                }
                Vector3 headTargetPos = targetPositionHead, hipsTargetPos = targetPositionHips;
                Quaternion hipsTargetRot = targetRotationHips;
                Quaternion offsetHips = offsetRotationHips, hipDesired = hipsTargetRot * offsetHips;
                Vector3 up = playerUp;
                float crouchFade = 1f;
                if (plan.crouchOffset)
                {
                    ApplyCrouchBodyOffset(headTargetPos, ref hipsTargetPos, ref hipDesired, up, crouchFade);
                }
                targetPositionHips = hipsTargetPos;
                if (plan.hasHips)
                {
                    poseStream.SetPosition(handleHips, hipsTargetPos);
                    poseStream.SetRotation(handleHips, hipDesired);
                }
            }
            BasisEerieMarkers.SpineHipsPlacement.End();
            if (plan.hasSpineChain && (plan.chestChain || plan.headChain))
            {
                BasisEerieMarkers.SpineSequentialIK.Begin();
                SolveRalivSpineIK(targetPositionHead, targetRotationHead * offsetRotationHead);
                BasisEerieMarkers.SpineSequentialIK.End();
            }
        }
        void SolveRalivSpineIK(Vector3 headTargetPosition, Quaternion headTargetRotation)
        {
            int count = chainHeadToSpine.Length;
            if (count < 2)
            {
                return;
            }
            if (SpineData.length <= epsilon || SpineData.positions.Length != count)
            {
                InitalizeRalivSpineIK();
            }
            if (SpineData.length <= epsilon || SpineData.positions.Length != count)
            {
                return;
            }
            SpineData.hipTargetPosition = plan.hasHips ? poseStream.GetPosition(handleHips) : SpineData.positions[0];
            SpineData.hipTargetRotation = plan.hasHips ? poseStream.GetRotation(handleHips) : SpineData.rotations[0];
            SpineData.headTargetPosition = headTargetPosition;
            SpineData.headTargetRotation = headTargetRotation;
            int chestIndex = count - 1 - plan.chestIdx;
            bool hasTrackedChest = plan.chestTracked && plan.chestChain && chestIndex > 0 && chestIndex < count - 1;
            SpineData.chestIndex = chestIndex;
            SpineData.chestTargetPosition = targetPositionChest;
            SpineData.chestTargetRotation = targetRotationChest * offsetRotationChest;
            SpineData.chestForward = Vector3.forward;
            SpineData.chestHintWeight = hasTrackedChest ? 1f : 0f;
            Vector3 hipGoal = SpineData.hipTargetPosition;
            RalivIKSpine.SolveSpine(ref SpineData);
            if (plan.plantFeet && plan.hasHips)
            {
                SettleHipsOnPlantedFeet(hipGoal, count);
            }
            BasisBoneHandle rootHandle = chainHeadToSpine[count - 1];
            poseStream.SetPosition(rootHandle, SpineData.positions[0]);
            for (int index = 0; index < count; index++)
            {
                BasisBoneHandle handle = chainHeadToSpine[count - 1 - index];
                poseStream.SetRotation(handle, SpineData.rotations[index]);
            }

            BasisBoneHandle headHandle = chainHeadToSpine[0];
            Vector3 headCorrection = SpineData.positions[count - 1] - poseStream.GetPosition(headHandle);
            if (headCorrection.sqrMagnitude > sqrEpsilon)
            {
                poseStream.SetPosition(rootHandle, poseStream.GetPosition(rootHandle) + headCorrection);
            }
        }
        const int PlantPasses = 3;
        const float ReachFadeStart = 0.8f, ReachFadeEnd = 0.95f, LoweredHeadSlipBand = 0.1f;
        void SettleHipsOnPlantedFeet(Vector3 hipGoal, int count)
        {
            Vector3 up = playerUp;
            float goalUp = Vector3.Dot(hipGoal, up), startUp = Vector3.Dot(SpineData.positions[0], up), curlLift = startUp - goalUp;
            float turn = Quaternion.Angle(SpineData.hipTargetRotation * Quaternion.Inverse(SpineData.restRotations[0]), SpineData.headTargetRotation * Quaternion.Inverse(SpineData.restRotations[count - 1])) * Mathf.Deg2Rad;
            float allowance = 2f * Mathf.Sin(0.5f * turn) * headSlipLever, headSlip = float.NaN;
            for (int pass = 0; pass < PlantPasses; pass++)
            {
                float hipsUp = Vector3.Dot(SpineData.positions[0], up), ceiling = PlantedFeetCeiling(up, startUp, out float reachable);
                if (float.IsInfinity(ceiling))
                {
                    return;
                }
                float floor = plan.hipsTracked ? float.NegativeInfinity : Mathf.Min(goalUp, reachable);
                float drop = hipsUp - Mathf.Clamp(hipsUp, floor, ceiling);
                if (!(Mathf.Abs(drop) > epsilon))
                {
                    return;
                }
                if (drop > 0f && float.IsNaN(headSlip))
                {
                    bool trackedFoot = (plan.leftLeg.solve && plan.leftLeg.target == BasisEerieSource.Tracker) || (plan.rightLeg.solve && plan.rightLeg.target == BasisEerieSource.Tracker);
                    headSlip = trackedFoot ? allowance : allowance * Mathf.Max(Mathf.Clamp01(curlLift / drop), Mathf.SmoothStep(0f, 1f, crouchDepth / Mathf.Max(LoweredHeadSlipBand * headSlipLever, epsilon)));
                }
                RalivIKSpine.LowerRoot(ref SpineData, up, drop, float.IsNaN(headSlip) ? 0f : headSlip);
            }
        }
        float PlantedFeetCeiling(Vector3 up, float releaseUp, out float reachable)
        {
            Vector3 hips = SpineData.positions[0];
            Quaternion solvedFromTarget = SpineData.rotations[0] * Quaternion.Inverse(poseStream.GetRotation(handleHips));
            float left = LegHipsCeiling(in plan.leftLeg, simLeftPlanted, handleLeftUpperLeg, handleLeftLowerLeg, handleLeftFoot, targetPositionLeftLowerLeg, hips, solvedFromTarget, up, out float leftRelease);
            float right = LegHipsCeiling(in plan.rightLeg, simRightPlanted, handleRightUpperLeg, handleRightLowerLeg, handleRightFoot, targetPositionRightLowerLeg, hips, solvedFromTarget, up, out float rightRelease);
            reachable = Mathf.Min(left, right);
            return Mathf.Min(left + Mathf.Max(0f, releaseUp - left) * leftRelease, right + Mathf.Max(0f, releaseUp - right) * rightRelease);
        }
        float LegHipsCeiling(in BasisEerieLegPlan leg, bool simPlanted, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 target, Vector3 hips, Quaternion solvedFromTarget, Vector3 up, out float release)
        {
            release = 1f;
            if (!leg.solve || (leg.target == BasisEerieSource.Sim && !simPlanted))
            {
                return float.PositiveInfinity;
            }
            Vector3 hipJoint = poseStream.GetPosition(root), knee = poseStream.GetPosition(mid);
            float reach = BasisLegSolveCore.MaxExtensionReach(Vector3.Distance(hipJoint, knee), Vector3.Distance(knee, poseStream.GetPosition(tip)));
            Vector3 offset = solvedFromTarget * (hipJoint - poseStream.GetPosition(handleHips));
            Vector3 toJoint = hips + offset - target;
            float along = Vector3.Dot(toJoint, up), horizontal = (toJoint - up * along).magnitude, spread = horizontal / Mathf.Max(reach, epsilon);
            if (!(spread < ReachFadeEnd))
            {
                return float.PositiveInfinity;
            }
            release = Mathf.SmoothStep(0f, 1f, (spread - ReachFadeStart) / (ReachFadeEnd - ReachFadeStart));
            return Vector3.Dot(target, up) + Mathf.Sqrt(reach * reach - horizontal * horizontal) - Vector3.Dot(offset, up);
        }
        public void InitalizeRalivSpineIK()
        {
            int count = chainHeadToSpine.Length;
            if (count < 2 || !poseStream.LocalPosition.IsCreated)
            {
                return;
            }
            SpineData = default;
            SpineData.positions.Length = count;
            SpineData.rotations.Length = count;
            SpineData.restPositions.Length = count;
            SpineData.restRotations.Length = count;
            SpineData.t.Length = count;
            SpineData.targetSpinePositions.Length = count;
            SpineData.hipTargetAlignedSpinePositions.Length = count;
            SpineData.headTargetAlignedSpinePositions.Length = count;

            float hiplessSpineLength = 0f;
            for (int index = 0; index < count; index++)
            {
                BasisBoneHandle handle = chainHeadToSpine[count - 1 - index];
                if (!poseStream.IsValid(handle))
                {
                    SpineData = default;
                    return;
                }
                poseStream.GetPositionAndRotation(handle, out Vector3 position, out Quaternion rotation);
                SpineData.positions[index] = position;
                SpineData.rotations[index] = rotation;
                SpineData.restPositions[index] = position;
                SpineData.restRotations[index] = rotation;

                // Match SpineTest: do not include the hips-to-first-spine-bone
                // segment in the interpolation length.
                if (index > 1)
                {
                    hiplessSpineLength += (SpineData.restPositions[index] - SpineData.restPositions[index - 1]).magnitude;
                }
                SpineData.t[index] = hiplessSpineLength;
            }

            if (hiplessSpineLength <= epsilon)
            {
                SpineData = default;
                return;
            }
            SpineData.length = hiplessSpineLength;
            for (int index = 0; index < count; index++)
            {
                SpineData.t[index] = SpineData.t[index] / hiplessSpineLength * 0.8f;
            }

            SpineData.hipTargetPosition = SpineData.positions[0];
            SpineData.hipTargetRotation = SpineData.rotations[0];
            SpineData.headTargetPosition = SpineData.positions[count - 1];
            SpineData.headTargetRotation = SpineData.rotations[count - 1];
            SpineData.chestTargetPosition = SpineData.positions[Mathf.Clamp(count - 1 - chainChestIdx, 0, count - 1)];
            SpineData.chestForward = Vector3.forward;
        }

        void RescaleRalivSpineIK(float scale)
        {
            int count = SpineData.restPositions.Length;
            if (count >= 2 && SpineData.positions.Length == count && scale > 0f && !float.IsNaN(scale) && !float.IsInfinity(scale))
            {
                Vector3 restRoot = SpineData.restPositions[0];
                Vector3 workingRoot = SpineData.positions[0];
                for (int index = 1; index < count; index++)
                {
                    SpineData.restPositions[index] = restRoot + (SpineData.restPositions[index] - restRoot) * scale;
                    SpineData.positions[index] = workingRoot + (SpineData.positions[index] - workingRoot) * scale;
                }
                SpineData.length *= scale;
            }
        }
        void RescaleRalivSpineSegments(float scale, int topIndex)
        {
            int count = SpineData.restPositions.Length;
            if (count < 2 || SpineData.positions.Length != count || SpineData.t.Length != count || topIndex < 1 || !(scale > 0f) || float.IsInfinity(scale))
            {
                return;
            }
            topIndex = Mathf.Min(topIndex, count - 1);
            Vector3 previousRest = SpineData.restPositions[0], previousWorking = SpineData.positions[0];
            for (int index = 1; index < count; index++)
            {
                Vector3 rest = SpineData.restPositions[index], working = SpineData.positions[index];
                float segmentScale = index <= topIndex ? scale : 1f;
                SpineData.restPositions[index] = SpineData.restPositions[index - 1] + (rest - previousRest) * segmentScale;
                SpineData.positions[index] = SpineData.positions[index - 1] + (working - previousWorking) * segmentScale;
                previousRest = rest;
                previousWorking = working;
            }
            float hipless = 0f;
            for (int index = 0; index < count; index++)
            {
                if (index > 1)
                {
                    hipless += (SpineData.restPositions[index] - SpineData.restPositions[index - 1]).magnitude;
                }
                SpineData.t[index] = hipless;
            }
            if (hipless <= epsilon)
            {
                return;
            }
            SpineData.length = hipless;
            for (int index = 0; index < count; index++)
            {
                SpineData.t[index] = SpineData.t[index] / hipless * 0.8f;
            }
        }
        public float RalivRestChainLength()
        {
            int count = SpineData.restPositions.Length;
            float length = 0f;
            for (int index = 1; index < count; index++)
            {
                length += (SpineData.restPositions[index] - SpineData.restPositions[index - 1]).magnitude;
            }
            return count >= 2 && !float.IsNaN(length) && !float.IsInfinity(length) ? length : 0f;
        }
        void ApplyCrouchBodyOffset(Vector3 headTargetPos, ref Vector3 hipsPos, ref Quaternion hipsRot, Vector3 playerUpDir, float fade)
        {
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
            hipsPos = result.HipsPos;
            hipsRot = result.HipsRot;
        }
    }
}
