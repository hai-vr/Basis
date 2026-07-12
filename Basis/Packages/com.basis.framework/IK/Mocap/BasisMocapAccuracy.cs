#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.IK.Mocap
{
    // How the elbow/knee pole is chosen. This is the whole question: with 3- or 6-point tracking the elbow is
    // NOT measured, so the solver has to invent it, and this is the only place we can find out how close that
    // invention lands to where a real human's elbow actually was.
    public enum BasisMocapHintSource
    {
        None,       // no hint at all -- the two-bone core's internal fallback
        Lookup,     // WHAT SHIPS for an untracked arm: ArmBendFrame -> BasisArmBendLookup -> chicken-wing flare
        TruthJoint, // the elbow/knee tracker case: hand the solver the real joint. The accuracy CEILING.
    }

    public struct BasisMocapAccuracySummary
    {
        public bool Ok;
        public string Error;
        public string Clip;
        public string Path;
        public BasisMocapHintSource Hint;
        public int Frames;

        public float ElbowMeanM, ElbowP95M, ElbowMaxM;
        public float KneeMeanM, KneeP95M, KneeMaxM;
        public float ElbowMeanFracArm;   // scale-free: error / arm length
        public float KneeMeanFracLeg;

        // Sanity: we COMMAND the hand and foot, so the solver must hit them. If these are not ~0 the harness
        // is not driving the solver properly and every other number here is meaningless.
        public float HandMaxM, FootMaxM;

        // Pole flips measured on REAL human motion: the elbow jumps while the hand barely moves.
        public int ElbowPops, KneePops;
    }

    public static class BasisMocapAccuracy
    {
        // Mirrors the live defaults (BasisFullBodyData.SetDefaultValues + the driver's ApplyTuningSettings).
        const float k_FlareMaxDeg = 45f;
        const float k_FlareInwardGain = 1f;
        const float k_FlareFullRollDeg = 70f;
        const float k_HipSpringHz = 8f;
        const float k_HipSpringDamping = 1f;
        const float k_HintRateDegPerSec = 540f;

        // A pole flip: the joint jumps hard while the end effector is essentially still. Real human motion is
        // smooth, so any such jump is the solver's doing, not the human's.
        const float k_PopJointM = 0.05f;   // 5 cm of elbow/knee travel in one frame
        const float k_PopEffectorM = 0.01f; // while the hand/foot moved under 1 cm

        struct Limb
        {
            public Vector3 Root, Mid, Tip;
            public Quaternion RootRot, MidRot;
            public bool Seeded;
        }

        public static BasisMocapAccuracySummary Run(BasisMotionClip clip, BasisMocapHintSource hint, string csvPath)
        {
            var s = new BasisMocapAccuracySummary { Hint = hint, Path = csvPath };

            if (clip == null || clip.FrameCount < 2) { s.Error = "clip too short"; return s; }
            if (!BasisBvhLoader.Validate(clip, out string why)) { s.Error = why; return s; }
            s.Clip = clip.Name;

            NativeArray<Vector3> lookup = default;
            try
            {
                if (hint == BasisMocapHintSource.Lookup)
                {
                    // Persistent, not Temp: Temp is frame-scoped and this table has to outlive a many-thousand
                    // frame sweep that never yields to a frame boundary.
                    Vector3[] table = BasisArmBendLookup.GenerateDefaultTable();
                    lookup = new NativeArray<Vector3>(table, Allocator.Persistent);
                }

                var elbowErr = new List<float>();
                var kneeErr = new List<float>();
                float armLen = 0f, legLen = 0f;
                float dt = Mathf.Max(clip.FrameTime, 1e-4f);
                var csv = new StringBuilder("clip,frame,limb,err_m,truth_x,truth_y,truth_z,solved_x,solved_y,solved_z,reach\n");

                var arms = new Limb[2];
                var legs = new Limb[2];
                Quaternion hipSpringRot = Quaternion.identity;
                Vector3 hipSpringVel = Vector3.zero;
                bool hipSpringSeeded = false;
                Vector3 prevElbow0 = Vector3.zero, prevKnee0 = Vector3.zero, prevHand0 = Vector3.zero, prevFoot0 = Vector3.zero;

                for (int f = 0; f < clip.FrameCount; f++)
                {
                    Quaternion hipsRot = clip.Get(f, BasisMocapJoint.Hips).Rotation;
                    Quaternion chestRot = clip.Get(f, BasisMocapJoint.Chest).Rotation;
                    Vector3 playerUp = hipsRot * Vector3.up;

                    // The live rig samples the elbow lookup in a spring-smoothed hips frame, so do the same --
                    // an unsmoothed frame would hand the solver a cleaner pole than it gets in the headset.
                    if (!hipSpringSeeded) { hipSpringRot = hipsRot; hipSpringVel = Vector3.zero; hipSpringSeeded = true; }
                    else
                    {
                        BasisHipFrameSpringCore.Step(hipSpringRot, hipSpringVel, hipsRot, dt, k_HipSpringHz, k_HipSpringDamping,
                            out hipSpringRot, out hipSpringVel);
                    }
                    Quaternion bendFrame = ArmBendFrame(hipSpringRot, chestRot);

                    for (int side = 0; side < 2; side++)
                    {
                        bool isLeft = side == 0;
                        SolveArm(clip, f, isLeft, ref arms[side], bendFrame, chestRot, playerUp, hint, lookup, dt,
                                 out Vector3 truthElbow, out Vector3 solvedElbow, out float handErr, out float reach, out float aLen);
                        armLen = aLen;
                        float e = Vector3.Distance(truthElbow, solvedElbow);
                        elbowErr.Add(e);
                        s.HandMaxM = Mathf.Max(s.HandMaxM, handErr);
                        Append(csv, clip.Name, f, isLeft ? "leftArm" : "rightArm", e, truthElbow, solvedElbow, reach);

                        if (side == 0 && f > 0)
                        {
                            Vector3 hand = clip.Get(f, BasisMocapJoint.LeftHand).Position;
                            if (Vector3.Distance(solvedElbow, prevElbow0) > k_PopJointM &&
                                Vector3.Distance(hand, prevHand0) < k_PopEffectorM) s.ElbowPops++;
                            prevHand0 = hand;
                        }
                        if (side == 0) { prevElbow0 = solvedElbow; if (f == 0) prevHand0 = clip.Get(0, BasisMocapJoint.LeftHand).Position; }

                        SolveLeg(clip, f, isLeft, ref legs[side], hipsRot, hint, dt,
                                 out Vector3 truthKnee, out Vector3 solvedKnee, out float footErr, out float lReach, out float lLen);
                        legLen = lLen;
                        float k = Vector3.Distance(truthKnee, solvedKnee);
                        kneeErr.Add(k);
                        s.FootMaxM = Mathf.Max(s.FootMaxM, footErr);
                        Append(csv, clip.Name, f, isLeft ? "leftLeg" : "rightLeg", k, truthKnee, solvedKnee, lReach);

                        if (side == 0 && f > 0)
                        {
                            Vector3 foot = clip.Get(f, BasisMocapJoint.LeftFoot).Position;
                            if (Vector3.Distance(solvedKnee, prevKnee0) > k_PopJointM &&
                                Vector3.Distance(foot, prevFoot0) < k_PopEffectorM) s.KneePops++;
                            prevFoot0 = foot;
                        }
                        if (side == 0) { prevKnee0 = solvedKnee; if (f == 0) prevFoot0 = clip.Get(0, BasisMocapJoint.LeftFoot).Position; }
                    }
                }

                elbowErr.Sort();
                kneeErr.Sort();
                s.Frames = clip.FrameCount;
                s.ElbowMeanM = Mean(elbowErr); s.ElbowP95M = Pct(elbowErr, 0.95f); s.ElbowMaxM = elbowErr[elbowErr.Count - 1];
                s.KneeMeanM = Mean(kneeErr); s.KneeP95M = Pct(kneeErr, 0.95f); s.KneeMaxM = kneeErr[kneeErr.Count - 1];
                s.ElbowMeanFracArm = armLen > 1e-4f ? s.ElbowMeanM / armLen : float.NaN;
                s.KneeMeanFracLeg = legLen > 1e-4f ? s.KneeMeanM / legLen : float.NaN;
                s.Ok = true;

                if (!string.IsNullOrEmpty(csvPath))
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(csvPath));
                    System.IO.File.WriteAllText(csvPath, csv.ToString());
                }
                return s;
            }
            catch (System.Exception e)
            {
                s.Ok = false;
                s.Error = e.Message;
                return s;
            }
            finally
            {
                if (lookup.IsCreated) lookup.Dispose();
            }
        }

        // Mirror of BasisFullIKConstraintJob.ArmBendFrame: spring-smoothed hips, chest swing only (yaw dropped).
        // Kept in lock-step by hand until the job's hint path is extracted into a core -- that extraction is the
        // next stage and it will delete this method.
        static Quaternion ArmBendFrame(Quaternion hipsRot, Quaternion chestRot)
        {
            Quaternion chestRelative = Quaternion.Inverse(hipsRot) * chestRot;
            Quaternion chestYaw = BasisTwistSolveCore.ExtractTwist(chestRelative, Vector3.up);
            Quaternion chestSwing = chestRelative * Quaternion.Inverse(chestYaw);
            return hipsRot * chestSwing;
        }

        static void SolveArm(BasisMotionClip clip, int f, bool isLeft, ref Limb limb, Quaternion bendFrame, Quaternion chestRot,
                             Vector3 playerUp, BasisMocapHintSource hint, NativeArray<Vector3> lookup, float dt,
                             out Vector3 truthElbow, out Vector3 solvedElbow, out float handErr, out float reach, out float armLen)
        {
            BasisMocapJoint jS = isLeft ? BasisMocapJoint.LeftUpperArm : BasisMocapJoint.RightUpperArm;
            BasisMocapJoint jE = isLeft ? BasisMocapJoint.LeftLowerArm : BasisMocapJoint.RightLowerArm;
            BasisMocapJoint jH = isLeft ? BasisMocapJoint.LeftHand : BasisMocapJoint.RightHand;

            Vector3 shoulder = clip.Get(f, jS).Position;
            truthElbow = clip.Get(f, jE).Position;
            Vector3 truthHand = clip.Get(f, jH).Position;
            Quaternion truthHandRot = clip.Get(f, jH).Rotation;
            armLen = Vector3.Distance(shoulder, truthElbow) + Vector3.Distance(truthElbow, truthHand);

            // The shoulder is handed to the solver from truth: the torso is not what is under test here, and
            // letting torso error leak in would confound the one number we actually want -- the elbow.
            CarryLimb(ref limb, shoulder, clip.Get(f, jS).Rotation, clip.Get(f, jE).Rotation, truthElbow, truthHand);

            BasisArmSolveInput i = default;
            i.Shoulder = shoulder;
            i.Elbow = limb.Mid;
            i.Hand = limb.Tip;
            i.RootRotation = limb.RootRot;
            i.MidRotation = limb.MidRot;
            i.TargetPosition = truthHand;
            i.TargetRotation = truthHandRot;
            i.TargetOffset = Quaternion.identity;
            i.PlayerUp = playerUp;
            i.HintMaxStepDeg = k_HintRateDegPerSec * dt;   // the live rate limit

            switch (hint)
            {
                case BasisMocapHintSource.TruthJoint:
                    i.HintPosition = truthElbow;
                    i.HintWeight = true;
                    i.HintIsTracker = true;
                    break;
                case BasisMocapHintSource.Lookup:
                {
                    Vector3 bend = ComputeArmBendFromLookup(lookup, bendFrame, shoulder, truthHand, truthHandRot, armLen, isLeft, playerUp);
                    i.HintPosition = shoulder + 0.5f * armLen * bend;
                    i.HintWeight = true;
                    i.HintIsTracker = false;   // the job passes hintIsTracker = hasHint && !usedLookup
                    break;
                }
                default:
                    i.HintWeight = false;
                    break;
            }

            BasisArmSolveCore.Solve(i, out BasisArmSolveResult r);
            solvedElbow = r.ElbowSolved;
            handErr = Vector3.Distance(r.HandSolved, truthHand);
            reach = r.ReachRatio;

            limb.Mid = r.ElbowSolved;
            limb.Tip = r.HandSolved;
            limb.RootRot = r.RootRotationSolved;
            limb.MidRot = r.MidRotationSolved;
        }

        // Mirror of BasisFullIKConstraintJob.ComputeArmBendFromLookup. Same lock-step caveat as ArmBendFrame.
        static Vector3 ComputeArmBendFromLookup(NativeArray<Vector3> lookup, Quaternion frameRot, Vector3 shoulder,
                                                Vector3 handTarget, Quaternion handTargetRot, float armLength, bool isLeft, Vector3 playerUp)
        {
            if (armLength < 1e-5f) return isLeft ? Vector3.left : Vector3.right;

            Quaternion invFrame = Quaternion.Inverse(frameRot);
            Vector3 shoulderToHand = handTarget - shoulder;
            Vector3 localPos = invFrame * shoulderToHand / armLength;
            if (isLeft) localPos.x = -localPos.x;

            Vector3 localBend = BasisArmBendLookup.SampleTrilinear(lookup, localPos);
            if (isLeft) localBend.x = -localBend.x;

            Vector3 worldBend = (frameRot * localBend).normalized;
            Vector3 outward = frameRot * (isLeft ? Vector3.left : Vector3.right);
            return BasisElbowFlareCore.ApplyChickenWingFlare(worldBend, shoulderToHand, outward, playerUp, handTargetRot,
                                                             k_FlareInwardGain, k_FlareFullRollDeg, k_FlareMaxDeg);
        }

        static void SolveLeg(BasisMotionClip clip, int f, bool isLeft, ref Limb limb, Quaternion hipsRot,
                             BasisMocapHintSource hint, float dt,
                             out Vector3 truthKnee, out Vector3 solvedKnee, out float footErr, out float reach, out float legLen)
        {
            BasisMocapJoint jH = isLeft ? BasisMocapJoint.LeftUpperLeg : BasisMocapJoint.RightUpperLeg;
            BasisMocapJoint jK = isLeft ? BasisMocapJoint.LeftLowerLeg : BasisMocapJoint.RightLowerLeg;
            BasisMocapJoint jF = isLeft ? BasisMocapJoint.LeftFoot : BasisMocapJoint.RightFoot;

            Vector3 hip = clip.Get(f, jH).Position;
            truthKnee = clip.Get(f, jK).Position;
            Vector3 truthFoot = clip.Get(f, jF).Position;
            Quaternion truthFootRot = clip.Get(f, jF).Rotation;
            legLen = Vector3.Distance(hip, truthKnee) + Vector3.Distance(truthKnee, truthFoot);

            CarryLimb(ref limb, hip, clip.Get(f, jH).Rotation, clip.Get(f, jK).Rotation, truthKnee, truthFoot);

            BasisLegSolveInput i = default;
            i.Root = hip;
            i.Mid = limb.Mid;
            i.Tip = limb.Tip;
            i.RootRotation = limb.RootRot;
            i.MidRotation = limb.MidRot;
            i.TargetPosition = truthFoot;
            i.TargetRotation = truthFootRot;
            i.TargetOffset = Quaternion.identity;
            i.BendNormal = hipsRot * Vector3.right;   // the runtime's no-tracker knee bend normal

            if (hint == BasisMocapHintSource.TruthJoint)
            {
                i.HintPosition = truthKnee;
                i.HintWeight = 1f;
            }
            else
            {
                i.HintWeight = 0f;   // no knee tracker: the leg falls back to the bend normal
            }

            BasisLegSolveCore.Solve(i, out BasisLegSolveResult r);
            solvedKnee = r.KneeSolved;
            footErr = Vector3.Distance(r.FootSolved, truthFoot);
            reach = r.ReachRatio;

            limb.Mid = r.KneeSolved;
            limb.Tip = r.FootSolved;
            limb.RootRot = r.RootRotationSolved;
            limb.MidRot = r.MidRotationSolved;
        }

        // Ride the parent, then let IK correct -- exactly what a parented rig does. Without this the limb would
        // be re-seeded from truth every frame and the temporal feedback loop (rate limiter, pole commit ramps)
        // would never be exercised, which is where the pole flips actually live.
        static void CarryLimb(ref Limb limb, Vector3 root, Quaternion truthRootRot, Quaternion truthMidRot, Vector3 truthMid, Vector3 truthTip)
        {
            if (!limb.Seeded)
            {
                limb.Root = root; limb.Mid = truthMid; limb.Tip = truthTip;
                limb.RootRot = truthRootRot; limb.MidRot = truthMidRot;
                limb.Seeded = true;
                return;
            }
            Vector3 delta = root - limb.Root;
            limb.Mid += delta;
            limb.Tip += delta;
            limb.Root = root;
        }

        static void Append(StringBuilder csv, string clip, int f, string limb, float err, Vector3 truth, Vector3 solved, float reach)
        {
            csv.Append(clip).Append(',').Append(f).Append(',').Append(limb).Append(',')
               .Append(F(err)).Append(',')
               .Append(F(truth.x)).Append(',').Append(F(truth.y)).Append(',').Append(F(truth.z)).Append(',')
               .Append(F(solved.x)).Append(',').Append(F(solved.y)).Append(',').Append(F(solved.z)).Append(',')
               .Append(F(reach)).Append('\n');
        }

        static string F(float v) => float.IsNaN(v) ? "nan" : v.ToString("0.######", CultureInfo.InvariantCulture);
        static float Mean(List<float> v) { float t = 0f; for (int i = 0; i < v.Count; i++) t += v[i]; return v.Count > 0 ? t / v.Count : float.NaN; }
        static float Pct(List<float> sorted, float p) => sorted.Count == 0 ? float.NaN : sorted[Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1)];

        public static (bool pass, string reason) Gate(in BasisMocapAccuracySummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Frames <= 0) return (false, "no frames");

            // The hand/foot are COMMANDED. If the solver cannot hit a target it was handed, nothing else here
            // means anything, so this is checked first and hard.
            if (s.HandMaxM > 0.01f) return (false, $"hand missed its own target by {s.HandMaxM * 100f:F1} cm -- harness is not driving the solve");
            if (s.FootMaxM > 0.01f) return (false, $"foot missed its own target by {s.FootMaxM * 100f:F1} cm -- harness is not driving the solve");

            if (s.Hint == BasisMocapHintSource.TruthJoint)
            {
                // Handed the real joint, the solver must essentially reproduce it. This is the ceiling, and it
                // also proves the harness is wired correctly.
                if (s.ElbowMeanM > 0.03f) return (false, $"elbow mean {s.ElbowMeanM * 100f:F1} cm even when handed the TRUE elbow -- wiring bug");
                if (s.KneeMeanM > 0.05f) return (false, $"knee mean {s.KneeMeanM * 100f:F1} cm even when handed the TRUE knee -- wiring bug");
            }

            return (true, $"{s.Clip} [{s.Hint}] elbow mean {s.ElbowMeanM * 100f:F1} cm (p95 {s.ElbowP95M * 100f:F1}, max {s.ElbowMaxM * 100f:F1}, " +
                          $"{s.ElbowMeanFracArm * 100f:F1}% of arm) | knee mean {s.KneeMeanM * 100f:F1} cm (p95 {s.KneeP95M * 100f:F1}) | " +
                          $"pops elbow {s.ElbowPops} knee {s.KneePops}");
        }
    }
}
#endif
