using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Pure-kinematic sweep of the locomotion vertical model in BasisLocalCharacterDriver /
    // BasisWalkMovementMode. The live jump/gravity path is CharacterController-coupled (the velocity is
    // integrated here but applied via characterController.Move, which only TRANSLATES by vSpeed*dt along Y),
    // so the colliders/scene physics can never run in a worker. This sweep RE-IMPLEMENTS the exact vertical
    // arithmetic from the driver constants -- the same launch velocity v0 = sqrt(jumpHeight * -2 * gravity),
    // the same per-fixed-dt semi-implicit gravity integration (vSpeed += gravity*dt; pos += vSpeed*dt), the
    // same fall-speed clamp max(vSpeed, -|gravity|), the same coyote-time countdown, and the same two-phase
    // landing-crouch Lerp blend -- and asserts the deterministic invariants:
    //   1. a simulated ballistic jump reaches an apex height that matches the configured jumpHeight (validates
    //      the gravity<->jumpHeight energy relation v0^2/(2|g|) == jumpHeight),
    //   2. the coyote-time window (counter set to coyoteTimeDuration, decremented by dt, CanJump while >0)
    //      stays open for exactly the configured duration,
    //   3. the landing-recovery dip eases in then recovers MONOTONICALLY back to rest within its recovery time
    //      with no overshoot,
    //   4. nothing goes NaN.
    // NOTE: this is the constants-derived re-implementation of the math, NOT a call into the live driver --
    // see the header constants block. CharacterController.Move, colliders and ground detection are out of scope.
    public struct BasisLocomotionSweepConfig
    {
        public float Fps;                 // fixed simulation rate (dt = 1/Fps), mirrors the FixedUpdate cadence
        public float JumpHeight;          // BasisLocalCharacterDriver.jumpHeight (m)
        public float Gravity;             // BasisLocalCharacterDriver.gravityValue (m/s^2, negative)
        public float CoyoteTimeDuration;  // BasisLocalCharacterDriver.coyoteTimeDuration (s)
        public float LandingDescentSpeed; // BasisLocalCharacterDriver.landingDescentSpeed (Lerp rate, phase 1)
        public float LandingRecoverySpeed;// BasisLocalCharacterDriver.landingRecoverySpeed (Lerp rate, phase 2)
        public float LandingImpactScale;  // BasisLocalCharacterDriver.landingImpactScale (dip per m/s fall)
        public float MaxLandingCrouch;    // BasisLocalCharacterDriver.maxLandingCrouchEffect (dip clamp, m)
        public float LandingFallSpeed;    // synthetic impact velocity fed to the landing model (m/s, magnitude)

        public static BasisLocomotionSweepConfig Default()
        {
            // Defaults mirror the live BasisLocalCharacterDriver field initializers (read 2026-06-16):
            //   gravityValue        = -9.81
            //   jumpHeight          =  1.0   (m)
            //   coyoteTimeDuration  =  0.15  (s)
            //   landingDescentSpeed = 15
            //   landingRecoverySpeed=  6
            //   landingImpactScale  =  0.06
            //   maxLandingCrouchEffect = 0.35
            // Fps 50 == Unity's default Fixed Timestep (0.02 s). LandingFallSpeed 9.81 is a representative
            // hard landing (terminal-clamped fall speed == |gravity|), giving a ~0.06*9.81 ≈ 0.59 -> clamped
            // to 0.35 m dip so the recovery curve is fully exercised.
            return new BasisLocomotionSweepConfig
            {
                Fps = 50f,
                JumpHeight = 1.0f,
                Gravity = -9.81f,
                CoyoteTimeDuration = 0.15f,
                LandingDescentSpeed = 15f,
                LandingRecoverySpeed = 6f,
                LandingImpactScale = 0.06f,
                MaxLandingCrouch = 0.35f,
                LandingFallSpeed = 9.81f,
            };
        }
    }

    public struct BasisLocomotionSweepSummary
    {
        public bool Ok;
        public string Path;
        public int Steps;
        public int NaNCount;

        // --- ballistic jump ---
        public float ConfiguredJumpHeightM; // the target the apex is compared against (m)
        public float SimApexHeightM;        // peak Y of the re-implemented fixed-dt jump integration (m)
        public float ApexErrM;              // |SimApexHeightM - ConfiguredJumpHeightM| (m)
        public float LaunchVelocity;        // v0 = sqrt(jumpHeight * -2 * gravity) (m/s)
        public float ApexDiscretizationBoundM; // expected sampled-apex undershoot ~v0*dt/2 (semi-implicit Euler half-step)

        // --- coyote time ---
        public float ConfiguredCoyoteS;     // the configured window (s)
        public float MeasuredCoyoteS;       // dt-quantized window the countdown actually keeps CanJump true (s)
        public float CoyoteErrS;            // |measured - configured|, expected <= one dt of quantization (s)

        // --- landing recovery blend ---
        public float LandingPeakDipM;       // worst (deepest) dip the two-phase blend reaches (m)
        public float LandingPeakTargetM;    // clamped impact target the dip eases toward (m)
        public float LandingRecoveryReversalM; // worst backward (re-deepening) move during phase-2 recovery (m); >0 = non-monotone
        public float LandingSettleTimeS;    // time from peak dip back to rest (< 0.001 m) (s)
        public bool LandingSettled;         // did the dip return to rest within the simulated horizon

        public string Error;
    }

    public static class BasisLocomotionSweep
    {
        public const string DefaultFileName = "BasisLocomotionSweep.csv";

        public static string DefaultPath() => System.IO.Path.Combine(Application.persistentDataPath, DefaultFileName);

        public static BasisLocomotionSweepSummary Run(BasisLocomotionSweepConfig cfg, string path)
        {
            var summary = new BasisLocomotionSweepSummary { Ok = false, Path = path };
            float fps = Mathf.Max(5f, cfg.Fps);
            float dt = 1f / fps;
            float gravity = -Mathf.Abs(cfg.Gravity); // driver treats gravityValue as negative; force the sign
            float absG = Mathf.Abs(gravity);

            int nan = 0;
            int totalSteps = 0;
            float simApex = 0f, apexErr = 0f, launchV = 0f;
            float measuredCoyote = 0f, coyoteErr = 0f;
            float peakDip = 0f, peakTarget = 0f, recoveryReversal = 0f, settleTime = 0f;
            bool settled = false;

            try
            {
                string d = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);

                using (var w = new StreamWriter(path, false, Encoding.UTF8))
                {
                    w.WriteLine("# BasisLocomotionSweep " + System.DateTime.UtcNow.ToString("o") +
                                " fps=" + F(fps) + " jumpHeight=" + F(cfg.JumpHeight) + " gravity=" + F(gravity) +
                                " coyote=" + F(cfg.CoyoteTimeDuration) + " descent=" + F(cfg.LandingDescentSpeed) +
                                " recovery=" + F(cfg.LandingRecoverySpeed) + " impactScale=" + F(cfg.LandingImpactScale) +
                                " maxCrouch=" + F(cfg.MaxLandingCrouch) + " fallSpeed=" + F(cfg.LandingFallSpeed));
                    w.WriteLine("scenario,step,t,a,b");
                    var sb = new StringBuilder(96);

                    // === Ballistic jump ===
                    // Reproduces BasisWalkMovementMode.Tick exactly: on the jump frame
                    //   vSpeed = Mathf.Sqrt(jumpHeight * -2 * gravity)
                    // then every fixed step
                    //   vSpeed += gravity * dt;   vSpeed = max(vSpeed, -|gravity|);   pos.y += vSpeed * dt
                    // (CharacterController.Move(move.y = vSpeed*dt) only translates -- no collision here.)
                    {
                        launchV = Mathf.Sqrt(Mathf.Max(0f, cfg.JumpHeight) * -2f * gravity);
                        float vSpeed = launchV;
                        float y = 0f;
                        // a: launch frame integrates first, then we record. Loop until the body returns to/below
                        // launch height (one full parabola), capped so a degenerate config can't spin forever.
                        int cap = Mathf.Max(64, Mathf.RoundToInt(8f / dt));
                        int i = 0;
                        WriteRow(w, sb, "jump", i, 0f, y, vSpeed);
                        for (i = 1; i <= cap; i++)
                        {
                            vSpeed += gravity * dt;
                            vSpeed = Mathf.Max(vSpeed, -absG); // driver's terminal-fall clamp
                            y += vSpeed * dt;                  // == characterController.Move(vSpeed*dt).y, no collider
                            if (!Finite(y) || !Finite(vSpeed)) { nan++; break; }
                            if (y > simApex) simApex = y;
                            WriteRow(w, sb, "jump", i, i * dt, y, vSpeed);
                            totalSteps++;
                            if (y <= 0f && vSpeed < 0f) break; // back down through launch height
                        }
                        apexErr = Mathf.Abs(simApex - cfg.JumpHeight);
                    }

                    // === Coyote-time window ===
                    // Reproduces GroundCheck's airborne branch: on the frame we walk off a ledge
                    // (LastWasGrounded && vSpeed<=0) coyoteTimeCounter = coyoteTimeDuration; thereafter it
                    // decrements by dt each airborne frame and CanJump stays true while counter > 0. We count
                    // how long CanJump holds -- it is dt-quantized, so the error vs the configured duration is
                    // expected to be within one dt.
                    {
                        float counter = Mathf.Max(0f, cfg.CoyoteTimeDuration); // seeded on leave-ground frame
                        int openFrames = 0;
                        int cap = Mathf.Max(64, Mathf.RoundToInt(4f / dt));
                        WriteRow(w, sb, "coyote", 0, 0f, counter, 1f);
                        for (int i = 1; i <= cap; i++)
                        {
                            // CanJump == counter > 0f evaluated at the TOP of the frame (before this tick's decrement),
                            // mirroring HandleJumpRequest/Tick order where the jump check precedes the countdown.
                            bool canJump = counter > 0f;
                            if (!canJump) break;
                            openFrames++;
                            counter -= dt;
                            if (!Finite(counter)) { nan++; break; }
                            WriteRow(w, sb, "coyote", i, i * dt, Mathf.Max(counter, 0f), canJump ? 1f : 0f);
                            totalSteps++;
                        }
                        measuredCoyote = openFrames * dt;
                        coyoteErr = Mathf.Abs(measuredCoyote - cfg.CoyoteTimeDuration);
                    }

                    // === Landing-recovery blend ===
                    // Reproduces the two-phase landing impact in SimulateMovement + GroundCheck:
                    //   on land: landingCrouchTarget = clamp(fallSpeed * landingImpactScale, 0, maxLandingCrouch)
                    //   phase 1 (target>0): effect = Lerp(effect, target, descentSpeed*dt);
                    //                       if (target - effect < 0.01) target = 0
                    //   phase 2 (effect>0): effect = Lerp(effect, 0, recoverySpeed*dt);
                    //                       if (effect < 0.001) effect = 0
                    // Invariants: the dip eases IN to ~peakTarget then recovers MONOTONICALLY to rest (no
                    // re-deepening) and settles (< 0.001 m) within the horizon.
                    {
                        float target = Mathf.Clamp(Mathf.Abs(cfg.LandingFallSpeed) * cfg.LandingImpactScale, 0f, cfg.MaxLandingCrouch);
                        peakTarget = target;
                        float effect = 0f;
                        float prevEffect = 0f;
                        bool reachedPeak = false;
                        int peakFrame = -1;
                        int cap = Mathf.Max(128, Mathf.RoundToInt(8f / dt));
                        WriteRow(w, sb, "landing", 0, 0f, effect, target);
                        for (int i = 1; i <= cap; i++)
                        {
                            if (target > 0f)
                            {
                                effect = Mathf.Lerp(effect, target, cfg.LandingDescentSpeed * dt);
                                if (target - effect < 0.01f) target = 0f;
                            }
                            else if (effect > 0f)
                            {
                                if (!reachedPeak) { reachedPeak = true; peakFrame = i; }
                                effect = Mathf.Lerp(effect, 0f, cfg.LandingRecoverySpeed * dt);
                                if (effect < 0.001f) effect = 0f;
                            }
                            if (!Finite(effect)) { nan++; break; }
                            if (effect > peakDip) peakDip = effect;
                            // monotone-recovery check: once we have entered phase 2, the dip must not grow again.
                            if (reachedPeak)
                            {
                                float back = effect - prevEffect; // positive = re-deepening (bad)
                                if (back > recoveryReversal) recoveryReversal = back;
                            }
                            WriteRow(w, sb, "landing", i, i * dt, effect, target);
                            totalSteps++;
                            prevEffect = effect;
                            if (reachedPeak && effect <= 0f)
                            {
                                settled = true;
                                settleTime = (i - Mathf.Max(0, peakFrame)) * dt;
                                break;
                            }
                        }
                        if (!settled && peakFrame < 0)
                        {
                            // never entered recovery (target never reached / zero impact) -- treat as settled at rest.
                            settled = effect <= 0f;
                        }
                    }
                }

                summary.Ok = true;
                summary.Steps = totalSteps;
                summary.NaNCount = nan;
                summary.ConfiguredJumpHeightM = cfg.JumpHeight;
                summary.SimApexHeightM = simApex;
                summary.ApexErrM = apexErr;
                summary.LaunchVelocity = launchV;
                // The sampled apex of semi-implicit Euler undershoots the true apex by ~v0*dt/2 (the half-step
                // sampling miss); the live FixedUpdate jump undershoots identically. The gate tolerates this.
                summary.ApexDiscretizationBoundM = 0.5f * launchV * dt;
                summary.ConfiguredCoyoteS = cfg.CoyoteTimeDuration;
                summary.MeasuredCoyoteS = measuredCoyote;
                summary.CoyoteErrS = coyoteErr;
                summary.LandingPeakDipM = peakDip;
                summary.LandingPeakTargetM = peakTarget;
                summary.LandingRecoveryReversalM = recoveryReversal;
                summary.LandingSettleTimeS = settleTime;
                summary.LandingSettled = settled;
            }
            catch (System.Exception e)
            {
                summary.Ok = false;
                summary.Error = e.Message;
            }

            return summary;
        }

        static void WriteRow(StreamWriter w, StringBuilder sb, string scenario, int step, float t, float a, float b)
        {
            sb.Clear();
            sb.Append(scenario).Append(',').Append(step).Append(',');
            sb.Append(F(t)).Append(',').Append(F(a)).Append(',').Append(F(b));
            w.WriteLine(sb.ToString());
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        static string F(float v)
        {
            if (float.IsNaN(v)) return "nan";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
