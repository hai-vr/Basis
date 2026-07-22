#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using Basis.IK;

namespace Basis.IK.Mocap
{
    /// <summary>
    /// IS THIS POSE A BODY A HUMAN COULD BE IN? -- asked of the WHOLE skeleton at once, for the first time.
    ///
    /// ================================================================================================
    /// WHY THIS EXISTS. Every anatomical guard in this project was written AFTER a user complained from
    /// inside a headset:
    ///
    ///   * the KNEE got its anterior half-space guard (BasisLegSolveCore) early;
    ///   * the ELBOW got BasisElbowAnatomyCore only after "the arms are able to get in rotations that are
    ///     not possible... the elbows point to the sky";
    ///   * the SPINE got BasisSpineAnatomyCore after "looking down forces the chest to rotate".
    ///
    /// Each of those is one joint, found one complaint at a time. NOBODY HAS EVER LOOKED AT THE WHOLE
    /// SKELETON AT ONCE, and the by-contrast list is long: BasisLegDiagnostics says so in its own comments
    /// ("Nothing constrains the femur against the pelvis anywhere in the solve, so these are logged rather
    /// than guarded"), and the 2026-07-19 audit found wrist, tracked ankle, toes, hips-vs-root, head-vs-neck,
    /// upper arm and upper leg with no ROM clamp of any kind.
    ///
    /// AND NOTHING AT ALL LOOKS AT THE BODY AS A SOLID. A per-joint envelope cannot see an arm passing
    /// through the chest, a forearm through a thigh in a deep crouch, or the feet swapping sides -- each
    /// individual joint is inside its own limits the whole time. That is a WHOLE-BODY question and it needs
    /// a whole-body measurement.
    ///
    /// So there are two independent checks here, and they fail for genuinely different reasons:
    ///
    ///   (A) SELF-INTERSECTION -- the body as capsules, does any limb pass through another or through the
    ///       torso. Geometry only; no anatomy, no thresholds from the literature.
    ///   (B) ROM CONFORMANCE -- every joint's angle in its own parent frame, against an anatomical envelope.
    /// ================================================================================================
    ///
    /// WHERE THE LIMITS COME FROM, AND THE ONE PLACE THIS DISAGREES WITH ITS OWN BRIEF.
    ///
    /// The instinct is "derive the limits from the corpus". THIS PROJECT HAS ALREADY LITIGATED THAT AND
    /// COME DOWN THE OTHER WAY, and the reasoning is in BasisSpineAnatomy's header:
    ///
    ///     "Motion capture records what people DID, not what they CAN do -- CMU subjects walk around a lab,
    ///      they do not side-bend to end range. Fitting the envelope to the corpus would encode 'didn't' as
    ///      'can't' and then clip the first VR user who leans sideways."
    ///
    /// So the limits are max-voluntary ROM from the clinical literature, and THE CORPUS HOLDS A VETO: it
    /// measures what real humans actually did, and if a limit fires in the FAT of that distribution the
    /// limit is wrong, not the human. That is the same requirement as "a limit that clamps poses real humans
    /// actually made is a wrong limit" -- it just runs the test in the honest direction. The measured
    /// distribution (p50 / p95 / p99 / min / max, per channel, per corpus tier) is what
    /// <see cref="BasisRomAccumulator"/> exists to produce, and it is reported whether or not anything gates.
    ///
    /// AND THREE OF THE CHANNELS HAVE A KNOWN ANSWER ALREADY, WHICH IS WHAT PROVES THE RULER.
    /// This project has shipped lying metrics before -- a pop detector whose count was identical whether its
    /// cause was present or absent, a smoothness metric that scored over-smoothed mush above a real human --
    /// so a new measurement is worth nothing until it reproduces a number somebody already established:
    ///
    ///   * ELBOW CEILING. BasisElbowAnatomyCore measured 55,140 frames of real arm motion and found the
    ///     worst violation of "the elbow never rises above the higher of shoulder and hand" is +0.015 arm
    ///     lengths. <see cref="BasisRomChannel.LeftElbowCeilingFracArm"/> measures exactly that quantity.
    ///
    ///     ⚠ IT DID NOT REPRODUCE, THE RULER WAS RIGHT, AND THE SHIPPED CODE HAS SINCE BEEN FIXED.
    ///     Ran 2026-07-22. In the frame the guard enforced AT THE TIME (PlayerUp, i.e. world up for an
    ///     upright root) the root tier's worst was 0.0525, not 0.015 -- past the guard's own 0.05 soft
    ///     margin. In the TORSO's frame the same tier gives exactly 0.0150. The published number had
    ///     therefore been measured in a torso-relative frame while the guard enforced the law in world
    ///     space, and on a bent-over torso the two disagree by the whole trunk lean (posture/ tier: +0.3723
    ///     world, -0.3713 torso, on the SAME frames -- the sign flips).
    ///     BasisArmSolveCore now passes BasisElbowAnatomyCore a TORSO up (chest->neck), so the guard and the
    ///     published evidence finally describe the same law.
    ///     <see cref="BasisRomChannel.LeftElbowCeilingTorsoFracArm"/> is the channel that tracks shipped
    ///     behaviour; the world channel above is kept as the historical comparison baseline. Note that
    ///     NEITHER NUMBER MOVED when the guard was fixed, and that is not a bug: both are measurements of
    ///     what the CORPUS did, and the humans did not change frame. The full evidence is in
    ///     BasisBodyPlausibilityTests's elbow test.
    ///   * SPINE. The lumbar / lower-thoracic / cervical channels do not re-implement anything -- they call
    ///     BasisSpineAnatomy.BuildRestFrame and BasisSpineAnatomyCore.Decompose, THE SHIPPING FUNCTIONS, so
    ///     they must reproduce the p99 table in BasisSpineAnatomy's header (lumbar 49.2 flex / 10.7 ext /
    ///     12.5 lat / 10.2 axial, and so on for the rest).
    ///   * KNEE ANTERIOR. BasisBvhLoader.Validate already asserts a majority of sampled frames put the knee
    ///     in front of the hip->ankle axis. <see cref="BasisRomChannel.LeftKneeAnteriorDot"/> is the same
    ///     half-space, measured on every frame rather than a sample.
    ///
    /// EVERY ANGLE IS TAKEN FROM POSITIONS, NOT FROM BONE-LOCAL AXES. A bone's local frame is a rig
    /// convention and does not transfer -- this project has been bitten by that repeatedly, which is why
    /// BasisSpineAnatomy.BuildRestFrame builds its axes from positions and why the arm swivel model is
    /// position-only. The pelvis and chest frames here are built the same way (up from the segment's own long
    /// axis, right from the hip sockets / shoulder joints), so the numbers carry no bind-pose dependency and
    /// mean the same thing on a CMU skeleton and on a shipped avatar. The ONE exception is
    /// <see cref="BasisRomChannel.LeftFemurTwistDeg"/>, which is a rotation and therefore convention-bound --
    /// it is reported and never gated, exactly as BasisLegDiagnostics does with the same quantity.
    ///
    /// AND IT MEASURES A POSE, NOT A CLIP. <see cref="BasisBodyFrame"/> is a plain array in BasisMocapJoint
    /// order, so the SOLVER'S OUTPUT goes through the identical maths as a real human -- which is the whole
    /// point. The corpus run is the ruler being calibrated; the solved run is the thing being measured.
    /// </summary>
    public enum BasisBodySegment
    {
        /// <summary>Hips -> Neck. The CORE, not the silhouette -- see <see cref="BasisBodyPlausibility.TorsoCoreFracHipWidth"/>.</summary>
        TorsoCore = 0,
        LeftUpperArm = 1,
        LeftForeArm = 2,
        RightUpperArm = 3,
        RightForeArm = 4,
        LeftThigh = 5,
        LeftShin = 6,
        RightThigh = 7,
        RightShin = 8,
        LeftFoot = 9,
        RightFoot = 10,
        Count = 11,
    }

    /// <summary>
    /// One measurable joint angle. Units differ per channel on purpose -- two of these are not angles at all
    /// but the exact quantities the shipped elbow and knee guards constrain, and putting them in the same
    /// table is what lets the report cross-check this file against numbers somebody already measured.
    /// Read <see cref="BasisRomSpec.Unit"/> before printing one.
    /// </summary>
    public enum BasisRomChannel
    {
        LeftElbowFlexDeg = 0,
        RightElbowFlexDeg = 1,
        /// <summary>THE SHIPPED LAW: elbow height above max(shoulder, hand), as a fraction of arm length.</summary>
        LeftElbowCeilingFracArm = 2,
        RightElbowCeilingFracArm = 3,
        LeftShoulderElevationDeg = 4,
        RightShoulderElevationDeg = 5,
        /// <summary>How far the humerus sits BEHIND the chest's coronal plane. Positive = behind.</summary>
        LeftShoulderPosteriorDeg = 6,
        RightShoulderPosteriorDeg = 7,
        LeftKneeFlexDeg = 8,
        RightKneeFlexDeg = 9,
        /// <summary>THE SHIPPED LAW: dot(kneeDir, anterior) about the hip->ankle axis. Must stay &gt; 0.</summary>
        LeftKneeAnteriorDot = 10,
        RightKneeAnteriorDot = 11,
        LeftHipFlexDeg = 12,
        RightHipFlexDeg = 13,
        LeftHipAbductionDeg = 14,
        RightHipAbductionDeg = 15,
        /// <summary>Convention-bound. REPORTED, NEVER GATED -- same status as BasisLegDiagnostics.FemurTwistDeg.</summary>
        LeftFemurTwistDeg = 16,
        RightFemurTwistDeg = 17,
        LumbarFlexDeg = 18,
        LumbarLateralDeg = 19,
        LumbarAxialDeg = 20,
        ChestFlexDeg = 21,
        ChestLateralDeg = 22,
        ChestAxialDeg = 23,
        NeckFlexDeg = 24,
        NeckLateralDeg = 25,
        NeckAxialDeg = 26,
        /// <summary>
        /// ⭐ THE SAME SHIPPED LAW, MEASURED IN THE TORSO'S FRAME INSTEAD OF THE WORLD'S -- the control that
        /// tells a frame mismatch apart from a stale limit, which is the one question the world-up channel
        /// above cannot answer on its own.
        ///
        /// ⭐ AND THIS IS NOW THE FRAME THE SHIPPED GUARD ACTUALLY ENFORCES. BasisArmSolveCore hands
        /// BasisElbowAnatomyCore a TORSO up (chest->neck, from BuildArmFrame), falling back to PlayerUp only
        /// when no body frame can be built.
        ///
        /// It used to hand it `PlayerUp` unconditionally -- the PLAYER ROOT's up, world up for an upright
        /// root per BasisLocalRigDriver.ApplyTuningSettings. Bend at the waist and the root stays vertical
        /// while the chest does not, so the two frames diverge by the whole trunk lean. Measured 2026-07-22
        /// over the corpus, that divergence was not academic: on posture/ the SAME 148 arm-samples read up
        /// to +0.3723 under world up and as low as -0.3713 in the torso frame -- the sign flips -- and the
        /// guard fired on all 148 of them, poses real humans held, at up to 66.15 deg of correction. See the
        /// header of BasisBodyPlausibilityTests.TheRuler_ReproducesTheElbowCeilingLaw... for the full table,
        /// and BasisElbowAnatomyTests's section 5 for the regression tests that now pin the frame.
        ///
        /// REPORTED, NEVER GATED, and that has not changed: this channel measures the CORPUS, so it says
        /// what humans did, not what the solver does. dynamic/ still violates in this frame too (0.2574%
        /// past 0.05, max 0.2155), which is a separate open question about the law's universality.
        /// </summary>
        LeftElbowCeilingTorsoFracArm = 27,
        RightElbowCeilingTorsoFracArm = 28,
        Count = 29,
    }

