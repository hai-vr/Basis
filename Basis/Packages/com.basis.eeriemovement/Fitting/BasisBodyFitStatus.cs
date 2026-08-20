namespace Basis.IK
{
    public enum BasisBodyFitStatus
    {
        Fitted,
        Disabled,
        PlayerEyeHeightMissing,
        AvatarEyeHeightMissing,
        PlayerArmSpanMissing,
        AvatarArmSpanMissing,
        ArmLengthDegenerate,
        ArmRatioOutOfBand,
        HipsTrackerMissing,
        HipHeightImplausible,
        AvatarHipHeightMissing,
        AvatarLegSpanDegenerate,
        AvatarSpineSpanDegenerate,
        HipRatioOutOfBand,
    }
}
