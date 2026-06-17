using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.Tests.IK
{
    /// <summary>
    /// "Legs twist when I stand very still and straight" gate for the leg knee-swivel smoothing
    /// (<see cref="BasisFullIKConstraintJob"/>.SmoothKneeSwivel + <see cref="BasisSwivelFilterCore"/>),
    /// the leg analog of the arm's SmoothElbowSwivel.
    ///
    /// Mechanism: with no foot trackers the foot is a position-only plant (rotation preserved) and the leg
    /// runs near full extension. There the two-bone solver's bend axis is the knee BendNormal (= hips-right,
    /// raw and unsmoothed) and the knee pole follows the (hips-yaw-derived) hint, so any hips-yaw jitter
    /// while standing rolls the near-straight leg about the hip->foot axis -- a visible twist. The fix
    /// low-passes the OUTPUT knee swivel (One-Euro): high-freq jitter is killed at the cutoff floor while a
    /// real turn opens the cutoff and tracks with negligible lag.
    ///
    /// Two layers, both lock-step with the live code:
    ///   - the FILTER gate (synthetic swivel) proves BasisSwivelFilterCore rejects jitter yet tracks a turn,
    ///   - the SOLVER gate drives BasisLegSolveCore with a yawing bend frame at standing extension and shows
    ///     the same filter collapses the resulting raw knee-swivel excursion.
    /// "p2p" = peak-to-peak knee swivel (deg) over the steady window; lower = stiller leg.
    /// </summary>
    public class BasisLegTwistSmoothingTests
    {
        const float Upper = 0.45f, Lower = 0.45f; // equal thigh/shin
        const float HintForward = 0.30f;          // knee hint forward of the knee
        const float Dt = 1f / 90f;                // VR frame time
        static float MaxReach => Upper + Lower;

        static readonly Vector3 Right = Vector3.right;
        static readonly Vector3 Fwd = Vector3.forward;
        static readonly Vector3 Up = Vector3.up;

        // Standing hips-yaw jitter (deg): zero-mean, high frequency -- the residual tracking noise of a
        // player trying to hold still. The derivative low-pass sees ~0 mean velocity so the cutoff stays at
        // its floor and the swivel is heavily smoothed.
        static float Jitter(float t) => 4f * (0.6f * Mathf.Sin(2f * Mathf.PI * 5f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 11f * t + 1.3f));

        // ----------------------------------------------------------------- filter gate (synthetic)

        [Test]
        public void Filter_RejectsStandingJitter()
        {
            // ±4 deg of high-freq swivel jitter, foot/leg otherwise still. The One-Euro must collapse it.
            int steps = 270; // 3 s
            var raw = new List<float>(steps);
            var smooth = new List<float>(steps);
            BasisSwivelFilterState s = default;
            bool seeded = false;
            for (int i = 0; i < steps; i++)
            {
                float t = i * Dt;
                float swivel = Jitter(t);
                if (!seeded) { s = BasisSwivelFilterCore.Seed(swivel); seeded = true; }
                else s = BasisSwivelFilterCore.Step(s, swivel, Dt);
                raw.Add(swivel);
                smooth.Add(s.Smooth);
            }
            float rawP2P = P2P(raw, 45);     // skip 0.5 s settle
            float smoothP2P = P2P(smooth, 45);
            TestContext.WriteLine($"Filter jitter: raw {rawP2P:0.0} deg p2p -> smoothed {smoothP2P:0.0} deg p2p ({smoothP2P / Mathf.Max(rawP2P, 1e-3f):P0})");

            Assert.That(rawP2P, Is.GreaterThan(3f), "synthetic jitter should give a visible raw swivel range (test wiring).");
            Assert.That(smoothP2P, Is.LessThan(rawP2P * 0.4f),
                $"One-Euro should cut standing swivel jitter to <40% of raw; got {smoothP2P:0.0}/{rawP2P:0.0} deg.");
        }

        [Test]
        public void Filter_TracksARealTurn_WithoutFreezing()
        {
            // A genuine body turn (steady yaw) must carry the swivel with the filter -- it must not be
            // mistaken for jitter and frozen. Steady-state lag must stay small.
            const float turnRateDeg = 60f;
            int steps = 180; // 2 s
            var raw = new List<float>(steps);
            var smooth = new List<float>(steps);
            BasisSwivelFilterState s = default;
            bool seeded = false;
            float maxLag = 0f;
            for (int i = 0; i < steps; i++)
            {
                float t = i * Dt;
                float swivel = turnRateDeg * t;
                if (!seeded) { s = BasisSwivelFilterCore.Seed(swivel); seeded = true; }
                else s = BasisSwivelFilterCore.Step(s, swivel, Dt);
                raw.Add(swivel);
                smooth.Add(s.Smooth);
                if (t > 0.5f) maxLag = Mathf.Max(maxLag, Mathf.Abs(swivel - s.Smooth));
            }
            float rawChange = raw[steps - 1] - raw[45];
            float smoothChange = smooth[steps - 1] - smooth[45];
            TestContext.WriteLine($"Filter turn @ {turnRateDeg}deg/s: raw d{rawChange:0.0} -> smoothed d{smoothChange:0.0} deg, steady lag {maxLag:0.0} deg");

            Assert.That(smoothChange, Is.GreaterThan(rawChange * 0.8f),
                $"smoothed swivel must follow a real turn (got {smoothChange:0.0} of {rawChange:0.0} deg) -- not frozen.");
            Assert.That(maxLag, Is.LessThan(20f), $"turn-tracking lag {maxLag:0.0} deg too high (over-smoothed).");
        }

        // ----------------------------------------------------------------- solver gate (BasisLegSolveCore)

        [Test]
        public void Solver_StandingYawJitter_IsSmoothed()
        {
            // Drive the real leg solver at standing extensions with a hips-yaw-jittering bend frame (bend
            // normal + knee hint both yaw, as both derive from the hips when standing). Measure the raw
            // knee-swivel excursion, then the same One-Euro pass; the filter must collapse it where the leg
            // is actually touchy. Thresholds calibrated to the deterministic sweep below -- re-check on first run.
            float worstRaw = 0f, worstRawSmoothed = 0f, worstExt = 0f;
            var rows = new List<(float ext, float raw, float smooth)>();
            foreach (float ext in new[] { 0.94f, 0.96f, 0.97f, 0.98f, 0.99f })
            {
                Drive(ext, Jitter, 270, out float rawP2P, out float smoothP2P, out _);
                rows.Add((ext, rawP2P, smoothP2P));
                if (rawP2P > worstRaw) { worstRaw = rawP2P; worstRawSmoothed = smoothP2P; worstExt = ext; }
            }
            LogRows("standing yaw jitter ±4deg", rows);

            Assert.That(worstRaw, Is.GreaterThan(2f),
                $"standing yaw jitter should visibly roll the near-straight leg somewhere in 0.94..0.99 (worst raw {worstRaw:0.0} deg @ ext {worstExt:0.00}); if not, the bend frame isn't reaching the knee.");
            Assert.That(worstRawSmoothed, Is.LessThan(worstRaw * 0.6f),
                $"knee-swivel smoothing should cut the worst standing twist below 60% of raw (got {worstRawSmoothed:0.0}/{worstRaw:0.0} deg @ ext {worstExt:0.00}).");
        }

        [Test]
        public void Solver_RealTurn_StillTracks()
        {
            // The same solver path under a real turn: the smoothed knee swivel must still follow the body so
            // the legs don't lag when you actually rotate.
            Drive(0.97f, t => 60f * t, 180, out float rawP2P, out float smoothP2P, out float maxLag);
            TestContext.WriteLine($"Solver turn @60deg/s ext0.97: raw {rawP2P:0.0} -> smoothed {smoothP2P:0.0} deg p2p, lag {maxLag:0.0} deg");
            Assert.That(smoothP2P, Is.GreaterThan(rawP2P * 0.7f),
                $"smoothed knee swivel must track a real turn (got {smoothP2P:0.0} of {rawP2P:0.0} deg).");
        }

        [Test]
        public void Characterize_PrintTables()
        {
            var rows = new List<(float ext, float raw, float smooth)>();
            foreach (float ext in new[] { 0.90f, 0.94f, 0.96f, 0.97f, 0.98f, 0.99f, 0.995f })
            {
                Drive(ext, Jitter, 270, out float rawP2P, out float smoothP2P, out _);
                rows.Add((ext, rawP2P, smoothP2P));
            }
            LogRows("knee swivel under ±4deg standing yaw jitter, per standing extension", rows);
            Assert.Pass("see table");
        }

        [Test]
        public void AllOutputs_StayFinite()
        {
            foreach (float ext in new[] { 0.90f, 0.97f, 0.999f })
            {
                Drive(ext, Jitter, 120, out float rawP2P, out float smoothP2P, out float lag);
                Assert.That(IsFinite(rawP2P) && IsFinite(smoothP2P) && IsFinite(lag), Is.True, $"ext {ext}: non-finite metric.");
            }
        }

        // ----------------------------------------------------------------- harness

        // Solve the standing leg over a yawing bend frame, run the live One-Euro on the output knee swivel,
        // and return raw/smoothed peak-to-peak swivel (steady window) plus the worst steady tracking lag.
        static void Drive(float ratio, System.Func<float, float> yawDeg, int steps, out float rawP2P, out float smoothP2P, out float maxLag)
        {
            BuildStanding(ratio, out Vector3 hip, out Vector3 knee, out Vector3 foot);
            Vector3 plant = foot;

            var raw = new List<float>(steps);
            var smooth = new List<float>(steps);
            BasisSwivelFilterState s = default;
            bool seeded = false;
            maxLag = 0f;
            for (int i = 0; i < steps; i++)
            {
                float t = i * Dt;
                Quaternion yaw = Quaternion.AngleAxis(yawDeg(t), Up);

                BasisLegSolveInput li;
                li.Root = hip;
                li.Mid = knee;
                li.Tip = foot;
                li.RootRotation = Quaternion.identity;
                li.MidRotation = Quaternion.identity;
                li.TargetPosition = plant;
                li.TargetRotation = Quaternion.identity;
                li.TargetOffset = Quaternion.identity;
                li.HintPosition = knee + (yaw * Fwd) * HintForward;
                li.HintWeight = 1f;
                li.BendNormal = yaw * Right;

                BasisLegSolveCore.Solve(li, out BasisLegSolveResult r);
                float swivel = ComputeSwivel(hip, r.KneeSolved, plant);
                if (!seeded) { s = BasisSwivelFilterCore.Seed(swivel); seeded = true; }
                else s = BasisSwivelFilterCore.Step(s, swivel, Dt);
                raw.Add(swivel);
                smooth.Add(s.Smooth);
                if (t > 0.5f) maxLag = Mathf.Max(maxLag, Mathf.Abs(swivel - s.Smooth));
            }
            rawP2P = P2P(raw, 45);
            smoothP2P = P2P(smooth, 45);
        }

        // Knee swivel about the hip->foot axis, referenced off forward -- identical to the live SmoothKneeSwivel.
        static float ComputeSwivel(Vector3 hip, Vector3 knee, Vector3 foot)
        {
            Vector3 ac = foot - hip;
            if (ac.sqrMagnitude < 1e-8f) return 0f;
            Vector3 axis = ac.normalized;
            Vector3 refDir = Fwd - axis * Vector3.Dot(Fwd, axis);
            if (refDir.sqrMagnitude < 1e-8f) refDir = Right - axis * Vector3.Dot(Right, axis);
            Vector3 pole = (knee - hip);
            pole -= axis * Vector3.Dot(pole, axis);
            if (refDir.sqrMagnitude < 1e-8f || pole.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.SignedAngle(refDir.normalized, pole, axis);
        }

        static float P2P(List<float> xs, int skip)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int i = skip; i < xs.Count; i++) { min = Mathf.Min(min, xs[i]); max = Mathf.Max(max, xs[i]); }
            return (max >= min) ? max - min : 0f;
        }

        static void BuildStanding(float ratio, out Vector3 hip, out Vector3 knee, out Vector3 foot)
        {
            float chord = ratio * MaxReach;
            foot = Vector3.zero;
            hip = new Vector3(0f, chord, 0f);
            knee = RestKnee(hip, foot);
        }

        static Vector3 RestKnee(Vector3 hip, Vector3 foot)
        {
            Vector3 chord = foot - hip;
            float d = Mathf.Min(chord.magnitude, MaxReach * 0.999f);
            Vector3 along = chord.normalized;
            float proj = (Upper * Upper + d * d - Lower * Lower) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, Upper * Upper - proj * proj));
            Vector3 perp = Vector3.Cross(along, Right).normalized;
            if (Vector3.Dot(perp, Fwd) < 0f) perp = -perp;
            return hip + along * proj + perp * h;
        }

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        static void LogRows(string title, List<(float ext, float raw, float smooth)> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {title} ===");
            sb.AppendLine(" standExt   rawP2P  smoothP2P   ratio");
            foreach (var r in rows)
                sb.AppendLine(string.Format("  {0:0.000}   {1,6:0.0}d  {2,6:0.0}d   {3,5:0.00}", r.ext, r.raw, r.smooth, r.smooth / Mathf.Max(r.raw, 1e-3f)));
            TestContext.WriteLine(sb.ToString());
        }
    }
}
