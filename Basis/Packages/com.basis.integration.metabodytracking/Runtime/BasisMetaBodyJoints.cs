#if BASIS_FRAMEWORK_EXISTS
namespace Basis.Integration.MetaBodyTracking
{
    /// <summary>
    /// Joint indices of the body skeletons Meta's OpenXR body tracking reports. The first 70 entries
    /// are <c>XrBodyJointFB</c> from XR_FB_body_tracking; XR_META_body_tracking_full_body keeps those
    /// indices and appends the legs and feet, so one enum covers both joint sets and a leg index is
    /// simply out of range when only the default set is running.
    /// </summary>
    public enum BasisMetaBodyJoint
    {
        Root = 0,
        Hips = 1,
        SpineLower = 2,
        SpineMiddle = 3,
        SpineUpper = 4,
        Chest = 5,
        Neck = 6,
        Head = 7,

        LeftShoulder = 8,
        LeftScapula = 9,
        LeftArmUpper = 10,
        LeftArmLower = 11,
        LeftHandWristTwist = 12,

        RightShoulder = 13,
        RightScapula = 14,
        RightArmUpper = 15,
        RightArmLower = 16,
        RightHandWristTwist = 17,

        LeftHandPalm = 18,
        LeftHandWrist = 19,
        RightHandPalm = 44,
        RightHandWrist = 45,

        // XR_META_body_tracking_full_body only.
        LeftUpperLeg = 70,
        LeftLowerLeg = 71,
        LeftFootAnkleTwist = 72,
        LeftFootAnkle = 73,
        LeftFootSubtalar = 74,
        LeftFootTransverse = 75,
        LeftFootBall = 76,

        RightUpperLeg = 77,
        RightLowerLeg = 78,
        RightFootAnkleTwist = 79,
        RightFootAnkle = 80,
        RightFootSubtalar = 81,
        RightFootTransverse = 82,
        RightFootBall = 83,
    }

    /// <summary>Sizes of the two joint sets, for allocating the locate buffer.</summary>
    public static class BasisMetaBodyJointCount
    {
        /// <summary>XR_BODY_JOINT_SET_DEFAULT_FB: upper body and both hands.</summary>
        public const int Default = 70;

        /// <summary>XR_BODY_JOINT_SET_FULL_BODY_META: the above plus legs and feet.</summary>
        public const int FullBody = 84;
    }

    /// <summary>Which joint set the runtime is currently tracking.</summary>
    public enum BasisMetaBodyJointSet
    {
        /// <summary>No tracker running.</summary>
        None = 0,
        /// <summary>XR_BODY_JOINT_SET_DEFAULT_FB — upper body only, no legs.</summary>
        UpperBody = 1,
        /// <summary>XR_BODY_JOINT_SET_FULL_BODY_META — upper body plus legs and feet.</summary>
        FullBody = 2,
    }
}
#endif
