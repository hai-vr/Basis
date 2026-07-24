using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// TWO DEFECTS THAT NEED NO HUMAN-MOTION REFERENCE, WHICH IS EXACTLY WHY THEY ARE GATED HERE.
    ///
    /// This file deliberately asserts nothing about what a human arm does. An earlier gate in this
    /// investigation pinned a phantom-twist target against a RIGIDLY ABDUCTED reference arm, and the CMU
    /// corpus then showed real humans roll the humerus 40-70 deg through the same elevation range while the
    /// rigid reference rolls 6 -- so the reference, not the solver, was the outlier, and that gate would have
    /// pinned an artifact permanently. It was deleted. Everything below is a statement about the SOLVER'S OWN
    /// CONSISTENCY, which no corpus can overturn:
    ///
    ///   1. THE FOREARM'S AXIAL ROLL MUST NOT BE INHERITED FROM THE ANIMATION CLIP. Twisting the animated
    ///      forearm about its own long axis moves no bone position at all -- the IK problem handed to the
    ///      solver is byte-identical -- so the solved forearm must not move either. It used to track 1:1.
    ///
    ///   2. A SMOOTH INPUT MUST NOT PRODUCE 4x AMPLIFIED BONE ROLL. The wrist-roll relief's old wrap fade
    ///      crushed a capped 70 deg of relief to zero over 23 deg of measured twist, so rolling the
    ///      controller one degree rolled the humerus up to 4.56. Whatever the right pose is, that is not it.
    ///
    /// ================================================================================================
    /// ⚠️ HOW THIS FILE AVOIDS BasisArmWristRollReliefTests' BLIND SPOT, WHICH IS STRUCTURAL AND NOT A
    /// CRITICISM OF THAT FILE -- IT MEASURES SOMETHING ELSE.
    ///
    /// That file's helpers build the controller rotation FROM THE ANIMATED BONES:
    ///     i.TipRotation    = i.MidRotation;
    ///     i.TargetRotation = Quaternion.AngleAxis(rollDeg, foreDir) * i.MidRotation;
    /// so the relief's `neutral` and the target are two expressions in the SAME animated forearm and the
    /// measured twist is IDENTICALLY the applied roll -- its own assertion says "the measured roll IS the
    /// applied roll here". Both defects above are invisible to that construction BY DEFINITION: defect 1 is a
    /// dependence ON the animated forearm (which is cancelled), and defect 2 lives at |twist| > 100 deg,
    /// which that construction can only reach by commanding a wrist roll no wrist can perform.
    ///
    /// SO NOTHING HERE DERIVES THE CONTROLLER ROTATION FROM AN ANIMATED BONE. The hand target rotation is
    /// built from the bind bone axis swung onto the SHOULDER->TARGET ray and then rolled, i.e. from the
    /// user's own geometry, and is therefore free to disagree with whatever the solver's neutral says --
    /// which is the whole quantity under test. Defect 1 goes further and holds the controller COMPLETELY
    /// FIXED while only the animated forearm's roll changes, so the input is provably constant.
    /// ================================================================================================
    /// </summary>
    public class BasisArmForearmRollAndSeamTests
    {
        const float Upper = 0.30f, Fore = 0.30f, Total = Upper + Fore;
        static readonly Vector3 BoneAxis = Vector3.right;      // shoulder->elbow at bind (a T-pose arm)
        static readonly Vector3 Shoulder = new Vector3(0.17f, 1.40f, 0f);
        static readonly Vector3 IdleUpperArm = new Vector3(0.45f, -1f, 0.05f);
        const float IdleElbowFlexDeg = 35f;

        struct Anim { public Vector3 Elbow, Hand; public Quaternion RootRot, MidRot, TipRot; }

        /// <summary>A properly rigged animated arm: the forearm flexes about the humerus's OWN local up, as an
        /// authored clip on a real rig does. `foreTwistDeg` then twists the forearm IN PLACE -- a rotation
        /// about its own long axis, which by construction moves neither the elbow nor the hand.</summary>
        static Anim Rigged(float foreTwistDeg)
        {
            Vector3 upperDir = IdleUpperArm.normalized;
            Quaternion rr = Quaternion.FromToRotation(BoneAxis, upperDir);
            Vector3 lowerDir = Quaternion.AngleAxis(-IdleElbowFlexDeg, rr * Vector3.up) * upperDir;

            Anim a;
            a.RootRot = rr;
            a.MidRot = Quaternion.FromToRotation(BoneAxis, lowerDir);
            a.Elbow = Shoulder + a.RootRot * BoneAxis * Upper;
            a.Hand = a.Elbow + a.MidRot * BoneAxis * Fore;
            a.MidRot = a.MidRot * Quaternion.AngleAxis(foreTwistDeg, BoneAxis);
            a.TipRot = a.MidRot;
            return a;
        }

        /// <summary>The rig's BIND arm: a dead-straight T-pose, upper arm and forearm both along +x. This is
        /// deliberately the WORST case for any construction needing an elbow hinge axis (the two bones are
        /// collinear, so their cross product is undefined) and a complete non-event for the relative
        /// quaternion the solver actually uses.</summary>
        static readonly Quaternion BindHumerus = Quaternion.identity;
        static readonly Quaternion BindLowerArm = Quaternion.identity;

        static BasisSwivelFrame Frame() => BasisSwivelHintCore.BuildFrame(
            new Vector3(-0.17f, 1.40f, 0f), new Vector3(0.17f, 1.40f, 0f),
            new Vector3(0f, 1.30f, 0f), new Vector3(0f, 1.50f, 0f));

        static Vector3 Dir(float elevDeg)
            => new Vector3(Mathf.Cos(elevDeg * Mathf.Deg2Rad), Mathf.Sin(elevDeg * Mathf.Deg2Rad), 0f);

        static Vector3 Target(float ext, float elevDeg) => Shoulder + Dir(elevDeg) * (ext * Total);

        static Vector3 ModelBend(Vector3 hand)
        {
            BasisSwivelHintCore.ArmHint(Frame(), Shoulder, hand, Total, false, out Vector3 hint, out _);
            return (hint - Shoulder) / (0.5f * Total);
        }

        /// <summary>ANTI-TAUTOLOGY: built from the shoulder->target ray and the bind bone axis. No animated
        /// bone appears anywhere in it.</summary>
        static Quaternion Grip(Vector3 target, float rollDeg)
            => Quaternion.FromToRotation(BoneAxis, (target - Shoulder).normalized)
               * Quaternion.AngleAxis(rollDeg, BoneAxis);

        /// <summary>Signed roll of a bone about its own long axis vs a twist-free transport of the bind bone
        /// onto its current direction: exactly the component that moves no joint.</summary>
        static float AbsRollDeg(Quaternion boneRot)
        {
            Vector3 dir = (boneRot * BoneAxis).normalized;
            Quaternion delta = boneRot * Quaternion.Inverse(Quaternion.FromToRotation(BoneAxis, dir));
            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            return 2f * Mathf.Atan2(Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), dir), delta.w) * Mathf.Rad2Deg;
        }

        static BasisArmSolveResult Solve(Anim a, Vector3 target, float gripRollDeg, bool bakeBind)
            => Solve(a, target, gripRollDeg, bakeBind, a.TipRot);

        static BasisArmSolveResult Solve(Anim a, Vector3 target, float gripRollDeg, bool bakeBind, Quaternion tip)
        {
            var i = new BasisArmSolveInput
            {
                Shoulder = Shoulder, Elbow = a.Elbow, Hand = a.Hand,
                RootRotation = a.RootRot, MidRotation = a.MidRot,
                TargetPosition = target,
                TargetRotation = Grip(target, gripRollDeg),
                TargetOffset = Quaternion.identity,
                HintPosition = Shoulder + 0.5f * Total * ModelBend(target),
                HintWeight = true, HintIsTracker = false,
                PlayerUp = Vector3.up, TorsoUp = Frame().Up,
                HintMaxStepDeg = float.MaxValue,
                TipRotation = tip,
                HasPrevPole = false,
                BindHumerusRotation = bakeBind ? BindHumerus : default,
                BindLowerArmRotation = bakeBind ? BindLowerArm : default,
            };
            BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);
            return r;
        }

        /// <summary>The solved forearm's world roll, INCLUDING MidPostRoll -- which the runtime applies to the
        /// forearm after the root deltas, so it is part of what the user sees and must be part of what is
        /// measured. MidRotationSolved already carries it.</summary>
        static float ForearmRoll(BasisArmSolveResult r) => AbsRollDeg(r.MidRotationSolved);

        // ── FIX 1 ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A pure twist of the ANIMATED forearm moves no bone position, so the solver is handed an identical
        /// problem and must return an identical forearm. Measured before the fix: it tracked the animated
        /// twist 1:1 to 0.000 deg, i.e. the avatar's forearm twist was decided by whichever idle clip was
        /// playing.
        ///
        /// ⚠️ WHAT THIS GATE DOES AND DOES NOT COVER, because the two are easy to confuse and the second is
        /// STILL OPEN. `TipRotation` -- the animated HAND rotation -- is held FIXED across the sweep while
        /// only `MidRotation` is twisted. That isolates the forearm-roll stage, which is what was fixed, and
        /// with it the result is bit-identical (measured: -3.024 deg five times across a 120 deg sweep).
        ///
        /// It does NOT cover the OTHER clip dependence, which is real and unfixed: the wrist-roll relief
        /// measures its twist against `midRot * Inverse(i.MidRotation) * i.TipRotation`, and on a real rig
        /// the animated hand RIDES the animated forearm, so twisting one twists the other and the relief
        /// reads a different wrist twist. Because the relief is a whole-arm swivel that then moves the
        /// humerus, the forearm's final roll still shifts (measured: 29.6 deg at overhead across the same
        /// 120 deg sweep, down from 67.0 before this fix). Rebuilding the relief's neutral on the bind
        /// relationship was measured EARLIER in this investigation and rejected -- it left the phantom drift
        /// bit-identical at 41.4 deg, moved the seam further into reachable wrist range, and added ~23 deg of
        /// measured twist at every pose. So that half is deliberately left open rather than "fixed" by a
        /// change already shown not to work. Do not widen this gate to cover it; fix it properly or leave it.
        /// </summary>
        [Test]
        public void ForearmRoll_DoesNotInheritTheAnimationClipsTwist()
        {
            foreach (float elev in new[] { 0f, 45f, 90f })
                foreach (float ext in new[] { 0.85f, 0.97f })
                {
                    Vector3 target = Target(ext, elev);
                    Anim baseline = Rigged(0f);
                    Quaternion fixedTip = baseline.TipRot;   // the SAME hand rotation for every row
                    float rollAt0 = ForearmRoll(Solve(baseline, target, 0f, true, fixedTip));

                    foreach (float twist in new[] { -60f, -30f, 30f, 60f })
                    {
                        Anim a = Rigged(twist);

                        // The premise: the animated twist really did move nothing.
                        Assert.That((a.Elbow - baseline.Elbow).magnitude * 1000f, Is.LessThan(1e-3f),
                            "the probe is broken: twisting the forearm about its own axis moved the elbow");
                        Assert.That((a.Hand - baseline.Hand).magnitude * 1000f, Is.LessThan(1e-3f),
                            "the probe is broken: twisting the forearm about its own axis moved the hand");

                        BasisArmSolveResult r = Solve(a, target, 0f, true, fixedTip);
                        float d = Mathf.Abs(Mathf.DeltaAngle(ForearmRoll(r), rollAt0));

                        Assert.That(d, Is.LessThan(1.0f),
                            $"A {twist:F0} deg twist of the ANIMATED forearm -- which moved no bone position at " +
                            $"all -- changed the SOLVED forearm's roll by {d:F1} deg (elev {elev:F0}, ext {ext:F2}). " +
                            $"The forearm's axial roll is a free DOF the solver must SET, not inherit: while it is " +
                            $"inherited, the avatar's forearm twist depends on which idle clip is playing.");
                    }
                }
        }

        /// <summary>ANTI-TAUTOLOGY for fix 1: "always output zero roll" would pass the test above trivially.
        /// The forearm must still carry the hand's real, in-band pronation.</summary>
        [Test]
        public void ForearmRoll_StillCarriesTheHandsOwnPronation()
        {
            Vector3 target = Target(0.97f, 30f);
            Anim a = Rigged(0f);
            float atZero = ForearmRoll(Solve(a, target, 0f, true));

            foreach (float grip in new[] { 30f, 60f, -30f, -60f })
            {
                BasisArmSolveResult r = Solve(a, target, grip, true);
                float moved = Mathf.DeltaAngle(atZero, ForearmRoll(r));
                Assert.That(Mathf.Abs(moved), Is.GreaterThan(5f),
                    $"Rolling the controller {grip:F0} deg moved the forearm only {moved:F1} deg. Pinning the " +
                    $"forearm to a constant would pass the inheritance gate while deleting pronation entirely.");
            }
        }

        /// <summary>Zero bind rotations are the documented off-switch, so every caller that has not baked them
        /// is bit-identical. This is what keeps the change safe to land ahead of the rig work.</summary>
        [Test]
        public void ForearmRoll_DeclinesExactlyWhenTheRigHasNotBakedBind()
        {
            Vector3 target = Target(0.97f, 45f);
            foreach (float twist in new[] { 0f, 45f })
            {
                BasisArmSolveResult r = Solve(Rigged(twist), target, 25f, false);
                Assert.That(r.ForearmRollDeg, Is.EqualTo(0f),
                    "an unbaked rig must not get a forearm roll");
                Assert.That(Quaternion.Angle(r.MidPostRoll, Quaternion.identity), Is.LessThan(1e-4f),
                    "MidPostRoll must be a valid identity for the runtime to multiply through");
            }
        }

        // ── FIX 2 ─────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// THE AMPLIFICATION GATE, and it is reference-free: it asserts a GAIN, not a pose. One degree of
        /// controller roll may not produce more than about one degree of humeral roll. The old wrap fade
        /// crushed a capped 70 deg of relief to zero over 23 deg of twist and measured 4.56 here.
        ///
        /// Swept over the whole controller-roll circle at several elevations and extensions, because the
        /// seam MIGRATES with elevation -- it sat near 125-135 deg of controller roll at a lateral reach but
        /// near 85-90 overhead, which is inside what a real wrist reaches.
        /// </summary>
        [Test]
        public void ControllerRoll_DoesNotAmplifyIntoHumeralRoll()
        {
            float worst = 0f; string where = "";

            foreach (float elev in new[] { 0f, 30f, 60f, 90f })
                foreach (float ext in new[] { 0.85f, 0.97f })
                {
                    Vector3 target = Target(ext, elev);
                    Anim a = Rigged(0f);
                    for (float g = -180f; g < 180f; g += 2f)
                    {
                        float lo = AbsRollDeg(Solve(a, target, g - 0.5f, true).RootRotationSolved);
                        float hi = AbsRollDeg(Solve(a, target, g + 0.5f, true).RootRotationSolved);
                        float gain = Mathf.Abs(Mathf.DeltaAngle(lo, hi));
                        if (gain > worst)
                        {
                            worst = gain;
                            where = $"elev {elev:F0}, ext {ext:F2}, controller roll {g:F0}";
                        }
                    }
                }

            Assert.That(worst, Is.LessThan(1.35f),
                $"One degree of controller roll rolled the humerus {worst:F2} deg ({where}). The relief's seam " +
                $"handling must be slope-bounded: min(relief, PI - |twist|) has gradient magnitude 1 by " +
                $"construction. A fade that crushes a capped relief to zero over a narrow window is an " +
                $"amplifier, and at full extension a swivel is ~99% bone ROLL, so the user sees all of it.");
        }

        /// <summary>ANTI-TAUTOLOGY for fix 2, and the reason the gate above cannot be met by simply switching
        /// the relief off: past the comfort band the humerus must still recruit, on the documented curve.</summary>
        [Test]
        public void PastTheBand_TheHumerusStillRecruits()
        {
            Vector3 target = Target(0.97f, 30f);
            Anim a = Rigged(0f);

            float best = 0f;
            for (float g = -180f; g < 180f; g += 5f)
            {
                best = Mathf.Max(best, Mathf.Abs(Solve(a, target, g, true).WristReliefDeg));
            }
            Assert.That(best, Is.GreaterThan(25f),
                $"The largest relief anywhere on the controller-roll circle was {best:F1} deg. Bounding the " +
                $"seam's slope must not become 'never relieve': the humerus answering for what the forearm " +
                $"cannot is the whole point of the stage.");
        }

        /// <summary>The seam is a PRINCIPAL-angle artifact: +179.5 and -179.5 are one hand pose, so the two
        /// sides must still agree. Slope-bounding must not reintroduce the 140 deg jump the old fade existed
        /// to remove -- that would be relocating the discontinuity, not fixing it.</summary>
        [Test]
        public void TheTwoSidesOfTheSeamStillAgree()
        {
            foreach (float elev in new[] { 0f, 45f, 90f })
            {
                Vector3 target = Target(0.97f, elev);
                Anim a = Rigged(0f);
                float plus = AbsRollDeg(Solve(a, target, 179.5f, true).RootRotationSolved);
                float minus = AbsRollDeg(Solve(a, target, -179.5f, true).RootRotationSolved);
                float gap = Mathf.Abs(Mathf.DeltaAngle(plus, minus));

                Assert.That(gap, Is.LessThan(3f),
                    $"+179.5 and -179.5 deg of controller roll are the SAME hand pose but the humerus differed " +
                    $"by {gap:F1} deg (elev {elev:F0}). The relief must meet itself at zero across the seam.");
            }
        }

        /// <summary>Reach preservation is structural for every stage here: the relief and the seam clamp are
        /// swivels about shoulder->hand, and the forearm roll is about the forearm's own axis. Neither can
        /// move the hand, so this is supposed to be impossible rather than merely small.</summary>
        [Test]
        public void NothingHereMovesTheHandOffItsTarget()
        {
            float worst = 0f;
            foreach (float elev in new[] { 0f, 45f, 90f })
                foreach (float ext in new[] { 0.85f, 0.97f })
                    for (float g = -180f; g < 180f; g += 15f)
                        worst = Mathf.Max(worst, Solve(Rigged(0f), Target(ext, elev), g, true).HandError);

            Assert.That(worst * 1000f, Is.LessThan(0.5f),
                $"the hand left its target by {worst * 1000f:F3} mm");
        }
    }
}
