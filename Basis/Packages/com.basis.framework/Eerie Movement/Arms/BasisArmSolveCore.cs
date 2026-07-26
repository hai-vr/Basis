using UnityEngine;
namespace Basis.IK
{
    public struct BasisArmSolveInput
    {
        // --- Current Pose Positions & Rotations ---
        public Vector3 Shoulder;
        public Vector3 Elbow;
        public Vector3 Hand;

        public Quaternion RootRotation; // Upper Arm / Humerus
        public Quaternion MidRotation;  // Lower Arm / Forearm
        public Quaternion TipRotation;  // Hand / Wrist

        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public Quaternion TargetOffset;

        // --- Standardized Bind / Rest Pose Rotations ---
        public Quaternion BindClavicleRotation;
        public Quaternion BindHumerusRotation;      // World space humerus bind
        public Quaternion BindLowerArmRotation;     // World space lower arm bind
        public Quaternion BindLowerArmLocalRotation;// Parent-local bind (Humerus -> Forearm)
        public Quaternion BindHandLocalRotation;    // Parent-local bind (Forearm -> Hand)
        public Quaternion BindHandRotation;         // World space hand bind

        public Vector3 BindHumerusDir;
        public Vector3 BindHumerusRefAxis;

        // --- Character & Environment Context ---
        public Quaternion ClavicleRotation;
        public Vector3 PlayerUp;
        public Vector3 TorsoUp;
        public Vector3 ElbowLateralOut;

        // --- Hint / Pole Vector Settings ---
        public bool HintWeight;
        public bool HintIsTracker;
        public Vector3 HintPosition;
        public Quaternion HintRotation;
        public float HintMaxStepDeg;

        // --- Tracker State Tracking ---
        public bool HasPrevPole;
        public Vector3 PrevPoleDir;
        public Quaternion PrevHintRotation;
        public int PrevGuardSide;

        // --- Constraints & Feature Toggles ---
        public bool ApplyWristAxialBound;
    }

    public struct BasisArmSolveResult
    {
        public Vector3 ElbowSolved;
        public Vector3 HandSolved;

        public Quaternion RootRotationSolved;
        public Quaternion MidRotationSolved;
        public Quaternion TipRotation;

        public Quaternion RootDelta;
        public Quaternion MidDelta;
        public Quaternion MidPostRoll;
        public Quaternion HintDelta;

        public bool HintApplied;
        public Vector3 PoleDirUsed;
        public Quaternion PoleRotUsed;
        public bool PoleAnchorValid;

        // Telemetry & Diagnostic Angles (in degrees)
        public float UpperLength;
        public float LowerLength;
        public float TargetDistance;
        public float ReachRatio;
        public float ElbowAngleDeg;
        public float WristTwistDeg;
        public float WristReliefDeg;
        public float HumeralTwistDeg;
        public float HumeralTwistGuardDeg;
        public float ForearmRollDemandDeg;
        public float ForearmRollDeg;
        public float WristAxialDeg;
        public float WristAxialGuardDeg;
        public float HandError;

        public float HintFade;
        public float HintProjMag;
        public float ArmProjMag;
        public float PoleConditioning;
        public byte AxisSource;
        public int GuardSideUsed;
    }
    public static class BasisArmSolveCore
    {
        private const float k_Epsilon = 1e-5f;
        private const float k_SqrEpsilon = 1e-10f;

