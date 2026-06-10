using System;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public struct BasisTrajectoryResult
    {
        public string Name;
        public int Steps;
        public float CleanMaxJumpDeg;   // largest single-step output change on SMOOTH motion (= pop magnitude)
        public int Pops;                // steps exceeding popThresh on smooth motion (discontinuities)
        public Vector3 WorstJumpTarget; // hand target where the worst clean pop occurred
        public float NoisyRoughDeg;     // mean |2nd difference| of the output under tracking noise (= jitter)
        public int Zigzags;             // signed-delta reversals under noise (= visible shake)
    }

    // Sweeps one output angle (e.g. elbow / knee swivel) along a CONTINUOUS hand path and reports how
    // smoothly it moves -- the temporal dimension the per-point grid sweeps are blind to. A smooth
    // motion that crosses a solver discontinuity shows up as a "pop"; a steep-but-smooth output turns
    // tracking noise into "rough"/"zigzag". Caller supplies the evaluate delegate (its own solver),
    // so any sweep can reuse this: pass a target -> output-swivel function.
    public static class BasisIKTrajectoryScan
    {
        public static Vector3[] Line(Vector3 a, Vector3 b, int n)
        {
            n = Mathf.Max(2, n);
            var p = new Vector3[n];
            for (int i = 0; i < n; i++) p[i] = Vector3.LerpUnclamped(a, b, i / (float)(n - 1));
            return p;
        }

        public static Vector3[] Circle(Vector3 center, Vector3 u, Vector3 v, float r, int n)
        {
            n = Mathf.Max(3, n);
            var p = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = 2f * Mathf.PI * i / n;
                p[i] = center + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * r;
            }
            return p;
        }

        // eval: hand target -> output swivel (deg), or float.NaN if unreachable/undefined there.
        // noise: per-axis uniform tracking noise (m) added on the jitter pass. seed: deterministic.
        public static BasisTrajectoryResult Scan(string name, Vector3[] path, Func<Vector3, float> eval,
            float noise, int seed, float popThresh = 8f, float zigzagThresh = 1.5f)
        {
            var res = new BasisTrajectoryResult { Name = name, Steps = path != null ? path.Length : 0 };
            if (path == null || path.Length < 2) return res;

            // Clean pass: discontinuities (pops) on smooth motion.
            float prev = float.NaN, maxJump = 0f;
            int pops = 0; Vector3 worst = Vector3.zero;
            for (int i = 0; i < path.Length; i++)
            {
                float s = eval(path[i]);
                if (!float.IsNaN(s) && !float.IsNaN(prev))
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(prev, s));
                    if (d > maxJump) { maxJump = d; worst = path[i]; }
                    if (d > popThresh) pops++;
                }
                prev = s;
            }

            // Noisy pass: jitter (roughness / zigzag) under tracking noise.
            var rng = new System.Random(seed);
            float p1 = float.NaN, sdPrev = float.NaN;
            double roughSum = 0.0; int roughN = 0, zz = 0;
            for (int i = 0; i < path.Length; i++)
            {
                Vector3 t = path[i];
                if (noise > 0f)
                {
                    t.x += (float)(rng.NextDouble() * 2.0 - 1.0) * noise;
                    t.y += (float)(rng.NextDouble() * 2.0 - 1.0) * noise;
                    t.z += (float)(rng.NextDouble() * 2.0 - 1.0) * noise;
                }
                float s = eval(t);
                if (!float.IsNaN(s) && !float.IsNaN(p1))
                {
                    float sd = Mathf.DeltaAngle(p1, s);
                    if (!float.IsNaN(sdPrev))
                    {
                        roughSum += Mathf.Abs(sd - sdPrev);
                        roughN++;
                        if (sd * sdPrev < 0f && Mathf.Abs(sd) > zigzagThresh && Mathf.Abs(sdPrev) > zigzagThresh) zz++;
                    }
                    sdPrev = sd;
                }
                else sdPrev = float.NaN;
                p1 = s;
            }

            res.CleanMaxJumpDeg = maxJump;
            res.Pops = pops;
            res.WorstJumpTarget = worst;
            res.NoisyRoughDeg = roughN > 0 ? (float)(roughSum / roughN) : 0f;
            res.Zigzags = zz;
            return res;
        }
    }
}
