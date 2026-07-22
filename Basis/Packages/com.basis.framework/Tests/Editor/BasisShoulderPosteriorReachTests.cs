using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// POSTERIOR REACH — the shoulder girdle's share of an arm going BEHIND the body.
    ///
    /// The coupled scapulohumeral swing is symmetric in the horizontal plane: one ProtractionFactor
    /// answers both a reach across the chest and a reach behind the back. Human shoulders are not.
    /// Glenohumeral horizontal EXTENSION runs out at ~45–60 deg posterior — the figure this repo
    /// already carries on BasisBodyPlausibility's ShoulderPosteriorDeg channel — while horizontal
    /// FLEXION reaches ~130. Measured against the live core with a T-pose bind, a 90 deg posterior
    /// hand target put 86.8 deg on the humerus and 3.2 deg on the girdle, so the humerus sat ~27 deg
    /// past its own declared bar with nothing clamping it. That surplus is paid as humeral rotation
    /// and reads as a pinched deltoid.
    ///
    /// Compounding it, the rear poses that need the girdle most are the ones `reachFade` switches it
    /// OFF for: a hand behind the back sits around 0.55 of reach, under k_ReachEngageThreshold. The
    /// reach gate is right about a folded arm IN FRONT and wrong about a folded arm BEHIND, so the
    /// retraction term is gated on DIRECTION alone and skips `engage` — the same call the shrug makes,
    /// for the same reason.
    ///
    /// What these tests hold it to:
    ///   - a level reach behind actually retracts the girdle, and brings the glenohumeral joint back
    ///     under the 60 deg bar (measured the way BasisBodyPlausibility measures it, not by azimuth,
    ///     which divides elevation out and overstates the load on a low arm);
    ///   - forward / cross-body is BIT-IDENTICAL — the asymmetry is the whole point;
    ///   - a resting hang whose hand drifts behind the hip does NOT retract;
    ///   - the term survives a closed reach gate (the anti-tautology case: engage is ~0 there, so
    ///     every degree of girdle in that pose comes from this term and nothing else);
    ///   - mirrored on the left, continuous, twist-free, and inside the clamp.
    /// </summary>
    public class BasisShoulderPosteriorReachTests
    {
        const float ArmLen = 0.70f;    // T-pose shoulder→hand
        const float ClavLen = 0.16f;   // shoulder→upperArm
        const float ElbowLen = 0.44f;  // shoulder→lowerArm

        // Shipped values: BasisFullBodyIK k_ShoulderCoupleRatio / k_ShoulderMaxDeg,
        // BasisSettingsDefaults FBIKShoulderElevation / FBIKShoulderProtraction.
        const float Couple = 0.4f, MaxShoulderDeg = 25f, Elevation = 0.4f, Protraction = 0.3f;

        // BasisBodyPlausibility.cs:707 — "shoulder (glenohumeral) extension 45-60 deg posterior".
        const float GhWarnDeg = 60f;

        static Vector3 Rest(bool isLeft) => new Vector3(isLeft ? -1f : 1f, 0f, 0f);

        // Azimuth 0 = lateral (the T-pose / frontal plane), POSITIVE = posterior (toward -Z).
        static Vector3 Dir(float azDeg, float elDeg, bool isLeft)
        {
            float a = azDeg * Mathf.Deg2Rad, e = elDeg * Mathf.Deg2Rad;
            float lat = Mathf.Cos(e) * Mathf.Cos(a) * (isLeft ? -1f : 1f);
            return new Vector3(lat, Mathf.Sin(e), -Mathf.Cos(e) * Mathf.Sin(a)).normalized;
        }

        // The BasisBodyPlausibility.cs:1138 measure: -asin(dot(humerus, chestForward)), + = behind.
        static float PostDeg(Vector3 d) => Mathf.Asin(Mathf.Clamp(-d.normalized.z, -1f, 1f)) * Mathf.Rad2Deg;

        static BasisShoulderSolveInput Input(float az, float el, float reachFrac, bool hasElbow,
                                             bool isLeft = false, float protraction = Protraction,
                                             bool retractEnabled = true)
        {
            Vector3 d = Dir(az, el, isLeft);
            BasisShoulderSolveInput s = default;
            s.ShoulderPos = Vector3.zero;
            s.HandTargetPos = d * (ArmLen * reachFrac);
            s.ElbowPos = d * (ElbowLen * reachFrac);
            s.HasElbow = hasElbow;
            s.HasShoulderTracker = false;
            s.ChestRot = Quaternion.identity;
            s.TposeChestRot = Quaternion.identity;
            s.TposeShoulderRot = Quaternion.identity;
            s.TposeArmDirWorld = Rest(isLeft);
            s.TposeArmLength = ArmLen;
            s.TposeClavicleLength = ClavLen;
            s.TposeElbowLength = hasElbow ? ElbowLen : 0f;
            s.ShrugEnabled = false;
            s.RetractEnabled = retractEnabled;
            s.ElevationFactor = Elevation;
            s.ProtractionFactor = protraction;
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

        // ----------------------------------------------------------------- it engages

        [Test]
        public void LevelReachBehind_RetractsTheGirdle()
        {
            var r = Solve(Input(75f, 0f, 0.90f, false));
            Assert.That(r.Apply, Is.True);
            Assert.That(r.RetractDeg, Is.GreaterThan(12f),
                $"a level reach behind barely retracted the girdle ({r.RetractDeg:0.0} deg).");
            Assert.That(r.AppliedAngleDeg, Is.GreaterThan(12f),
                $"the retraction did not reach the applied girdle ({r.AppliedAngleDeg:0.0} deg).");
        }

        [Test]
        public void ReachBehind_BringsGlenohumeralUnderTheAnatomicalBar()
        {
            // The poses that measured past the 60 deg bar before this term existed.
            foreach (var p in new[] { (75f, 0f, 0.90f), (80f, -25f, 0.55f), (95f, -10f, 0.95f) })
            {
                var s = Input(p.Item1, p.Item2, p.Item3, false);
                var r = Solve(s);
                Vector3 armDir = (s.HandTargetPos - s.ShoulderPos).normalized;
                Vector3 rootAfter = r.ShoulderRotation * Rest(false);
                float gh = PostDeg(armDir) - PostDeg(rootAfter);
                Assert.That(gh, Is.LessThanOrEqualTo(GhWarnDeg),
                    $"az={p.Item1} el={p.Item2}: glenohumeral extension {gh:0.0} deg is past the {GhWarnDeg} deg bar.");
            }
        }

        [Test]
        public void Retraction_SurvivesAClosedReachGate()
        {
            // A hand scratching the mid-back: deeply posterior but only ~0.55 of reach, so
            // ReachEngage returns 0 and the coupled swing contributes NOTHING. Every degree of
            // girdle here comes from the retraction term — which is exactly why it skips `engage`.
            var r = Solve(Input(80f, -25f, 0.55f, false));
            Assert.That(r.ComputedWeight, Is.LessThan(0.01f),
                $"the reach gate was supposed to be shut in this pose (engage {r.ComputedWeight:0.000}); " +
                "if it opened, this test no longer proves the term survives it.");
            Assert.That(r.RetractDeg, Is.GreaterThan(10f),
                $"the girdle stayed home behind the back ({r.RetractDeg:0.0} deg) — the reach gate ate it.");
        }

        // ----------------------------------------------------------------- it stays out of the way

        [Test]
        public void ForwardAndCrossBody_AreUntouched()
        {
            // Horizontal FLEXION has ~130 deg of glenohumeral range, so the shipped coefficient is
            // already right there. This term must be exactly zero across the whole forward half.
            for (float az = -170f; az <= 0f; az += 10f)
            foreach (bool elbow in new[] { false, true })
            {
                var r = Solve(Input(az, 0f, 0.95f, elbow));
                Assert.That(r.RetractDeg, Is.EqualTo(0f),
                    $"az={az} elbow={elbow}: retraction fired on a FORWARD reach ({r.RetractDeg:0.0} deg).");
            }
        }

        [Test]
        public void RestingHang_DoesNotRetract()
        {
            // A relaxed arm hangs with the hand a little behind the hip. Driving this off the raw
            // posterior COMPONENT rather than the horizontal azimuth is what keeps it silent here:
            // azimuth would read these as deep posterior reaches and retract the girdle at idle.
            foreach (var p in new[] { (0f, -90f), (25f, -80f), (40f, -75f), (10f, -60f) })
            {
                var r = Solve(Input(p.Item1, p.Item2, 0.95f, false));
                Assert.That(r.RetractDeg, Is.EqualTo(0f),
                    $"az={p.Item1} el={p.Item2}: the girdle retracted on a resting hang ({r.RetractDeg:0.0} deg).");
            }
        }

        [Test]
        public void ProtractionSliderZero_DisablesIt()
        {
            var r = Solve(Input(90f, 0f, 0.95f, false, false, 0f));
            Assert.That(r.RetractDeg, Is.EqualTo(0f), "Protraction 0 must switch the retraction off outright.");
        }

        [Test]
        public void ToggleOff_IsExactlyTheOldGirdle()
        {
            // FBIKShoulderRetraction off must restore the pre-term result BIT-FOR-BIT, not merely
            // something close: the term is the only thing gated by it, so anything else moving means
            // it leaked into a shared code path. Note the core input defaults RetractEnabled FALSE, so
            // the offline sweeps and BasisShoulderDirectionTests (both `= default`) never see the term.
            for (float az = 40f; az <= 120f; az += 20f)
            foreach (bool elbow in new[] { false, true })
            {
                var on = Solve(Input(az, 0f, 0.95f, elbow));
                var off = Solve(Input(az, 0f, 0.95f, elbow, false, Protraction, retractEnabled: false));
                Assert.That(off.RetractDeg, Is.EqualTo(0f), $"az={az}: the toggle did not switch the term off.");
                Assert.That(on.RetractDeg, Is.GreaterThan(0f), $"az={az}: the term was not on to begin with.");
                Assert.That(Quaternion.Angle(off.ShoulderRotation, on.ShoulderRotation), Is.GreaterThan(1f),
                    $"az={az}: on and off produced the same girdle — the toggle is not wired to anything.");
            }
        }

        [Test]
        public void WithoutTheTerm_TheDefectStillReproduces()
        {
            // Anti-tautology: with the term switched off (Protraction 0 kills it), the same pose must
            // still land the glenohumeral joint past the bar. If this ever stops failing, the defect
            // moved somewhere else and the tests above are measuring the wrong thing.
            var s = Input(90f, 0f, 0.95f, false, false, 0f);
            var r = Solve(s);
            Vector3 armDir = (s.HandTargetPos - s.ShoulderPos).normalized;
            Vector3 rootAfter = r.ShoulderRotation * Rest(false);
            float gh = PostDeg(armDir) - PostDeg(rootAfter);
            Assert.That(gh, Is.GreaterThan(GhWarnDeg + 15f),
                $"the unmitigated defect no longer reproduces (glenohumeral {gh:0.0} deg).");
        }

        // ----------------------------------------------------------------- it stays well-behaved

        [Test]
        public void LeftArm_RetractsMirrored()
        {
            for (float az = 40f; az <= 120f; az += 20f)
            {
                var right = Solve(Input(az, 0f, 0.95f, false, false));
                var left = Solve(Input(az, 0f, 0.95f, false, true));
                Assert.That(left.RetractDeg, Is.EqualTo(right.RetractDeg).Within(0.01f),
                    $"az={az}: left and right retracted by different amounts.");

                // Both arm roots must swing BACKWARD (chest-local -Z), not one each way.
                float rz = (right.ShoulderRotation * Rest(false)).z;
                float lz = (left.ShoulderRotation * Rest(true)).z;
                Assert.That(rz, Is.LessThan(-0.01f), $"az={az}: the right arm root did not swing backward.");
                Assert.That(lz, Is.LessThan(-0.01f), $"az={az}: the left arm root did not swing backward.");
            }
        }

        [Test]
        public void Retraction_IsContinuous()
        {
            // A jump here is a visible clavicle pop as the hand crosses into the rear half. Swept over
            // the whole rear half, both sides of the k_RetractStartDot ramp, at every useful elevation.
            //
            // Scoped to steps where the retraction is actually live on one side or the other. There is a
            // SEPARATE, PRE-EXISTING ~4.3 deg/deg gradient at az +-180 -- the arm pointing exactly
            // opposite the bind direction, i.e. FromToRotation's anti-parallel branch picking an
            // arbitrary perpendicular axis. Measured with RetractDeg == 0.000 on both sides of it, so it
            // is not this term's, and the existing Shoulder_MovesSmoothly_OnSmoothArmMotion sweep stops
            // short of it too. Asserting on it here would be this test policing someone else's code.
            float worst = 0f; float at = 0f, atEl = 0f;
            foreach (float el in new[] { -80f, -50f, -20f, 0f, 20f, 40f })
            {
                Quaternion prev = Quaternion.identity; float prevRetract = 0f; bool has = false;
                for (float az = -180f; az <= 180.001f; az += 1f)
                {
                    var r = Solve(Input(az, el, 0.90f, false));
                    if (has && (prevRetract > 0f || r.RetractDeg > 0f))
                    {
                        float d = Quaternion.Angle(prev, r.ShoulderRotation);
                        if (d > worst) { worst = d; at = az; atEl = el; }
                    }
                    prev = r.ShoulderRotation; prevRetract = r.RetractDeg; has = true;
                }
            }
            Assert.That(worst, Is.LessThan(3f),
                $"the girdle jumped {worst:0.00} deg between 1 deg samples at az {at:0} el {atEl:0} (a pop).");
        }

        [Test]
        public void Retraction_LeaksNoTwist()
        {
            // The file's load-bearing invariant: the clavicle swings the arm root, it never rolls
            // with the humerus. A twist-following clavicle was tried and reverted.
            float worst = 0f;
            for (float az = -180f; az <= 180f; az += 5f)
            for (float el = -90f; el <= 90f; el += 5f)
            foreach (bool elbow in new[] { false, true })
                worst = Mathf.Max(worst, Solve(Input(az, el, 0.95f, elbow)).TwistLeakDeg);
            Assert.That(worst, Is.LessThan(0.5f), $"retraction leaked {worst:0.00} deg of twist into the girdle.");
        }

        [Test]
        public void Retraction_StaysInsideTheClamp()
        {
            float worst = 0f;
            for (float az = -180f; az <= 180f; az += 5f)
            for (float el = -90f; el <= 90f; el += 5f)
            foreach (bool elbow in new[] { false, true })
                worst = Mathf.Max(worst, Solve(Input(az, el, 0.95f, elbow)).AppliedAngleDeg);
            Assert.That(worst, Is.LessThanOrEqualTo(MaxShoulderDeg + 0.5f),
                $"girdle reached {worst:0.0} deg, past the {MaxShoulderDeg} deg clamp.");
        }
    }
}
