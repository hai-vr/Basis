#if UNITY_EDITOR
using UnityEngine;
using Basis.Scripts.TransformBinders.BoneControl;

namespace Basis.IK.Mocap
{
    // A REAL TRACKER, MODELLED. The input-realism layer the accuracy harness never had.
    //
    // Editor-only on purpose but in the RUNTIME assembly, for exactly the reason documented in the header of
    // BasisBvhLoader.cs: Basis.Framework.IK.Tests does not reference BasisEditor, so a device model living in
    // the editor assembly would force the tests to hand-mirror its maths -- the drift trap the whole
    // core/sweep/gate convention exists to avoid.
    //
    // ================================================================================================
    // WHY THIS EXISTS: BasisMocapHintSource.TruthJoint IS A FICTION.
    //
    // That row is described as "the elbow/knee tracker case: hand the solver the real joint. The accuracy
    // CEILING." A real tracker is not the joint. It stands off the bone by centimetres, its mount rotation is
    // a strap convention rather than the bone's frame, it slips over a session, it jitters, it drops out, and
    // it arrives late and at a different rate from the headset. TruthJoint models none of that, so the
    // "ceiling" it reports is the ceiling of an input the runtime cannot produce.
    //
    // This blind spot has already cost a shipped bug. TruthJoint sat at 1.06% for a long time and was quoted
    // as the achievable ceiling; the solver was in fact quietly DISCARDING the tracker via fades and a boolean
    // pole gate, and once that was fixed the row went to 0.00%. The recorded lesson: WHEN A "KNOWN CEILING"
    // SURVIVES EVERY IMPROVEMENT, SUSPECT IT IS A BUG, NOT A LIMIT. The harness was structurally incapable of
    // seeing it because it never modelled a device.
    // ================================================================================================
    //
    // WHERE THE NUMBERS COME FROM. Two of the three sources are already calibrated in this repo and are
    // reproduced here VERBATIM -- see BasisSyntheticTrackerCalibration for the pinned constants:
    //
    //   * BasisArmTrackerHintAuthorityTests (Tests/Editor) carried a one-off strapped-puck model: stand-off
    //     swept 0-6 cm, a slide along the arm, a 1 mm gaussian noise floor, and 2 cm / 3 cm glitches. Those
    //     ranges were calibrated against a real user complaint. This file is that model PROMOTED to a shared,
    //     parameterised one; the numbers are carried across unchanged and pinned by test.
    //
    //   * BasisTrackerPlacementSweep (com.basis.framework.editor) already speaks a placement-quality
    //     vocabulary -- 0 / 2 / 4 cm, "perfect, typical strap/tracking slop, sloppy" -- applied ONCE per
    //     scenario, i.e. it is a PLACEMENT error, not a per-frame noise. It therefore corroborates the
    //     stand-off and mount-error bands here, and deliberately does NOT map onto JitterSigmaM. Its
    //     BasisBoneTrackedRole vocabulary is reused directly (MocapJointForRole) so the project has one set
    //     of role names, not two.
    //
    //   * The latency and update-rate figures are DEVICE ESTIMATES, not measurements taken in this repo.
    //     They are labelled as such on BasisSyntheticTrackerDevice. Treat them as the least trustworthy
    //     numbers here and replace them the moment a real capture exists.

    /// <summary>
    /// How well the device is strapped on: the SPATIAL half of the model. Ladder deliberately mirrors
    /// BasisTrackerPlacementSweep's "perfect / typical strap slop / sloppy" so the project has one vocabulary.
    /// </summary>
    public enum BasisSyntheticTrackerQuality
    {
        /// <summary>Not a device at all: an exact, instantaneous, noiseless report of the joint. This is what
        /// BasisMocapHintSource.TruthJoint already models, and it is a BIT-EXACT no-op here so a corpus run can
        /// be A/B'd against every existing baseline without re-basing it.</summary>
        Ideal,
        Good,
        Typical,
        Poor,
    }

    /// <summary>
    /// How fast and how late the device reports: the TEMPORAL half. Per-device, because a headset and a puck
    /// do not arrive on the same schedule and the solver sees whatever each last delivered.
    /// </summary>
    public enum BasisSyntheticTrackerDevice
    {
        /// <summary>Continuous and instantaneous. Not a real device -- the neutral element.</summary>
        Perfect,
        /// <summary>An HMD. ~90 Hz.</summary>
        Headset,
        /// <summary>A lighthouse puck over OpenVR. ~250 Hz. Note BasisTrackerHardwareClassification: SlimeVR
        /// also arrives through OpenVR, so device CLASS cannot be inferred from the transport.</summary>
        OpenVrPuck,
        /// <summary>An IMU tracker over WiFi. ~100 Hz, later, and its error is dominated by low-frequency
        /// rotational drift rather than by positional noise.</summary>
        SlimeVr,
    }

    /// <summary>The named configurations. Ideal is the identity and is guarded as bit-exact by test.</summary>
    public enum BasisSyntheticTrackerPreset
    {
        Ideal,
        Headset,
        GoodPuck,
        TypicalPuck,
        PoorPuck,
        SlimeVR,
    }

