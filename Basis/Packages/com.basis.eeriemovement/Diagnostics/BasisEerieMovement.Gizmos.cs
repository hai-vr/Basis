using Unity.Collections;
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
                gizmos.Normal(stage, hint, bendPref.normalized, gizmos.AxisLength, BasisIKGizmoPalette.Magenta);
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
                gizmos.Normal(stage, handleHips.GetPosition(stream), kneeAnteriorRef.normalized, gizmos.AxisLength, BasisIKGizmoPalette.Magenta);
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

            // The solver takes a bend NORMAL (kneeBendPref, hips-right by default) and derives the
            // pole from cross(limbAxis, normal). Drawing the normal as an arrow reads as "the knee
            // points sideways" -- it is a plane normal, so sideways is correct. The arrow below is
            // the derived pole: the direction the knee actually travels toward.
            Vector3 limbAxis = footPos - hipPos;
            Vector3 bendPlane = Vector3.Cross(kneePos - hipPos, footPos - kneePos);
            if (bendPlane.sqrMagnitude > k_SqrEpsilon)
            {
                gizmos.Normal(stage, kneePos, bendPlane.normalized, gizmos.AxisLength * 0.6f, BasisIKGizmoPalette.Cyan);
            }
            if (limbAxis.sqrMagnitude > k_SqrEpsilon && bendPlane.sqrMagnitude > k_SqrEpsilon)
            {
                Vector3 pole = Vector3.Cross(limbAxis.normalized, bendPlane.normalized);
                if (pole.sqrMagnitude > k_SqrEpsilon)
                {
                    gizmos.Direction(stage, kneePos, pole.normalized, gizmos.AxisLength * 1.5f, BasisIKGizmoPalette.Cyan);
                }
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

            // Both of these are directions rooted at the SHOULDER, not the elbow: the solver builds
            // the hint as shoulderPos + 0.5 * armLen * swingHintBend, and the pole anchor is the
            // pole direction off the same limb root. Drawn from the elbow they pointed nowhere real.
            float armLength = (handPos - shoulderPos).magnitude;
            float hintLength = armLength > k_MinMag ? armLength * 0.5f : gizmos.AxisLength * 2f;

            if (swingPoleAnchor.IsCreated && swingSlot < swingPoleAnchor.Length)
            {
                Vector3 pole = swingPoleAnchor[swingSlot];
                if (pole.sqrMagnitude > k_SqrEpsilon)
                {
                    gizmos.Direction(stage, shoulderPos, pole.normalized, hintLength, BasisIKGizmoPalette.Yellow);
                }
            }
            if (swingHintBend.IsCreated && swingSlot < swingHintBend.Length)
            {
                Vector3 bend = swingHintBend[swingSlot];
                if (bend.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 hintPoint = shoulderPos + bend.normalized * hintLength;
                    gizmos.Direction(stage, shoulderPos, bend.normalized, hintLength, BasisIKGizmoPalette.Magenta);
                    gizmos.Point(stage, hintPoint, BasisIKGizmoPalette.Magenta);
                }
            }
            if (swingCollided.IsCreated && swingSlot < swingCollided.Length && swingCollided[swingSlot] != 0)
            {
                gizmos.Circle(stage, elbowPos, handPos - shoulderPos, gizmos.PointSize * 3f, BasisIKGizmoPalette.Red);
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



        void RecordFrameGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Frames;
            if (!gizmos.Wants(stage))
            {
                return;
            }

            float len = gizmos.AxisLength * 1.5f;

            if (handleHips.IsValid(stream))
            {
                handleHips.GetPositionAndRotation(stream, out Vector3 hipsPos, out Quaternion hipsRot);
                gizmos.Direction(stage, hipsPos, playerUp.normalized, len * 2f, BasisIKGizmoPalette.Green);
                gizmos.Label(stage, hipsPos + playerUp.normalized * len * 2f, "playerUp");

                // Bind-cancelled hips frame: the space ApplyShoulderSlide and ApplyArmSwingChestFollow
                // express their yaw/pitch in. A raw bone frame reads a lean as twist on a rolled bind.
                Quaternion hipsAnat = hipsRot * Quaternion.Inverse(offsetRotationHips);
                gizmos.Axes(stage, hipsPos, hipsAnat, len);
                gizmos.Label(stage, hipsPos, "hips anat");
            }

            if (handleChest.IsValid(stream))
            {
                handleChest.GetPositionAndRotation(stream, out Vector3 chestPos, out Quaternion chestRot);
                gizmos.Axes(stage, chestPos, chestRot, len);
                gizmos.Label(stage, chestPos, "chest");

                if (handleLeftUpperArm.IsValid(stream) && handleRightUpperArm.IsValid(stream))
                {
                    Vector3 bodyRight = handleRightUpperArm.GetPosition(stream) - handleLeftUpperArm.GetPosition(stream);
                    if (bodyRight.sqrMagnitude > k_SqrEpsilon)
                    {
                        gizmos.Direction(stage, chestPos, bodyRight.normalized, len, BasisIKGizmoPalette.Red);
                        gizmos.Label(stage, chestPos + bodyRight.normalized * len, "bodyRight");
                    }
                }
            }

            RecordSpineRestFrames(stream, stage, len * 0.6f);
        }

        void RecordSpineRestFrames(BasisPoseStream stream, BasisIKGizmoStage stage, float len)
        {
            if (!chainSpineRestFrames.IsCreated || !chainHeadToSpine.IsCreated)
            {
                return;
            }

            int length = chainHeadToSpine.Length;
            for (int i = 1; i <= length - 2 && i < chainSpineRestFrames.Length; i++)
            {
                BasisSpineRestFrame frame = chainSpineRestFrames[i];
                if (!frame.Valid || !chainHeadToSpine[i].IsValid(stream) || !chainHeadToSpine[i + 1].IsValid(stream))
                {
                    continue;
                }

                // Rest frames are stored in the PARENT bone's local space, so they only mean
                // anything once carried back out through the parent's live world rotation.
                Quaternion parentRot = chainHeadToSpine[i + 1].GetRotation(stream);
                Vector3 pos = chainHeadToSpine[i].GetPosition(stream);
                gizmos.Line(stage, pos, pos + parentRot * frame.Right * len, BasisIKGizmoPalette.Red);
                gizmos.Line(stage, pos, pos + parentRot * frame.Up * len, BasisIKGizmoPalette.Green);
                gizmos.Line(stage, pos, pos + parentRot * frame.Forward * len, BasisIKGizmoPalette.Blue);
            }
        }

        void RecordLimitGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Limits;
            if (!gizmos.Wants(stage))
            {
                return;
            }

            RecordSpineRomCones(stream, stage);

            float radius = gizmos.AxisLength;
            RecordJointAngle(stream, stage, handleLeftUpperArm, handleLeftLowerArm, handleLeftHand, radius, "L elbow");
            RecordJointAngle(stream, stage, handleRightUpperArm, handleRightLowerArm, handleRightHand, radius, "R elbow");
            RecordJointAngle(stream, stage, handleLeftUpperLeg, handleLeftLowerLeg, handleLeftFoot, radius, "L knee");
            RecordJointAngle(stream, stage, handleRightUpperLeg, handleRightLowerLeg, handleRightFoot, radius, "R knee");
        }

        void RecordJointAngle(BasisPoseStream stream, BasisIKGizmoStage stage, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, float radius, in FixedString64Bytes label)
        {
            if (!root.IsValid(stream) || !mid.IsValid(stream) || !tip.IsValid(stream))
            {
                return;
            }
            Vector3 midPos = mid.GetPosition(stream);
            Vector3 toRoot = root.GetPosition(stream) - midPos;
            Vector3 toTip = tip.GetPosition(stream) - midPos;
            if (toRoot.sqrMagnitude <= k_SqrEpsilon || toTip.sqrMagnitude <= k_SqrEpsilon)
            {
                return;
            }
            gizmos.Angle(stage, midPos, toRoot, toTip, radius, BasisIKGizmoPalette.Yellow);
            gizmos.Label(stage, midPos, label, BasisIKGizmoPalette.Yellow);
        }

        void RecordSpineRomCones(BasisPoseStream stream, BasisIKGizmoStage stage)
        {
            if (!spineAnatomicalRom || !chainSpineRestFrames.IsCreated || !chainSpineRoms.IsCreated || !chainHeadToSpine.IsCreated)
            {
                return;
            }

            int length = chainHeadToSpine.Length;
            for (int i = 1; i <= length - 2 && i < chainSpineRestFrames.Length && i < chainSpineRoms.Length; i++)
            {
                BasisSpineRestFrame frame = chainSpineRestFrames[i];
                if (!frame.Valid || !chainHeadToSpine[i].IsValid(stream) || !chainHeadToSpine[i + 1].IsValid(stream))
                {
                    continue;
                }

                Quaternion parentRot = chainHeadToSpine[i + 1].GetRotation(stream);
                Quaternion boneRot = chainHeadToSpine[i].GetRotation(stream);
                Quaternion local = BasisSpineAnatomyCore.Conj(parentRot) * boneRot;

                // Clamp is pure -- calling it here reports whether the live pose is against the
                // limit without changing anything the solve already decided.
                BasisSpineAnatomyCore.Clamp(local, frame, chainSpineRoms[i], out BasisSpineClampInfo info);

                BasisSpineRom rom = chainSpineRoms[i];
                Vector3 pos = chainHeadToSpine[i].GetPosition(stream);
                Vector3 up = parentRot * frame.Up;
                Vector3 right = parentRot * frame.Right;
                Vector3 forward = parentRot * frame.Forward;

                float coneLength = (chainHeadToSpine[i - 1].IsValid(stream)
                    ? (chainHeadToSpine[i - 1].GetPosition(stream) - pos).magnitude
                    : gizmos.AxisLength * 2f);
                if (coneLength <= k_MinMag)
                {
                    continue;
                }

                uint color = info.SwingClamped ? BasisIKGizmoPalette.Red : gizmos.StageColor(stage);
                gizmos.SwingCone(stage, pos, up, right, forward, rom.LatDeg, rom.FlexDeg, rom.ExtDeg, coneLength, color);

                if (info.TwistClamped)
                {
                    gizmos.Normal(stage, pos, up, coneLength * 0.35f, BasisIKGizmoPalette.Orange);
                }
            }
        }

        void RecordReachGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Reach;
            if (!gizmos.Wants(stage))
            {
                return;
            }

            RecordArmReach(stream, stage, handleLeftUpperArm, handleLeftHand, tposeShoulderToHandLeft, "L arm");
            RecordArmReach(stream, stage, handleRightUpperArm, handleRightHand, tposeShoulderToHandRight, "R arm");
            RecordLegReach(stream, stage, handleLeftUpperLeg, handleLeftLowerLeg, handleLeftFoot, "L leg");
            RecordLegReach(stream, stage, handleRightUpperLeg, handleRightLowerLeg, handleRightFoot, "R leg");
        }

        void RecordArmReach(BasisPoseStream stream, BasisIKGizmoStage stage, BasisBoneHandle root, BasisBoneHandle tip, float maxReach, in FixedString64Bytes label)
        {
            if (!(maxReach > k_MinMag) || !root.IsValid(stream) || !tip.IsValid(stream))
            {
                return;
            }
            Vector3 rootPos = root.GetPosition(stream);
            float current = (tip.GetPosition(stream) - rootPos).magnitude;
            RecordReachRatio(stage, rootPos, maxReach, current / maxReach, label);
        }

        void RecordLegReach(BasisPoseStream stream, BasisIKGizmoStage stage, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, in FixedString64Bytes label)
        {
            if (!root.IsValid(stream) || !mid.IsValid(stream) || !tip.IsValid(stream))
            {
                return;
            }
            Vector3 rootPos = root.GetPosition(stream);
            Vector3 midPos = mid.GetPosition(stream);
            Vector3 tipPos = tip.GetPosition(stream);
            float maxReach = (midPos - rootPos).magnitude + (tipPos - midPos).magnitude;
            if (!(maxReach > k_MinMag))
            {
                return;
            }
            RecordReachRatio(stage, rootPos, maxReach, (tipPos - rootPos).magnitude / maxReach, label);
        }

        void RecordReachRatio(BasisIKGizmoStage stage, Vector3 rootPos, float maxReach, float ratio, in FixedString64Bytes label)
        {
            // Green through amber to red as the limb approaches full extension, which is where the
            // pole becomes ill-conditioned and the knee or elbow starts to snap.
            float t = Mathf.Clamp01((ratio - 0.75f) / 0.25f);
            uint color = BasisIKGizmoPalette.Rgba((byte)(60f + 195f * t), (byte)(255f - 195f * t), 60, 255);

            gizmos.Sphere(stage, rootPos, maxReach, BasisIKGizmoPalette.WithAlpha(color, 70));
            gizmos.Point(stage, rootPos, color);

            if (!gizmos.WantLabels)
            {
                return;
            }
            FixedString64Bytes text = label;
            text.Append(' ');
            text.Append(ratio);
            gizmos.Label(stage, rootPos, text, color);
        }

        void RecordNumberGizmos(BasisPoseStream stream)
        {
            const BasisIKGizmoStage stage = BasisIKGizmoStage.Numbers;
            if (!gizmos.Wants(stage) || !gizmos.WantLabels)
            {
                return;
            }

            RecordLegNumbers(stream, stage, 0, handleLeftLowerLeg);
            RecordLegNumbers(stream, stage, 1, handleRightLowerLeg);

            RecordResidual(stream, stage, handleLeftHand, targetPositionLeftHand, enabledLeftHand, "L hand off");
            RecordResidual(stream, stage, handleRightHand, targetPositionRightHand, enabledRightHand, "R hand off");
            RecordResidual(stream, stage, handleLeftFoot, targetPositionLeftLowerLeg, enabledLeftLowerLeg, "L foot off");
            RecordResidual(stream, stage, handleRightFoot, targetPositionRightLowerLeg, enabledRightLowerLeg, "R foot off");

            if (chainHeadToSpine.IsCreated && chainHeadToSpine.Length > 0 && chainHeadToSpine[0].IsValid(stream))
            {
                Vector3 solvedHead = chainHeadToSpine[0].GetPosition(stream);
                FixedString64Bytes text = "head off ";
                text.Append((solvedHead - targetPositionHead).magnitude);
                gizmos.Label(stage, solvedHead, text, k_GizmoResidual);
            }
        }

        void RecordResidual(BasisPoseStream stream, BasisIKGizmoStage stage, BasisBoneHandle tip, Vector3 target, float enabled, in FixedString64Bytes label)
        {
            if (!(enabled > 0f) || !tip.IsValid(stream))
            {
                return;
            }
            Vector3 pos = tip.GetPosition(stream);
            FixedString64Bytes text = label;
            text.Append(' ');
            text.Append((pos - target).magnitude);
            gizmos.Label(stage, pos, text, k_GizmoResidual);
        }

        void RecordLegNumbers(BasisPoseStream stream, BasisIKGizmoStage stage, int slot, BasisBoneHandle knee)
        {
            if (!legDiagnostics.IsCreated || slot >= legDiagnostics.Length || !knee.IsValid(stream))
            {
                return;
            }

            BasisLegDiagnostics d = legDiagnostics[slot];
            Vector3 pos = knee.GetPosition(stream);
            uint color = SideColor(slot == 0);
            float step = gizmos.AxisLength * 0.6f;

            FixedString64Bytes reach = "reach ";
            reach.Append(d.ReachRatio);
            reach.Append(' ');
            reach.Append(d.KneeAngleDeg);
            gizmos.Label(stage, pos, reach, color);

            FixedString64Bytes swivel = "swivel ";
            swivel.Append(d.RawSwivelDeg);
            swivel.Append(' ');
            swivel.Append(d.SmoothSwivelDeg);
            gizmos.Label(stage, pos + Vector3.up * step, swivel, color);

            FixedString64Bytes hip = "hip ";
            hip.Append(d.HipFlexionDeg);
            hip.Append(' ');
            hip.Append(d.HipAbductionDeg);
            hip.Append(' ');
            hip.Append(d.FemurTwistDeg);
            gizmos.Label(stage, pos + Vector3.up * (step * 2f), hip, color);

            FixedString64Bytes trust = "distrust ";
            trust.Append(d.HintDistrust);
            gizmos.Label(stage, pos + Vector3.up * (step * 3f), trust, color);
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
