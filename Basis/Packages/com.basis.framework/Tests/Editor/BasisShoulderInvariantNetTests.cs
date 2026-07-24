using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// THE SAME INVARIANT NET, OVER THE SHOULDER GIRDLE PRE-SOLVE.
    ///
    /// ================================================================================================
    /// BasisShoulderSolveCore is where two of the seven repeating classes have already bitten:
    ///
    ///   A WRONG FRAME. Everything downstream reads the girdle frame as X=lateral, Y=up, Z=forward -- the
    ///   raise/depression split, the shrug's hang gate, the retraction's posterior dot, the
    ///   elevation-vs-protraction axis split. Those are ANATOMY, and the chest BONE only carries them on a
    ///   rig that happens to be bound axis-aligned. On an ordinary Blender export (bone-Y-up, an X-90 bind)
    ///   a plain hanging arm reads as bone-local -Z -- dead ahead of the shoulder -- so the hang gate shuts,
    ///   the posterior dot saturates, and an IDLE arm takes a full scapular retraction and saturates the
    ///   girdle clamp. That ships as "this avatar's shoulders are permanently hunched", which reads as a
    ///   broken avatar rather than as an IK bug, and the genuine posterior reach loses its retraction.
    ///   ChestBind exists to cancel exactly that, and it DECLINES when zero -- so the invariant and its own
    ///   control are both already in the core.
    ///
    ///   A GUARD THAT CANNOT FIRE / A GATE THAT FAILS THE WRONG WAY. `elbowTrust` fails OPEN on purpose:
    ///   TposeElbowLength == 0 means "the caller did not bake one", not "the tracker is wrong", and reading
    ///   absence as maximum doubt would collapse the whole solve to the hand-path reach gate for every
    ///   caller that omits it. Which way a gate fails on missing data is exactly the kind of thing that is
    ///   obvious once stated and invisible otherwise, so it is stated here as a test.
    ///
    /// Plus the classes it has NOT yet been bitten by, asserted before it is: continuity through every gate
    /// (the girdle has five -- setting, reach, hang, rise, posterior -- and a swing that can reach 180 deg,
    /// where FromToRotation's axis becomes arbitrary), boundedness, twist-freedom measured INDEPENDENTLY of
    /// the core's own bookkeeping, determinism, and degenerate-input hygiene.
    /// ================================================================================================
    /// </summary>
    public class BasisShoulderInvariantNetTests
    {
        const float ArmLen = 0.60f;
        const float ClavLen = 0.14f;
        const float ElbowLen = 0.44f;
        const float Couple = 0.8f;
        const float MaxDeg = 25f;          // the live girdle clamp
        const float Elevation = 0.4f;      // the shipped slider defaults
        const float Protraction = 0.3f;

        static readonly Vector3 Shoulder = Vector3.zero;

        /// <summary>Deliberately NOT identity: an identity rest clavicle would let a solve that dropped
        /// `shoulderRestLocal` entirely still look right.</summary>
        static readonly Quaternion TposeShoulder = Quaternion.Euler(3f, 17f, -8f);

        struct Spec
        {
            public Vector3 ArmDir;          // ANATOMICAL arm direction (chest frame), unit
            public float Reach;             // fraction of ArmLen for the hand
            public float ElbowFrac;         // fraction of ElbowLen for the elbow driver
            public bool HasElbow;
            public bool HasTracker;
            public bool IsLeft;
            public bool ShrugEnabled;
            public bool RetractEnabled;
            public float MaxShoulderDeg;
            public Quaternion ChestConv;    // the chest BONE's authoring convention
            public bool FeedChestBind;
            public Quaternion World;        // a rigid re-orientation of the LIVE world only
            public Vector3 WorldT;
            public Quaternion ChestExtra;   // a live chest rotation away from its rest pose
        }

        static Spec Default()
        {
            Spec s = default;
            s.ArmDir = Vector3.right;
            s.Reach = 0.9f;
            s.ElbowFrac = 1f;
            s.HasElbow = true;
            s.IsLeft = false;
            s.ShrugEnabled = true;
            s.RetractEnabled = true;
            s.MaxShoulderDeg = MaxDeg;
            s.ChestConv = Quaternion.identity;
            s.FeedChestBind = true;
            s.World = Quaternion.identity;
            s.ChestExtra = Quaternion.identity;
            return s;
        }

        static Vector3 RestDir(bool isLeft) => isLeft ? Vector3.left : Vector3.right;

        static BasisShoulderSolveInput Build(in Spec s)
        {
            Quaternion W = s.World;
            Quaternion chestAnat = s.ChestExtra;              // live chest, in ANATOMICAL terms
            Quaternion tposeChest = Quaternion.identity;

            BasisShoulderSolveInput i = default;
            i.ShoulderPos = W * Shoulder + s.WorldT;
            i.HandTargetPos = W * (Shoulder + s.ArmDir.normalized * (s.Reach * ArmLen)) + s.WorldT;
            i.ElbowPos = W * (Shoulder + s.ArmDir.normalized * (s.ElbowFrac * ElbowLen)) + s.WorldT;
            i.HasElbow = s.HasElbow;
            i.HasShoulderTracker = s.HasTracker;

            // The chest BONE's rotations carry the authoring convention on the right; the anatomical frame
            // is what ChestBind cancels back out.
            i.ChestRot = W * (chestAnat * s.ChestConv);
            i.TposeChestRot = tposeChest * s.ChestConv;
            i.ChestBind = s.FeedChestBind ? s.ChestConv : default;

            i.TposeShoulderRot = tposeChest * TposeShoulder;
            i.TposeArmDirWorld = RestDir(s.IsLeft);
            i.TposeArmLength = ArmLen;
            i.TposeClavicleLength = ClavLen;
            i.TposeElbowLength = ElbowLen;
            i.ShrugEnabled = s.ShrugEnabled;
            i.RetractEnabled = s.RetractEnabled;
            i.ElevationFactor = Elevation;
            i.ProtractionFactor = Protraction;
            i.CoupleRatio = Couple;
            i.MaxShoulderDeg = s.MaxShoulderDeg;
            i.TrackerFinal = W * (chestAnat * TposeShoulder);
            i.IsLeft = s.IsLeft;
            return i;
        }

        /// <summary>Chest-frame direction from an anatomical azimuth/elevation. az 0 = lateral (+X, the
        /// right arm's rest), az 90 = forward (+Z), el -90 = hanging.</summary>
        static Vector3 Dir(float azDeg, float elDeg, bool isLeft)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            float lat = Mathf.Cos(el) * Mathf.Cos(az);
            return new Vector3(isLeft ? -lat : lat, Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        /// <summary>THE GIRDLE ROTATION, MEASURED -- not read out of the result struct. `TwistLeakDeg` is
        /// computed AFTER the core strips the twist, so it is ~0 by construction and asserting on it proves
        /// nothing. This recovers the rotation actually applied to the clavicle relative to its rest pose
        /// and reads the twist off THAT.</summary>
        static void MeasureGirdle(in BasisShoulderSolveInput i, in BasisShoulderSolveResult r,
                                  out float appliedDeg, out float twistLeakDeg)
        {
            Quaternion restLocal = Quaternion.Inverse(i.TposeChestRot) * i.TposeShoulderRot;
            Quaternion rest = i.ChestRot * restLocal;
            Quaternion delta = r.ShoulderRotation * Quaternion.Inverse(rest);
            appliedDeg = Quaternion.Angle(Quaternion.identity, delta);

            Vector3 armAxis = i.HandTargetPos - i.ShoulderPos;
            if (i.HasElbow) armAxis = i.ElbowPos - i.ShoulderPos;
            if (armAxis.sqrMagnitude < 1e-10f) { twistLeakDeg = 0f; return; }
            twistLeakDeg = Mathf.Abs(BasisArmNet.TwistDeg(delta, armAxis.normalized));
        }

        // ============================================================================================
        // 1. ⭐ THE GIRDLE FRAME IS ANATOMICAL, NOT THE CHEST BONE'S
        // ============================================================================================

        /// <summary>
        /// ⭐ THE SAME BODY, EXPORTED SEVEN WAYS, MUST GET THE SAME SHOULDER -- AND IT ONLY DOES WHEN THE
        /// CHEST BIND IS SUPPLIED. THIS TEST IS ITS OWN CONTROL.
        ///
        /// With ChestBind fed the answer is EXACTLY invariant to the chest bone's authoring convention:
        /// inv(ChestRot * inv(bind)) is bind * inv(ChestRot), so the frame costs one product, and the
        /// convention cancels out of both the measurement and the reconstruction. With ChestBind DECLINED
        /// -- the struct default, which is the pre-fix behaviour every un-updated caller still gets -- the
        /// very same sweep must come apart, and by a LOT: on an X-90 bind a plain hanging arm reads as
        /// pointing dead ahead, which is the permanently-hunched-avatar report.
        ///
        /// So the two halves are inseparable, and neither can be vacuous: if the invariance held with the
        /// bind declined, the frame would not be doing anything.
        /// </summary>
        [Test]
        public void ShoulderSolve_IsInvariantToTheChestBonesAuthoringConvention_OnlyWithTheBindFed()
        {
            var findings = new List<string>();
            var log = new StringBuilder();

            (string name, Quaternion conv)[] conventions =
            {
                ("identity", Quaternion.identity),
                ("X-90 (Blender bone-Y-up)", Quaternion.Euler(-90f, 0f, 0f)),
                ("X+90", Quaternion.Euler(90f, 0f, 0f)),
                ("Z+90 roll", Quaternion.Euler(0f, 0f, 90f)),
                ("Y+180", Quaternion.Euler(0f, 180f, 0f)),
                ("arbitrary 37/-22/61", Quaternion.Euler(37f, -22f, 61f)),
                ("arbitrary -14/88/-133", Quaternion.Euler(-14f, 88f, -133f)),
            };

            var poses = new List<Spec>();
            foreach (bool left in new[] { false, true })
            foreach (float az in new[] { 0f, 55f, 125f, 200f, 290f })
            foreach (float el in new[] { -88f, -60f, -20f, 30f, 75f })
            foreach (float reach in new[] { 0.45f, 0.80f, 0.97f })
            foreach (bool elbow in new[] { false, true })
            {
                Spec s = Default();
                s.IsLeft = left;
                s.ArmDir = Dir(az, el, left);
                s.Reach = reach;
                s.ElbowFrac = reach;
                s.HasElbow = elbow;
                poses.Add(s);
            }
            Assert.That(poses.Count, Is.GreaterThan(200));

            float worstFed = 0f, worstDeclined = 0f;
            float mostApplied = 0f;

            for (int p = 0; p < poses.Count; p++)
            {
                Spec baseSpec = poses[p];

                Quaternion refFed = Quaternion.identity, refDec = Quaternion.identity;
                bool haveRef = false;

                foreach (var c in conventions)
                {
                    Spec fed = baseSpec; fed.ChestConv = c.conv; fed.FeedChestBind = true;
                    Spec dec = baseSpec; dec.ChestConv = c.conv; dec.FeedChestBind = false;

                    BasisShoulderSolveInput iF = Build(fed), iD = Build(dec);
                    BasisShoulderSolveCore.Solve(iF, out BasisShoulderSolveResult rF);
                    BasisShoulderSolveCore.Solve(iD, out BasisShoulderSolveResult rD);
                    if (!rF.Apply || !rD.Apply) continue;

                    // The clavicle's world rotation carries the CLAVICLE's own convention, which is not
                    // being varied here -- only the CHEST's is. So the world result must be identical.
                    if (!haveRef) { refFed = rF.ShoulderRotation; refDec = rD.ShoulderRotation; haveRef = true; continue; }

                    float dF = BasisArmNet.PoseChangeDeg(refFed, rF.ShoulderRotation);
                    float dD = BasisArmNet.PoseChangeDeg(refDec, rD.ShoulderRotation);
                    worstFed = Mathf.Max(worstFed, dF);
                    worstDeclined = Mathf.Max(worstDeclined, dD);

                    MeasureGirdle(iF, rF, out float applied, out _);
                    mostApplied = Mathf.Max(mostApplied, applied);

                    if (dF > 0.05f)
                    {
                        findings.Add($"[{c.name}] the solved clavicle moved {dF:0.000} deg against the identity " +
                                     $"convention on pose {p} (arm {baseSpec.ArmDir}, reach {baseSpec.Reach:0.00}, " +
                                     $"elbow {baseSpec.HasElbow}). The chest bone's authoring frame is leaking into " +
                                     "an anatomical decision.");
                    }
                }
            }

            log.AppendLine($"      ChestBind FED:      worst spread across 7 conventions = {worstFed:F4} deg");
            log.AppendLine($"      ChestBind DECLINED: worst spread across 7 conventions = {worstDeclined:F1} deg   (the pre-fix behaviour)");

            BasisArmNet.Report(findings, log, "chest-bind convention invariance");

            Assert.That(mostApplied, Is.GreaterThan(5f),
                $"the girdle never moved more than {mostApplied:0.00} deg anywhere in this pose set, so 'the result is " +
                "invariant' is being asserted about a result that is always the rest pose.");
            BasisArmNet.Gate(
                "the solved clavicle's spread across seven chest-bone authoring conventions",
                worstFed, 0.05f, worstDeclined, 5f);

            TestContext.WriteLine("\n  chest-bind convention invariance:\n" + log);
        }

        // ============================================================================================
        // 2. RIGID EQUIVARIANCE
        // ============================================================================================

        /// <summary>
        /// Turn the user round, move them across the room: the girdle must come out rotated by exactly the
        /// same amount and every scalar it reports must be untouched.
        ///
        /// ⚠️ ChestBind and the T-pose data are deliberately NOT transformed. ChestBind DEFINES the
        /// anatomical frame and the T-pose rotations are read only RELATIVE to the live chest, so a global
        /// re-orientation must leave them alone -- transforming them would be testing a different, and
        /// wrong, contract. That the result is still equivariant is precisely because the core reads them
        /// relatively.
        /// </summary>
        [Test]
        public void ShoulderSolve_IsEquivariantUnderAnyRigidTransform()
        {
            var findings = new List<string>();

            (Quaternion R, Vector3 T, string name)[] xf =
            {
                (Quaternion.identity, Vector3.zero, "identity (harness self-check)"),
                (Quaternion.Euler(0f, 90f, 0f), Vector3.zero, "yaw 90"),
                (Quaternion.Euler(0f, -137f, 0f), new Vector3(4f, 0f, -9f), "yaw -137 + move"),
                (Quaternion.Euler(80f, 0f, 0f), Vector3.zero, "pitch 80"),
                (Quaternion.Euler(0f, 0f, -95f), Vector3.zero, "roll -95"),
                (Quaternion.Euler(20f, 160f, -40f), new Vector3(-6f, 3f, 11f), "general"),
            };

            float worstRot = 0f, worstScalar = 0f, mostApplied = 0f;

            foreach (var t in xf)
            foreach (bool left in new[] { false, true })
            foreach (bool tracker in new[] { false, true })
            foreach (float az in new[] { 0f, 90f, 190f, 280f })
            foreach (float el in new[] { -85f, -30f, 40f })
            {
                Spec s = Default();
                s.IsLeft = left;
                s.HasTracker = tracker;
                s.ArmDir = Dir(az, el, left);
                s.Reach = 0.88f;
                s.ElbowFrac = 0.88f;
                s.ChestExtra = Quaternion.Euler(6f, -12f, 4f);

                Spec moved = s; moved.World = t.R; moved.WorldT = t.T;

                BasisShoulderSolveCore.Solve(Build(s), out BasisShoulderSolveResult r0);
                BasisShoulderSolveCore.Solve(Build(moved), out BasisShoulderSolveResult r1);
                if (!r0.Apply || !r1.Apply) continue;

                MeasureGirdle(Build(s), r0, out float applied, out _);
                mostApplied = Mathf.Max(mostApplied, applied);

                float rot = BasisArmNet.PoseChangeDeg(t.R * r0.ShoulderRotation, r1.ShoulderRotation);
                worstRot = Mathf.Max(worstRot, rot);
                float sca = Mathf.Max(Mathf.Abs(r0.AppliedAngleDeg - r1.AppliedAngleDeg),
                            Mathf.Max(Mathf.Abs(r0.Elevation - r1.Elevation),
                            Mathf.Max(Mathf.Abs(r0.Protraction - r1.Protraction),
                            Mathf.Max(Mathf.Abs(r0.ShrugDeg - r1.ShrugDeg),
                            Mathf.Max(Mathf.Abs(r0.RetractDeg - r1.RetractDeg),
                                      Mathf.Abs(r0.SwingAngleDeg - r1.SwingAngleDeg))))));
                worstScalar = Mathf.Max(worstScalar, sca);

                if (rot > 0.1f) findings.Add($"[{t.name}] the clavicle is off by {rot:0.0000} deg after a rigid transform.");
                if (sca > 0.1f) findings.Add($"[{t.name}] a reported scalar moved {sca:0.000} after a rigid transform.");
            }

            BasisArmNet.Report(findings, null, "shoulder rigid equivariance");
            Assert.That(mostApplied, Is.GreaterThan(5f), "the girdle never moved; equivariance of a rest pose is trivial.");
            TestContext.WriteLine($"  shoulder equivariance: rotation {worstRot:0.0000} deg, scalars {worstScalar:0.000}.");
        }

        // ============================================================================================
        // 3. CONTINUITY THROUGH FIVE GATES AND ONE 180-DEGREE SWING
        // ============================================================================================

        /// <summary>
        /// The girdle carries five separate gates -- setting (8-95 deg of swing), reach (a hard threshold at
        /// 0.7 softened by t^2), hang (0.75-0.92), rise (a deficit window), posterior (0.50-0.95) -- plus a
        /// depression crossover, an elbow-trust fade, and a magnitude clamp. Each is a place where the
        /// output's DERIVATIVE changes, and a derivative change becomes a VALUE change the moment anyone
        /// gets the algebra slightly wrong.
        ///
        /// ⚠️ AND THERE IS A 180 DEGREE SWING IN HERE. `swing = FromToRotation(restDirL, armDirL)` is
        /// anti-parallel when the arm points to the OPPOSITE side of the body -- a right hand reaching to
        /// the left shoulder -- where FromToRotation abandons the plane and returns 180 deg about an
        /// arbitrary axis, and QuatToRotationVector's `deg > 180` wrap sits on the same point. Both are the
        /// principal-angle seam this repo has already been bitten by twice, on a pose a user can reach.
        /// </summary>
        [Test]
        public void ShoulderSolve_IsContinuous_ThroughEveryGate()
        {
            var findings = new List<string>();
            var log = new StringBuilder();

            // ── azimuth, at several elevations: crosses the posterior gate and, near el 0, the anti-parallel swing.
            foreach (bool left in new[] { false, true })
            foreach (float el in new[] { -80f, -45f, -5f, 40f })
            {
                RunSweep($"arm azimuth 0-360, el {el:0}, {(left ? "LEFT" : "right")}", t =>
                {
                    Spec s = Default();
                    s.IsLeft = left;
                    s.ArmDir = Dir(t, el, left);
                    s.Reach = 0.88f;
                    s.ElbowFrac = 0.88f;
                    return s;
                }, 0f, 360f, 2880, findings, log);
            }

            // ── elevation: crosses the hang gate, the depression crossover and the setting phase.
            foreach (float az in new[] { 0f, 80f, 175f, 260f })
            {
                RunSweep($"arm elevation -90..+90, az {az:0}", t =>
                {
                    Spec s = Default();
                    s.ArmDir = Dir(az, t, false);
                    s.Reach = 0.85f;
                    s.ElbowFrac = 0.85f;
                    return s;
                }, -90f, 90f, 2880, findings, log);
            }

            // ── reach: crosses the reach gate at 0.7 (hand path) and the elbow-trust fade (elbow path).
            foreach (bool elbow in new[] { false, true })
            {
                RunSweep($"reach 0.10-1.40, {(elbow ? "ELBOW driver" : "hand driver")}", t =>
                {
                    Spec s = Default();
                    s.ArmDir = Dir(15f, -70f, false);
                    s.Reach = t;
                    s.ElbowFrac = t;
                    s.HasElbow = elbow;
                    return s;
                }, 0.10f, 1.40f, 2600, findings, log);
            }

            // ── the shrug's own axis: the driver rises straight up while the arm's DIRECTION barely changes.
            RunSweep("shrug: hanging driver rises 0-0.30 of the arm", t =>
            {
                Spec s = Default();
                s.ArmDir = Dir(0f, -88f, false);
                s.Reach = 1f - t;
                s.ElbowFrac = 1f - t;
                return s;
            }, 0f, 0.30f, 2400, findings, log);

            BasisArmNet.Report(findings, log, "shoulder continuity");
        }

        static void RunSweep(string context, Func<float, Spec> at, float t0, float t1, int steps,
                             List<string> findings, StringBuilder log)
        {
            var inDir = new BasisArmNet.Channel("IN arm dir (deg)");
            var outRot = new BasisArmNet.Channel("clavicle rot (deg)");
            var outApplied = new BasisArmNet.Channel("applied angle (deg)");
            var outShrug = new BasisArmNet.Channel("shrug (deg)");
            var outRetract = new BasisArmNet.Channel("retract (deg)");

            bool first = true;
            Vector3 pDir = Vector3.zero;
            Quaternion pRot = Quaternion.identity;
            float pApplied = 0f, pShrug = 0f, pRetract = 0f;

            for (int k = 0; k <= steps; k++)
            {
                float t = Mathf.Lerp(t0, t1, k / (float)steps);
                Spec s = at(t);
                BasisShoulderSolveInput i = Build(s);
                BasisShoulderSolveCore.Solve(i, out BasisShoulderSolveResult r);

                if (!r.Apply) { first = true; continue; }
                if (!BasisArmNet.Finite(r.ShoulderRotation) || !BasisArmNet.Unit(r.ShoulderRotation))
                {
                    findings.Add($"{context}: at t={t:0.000} the clavicle rotation is not a valid rotation ({r.ShoulderRotation}).");
                    return;
                }

                MeasureGirdle(i, r, out float applied, out _);

                if (!first)
                {
                    inDir.Add(Vector3.Angle(pDir, s.ArmDir.normalized), t);
                    outRot.Add(BasisArmNet.PoseChangeDeg(pRot, r.ShoulderRotation), t);
                    outApplied.Add(Mathf.Abs(applied - pApplied), t);
                    outShrug.Add(Mathf.Abs(r.ShrugDeg - pShrug), t);
                    outRetract.Add(Mathf.Abs(r.RetractDeg - pRetract), t);
                }
                pDir = s.ArmDir.normalized; pRot = r.ShoulderRotation;
                pApplied = applied; pShrug = r.ShrugDeg; pRetract = r.RetractDeg;
                first = false;
            }

            log.AppendLine($"    {context}");

            // sweep-level non-vacuity: the clavicle has to have moved SOMEWHERE, or this sweep says nothing.
            if (!(outRot.Worst > 0f))
            {
                findings.Add($"{context}: the clavicle NEVER MOVED anywhere in this sweep, so every continuity " +
                             "bound it reports is vacuous -- the sweep is not driving the girdle at all.");
                return;
            }

            // the harness next: a sweep whose own input jumps proves nothing about the core.
            string h = BasisArmNet.GateSmooth(inDir, 0.05f, 4f, "HARNESS " + context, log);
            if (h != null) { findings.Add(h); return; }

            Add(findings, BasisArmNet.GateSmooth(outRot, 1.0f, 12f, context, log));
            Add(findings, BasisArmNet.GateSmooth(outApplied, 1.0f, 12f, context, log));
            Add(findings, BasisArmNet.GateSmooth(outShrug, 1.0f, 12f, context, log));
            Add(findings, BasisArmNet.GateSmooth(outRetract, 1.0f, 12f, context, log));
        }

        static void Add(List<string> findings, string f) { if (f != null) findings.Add(f); }

        // ============================================================================================
        // 4. BOUNDED, TWIST-FREE, AND EVERY STAGE REACHABLE
        // ============================================================================================

        /// <summary>
        /// The girdle is clamped, it never rolls with the arm, and each of its three contributors must be
        /// shown to ENGAGE somewhere -- a shrug, a retraction, and the coupled swing.
        ///
        /// ⚠️ THE TWIST LEAK IS MEASURED FROM THE APPLIED ROTATION, NOT FROM r.TwistLeakDeg. The core
        /// computes that field AFTER it strips the twist, so it is ~0 by construction and asserting on it
        /// proves only that subtraction works. The clavicle SWINGS the arm root; it does not roll with it,
        /// and that is a statement about the quaternion that reaches the bone.
        /// </summary>
        [Test]
        public void Girdle_IsClamped_TwistFree_AndEveryContributorEngages()
        {
            var log = new StringBuilder();
            float worstApplied = 0f, worstTwist = 0f, worstReported = 0f;
            int shrugFired = 0, retractFired = 0, clampBound = 0, coupled = 0, total = 0;
            float mostShrug = 0f, mostRetract = 0f;

            foreach (bool left in new[] { false, true })
            foreach (bool elbow in new[] { false, true })
            foreach (float az in new[] { 0f, 40f, 90f, 140f, 180f, 230f, 275f, 320f })
            foreach (float el in new[] { -89f, -75f, -55f, -25f, 5f, 40f, 70f, 88f })
            foreach (float reach in new[] { 0.55f, 0.75f, 0.90f, 0.99f })
            {
                Spec s = Default();
                s.IsLeft = left;
                s.HasElbow = elbow;
                s.ArmDir = Dir(az, el, left);
                s.Reach = reach;
                s.ElbowFrac = reach;

                BasisShoulderSolveInput i = Build(s);
                BasisShoulderSolveCore.Solve(i, out BasisShoulderSolveResult r);
                if (!r.Apply) continue;
                total++;

                MeasureGirdle(i, r, out float applied, out float twist);
                worstApplied = Mathf.Max(worstApplied, applied);
                worstTwist = Mathf.Max(worstTwist, twist);
                worstReported = Mathf.Max(worstReported, Mathf.Abs(applied - r.AppliedAngleDeg));

                if (r.ShrugDeg > 0.5f) { shrugFired++; mostShrug = Mathf.Max(mostShrug, r.ShrugDeg); }
                if (r.RetractDeg > 0.5f) { retractFired++; mostRetract = Mathf.Max(mostRetract, r.RetractDeg); }
                if (applied > MaxDeg - 0.5f) clampBound++;
                if (r.ComputedWeight > 0.05f) coupled++;
            }

            log.AppendLine($"      {total} poses: shrug fired {shrugFired} (peak {mostShrug:F1} deg), retraction fired " +
                           $"{retractFired} (peak {mostRetract:F1} deg), clamp bound on {clampBound}, coupled swing engaged on {coupled}");
            log.AppendLine($"      worst applied {worstApplied:F2} deg (clamp {MaxDeg:F0}), worst measured twist leak {worstTwist:F4} deg");

            Assert.That(total, Is.GreaterThan(500), "too few poses.");
            Assert.That(shrugFired, Is.GreaterThan(0),
                "the SHRUG never engaged anywhere in a full sphere of arm directions at four reaches. It is gated on " +
                "the arm HANGING and on a reach deficit, and it is deliberately not scaled by `engage` because a shrug " +
                "has zero humeral swing -- if it never fires, the gesture the core exists to detect is undetectable.");
            Assert.That(retractFired, Is.GreaterThan(0),
                "the scapular RETRACTION never engaged. Real glenohumeral horizontal extension runs out at ~45-60 deg " +
                "posterior; without the girdle a 90 deg posterior target puts ~87 deg on the humerus alone.");
            Assert.That(clampBound, Is.GreaterThan(0),
                $"the {MaxDeg:0} deg girdle clamp never bound, so the bound below is asserting nothing.");
            Assert.That(coupled, Is.GreaterThan(50), "the coupled swing barely engaged.");

            Assert.That(worstApplied, Is.LessThan(MaxDeg + 0.5f),
                $"the girdle rotated {worstApplied:0.00} deg, past its {MaxDeg:0} deg clamp -- measured from the " +
                "quaternion that actually reaches the clavicle, not from the core's own bookkeeping.");
            Assert.That(worstTwist, Is.LessThan(0.5f),
                $"the applied girdle rotation carries {worstTwist:0.000} deg of TWIST about the arm axis. The clavicle " +
                "swings the arm root; it does not roll with it (a twist-following clavicle was tried and reverted).");
            Assert.That(worstReported, Is.LessThan(0.5f),
                $"AppliedAngleDeg disagrees with the rotation actually applied to the clavicle by {worstReported:0.000} deg.");

            TestContext.WriteLine("\n  girdle bounds and engagement:\n" + log);
        }

        /// <summary>
        /// ⭐ elbowTrust FAILS OPEN, AND WHICH WAY A GATE FAILS ON MISSING DATA IS A DESIGN DECISION THAT
        /// DESERVES A TEST. TposeElbowLength == 0 means "the caller did not bake one", NOT "the tracker is
        /// wrong": starting from zero trust would read absence as maximum doubt and collapse the whole solve
        /// to the hand-path reach gate for every caller that omits it. Both halves are asserted -- absent
        /// data trusts, and BAD data (an elbow tracker far past where an elbow can be) does not.
        /// </summary>
        [Test]
        public void ElbowTrust_FailsOpenOnAbsentData_AndClosesOnImplausibleData()
        {
            // A folded arm: the HAND is close to the body, so the hand-path reach gate is shut and only the
            // elbow driver can move the girdle. That makes the difference visible.
            Spec s = Default();
            s.HasElbow = true;
            s.ArmDir = Dir(0f, 60f, false);   // elbow up
            s.Reach = 0.30f;                  // hand close: reachEngage == 0
            s.ElbowFrac = 1.0f;

            BasisShoulderSolveInput baked = Build(s);
            BasisShoulderSolveInput absent = baked; absent.TposeElbowLength = 0f;
            BasisShoulderSolveInput implausible = baked;
            implausible.ElbowPos = baked.ShoulderPos + (baked.ElbowPos - baked.ShoulderPos) * 2.2f;  // way past any elbow

            BasisShoulderSolveCore.Solve(baked, out BasisShoulderSolveResult rBaked);
            BasisShoulderSolveCore.Solve(absent, out BasisShoulderSolveResult rAbsent);
            BasisShoulderSolveCore.Solve(implausible, out BasisShoulderSolveResult rBad);

            Assert.IsTrue(rBaked.Apply && rAbsent.Apply && rBad.Apply);

            Assert.That(rAbsent.ReachRatio, Is.GreaterThan(0.999f),
                $"with TposeElbowLength absent the elbow path was trusted only {rAbsent.ReachRatio:0.000}. Absence of a " +
                "baked length is not evidence the tracker is wrong -- reading it that way silently collapses every " +
                "caller that omits it (the offline sweeps and BasisShoulderDirectionTests drive the elbow path with " +
                "the hand AT the shoulder, so rawReach is 0 and the girdle would never move at all).");
            // The expected reach is cast from the REST clavicle, so an elbow at the full baked length already
            // overshoots it by ~28% -- inside the ~19% + band the trust window deliberately leaves for live
            // girdle rotation plus its own smoothstep shoulder. Trust must stay high, not be exactly 1.
            Assert.That(rBaked.ReachRatio, Is.GreaterThan(0.9f),
                $"a correctly-placed elbow was trusted only {rBaked.ReachRatio:0.000}.");
            Assert.That(rBad.ReachRatio, Is.LessThan(0.35f),
                $"an elbow tracker at 2.2x the plausible distance was still trusted {rBad.ReachRatio:0.000}. Only the FAR " +
                "side is gated -- a shrug can only bring the elbow CLOSER -- so this is the side that must close.");
            Assert.That(rBad.AppliedAngleDeg, Is.LessThan(rBaked.AppliedAngleDeg),
                $"an implausible elbow moved the girdle {rBad.AppliedAngleDeg:0.0} deg, no less than a plausible one " +
                $"({rBaked.AppliedAngleDeg:0.0} deg).");

            TestContext.WriteLine($"  elbow trust: baked {rBaked.ReachRatio:0.000}, absent {rAbsent.ReachRatio:0.000} " +
                                  $"(fails OPEN), implausible {rBad.ReachRatio:0.000} (closes).");
        }

        // ============================================================================================
        // 5. DETERMINISM AND DEGENERATE INPUTS
        // ============================================================================================

        [Test]
        public void ShoulderSolve_IsDeterministic_AndSurvivesEveryDegenerateInput()
        {
            var findings = new List<string>();
            var zeroQuat = new List<string>();

            Spec s = Default();
            s.ArmDir = Dir(35f, -40f, false);
            s.ChestExtra = Quaternion.Euler(8f, -14f, 5f);
            BasisShoulderSolveInput baseline = Build(s);

            BasisShoulderSolveCore.Solve(baseline, out BasisShoulderSolveResult r0);
            for (int k = 0; k < 4; k++)
            {
                BasisShoulderSolveCore.Solve(baseline, out BasisShoulderSolveResult again);
                Assert.IsTrue(BasisArmNet.Same(r0.ShoulderRotation, again.ShoulderRotation)
                              && r0.AppliedAngleDeg == again.AppliedAngleDeg
                              && r0.ShrugDeg == again.ShrugDeg && r0.RetractDeg == again.RetractDeg,
                    "the shoulder solve is not deterministic.");
            }

            var cases = new List<(string, Func<BasisShoulderSolveInput, BasisShoulderSolveInput>)>
            {
                ("driver exactly AT the shoulder", i => { i.ElbowPos = i.ShoulderPos; i.HandTargetPos = i.ShoulderPos; return i; }),
                ("elbow exactly AT the shoulder", i => { i.ElbowPos = i.ShoulderPos; return i; }),
                ("hand exactly AT the shoulder", i => { i.HandTargetPos = i.ShoulderPos; return i; }),
                ("zero TposeArmDirWorld", i => { i.TposeArmDirWorld = Vector3.zero; return i; }),
                ("zero TposeArmLength", i => { i.TposeArmLength = 0f; return i; }),
                ("negative TposeArmLength", i => { i.TposeArmLength = -1f; return i; }),
                ("zero clavicle length", i => { i.TposeClavicleLength = 0f; return i; }),
                ("clavicle longer than the whole arm", i => { i.TposeClavicleLength = 10f; return i; }),
                ("zero elbow length", i => { i.TposeElbowLength = 0f; return i; }),
                ("zero ChestRot", i => { i.ChestRot = default; return i; }),
                ("zero TposeChestRot", i => { i.TposeChestRot = default; return i; }),
                ("zero TposeShoulderRot", i => { i.TposeShoulderRot = default; return i; }),
                ("zero ChestBind", i => { i.ChestBind = default; return i; }),
                ("zero TrackerFinal with a tracker", i => { i.HasShoulderTracker = true; i.TrackerFinal = default; return i; }),
                ("MaxShoulderDeg = 0", i => { i.MaxShoulderDeg = 0f; return i; }),
                ("MaxShoulderDeg negative", i => { i.MaxShoulderDeg = -25f; return i; }),
                ("negative sliders", i => { i.ElevationFactor = -1f; i.ProtractionFactor = -1f; i.CoupleRatio = -1f; return i; }),
                ("huge sliders", i => { i.ElevationFactor = 50f; i.ProtractionFactor = 50f; i.CoupleRatio = 50f; return i; }),
                ("arm exactly ANTI-PARALLEL to rest (the FromToRotation seam)", i =>
                {
                    Vector3 rest = i.TposeArmDirWorld.normalized;
                    i.ElbowPos = i.ShoulderPos - rest * 0.4f;
                    i.HandTargetPos = i.ShoulderPos - rest * 0.55f; return i;
                }),
                ("arm exactly PARALLEL to rest", i =>
                {
                    Vector3 rest = i.TposeArmDirWorld.normalized;
                    i.ElbowPos = i.ShoulderPos + rest * 0.4f;
                    i.HandTargetPos = i.ShoulderPos + rest * 0.55f; return i;
                }),
                ("a fully default input struct", _ => default),
            };

            foreach (var c in cases)
            {
                BasisShoulderSolveInput i = c.Item2(baseline);
                BasisShoulderSolveResult r;
                try { BasisShoulderSolveCore.Solve(i, out r); }
                catch (Exception ex) { findings.Add($"[{c.Item1}] THREW {ex.GetType().Name}: {ex.Message}"); continue; }

                if (!r.Apply) continue;   // declining is a perfectly good answer to nonsense

                if (!BasisArmNet.Finite(r.ShoulderRotation))
                    findings.Add($"[{c.Item1}] produced a NON-FINITE clavicle rotation ({r.ShoulderRotation}). A NaN " +
                                 "transform PERSISTS in Unity: the shoulder never recovers, even once good data returns.");
                else if (!BasisArmNet.Unit(r.ShoulderRotation))
                    zeroQuat.Add($"[{c.Item1}] -> {r.ShoulderRotation}");
                if (!BasisArmNet.Finite(r.AppliedAngleDeg) || !BasisArmNet.Finite(r.Elevation)
                    || !BasisArmNet.Finite(r.Protraction) || !BasisArmNet.Finite(r.ShrugDeg)
                    || !BasisArmNet.Finite(r.RetractDeg) || !BasisArmNet.Finite(r.SwingAngleDeg)
                    || !BasisArmNet.Finite(r.ReachRatio) || !BasisArmNet.Finite(r.ComputedWeight))
                    findings.Add($"[{c.Item1}] produced a NaN diagnostic.");

                if (i.MaxShoulderDeg > 0f && BasisArmNet.Finite(r.AppliedAngleDeg) && BasisArmNet.Unit(r.ShoulderRotation))
                {
                    MeasureGirdle(i, r, out float applied, out _);
                    if (applied > i.MaxShoulderDeg + 0.5f)
                        findings.Add($"[{c.Item1}] rotated the girdle {applied:0.0} deg past its {i.MaxShoulderDeg:0} deg clamp.");
                }
            }

            BasisArmNet.Report(findings, null, $"shoulder degenerate-input hygiene over {cases.Count} cases");
            TestContext.WriteLine($"  {cases.Count} degenerate shoulder inputs: deterministic, no NaN, clamp held.");

            if (zeroQuat.Count > 0)
            {
                BasisArmNet.KnownOpen(
                    "BasisShoulderSolveCore must never report Apply == true alongside a clavicle rotation that is " +
                    "not a rotation. It writes ShoulderRotation straight to the bone, and a zero quaternion " +
                    "collapses the transform exactly as a NaN would -- Quaternion.Inverse(zero) is zero, and the " +
                    "zero propagates through every product to the output with Apply still true",
                    $"{zeroQuat.Count} case(s): " + string.Join("; ", zeroQuat) +
                    ". Each is a rig whose chest or clavicle bind was never baked. Fix shape: the same " +
                    "`Quaternion.Dot(q, q) > eps` decline the core already applies to ChestBind, applied to " +
                    "ChestRot / TposeChestRot / TposeShoulderRot / TrackerFinal as well.");
            }
        }
    }
}