    /// <summary>
    /// The constants carried across from the models that already exist in this repo. Pinned by
    /// BasisSyntheticTrackerTests.CalibratedConstants_MatchTheModelsTheyWerePromotedFrom so that a later edit
    /// cannot silently retune a number that was calibrated against a real complaint.
    /// </summary>
    public static class BasisSyntheticTrackerCalibration
    {
        /// <summary>BasisArmTrackerHintAuthorityTests.k_StandOffs, verbatim. 0.00 is the calibrated-onto-the-bone
        /// case BasisLocalRigDriver actually produces; 0.03-0.06 is a puck proud of a real arm.</summary>
        public static readonly float[] StandOffsM = { 0.00f, 0.01f, 0.02f, 0.03f, 0.04f, 0.06f };

        /// <summary>The slide used throughout those tests: a strap goes round the limb, not through the joint.
        /// Provably invisible to BasisArmSolveCore, which reads only the component of shoulder->hint
        /// PERPENDICULAR to the shoulder->hand axis -- see StrapPosition_AlongTheArm_CannotChangeTheCommandedSwivel.
        /// Kept anyway because it is NOT invisible to the leg, nor to anything that consumes the hint's rotation.</summary>
        public const float SlideM = 0.05f;

        /// <summary>A lighthouse puck's static noise floor, from TrackerNoise(rng, 0.001f, ...).</summary>
        public const float JitterSigmaM = 0.001f;

        /// <summary>From AStrappedTracker_NeverMovesTheHandOffItsTarget_EvenUnderNoise:
        /// TrackerNoise(rng, 0.001f, 0.02f, 0.02f) -- 2% of samples get a 2 cm pop.</summary>
        public const float GlitchChanceTypical = 0.02f;
        public const float GlitchMTypical = 0.02f;

        /// <summary>From ATrackerGlitch_MovesTheElbowButBreaksNeitherTheHandNorTheAnatomy:
        /// TrackerNoise(rng, 0.001f, 0.10f, 0.03f) -- "10% of frames get a 3 cm pop".</summary>
        public const float GlitchChancePoor = 0.10f;
        public const float GlitchMPoor = 0.03f;

        /// <summary>BasisTrackerPlacementSweep.Default().JitterCm, verbatim: "perfect, typical strap/tracking
        /// slop, sloppy". Applied ONCE per scenario there, so it is a PLACEMENT error and corroborates the
        /// stand-off / mount bands below -- it is NOT a per-frame noise and must not be read as one.</summary>
        public const float PlacementCmPerfect = 0f;
        public const float PlacementCmTypical = 2f;
        public const float PlacementCmSloppy = 4f;
    }

    /// <summary>
    /// The mount frame: where round the limb the puck sits, and which way is "off the bone".
    ///
    /// Outward is the limb's outward normal at the joint -- the direction the joint bulges away from the
    /// root->tip axis. That is the SAME quantity BasisArmTrackerHintAuthorityTests.StrappedTracker used
    /// (u*cos t + v*sin t, the radial from the elbow circle's centre), which is why a strapped puck carries a
    /// swivel signal at all: it rides the limb's roll.
    /// </summary>
    public struct BasisSyntheticTrackerMount
    {
        /// <summary>Unit, root -> tip along the limb.</summary>
        public Vector3 Axis;
        /// <summary>Unit, perpendicular to Axis, pointing from the axis out through the joint.</summary>
        public Vector3 Outward;
        /// <summary>The joint's distance off the root->tip axis. Collapses to zero as the limb straightens.</summary>
        public float RadiusM;
        /// <summary>
        /// True when the limb is straight enough that Outward is not determined by geometry (RadiusM ~ 0) and
        /// an arbitrary perpendicular was substituted. This is not a corner case: it is full extension, which
        /// is where a VR user spends most of their time and where BasisArmSolveCore's hard pole epsilon lives.
        /// A caller that averages over it without noticing is measuring an arbitrary direction.
        /// </summary>
        public bool Degenerate;
    }

    /// <summary>What the device actually delivered this frame.</summary>
    public struct BasisSyntheticTrackerSample
    {
        public Vector3 Position;
        public Quaternion Rotation;

        /// <summary>False only when the device has never delivered anything -- there is nothing to hold.</summary>
        public bool Valid;

        /// <summary>
        /// This is the SAME sample as last frame: the device did not deliver a new one and the previous one is
        /// being held. A hold, never an interpolation, because a hold is what actually reaches the solver.
        ///
        /// Deliberately NOT "this sample is old". Latency alone makes every sample old while still delivering a
        /// NEW one each frame, and the two have completely different consequences: a delayed-but-moving input
        /// costs the solver phase, a repeated input costs it a velocity of zero followed by a step. Read AgeS
        /// for the former and this for the latter.
        /// </summary>
        public bool Held;

        /// <summary>This sample carries a glitch excursion (occlusion / IMU pop).</summary>
        public bool Glitched;

        /// <summary>Seconds since the held sample was measured. Zero on a fresh sample.</summary>
        public float AgeS;

        public BasisMocapPose AsPose() => new BasisMocapPose
        {
            Position = Position,
            Rotation = Rotation,
            Valid = Valid,
        };
    }

    /// <summary>
    /// Everything that stands between the bone and what the solver is handed. Every field is neutral at zero,
    /// and a wholly-zero params is a BIT-EXACT identity -- each stage is skipped rather than multiplied by
    /// zero, so no float rounding can creep in. That is what lets a realistic-tracker row be A/B'd against an
    /// existing baseline without re-basing the baseline.
    /// </summary>
    public struct BasisSyntheticTrackerParams
    {
        // ── mount: fixed for the session ────────────────────────────────────────────────────────────
        /// <summary>Metres the puck stands proud of the joint centre, along the limb's outward normal.
        /// Range from BasisSyntheticTrackerCalibration.StandOffsM: 0 (calibrated onto the bone, which is what
        /// BasisLocalRigDriver sends) to 0.06 (a mount with real residual offset).</summary>
        public float StandOffM;