    /// <summary>The envelope for one channel, plus where the number came from. `Gated` is the ratchet.</summary>
    public struct BasisRomSpec
    {
        public string Name;
        public string Unit;
        /// <summary>Anatomical minimum. Below this is a violation in the NEGATIVE direction.</summary>
        public float Lo;
        /// <summary>Anatomical maximum. Above this is a violation in the POSITIVE direction.</summary>
        public float Hi;
        /// <summary>Histogram window. Wider than the envelope so violations are still resolved, not clipped.</summary>
        public float HistLo, HistHi;
        /// <summary>
        /// ⭐ THE RATCHET. True = <see cref="BasisBodyPlausibility.Gate"/> fails on a violation of this
        /// channel. Only channels whose limit is an ALREADY-MEASURED shipped law start out gated; the rest
        /// are reported until the corpus number exists, because there is no honest threshold before that.
        /// </summary>
        public bool Gated;
        /// <summary>Where the limit came from. A limit with no source is a guess, and guesses get deleted.</summary>
        public string Source;
    }

    /// <summary>Measured distribution of one channel. Percentiles are histogram-resolved; min/max are exact.</summary>
    public struct BasisRomStat
    {
        public int Count;
        public float Min, P01, P50, P95, P99, Max, Mean;
        /// <summary>Frames outside [Lo, Hi]. The number the envelope is actually judged on.</summary>
        public int ViolationCount;
        public float ViolationFrac => Count > 0 ? (float)ViolationCount / Count : 0f;
        /// <summary>Worst distance outside the envelope, in the channel's own units. 0 = never violated.</summary>
        public float WorstExcess;
        /// <summary>Which way the worst violation went: -1 below Lo, +1 above Hi, 0 none. A knee bending
        /// BACKWARDS and a knee over-flexing are different defects and must not share a number.</summary>
        public int WorstDirection;
    }

    /// <summary>
    /// A whole skeleton at one instant, in BasisMocapJoint order -- the interface a real human and the
    /// solver's output share, so neither can be measured with a ruler the other never saw.
    /// </summary>
    public struct BasisBodyFrame
    {
        public BasisMocapPose[] Joints;

        public BasisMocapPose Get(BasisMocapJoint j) => Joints[(int)j];
        public bool Has(BasisMocapJoint j) => Joints != null && Joints[(int)j].Valid;
        public Vector3 Pos(BasisMocapJoint j) => Joints[(int)j].Position;
        public Quaternion Rot(BasisMocapJoint j) => Joints[(int)j].Rotation;

        public static BasisMocapPose[] Allocate() => new BasisMocapPose[(int)BasisMocapJoint.Count];

        /// <summary>Fill a reusable buffer from one clip frame. No allocation in the hot loop.</summary>
        public static BasisBodyFrame FromClip(BasisMotionClip c, int frame, BasisMocapPose[] into)
        {
            int n = (int)BasisMocapJoint.Count;
            for (int j = 0; j < n; j++)
            {
                into[j] = c.Poses[frame * n + j];
            }
            return new BasisBodyFrame { Joints = into };
        }
    }

    /// <summary>
    /// The skeleton's own dimensions, measured once. EVERY radius and every reported penetration is a
    /// fraction of one of these, so the whole layer is scale-free -- a child avatar and a giant get the same
    /// verdict, not the same centimetres. Bone lengths are rigid and pose-independent, so any frame will do.
    /// </summary>
    public struct BasisBodyScale
    {
        public bool Valid;
        public string Error;
        public float LegLen;        // thigh + shin, averaged
        public float ArmLen;        // upper + fore, averaged
        public float HipWidth;      // hip socket to hip socket
        public float ShoulderWidth; // shoulder joint to shoulder joint
        public float[] SegLen;      // per BasisBodySegment
        public float[] SegRadius;   // per BasisBodySegment
    }

    /// <summary>
    /// The three spine segments' rest frames, captured at the subject's straightest frame. Held separately
    /// from <see cref="BasisBodyScale"/> because a rest frame is a POSE measurement (it needs a neutral) and
    /// the scale is a LENGTH measurement (any frame will do).
    /// </summary>
    public struct BasisBodySpineRest
    {
        public BasisSpineRestFrame Lumbar, LowerThoracic, Cervical;
        public bool LumbarOk, LowerThoracicOk, CervicalOk;
        public int ReferenceFrame;
    }

    /// <summary>Corpus-wide accumulator. Histogram-backed, so memory is flat no matter how many frames.</summary>
    public sealed class BasisRomAccumulator
    {
        public const int Bins = 720;

        readonly int[][] m_Counts = new int[(int)BasisRomChannel.Count][];
        readonly int[] m_Under = new int[(int)BasisRomChannel.Count];
        readonly int[] m_Over = new int[(int)BasisRomChannel.Count];
        readonly int[] m_N = new int[(int)BasisRomChannel.Count];
        readonly int[] m_Violations = new int[(int)BasisRomChannel.Count];
        readonly int[] m_WorstDir = new int[(int)BasisRomChannel.Count];
        readonly float[] m_Min = new float[(int)BasisRomChannel.Count];
        readonly float[] m_Max = new float[(int)BasisRomChannel.Count];
        readonly float[] m_WorstExcess = new float[(int)BasisRomChannel.Count];
        readonly double[] m_Sum = new double[(int)BasisRomChannel.Count];

        public BasisRomAccumulator()
        {
            for (int c = 0; c < (int)BasisRomChannel.Count; c++)
            {
                m_Counts[c] = new int[Bins];
                m_Min[c] = float.MaxValue;
                m_Max[c] = float.MinValue;
            }
        }

        public void Add(BasisRomChannel channel, float value)
        {
            int c = (int)channel;
            // Reject-unless-good. A NaN fails every ordered comparison, so it is DROPPED here rather than
            // poisoning a mean or a min -- and silently poisoning a mean is exactly how a metric starts lying.
            if (!(value > float.MinValue) || !(value < float.MaxValue))
            {
                return;
            }

            BasisRomSpec spec = BasisBodyPlausibility.Spec(channel);
            m_N[c]++;
            m_Sum[c] += value;
            if (value < m_Min[c]) m_Min[c] = value;
            if (value > m_Max[c]) m_Max[c] = value;

            float excess = 0f;
            int dir = 0;
            if (value < spec.Lo) { excess = spec.Lo - value; dir = -1; }
            else if (value > spec.Hi) { excess = value - spec.Hi; dir = +1; }
            if (dir != 0)
            {
                m_Violations[c]++;
                if (excess > m_WorstExcess[c]) { m_WorstExcess[c] = excess; m_WorstDir[c] = dir; }
            }

            float span = spec.HistHi - spec.HistLo;
            if (!(span > 0f)) return;
            int bin = Mathf.FloorToInt((value - spec.HistLo) / span * Bins);
            if (bin < 0) m_Under[c]++;
            else if (bin >= Bins) m_Over[c]++;
            else m_Counts[c][bin]++;
        }

        public BasisRomStat Summarise(BasisRomChannel channel)
        {
            int c = (int)channel;
            var s = new BasisRomStat { Count = m_N[c], ViolationCount = m_Violations[c] };
            if (m_N[c] == 0) return s;

            s.Min = m_Min[c];
            s.Max = m_Max[c];
            s.Mean = (float)(m_Sum[c] / m_N[c]);
            s.WorstExcess = m_WorstExcess[c];
            s.WorstDirection = m_WorstDir[c];
            s.P01 = Percentile(c, 0.01f);
            s.P50 = Percentile(c, 0.50f);
            s.P95 = Percentile(c, 0.95f);
            s.P99 = Percentile(c, 0.99f);
            return s;
        }

        /// <summary>
        /// Percentile from the histogram, linear-interpolated inside the bin it lands in. Samples that fell
        /// outside the window are counted at the ends rather than discarded, so a percentile can never be
        /// dragged INWARD by an extreme value the window failed to hold -- it saturates at the exact min/max
        /// instead, which is the honest failure.
        /// </summary>
        float Percentile(int c, float p)
        {
            int total = m_N[c];
            if (total == 0) return 0f;
            int want = Mathf.Clamp(Mathf.RoundToInt(p * (total - 1)), 0, total - 1);

            if (want < m_Under[c]) return m_Min[c];

            BasisRomSpec spec = BasisBodyPlausibility.Spec((BasisRomChannel)c);
            float binW = (spec.HistHi - spec.HistLo) / Bins;
            int cum = m_Under[c];
            for (int b = 0; b < Bins; b++)
            {
                int n = m_Counts[c][b];
                if (cum + n > want)
                {
                    float within = n > 1 ? (want - cum) / (float)(n - 1) : 0.5f;
                    float v = spec.HistLo + (b + Mathf.Clamp01(within)) * binW;
                    return Mathf.Clamp(v, m_Min[c], m_Max[c]);
                }
                cum += n;
            }
            return m_Max[c];
        }
    }

    /// <summary>Per-clip verdict. Same shape as BasisMocapAccuracySummary / BasisFootQualitySummary.</summary>
    public struct BasisBodyPlausibilitySummary
    {
        public bool Ok;
        public string Error;
        public string Clip;
        public int Frames;

