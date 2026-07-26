using Basis.IK;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
[BurstCompile]
public struct BasisEerieArms
{
    const float threshold = 30f;
    const float maxCounter = 15f;
    const float fraction = 0.4f;
    public const float k_ShoulderCoupleRatio = 0.4f;
    public const float k_ShoulderMaxDeg = 25f;
    public const float MaxGain = 5f;
    public const float ReachGain = 3f;
    public const float ReachTrustLo = 0.06f;
    public const float ReachTrustHi = 0.10f;
    public static void SolveArms(ref BasisEerieMovement Self, BasisPoseStream stream)
    {
        //2) Shoulder pre-solve: elevate/protract based on hand targets before arm IK
        if (Self.shoulderSolveEnabled)
        {
            BasisEerieArms.SolveShoulder(ref Self, stream, Self.HandleLeftShoulder, Self.enabledLeftShoulder, Self.targetPositionLeftHand, Self.hintPositionLeftHand, Self.hintWeightLeftHand, Self.TposeLeftShoulderLocalDir, Self.TposeLeftShoulderRot, Self.TposeChestRot, Self.TposeShoulderToHandLeft, Self.TposeClavicleLenLeft, Self.TposeShoulderToElbowLeft, true);
            BasisEerieArms.SolveShoulder(ref Self, stream, Self.HandleRightShoulder, Self.enabledRightShoulder, Self.targetPositionRightHand, Self.hintPositionRightHand, Self.hintWeightRightHand, Self.TposeRightShoulderLocalDir, Self.TposeRightShoulderRot, Self.TposeChestRot, Self.TposeShoulderToHandRight, Self.TposeClavicleLenRight, Self.TposeShoulderToElbowRight, false);
        }
        else
        {
            BasisEerieMovement.ApplyRotation(stream, Self.enabledLeftShoulder, Self.HandleLeftShoulder, Self.TargetRotationLeftShoulder, Self.targetOffsetLeftShoulder);
            BasisEerieMovement.ApplyRotation(stream, Self.enabledRightShoulder, Self.HandleRightShoulder, Self.TargetRotationRightShoulder, Self.targetOffsetRightShoulder);
        }
        if (Self.anatShoulderSlide)
        {
            BasisEerieArms.ApplyShoulderSlide(ref Self, stream);
        }

        // 4) Hands: two-bone IK with collision + elbow protection. bodyRight (shoulder->shoulder) orients
        // the torso's elliptical collision cross-section; shared by both arms so it is computed once here.
        Vector3 bodyRight = (Self.HandleLeftUpperArm.IsValid(stream) && Self.HandleRightUpperArm.IsValid(stream)) ? Self.HandleRightUpperArm.GetPosition(stream) - Self.HandleLeftUpperArm.GetPosition(stream) : Vector3.zero;
        BasisEerieArms.SolveHand(ref Self, stream, Self.enabledLeftHand, Self.HandleLeftUpperArm, Self.HandleLeftLowerArm, Self.HandleLeftHand, Self.targetPositionLeftHand, Self.targetRotationLeftHand, Self.hintPositionLeftHand, Self.hintRotationLeftHand, Self.hintWeightLeftHand, Self.targetOffsetLeftHand, Self.HandleChest, Self.HandleNeck, Self.chestRadius, Self.collisionSkin, Self.collisionsEnabled, Self.handRadius, Self.handSkin, Self.protectElbow, Self.collideTrackedElbow, bodyRight, BasisEerieMovement.k_SwingLeftElbow);
        BasisEerieArms.SolveHand(ref Self, stream, Self.enabledRightHand, Self.HandleRightUpperArm, Self.HandleRightLowerArm, Self.HandleRightHand, Self.targetPositionRightHand, Self.targetRotationRightHand, Self.hintPositionRightHand, Self.hintRotationRightHand, Self.hintWeightRightHand, Self.targetOffsetRightHand, Self.HandleChest, Self.HandleNeck, Self.chestRadius, Self.collisionSkin, Self.collisionsEnabled, Self.handRadius, Self.handSkin, Self.protectElbow, Self.collideTrackedElbow, bodyRight, BasisEerieMovement.k_SwingRightElbow);

        // Arm pop continuity: rate-limit the elbow swing so a torso-collision change eases in
        // instead of popping in one frame. Runs before arm twist (which reads the arm pose).
        float swingRate = Self.swingSmoothRateDeg;
        float swingDt = stream.deltaTime;
        if (Self.enabledLeftHand > 0f)
        {
            BasisEerieArms.ApplySwingContinuity(ref Self, stream, BasisEerieMovement.k_SwingLeftElbow, Self.HandleLeftUpperArm, Self.HandleLeftLowerArm, Self.HandleLeftHand, Self.targetPositionLeftHand, swingRate, swingDt, bodyRight);
        }

        if (Self.enabledRightHand > 0f)
        {
            BasisEerieArms.ApplySwingContinuity(ref Self, stream, BasisEerieMovement.k_SwingRightElbow, Self.HandleRightUpperArm, Self.HandleRightLowerArm, Self.HandleRightHand, Self.targetPositionRightHand, swingRate, swingDt, bodyRight);
        }

        // 4b) Arm twist distribution: spread wrist/elbow roll along the optional twist bones
        // so the mesh doesn't pinch at the wrist when the hand rotates.
        float lowerTwist = Self.lowerArmTwistFraction;
        float upperTwist = Self.upperArmTwistFraction;
        BasisEerieArms.SolveArmTwist(stream, Self.HandleLeftLowerArm, Self.HandleLeftHand, Self.HandleLeftLowerArmTwist, lowerTwist);
        BasisEerieArms.SolveArmTwist(stream, Self.HandleRightLowerArm, Self.HandleRightHand, Self.HandleRightLowerArmTwist, lowerTwist);
        BasisEerieArms.SolveArmTwist(stream, Self.HandleLeftUpperArm, Self.HandleLeftLowerArm, Self.HandleLeftUpperArmTwist, upperTwist);
        BasisEerieArms.SolveArmTwist(stream, Self.HandleRightUpperArm, Self.HandleRightLowerArm, Self.HandleRightUpperArmTwist, upperTwist);
    }

