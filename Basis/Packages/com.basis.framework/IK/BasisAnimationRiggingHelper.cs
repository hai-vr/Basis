using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public static class BasisAnimationRiggingHelper
{
    /// <summary>Per-effector bind offset for the uncalibrated rig build: bone-sim LOCAL outgoing against the
    /// avatar bone RELATIVE TO the animator root (the avatar's own forward), so its spawn orientation can't
    /// leak in. FBT calibration recomputes these head-driven instead (FullBodyCalibration).</summary>
    public static Quaternion CalibratedRotationOffset(BasisLocalBoneControl control, Transform animatorRoot, Transform avatarBone)
    {
        return CalibratedRotationOffset(control.OutGoingData.rotation, animatorRoot != null ? animatorRoot.rotation : Quaternion.identity, avatarBone.rotation, animatorRoot != null);
    }

    /// <summary>Pure form: the per-effector bind offset from rotations alone. offset applied as
    /// boneOutgoing*offset reproduces the avatar bone relative to its animator root, so a spawn
    /// orientation can't leak across an avatar swap. Shared with the Calibration Math sweep.</summary>
    public static Quaternion CalibratedRotationOffset(Quaternion boneOutgoingRotation, Quaternion animatorRootRotation, Quaternion avatarBoneRotation, bool hasAnimatorRoot)
    {
        Quaternion boneRelativeToRoot = hasAnimatorRoot
            ? Quaternion.Inverse(animatorRootRotation) * avatarBoneRotation
            : avatarBoneRotation;
        return Quaternion.Inverse(boneOutgoingRotation) * boneRelativeToRoot;
    }

    /// <summary>
    /// Build the combined Full IK constraint from arrays of joints and roles.
    /// root/mid/tip must be length >= 3: [Head, LeftLowerLeg, RightLowerLeg]
    /// TargetRole/BendRole/UseBoneRole correspond index-by-index to those same chains.
    /// </summary>
    public static void CreateBasisFullBodyRIG(BasisLocalPlayer player, GameObject parent, BasisTransformMapping Mapping, out BasisFullBodyIK BasisFullIKConstraint)
    {
        // Holder + component
        var go = CreateAndSetParent(parent.transform, $"Full IK ({parent.name})");
        BasisFullIKConstraint = BasisHelpers.GetOrAddComponent<BasisFullBodyIK>(go);
        var data = BasisFullIKConstraint.data;
        // Torso / head chain
        data.hips = Mapping.Hips;
        data.spine = Mapping.spine;
        data.chest = Mapping.chest;
        data.upperChest = Mapping.Upperchest;
        data.neck = Mapping.neck;
        data.head = Mapping.head;
        // Shoulders
        data.LeftShoulder = Mapping.leftShoulder;
        data.RightShoulder = Mapping.RightShoulder;
        // Arms
        data.leftUpperArm = Mapping.leftUpperArm;
        data.leftLowerArm = Mapping.leftLowerArm;
        data.LeftHand = Mapping.leftHand;
        data.RightUpperArm = Mapping.RightUpperArm;
        data.RightLowerArm = Mapping.RightLowerArm;
        data.RightHand = Mapping.rightHand;
        // Optional twist bones (auto-detected from rig hierarchy; null when not present)
        data.LeftUpperArmTwist = Mapping.leftUpperArmTwist;
        data.LeftLowerArmTwist = Mapping.leftLowerArmTwist;
        data.RightUpperArmTwist = Mapping.RightUpperArmTwist;
        data.RightLowerArmTwist = Mapping.RightLowerArmTwist;
        // Legs
        data.LeftUpperLeg = Mapping.LeftUpperLeg;
        data.LeftLowerLeg = Mapping.LeftLowerLeg;
        data.leftFoot = Mapping.leftFoot;
        data.RightUpperLeg = Mapping.RightUpperLeg;
        data.RightLowerLeg = Mapping.RightLowerLeg;
        data.RightFoot = Mapping.rightFoot;
        // Toes
        data.LeftToe = Mapping.leftToe;
        data.RightToe = Mapping.rightToe;
        // Head
        data.m_CalibratedRotationHead = Mapping.Hashead ? CalibratedRotationOffset(BasisLocalBoneDriver.HeadControl, Mapping.AnimatorRoot, Mapping.head) : Quaternion.identity;
        // Feet
        data.M_CalibrationLeftFootRotation = Mapping.Hashead ? CalibratedRotationOffset(BasisLocalBoneDriver.LeftFootControl, Mapping.AnimatorRoot, Mapping.leftFoot) : Quaternion.identity;
        data.M_CalibrationRightFootRotation = Mapping.Hashead ? CalibratedRotationOffset(BasisLocalBoneDriver.RightFootControl, Mapping.AnimatorRoot, Mapping.rightFoot) : Quaternion.identity;

        Quaternion leftLandmarkBind = Quaternion.identity;
        Quaternion rightLandmarkBind = Quaternion.identity;

        Quaternion _lastGoodLeftRot = Quaternion.identity;
        Quaternion _lastGoodRightRot = Quaternion.identity;
        bool _hasLastLeft = false;
        bool _hasLastRight = false;

        if (Mapping.HasleftHand)
        {
            Vector3 wrist = Mapping.leftHand.position;
            var a = new[] { GetLM(Mapping.LeftIndex, 0), GetLM(Mapping.LeftIndex, 0), GetLM(Mapping.LeftMiddle, 0) };
            var b = new[] { GetLM(Mapping.LeftLittle, 0), GetLM(Mapping.LeftMiddle, 0), GetLM(Mapping.LeftLittle, 0) };
            leftLandmarkBind = ComputeHandRotationWithFallback(wrist, a, b, ref _hasLastLeft, ref _lastGoodLeftRot);
        }
        if (Mapping.HasrightHand)
        {
            Vector3 wrist = Mapping.rightHand.position;
            var a = new[] { GetLM(Mapping.RightIndex, 0), GetLM(Mapping.RightIndex, 0), GetLM(Mapping.RightMiddle, 0) };
            var b = new[] { GetLM(Mapping.RightLittle, 0), GetLM(Mapping.RightMiddle, 0), GetLM(Mapping.RightLittle, 0) };
            rightLandmarkBind = ComputeHandRotationWithFallback(wrist, a, b, ref _hasLastRight, ref _lastGoodRightRot);
        }
        // Bone bind rotations (world space)
        Quaternion leftBoneBind = Mapping.leftHand.rotation;
        Quaternion rightBoneBind = Mapping.rightHand.rotation;

        data.m_CalibratedRotationLeftHand = Quaternion.Inverse(leftLandmarkBind) * leftBoneBind;
        data.m_CalibratedRotationRightHand = Quaternion.Inverse(rightLandmarkBind) * rightBoneBind;

        data.m_CalibratedRotationChest = Mapping.Haschest ? CalibratedRotationOffset(BasisLocalBoneDriver.ChestControl, Mapping.AnimatorRoot, Mapping.chest) : Quaternion.identity;
        data.m_CalibratedRotationNeck = Mapping.Hasneck ? Mapping.neck.rotation : Quaternion.identity;
        data.m_CalibratedRotationLeftToe = Mapping.HasleftToes ? CalibratedRotationOffset(BasisLocalBoneDriver.LeftToeControl, Mapping.AnimatorRoot, Mapping.leftToe) : Quaternion.identity;
        data.m_CalibratedRotationRightToe = Mapping.HasrightToes ? CalibratedRotationOffset(BasisLocalBoneDriver.RightToeControl, Mapping.AnimatorRoot, Mapping.rightToe) : Quaternion.identity;


        data.m_CalibratedRotationLeftShoulder = Mapping.HasleftShoulder ? CalibratedRotationOffset(BasisLocalBoneDriver.LeftShoulderControl, Mapping.AnimatorRoot, Mapping.leftShoulder) : Quaternion.identity;
        data.m_CalibratedRotationRightShoulder = Mapping.HasRightShoulder ? CalibratedRotationOffset(BasisLocalBoneDriver.RightShoulderControl, Mapping.AnimatorRoot, Mapping.RightShoulder) : Quaternion.identity;
        // Hips reference rotation
        data.OffsetRotationHips = Mapping.HasHips ? CalibratedRotationOffset(BasisLocalBoneDriver.HipsControl, Mapping.AnimatorRoot, Mapping.Hips) : Quaternion.identity;
        // Head
        data.PositionHead = BasisLocalBoneDriver.HeadControl.OutgoingWorldData.position;
        data.RotationHead = BasisLocalBoneDriver.HeadControl.OutgoingWorldData.rotation;

        // Left foot
        data.LeftFootPosition = BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData.position;
        data.LeftFootRotation = BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData.rotation;

        // Right  foot
        data.RightFootPosition = BasisLocalBoneDriver.RightFootControl.OutgoingWorldData.position;
        data.RightFootRotation = BasisLocalBoneDriver.RightFootControl.OutgoingWorldData.rotation;

        // Hips
        data.PositionHips = BasisLocalBoneDriver.HipsControl.OutgoingWorldData.position;
        data.RotationHips = BasisLocalBoneDriver.HipsControl.OutgoingWorldData.rotation;

        // Hands
        data.PositionLeftHand = BasisLocalBoneDriver.LeftHandControl.OutgoingWorldData.position;
        data.RotationLeftHand = BasisLocalBoneDriver.LeftHandControl.OutgoingWorldData.rotation;

        data.PositionRightHand = BasisLocalBoneDriver.RightHandControl.OutgoingWorldData.position;
        data.RotationRightHand = BasisLocalBoneDriver.RightHandControl.OutgoingWorldData.rotation;

        // Cache world data once per control (less property spam, easier to read)
        var leftLowerArm = BasisLocalBoneDriver.LeftLowerArmControl.OutgoingWorldData;
        var rightLowerArm = BasisLocalBoneDriver.RightLowerArmControl.OutgoingWorldData;
        var chest = BasisLocalBoneDriver.ChestControl.OutgoingWorldData;
        var leftLowerLeg = BasisLocalBoneDriver.LeftLowerLegControl.OutgoingWorldData;
        var rightLowerLeg = BasisLocalBoneDriver.RightLowerLegControl.OutgoingWorldData;
        var leftShoulder = BasisLocalBoneDriver.LeftShoulderControl.OutgoingWorldData;
        var rightShoulder = BasisLocalBoneDriver.RightShoulderControl.OutgoingWorldData;

        // --- Arms ---
        data.LeftLowerArmPosition = BasisLocalRigDriver.ApplyHintBias(Basis.Scripts.TransformBinders.BoneControl.BasisBoneTrackedRole.LeftLowerArm, leftLowerArm.position, leftLowerArm.rotation);
        data.LeftLowerArmRotation = leftLowerArm.rotation;
        data.RightLowerArmPosition = BasisLocalRigDriver.ApplyHintBias(Basis.Scripts.TransformBinders.BoneControl.BasisBoneTrackedRole.RightLowerArm, rightLowerArm.position, rightLowerArm.rotation);
        data.RightLowerArmRotation = rightLowerArm.rotation;

        // --- Shoulders ---
        data.LeftShoulderRotation = leftShoulder.rotation;
        data.RightShoulderRotation = rightShoulder.rotation;

        // --- Legs ---
        data.PositionLeftLowerLeg = BasisLocalRigDriver.ApplyHintBias(Basis.Scripts.TransformBinders.BoneControl.BasisBoneTrackedRole.LeftLowerLeg, leftLowerLeg.position, leftLowerLeg.rotation);
        data.RotationLeftLowerLeg = leftLowerLeg.rotation;
        data.PositionRightLowerLeg = BasisLocalRigDriver.ApplyHintBias(Basis.Scripts.TransformBinders.BoneControl.BasisBoneTrackedRole.RightLowerLeg, rightLowerLeg.position, rightLowerLeg.rotation);
        data.RotationRightLowerLeg = rightLowerLeg.rotation;

        // --- Chest ---
        data.ChestPosition = BasisLocalRigDriver.ApplyHintBias(Basis.Scripts.TransformBinders.BoneControl.BasisBoneTrackedRole.Chest, chest.position, chest.rotation);
        data.ChestRotation = chest.rotation;

        // Developer diagnostics: dump the calibrated offsets, the runtime targets, and the avatar root.
        if (BasisCalibrationDebugRecorder.Enabled)
        {
            Transform animRoot = player?.BasisAvatar?.Animator != null ? player.BasisAvatar.Animator.transform : null;
            BasisCalibrationDebugRecorder.Bone("Offsets", "AnimatorRoot", animRoot);

            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibratedRotationHead", "offset", data.m_CalibratedRotationHead);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibrationLeftFootRotation", "offset", data.M_CalibrationLeftFootRotation);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibrationRightFootRotation", "offset", data.M_CalibrationRightFootRotation);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibratedRotationChest", "offset", data.m_CalibratedRotationChest);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibratedRotationNeck", "offset", data.m_CalibratedRotationNeck);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibratedRotationLeftToe", "offset", data.m_CalibratedRotationLeftToe);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibratedRotationRightToe", "offset", data.m_CalibratedRotationRightToe);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibratedRotationLeftShoulder", "offset", data.m_CalibratedRotationLeftShoulder);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibratedRotationRightShoulder", "offset", data.m_CalibratedRotationRightShoulder);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibratedRotationLeftHand", "offset", data.m_CalibratedRotationLeftHand);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "CalibratedRotationRightHand", "offset", data.m_CalibratedRotationRightHand);
            BasisCalibrationDebugRecorder.Rotation("Offsets", "OffsetRotationHips", "offset", data.OffsetRotationHips);

            BasisCalibrationDebugRecorder.Pose("Offsets", "TargetHead", "target", data.PositionHead, data.RotationHead, Vector3.one);
            BasisCalibrationDebugRecorder.Pose("Offsets", "TargetHips", "target", data.PositionHips, data.RotationHips, Vector3.one);
            BasisCalibrationDebugRecorder.Pose("Offsets", "TargetLeftHand", "target", data.PositionLeftHand, data.RotationLeftHand, Vector3.one);
            BasisCalibrationDebugRecorder.Pose("Offsets", "TargetRightHand", "target", data.PositionRightHand, data.RotationRightHand, Vector3.one);
            BasisCalibrationDebugRecorder.Pose("Offsets", "TargetLeftFoot", "target", data.LeftFootPosition, data.LeftFootRotation, Vector3.one);
            BasisCalibrationDebugRecorder.Pose("Offsets", "TargetRightFoot", "target", data.RightFootPosition, data.RightFootRotation, Vector3.one);

            // Bone-control rest frames (OutgoingWorld = ParentRotation * OutGoing).
            RecordControlRest("head.ctrl", BasisLocalBoneDriver.HeadControl);
            RecordControlRest("hips.ctrl", BasisLocalBoneDriver.HipsControl);
            RecordControlRest("chest.ctrl", BasisLocalBoneDriver.ChestControl);
            RecordControlRest("neck.ctrl", BasisLocalBoneDriver.NeckControl);
            RecordControlRest("leftFoot.ctrl", BasisLocalBoneDriver.LeftFootControl);
            RecordControlRest("rightFoot.ctrl", BasisLocalBoneDriver.RightFootControl);
        }

        data.CollisionsEnabled = true;
        data.UseHandCapsule = true;
        data.ProtectElbow = true;
        data.EnabledSpineIK = true;
        data.IKLockMode = (float)SMModuleCalibration.CurrentIKLockMode;

        // Shoulder pre-solve defaults
        data.ShoulderSolveEnabled = true;
        data.ShoulderElevationFactor = 0.4f;
        data.ShoulderProtractionFactor = 0.3f;

        BasisFullIKConstraint.data = data;

        GeneratedRequiredTransforms(player, Mapping.head);

        GeneratedRequiredTransforms(player, Mapping.leftFoot);
        GeneratedRequiredTransforms(player, Mapping.rightFoot);

        GeneratedRequiredTransforms(player, Mapping.leftHand);
        GeneratedRequiredTransforms(player, Mapping.rightHand);
    }

    /// <summary>
    /// Records a bone control's rest frames (local + world outgoing, T-pose local/scaled, and the
    /// inverse-offset-from-bone) for the calibration CSV. No-op unless the developer toggle is on.
    /// </summary>
    private static void RecordControlRest(string label, Basis.Scripts.TransformBinders.BoneControl.BasisLocalBoneControl c)
    {
        if (c == null)
        {
            return;
        }
        BasisCalibrationDebugRecorder.Pose("Offsets", label + ".OutGoing", "local", c.OutGoingData.position, c.OutGoingData.rotation, Vector3.one);
        BasisCalibrationDebugRecorder.Pose("Offsets", label + ".OutgoingWorld", "world", c.OutgoingWorldData.position, c.OutgoingWorldData.rotation, Vector3.one);
        BasisCalibrationDebugRecorder.Pose("Offsets", label + ".TposeLocal", "local", c.TposeLocal.position, c.TposeLocal.rotation, Vector3.one);
        BasisCalibrationDebugRecorder.Pose("Offsets", label + ".TposeLocalScaled", "local", c.TposeLocalScaled.position, c.TposeLocalScaled.rotation, Vector3.one);
        BasisCalibrationDebugRecorder.Pose("Offsets", label + ".InverseOffsetFromBone", "local", c.InverseOffsetFromBone.position, c.InverseOffsetFromBone.rotation, Vector3.one);
    }

    private static (bool valid, Vector3 pos) GetLM(Transform[] arr, int i)
    {
        if (arr != null && i >= 0 && i < arr.Length && arr[i] != null)
        {
            return (true, arr[i].position);
        }

        return (false, Vector3.zero);
    }
    private static Quaternion ComputeHandRotationWithFallback( Vector3 wrist,(bool valid, Vector3 pos)[] pointsA,(bool valid, Vector3 pos)[] pointsB, ref bool hasLast, ref Quaternion lastRot)
    {
        for (int Index = 0; Index < pointsA.Length; Index++)
        {
            if (!pointsA[Index].valid || !pointsB[Index].valid)
            {
                continue;
            }

            Quaternion rot = HandRotationFromLandmarks(wrist, pointsA[Index].pos, pointsB[Index].pos);
            if (rot == Quaternion.identity) continue;

            lastRot = rot;
            hasLast = true;
            return rot;
        }

        return hasLast ? lastRot : Quaternion.identity;
    }
    public static Quaternion HandRotationFromLandmarks(Vector3 wrist, Vector3 indexMCP, Vector3 pinkyMCP)
    {
        Vector3 right = (pinkyMCP - indexMCP);
        Vector3 knuckleMid = (indexMCP + pinkyMCP) * 0.5f;
        Vector3 forward = (knuckleMid - wrist);

        if (right.sqrMagnitude < 1e-8f || forward.sqrMagnitude < 1e-8f)
        {
            return Quaternion.identity; // caller will treat as "no usable landmark rotation"
        }

        right.Normalize();
        forward.Normalize();

        Vector3 up = Vector3.Cross(forward, right);
        if (up.sqrMagnitude < 1e-8f)
        {
            return Quaternion.identity;
        }
        up.Normalize();
        return Quaternion.LookRotation(forward, up);
    }
    public static void GeneratedRequiredTransforms(BasisLocalPlayer player,Transform baseLevel)
    {
        if (baseLevel == null)
        {
            return;
        }

        Transform hips = BasisLocalAvatarDriver.Mapping.Hips;
        Transform current = baseLevel;

        // Stop when we reach either the hips or the player root.
        while (current != null && current != hips && current != player.transform)
        {
            AddRigTransformIfMissing(player, current);
            current = current.parent;
        }

        AddRigTransformIfMissing(player, hips);
    }


    private static void AddRigTransformIfMissing(BasisLocalPlayer player, Transform t)
    {
        if (!t.TryGetComponent<RigTransform>(out var rig))
        {
            rig = t.gameObject.AddComponent<RigTransform>();
        }

        var list = player.LocalRigDriver.AdditionalTransforms;
        if (!list.Contains(rig))
        {
            list.Add(rig);
        }
    }
    public static GameObject CreateAndSetParent(Transform parent, string name)
    {
        Transform[] Children = parent.transform.GetComponentsInChildren<Transform>();
        foreach (Transform child in Children)
        {
            if (child.name == $"Bone Role {name}")
            {
                return child.gameObject;
            }
        }

        // Create a new empty GameObject
        GameObject newObject = new GameObject(name);

        // Set its parent
        newObject.transform.SetParent(parent);
        return newObject;
    }
}
