using Basis.Network.Core.Compression;
using NUnit.Framework;
using UnityEngine;
using Q = Basis.Network.Core.Compression.BasisAvatarBitPacking.BitQuality;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Round-trip and robustness of the curl/splay scalar codec that replaced the finger rotations.
    ///
    /// Fidelity is asserted in DEGREES AT THE JOINT rather than in wire units, because wire units
    /// are meaningless on their own: curl spans roughly 100° of flexion and splay roughly 50° of
    /// abduction, so the same quantiser step buys very different accuracy on each. Asserting the
    /// angle is what makes a future bit-budget change show up as a fidelity number instead of a
    /// silently-passing test.
    ///
    /// The clamp and NaN cases are not hypothetical. MediaPipe applies CurlGain/SplayGain to an
    /// angle ratio and can exceed 1, and a dropped tracking frame can hand through a NaN — an
    /// unguarded cast of either into an unsigned quantiser is how a hand ends up locked in a fist.
    /// </summary>
    public class BasisFingerBlockCodecTests
    {
        /// <summary>Full sweep a curl channel represents, used to convert wire error into joint error.</summary>
        const float CurlSweepDegrees = 100f;
        const float SplaySweepDegrees = 50f;

        static float StepUnits(int bits) => 2f / ((1 << bits) - 1);

        [TestCase(Q.High)]
        [TestCase(Q.Medium)]
        [TestCase(Q.Low)]
        [TestCase(Q.VeryLow)]
        public void RoundTrip_StaysWithinHalfAStep(Q quality)
        {
            foreach (int bits in new[] { BasisBoneRotationCompression.CurlBits(quality), BasisBoneRotationCompression.SplayBits(quality) })
            {
                float halfStep = StepUnits(bits) * 0.5f;
                for (int i = 0; i <= 2000; i++)
                {
                    float value = -1f + 2f * i / 2000f;
                    uint q = BasisBoneRotationCompression.EncodeSignedUnit(value, bits);
                    float back = BasisBoneRotationCompression.DecodeSignedUnit(q, bits);
                    Assert.LessOrEqual(System.Math.Abs(back - value), halfStep + 1e-6f,
                        $"{bits}-bit round trip of {value:F5} landed at {back:F5}");
                }
            }
        }

        // Computed worst-case half-step cost: High 0.20/0.40, Medium 0.39/0.81,
        // Low 0.79/1.67, VeryLow 1.61/3.57 degrees.
        [TestCase(Q.High, 0.25f, 0.45f)]
        [TestCase(Q.Medium, 0.45f, 0.85f)]
        [TestCase(Q.Low, 0.85f, 1.75f)]
        [TestCase(Q.VeryLow, 1.70f, 3.65f)]
        public void JointErrorFromQuantisation_StaysUnder(Q quality, float maxCurlDegrees, float maxSplayDegrees)
        {
            float curlError = StepUnits(BasisBoneRotationCompression.CurlBits(quality)) * 0.5f * CurlSweepDegrees * 0.5f;
            float splayError = StepUnits(BasisBoneRotationCompression.SplayBits(quality)) * 0.5f * SplaySweepDegrees * 0.5f;

            Assert.LessOrEqual(curlError, maxCurlDegrees,
                $"curl quantisation costs {curlError:F3}° at {quality}");
            Assert.LessOrEqual(splayError, maxSplayDegrees,
                $"splay quantisation costs {splayError:F3}° at {quality}");
        }

        [Test]
        public void HighCurl_ResolvesFinerThanTheBodyJointsItTravelsWith()
        {
            // Body joints run at 12 BPC across a full smallest-three range; the finger channels
            // should not be the visibly coarse part of the same packet.
            float curlStepDegrees = StepUnits(BasisBoneRotationCompression.CurlBits(Q.High)) * CurlSweepDegrees * 0.5f;
            Assert.Less(curlStepDegrees, 1.0f,
                $"an 8-bit curl step is {curlStepDegrees:F3}° of flexion");
        }

        [TestCase(8)]
        [TestCase(6)]
        [TestCase(5)]
        [TestCase(3)]
        public void OutOfRangeInput_ClampsInsteadOfWrapping(int bits)
        {
            uint max = (uint)((1 << bits) - 1);

            Assert.AreEqual(max, BasisBoneRotationCompression.EncodeSignedUnit(1f, bits));
            Assert.AreEqual(max, BasisBoneRotationCompression.EncodeSignedUnit(1.5f, bits));
            Assert.AreEqual(max, BasisBoneRotationCompression.EncodeSignedUnit(9999f, bits));

            Assert.AreEqual(0u, BasisBoneRotationCompression.EncodeSignedUnit(-1f, bits));
            Assert.AreEqual(0u, BasisBoneRotationCompression.EncodeSignedUnit(-1.5f, bits));
            Assert.AreEqual(0u, BasisBoneRotationCompression.EncodeSignedUnit(-9999f, bits));
        }

        [TestCase(8)]
        [TestCase(6)]
        [TestCase(5)]
        [TestCase(3)]
        public void NonFiniteInput_EncodesAsRestNotAsFullScale(int bits)
        {
            uint max = (uint)((1 << bits) - 1);
            uint mid = (max + 1) >> 1;

            Assert.AreEqual(mid, BasisBoneRotationCompression.EncodeSignedUnit(float.NaN, bits),
                "a NaN curl must decode near rest, not as a fist");

            // Infinities go through the ordinary clamp; the point is only that they stay in range.
            Assert.LessOrEqual(BasisBoneRotationCompression.EncodeSignedUnit(float.PositiveInfinity, bits), max);
            Assert.LessOrEqual(BasisBoneRotationCompression.EncodeSignedUnit(float.NegativeInfinity, bits), max);
        }

        [TestCase(8)]
        [TestCase(6)]
        [TestCase(5)]
        [TestCase(3)]
        public void EveryCode_DecodesInsideTheUnitRange(int bits)
        {
            uint max = (uint)((1 << bits) - 1);
            for (uint code = 0; code <= max; code++)
            {
                float value = BasisBoneRotationCompression.DecodeSignedUnit(code, bits);
                Assert.GreaterOrEqual(value, -1f);
                Assert.LessOrEqual(value, 1f);
            }
            Assert.AreEqual(-1f, BasisBoneRotationCompression.DecodeSignedUnit(0, bits), 1e-6f);
            Assert.AreEqual(1f, BasisBoneRotationCompression.DecodeSignedUnit(max, bits), 1e-6f);
        }

        [Test]
        public void Encoding_IsMonotonic()
        {
            for (int bits = 3; bits <= 8; bits++)
            {
                uint previous = 0;
                for (int i = 0; i <= 4000; i++)
                {
                    float value = -1f + 2f * i / 4000f;
                    uint code = BasisBoneRotationCompression.EncodeSignedUnit(value, bits);
                    Assert.GreaterOrEqual(code, previous,
                        $"{bits}-bit encoding went backwards at {value:F5}");
                    previous = code;
                }
            }
        }

        [Test]
        public void CorpusValues_SurviveTheRoundTripAtHigh()
        {
            int curlBits = BasisBoneRotationCompression.CurlBits(Q.High);
            int splayBits = BasisBoneRotationCompression.SplayBits(Q.High);
            float curlTolerance = StepUnits(curlBits) * 0.5f + 1e-6f;
            float splayTolerance = StepUnits(splayBits) * 0.5f + 1e-6f;

            foreach (var pose in BasisFingerCorpus.All())
            {
                for (int finger = 0; finger < BasisFingerCorpus.FingerCount; finger++)
                {
                    float curl = Mathf.Clamp(pose.Fingers[finger].x, -1f, 1f);
                    float splay = Mathf.Clamp(pose.Fingers[finger].y, -1f, 1f);

                    float curlBack = BasisBoneRotationCompression.DecodeSignedUnit(
                        BasisBoneRotationCompression.EncodeSignedUnit(curl, curlBits), curlBits);
                    float splayBack = BasisBoneRotationCompression.DecodeSignedUnit(
                        BasisBoneRotationCompression.EncodeSignedUnit(splay, splayBits), splayBits);

                    Assert.AreEqual(curl, curlBack, curlTolerance, $"{pose.Name} finger {finger} curl");
                    Assert.AreEqual(splay, splayBack, splayTolerance, $"{pose.Name} finger {finger} splay");
                }
            }
        }
    }
}
