namespace Basis.Scripts.TransformBinders.BoneControl
{
    public enum BasisBoneTrackedRole
    {
        CenterEye = 0,
        Head = 1,
        Neck = 2,
        Chest = 3,
        Hips = 4,
        Spine = 5,

        LeftUpperLeg = 6,
        RightUpperLeg = 7,

        LeftLowerLeg = 8,
        RightLowerLeg = 9,

        LeftFoot = 10,
        RightFoot = 11,

        LeftShoulder = 12,
        RightShoulder = 13,

        LeftUpperArm = 14,
        RightUpperArm = 15,

        LeftLowerArm = 16,
        RightLowerArm = 17,

        LeftHand = 18,
        RightHand = 19,

        LeftToes = 20,
        RightToes = 21,

        Mouth = 22,
    }

    public static class BasisBoneTrackedRoleCommonCheck
    {
        public static bool CheckItsFBTracker(BasisBoneTrackedRole role)
        {
            return role != BasisBoneTrackedRole.LeftHand
                && role != BasisBoneTrackedRole.RightHand

                && role != BasisBoneTrackedRole.LeftUpperLeg
                && role != BasisBoneTrackedRole.RightUpperLeg

                && role != BasisBoneTrackedRole.RightUpperArm
                && role != BasisBoneTrackedRole.LeftUpperArm

                && role != BasisBoneTrackedRole.CenterEye
                && role != BasisBoneTrackedRole.Head
                && role != BasisBoneTrackedRole.Neck
                && role != BasisBoneTrackedRole.Mouth
                && role != BasisBoneTrackedRole.Spine;
        }
    }
}
