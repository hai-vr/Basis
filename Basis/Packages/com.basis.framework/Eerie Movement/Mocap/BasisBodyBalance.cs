#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Basis.IK.Mocap
{
    // IS THE SOLVED POSE ONE A HUMAN COULD ACTUALLY STAND IN?
    //
    // Every other accuracy layer in this harness scores joints INDEPENDENTLY: BasisMocapAccuracy asks how far
    // the elbow is from a real elbow, BasisMocapFootQuality asks whether the feet walk like feet. A pose can be
    // 2 cm accurate at every single joint and still be one that falls over, because "2 cm at the shoulder,
    // 2 cm at the chest, 2 cm at the hips" can all lean the SAME WAY, and a per-joint mean averages that away
    // by construction. That is the tortoise/lean/crouch error family, and nothing in this project measured it.
    //
    // The physical statement is old and simple: a body is statically balanced when the vertical through its
    // centre of mass falls inside the BASE OF SUPPORT -- the convex hull of whatever is touching the ground.
    // Outside it, there is a net toppling moment about the edge of the support polygon and the human falls.
    //
    // ── THE ASSUMPTION THIS WHOLE LAYER RESTS ON ──────────────────────────────────────────────────────────
    // THE ONLY CONTACTS ARE THE FEET. A person sitting on a chair, leaning on a wall, or kneeling has a base
    // of support this code cannot see, and will measure as "falling over" while being perfectly stable. The
    // corpus has sit clips, so this is not hypothetical -- it is the expected residual in the posture tier and
    // is why the tests here read MEDIANS and FRACTIONS rather than demanding every frame pass. Do not "fix"
    // that residual by loosening the metric; the metric is right and the antecedent is false on those frames.
    //
    // ── SEGMENT MASS MODEL ────────────────────────────────────────────────────────────────────────────────
    // Dempster (1955), as tabulated in Winter, "Biomechanics and Motor Control of Human Movement", 4th ed.,
    // Table 4.1. Mass is expressed as a fraction of total body mass; the segment centre of mass sits at a
    // fixed fraction of the segment's length measured FROM THE PROXIMAL end. The table is used as published --
    // the fractions below sum to exactly 1.000, which MassSum asserts at runtime, because a silently
    // unnormalised mass model would bias the whole-body CoM toward whichever segment got dropped and every
    // number this file produces would be quietly wrong in the same direction.
    //
    // The trunk is carried as Winter's two SEGMENTED rows rather than the single lumped 0.497 row:
    //     pelvis 0.142 + abdomen 0.139 = 0.281   (hip joint centre -> chest)
    //     thorax 0.216                           (chest -> glenohumeral centre)
    //     0.281 + 0.216 = 0.497, i.e. exactly the lumped row, split at a joint the skeleton actually has.
    // This matters for the error family this file exists to catch. A single rigid hip->shoulder chord cannot
    // represent a curved spine: fold at the waist and the true trunk CoM travels FORWARD of the chord, so a
    // lumped trunk systematically UNDER-reports forward lean -- it would err toward passing exactly the poses
    // we are hunting. Splitting at the chest lets the trunk bend. Within each sub-segment the CoM is placed at
    // the midpoint, which is a documented simplification: Winter defines the sub-segment CoM fractions against
    // vertebral landmarks (L4-L5, T12-L1) that no humanoid rig exposes, and inventing a mapping for them would
    // be a worse error than the second-order one the midpoint costs.
    //
    // ── DYNAMIC BALANCE ───────────────────────────────────────────────────────────────────────────────────
    // Walking is a controlled fall: during single support the CoM is LEGITIMATELY outside the base of support,
    // and a static gate would fail every real human on every walking clip. The literature answer is the
    // extrapolated centre of mass (Hof, van Bokhoven & Bakker 2005, "The condition for dynamic stability",
    // J Biomech 38:1-8): the body is dynamically stable when
    //     XCoM = CoM_horizontal + v_horizontal / w0,    w0 = sqrt(g / l)
    // lies inside the base of support, where l is the pendulum length (CoM height above the floor). At zero
    // velocity XCoM collapses to the CoM and the criterion collapses to the static one, which is the property
    // that makes it safe to report both from the same code path.
    //
    // BOTH are measured. The static margin is reported on QUASI-STATIC frames only (the CoM barely moving),
    // where it is the honest question; the dynamic margin is reported on every supported frame.
    //
    // ── SCALE ─────────────────────────────────────────────────────────────────────────────────────────────
    // Margins are divided by FOOT LENGTH, so a child avatar and a giant get the same number for the same
    // posture -- the same scale-free convention BasisMocapFootQuality uses (fractions of leg length) and for
    // the same reason. Clips arrive already normalised to BasisBvhLoader.TargetLegLengthMetres.
    public struct BasisBalanceSummary
    {
        public bool Ok;
        public string Error;
        public string Clip;
        public int Frames;

        public float LegLen;        // metres, hip->ankle, normalised to ~0.85 by the loader
        public float FootLen;       // metres, heel to toe tip, inferred from the ankle->MTP bone
        public float MassSum;       // must be 1.000; a self-check on the segment table

        /// <summary>Whole-body CoM height over head-joint height, at the clip's tallest-head frame, both
        /// measured from the floor. Dempster puts a standing human's CoM at 0.553 of stature; the head JOINT
        /// is not the top of the head, so the expected ratio here is around 0.6. This is the cheapest
        /// independent check that the mass model is not nonsense -- drop the trunk and it collapses, put the
        /// head mass in the trunk and it sags. It is checked by the tests against a literature band, not
        /// against a number this file produced.</summary>
        public float ComHeightFracHead;

        /// <summary>The SAME height as a fraction of estimated STATURE, which is the form the literature
        /// states and therefore the one that can be checked against it rather than against ourselves. Stature
        /// is inferred from a different Winter row than the mass table (greater trochanter 0.530H, ankle
        /// 0.039H, so the hip-to-ankle span this skeleton measures is 0.491H) -- so agreement with Dempster's
        /// 0.553 is an INDEPENDENT confirmation of the segment masses, not a restatement of them.
        /// Measured across the corpus: 0.544 root, 0.539 posture, 0.551 dynamic, 0.547 slow.</summary>
        public float ComHeightFracStature;

        public int SupportedFrames;      // at least one foot in contact
        public int QuasiStaticFrames;    // supported AND the CoM is barely moving

        // Static margin, quasi-static frames only. Units: fractions of foot length, positive = inside.
        public float QsMarginMedian, QsMarginP05, QsMarginMin;
        public float QsBalancedFrac;     // fraction of quasi-static frames with a positive margin

        // Dynamic (XCoM) margin, every supported frame.
        public float DynMarginMedian, DynMarginP05;
        public float DynBalancedFrac;

        public float StaticMarginMedianAll;   // static margin over every supported frame, walking included

        public float VHatMean, VHatP95;       // CoM speed / sqrt(g L), the dimensionless speed
        public float DoubleSupportFrac, SingleSupportFrac, FlightFrac;
    }

    /// <summary>Per-frame detail, so a bad clip can be LOCALISED instead of theorised about -- the same reason
    /// BasisMocapAccuracy.BasisMocapTracks exists. Optional; pass null when only the summary is wanted.</summary>
    public sealed class BasisBalanceTracks
    {
        public Vector3[] CoM;
        public float[] StaticMarginFrac;    // NaN where unsupported
        public float[] DynamicMarginFrac;   // NaN where unsupported
        public float[] VHat;
        public bool[] Supported;
        public bool[] QuasiStatic;
        public byte[] ContactCount;
    }

    public static class BasisBodyBalance
    {
        public const float G = 9.81f;

        // ── Dempster/Winter Table 4.1: mass as a fraction of total body mass ──
        public const float MassHeadNeck = 0.081f;
        public const float MassTrunkLower = 0.281f;   // pelvis 0.142 + abdomen 0.139
        public const float MassTrunkUpper = 0.216f;   // thorax
        public const float MassUpperArm = 0.028f;
        public const float MassForearm = 0.016f;
        public const float MassHand = 0.006f;
        public const float MassThigh = 0.100f;
        public const float MassShank = 0.0465f;
        public const float MassFoot = 0.0145f;

        // ── Table 4.1: segment CoM as a fraction of segment length, from the PROXIMAL end ──
        public const float ComUpperArm = 0.436f;
        public const float ComForearm = 0.430f;
        public const float ComThigh = 0.433f;
        public const float ComShank = 0.433f;
        public const float ComFoot = 0.500f;
        // Head and neck: Winter's row runs C7-T1 to the ear canal with the CoM at 1.000 from proximal, i.e.
        // effectively AT the distal landmark. Mapped onto Neck -> Head that puts the head mass at the head
        // joint. The head joint sits a little below the ear canal, so this is a few centimetres low on 8.1%
        // of body mass -- under 3 mm of whole-body CoM, which is far inside the noise of everything else here.
        public const float ComHeadNeck = 1.000f;
        // Trunk sub-segments: midpoint, for the landmark reason set out in the header.
        public const float ComTrunk = 0.500f;

        /// <summary>The hand has no distal joint in BasisMocapJoint, so its CoM is placed at the wrist rather
        /// than half a hand further on. The hand is 0.6% of body mass over a ~9 cm lever, so the whole-body CoM
        /// error this costs is bounded by 0.6 mm per hand -- stated as a bound rather than hand-waved, because
        /// an unbounded approximation in a mass model is how a metric starts lying.</summary>
        public const float HandComAtWrist = 1f;

        public static readonly float MassTotal =
            MassHeadNeck + MassTrunkLower + MassTrunkUpper
            + 2f * (MassUpperArm + MassForearm + MassHand)
            + 2f * (MassThigh + MassShank + MassFoot);

        // ── foot polygon geometry ──
        // Winter's anthropometric figure (Fig 4.1, after Drillis & Contini): foot length 0.152H, foot breadth
        // 0.055H, so breadth is 0.36 of length and the half-width is 0.18 of it. The ankle sits about a quarter
        // of the way along the foot from the heel and the toe BASE (which is what a BVH ToeBase joint is -- the
        // metatarsophalangeal line, not the toe tip) about three quarters, so the ankle->MTP bone spans half
        // the foot: heel is half that bone behind the ankle, toe tip half that bone beyond the MTP.
        public const float HeelBehindAnkleFracMtp = 0.5f;
        public const float ToeAheadMtpFracMtp = 0.5f;
        public const float HalfWidthFracFoot = 0.18f;

        /// <summary>Winter's anthropometric figure again: the greater trochanter sits at 0.530 of stature and
        /// the ankle at 0.039, so the hip-to-ankle span this skeleton measures spans 0.491 of stature. Used
        /// ONLY to restate the CoM height in the units the literature quotes it in.</summary>
        public const float HipToAnkleFracStature = 0.491f;

        /// <summary>Dempster's standing whole-body CoM height, as a fraction of stature. Never used as an
        /// input -- it is the value ComHeightFracStature is CHECKED against.</summary>
        public const float DempsterComHeightFracStature = 0.553f;

        /// <summary>Quasi-static threshold on the dimensionless CoM speed v / sqrt(g L). 0.05 is about
        /// 0.14 m/s on an 0.85 m leg -- a standing sway or a slow weight shift, not a step. Above this the
        /// static criterion is simply the wrong question and the XCoM one is asked instead.</summary>
        public const float QuasiStaticVHat = 0.05f;

        // ── contact detection, MIRRORED from BasisMocapFootQuality.Contacts ──
        // Same thresholds, same hysteresis, deliberately identical so the two layers cannot disagree about
        // what "planted" means. It is a mirror rather than a call only because the original is private; if it
        // is ever made public, delete this and call it, and until then RETUNE BOTH.
        //
        // The floor is PER FOOT and is a low percentile of that foot's own height, never a global ground plane.
        // That is not a style choice: a global floor is dragged down by drift and by the single lowest outlier
        // anywhere in the clip, and it made whole clips read as "never planted".
        public const float ContactHeightFracLeg = 0.08f;
        public const float ContactSpeedFracVHat = 0.25f;
        public const int ContactFloorPercentile = 3;

        public static BasisBalanceSummary Run(BasisMotionClip clip) => Run(clip, null, null);

        public static BasisBalanceSummary Run(BasisMotionClip clip, Vector3[] positions) => Run(clip, positions, null);

        /// <summary>
        /// Score a clip. <paramref name="positions"/> is an optional FrameCount * BasisMocapJoint.Count array
        /// of joint positions to use INSTEAD of the clip's own -- that is how a SOLVED pose gets measured
        /// against the same ruler as the real one. Pass null to score the real human.
        /// </summary>
        public static BasisBalanceSummary Run(BasisMotionClip clip, Vector3[] positions, BasisBalanceTracks tracks)
        {
            var s = new BasisBalanceSummary { Ok = false, Clip = clip?.Name };

            if (clip == null || clip.FrameCount < 8) { s.Error = "clip too short"; return s; }
            foreach (BasisMocapJoint j in RequiredJoints)
            {
                if (!clip.Has(j)) { s.Error = $"clip is missing {j}"; return s; }
            }

            int n = clip.FrameCount;
            int jc = (int)BasisMocapJoint.Count;
            if (positions != null && positions.Length != n * jc)
            {
                s.Error = $"positions override is {positions.Length} long, expected {n * jc}";
                return s;
            }

            s.Frames = n;
            s.MassSum = MassTotal;
            float dt = clip.FrameTime;
            if (!(dt > 1e-6f)) { s.Error = "clip has no frame time"; return s; }

            Vector3 P(int f, BasisMocapJoint j) => positions != null ? positions[f * jc + (int)j] : clip.Get(f, j).Position;
            // Converted ONCE. A local function converted to a delegate allocates at every conversion, and this
            // one would otherwise be converted inside a per-frame loop.
            Sample sample = P;

            // Bone lengths are rigid, so measure them once. Frame 0 matches how BasisMocapFootQuality calibrates.
            float thigh = Vector3.Distance(P(0, BasisMocapJoint.LeftUpperLeg), P(0, BasisMocapJoint.LeftLowerLeg));
            float shin = Vector3.Distance(P(0, BasisMocapJoint.LeftLowerLeg), P(0, BasisMocapJoint.LeftFoot));
            float rThigh = Vector3.Distance(P(0, BasisMocapJoint.RightUpperLeg), P(0, BasisMocapJoint.RightLowerLeg));
            float rShin = Vector3.Distance(P(0, BasisMocapJoint.RightLowerLeg), P(0, BasisMocapJoint.RightFoot));
            float legLen = 0.5f * ((thigh + shin) + (rThigh + rShin));
            if (!(legLen > 1e-3f)) { s.Error = "degenerate leg length"; return s; }
            s.LegLen = legLen;

            float mtpL = Vector3.Distance(P(0, BasisMocapJoint.LeftFoot), P(0, BasisMocapJoint.LeftToes));
            float mtpR = Vector3.Distance(P(0, BasisMocapJoint.RightFoot), P(0, BasisMocapJoint.RightToes));
            float mtp = 0.5f * (mtpL + mtpR);
            if (!(mtp > 1e-4f)) { s.Error = "degenerate ankle->toe bone"; return s; }
            float footLen = mtp * (1f + HeelBehindAnkleFracMtp + ToeAheadMtpFracMtp);
            s.FootLen = footLen;
            float halfWidth = HalfWidthFracFoot * footLen;

            // ── centre of mass, per frame ──
            var com = new Vector3[n];
            for (int f = 0; f < n; f++) com[f] = CentreOfMass(sample, f);

            // ── contacts, per foot, against that foot's OWN floor ──
            bool[] contactL = Contacts(sample, n, dt, legLen, BasisMocapJoint.LeftFoot, BasisMocapJoint.LeftToes, out float floorL);
            bool[] contactR = Contacts(sample, n, dt, legLen, BasisMocapJoint.RightFoot, BasisMocapJoint.RightToes, out float floorR);
            float floor = Mathf.Min(floorL, floorR);

            // ── CoM velocity, central difference over a fixed 20 ms window ──
            // Fixed in SECONDS, not frames, so a 120 Hz clip and a 60 Hz clip get the same answer. A raw
            // one-frame difference at 120 Hz puts several centimetres of noise straight into XCoM.
            int half = Mathf.Max(1, Mathf.RoundToInt(0.02f / dt));
            float sqrtGL = Mathf.Sqrt(G * legLen);

            var qsMargin = new List<float>();
            var dynMargin = new List<float>();
            var allStatic = new List<float>();
            var vhats = new List<float>();
            int dbl = 0, single = 0, flight = 0, qsBalanced = 0, dynBalanced = 0;

            if (tracks != null)
            {
                tracks.CoM = com;
                tracks.StaticMarginFrac = new float[n];
                tracks.DynamicMarginFrac = new float[n];
                tracks.VHat = new float[n];
                tracks.Supported = new bool[n];
                tracks.QuasiStatic = new bool[n];
                tracks.ContactCount = new byte[n];
            }

            // Two feet, four corners each. The hull buffer is deliberately 2n: monotone chain pushes the lower
            // and upper chains into one array before trimming the shared endpoint.
            var pts = new Vector2[8];
            var hull = new Vector2[16];

            for (int f = 0; f < n; f++)
            {
                int a = Mathf.Max(f - half, 0), b = Mathf.Min(f + half, n - 1);
                Vector3 dv = com[b] - com[a];
                var vel = new Vector2(dv.x, dv.z) / ((b - a) * dt);
                float vhat = vel.magnitude / sqrtGL;
                vhats.Add(vhat);

                int contacts = (contactL[f] ? 1 : 0) + (contactR[f] ? 1 : 0);
                if (contacts == 2) dbl++; else if (contacts == 1) single++; else flight++;

                if (tracks != null) { tracks.VHat[f] = vhat; tracks.ContactCount[f] = (byte)contacts; }

                if (contacts == 0)
                {
                    // No feet down: the base of support is empty and the margin is not merely large and
                    // negative, it is UNDEFINED. Report it as such rather than inventing a number -- a
                    // fabricated value here would pollute every percentile below.
                    if (tracks != null) { tracks.StaticMarginFrac[f] = float.NaN; tracks.DynamicMarginFrac[f] = float.NaN; }
                    continue;
                }

                int np = 0;
                if (contactL[f]) np = AppendFootPolygon(sample, f, BasisMocapJoint.LeftFoot, BasisMocapJoint.LeftToes, halfWidth, pts, np);
                if (contactR[f]) np = AppendFootPolygon(sample, f, BasisMocapJoint.RightFoot, BasisMocapJoint.RightToes, halfWidth, pts, np);
                int nh = ConvexHull(pts, np, hull);

                var comXZ = new Vector2(com[f].x, com[f].z);
                float mStatic = SignedDistanceToConvex(comXZ, hull, nh) / footLen;

                // XCoM. The pendulum length is the CoM's height over the floor; clamp it off zero so a clip
                // whose floor estimate sits above the CoM (a fall, a lie-down) cannot produce an infinite w0.
                float l = Mathf.Max(0.1f * legLen, com[f].y - floor);
                float w0 = Mathf.Sqrt(G / l);
                Vector2 xcom = comXZ + vel / w0;
                float mDyn = SignedDistanceToConvex(xcom, hull, nh) / footLen;

                s.SupportedFrames++;
                allStatic.Add(mStatic);
                dynMargin.Add(mDyn);
                if (mDyn > 0f) dynBalanced++;

                bool quasiStatic = vhat < QuasiStaticVHat;
                if (quasiStatic)
                {
                    s.QuasiStaticFrames++;
                    qsMargin.Add(mStatic);
                    if (mStatic > 0f) qsBalanced++;
                }

                if (tracks != null)
                {
                    tracks.StaticMarginFrac[f] = mStatic;
                    tracks.DynamicMarginFrac[f] = mDyn;
                    tracks.Supported[f] = true;
                    tracks.QuasiStatic[f] = quasiStatic;
                }
            }

            s.DoubleSupportFrac = (float)dbl / n;
            s.SingleSupportFrac = (float)single / n;
            s.FlightFrac = (float)flight / n;

            s.QsMarginMedian = Percentile(qsMargin, 50);
            s.QsMarginP05 = Percentile(qsMargin, 5);
            s.QsMarginMin = Percentile(qsMargin, 0);
            s.QsBalancedFrac = s.QuasiStaticFrames > 0 ? (float)qsBalanced / s.QuasiStaticFrames : float.NaN;

            s.DynMarginMedian = Percentile(dynMargin, 50);
            s.DynMarginP05 = Percentile(dynMargin, 5);
            s.DynBalancedFrac = s.SupportedFrames > 0 ? (float)dynBalanced / s.SupportedFrames : float.NaN;

            s.StaticMarginMedianAll = Percentile(allStatic, 50);
            s.VHatMean = Mean(vhats);
            s.VHatP95 = Percentile(vhats, 95);

            // The mass-model sanity ratio, taken at the clip's most upright frame (highest head), which is the
            // only frame where "a standing human's CoM is at 0.553 of stature" is a statement about this clip.
            int tallest = 0;
            float best = float.MinValue;
            for (int f = 0; f < n; f++)
            {
                float hy = P(f, BasisMocapJoint.Head).y;
                if (hy > best) { best = hy; tallest = f; }
            }
            float headOverFloor = P(tallest, BasisMocapJoint.Head).y - floor;
            s.ComHeightFracHead = headOverFloor > 1e-3f ? (com[tallest].y - floor) / headOverFloor : float.NaN;
            s.ComHeightFracStature = (com[tallest].y - floor) / (legLen / HipToAnkleFracStature);

            s.Ok = true;
            return s;
        }

        static readonly BasisMocapJoint[] RequiredJoints =
        {
            BasisMocapJoint.Hips, BasisMocapJoint.Chest, BasisMocapJoint.Neck, BasisMocapJoint.Head,
            BasisMocapJoint.LeftUpperArm, BasisMocapJoint.LeftLowerArm, BasisMocapJoint.LeftHand,
            BasisMocapJoint.RightUpperArm, BasisMocapJoint.RightLowerArm, BasisMocapJoint.RightHand,
            BasisMocapJoint.LeftUpperLeg, BasisMocapJoint.LeftLowerLeg, BasisMocapJoint.LeftFoot, BasisMocapJoint.LeftToes,
            BasisMocapJoint.RightUpperLeg, BasisMocapJoint.RightLowerLeg, BasisMocapJoint.RightFoot, BasisMocapJoint.RightToes,
        };

        delegate Vector3 Sample(int frame, BasisMocapJoint joint);

        /// <summary>Whole-body centre of mass for one frame of a clip. The public entry point, for callers that
        /// want the CoM without the whole balance run.</summary>
        public static Vector3 CentreOfMass(BasisMotionClip clip, int frame)
            => CentreOfMass((f, j) => clip.Get(f, j).Position, frame);

        /// <summary>Whole-body centre of mass from an explicit FrameCount * JointCount position array.</summary>
        public static Vector3 CentreOfMass(Vector3[] positions, int frame)
        {
            int jc = (int)BasisMocapJoint.Count;
            return CentreOfMass((f, j) => positions[f * jc + (int)j], frame);
        }

        static Vector3 CentreOfMass(Sample p, int f)
        {
            Vector3 hipCentre = 0.5f * (p(f, BasisMocapJoint.LeftUpperLeg) + p(f, BasisMocapJoint.RightUpperLeg));
            Vector3 shoulderCentre = 0.5f * (p(f, BasisMocapJoint.LeftUpperArm) + p(f, BasisMocapJoint.RightUpperArm));
            Vector3 chest = p(f, BasisMocapJoint.Chest);

            Vector3 m = Vector3.zero;

            // Trunk, split at the chest so a bending spine actually moves the mass. Winter's proximal landmark
            // for the trunk is the greater trochanter and the distal one is the glenohumeral joint; in this
            // skeleton those ARE the upper-leg and upper-arm joints, so no landmark is being invented.
            m += MassTrunkLower * Vector3.Lerp(hipCentre, chest, ComTrunk);
            m += MassTrunkUpper * Vector3.Lerp(chest, shoulderCentre, ComTrunk);

            m += MassHeadNeck * Vector3.Lerp(p(f, BasisMocapJoint.Neck), p(f, BasisMocapJoint.Head), ComHeadNeck);

            m += Limb(p, f, BasisMocapJoint.LeftUpperArm, BasisMocapJoint.LeftLowerArm, BasisMocapJoint.LeftHand,
                      MassUpperArm, ComUpperArm, MassForearm, ComForearm);
            m += Limb(p, f, BasisMocapJoint.RightUpperArm, BasisMocapJoint.RightLowerArm, BasisMocapJoint.RightHand,
                      MassUpperArm, ComUpperArm, MassForearm, ComForearm);
            m += MassHand * p(f, BasisMocapJoint.LeftHand);
            m += MassHand * p(f, BasisMocapJoint.RightHand);

            m += Limb(p, f, BasisMocapJoint.LeftUpperLeg, BasisMocapJoint.LeftLowerLeg, BasisMocapJoint.LeftFoot,
                      MassThigh, ComThigh, MassShank, ComShank);
            m += Limb(p, f, BasisMocapJoint.RightUpperLeg, BasisMocapJoint.RightLowerLeg, BasisMocapJoint.RightFoot,
                      MassThigh, ComThigh, MassShank, ComShank);
            m += MassFoot * Vector3.Lerp(p(f, BasisMocapJoint.LeftFoot), p(f, BasisMocapJoint.LeftToes), ComFoot);
            m += MassFoot * Vector3.Lerp(p(f, BasisMocapJoint.RightFoot), p(f, BasisMocapJoint.RightToes), ComFoot);

            // The fractions sum to 1.000 by construction, so this division is a no-op that documents itself --
            // and stops a future edit to the table from silently rescaling the body.
            return m / MassTotal;
        }

        static Vector3 Limb(Sample p, int f, BasisMocapJoint root, BasisMocapJoint mid, BasisMocapJoint tip,
                            float massUpper, float comUpper, float massLower, float comLower)
        {
            Vector3 a = p(f, root), b = p(f, mid), c = p(f, tip);
            return massUpper * Vector3.Lerp(a, b, comUpper) + massLower * Vector3.Lerp(b, c, comLower);
        }

        // ───────────────────────── base of support ─────────────────────────

        /// <summary>
        /// One foot's ground footprint: a rectangle from the heel to the toe tip, laid along the foot's own
        /// horizontal axis. Its LENGTH is taken from the projected ankle->MTP vector rather than the rigid bone,
        /// so a pitched foot (heel up in push-off) correctly reports a shorter footprint; its WIDTH is taken
        /// from the rigid foot length, because a foot does not get narrower when it tilts.
        /// </summary>
        static int AppendFootPolygon(Sample p, int f, BasisMocapJoint ankleJoint, BasisMocapJoint toeJoint,
                                     float halfWidth, Vector2[] into, int at)
        {
            Vector3 ankle3 = p(f, ankleJoint), toe3 = p(f, toeJoint);
            var ankle = new Vector2(ankle3.x, ankle3.z);
            var toe = new Vector2(toe3.x, toe3.z);
            Vector2 along = toe - ankle;
            float d = along.magnitude;

            Vector2 axis;
            if (d > 1e-4f)
            {
                axis = along / d;
            }
            else
            {
                // Foot dead vertical (a full tip-toe): there is no horizontal axis to read, so fall back to the
                // body's facing rather than to an arbitrary constant, which would rotate the footprint relative
                // to a turning body and make the margin depend on which way the clip happens to face.
                //
                // Built from POSITIONS, because the override path carries no rotations. The hip sockets give
                // the lateral axis; Unity is left-handed, so forward = Cross(right, up), which in the ground
                // plane is (x, z) -> (-z, x).
                Vector3 hipAxis = p(f, BasisMocapJoint.RightUpperLeg) - p(f, BasisMocapJoint.LeftUpperLeg);
                var right = new Vector2(hipAxis.x, hipAxis.z);
                axis = right.sqrMagnitude > 1e-8f ? new Vector2(-right.y, right.x).normalized : Vector2.up;
            }
            var perp = new Vector2(-axis.y, axis.x);

            Vector2 back = ankle - axis * (HeelBehindAnkleFracMtp * d);
            Vector2 front = toe + axis * (ToeAheadMtpFracMtp * d);

            into[at + 0] = back - perp * halfWidth;
            into[at + 1] = back + perp * halfWidth;
            into[at + 2] = front + perp * halfWidth;
            into[at + 3] = front - perp * halfWidth;
            return at + 4;
        }

        /// <summary>Andrew's monotone chain. Small n (4 or 8), so the sort is a plain insertion sort and there
        /// is no allocation anywhere in the per-frame path.</summary>
        public static int ConvexHull(Vector2[] pts, int n, Vector2[] hull)
        {
            if (n <= 0) return 0;
            for (int i = 1; i < n; i++)
            {
                Vector2 key = pts[i];
                int j = i - 1;
                while (j >= 0 && (pts[j].x > key.x || (pts[j].x == key.x && pts[j].y > key.y))) { pts[j + 1] = pts[j]; j--; }
                pts[j + 1] = key;
            }

            int k = 0;
            for (int i = 0; i < n; i++)
            {
                while (k >= 2 && Cross(hull[k - 2], hull[k - 1], pts[i]) <= 0f) k--;
                hull[k++] = pts[i];
            }
            int lower = k + 1;
            for (int i = n - 2; i >= 0; i--)
            {
                while (k >= lower && Cross(hull[k - 2], hull[k - 1], pts[i]) <= 0f) k--;
                hull[k++] = pts[i];
            }
            return Mathf.Max(k - 1, 1);
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>
        /// Signed distance from a point to a CONVEX polygon: positive inside (distance to the nearest edge),
        /// negative outside (distance to the nearest edge segment). Orientation is read from the polygon's own
        /// signed area rather than assumed, so a hull built in either winding gives the same answer -- an
        /// assumed winding is the kind of thing that silently inverts a metric.
        /// </summary>
        public static float SignedDistanceToConvex(Vector2 p, Vector2[] hull, int n)
        {
            if (n <= 0) return float.NaN;
            if (n == 1) return -Vector2.Distance(p, hull[0]);
            if (n == 2) return -DistanceToSegment(p, hull[0], hull[1]);

            float area2 = 0f;
            for (int i = 0, j = n - 1; i < n; j = i++) area2 += hull[j].x * hull[i].y - hull[i].x * hull[j].y;
            float sgn = area2 >= 0f ? 1f : -1f;

            bool inside = true;
            float worstOutward = float.MinValue;   // most positive signed distance across the edge planes
            float nearestSeg = float.MaxValue;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % n];
                Vector2 e = b - a;
                var outward = new Vector2(e.y, -e.x) * sgn;
                float len = outward.magnitude;
                if (len > 1e-8f)
                {
                    float d = Vector2.Dot(p - a, outward / len);
                    if (d > 0f) inside = false;
                    if (d > worstOutward) worstOutward = d;
                }
                nearestSeg = Mathf.Min(nearestSeg, DistanceToSegment(p, a, b));
            }

            return inside ? -worstOutward : -nearestSeg;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 e = b - a;
            float l2 = e.sqrMagnitude;
            if (l2 < 1e-12f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, e) / l2);
            return Vector2.Distance(p, a + e * t);
        }

        // ───────────────────────── contact ─────────────────────────

        static bool[] Contacts(Sample p, int n, float dt, float legLen,
                               BasisMocapJoint footJoint, BasisMocapJoint toeJoint, out float floor)
        {
            var low = new float[n];
            var lows = new List<float>(n);
            var flat = new Vector2[n];
            for (int f = 0; f < n; f++)
            {
                Vector3 foot = p(f, footJoint), toe = p(f, toeJoint);
                low[f] = Mathf.Min(foot.y, toe.y);
                lows.Add(low[f]);
                flat[f] = new Vector2(foot.x, foot.z);
            }
            floor = Percentile(lows, ContactFloorPercentile);

            float vT = ContactSpeedFracVHat * Mathf.Sqrt(G * legLen);
            float hT = ContactHeightFracLeg * legLen;

            var raw = new bool[n];
            for (int f = 0; f < n; f++)
            {
                float v = 0f;
                if (n > 2)
                {
                    int a = Mathf.Max(f - 1, 0), b = Mathf.Min(f + 1, n - 1);
                    v = (flat[b] - flat[a]).magnitude / ((b - a) * dt);
                }
                raw[f] = (low[f] - floor < hT) && (v < vT);
            }
            return Hysteresis(raw, Mathf.Max(2, Mathf.RoundToInt(0.03f / dt)));
        }

        static bool[] Hysteresis(bool[] raw, int minRun)
        {
            var o = (bool[])raw.Clone();
            int n = raw.Length, i = 0;
            while (i < n)
            {
                int j = i;
                while (j < n && o[j] == o[i]) j++;
                if ((j - i) < minRun && i > 0) { for (int k = i; k < j; k++) o[k] = o[i - 1]; }
                i = j;
            }
            return o;
        }

        // ───────────────────────── gate ─────────────────────────

        /// <summary>
        /// ⚠ NOT A RATCHET YET. The thresholds below are DELIBERATELY loose: they are set to catch "this is not
        /// a body that could stand up", not to police posture quality, because the honest tight numbers do not
        /// exist until the corpus has been measured (see BasisBalanceCorpusTests, which reports rather than
        /// gates on first run). Tighten them from the measured table, not from taste.
        /// </summary>
        public static (bool pass, string reason) Gate(in BasisBalanceSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Frames <= 0) return (false, "no frames");

            // Checked first and hard: if the mass model does not sum to a whole body, nothing below is a
            // measurement of anything.
            if (Mathf.Abs(s.MassSum - 1f) > 1e-3f)
                return (false, $"segment mass fractions sum to {s.MassSum:F4}, not 1.000 -- the mass model is broken");

            if (s.SupportedFrames == 0)
                return (false, "no frame in this clip has a foot on the ground -- there is no base of support to measure against");

            if (s.QuasiStaticFrames < MinQuasiStaticFrames)
                return (false, $"only {s.QuasiStaticFrames} quasi-static frames (< {MinQuasiStaticFrames}) -- " +
                               "the static criterion has nothing to say about this clip, read the dynamic one");

            if (s.QsMarginMedian < MinQsMarginMedian)
                return (false, $"the typical quasi-static pose has its centre of mass {-s.QsMarginMedian:F2} foot lengths " +
                               $"OUTSIDE the base of support (median margin {s.QsMarginMedian:F2} < {MinQsMarginMedian}) -- this body falls over");

            if (s.QsBalancedFrac < MinQsBalancedFrac)
                return (false, $"only {s.QsBalancedFrac:P0} of quasi-static frames are statically balanced (< {MinQsBalancedFrac:P0})");

            if (s.DynBalancedFrac < MinDynBalancedFrac)
                return (false, $"only {s.DynBalancedFrac:P0} of supported frames satisfy the XCoM criterion (< {MinDynBalancedFrac:P0})");

            return (true, $"{s.Clip}: qs margin median {s.QsMarginMedian:F2} foot ({s.QsBalancedFrac:P0} balanced, " +
                          $"{s.QuasiStaticFrames} frames), dyn {s.DynBalancedFrac:P0}, CoM at {s.ComHeightFracHead:F2} of head height");
        }

        public const int MinQuasiStaticFrames = 20;
        public const float MinQsMarginMedian = -0.25f;
        public const float MinQsBalancedFrac = 0.50f;
        public const float MinDynBalancedFrac = 0.40f;

        // ───────────────────────── helpers ─────────────────────────

        /// <summary>A FrameCount * JointCount copy of the clip's positions, ready to be patched with solved
        /// joints and fed back through Run.</summary>
        public static Vector3[] CopyPositions(BasisMotionClip clip)
        {
            int jc = (int)BasisMocapJoint.Count;
            var p = new Vector3[clip.FrameCount * jc];
            for (int f = 0; f < clip.FrameCount; f++)
                for (int j = 0; j < jc; j++)
                    p[f * jc + j] = clip.Get(f, (BasisMocapJoint)j).Position;
            return p;
        }

        public static void SetPosition(Vector3[] positions, int frame, BasisMocapJoint joint, Vector3 value)
            => positions[frame * (int)BasisMocapJoint.Count + (int)joint] = value;

        static float Mean(List<float> v)
        {
            if (v.Count == 0) return float.NaN;
            float t = 0f;
            for (int i = 0; i < v.Count; i++) t += v[i];
            return t / v.Count;
        }

        static float Percentile(List<float> x, int pct)
        {
            if (x.Count == 0) return float.NaN;
            var s = new List<float>(x);
            s.Sort();
            int idx = Mathf.Clamp(Mathf.RoundToInt((pct / 100f) * (s.Count - 1)), 0, s.Count - 1);
            return s[idx];
        }
    }
}
#endif