        // ── (A) self-intersection ──
        /// <summary>Frames on which at least one checked pair of capsules overlapped.</summary>
        public int IntersectFrames;
        public float IntersectFrac;
        /// <summary>Deepest overlap seen, as a fraction of the SHORTER of the two segments' lengths.</summary>
        public float WorstPenetrationFracLimb;
        /// <summary>The same overlap as a fraction of the summed radii. 1.0 = the two axes are coincident.</summary>
        public float WorstPenetrationFracRadii;
        public string WorstPair;
        public int WorstPairFrame;
        /// <summary>
        /// ⭐ WHAT <see cref="BasisBodyPlausibility.Gate"/> ACTUALLY JUDGES: the deepest overlap on a pair
        /// whose depth number can tell contact from pass-through. <see cref="WorstPenetrationFracLimb"/>
        /// above is the RAW worst over every pair and stays unfiltered for the report -- but gating on it
        /// failed 8 of the corpus's 109 clips of real human motion, because the arm x same-side-leg family
        /// saturates. See <see cref="BasisBodyPlausibility.PairDepthDiscriminating"/>.
        /// </summary>
        public float WorstDiscriminatingPenetrationFracLimb;
        public string WorstDiscriminatingPair;
        public int WorstDiscriminatingPairFrame;
        /// <summary>Penetration percentiles over ALL frames (clear frames contribute 0).</summary>
        public float PenetrationP95FracLimb, PenetrationP99FracLimb;
        /// <summary>Per-pair worst penetration and hit count, for the report. Indexed by pair id.</summary>
        public float[] PairWorstFracLimb;
        /// <summary>
        /// Per-pair worst overlap as a fraction of the SUMMED CORE RADII. 1.0 = the two bone axes are
        /// coincident. Unlike <see cref="PairWorstFracLimb"/> this is bounded [0,1] for every pair, so it is
        /// the number to compare ACROSS pairs -- and the number that shows the arm x leg family saturating.
        /// </summary>
        public float[] PairWorstFracRadii;
        public int[] PairHitFrames;

        // ── (B) ROM ──
        /// <summary>Frames on which at least one GATED channel left its envelope.</summary>
        public int RomViolationFrames;
        public float RomViolationFrac;
        public string WorstRomChannel;
        public float WorstRomExcess;
        /// <summary>-1 = below the low limit, +1 = above the high limit, 0 = clean.</summary>
        public int WorstRomDirection;
        /// <summary>Per-channel stats for THIS clip. Corpus-wide numbers come from BasisRomAccumulator.</summary>
        public BasisRomStat[] Rom;
    }

    public static class BasisBodyPlausibility
    {
        const float k_Epsilon = 1e-6f;
        const float k_SqrEpsilon = 1e-12f;

        // ════════════════════════════════════════════════════════════════════════════════════════════════
        // (A) SELF-INTERSECTION -- radii
        //
        // THESE ARE CORE RADII, NOT SURFACE RADII, AND THE DIFFERENCE IS THE WHOLE DESIGN. A capsule sized
        // to the actual silhouette of a torso would fire on a person standing with their arms at their
        // sides: the upper arm's axis sits ~0.17 m from the spine and a full-breadth torso capsule reaches
        // ~0.18 m, so "arms down" -- the commonest pose in VR -- would read as the arm inside the chest.
        // A single capsule around the spine simply cannot both catch an arm passing THROUGH the chest and
        // pass arms FOLDED ON the chest, because a chest is not a cylinder. (That is what the parked
        // elliptical-torso work is for.)
        //
        // So this layer detects GROSS INTERPENETRATION and says so: a limb whose AXIS comes within a
        // fraction of the spine has unambiguously gone through the person, and a forearm resting on a thigh
        // has not. The radii below are roughly HALF the true flesh radius, which puts a surface-contact
        // pose (forearm on thigh: axes ~0.12 m apart, radii summing to ~0.05 m) comfortably clear while a
        // genuine pass-through (axes under 0.03 m) is unambiguous.
        //
        // ⚠ CONSEQUENCE, STATED SO NOBODY IS SURPRISED BY IT: this will NOT catch light surface clipping --
        // a hand grazing a hip, a bicep brushing a ribcage. It catches the class of defect users report,
        // which is a limb visibly inside the body. Tightening these toward the true flesh radius is only
        // honest once the corpus has been measured with them, because every one of those poses is one a
        // real human makes.
        // ════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Torso core radius as a fraction of hip-socket width. 0.42 of a ~0.18 m hip width is ~0.076 m.
        ///
        /// THE BINDING CASE IS AN ELBOW TUCKED TO THE STERNUM, not an arm through the chest. A real tucked
        /// elbow's axis sits ~0.11-0.13 m from the spine; the upper arm's own core radius is ~0.022, so the
        /// torso may not exceed ~0.09 without calling that pose a collision. 0.076 leaves ~0.013 m of margin
        /// there and still reports ~0.08 m of penetration for a forearm driven through the sternum, which is
        /// the separation this layer has to be able to tell apart.
        /// </summary>
        public const float TorsoCoreFracHipWidth = 0.42f;

        /// <summary>
        /// Arm / leg core radius as a fraction of that segment's own length -- roughly half the flesh radius
        /// (a 0.25 m forearm gets 0.020 m; a 0.44 m thigh gets 0.035 m).
        ///
        /// THE BINDING CASE IS STANDING WITH THE ARMS DOWN: the forearm's axis runs ~0.085 m outboard of the
        /// femur's, and those two radii sum to ~0.055, so the tightest legal pose in the whole body still has
        /// ~35% headroom. That pair is the one to watch first if the corpus ever reports a false positive.
        /// </summary>
        public const float LimbCoreFracLen = 0.08f;

        /// <summary>Foot core radius as a fraction of ankle->toe length.</summary>
        public const float FootCoreFracLen = 0.15f;

        // ════════════════════════════════════════════════════════════════════════════════════════════════
        // GATE THRESHOLDS
        //
        // ⭐ RATCHET SITE. Both of these are PROVISIONAL and deliberately loose: they were set before the
        // corpus had been measured with this code, which means they are guesses, and a guess dressed as a
        // threshold is how this project has previously shipped metrics that agreed with themselves and with
        // nothing else. Run BasisBodyPlausibilityTests, read the reported envelope, and tighten to just
        // above what real humans actually produce. Until then they only catch the gross case.
        // ════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>Fraction of frames allowed to show ANY capsule overlap.</summary>
        public const float MaxIntersectFrac = 0.02f;

        /// <summary>
        /// Deepest single overlap allowed, as a fraction of the shorter segment's length. This is the GROSS
        /// gate: a forearm driven through the sternum reports 0.3192, a limb merely brushing another reports
        /// a few hundredths. The rate gate above is the sensitive one; this one exists so a single
        /// catastrophic frame fails even in a long clip where the rate stays low.
        ///
        /// ⭐ NO LONGER A GUESS. Measured 2026-07-22 over all 109 clips / 133,078 frames, the worst depth
        /// real human motion produces on a DEPTH-DISCRIMINATING pair is 0.125 (torso x R-upperarm,
        /// root/141_17 f5). The synthetic arm-through-sternum fixture produces 0.3192. So 0.20 sits 1.60x
        /// above anything a real human did and 0.63x below an unambiguous pass-through -- headroom on both
        /// sides, which is what a threshold is supposed to have. It is UNCHANGED in value; what changed is
        /// that Gate() now applies it only where depth discriminates (PairDepthDiscriminating), because the
        /// arm x same-side-leg family saturates its depth number on real motion and was failing 8 of the 109
        /// clips before that scope fix.
        /// </summary>
        public const float MaxPenetrationFracLimb = 0.20f;

        /// <summary>Fraction of frames allowed to violate a GATED ROM channel.</summary>
        public const float MaxRomViolationFrac = 0.02f;

        /// <summary>
        /// The knee's anterior half-space is a DIRECTION, and a direction taken from a collapsing lever arm
        /// is noise. Below this perpendicular offset (fraction of leg length) the channel reports nothing
        /// rather than reporting a random sign -- the same "no circle, no test" discipline
        /// BasisElbowAnatomyTests.RequireInReach enforces. 0.015 of an 0.85 m leg is ~1.3 cm, the lever
        /// below which every roll/flip singularity in this codebase has lived (BasisLegSolveCore).
        /// </summary>
        public const float MinKneeLeverFracLeg = 0.015f;

        // ── the capsule pairs that are actually worth checking ──────────────────────────────────────────
        //
        // ADJACENT SEGMENTS ARE EXCLUDED because they share a joint and therefore "touch" at zero distance
        // in every legal pose -- checking them would produce a detector that fires on standing still.
        //
        // TORSO vs LEG IS ALSO EXCLUDED, and that one is a judgement call worth writing down: the torso
        // capsule's lower cap sits AT the hips, which is exactly where a folded leg goes. Heel-to-buttock
        // kneeling puts a real shin within a few centimetres of that cap, so the pair would fire on a pose
        // people genuinely hold. The thigh is adjacent (its parent IS the capsule's endpoint) and excluded
        // for the ordinary reason. Catching leg-through-pelvis needs the segmented/elliptical torso, not a
        // tighter number here.
        //
        // ⚠ AND TWELVE PAIRS ARE CHECKED FOR RATE BUT NOT FOR DEPTH. THE ARM x SAME-SIDE-LEG FAMILY CANNOT
        // TELL CONTACT FROM PASS-THROUGH AT THIS FIDELITY, and that is a measured statement, not a guess.
        // Measured 2026-07-22 over all 109 clips / 133,078 frames, worst overlap per pair as a fraction of
        // the SUMMED CORE RADII (`fracRadii`, which is 1.0 when the two bone axes are coincident and is
        // therefore comparable across pairs in a way `fracLimb` is not):
        //
        //     R-forearm  x R-thigh .... 0.999      | torso x R-forearm ....... 0.203
        //     R-upperarm x R-thigh .... 0.995      | L-forearm x R-forearm ... 0.179
        //     L-forearm  x L-thigh .... 0.989      | torso x R-upperarm ...... 0.161
        //     R-forearm  x R-shin ..... 0.965      | L-shin x R-shin ......... 0.448
        //     L-upperarm x L-thigh .... 0.953      | every other pair ........ 0.000
        //     L-forearm  x L-shin ..... 0.946      |
        //
        // The split is bimodal and there is nothing in between. On the arm x same-side-leg pairs real motion
        // drives the two bone AXES to within half a millimetre (dynamic/135_01 f2377: axis distance 0.0005 m
        // against summed radii of 0.0487 m) and the closest approach is INTERIOR to both segments, not an
        // endpoint touch -- 386 of the 655 deepest hits are interior, and they arrive in smooth multi-frame
        // arcs (root/26_09 f248-f259 ramps 0.153 -> 0.200 -> 0.169), so they are sustained real motion, not
        // glitch frames. CMU is joint-angle data solved without any collision constraint, so the source
        // genuinely lets a forearm sit inside a thigh.
        //
        // Two facts make the DEPTH number unusable on those pairs specifically:
        //   * the core radii are ~half the flesh radius, so forearm+thigh sum to ~0.049 m while a real
        //     forearm RESTING on a real thigh holds its axis ~0.11 m away. The pair therefore cannot fire at
        //     all until the limbs are already deep inside one another -- there is no "light contact" band.
        //   * `fracLimb` saturates: its ceiling is (rA+rB)/min(lenA,lenB), which for forearm x thigh is only
        //     ~0.25 (median over the corpus; range 0.226-0.295). The observed worst, 0.261, IS that ceiling.
        //     So ANY depth threshold near 0.25 is really testing "are the axes exactly coincident", and any
        //     threshold above ~0.30 can never fire for that pair no matter what the solver does.
        // The cross-side pairs (L-forearm x R-thigh and friends) never fire on a single frame of the corpus,
        // which is the tell that this is same-side limb geometry rather than corpus noise.
        //
        // So those pairs keep their RATE check -- a solver that put an arm through a leg on 5% of frames
        // would still be caught -- and are excluded from the DEPTH check with this reason recorded. Catching
        // arm-inside-leg by depth needs flesh-radius capsules, which needs the parked elliptical-torso work
        // and a corpus that is collision-clean; neither exists yet. BasisBodyPlausibilityTests asserts that
        // the excluded family really is saturated, so this exclusion cannot quietly outlive its own evidence.
        struct Pair
        {
            public BasisBodySegment A, B;
            public string Label;
            /// <summary>False = rate-checked only; its depth number saturates. See the comment above.</summary>
            public bool DepthDiscriminating;
            public Pair(BasisBodySegment a, BasisBodySegment b, string label, bool depthDiscriminating = true)
            {
                A = a; B = b; Label = label; DepthDiscriminating = depthDiscriminating;
            }
        }

