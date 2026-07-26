using UnityEngine;
namespace Basis.IK
{
    public struct BasisArmSolveInput
    {
        public Vector3 Shoulder;
        public Vector3 Elbow;
        public Vector3 Hand;
        public Quaternion RootRotation;
        public Quaternion MidRotation;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public Vector3 HintPosition;
        public bool HintWeight;
        public Quaternion TargetOffset;
        public Vector3 PlayerUp;
        public Vector3 TorsoUp;
        public float HintMaxStepDeg;   // max elbow-swivel change this solve; float.MaxValue = unclamped (offline)
        public bool HintIsTracker;     // hint is a REAL elbow tracker (trust it further before the down-stabilizer overrides); false = lookup-derived
        public Quaternion TipRotation; // ANIMATED hand world rotation (pre-IK), like RootRotation/MidRotation. Zero (the default) disables wrist-roll relief.
        public Quaternion HintRotation;
        /// <summary>Last frame's well-conditioned pole DIRECTION (world, unit, perpendicular to the then
        /// shoulder->hand axis), from BasisArmSolveResult.PoleDirUsed.</summary>
        public Vector3 PrevPoleDir;
        public Quaternion PrevHintRotation;
        /// <summary>False (the struct default) restores the pre-anchor behaviour exactly, so every existing
        /// caller, test and offline sweep is bit-identical.</summary>
        public bool HasPrevPole;
        /// <summary>LIVE world rotation of the clavicle (shoulder) bone, read after SolveShoulder.</summary>
        public Quaternion ClavicleRotation;
        /// <summary>Bind/T-pose world rotation of that same clavicle bone.</summary>
        public Quaternion BindClavicleRotation;
        /// <summary>Bind/T-pose world rotation of the UPPER ARM bone.</summary>
        public Quaternion BindHumerusRotation;
        public Vector3 BindHumerusDir;
        public Vector3 BindHumerusRefAxis;
        public Quaternion BindHandRotation;
        public bool ApplyWristAxialBound;
        public Quaternion BindLowerArmRotation;
        public Vector3 ElbowLateralOut;
        public int PrevGuardSide;
    }

    public struct BasisArmSolveResult
    {
        public Quaternion MidDelta;
        public Quaternion RootDelta;
        public Quaternion HintDelta;
        public Quaternion MidPostRoll;
        public Quaternion TipRotation;
        public bool HintApplied;

        public Vector3 ElbowSolved;
        public Vector3 HandSolved;
        public Quaternion RootRotationSolved;
        public Quaternion MidRotationSolved;

