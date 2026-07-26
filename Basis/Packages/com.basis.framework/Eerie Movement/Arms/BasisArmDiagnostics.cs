using UnityEngine;

namespace Basis.IK
{
    /// <summary>
    /// ⭐ THE ARM'S PUBLISHED DIAGNOSTICS, PLUS THE JUNCTION ANGLES, RECORDED PER ARM PER FRAME.
    ///
    /// BasisArmSolveResult has computed and published HumeralTwistDeg, HumeralTwistGuardDeg, WristTwistDeg,
    /// WristReliefDeg and ForearmRollDeg since the guards landed, and NOT ONE OF THEM WAS EVER READ. There is
    /// no plausibility channel for them, no CSV, no sink. The cost of that is on the record:
    ///
    ///   * three separate investigations aimed at the WRONG JOINT before a test caught it;
    ///   * one agent built three probes and had to invalidate two of its own;
    ///   * a shipped guard's "0.000 mm" claim came from a gate structurally incapable of failing.
    ///
    /// This struct is the sink. It is the ARM's sibling of <see cref="BasisLegDiagnostics"/> -- same shape,
    /// same all-float field convention (so the CSV formatting is uniform and a flag and a measurement cannot
    /// be told apart by accident at the write site), same Header/ToRow-on-the-struct rule so the column list
    /// exists exactly once and the two cannot drift.
    ///
    /// ================================================================================================
    /// ⚠️ EVERY JUNCTION HERE IS READ OFF THE STREAM COMPOSITION, NEVER OFF THE SOLVER'S BOOKKEEPING.
    ///
    /// That is not a stylistic choice, it is the entire reason this instrument can be trusted. The humeral
    /// twist guard updated rootRot and hintR but not cPosition or midRot, so the solver's OWN report of how
    /// far it moved the hand was 0.000 mm at every elbow flexion -- while the stream, which is what the
    /// avatar actually renders, moved the hand up to 205 mm. A recorder that logged r.HandError would have
    /// logged the lie in higher resolution.
    ///
    /// So <see cref="BasisArmDiagnosticsCore.Capture"/> replays BasisFullBodyIK.SolveTwoBoneIKArms's own
    /// composition -- MidDelta, RootDelta, HintDelta, MidPostRoll, TipRotation, in that order, with a
    /// parent's rotation carrying its children rigidly -- and measures the composed result.
    /// <see cref="ComposedHandErrorMm"/> and <see cref="SolverHandErrorMm"/> are BOTH recorded, side by
    /// side, precisely so that class of divergence can never again be invisible: their difference IS the
    /// bookkeeping leak, in millimetres, per frame.
    /// ================================================================================================
    /// </summary>
    public struct BasisArmDiagnostics
    {
        /// <summary>Stamped by the recorder on the main thread, not by the job: Time.time is not available
        /// under Burst, and the driver reads these out immediately after IKJob.Run() anyway.</summary>
        public float FrameIndex;
        public float TimeSeconds;

        /// <summary>-1 left, +1 right. Needed by <see cref="PlaneOfElevationDeg"/>, whose sign convention has
        /// to be mirrored or the two arms report opposite planes for the same gesture.</summary>
        public float Side;

        // ---------------------------------------------------------------- the five that were never recorded

