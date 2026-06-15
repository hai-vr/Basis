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
        // binds; a tracker never crosses to the opposite body side) gate unconditionally. Correctness
        // is gated strictly on the common "core" archetypes and at a tunable floor overall; extreme
        // archetypes are reported (CSV + confusions) rather than failing the gate.
        public static (bool pass, string reason) GateTrackerPlacement(in BasisTrackerPlacementSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Trackers <= 0) return (false, "no trackers");
            if (s.NearOriginLeaks > 0)
                return (false, $"{s.NearOriginLeaks} stale/origin tracker(s) bound to a role (must never happen)");
            if (s.CrossSideLeaks > 0)
                return (false, $"{s.CrossSideLeaks} tracker(s) bound to the opposite body side ({s.TopConfusions})");
            if (s.CoreCleanFraction < TrackerPlacementCoreMinFraction)
                return (false, $"core archetypes clean {s.CoreCleanFraction:P0} < {TrackerPlacementCoreMinFraction:P0} ({s.CoreCleanCorrect}/{s.CoreCleanTrackers}; {s.TopConfusions})");
            if (s.CleanCorrectFraction < TrackerPlacementCleanMinFraction)
                return (false, $"clean correctness {s.CleanCorrectFraction:P0} < {TrackerPlacementCleanMinFraction:P0} ({s.CleanCorrect}/{s.CleanTrackers}; {s.TopConfusions})");
            return (true, $"clean {s.CleanCorrectFraction:P0} (core {s.CoreCleanFraction:P0}) overall {s.Correct}/{s.Trackers} misassign={s.Misassigned} unassigned={s.Unassigned} invDiffs={s.InvarianceDiffs} minMargin={s.MinCorrectMargin:F1}");
        }
    }
}
