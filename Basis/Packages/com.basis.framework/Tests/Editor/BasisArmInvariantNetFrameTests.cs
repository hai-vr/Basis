using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// FRAMES. THE SOLVE MUST NOT CARE WHERE THE USER STANDS, WHICH WAY THEY FACE, HOW THEIR RIG WAS
    /// AUTHORED, OR WHETHER THEIR TORSO HAPPENS TO BE VERTICAL.
    ///
    /// ================================================================================================
    /// THE CLASS THIS CATCHES: A WRONG FRAME. Three instances shipped, and all three look identical from
    /// inside a single-orientation test:
    ///
    ///   * a guard evaluated against the RAW BONE AXES, so an ordinary Blender export (bone-Y-up, an X-90
    ///     bind) read a plain hanging arm as pointing dead ahead -- an idle avatar took a full scapular
    ///     retraction and shipped as "this avatar's shoulders are permanently hunched";
    ///   * the shoulder solved against the CHEST when its parent is the UPPER chest;
    ///   * the elbow anatomy guard evaluated against the PLAYER ROOT's up instead of the TORSO's, so
    ///     bending at the waist made its ceiling meaningless. Measured over 109 clips: the posture tier's
    ///     worst apparent elbow rise went 0.0242 (torso) -> 0.3723 (root), 7.4x the soft margin, and on all
    ///     148 offending frames THE MEASUREMENT'S SIGN IS FLIPPED. The guard fired hardest on people bent
    ///     over with their arms hanging perfectly normally.
    ///
    /// Three properties cover the whole class, and each carries its own control:
    ///
    ///   RIG-CONVENTION INVARIANCE. The same world pose, authored with different bone-local frames, must
    ///   produce the same world result. Exactly -- the solved bone rotations differ by the convention and
    ///   NOTHING ELSE, and every position and diagnostic is identical. Control: the hardcoded reference
    ///   axis a per-rig bake replaces, which must be shown to break it.
    ///
    ///   RIGID EQUIVARIANCE.  Solve(T . inputs) == T . Solve(inputs) for any rigid T.
    ///
    ///   FRAME SOURCING.  With a torso frame supplied, the arm guards must be COMPLETELY independent of the
    ///   player root's up -- and must be shown to depend on it when the torso frame is declined, or the
    ///   first half is vacuous.
    /// ================================================================================================
    /// </summary>
    public class BasisArmInvariantNetFrameTests
    {
        static Vector3 Dir(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az)).normalized;
        }

        static IEnumerable<BasisArmNet.Spec> Poses(BasisArmNet.Rig rig)
        {
            float[] azs = { 0f, 100f, 235f };
            float[] els = { -70f, -25f, 35f };
            float[] reaches = { 0.45f, 0.85f, 0.97f, 1.05f };
            int[] hints = { BasisArmNet.HintModel, BasisArmNet.HintTracker };

            foreach (float az in azs)
            foreach (float el in els)
            foreach (float reach in reaches)
            foreach (int hint in hints)
            foreach (float handRoll in new[] { 0f, 140f })
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(az, el);
                s.Reach = reach;
                s.HandRollDeg = handRoll;
                s.HintMode = hint;
                s.HintAzimuthDeg = 48f + az;
                s.HintRhoMin = 0.015f;
                s.TrackerRollDeg = hint == BasisArmNet.HintTracker ? 40f : float.NaN;
                s.RefPerp = Mathf.Abs(el) > 60f ? Dir(az + 90f, 0f) : Vector3.up;
                s.FeedTwistBind = true;
                s.FeedLowerBind = true;
                s.FeedTip = true;
                s.AnimHumRollDeg = 25f;
                s.AnimForeRollDeg = -50f;
                s.AnimHandRollDeg = 33f;
                s.ClavLive = Quaternion.Euler(9f, -4f, 13f);
                yield return s;
            }
        }

        // ============================================================================================
        // 1. ⭐ RIG-CONVENTION INVARIANCE
        // ============================================================================================

        /// <summary>
        /// ⭐ THE SAME ARM, AUTHORED SEVEN DIFFERENT WAYS, MUST SOLVE TO THE SAME ARM.
        ///
        /// A "rig convention" is a rotation applied to every bone's LOCAL frame: the world bind pose is
        /// unchanged, only the bone-space axes differ. That is the single variable that differs between two
        /// avatars of the same skeleton, and it is what the per-rig reference-axis bake exists to absorb.
        ///
        /// The contract is exact, not approximate: with the convention C applied, every solved bone
        /// rotation must come out as (reference result) * C -- because C enters only through the animated
        /// and bind rotations, all of which the solve carries multiplicatively on the right -- and every
        /// POSITION and every DIAGNOSTIC must be numerically identical. Anything else means a stage read the
        /// bone's authored axes as if they were anatomy.
        ///
        /// ⚠️ AND THE CONTROL IS THE HAZARD ITSELF: the hardcoded world reference axis a per-rig bake
        /// replaces is fed to the same solver, and MUST break the invariance (or decline silently, which is
        /// worse -- on a rig whose humerus points down its local -Y it projects to exactly zero and the
        /// guard turns itself off with no diagnostic and a green suite).
        /// </summary>
        [Test]
        public void SolvedArm_IsInvariantToTheRigsBoneAuthoringConvention()
        {
            var findings = new List<string>();
            var log = new StringBuilder();

            BasisArmNet.Rig refRig = BasisArmNet.MakeRig("identity conv (bone +X)", Quaternion.identity);
            var refElbow = new List<Vector3>();
            var refHand = new List<Vector3>();
            var refRoot = new List<Quaternion>();
            var refMid = new List<Quaternion>();
            var refTwist = new List<float>();
            var refGuard = new List<float>();
            var refRoll = new List<float>();

            foreach (BasisArmNet.Spec s in Poses(refRig))
            {
                BasisArmSolveInput i = BasisArmNet.Build(s);
                BasisArmNet.Solve(i, out BasisArmSolveResult r);
                BasisArmNet.StreamCompose(i, r, out Vector3 e, out Vector3 h, out Quaternion root, out Quaternion mid);
                refElbow.Add(e); refHand.Add(h); refRoot.Add(root); refMid.Add(mid);
                refTwist.Add(r.HumeralTwistDeg); refGuard.Add(r.HumeralTwistGuardDeg); refRoll.Add(r.ForearmRollDeg);
            }
            Assert.That(refElbow.Count, Is.GreaterThan(50), "the pose set is too small to say anything.");

            float worstPos = 0f, worstRot = 0f, worstTwist = 0f, worstGuard = 0f, worstRoll = 0f;
            float mostGuardSeen = 0f, mostRollSeen = 0f;
            foreach (float g in refGuard) mostGuardSeen = Mathf.Max(mostGuardSeen, Mathf.Abs(g));
            foreach (float g in refRoll) mostRollSeen = Mathf.Max(mostRollSeen, Mathf.Abs(g));

            foreach (BasisArmNet.Rig rig in BasisArmNet.RigConventions())
            {
                int k = 0;
                float posHere = 0f, rotHere = 0f, twHere = 0f;
                foreach (BasisArmNet.Spec s in Poses(rig))
                {
                    BasisArmSolveInput i = BasisArmNet.Build(s);
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    BasisArmNet.StreamCompose(i, r, out Vector3 e, out Vector3 h, out Quaternion root, out Quaternion mid);

                    posHere = Mathf.Max(posHere, Vector3.Distance(e, refElbow[k]));
                    posHere = Mathf.Max(posHere, Vector3.Distance(h, refHand[k]));
                    rotHere = Mathf.Max(rotHere, BasisArmNet.PoseChangeDeg(root, refRoot[k] * rig.Conv));
                    rotHere = Mathf.Max(rotHere, BasisArmNet.PoseChangeDeg(mid, refMid[k] * rig.Conv));
                    twHere = Mathf.Max(twHere, Mathf.Abs(r.HumeralTwistDeg - refTwist[k]));
                    worstGuard = Mathf.Max(worstGuard, Mathf.Abs(r.HumeralTwistGuardDeg - refGuard[k]));
                    worstRoll = Mathf.Max(worstRoll, Mathf.Abs(r.ForearmRollDeg - refRoll[k]));
                    k++;
                }
                worstPos = Mathf.Max(worstPos, posHere);
                worstRot = Mathf.Max(worstRot, rotHere);
                worstTwist = Mathf.Max(worstTwist, twHere);
                log.AppendLine($"      {rig.Name,-28} local bone {rig.LocalBone.normalized}  pos {posHere * 1000f,8:F4} mm  rot {rotHere,8:F4} deg  twist {twHere,7:F3} deg");

                if (!(posHere < 1e-4f))
                    findings.Add($"[{rig.Name}] the solved joints moved {posHere * 1000f:0.000} mm against the reference " +
                                 "convention. The same world pose authored differently is solving differently: a stage " +
                                 "is reading the bone's authored axes as if they were anatomy.");
                if (!(rotHere < 0.05f))
                    findings.Add($"[{rig.Name}] the solved bone rotations departed from (reference * convention) by " +
                                 $"{rotHere:0.0000} deg.");
                if (!(twHere < 0.05f))
                    findings.Add($"[{rig.Name}] the measured humeral twist changed by {twHere:0.000} deg with the bone " +
                                 "convention. The twist is a physical quantity -- how far the humerus has rotated inside " +
                                 "its own clavicle -- and cannot depend on how somebody exported the skeleton.");
            }

            // ── the control: the hardcoded axis the per-rig bake exists to replace.
            float naiveSpread = 0f;
            int naiveDeclines = 0, naiveTotal = 0;
            foreach (BasisArmNet.Rig rig in BasisArmNet.RigConventions())
            {
                int k = 0;
                foreach (BasisArmNet.Spec s in Poses(rig))
                {
                    BasisArmSolveInput i = BasisArmNet.Build(s);
                    i.BindHumerusRefAxis = Vector3.up;   // the hazard, fed to the real solver
                    BasisArmNet.Solve(i, out BasisArmSolveResult r);
                    naiveTotal++;
                    if (r.HumeralTwistDeg == 0f && refTwist[k] != 0f) naiveDeclines++;
                    naiveSpread = Mathf.Max(naiveSpread, Mathf.Abs(r.HumeralTwistDeg - refTwist[k]));
                    k++;
                }
            }

            BasisArmNet.Report(findings, log, "rig-convention invariance");

            Assert.That(mostGuardSeen, Is.GreaterThan(5f),
                $"the humeral twist guard never applied more than {mostGuardSeen:0.00} deg over the reference pose set, " +
                "so 'the correction is convention-invariant' is being asserted about a correction that never happened.");
            Assert.That(mostRollSeen, Is.GreaterThan(10f),
                $"the forearm roll never exceeded {mostRollSeen:0.00} deg over the reference pose set; same problem.");
            Assert.That(worstGuard, Is.LessThan(0.05f), $"the twist CORRECTION varied {worstGuard:0.000} deg with the bone convention.");
            Assert.That(worstRoll, Is.LessThan(0.05f), $"the forearm ROLL varied {worstRoll:0.000} deg with the bone convention.");
            Assert.That(naiveSpread, Is.GreaterThan(20f),
                $"the HARDCODED reference axis produced only {naiveSpread:0.0} deg of spread across the conventions. " +
                "It is supposed to be convention-DEPENDENT -- that is the whole reason the axis is baked per rig -- so " +
                "if it is not, this test is no longer able to tell a baked axis from a guessed one.");

            TestContext.WriteLine(
                $"\n  rig-convention invariance: joints {worstPos * 1000f:0.0000} mm, rotations {worstRot:0.0000} deg, " +
                $"twist {worstTwist:0.000} deg, correction {worstGuard:0.000} deg, forearm roll {worstRoll:0.000} deg.\n" +
                $"  the hardcoded axis control spreads {naiveSpread:0.0} deg and declines silently on " +
                $"{naiveDeclines}/{naiveTotal} poses.\n" + log);
        }

        // ============================================================================================
        // 2. RIGID EQUIVARIANCE
        // ============================================================================================

        /// <summary>
        /// Solve(T . inputs) == T . Solve(inputs) for any rigid T. "The avatar behaves the same wherever you
        /// stand and whichever way you face", stated formally.
        ///
        /// ⚠️ BindHumerusRefAxis is deliberately NOT transformed. It is a BONE-LOCAL axis, and rotating it
        /// with the world would be testing a different -- and wrong -- contract. If a future change makes
        /// the core treat it as a world vector, this test goes red, which is the point.
        /// </summary>
        [Test]
        public void ArmSolve_IsEquivariantUnderAnyRigidTransform()
        {
            var findings = new List<string>();
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            (Quaternion R, Vector3 T, string name)[] xf =
            {
                (Quaternion.identity, Vector3.zero, "identity (harness self-check)"),
                (Quaternion.Euler(0f, 90f, 0f), Vector3.zero, "yaw 90"),
                (Quaternion.Euler(0f, -137f, 0f), new Vector3(4f, 0f, -9f), "yaw -137 + move"),
                (Quaternion.Euler(78f, 0f, 0f), Vector3.zero, "pitch 78 (bent double)"),
                (Quaternion.Euler(0f, 0f, -95f), Vector3.zero, "roll -95 (lying on your side)"),
                (Quaternion.Euler(20f, 160f, -40f), new Vector3(-6f, 3f, 11f), "general"),
            };

            float worstPos = 0f, worstRot = 0f, worstScalar = 0f;
            string worstName = "";

            foreach (var t in xf)
            {
                float pos = 0f, rot = 0f, sca = 0f;
                foreach (BasisArmNet.Spec baseSpec in Poses(rig))
                {
                    BasisArmNet.Spec moved = baseSpec;
                    moved.World = t.R;
                    moved.WorldT = t.T;

                    BasisArmSolveInput i0 = BasisArmNet.Build(baseSpec);
                    BasisArmSolveInput i1 = BasisArmNet.Build(moved);
                    BasisArmNet.Solve(i0, out BasisArmSolveResult r0);
                    BasisArmNet.Solve(i1, out BasisArmSolveResult r1);

                    BasisArmNet.StreamCompose(i0, r0, out Vector3 e0, out Vector3 h0, out Quaternion root0, out Quaternion mid0);
                    BasisArmNet.StreamCompose(i1, r1, out Vector3 e1, out Vector3 h1, out Quaternion root1, out Quaternion mid1);

                    pos = Mathf.Max(pos, Vector3.Distance(t.R * e0 + t.T, e1));
                    pos = Mathf.Max(pos, Vector3.Distance(t.R * h0 + t.T, h1));
                    rot = Mathf.Max(rot, BasisArmNet.PoseChangeDeg(t.R * root0, root1));
                    rot = Mathf.Max(rot, BasisArmNet.PoseChangeDeg(t.R * mid0, mid1));

                    // Scalars are properties of the BODY, not of where the body is standing.
                    sca = Mathf.Max(sca, Mathf.Abs(r0.ElbowAngleDeg - r1.ElbowAngleDeg));
                    sca = Mathf.Max(sca, Mathf.Abs(r0.ReachRatio - r1.ReachRatio) * 100f);
                    sca = Mathf.Max(sca, Mathf.Abs(r0.HumeralTwistDeg - r1.HumeralTwistDeg));
                    sca = Mathf.Max(sca, Mathf.Abs(r0.HumeralTwistGuardDeg - r1.HumeralTwistGuardDeg));
                    sca = Mathf.Max(sca, Mathf.Abs(r0.ForearmRollDeg - r1.ForearmRollDeg));
                    sca = Mathf.Max(sca, Mathf.Abs(r0.WristReliefDeg - r1.WristReliefDeg));
                    sca = Mathf.Max(sca, Mathf.Abs(r0.HandError - r1.HandError) * 1000f);
                }

                if (pos > worstPos) { worstPos = pos; worstName = t.name; }
                worstRot = Mathf.Max(worstRot, rot);
                worstScalar = Mathf.Max(worstScalar, sca);

                if (!(pos < 5e-4f)) findings.Add($"[{t.name}] joints are off by {pos * 1000f:0.000} mm after the transform.");
                if (!(rot < 0.1f)) findings.Add($"[{t.name}] bone rotations are off by {rot:0.0000} deg after the transform.");
                if (!(sca < 0.2f)) findings.Add($"[{t.name}] a reported scalar moved by {sca:0.000} -- it describes the " +
                                                "body, so a rigid re-placement of that body cannot change it.");
            }

            BasisArmNet.Report(findings, null, "rigid equivariance");
            TestContext.WriteLine($"  equivariance: joints {worstPos * 1000f:0.000} mm (worst under '{worstName}'), " +
                                  $"rotations {worstRot:0.0000} deg, scalars {worstScalar:0.000}.");
        }

        // ============================================================================================
        // 3. ⭐ WHICH UP THE GUARDS READ
        // ============================================================================================

        /// <summary>
        /// ⭐ WITH A TORSO FRAME SUPPLIED, THE ARM SOLVE MUST BE COMPLETELY BLIND TO THE PLAYER ROOT'S UP.
        ///
        /// This is the strongest possible form of the frame claim -- not "close enough", but BIT-IDENTICAL
        /// under an arbitrary re-orientation of the root up -- and it is cheap, because with a tracker hint
        /// and a bent animated arm PlayerUp has exactly one consumer left: the elbow anatomy guard's
        /// fallback, which TorsoUp displaces.
        ///
        /// ⚠️ AND THE SECOND HALF IS WHAT MAKES THE FIRST HALF MEAN ANYTHING. With TorsoUp DECLINED (the
        /// struct default, which falls back to PlayerUp -- the pre-fix behaviour), the very same sweep MUST
        /// become sensitive to PlayerUp. Without that control, "the result did not change" would pass just
        /// as happily if the guard had stopped running at all.
        /// </summary>
        [Test]
        public void ArmGuards_ReadTheTorsoUp_AndAreBlindToThePlayerRootUp()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();

            Vector3[] rootUps =
            {
                Vector3.up,
                Quaternion.Euler(75f, 0f, 0f) * Vector3.up,     // bent double at the waist
                Quaternion.Euler(0f, 0f, 100f) * Vector3.up,    // lying on your side
                new Vector3(0.3f, -0.9f, 0.31f).normalized,     // upside down
            };

            float worstFed = 0f, worstDeclined = 0f;
            int poses = 0;

            foreach (BasisArmNet.Spec baseSpec in Poses(rig))
            {
                if (baseSpec.HintMode != BasisArmNet.HintTracker) continue;   // the model path still reads PlayerUp in dead code
                poses++;

                BasisArmNet.Spec fed = baseSpec; fed.TorsoUp = Vector3.up;
                BasisArmNet.Spec dec = baseSpec; dec.TorsoUp = Vector3.zero;

                BasisArmNet.Spec f0 = fed; f0.PlayerUp = rootUps[0];
                BasisArmNet.Spec d0 = dec; d0.PlayerUp = rootUps[0];
                BasisArmNet.Solve(BasisArmNet.Build(f0), out BasisArmSolveResult rf0);
                BasisArmNet.Solve(BasisArmNet.Build(d0), out BasisArmSolveResult rd0);

                for (int k = 1; k < rootUps.Length; k++)
                {
                    BasisArmNet.Spec f = fed; f.PlayerUp = rootUps[k];
                    BasisArmNet.Spec d = dec; d.PlayerUp = rootUps[k];
                    BasisArmNet.Solve(BasisArmNet.Build(f), out BasisArmSolveResult rf);
                    BasisArmNet.Solve(BasisArmNet.Build(d), out BasisArmSolveResult rd);

                    worstFed = Mathf.Max(worstFed, Vector3.Distance(rf.ElbowSolved, rf0.ElbowSolved));
                    worstDeclined = Mathf.Max(worstDeclined, Vector3.Distance(rd.ElbowSolved, rd0.ElbowSolved));
                }
            }

            Assert.That(poses, Is.GreaterThan(20), "too few tracker poses to say anything.");
            BasisArmNet.Gate(
                "the elbow's dependence on the PLAYER ROOT's up while a torso frame is supplied (the law is a " +
                "statement about the humerus against the ribcage; the ribcage has never heard of gravity)",
                worstFed, 1e-5f, worstDeclined, 0.005f);

            TestContext.WriteLine(
                $"  with TorsoUp fed, re-orienting PlayerUp through 4 body attitudes moves the elbow " +
                $"{worstFed * 1000f:0.000000} mm; with TorsoUp DECLINED the same sweep moves it " +
                $"{worstDeclined * 1000f:0.0} mm -- which is the pre-fix behaviour, and why the fallback is a fallback.");
        }

        /// <summary>
        /// AND THE TORSO FRAME MUST BE THE FRAME THE LAW IS EVALUATED IN -- not merely consulted. Take a
        /// pose, rotate the whole arm AND the torso frame together (the user bends over, their arm goes with
        /// them), and the answer must be the same answer rotated. Then feed the SAME bent-over arm an
        /// UPRIGHT torso up -- the shipped defect -- and the guard must visibly change its mind, or the
        /// equivariance above is not testing anything the frame does.
        /// </summary>
        [Test]
        public void ElbowAnatomyLaw_IsEvaluatedInTheTorsoFrame_NotTheWorldFrame()
        {
            BasisArmNet.Rig rig = BasisArmNet.DefaultRig();
            Quaternion bend = Quaternion.Euler(85f, 0f, 0f);   // touching your toes

            float worstEquivariant = 0f, worstWrongFrame = 0f;
            int poses = 0;
            string worstAt = "(none)";

            foreach (float az in new[] { 0f, 90f, 180f, 270f })
            foreach (float el in new[] { -50f, -15f, 20f })
            foreach (float reach in new[] { 0.70f, 0.88f, 0.96f })
            // ⚠️ NOT 0. RefPerp is world up, so a hint azimuth of exactly 0 puts the elbow at the TOP of
            // its circle -- which is the branch point of the elbow guard's `sign(s)` side selection (see
            // BasisArmInvariantNetKnownOpenDefectTests, D1). Two solves that differ only by a rigid
            // transform then land on opposite sides of the circle for float reasons, and this test would
            // report a known SEAM as a broken FRAME. Every other azimuth measures what it says it does.
            foreach (float hintAz in new[] { 35f, 60f, 150f, 300f })
            {
                BasisArmNet.Spec s = BasisArmNet.Default(rig);
                s.TargetDir = Dir(az, el);
                s.Reach = reach;
                s.HintMode = BasisArmNet.HintTracker;
                s.HintAzimuthDeg = hintAz;
                s.HintRhoMin = 0f;
                s.RefPerp = Vector3.up;
                s.FeedTwistBind = true;
                if (Mathf.Abs(Vector3.Dot(s.TargetDir, Vector3.up)) > 0.95f) continue;
                poses++;

                // upright
                BasisArmSolveInput i0 = BasisArmNet.Build(s);
                BasisArmNet.Solve(i0, out BasisArmSolveResult r0);

                // the same person, bent over: arm AND torso frame rotate together
                BasisArmNet.Spec bentRight = s;
                bentRight.World = bend;
                bentRight.TorsoUp = Vector3.up;   // Build applies World to TorsoUp
                BasisArmSolveInput i1 = BasisArmNet.Build(bentRight);
                BasisArmNet.Solve(i1, out BasisArmSolveResult r1);
                float eqErr = Vector3.Distance(bend * (r0.ElbowSolved - i0.Shoulder), r1.ElbowSolved - i1.Shoulder);
                if (eqErr > worstEquivariant)
                {
                    worstEquivariant = eqErr;
                    worstAt = $"az {az:0} el {el:0} reach {reach:0.00} hintAz {hintAz:0}: upright elbow " +
                              $"{r0.ElbowSolved}, bent elbow {r1.ElbowSolved}, expected {bend * r0.ElbowSolved}; " +
                              $"upright twist {r0.HumeralTwistDeg:0.0}/{r0.HumeralTwistGuardDeg:0.0}, " +
                              $"bent twist {r1.HumeralTwistDeg:0.0}/{r1.HumeralTwistGuardDeg:0.0}; " +
                              $"upright axisSource {r0.AxisSource} bent {r1.AxisSource}; " +
                              $"upright elbowAngle {r0.ElbowAngleDeg:0.00} bent {r1.ElbowAngleDeg:0.00}";
                }

                // the shipped defect: the same bent-over arm, judged against a WORLD-vertical up
                BasisArmSolveInput iWrong = i1;
                iWrong.TorsoUp = Vector3.up;      // NOT bent: the player root's up, which stays vertical
                BasisArmNet.Solve(iWrong, out BasisArmSolveResult rWrong);
                worstWrongFrame = Mathf.Max(worstWrongFrame,
                    Vector3.Distance(rWrong.ElbowSolved, r1.ElbowSolved));
            }

            Assert.That(poses, Is.GreaterThan(50), "too few poses.");

            TestContext.WriteLine($"  worst equivariance pose -- {worstAt}");
            TestContext.WriteLine(
                $"  bent 85 deg at the waist: torso-frame equivariance holds to {worstEquivariant * 1000f:0.000} mm; " +
                $"judging the same arm against a world-vertical up moves the elbow {worstWrongFrame * 1000f:0.0} mm -- " +
                "that is the guard firing on someone touching their toes with their arms hanging normally.");

            BasisArmNet.Gate(
                "the elbow guard's equivariance when the torso bends with the arm (bending at the waist does " +
                "not change what your shoulder can do)",
                worstEquivariant, 1e-4f, worstWrongFrame, 0.005f);
        }
    }
}