        /// <summary>Signed humeral axial rotation vs the clavicle, BEFORE guarding. The quantity the flat
        /// 120/150 envelope is asserted against, and the one a user capture exists to settle.</summary>
        public float HumeralTwistDeg;
        /// <summary>Signed correction the twist guard applied. Exactly 0 means one of two very different
        /// things -- inside the envelope, or declined -- which is why <see cref="TwistGuardAvailable"/> is
        /// recorded alongside it. Reading a flat zero as "the guard is happy" is how a guard that fired 0
        /// times in 22032 poses shipped.</summary>
        public float HumeralTwistGuardDeg;
        /// <summary>Signed hand-target roll vs the carried animated wrist, about the forearm axis.</summary>
        public float WristTwistDeg;
        /// <summary>Signed swivel actually spent relieving it. 0 = relief not engaged.</summary>
        public float WristReliefDeg;
        /// <summary>The solver's OWN reading of the hand-vs-solved-forearm axial roll (r.WristAxialDeg),
        /// recorded next to <see cref="WristAxialDeg"/>, which is the same quantity read off the stream
        /// replay. They measure the same thing by two different routes, so a gap between them is a stage
        /// that moved a bone without telling the result struct -- the identical cross-check that
        /// <see cref="ComposedHandErrorMm"/> and <see cref="SolverHandErrorMm"/> provide for position, and
        /// the one that would have caught the 205 mm the guard reported as 0.000.</summary>
        public float SolverWristAxialDeg;
        /// <summary>Signed roll the wrist bound took OFF the controller's hand rotation. 0 = the hand was
        /// already inside the envelope, which is NOT the same as the bound being declined -- see
        /// <see cref="TipAvailable"/>.</summary>
        public float WristAxialGuardDeg;
        /// <summary>Signed forearm roll applied via MidPostRoll. 0 = not engaged.</summary>
        public float ForearmRollDeg;
        /// <summary>TRACKER path only: the roll the stage was ASKED for, pre-wrap and pre-saturation. The
        /// envelope the APPLIED roll must satisfy is a function of this, not a constant -- the seam cap
        /// deliberately relaxes the hard bound approaching +/-180 -- so a gate that reads ForearmRollDeg
        /// without this one is gating against the wrong bound.</summary>
        public float ForearmRollDemandDeg;

        // ---------------------------------------------------------------- the junctions

        /// <summary>CLAVICLE -> HUMERUS axial. Zeroed at the rig's own bind and carried through the live
        /// clavicle, so it is convention-FREE and gateable. Declines to 0 when the twist binds are not fed;
        /// <see cref="TwistGuardAvailable"/> says which.</summary>
        public float ShoulderAxialDeg;
        /// <summary>HUMERUS -> FOREARM axial (pronation/supination), against the bind forearm-vs-humerus
        /// relation carried onto the SOLVED humerus. Convention-free zero, so gateable. Declines to 0
        /// without BindLowerArmRotation.</summary>
        public float ElbowAxialDeg;
        /// <summary>FOREARM -> HAND axial: what the forearm did NOT absorb, and therefore what is being
        /// asked of a joint with essentially no independent axial range at all.
        ///
        /// Measured against the SAME neutral the wrist bound uses -- the rig's bind hand-vs-forearm
        /// relation carried onto the solved forearm -- so the diagnostic and the bound cannot disagree
        /// about what "wrist axial" means. That is what makes it gateable rather than a relative signal.
        ///
        /// ⚠️ AND IT IS THE POST-BOUND VALUE, WHICH IS THE POINT. The solver publishes r.WristAxialDeg,
        /// which is the PRE-bound demand; this is what the stream actually renders. The two differ by
        /// exactly <see cref="WristAxialGuardDeg"/>, and asserting that identity is what proves the bound's
        /// reported correction accounts for the whole difference rather than some of it.
        ///
        /// ⚠️ WHEN <see cref="WristBindAvailable"/> IS 0 the rig did not bake a bind hand and the neutral
        /// falls back to the solved forearm itself, i.e. "the bind hand is axially aligned with the bind
        /// forearm". That is the core's own documented fallback, not an invention -- but it means the ZERO
        /// of this column is assumed rather than measured on those rows, so a bind-less rig's absolute
        /// values are a relative signal and must not be gated. Segment on the flag.</summary>
        public float WristAxialDeg;
        /// <summary>THE CHAIN TOTAL: the hand's axial roll measured against the bind-forearm neutral carried
        /// on the solved humerus. Convention-free zero, so this is the one an anatomical envelope can be
        /// asserted against.
        ///
        /// ⚠️ AND IT IS NOT ElbowAxialDeg + WristAxialDeg. Twists about a common axis only add when the
        /// intervening rotations are pure twists, and the forearm sits ~10 deg off the humerus even at a
        /// 170 deg elbow. Measured, the sum form carries 0.46 deg of error there and far more at flexion --
        /// enough to blur exactly the gate this field exists to feed. It is computed compositionally.</summary>
        public float ChainAxialDeg;

        // ---------------------------------------------------------------- kinematics

        /// <summary>Elbow flexion from the COMPOSED positions. 180 = straight.</summary>
        public float ElbowFlexionDeg;
        /// <summary>The solver's own r.ElbowAngleDeg. Recorded next to the composed one for the same reason
        /// the two hand errors are: a divergence between what the solver believes and what the stream does
        /// is a defect class this codebase has shipped, and it is only visible if both are written down.</summary>
        public float SolverElbowAngleDeg;

