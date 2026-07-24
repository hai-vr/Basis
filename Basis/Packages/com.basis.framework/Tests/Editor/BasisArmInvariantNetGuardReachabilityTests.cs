using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// EVERY GUARD MUST BE ABLE TO FIRE, AND MUST BE SHOWN TO FIRE, IN THE DOMAIN IT WAS WRITTEN FOR.
    ///
    /// ================================================================================================
    /// THE CLASS THIS CATCHES: A GUARD THAT PROVABLY CANNOT FIRE. The instance was closed-form, not a
    /// tuning accident, and it was invisible for months behind a full suite of green tests:
    ///
    ///   BasisElbowAnatomyCore's only authority is a swivel about shoulder->hand, so the elbow it is asked
    ///   to move travels on a circle of radius rho about that axis. Over the whole circle and every hand
    ///   direction, max(elbow height - ceiling) == rho EXACTLY. So A SOFT MARGIN OF rho OR MORE IS A GUARD
    ///   THAT CAN NEVER FIRE -- not rarely, never.
    ///
    ///   SoftMarginFracLimb was 0.05 of the limb. At MaxElbowAngleDeg = 170 -- THE CAP, i.e. the
    ///   straightest arm this solver will ever produce, and where 41% of the corpus lives -- rho/L is
    ///   0.0436. Measured on the live core: 0 firings out of 22032 poses, with the elbow parked at the very
    ///   TOP of its circle, pointing at the sky. The guard existed, was correct, was tested, and was
    ///   arithmetically incapable of acting exactly where it was needed.
    ///
    /// The first test below is the CLOSED FORM version of that statement, and it takes no solver at all: it
    /// is one inequality over the two constants, checked at every elbow flexion the solver can produce,
    /// against the uncapped margin as its control. It would have caught the defect on the day the cap was
    /// introduced. The rest are empirical reachability sweeps, SEGMENTED rather than pooled -- pooling is
    /// how a 90 deg humeral twist limit passed review while breaching 3% in two separate elevation bands.
    /// ================================================================================================
    /// </summary>
    public class BasisArmInvariantNetGuardReachabilityTests
    {
        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        /// <summary>The elbow's lever arm for segments (u, f) at elbow angle theta. Twice the triangle's
        /// area over the base: rho = u*f*sin(theta)/d. This is the circle the swivel moves the elbow on,
        /// and therefore the entire authority any shoulder->hand guard has.</summary>
        static float Rho(float u, float f, float thetaDeg, out float d)
        {
            float t = thetaDeg * Mathf.Deg2Rad;
            d = Mathf.Sqrt(Mathf.Max(u * u + f * f - 2f * u * f * Mathf.Cos(t), 1e-12f));
            return u * f * Mathf.Sin(t) / d;
        }

        // ============================================================================================
        // 1. ⭐ THE CLOSED FORM. NO SOLVER, ONE INEQUALITY, THE WHOLE DEFECT.
        // ============================================================================================

        /// <summary>
        /// ⭐ THE ELBOW ANATOMY GUARD'S SOFT MARGIN MUST BE STRICTLY INSIDE ITS OWN AUTHORITY, AT EVERY
        /// ELBOW FLEXION THE SOLVER CAN PRODUCE, FOR EVERY SEGMENT RATIO AND EVERY AVATAR SCALE.
        ///
        /// A margin at or above rho is unreachable BY CONSTRUCTION -- max(height - ceiling) over the circle
        /// is exactly rho -- so the guard is silent whatever the pose. The test is therefore not "does it
        /// fire on this sweep" (which depends on the sweep) but "CAN it fire", which does not.
        ///
        /// ⚠️ THE CONTROL IS THE SHIPPED DEFECT ITSELF. The uncapped margin (SoftMarginFracLimb * L, with
        /// no SoftMarginMaxFracRadius cap) is evaluated on the same grid, and it MUST come out unreachable
        /// somewhere -- at theta >= 2*acos(0.1) = 168.522 deg for equal segments. If it does not, this test
        /// has stopped modelling the guard and is worth nothing.
        ///
        /// This is deliberately scale- and ratio-swept: the bound is stated in units of the guard's own
        /// circle, so it holds for a child avatar, a giant, and a rig whose forearm is longer than its
        /// humerus. A future MaxElbowAngleDeg or margin change cannot silently decouple the two constants
        /// again without this going red.
        /// </summary>
        [Test]
        public void ElbowAnatomyGuard_SoftMargin_IsReachable_AtEveryElbowFlexion_ClosedForm()
        {
            var findings = new List<string>();
            var log = new StringBuilder();

            float worstCappedRatio = 0f;       // softRise / rho, capped: must stay < 1
            float worstUncappedRatio = 0f;     // softRise / rho, uncapped: the control, must exceed 1
            float worstCappedAt = 0f, worstUncappedAt = 0f;
            int uncappedUnreachable = 0, samples = 0;

            foreach (float scale in new[] { 0.35f, 1.0f, 2.4f })          // child, adult, giant
            foreach (float ratio in new[] { 0.72f, 0.867f, 1.0f, 1.25f }) // forearm : humerus
            {
                float u = 0.30f * scale;
                float f = u * ratio;
                float L = u + f;

                for (float theta = BasisArmSolveCore.MinElbowAngleDeg; theta <= BasisArmSolveCore.MaxElbowAngleDeg + 1e-4f; theta += 0.25f)
                {
                    float rho = Rho(u, f, theta, out _);
                    samples++;

                    float uncapped = BasisElbowAnatomyCore.SoftMarginFracLimb * L;
                    float capped = Mathf.Min(uncapped, BasisElbowAnatomyCore.SoftMarginMaxFracRadius * rho);

                    float rc = capped / rho, ru = uncapped / rho;
                    if (rc > worstCappedRatio) { worstCappedRatio = rc; worstCappedAt = theta; }
                    if (ru > worstUncappedRatio) { worstUncappedRatio = ru; worstUncappedAt = theta; }
                    if (ru >= 1f) uncappedUnreachable++;

                    if (!(rc < 1f))
                    {
                        findings.Add(
                            $"scale {scale:0.00} ratio {ratio:0.000} theta {theta:0.00} deg: the soft margin is " +
                            $"{capped * 1000f:0.00} mm against an elbow-circle radius of {rho * 1000f:0.00} mm " +
                            $"({rc:0.000} of it). max(elbow height - ceiling) over the circle is EXACTLY rho, so " +
                            "this guard cannot fire at this flexion for any pose whatsoever. That is the 0/22032 " +
                            "defect: the elbow parks at the very top of its circle, pointing at the sky, and the " +
                            "guard returns 0.");
                        break;
                    }
                }
            }

            log.AppendLine($"      capped   worst softRise/rho = {worstCappedRatio:0.0000} at theta {worstCappedAt:0.00} deg  (must be < 1)");
            log.AppendLine($"      UNCAPPED worst softRise/rho = {worstUncappedRatio:0.0000} at theta {worstUncappedAt:0.00} deg  " +
                           $"-- unreachable on {uncappedUnreachable} of {samples} samples  (the shipped defect)");

            BasisArmNet.Report(findings, log, "elbow-anatomy soft-margin reachability (closed form)");

            // ANTI-TAUTOLOGY: the uncapped margin -- the version that shipped -- must still be shown
            // unreachable, or this test has stopped modelling the guard.
            Assert.That(worstUncappedRatio, Is.GreaterThan(1f),
                $"the UNCAPPED soft margin peaks at {worstUncappedRatio:0.000} of the elbow circle's radius, i.e. it " +
                "never becomes unreachable on this grid. This test is supposed to reproduce the shipped defect as " +
                "its control; if it cannot, it is not measuring reachability at all.");
            Assert.That(uncappedUnreachable, Is.GreaterThan(50),
                $"the uncapped margin was unreachable on only {uncappedUnreachable} of {samples} samples; the grid no " +
                "longer covers the near-straight arm where this actually bit.");

            TestContext.WriteLine(
                $"  capped soft margin peaks at {worstCappedRatio:0.000} of the elbow circle (reachable everywhere); " +
                $"the uncapped one reaches {worstUncappedRatio:0.000} and is UNREACHABLE on {uncappedUnreachable}/{samples} " +
                "samples -- which is what shipped.");
        }

        // ============================================================================================
        // 2. AND IT MUST ACTUALLY FIRE, BAND BY BAND, DRIVEN THROUGH THE SHIPPING CORE
        // ============================================================================================

        /// <summary>
        /// The elbow tracker is commanded to the TOP of the elbow circle -- the pose the guard exists to
        /// forbid -- across the whole extension range, and the guard must both FIRE and BOUND, in every band
        /// separately. Segmented, because pooling is how the last unreachable band hid.
        ///
        /// ⚠️ THE CONTROL IS THE SAME SOLVE WITH THE GUARD SWITCHED OFF, not the commanded hint position.
        /// Zeroing BOTH ups makes BasisElbowAnatomyCore decline outright (`!(upSqr > k_SqrEpsilon)`) while
        /// changing nothing else on the tracker path -- PlayerUp's only other consumers are the
        /// straight-arm bend fallback and the pole-collapse stabiliser, and neither runs here. So the
        /// difference between the two solves is the guard and nothing but the guard. Reading the commanded
        /// hint instead would be wrong at high extension, where the tracker's own lever arm has collapsed
        /// further than the solved elbow's: the elbow the guard actually has to pull down is the one the
        /// SOLVE produced, not the one the tracker asked for.
        ///
        /// The bound is the guard's own rational saturation, evaluated here independently of the core:
        /// out = soft + M*e/(M + e), asymptotic to hard, with both margins capped at a fraction of the
        /// circle exactly as the core caps them.
        /// </summary>
        [Test]
        public void ElbowAnatomyGuard_FiresAndBounds_InEveryExtensionBand()
        {
            var findings = new List<string>();
            var log = new StringBuilder();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            float[] bandLo = { 0.50f, 0.70f, 0.85f, 0.92f, 0.96f, 0.99f };
            float[] bandHi = { 0.70f, 0.85f, 0.92f, 0.96f, 0.99f, 1.00f };
            int[] fired = new int[bandLo.Length];
            int[] total = new int[bandLo.Length];
            float[] worstRise = new float[bandLo.Length];
            float[] worstControl = new float[bandLo.Length];
            float[] worstCurveErr = new float[bandLo.Length];

            Vector3 up = Vector3.up;

            for (int b = 0; b < bandLo.Length; b++)
            {
                for (int rk = 0; rk <= 8; rk++)
                {
                    float reach = Mathf.Lerp(bandLo[b], bandHi[b] - 1e-4f, rk / 8f);
                    foreach (float az in new[] { 0f, 90f, 180f, 270f })
                    foreach (float el in new[] { -20f, -8f, 0f, 8f, 20f })
                    {
                        BasisArmNet.Spec s = BasisArmNet.Default(rig);
                        s.TargetDir = Dir(az, el);
                        s.Reach = reach;
                        s.HintMode = BasisArmNet.HintTracker;
                        s.RefPerp = up;                 // azimuth 0 == the TOP of the elbow circle
                        s.HintAzimuthDeg = 0f;
                        s.HintRhoMin = 0f;              // the true elbow position: rho collapses honestly
                        s.FeedTwistBind = true;
                        s.FeedLowerBind = true;

                        BasisArmSolveInput i = BasisArmNet.Build(s);
                        BasisArmNet.Solve(i, out BasisArmSolveResult r);
                        BasisArmNet.StreamCompose(i, r, out Vector3 elbow, out Vector3 hand, out _, out _);

                        // THE CONTROL: identical, with the guard declined.
                        BasisArmSolveInput iu = i;
                        iu.TorsoUp = Vector3.zero;
                        iu.PlayerUp = Vector3.zero;
                        BasisArmNet.Solve(iu, out BasisArmSolveResult ru);
                        BasisArmNet.StreamCompose(iu, ru, out Vector3 elbowU, out Vector3 handU, out _, out _);

                        Vector3 ac = hand - i.Shoulder;
                        if (ac.sqrMagnitude < 1e-8f) continue;
                        Vector3 acN = ac.normalized;
                        Vector3 ae = elbow - i.Shoulder;
                        Vector3 aeU = elbowU - i.Shoulder;
                        float rho = (ae - acN * Vector3.Dot(ae, acN)).magnitude;
                        if (!(rho > 1e-4f)) continue;   // the circle has genuinely collapsed: nothing to guard

                        float ceiling = Mathf.Max(0f, Vector3.Dot(ac, up));
                        float rise = Vector3.Dot(ae, up) - ceiling;
                        float riseU = Vector3.Dot(aeU, up) - ceiling;

                        // the margins, exactly as BasisElbowAnatomyCore caps them
                        float softRise = BasisElbowAnatomyCore.SoftMarginFracLimb * BasisArmNet.Total;
                        float hardRise = BasisElbowAnatomyCore.HardMarginFracLimb * BasisArmNet.Total;
                        float cap = BasisElbowAnatomyCore.SoftMarginMaxFracRadius * rho;
                        if (softRise > cap) { hardRise *= cap / softRise; softRise = cap; }

                        total[b]++;
                        if (riseU > softRise + 1e-5f)
                        {
                            fired[b]++;
                            worstControl[b] = Mathf.Max(worstControl[b], riseU);
                            worstRise[b] = Mathf.Max(worstRise[b], rise);

                            float M = hardRise - softRise;
                            float e = riseU - softRise;
                            float expected = softRise + M * e / (M + e);
                            worstCurveErr[b] = Mathf.Max(worstCurveErr[b], Mathf.Abs(rise - expected));

                            if (!(rise < hardRise + 5e-4f))
                            {
                                findings.Add(
                                    $"reach {reach:0.000} az {az:0} el {el:0}: unguarded the elbow sits " +
                                    $"{riseU * 1000f:0.0} mm above the ceiling and the guarded solve leaves it " +
                                    $"{rise * 1000f:0.0} mm above it, past the {hardRise * 1000f:0.0} mm asymptote. " +
                                    "An elbow cannot rise above the shoulder OR the hand, whichever is higher -- " +
                                    "worst real human violation in 55,140 frames is nine millimetres.");
                            }
                            else if (!(Mathf.Abs(rise - expected) < 1e-3f))
                            {
                                findings.Add(
                                    $"reach {reach:0.000} az {az:0} el {el:0}: the guard landed the elbow " +
                                    $"{rise * 1000f:0.00} mm above the ceiling where its own saturation curve gives " +
                                    $"{expected * 1000f:0.00} mm (unguarded {riseU * 1000f:0.00} mm, rho " +
                                    $"{rho * 1000f:0.0} mm). Either the margins are no longer capped at a fraction " +
                                    "of the circle, or the corrective swivel is not landing where it was solved for.");
                            }
                        }
                        else if (!(rise <= riseU + 1e-4f))
                        {
                            findings.Add($"reach {reach:0.000} az {az:0} el {el:0}: a LEGAL elbow was RAISED by the " +
                                         $"guard, {riseU * 1000f:0.00} -> {rise * 1000f:0.00} mm. Inside the envelope " +
                                         "the guard must be the exact identity.");
                        }
                    }
                }

                log.AppendLine($"      reach [{bandLo[b]:0.00},{bandHi[b]:0.00}): guard fired on {fired[b],4} of {total[b],4} " +
                               $"poses; worst guarded rise {worstRise[b] * 1000f,7:F1} mm vs unguarded " +
                               $"{worstControl[b] * 1000f,7:F1} mm; saturation curve matched to {worstCurveErr[b] * 1000f,7:F3} mm");
            }

            BasisArmNet.Report(findings, log, "elbow-anatomy guard, per extension band");

            // ⭐ REACHABILITY, BAND BY BAND. This is the assertion the 0/22032 defect needed and did not have.
            for (int b = 0; b < bandLo.Length; b++)
            {
                Assert.That(total[b], Is.GreaterThan(0), $"band [{bandLo[b]:0.00},{bandHi[b]:0.00}) was never sampled.");
                Assert.That(fired[b], Is.GreaterThan(0),
                    $"in the extension band [{bandLo[b]:0.00},{bandHi[b]:0.00}) NOT ONE pose put the commanded elbow " +
                    "over the anatomical ceiling, so the guard was never exercised there. That band is unmeasured -- " +
                    "and an unmeasured band is exactly where a guard that cannot fire hides. 41% of the motion corpus " +
                    "is past 0.95 extension.");
            }
        }

        // ============================================================================================
        // 3. THE OTHER GUARDS, SEGMENTED
        // ============================================================================================

        /// <summary>
        /// Each remaining bounded stage must be shown to ENGAGE somewhere in its own domain, per band. A
        /// stage that never engages is either dead code or a decline nobody noticed -- and both look
        /// identical to a suite that only asserts "the output stayed small".
        /// </summary>
        [Test]
        public void EveryBoundedStage_Engages_InItsOwnDomain()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            var log = new StringBuilder();

            // ── humeral twist guard, per elbow-flexion band. The two guards it neighbours are measured about
            //    axes only (180-theta)/2 apart at full extension and ORTHOGONAL at 90, so neither stands in
            //    for the other and both bands must be exercised.
            float[] flexBands = { 170f, 150f, 120f, 90f };
            foreach (float flex in flexBands)
            {
                // reach for a given elbow angle, unequal segments
                float d = Mathf.Sqrt(BasisArmNet.Upper * BasisArmNet.Upper + BasisArmNet.Fore * BasisArmNet.Fore
                                     - 2f * BasisArmNet.Upper * BasisArmNet.Fore * Mathf.Cos(flex * Mathf.Deg2Rad));
                float reach = d / BasisArmNet.Total;

                int fired = 0, n = 0;
                float most = 0f, mostRaw = 0f;
                for (int k = 0; k <= 360; k += 2)
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(20f, -35f);
                    s.Reach = reach;
                    s.HintMode = BasisArmNet.HintTracker;
                    s.HintAzimuthDeg = k;
                    s.RefPerp = Vector3.up;
                    s.FeedTwistBind = true;
                    BasisArmNet.Solve(BasisArmNet.Build(s), out BasisArmSolveResult r);
                    n++;
                    most = Mathf.Max(most, Mathf.Abs(r.HumeralTwistGuardDeg));
                    mostRaw = Mathf.Max(mostRaw, Mathf.Abs(r.HumeralTwistDeg));
                    if (Mathf.Abs(r.HumeralTwistGuardDeg) > 0f) fired++;
                }
                log.AppendLine($"      humeral twist guard @ flexion {flex,5:F0} (reach {reach:0.000}): fired {fired,4}/{n}, " +
                               $"worst correction {most,6:F1} deg, worst raw twist {mostRaw,6:F1} deg");
                Assert.That(fired, Is.GreaterThan(0),
                    $"the humeral twist guard NEVER fired at an elbow flexion of {flex:0} deg while the tracker was " +
                    "swept round a full circle. The raw twist reached " + $"{mostRaw:0.0} deg. Either the guard has " +
                    "silently declined (five bind fields, any one of which turns it off) or the reference axis is " +
                    "parallel to the bone on this rig -- a decline that does not fail loudly.");
            }

            // ── wrist-roll relief.
            {
                int fired = 0, n = 0; float most = 0f;
                for (int k = 0; k <= 360; k += 2)
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(45f, -25f);
                    s.Reach = 0.88f;
                    s.HandRollDeg = k;
                    s.HintMode = BasisArmNet.HintModel;
                    s.FeedTip = true;
                    s.FeedTwistBind = true;
                    BasisArmNet.Solve(BasisArmNet.Build(s), out BasisArmSolveResult r);
                    n++;
                    most = Mathf.Max(most, Mathf.Abs(r.WristReliefDeg));
                    if (Mathf.Abs(r.WristReliefDeg) > 0f) fired++;
                }
                log.AppendLine($"      wrist-roll relief: fired {fired,4}/{n}, worst {most,6:F1} deg");
                Assert.That(fired, Is.GreaterThan(0), "the wrist-roll relief never engaged over a full turn of the controller.");
                Assert.That(most, Is.GreaterThan(5f), $"the relief peaked at only {most:0.0} deg over a full controller turn.");
            }

            // ── tracker forearm roll, and its saturation.
            {
                int fired = 0, saturated = 0, n = 0; float most = 0f, worstDemand = 0f;
                for (int k = 0; k <= 360; k += 2)
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(0f, -40f);
                    s.Reach = 0.85f;
                    s.HintMode = BasisArmNet.HintTracker;
                    s.HintAzimuthDeg = 20f;
                    s.TrackerRollDeg = k;
                    s.RefPerp = Vector3.up;
                    s.FeedTip = true;
                    BasisArmNet.Solve(BasisArmNet.Build(s), out BasisArmSolveResult r);
                    n++;
                    float a = Mathf.Abs(r.ForearmRollDeg);
                    if (a > most) { most = a; worstDemand = r.ForearmRollDemandDeg; }
                    if (a > 0f) fired++;
                    if (a > BasisArmSolveCore.TrackerForearmRollSoftDeg) saturated++;
                }
                log.AppendLine($"      tracker forearm roll: fired {fired,4}/{n}, saturated {saturated,4}, worst {most,6:F1} deg");
                Assert.That(fired, Is.GreaterThan(0), "the tracker forearm roll never engaged.");
                Assert.That(saturated, Is.GreaterThan(0),
                    $"the tracker forearm roll never exceeded its {BasisArmSolveCore.TrackerForearmRollSoftDeg:0} deg soft " +
                    "limit, so its saturation curve is untested. A bound whose knee is never crossed is not a bound.");
                // ⚠️ SEAM-AWARE ENVELOPE, NOT A FLAT BOUND -- and this assertion USED to be flat only because
                // this stage was the one sibling with no seam cap at all. It has one now, so it pays the same
                // price the wrist-roll relief and the humeral twist guard already pay: near +-180 the hard
                // bound relaxes, because a principal angle cannot be clamped onto an arc continuously (see
                // BasisArmSolveCore's seam notes). Before the cap this stage jumped 135.003 deg in a single
                // 0.05 deg step -- a 2700x amplification that tore the mesh at BOTH ends of the forearm.
                // Below the crossing the envelope is the flat hard bound exactly, so this is a tightening
                // everywhere except where flatness was provably unachievable.
                float seamEnvelope = Mathf.Max(BasisArmSolveCore.TrackerForearmRollMaxDeg,
                                               2f * Mathf.Abs(Mathf.DeltaAngle(0f, worstDemand)) - 180f);
                Assert.That(most, Is.LessThan(seamEnvelope + 1f),
                    $"the tracker forearm roll reached {most:0.0} deg, past its seam envelope {seamEnvelope:0.0} deg " +
                    $"(hard bound {BasisArmSolveCore.TrackerForearmRollMaxDeg:0} deg, worst demand {worstDemand:0.0} deg).");
            }

            // ── no-tracker forearm roll.
            {
                int fired = 0, n = 0; float most = 0f, worstDemand = 0f;
                for (int k = 0; k <= 360; k += 2)
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(0f, -40f);
                    s.Reach = 0.85f;
                    s.HandRollDeg = k;
                    s.HintMode = BasisArmNet.HintModel;
                    s.FeedTip = true;
                    s.FeedLowerBind = true;
                    s.FeedTwistBind = true;
                    BasisArmNet.Solve(BasisArmNet.Build(s), out BasisArmSolveResult r);
                    n++;
                    float a = Mathf.Abs(r.ForearmRollDeg);
                    if (a > most) { most = a; worstDemand = r.ForearmRollDemandDeg; }
                    if (a > 0f) fired++;
                }
                log.AppendLine($"      no-tracker forearm roll: fired {fired,4}/{n}, worst {most,6:F1} deg");
                Assert.That(fired, Is.GreaterThan(0),
                    "the no-tracker forearm roll never engaged. It is the ONLY thing setting the forearm's axial " +
                    "DOF without a tracker -- without it the avatar's pronation is inherited verbatim from " +
                    "whichever idle clip is playing.");
            }

            // ── elbow flexion clamps, both ends.
            {
                float minSeen = 999f, maxSeen = 0f;
                for (int k = 0; k <= 200; k++)
                {
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(10f, -30f);
                    s.Reach = Mathf.Lerp(0.05f, 1.20f, k / 200f);
                    s.HintMode = BasisArmNet.HintModel;
                    BasisArmNet.Solve(BasisArmNet.Build(s), out BasisArmSolveResult r);
                    minSeen = Mathf.Min(minSeen, r.ElbowAngleDeg);
                    maxSeen = Mathf.Max(maxSeen, r.ElbowAngleDeg);
                }
                log.AppendLine($"      elbow flexion over reach 0.05-1.20: {minSeen:0.00} .. {maxSeen:0.00} deg " +
                               $"(clamps {BasisArmSolveCore.MinElbowAngleDeg:0} / {BasisArmSolveCore.MaxElbowAngleDeg:0})");
                Assert.That(minSeen, Is.LessThan(BasisArmSolveCore.MinElbowAngleDeg + 0.5f),
                    $"a target inside the fold never reached the {BasisArmSolveCore.MinElbowAngleDeg:0} deg minimum-flexion clamp " +
                    $"(closest {minSeen:0.00}); that clamp is untested.");
                Assert.That(minSeen, Is.GreaterThan(BasisArmSolveCore.MinElbowAngleDeg - 0.5f),
                    $"the elbow folded to {minSeen:0.00} deg, past the {BasisArmSolveCore.MinElbowAngleDeg:0} deg anatomical minimum.");
                Assert.That(maxSeen, Is.GreaterThan(BasisArmSolveCore.MaxElbowAngleDeg - 0.5f),
                    $"a target past full reach never reached the {BasisArmSolveCore.MaxElbowAngleDeg:0} deg extension clamp (peak {maxSeen:0.00}).");
                Assert.That(maxSeen, Is.LessThan(BasisArmSolveCore.MaxElbowAngleDeg + 0.5f),
                    $"the elbow straightened to {maxSeen:0.00} deg, past MaxElbowAngleDeg -- which is the ONE constant " +
                    "keeping the elbow's lever arm off zero and every roll singularity in this file unreachable.");
            }

            TestContext.WriteLine("\n  stage engagement:\n" + log);
        }

        // ============================================================================================
        // 4. THE POLE-COLLAPSE STABILISER: DEAD ON THE LIVE PATH, BY ARITHMETIC
        // ============================================================================================

        /// <summary>
        /// BasisArmSolveCore documents this block as UNREACHABLE on the shipping rig -- the model pole is
        /// perpendicular to shoulder->hand BY CONSTRUCTION, so |ahProj| is a flat 0.5*armLen, poleCond is
        /// identically 0.5, and collapse is identically 0. That is a claim of exactly the same KIND as the
        /// one that shipped as a defect (a guard that cannot fire), except deliberate -- so it is worth a
        /// test rather than a comment, and the test must show BOTH halves: dead where it is claimed dead,
        /// and alive on the offline lookup path the block is kept for.
        /// </summary>
        [Test]
        public void PoleCollapseStabiliser_IsArithmeticallyDeadOnTheModelPath_AndLiveOnTheLookupPath()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            float worstModelDeviation = 0f;
            float mostLookupCollapse = 0f;
            int modelPoses = 0, lookupCollapsed = 0, lookupPoses = 0;

            foreach (float reach in new[] { 0.20f, 0.50f, 0.80f, 0.95f, 0.999f, 1.05f, 1.20f })
            foreach (float el in new[] { -70f, -20f, 30f })
            foreach (float azHint in new[] { 0f, 120f, 240f })
            {
                BasisArmNet.Spec m = BasisArmNet.Default(rig);
                m.TargetDir = Dir(25f, el);
                m.Reach = reach;
                m.HintMode = BasisArmNet.HintModel;
                m.HintAzimuthDeg = azHint;
                m.RefPerp = Mathf.Abs(el) > 60f ? Dir(115f, 0f) : Vector3.up;
                BasisArmNet.Solve(BasisArmNet.Build(m), out BasisArmSolveResult rm);
                modelPoses++;
                float poleCond = rm.HintProjMag / BasisArmNet.Total;
                worstModelDeviation = Mathf.Max(worstModelDeviation, Mathf.Abs(poleCond - 0.5f));

                BasisArmNet.Spec l = m;
                l.HintMode = BasisArmNet.HintLookup;
                l.HintLookupStandoff = 0.06f;
                BasisArmNet.Solve(BasisArmNet.Build(l), out BasisArmSolveResult rl);
                lookupPoses++;
                float lc = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rl.HintProjMag / BasisArmNet.Total - 0.15f) / 0.15f));
                mostLookupCollapse = Mathf.Max(mostLookupCollapse, lc);
                if (lc > 0f) lookupCollapsed++;
            }

            Assert.That(worstModelDeviation, Is.LessThan(1e-4f),
                $"the MODEL pole's stand-off deviated from a flat 0.5 of the arm by {worstModelDeviation:0.000000} over " +
                $"{modelPoses} poses. BasisArmSolveCore's claim that the pole-collapse stabiliser is dead code on the " +
                "live path rests entirely on that being exact at EVERY reach -- if it is not, a block documented as " +
                "unreachable is now running on the shipping rig, easing the elbow toward world-down.");
            Assert.That(mostLookupCollapse, Is.GreaterThan(0.5f),
                $"the LOOKUP pole never collapsed past {mostLookupCollapse:0.000}, so the stabiliser is dead on that " +
                "path too and the block is entirely unreachable -- which is not what the file claims and would mean " +
                "the published offline numbers no longer come from the code that produced them.");

            TestContext.WriteLine(
                $"  model pole: |poleCond - 0.5| <= {worstModelDeviation:0.0000000} over {modelPoses} poses (stabiliser " +
                $"provably dead). lookup pole: collapse > 0 on {lookupCollapsed}/{lookupPoses}, peak {mostLookupCollapse:0.000} " +
                "(stabiliser alive, as documented).");
        }

        // ============================================================================================
        // 5. THE COLLINEAR FALLBACK CHAIN
        // ============================================================================================

        /// <summary>
        /// The bend-plane fallback chain (stable down-pole, then hint, then shoulder->target, then
        /// PlayerUp) only runs when the ANIMATED arm is dead straight, which no ordinary pose reaches -- so
        /// it is four branches nothing normally exercises. This enumerates which of them are reachable at
        /// all and reports the rest, because a branch nobody can reach is a branch nobody can have tested.
        /// </summary>
        [Test]
        public void BendPlaneFallbackChain_HasReachableBranches_AndNoneEmitNaN()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            var seen = new HashSet<int>();
            var log = new StringBuilder();

            // 0: the ordinary case -- the animated arm is bent, so Cross(ab,bc) defines the plane.
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(30f, -30f);
                BasisArmNet.Solve(BasisArmNet.Build(s), out BasisArmSolveResult r);
                seen.Add(r.AxisSource);
            }

            // 4: the animated arm is dead straight and NOT along PlayerUp -- the stable down-pole branch.
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.AnimElbowFlexDeg = 180f;
                s.TargetDir = Dir(30f, -30f);
                BasisArmNet.Solve(BasisArmNet.Build(s), out BasisArmSolveResult r);
                seen.Add(r.AxisSource);
                Assert.IsTrue(BasisArmNet.Finite(r.RootRotationSolved) && BasisArmNet.Finite(r.ElbowSolved),
                    "a dead-straight animated arm produced a non-finite solve.");
            }

            // 1 / 2 / 3: dead straight AND aligned with PlayerUp, so the down-pole is degenerate too. The
            // hint then defines the plane; a hint on the arm line falls through to shoulder->target; a
            // target on the arm line falls through to PlayerUp itself.
            for (int variant = 0; variant < 3; variant++)
            {
                BasisArmSolveInput i = default;
                i.Shoulder = Vector3.zero;
                i.Elbow = Vector3.down * BasisArmNet.Upper;
                i.Hand = Vector3.down * BasisArmNet.Total;
                i.RootRotation = BasisArmNet.BoneRot(Vector3.down, 0f, rig.Conv);
                i.MidRotation = i.RootRotation;
                i.TargetRotation = i.MidRotation;
                i.TargetOffset = Quaternion.identity;
                i.PlayerUp = Vector3.up;
                i.TorsoUp = Vector3.up;
                i.HintMaxStepDeg = float.MaxValue;
                i.HintWeight = true;
                i.TargetPosition = variant == 2 ? Vector3.down * (0.8f * BasisArmNet.Total)
                                                : new Vector3(0.2f, -0.4f, 0.1f);
                i.HintPosition = variant == 0 ? new Vector3(0.25f, -0.2f, 0f)
                                              : Vector3.down * (0.4f * BasisArmNet.Total);

                BasisArmNet.Solve(i, out BasisArmSolveResult r);
                seen.Add(r.AxisSource);
                Assert.IsTrue(BasisArmNet.Finite(r.RootRotationSolved) && BasisArmNet.Finite(r.MidRotationSolved)
                              && BasisArmNet.Finite(r.ElbowSolved) && BasisArmNet.Finite(r.HandSolved),
                    $"collinear fallback variant {variant} produced a non-finite solve -- a NaN transform PERSISTS " +
                    "in Unity, so the arm would never recover even once good data returned.");
                Assert.IsTrue(BasisArmNet.Unit(r.HintDelta) && BasisArmNet.Unit(r.MidPostRoll) && BasisArmNet.Unit(r.MidDelta),
                    $"collinear fallback variant {variant} emitted a non-unit quaternion.");
            }

            var sorted = new List<int>(seen);
            sorted.Sort();
            log.AppendLine("      reachable AxisSource values: " + string.Join(", ", sorted));

            Assert.That(seen.Contains(0), Is.True, "the ordinary bend-plane branch (AxisSource 0) was never taken.");
            Assert.That(seen.Contains(4), Is.True,
                "the stable down-pole branch (AxisSource 4) is UNREACHABLE. It is the branch that stops a " +
                "fully-stretched arm thrashing between the collinear fallbacks below it, so if nothing can reach " +
                "it, a straight arm is falling through to a fallback that flips its bend plane frame to frame.");
            Assert.That(seen.Count, Is.GreaterThan(2),
                $"only {seen.Count} of the five bend-plane branches are reachable at all: {string.Join(", ", sorted)}. " +
                "An unreachable branch cannot have been tested and cannot be relied on.");

            TestContext.WriteLine("\n  bend-plane fallbacks:\n" + log);
        }
    }
}
