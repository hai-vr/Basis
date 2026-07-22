using System;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    public sealed class BasisJointLimitCoreTests
    {
        static readonly BasisJointRom k_Hip = new BasisJointRom(112f, 15f, 45f, 25f, 50f, 15f);
        static readonly BasisJointRom k_Lumbar = new BasisJointRom(60f, 20f, 25f, 25f, 12f);

        uint _state;

        [SetUp]
        public void Reset() => _state = 0x9E3779B9u;

        float Rand01()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state & 0xFFFFFF) / 16777216f;
        }

        float RandSym(float m) => (Rand01() * 2f - 1f) * m;

        Quaternion RandomUnitQuat()
        {
            float u1 = Rand01(), u2 = Rand01(), u3 = Rand01();
            float s1 = Mathf.Sqrt(1f - u1), s2 = Mathf.Sqrt(u1);
            float t1 = 2f * Mathf.PI * u2, t2 = 2f * Mathf.PI * u3;
            return new Quaternion(s1 * Mathf.Sin(t1), s1 * Mathf.Cos(t1), s2 * Mathf.Sin(t2), s2 * Mathf.Cos(t2));
        }

        static BasisJointRestFrame Frame(Quaternion rest) => new BasisJointRestFrame
        {
            RestLocalRot = rest,
            Right = new Vector3(1f, 0f, 0f),
            Up = new Vector3(0f, 1f, 0f),
            Forward = new Vector3(0f, 0f, 1f),
            Valid = true,
        };

        static bool BitEqual(Quaternion a, Quaternion b) =>
            BitConverter.SingleToInt32Bits(a.x) == BitConverter.SingleToInt32Bits(b.x) &&
            BitConverter.SingleToInt32Bits(a.y) == BitConverter.SingleToInt32Bits(b.y) &&
            BitConverter.SingleToInt32Bits(a.z) == BitConverter.SingleToInt32Bits(b.z) &&
            BitConverter.SingleToInt32Bits(a.w) == BitConverter.SingleToInt32Bits(b.w);

        [Test]
        public void BelowTheSoftLimit_SaturateIsTheExactIdentity()
        {
            for (int i = 0; i < 2000; i++)
            {
                float soft = 10f + Rand01() * 80f;
                float x = Rand01() * soft;
                Assert.AreEqual(x, BasisJointLimitCore.Saturate(x, soft, soft * 1.25f),
                    "Saturate must not perturb a value inside the limit.");
            }
        }

        [Test]
        public void PastTheLimit_SaturateNeverReachesTheHardStop()
        {
            for (int i = 0; i < 5000; i++)
            {
                float soft = 5f + Rand01() * 80f;
                float hard = soft * 1.25f;
                float g = BasisJointLimitCore.Saturate(soft + Rand01() * 10000f, soft, hard);
                Assert.Less(g, hard, "The asymptote is a limit, not a wall -- it must never be attained.");
            }
        }

        [Test]
        public void AcrossTheLimit_TheSlopeDoesNotStep()
        {
            const float soft = 60f, hard = 75f, d = 1e-3f;

            float slopeIn = (BasisJointLimitCore.Saturate(soft - 0.01f + d, soft, hard)
                           - BasisJointLimitCore.Saturate(soft - 0.01f, soft, hard)) / d;
            float slopeOut = (BasisJointLimitCore.Saturate(soft + 0.01f + d, soft, hard)
                            - BasisJointLimitCore.Saturate(soft + 0.01f, soft, hard)) / d;

            Assert.AreEqual(1f, slopeIn, 0.01f, "Inside the limit the joint must move one-for-one.");
            Assert.AreEqual(slopeIn, slopeOut, 0.01f,
                "A derivative step at the boundary is exactly what a hard clamp does, and it is the pop.");
        }

        [Test]
        public void AFarOvershoot_StillYields_AndNeverLocks()
        {
            const float soft = 60f, hard = 75f;
            float far = BasisJointLimitCore.Saturate(soft + 200f, soft, hard);

            Assert.Greater(far, soft);
            Assert.Less(far, hard);
        }

        [Test]
        public void OnLegalPoses_ClampReturnsTheInputBitForBit()
        {
            int tested = 0;

            for (int i = 0; i < 20000; i++)
            {
                BasisJointRestFrame frame = Frame(RandomUnitQuat());

                float flex = (Rand01() < 0.5f ? k_Hip.FlexDeg : -k_Hip.ExtDeg) * Rand01() * 0.6f;
                float lat = (Rand01() < 0.5f ? k_Hip.AbductDeg : -k_Hip.AdductDeg) * Rand01() * 0.6f;

                float fN = flex / (flex >= 0f ? k_Hip.FlexDeg : k_Hip.ExtDeg);
                float lN = lat / (lat >= 0f ? k_Hip.AbductDeg : k_Hip.AdductDeg);
                float coneR = Mathf.Sqrt(fN * fN + lN * lN);
                if (coneR > 0.9f) continue;

                float twist = RandSym(BasisJointLimitCore.TwistLimitAt(coneR, k_Hip) * 0.8f);

                Quaternion legal = BasisJointLimitCore.Recompose(flex, lat, twist, frame);
                Quaternion got = BasisJointLimitCore.Clamp(legal, frame, k_Hip, out BasisJointClampInfo info);
                tested++;

                Assert.IsFalse(info.Touched, $"Guard fired on a legal pose: flex {flex} lat {lat} twist {twist}.");
                Assert.IsTrue(BitEqual(got, legal), "A legal pose must pass through untouched, bit for bit.");
            }

            Assert.Greater(tested, 1000, "Sanity: the generator must actually produce legal poses.");
        }

        [Test]
        public void DecomposeRecompose_RoundTripsToUnderAHundredthOfADegree()
        {
            float worstFlex = 0f, worstLat = 0f, worstTwist = 0f;

            for (int i = 0; i < 20000; i++)
            {
                BasisJointRestFrame frame = Frame(RandomUnitQuat());
                float flex = RandSym(80f), lat = RandSym(40f), twist = RandSym(60f);

                Quaternion q = BasisJointLimitCore.Recompose(flex, lat, twist, frame);
                Quaternion delta = q * BasisJointLimitCore.Conj(frame.RestLocalRot);
                BasisJointLimitCore.Decompose(delta, frame, out float f, out float l, out float t);

                worstFlex = Mathf.Max(worstFlex, Mathf.Abs(f - flex));
                worstLat = Mathf.Max(worstLat, Mathf.Abs(l - lat));
                worstTwist = Mathf.Max(worstTwist, Mathf.Abs(t - twist));
            }

            Assert.Less(worstFlex, 0.02f, "flexion drifted through the round trip");
            Assert.Less(worstLat, 0.02f, "lateral drifted through the round trip");
            Assert.Less(worstTwist, 0.02f, "twist drifted through the round trip");
        }

        [Test]
        public void NothingEscapesTheEnvelope_OverUniformlyRandomRotations()
        {
            float asymptote = BasisJointLimitCore.OvershootAsymptote;
            float worstCone = 0f, worstTwistRatio = 0f;

            for (int i = 0; i < 50000; i++)
            {
                BasisJointRestFrame frame = Frame(RandomUnitQuat());
                Quaternion guarded = BasisJointLimitCore.Clamp(RandomUnitQuat(), frame, k_Hip, out _);

                Quaternion delta = guarded * BasisJointLimitCore.Conj(frame.RestLocalRot);
                BasisJointLimitCore.Decompose(delta, frame, out float f, out float l, out float t);

                float fN = f / (f >= 0f ? k_Hip.FlexDeg : k_Hip.ExtDeg);
                float lN = l / (l >= 0f ? k_Hip.AbductDeg : k_Hip.AdductDeg);
                float coneR = Mathf.Sqrt(fN * fN + lN * lN);
                worstCone = Mathf.Max(worstCone, coneR);

                float tLim = BasisJointLimitCore.TwistLimitAt(coneR, k_Hip);
                if (tLim > 1e-4f) worstTwistRatio = Mathf.Max(worstTwistRatio, Mathf.Abs(t) / tLim);
            }

            Assert.LessOrEqual(worstCone, asymptote + 0.02f, "a swing escaped the guarded cone");
            Assert.LessOrEqual(worstTwistRatio, asymptote + 0.05f, "a twist escaped its guarded limit");
        }

        [Test]
        public void NaNInput_IsReturnedUnchanged_NotAmplified()
        {
            Quaternion nan = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
            Quaternion got = BasisJointLimitCore.Clamp(nan, Frame(Quaternion.identity), k_Hip, out _);

            Assert.IsTrue(BitEqual(got, nan));
        }

        [Test]
        public void AnInvalidRestFrame_IsAPassThrough()
        {
            Quaternion q = RandomUnitQuat();
            Assert.IsTrue(BitEqual(BasisJointLimitCore.Clamp(q, default, k_Hip, out _), q));
        }

        [Test]
        public void AnAllZeroRom_ProducesNoNaN()
        {
            Quaternion got = BasisJointLimitCore.Clamp(
                RandomUnitQuat(), Frame(Quaternion.identity), new BasisJointRom(0f, 0f, 0f, 0f, 0f), out _);

            Assert.IsFalse(float.IsNaN(got.x) || float.IsNaN(got.y) || float.IsNaN(got.z) || float.IsNaN(got.w));
        }

        [Test]
        public void TheRestPose_IsExactlyUntouched()
        {
            BasisJointRestFrame frame = Frame(RandomUnitQuat());
            Quaternion got = BasisJointLimitCore.Clamp(frame.RestLocalRot, frame, k_Hip, out BasisJointClampInfo info);

            Assert.IsFalse(info.Touched);
            Assert.IsTrue(BitEqual(got, frame.RestLocalRot));
        }

        [Test]
        public void HipTwistSlack_CollapsesTowardTheConeEdge()
        {
            float centre = BasisJointLimitCore.TwistLimitAt(0f, k_Hip);
            float mid = BasisJointLimitCore.TwistLimitAt(0.5f, k_Hip);
            float edge = BasisJointLimitCore.TwistLimitAt(1f, k_Hip);

            Assert.AreEqual(50f, centre, 1e-3f, "full rotational slack at mid-flexion, mid-abduction");
            Assert.AreEqual(15f, edge, 1e-3f, "slack collapses at the extremes (van Arkel 2015)");
            Assert.Greater(mid, edge);
            Assert.Less(mid, centre);
            Assert.AreEqual(15f, BasisJointLimitCore.TwistLimitAt(5f, k_Hip), 1e-3f,
                "past the cone edge the limit holds rather than inverting");
        }

        [Test]
        public void TheFiveArgRom_GivesAFlatTwistLimit()
        {
            for (float r = 0f; r <= 2f; r += 0.05f)
            {
                Assert.AreEqual(12f, BasisJointLimitCore.TwistLimitAt(r, k_Lumbar), 1e-3f,
                    "the spine measured no swing-twist coupling, so the flat case must stay flat");
            }
        }
    }
}