        /// <summary>Humeral elevation in the TORSO frame: 0 = arm at the side, 90 = horizontal, 180 =
        /// straight overhead. Measured against TorsoUp (chest->neck) and NOT PlayerUp -- the root's up stays
        /// vertical while the chest does not, so a PlayerUp elevation is meaningless the moment the user
        /// bends at the waist, which is the exact wrong-frame class that made a guard fire on ordinary
        /// bent-over poses with its measurement's sign flipped. <see cref="TorsoUpValid"/> records which
        /// frame was actually available.</summary>
        public float ElevationDeg;
        /// <summary>Plane of elevation, clinical convention, mirrored per arm so both report the same number
        /// for the same gesture: 0 = pure abduction (arm out to the side), +90 = forward flexion, -90 =
        /// extension behind the body. This is the second axis of the segmentation -- a glenohumeral arc is
        /// position-dependent in BOTH elevation and plane, and pooling over either hid a band-specific
        /// breach twice this week.</summary>
        public float PlaneOfElevationDeg;
        public float ReachRatio;

        // ---------------------------------------------------------------- conditioning

        /// <summary>|hint projected onto the swing plane|. Small = the pole has no lever arm and a tiny
        /// tracker error swings the elbow a long way.</summary>
        public float HintProjMag;
        /// <summary>|elbow projected onto the swing plane|. Small = elbow near-straight, pole ill-defined.</summary>
        public float ArmProjMag;
        /// <summary>1 = the measured pole's lever arm is healthy, 0 = fully anchored on last frame's.</summary>
        public float PoleConditioning;

        // ---------------------------------------------------------------- error, both ways

        /// <summary>Composed hand vs target, in MILLIMETRES, from the stream replay.</summary>
        public float ComposedHandErrorMm;
        /// <summary>The solver's own r.HandError, in millimetres. Equal to the composed one on a solve whose
        /// bookkeeping is honest; a gap is a stage that moved a bone without telling the result struct.</summary>
        public float SolverHandErrorMm;

        // ---------------------------------------------------------------- which paths ran

        public float HintWeight;
        public float HintIsTracker;
        public float HintApplied;
        public float HasPrevPole;
        public float PoleAnchorValid;
        public float AxisSource;
        /// <summary>-1 / +1 = the side the elbow anatomy guard put the elbow on. 0 = declined OR inside the
        /// envelope -- see <see cref="ElbowGuardFired"/>.</summary>
        public float GuardSideUsed;

        // ---------------------------------------------------------------- COULD each stage have fired

        /// <summary>
        /// ⭐ THE AVAILABILITY BITS ARE THE POINT, NOT DECORATION.
        ///
        /// A stage that is DECLINED and a stage that is INSIDE ITS ENVELOPE both report a flat zero
        /// correction, and telling them apart after the fact is impossible from the correction alone. That
        /// conflation is a named defect class in this codebase: a guard whose soft margin exceeded the
        /// circle it was guarding fired 0 times in 22032 poses, exactly where it was needed, and read as
        /// "quiet". These recompute each stage's OWN entry test from the input, so a session in which a
        /// guard was structurally dead shows up as a column of zeros HERE rather than as silence there.
        /// </summary>
        public float TwistGuardAvailable;
        public float TrackerRollAvailable;
        public float NoTrackerRollAvailable;
        public float TipAvailable;
        /// <summary>1 = the rig baked a bind HAND rotation, so <see cref="WristAxialDeg"/>'s zero is
        /// measured. 0 = it fell back to "bind hand aligned with bind forearm", so that column's zero is
        /// assumed and its absolute values must not be gated on those rows.</summary>
        public float WristBindAvailable;
        /// <summary>0 = the caller could not build a torso frame and the solve fell back to PlayerUp, so
        /// every torso-frame quantity in this row (elevation, plane, and the elbow guard's own ceiling) is
        /// being measured in the wrong frame. Frames with this at 0 must be segmented out, not pooled in.</summary>
        public float TorsoUpValid;

        // ---------------------------------------------------------------- did each stage fire

        public float TwistGuardFired;
        public float ForearmRollFired;
        public float WristReliefFired;
        public float WristAxialGuardFired;
        public float ElbowGuardFired;

        /// <summary>1 when any measured field came out non-finite. A NaN frame is not evidence about any
        /// band and the analysis must drop it, loudly, rather than let it poison a percentile.</summary>
        public float NonFinite;

