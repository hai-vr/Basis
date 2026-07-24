using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Basis.IK;
using Basis.IK.Mocap;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    // ⚠ Alias inside the namespace -- com.basis.sdk declares an unrelated global BasisMotionClip.
    using BasisMotionClip = Basis.IK.Mocap.BasisMotionClip;

    /// <summary>
    /// HOW MUCH HUMERAL AXIAL ARC DO REAL PEOPLE ACTUALLY USE, PER ELEVATION BAND?
    ///
    /// ================================================================================================
    /// WHY THIS FILE EXISTS. BasisArmSolveCore.HumeralTwistSoftDeg is a FLAT 120 deg at every elevation,
    /// and its own comment justifies that number with a segmented corpus audit ("<=2.5% in every band")
    /// THAT IS NOT PINNED BY ANY TEST. The number came from an offline sweep that was never landed, so
    /// there has been no baseline to re-run against and no way to referee a proposed change to it.
    ///
    /// A change IS proposed: make the bound elevation-dependent, carrying the shipped 120 at 90 deg
    /// through the clinical glenohumeral arc (~48/71/40 deg total at 45/90/135 deg of abduction) by the
    /// slack factor 120/71 = 1.69, giving 81 / 120 / 68. That is arithmetically self-consistent. But the
    /// slack factor is fitted to a SINGLE anchor point, and one anchor cannot choose between two equally
    /// available models of what the slack IS:
    ///
    ///     MULTIPLICATIVE   whole-complex(e) = 1.69 * GH(e)            ->  +-81 @45,  +-68 @135
    ///     ADDITIVE         whole-complex(e) = GH(e) + constant slack  -> +-108 @45, +-104 @135
    ///
    /// Both reproduce 120 at 90 deg exactly. They disagree by 27-37 deg at the ends -- more than the whole
    /// effect being chased. The girdle slack exists because THIS RIG'S CLAVICLE IS FROZEN and the humerus
    /// absorbs the girdle's share; there is no reason that share should be proportional to GH axial ROM,
    /// and clavicular axial rotation in fact GROWS with elevation, which is a third shape again.
    ///
    /// So this file does not pick a model. It measures the quantity both models are trying to predict --
    /// the axial arc real humans occupy at each elevation -- and lets that referee.
    /// ================================================================================================
    ///
    /// ⭐ THE MEASUREMENT IS BIND-FREE, ON PURPOSE. The shipped guard bounds twist against a T-pose BIND,
    /// and the corpus has no T-pose; synthesising one would make the answer a function of my choice of
    /// neutral rather than of the corpus. So this measures the ARC -- the spread of humeral axial rotation
    /// within an elevation band -- which needs no neutral at all. An arc is exactly what the clinical
    /// figures quote, so it is also the like-for-like comparison.
    ///
    /// ⚠ THE AXIS IS READ FROM POSITIONS, WHICH IS BOTH HONEST AND SELF-LIMITING. With the elbow bent, the
    /// forearm's offset from the humerus axis IS the humerus's axial rotation -- a hinge cannot hide it.
    /// With the elbow straight that offset vanishes and the quantity genuinely does not exist. That is the
    /// SAME singularity BasisElbowAnatomyCore's frame note records the corpus hitting, so the exclusion
    /// below is not a convenience: it is the boundary of what this corpus can referee, and it is asserted
    /// rather than assumed.
    /// </summary>
    public sealed class BasisHumeralTwistElevationCorpusTests
    {
        static string CorpusRoot => Path.GetFullPath("Packages/com.basis.framework/Tests/MocapCorpus~");
        static readonly string[] k_TierDirs = { "", "posture", "dynamic", "slow" };

        /// <summary>Clinical humeral elevation bands. The clinical arc figures the proposal is built from
        /// are quoted at 45 / 90 / 135, so the bands are centred to straddle those.</summary>
        static readonly float[] k_ElevEdges = { 30f, 60f, 90f, 120f, 150f };

        /// <summary>⚠ Below 30 deg and above 150 deg of elevation the humerus is within ~26 deg of the
        /// chest's own up axis, so the reference perpendicular this measurement is built on degenerates.
        /// Those bands are EXCLUDED and reported as excluded rather than silently folded in -- the clinical
        /// figures do not speak there either.</summary>
        const float k_MaxAxisAlignment = 0.90f;

        /// <summary>Elbow flexion admission. The forearm's perpendicular offset is what carries the axial
        /// rotation; below this fraction of the forearm's length it is noise, which is the corpus's
        /// documented blind spot near full extension.</summary>
        const float k_MinPerpFrac = 0.20f;

        struct Sample
        {
            public float ElevDeg;
            public float AxialDeg;
            public int Clip;
        }

        static List<Sample> s_Samples;
        static List<string> s_ClipNames;
        static int s_Rejected, s_Total;

        static int BandOf(float e)
        {
            for (int i = 0; i < k_ElevEdges.Length - 1; i++)
                if (e >= k_ElevEdges[i] && e < k_ElevEdges[i + 1]) return i;
            return -1;
        }
        static string BandName(int b) => $"[{k_ElevEdges[b]:0},{k_ElevEdges[b + 1]:0})";

        static void Load()
        {
            if (s_Samples != null) return;
            s_Samples = new List<Sample>(1 << 19);
            s_ClipNames = new List<string>();

            for (int t = 0; t < k_TierDirs.Length; t++)
            {
                string dir = t == 0 ? CorpusRoot : Path.Combine(CorpusRoot, k_TierDirs[t]);
                if (!Directory.Exists(dir)) continue;
                string[] files = Directory.GetFiles(dir, "*.bvh");
                Array.Sort(files);
                foreach (string f in files)
                {
                    if (!BasisBvhLoader.TryLoad(f, out BasisMotionClip c, out _)) continue;
                    int clip = s_ClipNames.Count;
                    s_ClipNames.Add(Path.GetFileNameWithoutExtension(f));

                    var buf = BasisBodyFrame.Allocate();
                    for (int fi = 0; fi < c.FrameCount; fi++)
                    {
                        BasisBodyFrame frame = BasisBodyFrame.FromClip(c, fi, buf);
                        if (!BasisBodyPlausibility.TryChestFrame(frame, out _, out Vector3 chestUp, out _)) continue;
                        Add(frame, chestUp, clip, false);
                        Add(frame, chestUp, clip, true);
                    }
                }
            }
            if (s_Samples.Count == 0) Assert.Ignore($"no corpus at {CorpusRoot}");
        }

        static void Add(in BasisBodyFrame f, Vector3 chestUp, int clip, bool right)
        {
            BasisMocapJoint sj = right ? BasisMocapJoint.RightUpperArm : BasisMocapJoint.LeftUpperArm;
            BasisMocapJoint ej = right ? BasisMocapJoint.RightLowerArm : BasisMocapJoint.LeftLowerArm;
            BasisMocapJoint hj = right ? BasisMocapJoint.RightHand : BasisMocapJoint.LeftHand;
            if (!f.Has(sj) || !f.Has(ej) || !f.Has(hj)) return;

            Vector3 sh = f.Pos(sj), el = f.Pos(ej), hd = f.Pos(hj);
            Vector3 hum = el - sh;
            Vector3 fore = hd - el;
            if (!(hum.sqrMagnitude > 1e-8f) || !(fore.sqrMagnitude > 1e-8f)) return;

            Vector3 up = chestUp.normalized;
            Vector3 hAxis = hum.normalized;
            s_Total++;

            // Clinical elevation: 0 = arm at the side, 180 = fully overhead.
            float elev = Vector3.Angle(-up, hAxis);
            if (BandOf(elev) < 0) { s_Rejected++; return; }

            // The reference perpendicular degenerates as the humerus approaches the chest's own up axis.
            if (Mathf.Abs(Vector3.Dot(hAxis, up)) > k_MaxAxisAlignment) { s_Rejected++; return; }

            // The forearm's offset from the humerus axis IS the axial rotation. Straight arm -> no offset
            // -> the quantity does not exist. This is the corpus's documented blind spot.
            Vector3 fPerp = fore - hAxis * Vector3.Dot(fore, hAxis);
            if (!(fPerp.magnitude > k_MinPerpFrac * fore.magnitude)) { s_Rejected++; return; }

            Vector3 refPerp = up - hAxis * Vector3.Dot(up, hAxis);
            if (!(refPerp.sqrMagnitude > 1e-8f)) { s_Rejected++; return; }

            // ⚠ atan2, never 2*acos(|dot|) -- see BasisArmNet.PoseChangeDeg. acos has infinite derivative
            // at 1 and puts a 0.1 deg noise floor under a measurement whose whole point is a spread.
            Vector3 a = refPerp.normalized;
            Vector3 b = fPerp.normalized;
            float axial = Mathf.Atan2(Vector3.Dot(Vector3.Cross(a, b), hAxis), Vector3.Dot(a, b)) * Mathf.Rad2Deg;
            if (!right) axial = -axial;   // mirror the left arm so both arms share one sign convention

            s_Samples.Add(new Sample { ElevDeg = elev, AxialDeg = axial, Clip = clip });
        }

        /// <summary>
        /// ⚠⚠ THE OCCUPIED ARC OF A CIRCULAR QUANTITY, NOT A LINEAR PERCENTILE RANGE.
        ///
        /// The axial angle lives on a circle, so p1..p99 of the raw numbers is WRONG the moment the
        /// distribution straddles +-180: a tight cluster sitting on the seam reports an arc of nearly 360
        /// deg. The first run of this file did exactly that and reported 355.6 deg for the [30,60) band --
        /// a number that would have looked like a spectacular finding and was an artefact of the seam.
        ///
        /// The correct statistic is the SMALLEST ARC CONTAINING A GIVEN FRACTION of the samples, which is
        /// seam-invariant by construction: slide a window of k = frac*n over the circularly-sorted angles
        /// and take the narrowest span. Rotating every sample by any constant leaves it unchanged.
        /// </summary>
        static float SmallestArcContaining(List<float> sorted, float frac)
        {
            int n = sorted.Count;
            if (n < 2) return float.NaN;
            int k = Mathf.Clamp(Mathf.CeilToInt(frac * n), 2, n);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int j = i + k - 1;
                float span = j < n ? sorted[j] - sorted[i] : sorted[j - n] + 360f - sorted[i];
                if (span < best) best = span;
            }
            return best;
        }

        /// <summary>
        /// ⭐ THE REFEREE. Per elevation band, the axial arc real humans occupy, against what each candidate
        /// envelope would permit. A bound that sits INSIDE the measured arc is clipping motion people
        /// actually make -- the same test the flat 90 deg bound failed in two bands.
        /// </summary>
        [Test]
        public void HumeralAxialArc_PerElevationBand_RefereesTheProposedEnvelope()
        {
            Load();

            var sb = new StringBuilder();
            sb.AppendLine($"{s_Samples.Count} usable arm-frames of {s_Total} ({100f * s_Rejected / Mathf.Max(s_Total, 1):0.0}% " +
                          "rejected: outside 30-150 deg elevation, humerus near the chest axis, or elbow too straight " +
                          "for the axial angle to exist)");
            sb.AppendLine();
            sb.AppendLine("elev band        n      98% arc      99.9% arc    |  flat 120  mult(81/120/68)  add(108/120/104)");

            int bands = k_ElevEdges.Length - 1;
            var arc99 = new float[bands];
            var n = new int[bands];

            // The two candidate shapes, sampled at each band's centre.
            float[] mult = { 81f, 100f, 120f, 68f };     // 45 / 75 / 105 / 135 deg centres, interpolated
            float[] add = { 108f, 114f, 120f, 104f };

            for (int b = 0; b < bands; b++)
            {
                var vals = new List<float>();
                foreach (Sample s in s_Samples) if (BandOf(s.ElevDeg) == b) vals.Add(s.AxialDeg);
                vals.Sort();
                n[b] = vals.Count;
                if (vals.Count < 500) { sb.AppendLine($"{BandName(b),-12}{vals.Count,8}   (too few to rate)"); continue; }

                arc99[b] = SmallestArcContaining(vals, 0.98f);
                float arc999 = SmallestArcContaining(vals, 0.999f);
                sb.AppendLine($"{BandName(b),-12}{vals.Count,8}{arc99[b],13:0.0}{arc999,14:0.0}   |{240f,9:0}{2f * mult[b],17:0}{2f * add[b],17:0}");
            }

            TestContext.WriteLine("HUMERAL AXIAL ARC OCCUPIED BY REAL MOTION, BY ELEVATION BAND\n" +
                                  "(the guard bounds |twist| against a bind, so a +-X bound permits a 2X arc)\n\n" + sb);

            Assert.That(s_Samples.Count, Is.GreaterThan(20000),
                $"only {s_Samples.Count} usable frames, so this is not measuring the corpus it claims to.");
            for (int b = 0; b < bands; b++)
            {
                Assert.That(n[b], Is.GreaterThan(500),
                    $"elevation band {BandName(b)} has only {n[b]} frames -- the bands must all be populated or " +
                    "this comparison is silently missing the very ends it exists to judge.");
            }

            // ⭐ THE NON-VACUITY THAT MATTERS: the measured arcs must actually DIFFER across bands. If real
            // humans used the same axial arc at every elevation, an elevation-dependent bound would be
            // solving nothing and the flat 120 would be right on its face.
            float minArc = float.MaxValue, maxArc = 0f;
            for (int b = 0; b < bands; b++) { minArc = Mathf.Min(minArc, arc99[b]); maxArc = Mathf.Max(maxArc, arc99[b]); }
            Assert.That(maxArc - minArc, Is.GreaterThan(15f),
                $"the occupied axial arc varies by only {maxArc - minArc:0.0} deg across elevation ({minArc:0.0} to " +
                $"{maxArc:0.0}). If real motion is that flat in elevation, an elevation-dependent envelope is not " +
                "supported by this corpus and BasisArmSolveCore.HumeralTwistSoftDeg should stay flat.");

            // ============================================================================================
            // ⭐ THE SHIPPED FLAT BOUND CLEARS EVERY BAND -- which is the veto its own comment claims and
            // nothing until now re-checked.
            // ============================================================================================
            for (int b = 0; b < bands; b++)
            {
                Assert.That(240f, Is.GreaterThan(arc99[b]),
                    $"the shipped flat +-120 deg bound permits 240 deg of arc but real motion occupies " +
                    $"{arc99[b]:0.0} deg in elevation band {BandName(b)}. The bound is inside the fat of the " +
                    "measured distribution.\n" + sb);
            }

            // ============================================================================================
            // ⛔ AND THE PROPOSED MULTIPLICATIVE ENVELOPE DOES NOT. PINNED SO IT IS NOT RE-PROPOSED.
            //
            // Carrying the shipped 120 @ 90 deg through the clinical GH arc by a constant factor of 1.69
            // gives +-81 at 45 deg of elevation, i.e. 162 deg of permitted arc, against 211.2 deg that real
            // people occupy in [30,60) -- 49 deg INSIDE the fat of the most populated band in the corpus
            // (51,455 frames, half the usable set). That is the same failure the flat 90 deg bound was
            // rejected for, in the same way, found by the same segmentation.
            //
            // ⚠ AND THE CLINICAL CURVE'S SHAPE DOES NOT TRANSFER. The proposal's premise is that ROM peaks
            // at 90 deg and falls off at BOTH ends. The corpus says the occupied whole-complex arc falls
            // MONOTONICALLY with elevation (211 / 183 / 162 / 97) -- it does not peak at 90 at all. That is
            // the frozen-clavicle objection BasisArmSolveCore's comment already makes, now measured: the
            // humerus is absorbing a girdle share that is itself elevation-dependent, so a constant slack
            // factor on the GH curve is the wrong shape however it is scaled.
            //
            // ⭐ WHAT THE CORPUS DOES SUPPORT: headroom at HIGH elevation only. [120,150) occupies 97.4 deg
            // against 240 permitted -- a 2.5x slack, and the one band where tightening is defensible on
            // this evidence. Any future revision should be one-sided in that direction and must re-run
            // this file, not the clinical table.
            // ============================================================================================
            const float k_MultAt45 = 81f;
            Assert.That(2f * k_MultAt45, Is.LessThan(arc99[0]),
                $"the multiplicative proposal (+-{k_MultAt45:0} deg at 45 deg elevation, i.e. {2f * k_MultAt45:0} deg of " +
                $"arc) no longer clips the {arc99[0]:0.0} deg that real motion occupies in band {BandName(0)}. This " +
                "assertion is INVERTED on purpose: it is a fence recording that the proposal was refuted here. If it " +
                "trips, either the corpus, the admission rules or the measurement has changed, and the refutation " +
                "needs re-deriving before anyone re-proposes an elevation-dependent envelope.\n" + sb);
        }

        /// <summary>
        /// ⚠ WHAT THIS CORPUS CANNOT REFEREE, ASSERTED SO IT CANNOT BE FORGOTTEN. The rejection rate is not
        /// incidental: it is the fraction of real motion on which the axial angle does not exist, and any
        /// future constant sized off this file must not be sized off the rejected region. Pinned as a
        /// number so that a change in the corpus, the loader or the admission rule shows up here rather
        /// than quietly widening what this file claims to judge.
        /// </summary>
        [Test]
        public void TheCorpusBlindSpot_IsBoundedAndDeclared()
        {
            Load();
            float rejected = (float)s_Rejected / Mathf.Max(s_Total, 1);
            TestContext.WriteLine($"\n  {s_Total} arm-frames seen, {s_Rejected} rejected ({rejected:P1}); " +
                                  $"{s_Samples.Count} carry a usable humeral axial angle.\n" +
                                  "  Rejection causes: elevation outside 30-150 deg, humerus within ~26 deg of the chest\n" +
                                  "  axis (reference perpendicular degenerate), or elbow too straight for the forearm to\n" +
                                  "  carry the humerus's roll -- the singularity BasisElbowAnatomyCore's frame note records.");

            Assert.That(rejected, Is.LessThan(0.90f),
                $"{rejected:P1} of arm-frames were rejected. Past this the surviving population is a narrow slice of " +
                "real motion and no constant should be sized off it.");
            Assert.That(rejected, Is.GreaterThan(0.02f),
                $"only {rejected:P1} was rejected, which means the admission rules have stopped excluding the " +
                "straight-arm singularity -- the axial angle there is noise, and letting it through would widen " +
                "every arc in the table above with numbers that do not mean anything.");
        }
    }
}
