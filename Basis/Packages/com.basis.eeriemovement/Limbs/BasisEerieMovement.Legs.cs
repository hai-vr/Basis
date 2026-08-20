using System.Runtime.CompilerServices;
using Basis.Scripts.Common;
using Unity.Collections;
using UnityEngine;
namespace Basis.IK
{
    public partial struct BasisEerieMovement
    {
        void SolveLegPass()
        {
            SolveLeg(0);
            SolveLeg(1);
        }
        void SolveToePass()
        {
            if (leftToeEnabled) ApplyRotation(true, handleLeftToe, leftDrivenTargetRot, offsetRotationLeftToe);
            else ApplyToeSurfaceBend(handleLeftToe, leftToeBendDeg, leftToeBendAxis);

            if (rightToeEnabled) ApplyRotation(true, handleRightToe, rightDrivenTargetRot, offsetRotationRightToe);
            else ApplyToeSurfaceBend(handleRightToe, rightToeBendDeg, rightToeBendAxis);
        }
        BasisSwivelFrame BuildLegFrame()
        {
            if (!poseStream.IsValid(handleLeftUpperLeg) || !poseStream.IsValid(handleRightUpperLeg) || !poseStream.IsValid(handleHips))
            {
                return default;
            }

            BasisBoneHandle upTo = poseStream.IsValid(handleChest) ? handleChest : poseStream.IsValid(handleSpine) ? handleSpine : poseStream.IsValid(handleNeck) ? handleNeck : handleHead;
            if (!poseStream.IsValid(upTo))
            {
                return default;
            }

            return BasisSwivelHintCore.BuildFrame( poseStream.GetPosition(handleLeftUpperLeg), poseStream.GetPosition(handleRightUpperLeg), poseStream.GetPosition(handleHips), poseStream.GetPosition(upTo));
        }
        public void SolveLeg(int legSlot)
        {
            bool isLeft = legSlot == 0;
            float posWeight = isLeft ? enabledLeftLowerLeg : enabledRightLowerLeg;
            if (posWeight <= 0f)
            {
                return;
            }

            BasisBoneHandle root = isLeft ? handleLeftUpperLeg : handleRightUpperLeg;
            BasisBoneHandle mid = isLeft ? handleLeftLowerLeg : handleRightLowerLeg;
            BasisBoneHandle tip = isLeft ? handleLeftFoot : handleRightFoot;
            if (!(poseStream.IsValid(root) && poseStream.IsValid(mid) && poseStream.IsValid(tip)))
            {
                return;
            }
            Vector3 hintPos = isLeft ? hintPositionLeftLowerLeg : hintPositionRightLowerLeg;
            Quaternion hintRot = isLeft ? hintRotationLeftLowerLeg : hintRotationRightLowerLeg;
            float hintW = isLeft ? hintWeightLeftLowerLeg : hintWeightRightLowerLeg;
            Quaternion targetOffset = isLeft ? offsetRotationLeftFoot : offsetRotationRightFoot;
            Vector3 bendNormal = isLeft ? kneeBendPrefLeft : kneeBendPrefRight;
            bool hintIsTracker = isLeft ? hintIsTrackerLeftLowerLeg : hintIsTrackerRightLowerLeg;
            bool footIsTracker = isLeft ? footIsTrackerLeftLeg : footIsTrackerRightLeg;
            Quaternion origRootRot = poseStream.GetRotation(root), origMidRot = poseStream.GetRotation(mid);
            Quaternion origTipRot = poseStream.GetRotation(tip);
            Quaternion tRot = isLeft ? targetRotationLeftLowerLeg : targetRotationRightLowerLeg;
            float tRotSqrLen = tRot.x * tRot.x + tRot.y * tRot.y + tRot.z * tRot.z + tRot.w * tRot.w;
            bool preserveTip = !(tRotSqrLen > 0.5f);
            if (preserveTip) tRot = origTipRot;

            Vector3 targetPos = isLeft ? targetPositionLeftLowerLeg : targetPositionRightLowerLeg, hint = hintPos;
            float hintDistrust = 0f;
            bool usedModelHint = false, fabricatedLeg = !hintIsTracker && !footIsTracker;
            if (!(hintW > 0f) || fabricatedLeg)
            {
                BasisSwivelFrame frame = BuildLegFrame();
                Vector3 hipPos = poseStream.GetPosition(root);
                float upperLen = (poseStream.GetPosition(mid) - hipPos).magnitude;
                float lowerLen = (poseStream.GetPosition(tip) - poseStream.GetPosition(mid)).magnitude;
                float legLen = upperLen + lowerLen;
                if (BasisSwivelHintCore.LegHint(frame, hipPos, targetPos, legLen, isLeft, out Vector3 modelHint, out float conf))
                {
                    hint = modelHint;
                    hintW = 1f;
                    usedModelHint = true;
                    if (legDiagnostics.IsCreated && legSlot < legDiagnostics.Length)
                    {
                        ref BasisLegDiagnostics d = ref Ref(legDiagnostics, legSlot);
                        d.ModelHintUsed = 1f;
                        d.ModelConfidence = conf;
                    }
                    hintDistrust = 1f - BasisSwivelHintCore.LegModelTrust(conf);
                }
            }

            BasisLegSolveInput input = default;
            poseStream.GetPositionAndRotation(root, out Vector3 rootPos, out Quaternion rootRot);
            poseStream.GetPositionAndRotation(mid, out Vector3 midPos, out Quaternion midRot);
            input.Root = rootPos;
            input.Mid = midPos;
            input.Tip = poseStream.GetPosition(tip);
            input.RootRotation = rootRot;
            input.MidRotation = midRot;
            input.TargetPosition = targetPos;
            input.TargetRotation = tRot;
            input.HintPosition = hint;
            input.HintWeight = hintW;
            input.HintDistrust = hintDistrust;
            input.TargetOffset = targetOffset;
            input.BendNormal = bendNormal;
            input.AnteriorNormal = kneeAnteriorRef;
            input.HintRotation = hintIsTracker ? hintRot : default;
            input.HintIsTracker = hintIsTracker;

            BasisLegSolveCore.Solve(input, out BasisLegSolveResult result);

            if (legDiagnostics.IsCreated && legSlot < legDiagnostics.Length)
            {
                ref BasisLegDiagnostics d = ref Ref(legDiagnostics, legSlot);
                d.ReachRatio = result.ReachRatio;
                d.KneeAngleDeg = result.KneeAngleDeg;
                d.AxisSource = result.AxisSource;
                d.HintApplied = result.HintApplied ? 1f : 0f;
                d.HintDistrust = hintDistrust;
                d.ShinRollDeg = result.ShinRollDeg;
            }

            poseStream.SetRotation(mid, result.MidDelta * poseStream.GetRotation(mid));
            poseStream.SetRotation(root, result.RootDelta * poseStream.GetRotation(root));
            poseStream.SetRotation(root, result.HintDelta * poseStream.GetRotation(root));
            poseStream.SetRotation(mid, result.MidPostRoll * poseStream.GetRotation(mid));
            poseStream.SetRotation(tip, result.TipRotation);
            Quaternion shinRoll = result.MidPostRoll;

            if (posWeight < 1f)
            {
                poseStream.SetRotation(root, Quaternion.Slerp(origRootRot, poseStream.GetRotation(root), posWeight));
                poseStream.SetRotation(mid, Quaternion.Slerp(origMidRot, poseStream.GetRotation(mid), posWeight));
                poseStream.SetRotation(tip, Quaternion.Slerp(origTipRot, poseStream.GetRotation(tip), posWeight));
            }
            if (preserveTip)
            {
                Quaternion carriedTip = shinRoll * origTipRot;
                poseStream.SetRotation(tip, posWeight < 1f ? Quaternion.Slerp(origTipRot, carriedTip, posWeight) : carriedTip);
            }

            RecordHipDiagnostics(root, mid, legSlot);
            if (legSwivelSmoothing)
            {
                if (hintIsTracker || footIsTracker)
                {
                    bool footDerivedPole = !hintIsTracker && footIsTracker && !usedModelHint;
                    SmoothKneeSwivel(root, mid, tip, legSlot, trackedKneeSwivelMinCutoffHz, trackedKneeSwivelBeta, trackedKneeSwivelDerivCutoffHz, conditionOnPole: !hintIsTracker && (!footDerivedPole || kneeFootPoleConditioning), holdWhenSingular: !footDerivedPole || kneeFootPoleHold);
                }
                else
                {
                    SmoothKneeSwivel(root, mid, tip, legSlot, BasisSwivelFilterCore.MinCutoffHz, BasisSwivelFilterCore.Beta, BasisSwivelFilterCore.DerivCutoffHz, conditionOnPole: true, holdWhenSingular: true);
                }
            }
        }
        void RecordHipDiagnostics(BasisBoneHandle root, BasisBoneHandle mid, int slot)
        {
            if (!legDiagnostics.IsCreated || slot < 0 || slot >= legDiagnostics.Length || !poseStream.IsValid(handleHips))
            {
                return;
            }

            Vector3 femur = poseStream.GetPosition(mid) - poseStream.GetPosition(root);
            if (!(femur.sqrMagnitude > 1e-8f))
            {
                return;
            }

            Quaternion hipsRot = poseStream.GetRotation(handleHips), hipsInv = Quaternion.Inverse(hipsRot);
            Vector3 femurLocal = (hipsInv * femur).normalized;

            ref BasisLegDiagnostics d = ref Ref(legDiagnostics, slot);
            d.HipFlexionDeg = Mathf.Atan2(femurLocal.z, -femurLocal.y) * Mathf.Rad2Deg;
            d.HipAbductionDeg = Mathf.Atan2(femurLocal.x, -femurLocal.y) * Mathf.Rad2Deg;
            d.FemurTwistDeg = TwistDeg(hipsInv * poseStream.GetRotation(root), femurLocal);
        }
        void SmoothKneeSwivel(BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, int slot, float minCutoffHz, float beta, float derivCutoffHz, bool conditionOnPole, bool holdWhenSingular)
        {
            if (!legState.IsCreated || (uint)slot >= (uint)legState.Length || !poseStream.IsValid(handleHips))
            {
                return;
            }
            ref BasisLegSlotState leg = ref Ref(legState, slot);
            BasisSwivelSmootherInput input = default;
            input.Root = poseStream.GetPosition(root);
            input.Mid = poseStream.GetPosition(mid);
            input.Tip = poseStream.GetPosition(tip);
            input.BodyRotation = poseStream.GetRotation(handleHips);
            input.ReferenceLocal = Vector3.forward;
            input.FallbackLocal = Vector3.right;
            input.TransportHomeLocal = Vector3.down;
            input.Dt = poseStream.deltaTime;
            input.MinCutoffHz = minCutoffHz;
            input.Beta = beta;
            input.DerivCutoffHz = derivCutoffHz;
            input.ConditionOnPole = conditionOnPole;
            input.SingularMinCutoffHz = BasisSwivelFilterCore.MinCutoffHz;
            input.GuardAnteriorHalfSpace = true;
            input.AnteriorSoftDeg = BasisLegSolveCore.KneeAnteriorSoftDeg;
            input.AnteriorHardDeg = BasisLegSolveCore.KneeAnteriorHardDeg;
            input.HoldWhenSingular = holdWhenSingular;
            input.HoldCondLo = BasisSwivelSmootherCore.DefaultHoldCondLo;
            input.HoldCondHi = BasisSwivelSmootherCore.DefaultHoldCondHi;
            input.State = leg.Swivel;
            input.Seeded = leg.SwivelSeeded;

            BasisSwivelSmootherCore.Solve(input, out BasisSwivelSmootherResult result);
            if (legDiagnostics.IsCreated && slot < legDiagnostics.Length)
            {
                ref BasisLegDiagnostics d = ref Ref(legDiagnostics, slot);
                d.RawSwivelDeg = result.RawSwivelDeg;
                d.SmoothSwivelDeg = result.SmoothSwivelDeg;
                d.Conditioning = result.Conditioning;
                d.HoldGate = result.HoldGate;
                d.AnteriorGuardApplied = result.AnteriorGuardApplied ? 1f : 0f;
                d.Seeded = result.Seeded ? 1f : 0f;
            }
            if (result.WriteState)
            {
                leg.Swivel = result.State;
                leg.SwivelSeeded = true;
            }
            if (!result.Valid)
            {
                return;
            }

            Vector3 preFoot = input.Tip;
            Quaternion preFootRot = poseStream.GetRotation(tip);
            SwingElbowAroundAC(root, mid, tip, result.DesiredMid);
            poseStream.SetPosition(tip, preFoot);
            poseStream.SetRotation(tip, preFootRot);
        }
        public void ApplyToeSurfaceBend(BasisBoneHandle handle, float bendDeg, Vector3 axis)
        {
            if (!poseStream.IsValid(handle)) return;
            if (Mathf.Abs(bendDeg) < 0.01f || axis.sqrMagnitude < 1e-6f) return;

            Quaternion current = poseStream.GetRotation(handle);
            poseStream.SetRotation(handle, Quaternion.AngleAxis(-bendDeg, axis.normalized) * current);
        }
    }
}