        public static string Header =>
            "arm,frame,t,side," +
            "humTwist,humTwistGuard,wristTwist,wristRelief,wristAxialSolver,wristAxialGuard,foreRoll,foreRollDemand," +
            "jShoulder,jElbow,jWrist,jChain," +
            "elbowFlex,elbowFlexSolver,elevation,planeOfElev,reach," +
            "hintProj,armProj,poleCond," +
            "handErrComposedMm,handErrSolverMm," +
            "hintWeight,hintIsTracker,hintApplied,hasPrevPole,poleAnchorValid,axisSrc,guardSide," +
            "twistAvail,trkRollAvail,noTrkRollAvail,tipAvail,wristBindAvail,torsoUpValid," +
            "twistFired,foreRollFired,reliefFired,wristGuardFired,elbowGuardFired,nonFinite";

        public string ToRow(string arm) =>
            $"{arm},{FrameIndex:F0},{TimeSeconds:F4},{Side:F0}," +
            $"{HumeralTwistDeg:F3},{HumeralTwistGuardDeg:F3},{WristTwistDeg:F3},{WristReliefDeg:F3},{SolverWristAxialDeg:F3},{WristAxialGuardDeg:F3},{ForearmRollDeg:F3},{ForearmRollDemandDeg:F3}," +
            $"{ShoulderAxialDeg:F3},{ElbowAxialDeg:F3},{WristAxialDeg:F3},{ChainAxialDeg:F3}," +
            $"{ElbowFlexionDeg:F3},{SolverElbowAngleDeg:F3},{ElevationDeg:F3},{PlaneOfElevationDeg:F3},{ReachRatio:F5}," +
            $"{HintProjMag:F5},{ArmProjMag:F5},{PoleConditioning:F4}," +
            $"{ComposedHandErrorMm:F4},{SolverHandErrorMm:F4}," +
            $"{HintWeight:F0},{HintIsTracker:F0},{HintApplied:F0},{HasPrevPole:F0},{PoleAnchorValid:F0},{AxisSource:F0},{GuardSideUsed:F0}," +
            $"{TwistGuardAvailable:F0},{TrackerRollAvailable:F0},{NoTrackerRollAvailable:F0},{TipAvailable:F0},{WristBindAvailable:F0},{TorsoUpValid:F0}," +
            $"{TwistGuardFired:F0},{ForearmRollFired:F0},{WristReliefFired:F0},{WristAxialGuardFired:F0},{ElbowGuardFired:F0},{NonFinite:F0}";

        /// <summary>Column count, derived from <see cref="Header"/> itself so it cannot be stated twice and
        /// drift. A row whose comma count disagrees with this mis-columns the whole CSV silently.</summary>
        public static int ColumnCount
        {
            get
            {
                string h = Header;
                int n = 1;
                for (int k = 0; k < h.Length; k++)
                {
                    if (h[k] == ',') n++;
                }
                return n;
            }
        }
    }

    /// <summary>
    /// Stream-free, allocation-free, Burst-safe capture of one arm's frame. Pure in the strongest available
    /// sense: it takes both of its inputs by `in` and writes only to `out`, so it CANNOT perturb the solve.
    /// See BasisArmDiagnosticsTests.Capture_IsReadOnly_SolveIsBitIdentical, which does not take that on
    /// trust -- it solves the same grid with and without capture and compares every result field bitwise,
    /// and proves its own comparator can see a one-ULP difference before it believes a pass.
    ///
    /// Written the long way -- no Vector3.Angle, no Quaternion.AngleAxis, no Vector3.SignedAngle -- for the
    /// same reason BasisArmSolveCore is: this runs inside a Burst job, and it must read the geometry with
    /// the same closed forms the core does or the diagnostic and the thing it measures disagree at the
    /// singularities, which are the only places anybody is looking.
    /// </summary>
    public static class BasisArmDiagnosticsCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        public const int ElevationBands = 6;    // 30 deg each, 0..180
        public const int ExtensionBands = 5;
        public const int CellCount = ElevationBands * ExtensionBands;