        /// <summary>Metres PROXIMAL of the joint along the limb axis -- a strap goes round the limb, not
        /// through the joint.</summary>
        public float SlideM;

        /// <summary>Magnitude of the fixed rotational offset between the device's frame and the bone's. A
        /// STRAP CONVENTION, not noise: it is drawn once from the seed and never changes during the session.
        /// Applies to the reported ROTATION only.</summary>
        public float MountErrorDeg;

        /// <summary>Magnitude of the fixed error in where ROUND the limb the puck sits. Drawn once from the
        /// seed, within +-this. Separate from MountErrorDeg because it moves the puck's POSITION (its angular
        /// station on the joint's circle) rather than its reported rotation, and those two failures look
        /// nothing alike to the solver.</summary>
        public float MountRollDeg;

        // ── slip: slow drift over the session ───────────────────────────────────────────────────────
        /// <summary>Peak drift along the limb axis, metres. LOW-FREQUENCY, not gaussian: a strap migrates, it
        /// does not rattle. Provably invisible to BasisArmSolveCore (it reads only the perpendicular
        /// component), which is exactly why it is modelled separately from SlipAroundDeg -- one of these can
        /// roll the elbow and the other cannot, and collapsing them would hide that.</summary>
        public float SlipAlongM;

        /// <summary>Peak drift in angular station about the limb axis, degrees. THIS is the slip that reaches
        /// the elbow.</summary>
        public float SlipAroundDeg;

        /// <summary>The slip's fundamental frequency. SLOW: 0.01 Hz is a drift over a couple of minutes.</summary>
        public float SlipHz;

        // ── noise ───────────────────────────────────────────────────────────────────────────────────
        /// <summary>Per-axis gaussian position noise, metres RMS. 0.001 is a lighthouse puck's static floor.</summary>
        public float JitterSigmaM;

        /// <summary>Per-axis gaussian rotation noise, degrees RMS.</summary>
        public float JitterSigmaDeg;

        /// <summary>Probability, per DEVICE SAMPLE (not per frame), of a large excursion.</summary>
        public float GlitchChance;

        /// <summary>The excursion's magnitude, metres.</summary>
        public float GlitchM;

        // ── delivery ────────────────────────────────────────────────────────────────────────────────
        /// <summary>Probability, per device sample, that the device fails to deliver at all.</summary>
        public float DropoutChance;

        /// <summary>How long one dropout keeps the device silent. The consumer holds the last sample
        /// throughout -- which is what the runtime does, and what therefore reaches the solver.</summary>
        public float DropoutHoldS;

        /// <summary>Transport + fusion delay, seconds. The consumer sees the newest sample taken at or before
        /// (now - this).</summary>
        public float LatencyS;

        /// <summary>Device sample rate. Zero means continuous (a sample every time it is asked). When the
        /// device is SLOWER than the consumer the consumer holds; when it is faster, every frame is fresh.</summary>
        public float UpdateRateHz;

        /// <summary>True when this configuration is the exact identity -- every stage skipped, output
        /// bit-identical to the truth.</summary>
        public bool IsIdentity =>
            StandOffM == 0f && SlideM == 0f && MountErrorDeg == 0f && MountRollDeg == 0f &&
            SlipAlongM == 0f && SlipAroundDeg == 0f &&
            JitterSigmaM <= 0f && JitterSigmaDeg <= 0f &&
            GlitchChance <= 0f && DropoutChance <= 0f &&
            LatencyS <= 0f && UpdateRateHz <= 0f;

        public static BasisSyntheticTrackerParams Identity => default;

