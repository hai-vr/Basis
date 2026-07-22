using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK.Mocap;

namespace Basis.Tests.IK
{
    /// <summary>
    /// GUARDS ON THE RULER.
    ///
    /// BasisSyntheticTracker is a MEASURING INSTRUMENT: every corpus number produced through it inherits its
    /// correctness. This project has already shipped several metrics that were quietly lying -- a pop detector
    /// that read the same count whether its cause was present or absent, and a smoothness metric that rewarded
    /// over-smoothed mush over a real human. Both passed their tests. An unguarded model becomes the next one.
    ///
    /// So the assertions here are on the MODEL, not on the solver:
    ///   * Ideal is a BIT-EXACT identity -- which is what lets a realistic-tracker row be A/B'd against every
    ///     existing baseline without re-basing the baseline.
    ///   * Same seed, same answer, exactly. A test that changes its answer run to run is worse than no test.
    ///   * Each parameter moves the output in its expected direction AND ONLY IN THAT WAY. Stand-off must not
    ///     touch the jitter; slip must be low-frequency and not gaussian; jitter must be zero-mean.
    ///   * The magnitudes land in the documented physical ranges.
    ///   * The constants promoted out of BasisArmTrackerHintAuthorityTests are pinned, so a later edit cannot
    ///     silently retune numbers that were calibrated against a real user complaint.
    ///
    /// Several tests carry an explicit NON-VACUITY assertion (a live control, or a floor on how much signal was
    /// actually measured) because the characteristic failure of a test like this is passing while measuring
    /// nothing at all.
    /// </summary>
    public sealed class BasisSyntheticTrackerTests
    {
        // The same body BasisArmTrackerHintAuthorityTests uses, so the geometry under test is the geometry the
        // one-off model was calibrated on rather than a second, subtly different arm.
        const float k_Upper = 0.30f;
        const float k_Lower = 0.30f;
        const float k_ArmLen = k_Upper + k_Lower;
        static readonly Vector3 k_Shoulder = new Vector3(0.17f, 1.40f, 0f);
        static readonly Vector3 k_ReachDir = new Vector3(0.92f, 0f, 0.39f).normalized;
        const float k_CommandedSwivel = 30f;

        const int k_Seed = 20260722;

        // ── geometry, copied verbatim from BasisArmTrackerHintAuthorityTests ────────────────────────
        // A COPY ON PURPOSE. StrappedPosition_ReproducesTheArmTestsOneOffPuckModel cross-checks the promoted
        // model against this copy; if the promotion drifted, the two stop agreeing and that test fails. That is
        // the only reason to duplicate maths in this repo, and it is the reason here.

        static void SwingBasis(Vector3 hand, out Vector3 axis, out Vector3 u, out Vector3 v)
        {
            Vector3 sa = hand - k_Shoulder;
            axis = sa.normalized;
            Vector3 refDown = Vector3.down;
            u = (refDown - axis * Vector3.Dot(refDown, axis)).normalized;
            v = Vector3.Cross(axis, u);
        }

        static void ElbowCircle(Vector3 hand, out Vector3 centre, out float radius)
        {
            Vector3 sa = hand - k_Shoulder;
            float d = sa.magnitude;
            float a = (k_Upper * k_Upper - k_Lower * k_Lower + d * d) / (2f * d);
            radius = Mathf.Sqrt(Mathf.Max(k_Upper * k_Upper - a * a, 0f));
            centre = k_Shoulder + (sa / d) * a;
        }

        static Vector3 ElbowOnCircle(Vector3 hand, float swivelDeg)
        {
            SwingBasis(hand, out _, out Vector3 u, out Vector3 v);
            ElbowCircle(hand, out Vector3 centre, out float radius);
            float t = swivelDeg * Mathf.Deg2Rad;
            return centre + radius * (u * Mathf.Cos(t) + v * Mathf.Sin(t));
        }

        /// <summary>The one-off strapped puck from BasisArmTrackerHintAuthorityTests, verbatim.</summary>
        static Vector3 LegacyStrappedTracker(Vector3 hand, float swivelDeg, float standOff, float slide)
        {
            SwingBasis(hand, out Vector3 axis, out Vector3 u, out Vector3 v);
            ElbowCircle(hand, out Vector3 centre, out float radius);
            float t = swivelDeg * Mathf.Deg2Rad;
            Vector3 outward = u * Mathf.Cos(t) + v * Mathf.Sin(t);
            return centre - axis * slide + outward * (radius + standOff);
        }

        // ── fixtures ────────────────────────────────────────────────────────────────────────────────

        struct Limb
        {
            public Vector3 Root, Joint, Tip;
            public BasisSyntheticTrackerMount Mount;
        }

        static Limb BentArm(float reach = 0.80f)
        {
            var l = default(Limb);
            l.Root = k_Shoulder;
            l.Tip = k_Shoulder + k_ReachDir * (reach * k_ArmLen);
            l.Joint = ElbowOnCircle(l.Tip, k_CommandedSwivel);
            l.Mount = BasisSyntheticTracker.MountForLimb(l.Root, l.Joint, l.Tip);
            return l;
        }

        static BasisMocapPose Pose(Vector3 p, Quaternion r) => new BasisMocapPose { Position = p, Rotation = r, Valid = true };

        static int Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);

        static void AssertBitExact(Vector3 a, Vector3 b, string what)
        {
            Assert.AreEqual(Bits(a.x), Bits(b.x), what + " (x is not bit-identical)");
            Assert.AreEqual(Bits(a.y), Bits(b.y), what + " (y is not bit-identical)");
            Assert.AreEqual(Bits(a.z), Bits(b.z), what + " (z is not bit-identical)");
        }

        static void AssertBitExact(Quaternion a, Quaternion b, string what)
        {
            Assert.AreEqual(Bits(a.x), Bits(b.x), what + " (x is not bit-identical)");
            Assert.AreEqual(Bits(a.y), Bits(b.y), what + " (y is not bit-identical)");
            Assert.AreEqual(Bits(a.z), Bits(b.z), what + " (z is not bit-identical)");
            Assert.AreEqual(Bits(a.w), Bits(b.w), what + " (w is not bit-identical)");
        }

