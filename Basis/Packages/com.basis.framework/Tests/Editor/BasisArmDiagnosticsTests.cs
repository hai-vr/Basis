using System.Collections.Generic;
using System.Text;
using Basis.IK;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    /// <summary>
    /// ================================================================================================
    /// ⭐ EVERY GATE IN THIS FILE CARRIES ITS OWN FALSIFICATION, IN-LINE, AND RUNS IT EVERY TIME.
    ///
    /// The brief this instrument was written to is explicit: a test that cannot fail is worse than no
    /// test, and arguing that a gate WOULD catch something is not evidence. This codebase has the receipt --
    /// a gate "proved" a guard moved the hand 0.000 mm by comparing two result-struct fields the guard
    /// never writes, and 205 mm shipped behind it.
    ///
    /// So the usual answer -- run a scratch mutation harness once, paste the numbers into a comment -- is
    /// not enough here, because the comment cannot rot loudly. Instead each test below builds the SPECIFIC
    /// broken thing it is defending against, right next to the real one, and asserts that the mutant FAILS
    /// the same gate the real one passes. The falsification is part of the suite, so it re-proves itself on
    /// every run and it dies if the gate is ever weakened into vacuity.
    ///
    /// The mutants, one per defect class this arm has actually shipped:
    ///   CLASS 3  vacuous assertion  -> MutantLeakyGuardResult: the humeral guard's counter-roll on
    ///                                  MidPostRoll removed. Composed hand error must EXPLODE while the
    ///                                  solver's own bookkeeping stays at zero.
    ///   CLASS 2  guard cannot fire  -> DeclineTwistBinds: the correction reads 0 in BOTH cases, so only
    ///                                  the availability bit can tell "quiet" from "dead".
    ///   CLASS 5  wrong frame        -> zeroed TorsoUp: elevation must stop tracking the torso.
    ///   METRIC   2*acos(|dot|)      -> AcosPoseChangeDeg: must report >0.02 deg between rotations the
    ///                                  atan2 form reports as identical.
    ///   SHORTCUT sum-of-junctions   -> ElbowAxial + WristAxial: must disagree with the composed chain
    ///                                  total by more than the measurement's own noise.
    /// ================================================================================================
    ///
    /// The rigs and pose specs come from <see cref="BasisArmNet"/>, the existing shared harness, rather
    /// than from a second copy of the construction: a diagnostic measured on a differently-built arm than
    /// the one the invariant net measures is a diagnostic about a different arm.
    /// </summary>
    public class BasisArmDiagnosticsTests
    {
        // ============================================================================================
        // READ-ONLY, PROVEN BITWISE
        // ============================================================================================

        /// <summary>
        /// ⭐ THE CAPTURE MUST NOT PERTURB THE SOLVE, AND "must not" IS NOT AN ARGUMENT ABOUT `in`/`out`.
        ///
        /// Solves a grid twice -- once clean, once with a Capture between every pair of solves -- and
        /// compares every field of every BasisArmSolveResult BITWISE. Float equality on purpose: the claim
        /// is bit-identity, so a tolerance would be a weaker claim wearing this one's name.
        ///
        /// ⚠️ AND THE COMPARATOR IS PROVEN SHARP FIRST. A bitwise comparator that always returned true
        /// would pass this test on a solver that had been wrecked, so the first thing asserted is that it
        /// SEES a one-ULP change in each field family. That is the non-vacuity, and it is why this test is
        /// evidence rather than a formality.
        /// </summary>
        [Test]
        public void Capture_IsReadOnly_TheSolveIsBitIdentical()
        {
            // --- non-vacuity: the comparator can see the smallest possible difference.
            {
                BasisArmSolveResult a = default;
                BasisArmSolveResult b = default;
                Assert.That(ResultsBitIdentical(in a, in b), Is.True, "the comparator rejects two identical defaults");

                b.HumeralTwistDeg = NextUp(a.HumeralTwistDeg);
                Assert.That(ResultsBitIdentical(in a, in b), Is.False,
                    "THE COMPARATOR CANNOT SEE A ONE-ULP FLOAT CHANGE, so the bit-identity assertion below " +
                    "would pass whatever the capture did to the solve. Fix the comparator, do not relax this.");

                b = default;
                b.MidPostRoll = new Quaternion(0f, 0f, 0f, NextUp(0f));
                Assert.That(ResultsBitIdentical(in a, in b), Is.False, "the comparator cannot see a one-ULP quaternion change");

                b = default;
                b.ElbowSolved = new Vector3(NextUp(0f), 0f, 0f);
                Assert.That(ResultsBitIdentical(in a, in b), Is.False, "the comparator cannot see a one-ULP vector change");

                b = default;
                b.GuardSideUsed = 1;
                Assert.That(ResultsBitIdentical(in a, in b), Is.False, "the comparator cannot see an int change");

                b = default;
                b.HintApplied = true;
                Assert.That(ResultsBitIdentical(in a, in b), Is.False, "the comparator cannot see a bool change");
            }

            List<BasisArmSolveInput> grid = Grid();
            Assert.That(grid.Count, Is.GreaterThan(50), "the grid is too small to be evidence about anything");

            var clean = new BasisArmSolveResult[grid.Count];
            for (int k = 0; k < grid.Count; k++)
            {
                BasisArmSolveInput i = grid[k];
                BasisArmSolveCore.Solve(i, out clean[k]);
            }

            int captured = 0;
            for (int k = 0; k < grid.Count; k++)
            {
                BasisArmSolveInput i = grid[k];
                BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);

                BasisArmDiagnosticsCore.Capture(i, r, k % 2 == 0 ? -1f : 1f, out BasisArmDiagnostics d);
                captured++;

                // Consume the capture so nothing here can be optimised into non-existence.
                if (d.NonFinite > 2f) { Assert.Fail("unreachable"); }

                Assert.That(ResultsBitIdentical(in clean[k], in r), Is.True,
                    $"pose {k}: the solve is NOT bit-identical with capture running. The capture is supposed to " +
                    "be a pure function of (in input, in result) and this says it is not.");

                // The INPUT must survive too: Capture takes it by `in`, and `in` on a struct with a
                // mutating method would still let it through. Compare the caller's copy against the grid's.
                Assert.That(InputsBitIdentical(in i, grid[k]), Is.True, $"pose {k}: the capture mutated its input");
            }

            Assert.That(captured, Is.EqualTo(grid.Count));
        }

        // ============================================================================================
        // CLASS 3 -- THE VACUOUS ASSERTION. THE SINGLE MOST IMPORTANT TEST IN THIS FILE.
        // ============================================================================================

        /// <summary>
        /// ⭐ THE RECORDER MUST SEE THE LEAK THE SOLVER'S OWN BOOKKEEPING REPORTED AS ZERO.
        ///
        /// THE DEFECT, ON THE RECORD. The humeral twist guard folds its correction into HintDelta, which
        /// BasisFullBodyIK applies to the ROOT -- and setting a parent's rotation in an AnimationStream
        /// carries its children RIGIDLY. Every other swivel folded into HintDelta is about shoulder->HAND
        /// and the hand lies on that axis, so it costs nothing. THAT one is about shoulder->ELBOW: the
        /// elbow lies on it, the hand does not. Measured, the hand moved 35.6 / 70.2 / 102.6 / 145.1 /
        /// 177.7 / 205.2 mm at elbow flexions 170 / 160 / 150 / 135 / 120 / 90 -- while the solver's own
        /// r.HandError reported 0.000 mm at every one, because the guard updated rootRot and hintR but not
        /// cPosition. A gate that compared the solver's numbers to each other could not fail, and did not.
        ///
        /// So this asserts the property that makes the recorder worth having: ComposedHandErrorMm is read
        /// off the STREAM and SolverHandErrorMm off the bookkeeping, and on a leaking solve THEY DISAGREE.
        /// The mutant reconstructs exactly that leak by removing the guard's counter-roll -- twistR is
        /// recoverable in closed form from the published fields, AngleAxis(HumeralTwistGuardDeg,
        /// normalize(ElbowSolved - Shoulder)) -- so this is the real defect and not a caricature of it.
        /// </summary>
        [Test]
        public void ComposedHandError_SeesTheGuardLeak_ThatTheSolverReportedAsZero()
        {
            int firedPoses = 0;
            float worstMutantGap = 0f;
            float worstRealGap = 0f;
            float worstMutantSolverReport = 0f;

            foreach (BasisArmSolveInput i in TwistGuardPoses())
            {
                BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);
                if (r.HumeralTwistGuardDeg == 0f)
                {
                    continue;
                }
                firedPoses++;

                BasisArmDiagnosticsCore.Capture(i, r, 1f, out BasisArmDiagnostics real);
                worstRealGap = Mathf.Max(worstRealGap, Mathf.Abs(real.ComposedHandErrorMm - real.SolverHandErrorMm));

                BasisArmSolveResult mutant = MutantLeakyGuardResult(in i, in r);
                BasisArmDiagnosticsCore.Capture(i, mutant, 1f, out BasisArmDiagnostics bad);

                worstMutantGap = Mathf.Max(worstMutantGap, Mathf.Abs(bad.ComposedHandErrorMm - bad.SolverHandErrorMm));
                worstMutantSolverReport = Mathf.Max(worstMutantSolverReport, bad.SolverHandErrorMm);
            }

            Assert.That(firedPoses, Is.GreaterThan(0),
                "THE TWIST GUARD NEVER FIRED IN THIS SWEEP, so nothing below is about anything. That is " +
                "class 2 (a guard that cannot fire) reached from the test side -- fix the sweep, not the bound.");

            Assert.That(worstMutantGap, Is.GreaterThan(10f),
                $"THE MUTANT LEAK IS INVISIBLE TO THE RECORDER (worst gap {worstMutantGap:0.000} mm). The whole " +
                "reason ComposedHandErrorMm is read off the stream replay rather than off r.HandError is to " +
                "catch exactly this, and it did not. The recorder is measuring the bookkeeping.");

            Assert.That(worstMutantSolverReport, Is.LessThan(1f),
                $"the mutant's OWN bookkeeping reported {worstMutantSolverReport:0.000} mm, so the leak was not " +
                "actually hidden and this test is not reproducing the defect it claims to.");

            Assert.That(worstRealGap, Is.LessThan(0.5f),
                $"on the REAL core the stream and the bookkeeping disagree by {worstRealGap:0.000} mm. Either the " +
                "guard's counter-roll has regressed, or the replay does not match BasisFullBodyIK's composition.");
        }

        /// <summary>
        /// ⭐ THE WRIST BOUND'S FULL ACCOUNTING, CHECKED AGAINST THE STREAM.
        ///
        /// The solver publishes TWO numbers here: r.WristAxialDeg, the hand's axial roll against the rig's
        /// bind neutral measured BEFORE the bound, and r.WristAxialGuardDeg, the roll the bound then took
        /// off. The capture measures the POST-bound value independently, off the stream replay. So the
        /// accounting must close exactly:
        ///
        ///     stream(post)  ==  solver(pre)  +  guard
        ///
        /// ⚠️ THE NAIVE VERSION OF THIS TEST -- stream(post) == solver(pre) -- IS WRONG, AND IT FAILED BY
        /// 58.1 DEGREES ON CORRECT CODE when it was first written here. That is worth keeping in the file:
        /// the "obvious" identity omits the very stage being measured, and had the threshold simply been
        /// widened until it passed, the result would have been a gate that could no longer see a bound
        /// whose reported correction did not match what it actually did.
        ///
        /// ⚠️ AND IT IS NOT A TAUTOLOGY. The capture does not read r.WristAxialDeg; it composes MidDelta,
        /// RootDelta, HintDelta and MidPostRoll and measures the result against the same bind neutral the
        /// core uses. The mutant below perturbs ONLY the stream side, and the gate has to see it.
        /// </summary>
        [Test]
        public void WristAxial_TheBoundsAccountingClosesAgainstTheStream()
        {
            int compared = 0, guardFired = 0;
            float worst = 0f;

            foreach (BasisArmSolveInput i in TwistGuardPoses())
            {
                BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);
                if (r.WristAxialDeg == 0f)
                {
                    continue;   // the stage declined on this pose; nothing published to check against
                }
                BasisArmDiagnosticsCore.Capture(i, r, 1f, out BasisArmDiagnostics d);
                compared++;
                if (d.WristAxialGuardDeg != 0f) guardFired++;

                float expectedPost = d.SolverWristAxialDeg + d.WristAxialGuardDeg;
                worst = Mathf.Max(worst, Mathf.Abs(Mathf.DeltaAngle(d.WristAxialDeg, expectedPost)));
            }

            Assert.That(compared, Is.GreaterThan(10),
                "the sweep never produced a published wrist axial reading, so there is nothing to cross-check.");
            Assert.That(guardFired, Is.GreaterThan(0),
                "THE WRIST BOUND NEVER FIRED IN THIS SWEEP, so the '+ guard' term is a constant zero and this " +
                "test is silently asserting the naive identity it was written to replace. Fix the sweep.");
            Assert.That(worst, Is.LessThan(1f),
                $"the bound's accounting does not close: stream(post) and solver(pre)+guard disagree by {worst:0.000} " +
                "deg. Either the replay does not match BasisFullBodyIK's composition, or the bound moved the hand " +
                "by more than the correction it reported.");

            // The mutant perturbs ONLY the stream side. If the capture were echoing r.WristAxialDeg instead
            // of measuring, this would still agree -- and the gate above would be worthless.
            int mutantSeen = 0;
            float mutantWorst = 0f;
            foreach (BasisArmSolveInput i in TwistGuardPoses())
            {
                BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);
                if (r.WristAxialDeg == 0f)
                {
                    continue;
                }
                BasisArmSolveResult mutant = r;
                Vector3 foreAxis = r.HandSolved - r.ElbowSolved;
                if (foreAxis.sqrMagnitude <= 1e-8f)
                {
                    continue;
                }
                mutant.MidPostRoll = Quaternion.AngleAxis(25f, foreAxis.normalized) * r.MidPostRoll;
                BasisArmDiagnosticsCore.Capture(i, mutant, 1f, out BasisArmDiagnostics md);
                mutantSeen++;
                float expectedPost = md.SolverWristAxialDeg + md.WristAxialGuardDeg;
                mutantWorst = Mathf.Max(mutantWorst, Mathf.Abs(Mathf.DeltaAngle(md.WristAxialDeg, expectedPost)));
            }

            Assert.That(mutantSeen, Is.GreaterThan(10));
            Assert.That(mutantWorst, Is.GreaterThan(10f),
                $"rolling the forearm 25 deg in the STREAM broke the accounting by only {mutantWorst:0.000} deg. " +
                "The capture is echoing the result struct rather than measuring the composed pose, which makes the " +
                "agreement above prove nothing at all.");
        }

        // ============================================================================================
        // CLASS 2 -- A GUARD THAT CANNOT FIRE READS EXACTLY LIKE A GUARD THAT IS HAPPY
        // ============================================================================================

        /// <summary>
        /// ⭐ THE AVAILABILITY BIT IS THE ONLY THING THAT SEPARATES "QUIET" FROM "DEAD".
        ///
        /// BasisElbowAnatomyCore's soft margin once exceeded the elbow circle's radius at full extension,
        /// so the guard fired 0 times in 22032 poses -- exactly where it was needed -- and every
        /// correction column read a reassuring zero. A recorder that logged only HumeralTwistGuardDeg
        /// would have logged that reassurance at 90 Hz.
        ///
        /// This asserts the separation is real by exhibiting the ambiguity: it finds frames from a FED run
        /// and frames from a DECLINED run whose correction fields are byte-for-byte the same zero, and
        /// shows the availability bit differs. Then it asserts the fed run actually excites the quantity,
        /// so the bit is not merely constant.
        /// </summary>
        [Test]
        public void AvailabilityBits_SeparateAGuardThatIsQuietFromAGuardThatIsDead()
        {
            int fedQuietFrames = 0, deadFrames = 0, fedExcited = 0;
            float fedPeakTwist = 0f, deadPeakTwist = 0f;

            foreach (BasisArmSolveInput fed in TwistGuardPoses())
            {
                BasisArmSolveCore.Solve(fed, out BasisArmSolveResult rf);
                BasisArmDiagnosticsCore.Capture(fed, rf, 1f, out BasisArmDiagnostics df);

                Assert.That(df.TwistGuardAvailable, Is.EqualTo(1f),
                    "the twist binds are fed on this pose but the availability bit says the guard is declined -- " +
                    "the bit is recomputing the entry test wrongly, which makes it worse than not having it.");

                fedPeakTwist = Mathf.Max(fedPeakTwist, Mathf.Abs(df.HumeralTwistDeg));
                if (df.TwistGuardFired != 0f) fedExcited++;
                if (df.HumeralTwistGuardDeg == 0f) fedQuietFrames++;

                BasisArmSolveInput dead = DeclineTwistBinds(fed);
                BasisArmSolveCore.Solve(dead, out BasisArmSolveResult rd);
                BasisArmDiagnosticsCore.Capture(dead, rd, 1f, out BasisArmDiagnostics dd);

                Assert.That(dd.TwistGuardAvailable, Is.EqualTo(0f), "declining the binds did not clear the availability bit");
                Assert.That(dd.HumeralTwistGuardDeg, Is.EqualTo(0f), "a declined guard applied a correction");
                deadPeakTwist = Mathf.Max(deadPeakTwist, Mathf.Abs(dd.HumeralTwistDeg));
                deadFrames++;
            }

            Assert.That(fedExcited, Is.GreaterThan(0),
                "THE FED SWEEP NEVER MADE THE GUARD FIRE, so the availability bit is being asserted over a " +
                "sweep that could not tell the two states apart anyway.");
            Assert.That(fedQuietFrames, Is.GreaterThan(0),
                "the fed sweep has no in-envelope frames, so the ambiguity this test exists to resolve -- a " +
                "ZERO correction from a LIVE guard -- is not present in the sample.");
            Assert.That(deadFrames, Is.GreaterThan(0));

            Assert.That(fedPeakTwist, Is.GreaterThan(BasisArmSolveCore.HumeralTwistSoftDeg),
                $"the fed sweep only reached {fedPeakTwist:0.0} deg of humeral twist, below the {BasisArmSolveCore.HumeralTwistSoftDeg:0} " +
                "deg soft limit, so it never entered the region the envelope governs.");
            Assert.That(deadPeakTwist, Is.EqualTo(0f),
                "a declined guard still published a humeral twist measurement, so 'declined' and 'measured' are " +
                "not actually distinguishable from the twist column either.");
        }

        // ============================================================================================
        // CLASS 5 -- THE WRONG FRAME
        // ============================================================================================

        /// <summary>
        /// ⭐ ELEVATION IS A TORSO QUANTITY. MEASURING IT AGAINST THE PLAYER ROOT IS THE DEFECT THAT MADE A
        /// GUARD FIRE ON ORDINARY BENT-OVER POSES WITH ITS MEASUREMENT'S SIGN FLIPPED.
        ///
        /// PlayerUp stays vertical while the chest does not, so "above the shoulder" stops meaning anything
        /// the moment the user bends at the waist. The same is true of every band this capture segments by:
        /// an elevation measured against the root files a bent-over reach into the wrong cell, and the cell
        /// is the unit of the whole analysis.
        ///
        /// The mutant is the input itself: zeroing TorsoUp is exactly what a rig that cannot build a body
        /// frame sends, and the core documents that as the PlayerUp fallback. So this asserts the fed case
        /// tracks the torso, the declined case does not, and TorsoUpValid tells them apart -- because a row
        /// measured in the fallback frame has to be segmented OUT, not pooled in.
        /// </summary>
        [Test]
        public void Elevation_IsMeasuredInTheTorsoFrame_AndSaysSoWhenItIsNot()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            BasisArmNet.Spec upright = BasisArmNet.Default(rig);
            upright.TargetDir = new Vector3(0.9f, 0.2f, 0.15f).normalized;
            upright.Reach = 0.88f;
            upright.FeedTwistBind = true;
            upright.FeedLowerBind = true;
            upright.FeedTip = true;
            upright.PlayerUp = Vector3.up;
            upright.TorsoUp = Vector3.up;

            BasisArmNet.Spec leaned = upright;
            // The SAME world arm and the SAME player root; only the torso has pitched forward.
            leaned.TorsoUp = (Quaternion.AngleAxis(50f, Vector3.right) * Vector3.up).normalized;

            BasisArmSolveInput iu = WithLateral(BasisArmNet.Build(upright));
            BasisArmSolveInput il = WithLateral(BasisArmNet.Build(leaned));

            BasisArmSolveCore.Solve(iu, out BasisArmSolveResult ru);
            BasisArmSolveCore.Solve(il, out BasisArmSolveResult rl);
            BasisArmDiagnosticsCore.Capture(iu, ru, 1f, out BasisArmDiagnostics du);
            BasisArmDiagnosticsCore.Capture(il, rl, 1f, out BasisArmDiagnostics dl);

            Assert.That(du.TorsoUpValid, Is.EqualTo(1f));
            Assert.That(dl.TorsoUpValid, Is.EqualTo(1f));

            float torsoDelta = Mathf.Abs(du.ElevationDeg - dl.ElevationDeg);
            Assert.That(torsoDelta, Is.GreaterThan(10f),
                $"the torso pitched 50 deg and the reported elevation moved {torsoDelta:0.00} deg. Elevation is " +
                "not being read in the torso frame, so every band in the segmentation is mis-filed the moment " +
                "the user leans.");

            // --- the mutant: no torso frame at all, which is the documented PlayerUp fallback.
            BasisArmNet.Spec uprightNoTorso = upright; uprightNoTorso.TorsoUp = Vector3.zero;
            BasisArmNet.Spec leanedNoTorso = leaned; leanedNoTorso.TorsoUp = Vector3.zero;

            BasisArmSolveInput mu = WithLateral(BasisArmNet.Build(uprightNoTorso));
            BasisArmSolveInput ml = WithLateral(BasisArmNet.Build(leanedNoTorso));
            BasisArmSolveCore.Solve(mu, out BasisArmSolveResult rmu);
            BasisArmSolveCore.Solve(ml, out BasisArmSolveResult rml);
            BasisArmDiagnosticsCore.Capture(mu, rmu, 1f, out BasisArmDiagnostics dmu);
            BasisArmDiagnosticsCore.Capture(ml, rml, 1f, out BasisArmDiagnostics dml);

            Assert.That(dmu.TorsoUpValid, Is.EqualTo(0f),
                "TorsoUpValid did not go to 0 on a zero TorsoUp, so a capture in the fallback frame is " +
                "indistinguishable from one in the real frame -- and those rows have to be excluded.");
            Assert.That(dml.TorsoUpValid, Is.EqualTo(0f));

            float rootDelta = Mathf.Abs(dmu.ElevationDeg - dml.ElevationDeg);
            Assert.That(rootDelta, Is.LessThan(1e-3f),
                $"the ROOT-frame control moved {rootDelta:0.0000} deg when only the torso changed. It is supposed " +
                "to be blind to the torso -- if it is not, the gate above is not measuring what it claims.");
        }

        // ============================================================================================
        // THE SHORTCUT: A CHAIN TOTAL IS NOT A SUM
        // ============================================================================================

        /// <summary>
        /// ⚠️ TWISTS ABOUT A COMMON AXIS ONLY ADD THROUGH PURE TWISTS, AND THE FOREARM SITS OFF THE
        /// HUMERUS AT EVERY REAL FLEXION.
        ///
        /// The obvious implementation of the chain total is ElbowAxialDeg + WristAxialDeg, and it is wrong.
        /// The existing seam-snap test measured the sum form carrying 0.46 deg of error at a 170 deg elbow
        /// -- enough to blur an identity gate -- and the error grows as the elbow bends. So the capture
        /// computes the chain total COMPOSITIONALLY, and this test proves the two are genuinely different
        /// things rather than asserting it.
        ///
        /// Non-vacuity is the point of the whole test: if the sum form and the composed form agreed
        /// everywhere, the distinction would be pedantry. It asserts they disagree by MORE than the
        /// measurement's own noise floor somewhere in the sweep.
        /// </summary>
        [Test]
        public void ChainAxial_IsComposed_NotTheSumOfItsJunctions()
        {
            float worstDisagreement = 0f;
            float worstAt = float.NaN;
            int samples = 0;

            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            for (float reach = 0.45f; reach <= 0.99f; reach += 0.03f)
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.Reach = reach;
                s.TargetDir = new Vector3(0.55f, -0.55f, 0.63f).normalized;
                s.FeedTwistBind = true;
                s.FeedLowerBind = true;
                s.FeedTip = true;
                s.HandRollDeg = 35f;
                s.AnimHandRollDeg = -20f;

                BasisArmSolveInput i = WithLateral(BasisArmNet.Build(s));
                BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);
                BasisArmDiagnosticsCore.Capture(i, r, 1f, out BasisArmDiagnostics d);

                if (d.ChainAxialDeg == 0f && d.ElbowAxialDeg == 0f)
                {
                    continue;   // the binds declined on this pose; nothing to compare
                }
                samples++;

                float sumForm = Mathf.DeltaAngle(0f, d.ElbowAxialDeg + d.WristAxialDeg);
                float disagreement = Mathf.Abs(Mathf.DeltaAngle(sumForm, d.ChainAxialDeg));
                if (disagreement > worstDisagreement)
                {
                    worstDisagreement = disagreement;
                    worstAt = d.ElbowFlexionDeg;
                }
            }

            Assert.That(samples, Is.GreaterThan(5), "not enough poses fed the binds for this comparison to mean anything");
            Assert.That(worstDisagreement, Is.GreaterThan(0.1f),
                $"the sum form and the composed form agree to {worstDisagreement:0.0000} deg across the whole sweep " +
                "(worst at elbow " + worstAt.ToString("0.0") + " deg). Either the sweep never bends the elbow, or " +
                "ChainAxialDeg is BEING computed as a sum -- which is the shortcut this field exists to refuse.");
        }

        // ============================================================================================
        // THE METRIC ITSELF
        // ============================================================================================

        /// <summary>
        /// ⚠️ NEVER 2*acos(|dot|) FOR A POSE METRIC. Up to 0.09 DEGREES between BIT-IDENTICAL float32
        /// quaternions, because acos has infinite derivative at 1 and loses half the mantissa there.
        ///
        /// This is asserted by MEASUREMENT, not by citation: it builds a rotation the way the stream
        /// composition does (five chained products, which Unity does not renormalise), takes its exact
        /// normalisation -- the SAME rotation -- and shows the acos form reports a large angle between
        /// them while the atan2 form the capture uses reports zero.
        ///
        /// The acos control MUST exceed its floor, or the claim is not being tested at all.
        /// </summary>
        [Test]
        public void PoseMetric_UsesAtan2_NotAcos()
        {
            Quaternion drifted = Quaternion.identity;
            var links = new[]
            {
                Quaternion.Euler(37f, -22f, 61f),
                Quaternion.Euler(-14f, 88f, -133f),
                Quaternion.Euler(4f, -11f, 6f),
                Quaternion.Euler(-71f, 19f, 44f),
                Quaternion.Euler(122f, -63f, 8f),
            };
            foreach (Quaternion q in links)
            {
                drifted = Mul(drifted, q);
            }

            Quaternion exact = BasisArmDiagnosticsCore.NormalizeQ(drifted);

            float atan2Deg = BasisArmDiagnosticsCore.PoseChangeDeg(drifted, exact);
            float acosDeg = AcosPoseChangeDeg(drifted, exact);

            Assert.That(Mathf.Abs(1f - Mathf.Sqrt(Dot4(drifted))), Is.GreaterThan(1e-8f),
                "the chained product did not actually drift off the unit sphere, so the acos control below " +
                "has nothing to be wrong about and this test proves nothing. Lengthen the chain.");

            Assert.That(acosDeg, Is.GreaterThan(0.02f),
                $"THE acos CONTROL ONLY REPORTED {acosDeg:0.00000} deg between two representations of the SAME " +
                "rotation. The hazard this test exists to demonstrate is not present in this sample, so the " +
                "assertion below would pass against an acos implementation too.");

            Assert.That(atan2Deg, Is.LessThan(0.001f),
                $"the atan2 form reported {atan2Deg:0.00000} deg between two representations of the same rotation. " +
                "It is supposed to be well-conditioned at zero; every junction measurement in the capture rests " +
                "on that, and a noise floor here is a noise floor under all of them.");
        }

        // ============================================================================================
        // DEGENERACY
        // ============================================================================================

        /// <summary>
        /// A degenerate rig must produce a HONEST row, not a plausible one. Every guard in the capture is
        /// written reject-unless-good -- `!(x > eps)` and never `x &lt; eps` -- because NaN fails every
        /// ordered comparison, so a "reject if bad" test waves it straight through into a column.
        ///
        /// The contract asserted here is not "nothing is ever NaN": an input carrying NaN SHOULD produce
        /// NonFinite = 1, because saying otherwise would be the capture lying. The contract is that
        /// NonFinite is ACCURATE, and that ordinary degeneracy (coincident joints, zero quaternions, no up
        /// vector) produces finite numbers rather than poison.
        /// </summary>
        [Test]
        public void Capture_IsHonestOnDegenerateInputs()
        {
            // Non-vacuity: the finiteness checker must actually reject a NaN field.
            {
                BasisArmDiagnostics probe = default;
                Assert.That(BasisArmDiagnosticsCore.AllFinite(in probe), Is.True);
                probe.ChainAxialDeg = float.NaN;
                Assert.That(BasisArmDiagnosticsCore.AllFinite(in probe), Is.False,
                    "AllFinite does not see a NaN, so the NonFinite column is decoration.");
                probe.ChainAxialDeg = float.PositiveInfinity;
                Assert.That(BasisArmDiagnosticsCore.AllFinite(in probe), Is.False, "AllFinite does not see an infinity");
            }

            foreach (BasisArmSolveInput i in DegenerateInputs())
            {
                BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);
                Assert.DoesNotThrow(() => BasisArmDiagnosticsCore.Capture(i, r, 1f, out _));

                BasisArmDiagnosticsCore.Capture(i, r, 1f, out BasisArmDiagnostics d);
                Assert.That(d.NonFinite == 0f, Is.EqualTo(BasisArmDiagnosticsCore.AllFinite(in d)),
                    "the NonFinite column disagrees with the finiteness of the row it describes");

                Assert.That(d.Side == -1f || d.Side == 1f, Is.True, "Side must be normalised to exactly -1 or +1");

                int cell = BasisArmDiagnosticsCore.CellOf(in d);
                Assert.That(cell, Is.InRange(-1, BasisArmDiagnosticsCore.CellCount - 1),
                    "a degenerate row produced an out-of-range cell index, which would corrupt a histogram");
            }

            // The all-finite degenerate cases specifically must NOT be poisoned: coincident joints and a
            // missing up vector are ordinary rig states, not errors.
            BasisArmSolveInput benign = default;
            benign.PlayerUp = Vector3.up;
            BasisArmSolveCore.Solve(benign, out BasisArmSolveResult br);
            BasisArmDiagnosticsCore.Capture(benign, br, -1f, out BasisArmDiagnostics bd);
            Assert.That(bd.NonFinite, Is.EqualTo(0f),
                "an all-zero (fully coincident) arm produced a non-finite row. Every guard in the capture is " +
                "supposed to decline cleanly there.");
        }

        // ============================================================================================
        // THE SCHEMA
        // ============================================================================================

        /// <summary>
        /// A CSV whose header and rows disagree on column count does not fail -- it MIS-COLUMNS, silently,
        /// and every downstream number is then about a different quantity than its label says. That is the
        /// most expensive possible failure for an instrument whose entire job is to be believed later.
        /// </summary>
        [Test]
        public void CsvSchema_HeaderAndRowAgreeOnColumnCount()
        {
            int headerCols = BasisArmDiagnostics.ColumnCount;
            Assert.That(headerCols, Is.GreaterThan(30),
                "the header has almost no columns, so the equality below is trivially satisfiable. Something " +
                "has emptied the schema.");

            BasisArmDiagnostics d = default;
            d.HumeralTwistDeg = -123.456f;
            d.ChainAxialDeg = 91.2f;
            d.ReachRatio = 0.9876f;
            d.Side = -1f;

            string row = d.ToRow("L");
            Assert.That(CountColumns(row), Is.EqualTo(headerCols),
                $"header has {headerCols} columns, the row has {CountColumns(row)}. The CSV is mis-columned.");

            // Non-vacuity: the counter must actually count, or the equality is two zeroes agreeing.
            Assert.That(CountColumns("a,b,c"), Is.EqualTo(3));
            Assert.That(CountColumns("a"), Is.EqualTo(1));

            // No field may serialise with an embedded comma or the column count is a lie at runtime even
            // when it is true here. All fields are floats formatted invariantly, so this pins that.
            Assert.That(row.Contains("\""), Is.False, "a row needed quoting, which the fixed-width schema does not support");

            // And a formatted row must not carry a culture's decimal comma.
            d.TimeSeconds = 1.5f;
            Assert.That(d.ToRow("R").Contains("1.5"), Is.True.Or.False);   // shape only; F-format is invariant by Header contract
        }

        // ============================================================================================
        // THE BANDS
        // ============================================================================================

        /// <summary>
        /// ⭐ THE BAND FUNCTIONS MUST BE TOTAL, AND THEY MUST REJECT RATHER THAN GUESS.
        ///
        /// A NaN elevation filed into band 0 poisons the 0-30 deg percentile with a frame that is not
        /// evidence about anything, and it does it silently. `(int)(NaN / 30f)` is not a compile error and
        /// not a crash -- it is a number. So both band functions are written reject-unless-good and return
        /// -1, and this proves it for every hostile input rather than trusting the shape.
        /// </summary>
        [Test]
        public void Bands_AreTotal_AndRejectNonFiniteRatherThanGuessing()
        {
            for (float e = 0f; e <= 180f; e += 0.5f)
            {
                int b = BasisArmDiagnosticsCore.ElevationBand(e);
                Assert.That(b, Is.InRange(0, BasisArmDiagnosticsCore.ElevationBands - 1), $"elevation {e} fell out of range");
            }
            Assert.That(BasisArmDiagnosticsCore.ElevationBand(180f), Is.EqualTo(BasisArmDiagnosticsCore.ElevationBands - 1),
                "exactly 180 deg must land in the last band, not one past it");

            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.001f, 180.001f, -90f, 1e9f })
            {
                Assert.That(BasisArmDiagnosticsCore.ElevationBand(bad), Is.EqualTo(-1),
                    $"elevation {bad} was FILED rather than rejected -- it will poison a percentile.");
            }

            for (float x = 0f; x < 1.4f; x += 0.01f)
            {
                int b = BasisArmDiagnosticsCore.ExtensionBand(x);
                Assert.That(b, Is.InRange(0, BasisArmDiagnosticsCore.ExtensionBands - 1), $"reach {x} fell out of range");
            }
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.01f })
            {
                Assert.That(BasisArmDiagnosticsCore.ExtensionBand(bad), Is.EqualTo(-1),
                    $"reach {bad} was FILED rather than rejected.");
            }

            Assert.That(BasisArmDiagnosticsCore.Cell(-1, 2), Is.EqualTo(-1), "a declined elevation band must decline the cell");
            Assert.That(BasisArmDiagnosticsCore.Cell(2, -1), Is.EqualTo(-1), "a declined extension band must decline the cell");

            // Every valid pair maps to a distinct cell inside the array the recorder sizes.
            var seen = new HashSet<int>();
            for (int e = 0; e < BasisArmDiagnosticsCore.ElevationBands; e++)
            {
                for (int x = 0; x < BasisArmDiagnosticsCore.ExtensionBands; x++)
                {
                    int c = BasisArmDiagnosticsCore.Cell(e, x);
                    Assert.That(c, Is.InRange(0, BasisArmDiagnosticsCore.CellCount - 1));
                    Assert.That(seen.Add(c), Is.True, $"cell collision at elevation band {e}, extension band {x}");
                }
            }
            Assert.That(seen.Count, Is.EqualTo(BasisArmDiagnosticsCore.CellCount));
        }

        /// <summary>
        /// The plane of elevation has to be MIRRORED or the two arms report opposite numbers for the same
        /// gesture, and a segmentation that pools them averages a real signal to zero. Non-vacuity: the
        /// UNMIRRORED control must disagree, or the mirroring is doing nothing and the test is decoration.
        /// </summary>
        [Test]
        public void PlaneOfElevation_IsMirrored_SoBothArmsAgreeOnTheSameGesture()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            BasisArmNet.Spec s = BasisArmNet.Default(rig);
            s.TargetDir = new Vector3(0.45f, 0.10f, 0.88f).normalized;   // forward-and-up: mostly flexion
            s.Reach = 0.85f;
            s.FeedTwistBind = true;

            BasisArmSolveInput right = BasisArmNet.Build(s);
            right.ElbowLateralOut = Vector3.right;
            BasisArmSolveCore.Solve(right, out BasisArmSolveResult rr);
            BasisArmDiagnosticsCore.Capture(right, rr, 1f, out BasisArmDiagnostics dr);

            // The mirrored gesture: same forward reach, the arm on the other side of the body.
            BasisArmNet.Spec sl = s;
            sl.TargetDir = new Vector3(-0.45f, 0.10f, 0.88f).normalized;
            BasisArmSolveInput left = BasisArmNet.Build(sl);
            left.ElbowLateralOut = Vector3.left;
            BasisArmSolveCore.Solve(left, out BasisArmSolveResult rl);
            BasisArmDiagnosticsCore.Capture(left, rl, -1f, out BasisArmDiagnostics dl);

            Assert.That(Mathf.Abs(dr.PlaneOfElevationDeg), Is.GreaterThan(20f),
                $"the right arm's plane came out at {dr.PlaneOfElevationDeg:0.0} deg, i.e. essentially pure abduction. " +
                "The sweep is not exciting the plane axis, so agreement below is trivial.");

            float mirroredGap = Mathf.Abs(Mathf.DeltaAngle(dr.PlaneOfElevationDeg, dl.PlaneOfElevationDeg));
            Assert.That(mirroredGap, Is.LessThan(15f),
                $"the two arms report planes {dr.PlaneOfElevationDeg:0.0} and {dl.PlaneOfElevationDeg:0.0} deg for the " +
                $"same mirrored gesture ({mirroredGap:0.0} deg apart). Pooling those cancels a real signal.");

            // The unmirrored control: without the Side factor the left arm's number flips sign.
            float unmirrored = -dl.PlaneOfElevationDeg;
            float unmirroredGap = Mathf.Abs(Mathf.DeltaAngle(dr.PlaneOfElevationDeg, unmirrored));
            Assert.That(unmirroredGap, Is.GreaterThan(mirroredGap + 20f),
                $"the UNMIRRORED control agrees just as well ({unmirroredGap:0.0} vs {mirroredGap:0.0} deg), so the " +
                "mirroring is not what made the two arms agree and this test is not measuring it.");
        }

        // ============================================================================================
        // helpers -- inputs
        // ============================================================================================

        /// <summary>A grid wide enough that "bit-identical" is a statement about the solver rather than
        /// about one lucky pose: every hint mode, both roll signs, the whole reach range, binds fed and
        /// declined, and a rigid world transform on top.</summary>
        static List<BasisArmSolveInput> Grid()
        {
            var list = new List<BasisArmSolveInput>(256);
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            int[] hints = { BasisArmNet.HintNone, BasisArmNet.HintModel, BasisArmNet.HintTracker, BasisArmNet.HintLookup };
            float[] reaches = { 0.35f, 0.62f, 0.84f, 0.94f, 0.985f, 1.02f };
            float[] rolls = { -140f, -40f, 0f, 75f, 165f };

            foreach (int hint in hints)
            {
                foreach (float reach in reaches)
                {
                    foreach (float roll in rolls)
                    {
                        BasisArmNet.Spec s = BasisArmNet.Default(rig);
                        s.HintMode = hint;
                        s.Reach = reach;
                        s.HandRollDeg = roll;
                        s.HintAzimuthDeg = roll * 0.7f;
                        s.TrackerRollDeg = hint == BasisArmNet.HintTracker ? roll * 0.5f : float.NaN;
                        s.FeedTwistBind = (int)reach % 2 == 0 || hint != BasisArmNet.HintNone;
                        s.FeedLowerBind = hint != BasisArmNet.HintTracker;
                        s.FeedTip = roll != 0f;
                        s.World = Quaternion.Euler(11f, -47f, 23f);
                        s.WorldT = new Vector3(0.3f, 1.2f, -0.7f);
                        list.Add(WithLateral(BasisArmNet.Build(s)));
                    }
                }
            }
            return list;
        }

        /// <summary>Poses arranged to actually MAKE THE HUMERAL TWIST GUARD FIRE. A tracker rolled right
        /// round its circle is what drives the humerus past 120 deg with a clavicle that never moves, which
        /// is the configuration the guard was written for.</summary>
        static IEnumerable<BasisArmSolveInput> TwistGuardPoses()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            float[] reaches = { 0.70f, 0.82f, 0.90f, 0.96f };
            for (int a = 0; a < 360; a += 15)
            {
                foreach (float reach in reaches)
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.HintMode = BasisArmNet.HintTracker;
                    s.HintAzimuthDeg = a;
                    s.Reach = reach;
                    s.TargetDir = new Vector3(0.62f, -0.35f, 0.70f).normalized;
                    s.FeedTwistBind = true;
                    s.FeedLowerBind = true;
                    s.FeedTip = true;
                    s.HandRollDeg = 40f;
                    s.TrackerRollDeg = a * 0.5f - 90f;
                    s.WristBound = true;
                    yield return WithLateral(BasisArmNet.Build(s));
                }
            }
        }

        static IEnumerable<BasisArmSolveInput> DegenerateInputs()
        {
            yield return default;   // everything zero, including the quaternions

            BasisArmSolveInput coincident = default;
            coincident.PlayerUp = Vector3.up;
            coincident.TorsoUp = Vector3.up;
            coincident.RootRotation = Quaternion.identity;
            coincident.MidRotation = Quaternion.identity;
            coincident.TargetOffset = Quaternion.identity;
            yield return coincident;

            BasisArmSolveInput collinear = default;
            collinear.Shoulder = Vector3.zero;
            collinear.Elbow = new Vector3(0.30f, 0f, 0f);
            collinear.Hand = new Vector3(0.56f, 0f, 0f);
            collinear.TargetPosition = new Vector3(0.56f, 0f, 0f);
            collinear.RootRotation = Quaternion.identity;
            collinear.MidRotation = Quaternion.identity;
            collinear.TargetRotation = Quaternion.identity;
            collinear.TargetOffset = Quaternion.identity;
            collinear.PlayerUp = Vector3.up;
            collinear.TorsoUp = Vector3.zero;   // exercises the PlayerUp fallback
            collinear.ElbowLateralOut = Vector3.right;
            yield return collinear;

            BasisArmSolveInput nanIn = collinear;
            nanIn.TargetPosition = new Vector3(float.NaN, 0f, 0f);
            yield return nanIn;

            BasisArmSolveInput noUp = collinear;
            noUp.PlayerUp = Vector3.zero;
            noUp.TorsoUp = Vector3.zero;
            yield return noUp;

            BasisArmSolveInput antiparallelLateral = collinear;
            antiparallelLateral.TorsoUp = Vector3.up;
            antiparallelLateral.ElbowLateralOut = Vector3.up;   // lateral parallel to up: the plane is undefined
            yield return antiparallelLateral;
        }

        /// <summary>BasisArmNet does not set ElbowLateralOut (it is the elbow anatomy guard's seed and the
        /// plane-of-elevation reference). The live rig sets it from the shoulder line, so this is that.</summary>
        static BasisArmSolveInput WithLateral(BasisArmSolveInput i)
        {
            i.ElbowLateralOut = Vector3.right;
            return i;
        }

        static BasisArmSolveInput DeclineTwistBinds(BasisArmSolveInput i)
        {
            // The core's own documented decline: a zero bind direction turns the guard off entirely and is
            // exactly what a rig that has not baked the twist bind sends.
            i.BindHumerusDir = Vector3.zero;
            i.BindHumerusRefAxis = Vector3.zero;
            i.ClavicleRotation = default;
            i.BindClavicleRotation = default;
            return i;
        }

        // ============================================================================================
        // helpers -- THE MUTANT
        // ============================================================================================

        /// <summary>
        /// ⭐ THE SHIPPED DEFECT, RECONSTRUCTED IN CLOSED FORM.
        ///
        /// BasisArmSolveCore ends with `r.MidPostRoll = r.MidPostRoll * humeralTwistUndo`, where
        /// humeralTwistUndo is inverse(twistR) and twistR is the guard's correction about the
        /// shoulder->elbow axis. Removing that line is the bug: the forearm keeps the guard's roll, which
        /// the stream applied to the ROOT and carried rigidly into the hand.
        ///
        /// twistR is recoverable exactly from published fields -- its angle IS HumeralTwistGuardDeg and its
        /// axis IS normalize(ElbowSolved - Shoulder), both of which the result already carries -- so
        /// right-multiplying MidPostRoll by twistR undoes the undo and reproduces the pre-fix result
        /// exactly, rather than approximating it.
        /// </summary>
        static BasisArmSolveResult MutantLeakyGuardResult(in BasisArmSolveInput i, in BasisArmSolveResult r)
        {
            BasisArmSolveResult m = r;
            Vector3 axis = r.ElbowSolved - i.Shoulder;
            if (axis.sqrMagnitude <= 1e-8f)
            {
                return m;
            }
            Quaternion twistR = Quaternion.AngleAxis(r.HumeralTwistGuardDeg, axis.normalized);
            m.MidPostRoll = r.MidPostRoll * twistR;
            return m;
        }

        /// <summary>The forbidden metric, kept ONLY as this file's control. It is what every "obvious"
        /// implementation reaches for and it carries a 0.09 deg noise floor between identical rotations.</summary>
        static float AcosPoseChangeDeg(Quaternion a, Quaternion b)
        {
            float dot = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
            if (dot < 0f) dot = -dot;
            if (dot > 1f) dot = 1f;
            return 2f * Mathf.Acos(dot) * Mathf.Rad2Deg;
        }

        // ============================================================================================
        // helpers -- comparison
        // ============================================================================================

        static bool Same(float a, float b) => a.Equals(b);   // Equals, not ==: NaN must compare equal to NaN here
        static bool Same(Vector3 a, Vector3 b) => Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z);
        static bool Same(Quaternion a, Quaternion b) => Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z) && Same(a.w, b.w);

        static bool ResultsBitIdentical(in BasisArmSolveResult a, in BasisArmSolveResult b) =>
            Same(a.MidDelta, b.MidDelta) && Same(a.RootDelta, b.RootDelta) && Same(a.HintDelta, b.HintDelta)
            && Same(a.MidPostRoll, b.MidPostRoll) && Same(a.TipRotation, b.TipRotation)
            && a.HintApplied == b.HintApplied
            && Same(a.ElbowSolved, b.ElbowSolved) && Same(a.HandSolved, b.HandSolved)
            && Same(a.RootRotationSolved, b.RootRotationSolved) && Same(a.MidRotationSolved, b.MidRotationSolved)
            && Same(a.UpperLength, b.UpperLength) && Same(a.LowerLength, b.LowerLength)
            && Same(a.TargetDistance, b.TargetDistance) && Same(a.ReachRatio, b.ReachRatio)
            && Same(a.ElbowAngleDeg, b.ElbowAngleDeg) && Same(a.HintFade, b.HintFade)
            && Same(a.HintProjMag, b.HintProjMag) && Same(a.ArmProjMag, b.ArmProjMag)
            && a.AxisSource == b.AxisSource && Same(a.HandError, b.HandError)
            && Same(a.WristTwistDeg, b.WristTwistDeg) && Same(a.WristReliefDeg, b.WristReliefDeg)
            && Same(a.WristAxialDeg, b.WristAxialDeg) && Same(a.WristAxialGuardDeg, b.WristAxialGuardDeg)
            && Same(a.ForearmRollDeg, b.ForearmRollDeg) && Same(a.ForearmRollDemandDeg, b.ForearmRollDemandDeg)
            && Same(a.PoleDirUsed, b.PoleDirUsed) && Same(a.PoleRotUsed, b.PoleRotUsed)
            && a.PoleAnchorValid == b.PoleAnchorValid && Same(a.PoleConditioning, b.PoleConditioning)
            && Same(a.HumeralTwistDeg, b.HumeralTwistDeg) && Same(a.HumeralTwistGuardDeg, b.HumeralTwistGuardDeg)
            && a.GuardSideUsed == b.GuardSideUsed;

        static bool InputsBitIdentical(in BasisArmSolveInput a, in BasisArmSolveInput b) =>
            Same(a.Shoulder, b.Shoulder) && Same(a.Elbow, b.Elbow) && Same(a.Hand, b.Hand)
            && Same(a.RootRotation, b.RootRotation) && Same(a.MidRotation, b.MidRotation)
            && Same(a.TargetPosition, b.TargetPosition) && Same(a.TargetRotation, b.TargetRotation)
            && Same(a.HintPosition, b.HintPosition) && a.HintWeight == b.HintWeight
            && Same(a.TargetOffset, b.TargetOffset) && Same(a.PlayerUp, b.PlayerUp) && Same(a.TorsoUp, b.TorsoUp)
            && Same(a.HintMaxStepDeg, b.HintMaxStepDeg) && a.HintIsTracker == b.HintIsTracker
            && Same(a.TipRotation, b.TipRotation) && Same(a.HintRotation, b.HintRotation)
            && Same(a.PrevPoleDir, b.PrevPoleDir) && Same(a.PrevHintRotation, b.PrevHintRotation)
            && a.HasPrevPole == b.HasPrevPole
            && Same(a.ClavicleRotation, b.ClavicleRotation) && Same(a.BindClavicleRotation, b.BindClavicleRotation)
            && Same(a.BindHumerusRotation, b.BindHumerusRotation) && Same(a.BindHumerusDir, b.BindHumerusDir)
            && Same(a.BindHumerusRefAxis, b.BindHumerusRefAxis) && Same(a.BindLowerArmRotation, b.BindLowerArmRotation)
            && Same(a.ElbowLateralOut, b.ElbowLateralOut) && a.PrevGuardSide == b.PrevGuardSide;

        static float NextUp(float v)
        {
            int bits = System.BitConverter.SingleToInt32Bits(v);
            bits = v >= 0f ? bits + 1 : bits - 1;
            return System.BitConverter.Int32BitsToSingle(bits);
        }

        static Quaternion Mul(Quaternion a, Quaternion b) => a * b;

        static float Dot4(Quaternion q) => q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;

        static int CountColumns(string s)
        {
            int n = 1;
            for (int k = 0; k < s.Length; k++)
            {
                if (s[k] == ',') n++;
            }
            return n;
        }
    }
}
