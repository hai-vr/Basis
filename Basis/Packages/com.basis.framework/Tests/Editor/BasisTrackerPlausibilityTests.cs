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

        static float ExpectedReach(Vector3 dir, float clavicleLength = 0.13f)
        {
            float c = Mathf.Clamp(clavicleLength, 0f, k_Upper * 0.45f);
            float seg = Mathf.Max(k_Upper - c, 1e-5f);
            float cr = Vector3.Dot(Vector3.right, dir);
            float root = c * c * (cr * cr - 1f) + seg * seg;
            return c * cr + Mathf.Sqrt(root > 0f ? root : 0f);
        }

        static readonly Vector3[] k_Directions =
        {
            new Vector3(1f, 0f, 0f),
            new Vector3(0.05f, -0.95f, 0.1f),
            new Vector3(0.3f, -0.5f, 0.8f),
            new Vector3(0.2f, -0.3f, -0.9f),
            new Vector3(0.5f, 0.7f, 0.2f),
        };

        [Test]
        public void ACorrectlyPlacedElbow_IsUntouchedAtEveryDirection()
        {
            foreach (Vector3 raw in k_Directions)
            {
                Vector3 dir = raw.normalized;
                float expected = ExpectedReach(dir);
                Vector3 hand = dir * 0.34f;

                BasisShoulderSolveCore.Solve(ShoulderInput(dir * expected, hand, k_Upper),
                    out BasisShoulderSolveResult atExpected);
                BasisShoulderSolveCore.Solve(ShoulderInput(dir * (expected * 0.7f), hand, k_Upper),
                    out BasisShoulderSolveResult closer);

                Assert.AreEqual(closer.AppliedAngleDeg, atExpected.AppliedAngleDeg, 1e-3f,
                    $"dir {dir}: the gate must not fire on a reachable elbow. The straight-arm reach is " +
                    "pose-dependent, so comparing against the flat T-pose length reads a phantom overshoot.");
            }
        }

        [Test]
        public void AnElbowBeyondAnyReachablePosition_LosesGirdleAuthority()
        {
            Vector3 dir = new Vector3(0.3f, -0.5f, 0.8f).normalized;
            Vector3 hand = dir * 0.34f;
            float expected = ExpectedReach(dir);

            BasisShoulderSolveCore.Solve(ShoulderInput(dir * expected, hand, k_Upper),
                out BasisShoulderSolveResult good);
            BasisShoulderSolveCore.Solve(ShoulderInput(dir * (expected * 2f), hand, k_Upper),
                out BasisShoulderSolveResult bogus);

            Assert.Greater(good.AppliedAngleDeg, 0f, "the reachable case must still drive the girdle");
            Assert.Less(bogus.AppliedAngleDeg, good.AppliedAngleDeg,
                "an elbow twice as far as any pose allows must lose authority, not keep it");
        }

        [Test]
        public void TheOvershootGate_HandsOverSmoothlyAndMonotonically()
        {
            Vector3 dir = new Vector3(0.3f, -0.5f, 0.8f).normalized;
            Vector3 hand = dir * 0.34f;
            float expected = ExpectedReach(dir);

            float prev = float.NaN;
            float worstStep = 0f;

            for (float f = 0.5f; f <= 3f; f += 0.01f)
            {
                BasisShoulderSolveCore.Solve(ShoulderInput(dir * (expected * f), hand, k_Upper),
                    out BasisShoulderSolveResult r);

                if (!float.IsNaN(prev))
                {
                    worstStep = Mathf.Max(worstStep, Mathf.Abs(r.AppliedAngleDeg - prev));
                    Assert.LessOrEqual(r.AppliedAngleDeg, prev + 1e-4f,
                        "authority must never increase as the elbow moves further out of reach");
                }
                prev = r.AppliedAngleDeg;
            }

            Assert.Less(worstStep, 1.0f, "handover must be smooth, never a step");
        }

        [Test]
        public void TheNearSide_IsNeverGated_SoTheShrugSurvives()
        {
            Vector3 dir = new Vector3(0.05f, -0.95f, 0.1f).normalized;
            float expected = ExpectedReach(dir, 0.020f);
            float maxShrug = 0f;

            for (float f = 0.30f; f <= 1f; f += 0.02f)
            {
                BasisShoulderSolveCore.Solve(ShoulderInput(dir * (expected * f), dir * 0.30f, k_Upper, 0.020f),
                    out BasisShoulderSolveResult r);
                maxShrug = Mathf.Max(maxShrug, r.ShrugDeg);
            }

            Assert.Greater(maxShrug, 0f,
                "a shrug can only bring the elbow CLOSER, so the near side must stay ungated");
        }
    }
}