        /// <summary>
        /// Compose a spatial quality with a device's timing. The two are orthogonal on purpose: how well a
        /// puck is strapped on says nothing about how fast it reports, and a harness that conflates them
        /// cannot tell a mount problem from a latency problem.
        /// </summary>
        public static BasisSyntheticTrackerParams Make(BasisSyntheticTrackerQuality quality, BasisSyntheticTrackerDevice device)
        {
            BasisSyntheticTrackerParams p = default;

            switch (quality)
            {
                case BasisSyntheticTrackerQuality.Ideal:
                    break;   // identity: every field stays neutral

                case BasisSyntheticTrackerQuality.Good:
                    // A careful mount. Stand-off from the low end of the arm tests' calibrated sweep.
                    p.StandOffM = 0.02f;
                    p.SlideM = BasisSyntheticTrackerCalibration.SlideM;
                    p.MountErrorDeg = 3f;
                    p.MountRollDeg = 3f;
                    p.SlipAlongM = 0.005f;
                    p.SlipAroundDeg = 2f;
                    p.SlipHz = 0.01f;
                    p.JitterSigmaM = BasisSyntheticTrackerCalibration.JitterSigmaM;
                    p.JitterSigmaDeg = 0.10f;
                    p.GlitchChance = 0.005f;
                    p.GlitchM = BasisSyntheticTrackerCalibration.GlitchMTypical;
                    p.DropoutChance = 0f;
                    p.DropoutHoldS = 0f;
                    break;

                case BasisSyntheticTrackerQuality.Typical:
                    // The mid of the arm tests' stand-off sweep, and its 2% / 2 cm glitch pair verbatim.
                    p.StandOffM = 0.04f;
                    p.SlideM = BasisSyntheticTrackerCalibration.SlideM;
                    p.MountErrorDeg = 8f;
                    p.MountRollDeg = 8f;
                    p.SlipAlongM = 0.015f;
                    p.SlipAroundDeg = 5f;
                    p.SlipHz = 0.01f;
                    p.JitterSigmaM = BasisSyntheticTrackerCalibration.JitterSigmaM;
                    p.JitterSigmaDeg = 0.25f;
                    p.GlitchChance = BasisSyntheticTrackerCalibration.GlitchChanceTypical;
                    p.GlitchM = BasisSyntheticTrackerCalibration.GlitchMTypical;
                    p.DropoutChance = 0.002f;
                    p.DropoutHoldS = 0.05f;
                    break;

                case BasisSyntheticTrackerQuality.Poor:
                    // The top of the arm tests' stand-off sweep, and its 10% / 3 cm glitch pair verbatim.
                    p.StandOffM = 0.06f;
                    p.SlideM = BasisSyntheticTrackerCalibration.SlideM;
                    p.MountErrorDeg = 15f;
                    p.MountRollDeg = 15f;
                    p.SlipAlongM = 0.030f;
                    p.SlipAroundDeg = 12f;
                    p.SlipHz = 0.02f;
                    p.JitterSigmaM = 2f * BasisSyntheticTrackerCalibration.JitterSigmaM;
                    p.JitterSigmaDeg = 0.50f;
                    p.GlitchChance = BasisSyntheticTrackerCalibration.GlitchChancePoor;
                    p.GlitchM = BasisSyntheticTrackerCalibration.GlitchMPoor;
                    p.DropoutChance = 0.01f;
                    p.DropoutHoldS = 0.10f;
                    break;
            }

            DeviceTiming(device, out float rateHz, out float latencyS);
            p.UpdateRateHz = rateHz;
            p.LatencyS = latencyS;

            if (device == BasisSyntheticTrackerDevice.SlimeVr && quality != BasisSyntheticTrackerQuality.Ideal)
            {
                // An IMU's error is not shaped like a lighthouse puck's. Its position comes from a body model
                // rather than from a measurement, so the positional NOISE is not the problem -- the yaw DRIFT
                // is, and drift is low-frequency by definition. So the slip terms go up and the jitter does not.
                p.SlipAroundDeg *= 2f;
                p.JitterSigmaDeg *= 3f;
            }

            return p;
        }

        public static BasisSyntheticTrackerParams FromPreset(BasisSyntheticTrackerPreset preset)
        {
            switch (preset)
            {
                case BasisSyntheticTrackerPreset.Headset:
                {
                    // A headset is not strapped to a limb: it is the best-tracked thing in the room and its
                    // offset is a calibration, not a strap. So quality terms are near zero and only the
                    // timing is real.
                    BasisSyntheticTrackerParams p = Make(BasisSyntheticTrackerQuality.Ideal, BasisSyntheticTrackerDevice.Headset);
                    p.MountErrorDeg = 2f;
                    p.JitterSigmaM = 0.0005f;
                    p.JitterSigmaDeg = 0.05f;
                    return p;
                }
                case BasisSyntheticTrackerPreset.GoodPuck:
                    return Make(BasisSyntheticTrackerQuality.Good, BasisSyntheticTrackerDevice.OpenVrPuck);
                case BasisSyntheticTrackerPreset.TypicalPuck:
                    return Make(BasisSyntheticTrackerQuality.Typical, BasisSyntheticTrackerDevice.OpenVrPuck);
                case BasisSyntheticTrackerPreset.PoorPuck:
                    return Make(BasisSyntheticTrackerQuality.Poor, BasisSyntheticTrackerDevice.OpenVrPuck);
                case BasisSyntheticTrackerPreset.SlimeVR:
                    return Make(BasisSyntheticTrackerQuality.Poor, BasisSyntheticTrackerDevice.SlimeVr);
                default:
                    return Identity;
            }
        }

        /// <summary>
        /// Update rate and delivery latency per device class.
        ///
        /// ⚠ THESE ARE ESTIMATES. Unlike the mount and noise numbers -- which are carried across from models
        /// already calibrated in this repo -- nothing here has been measured against a real device by this
        /// project. The RATES are well known (HMD ~90 Hz, OpenVR pucks ~250 Hz, SlimeVR ~100 Hz); the
        /// LATENCIES are plausible order-of-magnitude figures and are the least trustworthy constants in this
        /// file. Replace them the moment a capture exists, and do not quote a result that turns on them
        /// without saying so.
        /// </summary>
        public static void DeviceTiming(BasisSyntheticTrackerDevice device, out float rateHz, out float latencyS)
        {
            switch (device)
            {
                case BasisSyntheticTrackerDevice.Headset:
                    rateHz = 90f; latencyS = 0.011f; break;
                case BasisSyntheticTrackerDevice.OpenVrPuck:
                    rateHz = 250f; latencyS = 0.008f; break;
                case BasisSyntheticTrackerDevice.SlimeVr:
                    rateHz = 100f; latencyS = 0.030f; break;
                default:
                    rateHz = 0f; latencyS = 0f; break;
            }
        }
    }

