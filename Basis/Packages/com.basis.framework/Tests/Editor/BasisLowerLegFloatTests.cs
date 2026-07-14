using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.Tests.IK
{
    /// <summary>
    /// PROBE — "the lower leg floats around when I move the feet."
    ///
    /// Config: FOOT trackers, NO knee/lower-leg trackers. That is the ordinary 6-point FBT setup, and it
    /// routes the knee through TWO stacked smoothers that nothing currently gates for responsiveness:
    ///
    ///   1. BasisLocalRigDriver.TryComputeButterflyKnee -- FBIKButterflyKnees defaults TRUE, and with a foot
    ///      tracker and no knee tracker it is the branch that wins. It low-passes the knee hint as a WORLD
    ///      POSITION at ButterflyKneeSmoothRate = 8 (tau = 125 ms).
    ///   2. BasisFullBodyIK.SmoothKneeSwivel -- the output One-Euro. Reached via `!preserveTip` (a real foot
    ///      rotation IS supplied), so it takes the "tracked" cutoffs 1.5 Hz / beta 0.20 -- but ConditionOnPole
    ///      scales beta by the pole lever arm, and a near-straight leg has almost none, so beta collapses to
    ///      ~0 and the floor falls to SingularMinCutoffHz = 1.0 Hz (tau = 159 ms) with NO speed adaptation
    ///      left to open it.
    ///
    /// Moving your foot while standing is exactly the near-straight case. This measures how far the knee
    /// actually lags, stage by stage, against the zero-lag solve.
    /// </summary>
    public sealed class BasisLowerLegFloatTests
    {
        const float Dt = 1f / 90f;
        const float Thigh = 0.42f, Shin = 0.42f;
        static readonly Vector3 Hip = new Vector3(-0.09f, 0.90f, 0f);   // left hip socket
        static readonly Quaternion HipsRot = Quaternion.identity;

        // Live constants, mirrored (lock-step with the shipping code).
        const float ButterflyRate = 8f;                                     // BasisLocalRigDriver.ButterflyKneeSmoothRate
        const float TrackedMinCutoffHz = 1.5f, TrackedBeta = 0.20f, TrackedDerivHz = 1.0f;  // k_TrackedKneeSwivel*
        const float MaxOpenDeg = 60f;                                       // FBIKButterflyKneeMaxOpenDeg

        /// <summary>One frame of the real lower-leg pipeline. `smoothHint`/`smoothSwivel` select the stages.</summary>
        static Vector3 Step(Vector3 footPos, Quaternion footRot, float dt,
                            bool smoothHint, bool smoothSwivel,
                            ref Vector3 hintState, ref float weightState,
                            ref BasisSwivelFilterState swivelState, ref bool swivelSeeded,
                            ref Vector3 kneeState)
        {
            // ---- 1. butterfly knee hint (the branch a foot tracker with no knee tracker actually takes)
            BasisButterflyKneeInput bi = default;
            bi.HipPosition = Hip;
            bi.FootPosition = footPos;
            bi.FootInstepDir = footRot * Vector3.up;
            bi.OutwardDir = -(HipsRot * Vector3.right);      // left leg
            bi.DefaultBendDir = HipsRot * Vector3.forward;
            bi.PlayerUp = Vector3.up;
            bi.TorsoFacingDir = HipsRot * Vector3.forward;
            bi.UpperLength = Vector3.Distance(Hip, kneeState);
            bi.LowerLength = Vector3.Distance(kneeState, footPos);
            bi.MaxOpenDeg = MaxOpenDeg;
            bi.Strength = 1f;
            bi.SupineFloor = 1f;
            BasisButterflyKneeCore.Solve(bi, out BasisButterflyKneeResult br);

            Vector3 hint;
            float weight;
            if (smoothHint)
            {
                float a = 1f - Mathf.Exp(-ButterflyRate * dt);
                hintState = Vector3.Lerp(hintState, br.KneeHint, a);
                weightState = Mathf.Lerp(weightState, br.HintWeight, a);
                hint = hintState;
                weight = weightState;
            }
            else
            {
                hintState = br.KneeHint;
                weightState = br.HintWeight;
                hint = br.KneeHint;
                weight = br.HintWeight;
            }

            // ---- 2. the real two-bone leg solve
            BasisLegSolveInput li = default;
            li.Root = Hip;
            li.Mid = kneeState;
            li.Tip = footPos;
            li.RootRotation = Quaternion.identity;
            li.MidRotation = Quaternion.identity;
            li.TargetPosition = footPos;
            li.TargetRotation = footRot;
            li.TargetOffset = Quaternion.identity;
            li.HintPosition = hint;
            li.HintWeight = weight;
            li.BendNormal = HipsRot * Vector3.right;
            BasisLegSolveCore.Solve(li, out BasisLegSolveResult lr);

            Vector3 knee = lr.KneeSolved;

            // ---- 3. the output knee-swivel One-Euro (SmoothKneeSwivel), tracked cutoffs + pole conditioning
            if (smoothSwivel)
            {
                BasisSwivelSmootherInput si = default;
                si.Root = Hip;
                si.Mid = knee;
                si.Tip = footPos;
                si.BodyRotation = HipsRot;
                si.ReferenceLocal = Vector3.forward;
                si.FallbackLocal = Vector3.right;
                si.Dt = dt;
                si.MinCutoffHz = TrackedMinCutoffHz;
                si.Beta = TrackedBeta;
                si.DerivCutoffHz = TrackedDerivHz;
                si.ConditionOnPole = true;
                si.SingularMinCutoffHz = BasisSwivelFilterCore.MinCutoffHz;
                si.GuardAnteriorHalfSpace = true;
                si.AnteriorSoftDeg = BasisLegSolveCore.KneeAnteriorSoftDeg;
                si.AnteriorHardDeg = BasisLegSolveCore.KneeAnteriorHardDeg;
                si.State = swivelState;
                si.Seeded = swivelSeeded;

                BasisSwivelSmootherCore.Solve(si, out BasisSwivelSmootherResult sr);
                if (sr.WriteState) { swivelState = sr.State; swivelSeeded = sr.Seeded; }
                if (sr.Valid) knee = sr.DesiredMid;
            }

            kneeState = knee;
            return knee;
        }

        /// <summary>Runs a foot trajectory through the pipeline; returns the knee position each frame.</summary>
        static Vector3[] Run(System.Func<int, (Vector3 pos, Quaternion rot)> footAt, int frames,
                             bool smoothHint, bool smoothSwivel)
        {
            (Vector3 p0, Quaternion r0) = footAt(0);

            // Seed the knee at the rest solve so nothing is converging from a cold start.
            Vector3 knee = Hip + Vector3.down * Thigh + Vector3.forward * 0.02f;
            Vector3 hintState = Vector3.zero;
            float weightState = 0f;
            BasisSwivelFilterState swivelState = default;
            bool swivelSeeded = false;

            for (int i = 0; i < 60; i++)   // settle at rest
                Step(p0, r0, Dt, smoothHint, smoothSwivel, ref hintState, ref weightState,
                     ref swivelState, ref swivelSeeded, ref knee);

            var track = new Vector3[frames];
            for (int i = 0; i < frames; i++)
            {
                (Vector3 fp, Quaternion fr) = footAt(i);
                track[i] = Step(fp, fr, Dt, smoothHint, smoothSwivel, ref hintState, ref weightState,
                                ref swivelState, ref swivelSeeded, ref knee);
            }
            return track;
        }

        // --------------------------------------------------------------- the motions

        const float MoveSecs = 0.45f, HoldSecs = 1.5f;
        static int Frames => Mathf.RoundToInt((MoveSecs + HoldSecs) / Dt);
        static int StopFrame => Mathf.RoundToInt(MoveSecs / Dt);
        static float T(int i) => Mathf.Clamp01(i * Dt / MoveSecs);

        /// <summary>Foot slides 35 cm forward along the floor. Leg stays NEAR-EXTENDED — the low-conditioning
        /// case, where the swivel filter's beta collapses and it stops adapting to speed at all.</summary>
        static (Vector3, Quaternion) SlideForward(int i)
        {
            Vector3 rest = Hip + Vector3.down * (Thigh + Shin - 0.02f);
            return (rest + Vector3.forward * (0.35f * T(i)), Quaternion.identity);
        }

        /// <summary>Foot swings 30 cm out to the side. Also near-extended, but it swings the leg's plane —
        /// which is exactly what the knee swivel measures.</summary>
        static (Vector3, Quaternion) SwingOut(int i)
        {
            Vector3 rest = Hip + Vector3.down * (Thigh + Shin - 0.02f);
            return (rest + Vector3.left * (0.30f * T(i)), Quaternion.identity);
        }

        /// <summary>Knee lifted: foot comes up and back, so the leg genuinely BENDS (high conditioning).
        /// The contrast case — here the filter is supposed to recover its responsiveness.</summary>
        static (Vector3, Quaternion) LiftKnee(int i)
        {
            Vector3 rest = Hip + Vector3.down * (Thigh + Shin - 0.02f);
            float t = T(i);
            return (rest + Vector3.up * (0.35f * t) + Vector3.forward * (0.20f * t), Quaternion.identity);
        }

        // --------------------------------------------------------------- the probe

        [Test]
        public void Probe_HowFarDoesTheKneeLagWhenTheFootMoves()
        {
            var sb = new StringBuilder("[LOWER LEG] knee lag vs the ZERO-LAG solve, per smoothing stage.\n"
                                     + "            (foot tracker, no knee tracker, butterfly ON -- the 6-point FBT default)\n\n");

            foreach ((string name, System.Func<int, (Vector3, Quaternion)> motion) in new (string, System.Func<int, (Vector3, Quaternion)>)[]
                     {
                         ("foot SLIDES 35cm forward (leg near-straight)", SlideForward),
                         ("foot SWINGS 30cm out to the side", SwingOut),
                         ("knee LIFTED 35cm (leg genuinely bends)", LiftKnee),
                     })
            {
                Vector3[] rigid = Run(motion, Frames, smoothHint: false, smoothSwivel: false);
                Vector3[] hintOnly = Run(motion, Frames, smoothHint: true, smoothSwivel: false);
                Vector3[] swivelOnly = Run(motion, Frames, smoothHint: false, smoothSwivel: true);
                Vector3[] shipping = Run(motion, Frames, smoothHint: true, smoothSwivel: true);

                sb.AppendLine($"  {name}");
                sb.AppendLine($"      {"stage",-34} {"worst err",9} {"err @ stop",11} {"settle after stop",18}");
                Row(sb, "butterfly hint smoothing ONLY", hintOnly, rigid);
                Row(sb, "swivel One-Euro ONLY", swivelOnly, rigid);
                Row(sb, "BOTH (what ships)", shipping, rigid);
                sb.AppendLine();
            }
            Debug.Log(sb.ToString());
            Assert.Pass();
        }

        static void Row(StringBuilder sb, string label, Vector3[] got, Vector3[] rigid)
        {
            float worst = 0f;
            for (int i = 0; i < got.Length; i++) worst = Mathf.Max(worst, Vector3.Distance(got[i], rigid[i]));

            float atStop = Vector3.Distance(got[StopFrame], rigid[StopFrame]);

            // How long after the foot stops before the knee is within 2 mm of where it ends up.
            Vector3 settled = got[got.Length - 1];
            float restMs = 0f;
            for (int i = got.Length - 1; i >= StopFrame; i--)
                if (Vector3.Distance(got[i], settled) > 0.002f) { restMs = (i + 1 - StopFrame) * Dt * 1000f; break; }

            sb.AppendLine($"      {label,-34} {worst * 100f,7:F2}cm {atStop * 100f,9:F2}cm {restMs,15:F0} ms");
        }

        /// <summary>The mechanism, isolated: what does pole conditioning do to the filter as the leg straightens?</summary>
        [Test]
        public void Probe_WhatDoesPoleConditioningDoToTheFilterWhileStanding()
        {
            var sb = new StringBuilder("[LOWER LEG] SmoothKneeSwivel's effective cutoffs vs leg extension.\n"
                                     + "            beta *= conditioning, minCutoff = lerp(1.0Hz, 1.5Hz, conditioning)\n"
                                     + "            conditioning = sin(angle between thigh and the hip->ankle axis)\n\n");
            sb.AppendLine($"      {"reach",6} {"knee bend",10} {"conditioning",13} {"beta",7} {"minCut",8} {"tau@100deg/s",13}");

            foreach (float reach in new[] { 0.999f, 0.99f, 0.97f, 0.94f, 0.90f, 0.80f, 0.70f })
            {
                float d = reach * (Thigh + Shin);
                // Law of cosines: interior knee angle, then the thigh's angle off the hip->ankle axis.
                float cosKnee = Mathf.Clamp((Thigh * Thigh + Shin * Shin - d * d) / (2f * Thigh * Shin), -1f, 1f);
                float kneeDeg = Mathf.Acos(cosKnee) * Mathf.Rad2Deg;
                float sinHip = Mathf.Clamp01(Shin * Mathf.Sin(Mathf.Acos(cosKnee)) / Mathf.Max(d, 1e-4f));

                float beta = TrackedBeta * sinHip;
                float minCut = Mathf.Lerp(BasisSwivelFilterCore.MinCutoffHz, TrackedMinCutoffHz, sinHip);
                float cutoffAt100 = minCut + beta * 100f;              // 100 deg/s of real swivel motion
                float tau = 1f / (2f * Mathf.PI * cutoffAt100) * 1000f;

                sb.AppendLine($"      {reach,6:F3} {kneeDeg,8:F0}deg {sinHip,13:F3} {beta,7:F3} {minCut,6:F2}Hz {tau,10:F0} ms");
            }
            Debug.Log(sb.ToString());
            Assert.Pass();
        }
    }
}
