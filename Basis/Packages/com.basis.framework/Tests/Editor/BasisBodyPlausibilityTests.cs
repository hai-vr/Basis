using System.Collections.Generic;
using System.IO;
using System.Text;
using Basis.IK;
using Basis.IK.Mocap;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    // ⚠ The alias must live INSIDE the namespace. com.basis.sdk declares a completely unrelated
    // `BasisMotionClip : ScriptableObject` in the GLOBAL namespace; at file scope this alias would collide
    // with it (CS0576) instead of disambiguating. Same defence as BasisSpineAnatomyCorpusTests.
    using BasisMotionClip = Basis.IK.Mocap.BasisMotionClip;

    /// <summary>
    /// IS THE WHOLE BODY A BODY? -- the two whole-skeleton checks, and, first, the proof that they can
    /// measure anything at all.
    ///
    /// ================================================================================================
    /// GUARD THE RULER BEFORE YOU USE IT. This project has shipped metrics that lied:
    ///
    ///   * a pop detector whose count came out IDENTICAL whether its cause was present or absent;
    ///   * a smoothness metric that scored over-smoothed mush ABOVE a real human;
    ///   * an elbow-guard test that reported safety while the bug it was written for was still live,
    ///     because it asserted on a pose the geometry could not produce (BasisElbowAnatomyTests now
    ///     carries RequireInReach for exactly that reason).
    ///
    /// So nothing here reports a number until the number has been checked against something already known.
    /// The order of this file is deliberate and it is the order the tests must be read in:
    ///
    ///   1. THE ANSWER KEYS. Two channels have published values measured by somebody else:
    ///      BasisElbowAnatomyCore's +0.015 arm lengths over 55,140 frames, and BasisSpineAnatomy's p99
    ///      table over 35,081. If this file cannot reproduce those, it is wrong and nothing below counts.
    ///
    ///      ⚠ AND ONE OF THEM DID NOT REPRODUCE -- WHICH IS THE POINT, AND IT HAS SINCE BEEN ACTED ON. Run
    ///      2026-07-22: the spine table reproduced (and its 35,081 is exactly this corpus's root+posture
    ///      frame count, which is its own corroboration). The ELBOW law reproduced ONLY when measured in a
    ///      torso-relative frame -- in the frame the shipped guard enforced AT THE TIME (PlayerUp, world up)
    ///      real humans exceeded its soft margin, on the very population the published number was taken
    ///      over. That was a defect in shipped code, not in the ruler, and this file said so rather than
    ///      widening a margin to make the disagreement go away. BasisArmSolveCore now passes a torso up, and
    ///      the evidence trail is in TheRuler_ReproducesTheElbowCeilingLaw's header.
    ///   2. THE DETECTOR CAN FIRE. A self-intersection check that never fires is indistinguishable from a
    ///      clean body. So drive a synthetic arm through a synthetic chest and demand it be caught.
    ///   3. REAL HUMANS PASS. Only now is it meaningful that the corpus comes back clean.
    ///      ⚠ ON 13 OF THE 25 CAPSULE PAIRS FOR DEPTH. The other 12 (arm x same-side leg) are rate-checked
    ///      only: their depth number saturates on real motion, measured, and the exclusion is itself
    ///      asserted so it cannot outlive its evidence. See RealHumans_DoNotPassThroughThemselves.
    ///   4. THE REPORT. The measured envelope, per channel, per corpus tier. No gate -- the numbers do not
    ///      exist yet, and there is no honest threshold before that. THE RATCHET SITES ARE MARKED.
    ///
    /// TIERS ARE LOADED NON-RECURSIVELY, ONE AT A TIME, and reported as separate rows. The corpus is split
    /// deliberately (root 20 / posture 44 / dynamic 29 / slow 16) so one tier cannot re-base another's
    /// numbers -- the accuracy harness's headline figures are quoted all over this project and they stay
    /// byte-identical only because nobody folds the tiers together. For ROM the tier that matters is
    /// dynamic/: arms overhead, full extension, fast swings, martial arts. That is where the extremes are.
    /// ================================================================================================
    /// </summary>
    public class BasisBodyPlausibilityTests
    {
        static string CorpusRoot => Path.GetFullPath("Packages/com.basis.framework/Tests/MocapCorpus~");

        // (subfolder, label). "" is the root tier. NON-RECURSIVE per tier -- see the class comment.
        static readonly (string sub, string label)[] k_Tiers =
        {
            ("", "root"),
            ("posture", "posture"),
            ("dynamic", "dynamic"),
            ("slow", "slow"),
        };

        static List<BasisMotionClip> LoadTier(string sub)
        {
            var clips = new List<BasisMotionClip>();
            string dir = sub.Length == 0 ? CorpusRoot : Path.Combine(CorpusRoot, sub);
            if (!Directory.Exists(dir)) return clips;

            // Directory.GetFiles is TopDirectoryOnly by default. That is load-bearing, not incidental.
            string[] files = Directory.GetFiles(dir, "*.bvh");
            System.Array.Sort(files);
            foreach (string f in files)
            {
                if (BasisBvhLoader.TryLoad(f, out BasisMotionClip c, out _)) clips.Add(c);
            }
            return clips;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════════
        // 1. THE ANSWER KEYS -- is the ruler right?
        // ════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ THE STRONGEST CHECK IN THIS FILE, because somebody else already computed the answer -- and it
        /// is the check that found a defect in shipped code rather than in itself.
        ///
        /// BasisElbowAnatomyCore's header states the law and its evidence: across 55,140 frames of real
        /// human arm motion the worst violation of "the elbow never rises above the higher of shoulder and
        /// hand" is +0.015 of an arm length, violated on 0.0000% of frames at a margin of 0.05, and a margin
        /// of 0.10 would clamp ZERO poses a human actually made.
        ///
        /// ================================================================================================
        /// THE ANSWER KEY DID NOT REPRODUCE, AND THE RULER TURNED OUT TO BE RIGHT. Measured 2026-07-22 over
        /// the whole corpus, both arms, every frame (133,078 frames -> 266,156 arm-samples):
        ///
        ///   tier      samples   max under WORLD up   max under TORSO up   %>0.05 world   %>0.05 torso
        ///   root       27,570         0.0525               0.0150            0.0036%        0.0000%
        ///   posture    42,592         0.3723               0.0242            0.3475%        0.0000%
        ///   dynamic   129,366         0.2555               0.2155            0.7707%        0.2574%
        ///   slow       66,628        -0.0785              -0.0637            0.0000%        0.0000%
        ///
        /// TWO INDEPENDENT DEFECTS FELL OUT OF THAT TABLE, and they are not the same defect:
        ///
        /// (1) ⭐ THE FRAME WAS WRONG IN THE SHIPPED GUARD. ✅ FIXED -- this is the finding this test was
        ///     written to force, and it has been acted on. BasisArmSolveCore USED TO hand
        ///     BasisElbowAnatomyCore `i.PlayerUp`, and PlayerUp is the PLAYER ROOT's up (BasisLocalRigDriver:
        ///     "Identical to Vector3.up for an upright root"). But the anatomical law is about the TORSO:
        ///     bend at the waist and your elbow rises above your shoulder in world space while sitting
        ///     perfectly normally against your chest.
        ///     The corpus priced that exactly. Every one of posture/'s 148 violating samples has a trunk
        ///     tilted 73.7-119.7 deg off vertical (median 100.5 -- i.e. bent double), and on those SAME
        ///     frames the measurement reads +0.3723 in world space and -0.3713 in the torso frame. THE SIGN
        ///     FLIPS. +0.3723 is 7.4x the soft margin and 2.5x the HARD asymptote (0.15), so the guard was
        ///     not merely nudging those poses, it was hauling them back hard -- worst correction 66.15 deg,
        ///     mean 31.72, displacing the solved elbow by up to 21.28 cm.
        ///     Only the torso frame reproduces the published claims: root max 0.0150 == the published
        ///     +0.015, 0.0000% past 0.05, and 0.10 clamps zero. World up cannot produce +0.015 by ANY
        ///     exclusion -- root minus its single worst clip gives 0.0027, not 0.015. So the published
        ///     evidence described a torso-frame law while the shipped guard enforced a world-frame one.
        ///     BasisArmSolveCore now builds `TorsoUp` from the chest->neck frame and passes THAT, falling
        ///     back to PlayerUp only for a rig with no usable body frame. On posture/ the guard now fires
        ///     zero times. BasisElbowAnatomyTests's section 5 carries the regression tests.
        ///
        ///     ⚠ AND THE NUMBERS IN THE TABLE ABOVE DID NOT MOVE, WHICH IS CORRECT, NOT A REGRESSION.
        ///     `worstWorld` below comes from BasisRomChannel.LeftElbowCeilingFracArm, which
        ///     BasisBodyPlausibility computes from Vector3.up against THE CORPUS -- it never consults
        ///     BasisElbowAnatomyCore at all. Fixing the frame the SOLVER guards in cannot change what the
        ///     humans did in world space, so the root tier still reads 0.0525 and always will. Anyone
        ///     expecting this test's world column to fall after the fix has confused a measurement of the
        ///     corpus with a measurement of the solver.
        ///
        /// (2) THE PUBLISHED POPULATION IS ALSO STALE, but that is the smaller half. `dynamic/` and `slow/`
        ///     were added 2026-07-17 and 2026-07-18; BasisElbowAnatomyCore was written 2026-07-15, and the
        ///     corpus is byte-identical since. So the 55,140-frame measurement provably never saw them.
        ///     It matters less than (1) because dynamic/ violates in BOTH frames -- 0.2574% of samples past
        ///     0.05 even in the torso frame, max 0.2155 -- so a frame fix alone would not make the law
        ///     universal. Arms-overhead and full-extension material genuinely exceeds it.
        ///     (The 55,140 count matches no population: the corpus at publication was root+posture = 35,081
        ///     frames = 70,162 arm-samples, and root alone is 13,785 frames = 27,570. 55,140 is exactly
        ///     twice the root tier's arm-sample count.)
        ///
        /// NEITHER WAS A REASON TO WIDEN THE MARGIN, and this test never did. What it asserts now is that
        /// the ruler is sound -- the TORSO frame reproduces the published number, and it is the frame the
        /// shipped guard enforces, so those two assertions have become the same assertion. The world column
        /// is still measured and still printed, as the HISTORICAL COMPARISON BASELINE that priced the frame
        /// bug; it is no longer a defect being held at arm's length, and it is no longer asserted on beyond
        /// a drift canary.
        /// ================================================================================================
        /// </summary>
        [Test]
        public void TheRuler_ReproducesTheElbowCeilingLaw_AlreadyMeasuredOn55140Frames()
        {
            // ⚠ THE ASSERTIONS ARE ON THE ROOT TIER, AND THAT IS NOT LAZINESS. Reproducing somebody else's
            // number means reproducing it on their population, and root is the tier that existed when the
            // guard was written (posture/ did too; dynamic/ and slow/ did not -- see the header). Every tier
            // is MEASURED and PRINTED below, in both frames, because that table is the finding.
            var byTier = new Dictionary<string, BasisRomAccumulator>();
            var frameCount = new Dictionary<string, long>();
            foreach ((string sub, string label) in k_Tiers)
            {
                List<BasisMotionClip> clips = LoadTier(sub);
                if (clips.Count == 0) continue;
                var a = new BasisRomAccumulator();
                long frames = 0;
                foreach (BasisMotionClip c in clips)
                {
                    BasisBodyPlausibilitySummary s = BasisBodyPlausibility.Run(c, a);
                    Assert.That(s.Ok, Is.True, $"{label}/{c.Name}: {s.Error}");
                    frames += s.Frames;
                }
                byTier[label] = a;
                frameCount[label] = frames;
            }
            if (byTier.Count == 0) Assert.Ignore($"no mocap corpus at {CorpusRoot}");

            var log = new StringBuilder(
                "\nELBOW CEILING -- elbow height above max(shoulder, hand), as a fraction of arm length\n" +
                "  THE LAW (BasisElbowAnatomyCore): the elbow never rises above the higher of shoulder and hand.\n" +
                "  Published: worst real violation +0.015 over 55,140 frames; 0.0000% of frames past 0.05.\n" +
                $"  Guard soft margin: {BasisElbowAnatomyCore.SoftMarginFracLimb}   hard asymptote: {BasisElbowAnatomyCore.HardMarginFracLimb}\n" +
                "  WORLD = what the guard used to be handed (PlayerUp); kept as the historical comparison baseline.\n" +
                "  TORSO = the frame the guard enforces AND the frame the published number reproduces in (they agree now).\n" +
                "  max and outside% are pooled over both arms exactly; the percentiles are the worse arm's, not a\n" +
                "  pooled percentile -- they are here for shape, and every number asserted on below is an exact one.\n\n" +
                "  tier       frames  frame   arm    p50       p95       p99       max     outside%\n");

            foreach (KeyValuePair<string, BasisRomAccumulator> kv in byTier)
            {
                Row(log, kv.Key, frameCount[kv.Key], "WORLD", kv.Value,
                    BasisRomChannel.LeftElbowCeilingFracArm, BasisRomChannel.RightElbowCeilingFracArm);
                Row(log, "", frameCount[kv.Key], "TORSO", kv.Value,
                    BasisRomChannel.LeftElbowCeilingTorsoFracArm, BasisRomChannel.RightElbowCeilingTorsoFracArm);
            }
            TestContext.WriteLine(log.ToString());

            Assert.That(byTier.ContainsKey("root"), Is.True, "the root tier is the published population and is missing");
            BasisRomAccumulator root = byTier["root"];
            BasisRomStat rl = root.Summarise(BasisRomChannel.LeftElbowCeilingFracArm);
            BasisRomStat rr = root.Summarise(BasisRomChannel.RightElbowCeilingFracArm);
            BasisRomStat tl = root.Summarise(BasisRomChannel.LeftElbowCeilingTorsoFracArm);
            BasisRomStat tr = root.Summarise(BasisRomChannel.RightElbowCeilingTorsoFracArm);
            float worstWorld = Mathf.Max(rl.Max, rr.Max);
            float worstTorso = Mathf.Max(tl.Max, tr.Max);

            Assert.That(rl.Count + rr.Count, Is.GreaterThan(10000),
                $"only {rl.Count + rr.Count} elbow samples in the root tier -- too few to reproduce a 55,140-frame result");

            // It has to actually be MEASURING an elbow. Real humans get the elbow close to the ceiling
            // (hands at the face, hands overhead), so a max pinned far below zero means the channel is dead
            // and its clean verdict means nothing -- the same trap RequireInReach exists to close.
            Assert.That(worstWorld, Is.GreaterThan(-0.05f),
                $"the elbow never once came near its ceiling (max {worstWorld:F4}) -- this channel is not measuring an elbow");
            Assert.That(tl.Count + tr.Count, Is.GreaterThan(10000),
                $"only {tl.Count + tr.Count} torso-frame samples -- the chest frame is not being built");

            // ── 1. THE RULER IS SOUND: the torso frame reproduces the published number ────────────────────
            // THIS is the answer-key check, and it is what licenses every other number in this file. Measured
            // 0.0150 against a published +0.015 -- so the ceiling arithmetic, the arm-length denominator and
            // the corpus loading all agree with whoever wrote the guard. The window is generous on purpose:
            // it is asking "same quantity?", not "same decimal". A frame or convention error lands nowhere
            // near it (world up gives 0.0525 on this very tier, and a mirrored axis would give ~-0.5).
            Assert.That(worstTorso, Is.InRange(0.005f, 0.030f),
                $"measured in the TORSO frame the root tier's worst elbow rise is {worstTorso:F4}, but the published " +
                "law says +0.015 and this is the frame that reproduced it on 2026-07-22. If this moved, either the " +
                "corpus changed or the ceiling arithmetic did -- and if the ruler is wrong then the finding about " +
                "BasisElbowAnatomyCore that this test carries is worthless too. Fix the ruler before reading on.");
            Assert.That(RootTorsoViolationFrac(root), Is.EqualTo(0f),
                "the published law claims 0.0000% of frames past 0.05, and in the torso frame that reproduced " +
                "exactly. A non-zero rate here breaks the reproduction the rest of this test rests on.");

            // ── 2. THE WORLD COLUMN, AS A CORPUS-DRIFT CANARY ────────────────────────────────────────────
            // ⚠ THIS IS NOT A GATE ON THE SOLVER, AND IT CANNOT BE ONE. `worstWorld` is measured from
            // Vector3.up against the CORPUS (BasisBodyPlausibility.MeasureArm); it never consults
            // BasisElbowAnatomyCore, so no change to the shipped guard can move it. What it still catches is
            // the CORPUS or the CHANNEL moving underneath this file -- a reloaded clip set, a changed
            // arm-length denominator, a mirrored axis -- any of which would invalidate the torso-frame
            // reproduction asserted just above. 0.0525 was the value on 2026-07-22; this allows drift up to
            // 0.10 before complaining.
            //
            // (A previous revision asserted `worstWorld > SoftMarginFracLimb` here, deliberately backwards,
            // to pin the frame defect at its measured size while the decision about shipped code was
            // pending. That decision has been made -- BasisArmSolveCore passes a torso up -- so the pin has
            // been deleted rather than inverted. Inverting it would NOT have worked: worstWorld stays 0.0525
            // regardless, because the humans did not change frame. See finding (1) in the header.)
            Assert.That(worstWorld, Is.LessThan(0.10f),
                $"the root tier's world-frame elbow rise has moved to {worstWorld:F4} (it was 0.0525 on " +
                "2026-07-22, and it is a pure function of the corpus and Vector3.up, so it should not move at " +
                "all). The corpus or the channel changed underneath this file -- which puts the torso-frame " +
                "reproduction asserted above in doubt, because that is measured off the same clips by the same " +
                "code. Find out what moved before trusting any number in this file.");

            TestContext.WriteLine(
                $"\n  FRAME COMPARISON: root tier worst elbow rise is {worstTorso:F4} in the TORSO frame -- the frame\n" +
                $"    the shipped guard now enforces and the one the published +0.015 was measured in -- against\n" +
                $"    {worstWorld:F4} in the world/PlayerUp frame the guard used to be handed. The guard's soft margin\n" +
                $"    is {BasisElbowAnatomyCore.SoftMarginFracLimb}. The world column is a HISTORICAL BASELINE, not a defect: it measures what the\n" +
                "    corpus did, so it did not move when the guard was fixed, and it never will. See this test's header.");
        }

        /// <summary>
        /// One tier row of the elbow table. `max` and `outside%` are pooled over both arms exactly; the
        /// percentiles are the WORSE ARM's, not a pooled percentile -- two histograms cannot be merged by
        /// taking a max, and the row is for shape only. Every number this test asserts on is an exact one.
        /// </summary>
        static void Row(StringBuilder log, string tier, long frames, string frame, BasisRomAccumulator acc,
            BasisRomChannel left, BasisRomChannel right)
        {
            BasisRomStat l = acc.Summarise(left);
            BasisRomStat r = acc.Summarise(right);
            int n = l.Count + r.Count;
            float outside = n > 0 ? 100f * (l.ViolationCount + r.ViolationCount) / n : 0f;
            log.AppendLine($"  {tier,-9} {(tier.Length > 0 ? frames.ToString() : ""),7}  {frame,-5} both  " +
                           $"{Mathf.Max(l.P50, r.P50),8:F4}  {Mathf.Max(l.P95, r.P95),8:F4}  " +
                           $"{Mathf.Max(l.P99, r.P99),8:F4}  {Mathf.Max(l.Max, r.Max),8:F4}  {outside,8:F4}");
        }

        /// <summary>
        /// Fraction of root-tier torso-frame samples outside the envelope -- the published "0.0000% of frames
        /// past 0.05". The channel's Lo is -2 arm lengths, which the geometry cannot reach (the quantity is
        /// bounded below by roughly -1.6: the elbow is at most ~0.58 arm below the shoulder and the ceiling at
        /// most ~1 arm above it), so every violation counted here is a violation ABOVE the soft margin, which
        /// is the direction the published claim is about.
        /// </summary>
        static float RootTorsoViolationFrac(BasisRomAccumulator root)
        {
            BasisRomStat l = root.Summarise(BasisRomChannel.LeftElbowCeilingTorsoFracArm);
            BasisRomStat r = root.Summarise(BasisRomChannel.RightElbowCeilingTorsoFracArm);
            int n = l.Count + r.Count;
            return n > 0 ? (float)(l.ViolationCount + r.ViolationCount) / n : 0f;
        }

        /// <summary>
        /// ⭐ THE SECOND ANSWER KEY. The spine channels do not re-implement anything -- they call
        /// BasisSpineAnatomy.BuildRestFrame and BasisSpineAnatomyCore.Decompose, the shipping functions, and
        /// pick the neutral frame by the same rule BasisSpineAnatomyCorpusTests uses. So they must reproduce
        /// the p99 table published in BasisSpineAnatomy's header, measured over the same tiers (root+posture).
        ///
        /// The tolerance is deliberately loose. This is not asserting that two implementations agree to the
        /// decimal -- the percentiles here are histogram-resolved and the sign conventions are two-sided
        /// where the published table took absolute values. It is asserting that nothing is off by a FACTOR:
        /// a mirrored axis, a parent/child swap, or degrees-vs-radians all show up here immediately.
        /// </summary>
        [Test]
        public void TheRuler_ReproducesTheSpineP99Table_PublishedInBasisSpineAnatomy()
        {
            var clips = new List<BasisMotionClip>();
            clips.AddRange(LoadTier(""));
            clips.AddRange(LoadTier("posture"));
            if (clips.Count == 0) Assert.Ignore($"no root/posture corpus at {CorpusRoot}");

            var acc = new BasisRomAccumulator();
            foreach (BasisMotionClip c in clips)
            {
                BasisBodyPlausibilitySummary s = BasisBodyPlausibility.Run(c, acc);
                Assert.That(s.Ok, Is.True, $"{c.Name}: {s.Error}");
            }

            // Published p99, from BasisSpineAnatomy's header: flex / ext / lateral / axial.
            (BasisRomChannel flex, string label, float pFlex, float pExt, float pLat, float pAx)[] want =
            {
                (BasisRomChannel.LumbarFlexDeg, "lumbar    (Spine)", 49.2f, 10.7f, 12.5f, 10.2f),
                (BasisRomChannel.ChestFlexDeg,  "lo-thorac (Chest)", 18.2f, 11.1f, 10.4f, 14.2f),
                (BasisRomChannel.NeckFlexDeg,   "cervical  (Neck) ", 23.8f, 35.7f, 29.5f, 20.8f),
            };

            var log = new StringBuilder("\nSPINE p99 vs the published table (BasisSpineAnatomy header, 35,081 frames)\n");
            log.AppendLine("  segment              flex p99 / pub    ext p1 / pub     lat p99 / pub    axial p99 / pub");
            var failures = new List<string>();

            foreach (var w in want)
            {
                BasisRomStat fl = acc.Summarise(w.flex);
                BasisRomStat la = acc.Summarise(w.flex + 1);
                BasisRomStat ax = acc.Summarise(w.flex + 2);
                Assert.That(fl.Count, Is.GreaterThan(1000), $"{w.label}: only {fl.Count} samples");

                log.AppendLine($"  {w.label}  {fl.P99,8:F1} /{w.pFlex,6:F1}   {-fl.P01,8:F1} /{w.pExt,6:F1}   " +
                               $"{la.P99,8:F1} /{w.pLat,6:F1}   {ax.P99,8:F1} /{w.pAx,6:F1}");

                Compare(failures, $"{w.label} flex p99", fl.P99, w.pFlex);
                Compare(failures, $"{w.label} ext p1", -fl.P01, w.pExt);
                Compare(failures, $"{w.label} lat p99", la.P99, w.pLat);
                Compare(failures, $"{w.label} axial p99", ax.P99, w.pAx);
            }

            TestContext.WriteLine(log.ToString());
            Assert.That(failures, Is.Empty,
                "this file's spine measurements disagree with the ones BasisSpineAnatomy publishes, and it calls " +
                "the SAME shipping functions -- so either the reference frame differs or the published table is " +
                "stale. Do not adjust the tolerance to make this pass:\n" + string.Join("\n", failures));
        }

        /// <summary>Within 10 degrees or 60% of the published value, whichever is more generous. Catches a
        /// factor, not a decimal -- see the calling test's comment for why that is the right question.</summary>
        static void Compare(List<string> failures, string what, float got, float published)
        {
            float tol = Mathf.Max(10f, 0.60f * published);
            if (Mathf.Abs(got - published) > tol)
            {
                failures.Add($"  {what}: measured {got:F1}, published {published:F1} (tolerance {tol:F1})");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════════
        // 2. THE DETECTOR CAN FIRE -- the anti-tautology half.
        // ════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// A neutral standing skeleton, in metres, with an 0.85 m leg -- the same normalisation
        /// BasisBvhLoader applies to the corpus, so the numbers here and there are comparable.
        /// </summary>
        static BasisMocapPose[] NeutralPose()
        {
            var p = BasisBodyFrame.Allocate();
            void Set(BasisMocapJoint j, float x, float y, float z) =>
                p[(int)j] = new BasisMocapPose { Position = new Vector3(x, y, z), Rotation = Quaternion.identity, Valid = true };

            Set(BasisMocapJoint.Hips, 0f, 0.95f, 0f);
            Set(BasisMocapJoint.Spine, 0f, 1.05f, 0f);
            Set(BasisMocapJoint.Chest, 0f, 1.18f, 0f);
            Set(BasisMocapJoint.UpperChest, 0f, 1.32f, 0f);
            Set(BasisMocapJoint.Neck, 0f, 1.42f, 0f);
            Set(BasisMocapJoint.Head, 0f, 1.58f, 0f);

            Set(BasisMocapJoint.LeftShoulder, -0.06f, 1.38f, 0f);
            Set(BasisMocapJoint.LeftUpperArm, -0.17f, 1.36f, 0f);
            Set(BasisMocapJoint.LeftLowerArm, -0.17f, 1.08f, 0f);   // upper arm 0.28
            Set(BasisMocapJoint.LeftHand, -0.17f, 0.83f, 0f);       // forearm 0.25
            Set(BasisMocapJoint.RightShoulder, 0.06f, 1.38f, 0f);
            Set(BasisMocapJoint.RightUpperArm, 0.17f, 1.36f, 0f);
            Set(BasisMocapJoint.RightLowerArm, 0.17f, 1.08f, 0f);
            Set(BasisMocapJoint.RightHand, 0.17f, 0.83f, 0f);

            Set(BasisMocapJoint.LeftUpperLeg, -0.09f, 0.92f, 0f);
            Set(BasisMocapJoint.LeftLowerLeg, -0.09f, 0.485f, 0f);  // thigh 0.435
            Set(BasisMocapJoint.LeftFoot, -0.09f, 0.07f, 0f);       // shin 0.415
            Set(BasisMocapJoint.LeftToes, -0.09f, 0.03f, 0.15f);
            Set(BasisMocapJoint.RightUpperLeg, 0.09f, 0.92f, 0f);
            Set(BasisMocapJoint.RightLowerLeg, 0.09f, 0.485f, 0f);
            Set(BasisMocapJoint.RightFoot, 0.09f, 0.07f, 0f);
            Set(BasisMocapJoint.RightToes, 0.09f, 0.03f, 0.15f);
            return p;
        }

        static BasisMocapPose[] Clone(BasisMocapPose[] src) => (BasisMocapPose[])src.Clone();

        static BasisMotionClip ClipOf(params BasisMocapPose[][] frames)
        {
            int n = (int)BasisMocapJoint.Count;
            var clip = new BasisMotionClip
            {
                Name = "synthetic",
                FrameTime = 1f / 60f,
                FrameCount = frames.Length,
                Poses = new BasisMocapPose[frames.Length * n],
                SourceToMetres = 1f,
            };
            for (int f = 0; f < frames.Length; f++)
            {
                System.Array.Copy(frames[f], 0, clip.Poses, f * n, n);
            }
            return clip;
        }

        /// <summary>
        /// ⭐ THE ANTI-TAUTOLOGY TEST, and the reason the corpus test below is allowed to mean anything.
        ///
        /// A self-intersection detector that returns "clean" unconditionally passes every real human too.
        /// This project has shipped exactly that failure -- a pop detector whose count was identical whether
        /// its cause was present or absent. So: build a skeleton, prove it is clean, then drive its forearm
        /// through its own sternum and demand the check catch it, name the right pair, and fail the gate.
        /// </summary>
        [Test]
        public void AForearmDrivenThroughTheChest_IsCaught_AndTheNeutralPoseIsNot()
        {
            BasisMocapPose[] neutral = NeutralPose();
            var frame = new BasisBodyFrame { Joints = neutral };
            BasisBodyScale scale = BasisBodyPlausibility.BuildScale(frame);
            Assert.That(scale.Valid, Is.True, scale.Error);

            // A body standing normally must be completely clear, on EVERY pair. If this fires the radii are
            // too fat and the whole layer is a false-positive generator.
            int worstPair = BasisBodyPlausibility.WorstOverlap(frame, scale, out float pen, out _);
            Assert.That(worstPair, Is.EqualTo(-1),
                $"a neutral standing skeleton reports self-intersection on '{(worstPair >= 0 ? BasisBodyPlausibility.PairLabel(worstPair) : "?")}' " +
                $"({pen:F3} of a limb length). The capsule radii are too large -- every real human stands like this.");

            // Now the defect: the left hand inside the chest, the elbow tucked, forearm straight through it.
            BasisMocapPose[] through = Clone(neutral);
            through[(int)BasisMocapJoint.LeftLowerArm].Position = new Vector3(-0.10f, 1.25f, -0.05f);
            through[(int)BasisMocapJoint.LeftHand].Position = new Vector3(0.05f, 1.20f, 0f);

            var bad = new BasisBodyFrame { Joints = through };
            int hit = BasisBodyPlausibility.WorstOverlap(bad, scale, out float badPen, out float badPenR);
            Assert.That(hit, Is.GreaterThanOrEqualTo(0),
                "a forearm driven straight through the sternum was NOT detected -- the check cannot fire, so its " +
                "clean verdict on real humans proves nothing");
            Assert.That(BasisBodyPlausibility.PairLabel(hit), Does.Contain("torso"),
                $"the deepest overlap should be the torso pair, got '{BasisBodyPlausibility.PairLabel(hit)}'");
            Assert.That(badPen, Is.GreaterThan(BasisBodyPlausibility.MaxPenetrationFracLimb),
                $"the pass-through only registered {badPen:F3} of a limb length, under the gross gate " +
                $"({BasisBodyPlausibility.MaxPenetrationFracLimb}) -- it would slip past Gate()");

            TestContext.WriteLine($"\n  neutral: clear.  arm-through-chest: '{BasisBodyPlausibility.PairLabel(hit)}' " +
                                  $"penetrates {badPen:F3} limb lengths ({badPenR:P0} of the summed radii)");

            // And end to end, through the same entry point the corpus uses.
            BasisBodyPlausibilitySummary s = BasisBodyPlausibility.Run(ClipOf(neutral, through));
            Assert.That(s.Ok, Is.True, s.Error);
            Assert.That(s.IntersectFrames, Is.EqualTo(1), "exactly one of the two frames is a collision");
            (bool pass, string reason) = BasisBodyPlausibility.Gate(s);
            Assert.That(pass, Is.False, "Gate must fail a clip in which the arm goes through the chest");
            TestContext.WriteLine($"  Gate: {reason}");
        }

        /// <summary>
        /// ⭐ THE SAME PROOF FOR THE OTHER HALF. Bend a knee BACKWARDS -- the defect the shipped leg guard
        /// calls "not unnatural, anatomically unrepresentable" -- and demand the ROM layer flag it, in the
        /// right DIRECTION. Magnitude alone is not enough: a knee over-flexing and a knee inverting are
        /// different bugs with different causes and they must not share a number.
        /// </summary>
        [Test]
        public void AKneeBentBackwards_IsFlagged_AndInTheNegativeDirection()
        {
            BasisMocapPose[] neutral = NeutralPose();

            // A normally flexed knee: the knee bulges FORWARD of the hip->ankle line.
            BasisMocapPose[] good = Clone(neutral);
            good[(int)BasisMocapJoint.LeftLowerLeg].Position = new Vector3(-0.09f, 0.52f, 0.12f);
            good[(int)BasisMocapJoint.LeftFoot].Position = new Vector3(-0.09f, 0.10f, -0.02f);

            // The same knee driven BEHIND that line. Nothing else moves.
            BasisMocapPose[] inverted = Clone(good);
            inverted[(int)BasisMocapJoint.LeftLowerLeg].Position = new Vector3(-0.09f, 0.52f, -0.12f);

            var scaleFrame = new BasisBodyFrame { Joints = neutral };
            BasisBodyScale scale = BasisBodyPlausibility.BuildScale(scaleFrame);
            Assert.That(scale.Valid, Is.True, scale.Error);

            var spine = BasisBodyPlausibility.BuildSpineRest(ClipOf(neutral));
            var v = new float[(int)BasisRomChannel.Count];
            var ok = new bool[(int)BasisRomChannel.Count];

            BasisBodyPlausibility.MeasureRom(new BasisBodyFrame { Joints = good }, scale, spine, v, ok);
            Assert.That(ok[(int)BasisRomChannel.LeftKneeAnteriorDot], Is.True,
                "a bent knee has a lever arm, so the anterior channel must be measurable");
            float goodDot = v[(int)BasisRomChannel.LeftKneeAnteriorDot];
            Assert.That(goodDot, Is.GreaterThan(0f),
                $"a normally flexed knee must sit in the ANTERIOR half-space, got dot {goodDot:F3}");

            BasisBodyPlausibility.MeasureRom(new BasisBodyFrame { Joints = inverted }, scale, spine, v, ok);
            float badDot = v[(int)BasisRomChannel.LeftKneeAnteriorDot];
            Assert.That(badDot, Is.LessThan(0f),
                $"a knee driven behind the hip->ankle axis must report a NEGATIVE anterior dot, got {badDot:F3}");

            BasisRomSpec spec = BasisBodyPlausibility.Spec(BasisRomChannel.LeftKneeAnteriorDot);
            Assert.That(spec.Gated, Is.True, "the anterior half-space is a hard shipped law and must be gated");
            Assert.That(badDot, Is.LessThan(spec.Lo), "and it must be outside the envelope, not merely negative");

            BasisBodyPlausibilitySummary s = BasisBodyPlausibility.Run(ClipOf(good, inverted));
            Assert.That(s.Ok, Is.True, s.Error);
            Assert.That(s.WorstRomDirection, Is.EqualTo(-1),
                "an inverted knee is a violation BELOW the low limit; reporting it as 'above' would send the " +
                "next reader hunting an over-flexion bug that is not there");
            TestContext.WriteLine($"\n  knee anterior dot: normal {goodDot:F3}, inverted {badDot:F3} " +
                                  $"(worst channel '{s.WorstRomChannel}', direction {s.WorstRomDirection})");
        }

        /// <summary>
        /// SCALE-FREE, OR IT DOES NOT TRANSFER. Every radius is a fraction of a bone and every angle comes
        /// from directions, so a child avatar and a giant must get the SAME verdict -- not the same
        /// centimetres. Same reasoning, and the same test, as BasisElbowAnatomyTests.TheGuard_IsScaleFree.
        /// </summary>
        [Test]
        public void EverythingIsScaleFree_AGiantAndAChildGetTheSameVerdict()
        {
            BasisMocapPose[] small = NeutralPose();
            small[(int)BasisMocapJoint.LeftLowerArm].Position = new Vector3(-0.10f, 1.25f, -0.05f);
            small[(int)BasisMocapJoint.LeftHand].Position = new Vector3(0.05f, 1.20f, 0f);

            const float k = 2.5f;
            BasisMocapPose[] big = Clone(small);
            for (int j = 0; j < big.Length; j++) big[j].Position *= k;

            BasisMocapPose[] smallRest = NeutralPose();
            BasisMocapPose[] bigRest = Clone(smallRest);
            for (int j = 0; j < bigRest.Length; j++) bigRest[j].Position *= k;

            BasisBodyScale sSmall = BasisBodyPlausibility.BuildScale(new BasisBodyFrame { Joints = smallRest });
            BasisBodyScale sBig = BasisBodyPlausibility.BuildScale(new BasisBodyFrame { Joints = bigRest });

            int pSmall = BasisBodyPlausibility.WorstOverlap(new BasisBodyFrame { Joints = small }, sSmall, out float fSmall, out _);
            int pBig = BasisBodyPlausibility.WorstOverlap(new BasisBodyFrame { Joints = big }, sBig, out float fBig, out _);

            Assert.That(pBig, Is.EqualTo(pSmall), "the same pose on a bigger avatar must implicate the same pair");
            Assert.That(fBig, Is.EqualTo(fSmall).Within(1e-4f),
                $"penetration is reported as a fraction of a limb, so it must be identical at any scale " +
                $"({fSmall:F4} vs {fBig:F4})");

            var spine = BasisBodyPlausibility.BuildSpineRest(ClipOf(smallRest));
            var vS = new float[(int)BasisRomChannel.Count];
            var vB = new float[(int)BasisRomChannel.Count];
            var okS = new bool[(int)BasisRomChannel.Count];
            var okB = new bool[(int)BasisRomChannel.Count];
            BasisBodyPlausibility.MeasureRom(new BasisBodyFrame { Joints = small }, sSmall, spine, vS, okS);
            BasisBodyPlausibility.MeasureRom(new BasisBodyFrame { Joints = big }, sBig, spine, vB, okB);

            for (int c = 0; c < (int)BasisRomChannel.Count; c++)
            {
                if (!okS[c] || !okB[c]) continue;
                Assert.That(vB[c], Is.EqualTo(vS[c]).Within(1e-3f),
                    $"channel '{BasisBodyPlausibility.Spec((BasisRomChannel)c).Name}' changed with avatar size: " +
                    $"{vS[c]:F4} -> {vB[c]:F4}");
            }
        }

        /// <summary>
        /// IT MUST NOT BLOW UP. A NaN transform PERSISTS in Unity -- the limb would never recover -- so every
        /// entry point declines on bad input rather than propagating it. Same doctrine as
        /// BasisElbowAnatomyTests.DegenerateAndNaNInputs_DeclineRatherThanCorrupt.
        /// </summary>
        [Test]
        public void DegenerateAndNaNInputs_DeclineRatherThanCorrupt()
        {
            // Two coincident points: a zero-length segment is a point, and the distance is still defined.
            Assert.That(BasisBodyPlausibility.SegmentDistance(Vector3.zero, Vector3.zero, Vector3.one, Vector3.one),
                Is.EqualTo(Vector3.one.magnitude).Within(1e-4f), "point-to-point must still work");

            // Parallel segments: the classic degenerate case for the closest-point solve (denominator 0).
            Assert.That(BasisBodyPlausibility.SegmentDistance(
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                    new Vector3(0f, 0.5f, 0f), new Vector3(1f, 0.5f, 0f)),
                Is.EqualTo(0.5f).Within(1e-4f), "parallel segments must report their true separation");

            // Skew segments that do NOT overlap in their parameter range: the clamp is what makes this right.
            // Unclamped, the solve would report a distance between points that are not on either segment.
            Assert.That(BasisBodyPlausibility.SegmentDistance(
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                    new Vector3(5f, 1f, 0f), new Vector3(6f, 1f, 0f)),
                Is.EqualTo(Mathf.Sqrt(16f + 1f)).Within(1e-4f), "the closest points must be clamped onto the segments");

            // A skeleton with a NaN joint must fail scale construction, not produce NaN radii.
            BasisMocapPose[] nan = NeutralPose();
            nan[(int)BasisMocapJoint.LeftLowerLeg].Position = new Vector3(float.NaN, 0f, 0f);
            BasisBodyScale s = BasisBodyPlausibility.BuildScale(new BasisBodyFrame { Joints = nan });
            if (s.Valid)
            {
                // Lengths are still finite for the other segments; what must NOT happen is a NaN overlap
                // being reported as a collision. `!(pen > 0)` rejects NaN, so the pair reads clear.
                BasisBodyPlausibility.WorstOverlap(new BasisBodyFrame { Joints = nan }, s, out float nanPen, out _);
                Assert.That(float.IsNaN(nanPen), Is.False, "a NaN joint must not surface as a NaN penetration");
            }

            // A missing joint must be declined, not defaulted.
            BasisMocapPose[] missing = NeutralPose();
            missing[(int)BasisMocapJoint.LeftFoot].Valid = false;
            BasisBodyScale sm = BasisBodyPlausibility.BuildScale(new BasisBodyFrame { Joints = missing });
            Assert.That(sm.Valid, Is.False, "an incomplete skeleton must be refused");
            Assert.That(sm.Error, Is.Not.Null.And.Not.Empty, "and it must say which joint");

            // An empty clip is an error, not an exception.
            BasisBodyPlausibilitySummary empty = BasisBodyPlausibility.Run(null);
            Assert.That(empty.Ok, Is.False);
            (bool pass, string reason) = BasisBodyPlausibility.Gate(empty);
            Assert.That(pass, Is.False);
            Assert.That(reason, Is.Not.Null.And.Not.Empty);
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════════
        // 3. REAL HUMANS PASS -- only meaningful now that the detector is known to fire.
        // ════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ THE RULER-GUARD FOR (A). Real humans do not pass through themselves. If this fires, THE
        /// DETECTOR IS WRONG -- the radii are too fat, or a pair that should have been excluded as adjacent
        /// was not. Do not "fix" it by relaxing the threshold: the whole value of this layer is that a hit
        /// means something.
        ///
        /// It is a RATE, not a zero, and that follows the house precedent
        /// (BasisSpineAnatomyCorpusTests.k_MaxFireFraction = 3%): a corpus of real motion contains retarget
        /// slop and genuine limb-on-limb contact, and demanding literal zero would make the test a
        /// tuning-fork for the corpus rather than a check on the code. The per-pair breakdown is printed so
        /// a failure names the pair immediately instead of sending someone hunting.
        ///
        /// ================================================================================================
        /// ⚠ DEPTH IS ASSERTED ON 13 OF THE 25 PAIRS, NOT ALL 25, AND THE 12 EXCLUSIONS WERE MEASURED, NOT
        /// ASSUMED. The arm x same-side-leg family saturates its depth number on real human motion: measured
        /// 2026-07-22 over 109 clips / 133,078 frames, those pairs reach 0.946-0.999 of their SUMMED CORE
        /// RADII (bone axes effectively coincident -- dynamic/135_01 f2377 has them 0.0005 m apart), while
        /// every other pair in the corpus stays at or below 0.448. The split is bimodal with nothing in
        /// between, and the deep hits are interior to both segments in smooth multi-frame arcs, so they are
        /// real sustained motion rather than glitch frames -- CMU is solved without collision constraints.
        /// `fracLimb` also has a hard per-pair ceiling of (rA+rB)/min(lenA,lenB), which for forearm x thigh
        /// is only ~0.25, so the 0.261 that failed this test on 2026-07-22 WAS that pair's ceiling: the
        /// threshold and the saturation point were the same number, and no value could have separated
        /// contact from pass-through there. The full reasoning lives next to `Pair` in BasisBodyPlausibility.
        ///
        /// Those 12 pairs keep their RATE assertion (a solver putting an arm through a leg on 5% of frames
        /// is still caught) and the exclusion itself is CHECKED below, so it cannot outlive its evidence.
        /// ================================================================================================
        /// </summary>
        [Test]
        public void RealHumans_DoNotPassThroughThemselves()
        {
            const float k_MaxPooledFrac = 0.03f;
            const float k_MaxPerPairFrac = 0.05f;

            // ⭐ THE DEPTH ENVELOPE, DERIVED FROM THE MEASUREMENT RATHER THAN NUDGED TO FIT.
            // On the 13 depth-discriminating pairs the worst real human motion produces is 0.125
            // (torso x R-upperarm, root/141_17 f5). The synthetic arm-through-sternum fixture in this file
            // produces 0.3192. BasisBodyPlausibility.MaxPenetrationFracLimb is 0.20, which sits 1.60x above
            // the former and 0.63x below the latter -- headroom on both sides. It is REFERENCED, not
            // restated, so this test and Gate() can never drift apart about what "too deep" means.
            const float k_MeasuredWorstDiscriminating = 0.125f;   // 2026-07-22, whole corpus
            const float k_SyntheticPassThrough = 0.3192f;         // AForearmDrivenThroughTheChest fixture
            // The excluded family saturates; assert that it still does, so the exclusion stays earned.
            const float k_SaturationFracRadii = 0.90f;

            var log = new StringBuilder("\nSELF-INTERSECTION on REAL human motion (capsule cores; the ruler-guard)\n");
            log.AppendLine("  tier      clips  frames    frames w/ overlap    worst depth (frac limb)   worst pair");

            var pairHits = new long[BasisBodyPlausibility.PairCount];
            var pairWorst = new float[BasisBodyPlausibility.PairCount];
            var pairWorstRadii = new float[BasisBodyPlausibility.PairCount];
            long totalFrames = 0, totalHitFrames = 0;
            float worstDepth = 0f;
            string worstWhere = "none";
            // Tracked separately from worstDepth: only a pair whose depth discriminates may fail the gate.
            float worstDiscriminating = 0f;
            string worstDiscriminatingWhere = "none";
            int measuredTiers = 0;

            foreach ((string sub, string label) in k_Tiers)
            {
                List<BasisMotionClip> clips = LoadTier(sub);
                if (clips.Count == 0) continue;
                measuredTiers++;

                long tierFrames = 0, tierHits = 0;
                float tierWorst = 0f;
                string tierWorstPair = "none";

                foreach (BasisMotionClip c in clips)
                {
                    BasisBodyPlausibilitySummary s = BasisBodyPlausibility.Run(c);
                    Assert.That(s.Ok, Is.True, $"{label}/{c.Name}: {s.Error}");

                    tierFrames += s.Frames;
                    tierHits += s.IntersectFrames;
                    for (int p = 0; p < BasisBodyPlausibility.PairCount; p++)
                    {
                        pairHits[p] += s.PairHitFrames[p];
                        if (s.PairWorstFracLimb[p] > pairWorst[p]) pairWorst[p] = s.PairWorstFracLimb[p];
                        if (s.PairWorstFracRadii[p] > pairWorstRadii[p]) pairWorstRadii[p] = s.PairWorstFracRadii[p];
                    }
                    if (s.WorstPenetrationFracLimb > tierWorst)
                    {
                        tierWorst = s.WorstPenetrationFracLimb;
                        tierWorstPair = $"{s.WorstPair} ({c.Name} f{s.WorstPairFrame})";
                    }
                    if (s.WorstDiscriminatingPenetrationFracLimb > worstDiscriminating)
                    {
                        worstDiscriminating = s.WorstDiscriminatingPenetrationFracLimb;
                        worstDiscriminatingWhere = $"{label}: {s.WorstDiscriminatingPair} ({c.Name} f{s.WorstDiscriminatingPairFrame})";
                    }
                }

                if (tierWorst > worstDepth) { worstDepth = tierWorst; worstWhere = $"{label}: {tierWorstPair}"; }
                totalFrames += tierFrames;
                totalHitFrames += tierHits;

                log.AppendLine($"  {label,-8}  {clips.Count,5}  {tierFrames,6}   {tierHits,8} " +
                               $"({(tierFrames > 0 ? 100.0 * tierHits / tierFrames : 0),6:F3}%)   " +
                               $"{tierWorst,10:F3}            {tierWorstPair}");
            }

            if (measuredTiers == 0) Assert.Ignore($"no mocap corpus at {CorpusRoot}");

            // fracRadii is the column that decides whether a pair's DEPTH means anything: it is bounded [0,1]
            // for every pair (1.0 = the two bone axes are coincident), where fracLimb's ceiling varies by 5x
            // across pairs and so cannot be compared between them.
            log.AppendLine("\n  per-pair breakdown (pooled over every tier). 'depth' = is this pair's depth asserted on?");
            for (int p = 0; p < BasisBodyPlausibility.PairCount; p++)
            {
                bool disc = BasisBodyPlausibility.PairDepthDiscriminating(p);
                log.AppendLine($"    {BasisBodyPlausibility.PairLabel(p),-26} " +
                               $"{pairHits[p],8} frames ({(totalFrames > 0 ? 100.0 * pairHits[p] / totalFrames : 0),7:F4}%)  " +
                               $"worst {pairWorst[p]:F3} limb / {pairWorstRadii[p]:F3} radii   " +
                               $"depth {(disc ? "ASSERTED" : "excluded (saturates)")}");
            }
            log.AppendLine($"\n  pooled: {totalHitFrames}/{totalFrames} frames " +
                           $"({(totalFrames > 0 ? 100.0 * totalHitFrames / totalFrames : 0):F3}%)");
            log.AppendLine($"  worst depth, ANY pair            : {worstDepth:F3} at {worstWhere}");
            log.AppendLine($"  worst depth, DISCRIMINATING pairs: {worstDiscriminating:F3} at {worstDiscriminatingWhere}" +
                           $"   (envelope {BasisBodyPlausibility.MaxPenetrationFracLimb:F2})");
            TestContext.WriteLine(log.ToString());

            Assert.That(totalFrames, Is.GreaterThan(10000), "too few frames for this to mean anything");

            float pooled = (float)totalHitFrames / totalFrames;
            Assert.That(pooled, Is.LessThan(k_MaxPooledFrac),
                $"real humans register self-intersection on {100f * pooled:F2}% of frames. THE DETECTOR IS WRONG -- " +
                "a real human cannot pass through themselves, so the capsule radii are too large or a pair that " +
                "shares a joint slipped into the list. Read the per-pair table above; do not relax the threshold.");

            for (int p = 0; p < BasisBodyPlausibility.PairCount; p++)
            {
                float frac = (float)pairHits[p] / totalFrames;
                Assert.That(frac, Is.LessThan(k_MaxPerPairFrac),
                    $"pair '{BasisBodyPlausibility.PairLabel(p)}' fires on {100f * frac:F2}% of real human frames -- " +
                    "that pair's radii are wrong, or the two segments are effectively adjacent");
            }

            // ── DEPTH, on the pairs where depth is a real measurement ────────────────────────────────────
            // Measured worst on these pairs is 0.125; the envelope is 0.20; a genuine pass-through is 0.319.
            Assert.That(worstDiscriminating, Is.LessThan(BasisBodyPlausibility.MaxPenetrationFracLimb),
                $"a real human registered {worstDiscriminating:F3} of a limb length of penetration at " +
                $"{worstDiscriminatingWhere}, past the {BasisBodyPlausibility.MaxPenetrationFracLimb:F2} envelope. " +
                $"On these pairs depth DOES discriminate -- the worst real motion measured on 2026-07-22 was " +
                $"{k_MeasuredWorstDiscriminating:F3} and an unambiguous pass-through is {k_SyntheticPassThrough:F3} -- " +
                "so this is pass-through depth, not contact. Either the corpus clip is broken or the detector is. " +
                "Do not widen the envelope: read the per-pair table above and find out which pair moved.");

            // ── AND THE EXCLUSION MUST STAY EARNED ───────────────────────────────────────────────────────
            // Twelve pairs are exempt from the assertion above because their depth number saturates. That is
            // a claim about the radii, and radii can change. So CHECK it: if the arm x same-side-leg family
            // stops pinning its axes together on real motion, the exemption has outlived its evidence and
            // those pairs should go back under the depth assertion rather than sitting silently exempt.
            int saturating = 0, excluded = 0;
            for (int p = 0; p < BasisBodyPlausibility.PairCount; p++)
            {
                if (BasisBodyPlausibility.PairDepthDiscriminating(p)) continue;
                excluded++;
                if (pairWorstRadii[p] >= k_SaturationFracRadii) saturating++;
            }
            Assert.That(saturating, Is.GreaterThan(0),
                $"all {excluded} depth-excluded pairs now stay under {k_SaturationFracRadii:F2} of their summed core " +
                "radii on real human motion. The reason they were excluded -- that real motion drives their bone axes " +
                "together and saturates the depth number (0.946-0.999 measured 2026-07-22) -- no longer holds. Put " +
                "them back under the depth assertion, or restate why they are out.");

            // The converse: no ASSERTED pair may be anywhere near saturation, or its depth number is fiction
            // too and the exclusion list is incomplete. Measured worst on an asserted pair was 0.448.
            for (int p = 0; p < BasisBodyPlausibility.PairCount; p++)
            {
                if (!BasisBodyPlausibility.PairDepthDiscriminating(p)) continue;
                Assert.That(pairWorstRadii[p], Is.LessThan(k_SaturationFracRadii),
                    $"pair '{BasisBodyPlausibility.PairLabel(p)}' is depth-asserted but real human motion drives its " +
                    $"axes to {pairWorstRadii[p]:F3} of the summed core radii -- it saturates like the arm x leg " +
                    "family does, so its depth number cannot tell contact from pass-through either. It belongs on " +
                    "the exclusion list (BasisBodyPlausibility, next to Pair), not under this assertion.");
            }
        }

        /// <summary>
        /// ⭐ THE RULER-GUARD FOR (B). Every GATED channel's limit is a law somebody already measured, so
        /// real humans must sit inside it. A failure here is not "a human did something impossible" -- it is
        /// this file measuring the wrong quantity, or a limit being wrong.
        ///
        /// THE UNGATED CHANNELS ARE REPORTED, NOT ASSERTED, and that is the honest state: their limits come
        /// from the clinical literature and have never been confronted with this corpus through this code.
        /// The table below is what turns them into gates -- see the report test.
        /// </summary>
        [Test]
        public void RealHumans_StayInsideEveryGatedEnvelope()
        {
            const float k_MaxPooledFrac = 0.03f;   // same allowance, and same reasoning, as the spine corpus test

            var acc = new BasisRomAccumulator();
            long frames = 0, violating = 0;
            int clips = 0;
            var log = new StringBuilder("\nGATED ROM channels on REAL human motion (the ruler-guard)\n");
            var worst = new List<string>();

            foreach ((string sub, string label) in k_Tiers)
            {
                foreach (BasisMotionClip c in LoadTier(sub))
                {
                    BasisBodyPlausibilitySummary s = BasisBodyPlausibility.Run(c, acc);
                    Assert.That(s.Ok, Is.True, $"{label}/{c.Name}: {s.Error}");
                    frames += s.Frames;
                    violating += s.RomViolationFrames;
                    clips++;
                    if (s.RomViolationFrac > 0.10f)
                    {
                        worst.Add($"  {label}/{c.Name}: {100f * s.RomViolationFrac:F1}% of frames, worst " +
                                  $"'{s.WorstRomChannel}' {(s.WorstRomDirection < 0 ? "below" : "above")} by {s.WorstRomExcess:F2}");
                    }
                }
            }

            if (clips == 0) Assert.Ignore($"no mocap corpus at {CorpusRoot}");

            log.AppendLine("  channel                       gated   lo        hi        p01       p50       p95       p99       max       violate%");
            for (int c = 0; c < (int)BasisRomChannel.Count; c++)
            {
                BasisRomSpec spec = BasisBodyPlausibility.Spec((BasisRomChannel)c);
                if (!spec.Gated) continue;
                BasisRomStat st = acc.Summarise((BasisRomChannel)c);
                log.AppendLine($"  {spec.Name,-28}  {"yes",5}  {spec.Lo,8:F2}  {spec.Hi,8:F2}  " +
                               $"{st.P01,8:F2}  {st.P50,8:F2}  {st.P95,8:F2}  {st.P99,8:F2}  {st.Max,8:F2}  {100f * st.ViolationFrac,7:F3}");
            }
            float pooledRom = frames > 0 ? (float)violating / frames : 0f;
            log.AppendLine($"\n  {clips} clips, {frames} frames, {violating} with a gated violation ({100f * pooledRom:F3}%)");
            if (worst.Count > 0) log.AppendLine("  clips over 10%:\n" + string.Join("\n", worst));
            TestContext.WriteLine(log.ToString());

            Assert.That(frames, Is.GreaterThan(10000), "too few frames for this to mean anything");
            Assert.That(pooledRom, Is.LessThan(k_MaxPooledFrac),
                $"the gated envelope fires on {100f * pooledRom:F2}% of REAL human motion. Every gated limit is a " +
                "law somebody already measured (BasisElbowAnatomyCore's ceiling, BasisLegSolveCore's anterior " +
                "half-space, BasisSpineAnatomy's table), so this means THIS FILE is measuring the wrong quantity, " +
                "not that humans are impossible. Check the frames before touching a limit.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════════
        // 4. THE REPORT -- no gate. The numbers do not exist yet, and there is no honest threshold before
        //    they do. THE RATCHET SITES ARE MARKED.
        // ════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ THE MEASURED ENVELOPE, per channel, per corpus tier. THIS IS THE DELIVERABLE.
        ///
        /// Nothing is asserted about the ungated channels because nothing can honestly be: their limits are
        /// clinical max-voluntary ROM and have never met this corpus through this code. What the table
        /// answers is the only question that matters before gating one:
        ///
        ///     DOES THE LIMIT SIT OUTSIDE WHAT REAL HUMANS ACTUALLY DID?
        ///
        /// If a channel's p99 is comfortably inside its envelope, the limit is safe to gate -- flip
        /// <see cref="BasisRomSpec.Gated"/> to true in BasisBodyPlausibility.BuildSpecs. If p99 is OUTSIDE
        /// it, the limit is WRONG and gating it would clip real motion; widen it to the literature's upper
        /// bound, or accept that CMU retargeting slop is producing poses the subject never made and say so.
        ///
        /// TIERS ARE SEPARATE ROWS ON PURPOSE. dynamic/ is where the extremes live -- arms overhead, full
        /// extension, martial arts -- so a limit that survives root/ and dies in dynamic/ is a limit fitted
        /// to people walking around a lab, which is exactly the failure BasisSpineAnatomy's header warns
        /// about. Reading them pooled would hide it.
        /// </summary>
        [Test]
        public void MeasuredEnvelope_PerChannel_PerTier_TheRatchetSource()
        {
            var perTier = new Dictionary<string, BasisRomAccumulator>();
            var tierFrames = new Dictionary<string, long>();
            var tierClips = new Dictionary<string, int>();

            foreach ((string sub, string label) in k_Tiers)
            {
                List<BasisMotionClip> clips = LoadTier(sub);
                if (clips.Count == 0) continue;

                var acc = new BasisRomAccumulator();
                long frames = 0;
                foreach (BasisMotionClip c in clips)
                {
                    BasisBodyPlausibilitySummary s = BasisBodyPlausibility.Run(c, acc);
                    Assert.That(s.Ok, Is.True, $"{label}/{c.Name}: {s.Error}");
                    frames += s.Frames;
                }
                perTier[label] = acc;
                tierFrames[label] = frames;
                tierClips[label] = clips.Count;
            }

            if (perTier.Count == 0) Assert.Ignore($"no mocap corpus at {CorpusRoot}");

            var log = new StringBuilder("\n");
            log.AppendLine("================================================================================");
            log.AppendLine("  WHOLE-BODY ROM ENVELOPE, MEASURED ON REAL HUMANS -- the ratchet source");
            log.AppendLine("================================================================================");
            foreach (KeyValuePair<string, long> t in tierFrames)
            {
                log.AppendLine($"  tier {t.Key,-9} {tierClips[t.Key],3} clips, {t.Value,7} frames");
            }

            foreach (BasisRomChannel ch in System.Enum.GetValues(typeof(BasisRomChannel)))
            {
                if (ch == BasisRomChannel.Count) continue;
                BasisRomSpec spec = BasisBodyPlausibility.Spec(ch);

                log.AppendLine($"\n  {spec.Name}  [{spec.Unit}]   envelope [{spec.Lo:F2}, {spec.Hi:F2}]   " +
                               $"{(spec.Gated ? "GATED" : "reported only -- RATCHET SITE")}");
                log.AppendLine($"    source: {spec.Source}");
                log.AppendLine("    tier        n        min       p01       p50       p95       p99       max     outside%  worstDir");

                foreach (KeyValuePair<string, BasisRomAccumulator> kv in perTier)
                {
                    BasisRomStat s = kv.Value.Summarise(ch);
                    if (s.Count == 0) { log.AppendLine($"    {kv.Key,-9}  (not measurable in this tier)"); continue; }
                    string dir = s.WorstDirection == 0 ? "-" : (s.WorstDirection < 0 ? "BELOW lo" : "ABOVE hi");
                    log.AppendLine($"    {kv.Key,-9} {s.Count,7}  {s.Min,8:F2}  {s.P01,8:F2}  {s.P50,8:F2}  " +
                                   $"{s.P95,8:F2}  {s.P99,8:F2}  {s.Max,8:F2}  {100f * s.ViolationFrac,7:F3}  {dir}");
                }
            }

            log.AppendLine("\n  HOW TO USE THIS TABLE:");
            log.AppendLine("    p99 comfortably INSIDE [lo, hi]  -> the limit is safe. Set Gated = true in");
            log.AppendLine("                                        BasisBodyPlausibility.BuildSpecs and it becomes a ratchet.");
            log.AppendLine("    p99 OUTSIDE [lo, hi]             -> the limit is WRONG for real motion. Widen it, or");
            log.AppendLine("                                        establish that the corpus frames are retarget slop.");
            log.AppendLine("    dynamic/ far past the rest       -> expected; that tier was chosen for the extremes.");
            log.AppendLine("                                        A limit that only survives root/ is fitted to walking.");
            TestContext.WriteLine(log.ToString());

            // The only thing assertable without a settled baseline: the harness measured something.
            foreach (KeyValuePair<string, BasisRomAccumulator> kv in perTier)
            {
                Assert.That(kv.Value.Summarise(BasisRomChannel.LeftKneeFlexDeg).Count, Is.GreaterThan(0),
                    $"tier '{kv.Key}' produced no knee samples at all -- the harness is not reading the clips");
            }
        }

        /// <summary>
        /// THE DYNAMIC TIER MUST ACTUALLY REACH THE EXTREMES, or using it to calibrate an envelope is
        /// circular: a limit "validated" against motion that never approaches it has been validated against
        /// nothing. dynamic/ was curated for arms overhead, full extension and fast swings, so the shoulder
        /// must genuinely go overhead and the knee must genuinely fold. This is the corpus's own
        /// RequireInReach.
        /// </summary>
        [Test]
        public void TheDynamicTier_ReallyDoesReachTheExtremes_OrItCannotCalibrateAnything()
        {
            List<BasisMotionClip> clips = LoadTier("dynamic");
            if (clips.Count == 0) Assert.Ignore($"no dynamic tier at {Path.Combine(CorpusRoot, "dynamic")}");

            var acc = new BasisRomAccumulator();
            foreach (BasisMotionClip c in clips)
            {
                BasisBodyPlausibilitySummary s = BasisBodyPlausibility.Run(c, acc);
                Assert.That(s.Ok, Is.True, $"dynamic/{c.Name}: {s.Error}");
            }

            BasisRomStat elev = acc.Summarise(BasisRomChannel.LeftShoulderElevationDeg);
            BasisRomStat knee = acc.Summarise(BasisRomChannel.LeftKneeFlexDeg);
            BasisRomStat hip = acc.Summarise(BasisRomChannel.LeftHipFlexDeg);

            TestContext.WriteLine(
                $"\nDYNAMIC tier extremes ({clips.Count} clips)\n" +
                $"  shoulder elevation  p95 {elev.P95:F1}  max {elev.Max:F1} deg  (0 = hanging, 180 = overhead)\n" +
                $"  knee flexion        p95 {knee.P95:F1}  max {knee.Max:F1} deg\n" +
                $"  hip flexion         p95 {hip.P95:F1}  max {hip.Max:F1} deg");

            Assert.That(elev.Max, Is.GreaterThan(120f),
                $"the arms-overhead tier never got the humerus past {elev.Max:F0} deg of elevation -- it is not " +
                "exercising the poses it was curated for, so no envelope calibrated on it means anything");
            Assert.That(knee.Max, Is.GreaterThan(90f),
                $"the dynamic tier never bent a knee past {knee.Max:F0} deg");
        }
    }
}
