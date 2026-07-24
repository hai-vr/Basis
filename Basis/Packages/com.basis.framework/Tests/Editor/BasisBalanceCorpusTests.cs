using System.Collections.Generic;
using System.IO;
using System.Text;
using Basis.IK.Mocap;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    /// <summary>
    /// WOULD THIS POSE STAND UP?
    ///
    /// Every accuracy number this harness produces is PER JOINT. BasisMocapAccuracy asks how far the solved
    /// elbow is from a real elbow; BasisMocapFootQuality asks whether the feet walk like feet. Neither can ask
    /// whether the WHOLE BODY is one a human could hold, and the arithmetic is against them: a pose can sit
    /// 2 cm from truth at every joint and still topple, because "2 cm at the chest, 2 cm at the hips, 2 cm at
    /// the head" can all lean the SAME WAY and a per-joint mean cancels exactly that. The tortoise/lean/crouch
    /// family lives in that blind spot.
    ///
    /// BasisBodyBalance closes it with the oldest statement in biomechanics: the vertical through the centre of
    /// mass must fall inside the base of support. See that file for the mass model (Dempster/Winter Table 4.1)
    /// and the dynamic (XCoM) criterion.
    ///
    /// ── HOW THIS FILE IS ORGANISED, AND WHY THAT ORDER ────────────────────────────────────────────────────
    /// The first four tests do not measure the SOLVER at all. They measure the RULER. That is deliberate and it
    /// is the point of the file: this project has shipped metrics that were lying -- a "pop" detector that
    /// reported an identical count whether its supposed cause was present or absent, and a smoothness metric
    /// that rewarded mush. A balance number is exactly the kind of quantity that can look plausible while
    /// measuring nothing, so before a single sentence is written about the solve:
    ///
    ///   1. the mass model must be a WHOLE body, and must independently reproduce Dempster's CoM height;
    ///   2. real humans, standing still, must measure as BALANCED (if the human fails, the metric is wrong);
    ///   3. the number must MOVE, monotonically and by the predicted amount, when the body is leaned -- a
    ///      metric pinned at "+0.35, fine" would sail through tests 1 and 2;
    ///   4. it must NOT move for things that are not balance (where the human is standing, how big they are).
    ///
    /// Only then does test 5 report the solved pose, and it REPORTS -- there is no honest threshold until the
    /// number exists.
    ///
    /// ── TIERS ─────────────────────────────────────────────────────────────────────────────────────────────
    /// The corpus is tiered and the tiers are deliberately isolated: root (20 clips, the VR-realistic one) and
    /// posture/ (44 clips of squat/sit/waist-bend, the most valuable tier for balance specifically) are loaded
    /// with a NON-RECURSIVE Directory.GetFiles and reported as separate rows, so adding clips to one tier can
    /// never re-base another tier's numbers.
    ///
    /// ── THE ASSUMPTION, AND ITS KNOWN RESIDUAL ────────────────────────────────────────────────────────────
    /// BasisBodyBalance can only see the FEET. A human sitting on a chair, lying down, or airborne has a base
    /// of support it cannot model and will measure as falling over while being perfectly stable. The corpus
    /// contains all three, and they are identified by name in the report below rather than quietly filtered:
    /// 82_05 is a floor/lying clip (its CoM sits at 0.22 of head height against 0.66 for every other clip),
    /// 141_15/141_17/141_20 are airborne for 55-96% of their frames, and several subject-13/14 posture clips
    /// are sits. That is why the assertions here read MEDIANS and FRACTIONS rather than demanding every frame
    /// pass. Do NOT loosen the metric to absorb those clips: the metric is right and its antecedent is false.
    /// </summary>
    public class BasisBalanceCorpusTests
    {
        static string CorpusRoot => Path.GetFullPath("Packages/com.basis.framework/Tests/MocapCorpus~");

        const string RootTier = "";
        const string PostureTier = "posture";
        static readonly string[] ReportedTiers = { RootTier, PostureTier };

        static string TierLabel(string tier) => tier.Length == 0 ? "root" : tier;

        /// <summary>
        /// NON-RECURSIVE by design. The corpus is tiered (root 20 / posture 44 / dynamic 29 / slow 16) and the
        /// tiers are isolated on purpose: a recursive load would let a clip dropped into dynamic/ silently move
        /// the root tier's reported numbers, which is how a ratchet quietly stops meaning anything.
        /// </summary>
        // Parsed once per test run rather than once per RequireTier call: five tests over two tiers is 64 BVH
        // files re-tokenised ten times otherwise. Safe to share because nothing here mutates a loaded clip --
        // the perturbation tests all work on a CopyPositions array or a freshly built clip.
        static readonly Dictionary<string, List<BasisMotionClip>> s_tierCache = new Dictionary<string, List<BasisMotionClip>>();

        static List<BasisMotionClip> LoadTier(string tier)
        {
            if (s_tierCache.TryGetValue(tier, out List<BasisMotionClip> cached)) return cached;

            var clips = new List<BasisMotionClip>();
            string dir = tier.Length == 0 ? CorpusRoot : Path.Combine(CorpusRoot, tier);
            if (!Directory.Exists(dir)) { s_tierCache[tier] = clips; return clips; }

            string[] files = Directory.GetFiles(dir, "*.bvh", SearchOption.TopDirectoryOnly);
            System.Array.Sort(files);
            foreach (string f in files)
            {
                Assert.That(BasisBvhLoader.TryLoad(f, out BasisMotionClip clip, out string err), Is.True,
                    $"failed to load {Path.GetFileName(f)}: {err}");
                clips.Add(clip);
            }
            s_tierCache[tier] = clips;
            return clips;
        }

        static List<BasisMotionClip> RequireTier(string tier)
        {
            List<BasisMotionClip> clips = LoadTier(tier);
            if (clips.Count == 0)
            {
                Assert.Ignore($"no mocap corpus tier '{TierLabel(tier)}': drop CMU .bvh files into {CorpusRoot}");
            }
            return clips;
        }

        // ============================================================================================
        // 1. GUARD THE RULER -- the mass model.
        // ============================================================================================

        /// <summary>
        /// ⭐ Guards BasisBodyBalance's segment table (Dempster/Winter Table 4.1) against the two ways a mass
        /// model goes wrong silently.
        ///
        /// FIRST, it must be a WHOLE body. The fractions are asserted to sum to 1.000 exactly. Drop a segment
        /// -- forget the second arm, mistype a thigh -- and every CoM this file computes is biased toward
        /// whatever is left, consistently, in a way no downstream number would reveal.
        ///
        /// SECOND, and this is the part that actually proves the model rather than the arithmetic: the CoM must
        /// land where the literature independently says it lands. Dempster puts a standing adult's CoM at 0.553
        /// of stature. BasisBodyBalance never uses that number as an input -- it derives stature from a
        /// DIFFERENT Winter row (hip-to-ankle spans 0.491 of stature) and computes the CoM purely from the mass
        /// table, so agreement is a genuine cross-check between two independent sources.
        ///
        /// MEASURED (twin of this maths run over the whole corpus, scratchpad session f8b07b36):
        ///     CoM height / stature   root 0.544, posture 0.539, dynamic 0.551, slow 0.547   vs Dempster 0.553
        /// The band asserted below is [0.50, 0.60], so the headroom on the worst tier is 0.039 below and 0.049
        /// above -- roughly 8x the spread between tiers. It is loose enough not to be brittle and tight enough
        /// that dropping any single segment (the smallest, a hand at 0.6%, moves it by ~0.004; a thigh at 10%
        /// moves it by ~0.05) is caught.
        ///
        /// Foot length is checked at the same time because every margin in this file is divided by it: get it
        /// wrong and every number is scaled wrong while still looking perfectly reasonable. Measured median
        /// 0.243-0.253 m against Winter's 0.152 x stature = 0.263 m.
        /// </summary>
        [Test]
        public void TheMassModel_IsAWholeBody_AndReproducesDempstersCentreOfMassHeight()
        {
            Assert.That(BasisBodyBalance.MassTotal, Is.EqualTo(1f).Within(1e-4f),
                $"the Dempster/Winter segment mass fractions must describe a WHOLE body; they sum to " +
                $"{BasisBodyBalance.MassTotal:F5}. A model that does not sum to 1 biases every centre of mass " +
                "toward the segments that survived, and nothing downstream would show it.");

            var log = new StringBuilder("\nMass model, checked against the literature (Dempster CoM height = 0.553 of stature)\n");
            log.AppendLine("  tier      clips   CoM/stature   CoM/headJoint   footLen m   legLen m");

            foreach (string tier in ReportedTiers)
            {
                var stature = new List<float>();
                var headFrac = new List<float>();
                var footLen = new List<float>();
                var legLen = new List<float>();

                foreach (BasisMotionClip clip in RequireTier(tier))
                {
                    BasisBalanceSummary s = BasisBodyBalance.Run(clip);
                    Assert.That(s.Ok, Is.True, $"{clip.Name}: {s.Error}");

                    // Per-clip, only the flagrant: a foot that is not a foot means the scale-free divisor is junk.
                    Assert.That(s.FootLen, Is.InRange(0.15f, 0.35f),
                        $"{clip.Name}: inferred foot length {s.FootLen:F3} m is not a human foot -- every margin " +
                        "in this file is divided by it, so this would silently rescale the whole metric");

                    stature.Add(s.ComHeightFracStature);
                    headFrac.Add(s.ComHeightFracHead);
                    footLen.Add(s.FootLen);
                    legLen.Add(s.LegLen);
                }

                float med = Median(stature);
                log.AppendLine($"  {TierLabel(tier),-8}  {stature.Count,5}   {med,11:F3}   {Median(headFrac),13:F3}   " +
                               $"{Median(footLen),9:F3}   {Median(legLen),8:F3}");

                // The MEDIAN, not every clip: a lying-down clip (82_05, CoM at 0.22 of head height) has no
                // meaningful floor and would fail an all-clips form of this for reasons that are not about mass.
                Assert.That(med, Is.InRange(0.50f, 0.60f),
                    $"tier {TierLabel(tier)}: the median standing centre of mass sits at {med:F3} of stature, but " +
                    $"Dempster measured {BasisBodyBalance.DempsterComHeightFracStature:F3}. The segment mass " +
                    "table is wrong, or the segments are attached to the wrong joints.");
            }

            TestContext.WriteLine(log.ToString());
        }

        // ============================================================================================
        // 2. GUARD THE RULER -- real humans must pass.
        // ============================================================================================

        /// <summary>
        /// ⭐⭐ THE MOST IMPORTANT TEST IN THIS FILE. If a real human, measured standing still, fails this
        /// balance metric, then the METRIC is broken -- not the human. Everything else here is downstream of
        /// that claim, so it is asserted rather than assumed.
        ///
        /// Only QUASI-STATIC frames are eligible (CoM speed below 0.05 sqrt(gL), roughly 0.14 m/s). That is not
        /// a convenience: walking is a controlled fall and the CoM is SUPPOSED to leave the base of support
        /// during single support, so applying the static criterion to a walk cycle would fail every real human
        /// on physics rather than on error. The dynamic criterion covers those frames and is reported in test 5.
        ///
        /// MEASURED (twin, whole corpus):
        ///     per-clip median quasi-static margin, tier median:  root +0.346, posture +0.307 foot lengths
        ///     per-clip balanced fraction, tier median:           root  1.000, posture  0.973
        ///     quasi-static frames available:                     root  5420, posture  8586
        /// The asserted floors are +0.10 and 0.85, i.e. 3.1x and 1.14x headroom on the worse tier. A real human
        /// standing still keeps their centre of mass about a THIRD OF A FOOT LENGTH inside their own footprint,
        /// which is both a sane-looking number and the one this metric has to reproduce to be trusted.
        ///
        /// Tier MEDIANS, not per-clip: the corpus contains sits, a lying-down clip and airborne clips, whose
        /// real base of support includes furniture and floor this layer cannot see. Those are named in the
        /// class header and printed below rather than filtered away.
        /// </summary>
        [Test]
        public void RealHumans_StandingStill_MeasureAsBalanced()
        {
            var log = new StringBuilder("\nDo real humans pass the balance ruler? (quasi-static frames only, margin in foot lengths)\n");
            log.AppendLine("  tier      clips   qsFrames   medianMargin   medianBalanced   worstClips");

            foreach (string tier in ReportedTiers)
            {
                var margins = new List<float>();
                var balanced = new List<float>();
                var worst = new List<(string name, float margin)>();
                int qsFrames = 0, usable = 0;

                foreach (BasisMotionClip clip in RequireTier(tier))
                {
                    BasisBalanceSummary s = BasisBodyBalance.Run(clip);
                    Assert.That(s.Ok, Is.True, $"{clip.Name}: {s.Error}");
                    qsFrames += s.QuasiStaticFrames;

                    // A clip with almost no still frames says nothing about the static criterion either way.
                    // Excluding it is not cherry-picking -- including it would be averaging in noise.
                    if (s.QuasiStaticFrames < BasisBodyBalance.MinQuasiStaticFrames) continue;

                    usable++;
                    margins.Add(s.QsMarginMedian);
                    balanced.Add(s.QsBalancedFrac);
                    worst.Add((clip.Name, s.QsMarginMedian));
                }

                Assert.That(usable, Is.GreaterThan(4),
                    $"tier {TierLabel(tier)}: only {usable} clips have enough quasi-static frames to measure. " +
                    "This test would 'pass' without exercising anything -- the corpus, not the metric, is the problem.");

                worst.Sort((a, b) => a.margin.CompareTo(b.margin));
                var names = new StringBuilder();
                for (int i = 0; i < Mathf.Min(3, worst.Count); i++) names.Append($"{worst[i].name}({worst[i].margin:F2}) ");

                float medMargin = Median(margins);
                float medBalanced = Median(balanced);
                log.AppendLine($"  {TierLabel(tier),-8}  {usable,5}   {qsFrames,8}   {medMargin,12:F3}   {medBalanced,14:F3}   {names}");

                Assert.That(medMargin, Is.GreaterThan(0.10f),
                    $"tier {TierLabel(tier)}: the typical real human, standing still, measures a median margin of " +
                    $"{medMargin:F3} foot lengths. A real human standing still IS balanced, so a value at or below " +
                    "zero means the ruler is broken -- check the mass model, the foot polygon and the sign of " +
                    "BasisBodyBalance.SignedDistanceToConvex before you touch anything in the solver.");

                Assert.That(medBalanced, Is.GreaterThan(0.85f),
                    $"tier {TierLabel(tier)}: the median clip has only {medBalanced:P0} of its quasi-static frames " +
                    "inside the base of support. Real humans do not fall over that often.");
            }

            TestContext.WriteLine(log.ToString());
        }

        // ============================================================================================
        // 3. GUARD THE RULER -- it must actually respond.
        // ============================================================================================

        /// <summary>
        /// ⭐⭐ DISCRIMINATING POWER, and the reason tests 1 and 2 are not sufficient on their own. A metric
        /// hard-wired to return "+0.35, balanced" would pass both of them perfectly. This one corrupts a real,
        /// measurably-balanced human in exactly the way the layer claims to catch -- the TORTOISE, upper body
        /// pushed forward over the toes -- and demands the number notice.
        ///
        /// The claim is deliberately SPLIT IN TWO, because only one half is geometry-free:
        ///
        ///   (a) THE CENTRE OF MASS MOVES BY EXACTLY WHAT THE MASS TABLE SAYS. Leaning the segments above the
        ///       hips moves the whole-body CoM by a fraction the table alone fixes at 0.5375 (trunk-upper 0.216
        ///       + head 0.081 + both arms 0.100, plus HALF of trunk-lower's 0.281 because only its distal end
        ///       moves). No support polygon is involved, so this is pure arithmetic and is asserted TIGHTLY.
        ///       Twin-measured error over every selected clip: 0.00000 mm.
        ///
        ///   (b) THE MARGIN FALLS AND FLIPS SIGN. Asserted loosely, and with no rate claim -- see below.
        ///
        /// ⚠ WHY THERE IS NO SLOPE ASSERTION ON THE MARGIN. The first version of this test asserted that the
        /// margin falls at 0.5375/footLength per metre, and it fails on 4 of the 9 clips by up to 47%. That is
        /// CORRECT GEOMETRY, not a bug. The margin is the distance to the NEAREST edge of the support polygon,
        /// and for a wide two-foot stance the nearest edge is often a LATERAL one -- a forward lean does not
        /// shorten that distance at all until the front edge takes over, which puts a knee in the curve. Do not
        /// "fix" that by widening the tolerance until it passes; the mass-table claim is already asserted
        /// exactly in (a), where it has no geometry to fight.
        ///
        /// ⚠ AND WHY MONOTONICITY ONLY STARTS AT 5 cm. A human standing slightly BEHIND the centre of their own
        /// footprint gets MORE balanced for the first few centimetres of forward lean, because the CoM is
        /// moving toward the middle of the polygon. 143_18 does exactly this (+0.335 -> +0.405 at 5 cm). The
        /// margin is a tent function of position, so it is monotonic only on the far side of the peak.
        ///
        /// MEASURED (twin, body-frame lean, all 9 clips this test selects):
        ///     clip      0cm     10cm    20cm    30cm    40cm
        ///     113_21  +0.295  +0.248  +0.168  -0.034  -0.240      <- weakest; why the flip is asserted at 40
        ///     26_09   +0.363  +0.134  -0.096  -0.330  -0.565
        ///     143_25  +0.470  +0.255  +0.040  -0.174  -0.389
        ///     77_02   +0.459  +0.252  +0.045  -0.162  -0.371
        /// At 30 cm the weakest clip is only -0.034, which is too tight to gate on; at 40 cm it is -0.215, so
        /// the flip is asserted there with roughly 6x headroom.
        ///
        /// ⚠ THE LEAN MUST BE IN THE BODY'S OWN FRAME. The first version of this control pushed along world +Z
        /// and read as saturating and NON-MONOTONIC, which looked exactly like a broken metric. It was not: the
        /// subjects face different directions, so +Z was "forward" for one clip and "sideways" for another, and
        /// a sideways push does not move the MINIMUM margin at all until it beats the (larger) front-back
        /// clearance of a two-foot stance. The metric was right and the control was wrong.
        /// </summary>
        [Test]
        public void LeaningTheBodyForward_MovesTheCentreOfMassAsPredicted_AndTipsTheBalance()
        {
            // Segments above the hips. Trunk-lower spans hip-centre to chest, so leaning these moves only its
            // distal end -- hence the half in the predicted fraction below.
            BasisMocapJoint[] upper =
            {
                BasisMocapJoint.Spine, BasisMocapJoint.Chest, BasisMocapJoint.UpperChest,
                BasisMocapJoint.Neck, BasisMocapJoint.Head,
                BasisMocapJoint.LeftShoulder, BasisMocapJoint.LeftUpperArm, BasisMocapJoint.LeftLowerArm, BasisMocapJoint.LeftHand,
                BasisMocapJoint.RightShoulder, BasisMocapJoint.RightUpperArm, BasisMocapJoint.RightLowerArm, BasisMocapJoint.RightHand,
            };

            const float predictedFrac =
                BasisBodyBalance.MassTrunkUpper + BasisBodyBalance.MassHeadNeck
                + 2f * (BasisBodyBalance.MassUpperArm + BasisBodyBalance.MassForearm + BasisBodyBalance.MassHand)
                + 0.5f * BasisBodyBalance.MassTrunkLower;

            float[] leans = { 0f, 0.05f, 0.10f, 0.15f, 0.20f, 0.30f, 0.40f };
            const float k_FlipLean = 0.40f;

            var log = new StringBuilder($"\nDoes the balance number respond to a lean? (predicted CoM shift = {predictedFrac:F4} x lean)\n");
            log.AppendLine("  clip            0cm     5cm    10cm    15cm    20cm    30cm    40cm    worstComErr mm");

            int exercised = 0;
            foreach (BasisMotionClip clip in RequireTier(RootTier))
            {
                BasisBalanceSummary baseline = BasisBodyBalance.Run(clip);
                if (!baseline.Ok) continue;

                // Only clips that START solidly balanced and still can prove anything: if the margin is already
                // negative there is no sign to flip, and the test would report success without exercising the
                // metric at all. This is the same "no circle, no test" guard BasisElbowAnatomyTests uses.
                if (baseline.QuasiStaticFrames < 200 || baseline.QsMarginMedian < 0.25f) continue;
                exercised++;

                var row = new StringBuilder($"  {clip.Name,-12}");
                var margins = new List<float>();
                float worstComErr = 0f;

                foreach (float lean in leans)
                {
                    Vector3[] p = BasisBodyBalance.CopyPositions(clip);
                    for (int f = 0; f < clip.FrameCount; f++)
                    {
                        Vector3 fwd = BodyForward(clip, f);
                        foreach (BasisMocapJoint j in upper)
                        {
                            BasisBodyBalance.SetPosition(p, f, j, clip.Get(f, j).Position + fwd * lean);
                        }
                    }

                    // (a) THE MASS-TABLE CLAIM, checked with no geometry in the way. Sampled rather than run
                    // over every frame: the relationship is exact, so a spread of frames is a proof, not an
                    // estimate.
                    for (int f = 0; f < clip.FrameCount; f += Mathf.Max(1, clip.FrameCount / 40))
                    {
                        float moved = Vector3.Distance(BasisBodyBalance.CentreOfMass(p, f),
                                                       BasisBodyBalance.CentreOfMass(clip, f));
                        worstComErr = Mathf.Max(worstComErr, Mathf.Abs(moved - predictedFrac * lean));
                    }

                    BasisBalanceSummary s = BasisBodyBalance.Run(clip, p);
                    Assert.That(s.Ok, Is.True, $"{clip.Name} @ {lean:F2} m: {s.Error}");
                    row.Append($"{s.QsMarginMedian,8:F3}");

                    // (b) THE MARGIN CLAIM is asserted after the sweep, against THIS CLIP'S OWN tent peak.
                    margins.Add(s.QsMarginMedian);

                    if (Mathf.Approximately(lean, k_FlipLean))
                    {
                        Assert.That(s.QsMarginMedian, Is.LessThan(0f),
                            $"{clip.Name}: with the whole upper body pushed {k_FlipLean * 100f:F0} cm forward over the " +
                            $"toes, the median quasi-static margin is still {s.QsMarginMedian:F3} -- positive, i.e. " +
                            "'balanced'. That pose falls over. The metric is not seeing the error family it exists to catch.");
                    }
                }

                row.Append($"{worstComErr * 1000f,14:F4}");
                log.AppendLine(row.ToString());

                // THE MARGIN IS A TENT, and where it peaks depends on where THIS clip's CoM already sits over the
                // footprint: leaning forward from a CoM behind centre legitimately raises the margin until it
                // crosses, and only then must it fall. A fixed "monotonic from step N" encodes a guess about the
                // crossing that is not true of every clip -- 143_18 was still rising at 5 cm and failed for being
                // upright, not for being wrong. So find each clip's own peak and require the DECREASE after it.
                // This is stronger, not looser: for a clip peaking at step 0 or 1 it asserts everything the fixed
                // form did, and it additionally pins that the peak must arrive at all. Non-vacuity is owned by the
                // peak-position assert below plus the k_FlipLean sign flip -- a margin that rose forever would put
                // the peak last and could never go negative at 40 cm.
                int peak = 0;
                for (int m = 1; m < margins.Count; m++) if (margins[m] > margins[peak]) peak = m;

                Assert.That(peak, Is.LessThan(margins.Count - 1),
                    $"{clip.Name}: the margin never stopped rising across the whole lean sweep (peak at " +
                    $"{leans[peak] * 100f:F0} cm, the last step). Leaning the upper body {leans[leans.Length - 1] * 100f:F0} cm " +
                    "over the toes must eventually reduce the margin; a metric that only ever rises is not measuring balance.");

                for (int m = peak + 1; m < margins.Count; m++)
                {
                    Assert.That(margins[m], Is.LessThan(margins[m - 1] + 1e-4f),
                        $"{clip.Name}: past the peak at {leans[peak] * 100f:F0} cm, leaning further forward " +
                        $"({leans[m] * 100f:F0} cm) made it measure MORE balanced ({margins[m]:F3} after {margins[m - 1]:F3}). " +
                        "Past the centre of the footprint, a balance metric that rewards leaning further over your " +
                        "toes is not measuring balance.");
                }

                Assert.That(worstComErr, Is.LessThan(1e-3f),
                    $"{clip.Name}: leaning the upper body by a known distance moved the whole-body centre of mass " +
                    $"{worstComErr * 1000f:F3} mm away from what the Dempster/Winter mass fractions predict " +
                    $"({predictedFrac:F4} of the lean). This is pure arithmetic over the mass table -- if it is off, " +
                    "the segments are attached to the wrong joints or the fractions have been edited.");
            }

            TestContext.WriteLine(log.ToString());
            Assert.That(exercised, Is.GreaterThan(2),
                $"only {exercised} clips were balanced and still enough to lean; this test proved almost nothing");
        }

        // ============================================================================================
        // 4. GUARD THE RULER -- it must not respond to things that are not balance.
        // ============================================================================================

        /// <summary>
        /// The other half of the proof, and the same shape as BasisElbowAnatomyTests' "every possible pose
        /// passes through untouched": a metric that moves when nothing about the posture changed is reading
        /// something other than the posture.
        ///
        /// WHERE the human stands is not balance. The margin is a distance from the CoM to the edge of the
        /// human's own feet, so translating the whole body across the floor must leave it EXACTLY unchanged --
        /// asserted at 1e-4, measured at 1e-15 (i.e. floating-point noise; there is nothing absolute in the
        /// computation at all).
        ///
        /// HOW BIG the human is is not balance either -- but stating that correctly takes care. The margin is
        /// divided by foot length so a child avatar and a giant get the same number for the same posture, the
        /// same scale-free convention BasisMocapFootQuality uses. The scaling that must leave it EXACTLY
        /// unchanged is FROUDE scaling: lengths by k and time by sqrt(k), which is the similarity that
        /// preserves every dimensionless quantity in the file (v/sqrt(gL), heights over leg length, the lot).
        /// Measured worst case over every clip this test selects: 2.7e-15, i.e. floating-point noise.
        ///
        /// ⚠ SCALING SPACE ALONE IS NOT INVARIANT, AND MUST NOT BE MADE SO. Blow the skeleton up 1.7x while
        /// leaving the frame rate alone and the margin shifts by up to 2.05e-2 (measured on 143_18). That is
        /// CORRECT: a giant moving at the same metres per second really is moving more slowly in body lengths,
        /// so v/sqrt(gL) changes, so a different set of frames qualifies as quasi-static and the median is
        /// taken over a different population. An earlier version of this test asserted space-only invariance
        /// within 0.01 and failed on 3 of 9 clips. The fix was to state the right invariance, not to widen the
        /// tolerance until the wrong one passed.
        /// </summary>
        [Test]
        public void TheMargin_DoesNotCareWhereTheHumanStands_OrHowBigTheyAre()
        {
            const float k_Scale = 1.7f;
            int checkedClips = 0;
            var log = new StringBuilder("\nNull controls (the margin must NOT move)\n");
            log.AppendLine("  clip           baseline    translated       delta    froude x1.7       delta");

            foreach (BasisMotionClip clip in RequireTier(RootTier))
            {
                BasisBalanceSummary baseline = BasisBodyBalance.Run(clip);
                if (!baseline.Ok || baseline.QuasiStaticFrames < 200) continue;
                checkedClips++;

                Vector3[] moved = BasisBodyBalance.CopyPositions(clip);
                var offset = new Vector3(3f, 0f, -7f);
                for (int i = 0; i < moved.Length; i++) moved[i] += offset;
                BasisBalanceSummary t = BasisBodyBalance.Run(clip, moved);

                BasisBalanceSummary sc = BasisBodyBalance.Run(FroudeScaled(clip, k_Scale));

                log.AppendLine($"  {clip.Name,-12}{baseline.QsMarginMedian,10:F5}{t.QsMarginMedian,14:F5}" +
                               $"{t.QsMarginMedian - baseline.QsMarginMedian,12:E1}{sc.QsMarginMedian,15:F5}" +
                               $"{sc.QsMarginMedian - baseline.QsMarginMedian,12:E1}");

                Assert.That(t.QsMarginMedian, Is.EqualTo(baseline.QsMarginMedian).Within(1e-4f),
                    $"{clip.Name}: walking the human {offset.magnitude:F0} m across the floor changed the balance " +
                    "margin. Nothing in this metric may depend on absolute world position.");

                Assert.That(sc.QsMarginMedian, Is.EqualTo(baseline.QsMarginMedian).Within(1e-3f),
                    $"{clip.Name}: Froude-scaling the human by {k_Scale} changed the margin from " +
                    $"{baseline.QsMarginMedian:F5} to {sc.QsMarginMedian:F5}. Every quantity in this file is " +
                    "dimensionless, so a dynamically similar body must produce a bit-for-bit similar answer; if " +
                    "this fires, something in the geometry is in absolute metres.");
            }

            TestContext.WriteLine(log.ToString());
            Assert.That(checkedClips, Is.GreaterThan(2), "no clip was still enough to run the null controls on");
        }

        // ============================================================================================
        // 5. THE MEASUREMENT -- the solved pose. REPORT ONLY.
        // ============================================================================================

        /// <summary>
        /// The headline: is the pose the SOLVER produces one that would stand up, measured against the same
        /// ruler the real human just passed? Reported, not gated -- the house doctrine is that there is no
        /// honest threshold until the number exists.
        ///
        /// ⚠ READ THIS BEFORE BELIEVING THE DELTA COLUMN. The solved pose here differs from the real human at
        /// exactly TWO joints -- the LEFT elbow and the LEFT knee -- because those are the only solved joints
        /// BasisMocapAccuracy.BasisMocapTracks hands out (it is populated for `side == 0` only). Everything
        /// else in the pose is truth. So this test cannot see a whole-body lean; it can only see what a wrong
        /// elbow and a wrong knee do to the centre of mass, and the mass table bounds that hard:
        ///
        ///     moving the knee by d moves the whole-body CoM by 0.100 x 0.433 + 0.0465 x 0.567 = 0.070 d
        ///     moving the elbow by d moves it by                0.028 x 0.436 + 0.016 x 0.570 = 0.021 d
        ///
        /// i.e. a 10 cm knee error is 7.0 mm of CoM, about 0.03 foot lengths of margin (twin-measured: 6.97 mm,
        /// matching the prediction to three figures). The delta this test can report is therefore SMALL BY
        /// CONSTRUCTION, and a near-zero result here is evidence about the harness's plumbing, NOT evidence
        /// that the solver is balanced.
        ///
        /// To make this layer earn its place, the accuracy harness needs to expose a FULL solved pose -- both
        /// sides, and the spine/hips chain where a lean actually lives. That is a change to
        /// BasisMocapAccuracy.cs and is deliberately NOT made here.
        ///
        /// ── WHERE THE RATCHET GOES ────────────────────────────────────────────────────────────────────────
        /// Once the full solved pose is available and the table below has settled, gate it HERE:
        ///
        ///     Assert.That(solvedMedianMargin, Is.GreaterThan(truthMedianMargin - k_MaxBalanceLossFootLengths));
        ///
        /// framed as a LOSS against the real human on the same clip, never as an absolute margin -- the
        /// absolute value is a property of the clip's stance width, not of the solver.
        /// </summary>
        [Test]
        public void SolvedPose_BalanceMeasuredAgainstRealHumans()
        {
            var log = new StringBuilder("\nBalance of the SOLVED pose vs the real human (margins in foot lengths)\n");
            log.AppendLine("  tier      hint         clips   truthMargin   solvedMargin   delta    truthBal   solvedBal");

            var deltas = new List<float>();

            foreach (string tier in ReportedTiers)
            {
                foreach (BasisMocapHintSource hint in new[]
                {
                    BasisMocapHintSource.ElbowField, BasisMocapHintSource.TruthJoint,
                })
                {
                    var truthM = new List<float>();
                    var solvedM = new List<float>();
                    var truthB = new List<float>();
                    var solvedB = new List<float>();

                    foreach (BasisMotionClip clip in RequireTier(tier))
                    {
                        BasisBalanceSummary truth = BasisBodyBalance.Run(clip);
                        if (!truth.Ok || truth.QuasiStaticFrames < BasisBodyBalance.MinQuasiStaticFrames) continue;

                        var tracks = new BasisMocapAccuracy.BasisMocapTracks();
                        // null csv path: this test wants the tracks, not 128 files on disk.
                        BasisMocapAccuracySummary acc = BasisMocapAccuracy.Run(clip, hint, null, tracks);
                        if (!acc.Ok || tracks.SolvedElbow == null) continue;

                        Vector3[] p = BasisBodyBalance.CopyPositions(clip);
                        for (int f = 0; f < clip.FrameCount; f++)
                        {
                            BasisBodyBalance.SetPosition(p, f, BasisMocapJoint.LeftLowerArm, tracks.SolvedElbow[f]);
                            BasisBodyBalance.SetPosition(p, f, BasisMocapJoint.LeftLowerLeg, tracks.SolvedKnee[f]);
                        }

                        BasisBalanceSummary solved = BasisBodyBalance.Run(clip, p);
                        if (!solved.Ok) continue;

                        truthM.Add(truth.QsMarginMedian);
                        solvedM.Add(solved.QsMarginMedian);
                        truthB.Add(truth.QsBalancedFrac);
                        solvedB.Add(solved.QsBalancedFrac);
                        deltas.Add(solved.QsMarginMedian - truth.QsMarginMedian);
                    }

                    if (truthM.Count == 0) continue;

                    float tm = Median(truthM), sm = Median(solvedM);
                    log.AppendLine($"  {TierLabel(tier),-8}  {hint,-11}  {truthM.Count,5}   {tm,11:F3}   {sm,12:F3}   " +
                                   $"{sm - tm,6:F3}   {Median(truthB),8:F3}   {Median(solvedB),9:F3}");
                }
            }

            log.AppendLine();
            log.AppendLine("  NOTE: the solved pose differs from truth at the LEFT elbow and LEFT knee only -- those are");
            log.AppendLine("  the only solved joints BasisMocapAccuracy exposes. Bound on what that can move the whole-body");
            log.AppendLine("  centre of mass: 0.070 x knee error + 0.021 x elbow error. A small delta here is a statement");
            log.AppendLine("  about the harness's plumbing, not a clean bill of health for the solver.");
            TestContext.WriteLine(log.ToString());

            Assert.That(deltas.Count, Is.GreaterThan(0), "no clip produced both a truth and a solved balance measurement");

            // The one thing assertable without a settled baseline, and it is a HARNESS check rather than a
            // solver one: substituting two joints cannot move the centre of mass further than those two joints'
            // mass fractions allow. If it does, the substitution is landing on the wrong joints.
            const float knee = BasisBodyBalance.MassThigh * BasisBodyBalance.ComThigh
                               + BasisBodyBalance.MassShank * (1f - BasisBodyBalance.ComShank);
            const float elbow = BasisBodyBalance.MassUpperArm * BasisBodyBalance.ComUpperArm
                                + BasisBodyBalance.MassForearm * (1f - BasisBodyBalance.ComForearm);
            // A metre of joint error is far beyond anything the solver could produce; this is a ceiling, not a gate.
            float ceiling = (knee + elbow) * 1.0f / 0.15f;

            float worst = 0f;
            foreach (float d in deltas) worst = Mathf.Max(worst, Mathf.Abs(d));
            Assert.That(worst, Is.LessThan(ceiling),
                $"substituting the solved elbow and knee moved the balance margin by {worst:F3} foot lengths, which " +
                $"exceeds what those two segments' masses ({knee:F4} + {elbow:F4} of body mass) could possibly do. " +
                "The solved joints are being written to the wrong slots.");
        }

        static float Median(List<float> v)
        {
            if (v.Count == 0) return float.NaN;
            var s = new List<float>(v);
            s.Sort();
            return s[s.Count / 2];
        }

        /// <summary>A dynamically similar copy: lengths scaled by k, time by sqrt(k). That pairing is what
        /// keeps v/sqrt(gL) -- and therefore contact detection and the quasi-static classification -- identical,
        /// which is the only form of "scale it up" this metric is entitled to be exactly invariant to.</summary>
        static BasisMotionClip FroudeScaled(BasisMotionClip clip, float k)
        {
            var poses = new BasisMocapPose[clip.Poses.Length];
            for (int i = 0; i < poses.Length; i++)
            {
                poses[i] = clip.Poses[i];
                poses[i].Position *= k;   // rotations are scale-free, as in BasisBvhLoader.Rescale
            }
            return new BasisMotionClip
            {
                Name = $"{clip.Name}_x{k}",
                FrameTime = clip.FrameTime * Mathf.Sqrt(k),
                FrameCount = clip.FrameCount,
                Poses = poses,
                SourceToMetres = clip.SourceToMetres * k,
            };
        }

        /// <summary>The body's facing in the ground plane, from the hip sockets. Unity is left-handed, so
        /// forward = Cross(right, up), which in the (x, z) plane is (x, z) -> (-z, x).</summary>
        static Vector3 BodyForward(BasisMotionClip clip, int frame)
        {
            Vector3 hip = clip.Get(frame, BasisMocapJoint.RightUpperLeg).Position
                        - clip.Get(frame, BasisMocapJoint.LeftUpperLeg).Position;
            var right = new Vector2(hip.x, hip.z);
            if (right.sqrMagnitude < 1e-8f) return Vector3.forward;
            right.Normalize();
            return new Vector3(-right.y, 0f, right.x);
        }
    }
}