        static readonly Pair[] k_Pairs =
        {
            // arm through the torso -- the reported class of defect
            new Pair(BasisBodySegment.TorsoCore, BasisBodySegment.LeftUpperArm,  "torso x L-upperarm"),
            new Pair(BasisBodySegment.TorsoCore, BasisBodySegment.LeftForeArm,   "torso x L-forearm"),
            new Pair(BasisBodySegment.TorsoCore, BasisBodySegment.RightUpperArm, "torso x R-upperarm"),
            new Pair(BasisBodySegment.TorsoCore, BasisBodySegment.RightForeArm,  "torso x R-forearm"),

            // arm through leg -- the deep-crouch case. DEPTH-EXCLUDED, rate-checked: see the block comment
            // above for the corpus measurement that disqualified the depth number on this whole family.
            new Pair(BasisBodySegment.LeftForeArm,  BasisBodySegment.LeftThigh,  "L-forearm x L-thigh", false),
            new Pair(BasisBodySegment.LeftForeArm,  BasisBodySegment.RightThigh, "L-forearm x R-thigh", false),
            new Pair(BasisBodySegment.LeftForeArm,  BasisBodySegment.LeftShin,   "L-forearm x L-shin", false),
            new Pair(BasisBodySegment.LeftForeArm,  BasisBodySegment.RightShin,  "L-forearm x R-shin", false),
            new Pair(BasisBodySegment.RightForeArm, BasisBodySegment.LeftThigh,  "R-forearm x L-thigh", false),
            new Pair(BasisBodySegment.RightForeArm, BasisBodySegment.RightThigh, "R-forearm x R-thigh", false),
            new Pair(BasisBodySegment.RightForeArm, BasisBodySegment.LeftShin,   "R-forearm x L-shin", false),
            new Pair(BasisBodySegment.RightForeArm, BasisBodySegment.RightShin,  "R-forearm x R-shin", false),
            new Pair(BasisBodySegment.LeftUpperArm, BasisBodySegment.LeftThigh,  "L-upperarm x L-thigh", false),
            new Pair(BasisBodySegment.LeftUpperArm, BasisBodySegment.RightThigh, "L-upperarm x R-thigh", false),
            new Pair(BasisBodySegment.RightUpperArm, BasisBodySegment.LeftThigh, "R-upperarm x L-thigh", false),
            new Pair(BasisBodySegment.RightUpperArm, BasisBodySegment.RightThigh,"R-upperarm x R-thigh", false),

            // left limb through right limb
            new Pair(BasisBodySegment.LeftUpperArm, BasisBodySegment.RightUpperArm, "L-upperarm x R-upperarm"),
            new Pair(BasisBodySegment.LeftUpperArm, BasisBodySegment.RightForeArm,  "L-upperarm x R-forearm"),
            new Pair(BasisBodySegment.LeftForeArm,  BasisBodySegment.RightUpperArm, "L-forearm x R-upperarm"),
            new Pair(BasisBodySegment.LeftForeArm,  BasisBodySegment.RightForeArm,  "L-forearm x R-forearm"),
            new Pair(BasisBodySegment.LeftThigh,    BasisBodySegment.RightThigh,    "L-thigh x R-thigh"),
            new Pair(BasisBodySegment.LeftThigh,    BasisBodySegment.RightShin,     "L-thigh x R-shin"),
            new Pair(BasisBodySegment.LeftShin,     BasisBodySegment.RightThigh,    "L-shin x R-thigh"),
            new Pair(BasisBodySegment.LeftShin,     BasisBodySegment.RightShin,     "L-shin x R-shin"),

            // feet through each other
            new Pair(BasisBodySegment.LeftFoot, BasisBodySegment.RightFoot, "L-foot x R-foot"),
        };

        public static int PairCount => k_Pairs.Length;
        public static string PairLabel(int i) => k_Pairs[i].Label;

        /// <summary>
        /// Whether this pair's PENETRATION DEPTH means anything. False for the arm x same-side-leg family,
        /// whose depth number saturates on real human motion -- the reasoning and the measurement are in the
        /// comment above <see cref="Pair"/>. Rate is checked on every pair regardless.
        /// </summary>
        public static bool PairDepthDiscriminating(int i) => k_Pairs[i].DepthDiscriminating;

        // Segment endpoints. Order is (proximal, distal); nothing depends on it except readability.
        static readonly BasisMocapJoint[] k_SegA =
        {
            BasisMocapJoint.Hips,          // TorsoCore
            BasisMocapJoint.LeftUpperArm,  BasisMocapJoint.LeftLowerArm,
            BasisMocapJoint.RightUpperArm, BasisMocapJoint.RightLowerArm,
            BasisMocapJoint.LeftUpperLeg,  BasisMocapJoint.LeftLowerLeg,
            BasisMocapJoint.RightUpperLeg, BasisMocapJoint.RightLowerLeg,
            BasisMocapJoint.LeftFoot,      BasisMocapJoint.RightFoot,
        };

        static readonly BasisMocapJoint[] k_SegB =
        {
            BasisMocapJoint.Neck,          // TorsoCore
            BasisMocapJoint.LeftLowerArm,  BasisMocapJoint.LeftHand,
            BasisMocapJoint.RightLowerArm, BasisMocapJoint.RightHand,
            BasisMocapJoint.LeftLowerLeg,  BasisMocapJoint.LeftFoot,
            BasisMocapJoint.RightLowerLeg, BasisMocapJoint.RightFoot,
            BasisMocapJoint.LeftToes,      BasisMocapJoint.RightToes,
        };

        public static BasisMocapJoint SegmentStart(BasisBodySegment s) => k_SegA[(int)s];
        public static BasisMocapJoint SegmentEnd(BasisBodySegment s) => k_SegB[(int)s];

        // ════════════════════════════════════════════════════════════════════════════════════════════════
        // (B) THE ENVELOPE
        // ════════════════════════════════════════════════════════════════════════════════════════════════

        static readonly BasisRomSpec[] k_Specs = BuildSpecs();

        public static BasisRomSpec Spec(BasisRomChannel c) => k_Specs[(int)c];

        static BasisRomSpec Make(string name, string unit, float lo, float hi, float hLo, float hHi, bool gated, string source)
            => new BasisRomSpec { Name = name, Unit = unit, Lo = lo, Hi = hi, HistLo = hLo, HistHi = hHi, Gated = gated, Source = source };

