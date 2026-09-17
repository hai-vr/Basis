using Basis.IK;
using NUnit.Framework;
using UnityEngine;
namespace Basis.Tests.IK
{
    public class BasisLegRestBendPlaneTests
    {
        const float thighLen = 0.42f, shinLen = 0.40f, restOffset = 0.0175f, rollToleranceDeg = 4f;
        static readonly Vector3 hip = new Vector3(0f, 0.9f, 0f), bendNormal = Vector3.right, target = new Vector3(0f, 0.25f, 0.10f), hint = new Vector3(0f, 0.5f, 0.6f);
        static BasisLegSolveInput Rest(float kneeForward, float kneeOut, Vector3 normal)
        {
            Vector3 knee = hip + new Vector3(kneeOut, -1f, kneeForward).normalized * thighLen, ankle = knee + new Vector3(-kneeOut, -1f, -kneeForward).normalized * shinLen;
            return new BasisLegSolveInput { Root = hip, Mid = knee, Tip = ankle, RootRotation = Quaternion.identity, MidRotation = Quaternion.identity, TargetPosition = target, TargetRotation = Quaternion.identity, TargetOffset = Quaternion.identity, HintPosition = hint, HintWeight = 1f, BendNormal = normal, AnteriorNormal = normal };
        }
        static BasisLegSolveResult Solve(float kneeForward, float kneeOut, Vector3 normal)
        {
            BasisLegSolveCore.Solve(Rest(kneeForward, kneeOut, normal), out BasisLegSolveResult r);
            return r;
        }
        static float TwistDeg(Quaternion q, Vector3 axis)
        {
            float s = q.x * axis.x + q.y * axis.y + q.z * axis.z, c = q.w;
            if (c < 0f) { s = -s; c = -c; }
            return 2f * Mathf.Atan2(s, c) * Mathf.Rad2Deg;
        }
        static float KneeAngle(float ac, float ab, float bc) => Mathf.Acos(Mathf.Clamp((ab * ab + bc * bc - ac * ac) / (2f * ab * bc), -1f, 1f));
        [TestCase(-restOffset, 0f, TestName = "knee 1 deg backward")]
        [TestCase(0f, restOffset, TestName = "knee 1 deg outward")]
        [TestCase(0f, -restOffset, TestName = "knee 1 deg inward")]
        [TestCase(restOffset, restOffset, TestName = "knee 1 deg forward and 1 deg outward")]
        [TestCase(0f, 0f, TestName = "knee dead straight")]
        [TestCase(-0.12f, 0.07f, TestName = "knee hyperextended 7 deg and 4 deg outward")]
        public void ThighAndShinRoll_DoNotDependOnTheRestBendDirection(float kneeForward, float kneeOut)
        {
            BasisLegSolveResult reference = Solve(restOffset, 0f, bendNormal), r = Solve(kneeForward, kneeOut, bendNormal);
            Assert.Less(Vector3.Distance(reference.FootSolved, target), 1e-3f, "the forward-rest reference must reach the target");
            Assert.Less(Vector3.Distance(r.FootSolved, target), 1e-3f, "the foot must still reach the target");
            Assert.Less(Vector3.Distance(r.KneeSolved, reference.KneeSolved), 5e-3f, "the knee must land where the forward-rest solve puts it");
            Vector3 thighAxis = (r.KneeSolved - hip).normalized, shinAxis = (r.FootSolved - r.KneeSolved).normalized;
            float thigh = TwistDeg(r.RootRotationSolved * Quaternion.Inverse(reference.RootRotationSolved), thighAxis), shin = TwistDeg(r.MidRotationSolved * Quaternion.Inverse(reference.MidRotationSolved), shinAxis);
            Assert.Less(Mathf.Abs(thigh), rollToleranceDeg, $"thigh rolled {thigh:F1} deg relative to the forward-rest solve: the rest bend direction leaked into the roll");
            Assert.Less(Mathf.Abs(shin), rollToleranceDeg, $"shin rolled {shin:F1} deg relative to the forward-rest solve: the rest bend direction leaked into the roll");
            Assert.Greater(Vector3.Dot(r.MidRotationSolved * Vector3.forward, Vector3.forward), 0.5f, "the shin must keep facing forward");
        }
        [Test]
        public void ForwardRestBend_KeepsTheRestPlaneExactly()
        {
            BasisLegSolveInput input = Rest(restOffset, 0f, bendNormal);
            BasisLegSolveCore.Solve(input, out BasisLegSolveResult r);
            Vector3 ab = input.Mid - input.Root, bc = input.Tip - input.Mid;
            float oldAbc = KneeAngle((input.Tip - input.Root).magnitude, ab.magnitude, bc.magnitude), newAbc = KneeAngle((target - input.Root).magnitude, ab.magnitude, bc.magnitude);
            Quaternion legacy = Quaternion.AngleAxis((oldAbc - newAbc) * Mathf.Rad2Deg, Vector3.Cross(ab, bc).normalized);
            Assert.Less(Quaternion.Angle(r.MidDelta, legacy), 1e-3f, "a rest knee already bent about the bend normal must solve exactly as before");
            Assert.AreEqual(0, r.AxisSource, "the rest plane is the bend plane when it agrees with the bend normal");
        }
        [Test]
        public void StraightRest_WithoutABendNormal_StillBendsTowardTheHint()
        {
            BasisLegSolveResult r = Solve(0f, 0f, Vector3.zero);
            Assert.Less(Vector3.Distance(r.FootSolved, target), 1e-3f, "the foot must reach the target");
            Assert.Greater(Vector3.Dot((r.KneeSolved - hip).normalized, Vector3.forward), 0.2f, "the knee must bend toward the hint");
            Assert.AreEqual(1, r.AxisSource, "without a bend normal the hint defines the plane");
        }
    }
}