    public static void SolveHand(ref BasisEerieMovement self, BasisPoseStream stream, float enabledProp, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPosProp, Quaternion targetRotProp, Vector3 hintPosProp, Quaternion hintRotProp, bool hintWeightProp, Quaternion targetOffset, BasisBoneHandle chestStart, BasisBoneHandle chestEnd, float chestRadius, float collisionSkin, bool collisionsEnabled, float handRadius, float handSkin, bool protectElbow, bool collideTrackedElbow, Vector3 bodyRight, int swingSlot)
    {
        float weight = enabledProp;
        if (!(weight > 0f) || !(root.IsValid(stream) && mid.IsValid(stream) && tip.IsValid(stream)))
        {
            return;
        }

        Quaternion origRootRot = root.GetRotation(stream);
        Quaternion origMidRot = mid.GetRotation(stream);
        Quaternion origTipRot = tip.GetRotation(stream);

        // Read inputs
        Vector3 tgtPos = targetPosProp;
        Quaternion tgtRot = targetRotProp;
        Vector3 hintPos = hintPosProp;
        Quaternion hintRot = hintRotProp;

        var target = new BasisAffineTransform(tgtPos, tgtRot);
        var hint = new BasisAffineTransform(hintPos, hintRot);
        bool hintWeight = hintWeightProp;
        bool usedModel = false;

        if (!hintWeight)
        {
            BasisSwivelFrame frame = self.BuildArmFrame(stream);

            Vector3 shoulderPos = root.GetPosition(stream);
            float upperLen = (mid.GetPosition(stream) - shoulderPos).magnitude;
            float lowerLen = (tip.GetPosition(stream) - mid.GetPosition(stream)).magnitude;
            float armLen = upperLen + lowerLen;
            bool isLeft = swingSlot == BasisEerieMovement.k_SwingLeftElbow;
            if (BasisSwivelHintCore.ArmHint(frame, shoulderPos, tgtPos, armLen, isLeft, out Vector3 modelHint, out float poleConditioning))
            {
                Vector3 curAxisV = tgtPos - shoulderPos;
                Vector3 rawBendV = modelHint - shoulderPos;
                float axLen = curAxisV.magnitude;
                float rbLen = rawBendV.magnitude;
                if (axLen > 1e-5f && rbLen > 1e-5f)
                {
                    Vector3 curAxis = curAxisV / axLen;
                    Vector3 rawBend = rawBendV / rbLen;
                    bool seeded = self.swingHintInit[swingSlot] != 0;
                    float curReach = axLen / armLen;
                    Vector3 cappedBend = seeded ? (Vector3)Apply(self.swingHintBend[swingSlot], self.swingHintAxis[swingSlot], curAxis, rawBend, MaxGain, curReach - self.swingHintReach[swingSlot], poleConditioning) : rawBend;
                    self.swingHintBend[swingSlot] = cappedBend;
                    self.swingHintAxis[swingSlot] = curAxis;
                    self.swingHintReach[swingSlot] = curReach;

                    Quaternion bodyRot = frame.Valid
                        ? Quaternion.LookRotation(frame.Forward, frame.Up)
                        : self.HandleHips.IsValid(stream) ? self.HandleHips.GetRotation(stream) : Quaternion.identity;

                    Vector3 outBend = cappedBend;
                    if (self.elbowDragEnabled && seeded)
                    {
                        Quaternion bodyDelta = bodyRot * Quaternion.Inverse(self.swingHintBodyRot[swingSlot]);
                        outBend = (Vector3)BasisElbowDragCore.Apply(self.swingHintDrag[swingSlot], bodyDelta, curAxis, cappedBend, BasisElbowDragCore.Alpha(self.elbowDragHz, stream.deltaTime));
                    }
                    self.swingHintDrag[swingSlot] = outBend;
                    self.swingHintBodyRot[swingSlot] = bodyRot;
                    self.swingHintInit[swingSlot] = 1;
                    modelHint = shoulderPos + 0.5f * armLen * outBend;
                }

                hint = new BasisAffineTransform(modelHint, hintRot);
                hintWeight = true;
                usedModel = true;
            }
        }
        if (!usedModel)
        {
            self.swingHintInit[swingSlot] = 0;
        }
        bool hintIsTracker = hintWeight && !usedModel;
        // Geometry lives in BasisArmSolveCore so the offline sweep harness solves the
        // exact same elbow math. The core returns incremental deltas; apply them through
        // the stream in the original order (identity steps are exact no-ops).
        BasisArmSolveInput input = default;

        root.GetPositionAndRotation(stream, out Vector3 ReadshoulderPos, out Quaternion shoulderRot);
        mid.GetPositionAndRotation(stream, out Vector3 elbowPos, out Quaternion elbowRot);
        tip.GetPositionAndRotation(stream, out Vector3 handPos, out Quaternion handRot);

        input.Shoulder = ReadshoulderPos;
        input.Elbow = elbowPos;
        input.Hand = handPos;
        input.RootRotation = shoulderRot;
        input.MidRotation = elbowRot;
        input.TargetPosition = target.translation;
        input.TargetRotation = target.rotation;
        input.HintPosition = hint.translation;
        input.HintWeight = hintWeight;
        input.TargetOffset = targetOffset;
        input.PlayerUp = self.playerUp;
        // The anatomy guard's ceiling is TORSO-relative (see BasisElbowAnatomyCore's frame note), so it
        // needs the chest->neck up, not the root's. Same BuildFrame the elbow model already runs on --
        // the house body frame, from bone POSITIONS -- so the guard and the hint cannot disagree about
        // which way is up. Left at zero on a degenerate rig; BasisArmSolveCore then falls back to PlayerUp.
        BasisSwivelFrame torsoFrame = self.BuildArmFrame(stream);
        if (torsoFrame.Valid)
        {
            input.TorsoUp = torsoFrame.Up;
        }
        input.HintIsTracker = hintIsTracker;
        input.HintMaxStepDeg = float.MaxValue;
        input.TipRotation = handRot;
        input.HintRotation = hintIsTracker ? hint.rotation : default;
        if (swingSlot == BasisEerieMovement.k_SwingLeftElbow || swingSlot == BasisEerieMovement.k_SwingRightElbow)
        {
            bool twistIsLeft = swingSlot == BasisEerieMovement.k_SwingLeftElbow;
            // Lateral OUT seeds the cold start; the previous frame's side is what actually kills the buzz.
            input.ElbowLateralOut = twistIsLeft ? -bodyRight : bodyRight;
            if (self.swingGuardSide.IsCreated) input.PrevGuardSide = self.swingGuardSide[swingSlot];
            input.BindLowerArmRotation = twistIsLeft ? self.TposeLeftLowerArmRot : self.TposeRightLowerArmRot;
            input.BindHandRotation = twistIsLeft ? self.TposeLeftHandRot : self.TposeRightHandRot;
            input.ApplyWristAxialBound = self.wristAxialBound;
            BasisBoneHandle clavicle = twistIsLeft ? self.HandleLeftShoulder : self.HandleRightShoulder;
            if (clavicle.IsValid(stream))
            {
                input.ClavicleRotation = clavicle.GetRotation(stream);
                input.BindClavicleRotation = twistIsLeft ? self.TposeLeftShoulderRot : self.TposeRightShoulderRot;
                input.BindHumerusRotation = twistIsLeft ? self.TposeLeftUpperArmRot : self.TposeRightUpperArmRot;
                input.BindHumerusDir = twistIsLeft ? self.TposeLeftHumerusDir : self.TposeRightHumerusDir;
                input.BindHumerusRefAxis = twistIsLeft ? self.TposeLeftHumerusRefAxis : self.TposeRightHumerusRefAxis;
            }
        }

        bool anchorSlot = hintIsTracker && (uint)swingSlot < (uint)BasisEerieMovement.k_SwingCount
                          && self.swingPoleAnchor.IsCreated && self.swingPoleAnchorRot.IsCreated && self.swingPoleAnchorInit.IsCreated;
        if (anchorSlot && self.swingPoleAnchorInit[swingSlot] != 0)
        {
            input.PrevPoleDir = self.swingPoleAnchor[swingSlot];
            input.PrevHintRotation = self.swingPoleAnchorRot[swingSlot];
            input.HasPrevPole = true;
        }

        BasisArmSolveCore.Solve(input, out BasisArmSolveResult result);

        if (self.swingGuardSide.IsCreated && (uint)swingSlot < (uint)BasisEerieMovement.k_SwingCount)
        {
            self.swingGuardSide[swingSlot] = result.GuardSideUsed;
        }

        if (anchorSlot)
        {
            if (result.PoleAnchorValid)
            {
                self.swingPoleAnchor[swingSlot] = result.PoleDirUsed;
                self.swingPoleAnchorRot[swingSlot] = result.PoleRotUsed;
                self.swingPoleAnchorInit[swingSlot] = 1;
            }
        }
        else if ((uint)swingSlot < (uint)BasisEerieMovement.k_SwingCount && self.swingPoleAnchorInit.IsCreated)
        {
            self.swingPoleAnchorInit[swingSlot] = 0;
        }

        if (self.armDiagnosticsEnabled && self.armDiagnostics.IsCreated
            && (swingSlot == BasisEerieMovement.k_SwingLeftElbow || swingSlot == BasisEerieMovement.k_SwingRightElbow))
        {
            BasisArmDiagnosticsCore.Capture(input, result,
                swingSlot == BasisEerieMovement.k_SwingLeftElbow ? -1f : 1f,
                out BasisArmDiagnostics diag);
            self.armDiagnostics[swingSlot] = diag;
        }

        mid.SetRotation(stream, result.MidDelta * mid.GetRotation(stream));
        root.SetRotation(stream, result.RootDelta * root.GetRotation(stream));
        root.SetRotation(stream, result.HintDelta * root.GetRotation(stream));
        mid.SetRotation(stream, result.MidPostRoll * mid.GetRotation(stream));
        tip.SetRotation(stream, result.TipRotation);


        int collisionState = 0;
        float elbowSwivelDeg = float.NaN;   // NaN == no established choice to anchor on next frame
        bool doCollisions = collisionsEnabled && chestStart.IsValid(stream) && chestEnd.IsValid(stream);
        bool elbowTrackerForced = hintWeight && !usedModel;
        //elbow tracking is in a good spot
        if (doCollisions && protectElbow && (!elbowTrackerForced || collideTrackedElbow))
        {
            BasisElbowProtectInput epi = default;
            epi.Shoulder = root.GetPosition(stream);
            epi.Elbow = mid.GetPosition(stream);
            epi.Hand = tip.GetPosition(stream);
            epi.HasHips = self.HandleHips.IsValid(stream);
            epi.HasSpine = self.HandleSpine.IsValid(stream);
            epi.HipsPos = epi.HasHips ? self.HandleHips.GetPosition(stream) : Vector3.zero;
            epi.SpinePos = epi.HasSpine ? self.HandleSpine.GetPosition(stream) : Vector3.zero;
            epi.ChestPos = chestStart.GetPosition(stream);
            epi.NeckPos = chestEnd.GetPosition(stream);
            epi.ChestRadiusBase = chestRadius;
            epi.CollisionSkin = collisionSkin;
            epi.HandRadius = handRadius;
            epi.HandSkin = handSkin;
            epi.PlayerUp = self.playerUp;
            epi.BodyRight = bodyRight;
            epi.FullCircle = false;
            if (self.swingSwivelDeg.IsCreated)
            {
                float prev = self.swingSwivelDeg[swingSlot];
                if (!float.IsNaN(prev))
                {
                    epi.PrevSwivelDeg = prev;
                    epi.HasPrevSwivel = true;
                }
            }

            BasisElbowProtectCore.Solve(epi, out BasisElbowProtectResult epr);
            if (epr.Engaged)
            {
                tip.GetPositionAndRotation(stream, out Vector3 preservedHandPos, out Quaternion preservedHandRot);
                BasisEerieMovement.SwingElbowAroundAC(stream, root, mid, tip, epr.DesiredElbow);
                tip.SetPosition(stream, preservedHandPos);
                tip.SetRotation(stream, preservedHandRot);
                self.ReGuardElbowAnatomy(stream, root, mid, tip, swingSlot, bodyRight);
            }
            collisionState = epr.CollisionState;
            elbowSwivelDeg = epr.Engaged ? epr.ChosenSwivelDeg : float.NaN;
        }

        if (self.swingCollided.IsCreated)
        {
            self.swingCollided[swingSlot] = collisionState;
        }
        if (self.swingSwivelDeg.IsCreated)
        {
            self.swingSwivelDeg[swingSlot] = elbowSwivelDeg;
        }

        if (weight < 1f)
        {
            root.SetRotation(stream, Quaternion.Slerp(origRootRot, root.GetRotation(stream), weight));
            mid.SetRotation(stream, Quaternion.Slerp(origMidRot, mid.GetRotation(stream), weight));
            tip.SetRotation(stream, Quaternion.Slerp(origTipRot, tip.GetRotation(stream), weight));
        }
    }
    public static float ReachTrust(float conditioning)
    {
        if (!(conditioning > ReachTrustLo))
        {
            return 0f;
        }
        float t = math.saturate((conditioning - ReachTrustLo) / (ReachTrustHi - ReachTrustLo));
        return t * t * (3f - 2f * t);
    }
    public static float3 Apply(float3 prevBend, float3 prevAxis, float3 curAxis, float3 rawBend, float maxGain) => Apply(prevBend, prevAxis, curAxis, rawBend, maxGain, 0f, 0f);
    public static float3 Apply(float3 prevBend, float3 prevAxis, float3 curAxis, float3 rawBend, float maxGain, float dReach, float conditioning)
    {
        float3 tp = prevBend - curAxis * math.dot(prevBend, curAxis);
        float tpLen = math.length(tp);
        if (tpLen < 1e-4f)
        {
            return rawBend;   // degenerate transport (axis flipped ~180) -> just take the field
        }
        tp /= tpLen;

        float3 cross = math.cross(curAxis, tp);              // completes the tangent frame; rawBend = tp*cos+cross*sin
        float ang = math.atan2(math.dot(rawBend, cross), math.dot(rawBend, tp));
        float dHand = math.atan2(math.length(math.cross(prevAxis, curAxis)), math.dot(prevAxis, curAxis));
        float dRadial = 0f;
        float absReach = math.abs(dReach);
        if (absReach > 0f && math.isfinite(absReach))
        {
            dRadial = ReachGain * ReachTrust(conditioning) * absReach;
        }

        float cap = maxGain * (dHand + dRadial);
        float capped = math.clamp(ang, -cap, cap);
        if (capped == ang)
        {
            return rawBend;   // cap not binding -> exact field, no drift on ordinary reaching
        }

        float3 outb = tp * math.cos(capped) + cross * math.sin(capped);
        outb = outb - curAxis * math.dot(outb, curAxis);
        return math.normalizesafe(outb, rawBend);
    }
    public static void ApplySwingContinuity(ref BasisEerieMovement self, BasisPoseStream stream, int slot, BasisBoneHandle root, BasisBoneHandle mid, BasisBoneHandle tip, Vector3 targetPos, float rateDegPerSec, float dt, Vector3 bodyRight)
    {
        if (!self.swingContinuityInit.IsCreated || !root.IsValid(stream) || !mid.IsValid(stream) || !tip.IsValid(stream))
        {
            return;
        }

        Vector3 a = root.GetPosition(stream);
        Vector3 c = tip.GetPosition(stream);
        Vector3 b = mid.GetPosition(stream);

        BasisSwingContinuityState state;
        state.LastDir = self.swingLastDir[slot];
        state.LastAxis = self.swingLastAxis[slot];
        state.LastTarget = self.swingLastTarget[slot];
        state.SmoothState = self.swingSmoothState[slot];
        state.Seeded = self.swingContinuityInit[slot] != 0;
        int collided = self.swingCollided.IsCreated ? self.swingCollided[slot] : 0;

        BasisSwingContinuityCore.Step(state, a, b, c, targetPos, collided, rateDegPerSec, dt, out BasisSwingContinuityResult r);
        if (!r.Valid)
        {
            return;
        }

        if (r.ApplySwing)
        {
            Quaternion preservedHandRot = tip.GetRotation(stream);
            BasisEerieMovement.SwingElbowAroundAC(stream, root, mid, tip, a + r.NewDir);
            tip.SetPosition(stream, c);
            tip.SetRotation(stream, preservedHandRot);
            self.ReGuardElbowAnatomy(stream, root, mid, tip, slot, bodyRight);
        }

        self.swingLastDir[slot] = r.State.LastDir;
        self.swingLastAxis[slot] = r.State.LastAxis;
        self.swingLastTarget[slot] = r.State.LastTarget;
        self.swingSmoothState[slot] = r.State.SmoothState;
        self.swingContinuityInit[slot] = 1;
    }
    public static Vector3 CapsuleCapsuleResolve(Vector3 p1, Vector3 q1, float r1, Vector3 p2, Vector3 q2, float r2, Vector3 playerUp)
    {
        SegmentSegmentClosestPoints(p1, q1, p2, q2, out _, out _, out var c1, out var c2);
        Vector3 n = c1 - c2;
        float dSqr = Vector3.Dot(n, n);
        float rSum = r1 + r2;

        if (dSqr >= rSum * rSum) return Vector3.zero;

        Vector3 normal;
        if (dSqr > BasisEerieMovement.k_SqrEpsilon) normal = n / Mathf.Sqrt(dSqr);
        else
        {
            Vector3 axis = (q2 - p2);
            normal = Vector3.Normalize(Vector3.Cross(axis, playerUp));
            if (normal.sqrMagnitude < BasisEerieMovement.k_MinMag)
            {
                normal = Vector3.Normalize(Vector3.Cross(axis, Vector3.right));
            }

            if (normal.sqrMagnitude < BasisEerieMovement.k_MinMag)
            {
                normal = playerUp;
            }
        }

        float d = Mathf.Sqrt(Mathf.Max(dSqr, 0f));
        float penetration = (rSum - d);
        return normal * penetration;
    }
    public static void SegmentSegmentClosestPoints(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t, out Vector3 c1, out Vector3 c2)
    {
        Vector3 d1 = q1 - p1;
        Vector3 d2 = q2 - p2;
        Vector3 r = p1 - p2;
        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);