        static BasisRomSpec[] BuildSpecs()
        {
            var s = new BasisRomSpec[(int)BasisRomChannel.Count];

            // ── ELBOW ────────────────────────────────────────────────────────────────────────────────────
            // Flexion is 180 minus the interior angle at the elbow, so 0 is a straight arm and ~150 is a
            // full fold. It CANNOT go negative -- the interior angle of three points is at most 180 -- so
            // hyperextension is structurally invisible to this channel and the low limit is a formality.
            // Hyperextension of the ARM is caught by the ceiling channel below; the ARM has no hinge-plane
            // guard at all, which is precisely why BasisElbowAnatomyCore constrains HEIGHT instead.
            const string elbowSrc = "clinical elbow flexion 140-150 deg max; 160 leaves headroom for hypermobility. NOT gated: corpus number owed.";
            s[(int)BasisRomChannel.LeftElbowFlexDeg] = Make("L elbow flex", "deg", -1f, 160f, -20f, 200f, false, elbowSrc);
            s[(int)BasisRomChannel.RightElbowFlexDeg] = Make("R elbow flex", "deg", -1f, 160f, -20f, 200f, false, elbowSrc);

            // ⭐ THE SHIPPED LAW, and the one channel with an answer key. BasisElbowAnatomyCore measured
            // 55,140 real frames: worst violation +0.015 arm lengths, 0.0000% of frames past 0.05. The limit
            // here IS the guard's own soft margin -- referenced, not copied, so it cannot drift from it.
            string ceilSrc = $"BasisElbowAnatomyCore.SoftMarginFracLimb ({BasisElbowAnatomyCore.SoftMarginFracLimb}); corpus worst real violation +0.015 over 55,140 frames.";
            s[(int)BasisRomChannel.LeftElbowCeilingFracArm] = Make("L elbow above ceiling", "fracArm", -2f, BasisElbowAnatomyCore.SoftMarginFracLimb, -1f, 1f, true, ceilSrc);
            s[(int)BasisRomChannel.RightElbowCeilingFracArm] = Make("R elbow above ceiling", "fracArm", -2f, BasisElbowAnatomyCore.SoftMarginFracLimb, -1f, 1f, true, ceilSrc);

            // ── SHOULDER ─────────────────────────────────────────────────────────────────────────────────
            // Elevation is the angle of the humerus from straight down in the CHEST's frame: 0 hanging,
            // 180 straight overhead. Both ends are anatomically reachable, so there is nothing to gate --
            // this channel exists to show the distribution and to prove the arms-overhead corpus really does
            // populate the top of it (if it does not, the tier is not exercising what it was chosen for).
            const string elevSrc = "0-180 is fully reachable; REPORT ONLY, there is no violation to define.";
            s[(int)BasisRomChannel.LeftShoulderElevationDeg] = Make("L shoulder elevation", "deg", -1f, 181f, 0f, 180f, false, elevSrc);
            s[(int)BasisRomChannel.RightShoulderElevationDeg] = Make("R shoulder elevation", "deg", -1f, 181f, 0f, 180f, false, elevSrc);

            // How far behind the chest's coronal plane the humerus reaches. THIS one has a real limit --
            // you cannot swing your upper arm far behind you -- and it is the joint-level shadow of "the arm
            // went through the back of the torso", which check (A) sees as geometry and this sees as angle.
            const string postSrc = "shoulder (glenohumeral) extension 45-60 deg posterior. NOT gated: corpus number owed.";
            s[(int)BasisRomChannel.LeftShoulderPosteriorDeg] = Make("L shoulder posterior", "deg", -91f, 60f, -90f, 90f, false, postSrc);
            s[(int)BasisRomChannel.RightShoulderPosteriorDeg] = Make("R shoulder posterior", "deg", -91f, 60f, -90f, 90f, false, postSrc);

            // ── KNEE ─────────────────────────────────────────────────────────────────────────────────────
            const string kneeSrc = "clinical knee flexion ~140-160 deg. NOT gated: corpus number owed.";
            s[(int)BasisRomChannel.LeftKneeFlexDeg] = Make("L knee flex", "deg", -1f, 160f, -20f, 200f, false, kneeSrc);
            s[(int)BasisRomChannel.RightKneeFlexDeg] = Make("R knee flex", "deg", -1f, 160f, -20f, 200f, false, kneeSrc);

            // ⭐ THE OTHER SHIPPED LAW. BasisLegSolveCore: "a knee behind that axis is not unnatural, it is
            // anatomically unrepresentable". dot(kneeDir, anterior) > 0 is exactly |swivel| < 90, and the
            // guard's hard asymptote sits at 89.5 deg so the posterior half-space is unreachable, not merely
            // discouraged. Gated at 0 because that is a statement about bodies, not a tuning knob.
            const string antSrc = "BasisLegSolveCore anterior half-space (hard asymptote 89.5 deg => dot stays > 0).";
            s[(int)BasisRomChannel.LeftKneeAnteriorDot] = Make("L knee anterior", "dot", 0f, 1.01f, -1f, 1f, true, antSrc);
            s[(int)BasisRomChannel.RightKneeAnteriorDot] = Make("R knee anterior", "dot", 0f, 1.01f, -1f, 1f, true, antSrc);

            // ── HIP ──────────────────────────────────────────────────────────────────────────────────────
            // van Arkel 2015 (J Biomech 48(14):3803, cadaveric, full envelope): flexion 112 +/- 10 deg,
            // extension -12 +/- 7 deg. NOT the AAOS 30 deg extension -- goniometric "extension" absorbs
            // pelvic tilt, and this channel measures femur against PELVIS, which is the whole point.
            // Limits are mean + 1 SD so a hypermobile subject is not called impossible.
            const string hipFlexSrc = "van Arkel 2015: flexion 112+/-10, extension -12+/-7 (femur vs pelvis, NOT goniometric). NOT gated: corpus number owed.";
            s[(int)BasisRomChannel.LeftHipFlexDeg] = Make("L hip flex", "deg", -20f, 125f, -90f, 180f, false, hipFlexSrc);
            s[(int)BasisRomChannel.RightHipFlexDeg] = Make("R hip flex", "deg", -20f, 125f, -90f, 180f, false, hipFlexSrc);

            const string hipAbdSrc = "van Arkel 2015 envelope: abduction ~45, adduction ~25. NOT gated: corpus number owed.";
            s[(int)BasisRomChannel.LeftHipAbductionDeg] = Make("L hip abduction", "deg", -30f, 50f, -90f, 90f, false, hipAbdSrc);
            s[(int)BasisRomChannel.RightHipAbductionDeg] = Make("R hip abduction", "deg", -30f, 50f, -90f, 90f, false, hipAbdSrc);

            // ⚠ THE ONE ROTATION-DERIVED CHANNEL, AND THEREFORE THE ONE THAT CANNOT BE GATED. Femur twist
            // is measured about the femur's own axis from the upper-leg bone's rotation, and a bone's roll
            // is a rig convention -- the 2026-07 audit explicitly discarded a hinge-plane violation number
            // for depending on "an ASSUMED bind convention I could not validate offline". So this is a
            // RELATIVE signal: watch it move, never compare it to a limit. Same status, and the same
            // reasoning, as BasisLegDiagnostics.FemurTwistDeg.
            const string twistSrc = "CONVENTION-BOUND. Relative signal only -- never gated. Mirrors BasisLegDiagnostics.FemurTwistDeg.";
            s[(int)BasisRomChannel.LeftFemurTwistDeg] = Make("L femur twist", "deg", -181f, 181f, -180f, 180f, false, twistSrc);
            s[(int)BasisRomChannel.RightFemurTwistDeg] = Make("R femur twist", "deg", -181f, 181f, -180f, 180f, false, twistSrc);

            // ── SPINE ────────────────────────────────────────────────────────────────────────────────────
            // The limits are BasisSpineAnatomy's table, READ FROM IT rather than restated, so this audit and
            // the shipped guard can never disagree about what the envelope is. These three ARE gated: the
            // table already has its corpus veto (BasisSpineAnatomyCorpusTests -- under 0.6% fire rate across
            // 35,081 frames, and everything it touches is past p99), which is exactly the evidence the other
            // channels are still missing.
            AddSpine(s, BasisSpineSegment.Lumbar, BasisRomChannel.LumbarFlexDeg, "lumbar");
            AddSpine(s, BasisSpineSegment.LowerThoracic, BasisRomChannel.ChestFlexDeg, "chest");
            AddSpine(s, BasisSpineSegment.Cervical, BasisRomChannel.NeckFlexDeg, "neck");

            // ── ELBOW CEILING, TORSO FRAME ───────────────────────────────────────────────────────────────
            // The frame control. Same limit as the world-up channel so the two are directly comparable, but
            // NOT GATED: it exists to price the difference between the frame the guard enforces and the
            // frame its published evidence was taken in, and choosing between them is a decision about
            // BasisElbowAnatomyCore that this audit reports rather than makes.
            string ceilTorsoSrc = $"Same limit as the world-up channel ({BasisElbowAnatomyCore.SoftMarginFracLimb}), measured against the CHEST's up. " +
                                  "Reproduces the guard's published +0.015 on the root tier (max 0.0150); world up gives 0.0525 on the same tier. REPORTED, NEVER GATED.";
            s[(int)BasisRomChannel.LeftElbowCeilingTorsoFracArm] = Make("L elbow above ceiling (torso frame)", "fracArm", -2f, BasisElbowAnatomyCore.SoftMarginFracLimb, -1f, 1f, false, ceilTorsoSrc);
            s[(int)BasisRomChannel.RightElbowCeilingTorsoFracArm] = Make("R elbow above ceiling (torso frame)", "fracArm", -2f, BasisElbowAnatomyCore.SoftMarginFracLimb, -1f, 1f, false, ceilTorsoSrc);
            return s;
        }

