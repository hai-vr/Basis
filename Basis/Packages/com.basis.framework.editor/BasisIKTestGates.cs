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
        public const float ElbowMinClearedFraction = 0.55f; // protect must clear most of the clearable set (ceiling ~0.64)
        public const float ElbowMaxMeanResidualPenMm = 25f; // mean leftover torso penetration after the push
        public const float ElbowMaxSensDegPerCm = 90f;      // final elbow swivel per cm hand jitter
        public const float ShoulderMaxAngleDeg = 60f;
        public const float HeadMaxNeckDeg = 90f;
        public const float TrajMaxPopDeg = 45f;             // swivel jump on smooth motion (discontinuity)
        public const float TrajMaxRoughDeg = 15f;           // swivel roughness under tracking noise (jitter)

        public static (bool pass, string reason) GateArm(in BasisArmIKSweepSummary s)
        {
            if (!s.Ok) return (false, string.IsNullOrEmpty(s.Error) ? "did not run" : s.Error);
            if (s.Rows <= 0) return (false, "no rows");
            if (s.ReachablePoints <= 0) return (false, "no reachable points");
            if (s.TrackerMeanAlignErrDeg > ArmMaxMeanAlignErrDeg)
                return (false, $"tracker meanAlignErr {s.TrackerMeanAlignErrDeg:F1} > {ArmMaxMeanAlignErrDeg} deg (tracker not followed)");
            if (s.TrackerMaxSensDegPerCm > ArmMaxTrackerSensDegPerCm)
                return (false, $"max tracker sens {s.TrackerMaxSensDegPerCm:F0} > {ArmMaxTrackerSensDegPerCm} deg/cm (jitter)");
            return (true, $"reach={s.ReachablePoints} alignErr={s.TrackerMeanAlignErrDeg:F1} maxSens={s.TrackerMaxSensDegPerCm:F0}");
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
            if (s.MaxSensDegPerCm > ElbowMaxSensDegPerCm)
                return (false, $"max sens {s.MaxSensDegPerCm:F0} > {ElbowMaxSensDegPerCm} deg/cm (oscillation)");
            return (true, $"cleared={clearedFrac:P0} resid={s.MeanResidualPenMm:F0}mm maxSens={s.MaxSensDegPerCm:F0}");
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
    }
}
