using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public static class BasisAnimationRiggingHelper
{
    private const int IDX_HEAD = 0;
    private const int IDX_LLEG = 1;
    private const int IDX_RLEG = 2;

    /// <summary>
    /// Build the combined Full IK constraint from arrays of joints and roles.
    /// root/mid/tip must be length >= 3: [Head, LeftLowerLeg, RightLowerLeg]
    /// TargetRole/BendRole/UseBoneRole correspond index-by-index to those same chains.
    /// </summary>
    public static void CreateMainIKRIG(
        BasisLocalPlayer player,
        GameObject parent,
        Transform[] root,
        Transform[] mid,
        Transform[] tip,
        BasisBoneTrackedRole[] TargetRole,
        BasisBoneTrackedRole[] BendRole,
        out BasisFullIKConstraint BasisFullIKConstraint,
        Transform hips, 
        BasisBoneTrackedRole hipsTargetRole,
         Transform LeftToe, Transform RightToe,
         Transform ChestStart, Transform ChestEnd,
         Transform rootLeft, Transform midLeft, Transform tipLeft,
         Transform rootRight, Transform midRight, Transform tipRight
    )
    {
        // --- sanity checks (keep them cheap & defensive)
        if (root == null || mid == null || tip == null ||
            TargetRole == null || BendRole == null)
        {
            throw new System.ArgumentNullException("IK arrays cannot be null.");
        }

        if (root.Length < 3 || mid.Length < 3 || tip.Length < 3 ||
            TargetRole.Length < 3 || BendRole.Length < 3)
        {
            throw new System.ArgumentException("Expected at least 3 elements for Head, LeftLowerLeg, RightLowerLeg.");
        }

        // --- make holder and component
        var go = CreateAndSetParent(parent.transform, $"Full IK ({parent.name})");
        BasisFullIKConstraint = BasisHelpers.GetOrAddComponent<BasisFullIKConstraint>(go);

        // cache data ref — easier to see what we’re setting
        var data = BasisFullIKConstraint.data;

        // -----------------------------
        // HEAD
        // -----------------------------
        data.rootHead = root[IDX_HEAD];
        data.midHead = mid[IDX_HEAD];
        data.tipHead = tip[IDX_HEAD];

        data.m_CalibratedOffsetHead = Vector3.zero;
        data.m_CalibratedRotationHead = tip[IDX_HEAD] ? tip[IDX_HEAD].rotation : Quaternion.identity;

        if ( player.LocalBoneDriver.FindBone(out BasisLocalBoneControl headTarget, TargetRole[IDX_HEAD]))
        {
            data.TargetPositionHead = headTarget.OutgoingWorldData.position;
            data.TargetRotationHead = headTarget.OutgoingWorldData.rotation;
        }

        if (player.LocalBoneDriver.FindBone(out BasisLocalBoneControl headHint, BendRole[IDX_HEAD]))
        {
            data.HintPositionHead = headHint.OutgoingWorldData.position;
            data.HintRotationHead = headHint.OutgoingWorldData.rotation;
        }

        // -----------------------------
        // LEFT LOWER LEG
        // -----------------------------
        data.rootLeftLowerLeg = root[IDX_LLEG];
        data.midLeftLowerLeg = mid[IDX_LLEG];
        data.tipLeftLowerLeg = tip[IDX_LLEG];

        data.m_CalibratedOffsetLeftLowerLeg = Vector3.zero;
        data.m_CalibratedRotationLeftLowerLeg = tip[IDX_LLEG] ? tip[IDX_LLEG].rotation : Quaternion.identity;

        if (player.LocalBoneDriver.FindBone(out BasisLocalBoneControl lTarget, TargetRole[IDX_LLEG]))
        {
            data.LeftFootTargetPosition = lTarget.OutgoingWorldData.position;
            data.LeftFootTargetRotation = lTarget.OutgoingWorldData.rotation;
        }

        if (player.LocalBoneDriver.FindBone(out BasisLocalBoneControl lHint, BendRole[IDX_LLEG]))
        {
            data.HintPositionLeftLowerLeg = lHint.OutgoingWorldData.position;
            data.HintRotationLeftLowerLeg = lHint.OutgoingWorldData.rotation;
        }

        // -----------------------------
        // RIGHT LOWER LEG
        // -----------------------------
        data.rootRightLowerLeg = root[IDX_RLEG];
        data.midRightLowerLeg = mid[IDX_RLEG];
        data.tipRightLowerLeg = tip[IDX_RLEG];

        data.m_CalibratedOffsetRightLowerLeg = Vector3.zero;
        data.m_CalibratedRotationRightLowerLeg = tip[IDX_RLEG] ? tip[IDX_RLEG].rotation : Quaternion.identity;

        if (player.LocalBoneDriver.FindBone(out BasisLocalBoneControl rTarget, TargetRole[IDX_RLEG]))
        {
            data.RightFootTargetPosition = rTarget.OutgoingWorldData.position;
            data.RightFootTargetRotation = rTarget.OutgoingWorldData.rotation;
        }

        if (player.LocalBoneDriver.FindBone(out BasisLocalBoneControl rHint, BendRole[IDX_RLEG]))
        {
            data.HintPositionRightLowerLeg = rHint.OutgoingWorldData.position;
            data.HintRotationRightLowerLeg = rHint.OutgoingWorldData.rotation;
        }

        // -----------------------------
        // HIPS (optional minimal driver)
        // -----------------------------
        data.hips = hips;

        if (hips != null && player.LocalBoneDriver.FindBone(out BasisLocalBoneControl hipsCtrl, hipsTargetRole))
        {
            data.TargetPositionHips = hipsCtrl.OutgoingWorldData.position;
            data.TargetRotationEulerHips = hipsCtrl.OutgoingWorldData.rotation;   // Quaternion
            data.OffsetRotationHips = hips.rotation;                   // set your T-pose offset if needed
        }

        // write back
        BasisFullIKConstraint.data = data;

        BasisFullIKConstraint.data.LeftToe = LeftToe;
        BasisFullIKConstraint.data.RightToe = RightToe;

        BasisFullIKConstraint.data.m_CalibratedOffsetLeftHand = new Vector3(0, 0, 0);
        BasisFullIKConstraint.data.m_CalibratedOffsetRightHand = new Vector3(0, 0, 0);

        BasisFullIKConstraint.data.m_CalibratedRotationLeftHand = tipLeft.rotation;
        BasisFullIKConstraint.data.m_CalibratedRotationRightHand = tipRight.rotation;

        if (player.LocalBoneDriver.FindBone(out BasisLocalBoneControl leftHandControl, BasisBoneTrackedRole.LeftHand))
        {
            var outgoing = leftHandControl.OutgoingWorldData;
            BasisFullIKConstraint.data.TargetPositionLeftHand = outgoing.position;
            BasisFullIKConstraint.data.TargetRotationLeftHand = outgoing.rotation;
        }
        if (player.LocalBoneDriver.FindBone(out BasisLocalBoneControl rightHandControl, BasisBoneTrackedRole.RightHand))
        {
            var outgoing = rightHandControl.OutgoingWorldData;
            BasisFullIKConstraint.data.TargetPositionRightHand = outgoing.position;
            BasisFullIKConstraint.data.TargetRotationRightHand = outgoing.rotation;
        }
        if (player.LocalBoneDriver.FindBone(out BasisLocalBoneControl leftHintControl, BasisBoneTrackedRole.LeftLowerArm))
        {
            var outgoing = leftHintControl.OutgoingWorldData;
            BasisFullIKConstraint.data.HintPositionLeftHand = outgoing.position;
            BasisFullIKConstraint.data.HintRotationLeftHand = outgoing.rotation;
        }
        if (player.LocalBoneDriver.FindBone(out BasisLocalBoneControl rightHintControl, BasisBoneTrackedRole.RightLowerArm))
        {
            var outgoing = rightHintControl.OutgoingWorldData;
            BasisFullIKConstraint.data.HintPositionRightHand = outgoing.position;
            BasisFullIKConstraint.data.HintRotationRightHand = outgoing.rotation;
        }
        BasisFullIKConstraint.data.rootLeftHand = rootLeft;
        BasisFullIKConstraint.data.midLeftHand = midLeft;
        BasisFullIKConstraint.data.tipLeftHand = tipLeft;


        BasisFullIKConstraint.data.rootRightHand = rootRight;
        BasisFullIKConstraint.data.midRightHand = midRight;
        BasisFullIKConstraint.data.tipRightHand = tipRight;


        BasisFullIKConstraint.data.collisionsEnabled = true;
        BasisFullIKConstraint.data.chestCapsuleEnd = ChestEnd;
        BasisFullIKConstraint.data.chestCapsuleStart = ChestStart;
        BasisFullIKConstraint.data.useHandCapsule = true;
        BasisFullIKConstraint.data.protectElbow = true;

        SetHandCollisionScale(BasisFullIKConstraint, player.CurrentHeight.SelectedAvatarToAvatarDefaultScale);

        GeneratedRequiredTransforms(player, LeftToe);
        GeneratedRequiredTransforms(player, RightToe);

        // If you have any extra setup that needs the tips (like creating targets), do it now.
        GeneratedRequiredTransforms(player, tip[0]);
        GeneratedRequiredTransforms(player, tip[1]);
        GeneratedRequiredTransforms(player, tip[2]);

        GeneratedRequiredTransforms(player, tipLeft);
        GeneratedRequiredTransforms(player, tipRight);
    }
    public static void SetHandCollisionScale(BasisFullIKConstraint TwoBoneIKConstraint, float Scale)
    {
        //1.6m is the default values for below.
        TwoBoneIKConstraint.data.collisionSkin = 0.05f * Scale;
        TwoBoneIKConstraint.data.handRadius = 0.01f * Scale;
        TwoBoneIKConstraint.data.handSkin = 0.03f * Scale;
        TwoBoneIKConstraint.data.chestRadius = 0.07f * Scale;
    }
    public static void GeneratedRequiredTransforms(BasisLocalPlayer player, Transform BaseLevel)
    {
        // Go up the hierarchy until you hit the TopLevelParent
        if (BaseLevel != null)
        {
            Transform currentTransform = BaseLevel.parent;
            while (currentTransform != null && currentTransform != BasisLocalAvatarDriver.References.Hips)
            {
                // Add component if the current transform doesn't have it
                if (currentTransform.TryGetComponent<RigTransform>(out RigTransform RigTransform))
                {
                    if (player.LocalRigDriver.AdditionalTransforms.Contains(RigTransform) == false)
                    {
                        player.LocalRigDriver.AdditionalTransforms.Add(RigTransform);
                    }
                }
                else
                {
                    RigTransform = currentTransform.gameObject.AddComponent<RigTransform>();
                    player.LocalRigDriver.AdditionalTransforms.Add(RigTransform);
                }
                // Move to the parent for the next iteration
                currentTransform = currentTransform.parent;
            }
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