        /// <summary>
        /// ⚠️ NEVER POOLED. Pooling hid a band-specific breach twice this week: a 90 deg humeral bound
        /// looked defensible on pooled corpus data and breached the house 3% firing precedent in TWO
        /// elevation bands once split (4.2% at 60-90, 4.8% at 90-120). Segmenting is not a refinement of
        /// this analysis, it IS the analysis.
        ///
        /// Returns -1 for a non-finite or out-of-range input -- NOT band 0. A NaN frame is not evidence
        /// about the 0-30 deg band and silently filing it there is how a percentile gets poisoned. Written
        /// reject-unless-good so NaN takes the -1 branch rather than being waved through by a comparison it
        /// cannot fail.
        /// </summary>
        public static int ElevationBand(float elevationDeg)
        {
            if (!(elevationDeg >= 0f) || !(elevationDeg <= 180f))
            {
                return -1;
            }
            int band = (int)(elevationDeg / 30f);
            return band >= ElevationBands ? ElevationBands - 1 : band;
        }

        /// <summary>Extension bands, deliberately UNEVEN and bunched at the top. 57% of the corpus sits
        /// above 95% extension and every roll singularity in this solver lives above 0.99, so an even split
        /// would put the entire interesting region in one bucket. Edges: 0.70 / 0.85 / 0.95 / 0.99.</summary>
        public static int ExtensionBand(float reachRatio)
        {
            if (!(reachRatio >= 0f) || !(reachRatio < 1e6f))
            {
                return -1;
            }
            if (reachRatio < 0.70f) return 0;
            if (reachRatio < 0.85f) return 1;
            if (reachRatio < 0.95f) return 2;
            if (reachRatio < 0.99f) return 3;
            return 4;
        }

        /// <summary>Flat cell index, or -1 when either band declined.</summary>
        public static int Cell(int elevationBand, int extensionBand)
        {
            if (elevationBand < 0 || extensionBand < 0)
            {
                return -1;
            }
            return elevationBand * ExtensionBands + extensionBand;
        }

        public static int CellOf(in BasisArmDiagnostics d) =>
            Cell(ElevationBand(d.ElevationDeg), ExtensionBand(d.ReachRatio));

        /// <summary>
        /// ⭐ THE RUNTIME'S OWN COMPOSITION, REPLAYED. BasisFullBodyIK.SolveTwoBoneIKArms ends with
        ///
        ///   mid.SetRotation (MidDelta * mid);   root.SetRotation(RootDelta * root);
        ///   root.SetRotation(HintDelta * root); mid.SetRotation(MidPostRoll * mid); tip.SetRotation(Tip)
        ///
        /// and setting a PARENT's rotation in an AnimationStream carries its children RIGIDLY -- the child's
        /// world rotation is left-multiplied and its position pivots about the parent's origin. HintDelta
        /// goes on the ROOT, which is why the humeral guard folded into it could throw the hand.
        ///
        /// This is the difference between what the solver BOOKKEEPS and what the arm actually does, and
        /// every junction below is read off THIS and never off the result struct.
        /// </summary>
        public static void Compose(in BasisArmSolveInput i, in BasisArmSolveResult r,
            out Vector3 elbow, out Vector3 hand, out Quaternion rootRot, out Quaternion midRot)
        {
            Vector3 a = i.Shoulder;
            Vector3 b = i.Elbow;
            Vector3 c = i.Hand;

            c = b + r.MidDelta * (c - b);
            Quaternion mid = r.MidDelta * i.MidRotation;

            b = a + r.RootDelta * (b - a);
            c = a + r.RootDelta * (c - a);
            Quaternion root = r.RootDelta * i.RootRotation;
            mid = r.RootDelta * mid;

            b = a + r.HintDelta * (b - a);
            c = a + r.HintDelta * (c - a);
            root = r.HintDelta * root;
            mid = r.HintDelta * mid;

            c = b + r.MidPostRoll * (c - b);
            mid = r.MidPostRoll * mid;

            elbow = b;
            hand = c;
            rootRot = root;
            midRot = mid;
        }

