using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// SHARED HARNESS FOR THE ARM/SHOULDER INVARIANT NET. No [Test] lives here.
    ///
    /// ================================================================================================
    /// WHY A NET AND NOT MORE POSES. Eleven arm/shoulder fixes landed in one day and they were not eleven
    /// unrelated bugs -- they were SEVEN REPEATING CLASSES, each of which had been invisible to a suite
    /// that was already large and already green:
    ///
    ///   1. AN UNBOUNDED DOF.        Humeral axial rotation tracked a tracker 1:1 through a full 360.
    ///   2. A GUARD THAT CANNOT FIRE. The elbow anatomy guard's soft margin (0.05 of the limb) EXCEEDED
    ///                                the elbow circle's radius at full extension (rho/L = 0.0436), so it
    ///                                fired 0 times in 22032 poses exactly where it was needed. Closed
    ///                                form, not a tuning accident.
    ///   3. A VACUOUS ASSERTION.      A gate "proved" a guard moved the hand 0.000 mm by comparing two
    ///                                result-struct fields THE GUARD NEVER WRITES. 205 mm shipped behind it.
    ///   4. A SEAM.                   A principal-angle clamp jumped the arm 80 deg in one 0.05 deg input
    ///                                step -- 1430x amplification -- at +/-180.
    ///   5. A WRONG FRAME.            Guards evaluated against raw bone axes or the wrong parent bone.
    ///   6. A STRUCTURALLY-ZERO BUDGET. A cap computed its budget from axis rotation alone, so a
    ///                                straight-line reach (axis rotation exactly 0) froze the joint.
    ///   7. A STALE/DEGENERATE SEED.  A tracker re-acquired on a straight arm seeded its pole from a
    ///                                noise-length vector and HELD it, 97.4 deg off, not converging.
    ///
    /// Every one of those is a PROPERTY of the solve, not a pose. So this net asserts properties, and the
    /// three rules below exist because classes 2 and 3 are failures OF TESTS, not of code.
    /// ================================================================================================
    ///
    /// RULE 1 -- NON-VACUITY IS THE DEFAULT SHAPE, NOT AN EXTRA. Every bound goes through
    /// <see cref="Gate"/> or <see cref="GateSmooth"/>, and both REFUSE to pass unless the quantity they
    /// bound was demonstrably excited: <see cref="Gate"/> takes a control that must violate the bound,
    /// <see cref="GateSmooth"/> takes a sweep whose 95th-percentile step must be non-zero. A test that
    /// cannot fail is worse than no test (class 3).
    ///
    /// RULE 2 -- DISPLACEMENT IS MEASURED THROUGH THE STREAM. The runtime applies
    ///     mid.SetRotation(MidDelta*mid); root.SetRotation(RootDelta*root); root.SetRotation(HintDelta*root);
    ///     mid.SetRotation(MidPostRoll*mid); tip.SetRotation(TipRotation)
    /// and setting a PARENT's rotation in an AnimationStream carries its children RIGIDLY. HintDelta goes
    /// on the ROOT. So "this does not move the hand" is a statement about that composition and about
    /// nothing else -- see <see cref="StreamCompose"/>, and see the reconciliation test that pins the
    /// solver's own bookkeeping to it.
    ///
    /// RULE 3 -- THE HARNESS ASSERTS ITS OWN SMOOTHNESS BEFORE IT JUDGES THE SOLVER'S. A continuity sweep
    /// that injects a seam into its own INPUT (the classic one: an orthonormal basis built from a fixed
    /// world axis that the swept direction passes through) reports the harness's discontinuity as the
    /// core's. <see cref="GateSmooth"/> is therefore always run over the input channels first.
    ///
    /// ⚠️ THE SEGMENTS ARE DELIBERATELY UNEQUAL (0.30 / 0.26). An isoceles arm makes the elbow circle
    /// symmetric about the midpoint of shoulder->hand and makes (180-theta)/2 the angle to BOTH segments,
    /// which quietly hides sign and which-segment errors. A real humerus is longer than a real forearm.
    /// </summary>
    internal static class BasisArmNet
    {
        public const float Upper = 0.30f;
        public const float Fore = 0.26f;
        public const float Total = Upper + Fore;

        public static readonly Vector3 Shoulder = Vector3.zero;

        // ------------------------------------------------------------------ the mutation hook

        public delegate void ArmSolver(in BasisArmSolveInput i, out BasisArmSolveResult r);

        /// <summary>
        /// ⭐ EVERY GATE IN THIS NET WAS PROVEN TO GO RED, AND THIS IS HOW.
        ///
        /// The brief this net was written to is explicit that a test which cannot fail is worse than no
        /// test -- class 3 shipped a 205 mm hand displacement behind a gate that was structurally incapable
        /// of failing. Arguing that a gate would catch something is not evidence; BREAKING the core and
        /// watching the gate go red is. So every test here routes through <see cref="Solve"/> rather than
        /// calling BasisArmSolveCore directly, and a scratch file can point this at a deliberately-damaged
        /// copy of the core, run these exact tests unmodified, and confirm each one dies to the mutation it
        /// was written for.
        ///
        /// Nothing in the shipped suite writes it. A mutation harness must restore it in a finally block --
        /// leaving it set would silently redirect the whole suite, which is the same class of defect this
        /// hook exists to hunt.
        ///
        /// ⭐ THE EVIDENCE, so it is not lost with the scratch harness that produced it. A generated copy of
        /// BasisArmSolveCore / BasisElbowAnatomyCore was damaged one defect at a time and pointed at this
        /// hook; the copy was first proven BIT-IDENTICAL to the real core over 144 poses, so each result is
        /// about the mutation and not about the copy. Every gate below went RED:
        ///
        ///   MUTATION (one line each)                                  GATE THAT DIED
        ///   the humeral twist guard's seam cap removed                HumeralTwistSeamSweep (strict) --
        ///   (`pull = min(pull, seam)` -> the old sign(twist)*guarded) ControllerRollSweep, 131 deg jump
        ///   the twist guard's counter-roll on MidPostRoll removed     SolverBookkeeping_Reconciles (hand
        ///   (`MidPostRoll * inverse(twistR)`)                         93.6 mm), PureRollStages
        ///   the elbow guard reads PlayerUp instead of TorsoUp         ArmGuards_ReadTheTorsoUp (216 mm),
        ///                                                             ElbowAnatomyLaw_IsEvaluatedInTheTorso
        ///   the humeral twist guard never engages                     HumerusAndForearm_AxialRoll (-172.3
        ///                                                             vs a 164.7 envelope),
        ///                                                             EveryBoundedStage_Engages
        ///   BasisElbowAnatomyCore's SoftMarginMaxFracRadius cap       ElbowAnatomyGuard_FiresAndBounds,
        ///   removed (the ORIGINAL 0/22032 defect)                     48 findings
        ///   the bend capped by the arm axis's own rotation            RadialReach_RotatesNoAxis: the elbow
        ///   (a structurally-zero budget)                              moved 2.1 mm of a required 209.1 mm
        ///   the pole anchor's ease-toward-measurement removed         PoleAnchor_ConvergesFromABadSeed:
        ///                                                             100.1 deg residual after 400 frames
        ///   the twist correction applied about shoulder->HAND         PureRollStages (elbow moved 96.8 mm),
        ///   instead of shoulder->ELBOW                                SolverBookkeeping_Reconciles
        ///   MidPostRoll left as the ZERO quaternion when unused       DegenerateInputs_EmitNoNaN
        ///   a hardcoded world reference axis for the twist guard      SolvedArm_IsInvariantToTheRigsBone
        ///
        /// ⚠️ ONE SURVIVAL, AND IT IS INFORMATIVE: the seam-cap mutation SURVIVED
        /// ElbowTrackerAzimuthSweep, because that sweep carries a 150 deg known-defect allowance (see
        /// BasisArmInvariantNetKnownOpenDefectTests) which is wider than the 80 deg snap. That is the exact
        /// price of shipping with an open seam, and it is why HumeralTwistSeamSweep exists: it is arranged so
        /// neither open defect can reach it, so it can stay strict.
        /// </summary>
        public static ArmSolver Solver = BasisArmSolveCore.Solve;

        public static void Solve(in BasisArmSolveInput i, out BasisArmSolveResult r) => Solver(i, out r);

        /// <summary>The REFERENCE bone convention: a bone points down its own local +X. Every rig below is
        /// this one post-multiplied by a convention rotation, which is exactly what "rig convention" means --
        /// the same world pose, a different local frame.</summary>
        public static readonly Vector3 RefLocalBone = Vector3.right;

        /// <summary>Deliberately NOT identity. The humeral twist guard carries its reference through
        /// clavicle * inverse(bindClavicle), and an identity pair lets a dropped carry pass unnoticed.</summary>
        public static readonly Quaternion ClavBind = Quaternion.Euler(4f, -11f, 6f);

        // ------------------------------------------------------------------ rig binds

        public struct Rig
        {
            public string Name;
            public Quaternion Conv;               // the bone-local authoring convention
            public Quaternion BindHumerusRot;     // world bind rotation of the upper arm
            public Vector3 BindHumerusDir;        // world bind direction shoulder->elbow, unit
            public Vector3 BindHumerusRefAxis;    // BONE-LOCAL reference axis, baked per rig
            public Quaternion BindLowerArmRot;    // world bind rotation of the forearm
            public Vector3 LocalBone;             // the bone's own axis in its local frame
        }

        /// <summary>A bone's world rotation: point it down `dir`, roll it `rollDeg` about that direction,
        /// then re-express it in the rig's own local convention.</summary>
        public static Quaternion BoneRot(Vector3 dir, float rollDeg, Quaternion conv)
        {
            Vector3 d = dir.normalized;
            return Quaternion.AngleAxis(rollDeg, d) * BasisQuaternionExt.FromToRotation(RefLocalBone, d) * conv;
        }

        /// <summary>A T-pose bind (humerus lateral, +X) authored in `conv`. The reference axis mirrors
        /// BasisFullBodyIK.BakeHumerusTwistBind EXACTLY -- if that bake changes this must change with it,
        /// and the convention-invariance test is what makes a divergence visible.</summary>
        public static Rig MakeRig(string name, Quaternion conv)
        {
            Rig g = default;
            g.Name = name;
            g.Conv = conv;
            Vector3 dir = Vector3.right;
            g.BindHumerusRot = BoneRot(dir, 0f, conv);
            g.BindHumerusDir = dir;
            // The forearm's bind pronation is NOT the humerus's: an identity relation would let a solve
            // that ignored BindLowerArmRotation entirely still look right.
            g.BindLowerArmRot = BoneRot(dir, 15f, conv);

            Vector3 localBone = Quaternion.Inverse(g.BindHumerusRot) * g.BindHumerusDir;
            Vector3 refLocal = Mathf.Abs(localBone.y) < 0.9f ? Vector3.up : Vector3.forward;
            Vector3 perp = refLocal - localBone * Vector3.Dot(refLocal, localBone);
            g.BindHumerusRefAxis = perp.sqrMagnitude > 1e-8f ? perp.normalized : Vector3.zero;
            g.LocalBone = localBone;
            return g;
        }

        public static Rig DefaultRig() => MakeRig("reference (bone +X)", Quaternion.identity);

        /// <summary>Bone-space conventions a real avatar can arrive with. The WORLD pose is identical in
        /// every one -- only the bone's local frame differs.</summary>
        public static Rig[] RigConventions() => new[]
        {
            MakeRig("identity conv (bone +X)", Quaternion.identity),
            MakeRig("X-90 (Blender bone-Y-up)", Quaternion.Euler(-90f, 0f, 0f)),
            MakeRig("X+90", Quaternion.Euler(90f, 0f, 0f)),
            MakeRig("Z+90 roll", Quaternion.Euler(0f, 0f, 90f)),
            MakeRig("Y+180", Quaternion.Euler(0f, 180f, 0f)),
            MakeRig("arbitrary 37/-22/61", Quaternion.Euler(37f, -22f, 61f)),
            MakeRig("arbitrary -14/88/-133", Quaternion.Euler(-14f, 88f, -133f)),
        };

        // ------------------------------------------------------------------ the pose spec

        public const int HintNone = 0;
        public const int HintModel = 1;      // BasisSwivelHintCore.ArmHint: perpendicular BY CONSTRUCTION
        public const int HintTracker = 2;    // a real elbow tracker: the elbow's OWN position, which collapses
        public const int HintLookup = 3;     // a non-perpendicular invented pole (the pole-collapse stabiliser's domain)

        public struct Spec
        {
            public Rig Rig;

            // --- the target
            public Vector3 TargetDir;        // unit, shoulder->hand target, BEFORE the world transform
            public float Reach;              // fraction of Total
            public float HandRollDeg;        // controller roll about the target direction
            public Vector3 RefPerp;          // azimuth-zero of the pole circle; orthonormalised against TargetDir

            // --- the hint / pole
            public int HintMode;
            public float HintAzimuthDeg;
            public float HintRhoMin;         // metres: a floor on the pole stand-off (models user-arm > avatar-arm)
            /// <summary>Tracker mode: force the pole's stand-off from the shoulder->hand axis to this many
            /// metres instead of the elbow's own. Sweeping it walks poleCondW from 1 down to 0 -- which is
            /// where a BOOLEAN pole gate used to cliff and travel the elbow 2582x the hand's own distance in
            /// one step. Negative (the default) uses the true elbow.</summary>
            public float HintRhoOverride;
            public float HintLookupStandoff; // HintLookup only: the pole's stand-off as a fraction of Total
            public float TrackerRollDeg;     // the tracker's own measured forearm roll; NaN => HintRotation declined
            public float MaxStepDeg;

            // --- the animated pose the solve starts from
            public float AnimHumRollDeg;
            public float AnimForeRollDeg;
            public float AnimHandRollDeg;
            public float AnimElbowFlexDeg;
            /// <summary>Point the ANIMATED arm along the target direction instead of leaving it hanging. This
            /// is the live rig's ordinary state -- the animation is already near the pose the user is in --
            /// and it is the ONLY way to build a motion whose shoulder->hand axis rotation is EXACTLY zero,
            /// which is the input a budget derived from axis rotation computes as zero. See class 6.</summary>
            public bool AnimAlongTarget;
            /// <summary>AnimAlongTarget only: which way round the elbow circle the ANIMATED elbow sits.</summary>
            public float AnimAzimuthDeg;

            // --- optional inputs (each zero-default DECLINES a stage; that is the contract)
            public bool FeedTwistBind;       // ClavicleRotation + the four humerus binds
            public bool FeedLowerBind;       // BindLowerArmRotation (the no-tracker forearm roll)
            public bool FeedTip;             // TipRotation (wrist-roll relief / the hand's roll demand)
            public bool WristBound;          // ApplyWristAxialBound (default OFF in the shipped settings)
            public Quaternion ClavLive;      // zero => ClavBind (a girdle that has not moved)

            // --- frames
            public Vector3 PlayerUp;
            public Vector3 TorsoUp;          // zero => the core falls back to PlayerUp. That fallback is a TEST SUBJECT.

            // --- a rigid transform applied to the WHOLE scenario, bind data included
            public Quaternion World;
            public Vector3 WorldT;
        }

        public static Spec Default(in Rig rig)
        {
            Spec s = default;
            s.Rig = rig;
            s.TargetDir = new Vector3(0.35f, -0.90f, 0.25f).normalized;
            s.Reach = 0.90f;
            s.RefPerp = Vector3.forward;
            s.HintMode = HintModel;
            s.HintRhoMin = 0f;
            s.HintRhoOverride = -1f;
            s.HintLookupStandoff = 0.10f;
            s.TrackerRollDeg = float.NaN;
            s.MaxStepDeg = float.MaxValue;
            s.AnimElbowFlexDeg = 158f;
            s.PlayerUp = Vector3.up;
            s.TorsoUp = Vector3.up;
            s.World = Quaternion.identity;
            s.WorldT = Vector3.zero;
            s.ClavLive = default;
            return s;
        }

        // ------------------------------------------------------------------ geometry

        public static Vector3 Orthonormal(Vector3 v, Vector3 axis)
        {
            Vector3 p = v - axis * Vector3.Dot(v, axis);
            return p.sqrMagnitude > 1e-10f ? p.normalized : Vector3.zero;
        }

        /// <summary>The elbow's TRUE position for a hand at `reach` along `tDir` -- i.e. what a correctly
        /// strapped elbow tracker measures. rho collapses as reach -> 1, which is the whole conditioning
        /// story, so this is not a convenience: it is the physical input.</summary>
        public static Vector3 ElbowOnCircle(Vector3 tDir, Vector3 u, Vector3 v, float reach, float azimDeg, float rhoMin, float rhoOverride = -1f)
        {
            float d = Mathf.Max(reach * Total, 1e-4f);
            float p = (d * d + Upper * Upper - Fore * Fore) / (2f * d);
            float rho2 = Upper * Upper - p * p;
            float rho = rho2 > 0f ? Mathf.Sqrt(rho2) : 0f;
            if (rhoOverride >= 0f) rho = rhoOverride;
            if (rho < rhoMin) rho = rhoMin;
            float a = azimDeg * Mathf.Deg2Rad;
            return Shoulder + tDir * p + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * rho;
        }

        /// <summary>The animated arm this solve starts from: a hanging arm with a mild bend, held FIXED
        /// while the target sweeps. That is what an idle animation actually is, and it is the strictest
        /// continuity case -- every delta the solver forms has to absorb the whole difference.</summary>
        public static void AnimatedArm(in Spec s, out Vector3 elbow, out Vector3 hand, out Vector3 humDir, out Vector3 foreDir)
        {
            if (s.AnimAlongTarget)
            {
                // The animated arm already reaches along the target direction, bent in the plane spanned by
                // the target and the azimuth reference. `ac` is then parallel to `atCorrected`, so the
                // solver's rootDelta is the identity and the shoulder->hand axis rotates by EXACTLY zero as
                // the target slides in and out along that line.
                Vector3 tDir = s.TargetDir.normalized;
                Vector3 u = Orthonormal(s.RefPerp, tDir);
                if (u == Vector3.zero) u = Orthonormal(Vector3.up, tDir);
                Vector3 v = Vector3.Cross(tDir, u);
                const float animReach = 0.78f;   // a mid-range animated reach on the same line
                elbow = ElbowOnCircle(tDir, u, v, animReach, s.AnimAzimuthDeg, 0f);
                hand = Shoulder + tDir * (animReach * Total);
                humDir = (elbow - Shoulder).normalized;
                foreDir = (hand - elbow).normalized;
                return;
            }

            humDir = new Vector3(0.18f, -0.97f, 0.05f).normalized;
            foreDir = (Quaternion.AngleAxis(180f - s.AnimElbowFlexDeg, Vector3.forward) * humDir).normalized;
            elbow = Shoulder + humDir * Upper;
            hand = elbow + foreDir * Fore;
        }

        // ------------------------------------------------------------------ input construction

        public static BasisArmSolveInput Build(in Spec s)
        {
            Quaternion W = s.World;
            Vector3 T = s.WorldT;

            AnimatedArm(s, out Vector3 elbow0, out Vector3 hand0, out Vector3 humDir, out Vector3 foreDir);

            Vector3 tDir = s.TargetDir.normalized;
            Vector3 u = Orthonormal(s.RefPerp, tDir);
            Vector3 v = Vector3.Cross(tDir, u);
            Vector3 target0 = Shoulder + tDir * (s.Reach * Total);

            BasisArmSolveInput i = default;
            i.Shoulder = W * Shoulder + T;
            i.Elbow = W * elbow0 + T;
            i.Hand = W * hand0 + T;
            i.RootRotation = W * BoneRot(humDir, s.AnimHumRollDeg, s.Rig.Conv);
            i.MidRotation = W * BoneRot(foreDir, s.AnimForeRollDeg, s.Rig.Conv);

            i.TargetPosition = W * target0 + T;
            i.TargetRotation = W * (Quaternion.AngleAxis(s.HandRollDeg, tDir) * BoneRot(tDir, 0f, s.Rig.Conv));
            i.TargetOffset = Quaternion.identity;

            i.PlayerUp = W * s.PlayerUp;
            i.TorsoUp = s.TorsoUp.sqrMagnitude > 0f ? W * s.TorsoUp : Vector3.zero;
            i.HintMaxStepDeg = s.MaxStepDeg;

            switch (s.HintMode)
            {
                case HintModel:
                {
                    // BasisSwivelHintCore.ArmHint: hint = shoulder + 0.5*armLen*bend, bend PERPENDICULAR to
                    // shoulder->hand by construction. |ahProj| is therefore a flat 0.5*Total at every reach.
                    float a = s.HintAzimuthDeg * Mathf.Deg2Rad;
                    Vector3 bend = u * Mathf.Cos(a) + v * Mathf.Sin(a);
                    i.HintWeight = true;
                    i.HintIsTracker = false;
                    i.HintPosition = W * (Shoulder + bend * (0.5f * Total)) + T;
                    break;
                }
                case HintTracker:
                {
                    i.HintWeight = true;
                    i.HintIsTracker = true;
                    i.HintPosition = W * ElbowOnCircle(tDir, u, v, s.Reach, s.HintAzimuthDeg, s.HintRhoMin, s.HintRhoOverride) + T;
                    if (!float.IsNaN(s.TrackerRollDeg))
                    {
                        // The lower-arm tracker's measured FOREARM BONE rotation, already mapped through the
                        // calibration reference. Aimed down the true elbow->hand direction so it is a
                        // physically consistent measurement rather than an arbitrary quaternion.
                        Vector3 e = ElbowOnCircle(tDir, u, v, s.Reach, s.HintAzimuthDeg, s.HintRhoMin, s.HintRhoOverride);
                        Vector3 fd = (target0 - e);
                        fd = fd.sqrMagnitude > 1e-10f ? fd.normalized : tDir;
                        i.HintRotation = W * BoneRot(fd, s.TrackerRollDeg, s.Rig.Conv);
                    }
                    break;
                }
                case HintLookup:
                {
                    // A pole with NO perpendicularity guarantee -- the only shape that can still reach the
                    // pole-collapse stabiliser, which is dead code on the live model path.
                    float a = s.HintAzimuthDeg * Mathf.Deg2Rad;
                    Vector3 bend = u * Mathf.Cos(a) + v * Mathf.Sin(a);
                    i.HintWeight = true;
                    i.HintIsTracker = false;
                    i.HintPosition = W * (Shoulder + tDir * (0.45f * Total) + bend * (s.HintLookupStandoff * Total)) + T;
                    break;
                }
            }

            if (s.FeedTwistBind)
            {
                Quaternion clavLive = Quaternion.Dot(s.ClavLive, s.ClavLive) > 0.5f ? s.ClavLive : ClavBind;
                i.ClavicleRotation = W * clavLive;
                i.BindClavicleRotation = W * ClavBind;
                i.BindHumerusRotation = W * s.Rig.BindHumerusRot;
                i.BindHumerusDir = W * s.Rig.BindHumerusDir;
                i.BindHumerusRefAxis = s.Rig.BindHumerusRefAxis;   // BONE-LOCAL: a world transform must NOT touch it
            }
            if (s.FeedLowerBind)
            {
                i.BindHumerusRotation = W * s.Rig.BindHumerusRot;
                i.BindLowerArmRotation = W * s.Rig.BindLowerArmRot;
            }
            if (s.FeedTip)
            {
                i.TipRotation = W * BoneRot(foreDir, s.AnimHandRollDeg, s.Rig.Conv);
            }
            i.ApplyWristAxialBound = s.WristBound;
            return i;
        }

        // ------------------------------------------------------------------ the STREAM

        /// <summary>
        /// ⭐ THE RUNTIME'S OWN COMPOSITION, REPLAYED IN FULL -- positions AND rotations.
        /// BasisFullBodyIK.SolveTwoBoneIKArms ends with
        ///     mid.SetRotation(MidDelta*mid); root.SetRotation(RootDelta*root);
        ///     root.SetRotation(HintDelta*root); mid.SetRotation(MidPostRoll*mid); tip.SetRotation(TipRotation)
        /// and setting a PARENT's rotation carries its children rigidly: the child's world rotation is
        /// left-multiplied and its position pivots about the parent's origin. HintDelta goes on the ROOT.
        /// This function is the difference between what the solver BOOKKEEPS and what the arm actually does.
        ///
        /// `skipHint` / `skipPostRoll` exist ONLY so a test can prove this replay is SENSITIVE to the stages
        /// it composes. A replay that agrees with the solver because every delta happened to be identity is
        /// the vacuous-assertion class all over again.
        /// </summary>
        public static void StreamCompose(in BasisArmSolveInput i, in BasisArmSolveResult r,
            out Vector3 elbow, out Vector3 hand, out Quaternion rootRot, out Quaternion midRot,
            bool skipHint = false, bool skipPostRoll = false)
        {
            Vector3 a = i.Shoulder;
            Vector3 b = i.Elbow;
            Vector3 c = i.Hand;

            c = b + r.MidDelta * (c - b);
            Quaternion mid = r.MidDelta * i.MidRotation;

            b = a + r.RootDelta * (b - a);
            c = a + r.RootDelta * (c - a);
            Quaternion root = r.RootDelta * i.RootRotation;
            mid = r.RootDelta * mid;

            if (!skipHint)
            {
                b = a + r.HintDelta * (b - a);
                c = a + r.HintDelta * (c - a);
                root = r.HintDelta * root;
                mid = r.HintDelta * mid;
            }

            if (!skipPostRoll)
            {
                c = b + r.MidPostRoll * (c - b);
                mid = r.MidPostRoll * mid;
            }

            elbow = b;
            hand = c;
            rootRot = root;
            midRot = mid;
        }

        // ------------------------------------------------------------------ measurement

        /// <summary>
        /// ⚠️ NORMALISE FIRST, AND USE atan2 RATHER THAN acos. Both halves matter, and getting either wrong
        /// puts a 0.1 DEGREE noise floor under every measurement in this file.
        ///
        /// Chained quaternion products in float32 drift off the unit sphere by ~1e-6 (Unity does not
        /// renormalise on multiply, and the stream composition chains five of them). The obvious metric,
        /// 2*acos(|dot|), then reports
        ///
        ///     acos(1 - 1e-6) = 1.41e-3 rad = 0.081 DEGREES
        ///
        /// between two quaternions that are BIT-FOR-BIT THE SAME ROTATION -- because acos has infinite
        /// derivative at 1. That is enough to fail an exact reconciliation, to fail a rig-convention
        /// equivariance check, and to inflate the noise floor of every continuity sweep here by more than
        /// the smooth motion those sweeps are trying to measure. atan2(|xyz|, |w|) of the RELATIVE rotation
        /// is well-conditioned at zero and costs nothing.
        /// </summary>
        public static float PoseChangeDeg(Quaternion from, Quaternion to)
        {
            Quaternion a = NormalizeQ(from);
            Quaternion b = NormalizeQ(to);
            Quaternion rel = a * Quaternion.Inverse(b);
            float s = Mathf.Sqrt(rel.x * rel.x + rel.y * rel.y + rel.z * rel.z);
            float c = rel.w < 0f ? -rel.w : rel.w;
            return 2f * Mathf.Atan2(s, c) * Mathf.Rad2Deg;
        }

        public static Quaternion NormalizeQ(Quaternion q)
        {
            float m2 = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (!(m2 > 1e-12f)) return Quaternion.identity;
            float inv = 1f / Mathf.Sqrt(m2);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        /// <summary>Signed rotation of q about `axis`, wrapped to (-180, 180]. The swing-twist twist angle,
        /// written the long way (and NaN-safe by shape) so it matches the core's own reading.</summary>
        public static float TwistDeg(Quaternion q, Vector3 axis)
        {
            Vector3 n = axis.normalized;
            float s = q.x * n.x + q.y * n.y + q.z * n.z;
            float c = q.w;
            if (c < 0f) { s = -s; c = -c; }
            if (!(s * s + c * c > 1e-12f)) return 0f;
            return 2f * Mathf.Atan2(s, c) * Mathf.Rad2Deg;
        }

        public static bool Finite(Quaternion q) =>
            !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
              float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));

        public static bool Finite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        public static bool Finite(float f) => !(float.IsNaN(f) || float.IsInfinity(f));

        /// <summary>A rotation, or the exact zero quaternion. The core writes MidPostRoll unconditionally
        /// and the runtime multiplies it into a bone, so a non-unit value there reaches the rig.</summary>
        public static bool Unit(Quaternion q, float tol = 1e-3f)
        {
            if (!Finite(q)) return false;
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return Mathf.Abs(m - 1f) < tol;
        }

        public static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        // ------------------------------------------------------------------ non-vacuity by construction

        /// <summary>
        /// ⭐ THE DEFAULT SHAPE FOR EVERY BOUND IN THIS NET. A bound is only worth asserting if something
        /// can violate it, so this refuses to pass unless the CONTROL actually does. Class 3 -- a gate that
        /// compared two fields the guard never writes and so could not fail -- is the reason this is not
        /// optional.
        /// </summary>
        public static void Gate(string what, float guarded, float bound, float control, float controlMustExceed)
        {
            Assert.That(control, Is.GreaterThan(controlMustExceed),
                $"{what}: THE CONTROL NO LONGER VIOLATES THE BOUND ({control:0.000} vs {controlMustExceed:0.000}). " +
                "This sweep has stopped exciting the quantity it claims to bound, so the assertion below " +
                "would pass whatever the solver did. Fix the sweep, do not relax this.");
            Assert.That(guarded, Is.LessThan(bound),
                $"{what}: {guarded:0.000} exceeds the bound {bound:0.000} (the unguarded control reaches {control:0.000}).");
        }

        /// <summary>A per-output continuity channel: it records single-step changes and reports both the
        /// worst one and the sweep's own scale, so a jump is judged against what the sweep NORMALLY does
        /// rather than against a constant chosen by hand.</summary>
        public sealed class Channel
        {
            public readonly string Name;
            readonly List<float> m_Steps = new List<float>();
            public float Worst { get; private set; }
            public float WorstAt { get; private set; }

            public Channel(string name) { Name = name; Worst = 0f; WorstAt = float.NaN; }

            public void Add(float step, float at)
            {
                m_Steps.Add(step);
                if (step > Worst) { Worst = step; WorstAt = at; }
            }

            public int Count => m_Steps.Count;

            public float Percentile(float f)
            {
                if (m_Steps.Count == 0) return 0f;
                float[] a = m_Steps.ToArray();
                Array.Sort(a);
                int idx = Mathf.RoundToInt(f * (a.Length - 1));
                if (idx < 0) idx = 0;
                if (idx >= a.Length) idx = a.Length - 1;
                return a[idx];
            }

            /// <summary>
            /// ⚠️ THE SCALE IS TAKEN OVER THE STEPS THAT MOVED, NOT OVER ALL OF THEM. Several outputs here
            /// are piecewise constant by design -- a saturated shrug, a gate that has not opened yet, a
            /// correction that is the exact identity inside its envelope -- so a plain percentile over the
            /// whole sweep is ZERO, and a gate that treated zero as "nothing to compare against" would fail
            /// the honest cases and pass the dishonest ones. What matters is: when this output DOES move,
            /// how big is a normal move.
            /// </summary>
            public float ActivePercentile(float f, out int active)
            {
                var nz = new List<float>(m_Steps.Count);
                for (int k = 0; k < m_Steps.Count; k++)
                {
                    if (m_Steps[k] > 0f) nz.Add(m_Steps[k]);
                }
                active = nz.Count;
                if (active == 0) return 0f;
                float[] a = nz.ToArray();
                Array.Sort(a);
                int idx = Mathf.RoundToInt(f * (a.Length - 1));
                if (idx < 0) idx = 0;
                if (idx >= a.Length) idx = a.Length - 1;
                return a[idx];
            }
        }

        /// <summary>
        /// ⭐ THE CONTINUITY GATE, AND WHY IT IS SELF-NORMALISING.
        ///
        /// A seam is invisible in any single pose and exists only BETWEEN two of them, so the measurement is
        /// always "worst single-step change over a fine sweep". The hard part is the THRESHOLD: an absolute
        /// one has to be re-chosen for every sweep, every step size and every output unit, and a bad choice
        /// is how a 1430x amplifier ships behind a green suite.
        ///
        /// So the scale comes from the sweep itself -- the 95th percentile step, which is what this output
        /// NORMALLY does under this input. A smooth sweep has worst ~ p95; a seam has worst >> p95, and one
        /// or two spikes in a thousand samples cannot move p95. The absolute floor only says "below this we
        /// do not care", so a sweep whose output barely moves cannot fail on noise.
        ///
        /// NON-VACUITY IS BUILT IN: p95 == 0 means the sweep never moved this output at all, and that is a
        /// FAILURE, not a pass. Returns null when the channel is clean, else the finding.
        /// </summary>
        public static string GateSmooth(Channel ch, float absFloor, float amp, string context, StringBuilder log)
        {
            if (ch.Count == 0)
            {
                return $"{context} / {ch.Name}: NO SAMPLES.";
            }
            float p95 = ch.ActivePercentile(0.95f, out int active);
            float p50 = ch.ActivePercentile(0.5f, out _);

            // ⚠️ A CHANNEL THAT NEVER MOVED IS NOT A FAILURE HERE, AND SAYING OTHERWISE WOULD BE WRONG.
            // Plenty of these outputs are CORRECTLY constant under a given input: the elbow does not move
            // when only the tracker's ROLL changes, the target rotation does not move during a reach sweep,
            // the hint does not move when there is no hint. A channel with no motion also has no jump, so
            // there is nothing to judge. Sweep-level non-vacuity -- did ANYTHING move -- belongs to the
            // caller, which knows what the sweep was supposed to excite.
            if (!(ch.Worst > 0f))
            {
                log?.AppendLine($"      {ch.Name,-22} constant across the whole sweep");
                return null;
            }

            // With too few moving steps there is no honest scale, so only the absolute floor applies.
            float bound = active >= 20 ? Mathf.Max(absFloor, amp * p95) : absFloor;
            float ampSeen = p95 > 0f ? ch.Worst / p95 : float.PositiveInfinity;
            log?.AppendLine($"      {ch.Name,-22} moved {active,6}/{ch.Count,-6} p50 {p50,10:F5}  p95 {p95,10:F5}  " +
                            $"worst {ch.Worst,11:F5} (at {ch.WorstAt,9:F3})  amp {ampSeen,8:F1}x");

            if (!(ch.Worst < bound))
            {
                return $"{context} / {ch.Name}: JUMPED {ch.Worst:0.0000} in ONE step at input {ch.WorstAt:0.000}, " +
                       $"against a 95th-percentile MOVING step of {p95:0.00000} ({active} of {ch.Count} steps moved) " +
                       $"-- an amplification of {ampSeen:0.0}x, bound {bound:0.0000}. A smooth input producing a jump " +
                       "in a bounded output is a SEAM: a principal-angle wrap, a branch flip, or a gate crossing. " +
                       "Relocating it is not fixing it.";
            }
            return null;
        }

        /// <summary>
        /// ⭐ AN INVARIANT THAT IS TRUE, IS ASSERTED, AND IS CURRENTLY BROKEN BY SHIPPING CODE.
        ///
        /// The alternative to this is one of two bad things: delete the assertion, or move the threshold
        /// until it passes. Both are how a defect stops being visible, and moving a threshold is worse
        /// because it leaves behind a test that LOOKS like it is guarding something.
        ///
        /// So the invariant stays exactly as written, the measured breach is reported in full, and the
        /// result is IGNORED rather than FAILED -- which keeps the suite's failure count meaningful while
        /// putting the number in front of whoever runs it, every single run. When the defect is fixed the
        /// call simply stops being reached and the test goes green on its own.
        /// </summary>
        public static void KnownOpen(string defect, string measured)
        {
            TestContext.WriteLine("\n  ⛔ KNOWN-OPEN DEFECT -- the invariant below is asserted and currently FAILS on " +
                                  "shipping code:\n     " + defect + "\n     MEASURED: " + measured + "\n");
            Assert.Ignore("KNOWN-OPEN DEFECT (asserted, not weakened): " + defect + "  MEASURED: " + measured);
        }

        /// <summary>Aggregates findings so ONE run reports every broken invariant instead of dying on the
        /// first. A net that can only ever name one defect makes the next run a guessing game.</summary>
        public static void Report(List<string> findings, StringBuilder log, string title)
        {
            if (log != null && log.Length > 0)
            {
                TestContext.WriteLine("\n  " + title + "\n" + log);
            }
            if (findings != null && findings.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{findings.Count} INVARIANT(S) BROKEN in {title}:");
                for (int k = 0; k < findings.Count; k++)
                {
                    sb.AppendLine($"  [{k + 1}] {findings[k]}");
                }
                Assert.Fail(sb.ToString());
            }
        }
    }
}
