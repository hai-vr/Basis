namespace Basis.IK.Debugging
{
    // Pass/fail quality gates over the IK sweep summaries -- the assertion layer that turns the
    // sweeps from "did it run" into "did the IK math pass". Thresholds are set above acceptable and
    // below broken; calibrate them against a known-good baseline run, then a regression trips a gate.
    // Each gate returns (pass, reason) where reason names the failing metric and its value.
    public static class BasisIKTestGates
    {
        // --- tunable thresholds (calibrate against a known-good run) ---
        public const float ArmMaxTrackerSensDegPerCm = 90f; // elbow swivel per cm hand jitter -> oscillation
        public const float ArmMaxMeanAlignErrDeg = 12f;     // mean angle solved-elbow vs tracker pole (follow)
        public const float ArmMaxElbowMeanAlignErrDeg = 8f; // mean angle solved-elbow vs the LOOKUP (no-tracker) pole; drift off the natural down/back pole
        public const int ArmMaxElbowUpFlips = 0;            // forward, non-overhead, EXTENDED (reach>0.55) reaches whose elbow flips hard UP (|swivel|>120). Zero-tolerance: the fixed lookup is clean (0) at every density; folded near-body reaches are excluded (elbow-up is natural there). Pre-fix: 4-7.
        public const float ElbowMinClearedFraction = 0.55f; // protect must clear most of the clearable set (ceiling ~0.64)
        public const float ElbowMaxMeanResidualPenMm = 25f; // mean leftover torso penetration after the push
        public const float ElbowMaxSensDegPerCm = 90f;      // final elbow swivel per cm hand jitter
        public const float ShoulderMaxAngleDeg = 60f;
        public const float HeadMaxNeckDeg = 90f;
        public const float TrajMaxPopDeg = 45f;             // swivel jump on smooth motion (discontinuity)
        public const float TrajMaxRoughDeg = 15f;           // swivel roughness under tracking noise (jitter)
        public const float TemporalMaxRoughDeg = 3f;        // clean 2nd-diff on smooth motion = stepping/jitter as the hand glides (per-frame feedback)
        public const float LegInvertHintSafeConeDeg = 50f;  // hint within this of nominal must never bend the knee backward. Measured onset is ~54 deg (lift pose + posterior down-hint, ~30-35 deg off the leg axis), so 50 is the data-driven safe envelope with margin; beyond it a grossly-wrong pole can still invert (solver forward-clamp would be needed to push it further).
        // --- tracker placement / discovery gates (calibrate against a known-good run) ---
        public const float TrackerPlacementCleanMinFraction = 0.85f; // overall clean (no-jitter) correctness floor across ALL archetypes incl. extreme; below = broken
        public const float TrackerPlacementCoreMinFraction = 1.0f;   // common "core" archetypes must classify every tracker cleanly
        // --- multi-tracker ("double hip") rotation fusion ---
        public const float MultiTrackerMaxErrDeg = 2f;              // fused hip must track body rotation within this of a single tracker
        public const float MultiTrackerProjectionRigidTolDeg = 1f;  // the projection math must be exact for a RIGID pair (regression guard)
        public const float MultiTrackerTemporalMaxErrDeg = 3f;      // fused rotation must track the body within this during motion (a single tracker is ~0)
        public const float MultiTrackerTemporalMaxSnapDeg = 2f;     // frame-to-frame step jump above this = a visible rotation snap
        // --- foot placement / procedural stepping gates (calibrate against a known-good run) ---
        public const float FootMaxPlantedSlideMm = 15f;   // a planted foot is world-locked; horizontal move >this/tick = skating
        public const float FootMaxExtensionRatio = 1.18f;  // hips->foot / standing reach; above = leg can't reach (foot left behind, visible stretch)
        public const float FootMaxPenetrationMm = 30f;     // planted foot driven below the floor
        public const float FootMaxPlantedHoverMm = 60f;    // planted foot floating above the floor
        public const float FootTiltSlackDeg = 2f;          // headroom over the configured tilt clamp -- the slope clamp must hold
        public const float FootYawSlackDeg = 6f;           // headroom over the configured yaw clamp (sampled late in a step, where the clamp is live)
        public const float FootMaxKneeBehindM = 0.02f;     // knee hint may sit at most this far behind the hip->foot line before the knee would invert

        public static (bool pass, string reason) GateArm(in BasisArmIKSweepSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Rows <= 0) return (false, "no rows");
            if (s.ReachablePoints <= 0) return (false, "no reachable points");
            if (s.TrackerMeanAlignErrDeg > ArmMaxMeanAlignErrDeg)
                return (false, $"tracker meanAlignErr {s.TrackerMeanAlignErrDeg:F1} > {ArmMaxMeanAlignErrDeg} deg (tracker not followed)");
            // Gate on the 99.9th-percentile tracker sens, not the raw max: even after excluding the
            // pole-collapse singularity, the max chases the worst well-conditioned boundary pose, which
            // sharpens (not spreads) with grid density and so climbs forever. The percentile catches
            // WIDESPREAD tracker jitter and is density-stable (mirrors the protect sens gate).
            if (s.TrackerSens99DegPerCm > ArmMaxTrackerSensDegPerCm)
                return (false, $"tracker sens p99.9 {s.TrackerSens99DegPerCm:F0} > {ArmMaxTrackerSensDegPerCm} deg/cm (widespread jitter; max {s.TrackerMaxSensDegPerCm:F0} at boundary outliers)");
            return (true, $"reach={s.ReachablePoints} alignErr={s.TrackerMeanAlignErrDeg:F1} sensP99.9={s.TrackerSens99DegPerCm:F0} (max {s.TrackerMaxSensDegPerCm:F0})");
        }

        // Elbow DIRECTION (not the torso-collision "elbow protect"): the no-tracker lookup pole must keep
        // the elbow tucked behind/below the hand, and the solve must actually land it there. A flip = the
        // elbow ends up in front of / on the wrong side from where the lookup asked -- the artifact where
        // "the elbow is out in front instead of naturally behind the hand". Driven by the same arm sweep.
        public static (bool pass, string reason) GateArmElbowDirection(in BasisArmIKSweepSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.ReachablePoints <= 0) return (false, "no reachable points");
            if (s.LookupElbowUpCount > 0)
                return (false, $"{s.LookupElbowUpCount} reachable poses point the elbow UP (chicken-wing)");
            if (s.LookupElbowFlipCount > ArmMaxElbowUpFlips)
                return (false, $"{s.LookupElbowFlipCount} extended forward reaches flip the elbow hard UP (|swivel|>120) instead of behind/below -- elbow in front / wrong side (align mean {s.LookupMeanAlignErrDeg:F1}, max {s.LookupMaxAlignErrDeg:F0})");
            if (s.LookupMeanAlignErrDeg > ArmMaxElbowMeanAlignErrDeg)
                return (false, $"mean elbow-direction error {s.LookupMeanAlignErrDeg:F1} > {ArmMaxElbowMeanAlignErrDeg} deg (elbow drifting off the natural pole)");
            // Anatomical flexion: the solved elbow angle must never leave the human range (no over-flex / hyperextension).
            float minFlex = UnityEngine.Animations.Rigging.BasisArmSolveCore.MinElbowAngleDeg;
            float maxFlex = UnityEngine.Animations.Rigging.BasisArmSolveCore.MaxElbowAngleDeg;
            if (s.LookupMinElbowAngleDeg < minFlex - 1f || s.LookupMaxElbowAngleDeg > maxFlex + 1f)
                return (false, $"elbow flexion {s.LookupMinElbowAngleDeg:F0}..{s.LookupMaxElbowAngleDeg:F0} deg leaves the human range [{minFlex:F0},{maxFlex:F0}] (over-flex / hyperextension)");
            return (true, $"extUpFlips={s.LookupElbowFlipCount} elbowUp={s.LookupElbowUpCount} alignMean={s.LookupMeanAlignErrDeg:F1} flex={s.LookupMinElbowAngleDeg:F0}..{s.LookupMaxElbowAngleDeg:F0}");
        }

        public static (bool pass, string reason) GateElbow(in BasisElbowProtectSweepSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Rows <= 0) return (false, "no rows");
            if (s.EngagedPoints <= 0) return (false, "protect never engaged (sweep not exercising it)");
            float clearedFrac = (float)s.ClearedPoints / s.EngagedPoints;
            if (clearedFrac < ElbowMinClearedFraction)
                return (false, $"cleared {clearedFrac:P0} of engaged < {ElbowMinClearedFraction:P0} (protect not clearing torso)");
            if (s.MeanResidualPenMm > ElbowMaxMeanResidualPenMm)
                return (false, $"mean residual {s.MeanResidualPenMm:F0}mm > {ElbowMaxMeanResidualPenMm}mm (elbow left buried)");
            // Gate on the 99.9th-percentile sens, not the raw max: the max chases a handful of protect
            // engage/disengage boundary discontinuities that sharpen (not spread) with grid density, so it
            // climbs forever as you densify. The percentile catches WIDESPREAD oscillation and is stable.
            if (s.Sens99DegPerCm > ElbowMaxSensDegPerCm)
                return (false, $"sens p99.9 {s.Sens99DegPerCm:F0} > {ElbowMaxSensDegPerCm} deg/cm (widespread oscillation; max {s.MaxSensDegPerCm:F0} at boundary outliers)");
            return (true, $"cleared={clearedFrac:P0} resid={s.MeanResidualPenMm:F0}mm sensP99.9={s.Sens99DegPerCm:F0} (max {s.MaxSensDegPerCm:F0})");
        }

        public static (bool pass, string reason) GateShoulder(in BasisShoulderSweepSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Rows <= 0) return (false, "no rows");
            if (s.Engaged <= 0) return (false, "shoulder never engaged");
            if (s.MaxShoulderAngleDeg > ShoulderMaxAngleDeg)
                return (false, $"max shoulder {s.MaxShoulderAngleDeg:F0} > {ShoulderMaxAngleDeg} deg");
            return (true, $"engaged={s.Engaged} maxAngle={s.MaxShoulderAngleDeg:F0}");
        }

        public static (bool pass, string reason) GateLeg(in BasisLegIKSweepSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Rows <= 0) return (false, "no rows");
            if (s.ReachablePoints <= 0) return (false, "no reachable points");
            if (float.IsNaN(s.MaxSwivelShiftDeg) || s.MaxSwivelShiftDeg > 180f)
                return (false, $"knee swivel shift {s.MaxSwivelShiftDeg:F0} out of range");
            return (true, $"reach={s.ReachablePoints} maxSwivelShift={s.MaxSwivelShiftDeg:F0}");
        }

        // Inhuman knee detection: the knee must never bend backward (posterior to the hip->ankle line).
        // The gate lives on WELL-CONDITIONED hints -- a reasonable hint (within the safe cone) and any
        // reachable target under a good hint must stay human; a backward knee there means a tracker can
        // invert the pole and break the pose. Pole-on-limb singularities are excluded (and reported), the
        // way the trajectory gates exclude their kinematic singularities -- the solver blends those forward.
        public static (bool pass, string reason) GateLegInversion(in BasisLegInversionSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.HintSamples <= 0) return (false, "no hint samples (sweep not exercising the knee)");
            if (s.SafeConeInversions > 0)
                return (false, $"{s.SafeConeInversions}/{s.SafeConeSamples} well-conditioned knee inversions within hint cone (onset {s.OnsetDeviationDeg:F0} deg) -- pole flips the knee backward");
            if (s.TargetInversions > 0)
                return (false, $"{s.TargetInversions}/{s.TargetReachable} reachable targets bend the knee backward with a good hint (inhuman pose)");
            // Max-flexion limit: a human knee can't fold past the solver's MinKneeInteriorDeg clamp (calf
            // through thigh). The flexion pass pulls the foot to the hip; the solved interior must hold there.
            float flexLimit = UnityEngine.Animations.Rigging.BasisLegSolveCore.MinKneeInteriorDeg;
            if (s.FlexClampSamples > 0 && s.MinKneeFlexDeg < flexLimit - 3f)
                return (false, $"knee over-folds to {s.MinKneeFlexDeg:F0} deg interior < {flexLimit:F0} deg limit (calf through thigh -- clamp not holding)");
            string onset = float.IsNaN(s.OnsetDeviationDeg) ? "none" : $"{s.OnsetDeviationDeg:F0}deg";
            return (true, $"safe cone clean, onset={onset} natural={s.TargetInversions}/{s.TargetReachable} singular={s.SingularInversions}/{s.SingularSamples} minFlex={s.MinKneeFlexDeg:F0}/{flexLimit:F0}deg");
        }

        // Temporal knee inversion: on smooth foot motion with a good hint the knee must never cross to the
        // backward (posterior) side mid-motion. Pole-noise crossings are reported, not gated (enough jitter
        // always breaks it) -- they show how much tracker shake the knee tolerates before it transiently flips.
        public static (bool pass, string reason) GateLegInversionTemporal(in BasisLegInversionTemporalSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Steps <= 0) return (false, "no steps");
            if (s.Crossings > 0)
                return (false, $"knee flips backward {s.Crossings}x mid-motion on a good hint (min fwd {s.MinFwdFrac:F2}) -- transient inversion");
            return (true, $"clean (min fwd {s.MinFwdFrac:F2}); under pole noise: {s.NoisyCrossings} flips (min fwd {s.NoisyMinFwdFrac:F2})");
        }

        public static (bool pass, string reason) GateHead(in BasisHeadSweepSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Rows <= 0) return (false, "no rows");
            if (float.IsNaN(s.MaxNeckDeg) || s.MaxNeckDeg > HeadMaxNeckDeg)
                return (false, $"max neck {s.MaxNeckDeg:F0} > {HeadMaxNeckDeg} deg");
            if (float.IsNaN(s.ExtremeOnsetPitch)) return (false, "extreme region never engaged");
            return (true, $"maxNeck={s.MaxNeckDeg:F0} extremeOnset={s.ExtremeOnsetPitch:F0}");
        }

        // Trajectory gates take fields (not the struct) so the same gate serves arm/elbow/head trajectory
        // summaries. Pops = swivel jumps on smooth hand motion; rough = swivel jitter under tracking noise.
        public static (bool pass, string reason) GateTrajectory(bool ok, string error, float worstPopDeg, float worstRoughDeg)
        {
            if (!ok) return (false, string.IsNullOrEmpty(error) ? "did not run" : error);
            if (worstPopDeg > TrajMaxPopDeg)
                return (false, $"pop {worstPopDeg:F0} > {TrajMaxPopDeg} deg (snap on smooth motion)");
            if (worstRoughDeg > TrajMaxRoughDeg)
                return (false, $"rough {worstRoughDeg:F1} > {TrajMaxRoughDeg} deg (jitter under noise)");
            return (true, $"pop={worstPopDeg:F0} rough={worstRoughDeg:F1}");
        }

        // Per-frame feedback drive: worstRoughDeg is the clean 2nd-difference (stepping as the hand glides),
        // the live-only jitter the stateless scan can't see.
        public static (bool pass, string reason) GateTemporal(bool ok, string error, float worstPopDeg, float worstRoughDeg)
        {
            if (!ok) return (false, string.IsNullOrEmpty(error) ? "did not run" : error);
            if (worstPopDeg > TrajMaxPopDeg)
                return (false, $"pop {worstPopDeg:F0} > {TrajMaxPopDeg} deg");
            if (worstRoughDeg > TemporalMaxRoughDeg)
                return (false, $"glide-jitter {worstRoughDeg:F2} > {TemporalMaxRoughDeg} deg/step (elbow steps as the hand glides)");
            return (true, $"pop={worstPopDeg:F0} glideJitter={worstRoughDeg:F2}");
        }

        // Tracker placement DISCOVERY: synthetic constellations over many body archetypes must map
        // each tracker to the role it was placed for. Hard invariants (a stale/origin tracker never
        // binds; no CLEAN tracker crosses to the opposite body side) gate unconditionally; jittered
        // cross-side is reported, not gated (heavy noise at a narrow stance is inherently ambiguous). Correctness
        // is gated strictly on the common "core" archetypes and at a tunable floor overall; extreme
        // archetypes are reported (CSV + confusions) rather than failing the gate.
        public static (bool pass, string reason) GateTrackerPlacement(in BasisTrackerPlacementSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Trackers <= 0) return (false, "no trackers");
            if (s.NearOriginLeaks > 0)
                return (false, $"{s.NearOriginLeaks} stale/origin tracker(s) bound to a role (must never happen)");
            if (s.CleanCrossSideLeaks > 0)
                return (false, $"{s.CleanCrossSideLeaks} clean tracker(s) bound to the opposite body side ({s.TopConfusions})");
            if (s.CoreCleanFraction < TrackerPlacementCoreMinFraction)
                return (false, $"core archetypes clean {s.CoreCleanFraction:P0} < {TrackerPlacementCoreMinFraction:P0} ({s.CoreCleanCorrect}/{s.CoreCleanTrackers}; {s.TopConfusions})");
            if (s.CleanCorrectFraction < TrackerPlacementCleanMinFraction)
                return (false, $"clean correctness {s.CleanCorrectFraction:P0} < {TrackerPlacementCleanMinFraction:P0} ({s.CleanCorrect}/{s.CleanTrackers}; {s.TopConfusions})");
            return (true, $"clean {s.CleanCorrectFraction:P0} (core {s.CoreCleanFraction:P0}) overall {s.Correct}/{s.Trackers} misassign={s.Misassigned} unassigned={s.Unassigned} crossSide(jittered)={s.CrossSideLeaks} heightBias={s.HeightBiasPts:F1}pts(short {s.ShortAccuracy:F0}%/tall {s.TallAccuracy:F0}%) minMargin={s.MinCorrectMargin:F1}");
        }

        // Multi-tracker ("double hip") rotation fusion: the fused virtual hip must track the body's rotation
        // since calibration the same as a single tracker would (the calibration offset cancels, so a correct
        // fusion reads ~0). Two layers: a regression guard that the projection math is exact for a RIGID pair,
        // then the shipping behavior under a device-churn re-prime must stay within tolerance -- if it doesn't,
        // the gate names the robust convention the sweep found.
        public static (bool pass, string reason) GateMultiTrackerRotation(in BasisMultiTrackerRotationSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Rows <= 0) return (false, "no rows");
            int stable = s.StableConvIndex, cur = s.CurrentConvIndex, best = s.BestConvIndex;
            if (s.RigidWorstErrDeg[stable] > MultiTrackerProjectionRigidTolDeg)
                return (false, $"projected fusion off by {s.RigidWorstErrDeg[stable]:F1} deg for a RIGID pair (t-spread {s.TSpreadDeg[stable]:F1}) -- the projection math is wrong, not just the prime");
            // Gate on SYSTEMATIC error (the persistent hip offset), not transient flex passthrough -- the
            // latter is bounded by strap slop for every convention and isn't the bug being chased.
            float curSys = s.RigidWorstErrDeg[cur];
            float bestSys = s.RigidWorstErrDeg[best];
            string onset = float.IsNaN(s.OnsetYawDeg[cur]) ? "never" : $"{s.OnsetYawDeg[cur]:F0}deg yaw";
            if (curSys > MultiTrackerMaxErrDeg)
                return (false, $"re-prime convention '{s.ConvNames[cur]}' carries {curSys:F1} deg SYSTEMATIC hip-rotation error > {MultiTrackerMaxErrDeg} (onset {onset}); robust convention '{s.ConvNames[best]}' is {bestSys:F1} deg (flex passthrough: cur {s.FlexWorstErrDeg[cur]:F1}, best {s.FlexWorstErrDeg[best]:F1})");
            return (true, $"'{s.ConvNames[cur]}' systematic {curSys:F1} deg; best '{s.ConvNames[best]}' {bestSys:F1} deg (flex passthrough cur {s.FlexWorstErrDeg[cur]:F1})");
        }

        // Multi-tracker DYNAMIC behavior: synthetic body-motion trajectories driven frame-by-frame through the
        // real stateful fusion. A single tracker tracks the body ~1:1 with no extra lag or snap; the fused pair
        // must too. Failure here is the live "funky snap / lag" -- the pairing rotation low-pass and confidence
        // blend deviating from rigid tracking during motion -- which the static convention sweep can't see.
        public static (bool pass, string reason) GateMultiTrackerRotationTemporal(in BasisMultiTrackerRotationTemporalSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Frames <= 0) return (false, "no frames");
            if (s.WorstTrackErrDeg > MultiTrackerTemporalMaxErrDeg)
                return (false, $"fused rotation trails the body by up to {s.WorstTrackErrDeg:F1} deg in motion > {MultiTrackerTemporalMaxErrDeg} (a single tracker is ~0) -- the pairing low-pass/blend lags; lower PairingRotationHalfLife or drop the extra low-pass");
            if (s.WorstSnapDeg > MultiTrackerTemporalMaxSnapDeg)
                return (false, $"frame-to-frame step jumps up to {s.WorstSnapDeg:F1} deg > {MultiTrackerTemporalMaxSnapDeg} (a visible rotation snap; overshoot up to {s.WorstOvershootDeg:F1} deg)");
            return (true, $"tracks within {s.WorstTrackErrDeg:F1} deg, snap {s.WorstSnapDeg:F1} deg, overshoot {s.WorstOvershootDeg:F1} deg");
        }

        // --- calibration offset / rotation / height math tolerances ---
        public const float CalibMaxPosErrM = 5e-4f;         // offset + scale round-trip position error (metres)
        public const float CalibMaxRotErrDeg = 0.1f;        // offset + rotation-calibration round-trip error (degrees)
        public const float CalibMaxPitchHeightErrM = 5e-3f; // pitch-calibrated eye-height recovery error (metres)

        // Calibration math: the offset capture↔apply, device-scale, per-effector rotation, and pitch-
        // height formulas must round-trip / land on their targets within float tolerance, and the scale
        // modifier must sanitize bad overrides. A failure means a calibration formula changed in a way
        // that no longer reproduces the bone/height it was derived from.
        public static (bool pass, string reason) GateCalibrationMath(in BasisCalibrationMathSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Cases <= 0) return (false, "no cases");
            if (s.MaxOffsetPosErr > CalibMaxPosErrM || s.MaxRigidFollowErr > CalibMaxPosErrM || s.MaxScalePosErr > CalibMaxPosErrM)
                return (false, $"offset/scale round-trip off: offsetPos={s.MaxOffsetPosErr:F5} follow={s.MaxRigidFollowErr:F5} scalePos={s.MaxScalePosErr:F5} m > {CalibMaxPosErrM} m");
            if (s.MaxOffsetRotErrDeg > CalibMaxRotErrDeg || s.MaxRotCalErrDeg > CalibMaxRotErrDeg)
                return (false, $"rotation off: offsetRot={s.MaxOffsetRotErrDeg:F3} rotCal={s.MaxRotCalErrDeg:F3} deg > {CalibMaxRotErrDeg} (rotation calibration would leak orientation)");
            if (s.MaxPitchHeightErr > CalibMaxPitchHeightErrM)
                return (false, $"pitch-calibrated height off by {s.MaxPitchHeightErr:F4} m > {CalibMaxPitchHeightErrM} ({s.PitchSolvable} solved, {s.PitchFallback} fallback)");
            if (s.ScaleModifierMismatches > 0)
                return (false, $"{s.ScaleModifierMismatches} scale-modifier sanitization/FinalScale mismatches");
            return (true, $"offsetPos={s.MaxOffsetPosErr:F5}m rotCal={s.MaxRotCalErrDeg:F3}deg scalePos={s.MaxScalePosErr:F5}m pitch={s.MaxPitchHeightErr:F4}m ({s.PitchSolvable}/{s.PitchFallback}) cases={s.Cases} fails={s.Failures}");
        }

        // Procedural foot placement (BasisFootSimulateJob): a temporal stepping system, so the gate reads
        // the worst per-frame metric across the scripted locomotion battery (walk/strafe/turn/stop/slope/
        // stairs/gap). The invariants: feet never lift together or tangle, a planted foot is world-locked
        // (no skate) and on the ground, the leg can always reach its foot, the tilt/yaw clamps hold, and the
        // knee hint never falls behind the leg. NaN anywhere = the sim blew up.
        public static (bool pass, string reason) GateFoot(in BasisFootIKSweepSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Rows <= 0 || s.Scenarios <= 0) return (false, "no rows");
            if (s.HadNaN) return (false, "NaN foot position / knee hint (the sim blew up)");
            if (s.BothSteppingTicks > 0)
                return (false, $"both feet airborne for {s.BothSteppingTicks} ticks (worst: {s.BothSteppingWorstScenario} {s.BothSteppingWorstTicks}) -- a foot must never lift while the other is stepping");
            if (s.Crossovers > 0)
                return (false, $"feet crossed/tangled on {s.Crossovers} ticks (left foot ended up right of the right foot)");
            if (s.WorstExtensionRatio > FootMaxExtensionRatio)
                return (false, $"foot over-extended to {s.WorstExtensionRatio:F2}x standing reach > {FootMaxExtensionRatio:F2} (foot left behind -- the leg stretches)");
            if (s.WorstPlantedSlideMm > FootMaxPlantedSlideMm)
                return (false, $"planted-foot slide {s.WorstPlantedSlideMm:F0}mm/tick > {FootMaxPlantedSlideMm}mm (skating)");
            if (s.WorstPenetrationMm > FootMaxPenetrationMm)
                return (false, $"planted foot {s.WorstPenetrationMm:F0}mm below ground > {FootMaxPenetrationMm}mm");
            if (s.WorstPlantedHoverMm > FootMaxPlantedHoverMm)
                return (false, $"planted foot floats {s.WorstPlantedHoverMm:F0}mm above ground > {FootMaxPlantedHoverMm}mm");
            if (s.WorstTiltDeg > s.ConfiguredMaxTiltDeg + FootTiltSlackDeg)
                return (false, $"foot tilt {s.WorstTiltDeg:F0} > clamp {s.ConfiguredMaxTiltDeg:F0}+{FootTiltSlackDeg} deg (slope clamp not holding)");
            if (s.WorstYawDeg > s.ConfiguredMaxYawDeg + FootYawSlackDeg)
                return (false, $"foot yaw {s.WorstYawDeg:F0} > clamp {s.ConfiguredMaxYawDeg:F0}+{FootYawSlackDeg} deg (toe-out clamp not holding)");
            if (s.WorstKneeBackwardM > FootMaxKneeBehindM)
                return (false, $"knee hint {s.WorstKneeBackwardM * 100f:F1}cm behind the leg > {FootMaxKneeBehindM * 100f:F0}cm (knee would bend backward)");
            return (true, $"slide {s.WorstPlantedSlideMm:F1}mm ext {s.WorstExtensionRatio:F2} pen {s.WorstPenetrationMm:F0}mm tilt {s.WorstTiltDeg:F0} yaw {s.WorstYawDeg:F0} drift {s.WorstPlantToIdealM * 100f:F0}cm steps {s.TotalSteps}");
        }

        // --- twist (swing-twist decomposition) ---
        public const float TwistMaxErrDeg = 0.5f;        // pure-twist recovery + partial-twist blend error
        public const float TwistMaxAxisMisalignDeg = 1f; // extracted twist axis vs bone axis with a swing present

        // BasisTwistSolveCore: a pure twist about the bone axis must be recovered exactly, the Fraction must
        // blend it linearly, a perpendicular swing must not tilt the extracted twist axis, and the singular
        // inputs (no fraction / zero bone vector) must no-op.
        public static (bool pass, string reason) GateTwist(in BasisTwistSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Cases <= 0) return (false, "no cases");
            if (s.SingularityFailures > 0)
                return (false, $"{s.SingularityFailures} singular cases applied a twist (must no-op)");
            if (s.MaxPureTwistErrDeg > TwistMaxErrDeg || s.MaxFractionErrDeg > TwistMaxErrDeg)
                return (false, $"twist recovery off: pure={s.MaxPureTwistErrDeg:F3} frac={s.MaxFractionErrDeg:F3} deg > {TwistMaxErrDeg}");
            if (s.MaxAxisMisalignDeg > TwistMaxAxisMisalignDeg)
                return (false, $"swing tilted the extracted twist axis by {s.MaxAxisMisalignDeg:F2} deg > {TwistMaxAxisMisalignDeg} (swing-twist not separating)");
            return (true, $"pure={s.MaxPureTwistErrDeg:F3} frac={s.MaxFractionErrDeg:F3} axisMis={s.MaxAxisMisalignDeg:F2} deg cases={s.Cases}");
        }

        // --- virtual spine + remote bone chain ---
        public const float SpineMaxErr = 1e-3f;       // chain fraction / hips offset / yaw-flatness (metres or unitless)
        public const float SpineMaxYawDegErr = 0.1f;  // YawDegrees recovery + yaw-extraction idempotence (degrees)
        public const float RemoteBoneMaxErr = 1e-3f;  // remote FK composition / segment-length / scale-linearity (metres)

        // Virtual spine solve helpers: the chest/spine must sit on the neck→hips segment at their fractions
        // (no inversion), hips must drop the spine length below the neck with the pelvic bias, and yaw
        // extraction must strip pitch/roll. A failure means the torso synthesis geometry changed.
        public static (bool pass, string reason) GateSpine(in BasisSpineSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Cases <= 0) return (false, "no cases");
            if (s.ChainMonotonicFails > 0)
                return (false, $"{s.ChainMonotonicFails} chains put the chest farther from the neck than the spine (inverted)");
            if (s.MaxChainFracErr > SpineMaxErr)
                return (false, $"chest/spine off their neck→hips fraction by {s.MaxChainFracErr:F4} > {SpineMaxErr}");
            if (s.MaxHipsYErr > SpineMaxErr || s.MaxHipsXZErr > SpineMaxErr || s.MaxHipsFreezeErr > SpineMaxErr)
                return (false, $"hips position off: y={s.MaxHipsYErr:F4} xz={s.MaxHipsXZErr:F4} freeze={s.MaxHipsFreezeErr:F4} m > {SpineMaxErr}");
            if (s.MaxYawFlatErr > SpineMaxErr)
                return (false, $"extracted yaw not pitch/roll-free (forward.y={s.MaxYawFlatErr:F4} > {SpineMaxErr})");
            if (s.MaxYawIdempotentErr > SpineMaxYawDegErr || s.MaxYawDegErr > SpineMaxYawDegErr)
                return (false, $"yaw off: idempotence={s.MaxYawIdempotentErr:F3} degRecovery={s.MaxYawDegErr:F3} deg > {SpineMaxYawDegErr}");
            return (true, $"chainFrac={s.MaxChainFracErr:F4} hipsY={s.MaxHipsYErr:F4} yawFlat={s.MaxYawFlatErr:F4} yawDeg={s.MaxYawDegErr:F3} cases={s.Cases}");
        }

        // Remote head-chain FK: each child must compose as parent + headRot*offset, segment lengths must be
        // rotation-preserved and scale linearly, and outputs must stay finite at extreme/zero scale. (Hand/
        // foot end-effector drift is a play-mode measurement, not covered here.)
        public static (bool pass, string reason) GateRemoteBone(in BasisRemoteBoneSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Cases <= 0) return (false, "no cases");
            if (s.NaNCount > 0)
                return (false, $"{s.NaNCount} remote chains produced non-finite positions");
            if (s.MaxCompErr > RemoteBoneMaxErr || s.MaxSegLenErr > RemoteBoneMaxErr || s.MaxScaleErr > RemoteBoneMaxErr)
                return (false, $"remote FK off: comp={s.MaxCompErr:F4} segLen={s.MaxSegLenErr:F4} scale={s.MaxScaleErr:F4} m > {RemoteBoneMaxErr}");
            return (true, $"comp={s.MaxCompErr:F4} segLen={s.MaxSegLenErr:F4} scale={s.MaxScaleErr:F4} cases={s.Cases}");
        }
    }
}