    /// <summary>
    /// A small, explicit, seeded PRNG. SplitMix64.
    ///
    /// NOT UnityEngine.Random: that is global mutable state, so any other code touching it would silently
    /// change this model's answers, and a test whose answer moves run to run is worse than no test. Not
    /// System.Random either -- its float output is not guaranteed identical between Mono and CoreCLR, and
    /// this model is compiled by both (Unity's test runner and dotnet build). SplitMix64 is integer-only
    /// until the final division by a power of two, so the uniform stream is bit-identical everywhere.
    /// </summary>
    public struct BasisSyntheticTrackerRng
    {
        ulong m_State;

        public BasisSyntheticTrackerRng(ulong seed) { m_State = seed; }

        public ulong NextULong()
        {
            m_State += 0x9E3779B97F4A7C15UL;
            ulong z = m_State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform in [0,1). 24 bits over 2^24 -- an exact power-of-two division, so no rounding.</summary>
        public float NextFloat01() => (NextULong() >> 40) * (1f / 16777216f);

        /// <summary>Uniform in [-1,1).</summary>
        public float NextSigned() => NextFloat01() * 2f - 1f;

        /// <summary>Box-Muller, one variate per call. The second is discarded rather than cached: caching would
        /// make a draw depend on how many draws came before it, and every A/B in this file relies on a stream
        /// being a pure function of its seed and its index.</summary>
        public float NextGaussian()
        {
            float u1 = 1f - NextFloat01();   // (0,1], so Log is finite
            float u2 = NextFloat01();
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        public Vector3 NextGaussian3() => new Vector3(NextGaussian(), NextGaussian(), NextGaussian());

        /// <summary>A uniformly distributed unit vector. Rejection-free: Marsaglia's method.</summary>
        public Vector3 NextUnitVector()
        {
            float z = NextSigned();
            float t = 2f * Mathf.PI * NextFloat01();
            float r = Mathf.Sqrt(Mathf.Max(1f - z * z, 0f));
            return new Vector3(r * Mathf.Cos(t), r * Mathf.Sin(t), z);
        }
    }

    /// <summary>
    /// Turns a true joint pose into a plausible measured device pose. Stateful, because latency, update rate,
    /// dropout and slip are all temporal -- feed it frames in order.
    ///
    /// Deterministic: the whole stream is a pure function of (params, seed, the sequence of calls). Each
    /// effect draws from its OWN stream, so switching one on cannot shift another's numbers -- that is what
    /// makes "stand-off moves the position but not the jitter" a meaningful thing to assert rather than a
    /// coincidence of draw ordering.
    /// </summary>
    public sealed class BasisSyntheticTracker
    {
        // Enough history for the worst latency at the highest rate (30 ms at 250 Hz = 8 entries) with room to
        // spare. Latency beyond this is clamped rather than silently truncated -- see Sample.
        const int k_RingCapacity = 128;

        struct Entry
        {
            public float Time;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Glitched;
        }

        readonly BasisSyntheticTrackerParams m_P;
        readonly int m_Seed;

        // Session constants, drawn once. A mount error that changed frame to frame would be noise, and the
        // whole point of it is that it is NOT noise.
        readonly Quaternion m_MountError;
        readonly float m_MountRollDeg;
        readonly float m_SlipPhaseAlongA, m_SlipPhaseAlongB;
        readonly float m_SlipPhaseAroundA, m_SlipPhaseAroundB;

        BasisSyntheticTrackerRng m_JitterPos, m_JitterRot, m_Glitch, m_Dropout;

        readonly Entry[] m_Ring = new Entry[k_RingCapacity];
        int m_Count, m_Head;
        bool m_Started;
        float m_StartTime, m_NextDeviceTime, m_SilentUntil;

        // Which entry the consumer was given last, so a REPEAT can be distinguished from a merely delayed
        // sample. Entry times are unique per push, so the timestamp identifies the entry.
        float m_LastDeliveredTime;
        bool m_HaveDelivered;

        /// <summary>The fixed strap convention this session was seeded with. Exposed so a test can assert it is
        /// CONSTANT across the session rather than inferring that from behaviour.</summary>
        public Quaternion MountError => m_MountError;

        /// <summary>The fixed angular-station error about the limb axis, degrees.</summary>
        public float MountRollDeg => m_MountRollDeg;

        public BasisSyntheticTrackerParams Params => m_P;
        public int Seed => m_Seed;

        public BasisSyntheticTracker(BasisSyntheticTrackerPreset preset, int seed)
            : this(BasisSyntheticTrackerParams.FromPreset(preset), seed) { }

        public BasisSyntheticTracker(in BasisSyntheticTrackerParams p, int seed)
        {
            m_P = p;
            m_Seed = seed;

            // Distinct odd salts per stream. Independent streams are the reason a parameter can be moved in
            // isolation: turning glitches on does not consume a jitter draw, so the jitter sequence is
            // unchanged and the two effects can be attributed apart.
            var session = new BasisSyntheticTrackerRng(Salt(seed, 0x5EED0001UL));
            m_JitterPos = new BasisSyntheticTrackerRng(Salt(seed, 0x5EED0002UL));
            m_JitterRot = new BasisSyntheticTrackerRng(Salt(seed, 0x5EED0003UL));
            m_Glitch = new BasisSyntheticTrackerRng(Salt(seed, 0x5EED0004UL));
            m_Dropout = new BasisSyntheticTrackerRng(Salt(seed, 0x5EED0005UL));

            m_MountError = p.MountErrorDeg == 0f
                ? Quaternion.identity
                : Quaternion.AngleAxis(p.MountErrorDeg, session.NextUnitVector());
            m_MountRollDeg = p.MountRollDeg == 0f ? 0f : session.NextSigned() * p.MountRollDeg;

            m_SlipPhaseAlongA = session.NextFloat01() * 2f * Mathf.PI;
            m_SlipPhaseAlongB = session.NextFloat01() * 2f * Mathf.PI;
            m_SlipPhaseAroundA = session.NextFloat01() * 2f * Mathf.PI;
            m_SlipPhaseAroundB = session.NextFloat01() * 2f * Mathf.PI;
        }

        static ulong Salt(int seed, ulong salt) => unchecked((ulong)(uint)seed * 0x100000001B3UL ^ salt);

        public void Reset()
        {
            m_Count = 0;
            m_Head = 0;
            m_Started = false;
            m_StartTime = 0f;
            m_NextDeviceTime = 0f;
            m_SilentUntil = float.NegativeInfinity;
            m_LastDeliveredTime = 0f;
            m_HaveDelivered = false;
        }

        // ── geometry ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The mount frame for a limb: root -> joint -> tip (shoulder/elbow/hand, or hip/knee/foot).
        ///
        /// Outward is the radial from the root->tip axis out through the joint, which is exactly the direction
        /// BasisArmTrackerHintAuthorityTests.StrappedTracker used. When the limb straightens that radial
        /// collapses and the direction stops being determined by geometry; the mount is flagged Degenerate and
        /// a stable arbitrary perpendicular is substituted so the caller still gets something finite.
        /// </summary>
        public static BasisSyntheticTrackerMount MountForLimb(Vector3 root, Vector3 joint, Vector3 tip)
        {
            var m = default(BasisSyntheticTrackerMount);

            Vector3 rt = tip - root;
            float d = rt.magnitude;
            m.Axis = d > 1e-8f ? rt / d : Vector3.forward;

            Vector3 radial = joint - root;
            radial -= m.Axis * Vector3.Dot(radial, m.Axis);
            m.RadiusM = radial.magnitude;

            if (m.RadiusM > 1e-6f)
            {
                m.Outward = radial / m.RadiusM;
                m.Degenerate = false;
            }
            else
            {
                m.Outward = StablePerpendicular(m.Axis);
                m.Degenerate = true;
            }
            return m;
        }

        /// <summary>Deterministic perpendicular: cross with whichever cardinal axis the input leans on least.</summary>
        static Vector3 StablePerpendicular(Vector3 axis)
        {
            Vector3 pick = Mathf.Abs(axis.x) < 0.9f ? Vector3.right : Vector3.up;
            Vector3 p = Vector3.Cross(axis, pick);
            float len = p.magnitude;
            return len > 1e-8f ? p / len : Vector3.up;
        }

        /// <summary>
        /// The STRAPPED PUCK, statically -- no noise, no time, just the mount geometry. This is the promoted
        /// form of BasisArmTrackerHintAuthorityTests.StrappedTracker and reproduces it exactly: the puck shares
        /// the joint's angular station about the limb, stands standOffM proud of the bone, and sits slideM
        /// proximal of the joint along the limb axis.
        /// </summary>
        public static Vector3 StrappedPosition(Vector3 root, Vector3 joint, Vector3 tip, float standOffM, float slideM)
        {
            BasisSyntheticTrackerMount m = MountForLimb(root, joint, tip);
            return StrappedPosition(joint, m, standOffM, slideM);
        }

        public static Vector3 StrappedPosition(Vector3 joint, in BasisSyntheticTrackerMount mount, float standOffM, float slideM)
        {
            Vector3 p = joint;
            if (standOffM != 0f) p += mount.Outward * standOffM;
            if (slideM != 0f) p -= mount.Axis * slideM;
            return p;
        }

        // ── the model ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One frame. `timeS` must be non-decreasing across calls -- the delivery model is a pump, not a
        /// function of t alone.
        ///
        /// With BasisSyntheticTrackerParams.Identity this returns `truth` BIT-EXACTLY: every stage is skipped
        /// rather than scaled by zero, and the delivery ring stores and returns the struct verbatim.
        /// </summary>
        public BasisSyntheticTrackerSample Sample(in BasisMocapPose truth, in BasisSyntheticTrackerMount mount, float timeS)
        {
            if (!m_Started)
            {
                m_Started = true;
                m_StartTime = timeS;
                m_NextDeviceTime = timeS;
                m_SilentUntil = float.NegativeInfinity;
            }

            float period = m_P.UpdateRateHz > 0f ? 1f / m_P.UpdateRateHz : 0f;
            bool tick = period <= 0f || timeS >= m_NextDeviceTime;

            if (tick)
            {
                if (timeS >= m_SilentUntil)
                {
                    bool drop = m_P.DropoutChance > 0f && m_Dropout.NextFloat01() < m_P.DropoutChance;
                    if (drop)
                    {
                        // The device goes silent; the consumer holds. That -- not a NaN, not a zero -- is what
                        // a dropout looks like by the time it reaches the solver.
                        m_SilentUntil = timeS + Mathf.Max(m_P.DropoutHoldS, 0f);
                    }
                    else
                    {
                        Measure(truth, mount, timeS, out Vector3 pos, out Quaternion rot, out bool glitched);
                        Push(timeS, pos, rot, glitched);
                    }
                }

                if (period > 0f)
                {
                    m_NextDeviceTime += period;
                    if (m_NextDeviceTime <= timeS) m_NextDeviceTime = timeS + period;
                }
            }

            return Deliver(timeS);
        }

        /// <summary>Convenience for callers that have the limb rather than a prepared mount.</summary>
        public BasisSyntheticTrackerSample Sample(in BasisMocapPose truth, Vector3 root, Vector3 tip, float timeS)
            => Sample(truth, MountForLimb(root, truth.Position, tip), timeS);

        void Measure(in BasisMocapPose truth, in BasisSyntheticTrackerMount mount, float timeS,
                     out Vector3 pos, out Quaternion rot, out bool glitched)
        {
            pos = truth.Position;
            rot = truth.Rotation;
            glitched = false;

            float t = timeS - m_StartTime;

            // ── where round the limb the puck sits. Mount roll is fixed; slip-around is the slow drift.
            Vector3 outward = mount.Outward;
            float aroundDeg = m_MountRollDeg;
            if (m_P.SlipAroundDeg != 0f)
            {
                aroundDeg += m_P.SlipAroundDeg * Slip01(t, m_P.SlipHz, m_SlipPhaseAroundA, m_SlipPhaseAroundB);
            }
            if (aroundDeg != 0f) outward = Quaternion.AngleAxis(aroundDeg, mount.Axis) * outward;

            // ── off the bone, and along it.
            if (m_P.StandOffM != 0f) pos += outward * m_P.StandOffM;

            float slide = m_P.SlideM;
            if (m_P.SlipAlongM != 0f)
            {
                slide += m_P.SlipAlongM * Slip01(t, m_P.SlipHz, m_SlipPhaseAlongA, m_SlipPhaseAlongB);
            }
            if (slide != 0f) pos -= mount.Axis * slide;

            // ── the strap convention: a fixed rotation between the device and the bone. Right-multiplied, so
            //    it is fixed IN THE BONE'S FRAME and rides the limb, which is what a strap does.
            if (m_P.MountErrorDeg != 0f) rot = rot * m_MountError;

            // ── measurement noise.
            if (m_P.JitterSigmaM > 0f) pos += m_JitterPos.NextGaussian3() * m_P.JitterSigmaM;
            if (m_P.JitterSigmaDeg > 0f)
            {
                Vector3 e = m_JitterRot.NextGaussian3() * m_P.JitterSigmaDeg;
                rot = rot * Quaternion.Euler(e.x, e.y, e.z);
            }

            // ── and the occasional pop: occlusion, a reflection, an IMU spike. Not noise -- a different
            //    process, so it draws from its own stream and is flagged rather than blended in.
            if (m_P.GlitchChance > 0f && m_Glitch.NextFloat01() < m_P.GlitchChance)
            {
                pos += m_Glitch.NextUnitVector() * m_P.GlitchM;
                glitched = true;
            }
        }

        /// <summary>
        /// The slip shape, in [-1, 1], starting at exactly 0.
        ///
        /// LOW-FREQUENCY BY CONSTRUCTION, which is the whole point: a strap migrates over a session, it does
        /// not rattle. Two incommensurate sinusoids so it does not simply repeat, scaled so |slip| &lt;= 1 and
        /// slip(0) == 0 (a freshly-strapped tracker has not slipped yet). The rate of change is therefore
        /// bounded by 0.5 * 0.811 * 2*pi*hz, which is what makes "this is slip and not noise" an assertable
        /// property rather than an assurance.
        /// </summary>
        public static float Slip01(float t, float hz, float phaseA, float phaseB)
        {
            if (hz <= 0f) return 0f;
            float w = 2f * Mathf.PI * hz;
            float now = 0.7f * Mathf.Sin(w * t + phaseA) + 0.3f * Mathf.Sin(0.37f * w * t + phaseB);
            float zero = 0.7f * Mathf.Sin(phaseA) + 0.3f * Mathf.Sin(phaseB);
            return 0.5f * (now - zero);
        }

        /// <summary>The analytic bound on |d(Slip01)/dt|. Exposed so a test can assert the low-frequency claim
        /// against the maths rather than against a magic number.</summary>
        public static float Slip01MaxRatePerSecond(float hz) => hz <= 0f ? 0f : 0.5f * 0.811f * 2f * Mathf.PI * hz;

        void Push(float time, Vector3 pos, Quaternion rot, bool glitched)
        {
            m_Ring[m_Head] = new Entry { Time = time, Position = pos, Rotation = rot, Glitched = glitched };
            m_Head = (m_Head + 1) % k_RingCapacity;
            if (m_Count < k_RingCapacity) m_Count++;
        }

        BasisSyntheticTrackerSample Deliver(float timeS)
        {
            var s = default(BasisSyntheticTrackerSample);
            if (m_Count == 0)
            {
                // The device has never delivered. Nothing to hold, so say so rather than inventing a pose --
                // an invented pose here is exactly the kind of fiction this file exists to remove.
                s.Valid = false;
                return s;
            }

            float deliverBy = timeS - Mathf.Max(m_P.LatencyS, 0f);

            // Newest entry old enough to have arrived. Walk back from the head; the ring is in time order.
            int idx = -1;
            for (int k = 1; k <= m_Count; k++)
            {
                int i = (m_Head - k + k_RingCapacity) % k_RingCapacity;
                if (m_Ring[i].Time <= deliverBy)
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
            {
                // Warm-up: the session is younger than the latency, or the latency exceeds the ring. The
                // runtime seeds a tracker's pose on first poll rather than handing the solver nothing, so
                // deliver the oldest thing the device measured and flag it stale.
                idx = (m_Head - m_Count + k_RingCapacity) % k_RingCapacity;
            }

            Entry e = m_Ring[idx];
            s.Position = e.Position;
            s.Rotation = e.Rotation;
            s.Glitched = e.Glitched;
            s.Valid = true;
            s.AgeS = timeS - e.Time;
            s.Held = m_HaveDelivered && e.Time == m_LastDeliveredTime;

            m_LastDeliveredTime = e.Time;
            m_HaveDelivered = true;
            return s;
        }

        // ── the shared role vocabulary ──────────────────────────────────────────────────────────────

        /// <summary>
        /// BasisBoneTrackedRole -> the corpus joint it measures. Reuses the role vocabulary
        /// BasisTrackerPlacementSweep already speaks (its Presets are lists of exactly these roles) so a
        /// tracker SET defined there -- "waist+feet", "ten-point", "full+toes" -- can drive which joints this
        /// model is attached to here, instead of the two sweeps inventing separate names for the same thing.
        ///
        /// Returns BasisMocapJoint.Count for roles the mocap corpus does not carry.
        /// </summary>
        public static BasisMocapJoint MocapJointForRole(BasisBoneTrackedRole role)
        {
            switch (role)
            {
                case BasisBoneTrackedRole.Head: return BasisMocapJoint.Head;
                case BasisBoneTrackedRole.Neck: return BasisMocapJoint.Neck;
                case BasisBoneTrackedRole.Chest: return BasisMocapJoint.Chest;
                case BasisBoneTrackedRole.Hips: return BasisMocapJoint.Hips;
                case BasisBoneTrackedRole.Spine: return BasisMocapJoint.Spine;
                case BasisBoneTrackedRole.LeftUpperLeg: return BasisMocapJoint.LeftUpperLeg;
                case BasisBoneTrackedRole.RightUpperLeg: return BasisMocapJoint.RightUpperLeg;
                case BasisBoneTrackedRole.LeftLowerLeg: return BasisMocapJoint.LeftLowerLeg;
                case BasisBoneTrackedRole.RightLowerLeg: return BasisMocapJoint.RightLowerLeg;
                case BasisBoneTrackedRole.LeftFoot: return BasisMocapJoint.LeftFoot;
                case BasisBoneTrackedRole.RightFoot: return BasisMocapJoint.RightFoot;
                case BasisBoneTrackedRole.LeftToes: return BasisMocapJoint.LeftToes;
                case BasisBoneTrackedRole.RightToes: return BasisMocapJoint.RightToes;
                case BasisBoneTrackedRole.LeftShoulder: return BasisMocapJoint.LeftShoulder;
                case BasisBoneTrackedRole.RightShoulder: return BasisMocapJoint.RightShoulder;
                case BasisBoneTrackedRole.LeftUpperArm: return BasisMocapJoint.LeftUpperArm;
                case BasisBoneTrackedRole.RightUpperArm: return BasisMocapJoint.RightUpperArm;
                case BasisBoneTrackedRole.LeftLowerArm: return BasisMocapJoint.LeftLowerArm;
                case BasisBoneTrackedRole.RightLowerArm: return BasisMocapJoint.RightLowerArm;
                case BasisBoneTrackedRole.LeftHand: return BasisMocapJoint.LeftHand;
                case BasisBoneTrackedRole.RightHand: return BasisMocapJoint.RightHand;
                default: return BasisMocapJoint.Count;
            }
        }

        /// <summary>
        /// The limb a strapped tracker for this role rides: (root, joint, tip). The mount frame needs all
        /// three, and getting them from the role rather than from the call site is what keeps the arm and the
        /// leg on one model. Returns false for roles that are not mid-limb joints, which are the only ones
        /// this stand-off model describes.
        /// </summary>
        public static bool TryLimbForRole(BasisBoneTrackedRole role,
                                          out BasisMocapJoint root, out BasisMocapJoint joint, out BasisMocapJoint tip)
        {
            switch (role)
            {
                case BasisBoneTrackedRole.LeftLowerArm:
                    root = BasisMocapJoint.LeftUpperArm; joint = BasisMocapJoint.LeftLowerArm; tip = BasisMocapJoint.LeftHand; return true;
                case BasisBoneTrackedRole.RightLowerArm:
                    root = BasisMocapJoint.RightUpperArm; joint = BasisMocapJoint.RightLowerArm; tip = BasisMocapJoint.RightHand; return true;
                case BasisBoneTrackedRole.LeftLowerLeg:
                    root = BasisMocapJoint.LeftUpperLeg; joint = BasisMocapJoint.LeftLowerLeg; tip = BasisMocapJoint.LeftFoot; return true;
                case BasisBoneTrackedRole.RightLowerLeg:
                    root = BasisMocapJoint.RightUpperLeg; joint = BasisMocapJoint.RightLowerLeg; tip = BasisMocapJoint.RightFoot; return true;
                default:
                    root = joint = tip = BasisMocapJoint.Count; return false;
            }
        }
    }
}
#endif
