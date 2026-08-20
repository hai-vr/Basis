using Basis.Scripts.Common;
using UnityEngine;
namespace Basis.IK
{
    public static class BasisBodyFitApply
    {
        public const int BoneCount = 17;
        public static void CollectBones(BasisTransformMapping mapping, Transform[] bones)
        {
            if (mapping == null || bones == null || bones.Length < BoneCount)
            {
                return;
            }

            bones[0] = mapping.leftLowerArm;
            bones[1] = mapping.leftHand;
            bones[2] = mapping.HasleftUpperArmTwist ? mapping.leftUpperArmTwist : null;
            bones[3] = mapping.HasleftLowerArmTwist ? mapping.leftLowerArmTwist : null;
            bones[4] = mapping.RightLowerArm;
            bones[5] = mapping.rightHand;
            bones[6] = mapping.HasRightUpperArmTwist ? mapping.RightUpperArmTwist : null;
            bones[7] = mapping.HasRightLowerArmTwist ? mapping.RightLowerArmTwist : null;

            bones[8] = mapping.LeftLowerLeg;
            bones[9] = mapping.leftFoot;
            bones[10] = mapping.RightLowerLeg;
            bones[11] = mapping.rightFoot;

            bones[12] = mapping.spine;
            bones[13] = mapping.chest;
            bones[14] = mapping.Upperchest;
            bones[15] = mapping.neck;
            bones[16] = mapping.head;
        }
        public static void CollectScales(in BasisBodyFitResult fit, float[] scales)
        {
            if (scales == null || scales.Length < BoneCount)
            {
                return;
            }

            float arm = fit.HasArmFit ? fit.ArmScale : 1f;
            for (int i = 0; i < 8; i++)
            {
                scales[i] = arm;
            }

            float leg = fit.HasBodyFit ? fit.LegScale : 1f;
            for (int i = 8; i < 12; i++)
            {
                scales[i] = leg;
            }

            float torso = fit.HasBodyFit ? fit.TorsoScale : 1f;
            for (int i = 12; i < BoneCount; i++)
            {
                scales[i] = torso;
            }
        }
        static readonly Transform[] sbones = new Transform[BoneCount];
        static readonly float[] sscales = new float[BoneCount];
        public static void Apply(BasisPoseSkeleton skeleton, BasisTransformMapping mapping, in BasisBodyFitResult fit)
        {
            if (skeleton == null || !skeleton.IsCreated || mapping == null)
            {
                return;
            }

            skeleton.ResetFit();

            CollectBones(mapping, sbones);
            CollectScales(in fit, sscales);

            for (int i = 0; i < BoneCount; i++)
            {
                if (sbones[i] != null && sscales[i] != 1f)
                {
                    skeleton.SetFitScale(sbones[i], sscales[i]);
                }
            }
        }
    }
}
