using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Guards the RADIAL half of <see cref="BasisElbowSwingCapCore"/>'s budget (2026-07-22).
    ///
    /// ================================================================================================
    /// THE DEFECT THESE PIN.
    ///
    /// The cap's budget used to be `MaxGain * angle(prevAxis, curAxis)` alone. A hand travelling straight
    /// ALONG its own arm axis -- every punch, push, point-and-retract and straight-line reach -- rotates
    /// that axis by EXACTLY ZERO, so the budget was zero and the bend was FROZEN, not slowed. The field
    /// genuinely moves there (BasisElbowFieldModel.Elbow is affine in tipLocal, so at fixed direction the
    /// perpendicular component is `a + r*b`, whose direction rotates with r; BasisSwivelHintCore's
    /// elbow-down term then ramps over reach 0.90->0.99 on top). Measured 22.9-29.8 deg of pole error and
    /// 5.3-7.0 cm of solved elbow error against a field model whose own published error is 2.07 cm.
    ///
    /// ================================================================================================
    /// HOW THESE TESTS AVOID BEING TAUTOLOGICAL.
    ///
    /// A test that only asserts "the new path is good" proves nothing -- it would pass just as happily
    /// against a stub. Every claim here is therefore made against a CONTROL that must FAIL:
    ///
    ///   * TheDefect_IsReal_OnTheRotationOnlyCap is the RULER. It drives the OLD (5-arg) overload down
    ///     the same trajectory and asserts the error is still >= 20 deg. If someone "fixes" the defect by
    ///     weakening the trajectory, flattening the field, or making the harness blind, this test goes
    ///     green-to-red first and the improvement claims below become unclaimable.
    ///   * TheConditioningGate_IsWhatProtectsTheCore asserts BOTH directions at once: gated == old to the
    ///     bit at a core, AND the same code path with the gate DEFEATED (conditioning forced to 1) leaks
    ///     a double-digit flip. The second half is the anti-tautology control -- it proves the core check
    ///     can actually detect a broken gate rather than passing because nothing ever moves.
    /// </summary>
    public class BasisElbowSwingCapRadialTests
    {
        const float k_MaxGain = BasisElbowSwingCapCore.MaxGain;
        const float k_ArmLen = 0.60f;
        static readonly Vector3 k_Shoulder = new Vector3(0.17f, 1.40f, 0f);

        /// <summary>Identity body frame, so tipLocal is just (hand - shoulder) / armLen and the poses below
        /// read as plain body-frame directions.</summary>
        static BasisSwivelFrame Frame() => new BasisSwivelFrame
        { Right = Vector3.right, Up = Vector3.up, Forward = Vector3.forward, Valid = true };

        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        }
        static Vector3 HandAt(Vector3 dir, float reach) => k_Shoulder + dir.normalized * (reach * k_ArmLen);

        /// <summary>The shipped no-tracker bend for a hand pose: the FULL ArmHint path (field + tuck +
        /// elbow-down), which is what the job actually feeds the cap -- not the bare field.</summary>
        static bool Field(Vector3 hand, out Vector3 bend, out float conditioning)
        {
            bend = default;
            if (!BasisSwivelHintCore.ArmHint(Frame(), k_Shoulder, hand, k_ArmLen, false,
                                             out Vector3 hintPos, out conditioning))
            {
                return false;
            }
            bend = (hintPos - k_Shoulder).normalized;
            return true;
        }

        enum Mode { RotationOnly, Gated, GateDefeated }

        static float3 Cap(Mode m, float3 prevBend, float3 prevAxis, float3 curAxis, float3 rawBend,
                          float dReach, float conditioning)
        {
            switch (m)
            {
                case Mode.RotationOnly:                                   // the pre-fix 5-arg overload
                    return BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain);
                case Mode.GateDefeated:                                   // control: conditioning pinned high
                    return BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain, dReach, 1f);
                default:
                    return BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain, dReach, conditioning);
            }
        }

        /// <summary>
        /// Run a straight radial reach (hand DIRECTION fixed, reach growing) and return the angle between
        /// the capped pole and the field's own answer at the end of the move. `wanderDeg` adds a little
        /// honest direction drift so no claim rests on a mathematically perfect straight line.
        /// </summary>
        static float PunchPoleError(Mode m, Vector3 dir, float wanderDeg, int frames = 40)
        {
            float3 prevBend = default, prevAxis = default, capped = default;
            float prevReach = 0f;
            bool seeded = false;
            Vector3 lastField = Vector3.forward;

            for (int i = 0; i <= frames; i++)
            {
                float t = i / (float)frames;
                float reach = 0.55f + (1.00f - 0.55f) * t;
                Vector3 dd = (dir + Vector3.up * (Mathf.Sin(t * Mathf.PI) * wanderDeg * Mathf.Deg2Rad)).normalized;
                Vector3 hand = HandAt(dd, reach);
                if (!Field(hand, out Vector3 rawBend, out float cond))
                {
                    continue;
                }
                Vector3 axisV = (hand - k_Shoulder).normalized;
                float3 axis = new float3(axisV.x, axisV.y, axisV.z);
                float3 raw = new float3(rawBend.x, rawBend.y, rawBend.z);

                capped = seeded ? Cap(m, prevBend, prevAxis, axis, raw, reach - prevReach, cond) : raw;
                seeded = true;
                prevBend = capped; prevAxis = axis; prevReach = reach;
                lastField = rawBend;
            }
            return Vector3.Angle(lastField, new Vector3(capped.x, capped.y, capped.z));
        }

        static readonly Vector3[] k_Punches =
        {
            new Vector3(0.15f, -0.05f, 0.99f),    // forward punch
            new Vector3(0.60f, -0.05f, 0.80f),    // forward-out punch
            new Vector3(0.25f, -0.60f, 0.76f),    // down-forward reach
            new Vector3(-0.35f, 0.10f, 0.93f),    // cross-body push
        };

        /// <summary>
        /// ⭐⭐ THE RULER, AND THE ANTI-TAUTOLOGY FLOOR FOR EVERYTHING BELOW. The defect must still be
        /// reproducible on the rotation-only budget: a straight reach with the hand direction held fixed
        /// freezes the bend and ends 20+ degrees off the field. Measured 22.9 / 23.5 / 29.8 / 21.5 deg.
        ///
        /// If this ever goes green the improvement tests underneath are measuring nothing, and THIS is the
        /// test to fix first -- not them.
        /// </summary>
        [Test]
        public void TheDefect_IsReal_OnTheRotationOnlyCap()
        {
            foreach (Vector3 d in k_Punches)
            {
                float err = PunchPoleError(Mode.RotationOnly, d.normalized, 0f);
                Assert.Greater(err, 20f,
                    $"the rotation-only cap only lost {err:F1} deg of pole on a straight radial reach along {d} -- " +
                    "the defect this file guards is no longer reproducible, so every improvement assertion " +
                    "below is now vacuous. Fix this test before trusting those.");
            }
        }

        /// <summary>
        /// ⭐⭐ THE FIX. With the radial budget the same straight reach tracks the field: the bend is no
        /// longer frozen, so the pole lands on the field's own answer. 20+ deg of error becomes under 1.
        /// </summary>
        [Test]
        public void TheRadialBudget_TracksTheField_ThroughAStraightReach()
        {
            foreach (Vector3 d in k_Punches)
            {
                float err = PunchPoleError(Mode.Gated, d.normalized, 0f);
                Assert.Less(err, 1f,
                    $"a straight radial reach along {d} still ended {err:F1} deg off the field -- the radial " +
                    "budget is not reaching the cap (check ReachGain, and that the caller is passing dReach " +
                    "and conditioning at all)");
            }
        }

        /// <summary>
        /// ⭐ The damage was worst at 0 degrees of direction wander and gone by 8 (where the axis rotation
        /// alone already pays for the field). The fix must cover the whole band, not just the clean case --
        /// 2 degrees of wander was still 8.5-13.7 deg of error before it.
        /// </summary>
        [Test]
        public void TheRadialBudget_CoversThePartiallyWanderingReach()
        {
            foreach (Vector3 d in k_Punches)
            {
                float before = PunchPoleError(Mode.RotationOnly, d.normalized, 2f);
                float after = PunchPoleError(Mode.Gated, d.normalized, 2f);
                Assert.Greater(before, 5f, $"ruler: 2 deg of wander should still show the defect along {d}");
                Assert.Less(after, 1f, $"2 deg of wander along {d} still ended {after:F1} deg off the field");
            }
        }

        /// <summary>
        /// ⭐⭐ THE CORE, AND THE CONTROL THAT PROVES THIS TEST CAN FAIL.
        ///
        /// Radial budget must never help a topological core flip. The adversarial trajectory is a hand
        /// CRAWLING through a core (+/-1.5 deg of azimuth over the whole run, so rotation budget alone is
        /// near zero and the old cap is effectively frozen) while simultaneously PUNCHING -- 0.30 arm
        /// lengths of extension over 20 frames.
        ///
        /// Two assertions, and the second is the one that keeps the first honest:
        ///   1. gated == rotation-only, to the bit. The conditioning gate is identically zero at a core, so
        ///      the fix cannot have changed anything here.
        ///   2. with the gate DEFEATED the same trajectory leaks a double-digit step. Without this the
        ///      first assertion would also pass against a cap that ignored dReach entirely.
        /// </summary>
        [Test]
        public void TheConditioningGate_IsWhatProtectsTheCore()
        {
            // BasisElbowFieldModel's topological cores, located on the SHIPPED ArmHint path.
            var cores = new (float az, float el, float reach)[]
            {
                (131f, -18f, 0.70f),
                (264f, 28f, 0.74f),
                (117f, -31f, 0.38f),
                (266f, 29f, 0.54f),
            };

            foreach (var c in cores)
            {
                float gated = CoreCrawlWorstStep(Mode.Gated, c.az, c.el, c.reach, 0.30f);
                float rotOnly = CoreCrawlWorstStep(Mode.RotationOnly, c.az, c.el, c.reach, 0.30f);
                float defeated = CoreCrawlWorstStep(Mode.GateDefeated, c.az, c.el, c.reach, 0.30f);

                Assert.AreEqual(rotOnly, gated, 1e-3f,
                    $"at the core az={c.az} el={c.el} the gated cap let through {gated:F2} deg/frame against the " +
                    $"rotation-only cap's {rotOnly:F2} -- the conditioning gate must make the radial budget " +
                    "IDENTICALLY ZERO at a core, so these must agree to the bit");

                Assert.Greater(defeated, 5f,
                    $"CONTROL FAILED at core az={c.az}: with the conditioning gate defeated the cap should leak a " +
                    $"flip, but it only moved {defeated:F2} deg/frame. That means this test cannot detect a broken " +
                    "gate, so the equality assertion above proves nothing. Check the trajectory still crosses a core.");
            }
        }

        /// <summary>Worst single-frame bend rotation while crawling through a core and extending.</summary>
        static float CoreCrawlWorstStep(Mode m, float azDeg, float elDeg, float reachMid, float dReachTotal)
        {
            const int n = 20;
            float3 prevBend = default, prevAxis = default;
            float prevReach = 0f;
            bool seeded = false;
            float worst = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vector3 dir = Dir(azDeg - 1.5f + 3f * t, elDeg);
                float reach = Mathf.Clamp(reachMid - dReachTotal * 0.5f + dReachTotal * t, 0.10f, 0.995f);
                Vector3 hand = HandAt(dir, reach);
                if (!Field(hand, out Vector3 rawBend, out float cond))
                {
                    continue;
                }
                Vector3 axisV = (hand - k_Shoulder).normalized;
                float3 axis = new float3(axisV.x, axisV.y, axisV.z);
                float3 raw = new float3(rawBend.x, rawBend.y, rawBend.z);

                if (seeded)
                {
                    float3 capped = Cap(m, prevBend, prevAxis, axis, raw, reach - prevReach, cond);
                    // measure the step the way the shipped cap tests do: transport onto the NEW axis first
                    float3 tp = prevBend - axis * math.dot(prevBend, axis);
                    if (math.length(tp) > 1e-5f)
                    {
                        tp = math.normalize(tp);
                        worst = Mathf.Max(worst, Vector3.Angle((Vector3)tp, (Vector3)capped));
                    }
                    prevBend = capped;
                }
                else
                {
                    prevBend = raw;
                    seeded = true;
                }
                prevAxis = axis; prevReach = reach;
            }
            return worst;
        }

        /// <summary>
        /// ⭐⭐ THE DECLINE. Zero/absent new inputs must reproduce the rotation-only cap BIT-FOR-BIT, so
        /// every existing caller, test and offline sweep that cannot supply them is unchanged. Fuzzed over
        /// random frames, both through the 5-arg overload and the 7-arg one with zeros.
        /// </summary>
        [Test]
        public void ZeroOrAbsentNewInputs_AreBitIdenticalToTheRotationOnlyCap()
        {
            var rng = new System.Random(20260722);
            for (int t = 0; t < 5000; t++)
            {
                float3 curAxis = math.normalize(RandVec(rng));
                float3 prevAxis = math.normalize(curAxis + 0.15f * RandVec(rng));
                float3 prevBend = math.normalize(math.cross(prevAxis, RandVec(rng)));
                float3 rawBend = math.normalize(math.cross(curAxis, RandVec(rng)));
                if (!math.all(math.isfinite(prevBend)) || !math.all(math.isfinite(rawBend)))
                {
                    continue;
                }

                float3 five = BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain);
                float3 sevenZero = BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain, 0f, 0f);
                // a real reach change but zero conditioning: the gate must still refuse the budget
                float3 sevenUngated = BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain, 0.05f, 0f);

                Assert.IsTrue(five.Equals(sevenZero),
                    $"the 7-arg overload with zeros differed from the 5-arg one ({five} vs {sevenZero}) -- the new " +
                    "inputs must DECLINE to the old behaviour bit-for-bit");
                Assert.IsTrue(five.Equals(sevenUngated),
                    $"dReach with zero conditioning changed the output ({five} vs {sevenUngated}) -- conditioning " +
                    "below ReachTrustLo must zero the radial budget whatever dReach says");
            }
        }

        /// <summary>
        /// ⭐ FAIL CLOSED. A NaN reach delta or conditioning (a degenerate frame, an unseeded state) must
        /// fall back to the rotation-only budget, never poison the clamp and hand out an unbounded or NaN
        /// pole. The two-bone solve is downstream of this.
        /// </summary>
        [Test]
        public void NonFiniteNewInputs_DeclineToTheRotationOnlyCap()
        {
            float3 curAxis = math.normalize(new float3(0.2f, -0.3f, 0.9f));
            float3 prevAxis = math.normalize(new float3(0.25f, -0.28f, 0.9f));
            float3 prevBend = math.normalize(math.cross(prevAxis, new float3(0f, 1f, 0f)));
            float3 rawBend = math.normalize(math.cross(curAxis, new float3(1f, 0.2f, 0f)));
            float3 expect = BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain);

            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                float3 a = BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain, bad, 0.3f);
                float3 b = BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain, 0.01f, bad);
                Assert.IsTrue(math.all(math.isfinite(a)), $"dReach={bad} produced a non-finite pole {a}");
                Assert.IsTrue(math.all(math.isfinite(b)), $"conditioning={bad} produced a non-finite pole {b}");
                Assert.AreEqual(0f, Vector3.Angle((Vector3)expect, (Vector3)a), 1e-3f,
                    $"dReach={bad} must decline to the rotation-only cap");
            }

            Assert.AreEqual(0f, BasisElbowSwingCapCore.ReachTrust(float.NaN), 0f,
                "NaN conditioning must earn zero radial trust");
            Assert.AreEqual(0f, BasisElbowSwingCapCore.ReachTrust(-1f), 0f,
                "negative conditioning must earn zero radial trust");
            Assert.AreEqual(0f, BasisElbowSwingCapCore.ReachTrust(BasisElbowSwingCapCore.ReachTrustLo), 0f,
                "trust must be exactly 0 at the low edge, so the gate opens continuously");
            Assert.AreEqual(1f, BasisElbowSwingCapCore.ReachTrust(BasisElbowSwingCapCore.ReachTrustHi), 1e-6f,
                "trust must be exactly 1 at the high edge");
        }

        /// <summary>
        /// ⭐⭐ FRAMERATE INDEPENDENCE, THE REASON THIS IS NOT A RATE FLOOR. The radial term is homogeneous
        /// of degree one in the hand step, exactly like the rotation term, so the SAME hand path sampled at
        /// twice the frame rate must give the same pose at the shared times. A `minRate * dt` floor would
        /// have failed this the moment the budget stopped being what the hand did.
        /// </summary>
        [Test]
        public void TheRadialBudget_IsFramerateIndependent()
        {
            float3 Run(int frames)
            {
                float3 prevBend = default, prevAxis = default, capped = default;
                float prevReach = 0f;
                bool seeded = false;
                Vector3 dir = new Vector3(0.25f, -0.60f, 0.76f).normalized;   // the worst of the battery
                for (int i = 0; i <= frames; i++)
                {
                    float t = i / (float)frames;
                    float reach = 0.55f + 0.45f * t;
                    Vector3 hand = HandAt(dir, reach);
                    if (!Field(hand, out Vector3 rawBend, out float cond))
                    {
                        continue;
                    }
                    Vector3 axisV = (hand - k_Shoulder).normalized;
                    float3 axis = new float3(axisV.x, axisV.y, axisV.z);
                    float3 raw = new float3(rawBend.x, rawBend.y, rawBend.z);
                    capped = seeded ? Cap(Mode.Gated, prevBend, prevAxis, axis, raw, reach - prevReach, cond) : raw;
                    seeded = true;
                    prevBend = capped; prevAxis = axis; prevReach = reach;
                }
                return capped;
            }

            float spread = Vector3.Angle((Vector3)Run(40), (Vector3)Run(80));      // "90 Hz" vs "180 Hz"
            Assert.Less(spread, 0.5f,
                $"the same reach sampled at 1x and 2x frame density ended {spread:F2} deg apart -- the radial " +
                "budget must be per hand-STEP, never per second, or 72/90/120 Hz headsets diverge");
        }

        /// <summary>
        /// ⭐ The output contract is unchanged by the new term: still a UNIT vector PERPENDICULAR to the
        /// shoulder->hand axis, because that is the elbow's reachable circle and the two-bone solve relies
        /// on it. Fuzzed with the radial budget live and binding.
        /// </summary>
        [Test]
        public void TheCappedBend_StaysUnitAndPerpendicular_WithRadialBudget()
        {
            var rng = new System.Random(4242);
            for (int t = 0; t < 5000; t++)
            {
                float3 curAxis = math.normalize(RandVec(rng));
                float3 prevAxis = math.normalize(curAxis + 0.1f * RandVec(rng));
                float3 prevBend = math.normalize(math.cross(prevAxis, RandVec(rng)));
                float3 rawBend = math.normalize(math.cross(curAxis, RandVec(rng)));
                if (!math.all(math.isfinite(prevBend)) || !math.all(math.isfinite(rawBend)))
                {
                    continue;
                }
                float dReach = (float)(rng.NextDouble() * 0.08 - 0.04);
                float cond = (float)(rng.NextDouble() * 0.5);

                float3 b = BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain, dReach, cond);
                Assert.IsTrue(math.all(math.isfinite(b)), $"cap went non-finite at axis {curAxis}");
                Assert.AreEqual(1f, math.length(b), 3e-3f, "capped bend must be unit");
                Assert.AreEqual(0f, math.dot(curAxis, b), 3e-3f, "capped bend must be perpendicular to the arm");
            }
        }

        /// <summary>
        /// ⭐ MONOTONE BUDGET. The radial term may only ever ADD budget, so no pose that the rotation-only
        /// cap left untouched can start being clipped. Checked as: the gated output never sits further from
        /// the raw field than the rotation-only output does.
        /// </summary>
        [Test]
        public void TheRadialBudget_OnlyEverAddsBudget()
        {
            var rng = new System.Random(99);
            for (int t = 0; t < 5000; t++)
            {
                float3 curAxis = math.normalize(RandVec(rng));
                float3 prevAxis = math.normalize(curAxis + 0.2f * RandVec(rng));
                float3 prevBend = math.normalize(math.cross(prevAxis, RandVec(rng)));
                float3 rawBend = math.normalize(math.cross(curAxis, RandVec(rng)));
                if (!math.all(math.isfinite(prevBend)) || !math.all(math.isfinite(rawBend)))
                {
                    continue;
                }
                float dReach = (float)(rng.NextDouble() * 0.06);
                float cond = (float)(rng.NextDouble() * 0.4);

                float oldErr = Vector3.Angle((Vector3)rawBend,
                    (Vector3)BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain));
                float newErr = Vector3.Angle((Vector3)rawBend,
                    (Vector3)BasisElbowSwingCapCore.Apply(prevBend, prevAxis, curAxis, rawBend, k_MaxGain, dReach, cond));

                Assert.LessOrEqual(newErr, oldErr + 1e-2f,
                    $"the radial budget moved the pole FURTHER from the field ({newErr:F3} vs {oldErr:F3} deg) -- " +
                    "it must only ever widen the clamp, never narrow it");
            }
        }

        static float3 RandVec(System.Random r) => new float3(
            (float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1));
    }
}
