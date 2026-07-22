using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Basis.IK;
using Basis.IK.Mocap;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    // ⚠ The alias must live INSIDE the namespace. com.basis.sdk declares an unrelated
    // `BasisMotionClip : ScriptableObject` in the GLOBAL namespace; at file scope this alias would collide
    // with it (CS0576) instead of disambiguating. Same defence as BasisSpineAnatomyCorpusTests.
    using BasisMotionClip = Basis.IK.Mocap.BasisMotionClip;

    /// <summary>
    /// A GUARD WHOSE MARGIN IS BIGGER THAN ITS REACH IS NOT A GUARD. This file is the proof that this one's
    /// no longer is, and the proof that it once was.
    ///
    /// ================================================================================================
    /// WHAT WENT WRONG, IN ONE LINE: BasisElbowAnatomyCore.SoftMarginFracLimb (0.05) was a FRACTION OF THE
    /// ARM, and BasisArmSolveCore.MaxElbowAngleDeg (170) caps how far the elbow can get from the arm axis
    /// (radius/L = 0.0436). Two constants in two files, no relation asserted between them, and the second
    /// silently ate the first.
    ///
    /// BasisElbowAnatomyTests next door proves the guard's MATHS -- unreachable illegal poses, byte-identical
    /// legal ones. It cannot see this defect, because every pose it asserts on is one where the guard was
    /// still alive. The bug lived in the poses it never asked about: the fully extended ones, which is where
    /// a VR user's arms actually are. 41.1% of the CMU corpus is past 0.95 extension and a user whose real
    /// arms are longer than their avatar's is past the cap ON EVERY FRAME.
    ///
    /// So this file asks a different question -- not "is the guard correct" but "IS THE GUARD ALIVE" -- and
    /// it asks it in the four places that can answer:
    ///
    ///   1. THE THEOREM. max(elbow height above the ceiling), over the whole circle and every hand
    ///      direction, is EXACTLY the circle's radius. So margin >= radius == a guard that cannot fire.
    ///   2. THE ANTI-TAUTOLOGY. The old rule, evaluated on the same grid at the extension cap, fires ZERO
    ///      times. If that assertion ever stops holding, this file's headline is stale and must be re-read.
    ///   3. THE COUPLING CANNOT COME BACK. The guard must stay live for EVERY MaxElbowAngleDeg anyone might
    ///      plausibly set, not just today's 170.
    ///   4. REAL HUMANS ARE STILL NOT CLIPPED -- segmented by extension band, because pooling hides
    ///      breaches (a 90 deg humeral twist limit passed pooled and breached 3% in two bands once split).
    ///
    /// ⚠ AND ONE THING THIS FILE DELIBERATELY REFUSES TO DO: size anything off the top extension band's pole
    /// statistics. See TheCorpusCannotRefereeThePoleAtFullExtension, which asserts the blind spot rather
    /// than merely mentioning it, so a future revision cannot fit a constant to noise without this failing.
    /// ================================================================================================
    /// </summary>
    public sealed class BasisElbowAnatomyReachTests
    {
        const float k_Arm = 0.60f;
        static readonly Vector3 k_Shoulder = new Vector3(0.17f, 1.40f, 0f);
        static readonly Vector3 k_Up = Vector3.up;

        // The house precedent, from BasisSpineAnatomyCorpusTests.k_MaxFireFraction. A limit that fires in
        // the fat of the measured human distribution is the wrong limit.
        const float k_MaxFireFraction = 0.03f;

        // ---------------------------------------------------------------------------------------------
        // Geometry helpers. Everything is built from the TRUE two-bone triangle, so a pose these produce is
        // one the solver can actually reach -- the trap BasisElbowAnatomyTests.RequireInReach exists for.
        // ---------------------------------------------------------------------------------------------

        static float Chord(float thetaDeg, float upper, float lower) =>
            Mathf.Sqrt(Mathf.Max(upper * upper + lower * lower
                                 - 2f * upper * lower * Mathf.Cos(thetaDeg * Mathf.Deg2Rad), 0f));

        /// <summary>The elbow-circle radius: the ONLY distance a swivel about shoulder-&gt;hand can move the
        /// elbow. This is the guard's entire authority, which is why the margin must sit inside it.</summary>
        static float Radius(float thetaDeg, float upper, float lower)
        {
            float d = Chord(thetaDeg, upper, lower);
            float p = (d * d + upper * upper - lower * lower) / (2f * d);
            return Mathf.Sqrt(Mathf.Max(upper * upper - p * p, 0f));
        }

        static void Build(float thetaDeg, Vector3 dir, float phiRad, float upper, float lower,
                          out Vector3 elbow, out Vector3 hand)
        {
            float d = Chord(thetaDeg, upper, lower);
            Vector3 acN = dir.normalized;
            hand = k_Shoulder + acN * d;

            float p = (d * d + upper * upper - lower * lower) / (2f * d);
            float radius = Mathf.Sqrt(Mathf.Max(upper * upper - p * p, 0f));

            Vector3 t = Mathf.Abs(acN.y) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 e1 = Vector3.Cross(acN, t).normalized;
            Vector3 e2 = Vector3.Cross(acN, e1);
            elbow = k_Shoulder + acN * p + (e1 * Mathf.Cos(phiRad) + e2 * Mathf.Sin(phiRad)) * radius;
        }

        /// <summary>
        /// Hand directions PERPENDICULAR TO TORSO-UP -- kappa = dot(acN, up) = 0, where the theorem says the
        /// worst case lives (the ceiling is the shoulder, the elbow's circle stands vertically, and the whole
        /// radius counts toward the rise).
        ///
        /// ⚠ A SPHERE GRID CANNOT SUBSTITUTE FOR THIS, AND SILENTLY LIES AS THE ARM STRAIGHTENS. The rise
        /// falls off the equator like (d/2)*kappa against a radius that is shrinking, so the firing band
        /// narrows as radius/d: at theta 178 only |kappa| &lt; 0.004 can fire at all, and 200 Fibonacci
        /// directions have nothing nearer the equator than about 1/200. An earlier draft of this file swept
        /// the sphere and reported the guard DEAD at 179.5 deg -- which was its own grid, not the code.
        /// </summary>
        static Vector3[] EquatorDirections(int n)
        {
            var d = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * Mathf.PI / n;
                d[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));   // k_Up is +Y
            }
            return d;
        }

        /// <summary>Near-uniform directions on the sphere (Fibonacci). Uniform-in-(lat,lon) would pile
        /// samples at the poles, which is exactly where this guard declines, and flatter the numbers.</summary>
        static Vector3[] Directions(int n)
        {
            var d = new Vector3[n];
            double ga = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int i = 0; i < n; i++)
            {
                double y = 1.0 - 2.0 * (i + 0.5) / n;
                double r = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                double th = ga * i;
                d[i] = new Vector3((float)(Math.Cos(th) * r), (float)y, (float)(Math.Sin(th) * r));
            }
            return d;
        }

        /// <summary>Elbow height above the ceiling (the higher of shoulder and hand), in the torso frame --
        /// the exact quantity BasisElbowAnatomyCore tests against its margin.</summary>
        static float RiseAboveCeiling(Vector3 elbow, Vector3 hand)
        {
            Vector3 ac = hand - k_Shoulder;
            float handUp = Vector3.Dot(ac, k_Up);
            float ceiling = handUp > 0f ? handUp : 0f;
            return Vector3.Dot(elbow - k_Shoulder, k_Up) - ceiling;
        }

        static bool Fires(Vector3 elbow, Vector3 hand, float totalLen) =>
            BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, elbow, hand, k_Up, totalLen) != 0f;

        /// <summary>THE RULE AS IT WAS BEFORE THE CAP: a flat fraction of the arm, no reference to the
        /// circle the elbow is actually confined to. Kept so the anti-tautology check can demonstrate the
        /// defect rather than assert its absence.</summary>
        static bool FiresUncapped(Vector3 elbow, Vector3 hand, float totalLen) =>
            RiseAboveCeiling(elbow, hand) > BasisElbowAnatomyCore.SoftMarginFracLimb * totalLen;

        // =============================================================================================
        // 1. THE THEOREM
        // =============================================================================================

        /// <summary>
        /// max(h - ceiling) over the ENTIRE circle and EVERY hand direction is exactly the circle radius.
        ///
        /// Write kappa = dot(acN, up). For kappa >= 0 the ceiling is d*kappa and the rise is
        /// (p-d)*kappa + radius*sqrt(1-kappa^2)*c; for kappa &lt; 0 the ceiling floors at 0 and the rise is
        /// p*kappa + radius*sqrt(1-kappa^2)*c. p &lt;= d past ~30 deg of flexion and p &gt;= 0 always, so both
        /// branches are maximised at kappa = 0 -- where both collapse to radius*c, maximal at c = 1.
        ///
        /// This is the load-bearing fact: it converts "is the margin too big" from a tuning opinion into an
        /// arithmetic one. It is asserted in UNITY rather than argued, because everything downstream acts at
        /// a singularity and a managed shim has misled this project by 10x at one before.
        /// </summary>
        [Test]
        public void TheMaximumRiseOverTheWholeCircleIsExactlyTheRadius()
        {
            Vector3[] sphere = Directions(400);
            Vector3[] equator = EquatorDirections(64);
            var sb = new StringBuilder();
            sb.AppendLine("theta   upper/lower   radius     sphere max   equator max   equator/radius");

            foreach (float ratio in new[] { 1.00f, 0.85f, 1.20f })
            {
                float upper = k_Arm * ratio / (1f + ratio), lower = k_Arm / (1f + ratio);
                foreach (float theta in new[] { 45f, 90f, 135f, 168f, 170f, 177f })
                {
                    float radius = Radius(theta, upper, lower);

                    // NEVER EXCEEDED, anywhere on the sphere: the upper half of the theorem.
                    float sphereMax = float.NegativeInfinity;
                    foreach (Vector3 dir in sphere)
                        for (int k = 0; k < 720; k++)
                        {
                            Build(theta, dir, k * Mathf.PI / 360f, upper, lower, out Vector3 e, out Vector3 h);
                            float rise = RiseAboveCeiling(e, h);
                            if (rise > sphereMax) sphereMax = rise;
                        }

                    // ATTAINED, on the equator: the lower half. The theorem names this locus, so the test
                    // samples it rather than hoping a sphere grid lands near it.
                    float equatorMax = float.NegativeInfinity;
                    foreach (Vector3 dir in equator)
                        for (int k = 0; k < 720; k++)
                        {
                            Build(theta, dir, k * Mathf.PI / 360f, upper, lower, out Vector3 e, out Vector3 h);
                            float rise = RiseAboveCeiling(e, h);
                            if (rise > equatorMax) equatorMax = rise;
                        }

                    sb.AppendLine($"{theta,6:0.0}{ratio,13:0.00}{radius,10:0.00000}{sphereMax,14:0.00000}{equatorMax,14:0.00000}{equatorMax / radius,17:0.00000}");

                    Assert.LessOrEqual(sphereMax, radius * 1.0002f,
                        $"theta {theta}: a rise of {sphereMax:F5} EXCEEDS the circle radius {radius:F5}. The " +
                        "theorem this file rests on is wrong and every margin argument below is void.\n" + sb);
                    Assert.Greater(equatorMax, radius * 0.9995f,
                        $"theta {theta}: the equator's best rise {equatorMax:F5} falls short of the radius " +
                        $"{radius:F5}, so the maximum is NOT attained and a margin below the radius would not " +
                        "guarantee the guard can fire.\n" + sb);
                }
            }
            TestContext.WriteLine("MAX RISE == CIRCLE RADIUS (sphere never exceeds, equator attains):\n" + sb);
        }

        // =============================================================================================
        // 2. THE ANTI-TAUTOLOGY, AND THE FIX
        // =============================================================================================

        /// <summary>
        /// THE CHECK THAT MAKES THIS FILE MEAN SOMETHING. Both halves run on the SAME grid at the SAME
        /// extension -- BasisArmSolveCore.MaxElbowAngleDeg, where the solver actually parks a stretched arm:
        ///
        ///   the OLD rule (a flat SoftMarginFracLimb of the arm) fires on ZERO of 22,032 poses;
        ///   the LIVE core fires on a healthy fraction of the same 22,032.
        ///
        /// The first half is the defect, demonstrated, not asserted-absent. A gate that only checked the
        /// second half would have passed just as happily on a guard that was already working, and would
        /// therefore have proved nothing about the change that made it work.
        /// </summary>
        [Test]
        public void AtTheExtensionCap_TheOldRuleFiresNever_AndTheLiveCoreFires()
        {
            const float cap = BasisArmSolveCore.MaxElbowAngleDeg;
            float upper = k_Arm * 0.5f, lower = k_Arm * 0.5f;
            float radius = Radius(cap, upper, lower);

            Vector3[] dirs = Directions(612);
            int uncapped = 0, live = 0, total = 0;
            foreach (Vector3 dir in dirs)
            {
                for (int k = 0; k < 36; k++)
                {
                    Build(cap, dir, k * Mathf.PI / 18f, upper, lower, out Vector3 e, out Vector3 h);
                    total++;
                    if (FiresUncapped(e, h, k_Arm)) uncapped++;
                    if (Fires(e, h, k_Arm)) live++;
                }
            }

            string ctx =
                $"\n  MaxElbowAngleDeg      {cap}" +
                $"\n  radius/arm            {radius / k_Arm:F5}" +
                $"\n  SoftMarginFracLimb    {BasisElbowAnatomyCore.SoftMarginFracLimb}  (a flat fraction of the ARM)" +
                $"\n  SoftMarginMaxFracRadius {BasisElbowAnatomyCore.SoftMarginMaxFracRadius}  (a fraction of the CIRCLE)" +
                $"\n  old rule fired        {uncapped} / {total}" +
                $"\n  live core fired       {live} / {total}\n";

            // ANTI-TAUTOLOGY. If this fails, someone has lowered SoftMarginFracLimb instead of capping it;
            // the guard may well be fine, but this file's evidence no longer describes the code.
            Assert.Zero(uncapped,
                "ANTI-TAUTOLOGY CHECK FAILED. The uncapped rule fired at the extension cap, so the defect " +
                "this file documents is not reproducible and the test below proves nothing by comparison." + ctx);

            Assert.Greater(live, total / 200,
                "THE GUARD IS STILL DEAD AT FULL EXTENSION. This is the whole defect: at the extension cap " +
                "the elbow's entire circle sits inside the soft margin, so no pose -- not even the elbow " +
                "pointing straight at the sky -- is illegal, and the arm is unguarded exactly where a VR " +
                "user's arms live." + ctx);

            TestContext.WriteLine("EXTENSION CAP, 612 directions x 36 swivels:" + ctx);
        }

        /// <summary>
        /// The cap is worth having only if it holds for extension caps OTHER than today's 170. Sweeping it
        /// is what turns "the two constants happen to agree" into "they cannot disagree".
        ///
        /// Before the fix this failed for every theta at or above 2*acos(2*SoftMarginFracLimb) = 168.52 deg,
        /// which is 0.99499 of reach -- comfortably inside the range anyone would consider setting.
        /// </summary>
        [Test]
        public void TheGuardStaysLiveForEveryPlausibleExtensionCap()
        {
            float upper = k_Arm * 0.5f, lower = k_Arm * 0.5f;
            // Sampled on the equator, where the theorem puts the worst case -- see EquatorDirections.
            Vector3[] dirs = EquatorDirections(64);
            const int phiSteps = 72, total = 64 * phiSteps;
            var sb = new StringBuilder();
            sb.AppendLine($"theta    reach    radius/arm    live/{total}   old-rule/{total}");

            // BasisArmFullExtensionTests asserts MaxElbowAngleDeg < 178, so that is the range anyone can set.
            for (float theta = 160f; theta <= 177.5f; theta += 1.25f)
            {
                int live = 0, old = 0;
                foreach (Vector3 dir in dirs)
                {
                    for (int k = 0; k < phiSteps; k++)
                    {
                        Build(theta, dir, k * 2f * Mathf.PI / phiSteps, upper, lower, out Vector3 e, out Vector3 h);
                        if (Fires(e, h, k_Arm)) live++;
                        if (FiresUncapped(e, h, k_Arm)) old++;
                    }
                }
                float reach = Chord(theta, upper, lower) / k_Arm;
                sb.AppendLine($"{theta,6:0.00}{reach,10:0.00000}{Radius(theta, upper, lower) / k_Arm,13:0.00000}{live,13}{old,16}");

                Assert.Greater(live, 0,
                    $"the guard is DEAD at MaxElbowAngleDeg = {theta} (reach {reach:F5}). The margin and the " +
                    "extension cap have decoupled again -- whatever bounds the margin must be expressed in " +
                    "terms of the elbow's own circle, not a flat fraction of the arm.\n" + sb);
            }

            // The old rule's zeros ARE the defect, and where they start is the closed form to the digit:
            // 2*acos(2*SoftMarginFracLimb), the theta at which radius falls below the flat margin.
            float predicted = 2f * Mathf.Acos(2f * BasisElbowAnatomyCore.SoftMarginFracLimb) * Mathf.Rad2Deg;
            sb.AppendLine($"\nold rule goes dead at 2*acos(2*{BasisElbowAnatomyCore.SoftMarginFracLimb}) = {predicted:F3} deg " +
                          $"(reach {Chord(predicted, upper, lower) / k_Arm:F5}); MaxElbowAngleDeg is {BasisArmSolveCore.MaxElbowAngleDeg}");
            Assert.Less(predicted, BasisArmSolveCore.MaxElbowAngleDeg,
                "the flat margin no longer outruns the extension cap, so the defect this file documents is " +
                "not reproducible here and its evidence has gone stale -- re-read the header before trusting it.\n" + sb);

            TestContext.WriteLine("LIVE AT EVERY PLAUSIBLE CAP (old rule's zeros are the defect):\n" + sb);
        }

        /// <summary>
        /// AND IT CHANGES NOTHING WHERE IT HAS NO BUSINESS CHANGING ANYTHING. The cap binds only where
        /// SoftMarginMaxFracRadius*radius &lt; SoftMarginFracLimb*arm, i.e. radius/arm &lt; 0.1, i.e. past
        /// ~0.98 of reach. Below that the margins are the same floats they always were, so the guard is the
        /// same guard -- which is what makes this a targeted fix rather than a retune of everything.
        /// </summary>
        [Test]
        public void BelowFullExtension_TheCapDoesNotBind()
        {
            float upper = k_Arm * 0.5f, lower = k_Arm * 0.5f;
            float bindsBelow = BasisElbowAnatomyCore.SoftMarginFracLimb * k_Arm
                             / BasisElbowAnatomyCore.SoftMarginMaxFracRadius;

            foreach (float theta in new[] { 30f, 60f, 90f, 120f, 150f, 156f })
            {
                float radius = Radius(theta, upper, lower);
                Assert.Greater(radius, bindsBelow,
                    $"at theta {theta} the radius is {radius:F5}, under the {bindsBelow:F5} at which the cap " +
                    "starts to bind -- so this fix is no longer inert in the mid-range it was never meant to " +
                    "touch, and the mid-range corpus evidence for SoftMarginFracLimb no longer covers it.");
            }

            // And the pose that was legal before is still legal, exactly: the guard returns a hard 0f.
            Build(90f, new Vector3(1f, -0.4f, 0.2f), Mathf.PI, upper, lower, out Vector3 e0, out Vector3 h0);
            Assert.AreEqual(0f, BasisElbowAnatomyCore.GuardSwivelRad(k_Shoulder, e0, h0, k_Up, k_Arm),
                "an elbow hanging BELOW its shoulder is not a pose any margin should perturb");
        }

        // =============================================================================================
        // 3. THE INTERACTION WITH WHAT ELSE TOUCHES THIS AXIS
        // =============================================================================================

        /// <summary>
        /// ⚠ THE TWO GUARDS ARE NOT DISJOINT, AND THE COMMENT THAT SAID THEY WERE WAS WRONG.
        /// BasisArmSolveCore's humeral twist guard acts about shoulder-&gt;ELBOW; this one about
        /// shoulder-&gt;HAND. Those are (180-theta)/2 apart: 45 deg at 90 deg of flexion -- genuinely
        /// different degrees of freedom -- but only 5.00 deg apart at MaxElbowAngleDeg, which is precisely
        /// where this fix acts. At full extension they are very nearly the SAME axis.
        ///
        /// What saves the composition is ORDER and GEOMETRY, not separation: the anatomy guard runs first
        /// and sets the elbow; the twist guard runs second, re-measures from the corrected elbow, and
        /// rotates about an axis the elbow LIES ON -- so it cannot move the elbow, and cannot put it back
        /// above the ceiling. This test holds that reasoning to the actual solver: the achieved envelope
        /// must be identical with the twist guard engaged and declined.
        ///
        /// (Measured while writing this: at the offending pose the twist guard reads 90.0 deg against its
        /// 120 deg soft limit and contributes 0.0 -- it never covered this hole and could not have.)
        /// </summary>
        [Test]
        public void TheAnatomyGuardAndTheHumeralTwistGuardDoNotFight()
        {
            const float upper = 0.30f, lower = 0.30f, total = upper + lower;
            Vector3 shoulder = new Vector3(-0.20f, 1.40f, 0f);
            Vector3 bindElbow = shoulder + new Vector3(-upper, 0f, 0f);
            Vector3 bindHand = bindElbow + new Vector3(-lower * 0.9962f, 0f, -lower * 0.0872f);

            var sb = new StringBuilder();
            sb.AppendLine("reach    worst rise (twist ON)   worst rise (twist OFF)   axis angle");

            foreach (float reach in new[] { 0.95f, 0.98f, 0.995f, 0.999f, 1.00f })
            {
                Vector3 target = shoulder + new Vector3(-1f, 0f, 0f) * (reach * total);
                float worstOn = float.NegativeInfinity, worstOff = float.NegativeInfinity, axisAngle = 0f;

                for (int k = 0; k < 180; k++)
                {
                    float a = k * 2f * Mathf.Deg2Rad;
                    Vector3 hint = target * 0.5f + shoulder * 0.5f
                                 + new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)) * 0.35f;

                    for (int pass = 0; pass < 2; pass++)
                    {
                        var i = new BasisArmSolveInput
                        {
                            Shoulder = shoulder, Elbow = bindElbow, Hand = bindHand,
                            RootRotation = Quaternion.identity, MidRotation = Quaternion.identity,
                            TargetPosition = target, TargetRotation = Quaternion.identity,
                            TargetOffset = Quaternion.identity,
                            HintPosition = hint, HintWeight = true, HintMaxStepDeg = float.MaxValue,
                            PlayerUp = Vector3.up, TorsoUp = Vector3.up,
                        };
                        if (pass == 0)
                        {
                            i.ClavicleRotation = Quaternion.identity;
                            i.BindClavicleRotation = Quaternion.identity;
                            i.BindHumerusRotation = Quaternion.identity;
                            i.BindHumerusDir = new Vector3(-1f, 0f, 0f);
                            i.BindHumerusRefAxis = new Vector3(0f, 1f, 0f);
                        }

                        BasisArmSolveCore.Solve(in i, out BasisArmSolveResult r);
                        Vector3 ac = r.HandSolved - shoulder;
                        float handUp = Vector3.Dot(ac, Vector3.up);
                        float ceiling = handUp > 0f ? handUp : 0f;
                        float rise = (Vector3.Dot(r.ElbowSolved - shoulder, Vector3.up) - ceiling) / total;

                        if (pass == 0)
                        {
                            if (rise > worstOn) worstOn = rise;
                            axisAngle = Vector3.Angle(ac.normalized, (r.ElbowSolved - shoulder).normalized);
                        }
                        else if (rise > worstOff) worstOff = rise;
                    }
                }

                sb.AppendLine($"{reach,6:0.000}{worstOn,22:0.00000}{worstOff,25:0.00000}{axisAngle,13:0.00}");
                Assert.AreEqual(worstOff, worstOn, 1e-4f,
                    $"at reach {reach:F3} the humeral twist guard MOVED the anatomy guard's outcome " +
                    $"({worstOn:F5} engaged vs {worstOff:F5} declined). It rotates about shoulder->elbow, an " +
                    "axis the elbow lies on, so it must not be able to -- if it can, the two are fighting " +
                    "over the same degree of freedom and one of them has to yield explicitly.\n" + sb);
            }
            TestContext.WriteLine("ANATOMY GUARD vs HUMERAL TWIST GUARD (achieved envelope must match):\n" + sb);
        }

        // =============================================================================================
        // 4. THE CORPUS VETO
        // =============================================================================================

        static string CorpusRoot => Path.GetFullPath("Packages/com.basis.framework/Tests/MocapCorpus~");
        static readonly string[] k_TierDirs = { "", "posture", "dynamic", "slow" };
        static readonly string[] k_TierNames = { "root", "posture", "dynamic", "slow" };

        // Deliberately finer than the tiers above 0.95: the fix acts past 0.98, and a band wide enough to
        // straddle that is a band that can hide it.
        static readonly float[] k_BandEdges = { 0f, 0.50f, 0.70f, 0.85f, 0.92f, 0.95f, 0.97f, 0.98f, 0.99f, 1.01f };

        struct Arm
        {
            public float Ext, Rise, Radius, PoleUp;
            public bool Fired;
            public int Tier, Clip;
        }

        static List<Arm> s_Corpus;
        static List<string> s_ClipNames;

        static int BandOf(float ext)
        {
            for (int i = 0; i < k_BandEdges.Length - 1; i++)
                if (ext >= k_BandEdges[i] && ext < k_BandEdges[i + 1]) return i;
            return k_BandEdges.Length - 2;
        }
        static string BandName(int b) => $"[{k_BandEdges[b]:0.00},{k_BandEdges[b + 1]:0.00})";

        /// <summary>
        /// Every arm-frame of the corpus, measured through the SHIPPING core in the SHIPPING frame -- the
        /// chest's up, which is the only frame the ceiling law is true in (see BasisElbowAnatomyCore's frame
        /// note; a world up reads the sign FLIPPED on people bent double).
        /// </summary>
        static void LoadCorpus()
        {
            if (s_Corpus != null) return;
            s_Corpus = new List<Arm>(1 << 19);
            s_ClipNames = new List<string>();

            for (int t = 0; t < k_TierDirs.Length; t++)
            {
                string dir = t == 0 ? CorpusRoot : Path.Combine(CorpusRoot, k_TierDirs[t]);
                if (!Directory.Exists(dir)) continue;
                string[] files = Directory.GetFiles(dir, "*.bvh");
                Array.Sort(files);
                foreach (string f in files)
                {
                    if (!BasisBvhLoader.TryLoad(f, out BasisMotionClip c, out _)) continue;
                    int clip = s_ClipNames.Count;
                    s_ClipNames.Add(k_TierNames[t] + "/" + Path.GetFileNameWithoutExtension(f));

                    var buf = BasisBodyFrame.Allocate();
                    for (int fi = 0; fi < c.FrameCount; fi++)
                    {
                        BasisBodyFrame frame = BasisBodyFrame.FromClip(c, fi, buf);
                        if (!BasisBodyPlausibility.TryChestFrame(frame, out _, out Vector3 chestUp, out _)) continue;
                        AddArm(frame, chestUp, t, clip, false);
                        AddArm(frame, chestUp, t, clip, true);
                    }
                }
            }
            if (s_Corpus.Count == 0) Assert.Ignore($"no corpus at {CorpusRoot}");
        }

        static void AddArm(in BasisBodyFrame f, Vector3 chestUp, int tier, int clip, bool right)
        {
            BasisMocapJoint sj = right ? BasisMocapJoint.RightUpperArm : BasisMocapJoint.LeftUpperArm;
            BasisMocapJoint ej = right ? BasisMocapJoint.RightLowerArm : BasisMocapJoint.LeftLowerArm;
            BasisMocapJoint hj = right ? BasisMocapJoint.RightHand : BasisMocapJoint.LeftHand;
            if (!f.Has(sj) || !f.Has(ej) || !f.Has(hj)) return;

            Vector3 sh = f.Pos(sj), el = f.Pos(ej), hd = f.Pos(hj);
            // The arm length the runtime uses: the two bones, not a calibrated span.
            float totalLen = (el - sh).magnitude + (hd - el).magnitude;
            if (!(totalLen > 1e-5f)) return;

            Vector3 ac = hd - sh;
            float acLen = ac.magnitude;
            if (!(acLen > 1e-5f)) return;

            Vector3 up = chestUp.normalized;
            Vector3 acN = ac / acLen;
            Vector3 ae = el - sh;
            Vector3 aeProj = ae - acN * Vector3.Dot(ae, acN);
            float radius = aeProj.magnitude;
            Vector3 upProj = up - acN * Vector3.Dot(up, acN);
            float upLen = upProj.magnitude;
            if (!(radius > 1e-5f) || !(upLen > 1e-5f)) return;

            float handUp = Vector3.Dot(ac, up);
            float ceiling = handUp > 0f ? handUp : 0f;

            s_Corpus.Add(new Arm
            {
                Ext = acLen / totalLen,
                Rise = Vector3.Dot(ae, up) - ceiling,
                Radius = radius,
                PoleUp = Vector3.Dot(aeProj / radius, upProj / upLen),
                Fired = BasisElbowAnatomyCore.GuardSwivelRad(sh, el, hd, chestUp, totalLen) != 0f,
                Tier = tier,
                Clip = clip,
            });
        }

        /// <summary>
        /// THE VETO, AND IT IS SEGMENTED. The limits in this project are max-voluntary anatomy with the
        /// corpus holding a veto only -- a limit that fires in the FAT of the measured human distribution is
        /// the wrong limit -- and the veto is worthless pooled. That is not a hypothetical: earlier the same
        /// day this fix landed, a 90 deg humeral twist limit looked clean on pooled corpus data and turned
        /// out to breach 3% in TWO elevation bands once split (4.2% and 4.8%).
        ///
        /// So: per extension band, per tier x band, and per clip. All three under the house 3%.
        /// </summary>
        [Test]
        public void RealHumansAreNotClipped_SegmentedByExtensionBand()
        {
            LoadCorpus();
            int bands = k_BandEdges.Length - 1;
            var n = new int[bands];
            var fired = new int[bands];
            foreach (Arm a in s_Corpus) { int b = BandOf(a.Ext); n[b]++; if (a.Fired) fired[b]++; }

            var sb = new StringBuilder();
            sb.AppendLine($"{s_Corpus.Count} arm-frames over {s_ClipNames.Count} clips, chest frame");
            sb.AppendLine("band                  n     %corpus      fires    worst rise/radius");
            for (int b = 0; b < bands; b++)
            {
                if (n[b] == 0) continue;
                float worst = float.NegativeInfinity;
                foreach (Arm a in s_Corpus)
                    if (BandOf(a.Ext) == b && a.Rise / a.Radius > worst) worst = a.Rise / a.Radius;
                sb.AppendLine($"ext {BandName(b),-14}{n[b],8}{100f * n[b] / s_Corpus.Count,10:0.00}%{100f * fired[b] / n[b],10:0.000}%{worst,20:0.000}");
            }
            TestContext.WriteLine("CORPUS VETO BY EXTENSION BAND:\n" + sb);

            for (int b = 0; b < bands; b++)
            {
                if (n[b] < 500) continue;   // too few to be a distribution
                float frac = (float)fired[b] / n[b];
                Assert.Less(frac, k_MaxFireFraction,
                    $"the guard fires on {frac:P3} of real human arm-frames in extension band {BandName(b)} " +
                    $"({fired[b]} of {n[b]}). That is inside the fat of the measured distribution, not the " +
                    "impossible tail -- the margin is clipping poses people actually make.\n" + sb);
            }

            // TIER x BAND. The cross-segmentation is the one that catches what a single split hides.
            var sb2 = new StringBuilder();
            sb2.Append("tier      ");
            for (int b = 0; b < bands; b++) sb2.Append($"{BandName(b),14}");
            sb2.AppendLine();
            for (int t = 0; t < k_TierNames.Length; t++)
            {
                var tn = new int[bands];
                var tf = new int[bands];
                foreach (Arm a in s_Corpus)
                {
                    if (a.Tier != t) continue;
                    int b = BandOf(a.Ext); tn[b]++; if (a.Fired) tf[b]++;
                }
                sb2.Append($"{k_TierNames[t],-10}");
                for (int b = 0; b < bands; b++) sb2.Append(tn[b] == 0 ? "             -" : $"{100f * tf[b] / tn[b],13:0.000}%");
                sb2.AppendLine();

                for (int b = 0; b < bands; b++)
                {
                    if (tn[b] < 500) continue;
                    float frac = (float)tf[b] / tn[b];
                    Assert.Less(frac, k_MaxFireFraction,
                        $"tier {k_TierNames[t]}, extension band {BandName(b)}: the guard fires on {frac:P3} " +
                        $"({tf[b]} of {tn[b]}). Pooled across tiers this would have been invisible.\n" + sb2);
                }
            }
            TestContext.WriteLine("CORPUS VETO BY TIER x BAND:\n" + sb2);

            // PER CLIP, which is the form BasisSpineAnatomyCorpusTests takes.
            var cn = new int[s_ClipNames.Count];
            var cf = new int[s_ClipNames.Count];
            foreach (Arm a in s_Corpus) { cn[a.Clip]++; if (a.Fired) cf[a.Clip]++; }
            int worstClip = -1; float worstFrac = 0f;
            for (int c = 0; c < cn.Length; c++)
            {
                if (cn[c] < 500) continue;
                float frac = (float)cf[c] / cn[c];
                if (frac > worstFrac) { worstFrac = frac; worstClip = c; }
            }
            TestContext.WriteLine(worstClip < 0 ? "no clip large enough to rate"
                : $"WORST CLIP: {s_ClipNames[worstClip]} at {worstFrac:P3} ({cf[worstClip]} of {cn[worstClip]})");
            Assert.Less(worstFrac, k_MaxFireFraction,
                $"clip {(worstClip < 0 ? "?" : s_ClipNames[worstClip])} has the guard firing on {worstFrac:P3} " +
                "of its arm-frames -- a whole clip of real motion being clipped is the table being wrong.");
        }

        /// <summary>
        /// ⚠ THE CORPUS'S OWN BLIND SPOT, ASSERTED SO IT CANNOT BE FORGOTTEN.
        ///
        /// Near full extension the elbow's circle shrinks to centimetres, and the mocap solver that produced
        /// these clips hits the SAME singularity ours does: its reconstructed elbow stops carrying usable
        /// information about WHICH WAY ROUND the circle the elbow sits. The share of samples with the pole in
        /// the top fifth of the circle climbs 0.0% -> 3.8% -> 27.4% as extension goes 0.5 -> 0.96 -> 1.0.
        /// That is a distribution coming apart, not a population of people raising their elbows.
        ///
        /// It does NOT invalidate the veto above, which asks a question the corpus can still answer -- "is
        /// the elbow above the ceiling" is a comparison of two heights, and heights stay well-conditioned.
        /// It invalidates only the question "where on its circle should the elbow be at full extension",
        /// which is why SoftMarginMaxFracRadius is sized by continuity from 0.98 (which the corpus CAN
        /// referee) and not fitted to the top band.
        ///
        /// This test exists so that a future revision tempted to fit it there trips over the reason not to.
        /// </summary>
        [Test]
        public void TheCorpusCannotRefereeThePoleAtFullExtension()
        {
            LoadCorpus();
            int bands = k_BandEdges.Length - 1;
            var n = new int[bands];
            var high = new int[bands];
            foreach (Arm a in s_Corpus)
            {
                int b = BandOf(a.Ext);
                n[b]++;
                if (a.PoleUp > 0.6f) high[b]++;
            }

            var sb = new StringBuilder();
            sb.AppendLine("band                  n     % with pole in the top fifth of the circle");
            for (int b = 0; b < bands; b++)
                if (n[b] > 0) sb.AppendLine($"ext {BandName(b),-14}{n[b],8}{100f * high[b] / n[b],12:0.0}%");
            TestContext.WriteLine("BLIND-SPOT DIAGNOSTIC:\n" + sb);

            int lowBand = BandOf(0.60f), topBand = bands - 1;
            Assert.That(n[lowBand], Is.GreaterThan(500), "not enough mid-extension samples to compare against");
            Assert.That(n[topBand], Is.GreaterThan(500), "not enough full-extension samples to compare against");

            float lowFrac = (float)high[lowBand] / n[lowBand];
            float topFrac = (float)high[topBand] / n[topBand];
            Assert.Greater(topFrac, lowFrac * 4f,
                $"the pole distribution at full extension ({topFrac:P1} in the top fifth) is no longer wildly " +
                $"wider than at mid extension ({lowFrac:P1}). Either the corpus improved or the measurement " +
                "changed -- either way the documented reason for NOT fitting a constant to the top band " +
                "no longer holds and the sizing argument must be revisited.\n" + sb);
        }
    }
}