        /// <summary>
        /// Capture one arm's frame. <paramref name="side"/> is -1 for the left arm and +1 for the right; it
        /// only affects <see cref="BasisArmDiagnostics.PlaneOfElevationDeg"/>'s sign, which has to be
        /// mirrored or the two arms report opposite planes for the same gesture and the segmentation
        /// silently averages them out.
        /// </summary>
        public static void Capture(in BasisArmSolveInput i, in BasisArmSolveResult r, float side,
            out BasisArmDiagnostics d)
        {
            d = default;
            d.Side = side < 0f ? -1f : 1f;

            d.HumeralTwistDeg = r.HumeralTwistDeg;
            d.HumeralTwistGuardDeg = r.HumeralTwistGuardDeg;
            d.WristTwistDeg = r.WristTwistDeg;
            d.WristReliefDeg = r.WristReliefDeg;
            d.SolverWristAxialDeg = r.WristAxialDeg;
            d.WristAxialGuardDeg = r.WristAxialGuardDeg;
            d.ForearmRollDeg = r.ForearmRollDeg;
            d.ForearmRollDemandDeg = r.ForearmRollDemandDeg;

            d.ReachRatio = r.ReachRatio;
            d.SolverElbowAngleDeg = r.ElbowAngleDeg;
            d.SolverHandErrorMm = r.HandError * 1000f;
            d.HintProjMag = r.HintProjMag;
            d.ArmProjMag = r.ArmProjMag;
            d.PoleConditioning = r.PoleConditioning;
            d.AxisSource = r.AxisSource;
            d.GuardSideUsed = r.GuardSideUsed;

            d.HintWeight = i.HintWeight ? 1f : 0f;
            d.HintIsTracker = i.HintIsTracker ? 1f : 0f;
            d.HintApplied = r.HintApplied ? 1f : 0f;
            d.HasPrevPole = i.HasPrevPole ? 1f : 0f;
            d.PoleAnchorValid = r.PoleAnchorValid ? 1f : 0f;

            // Each stage's OWN entry test, recomputed from the input. These are the class-2 detectors: a
            // guard that CANNOT fire reads identically to a guard that is happy, unless this is recorded.
            float bindClavSqr = Dot4(i.BindClavicleRotation);
            float clavSqr = Dot4(i.ClavicleRotation);
            float bindHumSqr = Dot4(i.BindHumerusRotation);
            float bindLowerSqr = Dot4(i.BindLowerArmRotation);
            float tipRotSqr = Dot4(i.TipRotation);
            float hintRotSqr = Dot4(i.HintRotation);
            float bindHandSqr = Dot4(i.BindHandRotation);

            bool twistAvail = bindClavSqr > 0.5f && clavSqr > 0.5f && bindHumSqr > 0.5f
                              && i.BindHumerusDir.sqrMagnitude > k_SqrEpsilon
                              && i.BindHumerusRefAxis.sqrMagnitude > k_SqrEpsilon;
            d.TwistGuardAvailable = twistAvail ? 1f : 0f;
            d.TrackerRollAvailable = (i.HintIsTracker && hintRotSqr > 0.5f) ? 1f : 0f;
            d.NoTrackerRollAvailable = (!i.HintIsTracker && bindLowerSqr > 0.5f && bindHumSqr > 0.5f) ? 1f : 0f;
            d.TipAvailable = tipRotSqr > 0.5f ? 1f : 0f;
            d.WristBindAvailable = (bindLowerSqr > 0.5f && bindHandSqr > 0.5f) ? 1f : 0f;

            bool torsoValid = i.TorsoUp.sqrMagnitude > k_SqrEpsilon;
            d.TorsoUpValid = torsoValid ? 1f : 0f;

            d.TwistGuardFired = r.HumeralTwistGuardDeg != 0f ? 1f : 0f;
            d.ForearmRollFired = r.ForearmRollDeg != 0f ? 1f : 0f;
            d.WristReliefFired = r.WristReliefDeg != 0f ? 1f : 0f;
            d.WristAxialGuardFired = r.WristAxialGuardDeg != 0f ? 1f : 0f;
            d.ElbowGuardFired = r.GuardSideUsed != 0 ? 1f : 0f;

            Compose(i, r, out Vector3 elbow, out Vector3 hand, out Quaternion rootRot, out Quaternion midRot);

            d.ComposedHandErrorMm = (hand - i.TargetPosition).magnitude * 1000f;
            d.ElbowFlexionDeg = AngleDeg(i.Shoulder - elbow, hand - elbow);

            Vector3 humAxis = elbow - i.Shoulder;
            Vector3 foreAxis = hand - elbow;
            bool humOk = humAxis.sqrMagnitude > k_SqrEpsilon;
            bool foreOk = foreAxis.sqrMagnitude > k_SqrEpsilon;
            if (humOk) humAxis = humAxis.normalized;
            if (foreOk) foreAxis = foreAxis.normalized;

            // ---- CLAVICLE -> HUMERUS. The same construction the core's own guard uses, so the diagnostic
            // and the guard cannot disagree about what "humeral twist" means: carry the bind humerus through
            // the LIVE clavicle, take the minimal swing onto the live humeral direction, and read the
            // residual roll of the rig's baked perpendicular reference about that direction. A hardcoded
            // world reference axis would be parallel to the bone on some rigs and silently read zero.
            if (twistAvail && humOk)
            {
                Quaternion carry = i.ClavicleRotation * Quaternion.Inverse(i.BindClavicleRotation);
                Vector3 restDir = (carry * i.BindHumerusDir).normalized;
                if (restDir.sqrMagnitude > 0.5f)
                {
                    Quaternion swingMin = BasisQuaternionExt.FromToRotation(restDir, humAxis);
                    Vector3 refPerp = (swingMin * (carry * i.BindHumerusRotation)) * i.BindHumerusRefAxis;
                    Vector3 livePerp = rootRot * i.BindHumerusRefAxis;
                    refPerp -= humAxis * Vector3.Dot(refPerp, humAxis);
                    livePerp -= humAxis * Vector3.Dot(livePerp, humAxis);
                    d.ShoulderAxialDeg = SignedAngleDeg(refPerp, livePerp, humAxis);
                }
            }

            // ---- HUMERUS -> FOREARM, and the CHAIN TOTAL. Both hang off the same neutral: the forearm at
            // ZERO pronation, i.e. the rig's bind forearm-vs-humerus relation carried onto the SOLVED
            // humerus. That neutral is a property of the RIG and not of an animation clip or a fitted model,
            // which is what makes these two convention-free at their zero and therefore gateable.
            if (bindLowerSqr > 0.5f && bindHumSqr > 0.5f && foreOk)
            {
                Quaternion neutralFore = rootRot * (Quaternion.Inverse(i.BindHumerusRotation) * i.BindLowerArmRotation);
                d.ElbowAxialDeg = TwistDeg(midRot * Quaternion.Inverse(neutralFore), foreAxis);

                // ⚠️ NOT ElbowAxialDeg + WristAxialDeg. See the field's note: twists about a common axis
                // only add through PURE twists, and the forearm sits off the humerus at every real flexion.
                if (tipRotSqr > 0.5f)
                {
                    d.ChainAxialDeg = TwistDeg(r.TipRotation * Quaternion.Inverse(neutralFore), foreAxis);
                }
            }

            // ---- FOREARM -> HAND, against the SAME neutral the wrist bound uses. Mirrors the core exactly,
            // including its fallback: with no baked bind hand the neutral IS the solved forearm, which is
            // the statement "the rig's bind hand is axially aligned with its bind forearm". WristBindAvailable
            // records which of the two was in force, because only the first has a measured zero.
            if (foreOk && bindLowerSqr > 0.5f)
            {
                Quaternion neutralHand = bindHandSqr > 0.5f
                    ? midRot * (Quaternion.Inverse(i.BindLowerArmRotation) * i.BindHandRotation)
                    : midRot;
                d.WristAxialDeg = TwistDeg(r.TipRotation * Quaternion.Inverse(neutralHand), foreAxis);
            }

            // ---- ELEVATION AND PLANE, in the TORSO frame. PlayerUp is the documented fallback the core
            // itself takes, so taking it here too keeps the diagnostic aligned with the solve -- but
            // TorsoUpValid records which one was used, because a row measured against the root's up on a
            // bent-over user is not evidence about elevation and must be segmented out rather than pooled.
            Vector3 up = torsoValid ? i.TorsoUp : i.PlayerUp;
            if (up.sqrMagnitude > k_SqrEpsilon && humOk)
            {
                up = up.normalized;
                d.ElevationDeg = AngleDeg(-up, humAxis);

                Vector3 lateral = i.ElbowLateralOut;
                Vector3 lateralProj = lateral - up * Vector3.Dot(lateral, up);
                Vector3 humProj = humAxis - up * Vector3.Dot(humAxis, up);
                if (lateralProj.sqrMagnitude > k_SqrEpsilon && humProj.sqrMagnitude > k_SqrEpsilon)
                {
                    // Mirrored by side so both arms report the same number for the same gesture: flexion
                    // positive, extension negative, 0 = pure abduction out to the side.
                    d.PlaneOfElevationDeg = -d.Side * SignedAngleDeg(lateralProj, humProj, up);
                }
            }

            d.NonFinite = AllFinite(in d) ? 0f : 1f;
        }