        if (a <= BasisEerieMovement.k_SqrEpsilon && e <= BasisEerieMovement.k_SqrEpsilon)
        {
            s = t = 0.0f; c1 = p1; c2 = p2; return;
        }
        if (a <= BasisEerieMovement.k_SqrEpsilon)
        {
            s = 0.0f; t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= BasisEerieMovement.k_SqrEpsilon)
            {
                t = 0.0f; s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;

                if (denom != 0.0f) s = Mathf.Clamp01((b * f - c * e) / denom);
                else s = 0.0f;

                t = (b * s + f) / e;
                if (t < 0.0f) { t = 0.0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1.0f) { t = 1.0f; s = Mathf.Clamp01((b - c) / a); }
            }
        }

        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }
    public static void ApplyShoulderYaw(BasisPoseStream stream, BasisBoneHandle shoulder, Quaternion hipsRot, float yawDeg)
    {
        if (!shoulder.IsValid(stream))
            return;
        Quaternion delta = hipsRot * Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.Inverse(hipsRot);
        shoulder.SetRotation(stream, delta * shoulder.GetRotation(stream));
    }
    public static void ApplyArmSwingChestFollow(ref BasisEerieMovement self, BasisPoseStream stream)
    {
        float factor = self.chestArmSwingFactor;
        if (factor <= 0f)
        {
            return;
        }

        if (!self.HandleHips.IsValid(stream) || !self.HandleChest.IsValid(stream))
        {
            return;
        }

        bool leftEnabled = self.enabledLeftHand > 0f;
        bool rightEnabled = self.enabledRightHand > 0f;
        if (!leftEnabled && !rightEnabled)
        {
            return;
        }

        Vector3 leftPos = leftEnabled ? self.targetPositionLeftHand : Vector3.zero;
        Vector3 rightPos = rightEnabled ? self.targetPositionRightHand : Vector3.zero;
        Vector3 handMid = leftEnabled && rightEnabled ? (leftPos + rightPos) * 0.5f : leftEnabled ? leftPos : rightPos;
        Vector3 hipsPos = self.HandleHips.GetPosition(stream);
        Quaternion hipsAnat = self.HandleHips.GetRotation(stream) * Quaternion.Inverse(self.offsetRotationHips);
        Quaternion invHipsAnat = Quaternion.Inverse(hipsAnat);
        Vector3 localMid = invHipsAnat * (handMid - hipsPos);

        float forwardDist = Mathf.Max(0.1f, Mathf.Abs(localMid.z));
        float yawDeg = Mathf.Atan2(localMid.x, forwardDist) * Mathf.Rad2Deg * factor;

        Vector3 localMidChest = invHipsAnat * (handMid - self.HandleChest.GetPosition(stream));
        float pitchDeg = Mathf.Atan2(-localMidChest.y, forwardDist) * Mathf.Rad2Deg * factor;

        float maxDeg = self.chestArmSwingMaxDeg;
        if (maxDeg > 0f)
        {
            yawDeg = Mathf.Clamp(yawDeg, -maxDeg, maxDeg);
            pitchDeg = Mathf.Clamp(pitchDeg, -maxDeg, maxDeg);
        }

        Quaternion local = Quaternion.AngleAxis(yawDeg, Vector3.up) * Quaternion.AngleAxis(pitchDeg, Vector3.right);
        Quaternion deltaWorld = hipsAnat * local * invHipsAnat;

        if (self.HandleUpperChest.IsValid(stream))
        {
            Quaternion chestPart = Quaternion.Slerp(Quaternion.identity, deltaWorld, BasisEerieMovement.k_ChestFollowChestShare);
            Quaternion upperPart = Quaternion.Slerp(Quaternion.identity, deltaWorld, 1f - BasisEerieMovement.k_ChestFollowChestShare);
            self.HandleChest.SetRotation(stream, chestPart * self.HandleChest.GetRotation(stream));
            self.HandleUpperChest.SetRotation(stream, upperPart * self.HandleUpperChest.GetRotation(stream));
        }
        else
        {
            self.HandleChest.SetRotation(stream, deltaWorld * self.HandleChest.GetRotation(stream));
        }
    }
    public static void ApplyShoulderSlide(ref BasisEerieMovement self, BasisPoseStream stream)
    {
        if (!self.HandleHips.IsValid(stream) || !self.HandleChest.IsValid(stream))
        {
            return;
        }
        Quaternion hipsRot = self.HandleHips.GetRotation(stream) * Quaternion.Inverse(self.offsetRotationHips);
        Quaternion chestRot = self.HandleChest.GetRotation(stream);
        Quaternion chestLocal = Quaternion.Inverse(hipsRot) * chestRot;
        float chestYaw = BasisTwistSolveCore.SignedTwistAngleDeg(chestLocal, Vector3.up);
        float excess = Mathf.Abs(chestYaw) - threshold;
        if (excess <= 0f)
            return;

        float counterYaw = -Mathf.Sign(chestYaw) * Mathf.Min(excess * fraction, maxCounter);
        ApplyShoulderYaw(stream, self.HandleLeftShoulder, hipsRot, counterYaw);
        ApplyShoulderYaw(stream, self.HandleRightShoulder, hipsRot, counterYaw);
    }
    public static void SolveArmTwist(BasisPoseStream stream, BasisBoneHandle parent, BasisBoneHandle child, BasisBoneHandle twist, float fraction)
    {
        if (!twist.IsValid(stream) || fraction <= 0f)
            return;
        if (!parent.IsValid(stream) || !child.IsValid(stream))
            return;

        Vector3 parentPos = parent.GetPosition(stream);
        Vector3 childPos = child.GetPosition(stream);
        float positionFraction = BasisTwistSolveCore.SegmentPositionFraction(parentPos, childPos, twist.GetPosition(stream));

        BasisTwistSolveInput input;
        input.ParentRotation = parent.GetRotation(stream);
        input.ChildRotation = child.GetRotation(stream);
        input.ParentToChild = childPos - parentPos;
        input.Fraction = positionFraction * fraction;

        BasisTwistSolveCore.Solve(input, out BasisTwistSolveResult result);
        if (result.Apply)
        {
            twist.SetRotation(stream, result.TwistWorldRotation);
        }
    }
    public static void SolveShoulder(ref BasisEerieMovement self, BasisPoseStream stream, BasisBoneHandle shoulderHandle, bool hasShoulderTrackerProp, Vector3 handTargetPosProp, Vector3 hintPosProp, bool hintWeightProp, Vector3 tposeArmDir, Quaternion tposeShoulderRot, Quaternion tposeChestRot, float tposeArmLength, float tposeClavicleLen, float tposeElbowLen, bool isLeft)
    {
        if (!shoulderHandle.IsValid(stream))
        {
            return;
        }

        Quaternion trackerRot = isLeft ? self.TargetRotationLeftShoulder : self.TargetRotationRightShoulder;

        BasisShoulderSolveInput input;
        input.ShoulderPos = shoulderHandle.GetPosition(stream);
        input.HandTargetPos = handTargetPosProp;
        input.ElbowPos = hintPosProp;
        input.HasElbow = hintWeightProp;
        input.HasShoulderTracker = hasShoulderTrackerProp;
        input.ChestRot = self.HandleUpperChest.IsValid(stream) ? self.HandleUpperChest.GetRotation(stream)
                       : self.HandleChest.IsValid(stream) ? self.HandleChest.GetRotation(stream)
                       : Quaternion.identity;
        input.TposeChestRot = tposeChestRot;
        input.ChestBind = self.TposeChestBind;
        input.TposeShoulderRot = tposeShoulderRot;
        input.TposeArmDirWorld = tposeArmDir;
        input.TposeArmLength = tposeArmLength;
        input.TposeClavicleLength = tposeClavicleLen;
        input.TposeElbowLength = tposeElbowLen;
        input.ShrugEnabled = self.shoulderShrugEnabled;
        input.RetractEnabled = self.shoulderRetractionEnabled;
        input.RhythmEnabled = self.shoulderRhythmEnabled;
        input.ElevationFactor = self.shoulderElevationFactor;
        input.ProtractionFactor = self.shoulderProtractionFactor;
        input.CoupleRatio = k_ShoulderCoupleRatio;
        input.MaxShoulderDeg = k_ShoulderMaxDeg;
        input.TrackerFinal = trackerRot * (isLeft ? self.targetOffsetLeftShoulder : self.targetOffsetRightShoulder);
        input.IsLeft = isLeft;

        BasisShoulderSolveCore.Solve(input, out BasisShoulderSolveResult result);
        if (result.Apply)
        {
            shoulderHandle.SetRotation(stream, result.ShoulderRotation);
        }
    }
}
