using UnityEngine;

namespace Basis.IK
{
    /// <summary>
    /// Per-stage visualization of the FBIK solve, recorded top to bottom as the passes run.
    /// Each Record method is a no-op while its stage bit is off, so this file can grow freely:
    /// add a draw here, or a one-line <c>gizmos.*</c> call anywhere inside the solve itself, and
    /// BasisIKSolveGizmos replays it on the main thread with no further wiring.
    /// </summary>
    public partial struct BasisEerieMovement
    {
        const uint k_GizmoTarget = 0xFF00D7FFu;
        const uint k_GizmoHint = 0xFFFFFF00u;
        const uint k_GizmoResidual = 0xFF3030FFu;
        const uint k_GizmoRaw = 0xFF909090u;
        const uint k_GizmoLeft = 0xFF4090FFu;
        const uint k_GizmoRight = 0xFFFF9040u;
        const uint k_GizmoReach = 0x60FFFFFFu;

        static uint SideColor(bool isLeft) => isLeft ? k_GizmoLeft : k_GizmoRight;

        void RecordTargetGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Targets;
            if (!gizmos.Wants(stage))
            {
                return;
            }

            gizmos.Point(stage, targetPositionHead, k_GizmoTarget);
            gizmos.Axes(stage, targetPositionHead, targetRotationHead * targetOffsetHead);
            gizmos.Label(stage, targetPositionHead, "Head target");

            gizmos.Point(stage, targetPositionHips, k_GizmoTarget);
            gizmos.Axes(stage, targetPositionHips, targetRotationHips * offsetRotationHips);
            if (hasHipsTracker)
            {
                gizmos.Label(stage, targetPositionHips, "Hips target (tracked)");
            }
            else
            {
                gizmos.Label(stage, targetPositionHips, "Hips target (derived)");
            }
            gizmos.Line(stage, targetPositionHead, targetPositionHips, k_GizmoTarget);
            gizmos.Direction(stage, targetPositionHips, playerUp, gizmos.AxisLength * 3f, BasisIKGizmoPalette.Green);

            if (hasChestTracker)
            {
                gizmos.Point(stage, targetPositionChest, k_GizmoTarget);
                gizmos.Point(stage, targetPositionChestRaw, k_GizmoRaw);
                gizmos.Line(stage, targetPositionChestRaw, targetPositionChest, k_GizmoRaw);
                gizmos.Axes(stage, targetPositionChest, targetRotationChest * targetOffsetChest);
                gizmos.Label(stage, targetPositionChest, "Chest target");
            }

            RecordHandTarget(stage, enabledLeftHand, targetPositionLeftHand, targetRotationLeftHand * targetOffsetLeftHand, hintPositionLeftHand, hintWeightLeftHand, true);
            RecordHandTarget(stage, enabledRightHand, targetPositionRightHand, targetRotationRightHand * targetOffsetRightHand, hintPositionRightHand, hintWeightRightHand, false);

            RecordFootTarget(stage, enabledLeftLowerLeg, targetPositionLeftLowerLeg, targetRotationLeftLowerLeg * targetOffsetLeftFoot, hintPositionLeftLowerLeg, hintWeightLeftLowerLeg, kneeBendPrefLeft, true);
            RecordFootTarget(stage, enabledRightLowerLeg, targetPositionRightLowerLeg, targetRotationRightLowerLeg * targetOffsetRightFoot, hintPositionRightLowerLeg, hintWeightRightLowerLeg, kneeBendPrefRight, false);
        }

        void RecordHandTarget(BasisIKGizmoStage stage, float enabled, Vector3 target, Quaternion rotation, Vector3 hint, bool hasHint, bool isLeft)
        {
            if (!(enabled > 0f))
            {
                return;
            }
            uint color = SideColor(isLeft);
            gizmos.Point(stage, target, color);
            gizmos.Axes(stage, target, rotation);
            if (isLeft)
            {
                gizmos.Label(stage, target, "L hand target");
            }
            else
            {
                gizmos.Label(stage, target, "R hand target");
            }

            if (!hasHint)
            {
                return;
            }
            gizmos.Point(stage, hint, k_GizmoHint);
            gizmos.Line(stage, target, hint, k_GizmoHint);
            if (isLeft)
            {
                gizmos.Label(stage, hint, "L elbow hint", k_GizmoHint);
            }
            else
            {
                gizmos.Label(stage, hint, "R elbow hint", k_GizmoHint);
            }
        }

