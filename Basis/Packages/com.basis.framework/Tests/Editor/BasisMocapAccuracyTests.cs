using System.Collections.Generic;
using System.IO;
using System.Text;
using Basis.IK.Mocap;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Accuracy of the IK against REAL HUMAN MOTION.
    ///
    /// Every other IK gate in this project asks "is the solve plausible" -- no pops, no inversions, smooth,
    /// self-consistent. None of them asks "is it RIGHT", because until now there was nothing to be right about.
    /// This one has a ground truth: CMU motion capture, where a real person's elbow and knee were actually
    /// measured. Feed the solver only what a VR user is tracked at (hand and foot poses), and compare the joints
    /// it had to INVENT -- elbow, knee -- against where that human's really were.
    ///
    /// The three hint sources answer three different questions:
    ///   TruthJoint -- hand the solver the real elbow (i.e. an elbow tracker). The accuracy CEILING, and a
    ///                 wiring proof: if this is not ~0 the harness is lying and no other number here counts.
    ///   Lookup     -- WHAT ACTUALLY SHIPS for an untracked arm (ArmBendFrame -> BasisArmBendLookup ->
    ///                 chicken-wing flare). This is the number that matters.
    ///   None       -- the bare two-bone fallback with no pole guidance. The FLOOR: how much the lookup buys.
    ///
    /// The gap between Lookup and TruthJoint is the addressable error -- what an elbow tracker would win you,
    /// and what a lookup table refit from this same data could close for free.
    ///
    /// ── AND THE THREE THAT PUT A DEVICE IN FRONT OF THE JOINT ────────────────────────────────────────
    ///   TrackerGood / TrackerTypical / TrackerPoor -- the SAME tracker case as TruthJoint, driven through
    ///   BasisSyntheticTracker: a puck that stands off the bone, is strapped on at whatever angular station
    ///   the user managed, slips over a session, jitters, glitches, drops out and arrives late.
    ///
    /// TruthJoint is not a tracker, it is the JOINT, and that fiction has already cost this project a shipped
    /// bug: the row sat at 1.06% and was quoted as the achievable ceiling until it emerged the solver was
    /// silently DISCARDING the tracker through two fades and a boolean pole gate. Fixed, it went to 0.00%.
    /// The lesson on record -- WHEN A "KNOWN CEILING" SURVIVES EVERY IMPROVEMENT, SUSPECT IT IS A BUG, NOT A
    /// LIMIT -- is unenforceable from a single row, because a discarded tracker is not visibly wrong on its
    /// own; it just quietly reads like the no-tracker fallback. It is only visible ACROSS rows, which is what
    /// TheTrackerLadder_DegradesMonotonically below exists to say out loud.
    ///
    /// Corpus: CMU Graphics Lab Motion Capture Database (mocap.cs.cmu.edu), BVH conversion by Bruce Hahne.
    /// CMU places no restrictions on use. Files live in Tests/MocapCorpus~/ -- the trailing '~' keeps Unity from
    /// importing them as assets (no .meta churn). Drop more .bvh in there and they are picked up automatically.
    /// </summary>
    public class BasisMocapAccuracyTests
    {
        static string CorpusDir => Path.GetFullPath("Packages/com.basis.framework/Tests/MocapCorpus~");

        static List<BasisMotionClip> LoadCorpus()
        {
            var clips = new List<BasisMotionClip>();
            if (!Directory.Exists(CorpusDir)) return clips;

            string[] files = Directory.GetFiles(CorpusDir, "*.bvh");
            System.Array.Sort(files);
            foreach (string f in files)
            {
                Assert.That(BasisBvhLoader.TryLoad(f, out BasisMotionClip clip, out string err), Is.True,
                    $"failed to load {Path.GetFileName(f)}: {err}");
                clips.Add(clip);
            }
            return clips;
        }

        static List<BasisMotionClip> RequireCorpus()
        {
            List<BasisMotionClip> clips = LoadCorpus();
            if (clips.Count == 0)
            {
                Assert.Ignore($"no mocap corpus: drop CMU .bvh files into {CorpusDir}");
            }
            return clips;
        }

        /// <summary>
        /// Handedness is THE classic BVH bug and it is silent: BVH is right-handed, Unity is left-handed, and a
        /// bad conversion mirrors the skeleton so left and right swap. Every chirality-sensitive number the
        /// harness reports would be quietly wrong. So prove the loaded human is anatomically the right way round
        /// before trusting a single measurement taken from it.
        /// </summary>
        [Test]
        public void Corpus_LoadsAsAnAnatomicallySaneHuman()
        {
            foreach (BasisMotionClip clip in RequireCorpus())
            {
                Assert.That(BasisBvhLoader.Validate(clip, out string why), Is.True, $"{clip.Name}: {why}");

                float arm = Vector3.Distance(clip.Get(0, BasisMocapJoint.LeftUpperArm).Position, clip.Get(0, BasisMocapJoint.LeftLowerArm).Position)
                          + Vector3.Distance(clip.Get(0, BasisMocapJoint.LeftLowerArm).Position, clip.Get(0, BasisMocapJoint.LeftHand).Position);
                // Normalised to an 0.85 m leg, so the arm must land in adult range or the scaling is off.
                Assert.That(arm, Is.InRange(0.40f, 0.80f), $"{clip.Name}: arm length {arm:F2} m is not human after scaling");
            }
        }

        /// <summary>
        /// The wiring proof. Hand the solver the real elbow and knee; it must put them essentially there. If this
        /// fails, the harness is not driving the solve and every accuracy number below is meaningless.
        /// </summary>
        [Test]
        public void GivenTheTrueJoint_TheSolverReproducesIt()
        {
            var failures = new List<string>();
            foreach (BasisMotionClip clip in RequireCorpus())
            {
                string csv = Path.Combine(Application.persistentDataPath, "MocapAccuracy", $"{clip.Name}_TruthJoint.csv");
                BasisMocapAccuracySummary s = BasisMocapAccuracy.Run(clip, BasisMocapHintSource.TruthJoint, csv);
                (bool pass, string reason) = BasisMocapAccuracy.Gate(s);
                TestContext.WriteLine($"  [{(pass ? "ok" : "FAIL")}] {clip.Name}: {reason}");
                // Collect rather than abort, so every clip's CSV is written and the whole picture is visible.
                if (!pass) failures.Add($"{clip.Name}: {reason}");
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        /// <summary>
        /// The headline measurement: how far is the avatar's elbow from a real human's, on real motion, using the
        /// elbow the product actually ships (the bend lookup)? Reported, not asserted, on first run -- there is no
        /// honest threshold until the number exists. Once it has settled, gate it here and it becomes a ratchet.
        ///
        /// The three synthetic-device rows ride along here so every row's number is printed in ONE table and can
        /// be read against its neighbours. Their VERDICT (BasisMocapAccuracy.Gate) is printed per row and the
        /// failures are listed under the table, but it is deliberately NOT asserted -- see the footer note below.
        /// The assertion that actually guards these rows is TheTrackerLadder_DegradesMonotonically.
        /// </summary>
        [Test]
        public void ShippedElbow_MeasuredAgainstRealHumans()
        {
            var log = new StringBuilder("\nIK accuracy vs real human motion (CMU mocap, joint error in cm)\n");
            log.AppendLine("  clip          hint              elbow mean   p95     max    (% of arm)   knee mean   pops(e/k)   gate");

            var lookupElbow = new List<float>();
            var truthElbow = new List<float>();
            var effectorMisses = new List<string>();
            var gateFailures = new List<string>();
            float footSlip = 0f;

            foreach (BasisMotionClip clip in RequireCorpus())
            {
                foreach (BasisMocapHintSource hint in new[]
                {
                    BasisMocapHintSource.None, BasisMocapHintSource.Lookup, BasisMocapHintSource.TruthJoint,
                    // The device rows. Appended rather than interleaved so the legacy rows keep the order every
                    // recorded baseline was read in.
                    BasisMocapHintSource.TrackerGood, BasisMocapHintSource.TrackerTypical, BasisMocapHintSource.TrackerPoor,
                })
                {
                    string csv = Path.Combine(Application.persistentDataPath, "MocapAccuracy", $"{clip.Name}_{hint}.csv");
                    BasisMocapAccuracySummary s = BasisMocapAccuracy.Run(clip, hint, csv);
                    Assert.That(s.Ok, Is.True, $"{clip.Name} [{hint}]: {s.Error}");

                    (bool pass, string reason) = BasisMocapAccuracy.Gate(s);
                    if (!pass) gateFailures.Add($"{clip.Name} [{hint}]: {reason}");

                    log.AppendLine($"  {clip.Name,-12}  {hint,-15}  {s.ElbowMeanM * 100f,8:F1}  {s.ElbowP95M * 100f,6:F1}  {s.ElbowMaxM * 100f,6:F1}   " +
                                   $"{s.ElbowMeanFracArm * 100f,7:F1}%   {s.KneeMeanM * 100f,8:F1}     {s.ElbowPops}/{s.KneePops}   {(pass ? "ok" : "FAIL")}");

                    if (hint == BasisMocapHintSource.Lookup) lookupElbow.Add(s.ElbowMeanM);
                    if (hint == BasisMocapHintSource.TruthJoint) truthElbow.Add(s.ElbowMeanM);

                    // The hand is commanded and the arm solve is reach-preserving, so it must be hit. The FOOT is
                    // a different matter: the leg hint is not reach-preserving, so foot slip is a measured solver
                    // property, reported in the table above rather than treated as a harness fault.
                    if (s.HandMaxM > 0.01f) effectorMisses.Add($"{clip.Name} [{hint}]: hand missed by {s.HandMaxM * 100f:F1} cm");
                    footSlip = Mathf.Max(footSlip, s.FootMaxM);
                }
            }

            log.AppendLine($"\n  worst foot slip across every row above: {footSlip * 1000f:F1} mm");

            if (gateFailures.Count > 0)
            {
                // REPORTED, NOT ASSERTED, and the reason is specific rather than a shrug.
                //
                // Gate's FootMaxM clause (> 2 mm) fires on any row that drives the knee from a pole, because the
                // leg hint is documented as NOT reach-preserving. That is the SAME pre-existing failure
                // GivenTheTrueJoint_TheSolverReproducesIt already carries (worst ~51.9 mm on clip 69_70), and the
                // device rows drive the knee through byte-identical wiring, so hard-asserting here would clone one
                // known failure into a second test and report a leg-hint property as a tracker-row fault.
                //
                // ⚠ RATCHET GOES HERE. The moment the knee-hint foot slide is fixed, promote this to
                // Assert.That(gateFailures, Is.Empty, ...) -- Gate's tracker-ceiling clause is already derived
                // from the device parameters (BasisMocapAccuracy.TrackerCeilingFracOfLimb) and needs no tuning.
                log.AppendLine($"\n  GATE FAILURES ({gateFailures.Count}) -- reported, not asserted; see the note in this test:");
                foreach (string f in gateFailures) log.AppendLine($"    {f}");
            }

            log.AppendLine($"\n  CSVs: {Path.Combine(Application.persistentDataPath, "MocapAccuracy")}");
            TestContext.WriteLine(log.ToString());

            Assert.That(effectorMisses, Is.Empty, "the solver did not reach targets it was handed:\n" + string.Join("\n", effectorMisses));
            Assert.That(lookupElbow.Count, Is.GreaterThan(0), "no clips measured");

            // The one thing we can assert without a settled baseline: the shipped lookup must not be WORSE than
            // simply handing the solver the true elbow. If it were, the lookup would be actively harmful.
            float lookupMean = Mean(lookupElbow);
            float truthMean = Mean(truthElbow);
            Assert.That(lookupMean, Is.GreaterThanOrEqualTo(truthMean - 1e-4f),
                $"the shipped lookup elbow ({lookupMean * 100f:F1} cm) beat the TRUE elbow ({truthMean * 100f:F1} cm) -- impossible, the harness is wrong");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // THE MOUNT-QUALITY LADDER -- the cross-row properties Gate cannot see.
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Best mount first, worst last, bracketed by the two CONTROLS the device rows must sit between:
        /// TruthJoint (a perfect pole, no device at all) and None (no pole at all). The ladder is the assertion --
        /// each rung is only meaningful relative to its neighbours.
        /// </summary>
        static readonly BasisMocapHintSource[] k_MountLadder =
        {
            BasisMocapHintSource.TruthJoint,
            BasisMocapHintSource.TrackerGood,
            BasisMocapHintSource.TrackerTypical,
            BasisMocapHintSource.TrackerPoor,
            BasisMocapHintSource.None,
        };

        /// <summary>
        /// A FLOAT-NOISE FLOOR, NOT A TUNED THRESHOLD, and the distinction matters because this file has no
        /// honest thresholds yet. 0.1 mm is far below anything the device model can produce -- the rungs differ
        /// by 3 deg vs 8 deg vs 15 deg of mount roll, which on a joint circle of ~0.15 m is centimetres. Two rungs
        /// landing inside this of each other are not "close", they are the SAME ANSWER, and that is a structural
        /// fact rather than a judgement about how much error is acceptable.
        /// </summary>
        const float k_LadderNoiseM = 1e-4f;

        /// <summary>
        /// THE CROSS-ROW ASSERTION, AND THE ONLY ONE THAT CAN CATCH A DISCARDED TRACKER.
        ///
        /// BasisMocapAccuracy.Gate sees one summary at a time and therefore structurally cannot express this:
        ///
        ///     TruthJoint &lt;= TrackerGood &lt;= TrackerTypical &lt;= TrackerPoor &lt;= None
        ///
        /// Every rung down that ladder gives the solver a WORSE pole than the one above -- a perfect joint, then
        /// a careful mount, then a real user's strap, then a sloppy one with a 10% glitch rate and real dropout,
        /// then nothing at all. The solved elbow and knee must get worse in step. Nothing here is a tuned number:
        /// it is an ORDERING, which is a property of the arrangement rather than of the corpus.
        ///
        /// WHY THIS IS THE TEST THAT MATTERS. TruthJoint sat at "1.06%" and was quoted as the accuracy ceiling
        /// while the solver was in fact throwing the tracker away through two fades and a boolean pole gate;
        /// fixed, it went to 0.00%. A discarded tracker is invisible from any single row -- it just reads like a
        /// plausible fallback number. Across rows it is unmistakable: the device differences never reach the
        /// solver, so the three tracker rows COLLAPSE onto each other and onto None together. So the ladder is
        /// checked for TWO distinct failures and they are reported apart, because they mean different things:
        ///   ORDER INVERTED -- a worse mount produced a BETTER joint. No device model can do that.
        ///   COLLAPSED      -- two rungs are indistinguishable. The solver is not reading the difference.
        ///
        /// POOLED, NOT PER-CLIP, and deliberately so. A single clip's mean is a noisy estimator: a clip that
        /// never bends its elbow past the mount-roll signal, or one where a glitch happens to fire in a pose the
        /// solver was already wrong about, can order two adjacent rungs backwards without anything being broken.
        /// The ladder is a claim about the DEVICE MODEL, not about clip 69_70, so it is asserted on the corpus
        /// aggregate and the per-clip agreement count is printed alongside as the diagnostic -- "14/20 clips
        /// ordered" localises a marginal rung, "3/20" says the pooled pass was luck. Pooling is the UNWEIGHTED
        /// mean of per-clip means, matching ShippedElbow_MeasuredAgainstRealHumans, so that the 1368-frame clip
        /// does not outvote the 149-frame one; the corpus is a sample of motions, not of frames.
        ///
        /// ⚠ IF THIS FAILS, IT IS A FINDING. Do not reorder the ladder and do not widen k_LadderNoiseM to make
        /// it green -- the ordering is what is under test, and a test loosened until it passes is worse than no
        /// test. This project has already shipped a pop detector that read the same count whether its cause was
        /// present or absent, and a smoothness metric that scored over-smoothed mush above a real human.
        ///
        /// Headroom: UNMEASURED. This test asserts no magnitude, only the ordering and the separation. ⚠ RATCHET
        /// GOES HERE once the numbers settle: pin the TrackerTypical elbow figure, which is the one a shipping
        /// claim about elbow trackers would actually rest on.
        /// </summary>
        [Test]
        public void TheTrackerLadder_DegradesMonotonically()
        {
            List<BasisMotionClip> clips = RequireCorpus();
            int rungs = k_MountLadder.Length;

            var elbow = new List<float>[rungs];
            var knee = new List<float>[rungs];
            var elbowP95 = new List<float>[rungs];
            var kneeP95 = new List<float>[rungs];
            var elbowPop = new List<float>[rungs];
            var kneePop = new List<float>[rungs];
            for (int r = 0; r < rungs; r++)
            {
                elbow[r] = new List<float>(); knee[r] = new List<float>();
                elbowP95[r] = new List<float>(); kneeP95[r] = new List<float>();
                elbowPop[r] = new List<float>(); kneePop[r] = new List<float>();
            }

            foreach (BasisMotionClip clip in clips)
            {
                for (int r = 0; r < rungs; r++)
                {
                    // No CSV path: ShippedElbow_MeasuredAgainstRealHumans already writes every one of these, and
                    // this test wants only the two scalars.
                    BasisMocapAccuracySummary s = BasisMocapAccuracy.Run(clip, k_MountLadder[r], null);
                    Assert.That(s.Ok, Is.True, $"{clip.Name} [{k_MountLadder[r]}]: {s.Error}");
                    elbow[r].Add(s.ElbowMeanM);
                    knee[r].Add(s.KneeMeanM);
                    // TAIL and POPS are diagnostics only, never asserted. The mean is what the ladder claim rests
                    // on, but a mount can leave the mean ordered while inverting the tail a user actually sees, and
                    // a pop is a discontinuity rather than an offset -- a different failure the mean cannot show.
                    elbowP95[r].Add(s.ElbowP95M);
                    kneeP95[r].Add(s.KneeP95M);
                    elbowPop[r].Add(s.ElbowPops);
                    kneePop[r].Add(s.KneePops);
                }
            }

            var elbowMean = new float[rungs];
            var kneeMean = new float[rungs];
            var elbowP95Mean = new float[rungs];
            var kneeP95Mean = new float[rungs];
            var elbowPopMean = new float[rungs];
            var kneePopMean = new float[rungs];
            for (int r = 0; r < rungs; r++)
            {
                elbowMean[r] = Mean(elbow[r]); kneeMean[r] = Mean(knee[r]);
                elbowP95Mean[r] = Mean(elbowP95[r]); kneeP95Mean[r] = Mean(kneeP95[r]);
                elbowPopMean[r] = Mean(elbowPop[r]); kneePopMean[r] = Mean(kneePop[r]);
            }

            var log = new StringBuilder($"\n  == MOUNT QUALITY LADDER (pooled over {clips.Count} clips, unweighted mean of per-clip means) ==\n");
            log.AppendLine("  worse mount -> worse joint. 'ordered' counts the clips that agree with the rung above it.");
            log.AppendLine();
            log.AppendLine("  rung              elbow mean   ordered      knee mean   ordered");
            log.AppendLine("  ---------------   ----------   --------     ---------   --------");
            for (int r = 0; r < rungs; r++)
            {
                string eOrd = r == 0 ? "--" : $"{Ordered(elbow[r - 1], elbow[r])}/{clips.Count}";
                string kOrd = r == 0 ? "--" : $"{Ordered(knee[r - 1], knee[r])}/{clips.Count}";
                log.AppendLine($"  {k_MountLadder[r],-15}   {elbowMean[r] * 100f,7:F2} cm   {eOrd,-8}     {kneeMean[r] * 100f,6:F2} cm   {kOrd,-8}");
            }
            log.AppendLine();
            log.AppendLine("  TAIL AND POPS (diagnostic, not asserted -- the mean can stay ordered while these invert)");
            log.AppendLine("  rung              elbow p95    knee p95     elbow pops   knee pops");
            log.AppendLine("  ---------------   ----------   ----------   ----------   ---------");
            for (int r = 0; r < rungs; r++)
            {
                log.AppendLine($"  {k_MountLadder[r],-15}   {elbowP95Mean[r] * 100f,7:F2} cm   {kneeP95Mean[r] * 100f,7:F2} cm   " +
                               $"{elbowPopMean[r],8:F1}     {kneePopMean[r],7:F1}");
            }
            TestContext.WriteLine(log.ToString());

            var breaks = new List<string>();
            for (int r = 0; r + 1 < rungs; r++)
            {
                CheckRung(breaks, "elbow", k_MountLadder[r], k_MountLadder[r + 1], elbowMean[r], elbowMean[r + 1]);
                CheckRung(breaks, "knee", k_MountLadder[r], k_MountLadder[r + 1], kneeMean[r], kneeMean[r + 1]);
            }

            Assert.That(breaks, Is.Empty,
                "the mount-quality ladder is broken. A worse tracker must produce a worse joint, and two rows " +
                "that differ by centimetres of mount error must not produce the same answer:\n" +
                string.Join("\n", breaks) + "\n\n" + log);
        }

        /// <summary>How many clips put `worse` at or above `better`. A diagnostic, not a gate -- the assertion is
        /// on the pooled means, because per-clip ordering is expected to be noisy.</summary>
        static int Ordered(List<float> better, List<float> worse)
        {
            int n = 0;
            for (int i = 0; i < better.Count && i < worse.Count; i++) if (worse[i] >= better[i]) n++;
            return n;
        }

        static void CheckRung(List<string> breaks, string joint,
                              BasisMocapHintSource better, BasisMocapHintSource worse, float betterM, float worseM)
        {
            float d = worseM - betterM;
            if (d < -k_LadderNoiseM)
            {
                breaks.Add($"ORDER INVERTED [{joint}] {worse} ({worseM * 100f:F2} cm) BEAT {better} ({betterM * 100f:F2} cm) " +
                           $"by {-d * 100f:F2} cm. A worse mount produced a better joint, which no device model can do -- " +
                           "either the solver is mishandling tracker input, or a row is not wired to the mount it names.");
            }
            else if (d <= k_LadderNoiseM)
            {
                breaks.Add($"COLLAPSED [{joint}] {better} ({betterM * 100f:F2} cm) and {worse} ({worseM * 100f:F2} cm) are " +
                           $"indistinguishable -- {d * 1000f:F4} mm apart. These two inputs differ by centimetres of mount " +
                           "error, so the difference is not reaching the solve. THIS IS THE DISCARDED-TRACKER SIGNATURE: " +
                           "it is exactly how the 1.06% 'ceiling' stayed wrong, and it is why this test exists.");
            }
        }

        /// <summary>
        /// The device model is SEEDED -- FNV-1a over (clip name, side, limb), fed to SplitMix64 -- so the same
        /// clip must replay with the same mount error, the same slip phases, the same glitch frames and the same
        /// dropouts. Two Run calls must therefore agree BIT-EXACTLY, and that is asserted with no tolerance at
        /// all: a tolerance here would be admitting the number moves, and a number that moves run to run cannot
        /// be bisected, cannot be ratcheted, and is not a measurement. (BasisSyntheticTrackerTests already proves
        /// the RNG is reproducible in isolation; this proves the harness around it did not reintroduce ambient
        /// state -- a UnityEngine.Random draw, a hash-code seed, a static carried between rows.)
        ///
        /// The last assertion is the NEGATIVE CONTROL and it is not decoration. "Two runs agree" is trivially
        /// satisfied by a harness that ignores the row it was handed, which is a real failure mode here: this
        /// project has already shipped a pop detector that reported an identical count whether its supposed cause
        /// was present or absent. So a good puck and a poor one must NOT agree.
        /// </summary>
        [Test]
        public void TheSameTrackerRow_RunTwice_ReturnsTheSameNumbers()
        {
            List<BasisMotionClip> clips = RequireCorpus();

            // Three clips, not the whole corpus: determinism is a property of the mechanism, and every clip
            // exercises the identical code with a different seed. All three ROWS are swept, though, because they
            // differ in the stateful paths most likely to leak ambient state -- PoorPuck alone carries a 1%
            // dropout and a 10% glitch rate.
            int take = Mathf.Min(3, clips.Count);
            var mismatches = new List<string>();
            var blind = new List<string>();
            var log = new StringBuilder("\n  tracker determinism: two Run calls per (clip, row)\n");

            for (int c = 0; c < take; c++)
            {
                BasisMotionClip clip = clips[c];
                float good = float.NaN, poor = float.NaN;

                foreach (BasisMocapHintSource hint in new[]
                {
                    BasisMocapHintSource.TrackerGood, BasisMocapHintSource.TrackerTypical, BasisMocapHintSource.TrackerPoor,
                })
                {
                    BasisMocapAccuracySummary a = BasisMocapAccuracy.Run(clip, hint, null);
                    BasisMocapAccuracySummary b = BasisMocapAccuracy.Run(clip, hint, null);
                    Assert.That(a.Ok && b.Ok, Is.True, $"{clip.Name} [{hint}]: {a.Error} / {b.Error}");

                    Identical(mismatches, clip.Name, hint, "frames", a.Frames, b.Frames);
                    Identical(mismatches, clip.Name, hint, "elbow mean", a.ElbowMeanM, b.ElbowMeanM);
                    Identical(mismatches, clip.Name, hint, "elbow p95", a.ElbowP95M, b.ElbowP95M);
                    Identical(mismatches, clip.Name, hint, "elbow max", a.ElbowMaxM, b.ElbowMaxM);
                    Identical(mismatches, clip.Name, hint, "elbow frac arm", a.ElbowMeanFracArm, b.ElbowMeanFracArm);
                    Identical(mismatches, clip.Name, hint, "knee mean", a.KneeMeanM, b.KneeMeanM);
                    Identical(mismatches, clip.Name, hint, "knee p95", a.KneeP95M, b.KneeP95M);
                    Identical(mismatches, clip.Name, hint, "knee max", a.KneeMaxM, b.KneeMaxM);
                    Identical(mismatches, clip.Name, hint, "knee frac leg", a.KneeMeanFracLeg, b.KneeMeanFracLeg);
                    Identical(mismatches, clip.Name, hint, "hand max", a.HandMaxM, b.HandMaxM);
                    Identical(mismatches, clip.Name, hint, "foot max", a.FootMaxM, b.FootMaxM);
                    Identical(mismatches, clip.Name, hint, "rigidity max", a.RigidityMaxM, b.RigidityMaxM);
                    // The POP COUNTS are integers off a threshold, so they are the most brittle thing here and
                    // the first to move if a single frame's arithmetic drifts.
                    Identical(mismatches, clip.Name, hint, "elbow pops", a.ElbowPops, b.ElbowPops);
                    Identical(mismatches, clip.Name, hint, "knee pops", a.KneePops, b.KneePops);

                    if (hint == BasisMocapHintSource.TrackerGood) good = a.ElbowMeanM;
                    if (hint == BasisMocapHintSource.TrackerPoor) poor = a.ElbowMeanM;

                    log.AppendLine($"    {clip.Name,-12} {hint,-15} elbow {a.ElbowMeanM * 100f,6:F2} cm  knee {a.KneeMeanM * 100f,6:F2} cm  " +
                                   $"pops {a.ElbowPops}/{a.KneePops}");
                }

                // Collected rather than asserted here, so the table above still prints when it trips -- the
                // numbers are the whole diagnosis and aborting mid-loop would throw them away.
                if (Mathf.Abs(poor - good) <= k_LadderNoiseM)
                {
                    blind.Add($"  {clip.Name}: a GOOD puck ({good * 100f:F2} cm) and a POOR one ({poor * 100f:F2} cm) produced the " +
                              "same elbow");
                }
            }

            TestContext.WriteLine(log.ToString());

            Assert.That(blind, Is.Empty,
                "'two runs agree' proves nothing on this harness, because it does not distinguish the rows it is " +
                "handed -- a 3-degree mount and a 15-degree one came out the same. Fix this before reading the " +
                "determinism result below it:\n" + string.Join("\n", blind));

            Assert.That(mismatches, Is.Empty,
                "the synthetic tracker is not reproducible -- the same clip and row gave two different answers. " +
                "Something in this path is reading ambient state (UnityEngine.Random, a randomised string hash, a " +
                "static carried between runs) and every number the tracker rows report is unbisectable until it is found:\n" +
                string.Join("\n", mismatches));
        }

        // float.Equals rather than ==, so NaN compares equal to NaN: a NaN that reproduces is a different (and
        // separately gated) problem from an answer that moves, and conflating them would misdiagnose both.
        static void Identical(List<string> to, string clip, BasisMocapHintSource hint, string field, float a, float b)
        {
            if (!a.Equals(b)) to.Add($"  {clip} [{hint}] {field}: {a:R} then {b:R}");
        }

        static void Identical(List<string> to, string clip, BasisMocapHintSource hint, string field, int a, int b)
        {
            if (a != b) to.Add($"  {clip} [{hint}] {field}: {a} then {b}");
        }

        static float Mean(List<float> v) { float t = 0f; foreach (float x in v) t += x; return v.Count > 0 ? t / v.Count : float.NaN; }
    }
}
