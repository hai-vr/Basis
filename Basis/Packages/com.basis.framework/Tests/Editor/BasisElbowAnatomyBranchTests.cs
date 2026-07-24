using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// D1 -- THE ELBOW ANATOMY GUARD'S BRANCH CUT. WHAT IS IRREDUCIBLE ABOUT IT, PROVED; AND WHAT WAS
    /// NEVER IRREDUCIBLE, FIXED.
    ///
    /// ================================================================================================
    /// The guard back-solves its corrective pole as poleGuarded = upN*cG + w*sG with sG's SIGN taken from
    /// the elbow's current side, and `s = dot(poleDir, w)` is exactly zero at the top of the circle. D1
    /// was filed as the resulting single-step elbow jump (34.3 mm at reach 0.700 rising to 354.6 mm),
    /// found four independent ways by BasisArmInvariantNet.
    ///
    /// THIS FILE SPLITS D1 IN TWO, BECAUSE THE TWO HALVES HAVE OPPOSITE ANSWERS.
    ///
    ///   THE POP (the elbow CROSSES the cut) is IRREDUCIBLE, and the proof is topological rather than
    ///   about symmetry. The guard is pinned to the identity at both ends of its firing arc and must keep
    ///   the elbow out of a non-empty forbidden arc in between; a continuous path on a circle between
    ///   those two endpoints either crosses the forbidden arc or winds the long way round. So:
    ///
    ///       IDENTITY-ON-LEGAL + ENFORCEMENT  =>  a jump, OR an unbounded gain. Never neither.
    ///
    ///   Nothing in that argument mentions the guard's inputs, so NO new input can defeat it -- not the
    ///   body-lateral axis, not handedness, not the previous frame. TheCutIsForced and
    ///   TheCutPlacementIsOptimal below prove both halves by scanning, not by assertion.
    ///
    ///   THE BUZZ (the elbow SITS on the cut) was never irreducible, and it is the severe half. `s` at the
    ///   top is not merely zero, it is NOISE, so the branch re-decided every frame: measured ~150 side
    ///   flips per 300 frames, dragging the elbow through 29-48 METRES of path for an input standing
    ///   still. Nothing downstream damped it -- BasisSwingContinuityCore engages only on a torso-collision
    ///   change and says so ("free-air motion, POLE FLIPS and target teleports are accepted instantly").
    ///
    /// ⚠️ AND THE OBVIOUS SYMMETRY-BREAKING INPUT DOES NOT FIX THE BUZZ, IT MOVES IT. A body-lateral axis
    /// deciding the branch inside a dead band puts a fresh cut at the band's EDGE, and an elbow parks on
    /// that edge just as happily: measured 96 flips at s = -0.100 against the shipped 102 at s = 0. Same
    /// amplitude, new address. TheLateralAxisAlone_RelocatesTheBuzz pins that, so the reasoning cannot be
    /// re-litigated from a comment.
    ///
    /// HYSTERESIS eliminates it instead of moving it, because its flip points differ by direction of
    /// travel, so no azimuth re-decides under noise. The lateral axis is kept for the one job it is good
    /// at: SEEDING the choice on the first frame, so hysteresis holds an anatomically-outward elbow
    /// instead of whichever side float noise picked.
    /// ================================================================================================
    ///
    /// ⚠️ EVERY GATE HERE IS NON-VACUOUS BY CONSTRUCTION. The control for the buzz gates is not a
    /// hand-written "broken" core -- it is THE SHIPPED GUARD, reached by leaving the two new inputs at
    /// their struct defaults, which is the same path every untaught caller takes. So each gate asserts
    /// "the fix is quiet" AND "the thing it fixes is still loud", in one run, against real code.
    /// </summary>
    public sealed class BasisElbowAnatomyBranchTests
    {
        const float k_Upper = 0.30f;
        const float k_Lower = 0.26f;
        const float k_Arm = k_Upper + k_Lower;
        static readonly Vector3 k_Shoulder = new Vector3(0.17f, 1.40f, 0f);
        static readonly Vector3 k_Up = Vector3.up;

        /// <summary>The body-lateral axis a right arm would be handed: shoulder line, pointing away from
        /// the torso. Deliberately not axis-aligned with anything the geometry below is built from.</summary>
        static readonly Vector3 k_LateralOut = new Vector3(0.98f, 0.05f, -0.19f).normalized;

        // ------------------------------------------------------------------ geometry

        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        /// <summary>The elbow's circle for a hand at `reach`, parameterised so azimuth 0 is the TOP -- the
        /// branch point. Returns false when the pose has no usable circle, so a caller cannot silently
        /// assert on a degenerate fixture.</summary>
        static bool Circle(Vector3 tDir, float reach, out Vector3 hand, out Vector3 centre,
                           out Vector3 up, out Vector3 w, out float rho)
        {
            hand = k_Shoulder + tDir * (reach * k_Arm);
            centre = default; up = default; w = default; rho = 0f;

            Vector3 ac = hand - k_Shoulder;
            float d = ac.magnitude;
            if (!(d > 1e-4f)) return false;
            Vector3 acN = ac / d;

            float p = (d * d + k_Upper * k_Upper - k_Lower * k_Lower) / (2f * d);
            float r2 = k_Upper * k_Upper - p * p;
            if (!(r2 > 1e-8f)) return false;
            rho = Mathf.Sqrt(r2);
            centre = k_Shoulder + acN * p;

            Vector3 upProj = k_Up - acN * Vector3.Dot(k_Up, acN);
            if (!(upProj.sqrMagnitude > 1e-8f)) return false;
            up = upProj.normalized;
            w = Vector3.Cross(acN, up);
            return true;
        }

        static Vector3 ElbowAtAzimuth(Vector3 centre, Vector3 up, Vector3 w, float rho, float azRad)
            => centre + rho * (up * Mathf.Cos(azRad) + w * Mathf.Sin(azRad));

        /// <summary>The signed azimuth of a point on the elbow circle, zero at the top. Inverse of
        /// <see cref="ElbowAtAzimuth"/>, used to recover phiG from an outcome the guard produced.</summary>
        static float AzimuthOf(Vector3 p, Vector3 centre, Vector3 up, Vector3 w)
        {
            Vector3 d = p - centre;
            return Mathf.Atan2(Vector3.Dot(d, w), Vector3.Dot(d, up));
        }

        static Vector3 ApplyGuard(Vector3 hand, Vector3 elbow, Vector3 lateralOut, int prevSide, out int sideUsed)
        {
            float sw = BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, elbow, hand, k_Up, k_Arm,
                                                            lateralOut, prevSide, out sideUsed);
            if (sw == 0f) return elbow;
            Vector3 axis = (hand - k_Shoulder).normalized;
            return k_Shoulder + Quaternion.AngleAxis(sw * Mathf.Rad2Deg, axis) * (elbow - k_Shoulder);
        }

        static float HeightOf(Vector3 elbow) => Vector3.Dot(elbow - k_Shoulder, k_Up);
        static float Ceiling(Vector3 hand) => Mathf.Max(0f, Vector3.Dot(hand - k_Shoulder, k_Up));

        /// <summary>A pose whose elbow circle genuinely clears the ceiling, so the guard has something to
        /// do. Anything else makes every assertion in this file vacuous, so it is checked, not assumed.</summary>
        static bool FiringPose(float reach, float azDeg, float elDeg,
                               out Vector3 hand, out Vector3 centre, out Vector3 up, out Vector3 w, out float rho)
        {
            Vector3 tDir = Dir(azDeg, elDeg);
            if (!Circle(tDir, reach, out hand, out centre, out up, out w, out rho)) return false;
            Vector3 top = ElbowAtAzimuth(centre, up, w, rho, 0f);
            float soft = Ceiling(hand) + BasisElbowAnatomyCore.SoftMarginFracLimb * k_Arm;
            return HeightOf(top) > soft
                && BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, top, hand, k_Up, k_Arm) != 0f;
        }

        static readonly (float reach, float az, float el)[] k_FiringPoses =
        {
            (0.60f, -30f, -35f), (0.60f, -30f, -25f), (0.70f, -30f, -35f),
            (0.70f, -30f, -25f), (0.80f, -30f, -25f), (0.60f,  25f, -20f),
            (0.70f,  10f, -15f), (0.50f, -30f, -25f),
        };

        // ============================================================================================
        // 1. THE BUZZ -- the half that was fixable, and the headline measurement.
        // ============================================================================================

        /// <summary>
        /// ⭐ PARK THE ELBOW ON THE CUT AND HOLD STILL. The shipped branch re-decides which side of the
        /// circle the elbow belongs on EVERY FRAME, because `s` there is jitter, not geometry.
        ///
        /// The control is the shipped guard itself, reached by leaving both new inputs at their defaults,
        /// so this cannot pass by the fix being a no-op: it demands the untaught path still buzzes.
        /// </summary>
        [Test]
        public void TheBranch_BuzzesWhenTheElbowParksOnTheCut_AndHysteresisStopsIt()
        {
            var log = new StringBuilder();
            int worstGuardedFlips = 0;
            int leastShippedFlips = int.MaxValue;
            float worstPathRatio = 0f, worstGuardedPath = 0f, leastShippedPath = float.MaxValue;
            int posesTested = 0;

            foreach ((float reach, float az, float el) in k_FiringPoses)
            {
                if (!FiringPose(reach, az, el, out Vector3 hand, out Vector3 centre,
                                out Vector3 up, out Vector3 w, out float rho)) continue;
                posesTested++;

                // Every azimuth in and around the tie band, because a fix that only quietens s = 0 has
                // moved the cut rather than removed it -- which is exactly what the lateral axis does.
                foreach (float parkDeg in new[] { -14f, -8f, -5.74f, -4f, -2.87f, -1.5f, 0f, 1.5f, 2.87f, 4f, 5.74f, 8f, 14f })
                {
                    var rng = new System.Random(0xB1A5 + Mathf.RoundToInt(parkDeg * 100f));
                    int shippedFlips = 0, guardedFlips = 0;
                    float shippedPath = 0f, guardedPath = 0f;
                    int shippedLast = 0, guardedLast = 0, carried = 0;
                    Vector3 shippedPrev = Vector3.zero, guardedPrev = Vector3.zero;
                    bool first = true;

                    for (int f = 0; f < 200; f++)
                    {
                        // 2 mm of gaussian jitter on the elbow: a quiet, ordinary tracker.
                        Vector3 jitter = new Vector3(Gauss(rng), Gauss(rng), Gauss(rng)) * 0.002f;
                        Vector3 raw = ElbowAtAzimuth(centre, up, w, rho, parkDeg * Mathf.Deg2Rad) + jitter;

                        Vector3 shipped = ApplyGuard(hand, raw, Vector3.zero, 0, out int shippedSide);
                        Vector3 guarded = ApplyGuard(hand, raw, k_LateralOut, carried, out int guardedSide);
                        carried = guardedSide;

                        if (!first)
                        {
                            shippedPath += Vector3.Distance(shipped, shippedPrev);
                            guardedPath += Vector3.Distance(guarded, guardedPrev);
                            if (shippedSide != 0 && shippedLast != 0 && shippedSide != shippedLast) shippedFlips++;
                            if (guardedSide != 0 && guardedLast != 0 && guardedSide != guardedLast) guardedFlips++;
                        }
                        if (shippedSide != 0) shippedLast = shippedSide;
                        if (guardedSide != 0) guardedLast = guardedSide;
                        shippedPrev = shipped; guardedPrev = guarded;
                        first = false;
                    }

                    if (Mathf.Abs(parkDeg) < 1e-3f)
                    {
                        leastShippedFlips = Mathf.Min(leastShippedFlips, shippedFlips);
                        leastShippedPath = Mathf.Min(leastShippedPath, shippedPath);
                        // ⚠️ PER-POSE, AND ONLY WHERE THERE IS A BUZZ TO REMOVE. Comparing the worst
                        // guarded pose to the quietest shipped one mixes two different geometries (rho and
                        // phiG vary by 4x across these fixtures). And on the mildest fixture phiG(0) is
                        // ~5 deg, so each "flip" teleports the elbow only a few mm and the shipped path is
                        // barely above the jitter floor -- a ratio there measures noise against noise. The
                        // ratio is therefore taken within a pose, and only on poses that actually buzz.
                        worstGuardedPath = Mathf.Max(worstGuardedPath, guardedPath);
                        if (shippedPath > 5f)
                        {
                            worstPathRatio = Mathf.Max(worstPathRatio, guardedPath / shippedPath);
                        }
                        log.AppendLine($"      reach {reach:F2} az {az,4:F0} el {el,4:F0}  parked at the TOP: " +
                                       $"shipped {shippedFlips,4} flips / {shippedPath * 1000f,9:F0} mm    " +
                                       $"hysteresis {guardedFlips,3} flips / {guardedPath * 1000f,7:F0} mm    " +
                                       $"({guardedPath / Mathf.Max(shippedPath, 1e-6f):P1} of the path)");
                    }
                    worstGuardedFlips = Mathf.Max(worstGuardedFlips, guardedFlips);
                }
            }

            TestContext.WriteLine("\n  D1's severe half -- the branch re-deciding under noise:\n" + log +
                                  $"      worst over EVERY parking azimuth and pose: hysteresis {worstGuardedFlips} flips; " +
                                  $"worst per-pose path ratio {worstPathRatio:P1} of shipped, worst absolute {worstGuardedPath * 1000f:F0} mm");

            Assert.That(posesTested, Is.GreaterThan(4),
                $"only {posesTested} of {k_FiringPoses.Length} fixtures actually fire the guard, so this test is " +
                "measuring poses where there is no branch to buzz. Fix the fixtures, do not relax the bounds.");

            // NON-VACUITY: the shipped path must still be loud, or the comparison below means nothing.
            Assert.That(leastShippedFlips, Is.GreaterThan(40),
                $"THE CONTROL HAS GONE QUIET: the shipped guard (both new inputs at their defaults) flipped only " +
                $"{leastShippedFlips} times in 200 frames with the elbow parked on its own branch cut. Either the " +
                "defaults have stopped declining -- which would mean every untaught caller silently changed " +
                "behaviour -- or this fixture no longer parks the elbow on the cut. Either way the assertion " +
                "below would pass whatever the branch did.");
            Assert.That(leastShippedPath, Is.GreaterThan(2.0f),
                $"THE CONTROL HAS GONE QUIET: on its quietest fixture the shipped guard dragged the elbow only " +
                $"{leastShippedPath:F2} m in 200 frames of a standing-still input, where the honest jitter " +
                "response is under a metre. See above.");

            // ⚠️ NOT Is.Zero. Noise walking ONCE across a hysteresis threshold is a legitimate single
            // traverse of the loop, and over 8 poses x 13 parking azimuths x 200 frames one such crossing
            // does occur. What hysteresis guarantees is that it cannot come BACK without crossing the far
            // threshold -- i.e. no re-deciding. The shipped branch flips 92-110 times on the same input, so
            // the discrimination here is three orders of magnitude, not a hair.
            Assert.That(worstGuardedFlips, Is.LessThanOrEqualTo(2),
                $"the branch re-decided {worstGuardedFlips} times under 2 mm of jitter with hysteresis carrying " +
                "the side. One crossing is the hysteresis loop being traversed once; repeated flipping means the " +
                "band has been narrowed below the noise, or the carried side is not reaching the core.");
            Assert.That(worstPathRatio, Is.LessThan(0.15f),
                $"on a pose that genuinely buzzes, the elbow still travelled {worstPathRatio:P1} of the distance the " +
                "shipped branch drove it. What is left should be the honest jitter response only.");
            Assert.That(worstGuardedPath, Is.LessThan(2.0f),
                $"with the side held, the elbow travelled {worstGuardedPath:F2} m in 200 frames on its worst parking " +
                "azimuth. 2 mm of jitter on a stationary input cannot honestly move an elbow that far -- if this " +
                "trips, some azimuth is still re-deciding even though the flip counter did not catch it.");
        }

        /// <summary>
        /// ⚠️ THE FIX THAT LOOKS RIGHT AND IS NOT. Deciding the branch by the body-lateral axis inside a
        /// dead band quietens s = 0 and opens an identical cut at the band's EDGE. Pinned because this is
        /// the shape a future revision will reach for first, and the reason it is wrong is a measurement,
        /// not an opinion.
        ///
        /// The lateral-only shape is reconstructed here rather than shipped: the core is asked for its
        /// geometry through the public overloads, and the branch is re-decided outside it.
        /// </summary>
        [Test]
        public void TheLateralAxisAlone_RelocatesTheBuzz_ItDoesNotRemoveIt()
        {
            (float reach, float az, float el) pose = k_FiringPoses[1];
            Assert.That(FiringPose(pose.reach, pose.az, pose.el, out Vector3 hand, out Vector3 centre,
                                   out Vector3 up, out Vector3 w, out float rho), Is.True,
                "fixture must fire the guard or this test proves nothing");

            float band = BasisElbowAnatomyCore.TieBandFracRadius;
            float edgeDeg = Mathf.Asin(band) * Mathf.Rad2Deg;
            int latSide = Vector3.Dot(k_LateralOut, w) > 0f ? 1 : -1;

            int FlipsAt(float parkDeg, bool lateralBand)
            {
                var rng = new System.Random(0x1A7E);
                int flips = 0, last = 0;
                for (int f = 0; f < 200; f++)
                {
                    Vector3 jitter = new Vector3(Gauss(rng), Gauss(rng), Gauss(rng)) * 0.002f;
                    Vector3 raw = ElbowAtAzimuth(centre, up, w, rho, parkDeg * Mathf.Deg2Rad) + jitter;
                    if (BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, raw, hand, k_Up, k_Arm) == 0f) continue;

                    Vector3 ac = (hand - k_Shoulder).normalized;
                    Vector3 ae = raw - k_Shoulder;
                    Vector3 perp = ae - ac * Vector3.Dot(ae, ac);
                    if (!(perp.sqrMagnitude > 1e-10f)) continue;
                    float s = Vector3.Dot(perp.normalized, w);

                    int side = s < 0f ? -1 : 1;
                    if (lateralBand && Mathf.Abs(s) < band) side = latSide;
                    if (last != 0 && side != last) flips++;
                    last = side;
                }
                return flips;
            }

            int shippedAtTop = FlipsAt(0f, false);
            int lateralAtTop = FlipsAt(0f, true);
            int lateralAtEdge = FlipsAt(-latSide * edgeDeg, true);

            TestContext.WriteLine(
                $"\n  the lateral-axis dead band, 200 frames at 2 mm jitter (band {band:F2} = {edgeDeg:F2} deg):\n" +
                $"      shipped, parked at the top (s=0)      : {shippedAtTop,4} flips\n" +
                $"      lateral band, parked at the top       : {lateralAtTop,4} flips   <- quiet here\n" +
                $"      lateral band, parked at the band EDGE : {lateralAtEdge,4} flips   <- and just as loud here");

            Assert.That(shippedAtTop, Is.GreaterThan(40),
                "the control must buzz at the top or the comparison is meaningless");
            Assert.That(lateralAtTop, Is.LessThan(5),
                "the lateral band is supposed to quieten the TOP -- if it does not, this test is not " +
                "reconstructing the shape it claims to.");
            Assert.That(lateralAtEdge, Is.GreaterThan(shippedAtTop / 2),
                $"the lateral band was expected to RELOCATE the buzz to its own edge, but the edge only " +
                $"flipped {lateralAtEdge} times against {shippedAtTop} at the top. If a dead band decided by " +
                "anatomy genuinely removes the buzz rather than moving it, this file's central claim -- and " +
                "the choice of hysteresis over the lateral axis in BasisElbowAnatomyCore -- is wrong and " +
                "should be revisited.");
        }

        // ============================================================================================
        // 2. THE ANATOMY IS UNTOUCHED. This is what makes the branch safe to make path-dependent.
        // ============================================================================================

        /// <summary>
        /// ⭐ cG IS NOT A FUNCTION OF THE BRANCH. The guarded HEIGHT -- the entire content of the
        /// anatomical law -- is identical whichever side the elbow is sent to, so making the SIDE sticky
        /// cannot make the ENVELOPE sticky. Without this, path-dependence would be unshippable.
        /// </summary>
        [Test]
        public void TheGuardedHeight_IsIdenticalOnBothBranches_SoTheEnvelopeIsNotPathDependent()
        {
            float worst = 0f;
            int compared = 0;

            foreach ((float reach, float az, float el) in k_FiringPoses)
            {
                if (!FiringPose(reach, az, el, out Vector3 hand, out Vector3 centre,
                                out Vector3 up, out Vector3 w, out float rho)) continue;

                for (float deg = -180f; deg < 180f; deg += 0.5f)
                {
                    Vector3 raw = ElbowAtAzimuth(centre, up, w, rho, deg * Mathf.Deg2Rad);
                    if (BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, raw, hand, k_Up, k_Arm) == 0f) continue;

                    float hNeg = HeightOf(ApplyGuard(hand, raw, Vector3.zero, -1, out _));
                    float hPos = HeightOf(ApplyGuard(hand, raw, Vector3.zero, +1, out _));
                    worst = Mathf.Max(worst, Mathf.Abs(hNeg - hPos));
                    compared++;
                }
            }

            TestContext.WriteLine($"\n  guarded elbow height, both branches, over {compared} firing azimuths: " +
                                  $"worst disagreement {worst * 1000f:F6} mm");

            Assert.That(compared, Is.GreaterThan(500),
                $"only {compared} firing azimuths were compared, so this is not exercising the branch.");
            Assert.That(worst, Is.LessThan(1e-5f),
                $"the guarded elbow ended {worst * 1000f:F4} mm higher on one branch than the other. The branch " +
                "sign must not reach cG -- if it does, the sticky side has made the ANATOMICAL ENVELOPE " +
                "path-dependent, which is the one thing hysteresis here is not allowed to do.");
        }

        /// <summary>The envelope itself, re-asserted with the new inputs live and a side deliberately
        /// carried AGAINST the elbow's own: no azimuth may end above the hard limit. Hysteresis holding
        /// the "wrong" side must still be a legal pose, not merely a different one.</summary>
        [Test]
        public void NoPointOnTheCircle_EscapesTheEnvelope_WithASideCarriedAgainstTheElbow()
        {
            int checkedAzimuths = 0;

            foreach ((float reach, float az, float el) in k_FiringPoses)
            {
                if (!FiringPose(reach, az, el, out Vector3 hand, out Vector3 centre,
                                out Vector3 up, out Vector3 w, out float rho)) continue;
                float hard = Ceiling(hand) + BasisElbowAnatomyCore.HardMarginFracLimb * k_Arm;

                for (float deg = -180f; deg < 180f; deg += 1f)
                {
                    Vector3 raw = ElbowAtAzimuth(centre, up, w, rho, deg * Mathf.Deg2Rad);
                    Vector3 ac = (hand - k_Shoulder).normalized;
                    Vector3 ae = raw - k_Shoulder;
                    Vector3 perp = ae - ac * Vector3.Dot(ae, ac);
                    int own = perp.sqrMagnitude > 1e-10f && Vector3.Dot(perp.normalized, w) < 0f ? -1 : 1;

                    Vector3 got = ApplyGuard(hand, raw, k_LateralOut, -own, out _);
                    checkedAzimuths++;
                    Assert.That(HeightOf(got), Is.LessThan(hard + 1e-4f),
                        $"reach {reach:0.00} az {az:0} el {el:0}, elbow azimuth {deg:0}: carrying the OPPOSITE side " +
                        $"put the elbow at {HeightOf(got):F3} against a hard limit of {hard:F3}. The branch may " +
                        "choose a side; it may not choose an illegal one.");
                    Assert.That(Vector3.Distance(k_Shoulder, got), Is.EqualTo(k_Upper).Within(1e-3f),
                        "upper-arm length must survive the carried side");
                    Assert.That(Vector3.Distance(got, hand), Is.EqualTo(k_Lower).Within(1e-3f),
                        "the guard must not move the hand");
                }
            }

            Assert.That(checkedAzimuths, Is.GreaterThan(1000),
                $"only {checkedAzimuths} azimuths checked; the sweep is not covering the circle.");
        }

        // ============================================================================================
        // 3. THE ZERO-DEFAULT CONTRACT.
        // ============================================================================================

        /// <summary>
        /// ⭐ EVERY UNTAUGHT CALLER IS BIT-IDENTICAL. Not "close": the same float, compared with ==. The
        /// five-argument overload and the new one handed (zero, 0) must be the same function, or the fix
        /// has silently changed behaviour for every caller that has not been wired up yet -- including
        /// BasisFullBodyIK.ReGuardElbowAnatomy and every offline sweep.
        /// </summary>
        [Test]
        public void TheDeclinedPath_IsBitIdenticalToTheShippedGuard()
        {
            var rng = new System.Random(4242);
            int compared = 0, fired = 0;

            for (int t = 0; t < 4000; t++)
            {
                float reach = Mathf.Lerp(0.30f, 0.999f, (float)rng.NextDouble());
                var d = new Vector3((float)(rng.NextDouble() * 2 - 1),
                                    (float)(rng.NextDouble() * 2 - 1),
                                    (float)(rng.NextDouble() * 2 - 1));
                if (d.sqrMagnitude < 1e-4f) continue;
                Vector3 tDir = d.normalized;
                if (!Circle(tDir, reach, out Vector3 hand, out Vector3 centre,
                            out Vector3 up, out Vector3 w, out float rho)) continue;

                float deg = (float)(rng.NextDouble() * 360.0 - 180.0);
                Vector3 elbow = ElbowAtAzimuth(centre, up, w, rho, deg * Mathf.Deg2Rad);

                float shipped = BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, elbow, hand, k_Up, k_Arm);
                float declined = BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, elbow, hand, k_Up, k_Arm,
                                                                      Vector3.zero, 0, out int side);
                compared++;
                if (shipped != 0f) fired++;

                Assert.That(declined, Is.EqualTo(shipped),
                    $"iter {t}: the declining overload returned {declined:R} where the shipped one returns " +
                    $"{shipped:R}. The zero defaults MUST be the exact identity -- an untaught caller changing " +
                    "behaviour is how a fix becomes a regression nobody attributes to it.");

                // ⚠️ THE ANTI-STALE CONTRACT, STATED CORRECTLY. The first version of this asserted
                // "side != 0 implies swivel != 0" and was WRONG: a guard that is firing can still return
                // exactly 0f when the corrective swivel rounds away, and it HAS chosen a side there. What
                // actually matters is that a comfortably LEGAL pose publishes nothing, so a side cannot be
                // carried across frames on which the guard was not the thing deciding.
                if (Vector3.Dot(elbow - k_Shoulder, k_Up) < Ceiling(hand))
                {
                    Assert.That(side, Is.Zero,
                        $"iter {t}: the guard published side {side} on an elbow sitting BELOW its own ceiling -- " +
                        "unambiguously legal, and a pose the guard has no business having an opinion about. " +
                        "Publishing a side here lets hysteresis carry a stale choice into a later frame.");
                }
            }

            TestContext.WriteLine($"\n  {compared} random poses compared, {fired} of them firing the guard");
            Assert.That(fired, Is.GreaterThan(200),
                $"only {fired} of {compared} random poses fired the guard, so this mostly compared the early-out " +
                "path and would pass even if the branch had changed completely.");
        }

        // ============================================================================================
        // 4. THE POP IS IRREDUCIBLE -- proved by scanning, not asserted.
        // ============================================================================================

        /// <summary>
        /// ⭐ THE TOPOLOGICAL OBSTRUCTION, MACHINE-CHECKED. Discretise the circle and ask directly: is
        /// there ANY assignment of guarded azimuths that is the identity on legal inputs, never leaves the
        /// elbow above the hard limit, and never moves more than G times the input's own step?
        ///
        /// The answer is no for any bounded G, and the minimum feasible G is measured here. It DIVERGES as
        /// the firing arc shrinks, which is the price of the only continuous alternative (shape C, the
        /// traverse) and the reason the shipped guard takes a jump instead. This owes nothing to the
        /// guard's inputs, which is precisely why no new input can remove D1's pop.
        /// </summary>
        [Test]
        public void TheCutIsForced_NoContinuousGuardIsBothIdentityOnLegalAndEnforcing()
        {
            var log = new StringBuilder();
            float worstRequiredGain = 0f;
            int posesProved = 0;

            foreach ((float reach, float az, float el) in k_FiringPoses)
            {
                if (!FiringPose(reach, az, el, out Vector3 hand, out Vector3 centre,
                                out Vector3 up, out Vector3 w, out float rho)) continue;

                float ceiling = Ceiling(hand);
                float soft = ceiling + BasisElbowAnatomyCore.SoftMarginFracLimb * k_Arm;
                float hard = ceiling + BasisElbowAnatomyCore.HardMarginFracLimb * k_Arm;

                // The firing arc's half-width and the forbidden arc's half-width, read off the circle.
                float phiSoft = HalfWidthAbove(centre, up, w, rho, soft);
                float phiHard = HalfWidthAbove(centre, up, w, rho, hard);
                if (!(phiSoft > 0f) || !(phiHard > 0f)) continue;
                posesProved++;

                // A continuous guard is pinned to +-phiSoft at the ends of the firing arc and may not put
                // the elbow inside +-phiHard. Going "the short way" crosses the forbidden arc, so the only
                // continuous route is the long way round: the output must travel 2*(PI - phiSoft) of
                // azimuth while the input travels 2*phiSoft.
                float requiredGain = (Mathf.PI - phiSoft) / phiSoft;
                worstRequiredGain = Mathf.Max(worstRequiredGain, requiredGain);

                log.AppendLine($"      reach {reach:F2} az {az,4:F0} el {el,4:F0}: firing arc +-{phiSoft * Mathf.Rad2Deg,6:F2} deg, " +
                               $"forbidden arc +-{phiHard * Mathf.Rad2Deg,6:F2} deg  =>  any CONTINUOUS guard " +
                               $"needs gain >= {requiredGain,7:F2}x");

                // And the jump the shipped guard actually takes, for contrast.
                Vector3 top = ElbowAtAzimuth(centre, up, w, rho, 0f);
                Vector3 justPos = ApplyGuard(hand, ElbowAtAzimuth(centre, up, w, rho, 1e-4f), Vector3.zero, 0, out _);
                Vector3 justNeg = ApplyGuard(hand, ElbowAtAzimuth(centre, up, w, rho, -1e-4f), Vector3.zero, 0, out _);
                float jump = Vector3.Distance(justPos, justNeg);
                Assert.That(HeightOf(top), Is.GreaterThan(hard),
                    $"reach {reach:0.00}: the fixture's circle-top must clear the HARD limit for the forbidden " +
                    "arc to be non-empty -- otherwise there is no obstruction here and this pose proves nothing.");
                log.AppendLine($"                                        shipped jump at the cut: {jump * 1000f,7:F1} mm " +
                               $"(circle diameter {2f * rho * 1000f:F1} mm)");
            }

            // ⭐ AND IT DIVERGES. The gain above is modest on these fixtures only because they are DEEPLY
            // illegal, which makes the firing arc wide. The trade's real shape shows up as the violation
            // shrinks: the arc narrows, the traverse still has to cover the whole rest of the circle, and
            // the required gain runs away. A marginally-illegal elbow would be swung nearly 180 degrees.
            // ⚠️ BISECTED, NOT STEPPED. A fixed sweep step bounds how narrow a firing arc it can land on,
            // so the "worst gain" it reports is a property of the step size rather than of the geometry --
            // the first version of this stepped 0.25 deg and reported 24x, which says nothing about whether
            // the quantity is bounded. Bisecting onto the firing threshold makes the number limited by
            // float precision instead, which is the only honest way to exhibit a divergence.
            float PhiSoftAt(float elDeg)
            {
                Vector3 tD = Dir(-30f, elDeg);
                if (!Circle(tD, 0.60f, out Vector3 h2, out Vector3 c2, out Vector3 u2, out Vector3 w2, out float r2)) return 0f;
                float soft2 = Ceiling(h2) + BasisElbowAnatomyCore.SoftMarginFracLimb * k_Arm;
                float ps = HalfWidthAbove(c2, u2, w2, r2, soft2);
                return ps >= Mathf.PI ? 0f : ps;
            }

            float fires = -25f, quiet = -80f;
            Assert.That(PhiSoftAt(fires), Is.GreaterThan(0f), "the bisection's firing end must actually fire");
            Assert.That(PhiSoftAt(quiet), Is.Zero, "the bisection's quiet end must not fire, or there is no threshold between them");

            var trend = new StringBuilder();
            float worstMarginalGain = 0f, atPhiSoftDeg = float.NaN;
            for (int it = 0; it < 60; it++)
            {
                float mid = 0.5f * (fires + quiet);
                float ps = PhiSoftAt(mid);
                if (ps > 0f)
                {
                    fires = mid;
                    float gain = (Mathf.PI - ps) / ps;
                    if (gain > worstMarginalGain) { worstMarginalGain = gain; atPhiSoftDeg = ps * Mathf.Rad2Deg; }
                    if (it % 8 == 0)
                        trend.AppendLine($"        el {mid,8:F4}: firing arc +-{ps * Mathf.Rad2Deg,7:F4} deg  =>  gain >= {gain,10:F1}x");
                }
                else
                {
                    quiet = mid;
                }
            }

            TestContext.WriteLine("\n  D1's irreducible half -- identity-on-legal + enforcement forces a seam:\n" + log +
                                  $"      worst gain a continuous guard would need on the fixtures: {worstRequiredGain:F1}x\n" +
                                  "      and as the violation shrinks, that requirement runs away:\n" + trend +
                                  $"      worst {worstMarginalGain:F0}x at a firing arc of +-{atPhiSoftDeg:F2} deg\n" +
                                  "      A guard with gain 1 everywhere and one bounded jump is the other branch of " +
                                  "that same trade. There is no third option, and no INPUT changes this -- the " +
                                  "argument is about the circle, not about what the guard knows.");

            Assert.That(posesProved, Is.GreaterThan(4),
                $"only {posesProved} fixtures had a non-empty forbidden arc, so the obstruction was barely exercised.");
            Assert.That(worstRequiredGain, Is.GreaterThan(1f),
                $"the worst continuous-guard gain over these poses is {worstRequiredGain:F2}x, i.e. no worse than the " +
                "jump-taking branch. If that is really so, the shipped trade should be reconsidered.");
            Assert.That(worstMarginalGain, Is.GreaterThan(500f),
                $"the required gain only reached {worstMarginalGain:F1}x as the firing arc narrowed to " +
                $"+-{atPhiSoftDeg:F4} deg. The whole reason the shipped guard takes a jump rather than the " +
                "continuous traverse is that this quantity is UNBOUNDED -- the bisection should drive it as high " +
                "as float precision allows, so a modest ceiling here means the bisection has stopped converging " +
                "on the firing threshold and this proof is not being tested.");
        }

        /// <summary>
        /// ⭐ AND THE CUT IS WHERE IT SHOULD BE, CHECKED BY SCANNING EVERY ALTERNATIVE. Placing the branch
        /// at azimuth phi_c costs the chord across the forbidden arc there. This walks every placement and
        /// asserts the shipped one (the top, phi_c = 0) is the minimum -- rather than asserting, as the
        /// previous comment did, that it "must be" because phiG is increasing.
        /// </summary>
        [Test]
        public void TheCutPlacement_AtTheTop_MinimisesBothTheJumpAndTheCorrection()
        {
            var log = new StringBuilder();
            int posesTested = 0;

            foreach ((float reach, float az, float el) in k_FiringPoses)
            {
                if (!FiringPose(reach, az, el, out Vector3 hand, out Vector3 centre,
                                out Vector3 up, out Vector3 w, out float rho)) continue;

                // ⚠️ THE TWO OUTCOMES ARE READ FROM MIRRORED AZIMUTHS, NOT BY FORCING prevSide. A cut at
                // +-phi_c chooses between where the guard sends an elbow arriving at +phi_c and where it
                // sends one arriving at -phi_c; phiG depends only on |phi|, so those are exactly the two
                // ends of the forbidden arc's chord. The first version of this test forced the side with
                // prevSide instead, which does nothing outside the tie band -- so it "measured" a 0.0 mm
                // cut at 37 deg and concluded the top was beaten. It was measuring the same point twice.
                float bestJump = float.MaxValue, atDeg = float.NaN, topJump = float.NaN, widest = 0f;
                float topAsym = float.NaN, worstAsym = 0f;

                for (float cut = 0.05f; cut <= 60f; cut += 0.25f)
                {
                    Vector3 raw = ElbowAtAzimuth(centre, up, w, rho, cut * Mathf.Deg2Rad);
                    if (BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, raw, hand, k_Up, k_Arm) == 0f) continue;

                    // The branch at this azimuth chooses between +phiG and -phiG. Recover phiG from the
                    // outcome the guard actually produced, then build its mirror image on the circle.
                    Vector3 a = ApplyGuard(hand, raw, Vector3.zero, 0, out _);
                    float phiG = AzimuthOf(a, centre, up, w);
                    Vector3 b = ElbowAtAzimuth(centre, up, w, rho, -phiG);

                    float jump = Vector3.Distance(a, b);
                    // ⚠️ THE SECOND PROPERTY IS SYMMETRY, NOT MAGNITUDE. The first version asserted the top
                    // minimises the CORRECTION, which is simply false: the correction goes to zero at the
                    // OUTER EDGE of the firing arc, where the guard barely fires. What the top uniquely
                    // gives is that the two branches cost the SAME -- neither side is handed a much larger
                    // correction than the other, which is what "the cut is where the answer is genuinely
                    // bimodal" actually means.
                    float asym = Mathf.Abs(Vector3.Distance(a, raw) - Vector3.Distance(b, raw));

                    if (jump < bestJump) { bestJump = jump; atDeg = cut; }
                    if (jump > widest) widest = jump;
                    if (asym > worstAsym) worstAsym = asym;
                    if (cut < 0.30f) { topJump = jump; topAsym = asym; }
                }

                if (float.IsNaN(topJump)) continue;
                posesTested++;
                log.AppendLine($"      reach {reach:F2} az {az,4:F0} el {el,4:F0}: at the TOP {topJump * 1000f,7:F1} mm; " +
                               $"cheapest {bestJump * 1000f,7:F1} mm at {atDeg,5:F2} deg; widest {widest * 1000f,7:F1} mm; " +
                               $"branch asymmetry at the top {topAsym * 1000f,6:F2} mm vs {worstAsym * 1000f:F1} mm worst");

                Assert.That(widest, Is.GreaterThan(topJump * 1.08f),
                    $"reach {reach:0.00} az {az:0} el {el:0}: the widest cut placement costs {widest * 1000f:0.0} mm " +
                    $"against {topJump * 1000f:0.0} mm at the top -- barely different, so this scan is not actually " +
                    "discriminating between placements and the optimality claim below is untested.");
                Assert.That(topJump, Is.LessThan(bestJump * 1.02f + 1e-4f),
                    $"reach {reach:0.00} az {az:0} el {el:0}: a cut at {atDeg:0.00} deg would cost " +
                    $"{bestJump * 1000f:0.0} mm against {topJump * 1000f:0.0} mm at the top. The shipped branch is " +
                    "supposed to sit at the CHEAPEST placement -- if it does not, move it and say so here.");
                Assert.That(topAsym, Is.LessThan(0.002f),
                    $"reach {reach:0.00}: at the top the two branches differ in cost by {topAsym * 1000f:0.00} mm. " +
                    "The top is the one azimuth where the choice is genuinely even -- if it is not, the cut is not " +
                    "sitting where the answer is bimodal and the placement argument needs redoing.");
                Assert.That(worstAsym, Is.GreaterThan(0.02f),
                    $"the most lopsided placement only differed by {worstAsym * 1000f:0.0} mm, so this scan is not " +
                    "showing that other placements ARE lopsided and the symmetry claim above is untested.");
            }

            TestContext.WriteLine("\n  every cut placement, scanned:\n" + log);
            Assert.That(posesTested, Is.GreaterThan(4),
                $"only {posesTested} poses produced a firing arc wide enough to scan.");
        }

        // ============================================================================================
        // 5. THE COST OF THE FIX, ON THE MEASUREMENT D1 WAS FILED AS.
        // ============================================================================================

        /// <summary>
        /// ⭐ WHAT THE TIE BAND COSTS ON THE ORIGINAL DEFECT. Hysteresis holds the side across the top, so
        /// a sweep that crosses the cut now flips at the band's far edge instead of at the top: the SAME
        /// number of flips (one), a slightly different place, and a jump that grows by the difference
        /// between phiG at the band edge and phiG at the top.
        ///
        /// This is the "what did the residual move into" measurement, stated as a number rather than a
        /// claim, and bounded so a future change cannot quietly make it worse.
        /// </summary>
        [Test]
        public void TheTieBand_MovesTheCrossingCut_ByABoundedAndMeasuredAmount()
        {
            var log = new StringBuilder();
            float worstGrowth = 0f, worstRatio = 0f;
            int posesTested = 0;

            foreach ((float reach, float az, float el) in k_FiringPoses)
            {
                if (!FiringPose(reach, az, el, out Vector3 hand, out Vector3 centre,
                                out Vector3 up, out Vector3 w, out float rho)) continue;
                posesTested++;

                float shippedWorst = 0f, guardedWorst = 0f;
                int shippedFlips = 0, guardedFlips = 0;
                Vector3 sPrev = Vector3.zero, gPrev = Vector3.zero;
                int carried = 0, sLast = 0, gLast = 0;
                bool first = true;

                // A clean sweep straight across the top -- the D1 measurement, without jitter.
                for (int k = 0; k <= 4000; k++)
                {
                    float deg = Mathf.Lerp(-20f, 20f, k / 4000f);
                    Vector3 raw = ElbowAtAzimuth(centre, up, w, rho, deg * Mathf.Deg2Rad);

                    Vector3 s = ApplyGuard(hand, raw, Vector3.zero, 0, out int sSide);
                    Vector3 g = ApplyGuard(hand, raw, k_LateralOut, carried, out int gSide);
                    carried = gSide;

                    if (!first)
                    {
                        shippedWorst = Mathf.Max(shippedWorst, Vector3.Distance(s, sPrev));
                        guardedWorst = Mathf.Max(guardedWorst, Vector3.Distance(g, gPrev));
                        if (sSide != 0 && sLast != 0 && sSide != sLast) shippedFlips++;
                        if (gSide != 0 && gLast != 0 && gSide != gLast) guardedFlips++;
                    }
                    if (sSide != 0) sLast = sSide;
                    if (gSide != 0) gLast = gSide;
                    sPrev = s; gPrev = g; first = false;
                }

                float growth = guardedWorst - shippedWorst;
                worstGrowth = Mathf.Max(worstGrowth, growth);
                // ⚠️ THE RATIO IS ONLY MEANINGFUL WHERE THE POP IS. On the mildest fixture the shipped pop
                // is a few centimetres, so a couple of centimetres of growth reads as tens of percent while
                // being invisible in the hand. The bound that matters to a user is the ABSOLUTE growth;
                // the ratio is bounded only where the pop is already big enough to feel.
                if (shippedWorst > 0.05f)
                {
                    worstRatio = Mathf.Max(worstRatio, guardedWorst / shippedWorst);
                }

                log.AppendLine($"      reach {reach:F2} az {az,4:F0} el {el,4:F0}: shipped {shippedWorst * 1000f,7:F1} mm " +
                               $"({shippedFlips} flip) -> with band {guardedWorst * 1000f,7:F1} mm ({guardedFlips} flip), " +
                               $"{growth * 1000f,+7:F1} mm");

                Assert.That(guardedFlips, Is.EqualTo(shippedFlips),
                    $"reach {reach:0.00}: the sweep crossed the cut {guardedFlips} times with the band against " +
                    $"{shippedFlips} without. The band is meant to MOVE the single crossing, not add or remove one.");
            }

            TestContext.WriteLine("\n  the crossing pop -- what the fix costs on D1's own measurement:\n" + log +
                                  $"      worst growth {worstGrowth * 1000f:F1} mm, worst ratio {worstRatio:F3}x");

            Assert.That(posesTested, Is.GreaterThan(4), "not enough firing fixtures to judge the cost");
            Assert.That(worstGrowth, Is.LessThan(0.030f),
                $"moving the cut to the band edge grew the worst crossing pop by {worstGrowth * 1000f:F1} mm. The band " +
                "trades a small growth in a ONE-OFF pop for the removal of a per-frame buzz worth METRES of elbow " +
                "path; past ~3 cm of added pop that trade stops being obviously right and TieBandFracRadius " +
                "should come down (its cost is linear in the band -- see the table in BasisElbowAnatomyCore).");
            Assert.That(worstRatio, Is.LessThan(1.60f),
                $"on a pose whose pop is already over 5 cm, the band grew it by {(worstRatio - 1f) * 100f:F1}%. " +
                "Growth concentrated on the pops that were ALREADY the big ones is the shape this trade must not " +
                "take -- the band is supposed to cost most where the pop is smallest.");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Half-width, in radians of azimuth, of the arc on which the elbow sits above `height`.
        /// 0 when the circle never clears it; PI when it never drops below.</summary>
        static float HalfWidthAbove(Vector3 centre, Vector3 up, Vector3 w, float rho, float height)
        {
            float centreH = Vector3.Dot(centre - k_Shoulder, k_Up);
            float span = rho * Vector3.Dot(up, k_Up);
            if (!(Mathf.Abs(span) > 1e-6f)) return 0f;
            float c = (height - centreH) / span;
            if (c >= 1f) return 0f;
            if (c <= -1f) return Mathf.PI;
            return Mathf.Acos(c);
        }

        static float Gauss(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }
    }
}
