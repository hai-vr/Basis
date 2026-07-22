using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// AN ELBOW CANNOT POINT AT THE SKY.
    ///
    /// The report: "the arms are able to get in rotations that are not possible, I'm still seeing the
    /// occasional arm bending up (so the elbows point to the sky)."
    ///
    /// The KNEE has been hard-guarded into its anatomical half-space for a long time (BasisLegSolveCore:
    /// "a knee behind that axis is not unnatural, it is anatomically unrepresentable"). THE ARM HAD NO SUCH
    /// GUARD AT ALL -- nothing stopped the solver placing the elbow anywhere on its circle, including straight
    /// up. BasisElbowAnatomyCore closes that, and this file is the proof, in both directions:
    ///
    ///   * it makes the impossible pose UNREACHABLE, and
    ///   * it leaves every possible pose BYTE FOR BYTE untouched.
    ///
    /// The second half matters as much as the first. A guard that perturbs legal poses to fix illegal ones is
    /// the wrong trade, and it is how guards end up being deleted by the next person.
    /// </summary>
    public sealed class BasisElbowAnatomyTests
    {
        const float k_Upper = 0.30f;
        const float k_Lower = 0.30f;
        const float k_Arm = k_Upper + k_Lower;
        static readonly Vector3 k_Shoulder = new Vector3(0.17f, 1.40f, 0f);
        static readonly Vector3 k_Up = Vector3.up;

        /// <summary>A geometrically real elbow: on the true circle, at the true bone distances.</summary>
        static Vector3 ElbowAt(Vector3 hand, float swivelDeg)
        {
            Vector3 ac = hand - k_Shoulder;
            float d = ac.magnitude;
            Vector3 axis = ac / d;
            float a = (k_Upper * k_Upper - k_Lower * k_Lower + d * d) / (2f * d);
            float radius = Mathf.Sqrt(Mathf.Max(k_Upper * k_Upper - a * a, 0f));
            Vector3 centre = k_Shoulder + axis * a;

            Vector3 refDown = Vector3.down;
            Vector3 u = (refDown - axis * Vector3.Dot(refDown, axis)).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            float rad = swivelDeg * Mathf.Deg2Rad;
            return centre + radius * (u * Mathf.Cos(rad) + v * Mathf.Sin(rad));
        }

        /// <summary>Applies the guard and returns where the elbow ends up.</summary>
        static Vector3 Guarded(Vector3 hand, Vector3 elbow)
        {
            float sw = BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, elbow, hand, k_Up, k_Arm);
            if (sw == 0f) return elbow;
            Vector3 axis = (hand - k_Shoulder).normalized;
            return k_Shoulder + Quaternion.AngleAxis(sw * Mathf.Rad2Deg, axis) * (elbow - k_Shoulder);
        }

        /// <summary>
        /// The hand must be genuinely IN REACH, or the elbow's circle has collapsed to a point on the arm axis
        /// and there is no swivel that could put it anywhere -- so a guard test would "pass" without ever
        /// exercising the guard. That is exactly how the first version of this file reported safety while the
        /// bug it was written for was still live.
        /// </summary>
        static void RequireInReach(Vector3 hand)
        {
            float d = Vector3.Distance(k_Shoulder, hand);
            Assert.Less(d, 0.98f * k_Arm,
                $"the test's own hand is {d:F3} from a {k_Arm:F2} arm -- out of reach, so the elbow has no " +
                "circle to sit on and this test would prove nothing");
            float a = (k_Upper * k_Upper - k_Lower * k_Lower + d * d) / (2f * d);
            float radius = Mathf.Sqrt(Mathf.Max(k_Upper * k_Upper - a * a, 0f));
            Assert.Greater(radius, 0.02f,
                $"the elbow's circle has radius {radius:F4} -- too collapsed for this test to mean anything");
        }

        static float Ceiling(Vector3 hand)
        {
            float handUp = Vector3.Dot(hand - k_Shoulder, k_Up);
            return Mathf.Max(0f, handUp);
        }

        /// <summary>
        /// THE CORPUS'S OWN MEASURED EXTREMES: hand offset from the shoulder (x out, y up, z fwd) and the
        /// natural human elbow swivel for that pose, each at the tightest headroom its bin produced. Taken
        /// from the envelope table in BasisElbowAnatomyCore.
        ///
        /// SHARED, deliberately. EveryPoseARealHumanHolds_PassesThroughUntouched asserts the guard is the
        /// exact identity on these with the body upright; ABentTorso_LeavesEveryLegalPoseUntouched asserts
        /// the same thing with the body bent double. Two copies of this table would let somebody tighten
        /// one test's notion of "a pose a human actually holds" without the other noticing.
        /// </summary>
        static readonly (Vector3 offset, float swivel, string what)[] k_LegalHumanPoses =
        {
            (new Vector3(0.05f, -0.55f,  0.02f),   5f, "arms hanging at the sides"),
            (new Vector3(0.12f, -0.42f,  0.18f),  20f, "hands at the waist"),
            (new Vector3(0.18f, -0.20f,  0.42f),  35f, "hands at the chest, reaching forward"),
            (new Vector3(0.28f, -0.05f,  0.38f),  55f, "hands at the chest, out to the side"),
            (new Vector3(0.20f,  0.15f,  0.35f),  70f, "hands up at the face"),
            (new Vector3(0.15f,  0.40f,  0.20f),  95f, "hands overhead"),
            (new Vector3(0.40f, -0.10f,  0.10f),  60f, "arm straight out to the side"),
            (new Vector3(0.05f, -0.30f, -0.25f),  15f, "hand behind the hip"),
        };

        // ============================================================================================
        // 1. THE IMPOSSIBLE MUST BECOME UNREACHABLE.
        // ============================================================================================

        /// <summary>
        /// ⭐ THE REPORTED BUG. Hand at chest height, elbow commanded STRAIGHT UP -- the pose the user is
        /// seeing. The guard must pull it back under the ceiling, and must not move the hand doing it.
        /// </summary>
        [Test]
        public void AnElbowPointedAtTheSky_IsPulledBackUnderTheCeiling()
        {
            // ⚠ THE ELBOW CAN ONLY REACH THE SKY WITH THE ARM OUT TO THE SIDE, and that is not a quirk of the
            // test -- it is the geometry, and it agrees with the corpus. Drop your hand low and close and the
            // TOP of the elbow's whole circle still sits below your shoulder: the pose is unreachable, so
            // there is nothing to guard. (My first version of this test commanded "elbow at the sky" for a
            // hand at the waist, where the circle's summit is -0.010 -- it was asserting on a pose that cannot
            // exist.) The reachable-and-illegal case is the CHICKEN WING: hand out to the side, elbow up.
            // That is the pose the user is actually seeing.
            (Vector3 offset, string what)[] chickenWings =
            {
                (new Vector3(0.45f,  0.00f, 0.10f), "arm straight out to the side, hand at shoulder height"),
                (new Vector3(0.42f, -0.08f, 0.15f), "arm out to the side, hand just below the shoulder"),
                (new Vector3(0.40f, -0.12f, 0.20f), "arm out and slightly forward, hand below the shoulder"),
                (new Vector3(0.35f,  0.05f, 0.28f), "arm out and forward, hand at shoulder height"),
                (new Vector3(0.30f,  0.10f, 0.30f), "arm out and forward, hand above the shoulder"),
            };

            foreach ((Vector3 offset, string what) in chickenWings)
            {
                Vector3 hand = k_Shoulder + offset;
                RequireInReach(hand);

                // The elbow, driven to the very top of its circle: pointing at the sky.
                Vector3 sky = ElbowAt(hand, 180f);
                float skyH = Vector3.Dot(sky - k_Shoulder, k_Up);
                Assert.Greater(skyH, Ceiling(hand),
                    $"the test must start from a pose that is BOTH reachable AND illegal ({what}); the top of " +
                    $"the elbow's circle here is {skyH:F3} against a ceiling of {Ceiling(hand):F3}. If the " +
                    "circle cannot clear the ceiling, the guard has nothing to do and this proves nothing.");

                Vector3 got = Guarded(hand, sky);

                float h = Vector3.Dot(got - k_Shoulder, k_Up);
                float hard = Ceiling(hand) + BasisElbowAnatomyCore.HardMarginFracLimb * k_Arm;
                Assert.Less(h, hard,
                    $"an elbow commanded at the sky ({what}) must be pulled back inside the anatomical " +
                    $"envelope -- it ended at {h:F3}, the hard limit is {hard:F3}");

                // ...and it must still be a real elbow: the bone lengths are not negotiable.
                Assert.AreEqual(k_Upper, Vector3.Distance(k_Shoulder, got), 1e-3f, "upper-arm length must be preserved");
                Assert.AreEqual(k_Lower, Vector3.Distance(got, hand), 1e-3f, "forearm length must be preserved");
            }
        }

        /// <summary>THE HAND DOES NOT MOVE. The correction is a swivel about the shoulder->hand axis, and the
        /// hand lies on that axis -- so this is geometry, not a tolerance. Pinned so nobody ever "fixes" the
        /// elbow by trading the hand away.</summary>
        [Test]
        public void TheGuard_NeverMovesTheHand_AtAnyExtension()
        {
            var rng = new System.Random(90210);
            for (int t = 0; t < 400; t++)
            {
                float reach = Mathf.Lerp(0.35f, 0.999f, (float)rng.NextDouble());
                var dir = new Vector3(
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1));
                if (dir.sqrMagnitude < 1e-4f) continue;
                Vector3 hand = k_Shoulder + dir.normalized * (reach * k_Arm);

                float swivel = (float)(rng.NextDouble() * 360.0 - 180.0);
                Vector3 elbow = ElbowAt(hand, swivel);
                Vector3 got = Guarded(hand, elbow);

                Assert.AreEqual(k_Upper, Vector3.Distance(k_Shoulder, got), 1e-3f,
                    $"upper-arm length must survive the guard (iter {t})");
                Assert.AreEqual(k_Lower, Vector3.Distance(got, hand), 1e-3f,
                    $"the guard must not move the hand: forearm length changed (iter {t}, reach {reach:P0})");
            }
        }

        /// <summary>Sweep the elbow all the way round its circle: NO angle may end up above the hard limit.
        /// This is the "unreachable, not merely discouraged" property -- the same status the knee's guard has.
        /// </summary>
        [Test]
        public void NoPointOnTheCircle_CanEndUpAboveTheHardLimit()
        {
            foreach (float handY in new[] { -0.30f, -0.15f, 0f, 0.20f })
            {
                Vector3 hand = k_Shoulder + new Vector3(0.22f, handY, 0.30f);
                RequireInReach(hand);   // no circle, no test -- see the note above
                float hard = Ceiling(hand) + BasisElbowAnatomyCore.HardMarginFracLimb * k_Arm;

                for (float sw = -180f; sw < 180f; sw += 2f)
                {
                    Vector3 got = Guarded(hand, ElbowAt(hand, sw));
                    float h = Vector3.Dot(got - k_Shoulder, k_Up);
                    Assert.Less(h, hard + 1e-4f,
                        $"swivel {sw:F0} deg with hand y={handY:F2} escaped the envelope: elbow at {h:F3}, limit {hard:F3}");
                }
            }
        }

        // ============================================================================================
        // 2. THE POSSIBLE MUST BE UNTOUCHED -- exactly, not approximately.
        // ============================================================================================

        /// <summary>
        /// ⭐ THE OTHER HALF OF THE PROOF. Every pose a real human actually held must pass through the guard
        /// BIT FOR BIT. The margins were chosen from the corpus (worst real violation: 0.015 arm lengths; the
        /// soft margin is 0.05) precisely so this holds, and it is asserted rather than assumed.
        ///
        /// The poses are the corpus's own measured extremes (k_LegalHumanPoses): arms down, hands at waist,
        /// chest, face, and overhead -- each at the tightest headroom that bin produced.
        /// </summary>
        [Test]
        public void EveryPoseARealHumanHolds_PassesThroughUntouched()
        {
            foreach ((Vector3 offset, float swivel, string what) in k_LegalHumanPoses)
            {
                Vector3 hand = k_Shoulder + offset;
                Vector3 elbow = ElbowAt(hand, swivel);

                float sw = BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, elbow, hand, k_Up, k_Arm);
                Assert.AreEqual(0f, sw,
                    $"the guard must be the EXACT identity on a pose a human actually holds ({what}); " +
                    $"it returned {sw * Mathf.Rad2Deg:F3} deg. If this fires, the margins are too tight and " +
                    "the guard is bending legal poses to fix illegal ones.");
            }
        }

        /// <summary>The guard is SCALE-FREE: the margins are fractions of the arm, so a child avatar and a
        /// giant get the same posture, not the same centimetres.</summary>
        [Test]
        public void TheGuard_IsScaleFree()
        {
            Vector3 handOff = new Vector3(0.25f, -0.10f, 0.40f);
            Vector3 hand = k_Shoulder + handOff;
            Vector3 elbow = ElbowAt(hand, 175f);   // illegal: near the top of the circle

            float small = BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, elbow, hand, k_Up, k_Arm);

            // The same pose on an avatar twice the size: every length doubles, so the SWIVEL must be identical.
            const float s = 2f;
            Vector3 shoulder2 = k_Shoulder;
            Vector3 hand2 = shoulder2 + handOff * s;
            Vector3 elbow2 = shoulder2 + (elbow - k_Shoulder) * s;
            float big = BasisElbowAnatomyCore.GuardSwivelRad(shoulder2, elbow2, hand2, k_Up, k_Arm * s);

            Assert.AreEqual(small, big, 1e-4f,
                "the same posture on a bigger avatar must produce the same corrective swivel");
        }

        // ============================================================================================
        // 3. IT MUST NOT BLOW UP.
        // ============================================================================================

        [Test]
        public void DegenerateAndNaNInputs_DeclineRatherThanCorrupt()
        {
            Vector3 hand = k_Shoulder + new Vector3(0.25f, -0.10f, 0.40f);

            // A straight arm: the circle has collapsed, there is no swivel that moves the elbow anywhere.
            Vector3 straightHand = k_Shoulder + new Vector3(k_Arm, 0f, 0f);
            Vector3 straightElbow = k_Shoulder + new Vector3(k_Upper, 0f, 0f);
            Assert.AreEqual(0f, BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, straightElbow, straightHand, k_Up, k_Arm),
                "a straight arm has nothing to guard");

            // Hand on top of the shoulder: no axis.
            Assert.AreEqual(0f, BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, ElbowAt(hand, 90f), k_Shoulder, k_Up, k_Arm),
                "a zero-length arm axis must decline");

            // Arm pointing straight UP: the elbow's circle is horizontal, so no swivel changes its height.
            Vector3 upHand = k_Shoulder + new Vector3(0f, 0.55f, 0f);
            Assert.AreEqual(0f, BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, ElbowAt(upHand, 40f), upHand, k_Up, k_Arm),
                "a vertical arm axis makes the constraint inexpressible as a swivel; it must decline, not guess");

            // NaN in, zero out. A NaN transform PERSISTS in Unity -- the arm would never recover.
            var nan = new Vector3(float.NaN, 0f, 0f);
            Assert.AreEqual(0f, BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, nan, hand, k_Up, k_Arm));
            Assert.AreEqual(0f, BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, ElbowAt(hand, 90f), nan, k_Up, k_Arm));
            Assert.AreEqual(0f, BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, ElbowAt(hand, 90f), hand, nan, k_Arm));
            Assert.AreEqual(0f, BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, ElbowAt(hand, 90f), hand, k_Up, float.NaN));
            Assert.AreEqual(0f, BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, ElbowAt(hand, 90f), hand, k_Up, 0f));
        }

        // ============================================================================================
        // 4. END TO END, THROUGH THE REAL SOLVER.
        // ============================================================================================

        /// <summary>
        /// The guard lives at the END of BasisArmSolveCore, so nothing -- not a hint, not a tracker, not the
        /// pole-collapse stabilizer, not the animated pose the solve began from -- can leave the arm outside
        /// the envelope. Drive the solver with a hint that DEMANDS a sky-pointing elbow and check it cannot
        /// deliver one.
        /// </summary>
        [Test]
        public void TheSolver_RefusesASkyPointingHint_AndKeepsTheHandOnTarget()
        {
            Vector3 hand = k_Shoulder + new Vector3(0.25f, -0.15f, 0.42f);

            // A hint demanding the elbow at the top of its circle.
            Vector3 skyElbow = ElbowAt(hand, 180f);
            Vector3 skyHint = k_Shoulder + (skyElbow - k_Shoulder).normalized * (0.5f * k_Arm);

            BasisArmSolveInput i = default;
            i.Shoulder = k_Shoulder;
            i.Elbow = ElbowAt(hand, 20f);     // a sane animated start
            i.Hand = hand;
            i.RootRotation = Quaternion.identity;
            i.MidRotation = Quaternion.identity;
            i.TargetPosition = hand;
            i.TargetRotation = Quaternion.identity;
            i.TargetOffset = Quaternion.identity;
            i.HintPosition = skyHint;
            i.HintWeight = true;
            i.HintIsTracker = false;
            i.HintMaxStepDeg = float.MaxValue;
            i.PlayerUp = k_Up;

            BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);

            float h = Vector3.Dot(r.ElbowSolved - k_Shoulder, k_Up);
            float hard = Ceiling(hand) + BasisElbowAnatomyCore.HardMarginFracLimb * k_Arm;

            Assert.Less(h, hard,
                $"even handed a hint that explicitly demands a sky-pointing elbow, the solver must not deliver " +
                $"one -- it ended at {h:F3} against a hard limit of {hard:F3}");
            Assert.AreEqual(0f, Vector3.Distance(r.HandSolved, hand), 2e-3f,
                "and the hand must still be exactly on its target: the guard is a swivel about the " +
                "shoulder->hand axis, so it cannot move the hand");
        }

        // ============================================================================================
        // 5. THE FRAME. "ABOVE" MEANS ABOVE THE TORSO, NOT ABOVE THE WORLD.
        //
        // ⚠ EVERYTHING ABOVE THIS LINE IS BLIND TO THE DISTINCTION, AND THAT IS WHY IT WENT UNCAUGHT.
        // Every test in sections 1-4 builds an implicitly UPRIGHT body -- k_Shoulder at y=1.40 with
        // k_Up = Vector3.up -- where the torso's up and the world's up ARE THE SAME VECTOR. All seven of
        // them passed just as happily while BasisArmSolveCore was handing the guard i.PlayerUp, the player
        // ROOT's up, which stays vertical when the chest does not. A suite that cannot fail on the defect
        // cannot protect the fix for it.
        //
        // MEASURED, on the posture/ tier of the mocap corpus: handed a root/world up the guard fires on
        // 148 FRAMES OF POSES REAL HUMANS ACTUALLY HELD, worst correction 66.15 deg (mean 31.72), which
        // displaces the solved elbow by up to 21.28 cm. Handed the TORSO's up it fires ZERO times there.
        // Every one of those 148 frames has the trunk 73.7-119.7 deg off vertical (median 100.5) -- people
        // bent double with their arms hanging perfectly normally, having their elbows "corrected".
        // ============================================================================================

        /// <summary>The trunk tilts the corpus's world-frame misfires actually occurred at: 73.7-119.7 deg
        /// off vertical, median 100.5. Swept rather than sampled at one angle, because a guard that is right
        /// at 100 deg and wrong at 75 is still wrong.</summary>
        static readonly float[] k_MeasuredTrunkTilts = { 73.7f, 80f, 90f, 100.5f, 110f, 119.7f };

        /// <summary>The median violating trunk, and the tilt the fallback test pins its numbers at.</summary>
        const float k_MedianTrunkTiltDeg = 100.5f;

        /// <summary>
        /// Bends the body forward at the waist by `deg`, pivoting about the shoulder.
        ///
        /// ⭐ THE FIXTURE IS THE ARGUMENT. The arm is carried RIGIDLY with the trunk -- shoulder, elbow,
        /// hand and torsoUp all rotate together -- so NOTHING about the arm's relationship to its own chest
        /// changes. A pose that was legal against an upright chest is still legal against this one BY
        /// CONSTRUCTION, and the only thing that has moved is the world's up. That is the entire content of
        /// the bug, and it is what makes "the guard must return exactly 0f" a statement about the guard
        /// rather than a coincidence of the numbers chosen.
        /// </summary>
        static Quaternion Trunk(float deg) => Quaternion.AngleAxis(deg, Vector3.right);
        static Vector3 TorsoUpAt(float deg) => Trunk(deg) * Vector3.up;
        static Vector3 Bent(float deg, Vector3 p) => k_Shoulder + Trunk(deg) * (p - k_Shoulder);

        /// <summary>The ceiling -- the higher of shoulder and hand -- measured along a NAMED up.</summary>
        static float CeilingIn(Vector3 up, Vector3 hand)
        {
            float handUp = Vector3.Dot(hand - k_Shoulder, up);
            return Mathf.Max(0f, handUp);
        }

        /// <summary>Applies the guard IN A NAMED FRAME and returns where the elbow ends up, reporting the
        /// swivel it spent getting there.</summary>
        static Vector3 GuardedIn(Vector3 up, Vector3 hand, Vector3 elbow, out float swivelDeg)
        {
            float sw = BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, elbow, hand, up, k_Arm);
            swivelDeg = sw * Mathf.Rad2Deg;
            if (sw == 0f) return elbow;
            Vector3 axis = (hand - k_Shoulder).normalized;
            return k_Shoulder + Quaternion.AngleAxis(swivelDeg, axis) * (elbow - k_Shoulder);
        }

        /// <summary>
        /// ⭐ THE LAW IS TORSO-RELATIVE. Bend the body double and every pose a real human holds must STILL
        /// pass through untouched -- because the law is a statement about the humerus against the shoulder
        /// joint, and that joint is bolted to the ribcage, which came with it.
        ///
        /// THIS IS THE CASE THAT WAS PRODUCING THE DAMAGE. Handed a root/world up the guard "corrects" the
        /// arm of somebody touching their toes: 148 posture/ frames, worst 66.15 deg of spurious swivel
        /// (mean 31.72), up to 21.28 cm of elbow displacement, on poses that were never wrong. Handed the
        /// torso's up it fires zero times there.
        ///
        /// HEADROOM: all 48 pose x tilt combinations return EXACTLY 0f -- not "small", the bitwise identity,
        /// which is what the guard promises inside its envelope. Torso-frame headroom is invariant under the
        /// tilt (the arm is carried rigidly), and the tightest of the eight, 'hands at the chest, out to the
        /// side', sits 0.2161 arm lengths BELOW its torso ceiling -- 0.2661 clear of the 0.05 soft margin, so
        /// none of these is a marginal pass. In WORLD up the same eight break: 13 of the 48 combinations
        /// fire, worst 'arm straight out to the side' at 119.7 deg, costing 61.82 deg of correction and
        /// 21.80 cm of elbow displacement -- the corpus's own 66.15 deg / 21.28 cm worst case, reproduced
        /// synthetically to within a few percent.
        ///
        /// ⚠ IT DOES NOT EXERCISE THE PLUMBING. It calls the core directly, so it pins the LAW's frame, not
        /// the wiring that supplies it. TheFallback... below is the one that goes red if BasisArmSolveCore
        /// stops passing TorsoUp.
        /// </summary>
        [Test]
        public void ABentTorso_LeavesEveryLegalPoseUntouched_WhenTheGuardIsGivenTheTorsoFrame()
        {
            foreach (float tiltDeg in k_MeasuredTrunkTilts)
            {
                Vector3 torsoUp = TorsoUpAt(tiltDeg);

                foreach ((Vector3 offset, float swivel, string what) in k_LegalHumanPoses)
                {
                    Vector3 handUpright = k_Shoulder + offset;
                    Vector3 hand = Bent(tiltDeg, handUpright);
                    Vector3 elbow = Bent(tiltDeg, ElbowAt(handUpright, swivel));

                    float sw = BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, elbow, hand, torsoUp, k_Arm);
                    Assert.AreEqual(0f, sw,
                        $"trunk {tiltDeg:F1} deg off vertical, {what}: the guard returned " +
                        $"{sw * Mathf.Rad2Deg:F3} deg on a pose that is UNCHANGED relative to its own chest -- " +
                        "the arm was rotated rigidly with the trunk, so nothing the anatomical law talks about " +
                        "moved. A non-zero here means the guard is reading a frame it was not given (world/root " +
                        "up rather than torso up), which is the defect that fired on 148 posture/ frames at up " +
                        "to 66.15 deg. See BasisElbowAnatomyCore's frame note.");
                }
            }
        }

        /// <summary>
        /// ⭐ THE REGRESSION TEST PROPER: THE SAME SOLVE, TWICE, DIFFERING ONLY IN TorsoUp. This is the one
        /// that goes red if the fix is reverted, and it pins BOTH halves of it at once.
        ///
        ///     TorsoUp SET  -> the solve is the exact identity. Measured: the elbow moves 0.000000 cm.
        ///     TorsoUp ZERO -> BasisArmSolveCore falls back to i.PlayerUp, WHICH IS THE PRE-FIX BEHAVIOUR,
        ///                     and the guard fires: -41.515 deg, hauling the elbow 15.04 cm off a pose that
        ///                     was never wrong. (That is the median-trunk figure; at the top of the measured
        ///                     band, 119.7 deg, the same pose costs 61.82 deg and 21.80 cm -- which is the
        ///                     corpus's own 66.15 deg / 21.28 cm worst case, reproduced synthetically.)
        ///
        /// THE FALLBACK IS DELIBERATE AND IT IS ALSO THE DEFECT, so it is pinned rather than tolerated. It
        /// is reject-unless-good: a caller that cannot build a body frame gets a guarded arm rather than no
        /// guard at all, and for an upright torso it is the same vector anyway. But it must not silently
        /// rot into the only path, so this asserts it still reproduces the OLD behaviour, at the size that
        /// behaviour misfires by.
        ///
        /// VERIFIED BY REVERTING, 2026-07-22: with `guardUp` forced back to `i.PlayerUp`, the first
        /// assertion below fails with the elbow moved 15.036346 cm, and the second still passes -- i.e. the
        /// fallback genuinely is the old behaviour and this test genuinely distinguishes them.
        ///
        /// THE SOLVE IS THE IDENTITY BY CONSTRUCTION, so the guard is the only thing that can move the
        /// elbow and the measurement is unambiguous: no hint (so neither the hint swivel nor the
        /// pole-collapse stabilizer runs), target == hand (so the triangle solve and the root rotation are
        /// both no-ops), and TipRotation/HintRotation left at zero (so no wrist relief and no forearm roll).
        /// </summary>
        [Test]
        public void TheFallback_ReproducesTheOldWorldFrameBehaviour_WhenNoTorsoFrameIsAvailable()
        {
            // 'arm straight out to the side' -- a measured-legal human pose, and the one the world frame
            // misjudges hardest once the trunk goes over.
            (Vector3 offset, float swivel, string what) pose = k_LegalHumanPoses[6];
            Vector3 handUpright = k_Shoulder + pose.offset;
            RequireInReach(handUpright);

            Vector3 hand = Bent(k_MedianTrunkTiltDeg, handUpright);
            Vector3 elbow = Bent(k_MedianTrunkTiltDeg, ElbowAt(handUpright, pose.swivel));
            Vector3 torsoUp = TorsoUpAt(k_MedianTrunkTiltDeg);

            BasisArmSolveInput Identity()
            {
                BasisArmSolveInput i = default;
                i.Shoulder = k_Shoulder;
                i.Elbow = elbow;
                i.Hand = hand;
                i.RootRotation = Quaternion.identity;
                i.MidRotation = Quaternion.identity;
                i.TargetPosition = hand;          // target == hand: the triangle solve is a no-op
                i.TargetRotation = Quaternion.identity;
                i.TargetOffset = Quaternion.identity;
                i.HintWeight = false;             // no hint: no swivel, no pole-collapse stabilizer
                i.HintIsTracker = false;
                i.HintMaxStepDeg = float.MaxValue;
                i.PlayerUp = k_Up;                // the ROOT's up: upright, because the root always is
                return i;
            }

            // The rig's path: BasisFullBodyIK fills TorsoUp from BuildArmFrame(stream).Up (chest->neck).
            BasisArmSolveInput withTorso = Identity();
            withTorso.TorsoUp = torsoUp;
            BasisArmSolveCore.Solve(withTorso, out BasisArmSolveResult rTorso);
            float movedWithTorso = Vector3.Distance(rTorso.ElbowSolved, elbow);

            // The degenerate-rig path: TorsoUp left at the struct default, which is zero.
            BasisArmSolveInput noTorso = Identity();
            BasisArmSolveCore.Solve(noTorso, out BasisArmSolveResult rFallback);
            float movedFallback = Vector3.Distance(rFallback.ElbowSolved, elbow);

            TestContext.WriteLine(
                $"\n  trunk {k_MedianTrunkTiltDeg:F1} deg off vertical, '{pose.what}' (a measured-legal pose)\n" +
                $"    TorsoUp set  : elbow moved {movedWithTorso * 100f:F6} cm\n" +
                $"    TorsoUp zero : elbow moved {movedFallback * 100f:F4} cm  (the pre-fix behaviour)");

            Assert.That(movedWithTorso, Is.LessThan(1e-4f),
                $"given the TORSO's up, the guard must be the identity on this pose -- it is one of the corpus's " +
                $"own measured-legal arms, rotated rigidly with the trunk -- but the elbow moved " +
                $"{movedWithTorso * 100f:F4} cm. If this fails, BasisArmSolveCore has stopped honouring " +
                "i.TorsoUp and is back to guarding against the player ROOT's up, which is the defect that cost " +
                "up to 66.15 deg and 21.28 cm on 148 frames of posture/.");

            Assert.That(movedFallback, Is.GreaterThan(0.10f),
                $"with TorsoUp left at zero the solver must FALL BACK to i.PlayerUp and reproduce the OLD, " +
                $"world-frame behaviour -- which on this pose means firing hard, and it moved the elbow only " +
                $"{movedFallback * 100f:F4} cm. This assertion is backwards ON PURPOSE: the fallback exists so a " +
                "caller that cannot build a body frame still gets a guarded arm, and pinning it here is what stops " +
                "it rotting into a silent no-op. It is NOT an endorsement of the world frame -- it is the defect, " +
                "held at the size it misfires by.");

            // ...and the two disagree because the FRAME differs, not because the solve is unstable: the hand
            // is on target either way (the guard is a swivel about the shoulder->hand axis, and the hand lies
            // on it), and the torso frame independently agrees the pose was legal all along.
            Assert.AreEqual(0f, Vector3.Distance(rTorso.HandSolved, hand), 2e-3f,
                "the hand must stay on target on the torso path");
            Assert.AreEqual(0f, Vector3.Distance(rFallback.HandSolved, hand), 2e-3f,
                "the hand must stay on target on the fallback path too -- the guard cannot move the hand, so a " +
                "firing guard is not an excuse for losing the target");

            GuardedIn(torsoUp, hand, elbow, out float torsoSwivelDeg);
            Assert.AreEqual(0f, torsoSwivelDeg,
                $"and the core itself must call this pose legal in the torso frame ({torsoSwivelDeg:F3} deg) -- " +
                "if it does not, the fixture is wrong and neither assertion above means anything");
        }

        /// <summary>
        /// ⭐ THE FIX RE-AIMS THE GUARD; IT DOES NOT DISABLE IT. The cheapest way to make
        /// ABentTorso_LeavesEveryLegalPoseUntouched pass would be to stop guarding, so this demands the
        /// opposite half: the CHICKEN WING -- the reported bug, an elbow driven to the top of its circle --
        /// must still be caught when the body is bent double, and caught IN THE TILTED FRAME.
        ///
        /// Same rigid-rotation fixture as the legal case, so each pose is illegal against the tilted chest
        /// by exactly the margin AnElbowPointedAtTheSky_IsPulledBackUnderTheCeiling establishes upright.
        /// The reachable-AND-illegal precondition is re-asserted at every tilt for the reason RequireInReach
        /// exists: a guard test that starts from a pose the geometry cannot produce reports safety while the
        /// bug is live, and this file has been burned by that once already.
        ///
        /// HEADROOM: 30 combinations (6 tilts x 5 wings). Every one fires, every one lands under the tilted
        /// hard limit (ceiling + 0.15 arm lengths), and every one keeps both bone lengths to within 1e-3.
        /// </summary>
        [Test]
        public void AnIllegalPoseInTheTiltedFrame_IsStillCaught_TheFixReAimsTheGuardItDoesNotDisableIt()
        {
            (Vector3 offset, string what)[] chickenWings =
            {
                (new Vector3(0.45f,  0.00f, 0.10f), "arm straight out to the side, hand at shoulder height"),
                (new Vector3(0.42f, -0.08f, 0.15f), "arm out to the side, hand just below the shoulder"),
                (new Vector3(0.40f, -0.12f, 0.20f), "arm out and slightly forward, hand below the shoulder"),
                (new Vector3(0.35f,  0.05f, 0.28f), "arm out and forward, hand at shoulder height"),
                (new Vector3(0.30f,  0.10f, 0.30f), "arm out and forward, hand above the shoulder"),
            };

            foreach (float tiltDeg in k_MeasuredTrunkTilts)
            {
                Vector3 torsoUp = TorsoUpAt(tiltDeg);

                foreach ((Vector3 offset, string what) in chickenWings)
                {
                    Vector3 handUpright = k_Shoulder + offset;
                    RequireInReach(handUpright);

                    Vector3 hand = Bent(tiltDeg, handUpright);
                    Vector3 sky = Bent(tiltDeg, ElbowAt(handUpright, 180f));   // elbow at the top of its circle

                    // Reachable AND illegal, measured in the TILTED frame -- or this proves nothing.
                    float ceiling = CeilingIn(torsoUp, hand);
                    float skyH = Vector3.Dot(sky - k_Shoulder, torsoUp);
                    Assert.Greater(skyH, ceiling,
                        $"trunk {tiltDeg:F1} deg, {what}: the fixture must be BOTH reachable AND illegal against " +
                        $"the tilted chest; the top of the elbow's circle is {skyH:F3} against a torso-frame " +
                        $"ceiling of {ceiling:F3}. If the circle cannot clear the ceiling there is nothing to " +
                        "guard and this iteration proves nothing.");

                    Vector3 got = GuardedIn(torsoUp, hand, sky, out float swivelDeg);

                    Assert.AreNotEqual(0f, swivelDeg,
                        $"trunk {tiltDeg:F1} deg, {what}: the guard went SILENT on an elbow at the top of its " +
                        "circle. Re-aiming the guard at the torso frame must not switch it off -- the sky-pointing " +
                        "elbow is the bug it was written for.");

                    float h = Vector3.Dot(got - k_Shoulder, torsoUp);
                    float hard = ceiling + BasisElbowAnatomyCore.HardMarginFracLimb * k_Arm;
                    Assert.Less(h, hard,
                        $"trunk {tiltDeg:F1} deg, {what}: an elbow commanded at the sky must be pulled back inside " +
                        $"the anatomical envelope OF THE TILTED TORSO -- it ended at {h:F3} against a hard limit " +
                        $"of {hard:F3}");

                    // ...and it must still be a real elbow: the bone lengths are not negotiable.
                    Assert.AreEqual(k_Upper, Vector3.Distance(k_Shoulder, got), 1e-3f,
                        $"upper-arm length must survive the tilted guard (trunk {tiltDeg:F1} deg, {what})");
                    Assert.AreEqual(k_Lower, Vector3.Distance(got, hand), 1e-3f,
                        $"the guard must not move the hand (trunk {tiltDeg:F1} deg, {what})");
                }
            }
        }
    }
}
