using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    public sealed class BasisTrackerPlausibilityTests
    {
        static readonly Vector3 k_Shoulder = new Vector3(0.17f, 1.40f, 0f);
        const float k_Upper = 0.30f;
        const float k_Fore = 0.30f;
        const float k_Arm = k_Upper + k_Fore;

        static BasisArmSolveInput ArmInput(Vector3 target, Vector3 hint, bool hintIsTracker)
        {
            BasisArmSolveInput i = default;
            i.Shoulder = k_Shoulder;
            i.Elbow = k_Shoulder + new Vector3(0.25f, -0.75f, 0.20f).normalized * k_Upper;
            i.Hand = i.Elbow + new Vector3(0.10f, -0.30f, 0.90f).normalized * k_Fore;
            i.RootRotation = Quaternion.identity;
            i.MidRotation = Quaternion.identity;
            i.TargetPosition = target;
            i.TargetRotation = Quaternion.identity;
            i.TargetOffset = Quaternion.identity;
            i.HintPosition = hint;
            i.HintWeight = true;
            i.PlayerUp = Vector3.up;
            i.HintMaxStepDeg = float.MaxValue;
            i.HintIsTracker = hintIsTracker;
            return i;
        }

        static float ElbowInteriorDeg(in BasisArmSolveResult r)
        {
            Vector3 ba = k_Shoulder - r.ElbowSolved;
            Vector3 bc = r.HandSolved - r.ElbowSolved;
            if (ba.sqrMagnitude < 1e-12f || bc.sqrMagnitude < 1e-12f) return 0f;
            float c = Vector3.Dot(ba.normalized, bc.normalized);
            c = c > 1f ? 1f : (c > -1f ? c : -1f);
            return Mathf.Acos(c) * Mathf.Rad2Deg;
        }

        [Test]
        public void AnImpossibleHintDistance_CannotChangeTheArmSolve()
        {
            Vector3 target = k_Shoulder + new Vector3(0.35f, -0.25f, 0.40f);
            Vector3 hintDir = new Vector3(0.5f, -0.8f, -0.2f).normalized;

            BasisArmSolveCore.Solve(ArmInput(target, k_Shoulder + hintDir * (0.5f * k_Arm), false),
                out BasisArmSolveResult reference);

            float[] distances = { 0.05f, 0.15f, 0.3f, 0.6f, 1.2f, 5f, 20f, 50f };

            foreach (float d in distances)
            {
                BasisArmSolveCore.Solve(ArmInput(target, k_Shoulder + hintDir * d, false),
                    out BasisArmSolveResult r);

                Assert.AreEqual(ElbowInteriorDeg(reference), ElbowInteriorDeg(r), 1e-3f,
                    $"hint at {d} m changed the elbow angle; the swivel must read direction only");
                Assert.AreEqual(0f, (r.HandSolved - reference.HandSolved).magnitude, 1e-5f,
                    $"hint at {d} m moved the hand; the swivel must preserve reach");
                Assert.AreEqual(k_Upper, (r.ElbowSolved - k_Shoulder).magnitude, 1e-4f,
                    $"hint at {d} m stretched the upper arm");
                Assert.AreEqual(k_Fore, (r.HandSolved - r.ElbowSolved).magnitude, 1e-4f,
                    $"hint at {d} m stretched the forearm");
            }
        }

        [Test]
        public void TheForearmRollSoftLimit_SitsInsideTheHardBound_AndInsideAnatomy()
        {
            Assert.Less(BasisArmSolveCore.TrackerForearmRollSoftDeg, BasisArmSolveCore.TrackerForearmRollMaxDeg,
                "the soft knee must sit below the asymptote or there is no give");
            Assert.LessOrEqual(BasisArmSolveCore.TrackerForearmRollSoftDeg, 100f,
                "full pronation from a mid-range animated wrist is ~90-100 deg; a softer knee than that is not anatomy");
            Assert.GreaterOrEqual(BasisArmSolveCore.TrackerForearmRollSoftDeg, 80f,
                "below the wrist comfort band the forearm would be limited before the wrist is");
        }

        [Test]
        public void TrackerForearmRoll_SaturatesInsteadOfClamping()
        {
            float soft = BasisArmSolveCore.TrackerForearmRollSoftDeg;
            float hard = BasisArmSolveCore.TrackerForearmRollMaxDeg;

            Assert.AreEqual(45f, BasisJointLimitCore.Saturate(45f, soft, hard), 1e-4f,
                "a legal forearm roll must pass through untouched");

            for (float demand = soft; demand < 1000f; demand += 7.3f)
            {
                float got = BasisJointLimitCore.Saturate(demand, soft, hard);
                Assert.Less(got, hard, $"a {demand} deg roll demand escaped the asymptote");
                Assert.GreaterOrEqual(got, soft, $"a {demand} deg roll demand fell below the soft limit");
            }

            const float d = 1e-3f;
            float slopeIn = (BasisJointLimitCore.Saturate(soft - 0.01f + d, soft, hard)
                           - BasisJointLimitCore.Saturate(soft - 0.01f, soft, hard)) / d;
            float slopeOut = (BasisJointLimitCore.Saturate(soft + 0.01f + d, soft, hard)
                            - BasisJointLimitCore.Saturate(soft + 0.01f, soft, hard)) / d;

            Assert.AreEqual(slopeIn, slopeOut, 0.01f,
                "a derivative step here is the wrung-forearm pop the hard clamp produced");
        }

        static BasisShoulderSolveInput ShoulderInput(Vector3 elbowPos, Vector3 handTarget, float tposeElbowLength, float clavicleLength = 0.13f)
        {
            BasisShoulderSolveInput i = default;
            i.ShoulderPos = Vector3.zero;
            i.HandTargetPos = handTarget;
            i.ElbowPos = elbowPos;
            i.HasElbow = true;
            i.ChestRot = Quaternion.identity;
            i.TposeChestRot = Quaternion.identity;
            i.TposeShoulderRot = Quaternion.identity;
            i.TposeArmDirWorld = Vector3.right;
            i.TposeArmLength = k_Arm;
            i.TposeClavicleLength = clavicleLength;
            i.TposeElbowLength = tposeElbowLength;
            i.ShrugEnabled = true;
            i.RetractEnabled = true;
            i.ElevationFactor = 1f;
            i.ProtractionFactor = 0.3f;
            i.CoupleRatio = 0.4f;
            i.MaxShoulderDeg = 25f;
            i.TrackerFinal = Quaternion.identity;
            i.IsLeft = false;
            return i;
        }

        [Test]
        public void AWellCalibratedElbowTracker_KeepsFullGirdleAuthority()
        {
            Vector3 dir = new Vector3(0.3f, -0.5f, 0.8f).normalized;
            Vector3 elbow = dir * k_Upper;
            Vector3 hand = dir * 0.34f;

            BasisShoulderSolveCore.Solve(ShoulderInput(elbow, hand, k_Upper), out BasisShoulderSolveResult r);

            Assert.IsTrue(r.Apply);
            Assert.IsTrue(r.DriverIsElbow);
            Assert.Greater(r.AppliedAngleDeg, 0f,
                "a consistent tracker must still drive the girdle exactly as before");
        }

        [Test]
        public void AnImplausibleElbowTracker_FallsBackTowardTheReachGate()
        {
            Vector3 dir = new Vector3(0.3f, -0.5f, 0.8f).normalized;
            Vector3 hand = dir * 0.34f;

            BasisShoulderSolveCore.Solve(ShoulderInput(dir * k_Upper, hand, k_Upper),
                out BasisShoulderSolveResult trusted);
            BasisShoulderSolveCore.Solve(ShoulderInput(dir * (k_Upper * 2f), hand, k_Upper),
                out BasisShoulderSolveResult implausible);

            Assert.Less(implausible.AppliedAngleDeg, trusted.AppliedAngleDeg,
                "a tracker asserting a 2x upper arm must lose girdle authority, not keep it");
        }

        [Test]
        public void TheElbowTrustFallback_IsContinuousInTheMismatch()
        {
            Vector3 dir = new Vector3(0.3f, -0.5f, 0.8f).normalized;
            Vector3 hand = dir * 0.34f;

            float prev = float.NaN;
            float worstStep = 0f;

            for (float scale = 1f; scale <= 2.5f; scale += 0.01f)
            {
                BasisShoulderSolveCore.Solve(ShoulderInput(dir * (k_Upper * scale), hand, k_Upper),
                    out BasisShoulderSolveResult r);

                if (!float.IsNaN(prev))
                {
                    worstStep = Mathf.Max(worstStep, Mathf.Abs(r.AppliedAngleDeg - prev));
                }
                prev = r.AppliedAngleDeg;
            }

            Assert.Less(worstStep, 1.0f,
                "authority must hand over smoothly as the tracker becomes implausible, never in a step");
        }
    }
}
