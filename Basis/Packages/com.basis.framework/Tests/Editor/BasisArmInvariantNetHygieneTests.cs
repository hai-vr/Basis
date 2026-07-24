using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// DETERMINISM, DEGENERACY, AND THE TWO CLASSES THAT ONLY SHOW UP OVER TIME.
    ///
    /// ================================================================================================
    ///   * DETERMINISM / STATELESSNESS -- the solve is a pure function of its inputs. Anything else means
    ///     the same user pose renders differently depending on what happened before it, which is exactly
    ///     the complaint the no-tracker forearm roll was written to answer (the avatar's pronation
    ///     depended on which idle clip was playing).
    ///
    ///   * NaN / DEGENERATE HYGIENE -- zero-length segments, zero quaternions, collinear arms, a hand
    ///     exactly at the shoulder, a hand past reach. A NaN transform PERSISTS in Unity: the arm never
    ///     recovers, even once good data returns. So nothing may emit NaN and nothing may emit a non-unit
    ///     quaternion, whatever it is handed. The house idiom is "reject unless good" -- !(x > eps), never
    ///     x < eps -- because NaN fails every ordered comparison and sails straight through the second form.
    ///
    ///   * A STALE OR DEGENERATE SEED (class 7). MEASURED, 12 noise seeds: a tracker re-acquired while the
    ///     arm was straight seeded its pole from a noise-length vector -- a uniformly random direction --
    ///     and HELD it. The elbow ended 1.9 to 97.4 degrees off the tracker's actual pole (median ~60) AND
    ///     STAYED THERE. That is a temporal defect: every single frame of it is a perfectly good solve, so
    ///     no per-frame test of any kind can see it. It needs the loop.
    ///
    ///   * A STRUCTURALLY-ZERO BUDGET (class 6). A gain cap computed its budget from AXIS ROTATION alone,
    ///     so a straight-line reach -- which rotates the axis by exactly zero -- froze the joint. The
    ///     property that catches the class is "a motion that requires no axis rotation must still
    ///     articulate the joint", and the non-vacuity control is the geometry itself: the triangle solve
    ///     REQUIRES the elbow to travel a known distance as the arm extends.
    /// ================================================================================================
    /// </summary>
    public class BasisArmInvariantNetHygieneTests
    {
        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        static List<BasisArmSolveInput> Corpus()
        {
            var list = new List<BasisArmSolveInput>();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            foreach (float az in new[] { 0f, 120f, 250f })
            foreach (float el in new[] { -65f, -10f, 45f })
            foreach (float reach in new[] { 0.35f, 0.75f, 0.94f, 0.999f, 1.10f })
            foreach (int hint in new[] { BasisArmNet.HintNone, BasisArmNet.HintModel, BasisArmNet.HintTracker })
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(az, el);
                s.Reach = reach;
                s.HandRollDeg = 95f;
                s.HintMode = hint;
                s.HintAzimuthDeg = 33f + az;
                s.HintRhoMin = 0.01f;
                s.TrackerRollDeg = hint == BasisArmNet.HintTracker ? 60f : float.NaN;
                s.RefPerp = Mathf.Abs(el) > 60f ? Dir(az + 90f, 0f) : Vector3.up;
                s.FeedTwistBind = true;
                s.FeedLowerBind = true;
                s.FeedTip = true;
                s.AnimHumRollDeg = 40f;
                s.AnimForeRollDeg = -25f;
                list.Add(BasisArmNet.Build(s));
            }
            return list;
        }

        // ============================================================================================
        // 1. DETERMINISM AND STATELESSNESS
        // ============================================================================================

        /// <summary>
        /// Identical inputs must give BIT-IDENTICAL outputs, and the answer must not depend on what was
        /// solved before it. Bit-identical rather than "close": a solve that is merely close is carrying
        /// state, and state in a per-frame solve is a source of drift that no single-frame gate can see.
        /// </summary>
        [Test]
        public void ArmSolve_IsDeterministic_AndStateless()
        {
            List<BasisArmSolveInput> corpus = Corpus();
            Assert.That(corpus.Count, Is.GreaterThan(50));

            var first = new BasisArmSolveResult[corpus.Count];
            for (int k = 0; k < corpus.Count; k++)
            {
                BasisArmNet.Solve(corpus[k], out first[k]);
            }

            // (a) repeat, same order.
            for (int k = 0; k < corpus.Count; k++)
            {
                BasisArmNet.Solve(corpus[k], out BasisArmSolveResult again);
                AssertBitIdentical(first[k], again, $"pose {k}, repeated");
            }

            // (b) reverse order, and interleaved with an unrelated pose -- if the solve carries per-call
            //     state, the neighbour changes the answer.
            for (int k = corpus.Count - 1; k >= 0; k--)
            {
                BasisArmNet.Solve(corpus[(k + 7) % corpus.Count], out _);
                BasisArmNet.Solve(corpus[k], out BasisArmSolveResult again);
                AssertBitIdentical(first[k], again, $"pose {k}, solved after an unrelated pose");
            }
        }

        static void AssertBitIdentical(in BasisArmSolveResult a, in BasisArmSolveResult b, string what)
        {
            Assert.IsTrue(BasisArmNet.Same(a.MidDelta, b.MidDelta) && BasisArmNet.Same(a.RootDelta, b.RootDelta)
                       && BasisArmNet.Same(a.HintDelta, b.HintDelta) && BasisArmNet.Same(a.MidPostRoll, b.MidPostRoll)
                       && BasisArmNet.Same(a.TipRotation, b.TipRotation),
                $"{what}: a stream delta changed between two identical calls.");
            Assert.IsTrue(BasisArmNet.Same(a.ElbowSolved, b.ElbowSolved) && BasisArmNet.Same(a.HandSolved, b.HandSolved),
                $"{what}: a solved joint changed between two identical calls.");
            Assert.IsTrue(a.HumeralTwistDeg == b.HumeralTwistDeg && a.HumeralTwistGuardDeg == b.HumeralTwistGuardDeg
                       && a.ForearmRollDeg == b.ForearmRollDeg && a.WristReliefDeg == b.WristReliefDeg
                       && a.ElbowAngleDeg == b.ElbowAngleDeg && a.PoleConditioning == b.PoleConditioning,
                $"{what}: a diagnostic changed between two identical calls.");
        }

        // ============================================================================================
        // 2. DEGENERATE INPUT HYGIENE
        // ============================================================================================

        /// <summary>
        /// Every degeneracy the rig can actually hand this solver, run through it. NOTHING may come out as
        /// NaN or as a non-unit quaternion -- MidPostRoll especially, because the runtime multiplies it into
        /// the forearm UNCONDITIONALLY, so the zero quaternion that `default` would leave there is not a
        /// no-op, it collapses the bone.
        ///
        /// Deliberately finite-but-degenerate rather than NaN-valued positions: the core's reject-unless-good
        /// guards are written to survive ZERO-LENGTH and COLLINEAR geometry, which is what a rig with an
        /// unbaked bone, a T-posed straight arm or a tracker sitting exactly on the arm axis produces.
        /// </summary>
        [Test]
        public void DegenerateInputs_EmitNoNaN_AndOnlyValidRotations()
        {
            var findings = new List<string>();
            var degenerateNonUnit = new List<string>();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            BasisArmNet.Spec s = BasisArmNet.Default(rig);
            s.TargetDir = Dir(25f, -30f);
            s.Reach = 0.9f;
            s.HandRollDeg = 60f;
            s.HintMode = BasisArmNet.HintTracker;
            s.HintAzimuthDeg = 40f;
            s.HintRhoMin = 0.01f;
            s.TrackerRollDeg = 30f;
            s.FeedTwistBind = true;
            s.FeedLowerBind = true;
            s.FeedTip = true;
            BasisArmSolveInput baseline = BasisArmNet.Build(s);

            var cases = new List<(string name, Func<BasisArmSolveInput, BasisArmSolveInput> mutate)>
            {
                ("upper segment zero length (elbow ON the shoulder)", i => { i.Elbow = i.Shoulder; return i; }),
                ("lower segment zero length (hand ON the elbow)", i => { i.Hand = i.Elbow; return i; }),
                ("both segments zero (whole arm collapsed to a point)", i => { i.Elbow = i.Shoulder; i.Hand = i.Shoulder; return i; }),
                ("animated arm dead straight (collinear)", i => { i.Elbow = i.Shoulder + (i.Hand - i.Shoulder) * 0.53f; return i; }),
                ("target exactly AT the shoulder", i => { i.TargetPosition = i.Shoulder; return i; }),
                ("target far past reach (3x)", i => { i.TargetPosition = i.Shoulder + (i.TargetPosition - i.Shoulder) * 3f; return i; }),
                ("target exactly at the animated hand", i => { i.TargetPosition = i.Hand; return i; }),
                ("zero RootRotation", i => { i.RootRotation = default; return i; }),
                ("zero MidRotation", i => { i.MidRotation = default; return i; }),
                ("zero TargetRotation", i => { i.TargetRotation = default; return i; }),
                ("zero TargetOffset", i => { i.TargetOffset = default; return i; }),
                ("zero TipRotation (declines the relief)", i => { i.TipRotation = default; return i; }),
                ("zero HintRotation (declines the tracker roll)", i => { i.HintRotation = default; return i; }),
                ("zero ClavicleRotation", i => { i.ClavicleRotation = default; return i; }),
                ("zero BindClavicleRotation", i => { i.BindClavicleRotation = default; return i; }),
                ("zero BindHumerusRotation", i => { i.BindHumerusRotation = default; return i; }),
                ("zero BindLowerArmRotation", i => { i.BindLowerArmRotation = default; return i; }),
                ("zero BindHumerusDir", i => { i.BindHumerusDir = Vector3.zero; return i; }),
                ("zero BindHumerusRefAxis", i => { i.BindHumerusRefAxis = Vector3.zero; return i; }),
                ("reference axis laid ALONG the bone", i => { i.BindHumerusRefAxis = Quaternion.Inverse(i.BindHumerusRotation) * i.BindHumerusDir; return i; }),
                ("bind humerus dir ANTI-PARALLEL to the live one", i => { i.BindHumerusDir = -i.BindHumerusDir; return i; }),
                ("zero PlayerUp", i => { i.PlayerUp = Vector3.zero; return i; }),
                ("zero TorsoUp (declines to PlayerUp)", i => { i.TorsoUp = Vector3.zero; return i; }),
                ("zero PlayerUp AND zero TorsoUp", i => { i.PlayerUp = Vector3.zero; i.TorsoUp = Vector3.zero; return i; }),
                ("hint exactly at the shoulder", i => { i.HintPosition = i.Shoulder; return i; }),
                ("hint exactly at the hand target", i => { i.HintPosition = i.TargetPosition; return i; }),
                ("hint ON the shoulder->hand axis", i => { i.HintPosition = i.Shoulder + (i.TargetPosition - i.Shoulder) * 0.5f; return i; }),
                ("hint at 1e-7 m from the axis (sub-epsilon pole)", i =>
                {
                    Vector3 ax = (i.TargetPosition - i.Shoulder).normalized;
                    Vector3 p = Vector3.Cross(ax, Vector3.up).normalized;
                    i.HintPosition = i.Shoulder + ax * 0.25f + p * 1e-7f; return i;
                }),
                ("HintMaxStepDeg = 0 (a rate limit that permits nothing)", i => { i.HintMaxStepDeg = 0f; return i; }),
                ("HintMaxStepDeg = NaN", i => { i.HintMaxStepDeg = float.NaN; return i; }),
                ("HintMaxStepDeg negative", i => { i.HintMaxStepDeg = -30f; return i; }),
                ("HasPrevPole with a ZERO PrevPoleDir", i => { i.HasPrevPole = true; i.PrevPoleDir = Vector3.zero; i.PrevHintRotation = i.HintRotation; return i; }),
                ("HasPrevPole with ZERO quaternions", i => { i.HasPrevPole = true; i.PrevPoleDir = Vector3.up; i.PrevHintRotation = default; i.HintRotation = default; return i; }),
                ("PrevPoleDir laid ALONG the arm axis", i =>
                {
                    i.HasPrevPole = true;
                    i.PrevPoleDir = (i.TargetPosition - i.Shoulder).normalized;
                    i.PrevHintRotation = i.HintRotation; return i;
                }),
                ("everything at the origin", i =>
                {
                    i.Shoulder = Vector3.zero; i.Elbow = Vector3.zero; i.Hand = Vector3.zero;
                    i.TargetPosition = Vector3.zero; i.HintPosition = Vector3.zero; return i;
                }),
                ("a fully default input struct", _ => default),
            };

            foreach (var c in cases)
            {
                BasisArmSolveInput i = c.mutate(baseline);
                BasisArmSolveResult r;
                try
                {
                    BasisArmNet.Solve(i, out r);
                }
                catch (Exception ex)
                {
                    findings.Add($"[{c.name}] THREW {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (!BasisArmNet.Finite(r.MidDelta) || !BasisArmNet.Finite(r.RootDelta) || !BasisArmNet.Finite(r.HintDelta)
                    || !BasisArmNet.Finite(r.MidPostRoll) || !BasisArmNet.Finite(r.TipRotation))
                {
                    findings.Add($"[{c.name}] emitted a NON-FINITE stream delta. A NaN transform PERSISTS in Unity: " +
                                 "the arm would never recover even once good data returned.");
                    continue;
                }
                if (!BasisArmNet.Finite(r.ElbowSolved) || !BasisArmNet.Finite(r.HandSolved)
                    || !BasisArmNet.Finite(r.RootRotationSolved) || !BasisArmNet.Finite(r.MidRotationSolved))
                {
                    findings.Add($"[{c.name}] emitted a NON-FINITE solved pose.");
                    continue;
                }
                if (!BasisArmNet.Finite(r.HumeralTwistDeg) || !BasisArmNet.Finite(r.HumeralTwistGuardDeg)
                    || !BasisArmNet.Finite(r.ForearmRollDeg) || !BasisArmNet.Finite(r.WristReliefDeg)
                    || !BasisArmNet.Finite(r.ElbowAngleDeg) || !BasisArmNet.Finite(r.ReachRatio)
                    || !BasisArmNet.Finite(r.PoleConditioning) || !BasisArmNet.Finite(r.HandError))
                {
                    findings.Add($"[{c.name}] emitted a NaN DIAGNOSTIC. Diagnostics feed gates and sliders; " +
                                 "a NaN one silently disables whatever reads it.");
                    continue;
                }

                // MidPostRoll is multiplied into the forearm UNCONDITIONALLY, so it is the one that must be a
                // rotation on EVERY path out of the method -- including the paths that decline.
                if (!BasisArmNet.Unit(r.MidPostRoll))
                    findings.Add($"[{c.name}] MidPostRoll is not a unit quaternion ({r.MidPostRoll}). The runtime " +
                                 "multiplies it into the forearm on every frame; the zero quaternion `default` " +
                                 "leaves there is not a no-op, it collapses the bone.");
                if (!BasisArmNet.Unit(r.MidDelta))
                {
                    // ⛔ KNOWN-OPEN, and narrow: with EVERY joint coincident there is no bend plane, every
                    // fallback in the chain is degenerate too (PlayerUp is zero as well), so `axis.normalized`
                    // is the ZERO vector -- and the very next line builds
                    //     deltaR = new Quaternion(axis.x*sin, axis.y*sin, axis.z*sin, cos)
                    // from it without a reject-unless-good check. cos is not 1 because the elbow angle was
                    // clamped up to MinElbowAngleDeg, so the result is (0,0,0,0.98): a SCALED identity, which
                    // the runtime then multiplies into the forearm. Reachable only from a completely unbaked
                    // rig, but it is the one place in this solver where a zero axis reaches a quaternion.
                    degenerateNonUnit.Add($"[{c.name}] MidDelta = {r.MidDelta} (norm {Mathf.Sqrt(r.MidDelta.x * r.MidDelta.x + r.MidDelta.y * r.MidDelta.y + r.MidDelta.z * r.MidDelta.z + r.MidDelta.w * r.MidDelta.w):0.00000})");
                }
                if (!BasisArmNet.Unit(r.RootDelta)) findings.Add($"[{c.name}] RootDelta is not a unit quaternion ({r.RootDelta}).");
                if (!BasisArmNet.Unit(r.HintDelta)) findings.Add($"[{c.name}] HintDelta is not a unit quaternion ({r.HintDelta}).");
            }

            BasisArmNet.Report(findings, null, $"degenerate-input hygiene over {cases.Count} cases");
            TestContext.WriteLine($"  {cases.Count} degenerate inputs: no NaN, no exception.");

            if (degenerateNonUnit.Count > 0)
            {
                BasisArmNet.KnownOpen(
                    "every quaternion this solver emits must be a rotation, on every path out, including the " +
                    "paths that decline -- the runtime multiplies them into bones unconditionally",
                    $"{degenerateNonUnit.Count} case(s) emit a NON-UNIT quaternion: " +
                    string.Join("; ", degenerateNonUnit) +
                    ". Cause: with every joint coincident AND PlayerUp zero, the whole bend-plane fallback chain " +
                    "is degenerate, `axis.normalized` returns the ZERO vector, and deltaR is built from it " +
                    "without a reject-unless-good check. Fix shape: fall back to identity when the axis is zero.");
            }
        }

        // ============================================================================================
        // 3. ⭐ CLASS 7 -- A STALE OR DEGENERATE SEED MUST BE CORRECTABLE
        // ============================================================================================

        /// <summary>
        /// ⭐ THE POLE ANCHOR MUST BE ABLE TO BE WRONG, AND THEN GET BETTER. THIS NEEDS THE LOOP.
        ///
        /// The anchor is a PAIR -- a pole direction and the tracker rotation it was measured at -- carried
        /// forward frame to frame. A tracker dropout at full extension forces a re-seed, and at full
        /// extension the elbow's lever arm has collapsed, so the seed comes from a noise-length vector: a
        /// uniformly random direction. MEASURED over 12 noise seeds: the elbow ended 1.9 to 97.4 degrees
        /// off the tracker's real pole (median ~60) AND STAYED THERE.
        ///
        /// Every individual frame of that is a perfectly good solve. It is only wrong as a SEQUENCE, so
        /// this test runs the sequence: seed the anchor deliberately wrong, then hold a relaxed arm and
        /// feed each frame's PoleDirUsed / PoleRotUsed into the next frame's PrevPoleDir / PrevHintRotation,
        /// exactly as BasisFullBodyIK does.
        ///
        /// TWO PROPERTIES, AND THEY PULL IN OPPOSITE DIRECTIONS, WHICH IS WHY BOTH ARE HERE:
        ///   CONVERGES at a relaxed arm, where the measurement has some conditioning (poleCondW in (0,1)) --
        ///     and the test ASSERTS that conditioning is strictly interior, or it is not exercising the ease
        ///     branch at all;
        ///   HOLDS EXACTLY at full extension (poleCondW == 0), where the measurement is noise and easing
        ///     toward it would restore the 179 degree flips the anchor exists to stop.
        /// </summary>
        [Test]
        public void PoleAnchor_ConvergesFromABadSeed_AndHoldsWhereTheMeasurementIsNoise()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            var log = new StringBuilder();

            // A relaxed 164-degree arm: straight enough that the anchor is doing the work, bent enough that
            // the measurement has SOME conditioning. Reach is computed from the elbow angle so the geometry
            // is honest rather than picked.
            float flex = 164f;
            float d = Mathf.Sqrt(BasisArmNet.Upper * BasisArmNet.Upper + BasisArmNet.Fore * BasisArmNet.Fore
                                 - 2f * BasisArmNet.Upper * BasisArmNet.Fore * Mathf.Cos(flex * Mathf.Deg2Rad));
            float relaxedReach = d / BasisArmNet.Total;

            float worstFinal = 0f, worstInitial = 0f;
            float minCond = 1f, maxCond = 0f;

            for (int seed = 0; seed < 12; seed++)
            {
                float badAzimuth = 30f + seed * 27f;      // the noise seed, spread round the circle
                const float trueAzimuth = 200f;

                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(15f, -40f);
                s.Reach = relaxedReach;
                s.HintMode = BasisArmNet.HintTracker;
                s.HintAzimuthDeg = trueAzimuth;
                s.HintRhoMin = 0f;                        // the TRUE elbow: rho collapses honestly
                s.TrackerRollDeg = 0f;
                s.RefPerp = Vector3.up;
                s.FeedTwistBind = false;                  // isolate the anchor: no twist guard in the loop
                BasisArmSolveInput i = BasisArmNet.Build(s);

                Vector3 acN = (i.TargetPosition - i.Shoulder).normalized;
                Vector3 truePole = BasisArmNet.Orthonormal(i.HintPosition - i.Shoulder, acN);
                Vector3 badPole = Quaternion.AngleAxis(badAzimuth, acN) * truePole;

                i.HasPrevPole = true;
                i.PrevPoleDir = badPole;
                i.PrevHintRotation = i.HintRotation;

                float initialErr = 0f, finalErr = 0f;
                for (int frame = 0; frame < 400; frame++)
                {
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    minCond = Mathf.Min(minCond, r.PoleConditioning);
                    maxCond = Mathf.Max(maxCond, r.PoleConditioning);

                    Vector3 elbowPole = BasisArmNet.Orthonormal(r.ElbowSolved - i.Shoulder, acN);
                    float err = Mathf.Abs(Vector3.SignedAngle(elbowPole, truePole, acN));
                    if (frame == 0) initialErr = err;
                    finalErr = err;

                    Assert.IsTrue(r.PoleAnchorValid, $"seed {seed} frame {frame}: the anchor stopped being storable mid-hold.");
                    i.PrevPoleDir = r.PoleDirUsed;
                    i.PrevHintRotation = r.PoleRotUsed;
                }

                worstInitial = Mathf.Max(worstInitial, initialErr);
                worstFinal = Mathf.Max(worstFinal, finalErr);
                log.AppendLine($"      seed {seed,2} ({badAzimuth,5:F0} deg off): elbow starts {initialErr,6:F2} deg " +
                               $"off the tracker's pole, ends {finalErr,6:F2} deg off");
            }

            // The ease branch must actually be the thing under test: strictly interior conditioning.
            Assert.That(minCond, Is.GreaterThan(0f),
                $"the relaxed arm gave a pole conditioning of {minCond:0.0000}, i.e. zero -- at zero the anchor is a " +
                "pure HOLD by design and this loop is not exercising the correction at all.");
            Assert.That(maxCond, Is.LessThan(1f),
                $"the relaxed arm gave a pole conditioning of {maxCond:0.0000} -- at 1 the anchor is refreshed outright " +
                "and the ease branch never runs.");

            BasisArmNet.Gate(
                "the elbow's residual error against the elbow tracker's real pole after 400 frames of holding a " +
                "relaxed arm, having re-acquired the tracker while the arm was straight",
                worstFinal, 5f, worstInitial, 25f);

            // ── AND IT MUST HOLD WHERE THE MEASUREMENT IS NOISE.
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(15f, -40f);
                s.Reach = 1.02f;                          // past full stretch: poleCondW is 0
                s.HintMode = BasisArmNet.HintTracker;
                s.HintAzimuthDeg = 200f;
                s.HintRhoMin = 0f;
                s.TrackerRollDeg = 0f;
                s.RefPerp = Vector3.up;
                BasisArmSolveInput i = BasisArmNet.Build(s);
                Vector3 acN = (i.TargetPosition - i.Shoulder).normalized;
                Vector3 held = BasisArmNet.Orthonormal(Vector3.Cross(acN, Vector3.forward), acN);
                i.HasPrevPole = true;
                i.PrevPoleDir = held;
                i.PrevHintRotation = i.HintRotation;

                float drift = 0f;
                for (int frame = 0; frame < 300; frame++)
                {
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    Assert.That(r.PoleConditioning, Is.EqualTo(0f),
                        $"at reach {s.Reach:0.00} the pole conditioning is {r.PoleConditioning:0.0000}, not 0 -- this half " +
                        "of the test is supposed to sit exactly on the HOLD branch.");
                    drift = Mathf.Max(drift, Vector3.Angle(r.PoleDirUsed, held));
                    i.PrevPoleDir = r.PoleDirUsed;
                    i.PrevHintRotation = r.PoleRotUsed;
                }
                Assert.That(drift, Is.LessThan(1e-3f),
                    $"at zero conditioning the anchor drifted {drift:0.0000} deg over 300 frames. It must be the EXACT " +
                    "old hold there, bit for bit: easing toward a measurement that is pure noise is what the 179 deg " +
                    "flips were.");
                log.AppendLine($"      zero-conditioning hold: anchor drifted {drift:0.000000} deg over 300 frames");
            }

            TestContext.WriteLine($"\n  pole-anchor convergence (conditioning {minCond:0.000}..{maxCond:0.000}):\n" + log);
        }

        // ============================================================================================
        // 4. ⭐ CLASS 6 -- A MOTION THAT ROTATES NO AXIS MUST STILL ARTICULATE THE JOINT
        // ============================================================================================

        /// <summary>
        /// ⭐ A STRAIGHT-LINE RADIAL REACH ROTATES THE SHOULDER->HAND AXIS BY EXACTLY ZERO.
        ///
        /// That is the input a budget derived from axis rotation computes as zero, and it is why such a
        /// budget froze the joint completely -- not "moved it less", froze it, on the single most common
        /// motion a person makes with their arm. The property is: the elbow must still track its commanded
        /// pole through the whole reach, and it must travel the distance the triangle solve REQUIRES.
        ///
        /// The non-vacuity control is pure geometry and owes nothing to the solver: as the arm extends from
        /// 0.55 to 0.98 of reach the elbow MUST move by a computable amount. If the solved elbow travels
        /// materially less than that, something is capping it.
        /// </summary>
        [Test]
        public void RadialReach_RotatesNoAxis_AndStillArticulatesTheElbow()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            var log = new StringBuilder();

            foreach (float maxStep in new[] { float.MaxValue, 12f, 3f })
            {
                float worstPoleErr = 0f, solvedTravel = 0f, requiredTravel = 0f, worstAxisRot = 0f;
                Vector3 prevSolved = Vector3.zero, prevRequired = Vector3.zero, prevAxis = Vector3.zero;
                bool first = true;

                for (int k = 0; k <= 400; k++)
                {
                    float reach = Mathf.Lerp(0.55f, 0.98f, k / 400f);
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(20f, -35f);
                    s.Reach = reach;
                    s.HintMode = BasisArmNet.HintTracker;
                    s.HintAzimuthDeg = 180f;         // the BOTTOM of the elbow circle: the anatomy guard is inert here
                    s.HintRhoMin = 0f;
                    s.RefPerp = Vector3.up;
                    s.MaxStepDeg = maxStep;
                    // ⭐ The ANIMATED arm already lies along the target line, which is the live rig's ordinary
                    // state and the only configuration in which the solve's rootDelta is the exact identity.
                    // Without it the animated arm hangs somewhere else, rootDelta is a fixed non-zero
                    // rotation, and "this motion rotates no axis" would be a claim about the swept SOLUTION
                    // rather than about anything the solver is handed.
                    s.AnimAlongTarget = true;
                    s.AnimAzimuthDeg = 180f;
                    s.FeedTwistBind = true;
                    s.FeedLowerBind = true;

                    BasisArmSolveInput i = BasisArmNet.Build(s);
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    BasisArmNet.StreamCompose(i, r, out Vector3 elbow, out Vector3 hand, out _, out _);

                    Vector3 axis = (hand - i.Shoulder).normalized;
                    Vector3 want = i.HintPosition;

                    Vector3 solvedPole = BasisArmNet.Orthonormal(elbow - i.Shoulder, axis);
                    Vector3 wantPole = BasisArmNet.Orthonormal(want - i.Shoulder, axis);
                    if (solvedPole != Vector3.zero && wantPole != Vector3.zero)
                    {
                        worstPoleErr = Mathf.Max(worstPoleErr, Vector3.Angle(solvedPole, wantPole));
                    }

                    if (!first)
                    {
                        solvedTravel += Vector3.Distance(elbow, prevSolved);
                        requiredTravel += Vector3.Distance(want, prevRequired);
                        worstAxisRot = Mathf.Max(worstAxisRot, Vector3.Angle(axis, prevAxis));
                    }
                    prevSolved = elbow; prevRequired = want; prevAxis = axis; first = false;
                }

                log.AppendLine($"      rate limit {(float.IsInfinity(maxStep) || maxStep == float.MaxValue ? "none" : maxStep.ToString("0") + " deg"),8}: " +
                               $"axis turned at most {worstAxisRot,8:F5} deg/step; elbow travelled {solvedTravel * 1000f,7:F1} mm " +
                               $"against a required {requiredTravel * 1000f,7:F1} mm; worst pole error {worstPoleErr,6:F2} deg");

                Assert.That(worstAxisRot, Is.LessThan(1e-3f),
                    $"the shoulder->hand axis turned {worstAxisRot:0.00000} deg per step during what is supposed to be a " +
                    "purely RADIAL reach; the harness is not producing the input this test is about.");
                Assert.That(requiredTravel, Is.GreaterThan(0.05f),
                    $"the geometry only requires the elbow to travel {requiredTravel * 1000f:0.0} mm over this reach; " +
                    "the control is too weak to detect a frozen joint.");
                Assert.That(solvedTravel, Is.GreaterThan(0.85f * requiredTravel),
                    $"with a rate limit of {maxStep}, the elbow travelled {solvedTravel * 1000f:0.0} mm where the triangle " +
                    $"solve REQUIRES {requiredTravel * 1000f:0.0} mm. A straight-line reach rotates the arm axis by exactly " +
                    "zero, so any budget derived from axis rotation is structurally zero here -- and the joint freezes on " +
                    "the commonest motion a person makes.");
                Assert.That(worstPoleErr, Is.LessThan(2f),
                    $"the elbow stood up to {worstPoleErr:0.0} deg off the pole the tracker commanded during a radial reach.");
            }

            TestContext.WriteLine("\n  radial reach (axis rotation identically zero):\n" + log);
        }
    }
}
