using System.Globalization;
using System.IO;
using System.Text;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Stability / correctness sweep of the secondary bone-chain simulation (BasisBoneSimChainJob, the
    // Burst chain step that mirrors BasisLocalBoneControl.ComputeMovementLocal). The chain Simulate path
    // is a stateful per-frame feedback loop -- each bone lerps from its own LastRun pose toward a freshly
    // computed destination -- so a static grid proves nothing; this TICKS the real job over scripted
    // incoming tracker poses and watches the recurrence for the ways a feedback smoother goes wrong:
    // NaN/Inf, a non-unit quaternion or one whose magnitude GROWS (energy gain / divergence), unbounded
    // position lag (the chain never catches the target), and a sustained self-oscillation that never
    // settles. Two bones are driven: a tracked root on the smoothed inverse-offset path
    // (lerp(LastRun,dest,ts) / slerp), and a child on the target-follow path (TargetIndex=0, the
    // per-frame ApplyLerpToQuaternion + position lerp), so both shipping branches are exercised.
    //
    // THREAD-SAFETY: MAIN-THREAD-ONLY. BasisBoneSimChainJob is a [BurstCompile] IJob over NativeArrays;
    // Run allocates/ticks (via Execute(), the managed path -- no Schedule) and disposes those NativeArrays,
    // so it must run on the main thread like BasisFootIKSweep, not on a worker.
    public struct BasisBoneSimStabilitySweepConfig
    {
        public float Fps;
        public float Seconds;

        // Tunables mirrored from BasisLocalBoneControl (the smoothing/lerp constants the live chain runs).
        public float TrackerSmooth;          // BasisLocalBoneControl.trackersmooth (saturates to 1 in the job)
        public float PositionLerpAmount;     // BasisLocalBoneControl.PositionLerpAmount (per-second, *dt then clamp01)
        public float QuaternionLerp;         // BasisLocalBoneControl.QuaternionLerp (base rot rate)
        public float QuaternionLerpFastMovement; // BasisLocalBoneControl.QuaternionLerpFastMovement (fast rot rate)
        public float AngleBeforeSpeedup;     // BasisLocalBoneControl.AngleBeforeSpeedup (deg)

        // Scenario drive amplitudes.
        public float StepAngleDeg;           // yaw step the tracker rotation jumps through (0 -> this)
        public float StepPositionM;          // position step the tracker jumps through (0 -> this, along X)
        public float OscAngleDeg;            // sustained yaw oscillation amplitude
        public float OscPositionM;           // sustained position oscillation amplitude (along X)
        public float OscHz;                  // oscillation frequency
        public float ChildOffsetM;           // rest offset of the child bone from the root (local +Z)

        public static BasisBoneSimStabilitySweepConfig Default()
        {
            // Tunables are the exact BasisLocalBoneControl constants the live secondary chain runs on, so
            // the default sweep characterizes the shipping smoother; the window can sweep them to probe the
            // stability/latency tradeoff (e.g. a higher per-second rate at a low FPS approaching the clamp).
            return new BasisBoneSimStabilitySweepConfig
            {
                Fps = 90f,
                Seconds = 6f,
                TrackerSmooth = BasisLocalBoneControl.trackersmooth,
                PositionLerpAmount = BasisLocalBoneControl.PositionLerpAmount,
                QuaternionLerp = BasisLocalBoneControl.QuaternionLerp,
                QuaternionLerpFastMovement = BasisLocalBoneControl.QuaternionLerpFastMovement,
                AngleBeforeSpeedup = BasisLocalBoneControl.AngleBeforeSpeedup,
                StepAngleDeg = 45f,
                StepPositionM = 0.5f,
                OscAngleDeg = 30f,
                OscPositionM = 0.25f,
                OscHz = 1.5f,
                ChildOffsetM = 0.25f,
            };
        }
    }

    public struct BasisBoneSimStabilitySweepSummary
    {
        public bool Ok;
        public string Path;
        public int Steps;
        public int Scenarios;

        public int NaNCount;                 // non-finite root/child pose components across every tick

        public float MaxQuatNonUnit;         // worst |length(outgoing rot)-1| (drift off the unit sphere)
        public float MaxQuatEnergyGain;      // worst (rot-magnitude / target-magnitude - 1) -- >0 = the rotation grew past its drive (energy gain / divergence)

        public float MaxRootPosLagM;         // steady-state |root - tracker target| after a step settles (m)
        public float MaxChildPosLagM;        // steady-state |child - (root + offset)| after a step settles (m)
        public float StepSettleErrDeg;       // |root rot - target rot| at the end of the angle-step scenario (deg)
        public float StepSettleErrM;         // |root pos - target pos| at the end of the position-step scenario (m)

        public float OscResidualDeg;         // post-transient yaw overshoot past the drive envelope (deg) -- growth = self-oscillation
        public float OscResidualM;           // post-transient position overshoot past the drive envelope (m)
        public float SettleRatio;            // late-window output range / drive range on the sustained osc (~1 = tracks, >1.1 = ringing/energy gain)

        public string Error;
    }

    public static class BasisBoneSimStabilitySweep
    {
        public const string DefaultFileName = "BasisBoneSimStabilitySweep.csv";

        public static string DefaultPath() => System.IO.Path.Combine(Application.persistentDataPath, DefaultFileName);

        enum Drive { AngleStep, PositionStep, AngleOsc, PositionOsc }

        // MAIN-THREAD-ONLY: allocates/ticks/disposes Burst-job NativeArrays. Do not call from a worker.
        public static BasisBoneSimStabilitySweepSummary Run(BasisBoneSimStabilitySweepConfig cfg, string path)
        {
            var summary = new BasisBoneSimStabilitySweepSummary { Ok = false, Path = path };
            float fps = Mathf.Max(5f, cfg.Fps);
            float dt = 1f / fps;
            int n = Mathf.Max(16, Mathf.RoundToInt(cfg.Seconds * fps));
            int half = n / 2;          // step fires at the midpoint
            int lateStart = (n * 3) / 4; // post-transient window for settle/oscillation checks

            int nan = 0;
            float maxNonUnit = 0f, maxEnergy = 0f;
            float maxRootLag = 0f, maxChildLag = 0f;
            float stepSettleDeg = 0f, stepSettleM = 0f;
            float oscResidualDeg = 0f, oscResidualM = 0f, settleRatio = 0f;
            int scenarios = 0;

            try
            {
                string d = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisBoneSimStabilitySweep " + System.DateTime.UtcNow.ToString("o") +
                                " fps=" + F(fps) + " trackerSmooth=" + F(cfg.TrackerSmooth) + " posLerp=" + F(cfg.PositionLerpAmount) +
                                " quatLerp=" + F(cfg.QuaternionLerp) + " quatLerpFast=" + F(cfg.QuaternionLerpFastMovement) +
                                " angleBeforeSpeedup=" + F(cfg.AngleBeforeSpeedup) +
                                " (BasisBoneSimChainJob.Execute, main-thread, serial)");
                    w.WriteLine("scenario,step,driveDeg,driveX,rootRotErrDeg,rootPosLagM,childPosLagM,rootQuatNonUnit,rootQuatMag,childQuatMag");
                    var sb = new StringBuilder(160);

                    // ── Angle step: tracker yaw jumps 0 -> StepAngleDeg at the midpoint. The smoothed root must
                    //    converge (no energy gain, stays unit) and the child follows the rotation. ──
                    {
                        var r = RunScenario(cfg, Drive.AngleStep, dt, n, half, lateStart, w, sb, "angle-step",
                            ref nan, ref maxNonUnit, ref maxEnergy, ref maxRootLag, ref maxChildLag);
                        stepSettleDeg = r.finalRotErrDeg;
                        scenarios++;
                    }

                    // ── Position step: tracker position jumps 0 -> StepPositionM along X at the midpoint.
                    //    Root position lerps in (bounded lag); child trails by its offset. ──
                    {
                        var r = RunScenario(cfg, Drive.PositionStep, dt, n, half, lateStart, w, sb, "pos-step",
                            ref nan, ref maxNonUnit, ref maxEnergy, ref maxRootLag, ref maxChildLag);
                        stepSettleM = r.finalPosErrM;
                        scenarios++;
                    }

                    // ── Sustained yaw oscillation: must track without ringing up. Residual = output past the
                    //    drive envelope in the late window; SettleRatio = output range / drive range. ──
                    {
                        var r = RunScenario(cfg, Drive.AngleOsc, dt, n, half, lateStart, w, sb, "angle-osc",
                            ref nan, ref maxNonUnit, ref maxEnergy, ref maxRootLag, ref maxChildLag);
                        oscResidualDeg = r.oscResidual;
                        settleRatio = Mathf.Max(settleRatio, r.settleRatio);
                        scenarios++;
                    }

                    // ── Sustained position oscillation: same, on the position lerp. ──
                    {
                        var r = RunScenario(cfg, Drive.PositionOsc, dt, n, half, lateStart, w, sb, "pos-osc",
                            ref nan, ref maxNonUnit, ref maxEnergy, ref maxRootLag, ref maxChildLag);
                        oscResidualM = r.oscResidual;
                        settleRatio = Mathf.Max(settleRatio, r.settleRatio);
                        scenarios++;
                    }
                }

                summary.Ok = true;
                summary.Steps = n;
                summary.Scenarios = scenarios;
                summary.NaNCount = nan;
                summary.MaxQuatNonUnit = maxNonUnit;
                summary.MaxQuatEnergyGain = maxEnergy;
                summary.MaxRootPosLagM = maxRootLag;
                summary.MaxChildPosLagM = maxChildLag;
                summary.StepSettleErrDeg = stepSettleDeg;
                summary.StepSettleErrM = stepSettleM;
                summary.OscResidualDeg = oscResidualDeg;
                summary.OscResidualM = oscResidualM;
                summary.SettleRatio = settleRatio;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }

            return summary;
        }

        struct ScenarioResult
        {
            public float finalRotErrDeg;   // root rot vs target at the last tick
            public float finalPosErrM;     // root pos vs target at the last tick
            public float oscResidual;      // late-window output past the drive envelope (deg or m)
            public float settleRatio;      // late-window output range / drive range
        }

        // Ticks the real BasisBoneSimChainJob over a two-bone chain (root index 0 tracked + smoothed,
        // child index 1 following index 0) for one drive scenario, accumulating the global stability peaks
        // and returning the per-scenario settle/oscillation metrics.
        static ScenarioResult RunScenario(BasisBoneSimStabilitySweepConfig cfg, Drive drive, float dt, int n, int half, int lateStart,
            StreamWriter w, StringBuilder sb, string name,
            ref int nan, ref float maxNonUnit, ref float maxEnergy, ref float maxRootLag, ref float maxChildLag)
        {
            var res = new ScenarioResult();
            bool angle = drive == Drive.AngleStep || drive == Drive.AngleOsc;
            bool osc = drive == Drive.AngleOsc || drive == Drive.PositionOsc;

            // Two bones: 0 = tracked root, 1 = child. Chain order is {0,1} so the child's target (0) is
            // already solved earlier in the same serial pass, exactly like a real parent->child chain.
            var inputs = new NativeArray<BasisBoneSimInput>(2, Allocator.Persistent);
            var states = new NativeArray<BasisBoneSimState>(2, Allocator.Persistent);
            var chain = new NativeArray<int>(2, Allocator.Persistent);

            // Identity parent frame: outgoing-local == outgoing-world, so world checks read the local solve.
            float4x4 parentMatrix = float4x4.identity;
            quaternion parentRot = quaternion.identity;
            float3 childOffset = new float3(0f, 0f, cfg.ChildOffsetM);

            try
            {
                chain[0] = 0; chain[1] = 1;

                // Root: tracked, smoothed inverse-offset path. Zero inverse offset so the destination is the
                // raw incoming pose -- isolates the lerp/slerp recurrence, not the offset transform.
                inputs[0] = new BasisBoneSimInput
                {
                    IncomingPosition = float3.zero,
                    IncomingRotation = quaternion.identity,
                    InverseOffsetPosition = float3.zero,
                    InverseOffsetRotation = quaternion.identity,
                    ScaledOffset = float3.zero,
                    TargetIndex = -1,
                    HasTracker = 1,
                    UseInverseOffset = 1,
                    HasVirtualOverride = 0,
                    HasTarget = 0,
                };
                // Child: follows the root via the target path (the per-frame ApplyLerpToQuaternion + pos lerp).
                inputs[1] = new BasisBoneSimInput
                {
                    IncomingPosition = float3.zero,
                    IncomingRotation = quaternion.identity,
                    InverseOffsetPosition = float3.zero,
                    InverseOffsetRotation = quaternion.identity,
                    ScaledOffset = childOffset,
                    TargetIndex = 0,
                    HasTracker = 0,
                    UseInverseOffset = 0,
                    HasVirtualOverride = 0,
                    HasTarget = 1,
                };

                // Seed valid (identity) rotations -- a default NativeArray leaves quaternions at (0,0,0,0),
                // which is what the driver's WireControlsAndInitStore guards against.
                states[0] = SeedState();
                states[1] = SeedState();

                byte snap = 0;
                float oscMaxOut = 0f;   // late-window peak |output| (deg or m)
                float driveAmp = osc ? (angle ? cfg.OscAngleDeg : cfg.OscPositionM) : 0f;

                for (int i = 0; i < n; i++)
                {
                    // Build the scripted incoming tracker pose for this tick.
                    float driveDeg = 0f, driveX = 0f;
                    quaternion incRot = quaternion.identity;
                    float3 incPos = float3.zero;
                    switch (drive)
                    {
                        case Drive.AngleStep:
                            driveDeg = i < half ? 0f : cfg.StepAngleDeg;
                            incRot = quaternion.AxisAngle(new float3(0, 1, 0), math.radians(driveDeg));
                            break;
                        case Drive.PositionStep:
                            driveX = i < half ? 0f : cfg.StepPositionM;
                            incPos = new float3(driveX, 0f, 0f);
                            break;
                        case Drive.AngleOsc:
                            driveDeg = cfg.OscAngleDeg * Mathf.Sin(2f * Mathf.PI * cfg.OscHz * (i * dt));
                            incRot = quaternion.AxisAngle(new float3(0, 1, 0), math.radians(driveDeg));
                            break;
                        case Drive.PositionOsc:
                            driveX = cfg.OscPositionM * Mathf.Sin(2f * Mathf.PI * cfg.OscHz * (i * dt));
                            incPos = new float3(driveX, 0f, 0f);
                            break;
                    }

                    var inp = inputs[0];
                    inp.IncomingPosition = incPos;
                    inp.IncomingRotation = incRot;
                    inputs[0] = inp;

                    // Tick the REAL job, serially, via the managed Execute() path (same math as shipped).
                    new BasisBoneSimChainJob
                    {
                        ChainIndices = chain,
                        Inputs = inputs,
                        States = states,
                        ParentMatrix = parentMatrix,
                        ParentRotation = parentRot,
                        DeltaTime = dt,
                        InstantSnap = snap,
                    }.Execute();

                    BasisBoneSimState rootS = states[0];
                    BasisBoneSimState childS = states[1];

                    bool finite = Finite(rootS.OutgoingPosition) && Finite(rootS.OutgoingRotation)
                               && Finite(childS.OutgoingPosition) && Finite(childS.OutgoingRotation);
                    if (!finite) { nan++; break; }

                    // Quaternion health: stay unit, and don't grow past the drive (energy gain / divergence).
                    float rootMag = QuatLength(rootS.OutgoingRotation);
                    float childMag = QuatLength(childS.OutgoingRotation);
                    float nonUnit = Mathf.Max(Mathf.Abs(rootMag - 1f), Mathf.Abs(childMag - 1f));
                    if (nonUnit > maxNonUnit) maxNonUnit = nonUnit;

                    // Output rotation magnitude (angle from identity) must not exceed the drive's by more than
                    // float slop -- a feedback smoother that overshoots adds rotational energy it wasn't fed.
                    float driveAngleMag = angle ? Mathf.Abs(driveDeg) : 0f;
                    float rootAngleMag = AngleFromIdentityDeg(rootS.OutgoingRotation);
                    if (driveAngleMag > 1f)
                    {
                        float gain = rootAngleMag / driveAngleMag - 1f;
                        if (gain > maxEnergy) maxEnergy = gain;
                    }

                    // Position lag: root vs its tracker target; child vs (root + rotated offset).
                    float rootLag = math.length(rootS.OutgoingPosition - incPos);
                    float3 expectedChild = rootS.OutgoingPosition + math.mul(rootS.OutgoingRotation, childOffset);
                    float childLag = math.length(childS.OutgoingPosition - expectedChild);

                    // Only score lag in the post-transient window (after the step has had time to settle).
                    if (i >= lateStart)
                    {
                        if (rootLag > maxRootLag) maxRootLag = rootLag;
                        if (childLag > maxChildLag) maxChildLag = childLag;

                        // Oscillation residual: late-window output past the drive envelope.
                        if (osc)
                        {
                            float outMag = angle ? rootAngleMag : Mathf.Abs(rootS.OutgoingPosition.x);
                            if (outMag > oscMaxOut) oscMaxOut = outMag;
                        }
                    }

                    float rotErrDeg = AngleBetweenDeg(rootS.OutgoingRotation, incRot);
                    if (i == n - 1)
                    {
                        res.finalRotErrDeg = rotErrDeg;
                        res.finalPosErrM = rootLag;
                    }

                    sb.Clear();
                    sb.Append(name).Append(',').Append(i).Append(',');
                    sb.Append(F(driveDeg)).Append(',').Append(F(driveX)).Append(',');
                    sb.Append(F(rotErrDeg)).Append(',').Append(F(rootLag)).Append(',').Append(F(childLag)).Append(',');
                    sb.Append(F(nonUnit)).Append(',').Append(F(rootMag)).Append(',').Append(F(childMag));
                    w.WriteLine(sb.ToString());
                }

                if (osc && driveAmp > 1e-6f)
                {
                    // Residual past the drive envelope: a low-pass output should sit at or under the drive
                    // amplitude (lag attenuates it); anything above is overshoot/ringing.
                    res.oscResidual = Mathf.Max(0f, oscMaxOut - driveAmp);
                    res.settleRatio = oscMaxOut / driveAmp;
                }
            }
            finally
            {
                inputs.Dispose(); states.Dispose(); chain.Dispose();
            }
            return res;
        }

        static BasisBoneSimState SeedState()
        {
            return new BasisBoneSimState
            {
                OutgoingPosition = float3.zero,
                OutgoingRotation = quaternion.identity,
                LastRunPosition = float3.zero,
                LastRunRotation = quaternion.identity,
                OutgoingWorldPosition = float3.zero,
                OutgoingWorldRotation = quaternion.identity,
            };
        }

        static float QuatLength(quaternion q) => math.length(q.value);

        static float AngleFromIdentityDeg(quaternion q)
        {
            float w = Mathf.Abs(math.clamp(q.value.w, -1f, 1f));
            return math.degrees(2f * math.acos(w));
        }

        static float AngleBetweenDeg(quaternion a, quaternion b)
        {
            float dot = math.abs(math.clamp(math.dot(a.value, b.value), -1f, 1f));
            return math.degrees(2f * math.acos(dot));
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(float3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static bool Finite(quaternion q) => Finite(q.value.x) && Finite(q.value.y) && Finite(q.value.z) && Finite(q.value.w);

        static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