        /// <summary>A moving truth pose, so an "offset that never changes" is a real claim and not an artefact
        /// of a stationary fixture.</summary>
        static BasisMocapPose MovingTruth(in Limb l, int f)
        {
            Vector3 wobble = new Vector3(
                Mathf.Sin(f * 0.031f) * 0.05f,
                Mathf.Sin(f * 0.017f) * 0.04f,
                Mathf.Sin(f * 0.023f) * 0.06f);
            Quaternion spin = Quaternion.AngleAxis(f * 3.1f, new Vector3(0.3f, 0.8f, 0.5f).normalized);
            return Pose(l.Joint + wobble, spin);
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 1. THE IDENTITY. Without this, every A/B against an existing baseline is invalid.
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ Ideal MUST BE A BIT-EXACT NO-OP.
        ///
        /// Not "close to". Bit-exact, on every component of every frame. This is the property that lets the
        /// realistic-tracker row be introduced without re-basing a single existing corpus number: if Ideal is
        /// the identity, a run through the model with Ideal is provably the run without it, and any change to a
        /// baseline is therefore a change in the SOLVER and not in the harness.
        ///
        /// A model that is merely "identity to 1e-7" quietly moves every baseline by a hair, and the next
        /// person cannot tell a real regression from the ruler having been swapped.
        /// </summary>
        [Test]
        public void IdealPreset_IsABitExactNoOp()
        {
            Limb l = BentArm();
            var tracker = new BasisSyntheticTracker(BasisSyntheticTrackerPreset.Ideal, k_Seed);

            for (int f = 0; f < 500; f++)
            {
                BasisMocapPose truth = MovingTruth(l, f);
                BasisSyntheticTrackerSample s = tracker.Sample(truth, l.Mount, f / 120f);

                Assert.IsTrue(s.Valid, $"the ideal device must always deliver (frame {f})");
                Assert.IsFalse(s.Held, $"the ideal device is continuous, so nothing is ever stale (frame {f})");
                Assert.IsFalse(s.Glitched, $"the ideal device does not glitch (frame {f})");
                Assert.AreEqual(0f, s.AgeS, 0f, $"the ideal device delivers instantly (frame {f})");

                AssertBitExact(truth.Position, s.Position, $"Ideal moved the position on frame {f}");
                AssertBitExact(truth.Rotation, s.Rotation, $"Ideal moved the rotation on frame {f}");
            }
        }

        /// <summary>
        /// The IsIdentity predicate is what documents the identity, so it must not be able to lie in either
        /// direction. Ideal is the identity; every other preset is NOT -- because a preset that quietly
        /// collapsed to the identity would report a perfect score and look like a win.
        /// </summary>
        [Test]
        public void IsIdentity_IsTrueForIdealAndFalseForEveryOtherPreset()
        {
            Assert.IsTrue(BasisSyntheticTrackerParams.Identity.IsIdentity, "the default params must be the identity");
            Assert.IsTrue(BasisSyntheticTrackerParams.FromPreset(BasisSyntheticTrackerPreset.Ideal).IsIdentity,
                "the Ideal preset must be the identity");

            foreach (BasisSyntheticTrackerPreset p in (BasisSyntheticTrackerPreset[])Enum.GetValues(typeof(BasisSyntheticTrackerPreset)))
            {
                if (p == BasisSyntheticTrackerPreset.Ideal) continue;
                Assert.IsFalse(BasisSyntheticTrackerParams.FromPreset(p).IsIdentity,
                    $"preset {p} collapsed to the identity -- it models no device at all, so any score it " +
                    "produces is TruthJoint wearing a different name");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 2. DETERMINISM.
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ SAME SEED, SAME ANSWER, EXACTLY -- and a different seed must actually differ, or "deterministic"
        /// would be satisfied by a model that ignores its seed entirely.
        /// </summary>
        [Test]
        public void SameSeed_ReproducesTheStreamExactly_AndADifferentSeedDoesNot()
        {
            Limb l = BentArm();
            BasisSyntheticTrackerParams p = BasisSyntheticTrackerParams.FromPreset(BasisSyntheticTrackerPreset.TypicalPuck);

            var a = new BasisSyntheticTracker(p, k_Seed);
            var b = new BasisSyntheticTracker(p, k_Seed);
            var c = new BasisSyntheticTracker(p, k_Seed + 1);

            int differedFromC = 0;

            for (int f = 0; f < 600; f++)
            {
                BasisMocapPose truth = MovingTruth(l, f);
                float t = f / 120f;

                BasisSyntheticTrackerSample sa = a.Sample(truth, l.Mount, t);
                BasisSyntheticTrackerSample sb = b.Sample(truth, l.Mount, t);
                BasisSyntheticTrackerSample sc = c.Sample(truth, l.Mount, t);

                AssertBitExact(sa.Position, sb.Position, $"the same seed produced a different position on frame {f}");
                AssertBitExact(sa.Rotation, sb.Rotation, $"the same seed produced a different rotation on frame {f}");
                Assert.AreEqual(sa.Held, sb.Held, $"the same seed produced a different hold state on frame {f}");
                Assert.AreEqual(sa.Glitched, sb.Glitched, $"the same seed produced a different glitch state on frame {f}");

                if (Vector3.Distance(sa.Position, sc.Position) > 1e-9f) differedFromC++;
            }

            Assert.Greater(differedFromC, 300,
                $"a different seed changed only {differedFromC}/600 frames -- the seed is barely wired in, so " +
                "'deterministic' here would mean 'constant', which proves nothing about reproducibility");
        }

        /// <summary>
        /// ⭐ THE MODEL MUST NOT BE READING UnityEngine.Random.
        ///
        /// Global mutable RNG state is the classic way a "deterministic" harness stops being one: some
        /// unrelated code draws from it, and the corpus numbers move for no reason anybody can find. This kicks
        /// the global state hard between two otherwise identical runs and demands the output be bit-identical.
        /// </summary>
        [Test]
        public void UnityRandomState_CannotChangeTheOutput()
        {
            Limb l = BentArm();
            BasisSyntheticTrackerParams p = BasisSyntheticTrackerParams.FromPreset(BasisSyntheticTrackerPreset.PoorPuck);

            UnityEngine.Random.State saved = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(1);
                Vector3[] first = RunPositions(p, k_Seed, l, 300);

                UnityEngine.Random.InitState(999983);
                float churn = 0f;
                for (int i = 0; i < 1000; i++) churn += UnityEngine.Random.value;   // churn the global stream
                Assert.Greater(churn, 0f, "the global RNG was not actually disturbed, so this test proves nothing");

                Vector3[] second = RunPositions(p, k_Seed, l, 300);

                for (int f = 0; f < first.Length; f++)
                {
                    AssertBitExact(first[f], second[f],
                        $"the model's output moved when UnityEngine.Random's state changed (frame {f}) -- it is " +
                        "reading global RNG state, so any other code in the process can silently move every " +
                        "corpus number this harness reports");
                }
            }
            finally
            {
                UnityEngine.Random.state = saved;
            }
        }

        static Vector3[] RunPositions(in BasisSyntheticTrackerParams p, int seed, in Limb l, int frames)
        {
            var tracker = new BasisSyntheticTracker(p, seed);
            var outp = new Vector3[frames];
            for (int f = 0; f < frames; f++)
            {
                outp[f] = tracker.Sample(MovingTruth(l, f), l.Mount, f / 120f).Position;
            }
            return outp;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 3. ONE PARAMETER AT A TIME -- each moves the output the expected way, and ONLY that way.
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Stand-off puts the puck off the bone along the limb's outward normal, and does nothing else: not the
        /// rotation, not the timing, and -- separately asserted below -- not the noise.
        /// </summary>
        [Test]
        public void StandOff_MovesThePuckAlongTheOutwardNormal_AndNothingElse()
        {
            Limb l = BentArm();

            foreach (float standOff in BasisSyntheticTrackerCalibration.StandOffsM)
            {
                var p = BasisSyntheticTrackerParams.Identity;
                p.StandOffM = standOff;

                var tracker = new BasisSyntheticTracker(p, k_Seed);
                for (int f = 0; f < 50; f++)
                {
                    BasisMocapPose truth = MovingTruth(l, f);
                    BasisSyntheticTrackerSample s = tracker.Sample(truth, l.Mount, f / 120f);

                    Vector3 expected = truth.Position + l.Mount.Outward * standOff;
                    Assert.AreEqual(0f, Vector3.Distance(expected, s.Position), 1e-6f,
                        $"a {standOff * 100f:F0} cm stand-off did not land on the outward normal (frame {f})");

                    AssertBitExact(truth.Rotation, s.Rotation,
                        $"a stand-off moved the reported ROTATION (frame {f}) -- it is a position offset only");
                    Assert.IsFalse(s.Held, $"a stand-off must not make a sample stale (frame {f})");
                }
            }
        }

        /// <summary>
        /// ⭐ STAND-OFF MUST NOT DISTURB THE NOISE, AND A GLITCH MUST NOT DISTURB THE JITTER.
        ///
        /// Every effect draws from its own stream precisely so that a parameter can be attributed in isolation.
        /// If they shared one stream, switching a glitch on would shift every subsequent jitter draw and an A/B
        /// between two configurations would be measuring the draw ordering, not the parameter.
        ///
        /// Two claims, both exact:
        ///   * the jitter RESIDUAL is unchanged (to float association) when the stand-off moves 0 -> 6 cm;
        ///   * the position on NON-GLITCHED frames is BIT-IDENTICAL between a run with glitches and one without.
        /// </summary>
        [Test]
        public void StandOff_AndGlitches_DoNotDisturbTheJitterStream()
        {
            Limb l = BentArm();
            const int frames = 400;

            // ── stand-off vs jitter
            var flat = BasisSyntheticTrackerParams.Identity;
            flat.JitterSigmaM = BasisSyntheticTrackerCalibration.JitterSigmaM;

            var proud = flat;
            proud.StandOffM = 0.06f;

            var tFlat = new BasisSyntheticTracker(flat, k_Seed);
            var tProud = new BasisSyntheticTracker(proud, k_Seed);

            double residualSq = 0;
            for (int f = 0; f < frames; f++)
            {
                BasisMocapPose truth = MovingTruth(l, f);
                float t = f / 120f;

                Vector3 rFlat = tFlat.Sample(truth, l.Mount, t).Position - truth.Position;
                Vector3 rProud = tProud.Sample(truth, l.Mount, t).Position - truth.Position - l.Mount.Outward * 0.06f;

                Assert.AreEqual(0f, Vector3.Distance(rFlat, rProud), 1e-6f,
                    $"moving the stand-off changed the jitter residual on frame {f} -- the two effects share an " +
                    "RNG stream, so no A/B through this model can attribute a change to one of them");

                residualSq += rFlat.sqrMagnitude;
            }

            // NON-VACUITY: if there were no jitter at all, both residuals would be zero and the assertion above
            // would be satisfied by a model that does nothing.
            double rms = Math.Sqrt(residualSq / frames);
            Assert.Greater(rms, 0.5 * BasisSyntheticTrackerCalibration.JitterSigmaM,
                $"the jitter residual RMS was {rms * 1000.0:F3} mm -- there was essentially no jitter to disturb, " +
                "so this test compared nothing with nothing");

            // ── glitches vs jitter
            var clean = BasisSyntheticTrackerParams.Identity;
            clean.JitterSigmaM = BasisSyntheticTrackerCalibration.JitterSigmaM;

            var poppy = clean;
            poppy.GlitchChance = BasisSyntheticTrackerCalibration.GlitchChancePoor;
            poppy.GlitchM = BasisSyntheticTrackerCalibration.GlitchMPoor;

            var tClean = new BasisSyntheticTracker(clean, k_Seed);
            var tPoppy = new BasisSyntheticTracker(poppy, k_Seed);

            int glitches = 0, compared = 0;
            for (int f = 0; f < frames; f++)
            {
                BasisMocapPose truth = MovingTruth(l, f);
                float t = f / 120f;

                BasisSyntheticTrackerSample sc = tClean.Sample(truth, l.Mount, t);
                BasisSyntheticTrackerSample sp = tPoppy.Sample(truth, l.Mount, t);

                if (sp.Glitched) { glitches++; continue; }

                AssertBitExact(sc.Position, sp.Position,
                    $"switching glitches on changed a NON-GLITCHED frame ({f}) -- the glitch and jitter streams " +
                    "are coupled, so a glitch rate cannot be varied without moving the noise floor with it");
                compared++;
            }

            Assert.Greater(glitches, 10, $"only {glitches} glitches fired in {frames} frames at a 10% rate -- " +
                                         "the glitch path barely ran, so this comparison is nearly vacuous");
            Assert.Greater(compared, 200, $"only {compared} frames were actually compared");
        }

        /// <summary>
        /// ⭐ THE MOUNT ROTATION IS A STRAP CONVENTION, NOT NOISE.
        ///
        /// It is drawn once and never changes. The truth rotation is SPUN through the run so that "the offset is
        /// constant" is a real claim about the model rather than a restatement of a stationary fixture.
        /// </summary>
        [Test]
        public void MountRotation_IsFixedForTheWholeSession_NotNoise()
        {
            Limb l = BentArm();
            const float mountDeg = 8f;

            var p = BasisSyntheticTrackerParams.Identity;
            p.MountErrorDeg = mountDeg;

            var tracker = new BasisSyntheticTracker(p, k_Seed);

            Assert.AreEqual(mountDeg, Quaternion.Angle(Quaternion.identity, tracker.MountError), 1e-3f,
                "the realised mount error is not the magnitude it was asked for");

            // MEASURED ON THE COMPONENTS, NOT THROUGH Quaternion.Angle. Angle() is an acos of a dot product,
            // and near dot == 1 that is catastrophically ill-conditioned: float noise of ~1e-7 in the dot
            // becomes ~0.05 deg of reported angle. A gate below that floor would fire on arithmetic rather
            // than on drift. The components are well-conditioned, and for a small rotation a component moves
            // by about half the angle in radians -- so 1e-4 here is a sensitivity of roughly 0.01 deg.
            const float k_ComponentGate = 1e-4f;

            float worst = 0f;
            for (int f = 0; f < 400; f++)
            {
                BasisMocapPose truth = MovingTruth(l, f);
                BasisSyntheticTrackerSample s = tracker.Sample(truth, l.Mount, f / 120f);

                // Right-multiplied, so the offset is fixed IN THE BONE'S FRAME and rides the limb -- which is
                // what a strap does, and is why it is recovered by an inverse on the left.
                Quaternion offset = Quaternion.Inverse(truth.Rotation) * s.Rotation;
                Quaternion fixedError = tracker.MountError;

                worst = Mathf.Max(worst, Mathf.Abs(offset.x - fixedError.x));
                worst = Mathf.Max(worst, Mathf.Abs(offset.y - fixedError.y));
                worst = Mathf.Max(worst, Mathf.Abs(offset.z - fixedError.z));
                worst = Mathf.Max(worst, Mathf.Abs(offset.w - fixedError.w));

                AssertBitExact(truth.Position, s.Position,
                    $"a mount ROTATION error moved the reported POSITION (frame {f})");
            }

            Assert.Less(worst, k_ComponentGate,
                $"the mount offset wandered by up to {worst:E2} in quaternion components (~{worst * 2f * Mathf.Rad2Deg:F4} deg) " +
                "over the session. A strap convention is FIXED -- if it drifts it is slip, and if it rattles it " +
                "is noise, and the three have completely different consequences for the solver.");
        }

        /// <summary>
        /// Mount ROLL is where round the limb the puck sits. It moves the puck's angular station -- which is
        /// the only thing about a strapped puck the arm solver can actually read -- without touching the
        /// reported rotation. Kept separate from MountErrorDeg precisely because those two failures look
        /// nothing alike downstream.
        /// </summary>
        [Test]
        public void MountRoll_MovesTheAngularStation_ButNotTheReportedRotation()
        {
            Limb l = BentArm();
            const float standOff = 0.04f;

            var p = BasisSyntheticTrackerParams.Identity;
            p.StandOffM = standOff;
            p.MountRollDeg = 15f;

            var tracker = new BasisSyntheticTracker(p, k_Seed);

            Assert.AreNotEqual(0f, tracker.MountRollDeg,
                "the seeded mount roll came out exactly zero, so this test would prove nothing");
            Assert.LessOrEqual(Mathf.Abs(tracker.MountRollDeg), 15f + 1e-4f,
                "the seeded mount roll exceeded the magnitude it was bounded by");

            for (int f = 0; f < 100; f++)
            {
                BasisMocapPose truth = MovingTruth(l, f);
                BasisSyntheticTrackerSample s = tracker.Sample(truth, l.Mount, f / 120f);

                Vector3 offset = s.Position - truth.Position;
                Vector3 perp = offset - l.Mount.Axis * Vector3.Dot(offset, l.Mount.Axis);

                // Rolling round the limb cannot change how far OFF the limb the puck is.
                Assert.AreEqual(standOff, perp.magnitude, 1e-5f,
                    $"mount roll changed the stand-off distance (frame {f})");

                float station = Vector3.SignedAngle(l.Mount.Outward, perp, l.Mount.Axis);
                Assert.AreEqual(tracker.MountRollDeg, station, 1e-3f,
                    $"the puck is not at the angular station the mount roll asked for (frame {f})");

                AssertBitExact(truth.Rotation, s.Rotation,
                    $"mount ROLL moved the reported ROTATION (frame {f}) -- it is a position-station error only");
            }
        }

        /// <summary>
        /// ⭐ SLIP IS DRIFT, NOT NOISE, AND THIS SAYS SO WITH A LIVE CONTROL.
        ///
        /// A strap migrates over a session; it does not rattle. The discriminator is scale-free: the largest
        /// SINGLE-FRAME step as a fraction of the largest EXCURSION. A low-frequency drift covers its whole
        /// range in thousands of tiny steps; white gaussian noise of the same excursion covers it in one.
        ///
        /// The gaussian control is measured, not asserted from theory, so this test cannot pass by the gate
        /// having been set somewhere nothing could ever reach.
        /// </summary>
        [Test]
        public void Slip_IsLowFrequency_NotGaussian()
        {
            Limb l = BentArm();
            const float amp = 0.03f;
            const float hz = 0.05f;
            const float dt = 1f / 120f;
            const int frames = 4800;   // 40 s = two full periods of the fundamental

            var slipOnly = BasisSyntheticTrackerParams.Identity;
            slipOnly.SlipAlongM = amp;
            slipOnly.SlipHz = hz;

            var tracker = new BasisSyntheticTracker(slipOnly, k_Seed);

            float prev = 0f, maxStep = 0f, maxExcursion = 0f;
            for (int f = 0; f < frames; f++)
            {
                BasisMocapPose truth = Pose(l.Joint, Quaternion.identity);
                Vector3 offset = tracker.Sample(truth, l.Mount, f * dt).Position - truth.Position;

                float s = -Vector3.Dot(offset, l.Mount.Axis);   // displacement along the limb, metres
                if (f > 0) maxStep = Mathf.Max(maxStep, Mathf.Abs(s - prev));
                maxExcursion = Mathf.Max(maxExcursion, Mathf.Abs(s));
                prev = s;
            }

            // ── bounded by the model's own maths, not by a magic number
            float analyticStep = amp * BasisSyntheticTracker.Slip01MaxRatePerSecond(hz) * dt;
            Assert.LessOrEqual(maxStep, analyticStep * 1.05f,
                $"slip moved {maxStep * 1000f:F4} mm in one frame; the analytic bound for {hz} Hz is " +
                $"{analyticStep * 1000f:F4} mm. The slip generator is not band-limited the way it claims.");

            Assert.LessOrEqual(maxExcursion, amp * 1.001f,
                $"slip drifted {maxExcursion * 100f:F2} cm from a {amp * 100f:F1} cm amplitude");

            // ── NON-VACUITY: it must actually drift. The shape guarantees at least 0.2 * amp over two periods.
            Assert.Greater(maxExcursion, amp * 0.15f,
                $"slip only ever reached {maxExcursion * 1000f:F2} mm of a {amp * 1000f:F0} mm amplitude -- " +
                "it barely moved, so 'it is low-frequency' was proved about nothing");

            float slipRatio = maxStep / maxExcursion;

            // ── THE CONTROL: the same statistic on white gaussian noise, measured here rather than assumed.
            var jitterOnly = BasisSyntheticTrackerParams.Identity;
            jitterOnly.JitterSigmaM = BasisSyntheticTrackerCalibration.JitterSigmaM;
            var control = new BasisSyntheticTracker(jitterOnly, k_Seed);

            prev = 0f;
            float cStep = 0f, cExc = 0f;
            for (int f = 0; f < frames; f++)
            {
                BasisMocapPose truth = Pose(l.Joint, Quaternion.identity);
                Vector3 offset = control.Sample(truth, l.Mount, f * dt).Position - truth.Position;
                float s = -Vector3.Dot(offset, l.Mount.Axis);
                if (f > 0) cStep = Mathf.Max(cStep, Mathf.Abs(s - prev));
                cExc = Mathf.Max(cExc, Mathf.Abs(s));
                prev = s;
            }
            float noiseRatio = cStep / cExc;

            TestContext.WriteLine(
                $"\n  step/excursion:  slip {slipRatio:F5}   gaussian control {noiseRatio:F5}\n");

            Assert.Greater(noiseRatio, 0.5f,
                $"the gaussian CONTROL scored {noiseRatio:F4} on the step/excursion discriminator. It is " +
                "supposed to score near 1 -- if it does not, the discriminator does not separate drift from " +
                "noise and the assertion below is meaningless.");

            Assert.Less(slipRatio, 0.01f,
                $"slip scored {slipRatio:F5} on the step/excursion discriminator (gaussian control: " +
                $"{noiseRatio:F4}). It is behaving like noise, not like a strap migrating.");
        }

        /// <summary>
        /// Jitter is zero-mean at the sigma it advertises. A biased "noise" is a stand-off wearing a disguise,
        /// and it would show up in the corpus as a solver error rather than as an input error.
        /// </summary>
        [Test]
        public void Jitter_IsZeroMean_AtItsDocumentedSigma()
        {
            Limb l = BentArm();
            const float sigma = BasisSyntheticTrackerCalibration.JitterSigmaM;
            const int n = 20000;

            var p = BasisSyntheticTrackerParams.Identity;
            p.JitterSigmaM = sigma;

            var tracker = new BasisSyntheticTracker(p, k_Seed);
            BasisMocapPose truth = Pose(l.Joint, Quaternion.identity);

            Vector3 sum = Vector3.zero;
            double sumSq = 0;
            for (int f = 0; f < n; f++)
            {
                Vector3 r = tracker.Sample(truth, l.Mount, f / 120f).Position - truth.Position;
                sum += r;
                sumSq += r.sqrMagnitude;
            }

            Vector3 mean = sum / n;
            float tol = 4f * sigma / Mathf.Sqrt(n);   // 4 sigma of the sample mean

            Assert.Less(Mathf.Abs(mean.x), tol, $"jitter has a {mean.x * 1000f:F4} mm bias in x");
            Assert.Less(Mathf.Abs(mean.y), tol, $"jitter has a {mean.y * 1000f:F4} mm bias in y");
            Assert.Less(Mathf.Abs(mean.z), tol, $"jitter has a {mean.z * 1000f:F4} mm bias in z");

            float perAxisRms = (float)Math.Sqrt(sumSq / (3.0 * n));
            Assert.AreEqual(sigma, perAxisRms, sigma * 0.05f,
                $"jitter measured {perAxisRms * 1000f:F4} mm RMS per axis but advertises {sigma * 1000f:F1} mm");
        }

        /// <summary>
        /// A glitch fires at its documented rate and at its documented size, and the Glitched FLAG agrees with
        /// what the position actually did. A flag that does not track its cause is how this project shipped a
        /// pop detector that read the same count whether the pop was there or not.
        /// </summary>
        [Test]
        public void Glitches_FireAtTheirDocumentedRateAndSize_AndTheFlagAgrees()
        {
            Limb l = BentArm();
            const int n = 20000;

            var p = BasisSyntheticTrackerParams.Identity;
            p.GlitchChance = BasisSyntheticTrackerCalibration.GlitchChancePoor;
            p.GlitchM = BasisSyntheticTrackerCalibration.GlitchMPoor;

            var tracker = new BasisSyntheticTracker(p, k_Seed);
            BasisMocapPose truth = Pose(l.Joint, Quaternion.identity);

            int flagged = 0, moved = 0;
            for (int f = 0; f < n; f++)
            {
                BasisSyntheticTrackerSample s = tracker.Sample(truth, l.Mount, f / 120f);
                float d = Vector3.Distance(s.Position, truth.Position);
                bool didMove = d > 1e-6f;

                if (didMove) moved++;
                if (s.Glitched) flagged++;

                Assert.AreEqual(s.Glitched, didMove,
                    $"the Glitched flag says {s.Glitched} but the position moved {d * 1000f:F2} mm (frame {f}) -- " +
                    "the flag does not track its own cause");

                if (didMove)
                {
                    Assert.AreEqual(p.GlitchM, d, 1e-5f,
                        $"a glitch displaced {d * 100f:F2} cm, not the {p.GlitchM * 100f:F0} cm it advertises (frame {f})");
                }
            }

            float rate = flagged / (float)n;
            Assert.AreEqual(p.GlitchChance, rate, p.GlitchChance * 0.10f,
                $"glitches fired on {rate:P2} of samples, not the {p.GlitchChance:P0} advertised");
            Assert.AreEqual(flagged, moved, "the flag count and the movement count disagree");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 4. DELIVERY -- a stale HOLD, which is what actually reaches the solver.
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ A SLOW DEVICE HOLDS ITS LAST SAMPLE. IT DOES NOT INTERPOLATE.
        ///
        /// This distinction is the whole reason the update rate is modelled at all. An interpolated input is
        /// smoother than the runtime's, and a harness fed a smoother input than the runtime gets will report a
        /// solver that is calmer than the one that shipped -- which is exactly the class of blind spot the foot
        /// sweep already paid for once.
        ///
        /// The check is exact: every delivered position must be bit-identical to a truth pose the model was
        /// actually SHOWN. An interpolation would land between two of them and be a member of neither.
        /// </summary>
        [Test]
        public void ASlowDevice_HoldsItsLastSample_AndNeverInterpolates()
        {
            const float rateHz = 90f;
            const float frameHz = 360f;   // 4x oversampled, so a hold is unmistakable
            const float seconds = 2f;
            int frames = (int)(frameHz * seconds);

            var p = BasisSyntheticTrackerParams.Identity;
            p.UpdateRateHz = rateHz;   // spatially the identity: only the DELIVERY is under test

            var tracker = new BasisSyntheticTracker(p, k_Seed);
            var mount = BentArm().Mount;

            var shown = new HashSet<int>();
            var distinct = new HashSet<int>();
            int held = 0;

            for (int f = 0; f < frames; f++)
            {
                // A ramp, so every frame's truth is a distinct value and a hold cannot hide inside a plateau.
                var truth = Pose(new Vector3(f * 0.001f, 0f, 0f), Quaternion.identity);
                shown.Add(Bits(truth.Position.x));

                BasisSyntheticTrackerSample s = tracker.Sample(truth, mount, f / frameHz);

                Assert.IsTrue(shown.Contains(Bits(s.Position.x)),
                    $"the device delivered {s.Position.x:F6} on frame {f}, which is not any value it was ever " +
                    "shown. It INTERPOLATED. The runtime holds a stale pose; it does not blend toward the next one.");

                distinct.Add(Bits(s.Position.x));
                if (s.Held) held++;
            }

            int expectedDistinct = Mathf.RoundToInt(rateHz * seconds);
            Assert.AreEqual(expectedDistinct, distinct.Count, expectedDistinct * 0.05f,
                $"a {rateHz} Hz device delivered {distinct.Count} distinct poses in {seconds} s. It should deliver " +
                $"about {expectedDistinct} -- one per device tick, held in between.");

            float expectedHeldFraction = 1f - rateHz / frameHz;
            Assert.AreEqual(expectedHeldFraction, held / (float)frames, 0.05f,
                $"{held}/{frames} frames were flagged Held; a {rateHz} Hz device read at {frameHz} Hz should be " +
                $"stale on about {expectedHeldFraction:P0} of them");
        }

        /// <summary>
        /// Latency delivers THE PAST. On a ramp the delivered value must correspond to the truth one latency
        /// ago, to within one sample period -- not to the present, and not to something in between.
        /// </summary>
        [Test]
        public void Latency_DeliversThePast_NotThePresent()
        {
            const float dt = 1f / 120f;
            const int frames = 600;

            // DELIBERATELY NOT A WHOLE NUMBER OF FRAMES (0.0517 s = 6.204 frames at 120 Hz).
            //
            // A latency that is an exact multiple of the frame period puts the delivery boundary exactly ON a
            // sample timestamp every frame, and float rounding then breaks that tie in either direction -- so
            // the same sample is occasionally delivered twice. That is arithmetic, not behaviour, and gating on
            // it would make this test fail for a reason that has nothing to do with the model. An
            // incommensurate latency never lands on a tie, so the Held assertion below can be absolute.
            const float latency = 0.0517f;

            var p = BasisSyntheticTrackerParams.Identity;
            p.LatencyS = latency;   // continuous device, so the only lag is the transport

            var tracker = new BasisSyntheticTracker(p, k_Seed);
            var mount = BentArm().Mount;

            int checkedFrames = 0;
            for (int f = 0; f < frames; f++)
            {
                float t = f * dt;
                var truth = Pose(new Vector3(t, 0f, 0f), Quaternion.identity);   // x == the time it was measured
                BasisSyntheticTrackerSample s = tracker.Sample(truth, mount, t);

                if (t < latency + dt) continue;   // warm-up: the session is younger than the latency

                float lag = t - s.Position.x;
                Assert.GreaterOrEqual(lag, latency - 1e-5f,
                    $"the sample delivered at t={t:F4} was only {lag * 1000f:F1} ms old; the device advertises " +
                    $"{latency * 1000f:F0} ms of latency, so it is delivering the present");
                Assert.LessOrEqual(lag, latency + dt + 1e-5f,
                    $"the sample delivered at t={t:F4} was {lag * 1000f:F1} ms old, more than one frame past the " +
                    $"{latency * 1000f:F0} ms advertised");

                Assert.AreEqual(lag, s.AgeS, 1e-4f, $"the reported AgeS disagrees with the measured lag (frame {f})");

                // ⭐ AND LATENCY IS NOT A HOLD. The device here is continuous, so it delivers a NEW sample every
                // frame -- an old one, but a different old one each time. Held means "the same sample again",
                // which is a different failure with different consequences: a delayed-but-moving input costs the
                // solver phase, a repeated input costs it a velocity of zero followed by a step. Conflating them
                // is how a harness reports lag when the real problem is a stalled device, or vice versa.
                Assert.IsFalse(s.Held,
                    $"a latent but continuous device reported Held on frame {f}. Latency makes a sample OLD, " +
                    "not REPEATED -- if those two are the same flag, neither can be diagnosed.");

                checkedFrames++;
            }

            Assert.Greater(checkedFrames, 500, "the warm-up skip swallowed the run; almost nothing was checked");
        }

        /// <summary>
        /// A dropout is an EXTENDED HOLD, not a hole. The runtime keeps the last pose, so that is what reaches
        /// the solver -- and the held pose must be bit-identical to the last good one, because a dropout that
        /// quietly re-jitters what it is holding is a different (and much friendlier) failure than the real one.
        /// </summary>
        [Test]
        public void ADropout_ExtendsTheHold_AndHoldsTheLastGoodSampleExactly()
        {
            const float dt = 1f / 120f;
            const float devicePeriod = 1f / 100f;
            const int frames = 3000;

            var p = BasisSyntheticTrackerParams.Identity;
            p.UpdateRateHz = 100f;
            p.DropoutChance = 0.05f;
            p.DropoutHoldS = 0.20f;

            var tracker = new BasisSyntheticTracker(p, k_Seed);
            var mount = BentArm().Mount;

            bool everValid = false;
            int longHolds = 0;
            float maxAge = 0f;
            Vector3 lastDelivered = Vector3.zero;
            bool haveLast = false;

            for (int f = 0; f < frames; f++)
            {
                var truth = Pose(new Vector3(f * 0.001f, 0f, 0f), Quaternion.identity);
                BasisSyntheticTrackerSample s = tracker.Sample(truth, mount, f * dt);

                if (s.Valid) everValid = true;
                else
                {
                    Assert.IsFalse(everValid,
                        $"the device went from delivering to not delivering at frame {f}. A dropout HOLDS the " +
                        "last pose -- it does not invalidate one that already arrived.");
                    continue;
                }

                maxAge = Mathf.Max(maxAge, s.AgeS);
                if (s.AgeS > devicePeriod * 3f) longHolds++;

                if (s.Held && haveLast)
                {
                    AssertBitExact(lastDelivered, s.Position,
                        $"a held sample was not bit-identical to the one before it (frame {f}) -- the model is " +
                        "re-measuring while it claims to be holding");
                }

                lastDelivered = s.Position;
                haveLast = true;
            }

            Assert.IsTrue(everValid, "the device never delivered anything at all");
            Assert.Greater(longHolds, 20,
                $"only {longHolds} frames held longer than three device periods at a 5% dropout rate -- " +
                "the dropout path barely ran, so its behaviour was not actually exercised");
            Assert.GreaterOrEqual(maxAge, p.DropoutHoldS * 0.5f,
                $"the longest hold was {maxAge * 1000f:F0} ms; a {p.DropoutHoldS * 1000f:F0} ms dropout should " +
                "produce holds of that order");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 5. THE PROMOTION IS FAITHFUL, AND THE CALIBRATED NUMBERS ARE PINNED.
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ THE PROMOTED MODEL MUST BE THE MODEL IT WAS PROMOTED FROM.
        ///
        /// BasisArmTrackerHintAuthorityTests carries a one-off strapped-puck model whose parameter ranges were
        /// calibrated against a real user complaint. This file's job is to generalise it, not to replace it --
        /// so the generalisation is cross-checked against a verbatim copy of the original, across the original's
        /// own swept stand-offs and its own extension range.
        ///
        /// If this fails, every conclusion the arm tests reached and every conclusion drawn through the shared
        /// model are about two different pucks.
        /// </summary>
        [Test]
        public void StrappedPosition_ReproducesTheArmTestsOneOffPuckModel()
        {
            float[] reaches = { 0.50f, 0.80f, 0.95f, 0.97f, 0.99f };
            float[] slides = { -0.02f, 0f, 0.03f, BasisSyntheticTrackerCalibration.SlideM, 0.10f };

            float worst = 0f;
            int compared = 0;

            foreach (float standOff in BasisSyntheticTrackerCalibration.StandOffsM)
            {
                foreach (float reach in reaches)
                {
                    foreach (float slide in slides)
                    {
                        Vector3 hand = k_Shoulder + k_ReachDir * (reach * k_ArmLen);
                        Vector3 elbow = ElbowOnCircle(hand, k_CommandedSwivel);

                        Vector3 legacy = LegacyStrappedTracker(hand, k_CommandedSwivel, standOff, slide);
                        Vector3 promoted = BasisSyntheticTracker.StrappedPosition(k_Shoulder, elbow, hand, standOff, slide);

                        float d = Vector3.Distance(legacy, promoted);
                        worst = Mathf.Max(worst, d);
                        compared++;

                        Assert.AreEqual(0f, d, 1e-5f,
                            $"the promoted puck is {d * 1000f:F4} mm from the one-off model it replaces " +
                            $"(standOff {standOff * 100f:F0} cm, reach {reach:P1}, slide {slide * 100f:F0} cm)");
                    }
                }
            }

            TestContext.WriteLine($"\n  promoted vs one-off puck: {compared} configurations, worst {worst * 1e6f:F2} um\n");
            Assert.Greater(compared, 100, "almost nothing was compared");
        }

        /// <summary>
        /// ⭐ THE CALIBRATED CONSTANTS ARE PINNED.
        ///
        /// These numbers were not invented here. They come from models already in the repo and were calibrated
        /// against a real complaint, so a later edit must not be able to retune them by accident. Changing one
        /// deliberately means changing it HERE too, in a diff somebody has to read.
        /// </summary>
        [Test]
        public void CalibratedConstants_MatchTheModelsTheyWerePromotedFrom()
        {
            // BasisArmTrackerHintAuthorityTests.k_StandOffs
            CollectionAssert.AreEqual(
                new[] { 0.00f, 0.01f, 0.02f, 0.03f, 0.04f, 0.06f },
                BasisSyntheticTrackerCalibration.StandOffsM,
                "the stand-off sweep no longer matches BasisArmTrackerHintAuthorityTests.k_StandOffs");

            Assert.AreEqual(0.05f, BasisSyntheticTrackerCalibration.SlideM, 0f, "the strap slide changed");
            Assert.AreEqual(0.001f, BasisSyntheticTrackerCalibration.JitterSigmaM, 0f,
                "the 1 mm lighthouse noise floor changed");

            // TrackerNoise(rng, 0.001f, 0.02f, 0.02f)
            Assert.AreEqual(0.02f, BasisSyntheticTrackerCalibration.GlitchChanceTypical, 0f);
            Assert.AreEqual(0.02f, BasisSyntheticTrackerCalibration.GlitchMTypical, 0f);

            // TrackerNoise(rng, 0.001f, 0.10f, 0.03f) -- "10% of frames get a 3 cm pop"
            Assert.AreEqual(0.10f, BasisSyntheticTrackerCalibration.GlitchChancePoor, 0f);
            Assert.AreEqual(0.03f, BasisSyntheticTrackerCalibration.GlitchMPoor, 0f);

            // BasisTrackerPlacementSweep.Default().JitterCm
            Assert.AreEqual(0f, BasisSyntheticTrackerCalibration.PlacementCmPerfect, 0f);
            Assert.AreEqual(2f, BasisSyntheticTrackerCalibration.PlacementCmTypical, 0f);
            Assert.AreEqual(4f, BasisSyntheticTrackerCalibration.PlacementCmSloppy, 0f);

            // Every puck preset's stand-off must come from the calibrated sweep rather than from somewhere new.
            foreach (BasisSyntheticTrackerPreset preset in new[]
            {
                BasisSyntheticTrackerPreset.GoodPuck,
                BasisSyntheticTrackerPreset.TypicalPuck,
                BasisSyntheticTrackerPreset.PoorPuck,
                BasisSyntheticTrackerPreset.SlimeVR,
            })
            {
                float standOff = BasisSyntheticTrackerParams.FromPreset(preset).StandOffM;
                CollectionAssert.Contains(BasisSyntheticTrackerCalibration.StandOffsM, standOff,
                    $"preset {preset} uses a {standOff * 100f:F1} cm stand-off, which is not a value from the " +
                    "calibrated sweep. New numbers here need a reason somebody wrote down.");

                Assert.AreEqual(BasisSyntheticTrackerCalibration.JitterSigmaM,
                    BasisSyntheticTrackerParams.FromPreset(preset).JitterSigmaM, 0.0011f,
                    $"preset {preset} strayed far from the 1 mm lighthouse noise floor");
            }

            // The device RATES are the well-known figures; the LATENCIES are estimates and are deliberately
            // NOT pinned tightly here -- see the warning on BasisSyntheticTrackerParams.DeviceTiming.
            BasisSyntheticTrackerParams.DeviceTiming(BasisSyntheticTrackerDevice.Headset, out float hmdHz, out _);
            BasisSyntheticTrackerParams.DeviceTiming(BasisSyntheticTrackerDevice.OpenVrPuck, out float puckHz, out _);
            BasisSyntheticTrackerParams.DeviceTiming(BasisSyntheticTrackerDevice.SlimeVr, out float slimeHz, out _);
            BasisSyntheticTrackerParams.DeviceTiming(BasisSyntheticTrackerDevice.Perfect, out float perfectHz, out float perfectLat);

            Assert.AreEqual(90f, hmdHz, 0f, "the HMD rate changed");
            Assert.AreEqual(250f, puckHz, 0f, "the OpenVR puck rate changed");
            Assert.AreEqual(100f, slimeHz, 0f, "the SlimeVR rate changed");
            Assert.AreEqual(0f, perfectHz, 0f, "the neutral device must be continuous");
            Assert.AreEqual(0f, perfectLat, 0f, "the neutral device must be instantaneous");
        }

        /// <summary>
        /// The magnitudes each preset produces must land in the ranges it documents, decomposed the way the
        /// model decomposes them: the component PERPENDICULAR to the limb is the stand-off (roll-invariant, so
        /// this holds whatever angular station the mount ended up at), and the component ALONG it is the slide.
        /// </summary>
        [Test]
        public void EveryPreset_LandsInItsDocumentedPhysicalRange()
        {
            Limb l = BentArm();
            const int frames = 4000;
            const float dt = 1f / 120f;

            foreach (BasisSyntheticTrackerPreset preset in (BasisSyntheticTrackerPreset[])Enum.GetValues(typeof(BasisSyntheticTrackerPreset)))
            {
                BasisSyntheticTrackerParams p = BasisSyntheticTrackerParams.FromPreset(preset);
                var tracker = new BasisSyntheticTracker(p, k_Seed);

                var perp = new List<float>();
                var along = new List<float>();
                float worst = 0f;

                for (int f = 0; f < frames; f++)
                {
                    BasisMocapPose truth = Pose(l.Joint, Quaternion.identity);
                    BasisSyntheticTrackerSample s = tracker.Sample(truth, l.Mount, f * dt);
                    Assert.IsTrue(s.Valid, $"{preset} failed to deliver on frame {f}");

                    Vector3 offset = s.Position - truth.Position;
                    float axial = Vector3.Dot(offset, l.Mount.Axis);
                    perp.Add((offset - l.Mount.Axis * axial).magnitude);
                    along.Add(-axial);
                    worst = Mathf.Max(worst, offset.magnitude);
                }

                perp.Sort();
                along.Sort();
                float perpMedian = perp[perp.Count / 2];
                float alongMedian = along[along.Count / 2];

                if (preset == BasisSyntheticTrackerPreset.Ideal)
                {
                    Assert.AreEqual(0f, worst, 0f, "the Ideal preset displaced the joint at all");
                    continue;
                }

                // Perpendicular offset == the stand-off, whatever the roll. Jitter and slip widen it; the
                // median is robust to the glitch tail.
                float perpTol = 5f * p.JitterSigmaM + 0.002f;
                Assert.AreEqual(p.StandOffM, perpMedian, perpTol,
                    $"{preset}: the puck sits {perpMedian * 100f:F2} cm off the bone, not the " +
                    $"{p.StandOffM * 100f:F1} cm it documents");

                // Axial offset == the slide, drifting by at most the slip amplitude.
                float alongTol = p.SlipAlongM + 5f * p.JitterSigmaM + 0.002f;
                Assert.AreEqual(p.SlideM, alongMedian, alongTol,
                    $"{preset}: the strap sits {alongMedian * 100f:F2} cm proximal of the joint, not the " +
                    $"{p.SlideM * 100f:F1} cm it documents");

                // And nothing may exceed everything the model can contribute at once.
                float ceiling = p.StandOffM + p.SlideM + p.SlipAlongM + p.GlitchM + 8f * p.JitterSigmaM + 1e-3f;
                Assert.LessOrEqual(worst, ceiling,
                    $"{preset}: the worst excursion was {worst * 100f:F2} cm, past the {ceiling * 100f:F2} cm " +
                    "every modelled effect could produce simultaneously -- something is contributing that the " +
                    "parameters do not describe");
            }
        }

        /// <summary>
        /// A straight limb has no outward normal: the joint sits ON the root->tip axis and the direction the
        /// puck stands off in is not determined by geometry. That is not an edge case -- it is full extension,
        /// where a VR user spends most of their time and where BasisArmSolveCore's hard pole epsilon lives. The
        /// mount must SAY SO rather than return an arbitrary direction silently.
        /// </summary>
        [Test]
        public void MountForLimb_FlagsAStraightLimbAsDegenerate_RatherThanInventingADirection()
        {
            Vector3 root = k_Shoulder;
            Vector3 tip = k_Shoulder + k_ReachDir * k_ArmLen;

            BasisSyntheticTrackerMount straight = BasisSyntheticTracker.MountForLimb(root, (root + tip) * 0.5f, tip);
            Assert.IsTrue(straight.Degenerate,
                "a perfectly straight limb was not flagged degenerate, so the stand-off direction it returned " +
                "is arbitrary and nothing said so");
            Assert.AreEqual(1f, straight.Outward.magnitude, 1e-4f, "the substituted normal must still be a unit vector");
            Assert.AreEqual(0f, Vector3.Dot(straight.Outward, straight.Axis), 1e-4f,
                "the substituted normal must still be perpendicular to the limb");

            BasisSyntheticTrackerMount bent = BentArm().Mount;
            Assert.IsFalse(bent.Degenerate, "a bent arm has a perfectly good outward normal");
            Assert.Greater(bent.RadiusM, 0.05f, "the test fixture's arm is not actually bent, so this proves nothing");
            Assert.AreEqual(0f, Vector3.Dot(bent.Outward, bent.Axis), 1e-4f, "Outward must be perpendicular to Axis");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 6. INSTRUMENTATION -- the numbers, printed, so a claim about this model can be checked.
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Not a gate -- a MEASUREMENT. Prints what each preset actually does to a joint, so the next person to
        /// quote one of these numbers does not have to take the doc comment's word for it.
        /// </summary>
        [Test]
        public void Instrument_WhatEachPresetDoesToAJoint()
        {
            Limb l = BentArm();
            const int frames = 4000;
            const float dt = 1f / 120f;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("  ══ WHAT A SYNTHETIC TRACKER HANDS THE SOLVER (bent arm, 4000 frames @ 120 Hz) ══");
            sb.AppendLine("  repeat% = the SAME pose again (device slower than the frame rate, or dropped out).");
            sb.AppendLine("  age = how old the delivered pose is; latency makes it old WITHOUT making it a repeat.");
            sb.AppendLine();
            sb.AppendLine("  preset        rate    lat     |offset| p50   p95      max      repeat%   age      glitch%");
            sb.AppendLine("  -----------   -----   -----   ----------    ------   ------   -------   ------   -------");

            foreach (BasisSyntheticTrackerPreset preset in (BasisSyntheticTrackerPreset[])Enum.GetValues(typeof(BasisSyntheticTrackerPreset)))
            {
                BasisSyntheticTrackerParams p = BasisSyntheticTrackerParams.FromPreset(preset);
                var tracker = new BasisSyntheticTracker(p, k_Seed);

                var mags = new List<float>();
                int repeats = 0, glitched = 0;
                double ageSum = 0;

                for (int f = 0; f < frames; f++)
                {
                    BasisMocapPose truth = Pose(l.Joint, Quaternion.identity);
                    BasisSyntheticTrackerSample s = tracker.Sample(truth, l.Mount, f * dt);
                    mags.Add(Vector3.Distance(s.Position, truth.Position));
                    if (s.Held) repeats++;
                    if (s.Glitched) glitched++;
                    ageSum += s.AgeS;
                }
                mags.Sort();

                sb.AppendLine(
                    $"  {preset,-11}   {p.UpdateRateHz,5:F0}   {p.LatencyS * 1000f,4:F0}ms   " +
                    $"{mags[mags.Count / 2] * 100f,7:F2} cm   {mags[(int)(mags.Count * 0.95f)] * 100f,5:F2}   " +
                    $"{mags[mags.Count - 1] * 100f,5:F2}   {100f * repeats / frames,6:F1}%   " +
                    $"{ageSum / frames * 1000.0,4:F1}ms   {100f * glitched / frames,6:F2}%");
            }

            TestContext.WriteLine(sb.ToString());
            Assert.Pass();
        }
    }
}