        public float UpperLength;
        public float LowerLength;
        public float TargetDistance;
        public float ReachRatio;
        public float ElbowAngleDeg;
        public float HintFade;       // 0..1 tracker/hint influence actually used (0 at full extension = tracker ignored)
        public float HintProjMag;    // |hint projected onto swing plane|; small = unstable, tiny tracker error swings the elbow
        public float ArmProjMag;     // |elbow projected onto swing plane|; small = elbow near-straight (pole ill-defined)
        public byte AxisSource;     // 0 bend-plane, 1 hint, 2 shoulder->target, 3 playerUp
        public float HandError;
        public float WristTwistDeg;   // signed hand-target roll vs the carried animated wrist, about the forearm axis
        public float WristReliefDeg;  // signed swivel actually spent relieving it (0 = relief not engaged)
        public float ForearmRollDeg;  // signed forearm roll applied via MidPostRoll (0 = not engaged). Tracker
        public float ForearmRollDemandDeg;
        public Vector3 PoleDirUsed;   // pole direction to carry into next frame's PrevPoleDir
        public Quaternion PoleRotUsed;// HintRotation to carry into next frame's PrevHintRotation
        public bool PoleAnchorValid;  // false = nothing worth storing this frame
        public float PoleConditioning;// 1 = the measured pole's lever arm is healthy, 0 = fully anchored
        public float HumeralTwistDeg;      // signed humeral axial rotation vs the clavicle, before guarding
        public float HumeralTwistGuardDeg; // signed correction the twist guard applied (0 = inside the envelope)
        public float WristAxialDeg;
        public float WristAxialGuardDeg;
        public int GuardSideUsed;
    }
    public static class BasisArmSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;
        public const float MinElbowAngleDeg = 23f;
        public const float MaxElbowAngleDeg = 170f;
        public const float WristRollComfortDeg = 80f;
        public const float WristRollRampStartDeg = 55f;
        public const float WristRollMaxReliefDeg = 70f;
        public const float WristAxialSoftDeg = 5f;
        public const float WristAxialHardDeg = 45f;
        public const float TrackerRollHandBlend = 0.5f;
        public const float TrackerForearmRollMaxDeg = 170f;
        public const float TrackerForearmRollSoftDeg = 90f;
        const float k_WristWrapFadeStartDeg = 155f;
        const float k_WristWrapFadeEndDeg = 178f;
        public const float TrackerPoleAnchorFrac = 0.03f;
        public const float TrackerPoleTrustFrac = 0.12f;
        public const float HumeralTwistSoftDeg = 120f;
        public const float HumeralTwistHardDeg = 180f;
        public const float TwistSwingFadeStartDeg = 150f;
        public const float TwistSwingFadeEndDeg = 170f;
        public static void Solve(in BasisArmSolveInput i, out BasisArmSolveResult r)
        {
            r = default;
            r.MidPostRoll = Quaternion.identity;
            Vector3 aPosition = i.Shoulder;
            Vector3 bPosition = i.Elbow;
            Vector3 cPosition = i.Hand;
            Quaternion rootRot = i.RootRotation;
            Quaternion midRot = i.MidRotation;
            Vector3 tPosition = i.TargetPosition;
            Quaternion tRotation = i.TargetRotation * i.TargetOffset;
            Vector3 ab = bPosition - aPosition;
            Vector3 bc = cPosition - bPosition;
            Vector3 ac = cPosition - aPosition;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float totalLen = abLen + bcLen;

            Vector3 atCorrected = tPosition - aPosition;
            float acLen = ac.magnitude;

            float oldAbcAngle = TriangleAngle(acLen, abLen, bcLen);
            float atCorrectedLen = atCorrected.magnitude;
            float newAbcAngle = TriangleAngle(atCorrectedLen, abLen, bcLen);
            newAbcAngle = Mathf.Clamp(newAbcAngle, MinElbowAngleDeg * Mathf.Deg2Rad, MaxElbowAngleDeg * Mathf.Deg2Rad);
            byte axisSource = 0;
            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
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
                    axis = i.HintWeight ? Vector3.Cross(i.HintPosition - aPosition, bc) : Vector3.zero;
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
            }
            axis = axis.normalized;
            Quaternion deltaR = Quaternion.identity;
            if (axis.sqrMagnitude > 0.5f)   // normalized is either a unit vector or exactly zero
            {
                float a = 0.5f * (oldAbcAngle - newAbcAngle);
                float sin = Mathf.Sin(a);
                float cos = Mathf.Cos(a);
                deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);
            }

            // mid.SetRotation(deltaR * midRot): tip rotates about the elbow pivot.
            midRot = deltaR * midRot;
            cPosition = bPosition + deltaR * (cPosition - bPosition);
            ac = cPosition - aPosition;

            // --- rotate root toward the corrected target direction ---
            Quaternion rootDelta = Quaternion.identity;
            if (atCorrectedLen > k_Epsilon)
            {
                rootDelta = BasisQuaternionExt.FromToRotation(ac, atCorrected);
                rootRot = rootDelta * rootRot;
                // Propagate root rotation to its children (mid + tip), pivoting about A.
                bPosition = aPosition + rootDelta * (bPosition - aPosition);
                cPosition = aPosition + rootDelta * (cPosition - aPosition);
                midRot = rootDelta * midRot;
            }
            Quaternion hintR = Quaternion.identity;
            bool hintApplied = false;
            float hintFade = 0f;
            float swivelUsedRad = 0f;   // how much of the per-frame swivel budget the hint has already spent
            float hintProjMag = 0f;
            float armProjMag = 0f;
            float poleCondW = 1f;
            if (i.HintWeight)
            {
                float acSqrMag = ac.sqrMagnitude;
                if (acSqrMag > 0f)
                {
                    ab = bPosition - aPosition;
                    ac = cPosition - aPosition;

                    Vector3 acNorm = ac / Mathf.Sqrt(acSqrMag);
                    Vector3 ah = i.HintPosition - aPosition;
                    Vector3 abProj = ab - acNorm * Vector3.Dot(ab, acNorm);
                    Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);
                    Vector3 elbowDir = Vector3.Cross(acNorm, rootDelta * axis);
                    elbowDir -= acNorm * Vector3.Dot(elbowDir, acNorm);
                    hintProjMag = ahProj.magnitude;
                    armProjMag = abProj.magnitude;
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
                            float prevRotSqr = i.PrevHintRotation.x * i.PrevHintRotation.x + i.PrevHintRotation.y * i.PrevHintRotation.y
                                             + i.PrevHintRotation.z * i.PrevHintRotation.z + i.PrevHintRotation.w * i.PrevHintRotation.w;
                            float curRotSqr = i.HintRotation.x * i.HintRotation.x + i.HintRotation.y * i.HintRotation.y
                                            + i.HintRotation.z * i.HintRotation.z + i.HintRotation.w * i.HintRotation.w;
                            if (prevRotSqr > 0.5f && curRotSqr > 0.5f)
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
                    hintFade = 1f;
                    if (ahProj.sqrMagnitude > k_SqrEpsilon && elbowDir.sqrMagnitude > k_SqrEpsilon)
                    {
                        float effFade = hintFade;
                        float poleSwivel = SignedAngleRad(elbowDir, ahProj, acNorm);
                        if (i.HintIsTracker && i.HasPrevPole && poleCondW < 1f && hasAnchorCarried)
                        {
                            float anchorSwivel = SignedAngleRad(elbowDir, anchorCarried, acNorm);
                            float dSwivel = poleSwivel - anchorSwivel;
                            if (dSwivel > Mathf.PI) dSwivel -= 2f * Mathf.PI;
                            else if (dSwivel < -Mathf.PI) dSwivel += 2f * Mathf.PI;
                            poleSwivel = anchorSwivel + poleCondW * dSwivel;
                        }

                        swivelUsedRad = poleSwivel * effFade;
                        float swivel = swivelUsedRad;
                        float maxStep = i.HintMaxStepDeg * Mathf.Deg2Rad;
                        if (swivel > maxStep) swivel = maxStep;
                        else if (swivel < -maxStep) swivel = -maxStep;
                        swivelUsedRad = swivel;

                        hintR = AngleAxisRad(swivel, acNorm);

                        rootRot = hintR * rootRot;
                        bPosition = aPosition + hintR * (bPosition - aPosition);
                        cPosition = aPosition + hintR * (cPosition - aPosition);
                        midRot = hintR * midRot;
                        hintApplied = true;
                    }
                }
            }
            if (i.HintWeight && !i.HintIsTracker)
            {
                float poleCond = totalLen > k_Epsilon ? hintProjMag / totalLen : 1f;
                float collapse = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((poleCond - 0.15f) / 0.15f));
                Vector3 acStab = cPosition - aPosition;
                if (collapse > 0f && acStab.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 acStabN = acStab.normalized;
                    Vector3 downPole = -i.PlayerUp - acStabN * Vector3.Dot(-i.PlayerUp, acStabN);
                    Vector3 elbowPole = (bPosition - aPosition) - acStabN * Vector3.Dot(bPosition - aPosition, acStabN);
                    if (downPole.sqrMagnitude > k_SqrEpsilon && elbowPole.sqrMagnitude > k_SqrEpsilon)
                    {
                        float stabSwivel = SignedAngleRad(elbowPole, downPole, acStabN) * collapse;
                        float budget = i.HintMaxStepDeg * Mathf.Deg2Rad - Mathf.Abs(swivelUsedRad);
                        if (!(budget > 0f)) budget = 0f;   // NaN-safe
                        if (stabSwivel > budget) stabSwivel = budget;
                        else if (stabSwivel < -budget) stabSwivel = -budget;

                        Quaternion stab = AngleAxisRad(stabSwivel, acStabN);
                        rootRot = stab * rootRot;
                        bPosition = aPosition + stab * (bPosition - aPosition);
                        cPosition = aPosition + stab * (cPosition - aPosition);
                        midRot = stab * midRot;
                        hintR = stab * hintR; // fold into the hint delta the runtime applies
                    }
                }
            }
            float tipRotSqr = i.TipRotation.x * i.TipRotation.x + i.TipRotation.y * i.TipRotation.y + i.TipRotation.z * i.TipRotation.z + i.TipRotation.w * i.TipRotation.w;
            if (tipRotSqr > 0.5f && !i.HintIsTracker)
            {
                Vector3 fore = cPosition - bPosition;
                Vector3 acRelief = cPosition - aPosition;
                if (fore.sqrMagnitude > k_SqrEpsilon && acRelief.sqrMagnitude > k_SqrEpsilon)
                {
                    Quaternion neutral = midRot * Quaternion.Inverse(i.MidRotation) * i.TipRotation;
                    float twistRad = TwistAngleRad(tRotation * Quaternion.Inverse(neutral), fore.normalized);
                    r.WristTwistDeg = twistRad * Mathf.Rad2Deg;

                    float rollAbs = Mathf.Abs(twistRad);
                    float rampStart = WristRollRampStartDeg * Mathf.Deg2Rad;
                    float band = WristRollComfortDeg * Mathf.Deg2Rad;
                    float relief;
                    if (rollAbs <= rampStart)
                    {
                        relief = 0f;
                    }
                    else if (rollAbs <= band)
                    {
                        float t = rollAbs - rampStart;
                        relief = t * t / (2f * (band - rampStart));
                    }
                    else
                    {
                        relief = 0.5f * (band - rampStart) + (rollAbs - band);
                    }

                    float reliefCap = WristRollMaxReliefDeg * Mathf.Deg2Rad;
                    if (relief > reliefCap) relief = reliefCap;
                    float seam = Mathf.PI - rollAbs;
                    if (relief > seam) relief = seam;
                    if (!(relief > 0f)) relief = 0f;   // reject-unless-good: NaN lands here, not in a bone

                    if (relief > 0f)
                    {
                        float reliefSigned = twistRad < 0f ? -relief : relief;
                        Quaternion reliefR = AngleAxisRad(reliefSigned, acRelief.normalized);

                        rootRot = reliefR * rootRot;
                        bPosition = aPosition + reliefR * (bPosition - aPosition);
                        cPosition = aPosition + reliefR * (cPosition - aPosition);
                        midRot = reliefR * midRot;
                        hintR = reliefR * hintR;   // fold into the hint delta the runtime applies
                        r.WristReliefDeg = reliefSigned * Mathf.Rad2Deg;
                    }
                }
            }
            Vector3 guardUp = i.TorsoUp.sqrMagnitude > k_SqrEpsilon ? i.TorsoUp : i.PlayerUp;
            float guardSwivel = BasisElbowAnatomyCore.GuardSwivelRad(aPosition, bPosition, cPosition, guardUp, totalLen, i.ElbowLateralOut, i.PrevGuardSide, out int guardSideUsed);
            r.GuardSideUsed = guardSideUsed;
            if (guardSwivel != 0f)
            {
                Vector3 acGuard = cPosition - aPosition;
                if (acGuard.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 acGuardN = acGuard.normalized;
                    Quaternion guard = AngleAxisRad(guardSwivel, acGuardN);

                    rootRot = guard * rootRot;
                    bPosition = aPosition + guard * (bPosition - aPosition);
                    cPosition = aPosition + guard * (cPosition - aPosition);
                    midRot = guard * midRot;
                    hintR = guard * hintR;   // fold into the hint delta the runtime applies
                }
            }
            Quaternion humeralTwistUndo = Quaternion.identity;

            float bindClavSqr = i.BindClavicleRotation.x * i.BindClavicleRotation.x + i.BindClavicleRotation.y * i.BindClavicleRotation.y + i.BindClavicleRotation.z * i.BindClavicleRotation.z + i.BindClavicleRotation.w * i.BindClavicleRotation.w;
            float clavSqr = i.ClavicleRotation.x * i.ClavicleRotation.x + i.ClavicleRotation.y * i.ClavicleRotation.y+ i.ClavicleRotation.z * i.ClavicleRotation.z + i.ClavicleRotation.w * i.ClavicleRotation.w;
            float bindHumSqr = i.BindHumerusRotation.x * i.BindHumerusRotation.x + i.BindHumerusRotation.y * i.BindHumerusRotation.y + i.BindHumerusRotation.z * i.BindHumerusRotation.z + i.BindHumerusRotation.w * i.BindHumerusRotation.w;
            if (bindClavSqr > 0.5f && clavSqr > 0.5f && bindHumSqr > 0.5f  && i.BindHumerusDir.sqrMagnitude > k_SqrEpsilon  && i.BindHumerusRefAxis.sqrMagnitude > k_SqrEpsilon)
            {
                Vector3 liveDir = bPosition - aPosition;
                if (liveDir.sqrMagnitude > k_SqrEpsilon)
                {
                    liveDir = liveDir.normalized;
                    Quaternion carry = i.ClavicleRotation * Quaternion.Inverse(i.BindClavicleRotation);
                    Quaternion restHumerusRot = carry * i.BindHumerusRotation;
                    Vector3 restDir = (carry * i.BindHumerusDir).normalized;

                    float swingDeg = AngleDeg(restDir, liveDir);
                    float swingFade = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(TwistSwingFadeStartDeg, TwistSwingFadeEndDeg, swingDeg));
                    if (swingFade > 0f)
                    {
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
                                float guarded = BasisJointLimitCore.Saturate(mag,
                                    HumeralTwistSoftDeg * Mathf.Deg2Rad,
                                    HumeralTwistHardDeg * Mathf.Deg2Rad);
                                float pull = mag - guarded;
                                float seam = Mathf.PI - mag;
                                if (pull > seam)
                                {
                                    pull = seam;
                                }

                                if (!(pull > 0f))
                                {
                                    pull = 0f;   // reject-unless-good: NaN lands here, not in a bone
                                }

                                float need = (twistRad < 0f ? pull : -pull) * swingFade;
                                Quaternion twistR = AngleAxisRad(need, liveDir);
                                // Propagate the shoulder rotation to the solved arm.
                                bPosition = aPosition + twistR * (bPosition - aPosition);
                                cPosition = aPosition + twistR * (cPosition - aPosition);
                                midRot = twistR * midRot;
                                hintR = twistR * hintR;
                                humeralTwistUndo = Quaternion.Inverse(twistR);

                                r.HumeralTwistGuardDeg = need * Mathf.Rad2Deg;
                            }
                        }
                    }
                }
            }

            float hintRotSqr = i.HintRotation.x * i.HintRotation.x + i.HintRotation.y * i.HintRotation.y
                             + i.HintRotation.z * i.HintRotation.z + i.HintRotation.w * i.HintRotation.w;
            if (i.HintIsTracker && hintRotSqr > 0.5f)
            {
                Vector3 foreRoll = cPosition - bPosition;
                if (foreRoll.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 foreRollN = foreRoll.normalized;
                    float trackerRoll = TwistAngleRad(i.HintRotation * Quaternion.Inverse(midRot), foreRollN);

                    float roll = trackerRoll;
                    if (tipRotSqr > 0.5f)
                    {
                        Quaternion neutral = midRot * Quaternion.Inverse(i.MidRotation) * i.TipRotation;
                        float handRoll = TwistAngleRad(tRotation * Quaternion.Inverse(neutral), foreRollN);
                        r.WristTwistDeg = handRoll * Mathf.Rad2Deg;
                        float d = handRoll - trackerRoll;
                        if (d > Mathf.PI) d -= 2f * Mathf.PI;
                        else if (d < -Mathf.PI) d += 2f * Mathf.PI;
                        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                            (Mathf.Abs(d) * Mathf.Rad2Deg - k_WristWrapFadeStartDeg) / (k_WristWrapFadeEndDeg - k_WristWrapFadeStartDeg)));
                        roll = trackerRoll + TrackerRollHandBlend * d * fade;
                    }
                    r.ForearmRollDemandDeg = roll * Mathf.Rad2Deg;   // pre-wrap, pre-bound: see the field's note

                    if (roll > Mathf.PI) roll -= 2f * Mathf.PI;
                    else if (roll < -Mathf.PI) roll += 2f * Mathf.PI;

                    // Bound, then apply -- in the reject-unless-good shape, because Mathf.Clamp waves NaN
                    // through and a NaN written to a bone persists.
                    float rollAbs = Mathf.Abs(roll);
                    float rollSat = BasisJointLimitCore.Saturate(
                        rollAbs,
                        TrackerForearmRollSoftDeg * Mathf.Deg2Rad,
                        TrackerForearmRollMaxDeg * Mathf.Deg2Rad);
                    float rollPull = rollAbs - rollSat;
                    float rollSeam = Mathf.PI - rollAbs;
                    if (rollPull > rollSeam) rollPull = rollSeam;
                    if (!(rollPull > 0f)) rollPull = 0f;   // reject-unless-good: NaN lands here, not in a bone
                    rollAbs -= rollPull;
                    if (rollAbs > 1e-6f)
                    {
                        float rollSigned = roll < 0f ? -rollAbs : rollAbs;
                        r.MidPostRoll = AngleAxisRad(rollSigned, foreRollN);
                        midRot = r.MidPostRoll * midRot;
                        r.ForearmRollDeg = rollSigned * Mathf.Rad2Deg;
                    }
                }
            }

            float bindLowerSqr = i.BindLowerArmRotation.x * i.BindLowerArmRotation.x + i.BindLowerArmRotation.y * i.BindLowerArmRotation.y
                               + i.BindLowerArmRotation.z * i.BindLowerArmRotation.z + i.BindLowerArmRotation.w * i.BindLowerArmRotation.w;
            if (!i.HintIsTracker && bindLowerSqr > 0.5f && bindHumSqr > 0.5f)
            {
                Vector3 foreRoll = cPosition - bPosition;
                if (foreRoll.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 foreRollN = foreRoll.normalized;

                    // The forearm at ZERO pronation: the bind forearm-vs-humerus relation on the solved humerus.
                    Quaternion neutralFore = rootRot * (Quaternion.Inverse(i.BindHumerusRotation) * i.BindLowerArmRotation);

                    // What the animation left in the forearm, about its own long axis. This is the arbitrary part.
                    float inherited = TwistAngleRad(midRot * Quaternion.Inverse(neutralFore), foreRollN);

                    float want = 0f;   // no hand feed: pronation is simply defined to be zero
                    if (tipRotSqr > 0.5f)
                    {
                        float demand = TwistAngleRad(tRotation * Quaternion.Inverse(neutralFore), foreRollN);
                        float band = WristRollComfortDeg * Mathf.Deg2Rad;

                        float mag = demand < 0f ? -demand : demand;
                        if (mag > band)
                        {
                            float pull = mag - band;
                            float seam = Mathf.PI - mag;
                            if (pull > seam) pull = seam;
                            if (!(pull > 0f)) pull = 0f;   // reject-unless-good: NaN lands here, not in a bone
                            mag -= pull;
                        }
                        want = demand < 0f ? -mag : mag;
                    }

                    float roll = want - inherited;
                    if (Mathf.Abs(roll) > 1e-6f && roll > -2f * Mathf.PI && roll < 2f * Mathf.PI)
                    {
                        r.MidPostRoll = AngleAxisRad(roll, foreRollN);
                        midRot = r.MidPostRoll * midRot;
                        r.ForearmRollDeg = roll * Mathf.Rad2Deg;
                    }
                }
            }

            r.MidPostRoll = r.MidPostRoll * humeralTwistUndo;

            if (bindLowerSqr > 0.5f)
            {
                Vector3 foreWrist = cPosition - bPosition;
                if (foreWrist.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 foreWristN = foreWrist.normalized;

                    float bindHandSqr = i.BindHandRotation.x * i.BindHandRotation.x + i.BindHandRotation.y * i.BindHandRotation.y
                                      + i.BindHandRotation.z * i.BindHandRotation.z + i.BindHandRotation.w * i.BindHandRotation.w;
                    Quaternion neutralHand = bindHandSqr > 0.5f
                        ? midRot * (Quaternion.Inverse(i.BindLowerArmRotation) * i.BindHandRotation)
                        : midRot;

                    float wristRad = TwistAngleRad(tRotation * Quaternion.Inverse(neutralHand), foreWristN);
                    r.WristAxialDeg = wristRad * Mathf.Rad2Deg;

                    float mag = wristRad < 0f ? -wristRad : wristRad;
                    float bound = BasisJointLimitCore.Saturate(mag,
                        WristAxialSoftDeg * Mathf.Deg2Rad,
                        WristAxialHardDeg * Mathf.Deg2Rad);
                    float seam = Mathf.PI - mag;
                    if (bound > seam) bound = seam;
                    if (!(bound > 0f)) bound = 0f;   // reject-unless-good: NaN lands here, not in a bone

                    float pull = mag - bound;
                    if (i.ApplyWristAxialBound && pull > 1e-6f)
                    {
                        float need = wristRad < 0f ? pull : -pull;
                        tRotation = AngleAxisRad(need, foreWristN) * tRotation;
                        r.WristAxialGuardDeg = need * Mathf.Rad2Deg;
                    }
                }
            }

            r.MidDelta = deltaR;
            r.RootDelta = rootDelta;
            r.HintDelta = hintR;
            r.TipRotation = tRotation;
            r.HintApplied = hintApplied;

            r.ElbowSolved = bPosition;
            r.HandSolved = cPosition;
            r.RootRotationSolved = rootRot;
            r.MidRotationSolved = midRot;

            r.UpperLength = abLen;
            r.LowerLength = bcLen;
            r.TargetDistance = atCorrectedLen;
            r.ReachRatio = (totalLen > k_Epsilon) ? atCorrectedLen / totalLen : 0f;
            r.ElbowAngleDeg = AngleDeg(aPosition - bPosition, cPosition - bPosition);
            r.HintFade = hintFade;
            r.HintProjMag = hintProjMag;
            r.ArmProjMag = armProjMag;
            r.PoleConditioning = poleCondW;
            r.AxisSource = axisSource;
            r.HandError = (cPosition - tPosition).magnitude;
        }

        static float SignedAngleRad(Vector3 from, Vector3 to, Vector3 axis)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (!(denom > k_Epsilon))
            {
                return 0f;
            }

            float c = Vector3.Dot(from, to) / denom;
            c = c > 1f ? 1f : (c > -1f ? c : -1f);   // Mathf.Clamp does NOT clamp NaN; this shape sends it to -1
            float angle = Mathf.Acos(c);
            return Vector3.Dot(axis, Vector3.Cross(from, to)) < 0f ? -angle : angle;
        }

        static Quaternion AngleAxisRad(float radians, Vector3 axis)
        {
            float h = 0.5f * radians;
            float s = Mathf.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(h));
        }

        static float TwistAngleRad(Quaternion q, Vector3 axis)
        {
            float s = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float c = q.w;
            if (c < 0f) { s = -s; c = -c; }
            if (!(s * s + c * c > k_SqrEpsilon))
            {
                return 0f;
            }
            return 2f * Mathf.Atan2(s, c);
        }

        static float AngleDeg(Vector3 from, Vector3 to)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (denom < k_Epsilon)
            {
                return 0f;
            }

            float c = Mathf.Clamp(Vector3.Dot(from, to) / denom, -1f, 1f);
            return Mathf.Acos(c) * Mathf.Rad2Deg;
        }

        static float TriangleAngle(float aLen, float aLen1, float aLen2)
        {
            if (aLen1 <= k_Epsilon || aLen2 <= k_Epsilon)
            {
                return 0f;
            }

            float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (2.0f * aLen1 * aLen2), -1.0f, 1.0f);
            return Mathf.Acos(c);
        }
    }
}