        static void AddSpine(BasisRomSpec[] s, BasisSpineSegment seg, BasisRomChannel flexChannel, string label)
        {
            BasisSpineRom rom = BasisSpineAnatomy.Rom(seg);
            string src = $"BasisSpineAnatomy.Rom({seg}) = {rom.FlexDeg}/{rom.ExtDeg}/{rom.LatDeg}/{rom.AxialDeg}; corpus-vetoed by BasisSpineAnatomyCorpusTests.";
            int f = (int)flexChannel;
            s[f + 0] = Make($"{label} flex", "deg", -rom.ExtDeg, rom.FlexDeg, -90f, 90f, true, src);
            s[f + 1] = Make($"{label} lateral", "deg", -rom.LatDeg, rom.LatDeg, -90f, 90f, true, src);
            s[f + 2] = Make($"{label} axial", "deg", -rom.AxialDeg, rom.AxialDeg, -90f, 90f, true, src);
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════════
        // MEASUREMENT
        // ════════════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Measure the skeleton's dimensions and derive every capsule radius. Bone lengths are rigid, so the
        /// frame handed in only has to be a frame where every joint is valid.
        /// </summary>
        public static BasisBodyScale BuildScale(in BasisBodyFrame f)
        {
            var s = new BasisBodyScale
            {
                SegLen = new float[(int)BasisBodySegment.Count],
                SegRadius = new float[(int)BasisBodySegment.Count],
            };

            // Require only the joints the SEGMENTS are built from, plus the two width measurements. Demanding
            // all 22 would make a clip with a cosmetic joint missing fail the whole run, when the spine and
            // chest channels already decline gracefully on their own (their frames just report not-valid).
            for (int i = 0; i < (int)BasisBodySegment.Count; i++)
            {
                if (!f.Has(k_SegA[i])) { s.Error = $"joint {k_SegA[i]} is not present"; return s; }
                if (!f.Has(k_SegB[i])) { s.Error = $"joint {k_SegB[i]} is not present"; return s; }
            }

            for (int i = 0; i < (int)BasisBodySegment.Count; i++)
            {
                s.SegLen[i] = Vector3.Distance(f.Pos(k_SegA[i]), f.Pos(k_SegB[i]));
            }

            s.LegLen = 0.5f * (s.SegLen[(int)BasisBodySegment.LeftThigh] + s.SegLen[(int)BasisBodySegment.LeftShin]
                             + s.SegLen[(int)BasisBodySegment.RightThigh] + s.SegLen[(int)BasisBodySegment.RightShin]);
            s.ArmLen = 0.5f * (s.SegLen[(int)BasisBodySegment.LeftUpperArm] + s.SegLen[(int)BasisBodySegment.LeftForeArm]
                             + s.SegLen[(int)BasisBodySegment.RightUpperArm] + s.SegLen[(int)BasisBodySegment.RightForeArm]);
            s.HipWidth = Vector3.Distance(f.Pos(BasisMocapJoint.LeftUpperLeg), f.Pos(BasisMocapJoint.RightUpperLeg));
            s.ShoulderWidth = Vector3.Distance(f.Pos(BasisMocapJoint.LeftUpperArm), f.Pos(BasisMocapJoint.RightUpperArm));

            // Reject-unless-good: a degenerate skeleton must fail HERE, loudly, rather than divide by ~0 a
            // thousand frames later and hand back a plausible-looking table of infinities.
            if (!(s.LegLen > k_Epsilon) || !(s.ArmLen > k_Epsilon) || !(s.HipWidth > k_Epsilon))
            {
                s.Error = $"degenerate skeleton (leg {s.LegLen}, arm {s.ArmLen}, hips {s.HipWidth})";
                return s;
            }

            for (int i = 0; i < (int)BasisBodySegment.Count; i++)
            {
                var seg = (BasisBodySegment)i;
                if (seg == BasisBodySegment.TorsoCore)
                {
                    s.SegRadius[i] = TorsoCoreFracHipWidth * s.HipWidth;
                }
                else if (seg == BasisBodySegment.LeftFoot || seg == BasisBodySegment.RightFoot)
                {
                    s.SegRadius[i] = FootCoreFracLen * s.SegLen[i];
                }
                else
                {
                    s.SegRadius[i] = LimbCoreFracLen * s.SegLen[i];
                }
            }

            s.Valid = true;
            return s;
        }

        /// <summary>
        /// Capture the three spine rest frames at the subject's straightest frame -- the clip's own
        /// anatomical neutral, which is what makes every spine angle a deviation FROM rest rather than a
        /// deviation from whatever the rig happened to author. Same reference-finding rule as
        /// BasisSpineAnatomyCorpusTests, and the same shipping BuildRestFrame.
        ///
        /// THE UPPER THORACIC IS DELIBERATELY ABSENT. In the CMU skeleton `Neck` sits at ZERO offset from
        /// `Spine1` (the upper-chest joint) -- they are the same point -- so UpperChest has no measurable
        /// long axis in this corpus, and routing the axis up through the coincident neck instead reads a
        /// phantom ~27 deg of flexion. That is a property of the corpus, not of the segment; a real avatar
        /// has a genuine upperChest->neck length and measures fine at runtime.
        /// </summary>
        public static BasisBodySpineRest BuildSpineRest(BasisMotionClip c)
        {
            var r = new BasisBodySpineRest { ReferenceFrame = StraightestFrame(c) };
            int f = r.ReferenceFrame;

            r.LumbarOk = TryRestFrame(c, f, BasisMocapJoint.Spine, BasisMocapJoint.Chest, BasisMocapJoint.Hips, out r.Lumbar);
            r.LowerThoracicOk = TryRestFrame(c, f, BasisMocapJoint.Chest, BasisMocapJoint.UpperChest, BasisMocapJoint.Spine, out r.LowerThoracic);
            r.CervicalOk = TryRestFrame(c, f, BasisMocapJoint.Neck, BasisMocapJoint.Head, BasisMocapJoint.UpperChest, out r.Cervical);
            return r;
        }

        /// <summary>The clip's most neutral frame: the trunk bones' long axes closest to straight up at once.
        /// The neck is excluded because it has a real rest tilt and chasing it would move the reference.</summary>
        public static int StraightestFrame(BasisMotionClip c)
        {
            int best = 0;
            float bestScore = float.MaxValue;
            for (int f = 0; f < c.FrameCount; f++)
            {
                BasisMocapPose spine = c.Get(f, BasisMocapJoint.Spine);
                BasisMocapPose chest = c.Get(f, BasisMocapJoint.Chest);
                BasisMocapPose upper = c.Get(f, BasisMocapJoint.UpperChest);
                if (!spine.Valid || !chest.Valid || !upper.Valid) continue;

                float score = (1f - (chest.Position - spine.Position).normalized.y)
                            + (1f - (upper.Position - chest.Position).normalized.y);
                if (score < bestScore) { bestScore = score; best = f; }
            }
            return best;
        }

        static bool TryRestFrame(BasisMotionClip c, int f, BasisMocapJoint bone, BasisMocapJoint child, BasisMocapJoint parent, out BasisSpineRestFrame frame)
        {
            frame = default;
            BasisMocapPose b = c.Get(f, bone), ch = c.Get(f, child), p = c.Get(f, parent);
            BasisMocapPose ls = c.Get(f, BasisMocapJoint.LeftUpperArm), rs = c.Get(f, BasisMocapJoint.RightUpperArm);
            if (!b.Valid || !ch.Valid || !p.Valid || !ls.Valid || !rs.Valid) return false;

            frame = BasisSpineAnatomy.BuildRestFrame(b.Position, ch.Position, b.Rotation, p.Rotation, rs.Position - ls.Position);
            return frame.Valid;
        }

        // ── (A) capsule geometry ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Minimum distance between two line SEGMENTS, with the closest points clamped to the segments.
        /// The textbook construction (Ericson, Real-Time Collision Detection): solve the unconstrained
        /// least-squares for the two parameters, clamp one, re-solve the other, clamp again. The clamping is
        /// not a nicety -- unclamped, two nearly-parallel bones produce parameters far outside [0,1] and the
        /// reported "distance" is between points that are not on either bone.
        /// </summary>
        public static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float fdot = Vector3.Dot(d2, r);

            float s, t;
            if (a <= k_SqrEpsilon && e <= k_SqrEpsilon)
            {
                return r.magnitude;   // both degenerate: point to point
            }
            if (a <= k_SqrEpsilon)
            {
                s = 0f;
                t = Mathf.Clamp01(fdot / e);
            }
            else
            {
                float cdot = Vector3.Dot(d1, r);
                if (e <= k_SqrEpsilon)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-cdot / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    // denom == 0 means the segments are parallel: any s does, so pick the start and let the
                    // t-clamp below place the answer. Picking arbitrarily is correct here, not a fallback.
                    s = denom > k_SqrEpsilon ? Mathf.Clamp01((b * fdot - cdot * e) / denom) : 0f;
                    t = (b * s + fdot) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-cdot / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - cdot) / a); }
                }
            }

            Vector3 c1 = p1 + d1 * s;
            Vector3 c2 = p2 + d2 * t;
            return Vector3.Distance(c1, c2);
        }

        /// <summary>
        /// The worst capsule overlap in this pose. Returns the pair index, or -1 if nothing overlaps.
        /// `penFracLimb` is the overlap depth over the SHORTER segment's length -- scale-free, and the
        /// number to quote, because "3 cm of overlap" means something different on a forearm and a thigh.
        /// </summary>
        public static int WorstOverlap(in BasisBodyFrame f, in BasisBodyScale scale, out float penFracLimb, out float penFracRadii)
        {
            penFracLimb = 0f;
            penFracRadii = 0f;
            int worst = -1;
            if (!scale.Valid) return -1;

            for (int i = 0; i < k_Pairs.Length; i++)
            {
                float pen = Overlap(f, scale, i, out float fracLimb, out float fracRadii);
                if (pen > 0f && fracLimb > penFracLimb)
                {
                    penFracLimb = fracLimb;
                    penFracRadii = fracRadii;
                    worst = i;
                }
            }
            return worst;
        }

        /// <summary>Overlap depth in metres for one pair; 0 or negative means clear.</summary>
        public static float Overlap(in BasisBodyFrame f, in BasisBodyScale scale, int pairIndex, out float fracLimb, out float fracRadii)
        {
            fracLimb = 0f;
            fracRadii = 0f;
            Pair p = k_Pairs[pairIndex];
            int a = (int)p.A, b = (int)p.B;

            if (!f.Has(k_SegA[a]) || !f.Has(k_SegB[a]) || !f.Has(k_SegA[b]) || !f.Has(k_SegB[b])) return 0f;

            float d = SegmentDistance(f.Pos(k_SegA[a]), f.Pos(k_SegB[a]), f.Pos(k_SegA[b]), f.Pos(k_SegB[b]));
            float rSum = scale.SegRadius[a] + scale.SegRadius[b];
            float pen = rSum - d;
            if (!(pen > 0f)) return 0f;   // NaN falls out here too

            float shorter = Mathf.Min(scale.SegLen[a], scale.SegLen[b]);
            fracLimb = shorter > k_Epsilon ? pen / shorter : 0f;
            fracRadii = rSum > k_Epsilon ? pen / rSum : 0f;
            return pen;
        }

        // ── (B) joint angles ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The pelvis's anatomical frame, built from POSITIONS ONLY: up along the pelvis->lumbar segment,
        /// right across the hip sockets, forward = cross(right, up) (Unity's left-handed convention, the
        /// same one BasisBvhLoader.Validate and BasisSpineAnatomy.BuildRestFrame use). No bone-local axis is
        /// read anywhere, so this frame means the same thing on a CMU skeleton and on a shipped avatar.
        /// </summary>
        public static bool TryPelvisFrame(in BasisBodyFrame f, out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            right = Vector3.right; up = Vector3.up; forward = Vector3.forward;
            if (!f.Has(BasisMocapJoint.Hips) || !f.Has(BasisMocapJoint.Spine)
                || !f.Has(BasisMocapJoint.LeftUpperLeg) || !f.Has(BasisMocapJoint.RightUpperLeg)) return false;

            Vector3 u = f.Pos(BasisMocapJoint.Spine) - f.Pos(BasisMocapJoint.Hips);
            Vector3 r = f.Pos(BasisMocapJoint.RightUpperLeg) - f.Pos(BasisMocapJoint.LeftUpperLeg);
            return Orthonormal(u, r, out right, out up, out forward);
        }

        /// <summary>The chest's anatomical frame: up along chest->neck, right across the shoulder joints.</summary>
        public static bool TryChestFrame(in BasisBodyFrame f, out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            right = Vector3.right; up = Vector3.up; forward = Vector3.forward;
            if (!f.Has(BasisMocapJoint.Chest) || !f.Has(BasisMocapJoint.Neck)
                || !f.Has(BasisMocapJoint.LeftUpperArm) || !f.Has(BasisMocapJoint.RightUpperArm)) return false;

            Vector3 u = f.Pos(BasisMocapJoint.Neck) - f.Pos(BasisMocapJoint.Chest);
            Vector3 r = f.Pos(BasisMocapJoint.RightUpperArm) - f.Pos(BasisMocapJoint.LeftUpperArm);
            return Orthonormal(u, r, out right, out up, out forward);
        }

        static bool Orthonormal(Vector3 upRaw, Vector3 rightRaw, out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            right = Vector3.right; up = Vector3.up; forward = Vector3.forward;
            float uSqr = upRaw.sqrMagnitude;
            if (!(uSqr > k_SqrEpsilon) || !(rightRaw.sqrMagnitude > k_SqrEpsilon)) return false;
            up = upRaw / Mathf.Sqrt(uSqr);

            // Orthogonalise right against up. The shoulder line is not perpendicular to a leaning trunk, and
            // a non-orthogonal frame leaks flexion into the lateral read-out -- the exact failure
            // BasisSpineAnatomy.BuildRestFrame calls out.
            Vector3 r = rightRaw - up * Vector3.Dot(rightRaw, up);
            float rSqr = r.sqrMagnitude;
            if (!(rSqr > k_SqrEpsilon)) return false;
            right = r / Mathf.Sqrt(rSqr);
            forward = Vector3.Cross(right, up);   // Unity: cross(right, up) == forward
            return true;
        }

        /// <summary>
        /// Every ROM channel for one pose. `values` and `valid` must be BasisRomChannel.Count long.
        /// A channel whose inputs are missing or degenerate is left INVALID rather than filled with a
        /// plausible number -- a measurement nobody could take is not a measurement of zero.
        /// </summary>
        public static void MeasureRom(in BasisBodyFrame f, in BasisBodyScale scale, in BasisBodySpineRest spine, float[] values, bool[] valid)
        {
            for (int i = 0; i < values.Length; i++) { values[i] = 0f; valid[i] = false; }

            bool pelvisOk = TryPelvisFrame(f, out Vector3 pRight, out Vector3 pUp, out Vector3 pFwd);
            bool chestOk = TryChestFrame(f, out Vector3 cRight, out Vector3 cUp, out Vector3 cFwd);

            MeasureArm(f, scale, chestOk, cRight, cUp, cFwd, false, values, valid);
            MeasureArm(f, scale, chestOk, cRight, cUp, cFwd, true, values, valid);
            MeasureLeg(f, scale, pelvisOk, pRight, pUp, pFwd, false, values, valid);
            MeasureLeg(f, scale, pelvisOk, pRight, pUp, pFwd, true, values, valid);

            MeasureSpine(f, spine.Lumbar, spine.LumbarOk, BasisMocapJoint.Spine, BasisMocapJoint.Hips, BasisRomChannel.LumbarFlexDeg, values, valid);
            MeasureSpine(f, spine.LowerThoracic, spine.LowerThoracicOk, BasisMocapJoint.Chest, BasisMocapJoint.Spine, BasisRomChannel.ChestFlexDeg, values, valid);
            MeasureSpine(f, spine.Cervical, spine.CervicalOk, BasisMocapJoint.Neck, BasisMocapJoint.UpperChest, BasisRomChannel.NeckFlexDeg, values, valid);
        }

        static void MeasureArm(in BasisBodyFrame f, in BasisBodyScale scale, bool chestOk,
            Vector3 cRight, Vector3 cUp, Vector3 cFwd, bool rightSide, float[] v, bool[] ok)
        {
            BasisMocapJoint shoulderJ = rightSide ? BasisMocapJoint.RightUpperArm : BasisMocapJoint.LeftUpperArm;
            BasisMocapJoint elbowJ = rightSide ? BasisMocapJoint.RightLowerArm : BasisMocapJoint.LeftLowerArm;
            BasisMocapJoint handJ = rightSide ? BasisMocapJoint.RightHand : BasisMocapJoint.LeftHand;
            if (!f.Has(shoulderJ) || !f.Has(elbowJ) || !f.Has(handJ)) return;

            Vector3 shoulder = f.Pos(shoulderJ), elbow = f.Pos(elbowJ), hand = f.Pos(handJ);
            int flexC = (int)(rightSide ? BasisRomChannel.RightElbowFlexDeg : BasisRomChannel.LeftElbowFlexDeg);
            int ceilC = (int)(rightSide ? BasisRomChannel.RightElbowCeilingFracArm : BasisRomChannel.LeftElbowCeilingFracArm);
            int elevC = (int)(rightSide ? BasisRomChannel.RightShoulderElevationDeg : BasisRomChannel.LeftShoulderElevationDeg);
            int postC = (int)(rightSide ? BasisRomChannel.RightShoulderPosteriorDeg : BasisRomChannel.LeftShoulderPosteriorDeg);

            // ELBOW FLEXION: 180 minus the interior angle. 0 = straight, ~150 = fully folded.
            Vector3 up1 = shoulder - elbow, dn1 = hand - elbow;
            if (up1.sqrMagnitude > k_SqrEpsilon && dn1.sqrMagnitude > k_SqrEpsilon)
            {
                v[flexC] = 180f - Vector3.Angle(up1, dn1);
                ok[flexC] = true;
            }

            // ⭐ THE CEILING LAW. Height of the elbow above the HIGHER of shoulder and hand, over arm length.
            // `up` is WORLD up. That is NO LONGER the frame the shipped guard enforces: BasisArmSolveCore now
            // hands BasisElbowAnatomyCore a TORSO up (chest->neck), which is the frame the law was measured
            // in, so the torso channel below is the one that tracks shipped behaviour.
            //
            // THIS CHANNEL IS RETAINED AS THE HISTORICAL COMPARISON BASELINE, not as a defect. Holding the
            // two frames side by side is what priced the frame bug in the first place (root tier: 0.0525
            // world against 0.0150 torso; posture/ 0.3723 against 0.0242, sign-flipped on the same frames),
            // and it is what would price a regression back to it. It is a measurement of what the CORPUS did
            // in world space, so it is unaffected by anything the guard does -- which is exactly why it makes
            // a stable baseline.
            if (scale.Valid && scale.ArmLen > k_Epsilon)
            {
                float handUp = Vector3.Dot(hand - shoulder, Vector3.up);
                float ceiling = handUp > 0f ? handUp : 0f;
                v[ceilC] = (Vector3.Dot(elbow - shoulder, Vector3.up) - ceiling) / scale.ArmLen;
                ok[ceilC] = true;
            }

            if (!chestOk) return;

            // ⭐ THE SAME CEILING LAW, IN THE TORSO'S FRAME. Identical arithmetic to the block above with
            // `cUp` swapped for `Vector3.up`, so the ONLY thing that can differ between the two channels is
            // the choice of up -- which is exactly the question. This is the frame in which the guard's
            // published +0.015 reproduces (root tier max 0.0150); under world up the same tier reads 0.0525.
            int ceilTorsoC = (int)(rightSide ? BasisRomChannel.RightElbowCeilingTorsoFracArm : BasisRomChannel.LeftElbowCeilingTorsoFracArm);
            if (scale.Valid && scale.ArmLen > k_Epsilon)
            {
                float handUpTorso = Vector3.Dot(hand - shoulder, cUp);
                float ceilingTorso = handUpTorso > 0f ? handUpTorso : 0f;
                v[ceilTorsoC] = (Vector3.Dot(elbow - shoulder, cUp) - ceilingTorso) / scale.ArmLen;
                ok[ceilTorsoC] = true;
            }

            Vector3 humerus = elbow - shoulder;
            if (!(humerus.sqrMagnitude > k_SqrEpsilon)) return;
            humerus.Normalize();

            // Into the chest's own anatomical frame: (x right, y up, z forward), all position-derived.
            float hx = Vector3.Dot(humerus, cRight);
            float hy = Vector3.Dot(humerus, cUp);
            float hz = Vector3.Dot(humerus, cFwd);

            // ELEVATION: angle from straight down in the chest frame. 0 hanging, 180 straight overhead.
            v[elevC] = Vector3.Angle(new Vector3(hx, hy, hz), Vector3.down);
            ok[elevC] = true;

            // POSTERIOR: how far behind the chest's coronal plane the humerus points. Positive = behind.
            v[postC] = -Mathf.Asin(Mathf.Clamp(hz, -1f, 1f)) * Mathf.Rad2Deg;
            ok[postC] = true;
        }

        static void MeasureLeg(in BasisBodyFrame f, in BasisBodyScale scale, bool pelvisOk,
            Vector3 pRight, Vector3 pUp, Vector3 pFwd, bool rightSide, float[] v, bool[] ok)
        {
            BasisMocapJoint hipJ = rightSide ? BasisMocapJoint.RightUpperLeg : BasisMocapJoint.LeftUpperLeg;
            BasisMocapJoint kneeJ = rightSide ? BasisMocapJoint.RightLowerLeg : BasisMocapJoint.LeftLowerLeg;
            BasisMocapJoint ankleJ = rightSide ? BasisMocapJoint.RightFoot : BasisMocapJoint.LeftFoot;
            if (!f.Has(hipJ) || !f.Has(kneeJ) || !f.Has(ankleJ)) return;

            Vector3 hip = f.Pos(hipJ), knee = f.Pos(kneeJ), ankle = f.Pos(ankleJ);
            int flexC = (int)(rightSide ? BasisRomChannel.RightKneeFlexDeg : BasisRomChannel.LeftKneeFlexDeg);
            int antC = (int)(rightSide ? BasisRomChannel.RightKneeAnteriorDot : BasisRomChannel.LeftKneeAnteriorDot);
            int hFlexC = (int)(rightSide ? BasisRomChannel.RightHipFlexDeg : BasisRomChannel.LeftHipFlexDeg);
            int hAbdC = (int)(rightSide ? BasisRomChannel.RightHipAbductionDeg : BasisRomChannel.LeftHipAbductionDeg);
            int twistC = (int)(rightSide ? BasisRomChannel.RightFemurTwistDeg : BasisRomChannel.LeftFemurTwistDeg);

            Vector3 up1 = hip - knee, dn1 = ankle - knee;
            if (up1.sqrMagnitude > k_SqrEpsilon && dn1.sqrMagnitude > k_SqrEpsilon)
            {
                v[flexC] = 180f - Vector3.Angle(up1, dn1);
                ok[flexC] = true;
            }

            if (!pelvisOk) return;

            // ⭐ THE ANTERIOR HALF-SPACE, built exactly as the shipped guard builds it:
            // anterior = cross(legAxis, hipsRight), and the test is dot(kneeDir, anterior) > 0.
            Vector3 axis = ankle - hip;
            float axisSqr = axis.sqrMagnitude;
            if (axisSqr > k_SqrEpsilon && scale.Valid)
            {
                axis /= Mathf.Sqrt(axisSqr);
                Vector3 kneeOff = knee - hip;
                Vector3 perp = kneeOff - axis * Vector3.Dot(kneeOff, axis);
                float lever = perp.magnitude;
                // No lever, no direction. Report nothing rather than a random sign -- the same discipline as
                // BasisElbowAnatomyTests.RequireInReach ("no circle, no test").
                if (lever > MinKneeLeverFracLeg * scale.LegLen)
                {
                    Vector3 anterior = Vector3.Cross(axis, pRight);
                    float aSqr = anterior.sqrMagnitude;
                    if (aSqr > k_SqrEpsilon)
                    {
                        v[antC] = Vector3.Dot(perp / lever, anterior / Mathf.Sqrt(aSqr));
                        ok[antC] = true;
                    }
                }
            }

            // HIP: the femur's DIRECTION in the pelvis frame -- no bind-convention dependency, exactly the
            // construction BasisLegDiagnostics documents for HipFlexionDeg / HipAbductionDeg.
            Vector3 femur = knee - hip;
            if (femur.sqrMagnitude > k_SqrEpsilon)
            {
                femur.Normalize();
                float fx = Vector3.Dot(femur, pRight);
                float fy = Vector3.Dot(femur, pUp);
                float fz = Vector3.Dot(femur, pFwd);

                // Flexion in the sagittal plane: 0 straight down, +90 thigh horizontal-forward, negative
                // behind. Measured from the projection, so a splayed leg still reports its true flexion.
                v[hFlexC] = Mathf.Atan2(fz, -fy) * Mathf.Rad2Deg;
                ok[hFlexC] = true;

                // Abduction is elevation OUT of the sagittal plane, away from the midline. Left is -x in the
                // pelvis frame (BasisBvhLoader.Validate: the left hand has a negative dot with `right`), so
                // the outward axis flips sign per side and both legs report abduction as positive.
                float lateral = rightSide ? fx : -fx;
                v[hAbdC] = Mathf.Asin(Mathf.Clamp(lateral, -1f, 1f)) * Mathf.Rad2Deg;
                ok[hAbdC] = true;

                // ⚠ CONVENTION-BOUND, REPORTED ONLY. Twist of the upper-leg bone about the femur's own axis,
                // relative to the pelvis. Meaningful as a relative signal; NOT comparable to a limit.
                if (f.Has(hipJ))
                {
                    Quaternion rel = BasisSpineAnatomyCore.Conj(f.Rot(BasisMocapJoint.Hips)) * f.Rot(hipJ);
                    Vector3 axisLocal = BasisSpineAnatomyCore.Conj(f.Rot(BasisMocapJoint.Hips)) * femur;
                    v[twistC] = TwistDeg(rel, axisLocal);
                    ok[twistC] = true;
                }
            }
        }

        static void MeasureSpine(in BasisBodyFrame f, in BasisSpineRestFrame rest, bool restOk,
            BasisMocapJoint bone, BasisMocapJoint parent, BasisRomChannel flexChannel, float[] v, bool[] ok)
        {
            if (!restOk || !f.Has(bone) || !f.Has(parent)) return;

            // NOT a re-implementation: this is the SHIPPING decomposition, handed the SHIPPING rest frame,
            // so a change to either shows up here instead of quietly drifting away from it.
            Quaternion local = BasisSpineAnatomyCore.Conj(f.Rot(parent)) * f.Rot(bone);
            BasisSpineAnatomyCore.Decompose(local * BasisSpineAnatomyCore.Conj(rest.RestLocalRot), rest,
                out float flex, out float lat, out float axial);

            int c = (int)flexChannel;
            v[c + 0] = flex; ok[c + 0] = true;
            v[c + 1] = lat; ok[c + 1] = true;
            v[c + 2] = axial; ok[c + 2] = true;
        }

        /// <summary>Rotation of `q` about `axis`, in degrees, from a swing-twist split. `axis` need not be unit.</summary>
        static float TwistDeg(Quaternion q, Vector3 axis)
        {
            float aSqr = axis.sqrMagnitude;
            if (!(aSqr > k_SqrEpsilon)) return 0f;
            axis /= Mathf.Sqrt(aSqr);

            if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);   // shortest arc, or the sign wraps at 180
            float proj = Vector3.Dot(new Vector3(q.x, q.y, q.z), axis);
            float norm = Mathf.Sqrt(proj * proj + q.w * q.w);
            if (!(norm > k_Epsilon)) return 0f;
            return 2f * Mathf.Atan2(proj / norm, q.w / norm) * Mathf.Rad2Deg;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════════
        // THE RUN
        // ════════════════════════════════════════════════════════════════════════════════════════════════

        public static BasisBodyPlausibilitySummary Run(BasisMotionClip clip) => Run(clip, null);

        /// <summary>
        /// Score a whole clip. Pass an accumulator to fold this clip into a corpus-wide envelope; the
        /// per-clip summary is filled either way.
        /// </summary>
        public static BasisBodyPlausibilitySummary Run(BasisMotionClip clip, BasisRomAccumulator into)
        {
            var r = new BasisBodyPlausibilitySummary
            {
                Ok = false,
                Clip = clip?.Name,
                PairWorstFracLimb = new float[k_Pairs.Length],
                PairWorstFracRadii = new float[k_Pairs.Length],
                PairHitFrames = new int[k_Pairs.Length],
                Rom = new BasisRomStat[(int)BasisRomChannel.Count],
            };

            try
            {
                if (clip == null || clip.FrameCount < 2) { r.Error = "clip too short"; return r; }

                BasisMocapPose[] buf = BasisBodyFrame.Allocate();
                BasisBodyFrame first = BasisBodyFrame.FromClip(clip, 0, buf);
                BasisBodyScale scale = BuildScale(first);
                if (!scale.Valid) { r.Error = scale.Error; return r; }

                BasisBodySpineRest spineRest = BuildSpineRest(clip);

                int n = clip.FrameCount;
                r.Frames = n;

                var clipRom = new BasisRomAccumulator();
                var values = new float[(int)BasisRomChannel.Count];
                var valid = new bool[(int)BasisRomChannel.Count];
                var pen = new List<float>(n);

                for (int fi = 0; fi < n; fi++)
                {
                    BasisBodyFrame frame = BasisBodyFrame.FromClip(clip, fi, buf);

                    // ── (A) ──
                    float framePen = 0f;
                    for (int p = 0; p < k_Pairs.Length; p++)
                    {
                        if (Overlap(frame, scale, p, out float fracLimb, out float fracRadii) <= 0f) continue;
                        r.PairHitFrames[p]++;
                        if (fracLimb > r.PairWorstFracLimb[p]) r.PairWorstFracLimb[p] = fracLimb;
                        if (fracRadii > r.PairWorstFracRadii[p]) r.PairWorstFracRadii[p] = fracRadii;
                        if (fracLimb > framePen) framePen = fracLimb;
                        if (fracLimb > r.WorstPenetrationFracLimb)
                        {
                            r.WorstPenetrationFracLimb = fracLimb;
                            r.WorstPenetrationFracRadii = fracRadii;
                            r.WorstPair = k_Pairs[p].Label;
                            r.WorstPairFrame = fi;
                        }
                        // The gated worst is tracked separately -- the raw worst above stays honest for the
                        // report, but only a pair whose depth DISCRIMINATES is allowed to fail a clip.
                        if (k_Pairs[p].DepthDiscriminating && fracLimb > r.WorstDiscriminatingPenetrationFracLimb)
                        {
                            r.WorstDiscriminatingPenetrationFracLimb = fracLimb;
                            r.WorstDiscriminatingPair = k_Pairs[p].Label;
                            r.WorstDiscriminatingPairFrame = fi;
                        }
                    }
                    if (framePen > 0f) r.IntersectFrames++;
                    pen.Add(framePen);

                    // ── (B) ──
                    MeasureRom(frame, scale, spineRest, values, valid);
                    bool violated = false;
                    for (int c = 0; c < values.Length; c++)
                    {
                        if (!valid[c]) continue;
                        clipRom.Add((BasisRomChannel)c, values[c]);
                        into?.Add((BasisRomChannel)c, values[c]);

                        BasisRomSpec spec = k_Specs[c];
                        if (!spec.Gated) continue;
                        float excess = values[c] < spec.Lo ? spec.Lo - values[c]
                                     : values[c] > spec.Hi ? values[c] - spec.Hi : 0f;
                        if (!(excess > 0f)) continue;
                        violated = true;
                        if (excess > r.WorstRomExcess)
                        {
                            r.WorstRomExcess = excess;
                            r.WorstRomChannel = spec.Name;
                            r.WorstRomDirection = values[c] < spec.Lo ? -1 : +1;
                        }
                    }
                    if (violated) r.RomViolationFrames++;
                }

                r.IntersectFrac = (float)r.IntersectFrames / n;
                r.RomViolationFrac = (float)r.RomViolationFrames / n;
                pen.Sort();
                r.PenetrationP95FracLimb = Percentile(pen, 95);
                r.PenetrationP99FracLimb = Percentile(pen, 99);
                for (int c = 0; c < (int)BasisRomChannel.Count; c++) r.Rom[c] = clipRom.Summarise((BasisRomChannel)c);

                r.Ok = true;
            }
            catch (System.Exception e)
            {
                r.Ok = false;
                r.Error = e.Message;
            }
            return r;
        }

        static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0) return 0f;
            int i = Mathf.Clamp(Mathf.RoundToInt(p / 100f * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[i];
        }

        /// <summary>
        /// The verdict, in the shape of BasisMocapAccuracy.Gate / BasisMocapFootQuality.Gate.
        ///
        /// ⭐ WHAT IS AND IS NOT GATED, AND WHY IT MATTERS THAT THE LIST IS SHORT. Only channels whose limit
        /// is an ALREADY-MEASURED law gate: the elbow ceiling (55,140 frames), the knee's anterior half-space
        /// (a hard geometric guard), and the spine table (corpus-vetoed under 0.6%). Everything else is
        /// reported. That is not timidity -- a gate on a guessed threshold either fires on real humans, in
        /// which case it gets disabled and stops protecting anything, or it is set so loose it never fires,
        /// in which case it was decoration. Read the report, then move a channel's `Gated` to true.
        /// </summary>
        public static (bool pass, string reason) Gate(in BasisBodyPlausibilitySummary s)
        {
            if (!s.Ok) return (false, s.Error);

            if (s.IntersectFrac > MaxIntersectFrac)
                return (false, $"the body passes through itself on {100f * s.IntersectFrac:F2}% of frames (limit {100f * MaxIntersectFrac:F2}%); worst {s.WorstPair} at frame {s.WorstPairFrame}");

            // ⚠ ON THE DISCRIMINATING PAIRS ONLY. Judging the raw worst here failed 8 of 109 real human clips
            // -- see PairDepthDiscriminating and the measurement above the Pair struct.
            if (s.WorstDiscriminatingPenetrationFracLimb > MaxPenetrationFracLimb)
                return (false, $"{s.WorstDiscriminatingPair} penetrates {s.WorstDiscriminatingPenetrationFracLimb:F3} of a limb length at frame {s.WorstDiscriminatingPairFrame} (limit {MaxPenetrationFracLimb:F3})");

            if (s.RomViolationFrac > MaxRomViolationFrac)
            {
                string dir = s.WorstRomDirection < 0 ? "BELOW its low limit" : "ABOVE its high limit";
                return (false, $"a joint leaves its anatomical envelope on {100f * s.RomViolationFrac:F2}% of frames (limit {100f * MaxRomViolationFrac:F2}%); worst '{s.WorstRomChannel}' {dir} by {s.WorstRomExcess:F3}");
            }

            return (true, null);
        }
    }
}
#endif
