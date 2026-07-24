using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// DISPLACEMENT IS MEASURED THROUGH THE STREAM, OR IT IS NOT MEASURED.
    ///
    /// ================================================================================================
    /// THE CLASS THIS CATCHES: a VACUOUS ASSERTION. The instance that shipped:
    /// HumeralTwistGuard_MovesNoJoint_AndActsAboutShoulderToElbow "proved" the guard moved the hand
    /// 0.000 mm by comparing BasisArmSolveResult.HandSolved before and after -- a field THE GUARD NEVER
    /// WRITES. The two numbers were equal because both were untouched. A hand displacement of 205 mm
    /// shipped behind that gate and reported perfect safety the whole time.
    ///
    /// The runtime does not read HandSolved. It applies
    ///     mid.SetRotation(MidDelta*mid); root.SetRotation(RootDelta*root); root.SetRotation(HintDelta*root);
    ///     mid.SetRotation(MidPostRoll*mid); tip.SetRotation(TipRotation)
    /// to an AnimationStream, where setting a PARENT's rotation carries its children RIGIDLY -- and
    /// HintDelta goes on the ROOT. Every swivel folded into HintDelta is about shoulder->HAND, where
    /// carrying the children is free because the hand lies on the axis. The humeral twist guard's is about
    /// shoulder->ELBOW, and the hand does not lie on that one.
    ///
    /// ⭐ THE HEADLINE TEST HERE IS NOT PER-GUARD. It is the RECONCILIATION: over a broad sweep, the
    /// solver's four bookkeeping outputs must equal what the stream composition produces, EXACTLY. That one
    /// property makes the whole class unshippable -- any future stage that rotates a bone without updating
    /// its own bookkeeping, or picks an axis a joint does not lie on, breaks it, whether or not anybody
    /// remembers to write a per-guard gate. The per-guard tests below are then honest, because they are
    /// measured with a replay that has been proven faithful FIRST.
    ///
    /// ⚠️ AND THE REPLAY IS PROVEN SENSITIVE, NOT JUST FAITHFUL. A replay that agreed with the solver
    /// because every delta happened to be identity would be the same vacuous shape one level up. So the
    /// same sweep is re-composed with HintDelta omitted and with MidPostRoll omitted, and those MUST
    /// disagree -- which proves the sweep actually drives both stages.
    /// ================================================================================================
    /// </summary>
    public class BasisArmInvariantNetStreamTests
    {
        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        /// <summary>A broad pose matrix: every hint mode, both roll paths, both bind-fed states, across the
        /// sphere and through full extension. Everything in this file runs over it, so a defect anywhere in
        /// the pose space is caught by every property here rather than by whichever one happened to sample it.</summary>
        static IEnumerable<BasisArmNet.Spec> Matrix(BasisArmNet.Rig rig, bool includeBeyondReach)
        {
            float[] azs = { 0f, 65f, 140f, 215f, 300f };
            float[] els = { -75f, -35f, 0f, 45f };
            float[] reaches = includeBeyondReach
                ? new[] { 0.30f, 0.62f, 0.85f, 0.95f, 0.995f, 1.08f }
                : new[] { 0.30f, 0.62f, 0.85f, 0.95f, 0.99f };
            int[] hints = { BasisArmNet.HintNone, BasisArmNet.HintModel, BasisArmNet.HintTracker, BasisArmNet.HintLookup };
            float[] handRolls = { 0f, 110f, 200f };

            foreach (float az in azs)
            foreach (float el in els)
            foreach (float reach in reaches)
            foreach (int hint in hints)
            foreach (float hr in handRolls)
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(az, el);
                s.Reach = reach;
                s.HandRollDeg = hr;
                s.HintMode = hint;
                s.HintAzimuthDeg = 37f + az * 0.7f;
                s.HintRhoMin = 0.015f;
                s.TrackerRollDeg = hint == BasisArmNet.HintTracker ? 25f + hr : float.NaN;
                s.RefPerp = Mathf.Abs(el) > 60f ? Dir(az + 90f, 0f) : Vector3.up;
                s.FeedTwistBind = true;
                s.FeedLowerBind = true;
                s.FeedTip = true;
                s.AnimHumRollDeg = 20f;
                s.AnimForeRollDeg = -35f;
                s.AnimHandRollDeg = 15f;
                yield return s;
            }
        }

        // ============================================================================================
        // 1. ⭐ THE RECONCILIATION -- THE PROPERTY THAT MAKES THE WHOLE CLASS UNSHIPPABLE
        // ============================================================================================

        /// <summary>
        /// ⭐ WHAT THE SOLVER SAYS IT DID MUST BE WHAT THE STREAM ACTUALLY DOES.
        ///
        /// Four equalities, each exact by construction rather than by tolerance:
        ///
        ///   HintDelta * RootDelta * RootRotation                        == RootRotationSolved
        ///   MidPostRoll * HintDelta * RootDelta * MidDelta * MidRotation == MidRotationSolved
        ///   stream elbow                                                 == ElbowSolved
        ///   stream hand                                                  == HandSolved
        ///
        /// The second one is subtle and is the one that carries the fix: the humeral twist guard rotates the
        /// root by twistR and folds it into HintDelta WITHOUT touching midRot, so the stream's forearm
        /// would run ahead of the solver's -- except that MidPostRoll is right-multiplied by inverse(twistR)
        /// at the very end, which cancels it exactly. Remove that undo and this equality breaks, and so does
        /// the hand, by up to 205 mm. The third and fourth then hold only because twistR is about
        /// shoulder->elbow (the elbow is ON it) and MidPostRoll is about elbow->hand (the hand is ON it).
        ///
        /// So this single test pins the composition order, the choice of axis for every stage, and every
        /// piece of bookkeeping -- for stages that do not exist yet as much as for the ones that do.
        /// </summary>
        [Test]
        public void SolverBookkeeping_ReconcilesExactlyWithTheAnimationStream()
        {
            var findings = new List<string>();
            float worstElbow = 0f, worstHand = 0f, worstRoot = 0f, worstMid = 0f, worstErr = 0f;
            float sensHintPos = 0f, sensRollRot = 0f;
            int poses = 0;

            foreach (BasisArmNet.Rig rig in BasisArmNet.RigConventions())
            foreach (BasisArmNet.Spec s in Matrix(rig, true))
            {
                BasisArmSolveInput i = BasisArmNet.Build(s);
                BasisArmNet.Solve(i, out BasisArmSolveResult r);
                poses++;

                BasisArmNet.StreamCompose(i, r, out Vector3 e, out Vector3 h, out Quaternion root, out Quaternion mid);

                worstElbow = Mathf.Max(worstElbow, Vector3.Distance(e, r.ElbowSolved));
                worstHand = Mathf.Max(worstHand, Vector3.Distance(h, r.HandSolved));
                worstRoot = Mathf.Max(worstRoot, BasisArmNet.PoseChangeDeg(root, r.RootRotationSolved));
                worstMid = Mathf.Max(worstMid, BasisArmNet.PoseChangeDeg(mid, r.MidRotationSolved));
                worstErr = Mathf.Max(worstErr, Mathf.Abs(Vector3.Distance(h, i.TargetPosition) - r.HandError));

                // ── the replay is SENSITIVE: drop a stage and it must visibly disagree.
                BasisArmNet.StreamCompose(i, r, out _, out Vector3 hNoHint, out _, out _, skipHint: true);
                BasisArmNet.StreamCompose(i, r, out _, out _, out _, out Quaternion midNoRoll, skipPostRoll: true);
                sensHintPos = Mathf.Max(sensHintPos, Vector3.Distance(hNoHint, h));
                sensRollRot = Mathf.Max(sensRollRot, BasisArmNet.PoseChangeDeg(midNoRoll, mid));
            }

            // ANTI-TAUTOLOGY FIRST, ALWAYS. If omitting HintDelta or MidPostRoll changes nothing, the sweep
            // never drove those stages and the equalities above are being satisfied by identities.
            BasisArmNet.Gate("the replay's sensitivity to HintDelta (a replay that ignores a stage and still " +
                             "agrees is measuring nothing)", 0f, 1f, sensHintPos, 0.02f);
            BasisArmNet.Gate("the replay's sensitivity to MidPostRoll", 0f, 1f, sensRollRot, 5f);

            if (!(worstElbow < 1e-5f)) findings.Add($"ELBOW: stream {worstElbow * 1000f:0.000000} mm from ElbowSolved.");
            if (!(worstHand < 1e-5f))
            {
                findings.Add($"HAND: the stream puts the hand {worstHand * 1000f:0.000} mm from where the solver " +
                             "says it is. The solver's bookkeeping is FICTION at these poses, and every test that " +
                             "reads HandSolved to prove a stage moved nothing is vacuous. A stage rotated a bone " +
                             "about an axis the hand is not on, or folded a correction into HintDelta (which the " +
                             "runtime applies to the ROOT) without taking it back off the forearm.");
            }
            if (!(worstRoot < 0.01f)) findings.Add($"UPPER ARM: stream rotation {worstRoot:0.0000} deg from RootRotationSolved.");
            if (!(worstMid < 0.01f))
            {
                findings.Add($"FOREARM: stream rotation {worstMid:0.0000} deg from MidRotationSolved. The likely " +
                             "cause is the humeral twist guard's counter-rotation on MidPostRoll " +
                             "(r.MidPostRoll * inverse(twistR)) having been dropped or re-sided: it is what makes " +
                             "the guard a PURE HUMERAL ROLL instead of a 205 mm hand displacement.");
            }
            if (!(worstErr < 1e-4f)) findings.Add($"HandError disagrees with the stream by {worstErr * 1000f:0.000} mm.");

            BasisArmNet.Report(findings, null, $"stream reconciliation over {poses} poses");

            TestContext.WriteLine(
                $"  {poses} poses x 7 rig conventions reconcile: elbow {worstElbow * 1000f:0.000000} mm, " +
                $"hand {worstHand * 1000f:0.000000} mm, upper arm {worstRoot:0.00000} deg, forearm {worstMid:0.00000} deg, " +
                $"HandError {worstErr * 1000f:0.000000} mm.\n" +
                $"  replay sensitivity: dropping HintDelta moves the hand {sensHintPos * 1000f:0.0} mm, dropping " +
                $"MidPostRoll turns the forearm {sensRollRot:0.0} deg -- so the replay is measuring both stages.");
        }

        // ============================================================================================
        // 2. THE REACH-PRESERVING STAGES, THROUGH THE STREAM
        // ============================================================================================

        /// <summary>
        /// EVERY SWIVEL IN THIS SOLVER CLAIMS TO PRESERVE REACH BY CONSTRUCTION -- the hint swivel, the
        /// pole-collapse stabiliser, the wrist-roll relief and the elbow anatomy guard are all rotations
        /// about shoulder->hand, and the hand LIES on that axis. Structural, not a tolerance. So the hand
        /// must land on the controller through the STREAM, at every pose the elbow clamps do not
        /// deliberately shorten.
        ///
        /// The non-vacuity control is the geometry itself: past full extension the arm CANNOT reach, and the
        /// error must be large there. If it were not, this test would be measuring a hand that goes nowhere.
        /// </summary>
        [Test]
        public void EveryReachPreservingStage_LandsTheHandOnTarget_ThroughTheStream()
        {
            var findings = new List<string>();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            float worstInRange = 0f, atAz = 0f, atEl = 0f, atReach = 0f;
            float beyondReach = 0f;
            int inRange = 0;

            foreach (BasisArmNet.Spec s in Matrix(rig, true))
            {
                BasisArmSolveInput i = BasisArmNet.Build(s);
                BasisArmNet.Solve(i, out BasisArmSolveResult r);
                BasisArmNet.StreamCompose(i, r, out _, out Vector3 h, out _, out _);
                float err = Vector3.Distance(h, i.TargetPosition);

                // The elbow clamps deliberately shorten the arm outside [0.25, 0.9962] of reach: min flexion
                // (23 deg) cannot fold closer than 0.211, max flexion (170 deg) cannot stretch past 0.9962.
                if (s.Reach >= 0.25f && s.Reach <= 0.99f)
                {
                    inRange++;
                    if (err > worstInRange)
                    {
                        worstInRange = err;
                        atReach = s.Reach;
                        atAz = Mathf.Atan2(s.TargetDir.z, s.TargetDir.x) * Mathf.Rad2Deg;
                        atEl = Mathf.Asin(Mathf.Clamp(s.TargetDir.y, -1f, 1f)) * Mathf.Rad2Deg;
                    }
                }
                else if (s.Reach > 1.0f)
                {
                    beyondReach = Mathf.Max(beyondReach, err);
                }
            }

            Assert.That(inRange, Is.GreaterThan(100), "the matrix barely sampled the in-range band.");
            BasisArmNet.Gate(
                $"hand-on-target error through the STREAM over {inRange} in-range poses (worst at reach " +
                $"{atReach:0.000}, az {atAz:0}, el {atEl:0})",
                worstInRange, 0.001f, beyondReach, 0.01f);

            TestContext.WriteLine($"  in-range hand error {worstInRange * 1000f:0.000} mm; the beyond-reach control " +
                                  $"misses by {beyondReach * 1000f:0.0} mm, so the measurement is live.");
        }

        // ============================================================================================
        // 3. THE PURE-ROLL STAGES MOVE NO JOINT -- MEASURED WHERE IT COUNTS
        // ============================================================================================

        /// <summary>
        /// MidPostRoll advertises itself as "a pure roll about its own long axis, so it can move no joint",
        /// and the humeral twist guard as a correction "about shoulder->ELBOW, so the elbow lies ON it".
        /// Both claims are about POSITIONS AFTER COMPOSITION, and both were previously asserted against
        /// fields the stages do not write.
        ///
        /// Here each stage is toggled on and off and BOTH joints are compared through the stream, with the
        /// stage's own reported magnitude used as the non-vacuity control: a stage that never engaged
        /// cannot have moved anything, and a test that does not check for that is the 205 mm again.
        /// </summary>
        [Test]
        public void PureRollStages_MoveNeitherJoint_ThroughTheStream()
        {
            var findings = new List<string>();
            float worstTwistHand = 0f, worstTwistElbow = 0f, mostTwistGuard = 0f;
            float worstRollHand = 0f, worstRollElbow = 0f, mostRoll = 0f;
            int poses = 0;

            foreach (BasisArmNet.Rig rig in BasisArmNet.RigConventions())
            foreach (BasisArmNet.Spec s in Matrix(rig, false))
            {
                poses++;

                // ── the humeral twist guard: bind fed vs declined.
                BasisArmNet.Spec on = s; on.FeedTwistBind = true;
                BasisArmNet.Spec off = s; off.FeedTwistBind = false;
                BasisArmSolveInput iOn = BasisArmNet.Build(on), iOff = BasisArmNet.Build(off);
                BasisArmNet.Solve(iOn, out BasisArmSolveResult rOn);
                BasisArmNet.Solve(iOff, out BasisArmSolveResult rOff);
                BasisArmNet.StreamCompose(iOn, rOn, out Vector3 eOn, out Vector3 hOn, out _, out _);
                BasisArmNet.StreamCompose(iOff, rOff, out Vector3 eOff, out Vector3 hOff, out _, out _);
                mostTwistGuard = Mathf.Max(mostTwistGuard, Mathf.Abs(rOn.HumeralTwistGuardDeg));
                worstTwistElbow = Mathf.Max(worstTwistElbow, Vector3.Distance(eOn, eOff));
                worstTwistHand = Mathf.Max(worstTwistHand, Vector3.Distance(hOn, hOff));

                // ── the forearm roll: the bind fed vs declined (no-tracker path), or the tracker's own
                //    HintRotation fed vs declined (tracker path). Either way a PURE roll about elbow->hand.
                BasisArmNet.Spec rollOn = s, rollOff = s;
                if (s.HintMode == BasisArmNet.HintTracker)
                {
                    rollOn.TrackerRollDeg = s.TrackerRollDeg;
                    rollOff.TrackerRollDeg = float.NaN;
                }
                else
                {
                    rollOn.FeedLowerBind = true;
                    rollOff.FeedLowerBind = false;
                }
                BasisArmSolveInput iR1 = BasisArmNet.Build(rollOn), iR0 = BasisArmNet.Build(rollOff);
                BasisArmNet.Solve(iR1, out BasisArmSolveResult r1);
                BasisArmNet.Solve(iR0, out BasisArmSolveResult r0);
                BasisArmNet.StreamCompose(iR1, r1, out Vector3 e1, out Vector3 h1, out _, out _);
                BasisArmNet.StreamCompose(iR0, r0, out Vector3 e0, out Vector3 h0, out _, out _);
                mostRoll = Mathf.Max(mostRoll, Mathf.Abs(r1.ForearmRollDeg));
                worstRollElbow = Mathf.Max(worstRollElbow, Vector3.Distance(e1, e0));
                worstRollHand = Mathf.Max(worstRollHand, Vector3.Distance(h1, h0));
            }

            Assert.That(mostTwistGuard, Is.GreaterThan(5f),
                $"the humeral twist guard never applied more than {mostTwistGuard:0.00} deg over {poses} poses; " +
                "the displacement assertions below would be comparing the same arm with itself.");
            Assert.That(mostRoll, Is.GreaterThan(20f),
                $"the forearm roll never exceeded {mostRoll:0.00} deg; same problem.");

            if (!(worstTwistElbow < 1e-5f))
                findings.Add($"the HUMERAL TWIST GUARD moved the ELBOW {worstTwistElbow * 1000f:0.000} mm through " +
                             "the stream. It is applied about shoulder->elbow and the elbow lies ON that axis, so " +
                             "this must be zero however the correction is composed.");
            if (!(worstTwistHand < 1e-4f))
                findings.Add($"the HUMERAL TWIST GUARD moved the HAND {worstTwistHand * 1000f:0.000} mm through the " +
                             "stream (up to 205 mm was the shipped defect). HintDelta goes on the ROOT and carries " +
                             "its children rigidly; the correction is about shoulder->ELBOW, which the hand is not on.");
            if (!(worstRollElbow < 1e-5f))
                findings.Add($"the FOREARM ROLL moved the ELBOW {worstRollElbow * 1000f:0.000} mm; it pivots ON the elbow.");
            if (!(worstRollHand < 1e-4f))
                findings.Add($"the FOREARM ROLL moved the HAND {worstRollHand * 1000f:0.000} mm; the hand LIES on the " +
                             "forearm's long axis, so a roll about it cannot move the hand.");

            BasisArmNet.Report(findings, null, "pure-roll displacement through the stream");

            TestContext.WriteLine(
                $"  {poses} poses: twist guard up to {mostTwistGuard:0.0} deg moved elbow " +
                $"{worstTwistElbow * 1000f:0.000000} mm / hand {worstTwistHand * 1000f:0.000000} mm; forearm roll up " +
                $"to {mostRoll:0.0} deg moved elbow {worstRollElbow * 1000f:0.000000} mm / hand {worstRollHand * 1000f:0.000000} mm.");
        }

        // ============================================================================================
        // 4. THE WRIST-ROLL RELIEF SPENDS THE SWIVEL DOF AND NOTHING ELSE
        // ============================================================================================

        /// <summary>
        /// The relief answers a controller roll the wrist cannot by swivelling the whole arm about
        /// shoulder->hand -- so it MUST move the elbow (that is the entire point: the humerus answers for
        /// what the forearm cannot) and MUST NOT move the hand. Both halves are asserted, because a relief
        /// that moved nothing would satisfy the second half perfectly.
        /// </summary>
        [Test]
        public void WristRollRelief_MovesTheElbow_AndNotTheHand()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            float worstHand = 0f, mostElbow = 0f, mostRelief = 0f;
            int engaged = 0, total = 0;

            foreach (float el in new[] { -60f, -20f, 30f })
            foreach (float reach in new[] { 0.70f, 0.88f, 0.97f })
            for (int k = 0; k <= 72; k++)
            {
                float roll = k * 5f;
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(40f, el);
                s.Reach = reach;
                s.HandRollDeg = roll;
                s.HintMode = BasisArmNet.HintModel;   // the relief stands down for a tracker, by design
                s.HintAzimuthDeg = 15f;
                s.RefPerp = Vector3.up;
                s.FeedTwistBind = true;
                s.FeedLowerBind = true;

                BasisArmNet.Spec on = s; on.FeedTip = true;
                BasisArmNet.Spec off = s; off.FeedTip = false;

                BasisArmSolveInput iOn = BasisArmNet.Build(on), iOff = BasisArmNet.Build(off);
                BasisArmNet.Solve(iOn, out BasisArmSolveResult rOn);
                BasisArmNet.Solve(iOff, out BasisArmSolveResult rOff);
                BasisArmNet.StreamCompose(iOn, rOn, out Vector3 eOn, out Vector3 hOn, out _, out _);
                BasisArmNet.StreamCompose(iOff, rOff, out Vector3 eOff, out Vector3 hOff, out _, out _);

                total++;
                if (Mathf.Abs(rOn.WristReliefDeg) > 0.5f) engaged++;
                mostRelief = Mathf.Max(mostRelief, Mathf.Abs(rOn.WristReliefDeg));
                mostElbow = Mathf.Max(mostElbow, Vector3.Distance(eOn, eOff));
                worstHand = Mathf.Max(worstHand, Vector3.Distance(hOn, hOff));
            }

            Assert.That(engaged, Is.GreaterThan(20),
                $"the wrist-roll relief engaged on only {engaged} of {total} poses; a sweep of the controller " +
                "through a full turn at three elevations must cross the comfort band, or the relief has been " +
                "switched off and every assertion here is vacuous.");
            Assert.That(mostElbow, Is.GreaterThan(0.02f),
                $"the relief moved the elbow at most {mostElbow * 1000f:0.0} mm. It exists to recruit the humerus " +
                "for roll the forearm cannot supply -- if the elbow does not move, it is not doing that.");
            Assert.That(worstHand, Is.LessThan(1e-4f),
                $"the relief moved the HAND {worstHand * 1000f:0.000} mm through the stream. It is a swivel about " +
                "shoulder->hand and the hand lies ON that axis: reach preservation here is geometry, not tolerance.");

            TestContext.WriteLine($"  relief engaged on {engaged}/{total} poses, up to {mostRelief:0.0} deg; it moved " +
                                  $"the elbow up to {mostElbow * 1000f:0.0} mm and the hand {worstHand * 1000f:0.000000} mm.");
        }
    }
}
