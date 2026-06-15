using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Offline sweep of the remote-avatar head-chain forward kinematics (BasisRemoteBoneMath.ComposeHeadChain,
    // the real BasisRemoteBoneJob math): neck→chest→spine plus derived eye/mouth, FK off the networked head
    // pose and scaled T-pose offsets. Pure math, edit mode. Asserts each child composes as
    // parent + headRot*offset, segment lengths are rotation-preserved and scale linearly, and nothing NaNs.
    //
    // NOTE: this covers the head chain. The remote HAND/FOOT end-effector drift (the known sliding) is a
    // play-mode, motion-dependent phenomenon (the limbs are FK'd through TransformAccess from networked
    // rotations off the interpolated hips) — measure that with a play-mode recorder, not an offline sweep.

    public struct BasisRemoteBoneSweepConfig
    {
        public int Cases;
        public static BasisRemoteBoneSweepConfig Default() => new BasisRemoteBoneSweepConfig { Cases = 5000 };
    }

    public struct BasisRemoteBoneSummary
    {
        public bool Ok;
        public string Path;
        public string Error;
        public int Cases;
        public float MaxSegLenErr;    // |child-parent| must equal the rotated offset's length (rotation preserves length)
        public float MaxCompErr;      // child-parent must equal headRot*offset (composition)
        public float MaxScaleErr;     // segment length must scale linearly with world scale
        public int NaNCount;          // outputs must stay finite at extreme/zero scale
    }

    public static class BasisRemoteBoneSweep
    {
        public const string DefaultFileName = "BasisRemoteBoneSweep.csv";
        public static string DefaultPath() => Path.Combine(Application.persistentDataPath, DefaultFileName);

        public static BasisRemoteBoneSummary Run(BasisRemoteBoneSweepConfig cfg, string path)
        {
            var s = new BasisRemoteBoneSummary { Ok = false, Path = path };
            int cases = Mathf.Max(1, cfg.Cases);

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisRemoteBoneSweep " + System.DateTime.UtcNow.ToString("o"));
                    w.WriteLine("case,seg_len_err,comp_err,scale_err,scale,finite");
                    var sb = new StringBuilder(128);

                    for (int i = 0; i < cases; i++)
                    {
                        var rng = new System.Random(9000 + i);
                        float3 headP = RandVec(rng, 2f);
                        quaternion headR = RandRot(rng);
                        float3 neckU = RandVec(rng, 0.3f);
                        float3 chestU = RandVec(rng, 0.3f);
                        float3 spineU = RandVec(rng, 0.3f);
                        float3 eyeU = RandVec(rng, 0.2f);
                        float3 mouthU = RandVec(rng, 0.2f);

                        // Mix in extreme/zero scales to probe finiteness.
                        float scale = (i % 50 == 0) ? 0f : (i % 50 == 1) ? 1e5f : Mathf.Lerp(0.2f, 3f, (float)rng.NextDouble());
                        float3 neck = neckU * scale, chest = chestU * scale, spine = spineU * scale, eye = eyeU * scale, mouth = mouthU * scale;

                        BasisRemoteBoneMath.ComposeHeadChain(headP, headR, neck, chest, spine, eye, mouth,
                            out float3 neckP, out float3 chestP, out float3 spineP, out float3 eyeP, out float3 mouthP);

                        bool finite = IsFinite(neckP) && IsFinite(chestP) && IsFinite(spineP) && IsFinite(eyeP) && IsFinite(mouthP);
                        if (!finite) { s.NaNCount++; WriteRow(w, sb, i, 0, 0, 0, scale, false); continue; }

                        // Beyond human scale, float32 can't hold mm precision (a 1e5x offset is tens of km), so
                        // only finiteness is asserted there; the mm-tolerance metrics apply at human scale.
                        if (scale > 100f) { s.Cases++; if (i < 3) WriteRow(w, sb, i, 0f, 0f, 0f, scale, true); continue; }

                        // Composition: child - parent == headR * offset.
                        float compErr = Mathf.Max(Mathf.Max(
                            math.distance(neckP - headP, math.mul(headR, neck)),
                            math.distance(chestP - neckP, math.mul(headR, chest))),
                            math.distance(spineP - chestP, math.mul(headR, spine)));
                        compErr = Mathf.Max(compErr, Mathf.Max(
                            math.distance(eyeP - headP, math.mul(headR, eye)),
                            math.distance(mouthP - headP, math.mul(headR, mouth))));

                        // Rotation preserves length: |child-parent| == |offset|.
                        float segErr = Mathf.Max(Mathf.Max(
                            Mathf.Abs(math.distance(neckP, headP) - math.length(neck)),
                            Mathf.Abs(math.distance(chestP, neckP) - math.length(chest))),
                            Mathf.Abs(math.distance(spineP, chestP) - math.length(spine)));

                        // Scale linearity: segment length == unscaled length * scale.
                        float scaleErr = Mathf.Abs(math.distance(neckP, headP) - math.length(neckU) * scale);

                        s.MaxSegLenErr = Mathf.Max(s.MaxSegLenErr, segErr);
                        s.MaxCompErr = Mathf.Max(s.MaxCompErr, compErr);
                        s.MaxScaleErr = Mathf.Max(s.MaxScaleErr, scaleErr);
                        s.Cases++;

                        bool fail = compErr > 1e-3f || segErr > 1e-3f || scaleErr > 1e-3f;
                        if (fail || i < 3) WriteRow(w, sb, i, segErr, compErr, scaleErr, scale, true);
                    }
                }
                s.Ok = true;
            }
            catch (System.Exception e)
            {
                s.Ok = false;
                s.Error = e.Message;
            }
            return s;
        }

        private static bool IsFinite(float3 v) => math.all(math.isfinite(v));

        private static void WriteRow(StreamWriter w, StringBuilder sb, int i, float seg, float comp, float scaleErr, float scale, bool finite)
        {
            sb.Clear();
            sb.Append(i).Append(',').Append(F(seg)).Append(',').Append(F(comp)).Append(',').Append(F(scaleErr)).Append(',').Append(F(scale)).Append(',').Append(finite ? 1 : 0);
            w.WriteLine(sb.ToString());
        }

        private static quaternion RandRot(System.Random r)
        {
            var uq = UnityEngine.Quaternion.Euler((float)(r.NextDouble() * 360.0 - 180.0), (float)(r.NextDouble() * 360.0 - 180.0), (float)(r.NextDouble() * 360.0 - 180.0));
            return new quaternion(uq.x, uq.y, uq.z, uq.w);
        }

        private static float3 RandVec(System.Random r, float m) =>
            new float3((float)(r.NextDouble() * 2.0 - 1.0), (float)(r.NextDouble() * 2.0 - 1.0), (float)(r.NextDouble() * 2.0 - 1.0)) * m;

        private static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            if (float.IsInfinity(v)) return "inf";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
