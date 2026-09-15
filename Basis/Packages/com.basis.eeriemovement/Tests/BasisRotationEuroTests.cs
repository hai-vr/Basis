using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Basis.Scripts.Drivers;
namespace Basis.Tests.IK
{
    public class BasisRotationEuroTests
    {
        const float dt = 1f / 90f;
        static float Yaw(quaternion q) => Mathf.DeltaAngle(0f, ((Quaternion)q).eulerAngles.y);
        static quaternion YawQ(float deg) => quaternion.AxisAngle(math.up(), deg * Mathf.Deg2Rad);
        [Test]
        public void StepConvergesWithoutOvershoot()
        {
            var st = default(BasisEuroQuatState);
            for (int i = 0; i < 10; i++) BasisFilterMath.EuroQuat(ref st, quaternion.identity, dt, 1f, 0f, 1f);
            float prev = 0f;
            for (int i = 0; i < 270; i++)
            {
                float yaw = Yaw(BasisFilterMath.EuroQuat(ref st, YawQ(60f), dt, 1f, 0f, 1f));
                Assert.GreaterOrEqual(yaw, prev - 1e-3f, $"frame {i}: yaw went backwards ({prev:F4} -> {yaw:F4})");
                Assert.LessOrEqual(yaw, 60f + 1e-3f, $"frame {i}: overshot the target ({yaw:F4})");
                prev = yaw;
            }
            Assert.AreEqual(60f, prev, 0.5f, "did not converge to the step target");
        }
        [Test]
        public void RampThenHoldDoesNotRing()
        {
            var st = default(BasisEuroQuatState);
            const float minCutoff = 2.2f, beta = 3.25f, dCutoff = 1.2f, rateDeg = 180f, holdDeg = 90f;
            int rampFrames = Mathf.RoundToInt(holdDeg / rateDeg / dt);
            float yaw = 0f;
            for (int i = 0; i <= rampFrames; i++) yaw = Yaw(BasisFilterMath.EuroQuat(ref st, YawQ(Mathf.Min(holdDeg, rateDeg * i * dt)), dt, minCutoff, beta, dCutoff));
            float err = holdDeg - yaw;
            Assert.Greater(err, 0f, "filter should trail the ramp when the hold begins");
            for (int i = 0; i < 180; i++)
            {
                yaw = Yaw(BasisFilterMath.EuroQuat(ref st, YawQ(holdDeg), dt, minCutoff, beta, dCutoff));
                float next = holdDeg - yaw;
                Assert.GreaterOrEqual(next, -1e-3f, $"hold frame {i}: overshot the held pose by {-next:F3} deg");
                Assert.LessOrEqual(next, err + 1e-3f, $"hold frame {i}: error grew from {err:F4} to {next:F4} deg");
                err = next;
            }
            Assert.Less(err, 0.05f, "did not settle onto the held pose");
        }
        [Test]
        public void HemisphereFlipOfTheSameRotationIsInert()
        {
            var st = default(BasisEuroQuatState);
            quaternion q = YawQ(40f), negQ = new quaternion(-q.value);
            for (int i = 0; i < 30; i++) BasisFilterMath.EuroQuat(ref st, q, dt, 1f, 0f, 1f);
            for (int i = 0; i < 60; i++)
            {
                quaternion o = BasisFilterMath.EuroQuat(ref st, (i & 1) == 0 ? negQ : q, dt, 1f, 0f, 1f);
                Assert.AreEqual(40f, Yaw(o), 0.05f, $"frame {i}: sign flip of the input moved the output");
                Assert.AreEqual(1f, math.length(o.value), 1e-4f, $"frame {i}: output left the unit sphere");
            }
        }
        static float YawDeg(quaternion q)
        {
            if (q.value.w < 0f) q = new quaternion(-q.value);
            return 2f * math.degrees(math.atan2(q.value.y, q.value.w));
        }
        static bool Unit(quaternion q)
        {
            float lengthSq = math.lengthsq(q.value);
            return math.isfinite(lengthSq) && math.abs(lengthSq - 1f) < 1e-3f;
        }
        [Test]
        public void ZeroQuaternionAtSeedDoesNotLatchNaN()
        {
            var st = default(BasisEuroQuatState);
            for (int i = 0; i < 3; i++) Assert.IsTrue(Unit(BasisFilterMath.EuroQuat(ref st, default, dt, 1f, 0f, 1f)), $"frame {i}: zero input produced a non-unit output");
            float yaw = 0f;
            for (int i = 0; i < 270; i++)
            {
                quaternion o = BasisFilterMath.EuroQuat(ref st, YawQ(60f), dt, 1f, 0f, 1f);
                Assert.IsTrue(Unit(o), $"frame {i}: output left the unit sphere after a zero seed");
                yaw = YawDeg(o);
            }
            Assert.AreEqual(60f, yaw, 0.5f, "did not converge after a zero seed");
        }
        [Test]
        public void NonFiniteInputHoldsTheLastRotation()
        {
            var st = default(BasisEuroQuatState);
            for (int i = 0; i < 60; i++) BasisFilterMath.EuroQuat(ref st, YawQ(40f), dt, 1f, 0f, 1f);
            quaternion nan = new quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
            for (int i = 0; i < 30; i++)
            {
                quaternion o = BasisFilterMath.EuroQuat(ref st, (i & 1) == 0 ? nan : default, dt, 1f, 0f, 1f);
                Assert.IsTrue(Unit(o), $"frame {i}: garbage input leaked out");
                Assert.AreEqual(40f, YawDeg(o), 0.05f, $"frame {i}: garbage input moved the held rotation");
            }
            Assert.AreEqual(40f, YawDeg(BasisFilterMath.EuroQuat(ref st, YawQ(40f), dt, 1f, 0f, 1f)), 0.05f, "valid input after garbage did not resume from the held rotation");
        }
        [Test]
        public void PoisonedStateSelfHeals()
        {
            var st = new BasisEuroQuatState { hasPrev = true, prev = new quaternion(float.NaN, 0f, 0f, float.NaN) };
            float yaw = 0f;
            for (int i = 0; i < 270; i++)
            {
                quaternion o = BasisFilterMath.EuroQuat(ref st, YawQ(25f), dt, 1f, 0f, 1f);
                Assert.IsTrue(Unit(o), $"frame {i}: poisoned state was not discarded");
                yaw = YawDeg(o);
            }
            Assert.AreEqual(25f, yaw, 0.5f, "did not converge after discarding the poisoned state");
        }
        [Test]
        public void NonFinitePositionHoldsTheLastValue()
        {
            var st = default(BasisEuroVec3State);
            float3 target = new float3(1f, 2f, 3f);
            for (int i = 0; i < 60; i++) BasisFilterMath.EuroVec3(ref st, target, dt, 1f, 0f, 1f);
            float3 held = BasisFilterMath.EuroVec3(ref st, new float3(float.NaN, 0f, 0f), dt, 1f, 0f, 1f);
            Assert.IsTrue(math.all(math.isfinite(held)) && math.distance(held, target) < 1e-3f, "non-finite position input leaked out");
            Assert.Less(math.distance(BasisFilterMath.EuroVec3(ref st, target, dt, 1f, 0f, 1f), target), 1e-3f, "position filter did not resume after non-finite input");
        }
    }
}