        static float Dot4(Quaternion q) => q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;

        /// <summary>
        /// ⚠️ NOT `2 * acos(|dot|)`, AND THE DIFFERENCE IS THE VALIDITY OF EVERY NUMBER IN THIS FILE.
        ///
        /// acos loses half the float mantissa near dot = 1: an error of eps in the dot becomes sqrt(2*eps)
        /// in the angle, so at float32 precision two BIT-IDENTICAL rotations measure up to ~0.09 deg apart.
        /// The composition above chains five quaternion products and Unity does not renormalise on multiply,
        /// so the drift is real and always present. atan2 of the vector part against the scalar part is
        /// well-conditioned at zero and costs nothing.
        ///
        /// NaN-safe by shape: `!(x > eps)` takes the reject branch on NaN, where `x < eps` would wave it
        /// through -- NaN fails every ordered comparison, so a guard must read "reject unless good".
        /// </summary>
        public static float TwistDeg(Quaternion q, Vector3 axis)
        {
            float s = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float c = q.w;
            if (c < 0f) { s = -s; c = -c; }
            if (!(s * s + c * c > k_SqrEpsilon))
            {
                return 0f;
            }
            return 2f * Mathf.Atan2(s, c) * Mathf.Rad2Deg;
        }

        /// <summary>Angle between two rotations, in the stable atan2 form. Public so a test can show the
        /// acos form's 0.09 deg noise floor against it rather than assert the claim.</summary>
        public static float PoseChangeDeg(Quaternion from, Quaternion to)
        {
            Quaternion a = NormalizeQ(from);
            Quaternion b = NormalizeQ(to);
            Quaternion rel = a * Quaternion.Inverse(b);
            float s = Mathf.Sqrt(rel.x * rel.x + rel.y * rel.y + rel.z * rel.z);
            float c = rel.w < 0f ? -rel.w : rel.w;
            return 2f * Mathf.Atan2(s, c) * Mathf.Rad2Deg;
        }