        void RecordFootTarget(BasisIKGizmoStage stage, float enabled, Vector3 target, Quaternion rotation, Vector3 hint, float hintWeight, Vector3 bendPref, bool isLeft)
        {
            if (!(enabled > 0f))
            {
                return;
            }
            uint color = SideColor(isLeft);
            gizmos.Point(stage, target, color);
            gizmos.Axes(stage, target, rotation);
            if (isLeft)
            {
                gizmos.Label(stage, target, "L foot target");
            }
            else
            {
                gizmos.Label(stage, target, "R foot target");
            }

            if (!(hintWeight > 0f))
            {
                return;
            }
            gizmos.Point(stage, hint, k_GizmoHint);
            gizmos.Line(stage, target, hint, k_GizmoHint);
            if (isLeft)
            {
                gizmos.Label(stage, hint, "L knee hint", k_GizmoHint);
            }
            else
            {
                gizmos.Label(stage, hint, "R knee hint", k_GizmoHint);
            }
            if (bendPref.sqrMagnitude > k_SqrEpsilon)
            {
                gizmos.Direction(stage, hint, bendPref.normalized, gizmos.AxisLength * 2f, BasisIKGizmoPalette.Magenta);
            }
        }

        void RecordSpineGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Spine;
            if (!gizmos.Wants(stage) || !chainHeadToSpine.IsCreated)
            {
                return;
            }

            uint color = gizmos.StageColor(stage);
            int length = chainHeadToSpine.Length;
            for (int i = length - 1; i > 0; i--)
            {
                gizmos.Chain(stage, stream, chainHeadToSpine[i], chainHeadToSpine[i - 1], color);
            }

            if (length > 0 && chainHeadToSpine[0].IsValid(stream))
            {
                Vector3 solvedHead = chainHeadToSpine[0].GetPosition(stream);
                gizmos.Point(stage, solvedHead, color);
                gizmos.Line(stage, solvedHead, targetPositionHead, k_GizmoResidual);
                gizmos.Label(stage, solvedHead, "Head pin residual", k_GizmoResidual);
            }

            gizmos.BoneAxes(stage, stream, handleHips, gizmos.AxisLength);
            gizmos.BoneAxes(stage, stream, handleChest, gizmos.AxisLength);
            gizmos.BoneAxes(stage, stream, handleNeck, gizmos.AxisLength);

