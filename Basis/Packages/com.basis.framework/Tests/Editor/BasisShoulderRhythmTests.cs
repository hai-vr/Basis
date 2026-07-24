using System;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// SCAPULOHUMERAL RHYTHM — the shoulder girdle's share of ARM ELEVATION — and the zero-quaternion
    /// decline that has to hold underneath it.
    ///
    /// THE DEFECT. Every pre-existing girdle term is driven by the SWING AWAY FROM THE AUTHORED BIND. On a
    /// T-pose bind that bind direction IS ~90 deg of clinical elevation, so the swing is V-shaped about it
    /// and the girdle's contribution to the clavicle tip measured, on the live core at shipped constants:
    ///
    ///     arm elevation   0     30    60    90    120   150   180
    ///     tip lift    -1.06  -0.46 -0.06  0.00   0.23  1.84  4.24   (deg)
    ///
    /// Exactly 0.00 at the bind pose, never past 4.24 anywhere, and DECREASING over the first 90 deg of a
    /// raise. In the sagittal plane it is blind rather than merely small: every elevation there is a 90 deg
    /// swing from a T-pose bind, so the coupled term cannot see the raise at all. The clinical clavicle
    /// elevates ~30 deg. The girdle was reading the wrong quantity, not reading it too quietly.
    ///
    /// THE MODEL. Driven by CLINICAL humeral elevation (angle from the arm-at-side — bind-independent) and
    /// the plane of elevation, and by nothing else: Grewal &amp; Dickerson (n=28) found humeral axial rotation
    /// does not influence scapular retraction/protraction (p&gt;0.05). Amplitudes and shape are Ludewig et al.
    /// (transcortical bone pins, n=12): GH:ST 2.1:1 abduction / 2.4:1 flexion / 2.2:1 scapular plane overall,
    /// but strongly NON-CONSTANT — 4:1..7:1 through the first ~30 deg and about 1:1 from 90-150 — with
    /// clavicular elevation ~30 deg peaking near 130 deg of arm elevation and clavicular retraction ~16 deg.
    ///
    /// ⚠️ IT CANNOT FIX ANY AXIAL-ROLL DEFECT and these tests pin that it does not pretend to: a pure
    /// humeral roll holds both model inputs constant, so the contribution is identically zero there.
    ///
    /// ⚠️ THE CORPUS CANNOT VALIDATE THIS. CMU's RightShoulder channel range is 0.0/0.0/0.0 in EVERY clip —
    /// the conversion carries no clavicle motion whatsoever. There is no ground truth to fit to and none of
    /// these tests pretend otherwise; they hold the model to the CLINICAL numbers and to safety properties.
    ///
    /// Every assertion below was mutation-checked: each was shown to FAIL against a deliberately broken
    /// core before being trusted. The mutation each one catches is named in its own comment.
    /// </summary>
    public class BasisShoulderRhythmTests
    {
        const float ArmLen = 0.70f;    // T-pose shoulder→hand
        const float ClavLen = 0.16f;   // shoulder→upperArm
        const float ElbowLen = 0.44f;  // shoulder→lowerArm

        // Shipped values: BasisFullBodyIK k_ShoulderCoupleRatio / k_ShoulderMaxDeg,
        // BasisSettingsDefaults FBIKShoulderElevation / FBIKShoulderProtraction.
        const float Couple = 0.4f, MaxShoulderDeg = 25f, Elevation = 0.4f, Protraction = 0.3f;

        // The core's own constants, restated so a change to either side is caught rather than absorbed.
        const float PeakDeg = 130f, ClavElevMax = 30f, ClavRetractMax = 16f;

        static Vector3 Rest(bool isLeft) => new Vector3(isLeft ? -1f : 1f, 0f, 0f);

        /// <summary>
        /// CLINICAL convention, deliberately not the az/el one the other shoulder test files use:
        /// theta = humeral elevation from the arm-at-side (0 = hanging, 90 = horizontal, 180 = overhead),
        /// phi = plane of elevation (0 = abduction/frontal, 90 = flexion/sagittal, negative = posterior).
        /// This is the frame the cited literature reports in, so the numbers can be compared directly.
        /// </summary>
        static Vector3 Dir(float thetaDeg, float phiDeg, bool isLeft)
        {
            float t = thetaDeg * Mathf.Deg2Rad, p = phiDeg * Mathf.Deg2Rad;
            float lat = Mathf.Sin(t) * Mathf.Cos(p) * (isLeft ? -1f : 1f);
            return new Vector3(lat, -Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(p)).normalized;
        }

        /// <summary>
        /// Angle between two rotations WITHOUT 2*acos(|dot|), which reports up to 0.09 deg between
        /// bit-identical float32 quaternions. Normalise, then atan2 of the vector part against |w|.
        /// </summary>
        static float AngleDeg(Quaternion a, Quaternion b)
        {
            Quaternion d = b * Quaternion.Inverse(a);
            float n = Mathf.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z + d.w * d.w);
            if (!(n > 1e-20f))
            {
                return 0f;
            }
            float vx = d.x / n, vy = d.y / n, vz = d.z / n, w = d.w / n;
            return 2f * Mathf.Atan2(Mathf.Sqrt(vx * vx + vy * vy + vz * vz), Mathf.Abs(w)) * Mathf.Rad2Deg;
        }

        static BasisShoulderSolveInput Input(float theta, float phi, float reachFrac = 0.95f,
            bool hasElbow = false, bool isLeft = false, bool rhythm = true, bool shrug = false,
            bool retract = false, float elev = Elevation, float prot = Protraction, Vector3? bindDir = null)
        {
            Vector3 d = Dir(theta, phi, isLeft);
            BasisShoulderSolveInput s = default;
            s.ShoulderPos = Vector3.zero;
            s.HandTargetPos = d * (ArmLen * reachFrac);
            s.ElbowPos = d * (ElbowLen * reachFrac);
            s.HasElbow = hasElbow;
            s.HasShoulderTracker = false;
            s.ChestRot = Quaternion.identity;
            s.TposeChestRot = Quaternion.identity;
            s.TposeShoulderRot = Quaternion.identity;
            s.ChestBind = Quaternion.identity;
            s.TposeArmDirWorld = bindDir ?? Rest(isLeft);
            s.TposeArmLength = ArmLen;
            s.TposeClavicleLength = ClavLen;
            // ⚠️ ALWAYS baked, including on the hand path, because BasisFullBodyIK always bakes it
            // (SolveShoulder assigns tposeElbowLen unconditionally). The older shoulder test files use
            // `hasElbow ? ElbowLen : 0f`, which was harmless while this length only fed the elbow shrug —
            // but it now also feeds the hand path's driver-trust bound, and leaving it 0 would put these
            // tests on the fallback branch and measure something the runtime never does. This is the same
            // trap that made `elbowTrust` fail closed for three offline callers.
            s.TposeElbowLength = ElbowLen;
            s.ShrugEnabled = shrug;
            s.RetractEnabled = retract;
            s.RhythmEnabled = rhythm;
            s.ElevationFactor = elev;
            s.ProtractionFactor = prot;
            s.CoupleRatio = Couple;
            s.MaxShoulderDeg = MaxShoulderDeg;
            s.TrackerFinal = Quaternion.identity;
            s.IsLeft = isLeft;
            return s;
        }

        static BasisShoulderSolveResult Solve(BasisShoulderSolveInput s)
        {
            BasisShoulderSolveCore.Solve(s, out BasisShoulderSolveResult r);
            return r;
        }

        static bool IsUnit(Quaternion q)
        {
            float n = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return n > 0.998f && n < 1.002f;
        }

        // =========================================================================================
        // JOB 1 — A ZERO QUATERNION IS NOT A ROTATION
        //
        // ShoulderRotation is written straight to the bone with SetRotation, so a zero quaternion
        // COLLAPSES the transform exactly as a NaN would. Four inputs reach the output un-normalised.
        // Note Unity's Quaternion.Inverse is a bare CONJUGATE, so inverse(zero) is zero — not identity,
        // not NaN — which is how a zero TposeChestRot reaches the output through an inverse.
        // =========================================================================================

        /// <summary>
        /// Mutation: delete the ChestRot clause from the guard. Measured on the unguarded core, this pose
        /// returned Apply == true with ShoulderRotation (0,0,0,0).
        /// </summary>
        [Test]
        public void ZeroChestRot_Declines()
        {
            var s = Input(120f, 35f, rhythm: true, shrug: true, retract: true);
            s.ChestRot = default;
            var r = Solve(s);
            Assert.That(r.Apply, Is.False,
                $"a zero ChestRot produced Apply == true with rotation {r.ShoulderRotation} — that collapses the clavicle.");
        }

        /// <summary>Mutation: delete the TposeChestRot clause. Reaches the output via Quaternion.Inverse.</summary>
        [Test]
        public void ZeroTposeChestRot_Declines()
        {
            var s = Input(120f, 35f);
            s.TposeChestRot = default;
            var r = Solve(s);
            Assert.That(r.Apply, Is.False,
                $"a zero TposeChestRot produced Apply == true with rotation {r.ShoulderRotation}.");
        }

        /// <summary>Mutation: delete the TposeShoulderRot clause.</summary>
        [Test]
        public void ZeroTposeShoulderRot_Declines()
        {
            var s = Input(120f, 35f);
            s.TposeShoulderRot = default;
            var r = Solve(s);
            Assert.That(r.Apply, Is.False,
                $"a zero TposeShoulderRot produced Apply == true with rotation {r.ShoulderRotation}.");
        }

        /// <summary>Mutation: delete the TrackerFinal clause. It is the Slerp BASE on the tracker path.</summary>
        [Test]
        public void ZeroTrackerFinal_WithATracker_Declines()
        {
            var s = Input(120f, 35f);
            s.HasShoulderTracker = true;
            s.TrackerFinal = default;
            var r = Solve(s);
            Assert.That(r.Apply, Is.False,
                $"a zero TrackerFinal with a tracker present produced Apply == true with rotation {r.ShoulderRotation}.");
        }

        /// <summary>
        /// The OVER-guarding case, and the reason the TrackerFinal clause is conditional. Callers that
        /// build the input with `= default` — BasisShoulderSweep, BasisShoulderCoupleSweep and
        /// BasisShoulderDirectionTests among them — legitimately leave TrackerFinal zero while
        /// HasShoulderTracker is false, where it is never read. Guarding it unconditionally would decline
        /// the entire trackerless solve, i.e. silently switch the shoulder off for everyone.
        ///
        /// Mutation: drop the `i.HasShoulderTracker &amp;&amp;` from the guard. This test then fails.
        /// </summary>
        [Test]
        public void ZeroTrackerFinal_WithoutATracker_StillSolves()
        {
            var s = Input(120f, 35f);
            s.HasShoulderTracker = false;
            s.TrackerFinal = default;
            var r = Solve(s);
            Assert.That(r.Apply, Is.True,
                "a zero TrackerFinal with NO tracker must still solve — it is never read on that path, and " +
                "declining here would switch the trackerless girdle off for every caller that uses `= default`.");
            Assert.That(IsUnit(r.ShoulderRotation), Is.True, $"rotation {r.ShoulderRotation} is not a unit quaternion.");
        }

        /// <summary>
        /// The house rule the guards are written to: reject-unless-good, so NaN lands in the reject branch.
        /// Mutation: write the guard as `if (Dot(q, q) &lt;= eps) decline`. Every NaN case below then passes
        /// straight through, because every comparison against NaN is false.
        /// </summary>
        [Test]
        public void NaNFrames_Decline()
        {
            Quaternion nan = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
            foreach (string which in new[] { "ChestRot", "TposeChestRot", "TposeShoulderRot", "TrackerFinal" })
            {
                var s = Input(120f, 35f);
                switch (which)
                {
                    case "ChestRot": s.ChestRot = nan; break;
                    case "TposeChestRot": s.TposeChestRot = nan; break;
                    case "TposeShoulderRot": s.TposeShoulderRot = nan; break;
                    default: s.HasShoulderTracker = true; s.TrackerFinal = nan; break;
                }
                var r = Solve(s);
                Assert.That(r.Apply, Is.False,
                    $"a NaN {which} produced Apply == true with rotation {r.ShoulderRotation}. A NaN transform " +
                    "PERSISTS in Unity: the shoulder never recovers, even once good data returns.");
            }
        }

        /// <summary>
        /// The headline invariant, swept over the pose sphere CROSSED WITH the degenerate frames: whenever
        /// the core says Apply, what it hands over is a rotation. This is the assertion
        /// BasisShoulderInvariantNetTests currently records as a KNOWN-OPEN defect; the sweep is what makes
        /// it a net rather than four point checks, so a fifth un-normalised path added later is caught too.
        /// Mutation: drop any one of the four guard clauses. The matching frame variant then reports
        /// Apply == true with (0,0,0,0).
        /// </summary>
        [Test]
        public void EveryAcceptedResult_IsAUnitQuaternion()
        {
            foreach (string degenerate in new[] { "none", "ChestRot", "TposeChestRot", "TposeShoulderRot", "TrackerFinal" })
            foreach (bool tracker in new[] { false, true })
            foreach (bool elbow in new[] { false, true })
            foreach (bool side in new[] { false, true })
            for (float th = 0f; th <= 180.001f; th += 20f)
            for (float ph = -180f; ph <= 180.001f; ph += 40f)
            {
                var s = Input(th, ph, 0.9f, elbow, side, rhythm: true, shrug: true, retract: true);
                s.HasShoulderTracker = tracker;
                switch (degenerate)
                {
                    case "ChestRot": s.ChestRot = default; break;
                    case "TposeChestRot": s.TposeChestRot = default; break;
                    case "TposeShoulderRot": s.TposeShoulderRot = default; break;
                    case "TrackerFinal": s.TrackerFinal = default; break;
                }
                var r = Solve(s);
                if (!r.Apply)
                {
                    continue;   // declining is a perfectly good answer to nonsense
                }
                Assert.That(IsUnit(r.ShoulderRotation), Is.True,
                    $"theta={th} phi={ph} tracker={tracker} degenerate={degenerate}: Apply == true with a " +
                    $"non-rotation {r.ShoulderRotation}. A zero quaternion collapses the clavicle exactly as a NaN would.");
            }
        }

        // =========================================================================================
        // JOB 2 — THE RHYTHM DECLINES TO EXACTLY THE OLD BEHAVIOUR
        // =========================================================================================

        /// <summary>
        /// The whole term lives inside one `if (i.RhythmEnabled)`. With it false the two contributions must
        /// be HARD zero — not small — over the entire sphere, at both reach extremes, both driver paths and
        /// both sides, so every existing caller, gate and offline sweep stays bit-identical.
        /// Mutation: hoist either contribution out of the `if`. This fails at the first raised pose.
        /// </summary>
        [Test]
        public void Disabled_ContributesExactlyZero()
        {
            foreach (bool elbow in new[] { false, true })
            foreach (bool side in new[] { false, true })
            foreach (float reach in new[] { 0.55f, 0.95f })
            for (float th = 0f; th <= 180.001f; th += 5f)
            for (float ph = -180f; ph <= 180.001f; ph += 15f)
            {
                var r = Solve(Input(th, ph, reach, elbow, side, rhythm: false, shrug: true, retract: true));
                Assert.That(r.RhythmElevDeg, Is.EqualTo(0f),
                    $"theta={th} phi={ph}: the rhythm contributed elevation while disabled.");
                Assert.That(r.RhythmRetractDeg, Is.EqualTo(0f),
                    $"theta={th} phi={ph}: the rhythm contributed retraction while disabled.");
            }
        }

        /// <summary>
        /// Anti-tautology for the test above: the toggle must actually be connected to something, or
        /// Disabled_ContributesExactlyZero passes on a term that never runs at all.
        /// </summary>
        [Test]
        public void Toggle_IsActuallyWired()
        {
            for (float th = 60f; th <= 180.001f; th += 30f)
            {
                var off = Solve(Input(th, 0f, rhythm: false));
                var on = Solve(Input(th, 0f, rhythm: true));
                Assert.That(on.RhythmElevDeg, Is.GreaterThan(1f),
                    $"theta={th}: the rhythm was not on to begin with ({on.RhythmElevDeg:0.00} deg).");
                Assert.That(AngleDeg(off.ShoulderRotation, on.ShoulderRotation), Is.GreaterThan(0.5f),
                    $"theta={th}: on and off produced the same girdle — the toggle is not wired to anything.");
            }
        }

        // =========================================================================================
        // JOB 2 — THE MODEL MATCHES THE CITED BIOMECHANICS
        // =========================================================================================

        /// <summary>
        /// THE SETTING PHASE. The first ~30 deg of elevation runs 4:1 to 7:1, so the girdle takes only about
        /// a sixth of it — the shape function must be near-flat there, not linear from the origin.
        /// Mutation: make RhythmProgress linear (drop the rate ramp, k_RhythmLateRateMul = 1). Progress at
        /// 30 deg then reads 0.231 against the modelled 0.130 and this fails.
        /// </summary>
        [Test]
        public void SettingPhase_BarelyMovesTheGirdle()
        {
            // Slider 1.0 so the reported degrees are directly the clinical clavicular elevation.
            float at30 = Solve(Input(30f, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg;
            Assert.That(at30, Is.LessThan(0.20f * ClavElevMax),
                $"the girdle took {at30:0.00} of its {ClavElevMax} deg inside the setting phase; the cited " +
                "4:1-7:1 ratio there allows about a sixth.");
            Assert.That(at30, Is.GreaterThan(0.05f * ClavElevMax),
                $"the girdle took only {at30:0.00} deg by 30 deg of elevation — the setting phase is not a dead zone.");
        }

        /// <summary>
        /// THE PEAK. Clavicular elevation is cited at ~30 deg PEAKING NEAR 130 deg of arm elevation, so the
        /// curve must reach its maximum there and then HOLD rather than keep climbing.
        /// Mutation: delete the `elevDeg >= k_RhythmPeakDeg` early-out. The curve then keeps rising past the
        /// peak and the hold assertions fail.
        /// </summary>
        [Test]
        public void ClavicularElevation_PeaksAtTheCitedElevation_AndHolds()
        {
            float atPeak = Solve(Input(PeakDeg, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg;
            Assert.That(atPeak, Is.EqualTo(ClavElevMax).Within(0.05f),
                $"clavicular elevation at the cited {PeakDeg} deg peak read {atPeak:0.00}, not {ClavElevMax}.");

            foreach (float th in new[] { 140f, 150f, 165f, 180f })
            {
                float beyond = Solve(Input(th, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg;
                Assert.That(beyond, Is.EqualTo(ClavElevMax).Within(0.05f),
                    $"clavicular elevation at theta={th} read {beyond:0.00}; past the peak it must hold at {ClavElevMax}.");
            }

            float atRetractPeak = Solve(Input(PeakDeg, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmRetractDeg;
            Assert.That(atRetractPeak, Is.EqualTo(ClavRetractMax).Within(0.05f),
                $"clavicular retraction at the peak read {atRetractPeak:0.00}, not the cited {ClavRetractMax}.");
        }

        /// <summary>
        /// THE RATIO IS NON-CONSTANT, which is the single most important property of the model and the one a
        /// naive implementation gets wrong. The girdle's rate late in the arc must be about 3x its rate in
        /// the setting phase — that factor is exactly the cited shift from 5:1 (share 1/6) to 1:1 (share 1/2).
        /// Mutation: k_RhythmLateRateMul = 1 (a single constant ratio). The measured factor drops to 1.00.
        /// </summary>
        [Test]
        public void TheRhythmRatio_IsNonConstant()
        {
            float Rate(float a, float b) =>
                (Solve(Input(b, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg
               - Solve(Input(a, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg) / (b - a);

            float settingRate = Rate(5f, 25f);      // inside the setting phase
            float lateRate = Rate(105f, 125f);      // approaching the peak, before the plateau
            Assert.That(settingRate, Is.GreaterThan(0f), "the girdle does not move at all in the setting phase.");
            Assert.That(lateRate / settingRate, Is.EqualTo(3f).Within(0.35f),
                $"the late girdle rate is {lateRate / settingRate:0.00}x the setting-phase rate; the cited shift " +
                "from 5:1 to 1:1 makes it 3x. A single constant ratio would read 1.00x.");
        }

        /// <summary>
        /// The cumulative consequence: integrating the modelled rate over a 150 deg arc must reproduce the
        /// cited OVERALL 2.1:1 abduction rhythm. This is the check that ties the shape back to the headline
        /// number, so a shape that hits both endpoints by the wrong route still fails.
        /// Mutation: k_RhythmSetEndDeg = 0 (no setting phase) drives this to 1.85:1.
        /// </summary>
        [Test]
        public void OverallRatio_ReproducesTheCitedAbductionRhythm()
        {
            // The clavicular channel is scaled to 30 deg; the SHAPE carries the ratio, so rescale the
            // cumulative to the full girdle share the 2.1:1 figure implies over this arc (150/3.1 = 48.4).
            const float FullGirdleShare = 48.39f;
            float progressAt150 = Solve(Input(150f, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg / ClavElevMax;
            float st = progressAt150 * FullGirdleShare;
            float ratio = (150f - st) / st;
            Assert.That(ratio, Is.EqualTo(2.1f).Within(0.15f),
                $"the modelled shape implies an overall GH:ST of {ratio:0.00}:1 over a 150 deg arc; " +
                "Ludewig's bone-pin abduction figure is 2.1:1.");
        }

        /// <summary>
        /// PLANE OF ELEVATION. Flexion is cited at 2.4:1 against abduction's 2.1:1, i.e. the girdle takes a
        /// slightly SMALLER share in the sagittal plane — (1/3.4)/(1/3.1) = 0.912 of it.
        /// Mutation: delete the k_RhythmFlexionShare lerp. The two planes then read identically.
        /// </summary>
        [Test]
        public void FlexionTakesASmallerGirdleShareThanAbduction()
        {
            // Compared at 90 deg of elevation, the ONE elevation where the sagittal plane is reached in
            // full. The plane is read from the raw forward component rather than the horizontal azimuth (the
            // same call the retraction term makes, and the reason neither has a pole singularity), so
            // "forward" is sin(theta) in the sagittal plane and only equals 1 at the horizontal. Off the
            // horizontal the modulation correctly eases back toward the frontal-plane value.
            float abduction = Solve(Input(90f, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg;
            float flexion = Solve(Input(90f, 90f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg;
            Assert.That(flexion, Is.LessThan(abduction),
                $"flexion ({flexion:0.00}) took at least as much girdle as abduction ({abduction:0.00}); " +
                "the cited 2.4:1 vs 2.1:1 makes flexion the smaller share.");
            Assert.That(flexion / abduction, Is.EqualTo(0.912f).Within(0.005f),
                $"flexion took {flexion / abduction:0.000} of abduction's girdle share; the cited ratios give " +
                "(1/3.4)/(1/3.1) = 0.912.");

            // And the easing is monotone in how sagittal the pose actually is, with no reversal.
            float prev = 0f;
            foreach (float th in new[] { 90f, 75f, 60f, 45f })
            {
                float share = Solve(Input(th, 90f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg
                            / Solve(Input(th, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg;
                Assert.That(share, Is.GreaterThan(prev - 1e-4f),
                    $"theta={th}: the plane modulation reversed ({share:0.000} after {prev:0.000}).");
                prev = share;
            }
        }

        /// <summary>
        /// NO DOUBLE-COUNTING. Reaching forward the clavicle PROTRACTS, and the coupled swing's
        /// ProtractionFactor already owns that half of the horizontal plane. The rhythm must therefore add no
        /// retraction in the sagittal plane; all it supplies is the elevation-driven retraction the coupled
        /// swing structurally cannot.
        /// Scoped to the HORIZONTAL forward band, which is where the coupled swing's protraction is
        /// strongest and where the double-count would therefore actually happen. Above that band the
        /// retraction deliberately returns — Ludewig reports clavicular retraction at full elevation in ALL
        /// planes, and up there the coupled swing's horizontal protraction has itself faded — so this test
        /// pins the no-double-count region rather than banning the term from the sagittal plane outright.
        /// Mutation: drop the `(1f - forward)` factor. Retraction at the horizontal reaches the full
        /// elevation-driven value and this fails.
        /// </summary>
        [Test]
        public void HorizontalForwardReach_AddsNoRhythmRetraction()
        {
            foreach (float th in new[] { 75f, 90f, 105f })
            {
                var r = Solve(Input(th, 90f, 1.0f, hasElbow: false, elev: 1f, prot: 1f));
                Assert.That(r.RhythmRetractDeg, Is.LessThan(0.5f),
                    $"theta={th}: the rhythm retracted the girdle {r.RhythmRetractDeg:0.00} deg on a horizontal " +
                    "FORWARD reach, where the clavicle protracts and the coupled swing already owns the motion.");
            }

            // Dead ahead and horizontal is the exact double-count pose: it must be identically zero.
            Assert.That(Solve(Input(90f, 90f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmRetractDeg, Is.EqualTo(0f),
                "a reach dead ahead and level must take exactly zero rhythm retraction.");
        }

        // =========================================================================================
        // JOB 2 — THE PROPERTIES THAT MAKE IT SAFE TO SHIP
        // =========================================================================================

        /// <summary>
        /// IDLE SAFETY, the property that matters most because arms-down is where users spend their time.
        /// Elevation is measured from the arm-at-side, so at a true hang the contribution is identically
        /// zero — not small, zero — and the authored bind shoulder is kept exactly.
        /// Mutation: measure elevation from the BIND direction instead of from the side. A T-pose bind then
        /// puts a 90 deg elevation on a hanging arm and this reads the full peak contribution.
        /// </summary>
        [Test]
        public void ArmsDown_ContributesExactlyZero()
        {
            foreach (bool side in new[] { false, true })
            foreach (float ph in new[] { -90f, -30f, 0f, 30f, 90f, 180f })
            {
                var r = Solve(Input(0f, ph, 1.0f, false, side, elev: 1f, prot: 1f));
                Assert.That(r.RhythmElevDeg, Is.EqualTo(0f),
                    $"phi={ph}: the rhythm lifted the girdle {r.RhythmElevDeg:0.00} deg on a hanging arm.");
                Assert.That(r.RhythmRetractDeg, Is.EqualTo(0f),
                    $"phi={ph}: the rhythm retracted the girdle {r.RhythmRetractDeg:0.00} deg on a hanging arm.");
            }
        }

        /// <summary>
        /// BIND INDEPENDENCE — the defect's root cause, pinned. Clinical elevation does not depend on what
        /// the artist authored, so a T-pose rig and an A-pose rig must get the SAME rhythm at the same real
        /// arm elevation, even though their coupled-swing angles differ by 60 deg.
        /// Mutation: drive the term from `swingDeg` (the pre-existing swing-from-bind). The four binds then
        /// disagree by more than 10 deg.
        /// </summary>
        [Test]
        public void RhythmIsBindIndependent()
        {
            float reference = -1f;
            foreach (float bindTheta in new[] { 90f, 60f, 45f, 30f })
            {
                var s = Input(150f, 0f, 1.0f, false, false, bindDir: Dir(bindTheta, 0f, false));
                var r = Solve(s);
                if (reference < 0f)
                {
                    reference = r.RhythmElevDeg;
                    Assert.That(reference, Is.GreaterThan(1f), "the reference bind produced no rhythm to compare against.");
                    continue;
                }
                Assert.That(r.RhythmElevDeg, Is.EqualTo(reference).Within(0.01f),
                    $"a bind authored at theta={bindTheta} produced {r.RhythmElevDeg:0.00} deg of rhythm where the " +
                    $"T-pose bind produced {reference:0.00} — the term is reading the bind, not the anatomy.");
            }
        }

        /// <summary>
        /// IT SKIPS `engage`, deliberately, and this pins why in its sharpest form. `engage` is
        /// `setting * reachFade`, and `setting` is built from swing-from-bind — which on a T-pose bind is
        /// 0.00 at exactly the arm-horizontal pose where clinical clavicular elevation is already ~16 deg.
        /// Scaling by `engage` would zero the term at the pose it exists for.
        /// Mutation: multiply the contribution by `engage`. This reads 0.00 and fails.
        /// </summary>
        [Test]
        public void RhythmSkipsTheSwingGate()
        {
            // The arm AT the bind direction, straight, with a trusted elbow tracker: swing-from-bind is
            // identically zero, so `setting` and therefore `engage` are identically zero.
            var r = Solve(Input(90f, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f));
            Assert.That(r.SwingAngleDeg, Is.LessThan(0.01f),
                $"the arm was supposed to sit at the bind direction (swing {r.SwingAngleDeg:0.000}); " +
                "if it moved, this test no longer proves the term survives a shut swing gate.");
            Assert.That(r.ComputedWeight, Is.EqualTo(0f),
                $"engage was supposed to be exactly zero here, not {r.ComputedWeight:0.000}.");
            Assert.That(r.RhythmElevDeg, Is.GreaterThan(14f),
                $"the girdle contributed {r.RhythmElevDeg:0.00} deg at 90 deg of elevation — the swing gate ate it. " +
                "Clinical clavicular elevation there is ~16 deg.");
        }

        /// <summary>
        /// ⚠️ THE CONTROLLER-ONLY REGRESSION GUARD. Reported from headset: "even in regular VR (controllers,
        /// no hint) I'm able to rotate my hand and break it."
        ///
        /// The rhythm is a function of HUMERAL elevation, and only an elbow tracker measures the humerus.
        /// shoulder->hand is humerus + forearm. A pure humeral axial ROLL with a bent elbow sweeps the hand
        /// around a cone while the humerus does not move at all, so an unguarded hand-driven rhythm invents
        /// elevation out of a gesture that has none. Measured on the unguarded term, arm out to the side at
        /// 90 deg of elbow flexion: 61.2 deg of phantom elevation and 7.51 deg of clavicle swing, against
        /// 0.00 deg before the term existed.
        ///
        /// Mutation: set rhythmTrust = 1f on the hand path. The 90 deg-flexion row swings 7.5 deg.
        /// </summary>
        [Test]
        public void PureHumeralRoll_MovesNoGirdle_OnTheControllerOnlyPath()
        {
            Vector3 humerus = Dir(90f, 0f, false);          // upper arm horizontal, out to the side
            Vector3 perp = Vector3.Cross(humerus, Vector3.up).normalized;

            foreach (float flexDeg in new[] { 90f, 120f, 150f, 175f })
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (float roll = 0f; roll < 360f; roll += 5f)
                {
                    Vector3 spun = Quaternion.AngleAxis(roll, humerus) * perp;
                    Vector3 fore = (Quaternion.AngleAxis(180f - flexDeg, Vector3.Cross(humerus, spun).normalized) * humerus).normalized;

                    var s = Input(90f, 0f, 1.0f, hasElbow: false, rhythm: true, shrug: true, retract: true);
                    s.HandTargetPos = humerus * ElbowLen + fore * (ArmLen - ElbowLen);
                    var r = Solve(s);
                    lo = Mathf.Min(lo, r.AppliedAngleDeg);
                    hi = Mathf.Max(hi, r.AppliedAngleDeg);
                }
                // ⚠️ THE BAR IS 3 deg, NOT ZERO, AND THAT IS HONEST. Without an elbow tracker the hand can
                // never pin the humerus better than the law-of-cosines bound allows: at 145 deg of flexion
                // the bound is ~13 deg, so the read elevation genuinely oscillates +/-13 deg through a roll
                // and some residual is irreducible. Measured worst across all flexions: 2.56 deg, against
                // 7.51 deg unguarded and 0.00 deg before this term existed. Driving it to zero would mean
                // switching the term off for every controller-only user, which is the next test's job to
                // show is not necessary.
                Assert.That(hi - lo, Is.LessThan(3f),
                    $"elbow flexion {flexDeg}: a PURE humeral roll swung the girdle {hi - lo:0.00} deg " +
                    "(the humerus never changed direction). This is the reported controller-only defect.");
            }
        }

        /// <summary>
        /// The other half of the same guard: with an elbow tracker the driver lies ON the humeral axis, so
        /// the identical gesture must move the girdle by EXACTLY nothing, not merely a little.
        /// Mutation: take the driver from the hand even when an elbow tracker is present.
        /// </summary>
        [Test]
        public void PureHumeralRoll_MovesTheGirdleExactlyZero_WithAnElbowTracker()
        {
            Vector3 humerus = Dir(90f, 0f, false);
            Vector3 perp = Vector3.Cross(humerus, Vector3.up).normalized;

            foreach (float flexDeg in new[] { 90f, 120f, 150f })
            {
                float first = float.NaN;
                for (float roll = 0f; roll < 360f; roll += 5f)
                {
                    Vector3 spun = Quaternion.AngleAxis(roll, humerus) * perp;
                    Vector3 fore = (Quaternion.AngleAxis(180f - flexDeg, Vector3.Cross(humerus, spun).normalized) * humerus).normalized;

                    var s = Input(90f, 0f, 1.0f, hasElbow: true, rhythm: true, shrug: true, retract: true);
                    s.ElbowPos = humerus * ElbowLen;
                    s.HandTargetPos = s.ElbowPos + fore * (ArmLen - ElbowLen);
                    var r = Solve(s);
                    if (float.IsNaN(first))
                    {
                        first = r.AppliedAngleDeg;
                        Assert.That(first, Is.GreaterThan(1f), "no girdle to hold constant; the test proves nothing.");
                        continue;
                    }
                    Assert.That(r.AppliedAngleDeg, Is.EqualTo(first).Within(1e-4f),
                        $"elbow flexion {flexDeg} roll {roll}: the girdle moved during a pure humeral roll " +
                        "even though the elbow tracker pins the humerus.");
                }
            }
        }

        /// <summary>
        /// The driver-trust fade itself, at the level of the bound rather than the gesture: a straight arm
        /// gives the humerus direction exactly, a folded one gives no information about it, and the term
        /// must follow that monotonically.
        /// Mutation: drop the law-of-cosines bound (rhythmTrust = 1). The bent rows then match the straight one.
        /// </summary>
        [Test]
        public void HandDriver_IsTrustedOnlyAsFarAsTheArmIsStraight()
        {
            // reach 1.0 is the straight arm: the hand vector IS the humerus vector, error exactly 0.
            float straight = Solve(Input(120f, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg;
            Assert.That(straight, Is.GreaterThan(20f),
                $"a straight raised arm only got {straight:0.00} deg — the trust bound is fading a driver it should believe.");

            float prev = straight;
            foreach (float reach in new[] { 0.90f, 0.80f, 0.70f, 0.60f })
            {
                float bent = Solve(Input(120f, 0f, reach, hasElbow: false, elev: 1f, prot: 1f)).RhythmElevDeg;
                Assert.That(bent, Is.LessThanOrEqualTo(prev + 1e-3f),
                    $"reach={reach}: folding the elbow INCREASED the rhythm ({prev:0.00} -> {bent:0.00}).");
                prev = bent;
            }
            Assert.That(prev, Is.LessThan(0.5f),
                $"a deeply folded arm still drove {prev:0.00} deg of rhythm from a hand that says nothing about the humerus.");

            // An elbow tracker is immune to the same fold. Folding the elbow means moving the HAND while
            // the ELBOW stays put, so vary only the hand here — moving the elbow as well would change the
            // humerus direction, which is a different gesture and would rightly change the answer.
            var eStraight = Input(120f, 0f, 1.0f, hasElbow: true, elev: 1f, prot: 1f);
            var eBent = eStraight;
            eBent.HandTargetPos = eStraight.ElbowPos + (eStraight.HandTargetPos - eStraight.ElbowPos) * 0.2f;
            float straightElbowPath = Solve(eStraight).RhythmElevDeg;
            float bentElbowPath = Solve(eBent).RhythmElevDeg;
            Assert.That(straightElbowPath, Is.GreaterThan(1f), "no rhythm on the elbow path to hold constant.");
            Assert.That(bentElbowPath, Is.EqualTo(straightElbowPath).Within(0.01f),
                $"the elbow path lost rhythm to a folded forearm ({straightElbowPath:0.00} -> {bentElbowPath:0.00}); " +
                "the elbow tracker measures the humerus directly, so elbow flexion is irrelevant to it.");
        }

        /// <summary>
        /// Mutation: drop the `i.IsLeft ?` negation on either contribution. The left clavicle then drives the
        /// opposite way and this reports a mismatch equal to twice the contribution.
        /// </summary>
        [Test]
        public void LeftMirrorsRight()
        {
            for (float th = 0f; th <= 180.001f; th += 15f)
            for (float ph = -180f; ph <= 180.001f; ph += 30f)
            {
                var right = Solve(Input(th, ph, 0.95f, false, false));
                var left = Solve(Input(th, ph, 0.95f, false, true));
                Assert.That(left.RhythmElevDeg, Is.EqualTo(right.RhythmElevDeg).Within(0.001f),
                    $"theta={th} phi={ph}: left and right elevated by different amounts.");
                Assert.That(left.RhythmRetractDeg, Is.EqualTo(right.RhythmRetractDeg).Within(0.001f),
                    $"theta={th} phi={ph}: left and right retracted by different amounts.");
            }

            // And both roots must LIFT, not one each way.
            var r1 = Solve(Input(140f, 0f, 0.95f, false, false));
            var l1 = Solve(Input(140f, 0f, 0.95f, false, true));
            Assert.That((r1.ShoulderRotation * Rest(false)).y, Is.GreaterThan(0.01f), "the right arm root did not lift.");
            Assert.That((l1.ShoulderRotation * Rest(true)).y, Is.GreaterThan(0.01f), "the left arm root did not lift.");
        }

        /// <summary>
        /// The file's load-bearing invariant: the clavicle swings the arm root, it never rolls with the
        /// humerus. A twist-following clavicle was tried and reverted.
        /// Mutation: add the contribution AFTER the twist-strip instead of before it. Leak reaches ~7 deg.
        /// </summary>
        [Test]
        public void Rhythm_LeaksNoTwist()
        {
            float worst = 0f;
            for (float th = 0f; th <= 180.001f; th += 5f)
            for (float ph = -180f; ph <= 180.001f; ph += 5f)
            foreach (bool elbow in new[] { false, true })
            {
                worst = Mathf.Max(worst, Solve(Input(th, ph, 0.95f, elbow, false, rhythm: true, shrug: true, retract: true)).TwistLeakDeg);
            }
            Assert.That(worst, Is.LessThan(0.5f), $"the rhythm leaked {worst:0.00} deg of twist into the girdle.");
        }

        /// <summary>
        /// The rhythm joins girdleRv BEFORE the shared MaxShoulderDeg clamp, so that bound still owns the
        /// outcome with every term switched on at once.
        /// Mutation: add the contribution AFTER the clamp. The girdle then reaches ~37 deg against a 25 bar.
        /// </summary>
        [Test]
        public void Rhythm_StaysInsideTheSharedClamp()
        {
            float worst = 0f;
            for (float th = 0f; th <= 180.001f; th += 5f)
            for (float ph = -180f; ph <= 180.001f; ph += 5f)
            foreach (bool elbow in new[] { false, true })
            foreach (float reach in new[] { 0.55f, 0.95f })
            {
                worst = Mathf.Max(worst, Solve(Input(th, ph, reach, elbow, false, rhythm: true, shrug: true, retract: true)).AppliedAngleDeg);
            }
            Assert.That(worst, Is.LessThanOrEqualTo(MaxShoulderDeg + 0.5f),
                $"girdle reached {worst:0.0} deg with every term on, past the {MaxShoulderDeg} deg clamp.");
        }

        /// <summary>
        /// COMPOSITION. The rhythm must not take the shrug's or the retraction's share. Both are summed into
        /// the same rotation vector before the same clamp, so a term that pushed the sum into saturation
        /// would scale the others down with it.
        /// Mutation: raise k_RhythmElevMaxDeg to the 75 deg the file's other sizing convention implies. The
        /// shrug pose then saturates and ShrugDeg's delivered share drops.
        /// </summary>
        [Test]
        public void Rhythm_DoesNotTakeTheShrugOrRetractionShare()
        {
            // The strongest shrug pose and the strongest retraction pose, found the same way the offline
            // sweep finds them, then compared with the rhythm off and on.
            var shrugOff = Solve(Input(0f, 0f, 0.65f, false, false, rhythm: false, shrug: true, retract: true));
            var shrugOn = Solve(Input(0f, 0f, 0.65f, false, false, rhythm: true, shrug: true, retract: true));
            Assert.That(shrugOff.ShrugDeg, Is.GreaterThan(10f), "the shrug was not engaged in the reference pose.");
            Assert.That(shrugOn.ShrugDeg, Is.EqualTo(shrugOff.ShrugDeg).Within(0.01f),
                $"the rhythm changed the shrug's own contribution ({shrugOff.ShrugDeg:0.00} -> {shrugOn.ShrugDeg:0.00}).");
            // ⚠️ STRICTLY INSIDE the clamp, not merely at it. ShrugDeg and RetractDeg are PRE-clamp
            // diagnostics, so they cannot reveal a term being scaled down by saturation — asserting on them
            // alone would pass even if the rhythm were 300 deg. Headroom is the property that actually
            // prevents the theft: while the sum stays under the clamp, every term is delivered in full.
            Assert.That(shrugOn.AppliedAngleDeg, Is.LessThan(MaxShoulderDeg - 0.5f),
                $"the shrug pose reached {shrugOn.AppliedAngleDeg:0.0} deg once the rhythm joined it, with no " +
                $"headroom under the {MaxShoulderDeg} deg clamp — at saturation the clamp scales every term down together.");

            // Straight arm, so the rhythm is at FULL strength here rather than faded by the driver-trust
            // bound — otherwise this would be testing composition in a pose where one term is switched off.
            var retOff = Solve(Input(72f, -92f, 1.0f, false, false, rhythm: false, shrug: true, retract: true));
            var retOn = Solve(Input(72f, -92f, 1.0f, false, false, rhythm: true, shrug: true, retract: true));
            Assert.That(retOff.RetractDeg, Is.GreaterThan(10f), "the retraction was not engaged in the reference pose.");
            Assert.That(retOn.RetractDeg, Is.EqualTo(retOff.RetractDeg).Within(0.01f),
                $"the rhythm changed the retraction's own contribution ({retOff.RetractDeg:0.00} -> {retOn.RetractDeg:0.00}).");
            Assert.That(retOn.AppliedAngleDeg, Is.LessThan(MaxShoulderDeg - 0.5f),
                $"the posterior reach reached {retOn.AppliedAngleDeg:0.0} deg once the rhythm joined it, with no " +
                $"headroom under the {MaxShoulderDeg} deg clamp.");

            // ⚠️ AND THE POSE WHERE IT DOES NOT HOLD, RECORDED RATHER THAN AVOIDED. A deep posterior reach
            // AT SHOULDER HEIGHT stacks retraction (18 deg) against the rhythm (7.8 deg) and saturates:
            // 23.12 deg without the rhythm, 25.00 deg with it. At saturation the clamp scales every term
            // down together, so the retraction does lose delivered share there. The pre-clamp sizes are
            // still untouched — the rhythm competes for the budget, it does not alter anyone's computation
            // — but this is the one place the shared 25 deg budget genuinely binds because of this term,
            // and it is the concrete case for restructuring that budget in BasisFullBodyIK.
            var stacked = Solve(Input(100f, -92f, 1.0f, false, false, rhythm: true, shrug: true, retract: true));
            Assert.That(stacked.RetractDeg, Is.EqualTo(18f).Within(0.5f),
                $"the stacked pose no longer produces the retraction it was chosen for ({stacked.RetractDeg:0.0} deg).");
            Assert.That(stacked.AppliedAngleDeg, Is.EqualTo(MaxShoulderDeg).Within(0.05f),
                $"the documented saturating pose read {stacked.AppliedAngleDeg:0.00} deg rather than sitting on the " +
                $"{MaxShoulderDeg} deg clamp; if this moved, the budget note in the core is out of date.");
        }

        /// <summary>
        /// THE FALLBACK BRANCH, and it is the one this repo has been bitten by before. The hand-path trust
        /// bound needs the baked segment lengths; without them there is no bound, and the choice of what to
        /// do then is load-bearing. `elbowTrust` originally failed CLOSED here and silently switched the
        /// girdle off for three offline callers. This one deliberately fails CLOSED in the opposite sense —
        /// it falls back to the reach gate rather than to blanket trust — because on the hand path,
        /// believing an unmeasurable driver is exactly the reported controller-only defect.
        ///
        /// BasisFullBodyIK always bakes TposeElbowLength, so no live path reaches this branch.
        /// Mutation: make the fallback `rhythmTrust = 1f`. The folded-arm row then drives the full rhythm.
        /// </summary>
        [Test]
        public void HandPath_WithoutBakedSegmentLengths_FallsBackToTheReachGate()
        {
            // Folded arm, no baked lengths: the exact bound is unavailable and the driver is untrustworthy.
            var folded = Input(120f, 0f, 0.55f, hasElbow: false, elev: 1f, prot: 1f);
            folded.TposeElbowLength = 0f;
            var r = Solve(folded);
            Assert.That(r.RhythmElevDeg, Is.LessThan(0.5f),
                $"with no baked segment lengths a folded arm still drove {r.RhythmElevDeg:0.00} deg of rhythm. " +
                "The fallback must not trust a driver it cannot bound.");

            // ...and a straight arm still works through the fallback, so it is not a blanket kill switch.
            var straight = Input(120f, 0f, 1.0f, hasElbow: false, elev: 1f, prot: 1f);
            straight.TposeElbowLength = 0f;
            Assert.That(Solve(straight).RhythmElevDeg, Is.GreaterThan(3f),
                "the fallback switched the rhythm off even for a straight arm; that is a kill switch, not a gate.");
        }

        /// <summary>
        /// The elbow path inherits the elbow tracker's own plausibility check rather than trusting it
        /// blindly: an elbow parked somewhere no elbow can be must not drive the girdle. This reuses the
        /// existing `elbowTrust` rather than adding a second notion of the same thing.
        /// Mutation: set rhythmTrust = 1f on the elbow path. The implausible row then drives full rhythm.
        /// </summary>
        [Test]
        public void ElbowPath_IgnoresAnImplausibleElbowTracker()
        {
            var good = Input(120f, 0f, 1.0f, hasElbow: true, elev: 1f, prot: 1f);
            // Same direction, but far past where the upper arm could reach — a drifting or mis-seated puck.
            var bad = good;
            bad.ElbowPos = good.ElbowPos.normalized * (ElbowLen * 2.2f);

            float goodDeg = Solve(good).RhythmElevDeg;
            float badDeg = Solve(bad).RhythmElevDeg;
            Assert.That(goodDeg, Is.GreaterThan(3f), "the plausible elbow drove no rhythm; the test proves nothing.");
            Assert.That(badDeg, Is.LessThan(0.5f),
                $"an elbow {ElbowLen * 2.2f:0.00} m from the shoulder — well past the upper arm's {ElbowLen:0.00} m — " +
                $"still drove {badDeg:0.00} deg of rhythm.");
        }

        /// <summary>
        /// No pops. The girdle gradient is bounded everywhere EXCEPT the pre-existing FromToRotation
        /// antipode at the arm direction exactly opposite the bind, where a ~20 deg/deg step already exists
        /// with this term switched off (measured: 20.694 off, 20.807 on) and which is therefore not this
        /// term's to police. Scoped away from that band, exactly as the posterior-reach suite does.
        /// Mutation: replace RhythmProgress's C1 piecewise-quadratic with a step at the peak. Gradient
        /// jumps past the bar immediately.
        /// </summary>
        [Test]
        public void Rhythm_IsContinuous()
        {
            float worst = 0f, atTh = 0f, atPh = 0f;
            for (float ph = -170f; ph <= 170.001f; ph += 5f)
            {
                Quaternion prev = Quaternion.identity;
                bool has = false;
                for (float th = 0f; th <= 180.001f; th += 0.5f)
                {
                    var r = Solve(Input(th, ph, 0.95f, false, false, rhythm: true, shrug: true, retract: true));
                    if (has)
                    {
                        float grad = AngleDeg(prev, r.ShoulderRotation) / 0.5f;
                        if (grad > worst) { worst = grad; atTh = th; atPh = ph; }
                    }
                    prev = r.ShoulderRotation;
                    has = true;
                }
            }
            Assert.That(worst, Is.LessThan(1.5f),
                $"the girdle moved {worst:0.00} deg per deg of elevation at theta={atTh:0} phi={atPh:0} (a pop).");
        }

        /// <summary>
        /// HONESTY TEST — this is a posture feature and must never be credited with fixing an axial-roll
        /// defect. A pure humeral roll holds elevation and plane of elevation constant, so the rhythm's
        /// contribution to it is identically zero. Asserted rather than merely written down, because the
        /// claim is exactly the kind that gets quietly overstated later.
        /// </summary>
        [Test]
        public void PureAxialRoll_GetsExactlyNoRhythm()
        {
            // A REAL axial roll, not a trivial one: the elbow is bent, so the hand sits OFF the humeral
            // axis and orbits it as the humerus rolls, while the elbow — which lies ON the axis — does not
            // move at all. That is the gesture, and with an elbow tracker present the girdle must not
            // notice it. Rolling a hand that is already on the axis would be a tautology.
            // Mutation: take the driver from the hand even when an elbow tracker is present. The orbiting
            // hand then swings the girdle and this fails.
            for (float th = 30f; th <= 150.001f; th += 30f)
            {
                Vector3 axis = Dir(th, 0f, false);
                Vector3 perp = Vector3.Cross(axis, Vector3.up);
                if (perp.sqrMagnitude < 1e-4f)
                {
                    perp = Vector3.Cross(axis, Vector3.forward);
                }
                perp = perp.normalized;

                BasisShoulderSolveResult first = default;
                for (float roll = 0f; roll <= 315.001f; roll += 45f)
                {
                    var s = Input(th, 0f, 0.95f, hasElbow: true);
                    // Elbow on the axis (unmoved by the roll); hand at 90 deg of flexion, so it orbits.
                    s.ElbowPos = axis * ElbowLen;
                    s.HandTargetPos = s.ElbowPos + (Quaternion.AngleAxis(roll, axis) * perp) * (ArmLen - ElbowLen);
                    var res = Solve(s);
                    if (roll == 0f)
                    {
                        first = res;
                        Assert.That(first.RhythmElevDeg, Is.GreaterThan(1f),
                            $"theta={th}: no rhythm to hold constant in this pose, so the test proves nothing.");
                        continue;
                    }
                    Assert.That(res.RhythmElevDeg, Is.EqualTo(first.RhythmElevDeg).Within(0.001f),
                        $"theta={th} roll={roll}: a pure humeral axial roll changed the rhythm's elevation " +
                        $"({first.RhythmElevDeg:0.000} -> {res.RhythmElevDeg:0.000}). It is a 2-input model — " +
                        "elevation and plane of elevation — so it must contribute nothing to a roll, and must " +
                        "not be sold as a fix for any axial-roll defect.");
                    Assert.That(res.RhythmRetractDeg, Is.EqualTo(first.RhythmRetractDeg).Within(0.001f),
                        $"theta={th} roll={roll}: a pure humeral axial roll changed the rhythm's retraction.");
                }
            }
        }

        /// <summary>
        /// The measured before/after that justifies the feature existing at all, pinned so a regression that
        /// quietly restores the old behaviour is caught. Numbers are the live core at shipped defaults.
        /// </summary>
        [Test]
        public void BeforeAndAfter_TheGirdleActuallyMovesNow()
        {
            // CONTROLLER-ONLY WITH A STRAIGHT ARM — the configuration most users are actually in, and the
            // one where the hand direction IS the humerus direction, so the driver-trust bound is exactly 1
            // and the model runs at full strength with no tracker at all. Measured on the live core.
            //
            //     arm elevation   0     30    60    90    120    150    180
            //     before        1.73  0.75  0.08  0.00   0.37   3.01   6.93
            //     after         1.73  0.87  3.72  7.08  11.45  15.20  18.93
            foreach (var p in new[] { (60f, 1.0f, 3.0f), (90f, 0.5f, 6.0f), (120f, 1.5f, 10.0f) })
            {
                var off = Solve(Input(p.Item1, 0f, 1.0f, hasElbow: false, rhythm: false));
                var on = Solve(Input(p.Item1, 0f, 1.0f, hasElbow: false, rhythm: true));
                Assert.That(off.AppliedAngleDeg, Is.LessThan(p.Item2),
                    $"theta={p.Item1}: the OLD girdle contributed {off.AppliedAngleDeg:0.00} deg — the defect this " +
                    "feature exists for no longer reproduces, so these numbers are measuring the wrong thing.");
                Assert.That(on.AppliedAngleDeg, Is.GreaterThan(p.Item3),
                    $"theta={p.Item1}: the girdle contributed {on.AppliedAngleDeg:0.00} deg, under the {p.Item3} deg this " +
                    "model was measured to deliver at the shipped Elevation 0.4 default.");
            }

            // And an elbow tracker reaches the same place at the horizontal, from a driver that is immune
            // to elbow flexion entirely.
            Assert.That(Solve(Input(90f, 0f, 1.0f, hasElbow: true, rhythm: true)).RhythmElevDeg,
                Is.GreaterThan(6f), "the elbow-tracker path lost the rhythm at 90 deg of elevation.");
        }
    }
}
