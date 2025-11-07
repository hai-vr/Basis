using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public static class BasisAnimationRiggingHelper
{
    /// <summary>
    /// Build the combined Full IK constraint from arrays of joints and roles.
    /// root/mid/tip must be length >= 3: [Head, LeftLowerLeg, RightLowerLeg]
    /// TargetRole/BendRole/UseBoneRole correspond index-by-index to those same chains.
    /// </summary>
    public static void CreateBasisFullBodyRIG(BasisLocalPlayer player, GameObject parent, BasisTransformMapping BasisTransformMapping, out BasisFullBodyIK BasisFullIKConstraint)
    {
        // --- make holder and component
        var go = CreateAndSetParent(parent.transform, $"Full IK ({parent.name})");
        BasisFullIKConstraint = BasisHelpers.GetOrAddComponent<BasisFullBodyIK>(go);

        // cache data ref — easier to see what we’re setting
        var data = BasisFullIKConstraint.data;
        data.rootHead = BasisTransformMapping.chest;
        data.midHead = BasisTransformMapping.neck;
        data.tipHead = BasisTransformMapping.head;

        data.m_CalibratedOffsetHead = Vector3.zero;
        data.m_CalibratedRotationHead = BasisTransformMapping.head.rotation;

        data.TargetPositionHead = BasisLocalBoneDriver.HeadControl.OutgoingWorldData.position;
        data.TargetRotationHead = BasisLocalBoneDriver.HeadControl.OutgoingWorldData.rotation;

        data.HintPositionHead = BasisLocalBoneDriver.ChestControl.OutgoingWorldData.position;
        data.HintRotationHead = BasisLocalBoneDriver.ChestControl.OutgoingWorldData.rotation;

        data.LeftUpperLeg = BasisTransformMapping.LeftUpperLeg;
        data.LeftLowerLeg = BasisTransformMapping.LeftLowerLeg;
        data.leftFoot = BasisTransformMapping.leftFoot;

        data.m_CalibratedOffsetLeftFoot = Vector3.zero;
        data.m_CalibratedRotationLeftFoot = BasisTransformMapping.leftFoot.rotation;

        data.LeftFootTargetPosition = BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData.position;
        data.LeftFootTargetRotation = BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData.rotation;

        data.HintPositionLeftLowerLeg = BasisLocalBoneDriver.LeftLowerLegControl.OutgoingWorldData.position;
        data.HintRotationLeftLowerLeg = BasisLocalBoneDriver.LeftLowerLegControl.OutgoingWorldData.rotation;

        data.RightUpperLeg = BasisTransformMapping.RightUpperLeg;
        data.RightLowerLeg = BasisTransformMapping.RightLowerLeg;
        data.RightFoot = BasisTransformMapping.rightFoot;

        data.m_CalibratedOffsetRightLeftLeg = Vector3.zero;
        data.m_CalibratedRotationRightLeftLeg = BasisTransformMapping.rightFoot.rotation;

        data.RightFootTargetPosition = BasisLocalBoneDriver.RightFootControl.OutgoingWorldData.position;
        data.RightFootTargetRotation = BasisLocalBoneDriver.RightFootControl.OutgoingWorldData.rotation;

        data.HintPositionRightLowerLeg = BasisLocalBoneDriver.RightLowerLegControl.OutgoingWorldData.position;
        data.HintRotationRightLowerLeg = BasisLocalBoneDriver.RightLowerLegControl.OutgoingWorldData.rotation;

        data.hips = BasisTransformMapping.Hips;

        data.TargetPositionHips = BasisLocalBoneDriver.HipsControl.OutgoingWorldData.position;
        data.TargetRotationEulerHips = BasisLocalBoneDriver.HipsControl.OutgoingWorldData.rotation;
        data.OffsetRotationHips = BasisTransformMapping.Hips.rotation;

        // write back
        BasisFullIKConstraint.data = data;

        BasisFullIKConstraint.data.LeftToe = BasisTransformMapping.leftToes;
        BasisFullIKConstraint.data.RightToe = BasisTransformMapping.rightToes;

        BasisFullIKConstraint.data.m_CalibratedOffsetLeftHand = new Vector3(0, 0, 0);
        BasisFullIKConstraint.data.m_CalibratedOffsetRightHand = new Vector3(0, 0, 0);

        BasisFullIKConstraint.data.m_CalibratedRotationLeftHand = BasisTransformMapping.leftHand.rotation;
        BasisFullIKConstraint.data.m_CalibratedRotationRightHand = BasisTransformMapping.rightHand.rotation;

        BasisFullIKConstraint.data.TargetPositionLeftHand = BasisLocalBoneDriver.LeftHandControl.OutgoingWorldData.position;
        BasisFullIKConstraint.data.TargetRotationLeftHand = BasisLocalBoneDriver.LeftHandControl.OutgoingWorldData.rotation;
        BasisFullIKConstraint.data.TargetPositionRightHand = BasisLocalBoneDriver.RightHandControl.OutgoingWorldData.position;
        BasisFullIKConstraint.data.TargetRotationRightHand = BasisLocalBoneDriver.RightHandControl.OutgoingWorldData.rotation;
        BasisFullIKConstraint.data.HintPositionLeftHand = BasisLocalBoneDriver.LeftLowerArmControl.OutgoingWorldData.position;
        BasisFullIKConstraint.data.HintRotationLeftHand = BasisLocalBoneDriver.LeftLowerArmControl.OutgoingWorldData.rotation;
        BasisFullIKConstraint.data.HintPositionRightHand = BasisLocalBoneDriver.RightLowerArmControl.OutgoingWorldData.position;
        BasisFullIKConstraint.data.HintRotationRightHand = BasisLocalBoneDriver.RightLowerArmControl.OutgoingWorldData.rotation;
        BasisFullIKConstraint.data.rootLeftHand = BasisTransformMapping.leftUpperArm;
        BasisFullIKConstraint.data.midLeftHand = BasisTransformMapping.leftLowerArm;
        BasisFullIKConstraint.data.tipLeftHand = BasisTransformMapping.leftHand;
        BasisFullIKConstraint.data.rootRightHand = BasisTransformMapping.RightUpperArm;
        BasisFullIKConstraint.data.midRightHand = BasisTransformMapping.RightLowerArm;
        BasisFullIKConstraint.data.tipRightHand = BasisTransformMapping.rightHand;
        BasisFullIKConstraint.data.collisionsEnabled = true;
        BasisFullIKConstraint.data.chestCapsuleEnd = BasisTransformMapping.neck;
        BasisFullIKConstraint.data.chestCapsuleStart = BasisTransformMapping.chest;
        BasisFullIKConstraint.data.useHandCapsule = true;
        BasisFullIKConstraint.data.protectElbow = true;
        BasisFullIKConstraint.data.enabledHead = true;
        BasisFullIKConstraint.data.enabledHips = true;

        SetHandCollisionScale(BasisFullIKConstraint, player.CurrentHeight.SelectedAvatarToAvatarDefaultScale);

        GeneratedRequiredTransforms(player, BasisTransformMapping.leftToes);
        GeneratedRequiredTransforms(player, BasisTransformMapping.rightToes);
        GeneratedRequiredTransforms(player, BasisTransformMapping.Hips);
        GeneratedRequiredTransforms(player, BasisTransformMapping.leftHand);
        GeneratedRequiredTransforms(player, BasisTransformMapping.rightHand);
    }
    public static void ChooseSpine(BasisTransformMapping BasisTransformMapping, out Transform root, out Transform middle, out Transform tip)
    {
        if (BasisTransformMapping.HasUpperchest)
        {
            root = BasisTransformMapping.Upperchest;
            middle = BasisTransformMapping.neck;
            tip = BasisTransformMapping.head;
        }
        else if (BasisTransformMapping.Haschest)
        {
            root = BasisTransformMapping.chest;
            middle = BasisTransformMapping.neck;
            tip = BasisTransformMapping.head;
        }
        else
        {
            root = BasisTransformMapping.spine;
            middle = BasisTransformMapping.neck;
            tip = BasisTransformMapping.head;
        }
    }

    public static void SetHandCollisionScale(BasisFullBodyIK TwoBoneIKConstraint, float Scale)
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