        public static Quaternion NormalizeQ(Quaternion q)
        {
            float m2 = Dot4(q);
            if (!(m2 > 1e-12f)) return Quaternion.identity;
            float inv = 1f / Mathf.Sqrt(m2);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        public static float SignedAngleDeg(Vector3 from, Vector3 to, Vector3 axis)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (!(denom > k_Epsilon))
            {
                return 0f;
            }
            float c = Vector3.Dot(from, to) / denom;
            c = c > 1f ? 1f : (c > -1f ? c : -1f);   // Mathf.Clamp does NOT clamp NaN; this shape sends it to -1
            float angle = Mathf.Acos(c) * Mathf.Rad2Deg;
            return Vector3.Dot(axis, Vector3.Cross(from, to)) < 0f ? -angle : angle;
        }

        public static float AngleDeg(Vector3 from, Vector3 to)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (!(denom > k_Epsilon))
            {
                return 0f;
            }
            float c = Vector3.Dot(from, to) / denom;
            c = c > 1f ? 1f : (c > -1f ? c : -1f);
            return Mathf.Acos(c) * Mathf.Rad2Deg;
        }

        static bool F(float v) => !(float.IsNaN(v) || float.IsInfinity(v));

        public static bool AllFinite(in BasisArmDiagnostics d) =>
            F(d.HumeralTwistDeg) && F(d.HumeralTwistGuardDeg) && F(d.WristTwistDeg) && F(d.WristReliefDeg)
            && F(d.SolverWristAxialDeg) && F(d.WristAxialGuardDeg)
            && F(d.ForearmRollDeg) && F(d.ForearmRollDemandDeg)
            && F(d.ShoulderAxialDeg) && F(d.ElbowAxialDeg) && F(d.WristAxialDeg) && F(d.ChainAxialDeg)
            && F(d.ElbowFlexionDeg) && F(d.SolverElbowAngleDeg) && F(d.ElevationDeg) && F(d.PlaneOfElevationDeg)
            && F(d.ReachRatio) && F(d.HintProjMag) && F(d.ArmProjMag) && F(d.PoleConditioning)
            && F(d.ComposedHandErrorMm) && F(d.SolverHandErrorMm);
    }
}