            if (chainChestIdx >= 0 && chainChestIdx < length && chainHeadToSpine[chainChestIdx].IsValid(stream))
            {
                gizmos.Label(stage, chainHeadToSpine[chainChestIdx].GetPosition(stream), "Chest joint");
            }
            if (handleHips.IsValid(stream))
            {
                Vector3 hipsPos = handleHips.GetPosition(stream);
                if (proneBodyPose)
                {
                    gizmos.Label(stage, hipsPos, "Hips (prone)");
                }
                else
                {
                    gizmos.Label(stage, hipsPos, "Hips");
                }
            }
        }

        void RecordShoulderGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Shoulders;
            if (!gizmos.Wants(stage))
            {
                return;
            }

            RecordClavicle(stage, stream, handleLeftShoulder, handleLeftUpperArm, true);
            RecordClavicle(stage, stream, handleRightShoulder, handleRightUpperArm, false);
        }

        void RecordClavicle(BasisIKGizmoStage stage, BasisPoseStream stream, BasisBoneHandle shoulder, BasisBoneHandle upperArm, bool isLeft)
        {
            if (!shoulder.IsValid(stream))
            {
                return;
            }
            uint color = SideColor(isLeft);
            gizmos.Chain(stage, stream, handleChest, shoulder, k_GizmoRaw);
            gizmos.Chain(stage, stream, shoulder, upperArm, color);
            gizmos.BoneAxes(stage, stream, shoulder, gizmos.AxisLength);
            Vector3 position = shoulder.GetPosition(stream);
            if (isLeft)
            {
                gizmos.Label(stage, position, "L clavicle", color);
            }
            else
            {
                gizmos.Label(stage, position, "R clavicle", color);
            }
        }

        void RecordLegGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Legs;
            if (!gizmos.Wants(stage))
            {
                return;
            }

            RecordLeg(stage, stream, handleLeftUpperLeg, handleLeftLowerLeg, handleLeftFoot,
                enabledLeftLowerLeg, targetPositionLeftLowerLeg, hintPositionLeftLowerLeg, hintWeightLeftLowerLeg, true);
            RecordLeg(stage, stream, handleRightUpperLeg, handleRightLowerLeg, handleRightFoot,
                enabledRightLowerLeg, targetPositionRightLowerLeg, hintPositionRightLowerLeg, hintWeightRightLowerLeg, false);

            if (kneeAnteriorRef.sqrMagnitude > k_SqrEpsilon && handleHips.IsValid(stream))
            {
                gizmos.Direction(stage, handleHips.GetPosition(stream), kneeAnteriorRef.normalized, gizmos.AxisLength * 3f, BasisIKGizmoPalette.Magenta);
            }
        }

        void RecordLeg(BasisIKGizmoStage stage, BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip,
            float enabled, Vector3 target, Vector3 hint, float hintWeight, bool isLeft)
        {
            if (!(enabled > 0f) || !root.IsValid(stream) || !mid.IsValid(stream) || !tip.IsValid(stream))
            {
                return;
            }

            uint color = SideColor(isLeft);
            Vector3 hipPos = root.GetPosition(stream);
            Vector3 kneePos = mid.GetPosition(stream);
            Vector3 footPos = tip.GetPosition(stream);

            gizmos.Bone(stage, hipPos, kneePos, color);
            gizmos.Bone(stage, kneePos, footPos, color);
            gizmos.Point(stage, footPos, color);
            gizmos.BoneAxes(stage, stream, tip, gizmos.AxisLength);
            gizmos.Line(stage, footPos, target, k_GizmoResidual);

            Vector3 bendPlane = Vector3.Cross(kneePos - hipPos, footPos - kneePos);
            if (bendPlane.sqrMagnitude > k_SqrEpsilon)
            {
                gizmos.Direction(stage, kneePos, bendPlane.normalized, gizmos.AxisLength, BasisIKGizmoPalette.Cyan);
            }

            if (hintWeight > 0f)
            {
                gizmos.Line(stage, kneePos, hint, k_GizmoHint);
            }

            if (isLeft)
            {
                gizmos.Label(stage, kneePos, "L knee", color);
            }
            else
            {
                gizmos.Label(stage, kneePos, "R knee", color);
            }
        }

        void RecordArmGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Arms;
            if (!gizmos.Wants(stage))
            {
                return;
            }

            RecordArm(stage, stream, handleLeftUpperArm, handleLeftLowerArm, handleLeftHand,
                enabledLeftHand, targetPositionLeftHand, hintPositionLeftHand, hintWeightLeftHand,
                tposeShoulderToHandLeft, k_SwingLeftElbow, true);
            RecordArm(stage, stream, handleRightUpperArm, handleRightLowerArm, handleRightHand,
                enabledRightHand, targetPositionRightHand, hintPositionRightHand, hintWeightRightHand,
                tposeShoulderToHandRight, k_SwingRightElbow, false);
        }

        void RecordArm(BasisIKGizmoStage stage, BasisPoseStream stream, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip,
            float enabled, Vector3 target, Vector3 hint, bool hasHint, float reach, int swingSlot, bool isLeft)
        {
            if (!(enabled > 0f) || !root.IsValid(stream) || !mid.IsValid(stream) || !tip.IsValid(stream))
            {
                return;
            }

            uint color = SideColor(isLeft);
            Vector3 shoulderPos = root.GetPosition(stream);
            Vector3 elbowPos = mid.GetPosition(stream);
            Vector3 handPos = tip.GetPosition(stream);

            gizmos.Bone(stage, shoulderPos, elbowPos, color);
            gizmos.Bone(stage, elbowPos, handPos, color);
            gizmos.Point(stage, handPos, color);
            gizmos.BoneAxes(stage, stream, tip, gizmos.AxisLength);
            gizmos.Line(stage, handPos, target, k_GizmoResidual);

            if (reach > 0f)
            {
                gizmos.Circle(stage, shoulderPos, playerUp, reach, k_GizmoReach);
            }

            if (hasHint)
            {
                gizmos.Line(stage, elbowPos, hint, k_GizmoHint);
            }

            if (swingPoleAnchor.IsCreated && swingSlot < swingPoleAnchor.Length)
            {
                Vector3 pole = swingPoleAnchor[swingSlot];
                if (pole.sqrMagnitude > k_SqrEpsilon)
                {
                    gizmos.Direction(stage, elbowPos, pole.normalized, gizmos.AxisLength * 2f, BasisIKGizmoPalette.Yellow);
                }
            }
            if (swingHintBend.IsCreated && swingSlot < swingHintBend.Length)
            {
                Vector3 bend = swingHintBend[swingSlot];
                if (bend.sqrMagnitude > k_SqrEpsilon)
                {
                    gizmos.Direction(stage, elbowPos, bend.normalized, gizmos.AxisLength * 2f, BasisIKGizmoPalette.Magenta);
                }
            }

            if (isLeft)
            {
                gizmos.Label(stage, elbowPos, "L elbow", color);
            }
            else
            {
                gizmos.Label(stage, elbowPos, "R elbow", color);
            }
        }

        void RecordToeGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Toes;
            if (!gizmos.Wants(stage))
            {
                return;
            }

            RecordToe(stage, stream, handleLeftFoot, handleLeftToe, leftToeEnabled, leftToeBendAxis, leftToeBendDeg, true);
            RecordToe(stage, stream, handleRightFoot, handleRightToe, rightToeEnabled, rightToeBendAxis, rightToeBendDeg, false);
        }

        void RecordToe(BasisIKGizmoStage stage, BasisPoseStream stream, BasisBoneHandle foot, BasisBoneHandle toe, bool driven, Vector3 bendAxis, float bendDeg, bool isLeft)
        {
            if (!toe.IsValid(stream))
            {
                return;
            }
            uint color = SideColor(isLeft);
            gizmos.Chain(stage, stream, foot, toe, color);
            gizmos.BoneAxes(stage, stream, toe, gizmos.AxisLength * 0.5f);

            Vector3 toePos = toe.GetPosition(stream);
            if (!driven && bendAxis.sqrMagnitude > k_SqrEpsilon && bendDeg != 0f)
            {
                gizmos.Direction(stage, toePos, bendAxis.normalized, gizmos.AxisLength, BasisIKGizmoPalette.Orange);
            }

            if (driven)
            {
                if (isLeft)
                {
                    gizmos.Label(stage, toePos, "L toe (tracked)", color);
                }
                else
                {
                    gizmos.Label(stage, toePos, "R toe (tracked)", color);
                }
            }
            else
            {
                if (isLeft)
                {
                    gizmos.Label(stage, toePos, "L toe (surface)", color);
                }
                else
                {
                    gizmos.Label(stage, toePos, "R toe (surface)", color);
                }
            }
        }

        void RecordOverrideGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Overrides;
            if (!gizmos.Wants(stage))
            {
                return;
            }

            uint color = gizmos.StageColor(stage);
            for (int i = 0; i < slotHandles.Length; i++)
            {
                if (!slotWeights[i] || !slotHandles[i].IsValid(stream))
                {
                    continue;
                }
                Vector3 position = slotPositions[i];
                gizmos.Point(stage, position, color);
                gizmos.Axes(stage, position, slotRotations[i] * slotOffsets[i], gizmos.AxisLength * 0.75f);
            }
        }

        void RecordSkeletonGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Skeleton;
            if (!gizmos.Wants(stage) || !stream.Parent.IsCreated)
            {
                return;
            }

            uint color = gizmos.StageColor(stage);
            for (int i = 0; i < stream.Count; i++)
            {
                int parent = stream.Parent[i];
                if (parent < 0)
                {
                    continue;
                }
                gizmos.Line(stage, stream.GetWorldPosition(parent), stream.GetWorldPosition(i), color);
            }
        }
    }
}