        // --- Biomechanically Corrected Parameters ---
        public const float MinElbowAngleDeg = 0.0f;          // AAOS standard extension
        public const float MaxElbowAngleDeg = 180;//angle the elbow is allowed to get to, 180
        public const float TrackerPoleAnchorFrac = 0.05f;    // 5% offset along humerus vector
        public const float TrackerPoleTrustFrac = 0.25f;     // Damping factor for VR noise
        public const float WristRollRampStartDeg = 15.0f;    // Neutral comfort boundary (ISO 11226)
        public const float WristRollComfortDeg = 45.0f;      // Ergonomic functional threshold
        public const float WristRollMaxReliefDeg = 60.0f;     // Max anatomical strain compensation
        public const float TwistSwingFadeStartDeg = 60.0f;   // Start of non-linear muscle resistance
        public const float TwistSwingFadeEndDeg = 165.0f;     // Total anatomical arc endpoint
        public const float HumeralTwistSoftDeg = 30.0f;      // Shoulder damping threshold
        public const float HumeralTwistHardDeg = 170.0f;     // Clinical total shoulder rotation arc
        public const float TrackerForearmRollSoftDeg = 45.0f;
        public const float TrackerForearmRollMaxDeg = 165.0f;  // Combined pronation (80°) + supination (85°)
        public const float TrackerRollHandBlend = 0.5f;       // 50/50 radius-ulna twist distribution
        public const float k_WristWrapFadeStartDeg = 60.0f;
        public const float k_WristWrapFadeEndDeg = 165.0f;
        public const float WristAxialSoftDeg = 40.0f;
        public const float WristAxialHardDeg = 165.0f;
        /// <summary>
        /// Executes the full 2-bone arm analytic IK solver with pole vector, twist relief, and joint guards.
        /// </summary>
        public static void Solve(in BasisArmSolveInput i, out BasisArmSolveResult r)
        {
            r = default;
            r.MidPostRoll = Quaternion.identity;

            // 1. Initial Position Setup & Target Pose Transformation
            Vector3 aPos = i.Shoulder;
            Vector3 bPos = i.Elbow;
            Vector3 cPos = i.Hand;

            Quaternion rootRot = i.RootRotation;
            Quaternion midRot = i.MidRotation;
            Vector3 tPosition = i.TargetPosition;
            Quaternion tRotation = i.TargetRotation * i.TargetOffset;

            Vector3 ab = bPos - aPos;
            Vector3 bc = cPos - bPos;
            Vector3 ac = cPos - aPos;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float totalLen = abLen + bcLen;

            Vector3 atCorrected = tPosition - aPos;
            float acLen = ac.magnitude;
            float atCorrectedLen = atCorrected.magnitude;

            // 2. Solve Basic 2-Bone Trigonometry (Law of Cosines)
            float oldAbcAngle = TriangleAngle(acLen, abLen, bcLen);
            float newAbcAngle = TriangleAngle(atCorrectedLen, abLen, bcLen);
            newAbcAngle = Mathf.Clamp(newAbcAngle, MinElbowAngleDeg * Mathf.Deg2Rad, MaxElbowAngleDeg * Mathf.Deg2Rad);

            Vector3 bendAxis = ResolveBendAxis(ab, bc, ac, atCorrected, i, out byte axisSource);
            Quaternion deltaR = Quaternion.identity;

            if (bendAxis.sqrMagnitude > 0.5f)
            {
                float halfDelta = 0.5f * (oldAbcAngle - newAbcAngle);
                float sin = Mathf.Sin(halfDelta);
                float cos = Mathf.Cos(halfDelta);
                deltaR = new Quaternion(bendAxis.x * sin, bendAxis.y * sin, bendAxis.z * sin, cos);
            }

            // Apply elbow angle rotation
            midRot = deltaR * midRot;
            cPos = bPos + deltaR * (cPos - bPos);
            ac = cPos - aPos;

            // 3. Aim Root Toward Corrected Target
            Quaternion rootDelta = Quaternion.identity;
            if (atCorrectedLen > k_Epsilon)
            {
                rootDelta = BasisQuaternionExt.FromToRotation(ac, atCorrected);
                ApplyTransformDelta(rootDelta, aPos, ref rootRot, ref midRot, ref bPos, ref cPos);
                ac = cPos - aPos;
            }

            // 4. Pole Vector / Hint Resolution
            Quaternion hintR = Quaternion.identity;
            float hintProjMag = 0f;
            float armProjMag = 0f;
            float poleCondW = 1f;

            if (i.HintWeight && ac.sqrMagnitude > 0f)
            {
                float swivelUsed = SolveHintSwivel(
                    i, ref r, aPos, ref bPos, ref cPos, ref rootRot, ref midRot,
                    totalLen, out hintR, out hintProjMag, out armProjMag, out poleCondW
                );

                // Handle Low-Projection Collapse Stabilization for Non-Tracker Hints
                if (!i.HintIsTracker)
                {
                    ApplyHintCollapseStabilization(
                        i, swivelUsed, aPos, totalLen, hintProjMag,
                        ref bPos, ref cPos, ref rootRot, ref midRot, ref hintR
                    );
                }
            }

            // 5. Wrist Twist Relief Calculation
            if (IsValidRotation(i.TipRotation) && !i.HintIsTracker)
            {
                ApplyWristTwistRelief(i, ref r, aPos, ref bPos, ref cPos, ref rootRot, ref midRot, ref hintR, tRotation);
            }

            // 6. Anatomical Guard Swivel
            ApplyGuardSwivel(i, ref r, aPos, totalLen, ref bPos, ref cPos, ref rootRot, ref midRot, ref hintR);

            // 7. Humeral Twist Limits & Soft Guard
            ApplyHumeralTwistGuard(i, ref r, aPos, ref bPos, ref cPos, ref rootRot, ref midRot, ref tRotation, ref hintR);

            // 8. Forearm Roll / Pronation-Supination Solve
            if (i.HintIsTracker && IsValidRotation(i.HintRotation))
            {
                SolveTrackerForearmRoll(i, ref r, bPos, cPos, tRotation, ref midRot);
            }
            else if (!i.HintIsTracker && IsValidRotation(i.BindLowerArmRotation) && IsValidRotation(i.BindHumerusRotation))
            {
                SolveStandardForearmRoll(i, ref r, bPos, cPos, rootRot, tRotation, ref midRot);
            }

            // 9. Wrist Axial Limit Guards
            if (IsValidRotation(i.BindLowerArmRotation))
            {
                ApplyWristAxialGuard(i, ref r, bPos, cPos, midRot, ref tRotation);
            }

            // 10. Package Output Results
            r.MidDelta = deltaR;
            r.RootDelta = rootDelta;
            r.HintDelta = hintR;
            r.TipRotation = tRotation;
            r.HintApplied = i.HintWeight && (hintR != Quaternion.identity);

            r.ElbowSolved = bPos;
            r.HandSolved = cPos;
            r.RootRotationSolved = rootRot;
            r.MidRotationSolved = midRot;

            r.UpperLength = abLen;
            r.LowerLength = bcLen;
            r.TargetDistance = atCorrectedLen;
            r.ReachRatio = (totalLen > k_Epsilon) ? atCorrectedLen / totalLen : 0f;
            r.ElbowAngleDeg = AngleDeg(aPos - bPos, cPos - bPos);
            r.HintFade = i.HintWeight ? 1f : 0f;
            r.HintProjMag = hintProjMag;
            r.ArmProjMag = armProjMag;
            r.PoleConditioning = poleCondW;
            r.AxisSource = axisSource;
            r.HandError = (cPos - tPosition).magnitude;
        }

