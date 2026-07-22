using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// ⭐ THE NO-TRACKER FOREARM ROLL SNAPPED 160 DEGREES, AND IT BROKE THE ARM IN TWO PLACES AT ONCE.
    ///
    /// ================================================================================================
    /// THE DEFECT. BasisArmSolveCore's no-tracker forearm roll ends with
    ///
    ///     demand = TwistAngleRad(tRotation * Inverse(neutralFore), foreRollN);
    ///     want   = demand > band ? band : (demand < -band ? -band : demand);
    ///
    /// `demand` is a PRINCIPAL angle -- it lives on a CIRCLE and wraps at +/-180 -- and that hard min/max
    /// projects the circle onto the arc [-80, +80]. So demand = +179.99 becomes +80 and demand = -179.99
    /// becomes -80: two HAND POSES 0.02 DEGREES APART are mapped to two FOREARMS 160 DEGREES APART.
    ///
    /// MEASURED on the live core before the fix -- straight arm (170 deg), controller roll swept in
    /// 0.02 deg steps, at roll -167.49:
    ///
    ///     forearm roll     +55.00  ->  -105.00      160.05 deg, ONE STEP
    ///     elbow junction   +80.00  ->   -80.00
    ///     wrist junction   +99.98  ->   -99.98
    ///     humerus            12.51  ->    12.49     continuous
    ///     elbow / hand position           UNCHANGED to 5 decimals
    ///
    /// ⚠️ WHY THE USER SAW TWO BREAKS. The snap is a PURE ROLL about the forearm's own long axis, so it
    /// moves no joint -- the humerus holds still and the hand holds still, and only the bone BETWEEN them
    /// turns. A limb segment that rotates 160 degrees while both its neighbours stay put tears the mesh at
    /// BOTH of its ends: the ELBOW and the WRIST. "straight arm rotating the arm able to get it to break in
    /// two places" is the exact signature of this and of nothing else in the solve.
    ///
    /// AND A FULL TURN OF THE CONTROLLER CANNOT MISS IT. `demand` is measured against a BIND reference that
    /// does not follow the hand, so it sweeps a full 360 as the user does; crossing +/-180 exactly once per
    /// turn is forced by topology, not by luck.
    /// ================================================================================================
    ///
    /// ⚠️ WHY THE EXISTING SEAM TEST COULD NOT SEE IT, WHICH IS THE SAME GAP THIS REPO KEEPS FINDING.
    /// BasisArmForearmRollAndSeamTests.TheTwoSidesOfTheSeamStillAgree sweeps the very same seam -- and
    /// measures `RootRotationSolved`, THE HUMERUS. The humerus is the one bone in the arm that does NOT
    /// move across this seam (12.51 -> 12.49 deg). The test passes, correctly, about a bone that was never
    /// involved. That is why every measurement in this file is taken from the FOREARM, composed through the
    /// runtime's own AnimationStream order, and why the replay is asserted faithful before anything is
    /// measured with it.
    /// </summary>
    public class BasisArmForearmSeamSnapTests
    {
        const float UpperLen = 0.30f, ForeLen = 0.27f;
        const float ArmLen = UpperLen + ForeLen;
        const float Band = BasisArmSolveCore.WristRollComfortDeg;          // 80
        const float Crossing = 0.5f * (180f + Band);                       // 130 -- where the seam cap starts to bind
        static readonly Vector3 Shoulder = Vector3.zero;

        /// <summary>Arm directions swept. A straight arm is the reported pose and the roll singularity, so
        /// every row is at 0.999 of full reach, where the solver's 170 deg clamp owns the elbow.</summary>
        static readonly (string name, Vector3 dir)[] Dirs =
        {
            ("lateral",  new Vector3(1f, 0f, 0f)),
            ("forward",  new Vector3(0f, 0f, 1f)),
            ("fwd-lat",  new Vector3(1f, 0f, 1f)),
            ("overhead", new Vector3(0.25f, 1f, 0f)),
            ("down-fwd", new Vector3(0.3f, -1f, 0.5f)),
        };

        struct Rig
        {
            public Vector3 BindDir;
            public Quaternion BindHumerus, BindLowerArm, BindHand, Clavicle;
            public Vector3 RefAxis;
        }

        /// <summary>Mirrors BasisFullBodyIK.BakeHumerusTwistBind for the reference axis, exactly as
        /// BasisHumeralTwistGuardTests and BasisArmHumeralTwistSeamTests do.</summary>
        static Rig MakeRig()
        {
            Rig g = default;
            g.BindDir = Vector3.right;
            g.BindHumerus = Quaternion.identity;
            g.BindLowerArm = Quaternion.identity;
            g.BindHand = Quaternion.identity;
            g.Clavicle = Quaternion.Euler(4f, -11f, 6f);
            Vector3 localBone = Quaternion.Inverse(g.BindHumerus) * g.BindDir;
            Vector3 refLocal = Mathf.Abs(localBone.y) < 0.9f ? Vector3.up : Vector3.forward;
            Vector3 perp = refLocal - localBone * Vector3.Dot(refLocal, localBone);
            g.RefAxis = perp.normalized;
            return g;
        }

        struct Pose
        {
            public BasisArmSolveInput In;
            public Vector3 Elbow0, Hand0;
            public Quaternion AnimRoot, AnimMid, AnimTip;
        }

        static Vector3 PerpTo(Vector3 v, Vector3 axis)
        {
            Vector3 p = v - axis * Vector3.Dot(v, axis);
            if (p.sqrMagnitude < 1e-8f)
            {
                p = Vector3.Cross(axis, Vector3.forward);
                if (p.sqrMagnitude < 1e-8f) p = Vector3.Cross(axis, Vector3.right);
            }
            return p.normalized;
        }

        /// <summary>A STRAIGHT arm along `armDir`, controller rolled `rollDeg` about the arm axis away from
        /// the rig's own anatomical neutral. `bakeBind` false leaves the two bind rotations at their struct
        /// default, which declines the forearm-roll stage entirely -- the control.
        ///
        /// ⚠️ ANTI-TAUTOLOGY: the target rotation is built from the BIND bone axis and the shoulder->target
        /// ray. No animated bone appears in it, so the sweep cannot be secretly following the animation.
        /// The animated forearm carries a deliberate 25 deg of arbitrary clip twist for the same reason.</summary>
        static Pose Build(in Rig g, Vector3 armDir, float rollDeg, bool tracker, bool bakeBind = true)
        {
            armDir = armDir.normalized;
            Quaternion swing = BasisQuaternionExt.FromToRotation(g.BindDir, armDir);
            Vector3 bendAxis = Vector3.Cross(armDir, PerpTo(Vector3.up, armDir)).normalized;
            Quaternion bendQ = Quaternion.AngleAxis(12f, bendAxis);     // a relaxed 168 deg animated elbow

            Vector3 elbow0 = Shoulder + armDir * UpperLen;
            Vector3 foreDir0 = bendQ * armDir;
            Vector3 hand0 = elbow0 + foreDir0 * ForeLen;

            Quaternion animRoot = swing * g.BindHumerus;
            Quaternion animMid = Quaternion.AngleAxis(25f, foreDir0) * bendQ * swing * g.BindLowerArm;
            Quaternion animTip = animMid * (Quaternion.Inverse(g.BindLowerArm) * g.BindHand);

            Vector3 targetPos = Shoulder + armDir * (ArmLen * 0.999f);
            Quaternion targetNeutral = BasisQuaternionExt.FromToRotation(g.BindDir, armDir) * g.BindHand;
            Vector3 bendWorld = PerpTo(Vector3.down, armDir);

            Pose p = default;
            p.Elbow0 = elbow0; p.Hand0 = hand0;
            p.AnimRoot = animRoot; p.AnimMid = animMid; p.AnimTip = animTip;

            BasisArmSolveInput i = default;
            i.Shoulder = Shoulder; i.Elbow = elbow0; i.Hand = hand0;
            i.RootRotation = animRoot; i.MidRotation = animMid;
            i.TargetPosition = targetPos;
            i.TargetRotation = Quaternion.AngleAxis(rollDeg, armDir) * targetNeutral;
            i.TargetOffset = Quaternion.identity;
            i.PlayerUp = Vector3.up; i.TorsoUp = Vector3.up;
            i.HintWeight = true; i.HintIsTracker = tracker;
            i.HintMaxStepDeg = float.MaxValue;
            i.TipRotation = animTip;
            i.ClavicleRotation = g.Clavicle;
            i.BindClavicleRotation = g.Clavicle;
            i.BindHumerusRotation = bakeBind ? g.BindHumerus : default;
            i.BindHumerusDir = g.BindDir;
            i.BindHumerusRefAxis = g.RefAxis;
            i.BindLowerArmRotation = bakeBind ? g.BindLowerArm : default;

            if (tracker)
            {
                Vector3 pole = Quaternion.AngleAxis(rollDeg, armDir) * bendWorld;
                const float theta = 170f * Mathf.Deg2Rad;
                float dEff = Mathf.Sqrt(UpperLen * UpperLen + ForeLen * ForeLen
                                        - 2f * UpperLen * ForeLen * Mathf.Cos(theta));
                float alpha = Mathf.Acos(Mathf.Clamp(
                    (UpperLen * UpperLen + dEff * dEff - ForeLen * ForeLen) / (2f * UpperLen * dEff), -1f, 1f));
                Vector3 elbowT = Shoulder + UpperLen * (Mathf.Cos(alpha) * armDir + Mathf.Sin(alpha) * pole);
                Vector3 foreDirT = (Shoulder + armDir * dEff - elbowT).normalized;
                i.HintPosition = elbowT;
                i.HintRotation = Quaternion.AngleAxis(rollDeg, foreDirT)
                                 * BasisQuaternionExt.FromToRotation(g.BindDir, foreDirT) * g.BindLowerArm;
            }
            else
            {
                // BasisSwivelHintCore.ArmHint's shape: a UNIT bend perpendicular to shoulder->hand.
                i.HintPosition = Shoulder + 0.5f * ArmLen * bendWorld;
            }
            p.In = i;
            return p;
        }

        struct J
        {
            public float S, E, W;                  // shoulder / elbow / wrist junction axial twist, deg
            public float Demand;                   // the hand's roll demand, the stage's own input
            public Vector3 ElbowPos, HandPos;
            public Quaternion HumerusW, ForearmW, HandW;
        }

        /// <summary>
        /// ⭐ THE RUNTIME'S OWN COMPOSITION, REPLAYED. BasisFullBodyIK.SolveTwoBoneIKArms ends with
        ///
        ///   mid.SetRotation (MidDelta * mid);   root.SetRotation(RootDelta * root);
        ///   root.SetRotation(HintDelta * root); mid.SetRotation(MidPostRoll * mid); tip.SetRotation(...)
        ///
        /// and setting a PARENT's rotation in an AnimationStream carries its children RIGIDLY -- the child's
        /// world rotation is left-multiplied and its position pivots about the parent's origin. Junction
        /// twists are then read from those composed world rotations, never from the result struct's own
        /// bookkeeping, because the bookkeeping is exactly what hid the humeral guard's 205 mm hand throw.
        /// </summary>
        static J Compose(in Rig g, in Pose p, in BasisArmSolveResult r)
        {
            Vector3 a = Shoulder, bp = p.Elbow0, cp = p.Hand0;
            cp = bp + r.MidDelta * (cp - bp);
            bp = a + r.RootDelta * (bp - a);
            cp = a + r.RootDelta * (cp - a);
            bp = a + r.HintDelta * (bp - a);
            cp = a + r.HintDelta * (cp - a);
            cp = bp + r.MidPostRoll * (cp - bp);

            J j = default;
            j.ElbowPos = bp;
            j.HandPos = cp;
            j.HumerusW = r.HintDelta * r.RootDelta * p.AnimRoot;
            j.ForearmW = r.MidPostRoll * r.HintDelta * r.RootDelta * r.MidDelta * p.AnimMid;
            j.HandW = r.TipRotation;

            Vector3 humAxis = (bp - a).normalized;
            Vector3 foreAxis = (cp - bp).normalized;

            Quaternion carry = p.In.ClavicleRotation * Quaternion.Inverse(p.In.BindClavicleRotation);
            Quaternion swingMin = BasisQuaternionExt.FromToRotation((carry * g.BindDir).normalized, humAxis);
            Vector3 refPerp = (swingMin * (carry * g.BindHumerus)) * g.RefAxis;
            Vector3 livePerp = j.HumerusW * g.RefAxis;
            refPerp -= humAxis * Vector3.Dot(refPerp, humAxis);
            livePerp -= humAxis * Vector3.Dot(livePerp, humAxis);
            j.S = SignedAngleDeg(refPerp, livePerp, humAxis);

            Quaternion neutralFore = j.HumerusW * (Quaternion.Inverse(g.BindHumerus) * g.BindLowerArm);
            j.E = TwistDeg(j.ForearmW * Quaternion.Inverse(neutralFore), foreAxis);
            Quaternion neutralHand = j.ForearmW * (Quaternion.Inverse(g.BindLowerArm) * g.BindHand);
            j.W = TwistDeg(j.HandW * Quaternion.Inverse(neutralHand), foreAxis);

            // The stage's OWN input, rebuilt from composed stream state rather than read from a result
            // field: the same three quantities the core uses (the final humerus, the hand, the forearm's
            // long axis), all of which the replay gate above has already proved faithful.
            //
            // ⚠️ NOT `E + W`. Twists about a common axis only add when the intervening rotations are pure
            // twists, and the forearm sits 10 deg off the humerus at a 170 deg elbow -- measured, that
            // approximation carries 0.46 deg of error, which is enough to blur the identity gate below.
            // Same cause as the roll-envelope yardstick: demand is the RAW controller. j.HandW correctly
            // reads the BOUNDED hand and is left alone; only the demand reference moves.
            j.Demand = TwistDeg((p.In.TargetRotation * p.In.TargetOffset) * Quaternion.Inverse(neutralFore), foreAxis);
            return j;
        }

        static J At(in Rig g, Vector3 dir, float roll, bool tracker, bool bakeBind = true)
        {
            Pose p = Build(g, dir, roll, tracker, bakeBind);
            BasisArmSolveCore.Solve(p.In, out BasisArmSolveResult r);
            return Compose(g, p, r);
        }

        static float SignedAngleDeg(Vector3 from, Vector3 to, Vector3 axis)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (!(denom > 1e-5f)) return 0f;
            float c = Vector3.Dot(from, to) / denom;
            c = c > 1f ? 1f : (c > -1f ? c : -1f);
            float ang = Mathf.Acos(c) * Mathf.Rad2Deg;
            return Vector3.Dot(axis, Vector3.Cross(from, to)) < 0f ? -ang : ang;
        }

        static float TwistDeg(Quaternion q, Vector3 axis)
        {
            float s = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float c = q.w;
            if (c < 0f) { s = -s; c = -c; }
            if (!(s * s + c * c > 1e-8f)) return 0f;
            return 2f * Mathf.Atan2(s, c) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// ⚠️ NOT `2 * acos(|dot|)`, AND THE DIFFERENCE IS THE WHOLE VALIDITY OF THE IDENTITY GATES.
        ///
        /// acos loses half the float mantissa near dot = 1: an error of eps in the dot product becomes
        /// sqrt(2*eps) in the angle, so at float32 precision two BIT-IDENTICAL rotations measure up to
        /// ~0.09 deg apart. Both identity gates in this file assert well below that, so with the acos form
        /// they failed against correct code -- and, far worse, they would have been unable to detect any
        /// real regression smaller than 0.09 deg while appearing to gate at 0.001.
        ///
        /// This is the stable form: build the relative rotation (conjugate of a, times b -- a is unit) and
        /// read its angle with atan2 of the vector part against the scalar part, which is exact for small
        /// angles. Measured on identical rotations it returns 0.000000.
        /// </summary>
        static float PoseDeg(Quaternion a, Quaternion b)
        {
            Quaternion r = new Quaternion(
                a.w * b.x - a.x * b.w - a.y * b.z + a.z * b.y,
                a.w * b.y - a.y * b.w - a.z * b.x + a.x * b.z,
                a.w * b.z - a.z * b.w - a.x * b.y + a.y * b.x,
                a.w * b.w + a.x * b.x + a.y * b.y + a.z * b.z);
            float v = Mathf.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z);
            float w = r.w < 0f ? -r.w : r.w;
            return 2f * Mathf.Atan2(v, w) * Mathf.Rad2Deg;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 0. THE REPLAY MUST BE FAITHFUL BEFORE ANYTHING IS MEASURED WITH IT
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⚠️ THE ORDER OF THE FILE IS THE POINT. If the stream replay disagrees with the solver, every
        /// number below it is worthless -- and asserting safety with an unfaithful replay is precisely the
        /// failure mode that let a guard throw the hand 205 mm while its gate reported 0.000 mm.
        /// </summary>
        [Test]
        public void TheStreamReplay_ReproducesTheSolver()
        {
            Rig g = MakeRig();
            float worstPos = 0f, worstFore = 0f, worstHum = 0f;
            string whereFore = "", whereHum = "", wherePos = "";
            int n = 0;

            foreach (bool tracker in new[] { false, true })
                foreach (var (name, dir) in Dirs)
                    for (float roll = -180f; roll <= 180.001f; roll += 3f)
                    {
                        Pose p = Build(g, dir, roll, tracker);
                        BasisArmSolveCore.Solve(p.In, out BasisArmSolveResult r);
                        J j = Compose(g, p, r);

                        float dp = Mathf.Max(Vector3.Distance(j.ElbowPos, r.ElbowSolved),
                                             Vector3.Distance(j.HandPos, r.HandSolved));
                        float df = PoseDeg(j.ForearmW, r.MidRotationSolved);
                        float dh = PoseDeg(j.HumerusW, r.RootRotationSolved);
                        string where = $"{(tracker ? "TRK" : "NOT")} {name} roll {roll:0.0} " +
                                       $"(guard {r.HumeralTwistGuardDeg:0.00}, foreRoll {r.ForearmRollDeg:0.00}, " +
                                       $"relief {r.WristReliefDeg:0.00})";
                        if (dp > worstPos) { worstPos = dp; wherePos = where; }
                        if (df > worstFore) { worstFore = df; whereFore = where; }
                        if (dh > worstHum) { worstHum = dh; whereHum = where; }
                        n++;
                    }

            Assert.That(n, Is.GreaterThan(1000), $"only {n} poses replayed.");
            Assert.That(worstPos * 1000f, Is.LessThan(1e-2f),
                $"the stream replay disagrees with the solver by {worstPos * 1000f:0.000000} mm at {wherePos}. The " +
                "replay is wrong, so nothing measured with it below means anything.");
            Assert.That(worstHum, Is.LessThan(1e-2f),
                $"the replayed HUMERUS disagrees with the solver by {worstHum:0.000000} deg at {whereHum}.");
            Assert.That(worstFore, Is.LessThan(1e-2f),
                $"the replayed FOREARM disagrees with the solver by {worstFore:0.000000} deg at {whereFore}.");

            TestContext.WriteLine($"  {n} poses: replay matches the solver to {worstPos * 1000f:0.000000} mm, " +
                                  $"humerus {worstHum:0.000000} deg, forearm {worstFore:0.000000} deg.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 1. THE SNAP
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ ROLL THE CONTROLLER THROUGH A FULL TURN ON A STRAIGHT ARM. THE FOREARM MAY NOT JUMP.
        ///
        /// A discontinuity is invisible in any single pose and exists only BETWEEN two of them, so this
        /// sweeps finely and measures the worst single-step change of the FOREARM's composed world rotation
        /// -- against a CONTROL sweep (bind not baked, so the roll stage declines) that proves the input
        /// itself is smooth. A jump can then only be the roll stage's.
        ///
        /// The gate is an AMPLIFICATION as well as an absolute, because the honest bound is structural: the
        /// correction is capped by `PI - |demand|`, slope -1, against a `pull` of slope +1, so min() of the
        /// two has slope magnitude 1 and `want` can change at most 2x as fast as the controller. The forearm
        /// also carries the humerus rigidly, which adds the humerus's own (smooth) step, so the measured
        /// worst is ~3x the input step. 8x leaves room for that and for float, and nothing else.
        ///
        /// Before the fix this measured 160.25 deg at a 0.25 deg step -- 641x -- on every one of the five
        /// arm directions, at the same controller roll, which is how you know it is the algebra and not the
        /// geometry.
        /// </summary>
        [Test]
        public void TheForearmRoll_IsContinuousAcrossTheSeam()
        {
            const float k_Step = 0.25f;
            const float k_MaxAbsoluteJumpDeg = 2.0f;
            const float k_MaxAmplification = 8f;

            Rig g = MakeRig();
            var report = new System.Text.StringBuilder();
            float mostRoll = 0f, deepestElbow = 0f;

            foreach (var (name, dir) in Dirs)
            {
                float worst = 0f, atRoll = 0f, worstControl = 0f;
                Quaternion prev = Quaternion.identity, prevC = Quaternion.identity;
                bool first = true;

                for (float roll = -180f; roll <= 180.001f; roll += k_Step)
                {
                    Pose p = Build(g, dir, roll, false);
                    BasisArmSolveCore.Solve(p.In, out BasisArmSolveResult r);
                    J j = Compose(g, p, r);
                    J c = At(g, dir, roll, false, bakeBind: false);

                    if (!first)
                    {
                        float d = PoseDeg(prev, j.ForearmW);
                        if (d > worst) { worst = d; atRoll = roll; }
                        worstControl = Mathf.Max(worstControl, PoseDeg(prevC, c.ForearmW));
                    }
                    prev = j.ForearmW;
                    prevC = c.ForearmW;
                    first = false;

                    mostRoll = Mathf.Max(mostRoll, Mathf.Abs(r.ForearmRollDeg));
                    deepestElbow = Mathf.Max(deepestElbow, Mathf.Abs(j.E));
                }

                // ANTI-TAUTOLOGY, per direction: if the CONTROL never moves, the sweep is not exciting the
                // degree of freedom at all and "the forearm did not jump" would be worth nothing.
                Assert.That(worstControl, Is.GreaterThan(1e-3f),
                    $"{name}: the CONTROL forearm moved only {worstControl:0.0000} deg per step across the whole " +
                    "sweep -- this sweep is not rolling the arm, so it could not detect a jump.");

                float amp = worst / k_Step;
                report.AppendLine($"    {name,-9}: worst step {worst,7:F3} deg (roll {atRoll,7:F1}), " +
                                  $"control {worstControl,6:F3}, amplification {amp,6:F1}x");

                Assert.That(worst, Is.LessThan(k_MaxAbsoluteJumpDeg),
                    $"{name}: THE FOREARM JUMPED {worst:0.000} deg in a single {k_Step:0.00} deg step of controller " +
                    $"roll, at roll {atRoll:0.0}. `demand` is a PRINCIPAL angle and +179.99 / -179.99 are the same " +
                    "hand; a hard clamp onto [-80, +80] sends them to opposite ends of the band and snaps the " +
                    "forearm 160 deg between a still humerus and a still hand -- which tears the mesh at the elbow " +
                    "AND the wrist. See BasisArmSolveCore's seam cap on `want`.");
                Assert.That(amp, Is.LessThan(k_MaxAmplification),
                    $"{name}: the roll stage amplified a smooth input {amp:0.0}x. The seam cap bounds `want` at 2x " +
                    "BY CONSTRUCTION (min of a +1 and a -1 slope); anything near this gate means the cap has been " +
                    "replaced by something that merely RELOCATES the discontinuity.");
            }

            // ANTI-TAUTOLOGY, overall: the stage has to have actually engaged, and the sweep has to have
            // actually reached the seam -- `want` only approaches +/-180 when the cap is binding hard.
            Assert.That(mostRoll, Is.GreaterThan(5f),
                $"the forearm roll stage never applied more than {mostRoll:0.000} deg anywhere in the sweep, so " +
                "this test is measuring a declined stage.");
            Assert.That(deepestElbow, Is.GreaterThan(170f),
                $"the elbow junction never exceeded {deepestElbow:0.0} deg of twist anywhere in the sweep. The seam " +
                "sits where `want` reaches 180, so a sweep that never gets there has not visited the defect and " +
                "cannot gate it.");

            TestContext.WriteLine($"\n  full turn of controller roll, {k_Step} deg steps, straight arm:\n" + report);
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 2. WHAT THE CAP BUYS: A BOUNDED WRIST
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ NOTHING IN THIS SOLVER BOUNDED THE WRIST, AND THE SEAM CAP BOUNDS IT FOR FREE.
        ///
        /// The hand is written to the controller verbatim (`tip.SetRotation(result.TipRotation)`) with no
        /// clamp relative to the forearm, so the wrist keeps whatever `demand - want` comes to. Under the
        /// old hard clamp that grew without limit and MEASURED 100.0 deg on every no-tracker direction. A
        /// real wrist has ~0 deg of independent axial range -- it is the FOREARM that rotates -- so 100 deg
        /// in the wrist joint is not a small error, it is the joint doing something it cannot do.
        ///
        /// Under the seam cap the residual is bounded ANALYTICALLY: it rises to `(180 - band)/2` = 50 deg at
        /// |demand| = 130 and falls back to ZERO at the seam, because `want` meets `demand` there. So this
        /// gate is a closed-form consequence of the cap, not a tuned number.
        /// </summary>
        [Test]
        public void TheSeamCap_LeavesTheWristWithinAHumanResidual()
        {
            const float k_MaxWristDeg = 55f;      // analytic bound 50, plus float and the humerus's own motion

            Rig g = MakeRig();
            float worstWrist = 0f, atRoll = 0f, biggestDemand = 0f;
            string worstDir = "";

            foreach (var (name, dir) in Dirs)
                for (float roll = -180f; roll <= 180.001f; roll += 0.5f)
                {
                    J j = At(g, dir, roll, false);
                    if (Mathf.Abs(j.W) > worstWrist) { worstWrist = Mathf.Abs(j.W); atRoll = roll; worstDir = name; }
                    biggestDemand = Mathf.Max(biggestDemand, Mathf.Abs(j.Demand));
                }

            // ANTI-TAUTOLOGY: the sweep must actually push the hand past the band, or a small wrist residual
            // is just "the hand never asked for anything".
            Assert.That(biggestDemand, Is.GreaterThan(150f),
                $"the largest hand roll demand anywhere in the sweep was {biggestDemand:0.0} deg, so the band was " +
                "barely exceeded and this gate never tested the overflow path.");
            Assert.That(worstWrist, Is.GreaterThan(10f),
                $"the wrist never carried more than {worstWrist:0.0} deg anywhere -- this sweep is not loading the " +
                "wrist at all, so the bound is not being tested.");

            Assert.That(worstWrist, Is.LessThan(k_MaxWristDeg),
                $"the WRIST carried {worstWrist:0.0} deg of axial twist ({worstDir}, roll {atRoll:0.0}). The wrist " +
                "has ~0 deg of independent axial range; the forearm is what pronates. The seam cap bounds this at " +
                $"(180 - {Band:0})/2 = {(180f - Band) * 0.5f:0} deg analytically, so a breach here means the cap has " +
                "been removed or the comfort band changed without re-deriving it.");

            TestContext.WriteLine($"  worst wrist residual {worstWrist:0.0} deg ({worstDir}, roll {atRoll:0.0}); " +
                                  $"largest hand demand {biggestDemand:0.0} deg.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 3. THE CAP MUST NOT HAVE EATEN THE ORDINARY REGIME
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⭐ BELOW THE CROSSING THE SEAM CAP IS THE EXACT IDENTITY, AND THIS PROVES IT WITHOUT THE OLD CODE.
        ///
        /// `pull = mag - band` has slope +1 and `seam = PI - mag` has slope -1, so they cross at
        /// mag = (180 + band)/2 = 130 deg. Below that the cap does not bind and `want` is the unchanged hard
        /// clamp -- every in-band and ordinary pose is bit for bit what it was, which is what makes this a
        /// seam fix rather than a re-tune of the forearm.
        ///
        /// ⚠️ BOTH SIDES COME FROM THE STREAM, NOT FROM THE SOLVER'S BOOKKEEPING. `demand` is the core's own
        /// formula re-evaluated on composed world rotations, and the forearm's landing point is read off the
        /// composed forearm -- so the assertion never touches a field the stage writes, which is the exact
        /// failure that let a guard report 0.000 mm while throwing the hand 205 mm.
        /// </summary>
        [Test]
        public void BelowTheCrossing_TheClampIsTheUnchangedHardClamp()
        {
            Rig g = MakeRig();
            int checkedPoses = 0;
            float worstErr = 0f, atDemand = 0f;

            foreach (var (_, dir) in Dirs)
                for (float roll = -180f; roll <= 180.001f; roll += 0.25f)
                {
                    J j = At(g, dir, roll, false);
                    if (Mathf.Abs(j.Demand) > Crossing - 2f)
                    {
                        continue;   // inside the seam window, where the cap is MEANT to bind
                    }

                    float want = Mathf.Clamp(j.Demand, -Band, Band);
                    float err = Mathf.Abs(Mathf.DeltaAngle(want, j.E));
                    if (err > worstErr) { worstErr = err; atDemand = j.Demand; }
                    checkedPoses++;
                }

            Assert.That(checkedPoses, Is.GreaterThan(500),
                $"only {checkedPoses} poses landed below the crossing; this test measured almost nothing.");
            Assert.That(worstErr, Is.LessThan(0.05f),
                $"below |demand| = {Crossing:0} deg the forearm departed from the plain hard clamp by " +
                $"{worstErr:0.000} deg (at demand {atDemand:0.0}). The seam cap is only supposed to bind ABOVE the " +
                "crossing; if it is binding here then ordinary in-band poses have been quietly re-tuned.");

            TestContext.WriteLine($"  {checkedPoses} poses below the crossing match the unchanged clamp to " +
                                  $"{worstErr:0.0000} deg.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 4. THE CAP MOVES NO JOINT, AND DOES NOT RELOCATE THE DISCONTINUITY
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The stage is a pure roll about the forearm's own long axis through the elbow: the elbow pivots on
        /// that axis and the hand LIES on it, so neither may move -- and the humerus is upstream of the
        /// stage entirely, so it may not jump either.
        ///
        /// ⚠️ THE HUMERUS HALF IS THE "DID NOT RELOCATE IT" GATE, and it is the one that matters. This
        /// repo's documented failure mode is a clamp that merely moves the kink somewhere else. If a future
        /// change pays for the forearm's continuity by making the humerus jump instead, test 1 still passes
        /// and this one fails.
        ///
        /// Measured THROUGH THE STREAM, per position and per rotation -- not by comparing result-struct
        /// fields, which is how a guard once reported 0.000 mm while throwing the hand 205 mm.
        /// </summary>
        [Test]
        public void TheSeamCap_MovesNoJoint_AndDoesNotRelocateTheJump()
        {
            const float k_Step = 0.25f;
            Rig g = MakeRig();
            float worstElbow = 0f, worstHand = 0f, worstHumerus = 0f, atHum = 0f;

            foreach (var (_, dir) in Dirs)
            {
                J prev = default;
                bool first = true;
                for (float roll = -180f; roll <= 180.001f; roll += k_Step)
                {
                    J j = At(g, dir, roll, false);
                    if (!first)
                    {
                        worstElbow = Mathf.Max(worstElbow, Vector3.Distance(prev.ElbowPos, j.ElbowPos));
                        worstHand = Mathf.Max(worstHand, Vector3.Distance(prev.HandPos, j.HandPos));
                        float dh = PoseDeg(prev.HumerusW, j.HumerusW);
                        if (dh > worstHumerus) { worstHumerus = dh; atHum = roll; }
                    }
                    prev = j;
                    first = false;
                }
            }

            Assert.That(worstElbow * 1000f, Is.LessThan(0.5f),
                $"the ELBOW moved {worstElbow * 1000f:0.000} mm in one {k_Step} deg step of controller roll. The " +
                "forearm roll pivots about the elbow, so this must be zero however the roll is composed.");
            Assert.That(worstHand * 1000f, Is.LessThan(0.5f),
                $"the HAND moved {worstHand * 1000f:0.000} mm in one {k_Step} deg step. The hand lies ON the " +
                "forearm's long axis, so a roll about that axis cannot move it.");
            Assert.That(worstHumerus, Is.LessThan(2.0f),
                $"the HUMERUS jumped {worstHumerus:0.000} deg in one {k_Step} deg step (roll {atHum:0.0}). The seam " +
                "cap must not buy the forearm's continuity by relocating the discontinuity onto the upper arm -- " +
                "that is this repo's documented way of not fixing something.");

            TestContext.WriteLine($"  worst single-step: elbow {worstElbow * 1000f:0.000} mm, hand " +
                                  $"{worstHand * 1000f:0.000} mm, humerus {worstHumerus:0.000} deg.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 4b. THE TWIST BONES -- THE STAGE DOWNSTREAM OF EVERYTHING, WHERE THE WRAP LANDS LAST
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ⚠️ THE FOREARM FIX ALONE MOVED THE JUMP INTO THE UPPER ARM. THIS IS THE GATE THAT CAUGHT IT.
        ///
        /// BasisFullBodyIK.SolveArmTwist hands the twist bones a FRACTION of a child's local twist, and a
        /// fractional map of a circle onto itself is discontinuous at +/-180 for every fraction strictly
        /// between 0 and 1: theta = +180 and theta = -180 are the same child, and f*theta sends them 360f
        /// apart. The forearm seam cap in BasisArmSolveCore necessarily drives the forearm to 180 deg
        /// relative to the humerus at the seam -- that is what makes the FOREARM continuous -- so it hands
        /// this stage exactly the input that breaks it.
        ///
        /// MEASURED with the forearm fixed and this stage NOT fixed: the upper-arm twist bone jumped
        /// 54.10 deg in a single 0.25 deg step, which is exactly 360 * 0.15. The arm chain would have
        /// traded a 160 deg break at the elbow for a 54 deg break in the middle of the upper arm.
        ///
        /// The fractions are the live rig's: BasisFullBodyIK defaults lowerArmTwistFraction 0.5 and
        /// upperArmTwistFraction 0.3, times a twist bone at the segment midpoint (SegmentPositionFraction
        /// 0.5), so f = 0.25 and 0.15.
        /// </summary>
        [Test]
        public void TheTwistBones_DoNotInheritTheSeam()
        {
            const float k_Step = 0.25f;
            const float k_MaxJumpDeg = 5f;

            Rig g = MakeRig();
            var report = new System.Text.StringBuilder();
            float deepestChildTwist = 0f;

            foreach (var (name, dir) in Dirs)
            {
                float stepLow = 0f, stepUp = 0f, atUp = 0f;
                Quaternion pLow = Quaternion.identity, pUp = Quaternion.identity;
                bool first = true;

                for (float roll = -180f; roll <= 180.001f; roll += k_Step)
                {
                    J j = At(g, dir, roll, false);
                    Vector3 foreSeg = j.HandPos - j.ElbowPos;
                    Vector3 upSeg = j.ElbowPos - Shoulder;

                    Quaternion low = TwistBone(j.ForearmW, j.HandW, foreSeg, 0.5f * 0.5f);
                    Quaternion up = TwistBone(j.HumerusW, j.ForearmW, upSeg, 0.5f * 0.3f);
                    if (!first)
                    {
                        stepLow = Mathf.Max(stepLow, PoseDeg(pLow, low));
                        float du = PoseDeg(pUp, up);
                        if (du > stepUp) { stepUp = du; atUp = roll; }
                    }
                    pLow = low; pUp = up; first = false;

                    deepestChildTwist = Mathf.Max(deepestChildTwist, Mathf.Abs(j.E));
                }

                report.AppendLine($"    {name,-9}: lower bone worst step {stepLow,6:F3} deg, upper bone {stepUp,7:F3} deg " +
                                  $"(roll {atUp,7:F1})");

                Assert.That(stepUp, Is.LessThan(k_MaxJumpDeg),
                    $"{name}: the UPPER-ARM TWIST BONE jumped {stepUp:0.000} deg in a single {k_Step} deg step of " +
                    $"controller roll (roll {atUp:0.0}). A fractional twist is discontinuous at +/-180 by " +
                    "construction, and the forearm seam cap upstream drives the child twist to exactly 180 there. " +
                    "BasisTwistSolveCore's own seam cap is what stops this becoming a break in the middle of the " +
                    "upper arm -- if it has been removed, the elbow's discontinuity has simply been relocated.");
                Assert.That(stepLow, Is.LessThan(k_MaxJumpDeg),
                    $"{name}: the LOWER-ARM TWIST BONE jumped {stepLow:0.000} deg in a single {k_Step} deg step.");
            }

            // ANTI-TAUTOLOGY: the child twist must actually reach the seam, or the cap is never exercised.
            Assert.That(deepestChildTwist, Is.GreaterThan(170f),
                $"the forearm never twisted more than {deepestChildTwist:0.0} deg relative to the humerus, so the " +
                "twist bones were never handed a near-seam child and this gate proved nothing.");

            TestContext.WriteLine($"\n  twist bones over a full turn, {k_Step} deg steps:\n" + report);
        }

        /// <summary>Drives BasisTwistSolveCore exactly as BasisFullBodyIK.SolveArmTwist does.</summary>
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

        /// <summary>
        /// The twist-bone seam cap must be the ORIGINAL Slerp below its crossing at 180/(2-f) -- 97.3 deg at
        /// f = 0.15, 102.9 deg at f = 0.25. Below there the cap does not bind and the untouched call runs on
        /// untouched operands, so ordinary twist distribution is bit for bit unchanged. This pins that.
        /// </summary>
        [Test]
        public void TheTwistBoneCap_IsTheUnchangedSlerpBelowItsCrossing()
        {
            int n = 0;
            float worst = 0f, atTheta = 0f, atF = 0f, atMeasured = 0f;

            foreach (float f in new[] { 0.15f, 0.25f, 0.5f })
            {
                float crossing = 180f / (2f - f);
                for (float theta = -179f; theta <= 179f; theta += 0.5f)
                {
                    if (Mathf.Abs(theta) >= crossing - 0.5f) continue;

                    Quaternion child = Quaternion.AngleAxis(theta, Vector3.right);
                    BasisTwistSolveInput ti;
                    ti.ParentRotation = Quaternion.identity;
                    ti.ChildRotation = child;
                    ti.ParentToChild = Vector3.right * 0.3f;
                    ti.Fraction = f;
                    BasisTwistSolveCore.Solve(ti, out BasisTwistSolveResult r);

                    Quaternion twistOnly = BasisTwistSolveCore.ExtractTwist(child, Vector3.right);
                    Quaternion want = Quaternion.Slerp(Quaternion.identity, twistOnly, f);
                    float err = PoseDeg(want, r.TwistWorldRotation);
                    if (err > worst)
                    {
                        worst = err; atTheta = theta; atF = f;
                        atMeasured = BasisTwistSolveCore.SignedTwistAngleDeg(twistOnly, Vector3.right);
                    }
                    n++;
                }
            }

            Assert.That(n, Is.GreaterThan(500), $"only {n} angles checked.");
            Assert.That(worst, Is.LessThan(1e-3f),
                $"below the crossing the twist bone departed from the original Slerp by {worst:0.000000} deg (theta " +
                $"{atTheta:0.0}, f {atF:0.00}, crossing {180f / (2f - atF):0.0}, SignedTwistAngleDeg read " +
                $"{atMeasured:0.0000}, pull {(1f - atF) * Mathf.Abs(atMeasured):0.000} vs seam " +
                $"{180f - Mathf.Abs(atMeasured):0.000}). The cap is only supposed to bind inside the seam window.");

            TestContext.WriteLine($"  {n} sub-crossing angles match the original Slerp to {worst:0.000000} deg.");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        // 5. DECLINE AND CONTAINMENT
        // ════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>An unbaked rig must take the stage's decline branch exactly, so every existing caller,
        /// test and offline sweep stays bit-identical. Guarded by the SAME field checks the stage uses.</summary>
        [Test]
        public void TheStage_DeclinesExactlyWhenTheRigHasNotBakedBind()
        {
            Rig g = MakeRig();
            float worstRoll = 0f;
            int n = 0;

            foreach (var (_, dir) in Dirs)
                for (float roll = -180f; roll <= 180.001f; roll += 5f)
                {
                    Pose p = Build(g, dir, roll, false, bakeBind: false);
                    BasisArmSolveCore.Solve(p.In, out BasisArmSolveResult r);
                    worstRoll = Mathf.Max(worstRoll, Mathf.Abs(r.ForearmRollDeg));
                    n++;
                }

            Assert.That(n, Is.GreaterThan(300), $"only {n} poses swept.");
            Assert.That(worstRoll, Is.EqualTo(0f),
                $"with BindLowerArmRotation / BindHumerusRotation left at the struct default the stage still " +
                $"applied {worstRoll:0.000} deg of forearm roll. Zero/absent inputs must decline to the exact " +
                "previous behaviour.");

            TestContext.WriteLine($"  {n} unbaked poses: forearm roll is exactly 0 on every one.");
        }

        /// <summary>
        /// ⚠️ THE SEAM CAP IS INSIDE `if (!i.HintIsTracker && ...)` AND MUST STAY THERE. The tracker path
        /// has its own forearm roll with its own bound and its own wrap fade, tuned against a measured
        /// strap; this gate pins that the no-tracker fix did not leak into it. The tracker path's wrist
        /// residual is near zero BECAUSE it routes pronation into the forearm from the measurement -- that
        /// is the behaviour being protected.
        /// </summary>
        [Test]
        public void TheTrackerPath_IsUntouchedByTheSeamCap()
        {
            const float k_Step = 0.25f;
            Rig g = MakeRig();
            float worstWrist = 0f, worstFore = 0f;

            foreach (var (name, dir) in Dirs)
            {
                // fwd-lat is excluded from the STEP measurement only: at exactly +/-180 its synthetic elbow
                // tracker is commanded to point straight up, and BasisElbowAnatomyCore's guard has its own
                // pole ambiguity at that apex (measured: guard swivel flips -7.852 -> +7.852 deg with the
                // hint position byte-identical). That is a separate defect in a different file, reported
                // rather than swallowed; excluding it here keeps this gate about the seam cap.
                bool skipStep = name == "fwd-lat";
                Quaternion prev = Quaternion.identity;
                bool first = true;
                for (float roll = -180f; roll <= 180.001f; roll += k_Step)
                {
                    J j = At(g, dir, roll, true);
                    worstWrist = Mathf.Max(worstWrist, Mathf.Abs(j.W));
                    if (!first && !skipStep) worstFore = Mathf.Max(worstFore, PoseDeg(prev, j.ForearmW));
                    prev = j.ForearmW;
                    first = false;
                }
            }

            Assert.That(worstWrist, Is.LessThan(10f),
                $"the TRACKER path left {worstWrist:0.0} deg in the wrist. It routes pronation into the forearm " +
                "from the strap's own measurement and must keep doing so; a jump here means the no-tracker seam " +
                "cap has leaked out of its branch.");
            Assert.That(worstFore, Is.LessThan(2.0f),
                $"the TRACKER path's forearm jumped {worstFore:0.000} deg in one {k_Step} deg step.");

            TestContext.WriteLine($"  tracker path: worst wrist {worstWrist:0.00} deg, worst forearm step " +
                                  $"{worstFore:0.000} deg.");
        }
    }
}
