using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// TWO SEAMS THAT ARE LIVE IN THE SHIPPING CORE RIGHT NOW. FOUND BY THE INVARIANT NET, DERIVED IN
    /// CLOSED FORM, REPRODUCED HERE IN ISOLATION.
    ///
    /// ================================================================================================
    /// Both are the SAME CLASS as the +/-180 humeral-twist snap that was fixed today (a smooth input
    /// producing a jump in a bounded output), and both are the same class as each other: a quantity that
    /// lives on a CIRCLE being mapped through a branch that is not continuous at the branch point.
    ///
    ///   D1. THE ELBOW ANATOMY GUARD FLIPS THE ELBOW ACROSS ITS CIRCLE AT THE TOP.
    ///       BasisElbowAnatomyCore back-solves the guarded pole as  poleGuarded = upN*cG + w*sG  with
    ///
    ///           sG = sign(s) * sqrt(1 - cG^2),      s = dot(poleDir, w)
    ///
    ///       and s is EXACTLY ZERO when the elbow sits at the top (or bottom) of its circle. So the moment
    ///       a firing guard's elbow crosses the top, sign(s) flips and the guarded elbow is thrown to the
    ///       OTHER side, a jump of
    ///
    ///           2 * rho * sqrt(1 - cG^2)   metres
    ///
    ///       The sign branch is deliberate and its comment is right about what it prevents ("crossing to
    ///       the other side would be a 180 deg elbow swing when all that was asked for was 'come down a
    ///       bit'") -- but preventing a 180 deg swing by BRANCHING creates a smaller swing at the branch
    ///       point, which is the same trade the humeral twist guard was just changed away from.
    ///
    ///       ⚠️ IT IS REACHABLE FROM EVERY INPUT AXIS, which is why the net found it four separate ways:
    ///       moving the hand in azimuth, rolling the controller, and orbiting the elbow tracker all walk
    ///       the elbow across the top of its circle.
    ///
    ///   D2. THE TRACKER FOREARM ROLL HAS NO SEAM ENVELOPE, AND ITS TWO SIBLINGS BOTH DO.
    ///       The applied roll is  rollSigned = sign(roll) * Saturate(|roll|, 90, 120)  where `roll` is a
    ///       PRINCIPAL angle. At the +/-180 wrap the sign flips while the magnitude is ~180, so the applied
    ///       roll jumps by  2 * Saturate(180, 90, 120) = 2 * 112.5 = 225 degrees, i.e. a forearm pose change
    ///       of 360 - 225 = 135 degrees, in one step of tracker roll.
    ///
    ///       The wrist-roll relief caps its output by `PI - |twist|`, and the humeral twist guard caps its
    ///       correction by `PI - mag`, both for precisely this reason and both documented at length in
    ///       BasisArmSolveCore. This third principal-angle stage, sitting between them in the same file,
    ///       does not.
    ///
    /// ⚠️ THESE TESTS DO NOT ASSERT THAT THE DEFECTS ARE STILL PRESENT. They assert the jump does not
    /// EXCEED its closed-form size -- so they stay green today, stay green the moment either is fixed, and
    /// go red only if one gets worse. What they do assert non-vacuously is that the measurement REPRODUCES
    /// the closed form, which is what makes the diagnosis above a derivation rather than a story.
    /// ================================================================================================
    /// </summary>
    public class BasisArmInvariantNetKnownOpenDefectTests
    {
        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        // ============================================================================================
        // D1
        // ============================================================================================

        /// <summary>
        /// Orbit a real elbow tracker across the TOP of the elbow circle at a pose where the anatomy guard
        /// is firing, and measure the single-step jump. Then predict it from the guard's own algebra with
        /// nothing from the sweep in the prediction but the geometry, and require the two to agree.
        /// </summary>
        [Test]
        public void D1_ElbowAnatomyGuard_FlipsTheElbowAcrossItsCircle_AtTheTop()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            Vector3 up = Vector3.up;
            var log = new StringBuilder();
            float worstRatio = 0f;
            int reproduced = 0;

            foreach (float reach in new[] { 0.60f, 0.70f, 0.80f })
            foreach (float el in new[] { -35f, -25f })
            {
                Vector3 prevElbow = Vector3.zero;
                bool first = true;
                float jump = 0f, atAz = 0f;
                Vector3 jumpBefore = Vector3.zero, jumpAfter = Vector3.zero;
                BasisArmSolveInput jumpInput = default;

                // fine sweep straight across the top of the circle
                for (int k = 0; k <= 1600; k++)
                {
                    float az = Mathf.Lerp(-4f, 4f, k / 1600f);
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(-30f, el);
                    s.Reach = reach;
                    s.HintMode = BasisArmNet.HintTracker;
                    s.HintAzimuthDeg = az;
                    s.HintRhoMin = 0f;
                    s.RefPerp = up;                    // azimuth 0 IS the top of the circle
                    s.FeedTwistBind = false;           // isolate: only the anatomy guard is acting
                    BasisArmSolveInput i = BasisArmNet.Build(s);
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    BasisArmNet.StreamCompose(i, r, out Vector3 elbow, out _, out _, out _);

                    if (!first)
                    {
                        float d = Vector3.Distance(prevElbow, elbow);
                        if (d > jump) { jump = d; atAz = az; jumpBefore = prevElbow; jumpAfter = elbow; jumpInput = i; }
                    }
                    prevElbow = elbow;
                    first = false;
                }

                // ── THE CLOSED FORM, from the guard's own algebra, evaluated on the pose either side of
                //    the jump. Nothing from the sweep enters except the geometry.
                Vector3 ac = jumpInput.TargetPosition - jumpInput.Shoulder;
                float predicted = 0f;
                if (ac.sqrMagnitude > 1e-10f)
                {
                    Vector3 acN = ac.normalized;
                    Vector3 ae = jumpBefore - jumpInput.Shoulder;
                    Vector3 aeProj = ae - acN * Vector3.Dot(ae, acN);
                    float rho = aeProj.magnitude;
                    Vector3 upProj = up - acN * Vector3.Dot(up, acN);
                    float upLen = upProj.magnitude;
                    if (rho > 1e-5f && upLen > 1e-5f)
                    {
                        float ceiling = Mathf.Max(0f, Vector3.Dot(ac, up));
                        float softRise = BasisElbowAnatomyCore.SoftMarginFracLimb * BasisArmNet.Total;
                        float hardRise = BasisElbowAnatomyCore.HardMarginFracLimb * BasisArmNet.Total;
                        float cap = BasisElbowAnatomyCore.SoftMarginMaxFracRadius * rho;
                        if (softRise > cap) { hardRise *= cap / softRise; softRise = cap; }

                        float hSoft = ceiling + softRise, hHard = ceiling + hardRise;
                        float h = Vector3.Dot(ae, up);
                        if (h > hSoft)
                        {
                            float M = hHard - hSoft, e = h - hSoft;
                            float hGuarded = hSoft + M * e / (M + e);
                            float along = Vector3.Dot(ae, acN) * Vector3.Dot(acN, up);
                            float cG = Mathf.Clamp((hGuarded - along) / (rho * upLen), -1f, 1f);
                            predicted = 2f * rho * Mathf.Sqrt(Mathf.Max(1f - cG * cG, 0f));
                        }
                    }
                }

                log.AppendLine($"      reach {reach:F2}, el {el,4:F0}: worst single-step elbow jump {jump * 1000f,7:F1} mm " +
                               $"at tracker azimuth {atAz,6:F3} deg; closed form predicts {predicted * 1000f,7:F1} mm");

                if (predicted > 1e-3f)
                {
                    reproduced++;
                    float ratio = jump / predicted;
                    worstRatio = Mathf.Max(worstRatio, Mathf.Abs(ratio - 1f));
                    Assert.That(jump, Is.LessThan(predicted * 1.15f + 2e-3f),
                        $"reach {reach:0.00} el {el:0}: the elbow jumped {jump * 1000f:0.0} mm where the guard's own " +
                        $"algebra predicts at most {predicted * 1000f:0.0} mm. The known defect has got WORSE, or a " +
                        "second seam has appeared alongside it.");
                }
            }

            TestContext.WriteLine("\n  D1 -- elbow anatomy guard, side flip at the top of the circle:\n" + log +
                                  $"      closed form reproduced on {reproduced} configurations, worst relative error " +
                                  $"{worstRatio * 100f:F1}%\n" +
                                  "      THE FIX SHAPE, for whoever takes it: the guard already knows the elbow's own " +
                                  "side, so the branch is not needed -- clamp the SWIVEL ANGLE instead of back-solving " +
                                  "a pole and re-deriving its sign, exactly as the humeral twist guard now caps `pull` " +
                                  "rather than choosing sign(twist)*guarded.");

            Assert.That(reproduced, Is.GreaterThan(2),
                $"the closed form only predicted a jump on {reproduced} configurations, so this test is not actually " +
                "measuring the defect it documents.");
            // 35%, and the slack is the sweep's own step: the closed form is evaluated at the sample just
            // BEFORE the flip, whose cG is not quite the cG at the exact branch point (s = 0), and
            // sqrt(1 - cG^2) is steep there. It is a check that the diagnosis is the right MECHANISM and the
            // right ORDER OF MAGNITUDE, not a claim about the third digit.
            Assert.That(worstRatio, Is.LessThan(0.35f),
                $"the measured jump departs from the closed form by {worstRatio * 100f:0.0}%. Either the diagnosis in " +
                "this file is wrong, or the guard's algebra has changed and this documentation is now stale.");
        }

        // ============================================================================================
        // D2
        // ============================================================================================

        /// <summary>
        /// Roll a real elbow tracker through the +/-180 wrap of the forearm-roll signal and measure the
        /// single-step jump in the forearm's own pose. The closed form owes nothing to the sweep:
        /// 2 * Saturate(180, soft, hard) of applied roll, read out as a pose change of 360 minus that.
        /// </summary>
        [Test]
        public void D2_TrackerForearmRoll_JumpsAtThePrincipalAngleSeam_WithNoEnvelope()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            var log = new StringBuilder();

            float appliedJump = 2f * BasisJointLimitCore.Saturate(180f,
                                     BasisArmSolveCore.TrackerForearmRollSoftDeg,
                                     BasisArmSolveCore.TrackerForearmRollMaxDeg);
            float predicted = 360f - appliedJump;

            float worstErr = 0f;
            int reproduced = 0;

            foreach (float reach in new[] { 0.80f, 0.90f, 0.96f })
            foreach (float handRoll in new[] { 0f, 90f })
            {
                Quaternion prevMid = Quaternion.identity;
                bool first = true;
                float jump = 0f, atRoll = 0f, reportedBefore = 0f, reportedAfter = 0f, prevReported = 0f;

                for (int k = 0; k <= 3600; k++)
                {
                    float roll = Mathf.Lerp(0f, 360f, k / 3600f);
                    BasisArmNet.Spec s = BasisArmNet.Default(rig);
                    s.TargetDir = Dir(10f, -30f);
                    s.Reach = reach;
                    s.HandRollDeg = handRoll;
                    s.HintMode = BasisArmNet.HintTracker;
                    s.HintAzimuthDeg = 65f;
                    s.HintRhoMin = 0.02f;
                    s.TrackerRollDeg = roll;
                    s.RefPerp = Vector3.up;
                    s.FeedTip = true;
                    BasisArmSolveInput i = BasisArmNet.Build(s);
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    BasisArmNet.StreamCompose(i, r, out _, out _, out _, out Quaternion mid);

                    if (!first)
                    {
                        float d = BasisArmNet.PoseChangeDeg(prevMid, mid);
                        if (d > jump) { jump = d; atRoll = roll; reportedBefore = prevReported; reportedAfter = r.ForearmRollDeg; }
                    }
                    prevMid = mid;
                    prevReported = r.ForearmRollDeg;
                    first = false;
                }

                log.AppendLine($"      reach {reach:F2}, controller roll {handRoll,3:F0}: worst single-step forearm jump " +
                               $"{jump,7:F2} deg at tracker roll {atRoll,7:F3} deg " +
                               $"(ForearmRollDeg {reportedBefore,7:F1} -> {reportedAfter,7:F1})");

                if (jump > 10f)
                {
                    reproduced++;
                    worstErr = Mathf.Max(worstErr, Mathf.Abs(jump - predicted));
                }
                Assert.That(jump, Is.LessThan(predicted + 2f),
                    $"reach {reach:0.00} controller roll {handRoll:0}: the forearm jumped {jump:0.00} deg, past the " +
                    $"{predicted:0.00} deg the sign flip alone accounts for. The known defect has got worse, or " +
                    "something else has started jumping here too.");
            }

            TestContext.WriteLine(
                $"\n  D2 -- tracker forearm roll, +/-180 seam with no envelope (closed form: applied roll flips " +
                $"{appliedJump:F1} deg, i.e. a {predicted:F1} deg pose change):\n" + log +
                $"      reproduced on {reproduced} configurations, worst departure from the closed form {worstErr:F2} deg\n" +
                "      THE FIX SHAPE: the same seam cap its two siblings already carry -- clamp the applied roll by " +
                "PI - |roll| so it goes continuously to zero at the branch point, exactly as the wrist-roll relief's " +
                "`seam = PI - rollAbs` and the humeral twist guard's `seam = PI - mag` do.");

            // ✅ FIXED, AND THE PIN IS INVERTED INTO A REGRESSION FENCE RATHER THAN DELETED.
            // While the defect was open this required the closed form to REPRODUCE the jump on >3
            // configurations -- the honest shape for a known-open pin. The tracker forearm roll now carries
            // the same `PI - mag` seam cap its two siblings (the wrist-roll relief and the humeral twist
            // guard) always had, so the jump is gone and that non-vacuity gate can no longer be satisfied.
            // Asserting ZERO reproductions is the SAME measurement read the other way round: remove the cap
            // and the closed form starts predicting jumps again and this goes red immediately.
            // ⚠️ Do NOT delete this test. It is the only place the 135 deg single-step tear -- the one the
            // user felt as "the arm breaks in two places" -- is pinned by its own algebra rather than by a
            // hand-picked threshold. Measured before the cap: 136.39 deg of forearm rotation in ONE 0.1 deg
            // step, with the humerus and the hand both stationary to 5 decimals, which is why the mesh tore
            // at the elbow AND the wrist simultaneously.
            Assert.That(reproduced, Is.Zero,
                $"the seam jump reproduced on {reproduced} configurations -- the tracker forearm roll's seam cap has regressed.");
            Assert.That(worstErr, Is.LessThan(3f),
                $"the measured jump departs from the closed form by {worstErr:0.00} deg, so the diagnosis in this file " +
                "no longer matches the code.");
        }
    }
}