        #region Pipeline Sub-systems

        private static Vector3 ResolveBendAxis(Vector3 ab, Vector3 bc, Vector3 ac, Vector3 atCorrected, in BasisArmSolveInput i, out byte axisSource)
        {
            axisSource = 0;
            Vector3 axis = Vector3.Cross(ab, bc);

            if (axis.sqrMagnitude >= k_SqrEpsilon)
                return axis.normalized;

            Vector3 straightArm = ac.sqrMagnitude > k_SqrEpsilon ? ac : bc;
            if (straightArm.sqrMagnitude > k_SqrEpsilon)
            {
                Vector3 saN = straightArm.normalized;
                Vector3 downPole = -i.PlayerUp - saN * Vector3.Dot(-i.PlayerUp, saN);
                axis = Vector3.Cross(downPole, bc);
                axisSource = 4;
            }

            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                axis = i.HintWeight ? Vector3.Cross(i.HintPosition - i.Shoulder, bc) : Vector3.zero;
                axisSource = 1;
            }
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                axis = Vector3.Cross(atCorrected, bc);
                axisSource = 2;
            }
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                axis = i.PlayerUp;
                axisSource = 3;
            }

            return axis.normalized;
        }

        private static float SolveHintSwivel(
            in BasisArmSolveInput i, ref BasisArmSolveResult r, Vector3 aPos,
            ref Vector3 bPos, ref Vector3 cPos, ref Quaternion rootRot, ref Quaternion midRot,
            float totalLen, out Quaternion hintR, out float hintProjMag, out float armProjMag, out float poleCondW)
        {
            hintR = Quaternion.identity;
            Vector3 ac = cPos - aPos;
            Vector3 ab = bPos - aPos;
            Vector3 acNorm = ac.normalized;
            Vector3 ah = i.HintPosition - aPos;

            // FIXED: Use true projected elbow vector abProj directly as reference direction
            Vector3 abProj = ab - acNorm * Vector3.Dot(ab, acNorm);
            Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);
            Vector3 elbowDir = abProj;

            hintProjMag = ahProj.magnitude;
            armProjMag = abProj.magnitude;
            poleCondW = 1f;

            Vector3 anchorCarriedRaw = Vector3.zero;
            Vector3 anchorCarried = Vector3.zero;
            bool hasAnchorCarried = false;

            if (i.HintIsTracker && totalLen > k_Epsilon)
            {
                poleCondW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                    (hintProjMag / totalLen - TrackerPoleAnchorFrac) / (TrackerPoleTrustFrac - TrackerPoleAnchorFrac)));

                bool poleMeasurable = ahProj.sqrMagnitude > k_SqrEpsilon;
                if (i.HasPrevPole)
                {
                    Quaternion carryRot = Quaternion.identity;
                    if (IsValidRotation(i.PrevHintRotation) && IsValidRotation(i.HintRotation))
                    {
                        carryRot = i.HintRotation * Quaternion.Inverse(i.PrevHintRotation);
                    }

                    anchorCarriedRaw = carryRot * i.PrevPoleDir;
                    anchorCarried = anchorCarriedRaw - acNorm * Vector3.Dot(anchorCarriedRaw, acNorm);
                    hasAnchorCarried = anchorCarried.sqrMagnitude > k_SqrEpsilon;
                }

                if (poleMeasurable && (poleCondW >= 1f || !i.HasPrevPole))
                {
                    r.PoleDirUsed = ahProj / hintProjMag;
                    r.PoleRotUsed = i.HintRotation;
                    r.PoleAnchorValid = true;
                }
                else if (i.HasPrevPole)
                {
                    r.PoleDirUsed = i.PrevPoleDir;
                    r.PoleRotUsed = i.PrevHintRotation;
                    r.PoleAnchorValid = true;
                    if (poleMeasurable && poleCondW > 0f && hasAnchorCarried)
                    {
                        float ease = SignedAngleRad(anchorCarried, ahProj, acNorm) * poleCondW;
                        r.PoleDirUsed = (AngleAxisRad(ease, acNorm) * anchorCarriedRaw).normalized;
                        r.PoleRotUsed = i.HintRotation;
                    }
                }
            }

            if (ahProj.sqrMagnitude <= k_SqrEpsilon || elbowDir.sqrMagnitude <= k_SqrEpsilon)
                return 0f;

            float poleSwivel = SignedAngleRad(elbowDir, ahProj, acNorm);
            if (i.HintIsTracker && i.HasPrevPole && poleCondW < 1f && hasAnchorCarried)
            {
                float anchorSwivel = SignedAngleRad(elbowDir, anchorCarried, acNorm);
                float dSwivel = Mathf.DeltaAngle(anchorSwivel * Mathf.Rad2Deg, poleSwivel * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                poleSwivel = anchorSwivel + poleCondW * dSwivel;
            }

            float maxStep = i.HintMaxStepDeg * Mathf.Deg2Rad;
            float swivel = Mathf.Clamp(poleSwivel, -maxStep, maxStep);

            hintR = AngleAxisRad(swivel, acNorm);
            ApplyTransformDelta(hintR, aPos, ref rootRot, ref midRot, ref bPos, ref cPos);

            return swivel;
        }

        private static void ApplyHintCollapseStabilization(
            in BasisArmSolveInput i, float swivelUsedRad, Vector3 aPos, float totalLen, float hintProjMag,
            ref Vector3 bPos, ref Vector3 cPos, ref Quaternion rootRot, ref Quaternion midRot, ref Quaternion hintR)
        {
            float poleCond = totalLen > k_Epsilon ? hintProjMag / totalLen : 1f;
            float collapse = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((poleCond - 0.15f) / 0.15f));
            Vector3 acStab = cPos - aPos;

            if (collapse <= 0f || acStab.sqrMagnitude <= k_SqrEpsilon) return;

            Vector3 acStabN = acStab.normalized;
            Vector3 downPole = -i.PlayerUp - acStabN * Vector3.Dot(-i.PlayerUp, acStabN);
            Vector3 elbowPole = (bPos - aPos) - acStabN * Vector3.Dot(bPos - aPos, acStabN);

            if (downPole.sqrMagnitude > k_SqrEpsilon && elbowPole.sqrMagnitude > k_SqrEpsilon)
            {
                float stabSwivel = SignedAngleRad(elbowPole, downPole, acStabN) * collapse;
                float budget = Mathf.Max(0f, i.HintMaxStepDeg * Mathf.Deg2Rad - Mathf.Abs(swivelUsedRad));
                stabSwivel = Mathf.Clamp(stabSwivel, -budget, budget);

                Quaternion stab = AngleAxisRad(stabSwivel, acStabN);
                ApplyTransformDelta(stab, aPos, ref rootRot, ref midRot, ref bPos, ref cPos);
                hintR = stab * hintR;
            }
        }

        private static void ApplyWristTwistRelief(
            in BasisArmSolveInput i, ref BasisArmSolveResult r, Vector3 aPos,
            ref Vector3 bPos, ref Vector3 cPos, ref Quaternion rootRot, ref Quaternion midRot,
            ref Quaternion hintR, Quaternion tRotation)
        {
            // FIXED: Use bone's transformed longitudinal local Y axis instead of geometric segment
            Vector3 foreRollN = (midRot * Vector3.up).normalized;
            Vector3 acRelief = cPos - aPos;

            if (acRelief.sqrMagnitude <= k_SqrEpsilon) return;

            // FIXED: Correct quaternion multiplication order (world-space delta applied on left)
            Quaternion deltaMid = midRot * Quaternion.Inverse(i.MidRotation);
            Quaternion neutral = deltaMid * i.TipRotation;

            float twistRad = TwistAngleRad(tRotation * Quaternion.Inverse(neutral), foreRollN);
            r.WristTwistDeg = twistRad * Mathf.Rad2Deg;

            float rollAbs = Mathf.Abs(twistRad);
            float rampStart = WristRollRampStartDeg * Mathf.Deg2Rad;
            float band = WristRollComfortDeg * Mathf.Deg2Rad;
            float relief = 0f;

            if (rollAbs > rampStart && rollAbs <= band)
            {
                float t = rollAbs - rampStart;
                relief = (t * t) / (2f * (band - rampStart));
            }
            else if (rollAbs > band)
            {
                relief = 0.5f * (band - rampStart) + (rollAbs - band);
            }

            float reliefCap = WristRollMaxReliefDeg * Mathf.Deg2Rad;
            float seam = Mathf.PI - rollAbs;
            relief = Mathf.Clamp(relief, 0f, Mathf.Min(reliefCap, seam));

            if (relief > 0f)
            {
                float reliefSigned = twistRad < 0f ? -relief : relief;
                Quaternion reliefR = AngleAxisRad(reliefSigned, acRelief.normalized);

                ApplyTransformDelta(reliefR, aPos, ref rootRot, ref midRot, ref bPos, ref cPos);
                hintR = reliefR * hintR;
                r.WristReliefDeg = reliefSigned * Mathf.Rad2Deg;
            }
        }

        private static void ApplyGuardSwivel(
            in BasisArmSolveInput i, ref BasisArmSolveResult r, Vector3 aPos, float totalLen,
            ref Vector3 bPos, ref Vector3 cPos, ref Quaternion rootRot, ref Quaternion midRot, ref Quaternion hintR)
        {
            Vector3 guardUp = i.TorsoUp.sqrMagnitude > k_SqrEpsilon ? i.TorsoUp : i.PlayerUp;
            float guardSwivel = GuardSwivelRad(aPos, bPos, cPos, guardUp, totalLen, i.ElbowLateralOut, i.PrevGuardSide, out int guardSideUsed);
            r.GuardSideUsed = guardSideUsed;

            if (guardSwivel != 0f)
            {
                Vector3 acGuard = cPos - aPos;
                if (acGuard.sqrMagnitude > k_SqrEpsilon)
                {
                    Quaternion guard = AngleAxisRad(guardSwivel, acGuard.normalized);
                    ApplyTransformDelta(guard, aPos, ref rootRot, ref midRot, ref bPos, ref cPos);
                    hintR = guard * hintR;
                }
            }
        }

        private static void ApplyHumeralTwistGuard(
            in BasisArmSolveInput i, ref BasisArmSolveResult r, Vector3 aPos,
            ref Vector3 bPos, ref Vector3 cPos, ref Quaternion rootRot, ref Quaternion midRot,
            ref Quaternion tRotation, ref Quaternion hintR)
        {
            if (!IsValidRotation(i.BindClavicleRotation) || !IsValidRotation(i.ClavicleRotation) ||
                !IsValidRotation(i.BindHumerusRotation) || i.BindHumerusDir.sqrMagnitude <= k_SqrEpsilon ||
                i.BindHumerusRefAxis.sqrMagnitude <= k_SqrEpsilon) return;

            Vector3 liveDir = bPos - aPos;
            if (liveDir.sqrMagnitude <= k_SqrEpsilon) return;

            liveDir = liveDir.normalized;
            Quaternion carry = i.ClavicleRotation * Quaternion.Inverse(i.BindClavicleRotation);
            Quaternion restHumerusRot = carry * i.BindHumerusRotation;
            Vector3 restDir = (carry * i.BindHumerusDir).normalized;

            float swingDeg = AngleDeg(restDir, liveDir);
            float swingFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TwistSwingFadeStartDeg, TwistSwingFadeEndDeg, swingDeg));

            if (swingFade <= 0f) return;

            Quaternion swingMin = BasisQuaternionExt.FromToRotation(restDir, liveDir);
            Vector3 refPerp = (swingMin * restHumerusRot) * i.BindHumerusRefAxis;
            Vector3 livePerp = rootRot * i.BindHumerusRefAxis;

            refPerp -= liveDir * Vector3.Dot(refPerp, liveDir);
            livePerp -= liveDir * Vector3.Dot(livePerp, liveDir);

            if (refPerp.sqrMagnitude > k_SqrEpsilon && livePerp.sqrMagnitude > k_SqrEpsilon)
            {
                float twistRad = SignedAngleRad(refPerp, livePerp, liveDir);
                r.HumeralTwistDeg = twistRad * Mathf.Rad2Deg;

                float mag = Mathf.Abs(twistRad);
                if (mag > HumeralTwistSoftDeg * Mathf.Deg2Rad)
                {
                    float guarded = SaturateLimit(mag, HumeralTwistSoftDeg * Mathf.Deg2Rad, HumeralTwistHardDeg * Mathf.Deg2Rad);
                    float pull = Mathf.Clamp(mag - guarded, 0f, Mathf.PI - mag);

                    float need = (twistRad < 0f ? pull : -pull) * swingFade;
                    Quaternion twistR = AngleAxisRad(need, liveDir);

                    ApplyTransformDelta(twistR, aPos, ref rootRot, ref midRot, ref bPos, ref cPos);
                    tRotation = twistR * tRotation;
                    hintR = twistR * hintR;
                    r.HumeralTwistGuardDeg = need * Mathf.Rad2Deg;
                }
            }
        }

        private static void SolveTrackerForearmRoll(
            in BasisArmSolveInput i, ref BasisArmSolveResult r, Vector3 bPos, Vector3 cPos, Quaternion tRotation, ref Quaternion midRot)
        {
            // FIXED: Use local transform roll axis instead of position segment
            Vector3 foreRollN = (midRot * Vector3.up).normalized;

            float trackerRoll = TwistAngleRad(i.HintRotation * Quaternion.Inverse(midRot), foreRollN);
            float roll = trackerRoll;

            if (IsValidRotation(i.TipRotation))
            {
                Quaternion neutral = midRot * Quaternion.Inverse(i.MidRotation) * i.TipRotation;
                float handRoll = TwistAngleRad(tRotation * Quaternion.Inverse(neutral), foreRollN);
                r.WristTwistDeg = handRoll * Mathf.Rad2Deg;

                float d = Mathf.DeltaAngle(trackerRoll * Mathf.Rad2Deg, handRoll * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                    (Mathf.Abs(d) * Mathf.Rad2Deg - k_WristWrapFadeStartDeg) / (k_WristWrapFadeEndDeg - k_WristWrapFadeStartDeg)));
                roll = trackerRoll + TrackerRollHandBlend * d * fade;
            }

            r.ForearmRollDemandDeg = roll * Mathf.Rad2Deg;
            roll = Mathf.DeltaAngle(0f, roll * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            float rollAbs = Mathf.Abs(roll);
            float rollSat = SaturateLimit(rollAbs, TrackerForearmRollSoftDeg * Mathf.Deg2Rad, TrackerForearmRollMaxDeg * Mathf.Deg2Rad);
            float rollPull = Mathf.Clamp(rollAbs - rollSat, 0f, Mathf.PI - rollAbs);

            rollAbs -= rollPull;
            if (rollAbs > 1e-6f)
            {
                float rollSigned = roll < 0f ? -rollAbs : rollAbs;
                r.MidPostRoll = AngleAxisRad(rollSigned, foreRollN);
                midRot = r.MidPostRoll * midRot;
                r.ForearmRollDeg = rollSigned * Mathf.Rad2Deg;
            }
        }

        private static void SolveStandardForearmRoll(
            in BasisArmSolveInput i, ref BasisArmSolveResult r, Vector3 bPos, Vector3 cPos, Quaternion rootRot, Quaternion tRotation, ref Quaternion midRot)
        {
            // FIXED: Roll along local transform axis
            Vector3 foreRollN = (midRot * Vector3.up).normalized;

            // FIXED: Compute neutral forearm orientation cleanly using explicitly defined local bind quaternion
            Quaternion neutralFore = rootRot * i.BindLowerArmLocalRotation;

            float inherited = TwistAngleRad(midRot * Quaternion.Inverse(neutralFore), foreRollN);

            // FIXED: Default want to inherited so missing targets don't zero out and force a 45-90 deg error
            float want = inherited;

            if (IsValidRotation(i.TipRotation))
            {
                Quaternion neutralHand = neutralFore * i.BindHandLocalRotation;
                float demand = TwistAngleRad(tRotation * Quaternion.Inverse(neutralHand), foreRollN);
                float band = WristRollComfortDeg * Mathf.Deg2Rad;
                float mag = Mathf.Abs(demand);

                if (mag > band)
                {
                    float pull = Mathf.Clamp(mag - band, 0f, Mathf.PI - mag);
                    mag -= pull;
                }
                want = demand < 0f ? -mag : mag;
            }

            float roll = want - inherited;
            if (Mathf.Abs(roll) > 1e-6f && Mathf.Abs(roll) < 2f * Mathf.PI)
            {
                r.MidPostRoll = AngleAxisRad(roll, foreRollN);
                midRot = r.MidPostRoll * midRot;
                r.ForearmRollDeg = roll * Mathf.Rad2Deg;
            }
        }

        private static void ApplyWristAxialGuard(
            in BasisArmSolveInput i, ref BasisArmSolveResult r, Vector3 bPos, Vector3 cPos, Quaternion midRot, ref Quaternion tRotation)
        {
            Vector3 foreWristN = (midRot * Vector3.up).normalized;
            Quaternion neutralHand = IsValidRotation(i.BindHandRotation)
                ? midRot * (Quaternion.Inverse(i.BindLowerArmRotation) * i.BindHandRotation)
                : midRot;

            float wristRad = TwistAngleRad(tRotation * Quaternion.Inverse(neutralHand), foreWristN);
            r.WristAxialDeg = wristRad * Mathf.Rad2Deg;

            float mag = Mathf.Abs(wristRad);
            float bound = SaturateLimit(mag, WristAxialSoftDeg * Mathf.Deg2Rad, WristAxialHardDeg * Mathf.Deg2Rad);
            float pull = Mathf.Clamp(mag - bound, 0f, Mathf.PI - mag);

            if (i.ApplyWristAxialBound && pull > 1e-6f)
            {
                float need = wristRad < 0f ? pull : -pull;
                tRotation = AngleAxisRad(need, foreWristN) * tRotation;
                r.WristAxialGuardDeg = need * Mathf.Rad2Deg;
            }
        }

        #endregion

        #region Helper Utilities & Math Mechanics

        /// <summary>
        /// Performs robust swing-twist decomposition to extract the signed twist angle around a specified axis.
        /// </summary>
        public static float TwistAngleRad(Quaternion q, Vector3 twistAxis)
        {
            Vector3 r = new Vector3(q.x, q.y, q.z);
            if (r.sqrMagnitude < k_SqrEpsilon) return 0f;

            Vector3 p = Vector3.Project(r, twistAxis);
            Quaternion twist = new Quaternion(p.x, p.y, p.z, q.w);

            float sqrMag = twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w;
            if (sqrMag < k_SqrEpsilon) return 0f;

            float invMag = 1f / Mathf.Sqrt(sqrMag);
            twist = new Quaternion(twist.x * invMag, twist.y * invMag, twist.z * invMag, twist.w * invMag);

            float angle = 2f * Mathf.Atan2(Vector3.Dot(new Vector3(twist.x, twist.y, twist.z), twistAxis), twist.w);
            return Mathf.DeltaAngle(0f, angle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        }

        private static void ApplyTransformDelta(Quaternion delta, Vector3 pivot, ref Quaternion rootRot, ref Quaternion midRot, ref Vector3 bPos, ref Vector3 cPos)
        {
            rootRot = delta * rootRot;
            bPos = pivot + delta * (bPos - pivot);
            cPos = pivot + delta * (cPos - pivot);
            midRot = delta * midRot;
        }

        private static float TriangleAngle(float a, float b, float c)
        {
            if (b < k_Epsilon || c < k_Epsilon) return 0f;
            float cosVal = (b * b + c * c - a * a) / (2f * b * c);
            return Mathf.Acos(Mathf.Clamp(cosVal, -1f, 1f));
        }

        private static float SignedAngleRad(Vector3 from, Vector3 to, Vector3 axis) =>
            Vector3.SignedAngle(from, to, axis) * Mathf.Deg2Rad;

        private static float AngleDeg(Vector3 v1, Vector3 v2) =>
            Vector3.Angle(v1, v2);

        private static Quaternion AngleAxisRad(float angleRad, Vector3 axis) =>
            Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, axis);

        private static bool IsValidRotation(Quaternion q) =>
            (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) > 0.5f;

        private static float SaturateLimit(float val, float soft, float hard)
        {
            if (val <= soft) return val;
            if (val >= hard) return hard;
            float t = (val - soft) / (hard - soft);
            return soft + (hard - soft) * (1f - (1f - t) * (1f - t));
        }

        private static float GuardSwivelRad(Vector3 aPos, Vector3 bPos, Vector3 cPos, Vector3 up, float totalLen, Vector3 lateralOut, int prevSide, out int sideUsed)
        {
            sideUsed = prevSide;
            Vector3 ac = cPos - aPos;
            if (ac.sqrMagnitude < k_SqrEpsilon) return 0f;

            Vector3 acN = ac.normalized;
            Vector3 elbow = bPos - aPos;
            Vector3 elbowProj = elbow - acN * Vector3.Dot(elbow, acN);

            Vector3 upProj = up - acN * Vector3.Dot(up, acN);
            if (upProj.sqrMagnitude < k_SqrEpsilon || elbowProj.sqrMagnitude < k_SqrEpsilon) return 0f;

            float angle = SignedAngleRad(upProj.normalized, elbowProj.normalized, acN);
            return 0f; // Pass-through for custom anatomical guard boundaries
        }

        #endregion
    }
}
/*
 * ere's my take: To make a 2-bone arm solver behave like a true human arm, standard length and angle limits aren't enough. Human arms aren't mechanical links—they rely on joint coupling, muscle elasticity, soft-tissue deformation, and anatomical offsets.

Below is a breakdown of the critical parameters missing from your current float list, structured by the physiological systems they model, followed by how to integrate them into your C# code.

1. Primary Missing Parameters
A. Shoulder Complex (Scapulohumeral Rhythm & Offsets)
The shoulder isn't a fixed ball-and-socket; the clavicle and scapula elevate, retract, and rotate as the arm reaches overhead.

ScapularElevationWeight (float, range: 0.0–0.5): How much the shoulder joint moves upward as the reach ratio approaches 1.0.

ShoulderOffsetDegrees (float, typical: 15°–20°): The humerus does not sit straight out from the torso; it sits in the Glenoid Fossa at a forward tilt (scaption plane).

SternoclavicularPivotOffset (Vector3): The true mechanical pivot of the arm isn't the shoulder joint—it's near the breastbone.

B. Elbow Kinematics (Carrying Angle & Soft Tissue)
CarryingAngleDeg (float, range: 5°–15°): When the arm is fully extended, the forearm angles outward away from the body to keep the hips clear when walking.

HyperExtensionLimitDeg (float, typical: -5° to -15°): Many humans can extend their elbow beyond 0° (straight line).

SoftTissueCompressionDeg (float, typical: 135°–145°): Biceps collision prevents the elbow from reaching a absolute geometric 180° flexion without soft-tissue resistance ramping up.

C. Muscle Elasticity & Joint Resistance
Anatomy doesn't hit a hard limit and stop; it uses non-linear springs (tendons and ligaments).

JointStiffness (float, range: 0.0–1.0): Resistance against bending near extreme angles.

DampingCoefficient (float): Prevents high-frequency VR tracker jitter or mechanical snapping during fast movements.
 */
