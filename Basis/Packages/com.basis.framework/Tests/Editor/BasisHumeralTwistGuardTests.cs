using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// NOTHING BOUNDED HUMERAL AXIAL ROTATION. These are the gates on the guard that now does.
    ///
    /// THE DEFECT, MEASURED IN UNITY: with an elbow tracker on an extended arm, rolling the tracker rolled
    /// the humerus 1:1 THROUGH A FULL 360 DEGREES. The clavicle never moved while it happened --
    /// BasisShoulderSolveCore is direction-driven and a pure roll changes no direction, so the girdle
    /// contributed exactly 0.0 deg and never engaged. Every neighbouring degree of freedom was bounded
    /// (elbow height, elbow flexion, forearm roll, wrist relief, clavicle-vs-chest); this one was not.
    ///
    /// WHAT THE SWEEP BELOW REPRODUCES, and why it is the real thing rather than a contrivance: an elbow
    /// tracker's POSITION is the solver's pole, and rolling a forearm strap orbits that position about the
    /// arm axis. The solver obeys the pole -- correctly -- with a swivel about shoulder->hand. On a
    /// near-straight arm the elbow's lever arm has collapsed, so obeying the pole is almost pure ROLL of
    /// the upper arm. Sweeping the pole 0-360 deg therefore sweeps the humerus through a full turn, and the
    /// UNGUARDED control here reaches a flat 180 deg of twist.
    ///
    /// THE FOUR PROMISES THESE TESTS HOLD THE GUARD TO:
    ///   1. IT BOUNDS, AND THE CONTROL PROVES THE TEST STILL MEASURES. Every sweep runs twice -- once with
    ///      the bind fields fed, once with `default` -- and the control must still break 170 deg. A test
    ///      that only asserted "small" would pass just as happily if the sweep stopped exciting the DOF.
    ///   2. IT MOVES NOTHING. The correction is applied about shoulder->ELBOW, the humerus's own axis, and
    ///      the elbow LIES on it, so no joint moves at all -- the guard writes rotations only. This is the
    ///      assertion that pins the OPERATOR: a regression to a shoulder->hand swivel moves the elbow
    ///      (measured: 1.6 cm), and a positional shoulder->elbow swivel moves the HAND. Checking both
    ///      deltas plus the correction's own axis catches every one of those.
    ///   3. IT IS THE EXACT IDENTITY INSIDE THE ENVELOPE. Legal poses take the untouched path bit for bit,
    ///      not "approximately unchanged".
    ///   4. ZERO DECLINES, FIELD BY FIELD. Any one of the five bind inputs left at its struct default turns
    ///      the guard off, so every existing caller, test and offline sweep stays bit-identical.
    ///
    /// AND THE ONE THIS FILE EXISTS FOR MOST: <see cref="RefAxis_IsBakedPerRig_GuardEngagesOnEveryConvention"/>.
    /// BindHumerusRefAxis is baked per rig by BasisFullBodyIK.BakeHumerusTwistBind precisely because a
    /// HARDCODED axis lands PARALLEL to the bone on rigs whose humerus points down its local -Y -- and a
    /// parallel reference does not fail loudly, it projects to zero and the guard DECLINES SILENTLY. That
    /// test runs every rig convention through the real solver, and it also feeds the naive hardcoded axis
    /// to the same solver to show the hazard is real rather than hypothetical.
    ///
    /// The measurement here mirrors the core's own reference construction (it is the definition of the
    /// quantity being bounded), so it is checked against an INDEPENDENT closed form as well: swivelling a
    /// vector standing alpha off the swivel axis by theta twists it by 2*atan(cos(alpha)*tan(theta/2))
    /// about its own axis. The control sweep must match that curve, which is what stops the whole file
    /// being circular.
    /// </summary>
    public class BasisHumeralTwistGuardTests
    {
        const float UpperLen = 0.30f;
        const float ForeLen = 0.30f;
        const float TotalLen = UpperLen + ForeLen;
        static readonly Vector3 Shoulder = Vector3.zero;

        const float SoftDeg = BasisArmSolveCore.HumeralTwistSoftDeg;   // 120
        const float HardDeg = BasisArmSolveCore.HumeralTwistHardDeg;   // 150

        /// <summary>Elbow flexions the sweep visits. 170 is MaxElbowAngleDeg (the straightest the solver
        /// will ever produce, and where the roll defect is worst); 90 is where shoulder->elbow and
        /// shoulder->hand are ORTHOGONAL, which is the pose that separates the correct operator from the
        /// shoulder->hand one.</summary>
        static readonly float[] Flexions = { 170f, 165f, 160f, 150f, 135f, 120f, 90f };

        /// <summary>THE ARM HANGS. Not for convenience -- it is the pose the defect was measured in, and it
        /// also keeps BasisElbowAnatomyCore's elbow-height guard exactly inert (the elbow never reaches the
        /// shoulder's height, let alone the 5%-of-limb soft margin above it), so the twist guard is the only
        /// thing acting on the sweep. Every sweep ASSERTS that inertness rather than assuming it.</summary>
        static readonly Vector3 HangDir = Vector3.down;

        /// <summary>Deliberately NOT identity: the guard carries its reference through
        /// clavicle * inverse(bindClavicle), and an identity pair would let a regression that dropped the
        /// carry entirely pass unnoticed. See <see cref="Twist_IsMeasuredInTheClavicleFrame"/>.</summary>
        static readonly Quaternion ClavBind = Quaternion.Euler(4f, -11f, 6f);

        // ------------------------------------------------------------------ rig bind data

        struct Bind
        {
            public string Name;
            public Quaternion HumerusRot;   // world bind rotation of the upper-arm bone
            public Vector3 Dir;             // world bind direction, shoulder->elbow, unit
            public Vector3 RefAxis;         // BONE-LOCAL reference axis, baked
            public Vector3 LocalBone;       // the bone's own axis, in its local frame (what the convention IS)
        }

        /// <summary>Mirrors BasisFullBodyIK.BakeHumerusTwistBind exactly. If that bake changes, this must
        /// change with it -- and the rig-convention test below is what makes a divergence visible.</summary>
        static Bind Bake(string name, Quaternion bindRot, Vector3 bindDir)
        {
            Bind b = default;
            b.Name = name;
            b.HumerusRot = bindRot;
            b.Dir = bindDir.normalized;

            Vector3 localBone = Quaternion.Inverse(bindRot) * b.Dir;
            Vector3 refLocal = Mathf.Abs(localBone.y) < 0.9f ? Vector3.up : Vector3.forward;
            Vector3 perp = refLocal - localBone * Vector3.Dot(refLocal, localBone);
            b.RefAxis = perp.sqrMagnitude > 1e-8f ? perp.normalized : Vector3.zero;
            b.LocalBone = localBone;
            return b;
        }

        /// <summary>The rig the numeric sweeps run on: a Unity-humanoid-style bind whose humerus points
        /// down its local +X.</summary>
        static Bind DefaultRig() => Bake("bone +X", Quaternion.FromToRotation(Vector3.right, HangDir), HangDir);

        /// <summary>Bone-space conventions a real avatar can arrive with. The world pose is identical in
        /// every one of them -- a hanging arm is a hanging arm -- and ONLY the bone's local frame differs,
        /// which is exactly what "rig convention" means and exactly the variable the baked reference axis
        /// exists to absorb.</summary>
        static Bind[] RigConventions()
        {
            return new[]
            {
                Bake("identity bind (humerus down local -Y)", Quaternion.identity, HangDir),
                Bake("X-90 (Blender bone-Y-up authoring)", Quaternion.Euler(-90f, 0f, 0f), HangDir),
                Bake("Z+90 roll", Quaternion.Euler(0f, 0f, 90f), HangDir),
                Bake("humerus down local -Y, yawed 55", Quaternion.Euler(0f, 55f, 0f), HangDir),
                Bake("bone +Y (FromTo up->down)", Quaternion.FromToRotation(Vector3.up, HangDir), HangDir),
                Bake("bone +X (FromTo right->down)", Quaternion.FromToRotation(Vector3.right, HangDir), HangDir),
                Bake("arbitrary non-axis-aligned bind", Quaternion.Euler(37f, -22f, 61f), HangDir),
            };
        }

        // ------------------------------------------------------------------ pose construction

        /// <summary>A hanging arm bent to `flexDeg` at the elbow, with the ELBOW TRACKER rolled `rollDeg`
        /// about the shoulder->hand axis -- the strap turning on the forearm. The animated pose IS the bind
        /// pose (humerus along the bind direction, hand exactly on target), so at roll 0 the whole solve is
        /// the identity and the measured twist is exactly zero: everything the sweep reads afterwards is
        /// the tracker's roll and nothing else.
        ///
        /// `w` rotates the entire scenario -- geometry, bone rotations, body frame and the LIVE clavicle --
        /// for the clavicle-frame equivariance test. `feedGuard` false leaves all five new fields at their
        /// struct default, which is the unguarded control.</summary>
        static BasisArmSolveInput Pose(in Bind b, float flexDeg, float rollDeg, bool feedGuard, Quaternion w)
        {
            Vector3 elbow0 = HangDir * UpperLen;
            Quaternion bend = Quaternion.AngleAxis(180f - flexDeg, Vector3.right);
            Vector3 hand0 = elbow0 + (bend * HangDir) * ForeLen;
            Vector3 acN = hand0.normalized;                       // the shoulder is the origin
            Vector3 hint0 = Quaternion.AngleAxis(rollDeg, acN) * elbow0;

            BasisArmSolveInput i = default;
            i.Shoulder = Shoulder;
            i.Elbow = w * elbow0;
            i.Hand = w * hand0;
            i.RootRotation = w * b.HumerusRot;
            i.MidRotation = w * (bend * b.HumerusRot);
            i.TargetPosition = i.Hand;                            // triangle solve is a no-op
            i.TargetRotation = i.MidRotation;
            i.TargetOffset = Quaternion.identity;
            i.PlayerUp = w * Vector3.up;
            i.TorsoUp = w * Vector3.up;
            i.HintWeight = true;
            // A real elbow TRACKER: this also stands the wrist-roll relief and the pole-collapse
            // stabiliser down (both are !HintIsTracker), and HintRotation left at zero stands the tracker
            // forearm roll down, so the swivel and the two guards are the only things running.
            i.HintIsTracker = true;
            i.HintPosition = w * hint0;
            i.HintMaxStepDeg = float.MaxValue;

            if (feedGuard)
            {
                i.ClavicleRotation = w * ClavBind;
                i.BindClavicleRotation = ClavBind;
                i.BindHumerusRotation = b.HumerusRot;
                i.BindHumerusDir = b.Dir;
                i.BindHumerusRefAxis = b.RefAxis;
            }
            return i;
        }

        /// <summary>Where the elbow must land: exactly on the tracker. Used to prove the sweep is doing
        /// what it claims (nothing else perturbed the elbow) before anything is read off it.</summary>
        static Vector3 ExpectedElbow(float flexDeg, float rollDeg, Quaternion w)
        {
            Vector3 elbow0 = HangDir * UpperLen;
            Quaternion bend = Quaternion.AngleAxis(180f - flexDeg, Vector3.right);
            Vector3 hand0 = elbow0 + (bend * HangDir) * ForeLen;
            return w * (Quaternion.AngleAxis(rollDeg, hand0.normalized) * elbow0);
        }

        static Vector3 ExpectedHand(float flexDeg, Quaternion w)
        {
            Vector3 elbow0 = HangDir * UpperLen;
            Quaternion bend = Quaternion.AngleAxis(180f - flexDeg, Vector3.right);
            return w * (elbow0 + (bend * HangDir) * ForeLen);
        }

        /// <summary>Angle between the humerus and the shoulder->hand axis at a given flexion. The arm is
        /// isoceles, so it is exactly (180 - flexion)/2 -- and it is the alpha in the closed form below.</summary>
        static float ShoulderAlphaDeg(float flexDeg) => 0.5f * (180f - flexDeg);

        /// <summary>THE INDEPENDENT ORACLE. Rotating by theta about an axis that stands alpha off a vector
        /// twists that vector about its OWN axis by 2*atan(cos(alpha) * tan(theta/2)) relative to the
        /// minimal swing. No part of the solver or of the measurement below is involved in deriving it.</summary>
        static float PredictedTwistDeg(float alphaDeg, float rollDeg)
        {
            float t = Mathf.Repeat(rollDeg + 180f, 360f) - 180f;   // wrap to (-180, 180]
            float h = 0.5f * t * Mathf.Deg2Rad;
            return 2f * Mathf.Atan2(Mathf.Cos(alphaDeg * Mathf.Deg2Rad) * Mathf.Sin(h), Mathf.Cos(h)) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------------ measurement

        /// <summary>The humeral twist, read off the SOLVED arm. Mirrors the core's reference construction --
        /// it is the definition of the bounded quantity -- and works on the unguarded control too, which is
        /// the whole point: the control's bind fields are `default`, so the core reports nothing at all.</summary>
        static float MeasureTwistDeg(in Bind b, Quaternion clavLive, Quaternion clavBind,
                                     Vector3 elbowSolved, Quaternion rootSolved, out float swingDeg)
        {
            Vector3 liveDir = (elbowSolved - Shoulder).normalized;
            Quaternion carry = clavLive * Quaternion.Inverse(clavBind);
            Quaternion restHum = carry * b.HumerusRot;
            Vector3 restDir = (carry * b.Dir).normalized;

            swingDeg = Vector3.Angle(restDir, liveDir);
            Quaternion swingMin = BasisQuaternionExt.FromToRotation(restDir, liveDir);

            Vector3 refPerp = (swingMin * restHum) * b.RefAxis;
            Vector3 livePerp = rootSolved * b.RefAxis;
            refPerp -= liveDir * Vector3.Dot(refPerp, liveDir);
            livePerp -= liveDir * Vector3.Dot(livePerp, liveDir);
            return Vector3.SignedAngle(refPerp, livePerp, liveDir);
        }

        static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

        // ==================================================================================
        // 1. THE BOUND, AND THE CONTROL THAT PROVES THE TEST STILL MEASURES
        // ==================================================================================

        [Test]
        public void HumeralTwist_IsBounded_WhileTheUnguardedControlRunsAwayTo180()
        {
            Bind b = DefaultRig();
            Assert.That(b.RefAxis, Is.Not.EqualTo(Vector3.zero), "the bake declined on the default rig; the sweep would measure nothing.");

            float worstControl = 0f, worstGuarded = 0f, worstOracleErr = 0f, worstReported = 0f;
            float worstElbowErr = 0f, worstHandErr = 0f;
            float controlAt = 0f, guardedAt = 0f;

            foreach (float flex in Flexions)
            {
                float alpha = ShoulderAlphaDeg(flex);
                for (int step = 0; step <= 180; step++)
                {
                    float roll = step * 2f;

                    // The elbow-height guard must be inert for the WHOLE sweep, or something other than the
                    // twist guard is shaping these numbers. Evaluated on the pose the solver will see.
                    Vector3 wantElbow = ExpectedElbow(flex, roll, Quaternion.identity);
                    Vector3 wantHand = ExpectedHand(flex, Quaternion.identity);
                    float anat = BasisElbowAnatomyCore.GuardSwivelRad(Shoulder, wantElbow, wantHand, Vector3.up, TotalLen);
                    Assert.That(anat, Is.EqualTo(0f),
                        $"flex={flex} roll={roll}: the elbow ANATOMY guard fired ({anat:0.0000} rad); this sweep is meant to isolate the twist guard.");

                    BasisArmSolveCore.Solve(Pose(b, flex, roll, false, Quaternion.identity), out var c);
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, true, Quaternion.identity), out var g);

                    worstElbowErr = Mathf.Max(worstElbowErr, Vector3.Distance(c.ElbowSolved, wantElbow));
                    worstHandErr = Mathf.Max(worstHandErr, Vector3.Distance(c.HandSolved, wantHand));

                    float ct = MeasureTwistDeg(b, ClavBind, ClavBind, c.ElbowSolved, c.RootRotationSolved, out _);
                    float gt = MeasureTwistDeg(b, ClavBind, ClavBind, g.ElbowSolved, g.RootRotationSolved, out float swing);

                    // The closed form, with nothing from the solver in it.
                    worstOracleErr = Mathf.Max(worstOracleErr, Mathf.Abs(Mathf.Abs(ct) - Mathf.Abs(PredictedTwistDeg(alpha, roll))));

                    // The core's own pre-guard diagnostic must agree with the control's measured twist.
                    worstReported = Mathf.Max(worstReported, Mathf.Abs(g.HumeralTwistDeg - ct));

                    Assert.That(swing, Is.LessThan(BasisArmSolveCore.TwistSwingFadeStartDeg),
                        $"flex={flex} roll={roll}: clavicle->humerus swing {swing:0.0} deg reached the anti-parallel fade, so the guard was applied at partial strength -- the bound below would not be a clean read.");

                    if (Mathf.Abs(ct) > worstControl) { worstControl = Mathf.Abs(ct); controlAt = flex; }
                    // ⚠️ THE HARD BOUND CANNOT HOLD AT THE SEAM, AND THAT IS TOPOLOGY, NOT SLACK. The twist is a
                    // PRINCIPAL angle, so any guard maps a CIRCLE onto an ARC; a map that is the identity inside
                    // the arc must either be DISCONTINUOUS or satisfy g(180) = 0. The discontinuous branch was
                    // measured at an 80.009 deg arm-pose jump for a 0.05 deg input step (1430x amplification);
                    // g(180) = 0 drives the humerus to zero twist and makes the arm sweep 140 deg BACKWARDS.
                    // There is no third option, so the guard takes g(180) = 180 and the bound relaxes over the
                    // last ~15 deg. Below 165 deg this envelope is STRICTER than the old flat constant.
                    if (Mathf.Abs(ct) <= 165f && Mathf.Abs(gt) > worstGuarded) { worstGuarded = Mathf.Abs(gt); guardedAt = flex; }
                    float envelope = Mathf.Max(HardDeg, 2f * Mathf.Abs(ct) - 180f);
                    Assert.That(Mathf.Abs(gt), Is.LessThan(envelope + 1f),
                        $"flex={flex} roll={roll}: guarded twist {gt:0.0} deg is past the seam envelope {envelope:0.0} deg for a pre-guard twist of {ct:0.0} deg.");

                    // The guard reduces the magnitude; it never flips the sense of the rotation.
                    if (Mathf.Abs(ct) > SoftDeg && Mathf.Abs(ct) < 179f)
                    {
                        Assert.That(Mathf.Abs(gt), Is.LessThan(Mathf.Abs(ct) + 1e-3f),
                            $"flex={flex} roll={roll}: the guard INCREASED the twist, {ct:0.0} -> {gt:0.0} deg.");
                        Assert.That(Mathf.Sign(gt), Is.EqualTo(Mathf.Sign(ct)),
                            $"flex={flex} roll={roll}: the guard flipped the twist's sign, {ct:0.0} -> {gt:0.0} deg.");
                    }
                }
            }

            Assert.That(worstElbowErr, Is.LessThan(1e-4f),
                $"the solved elbow missed the tracker by {worstElbowErr * 1000f:0.000} mm; the sweep is not exciting the DOF it claims to.");
            Assert.That(worstHandErr, Is.LessThan(1e-4f),
                $"the solved hand missed its target by {worstHandErr * 1000f:0.000} mm; the sweep is not the pose it claims to be.");
            Assert.That(worstOracleErr, Is.LessThan(0.5f),
                $"the measured twist departs from the independent closed form by {worstOracleErr:0.000} deg; the measurement itself is suspect.");
            Assert.That(worstReported, Is.LessThan(0.05f),
                $"BasisArmSolveResult.HumeralTwistDeg disagrees with the measured PRE-guard twist by {worstReported:0.000} deg.");

            // ANTI-TAUTOLOGY. Without the bind fields the humerus still runs away, so a pass below is the
            // guard working and not the sweep having gone quiet.
            Assert.That(worstControl, Is.GreaterThan(170f),
                $"the UNGUARDED control only reached {worstControl:0.0} deg of humeral twist (worst at flexion {controlAt:0}); this test has stopped measuring the defect it was written for.");

            Assert.That(worstGuarded, Is.LessThan(HardDeg + 1f),
                $"guarded humeral twist reached {worstGuarded:0.0} deg (worst at flexion {guardedAt:0}), past the {HardDeg:0} deg hard bound.");

            TestContext.WriteLine($"control worst |twist| = {worstControl:0.0} deg (flexion {controlAt:0}), guarded worst = {worstGuarded:0.0} deg (flexion {guardedAt:0}); oracle err {worstOracleErr:0.000} deg.");
        }

        // ==================================================================================
        // 2. THE GUARD MOVES NOTHING -- AND THAT IS WHAT PINS THE OPERATOR
        // ==================================================================================

        [Test]
        public void HumeralTwistGuard_MovesNoJoint_AndActsAboutShoulderToElbow()
        {
            Bind b = DefaultRig();
            float worstElbowMove = 0f, worstReachErr = 0f, worstAxisDeg = 0f, worstAngleErr = 0f;
            float mostGuard = 0f;

            foreach (float flex in Flexions)
            {
                for (int step = 0; step <= 180; step++)
                {
                    float roll = step * 2f;
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, false, Quaternion.identity), out var c);
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, true, Quaternion.identity), out var g);

                    worstElbowMove = Mathf.Max(worstElbowMove, Vector3.Distance(c.ElbowSolved, g.ElbowSolved));
                    worstReachErr = Mathf.Max(worstReachErr, Vector3.Distance(c.HandSolved, g.HandSolved));
                    mostGuard = Mathf.Max(mostGuard, Mathf.Abs(g.HumeralTwistGuardDeg));

                    if (Mathf.Abs(g.HumeralTwistGuardDeg) > 0.5f)
                    {
                        // The correction, isolated: guarded = correction * control.
                        Quaternion delta = g.RootRotationSolved * Quaternion.Inverse(c.RootRotationSolved);
                        delta.ToAngleAxis(out float deg, out Vector3 axis);
                        if (deg > 180f) { deg = 360f - deg; axis = -axis; }

                        worstAngleErr = Mathf.Max(worstAngleErr, Mathf.Abs(deg - Mathf.Abs(g.HumeralTwistGuardDeg)));

                        // ...about the HUMERUS's own axis. At flexion 90 shoulder->hand stands 45 deg away
                        // from this, so a shoulder->hand regression cannot hide inside the tolerance.
                        Vector3 humerus = (g.ElbowSolved - Shoulder).normalized;
                        float off = Vector3.Angle(axis.normalized, humerus);
                        off = Mathf.Min(off, 180f - off);
                        worstAxisDeg = Mathf.Max(worstAxisDeg, off);
                    }
                }
            }

            Assert.That(mostGuard, Is.GreaterThan(1f),
                "the guard never fired anywhere in the sweep; the assertions below would be vacuous.");
            Assert.That(worstElbowMove, Is.LessThan(1e-6f),
                $"the twist guard moved the ELBOW {worstElbowMove * 1000f:0.000} mm. It is applied about shoulder->elbow and the elbow lies ON that axis, so this must be zero -- a shoulder->hand swivel is what moves it (measured: 1.6 cm).");
            Assert.That(worstReachErr, Is.LessThan(1e-6f),
                $"the twist guard moved the HAND {worstReachErr * 1000f:0.000} mm; the correction must be rotation-only.");
            Assert.That(worstAxisDeg, Is.LessThan(0.5f),
                $"the correction's axis stood {worstAxisDeg:0.00} deg off shoulder->elbow; it is not being applied about the humerus's own axis.");
            Assert.That(worstAngleErr, Is.LessThan(0.05f),
                $"HumeralTwistGuardDeg disagrees with the rotation actually applied to the upper arm by {worstAngleErr:0.000} deg.");

            TestContext.WriteLine($"elbow moved {worstElbowMove * 1000f:0.000} mm, hand moved {worstReachErr * 1000f:0.000} mm, worst correction {mostGuard:0.0} deg about an axis {worstAxisDeg:0.00} deg off the humerus.");
        }

        // ==================================================================================
        // 3. EXACT IDENTITY INSIDE THE ENVELOPE
        // ==================================================================================

        [Test]
        public void HumeralTwistGuard_InsideTheEnvelope_IsTheExactIdentity()
        {
            Bind b = DefaultRig();
            float worstTwist = 0f;

            foreach (float flex in Flexions)
            {
                for (float roll = -110f; roll <= 110.001f; roll += 1f)
                {
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, false, Quaternion.identity), out var c);
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, true, Quaternion.identity), out var g);

                    worstTwist = Mathf.Max(worstTwist, Mathf.Abs(g.HumeralTwistDeg));

                    Assert.IsTrue(g.HumeralTwistGuardDeg == 0f,
                        $"flex={flex} roll={roll:0}: an in-envelope pose (twist {g.HumeralTwistDeg:0.0} deg) was corrected by {g.HumeralTwistGuardDeg:0.000000} deg. Inside the envelope the guard must be the EXACT identity.");
                    Assert.IsTrue(Same(g.RootRotationSolved, c.RootRotationSolved),
                        $"flex={flex} roll={roll:0}: the upper arm is not bit-identical to the unguarded solve inside the envelope.");
                    Assert.IsTrue(Same(g.MidRotationSolved, c.MidRotationSolved) && Same(g.HintDelta, c.HintDelta),
                        $"flex={flex} roll={roll:0}: the forearm / hint delta drifted inside the envelope.");
                }
            }

            Assert.That(worstTwist, Is.LessThan(SoftDeg),
                $"the +/-110 deg roll band reached {worstTwist:0.0} deg of twist, past the {SoftDeg:0} deg soft limit -- the band is no longer inside the envelope, so this test is not asserting what it says.");

            TestContext.WriteLine($"in-envelope sweep peaked at {worstTwist:0.0} deg of twist with a correction of exactly 0.");
        }

        // The general statement, independent of how the sweep is parameterised: whenever the measured
        // pre-guard twist is inside the soft limit, the correction is exactly zero.
        [Test]
        public void HumeralTwistGuard_FiresOnlyOutsideTheSoftLimit()
        {
            Bind b = DefaultRig();
            int fired = 0, silent = 0;

            foreach (float flex in Flexions)
            {
                for (int step = 0; step <= 360; step++)
                {
                    float roll = step;
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, true, Quaternion.identity), out var g);
                    if (Mathf.Abs(g.HumeralTwistDeg) <= SoftDeg)
                    {
                        Assert.IsTrue(g.HumeralTwistGuardDeg == 0f,
                            $"flex={flex} roll={roll:0}: twist {g.HumeralTwistDeg:0.00} deg is inside the {SoftDeg:0} deg soft limit but the guard applied {g.HumeralTwistGuardDeg:0.000000} deg.");
                        silent++;
                    }
                    else if (Mathf.Abs(g.HumeralTwistDeg) < 179.9f)
                    {
                        Assert.IsTrue(g.HumeralTwistGuardDeg != 0f,
                            $"flex={flex} roll={roll:0}: twist {g.HumeralTwistDeg:0.00} deg is past the soft limit but the guard did nothing.");
                        fired++;
                    }
                    else
                    {
                        Assert.That(g.HumeralTwistGuardDeg, Is.EqualTo(0f),
                            $"flex={flex} roll={roll:0}: at the {g.HumeralTwistDeg:0.00} deg seam the guard must be silent, not apply {g.HumeralTwistGuardDeg:0.000000} deg.");
                    }
                }
            }

            Assert.That(fired, Is.GreaterThan(0), "the guard never fired; the outside-the-limit branch is untested.");
            Assert.That(silent, Is.GreaterThan(0), "the guard always fired; the inside-the-limit branch is untested.");
            TestContext.WriteLine($"{fired} samples past the soft limit, {silent} inside it.");
        }

        // ==================================================================================
        // 4. ZERO DECLINES -- FIELD BY FIELD
        // ==================================================================================

        [Test]
        public void HumeralTwistGuard_ZeroDefaults_Decline_FieldByField()
        {
            Bind b = DefaultRig();
            const float flex = 170f, roll = 150f;   // draws 14.9 deg. NOT 180: that is the seam, where the guard is deliberately silent

            BasisArmSolveCore.Solve(Pose(b, flex, roll, true, Quaternion.identity), out var fed);
            Assert.That(Mathf.Abs(fed.HumeralTwistGuardDeg), Is.GreaterThan(5f),
                $"the reference pose only drew a {fed.HumeralTwistGuardDeg:0.00} deg correction; the decline assertions below would be vacuous.");

            BasisArmSolveInput baseline = Pose(b, flex, roll, false, Quaternion.identity);
            BasisArmSolveCore.Solve(baseline, out var declined);
            Assert.IsTrue(declined.HumeralTwistGuardDeg == 0f && declined.HumeralTwistDeg == 0f,
                $"all-default bind fields still produced twist {declined.HumeralTwistDeg} / correction {declined.HumeralTwistGuardDeg}.");

            for (int field = 0; field < 5; field++)
            {
                BasisArmSolveInput i = Pose(b, flex, roll, true, Quaternion.identity);
                string name;
                switch (field)
                {
                    case 0: i.ClavicleRotation = default; name = nameof(i.ClavicleRotation); break;
                    case 1: i.BindClavicleRotation = default; name = nameof(i.BindClavicleRotation); break;
                    case 2: i.BindHumerusRotation = default; name = nameof(i.BindHumerusRotation); break;
                    case 3: i.BindHumerusDir = default; name = nameof(i.BindHumerusDir); break;
                    default: i.BindHumerusRefAxis = default; name = nameof(i.BindHumerusRefAxis); break;
                }

                BasisArmSolveCore.Solve(i, out var r);
                Assert.IsTrue(r.HumeralTwistGuardDeg == 0f,
                    $"a zero {name} did not decline: the guard applied {r.HumeralTwistGuardDeg:0.000000} deg.");
                Assert.IsTrue(r.HumeralTwistDeg == 0f,
                    $"a zero {name} still reported a twist of {r.HumeralTwistDeg:0.000000} deg.");
                Assert.IsTrue(Same(r.RootRotationSolved, declined.RootRotationSolved)
                           && Same(r.MidRotationSolved, declined.MidRotationSolved)
                           && Same(r.HintDelta, declined.HintDelta)
                           && Same(r.MidPostRoll, declined.MidPostRoll),
                    $"a zero {name} declined the correction but the solve is not bit-identical to the all-default caller.");
                Assert.That(Vector3.Distance(r.ElbowSolved, declined.ElbowSolved), Is.EqualTo(0f),
                    $"a zero {name} moved the elbow.");
            }
        }

        // ==================================================================================
        // 5. ⭐ THE BAKED REFERENCE AXIS, ON RIGS WHOSE BIND IS NOT IDENTITY
        // ==================================================================================

        /// <summary>
        /// THE HIGHEST-RISK CLAIM IN THE FIX, GATED. BindHumerusRefAxis is baked PER RIG because a hardcoded
        /// axis lands parallel to the bone on rigs whose humerus points down its local -Y -- and parallel
        /// does not fail loudly. It projects to exactly zero against the bone axis, the core's
        /// `refPerp.sqrMagnitude > k_SqrEpsilon` test rejects it, and the guard DECLINES SILENTLY: no
        /// exception, no diagnostic, an unbounded humerus and a green test suite.
        ///
        /// Every convention below is the SAME hanging arm in world space. Only the bone's local frame
        /// differs -- which is the only thing that ever differs between rigs -- so any convention that
        /// fails here fails for rig authoring reasons alone.
        /// </summary>
        [Test]
        public void RefAxis_IsBakedPerRig_GuardEngagesOnEveryConvention()
        {
            int hazardous = 0;

            foreach (Bind b in RigConventions())
            {
                Assert.That(b.RefAxis.sqrMagnitude, Is.GreaterThan(0.99f),
                    $"[{b.Name}] the bake produced no reference axis (local bone {b.LocalBone}); the guard would decline on this rig.");
                Assert.That(Mathf.Abs(Vector3.Dot(b.RefAxis, b.LocalBone.normalized)), Is.LessThan(1e-3f),
                    $"[{b.Name}] the baked reference axis is not perpendicular to the bone (dot {Vector3.Dot(b.RefAxis, b.LocalBone.normalized):0.0000}).");

                float worstControl = 0f, worstGuarded = 0f, mostGuard = 0f;

                foreach (float flex in new[] { 170f, 160f, 135f, 90f })
                {
                    for (int step = 0; step <= 90; step++)
                    {
                        float roll = step * 4f;
                        BasisArmSolveCore.Solve(Pose(b, flex, roll, false, Quaternion.identity), out var c);
                        BasisArmSolveCore.Solve(Pose(b, flex, roll, true, Quaternion.identity), out var g);

                        float ct = MeasureTwistDeg(b, ClavBind, ClavBind, c.ElbowSolved, c.RootRotationSolved, out _);
                        float gt = MeasureTwistDeg(b, ClavBind, ClavBind, g.ElbowSolved, g.RootRotationSolved, out _);

                        worstControl = Mathf.Max(worstControl, Mathf.Abs(ct));
                        if (Mathf.Abs(ct) <= 165f) worstGuarded = Mathf.Max(worstGuarded, Mathf.Abs(gt));
                        float envelope = Mathf.Max(HardDeg, 2f * Mathf.Abs(ct) - 180f);
                        Assert.That(Mathf.Abs(gt), Is.LessThan(envelope + 1f),
                            $"[{b.Name}] flex={flex} roll={roll}: guarded twist {gt:0.0} deg past the seam envelope {envelope:0.0} deg.");
                        mostGuard = Mathf.Max(mostGuard, Mathf.Abs(g.HumeralTwistGuardDeg));

                        // The guard must not move a joint on ANY convention either.
                        Assert.That(Vector3.Distance(c.ElbowSolved, g.ElbowSolved), Is.LessThan(1e-6f),
                            $"[{b.Name}] flex={flex} roll={roll}: the guard moved the elbow.");
                        // ⚠️ NOT a hand-displacement check: the guard writes neither HandSolved, so this can
                        // never fail. Real hand motion happens through the STREAM (HintDelta is applied to the
                        // ROOT, which carries its children) and is gated by BasisArmHumeralTwistSeamTests.
                        Assert.That(Vector3.Distance(c.HandSolved, g.HandSolved), Is.LessThan(1e-6f),
                            $"[{b.Name}] flex={flex} roll={roll}: the guard perturbed the reported hand.");
                    }
                }

                Assert.That(worstControl, Is.GreaterThan(170f),
                    $"[{b.Name}] the unguarded control only reached {worstControl:0.0} deg; this convention is no longer exercising the defect.");
                Assert.That(mostGuard, Is.GreaterThan(1f),
                    $"[{b.Name}] the guard NEVER ENGAGED on this rig -- the silent decline the per-rig bake exists to prevent.");
                Assert.That(worstGuarded, Is.LessThan(HardDeg + 1f),
                    $"[{b.Name}] guarded twist reached {worstGuarded:0.0} deg, past the {HardDeg:0} deg hard bound.");

                bool naiveWouldBeParallel = Mathf.Abs(b.LocalBone.normalized.y) > 0.99f;
                if (naiveWouldBeParallel) hazardous++;

                TestContext.WriteLine($"[{b.Name}] local bone {b.LocalBone.normalized}, baked ref {b.RefAxis}: control {worstControl:0.0} -> guarded {worstGuarded:0.0} deg, worst correction {mostGuard:0.0} deg{(naiveWouldBeParallel ? "  (a hardcoded local +Y would be PARALLEL here)" : "")}");
            }

            Assert.That(hazardous, Is.GreaterThan(0),
                "not one convention in this set puts the bone along local +/-Y, so the set does not actually cover the hazard the per-rig bake exists for.");
        }

        /// <summary>
        /// The hazard, demonstrated through the real solver rather than asserted. On a rig whose humerus
        /// points down its local -Y, feeding the naive hardcoded axis a per-rig bake replaces makes the
        /// guard decline SILENTLY -- and the twist then runs to 180 deg with nothing reported. This is the
        /// failure the baked axis buys off, and it is why <see cref="RefAxis_IsBakedPerRig_GuardEngagesOnEveryConvention"/>
        /// asserting "engages" is a real assertion and not decoration.
        /// </summary>
        [Test]
        public void RefAxis_HardcodedWorldAxis_DeclinesSilently_OnAMinusYRig()
        {
            Bind rig = Bake("identity bind (humerus down local -Y)", Quaternion.identity, HangDir);
            Assert.That(Mathf.Abs(rig.LocalBone.normalized.y), Is.GreaterThan(0.99f),
                "this rig is supposed to point its humerus down local -Y; the demonstration is meaningless otherwise.");

            float worstNaiveTwist = 0f, mostNaiveGuard = 0f, mostBakedGuard = 0f;

            foreach (float flex in new[] { 170f, 160f })
            {
                for (int step = 0; step <= 90; step++)
                {
                    float roll = step * 4f;

                    BasisArmSolveInput naive = Pose(rig, flex, roll, true, Quaternion.identity);
                    naive.BindHumerusRefAxis = Vector3.up;   // the hardcoded axis the per-rig bake replaces
                    BasisArmSolveCore.Solve(naive, out var n);

                    BasisArmSolveCore.Solve(Pose(rig, flex, roll, true, Quaternion.identity), out var g);

                    worstNaiveTwist = Mathf.Max(worstNaiveTwist,
                        Mathf.Abs(MeasureTwistDeg(rig, ClavBind, ClavBind, n.ElbowSolved, n.RootRotationSolved, out _)));
                    mostNaiveGuard = Mathf.Max(mostNaiveGuard, Mathf.Abs(n.HumeralTwistGuardDeg));
                    mostBakedGuard = Mathf.Max(mostBakedGuard, Mathf.Abs(g.HumeralTwistGuardDeg));

                    Assert.IsTrue(n.HumeralTwistDeg == 0f,
                        $"flex={flex} roll={roll}: the hardcoded axis reported a twist of {n.HumeralTwistDeg:0.000} deg; the failure is supposed to be a silent decline, so this demonstration no longer matches the hazard.");
                }
            }

            Assert.IsTrue(mostNaiveGuard == 0f,
                $"the hardcoded axis applied a correction of {mostNaiveGuard:0.000} deg; it was expected to decline entirely.");
            Assert.That(worstNaiveTwist, Is.GreaterThan(170f),
                $"with the hardcoded axis the humerus only reached {worstNaiveTwist:0.0} deg, so the decline is not visibly costly here.");
            Assert.That(mostBakedGuard, Is.GreaterThan(1f),
                "the BAKED axis failed to engage on the same rig -- the bake is not buying the hazard off.");

            TestContext.WriteLine($"hardcoded local +Y on a -Y rig: correction {mostNaiveGuard:0.000} deg (silent decline), twist ran to {worstNaiveTwist:0.0} deg; the baked axis corrects up to {mostBakedGuard:0.0} deg on the identical pose.");
        }

        // ==================================================================================
        // 6. THE REFERENCE IS THE CLAVICLE'S, NOT THE WORLD'S
        // ==================================================================================

        /// <summary>The guard carries its bind reference through clavicle * inverse(bindClavicle). So
        /// rotating the clavicle AND the whole arm together by the same rotation must leave the measured
        /// twist and the applied correction untouched: the girdle taking the humerus somewhere new is not
        /// the humerus twisting. Drop the carry and this fails immediately.</summary>
        [Test]
        public void Twist_IsMeasuredInTheClavicleFrame()
        {
            Bind b = DefaultRig();
            Quaternion w = Quaternion.Euler(20f, 35f, -15f);
            float worstTwistErr = 0f, worstGuardErr = 0f, mostGuard = 0f;

            foreach (float flex in new[] { 170f, 160f, 135f, 90f })
            {
                foreach (float roll in new[] { 0f, 45f, 90f, 135f, 170f, 200f, 250f, 300f })
                {
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, true, Quaternion.identity), out var a);
                    BasisArmSolveCore.Solve(Pose(b, flex, roll, true, w), out var r);

                    worstTwistErr = Mathf.Max(worstTwistErr, Mathf.Abs(a.HumeralTwistDeg - r.HumeralTwistDeg));
                    worstGuardErr = Mathf.Max(worstGuardErr, Mathf.Abs(a.HumeralTwistGuardDeg - r.HumeralTwistGuardDeg));
                    mostGuard = Mathf.Max(mostGuard, Mathf.Abs(a.HumeralTwistGuardDeg));
                }
            }

            Assert.That(mostGuard, Is.GreaterThan(1f), "the guard never fired; the comparison is vacuous.");
            Assert.That(worstTwistErr, Is.LessThan(0.2f),
                $"rotating the clavicle and the arm together changed the measured twist by {worstTwistErr:0.000} deg; the reference is not being carried through the girdle.");
            Assert.That(worstGuardErr, Is.LessThan(0.2f),
                $"rotating the clavicle and the arm together changed the correction by {worstGuardErr:0.000} deg.");
        }

        // ==================================================================================
        // 7. NOTHING PATHOLOGICAL COMES OUT
        // ==================================================================================

        [Test]
        public void HumeralTwistGuard_ProducesNoNaN_OnDegenerateBinds()
        {
            Bind b = DefaultRig();

            // A reference axis laid along the bone, a bind direction anti-parallel to the live humerus, and
            // a sub-epsilon bind direction. None of these may reach a bone as NaN -- a NaN transform
            // PERSISTS in Unity, so the arm would never recover even once good data returned.
            foreach (float flex in new[] { 170f, 90f })
            {
                foreach (float roll in new[] { 0f, 90f, 180f, 270f })
                {
                    BasisArmSolveInput i = Pose(b, flex, roll, true, Quaternion.identity);

                    // Reference axis deliberately laid ALONG the bone: the core must reject it, not divide by it.
                    BasisArmSolveInput parallel = i;
                    parallel.BindHumerusRefAxis = Quaternion.Inverse(b.HumerusRot) * b.Dir;
                    BasisArmSolveCore.Solve(parallel, out var p);
                    AssertFinite(p, $"ref axis along the bone, flex={flex} roll={roll}");
                    Assert.IsTrue(p.HumeralTwistGuardDeg == 0f,
                        $"a reference axis laid along the bone produced a correction of {p.HumeralTwistGuardDeg:0.000} deg; it carries no twist information and must decline.");

                    // A bind direction anti-parallel to the live humerus: the FromToRotation seam the
                    // TwistSwingFade window exists for.
                    BasisArmSolveInput flipped = i;
                    flipped.BindHumerusDir = -b.Dir;
                    BasisArmSolveCore.Solve(flipped, out var f);
                    AssertFinite(f, $"anti-parallel bind dir, flex={flex} roll={roll}");

                    // Sub-epsilon bind direction: below the decline threshold, so it must decline cleanly.
                    BasisArmSolveInput tiny = i;
                    tiny.BindHumerusDir = b.Dir * 1e-6f;
                    BasisArmSolveCore.Solve(tiny, out var t);
                    AssertFinite(t, $"tiny bind dir, flex={flex} roll={roll}");
                    Assert.IsTrue(t.HumeralTwistGuardDeg == 0f, "a sub-epsilon bind direction must decline.");
                }
            }
        }

        static void AssertFinite(in BasisArmSolveResult r, string what)
        {
            Assert.IsTrue(Finite(r.RootRotationSolved), $"{what}: non-finite upper-arm rotation.");
            Assert.IsTrue(Finite(r.MidRotationSolved), $"{what}: non-finite forearm rotation.");
            Assert.IsTrue(Finite(r.HintDelta), $"{what}: non-finite hint delta.");
            Assert.IsTrue(Finite(r.MidPostRoll), $"{what}: non-finite post roll.");
            Assert.IsFalse(float.IsNaN(r.ElbowSolved.x) || float.IsNaN(r.ElbowSolved.y) || float.IsNaN(r.ElbowSolved.z), $"{what}: NaN elbow.");
            Assert.IsFalse(float.IsNaN(r.HumeralTwistDeg) || float.IsNaN(r.HumeralTwistGuardDeg), $"{what}: NaN twist diagnostic.");
        }

        static bool Finite(Quaternion q) =>
            !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
              float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));
    }
}
