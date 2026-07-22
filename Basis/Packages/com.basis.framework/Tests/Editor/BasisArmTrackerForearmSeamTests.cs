using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// ⭐ THE ELBOW-TRACKER FOREARM ROLL SNAPPED 136 DEGREES AT +/-180, AND IT WAS THE LAST OF THE THREE
    /// PRINCIPAL-ANGLE STAGES IN BasisArmSolveCore WITH NO SEAM ENVELOPE.
    ///
    /// ================================================================================================
    /// THE DEFECT. The tracker path ended with
    ///
    ///     rollSigned = sign(roll) * Saturate(|roll|, 90, 120)
    ///
    /// and `roll` is a PRINCIPAL angle. At the wrap the SIGN flips while the MAGNITUDE stays ~180, so the
    /// applied roll jumped 2 * Saturate(180, 90, 120) = 225 deg -- a forearm pose change of 360 - 225 =
    /// 135 deg. MEASURED on the live core through the runtime's own stream composition, elbow tracker
    /// rolled in 0.1 deg steps, at tracker roll 81.7:
    ///
    ///     ForearmRollDeg  +109.4  ->  -114.2     136.39 deg of forearm, ONE STEP
    ///     humerus                                  0.000 deg   (continuous)
    ///     elbow / hand position                    0.0000 mm   (unmoved)
    ///
    /// ⚠️ WHICH IS WHY IT BREAKS THE ARM IN TWO PLACES AT ONCE. The jump is a PURE ROLL about the
    /// forearm's own long axis: the humerus holds still, the hand holds still, and only the bone BETWEEN
    /// them turns -- so the mesh tears at the ELBOW and at the WRIST and nowhere else. It is the same
    /// signature BasisArmForearmSeamSnapTests documents for the NO-TRACKER stage, on the path a user with
    /// an elbow tracker actually takes, and a full turn of the tracker cannot miss it.
    ///
    /// SECOND HOLE, SAME LINES: `roll` is `trackerRoll + TrackerRollHandBlend * d * fade`, a sum of two
    /// principal angles, so it is NOT itself principal -- measured up to 214.9 deg. Past 180 the seam
    /// distance goes negative, the cap declines, AND the saturation is bypassed entirely, so the stage
    /// applied a 214.9 deg roll with no bound of any kind.
    ///
    /// THE FIX, both halves: put `roll` back on its principal branch, then cap the bound's own correction
    /// by the distance to the seam, exactly as the wrist-roll relief's `seam = PI - rollAbs` and the
    /// humeral twist guard's `seam = PI - mag` already do in the same file.
    /// ================================================================================================
    ///
    /// ⚠️ EVERY MEASUREMENT HERE IS TAKEN THROUGH <see cref="BasisArmNet.StreamCompose"/>, the runtime's
    /// own MidDelta / RootDelta / HintDelta / MidPostRoll order, never off the result struct. A gate that
    /// compares two fields the stage never writes is how a 205 mm hand displacement shipped behind a green
    /// suite, and MidPostRoll is precisely a field whose effect the solver's own bookkeeping does not show.
    /// </summary>
    public class BasisArmTrackerForearmSeamTests
    {
        const float Soft = BasisArmSolveCore.TrackerForearmRollSoftDeg;   // 90
        const float Hard = BasisArmSolveCore.TrackerForearmRollMaxDeg;    // 120

        /// <summary>
        /// Where the cap starts to bind, in closed form off the two constants rather than as a literal, so
        /// a retune of the saturation cannot leave this file asserting about the wrong window.
        ///
        /// pull = m - Saturate(m) = e^2/(M + e) with e = m - Soft, M = Hard - Soft; seam = 180 - m. Setting
        /// them equal gives 2e^2 + e(M - P) - P*M = 0 with P = 180 - Soft. At 90/120 that is 144.686 deg.
        /// </summary>
        static float CrossingDeg()
        {
            float M = Hard - Soft;
            float P = 180f - Soft;
            float b = M - P;
            return Soft + (-b + Mathf.Sqrt(b * b + 8f * P * M)) / 4f;
        }

        /// <summary>The applied magnitude the fix promises, from the constants alone: the saturation, eased
        /// out along 2m - 180 once the seam is closer than the correction the saturation wants to make.</summary>
        static float EnvelopeDeg(float magDeg)
        {
            float sat = BasisJointLimitCore.Saturate(magDeg, Soft, Hard);
            float pull = magDeg - sat;
            float seam = 180f - magDeg;
            if (pull > seam) pull = seam;
            if (!(pull > 0f)) pull = 0f;
            return magDeg - pull;
        }

        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        /// <summary>
        /// The tracker rolls; NOTHING ELSE MOVES. The hint POSITION, the target and the animated pose are
        /// all independent of TrackerRollDeg, so the only thing this sweep can change is MidPostRoll -- which
        /// is what makes "the forearm jumped" attributable to this stage and to nothing else.
        ///
        /// `feedTip` false declines the hand blend, leaving roll == trackerRoll exactly: that isolates the
        /// seam cap so its 2x structural bound can be gated strictly. True re-enables the blend and its wrap
        /// fade, which carry their own (pre-existing, and much larger) gain.
        ///
        /// ⚠️ FeedTwistBind is deliberately LEFT OFF. The humeral twist guard carries its OWN seam device
        /// and its own documented relaxation near +/-180; with it fed, a jump here could not be attributed.
        /// </summary>
        static BasisArmNet.Spec Roll(in BasisArmNet.Rig rig, float reach, float el, float handRoll,
                                     float trackerRoll, bool feedTip)
        {
            BasisArmNet.Spec s = BasisArmNet.Default(rig);
            s.TargetDir = Dir(10f, el);
            s.Reach = reach;
            s.HandRollDeg = handRoll;
            s.HintMode = BasisArmNet.HintTracker;
            s.HintAzimuthDeg = 65f;
            s.HintRhoMin = 0.02f;
            s.TrackerRollDeg = trackerRoll;
            s.RefPerp = Vector3.up;
            s.FeedTip = feedTip;
            return s;
        }

        static readonly (float reach, float el)[] k_Rows =
        {
            (0.80f, -30f), (0.90f, -30f), (0.96f, -10f), (0.90f, 20f),
        };

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 1. THE SNAP ITSELF
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ ROLL THE ELBOW TRACKER THROUGH A FULL TURN. THE FOREARM MAY NOT JUMP.
        ///
        /// With the hand blend declined the stage's input is the tracker's own roll, one-for-one, so the
        /// bound here is STRUCTURAL rather than chosen: the applied magnitude is min-capped between a slope
        /// +1 correction and a slope -1 seam, so it can change at most 2x as fast as the input. Nothing else
        /// in the solve moves during this sweep, so the forearm's step IS the applied roll's step.
        ///
        /// Before the fix this measured 136.39 deg at a 0.05 deg step -- 2728x -- at every reach and
        /// elevation tested, which is how you know it is the algebra and not the geometry.
        /// </summary>
        [Test]
        public void TheTrackerForearmRoll_IsContinuousAcrossTheSeam()
        {
            const float k_Step = 0.05f;
            const float k_MaxAmplification = 2.5f;   // structural bound is 2; the rest is float and the sweep grid

            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            var log = new StringBuilder();
            float reachedDemand = 0f, mostApplied = 0f, worstInputStep = 0f;

            foreach ((float reach, float el) in k_Rows)
            {
                Quaternion prevMid = Quaternion.identity, prevHint = Quaternion.identity;
                bool first = true;
                float worst = 0f, atRoll = 0f, worstAfter = 0f;

                for (float t = 0f; t <= 360f + 1e-4f; t += k_Step)
                {
                    BasisArmSolveInput i = BasisArmNet.Build(Roll(rig, reach, el, 0f, t, feedTip: false));
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    BasisArmNet.StreamCompose(i, r, out _, out _, out _, out Quaternion mid);

                    reachedDemand = Mathf.Max(reachedDemand, Mathf.Abs(Mathf.DeltaAngle(0f, r.ForearmRollDemandDeg)));
                    mostApplied = Mathf.Max(mostApplied, Mathf.Abs(r.ForearmRollDeg));

                    if (!first)
                    {
                        // THE HARNESS FIRST: the tracker's own rotation must be sweeping smoothly, or the
                        // discontinuity being measured would be the fixture's.
                        worstInputStep = Mathf.Max(worstInputStep, BasisArmNet.PoseChangeDeg(prevHint, i.HintRotation));

                        float d = BasisArmNet.PoseChangeDeg(prevMid, mid);
                        if (d > worst) { worst = d; atRoll = t; worstAfter = r.ForearmRollDeg; }
                    }
                    prevMid = mid;
                    prevHint = i.HintRotation;
                    first = false;
                }

                log.AppendLine($"      reach {reach:F2} el {el,4:F0}: worst forearm step {worst,7:F4} deg at tracker " +
                               $"roll {atRoll,7:F2} (ForearmRollDeg there {worstAfter,7:F1}), " +
                               $"amplification {worst / k_Step,6:F2}x");

                Assert.That(worst, Is.LessThan(k_MaxAmplification * k_Step),
                    $"reach {reach:0.00} el {el:0}: THE FOREARM JUMPED {worst:0.000} deg in a single {k_Step:0.00} deg " +
                    $"step of ELBOW TRACKER roll, at roll {atRoll:0.00}. `roll` is a PRINCIPAL angle, so +179.99 and " +
                    "-179.99 are the same forearm; sign(roll) * Saturate(|roll|) sends them 2 * 112.5 = 225 deg " +
                    "apart, which is a 135 deg forearm pose change between a still humerus and a still hand -- a " +
                    "tear at the elbow AND at the wrist. See BasisArmSolveCore's seam cap on the tracker roll.");
            }

            // ── NON-VACUITY. Three separate things have to be true or the gate above proves nothing.
            Assert.That(worstInputStep, Is.LessThan(2f * k_Step),
                $"the tracker's OWN rotation moved {worstInputStep:0.000} deg in a {k_Step:0.00} deg step, so the " +
                "fixture is not smooth and the harness would be the discontinuity, not the core.");
            Assert.That(reachedDemand, Is.GreaterThan(178f),
                $"the roll demand only reached {reachedDemand:0.0} deg anywhere in the sweep, so it never visited " +
                "the +/-180 seam and this test cannot have gated it. The sweep is not exercising the defect.");
            Assert.That(mostApplied, Is.GreaterThan(Hard),
                $"the applied roll never exceeded {mostApplied:0.0} deg, i.e. the cap's eased-out region was never " +
                "entered. Below the crossing the fix is the exact identity, so a sweep that stays there is measuring " +
                "the OLD code and would pass with the fix reverted.");

            TestContext.WriteLine($"\n  elbow-tracker roll through a full turn, {k_Step} deg steps " +
                                  $"(hand blend declined; demand reached {reachedDemand:F1} deg, applied peaked at " +
                                  $"{mostApplied:F1} deg):\n" + log);
        }

        /// <summary>
        /// The same sweep with the HAND BLEND live, which is the shipping configuration. The blend and its
        /// wrap fade carry their own gain -- `roll = trackerRoll + 0.5 * d * fade`, and `fade` crushes 1 to 0
        /// over 23 deg of `d`, so 0.5 * |d| * dfade/dd alone reaches ~5.5 -- so this cannot be gated at the
        /// cap's structural 2x. It is gated ABSOLUTELY instead: whatever the composition, a smooth turn of
        /// the tracker may not move the forearm by a degree and a half in a twentieth of a degree.
        /// </summary>
        [Test]
        public void TheTrackerForearmRoll_IsContinuous_WithTheHandBlendLive()
        {
            const float k_Step = 0.05f;
            const float k_MaxJumpDeg = 1.5f;

            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            var log = new StringBuilder();
            float reachedDemand = 0f, offBranch = 0f;

            foreach ((float reach, float el) in k_Rows)
            foreach (float handRoll in new[] { 0f, 90f })
            {
                Quaternion prevMid = Quaternion.identity;
                bool first = true;
                float worst = 0f, atRoll = 0f;

                for (float t = 0f; t <= 360f + 1e-4f; t += k_Step)
                {
                    BasisArmSolveInput i = BasisArmNet.Build(Roll(rig, reach, el, handRoll, t, feedTip: true));
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    BasisArmNet.StreamCompose(i, r, out _, out _, out _, out Quaternion mid);

                    reachedDemand = Mathf.Max(reachedDemand, Mathf.Abs(Mathf.DeltaAngle(0f, r.ForearmRollDemandDeg)));
                    offBranch = Mathf.Max(offBranch, Mathf.Abs(r.ForearmRollDemandDeg));

                    if (!first)
                    {
                        float d = BasisArmNet.PoseChangeDeg(prevMid, mid);
                        if (d > worst) { worst = d; atRoll = t; }
                    }
                    prevMid = mid;
                    first = false;
                }

                log.AppendLine($"      reach {reach:F2} el {el,4:F0} hand {handRoll,3:F0}: worst forearm step " +
                               $"{worst,7:F4} deg at tracker roll {atRoll,7:F2}");

                Assert.That(worst, Is.LessThan(k_MaxJumpDeg),
                    $"reach {reach:0.00} el {el:0} hand roll {handRoll:0}: the forearm jumped {worst:0.000} deg in a " +
                    $"single {k_Step:0.00} deg step of tracker roll (at {atRoll:0.00}), with the hand blend live. " +
                    "Before the seam cap this measured 136.39 deg.");
            }

            Assert.That(reachedDemand, Is.GreaterThan(178f),
                $"the roll demand only reached {reachedDemand:0.0} deg, so the seam was never visited.");
            Assert.That(offBranch, Is.GreaterThan(185f),
                $"the blended demand never left its principal branch (peak |demand| {offBranch:0.0} deg), so this " +
                "sweep does not exercise the wrap and TheAppliedRoll_StaysOnItsPrincipalBranch below would be " +
                "measuring nothing either.");

            TestContext.WriteLine($"\n  elbow-tracker roll, hand blend live, {k_Step} deg steps (pre-wrap demand " +
                                  $"peaked at {offBranch:F1} deg):\n" + log);
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 2. THE SECOND HOLE: A DEMAND THAT IS NOT ON ITS OWN BRANCH
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ `roll` IS A SUM OF TWO PRINCIPAL ANGLES AND SO IS NOT ONE, AND PAST 180 THE BOUND VANISHED.
        ///
        /// seam = PI - |roll| goes NEGATIVE there, so `pull` is rejected by the reject-unless-good test and
        /// the saturation is skipped along with it: the stage applied the raw demand, unbounded. Measured
        /// pre-wrap demands of 214.9 deg on the sweep above, i.e. a 214.9 deg forearm roll out of a stage
        /// whose stated hard bound is 120.
        ///
        /// The gate is the applied roll's own magnitude, read through the stream, against the ONLY bound the
        /// fix claims: the eased-out envelope can reach 180 and no further, because +180 and -180 are the
        /// same rotation and that is where the correction goes to zero.
        /// </summary>
        [Test]
        public void TheAppliedRoll_StaysOnItsPrincipalBranch_AndInsideTheSeamEnvelope()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            float worstApplied = 0f, worstEnvelopeErr = 0f, atDemand = 0f, offBranch = 0f;
            int n = 0, eased = 0;

            foreach ((float reach, float el) in k_Rows)
            foreach (float handRoll in new[] { 0f, 55f, 90f, 160f })
            for (float t = 0f; t <= 360f + 1e-4f; t += 0.25f)
            {
                BasisArmNet.Solve(BasisArmNet.Build(Roll(rig, reach, el, handRoll, t, feedTip: true)),
                                  out BasisArmSolveResult r);
                n++;

                float applied = Mathf.Abs(r.ForearmRollDeg);
                worstApplied = Mathf.Max(worstApplied, applied);
                offBranch = Mathf.Max(offBranch, Mathf.Abs(r.ForearmRollDemandDeg));

                // The applied roll must be EXACTLY the envelope of the demand's own principal branch. That
                // is the whole content of both halves of the fix in one comparison.
                float mag = Mathf.Abs(Mathf.DeltaAngle(0f, r.ForearmRollDemandDeg));
                float err = Mathf.Abs(applied - EnvelopeDeg(mag));
                if (err > worstEnvelopeErr) { worstEnvelopeErr = err; atDemand = r.ForearmRollDemandDeg; }
                if (applied > Hard + 0.5f) eased++;
            }

            Assert.That(n, Is.GreaterThan(20000), $"only {n} poses swept.");
            Assert.That(offBranch, Is.GreaterThan(185f),
                $"the pre-wrap demand never exceeded {offBranch:0.0} deg, so no pose in this sweep is off its " +
                "principal branch and the wrap is untested. The bound below would hold with the wrap deleted.");
            Assert.That(eased, Is.GreaterThan(50),
                $"only {eased} poses entered the cap's eased-out region, so the envelope is barely exercised.");

            Assert.That(worstApplied, Is.LessThan(180f + 0.05f),
                $"the stage applied {worstApplied:0.0} deg of forearm roll. The seam cap's envelope peaks at 180 " +
                "(where +180 and -180 are the same rotation); anything past that means `roll` reached the stage off " +
                "its principal branch, where seam = PI - |roll| is NEGATIVE and takes the saturation down with it.");
            Assert.That(worstEnvelopeErr, Is.LessThan(0.05f),
                $"the applied roll departed from its own closed-form envelope by {worstEnvelopeErr:0.000} deg (at a " +
                $"pre-wrap demand of {atDemand:0.0}). Either the wrap or the cap is not doing what this file says.");

            TestContext.WriteLine($"  {n} poses: worst applied {worstApplied:F2} deg, matches the closed-form " +
                                  $"envelope to {worstEnvelopeErr:F4} deg; pre-wrap demand reached {offBranch:F1} deg; " +
                                  $"{eased} poses in the eased-out region.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 3. THE CAP MUST NOT HAVE EATEN THE ORDINARY REGIME
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ BELOW THE CROSSING THE FIX IS THE EXACT IDENTITY WITH THE OLD SATURATION, AND THIS PROVES IT
        /// WITHOUT THE OLD CODE. `pull = mag - Saturate(mag)` and `seam = 180 - mag` cross at 144.686 deg
        /// (derived here from the two constants, not written down), and below that the min() does not bind.
        /// So every ordinary pronation is bit for bit what it was -- which is what makes this a seam fix
        /// rather than a re-tune of the forearm.
        /// </summary>
        [Test]
        public void BelowTheCrossing_TheAppliedRollIsTheUnchangedSaturation()
        {
            float crossing = CrossingDeg();
            Assert.That(crossing, Is.EqualTo(144.686f).Within(0.01f),
                $"the crossing came out at {crossing:0.000} deg, not the 144.686 this file documents -- the " +
                "saturation constants have moved and every number in these comments is stale.");

            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            float worstErr = 0f, atMag = 0f;
            int n = 0, saturating = 0;

            foreach ((float reach, float el) in k_Rows)
            foreach (float handRoll in new[] { 0f, 90f })
            for (float t = 0f; t <= 360f + 1e-4f; t += 0.25f)
            {
                BasisArmNet.Solve(BasisArmNet.Build(Roll(rig, reach, el, handRoll, t, feedTip: true)),
                                  out BasisArmSolveResult r);

                float mag = Mathf.Abs(Mathf.DeltaAngle(0f, r.ForearmRollDemandDeg));
                if (mag > crossing - 1f) continue;                       // inside the seam window, where it must bind
                if (Mathf.Abs(r.ForearmRollDemandDeg) > 180f) continue;  // the wrap is a deliberate change; not this gate

                float err = Mathf.Abs(Mathf.Abs(r.ForearmRollDeg) - BasisJointLimitCore.Saturate(mag, Soft, Hard));
                if (err > worstErr) { worstErr = err; atMag = mag; }
                if (mag > Soft) saturating++;
                n++;
            }

            Assert.That(n, Is.GreaterThan(5000), $"only {n} poses landed below the crossing; this gate measured little.");
            Assert.That(saturating, Is.GreaterThan(500),
                $"only {saturating} of those poses were past the {Soft:0} deg soft limit, so the saturation curve " +
                "itself is barely covered and 'unchanged' would be a statement about the identity region only.");
            Assert.That(worstErr, Is.LessThan(1e-3f),
                $"below a demand of {crossing:0.00} deg the applied roll departed from the plain saturation by " +
                $"{worstErr:0.00000} deg (at |demand| {atMag:0.0}). The seam cap is only supposed to bind ABOVE the " +
                "crossing; binding here means ordinary pronation has been quietly re-tuned.");

            TestContext.WriteLine($"  {n} sub-crossing poses ({saturating} past the soft limit) match the unchanged " +
                                  $"saturation to {worstErr:0.0000000} deg. Crossing derived at {crossing:F3} deg.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 4. WHERE THE RESIDUAL WENT -- THE GATE THAT MATTERS MOST
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⚠️ THIS REPO'S DOCUMENTED FAILURE MODE IS A CLAMP THAT MOVES THE KINK SOMEWHERE ELSE. The
        /// no-tracker sibling's fix did exactly that once: capping the forearm alone drove the forearm to
        /// 180 deg relative to the humerus at the seam, which is precisely the input a FRACTIONAL twist map
        /// is discontinuous on, and the upper-arm twist bone then jumped 54.10 deg (= 360 * 0.15).
        ///
        /// The tracker cap drives the forearm to the SAME 180 deg at the SAME seam, so it hands
        /// BasisTwistSolveCore the same input. Everything downstream of the forearm is therefore measured
        /// here, through the stream, over the same sweep:
        ///
        ///   * the ELBOW and the HAND -- a roll about the forearm's own long axis pivots on the elbow and
        ///     the hand LIES on that axis, so neither may move at all;
        ///   * the HUMERUS -- entirely upstream of the stage, so it may not jump;
        ///   * the WRIST junction -- the hand is written verbatim from the controller, so whatever the
        ///     forearm does not absorb lands there;
        ///   * both TWIST BONES, driven exactly as BasisFullBodyIK.SolveArmTwist drives them.
        /// </summary>
        [Test]
        public void TheTrackerSeamCap_MovesNoJoint_AndDoesNotRelocateTheJump()
        {
            const float k_Step = 0.05f;
            // The hand blend is live here (this is the shipping configuration), so the wrist junction and
            // the forearm both inherit the blend's own pre-existing gain -- the fade crushes 1 to 0 over
            // 23 deg of `d` -- and are held to the same absolute bound the blend-live sweep uses. The
            // joints below are held far tighter because nothing about the blend can move them at all.
            const float k_MaxRollJumpDeg = 1.5f;
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            float worstElbow = 0f, worstHand = 0f, worstHum = 0f, worstWrist = 0f;
            float worstLowTwist = 0f, worstUpTwist = 0f, atUp = 0f;
            float deepestElbowJunction = 0f, worstWristResidual = 0f;

            foreach ((float reach, float el) in k_Rows)
            {
                Vector3 pElbow = Vector3.zero, pHand = Vector3.zero;
                Quaternion pRoot = Quaternion.identity, pLow = Quaternion.identity, pUp = Quaternion.identity;
                float pWrist = 0f;
                bool first = true;

                for (float t = 0f; t <= 360f + 1e-4f; t += k_Step)
                {
                    BasisArmSolveInput i = BasisArmNet.Build(Roll(rig, reach, el, 0f, t, feedTip: true));
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    BasisArmNet.StreamCompose(i, r, out Vector3 elbow, out Vector3 hand,
                                              out Quaternion root, out Quaternion mid);

                    Vector3 foreSeg = hand - elbow;
                    Vector3 upSeg = elbow - i.Shoulder;
                    if (foreSeg.sqrMagnitude < 1e-10f || upSeg.sqrMagnitude < 1e-10f) continue;
                    Vector3 foreAxis = foreSeg.normalized;

                    // The wrist junction: the hand's axial roll relative to the forearm it hangs off.
                    float wrist = BasisArmNet.TwistDeg(r.TipRotation * Quaternion.Inverse(mid), foreAxis);
                    // The elbow junction: the forearm's axial roll relative to the humerus.
                    float elbowJunction = BasisArmNet.TwistDeg(mid * Quaternion.Inverse(root), foreAxis);
                    deepestElbowJunction = Mathf.Max(deepestElbowJunction, Mathf.Abs(elbowJunction));
                    worstWristResidual = Mathf.Max(worstWristResidual, Mathf.Abs(wrist));

                    Quaternion low = TwistBone(mid, r.TipRotation, foreSeg, 0.5f * 0.5f);
                    Quaternion up = TwistBone(root, mid, upSeg, 0.5f * 0.3f);

                    if (!first)
                    {
                        worstElbow = Mathf.Max(worstElbow, Vector3.Distance(pElbow, elbow));
                        worstHand = Mathf.Max(worstHand, Vector3.Distance(pHand, hand));
                        worstHum = Mathf.Max(worstHum, BasisArmNet.PoseChangeDeg(pRoot, root));
                        worstWrist = Mathf.Max(worstWrist, Mathf.Abs(Mathf.DeltaAngle(pWrist, wrist)));
                        worstLowTwist = Mathf.Max(worstLowTwist, BasisArmNet.PoseChangeDeg(pLow, low));
                        float du = BasisArmNet.PoseChangeDeg(pUp, up);
                        if (du > worstUpTwist) { worstUpTwist = du; atUp = t; }
                    }
                    pElbow = elbow; pHand = hand; pRoot = root; pWrist = wrist; pLow = low; pUp = up;
                    first = false;
                }
            }

            // ── NON-VACUITY: the sweep has to have driven the forearm to the seam, or none of the
            //    downstream stages was ever handed the input that breaks them.
            Assert.That(deepestElbowJunction, Is.GreaterThan(170f),
                $"the forearm never twisted more than {deepestElbowJunction:0.0} deg relative to the humerus, so the " +
                "twist bones were never handed a near-seam child and the gates below prove nothing.");

            Assert.That(worstElbow * 1000f, Is.LessThan(0.05f),
                $"the ELBOW moved {worstElbow * 1000f:0.0000} mm in one {k_Step:0.00} deg step of tracker roll. " +
                "MidPostRoll pivots the forearm about the elbow, so the elbow cannot move however it is composed.");
            Assert.That(worstHand * 1000f, Is.LessThan(0.05f),
                $"the HAND moved {worstHand * 1000f:0.0000} mm in one {k_Step:0.00} deg step. The hand lies ON the " +
                "forearm's long axis; a roll about that axis cannot move it.");
            Assert.That(worstHum, Is.LessThan(0.05f),
                $"the HUMERUS jumped {worstHum:0.0000} deg in one {k_Step:0.00} deg step. The tracker forearm roll " +
                "is entirely downstream of the humerus, so a jump here means the fix has relocated the " +
                "discontinuity onto the upper arm -- this repo's documented way of not fixing something.");
            Assert.That(worstWrist, Is.LessThan(k_MaxRollJumpDeg),
                $"the WRIST junction jumped {worstWrist:0.000} deg in one {k_Step:0.00} deg step. The hand is written " +
                "verbatim from the controller and does not move during this sweep, so the junction's step IS the " +
                "forearm's step -- if the forearm is continuous and this is not, the continuity was bought by " +
                "moving the hand, which this solver may never do. Before the seam cap this measured 136.39 deg.");
            Assert.That(worstUpTwist, Is.LessThan(2f),
                $"the UPPER-ARM TWIST BONE jumped {worstUpTwist:0.000} deg in one {k_Step:0.00} deg step (tracker " +
                $"roll {atUp:0.00}). A fractional twist map is discontinuous at +/-180 for every fraction strictly " +
                "between 0 and 1, and this cap deliberately drives its child to exactly 180 there. " +
                "BasisTwistSolveCore's own seam cap is what stops that becoming a break in the middle of the upper " +
                "arm; the no-tracker sibling measured 54.10 deg here before that cap landed.");
            Assert.That(worstLowTwist, Is.LessThan(2f),
                $"the LOWER-ARM TWIST BONE jumped {worstLowTwist:0.000} deg in one {k_Step:0.00} deg step.");

            TestContext.WriteLine(
                $"  worst single-step across the seam: elbow {worstElbow * 1000f:F5} mm, hand {worstHand * 1000f:F5} mm, " +
                $"humerus {worstHum:F5} deg, wrist junction {worstWrist:F4} deg, twist bones " +
                $"{worstLowTwist:F4} / {worstUpTwist:F4} deg.\n" +
                $"  the residual the forearm does NOT absorb sits in the wrist: worst {worstWristResidual:F1} deg " +
                $"(the forearm reaches {deepestElbowJunction:F1} deg at the seam, where it used to stop at 112.5 and " +
                "hand the difference to the wrist).");
        }

        /// <summary>Drives BasisTwistSolveCore exactly as BasisFullBodyIK.SolveArmTwist does: the live rig's
        /// default fractions (0.5 lower / 0.3 upper) times a twist bone at the segment midpoint.</summary>
        static Quaternion TwistBone(Quaternion parentW, Quaternion childW, Vector3 parentToChild, float fraction)
        {
            BasisTwistSolveInput ti;
            ti.ParentRotation = parentW;
            ti.ChildRotation = childW;
            ti.ParentToChild = parentToChild;
            ti.Fraction = fraction;
            BasisTwistSolveCore.Solve(ti, out BasisTwistSolveResult tr);
            return tr.Apply ? tr.TwistWorldRotation : parentW;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 5. DECLINE
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>A rig that sends no tracker rotation must take the stage's decline branch exactly, so
        /// every existing caller and offline sweep stays bit-identical. Guarded by the SAME field test the
        /// stage uses, and asserted on the applied roll AND on the demand the new field publishes.</summary>
        [Test]
        public void TheStage_DeclinesExactlyWhenTheTrackerSendsNoRotation()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            float worstRoll = 0f, worstDemand = 0f;
            int n = 0;

            foreach ((float reach, float el) in k_Rows)
            foreach (float handRoll in new[] { 0f, 90f, 170f })
            {
                BasisArmNet.Spec s = Roll(rig, reach, el, handRoll, 0f, feedTip: true);
                s.TrackerRollDeg = float.NaN;   // declines HintRotation, exactly as an unbaked rig does
                BasisArmNet.Solve(BasisArmNet.Build(s), out BasisArmSolveResult r);
                worstRoll = Mathf.Max(worstRoll, Mathf.Abs(r.ForearmRollDeg));
                worstDemand = Mathf.Max(worstDemand, Mathf.Abs(r.ForearmRollDemandDeg));
                n++;
            }

            Assert.That(n, Is.GreaterThan(10), $"only {n} poses swept.");
            Assert.That(worstRoll, Is.EqualTo(0f),
                $"with HintRotation left at the struct default the tracker stage still applied {worstRoll:0.000} deg " +
                "of forearm roll. A zero/absent input must decline to the exact previous behaviour.");
            Assert.That(worstDemand, Is.EqualTo(0f),
                $"the stage published a demand of {worstDemand:0.000} deg on a path it declined. A diagnostic that " +
                "reports a number the stage never acted on is the field that makes the next gate vacuous.");

            TestContext.WriteLine($"  {n} declined poses: forearm roll and published demand are exactly 0 on every one.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 6. ⭐ THE ELBOW GUARD'S BRANCH CUT -- WHY THE SAME DEVICE IS *NOT* APPLIED THERE
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ BasisElbowAnatomyCore HAS A DISCONTINUITY OF THE SAME FAMILY AND IT IS DELIBERATELY KEPT.
        /// THIS IS THE MACHINE-CHECKED FORM OF THAT ARGUMENT, so the next person to reach for the seam cap
        /// finds a red test instead of a 24 cm regression.
        ///
        /// The guard back-solves its corrected pole as sG = sign(s) * sqrt(1 - cG^2), and `s` is EXACTLY
        /// zero at the top of the elbow circle -- so a firing guard whose elbow crosses the top throws it to
        /// the other side. See BasisArmInvariantNetKnownOpenDefectTests, which measures it.
        ///
        /// ⚠️ THE OBSTRUCTION IS A MIRROR SYMMETRY, NOT AN OVERSIGHT. At the top the guard's ENTIRE input is
        /// symmetric about the plane spanned by the arm axis and the up direction, and the function has no
        /// other input -- no handedness, no body-lateral axis, no previous frame. So a CONTINUOUS guard must
        /// be equivariant, and its output at the top must lie in that mirror plane: the elbow stays at the
        /// TOP (no correction) or goes to the BOTTOM (a 180 deg swing). Nothing else is available.
        ///
        /// Both are evaluated below against the SHIPPED core's own constants and the SHIPPED core's own
        /// numbers at the same pose, and both are worse:
        ///
        ///   * TOP  -- the guard is the identity exactly where "an elbow cannot point at the sky" is the
        ///             whole point of it. Measured by running it: ten tests red, the elbow 24.0 cm above a
        ///             9.0 cm ceiling.
        ///   * BOTTOM -- the correction becomes 180 deg for ANY firing pose, so it jumps 0 -> 180 as a pose
        ///             crosses the soft margin by a micron: the discontinuity relocated, and enlarged.
        ///
        /// AND THE CUT IS ALREADY WHERE THE POP IS SMALLEST, which is the last assertion here and the one
        /// that is a genuine gate on the core: the jump is 2 * rho * sin(phiG), phiG grows with the elbow's
        /// own azimuth, so the branch belongs at azimuth zero and nowhere else.
        /// </summary>
        [Test]
        public void TheElbowGuardsBranchCut_IsTheLeastBadOfThree()
        {
            // A reachable-and-illegal pose: hand out to the side at shoulder height, elbow at the very top
            // of its circle. This is the "chicken wing" BasisElbowAnatomyTests is built on.
            const float upper = 0.30f, fore = 0.30f, arm = upper + fore;
            Vector3 shoulder = Vector3.zero;
            Vector3 hand = new Vector3(0.45f, 0f, 0.10f);
            Vector3 up = Vector3.up;

            Vector3 ac = hand - shoulder;
            float d = ac.magnitude;
            Vector3 acN = ac / d;
            float p = (upper * upper - fore * fore + d * d) / (2f * d);
            float rho = Mathf.Sqrt(Mathf.Max(upper * upper - p * p, 0f));
            Vector3 upN = (up - acN * Vector3.Dot(up, acN)).normalized;
            Vector3 w = Vector3.Cross(acN, upN);

            Assert.That(rho, Is.GreaterThan(0.05f), $"the elbow circle has collapsed to {rho:0.000} m; no test here.");

            Vector3 ElbowAt(float phiDeg)
            {
                float a = phiDeg * Mathf.Deg2Rad;
                return shoulder + acN * p + (upN * Mathf.Cos(a) + w * Mathf.Sin(a)) * rho;
            }
            float RiseOf(Vector3 e) => Vector3.Dot(e - shoulder, up) - Mathf.Max(0f, Vector3.Dot(ac, up));
            Vector3 Guarded(Vector3 e)
            {
                float sw = BasisElbowAnatomyCore.GuardSwivelRad(shoulder, e, hand, up, arm);
                return sw == 0f ? e : shoulder + Quaternion.AngleAxis(sw * Mathf.Rad2Deg, acN) * (e - shoulder);
            }

            float hardRise = BasisElbowAnatomyCore.HardMarginFracLimb * arm;

            // ── THE FIXTURE MUST BE REACHABLE AND ILLEGAL, or none of this means anything.
            Vector3 top = ElbowAt(0f);
            Assert.That(RiseOf(top), Is.GreaterThan(hardRise),
                $"the top of the elbow circle sits {RiseOf(top) * 100f:0.0} cm above the ceiling, which is not past " +
                $"the {hardRise * 100f:0.0} cm hard limit -- the pose is not illegal and this test proves nothing.");

            // ── (1) THE SHIPPED GUARD. A genuine gate: it must fire AT the top and land under the limit.
            Vector3 shipped = Guarded(top);
            float shippedRise = RiseOf(shipped);
            Assert.That(shippedRise, Is.LessThan(hardRise),
                $"the SHIPPED guard left the elbow {shippedRise * 100f:0.0} cm above the ceiling at the top of its " +
                $"circle, past the {hardRise * 100f:0.0} cm hard limit. If this is red, the sign branch has been " +
                "replaced by something that goes silent at the top -- which is the original 'elbow points at the " +
                "sky' report, reopened. Read BasisElbowAnatomyCore's note at the branch before changing it back.");
            Assert.That(Vector3.Distance(shipped, hand), Is.EqualTo(fore).Within(1e-3f),
                "the guard is a swivel about shoulder->hand; it cannot move the hand.");

            // ── (2) THE 'STAY AT THE TOP' ALTERNATIVE -- capping the swivel by the distance to the branch
            //       point, which is what the two roll stages above do. At the top that distance is |phi| = 0,
            //       so the correction is zero and the elbow is left exactly where it was. The dismissal is
            //       therefore just the fixture's own precondition, re-read: the unguarded top IS illegal.
            Assert.That(RiseOf(top), Is.GreaterThan(hardRise),
                $"the 'cap the swivel by the distance to the branch point' alternative is the IDENTITY at the top " +
                $"(the cap is |phi|, which is zero there), so it leaves the elbow at {RiseOf(top) * 100f:0.0} cm " +
                $"against a {hardRise * 100f:0.0} cm hard limit. Run in full it takes ten tests red and puts the " +
                "elbow 24.0 cm over the ceiling with a mis-strapped tracker.");

            // ── (3) THE 'GO TO THE BOTTOM' ALTERNATIVE. Continuous in the elbow's AZIMUTH, but its
            //       correction at the top is 180 deg for ANY firing pose and 0 for any legal one -- so it is
            //       discontinuous in the POSE instead. Slide the hand down so the ceiling drops past the
            //       elbow's own summit and the pose crosses smoothly from legal-at-the-top to illegal, then
            //       compare the two alternatives' step against the SHIPPED guard's over the same crossing.
            float worstShippedStep = 0f, worstBottomStep = 0f;
            float prevShipped = float.NaN, prevBottom = float.NaN;
            int crossings = 0;
            for (int k = 0; k <= 2000; k++)
            {
                Vector3 h2 = new Vector3(0.45f, Mathf.Lerp(0.32f, 0.16f, k / 2000f), 0.10f);
                Vector3 ac2 = h2 - shoulder;
                float d2 = ac2.magnitude;
                if (!(d2 > 1e-4f) || d2 >= 0.99f * arm) continue;
                float p2 = (upper * upper - fore * fore + d2 * d2) / (2f * d2);
                float rho2 = Mathf.Sqrt(Mathf.Max(upper * upper - p2 * p2, 0f));
                if (!(rho2 > 1e-3f)) continue;
                Vector3 acN2 = ac2 / d2;
                Vector3 upN2 = (up - acN2 * Vector3.Dot(up, acN2)).normalized;
                Vector3 e2 = shoulder + acN2 * p2 + upN2 * rho2;   // the elbow AT THE TOP of its circle

                // the shipped guard, read from the core
                float shippedSwivel = Mathf.Abs(BasisElbowAnatomyCore.GuardSwivelRad(shoulder, e2, h2, up, arm)) * Mathf.Rad2Deg;
                // the 'go to the bottom' alternative: 180 whenever the guard fires at all, 0 otherwise
                float bottomSwivel = shippedSwivel > 0f ? 180f : 0f;

                if (!float.IsNaN(prevShipped))
                {
                    worstShippedStep = Mathf.Max(worstShippedStep, Mathf.Abs(shippedSwivel - prevShipped));
                    float bs = Mathf.Abs(bottomSwivel - prevBottom);
                    if (bs > worstBottomStep) worstBottomStep = bs;
                    if (bs > 0f) crossings++;
                }
                prevShipped = shippedSwivel;
                prevBottom = bottomSwivel;
            }

            Assert.That(crossings, Is.GreaterThan(0),
                "no pose in the sweep crossed the guard's soft margin with the elbow at the top of its circle, so " +
                "the 'go to the bottom' alternative's 0 -> 180 step is not demonstrated and this derivation is " +
                "vacuous. Widen the hand-height sweep.");
            Assert.That(worstShippedStep, Is.LessThan(2f),
                $"the SHIPPED guard's own correction jumped {worstShippedStep:0.00} deg as a pose crossed its soft " +
                "margin at the top of the circle. It is supposed to enter continuously from zero there -- the branch " +
                "cut is in the AZIMUTH channel only, and if it has leaked into the pose channel as well then the " +
                "argument for keeping it no longer holds.");
            Assert.That(worstBottomStep, Is.GreaterThan(90f),
                $"the 'go to the bottom' alternative stepped only {worstBottomStep:0.0} deg across the same crossing, " +
                "so the claim that it merely RELOCATES the discontinuity (and enlarges it) is not demonstrated.");

            // ── (4) ⭐ THE GATE THAT READS THE CORE: THE CUT IS AT THE MINIMUM. The pop at a cut placed at
            //       azimuth phi is 2*rho*sin(phiG(phi)), and phiG is the guarded azimuth. It must be smallest
            //       at phi = 0, or the branch belongs somewhere else.
            float atZero = PopAt(0.01f);
            float worstElsewhere = 0f, atWorst = 0f;
            for (float phi = 1f; phi <= 80f; phi += 0.5f)
            {
                float pop = PopAt(phi);
                if (pop > worstElsewhere) { worstElsewhere = pop; atWorst = phi; }
            }

            float PopAt(float phiDeg)
            {
                Vector3 e = ElbowAt(phiDeg);
                float sw = BasisElbowAnatomyCore.GuardSwivelRad(shoulder, e, hand, up, arm);
                if (sw == 0f) return 0f;
                // the guarded azimuth, and the chord between the two branches that share its magnitude
                float phiG = phiDeg * Mathf.Deg2Rad + sw;
                return 2f * rho * Mathf.Abs(Mathf.Sin(phiG));
            }

            Assert.That(worstElsewhere, Is.GreaterThan(atZero),
                $"the branch pop at azimuth 0 is {atZero * 1000f:0.0} mm and the largest anywhere else in the firing " +
                $"arc is {worstElsewhere * 1000f:0.0} mm (at {atWorst:0.0} deg). The cut is supposed to sit where the " +
                "pop is SMALLEST -- if that is no longer azimuth zero, the guard's saturation has changed shape and " +
                "the branch placement needs re-deriving.");

            TestContext.WriteLine(
                $"  shipped guard at the top of the circle: rise {RiseOf(top) * 100f:F1} cm -> {shippedRise * 100f:F1} cm " +
                $"(hard limit {hardRise * 100f:F1} cm), hand unmoved.\n" +
                $"  branch pop at azimuth 0: {atZero * 1000f:F1} mm; worst elsewhere in the firing arc " +
                $"{worstElsewhere * 1000f:F1} mm at {atWorst:F1} deg -- the cut is at the minimum.\n" +
                "  the two continuous alternatives (stay at the top / go to the bottom) are the ONLY two a\n" +
                "  reflection-equivariant guard can have, and both are worse. See BasisElbowAnatomyCore.");
        }
    }
}
