using Basis.Scripts.Common;

namespace Basis.IK
{
    public static class BasisBodyFitApply
    {
        public static void Apply(BasisPoseSkeleton skeleton, BasisTransformMapping mapping, in BasisBodyFitResult fit)
        {
            if (skeleton == null || !skeleton.IsCreated || mapping == null)
            {
                return;
            }

            skeleton.ResetFit();

            if (fit.HasArmFit)
            {
                float arm = fit.ArmScale;
                skeleton.SetFitScale(mapping.leftLowerArm, arm);
                skeleton.SetFitScale(mapping.leftHand, arm);
                skeleton.SetFitScale(mapping.leftUpperArmTwist, arm);
                skeleton.SetFitScale(mapping.leftLowerArmTwist, arm);

                skeleton.SetFitScale(mapping.RightLowerArm, arm);
                skeleton.SetFitScale(mapping.rightHand, arm);
                skeleton.SetFitScale(mapping.RightUpperArmTwist, arm);
                skeleton.SetFitScale(mapping.RightLowerArmTwist, arm);
            }

            if (fit.HasBodyFit)
            {
                float leg = fit.LegScale;
                skeleton.SetFitScale(mapping.LeftLowerLeg, leg);
                skeleton.SetFitScale(mapping.leftFoot, leg);
                skeleton.SetFitScale(mapping.RightLowerLeg, leg);
                skeleton.SetFitScale(mapping.rightFoot, leg);

                float torso = fit.TorsoScale;
                skeleton.SetFitScale(mapping.spine, torso);
                skeleton.SetFitScale(mapping.chest, torso);
                skeleton.SetFitScale(mapping.Upperchest, torso);
                skeleton.SetFitScale(mapping.neck, torso);
                skeleton.SetFitScale(mapping.head, torso);
            }
        }
    }
}
