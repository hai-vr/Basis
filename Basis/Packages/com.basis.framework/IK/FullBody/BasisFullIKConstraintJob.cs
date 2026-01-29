using Unity.Mathematics;

namespace UnityEngine.Animations.Rigging
{
    [Unity.Burst.BurstCompile]
    public struct BasisFullIKConstraintJob : IWeightedAnimationJob
    {
        public ReadWriteTransformHandle HandleChest, HandleNeck, HandleHead, HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot, HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot, HandleHips, HandleSpine, HandleUpperChest, HandleLeftShoulder, HandleRightShoulder, HandleLeftToe, HandleRightToe, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand;

        public Vector3Property targetPositionHead, hintPositionHead, bendNormalHead, targetPositionLeftLowerLeg, hintPositionLeftLowerLeg, targetPositionRightLowerLeg, hintPositionRightLowerLeg, targetPositionHips, leftDrivenTargetPos, rightDrivenTargetPos, targetPositionLeftHand, hintPositionLeftHand, targetPositionRightHand, hintPositionRightHand, p0, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15, p16, p17, p18, p19, p20, p54;

        public Vector4Property targetRotationHead, hintRotationHead, targetRotationLeftLowerLeg, hintRotationLeftLowerLeg, targetRotationRightLowerLeg, hintRotationRightLowerLeg, targetRotationHips, offsetRotationHips, leftDrivenTargetRot, rightDrivenTargetRot, targetRotationLeftHand, hintRotationLeftHand, targetRotationRightHand, hintRotationRightHand, TargetRotationLeftShoulder, TargetRotationRightShoulder, r0, r1, r2, r3, r4, r5, r6, r7, r8, r9, r10, r11, r12, r13, r14, r15, r16, r17, r18, r19, r20, r54, o0, o1, o2, o3, o4, o5, o6, o7, o8, o9, o10, o11, o12, o13, o14, o15, o16, o17, o18, o19, o20, o54;

        public Quaternion targetOffsetNeck, targetOffsetHead, targetOffsetChest, targetOffsetLeftToe, targetOffsetRightToe, targetOffsetLeftShoulder, targetOffsetRightShoulder, targetOffsetLeftFoot, targetOffsetRightFoot, targetOffsetLeftHand, targetOffsetRightHand;

        public BoolProperty hintWeightHead, enabledSpineIK, hintWeightLeftLowerLeg, enabledLeftLowerLeg, hintWeightRightLowerLeg, enabledRightLowerLeg, enabledLeftShoulder, enabledRightShoulder, leftToeEnabled, RightToeEnabled, hintWeightLeftHand, enabledLeftHand, hintWeightRightHand, enabledRightHand, useHandCapsule, protectElbow, collisionsEnabled, w0, w1, w2, w3, w4, w5, w6, w7, w8, w9, w10, w11, w12, w13, w14, w15, w16, w17, w18, w19, w20, w54;

        public FloatProperty WristTwistLimitDeg, handRadius, handSkin, chestRadius, collisionSkin, maxBendDeg, maxFactor, struggleStart, struggleEnd, MaxChestDeltaDeg, UpperArmTwistLimitDeg, ForearmTwistLimitDeg, ElbowSwivelLimitDeg, ShoulderSwingConeDeg;


        public FloatProperty jobWeight { get; set; }
        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            float w = jobWeight.Get(stream);
            if (w <= 0f)
            {
                BasisIKHelpers.Pass(stream, HandleHips, HandleLeftToe, HandleRightToe);
                BasisIKHelpers.Pass(stream, HandleChest, HandleNeck, HandleHead);
                BasisIKHelpers.Pass(stream, HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot);
                BasisIKHelpers.Pass(stream, HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot);
                BasisIKHelpers.Pass(stream, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand);
                BasisIKHelpers.Pass(stream, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand);
                return;
            }

            BasisIKSpine Spine = BasisIKSpine.PackChain(stream, HandleHips, HandleSpine, HandleChest, HandleUpperChest, HandleNeck, HandleHead);
            float chainLen = BasisIKSpine.SpineChainLength(stream, Spine);

            Vector3 headTargetPos = targetPositionHead.Get(stream);
            Vector3 hipsTargetPos = targetPositionHips.Get(stream);

            hipsTargetPos = ComputeReachableHipsFromHeadFABRIK_Inline(stream, Spine, headTargetPos, hipsTargetPos, iterations: 6);

            SolveHipsAndSpine(stream, chainLen, headTargetPos, hipsTargetPos,
                targetRotationHips, offsetRotationHips, enabledSpineIK,
                HandleHips, HandleChest, HandleNeck, HandleHead,
                targetPositionHead, targetRotationHead, targetOffsetHead, bendNormalHead);

            BasisIKHelpers.ApplyRotation(stream, enabledLeftShoulder, HandleLeftShoulder, TargetRotationLeftShoulder, targetOffsetLeftShoulder);
            BasisIKHelpers.ApplyRotation(stream, enabledRightShoulder, HandleRightShoulder, TargetRotationRightShoulder, targetOffsetRightShoulder);

            Quaternion ChestRotation = Quaternion.identity;
            if (HandleUpperChest.IsValid(stream)) ChestRotation = HandleUpperChest.GetRotation(stream);
            else if (HandleChest.IsValid(stream)) ChestRotation = HandleChest.GetRotation(stream);

            SolveLegs(stream, enabledLeftLowerLeg, HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot, targetPositionLeftLowerLeg, targetRotationLeftLowerLeg, hintPositionLeftLowerLeg, hintRotationLeftLowerLeg, hintWeightLeftLowerLeg, targetOffsetLeftFoot, bendNormalHead);
            SolveLegs(stream, enabledRightLowerLeg, HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot, targetPositionRightLowerLeg, targetRotationRightLowerLeg, hintPositionRightLowerLeg, hintRotationRightLowerLeg, hintWeightRightLowerLeg, targetOffsetRightFoot, bendNormalHead);

            SolveHand(stream, enabledLeftHand, HandleLeftShoulder, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand,
                targetPositionLeftHand, targetRotationLeftHand, hintPositionLeftHand, hintRotationLeftHand, hintWeightLeftHand,
                targetOffsetLeftHand, HandleChest, HandleNeck, chestRadius, collisionSkin, collisionsEnabled, handRadius, handSkin,
                useHandCapsule, protectElbow, hintPositionLeftHand, ChestRotation, isLeft: true);

            SolveHand(stream, enabledRightHand, HandleRightShoulder, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand,
                targetPositionRightHand, targetRotationRightHand, hintPositionRightHand, hintRotationRightHand, hintWeightRightHand,
                targetOffsetRightHand, HandleChest, HandleNeck, chestRadius, collisionSkin, collisionsEnabled, handRadius, handSkin,
                useHandCapsule, protectElbow, hintPositionRightHand, ChestRotation, isLeft: false);

            BasisIKHelpers.ApplyRotation(stream, leftToeEnabled, HandleLeftToe, leftDrivenTargetRot, targetOffsetLeftToe);
            BasisIKHelpers.ApplyRotation(stream, RightToeEnabled, HandleRightToe, rightDrivenTargetRot, targetOffsetRightToe);

            ApplyOverrides(stream);
        }

        public void ApplyOverrides(AnimationStream stream)
        {
            BasisIKHelpers.Apply(stream, HandleHips, p0, r0, o0, w0);
            BasisIKHelpers.Apply(stream, HandleLeftUpperLeg, p1, r1, o1, w1);
            BasisIKHelpers.Apply(stream, HandleRightUpperLeg, p2, r2, o2, w2);
            BasisIKHelpers.Apply(stream, HandleLeftLowerLeg, p3, r3, o3, w3);
            BasisIKHelpers.Apply(stream, HandleRightLowerLeg, p4, r4, o4, w4);
            BasisIKHelpers.Apply(stream, HandleLeftFoot, p5, r5, o5, w5);
            BasisIKHelpers.Apply(stream, HandleRightFoot, p6, r6, o6, w6);
            BasisIKHelpers.Apply(stream, HandleSpine, p7, r7, o7, w7);
            BasisIKHelpers.Apply(stream, HandleChest, p8, r8, o8, w8);
            BasisIKHelpers.Apply(stream, HandleNeck, p9, r9, o9, w9);
            BasisIKHelpers.Apply(stream, HandleHead, p10, r10, o10, w10);
            BasisIKHelpers.Apply(stream, HandleLeftShoulder, p11, r11, o11, w11);
            BasisIKHelpers.Apply(stream, HandleRightShoulder, p12, r12, o12, w12);
            BasisIKHelpers.Apply(stream, HandleLeftUpperArm, p13, r13, o13, w13);
            BasisIKHelpers.Apply(stream, HandleRightUpperArm, p14, r14, o14, w14);
            BasisIKHelpers.Apply(stream, HandleLeftLowerArm, p15, r15, o15, w15);
            BasisIKHelpers.Apply(stream, HandleRightLowerArm, p16, r16, o16, w16);
            BasisIKHelpers.Apply(stream, HandleLeftHand, p17, r17, o17, w17);
            BasisIKHelpers.Apply(stream, HandleRightHand, p18, r18, o18, w18);
            BasisIKHelpers.Apply(stream, HandleLeftToe, p19, r19, o19, w19);
            BasisIKHelpers.Apply(stream, HandleRightToe, p20, r20, o20, w20);
            BasisIKHelpers.Apply(stream, HandleUpperChest, p54, r54, o54, w54);
        }

        static Vector3 ClampHipsAroundHeadByChain(Vector3 headTargetPos, Vector3 hipsTargetPos, float chainLen)
        {
            Vector3 v = hipsTargetPos - headTargetPos;
            float d2 = v.sqrMagnitude;

            if (d2 < BasisIKHelpers.k_MinSqrMagnitude) v = Vector3.down;

            float d = Mathf.Sqrt(Mathf.Max(d2, BasisIKHelpers.k_MinSqrMagnitude));
            Vector3 dir = v / d;

            float clamped = Mathf.Clamp(d, 0.0001f, Mathf.Max(0.0001f, chainLen));
            return headTargetPos + dir * clamped;
        }

        // ------------------------------------------------------------
        // 5) Shoulder pre-swing distribution
        // ------------------------------------------------------------
        static void ApplyShoulderPreSwing(
            AnimationStream stream,
            ReadWriteTransformHandle shoulder,
            Vector3 shoulderPos,
            Vector3 wristTarget,
            Quaternion chestRot,
            float maxReach,
            float maxClavicleDeg,
            float maxScapulaDeg,
            bool isLeft)
        {
            if (!shoulder.IsValid(stream)) return;

            float reach01 = BasisIKHelpers.ComputeReach01(shoulderPos, wristTarget, maxReach);
            float cross01 = BasisIKHelpers.ComputeCrossBody01(chestRot, shoulderPos, wristTarget, isLeft);

            Vector3 to = wristTarget - shoulderPos;
            float toLen = to.magnitude;
            float up01 = (toLen > 1e-6f) ? Mathf.Clamp01(Vector3.Dot(to / toLen, chestRot * Vector3.up)) : 0f;

            float w = Mathf.Clamp01(0.6f * reach01 + 0.5f * cross01 + 0.5f * up01);

            Vector3 aim = wristTarget - shoulderPos;
            if (aim.sqrMagnitude < 1e-8f) return;
            aim.Normalize();

            Vector3 shoulderFwd = chestRot * Vector3.forward;

            Quaternion swing = QuaternionExt.FromToRotation(shoulderFwd, aim);
            swing.ToAngleAxis(out float ang, out Vector3 ax);
            if (ang > 180f) ang -= 360f;

            float maxDeg = Mathf.Lerp(maxClavicleDeg, maxClavicleDeg + maxScapulaDeg, 0.5f);
            float clamped = Mathf.Clamp(ang, -maxDeg, maxDeg);

            Quaternion swingClamped = Quaternion.AngleAxis(clamped, ax);
            Quaternion pre = Quaternion.Slerp(Quaternion.identity, swingClamped, w);
            shoulder.SetRotation(stream, pre * shoulder.GetRotation(stream));
        }

        // ------------------------------------------------------------
        // SolveTwoBoneIKArms: now has option to NOT set wrist rotation
        // (so we can do section 6 wrist swing/twist at the end)
        // ------------------------------------------------------------
        public void SolveTwoBoneIKArms(
            AnimationStream stream,
            ReadWriteTransformHandle root,
            ReadWriteTransformHandle mid,
            ReadWriteTransformHandle tip,
            AffineTransform target,
            AffineTransform hint,
            bool hintWeight,
            Quaternion targetOffset,
            bool setTipRotation)
        {
            Vector3 aPosition = root.GetPosition(stream);
            Vector3 bPosition = mid.GetPosition(stream);
            Vector3 cPosition = tip.GetPosition(stream);

            Vector3 targetPos = target.translation;
            Quaternion targetRot = target.rotation;

            Vector3 tPosition = targetPos;
            Quaternion tRotation = targetRot * targetOffset;

            Vector3 ab = bPosition - aPosition;
            Vector3 bc = cPosition - bPosition;
            Vector3 ac = cPosition - aPosition;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float totalLen = abLen + bcLen;

            Vector3 atCorrected = tPosition - aPosition;
            float acLen = ac.magnitude;

            float oldAbcAngle = BasisIKHelpers.TriangleAngle(acLen, abLen, bcLen);
            float atCorrectedLen = atCorrected.magnitude;
            float newAbcAngle = BasisIKHelpers.TriangleAngle(atCorrectedLen, abLen, bcLen);

            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < BasisIKHelpers.k_MinSqrMagnitude)
            {
                axis = hintWeight ? Vector3.Cross(hint.translation - aPosition, bc) : Vector3.zero;
                if (axis.sqrMagnitude < BasisIKHelpers.k_MinSqrMagnitude)
                    axis = Vector3.Cross(atCorrected, bc);

                if (axis.sqrMagnitude < BasisIKHelpers.k_MinSqrMagnitude)
                    axis = Vector3.up;
            }
            axis = axis.normalized;

            float a = 0.5f * (oldAbcAngle - newAbcAngle);
            float sin = Mathf.Sin(a);
            float cos = Mathf.Cos(a);
            Quaternion deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);
            mid.SetRotation(stream, deltaR * mid.GetRotation(stream));

            cPosition = tip.GetPosition(stream);
            ac = cPosition - aPosition;

            if (atCorrectedLen > BasisIKHelpers.k_LengthEpsilon)
            {
                Quaternion rootDelta = QuaternionExt.FromToRotation(ac, atCorrected);
                root.SetRotation(stream, rootDelta * root.GetRotation(stream));
            }

            if (hintWeight)
            {
                float acSqrMag = ac.sqrMagnitude;
                if (acSqrMag > 0f)
                {
                    bPosition = mid.GetPosition(stream);
                    cPosition = tip.GetPosition(stream);
                    ab = bPosition - aPosition;
                    ac = cPosition - aPosition;

                    Vector3 acNorm = ac / Mathf.Sqrt(acSqrMag);
                    Vector3 ah = hint.translation - aPosition;
                    Vector3 abProj = ab - acNorm * Vector3.Dot(ab, acNorm);
                    Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);

                    if (abProj.sqrMagnitude > (totalLen * totalLen * BasisIKHelpers.K_Soften) && ahProj.sqrMagnitude > 0f)
                    {
                        Quaternion hintR = QuaternionExt.FromToRotation(abProj, ahProj);
                        hintR = QuaternionExt.NormalizeSafe(hintR);
                        root.SetRotation(stream, hintR * root.GetRotation(stream));
                    }
                }
            }

            if (setTipRotation)
                tip.SetRotation(stream, tRotation);
        }

        // Overload for existing call sites that want old behavior
        public void SolveTwoBoneIKArms(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, AffineTransform target, AffineTransform hint, bool hintWeight, Quaternion targetOffset)
        {
            SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hintWeight, targetOffset, setTipRotation: true);
        }

        // ------------------------------------------------------------
        // Geometry helpers you already had
        // ------------------------------------------------------------
        public static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float abSqr = Vector3.Dot(ab, ab);
            if (abSqr <= BasisIKHelpers.k_MinSqrMagnitude) return a;

            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / abSqr);
            return a + ab * t;
        }

        public static void SegmentSegmentClosestPoints(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            if (a <= BasisIKHelpers.k_MinSqrMagnitude && e <= BasisIKHelpers.k_MinSqrMagnitude)
            {
                s = t = 0.0f; c1 = p1; c2 = p2; return;
            }
            if (a <= BasisIKHelpers.k_MinSqrMagnitude)
            {
                s = 0.0f; t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= BasisIKHelpers.k_MinSqrMagnitude)
                {
                    t = 0.0f; s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;

                    s = denom != 0.0f ? Mathf.Clamp01((b * f - c * e) / denom) : 0.0f;

                    t = (b * s + f) / e;
                    switch (t)
                    {
                        case < 0.0f:
                            t = 0.0f; s = Mathf.Clamp01(-c / a); break;
                        case > 1.0f:
                            t = 1.0f; s = Mathf.Clamp01((b - c) / a); break;
                    }
                }
            }

            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }

        public static Vector3 CapsuleCapsuleResolve(Vector3 p1, Vector3 q1, float r1, Vector3 p2, Vector3 q2, float r2)
        {
            SegmentSegmentClosestPoints(p1, q1, p2, q2, out _, out _, out var c1, out var c2);
            Vector3 n = c1 - c2;
            float dSqr = Vector3.Dot(n, n);
            float rSum = r1 + r2;

            if (dSqr >= rSum * rSum) return Vector3.zero;

            Vector3 normal;
            if (dSqr > BasisIKHelpers.k_MinSqrMagnitude)
            {
                normal = n / Mathf.Sqrt(dSqr);
            }
            else
            {
                Vector3 axis = (q2 - p2);
                normal = Vector3.Normalize(Vector3.Cross(axis, Vector3.up));
                if (normal.sqrMagnitude < BasisIKHelpers.k_MinMagnitude)
                    normal = Vector3.Normalize(Vector3.Cross(axis, Vector3.right));
                if (normal.sqrMagnitude < BasisIKHelpers.k_MinMagnitude)
                    normal = Vector3.up;
            }

            float d = Mathf.Sqrt(Mathf.Max(dSqr, 0f));
            float penetration = (rSum - d);
            return normal * penetration;
        }

        public static Vector3 PushOutFromCapsule(Vector3 p, Vector3 a, Vector3 b, float radiusWithSkin)
        {
            Vector3 q = ClosestPointOnSegment(p, a, b);
            Vector3 qp = p - q;
            float dSqr = Vector3.Dot(qp, qp);
            if (dSqr >= radiusWithSkin * radiusWithSkin) return p;

            float d = Mathf.Sqrt(Mathf.Max(dSqr, BasisIKHelpers.k_MinSqrMagnitude));
            Vector3 n = (d > 0f) ? (qp / d) : Vector3.up;
            return q + n * radiusWithSkin;
        }

        // ------------------------------------------------------------
        // Your existing SolveTwoBone / SolveLegs unchanged
        // ------------------------------------------------------------
        public void SolveTwoBone(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, AffineTransform target, AffineTransform hint, bool HasHint, Quaternion targetOffset, Vector3 BendNormal)
        {
            Vector3 aPosition = root.GetPosition(stream);
            Vector3 bPosition = mid.GetPosition(stream);
            Vector3 cPosition = tip.GetPosition(stream);

            Vector3 targetPos = target.translation;
            Quaternion targetRot = target.rotation;

            Vector3 tPosition = targetPos;
            Quaternion tRotation = targetRot * targetOffset;

            Vector3 ab = bPosition - aPosition;
            Vector3 bc = cPosition - bPosition;
            Vector3 ac = cPosition - aPosition;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float acLen = ac.magnitude;

            float maxReach = abLen + bcLen;
            float oldAbcAngle = BasisIKHelpers.TriangleAngle(acLen, abLen, bcLen);
            Vector3 atCorrected = tPosition - aPosition;
            float atCorrectedLen = atCorrected.magnitude;

            float newAbcAngle = BasisIKHelpers.TriangleAngle(atCorrectedLen, abLen, bcLen);

            Vector3 axis;
            if (HasHint)
            {
                axis = Vector3.Cross(hint.translation - aPosition, bc);
                if (axis.sqrMagnitude < BasisIKHelpers.k_MinSqrMagnitude)
                    axis = Vector3.Cross(atCorrected, bc);
                if (axis.sqrMagnitude < BasisIKHelpers.k_MinSqrMagnitude)
                    axis = BendNormal;
            }
            else
            {
                axis = BendNormal;
            }

            axis = Vector3.Normalize(axis);

            float a = 0.5f * (oldAbcAngle - newAbcAngle);
            float sin = Mathf.Sin(a);
            float cos = Mathf.Cos(a);
            Quaternion deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);
            mid.SetRotation(stream, deltaR * mid.GetRotation(stream));

            cPosition = tip.GetPosition(stream);
            ac = cPosition - aPosition;

            if (atCorrectedLen > BasisIKHelpers.k_LengthEpsilon)
                root.SetRotation(stream, QuaternionExt.FromToRotation(ac, atCorrected) * root.GetRotation(stream));

            if (HasHint)
            {
                float acSqrMag = ac.sqrMagnitude;
                if (acSqrMag > 0f)
                {
                    bPosition = mid.GetPosition(stream);
                    cPosition = tip.GetPosition(stream);
                    ab = bPosition - aPosition;
                    ac = cPosition - aPosition;

                    Vector3 acNorm = ac / Mathf.Sqrt(acSqrMag);
                    Vector3 ah = hint.translation - aPosition;
                    Vector3 abProj = ab - acNorm * Vector3.Dot(ab, acNorm);
                    Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);

                    if (abProj.sqrMagnitude > (maxReach * maxReach * BasisIKHelpers.K_Soften) && ahProj.sqrMagnitude > 0f)
                    {
                        Quaternion hintR = QuaternionExt.FromToRotation(abProj, ahProj);
                        hintR = QuaternionExt.NormalizeSafe(hintR);
                        root.SetRotation(stream, hintR * root.GetRotation(stream));
                    }
                }
            }

            tip.SetRotation(stream, tRotation);
        }

        public void SolveLegs(AnimationStream stream, BoolProperty enabledProp, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, Vector3Property targetPosProp, Vector4Property targetRotProp, Vector3Property hintPosProp, Vector4Property hintRotProp, BoolProperty hintWeightProp, Quaternion targetOffset, Vector3Property bendNormalProp)
        {
            if (!enabledProp.Get(stream))
            {
                BasisIKHelpers.Pass(stream, root, mid, tip);
                return;
            }

            if (!(root.IsValid(stream) && mid.IsValid(stream) && tip.IsValid(stream)))
            {
                BasisIKHelpers.Pass(stream, root, mid, tip);
                return;
            }

            Quaternion tRot = BasisIKHelpers.ConvertToQuaternion(targetRotProp.Get(stream));
            Quaternion hRot = BasisIKHelpers.ConvertToQuaternion(hintRotProp.Get(stream));

            AffineTransform target = new AffineTransform(targetPosProp.Get(stream), tRot);
            AffineTransform hint = new AffineTransform(hintPosProp.Get(stream), hRot);
            Vector3 bendNormal = bendNormalProp.Get(stream);

            SolveTwoBone(stream, root, mid, tip, target, hint, hintWeightProp.Get(stream), targetOffset, bendNormal);
        }

        // ------------------------------------------------------------
        // 5 + 6 + 7 + 8: updated SolveHand
        // ------------------------------------------------------------
        public void SolveHand(
            AnimationStream stream,
            BoolProperty enabledProp,
            ReadWriteTransformHandle Shoulder,
            ReadWriteTransformHandle root,
            ReadWriteTransformHandle mid,
            ReadWriteTransformHandle tip,
            Vector3Property targetPosProp,
            Vector4Property targetRotProp,
            Vector3Property hintPosProp,
            Vector4Property hintRotProp,
            BoolProperty hintWeightProp,
            Quaternion targetOffset,
            ReadWriteTransformHandle chestStart,
            ReadWriteTransformHandle chestEnd,
            FloatProperty chestRadius,
            FloatProperty collisionSkin,
            BoolProperty collisionsEnabled,
            FloatProperty handRadius,
            FloatProperty handSkin,
            BoolProperty useHandCapsule,
            BoolProperty protectElbow,
            Vector3Property elbowTrackerPosProp,
            Quaternion ChestRotation,
            bool isLeft)
        {
            if (!enabledProp.Get(stream))
            {
                BasisIKHelpers.Pass(stream, root, mid, tip);
                return;
            }
            if (!(root.IsValid(stream) && mid.IsValid(stream) && tip.IsValid(stream)))
            {
                BasisIKHelpers.Pass(stream, root, mid, tip);
                return;
            }

            Vector3 tgtPos = targetPosProp.Get(stream);
            Quaternion tgtRot = BasisIKHelpers.ConvertToQuaternion(targetRotProp.Get(stream));
            Vector3 hintPos = hintPosProp.Get(stream);
            Quaternion hintRot = BasisIKHelpers.ConvertToQuaternion(hintRotProp.Get(stream));
            bool doCollisions = collisionsEnabled.Get(stream) && chestStart.IsValid(stream) && chestEnd.IsValid(stream);

            // Cache original desired wrist rotation (+offset) so we can apply 6) later
            Quaternion desiredWristRot = tgtRot * targetOffset;

            // ----------------------------
            // Collision pre-nudge (your original)
            // ----------------------------
            Vector3 chestA = default, chestB = default;
            float chestR = 0f;

            if (doCollisions)
            {
                chestA = chestStart.GetPosition(stream);
                chestB = chestEnd.GetPosition(stream);
                chestR = Mathf.Max(0f, chestRadius.Get(stream) + collisionSkin.Get(stream));

                if (useHandCapsule.Get(stream))
                {
                    float hRad = Mathf.Max(0f, handRadius.Get(stream) + handSkin.Get(stream));
                    Vector3 handA = mid.GetPosition(stream);
                    Vector3 handB = tip.GetPosition(stream);

                    Vector3 correction = CapsuleCapsuleResolve(handA, handB, hRad, chestA, chestB, chestR);
                    if (correction.sqrMagnitude > 0f)
                    {
                        tgtPos += correction;
                        hintPos += correction * 0.25f;
                    }
                }
                else
                {
                    tgtPos = PushOutFromCapsule(tgtPos, chestA, chestB, chestR);
                    Vector3 nudgedHint = PushOutFromCapsule(hintPos, chestA, chestB, chestR * 0.9f);
                    hintPos = Vector3.Lerp(hintPos, nudgedHint, 0.6f);
                }
            }

            // ----------------------------
            // 7) Soft reach clamp BEFORE solving (no snapping at max reach)
            // ----------------------------
            Vector3 A0 = root.GetPosition(stream);
            Vector3 B0 = mid.GetPosition(stream);
            Vector3 C0 = tip.GetPosition(stream);

            float abLen0 = (B0 - A0).magnitude;
            float bcLen0 = (C0 - B0).magnitude;
            float maxReach = abLen0 + bcLen0;

            // soften zone at ~15% of reach
            tgtPos = BasisIKHelpers.SoftClampToReach(A0, tgtPos, maxReach, softZone: 0.15f * Mathf.Max(maxReach, 1e-4f));

            var target = new AffineTransform(tgtPos, tgtRot);
            var hint = new AffineTransform(hintPos, hintRot);

            // ----------------------------
            // 5) Shoulder pre-swing distribution (clavicle/scapula-ish)
            // ----------------------------
            // (Tune these degrees)
            ApplyShoulderPreSwing(
                stream,
                Shoulder,
                Shoulder.IsValid(stream) ? Shoulder.GetPosition(stream) : A0,
                tgtPos,
                ChestRotation,
                maxReach,
                maxClavicleDeg: 18f,
                maxScapulaDeg: 14f,
                isLeft: isLeft);

            // After shoulder moved, refresh A/B/C for cone clamp and solve
            Vector3 A = root.GetPosition(stream);
            Vector3 B = mid.GetPosition(stream);
            Vector3 C = tip.GetPosition(stream);

            // ----------------------------
            // Your swing cone clamp (still good)
            // ----------------------------
            ClampUpperArmSwingCone(stream, root, mid, ChestRotation * Vector3.forward, ShoulderSwingConeDeg.Get(stream));

            // ============================================================
            // A) First solve: positions only (don't set wrist rotation here)
            // ============================================================
            Quaternion rootRotBeforeSolve = root.GetRotation(stream);
            Quaternion midRotBeforeSolve = mid.GetRotation(stream);

            SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hintWeightProp.Get(stream), targetOffset, setTipRotation: false);

            // Clamp root & forearm twist from the solve itself
            ClampRootTwistAroundAC(stream, root, tip, rootRotBeforeSolve, UpperArmTwistLimitDeg.Get(stream));
            ClampForearmTwistAroundBC(stream, mid, tip, midRotBeforeSolve, ForearmTwistLimitDeg.Get(stream));

            // Re-solve positions after twist limits (still no wrist rotation)
            SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hintWeightProp.Get(stream), targetOffset, setTipRotation: false);

            // ============================================================
            // B) Elbow tracker swivel (your current behavior)
            // ============================================================
            if (hintWeightProp.Get(stream))
            {
                A = root.GetPosition(stream);
                B = mid.GetPosition(stream);
                C = tip.GetPosition(stream);

                float abLen = (B - A).magnitude;
                float bcLen = (C - B).magnitude;

                Vector3 elbowTracker = elbowTrackerPosProp.Get(stream);

                Vector3 Bvalid = ProjectElbowToValidCircle(A, C, abLen, bcLen, elbowTracker);
                Vector3 Bblend = LerpOnCircleAroundAxis(A, C, B, Bvalid, 1f);

                Quaternion rootRotBeforeSwivel = root.GetRotation(stream);
                Quaternion midRotBeforeSwivel = mid.GetRotation(stream);

                SwingElbowAroundAC_Clamped(stream, root, mid, tip, Bblend, ElbowSwivelLimitDeg.Get(stream));

                ClampRootTwistAroundAC(stream, root, tip, rootRotBeforeSwivel, UpperArmTwistLimitDeg.Get(stream));
                ClampForearmTwistAroundBC(stream, mid, tip, midRotBeforeSwivel, ForearmTwistLimitDeg.Get(stream));

                SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hintWeightProp.Get(stream), targetOffset, setTipRotation: false);
            }

            // ============================================================
            // 8) Constraint-space collision resolution loop
            // - DO NOT just nudge target; instead swivel joint-space and re-lock.
            // ============================================================
            if (doCollisions)
            {
                // 2 quick passes is usually enough
                for (int it = 0; it < 2; it++)
                {
                    // Elbow point vs chest capsule
                    Vector3 elbow = mid.GetPosition(stream);
                    Vector3 pushElbow = BasisIKHelpers.PointCapsuleResolve(elbow, chestA, chestB, chestR);

                    // Forearm capsule vs chest capsule (stronger)
                    float forearmR = Mathf.Max(0f, handRadius.Get(stream) + handSkin.Get(stream)) * 0.65f; // tune
                    Vector3 foreA = mid.GetPosition(stream);
                    Vector3 foreB = tip.GetPosition(stream);
                    Vector3 pushFore = CapsuleCapsuleResolve(foreA, foreB, forearmR, chestA, chestB, chestR);

                    Vector3 push = pushElbow + pushFore;

                    if (push.sqrMagnitude < 1e-10f) break;

                    Vector3 desiredElbow = elbow + push;

                    Quaternion rootRotBefore = root.GetRotation(stream);
                    Quaternion midRotBefore = mid.GetRotation(stream);

                    SwingElbowAroundAC_Clamped(stream, root, mid, tip, desiredElbow, ElbowSwivelLimitDeg.Get(stream));
                    ClampRootTwistAroundAC(stream, root, tip, rootRotBefore, UpperArmTwistLimitDeg.Get(stream));
                    ClampForearmTwistAroundBC(stream, mid, tip, midRotBefore, ForearmTwistLimitDeg.Get(stream));

                    SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hintWeightProp.Get(stream), targetOffset, setTipRotation: false);
                }
            }

            // ============================================================
            // C) Your elbow protection pass (keep it if you like)
            // ============================================================
            if (protectElbow.Get(stream) && doCollisions)
            {
                Vector3 Bnow = mid.GetPosition(stream);
                Vector3 pushedB = PushOutFromCapsule(Bnow, chestA, chestB, chestR);

                if ((pushedB - Bnow).sqrMagnitude > BasisIKHelpers.k_DivisionSafetyEpsilon)
                {
                    Quaternion rootRotBefore = root.GetRotation(stream);
                    Quaternion midRotBefore = mid.GetRotation(stream);

                    SwingElbowAroundAC_Clamped(stream, root, mid, tip, pushedB, ElbowSwivelLimitDeg.Get(stream));
                    ClampRootTwistAroundAC(stream, root, tip, rootRotBefore, UpperArmTwistLimitDeg.Get(stream));
                    ClampForearmTwistAroundBC(stream, mid, tip, midRotBefore, ForearmTwistLimitDeg.Get(stream));

                    SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hintWeightProp.Get(stream), targetOffset, setTipRotation: false);
                }
            }

            // ============================================================
            // 6) Wrist orientation: decouple aim from twist (clamp twist only)
            // ============================================================
            Vector3 Bf = mid.GetPosition(stream);
            Vector3 Cf = tip.GetPosition(stream);
            Vector3 axisBC = (Cf - Bf);
            if (axisBC.sqrMagnitude > 1e-10f)
            {
                axisBC.Normalize();
                Quaternion curWrist = tip.GetRotation(stream);
                float wristTwistLimit = WristTwistLimitDeg.Get(stream); // e.g. 60..90
                Quaternion finalWrist = BasisIKHelpers.ClampWristTwistAroundForearm(axisBC, curWrist, desiredWristRot, wristTwistLimit);
                tip.SetRotation(stream, finalWrist);
            }
            else
            {
                tip.SetRotation(stream, desiredWristRot);
            }
        }

        // ------------------------------------------------------------
        // Existing swivel/twist clamps you already have (unchanged)
        // ------------------------------------------------------------
        static float SignedAngleDegAroundAxis(Vector3 from, Vector3 to, Vector3 axis)
        {
            from -= axis * Vector3.Dot(from, axis);
            to -= axis * Vector3.Dot(to, axis);

            float fromLen = from.magnitude;
            float toLen = to.magnitude;
            if (fromLen < BasisIKHelpers.k_MinMagnitude || toLen < BasisIKHelpers.k_MinMagnitude) return 0f;

            from /= fromLen;
            to /= toLen;

            float dot = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
            float ang = Mathf.Acos(dot) * Mathf.Rad2Deg;
            float sgn = Mathf.Sign(Vector3.Dot(Vector3.Cross(from, to), axis));
            return ang * sgn;
        }

        public static void SwingElbowAroundAC_Clamped(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, Vector3 desiredB, float maxSwivelDeg)
        {
            Vector3 A = root.GetPosition(stream);
            Vector3 C = tip.GetPosition(stream);
            Vector3 B = mid.GetPosition(stream);

            Vector3 AC = C - A;
            float acSqr = Vector3.Dot(AC, AC);
            if (acSqr <= 1e-8f) return;

            Vector3 n = AC / Mathf.Sqrt(acSqr);

            Vector3 vCur = B - A; vCur -= n * Vector3.Dot(vCur, n);
            Vector3 vDes = desiredB - A; vDes -= n * Vector3.Dot(vDes, n);

            if (vCur.sqrMagnitude <= 1e-10f || vDes.sqrMagnitude <= 1e-10f) return;

            float angDeg = SignedAngleDegAroundAxis(vCur, vDes, n);
            float clamped = Mathf.Clamp(angDeg, -maxSwivelDeg, maxSwivelDeg);

            Quaternion swivel = Quaternion.AngleAxis(clamped, n);
            root.SetRotation(stream, swivel * root.GetRotation(stream));
        }

        public static void ClampRootTwistAroundAC(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle tip, Quaternion rootRotBefore, float twistLimitDeg)
        {
            Vector3 A = root.GetPosition(stream);
            Vector3 C = tip.GetPosition(stream);
            Vector3 axis = C - A;
            float axisSqr = axis.sqrMagnitude;
            if (axisSqr < 1e-10f) return;
            axis /= Mathf.Sqrt(axisSqr);

            Quaternion rootRotAfter = root.GetRotation(stream);
            Quaternion delta = rootRotAfter * Quaternion.Inverse(rootRotBefore);
            delta = BasisIKHelpers.NormalizeSafe(delta);

            BasisIKHelpers.SwingTwist(delta, axis, out var swing, out var twist);
            Quaternion twistClamped = BasisIKHelpers.ClampTwistDegrees(twist, twistLimitDeg);

            Quaternion deltaLimited = BasisIKHelpers.NormalizeSafe(swing * twistClamped);
            Quaternion newRootRot = BasisIKHelpers.NormalizeSafe(deltaLimited * rootRotBefore);

            root.SetRotation(stream, newRootRot);
        }

        public static void ClampUpperArmSwingCone(AnimationStream stream, ReadWriteTransformHandle upperArm, ReadWriteTransformHandle lowerArm, Vector3 referenceAxisWorld, float coneHalfAngleDeg)
        {
            Vector3 A = upperArm.GetPosition(stream);
            Vector3 B = lowerArm.GetPosition(stream);

            Vector3 AB = B - A;
            float abLen = AB.magnitude;
            if (abLen < BasisIKHelpers.k_MinMagnitude) return;

            Vector3 desiredDir = AB / abLen;
            Vector3 clampedDir = BasisIKHelpers.ClampDirectionInCone(desiredDir, referenceAxisWorld, coneHalfAngleDeg);

            Quaternion rot = QuaternionExt.FromToRotation(desiredDir, clampedDir);
            upperArm.SetRotation(stream, rot * upperArm.GetRotation(stream));
        }

        public static void ClampForearmTwistAroundBC(AnimationStream stream, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, Quaternion midRotBefore, float twistLimitDeg)
        {
            Vector3 B = mid.GetPosition(stream);
            Vector3 C = tip.GetPosition(stream);
            Vector3 axis = C - B;
            float axisSqr = axis.sqrMagnitude;
            if (axisSqr < 1e-10f) return;
            axis /= Mathf.Sqrt(axisSqr);

            Quaternion midRotAfter = mid.GetRotation(stream);
            Quaternion delta = midRotAfter * Quaternion.Inverse(midRotBefore);
            delta = BasisIKHelpers.NormalizeSafe(delta);

            BasisIKHelpers.SwingTwist(delta, axis, out var swing, out var twist);
            Quaternion twistClamped = BasisIKHelpers.ClampTwistDegrees(twist, twistLimitDeg);

            Quaternion deltaLimited = BasisIKHelpers.NormalizeSafe(swing * twistClamped);
            Quaternion newMidRot = BasisIKHelpers.NormalizeSafe(deltaLimited * midRotBefore);
            mid.SetRotation(stream, newMidRot);
        }

        // ------------------------------------------------------------
        // NOTE: Everything below here is your existing spine/hips code
        // (kept as-is except references to BasisIKHelpers.SwingTwist etc.)
        // ------------------------------------------------------------

        public void SolveHipsAndSpine(AnimationStream stream, float chainlength, Vector3 headTargetPos, Vector3 hipsTargetPos, Vector4Property targetRotationHips, Vector4Property offsetRotationHips, BoolProperty EnableSpineIK, ReadWriteTransformHandle HandleHips, ReadWriteTransformHandle HandleChest, ReadWriteTransformHandle HandleNeck, ReadWriteTransformHandle HandleHead, Vector3Property targetPositionHead, Vector4Property targetRotationHead, Quaternion targetOffsetHead, Vector3Property bendNormalHead)
        {
            hipsTargetPos = ClampHipsAroundHeadByChain(headTargetPos, hipsTargetPos, chainlength);

            if (!EnableSpineIK.Get(stream))
            {
                BasisIKHelpers.Pass(stream, HandleChest, HandleNeck, HandleHead);
                BasisIKHelpers.PassThrough(stream, HandleHips);
                return;
            }

            if (HandleHips.IsValid(stream))
            {
                HandleHips.SetPosition(stream, hipsTargetPos);
                Quaternion hipsOrigRot = HandleHips.GetRotation(stream);

                HandleHips.SetRotation(stream, hipsOrigRot);

                Quaternion tRot = BasisIKHelpers.ConvertToQuaternion(targetRotationHead.Get(stream));
                Vector3 bendNormal = bendNormalHead.Get(stream);
                SolveTwoBoneSpine(stream, HandleChest, HandleNeck, HandleHead, targetPositionHead.Get(stream), tRot, targetOffsetHead, bendNormal);

                Vector3 spineAxis = Vector3.up;
                if (HandleSpine.IsValid(stream)) spineAxis = HandleSpine.GetPosition(stream) - HandleHips.GetPosition(stream);
                else if (HandleChest.IsValid(stream)) spineAxis = HandleChest.GetPosition(stream) - HandleHips.GetPosition(stream);

                if (spineAxis.sqrMagnitude < 1e-8f) spineAxis = HandleHips.GetRotation(stream) * Vector3.up;
                spineAxis.Normalize();

                Quaternion hipRot = BasisIKHelpers.ConvertToQuaternion(targetRotationHips.Get(stream));
                Quaternion hipOff = BasisIKHelpers.ConvertToQuaternion(offsetRotationHips.Get(stream));
                Quaternion hipsDesiredRot = hipRot * hipOff;

                Quaternion delta = hipsDesiredRot * Quaternion.Inverse(hipsOrigRot);
                BasisIKHelpers.SwingTwist(delta, spineAxis, out var swing, out var twist);

                swing.ToAngleAxis(out float swingAngle, out Vector3 swingAxis);
                if (swingAngle > 180f) swingAngle -= 360f;

                if (Mathf.Abs(swingAngle) < 0.001f || swingAxis.sqrMagnitude < 1e-8f)
                {
                    swing = Quaternion.identity;
                    swingAxis = spineAxis;
                    swingAngle = 0f;
                }
                else swingAxis.Normalize();

                float swingClampDeg = 75;
                float clamped = Mathf.Clamp(swingAngle, -swingClampDeg, swingClampDeg);
                Quaternion swingClamped = Quaternion.AngleAxis(clamped, swingAxis);

                Quaternion hipsFinal = (twist * swingClamped) * hipsOrigRot;
                HandleHips.SetRotation(stream, hipsFinal);

                SolveTwoBoneSpine(stream, HandleChest, HandleNeck, HandleHead, targetPositionHead.Get(stream), tRot, targetOffsetHead, bendNormal);
            }

            if (HandleChest.IsValid(stream) & HandleNeck.IsValid(stream) & HandleHead.IsValid(stream))
            {
                Quaternion tRot = BasisIKHelpers.ConvertToQuaternion(targetRotationHead.Get(stream));
                Vector3 bendNormal = bendNormalHead.Get(stream);

                SolveTwoBoneSpine(stream, HandleChest, HandleNeck, HandleHead, targetPositionHead.Get(stream), tRot, targetOffsetHead, bendNormal);

                if (hintWeightHead.Get(stream))
                {
                    Quaternion neckRot = HandleNeck.GetRotation(stream);
                    Quaternion spineRot = HandleSpine.IsValid(stream) ? HandleSpine.GetRotation(stream) : neckRot;
                    Quaternion trackerChestRot = BasisIKHelpers.ConvertToQuaternion(hintRotationHead.Get(stream)) * targetOffsetChest;

                    float MaxChestDelta = MaxChestDeltaDeg.Get(stream);
                    Quaternion clampedChestRot = BasisIKHelpers.ClampRotation(trackerChestRot, neckRot, MaxChestDelta);
                    clampedChestRot = BasisIKHelpers.ClampRotation(clampedChestRot, spineRot, MaxChestDelta);

                    HandleChest.SetRotation(stream, clampedChestRot);

                    tRot = BasisIKHelpers.ConvertToQuaternion(targetRotationHead.Get(stream));
                    Vector3 TargetPosition = targetPositionHead.Get(stream);

                    SolveTwoBoneSpine(stream, HandleChest, HandleNeck, HandleHead, TargetPosition, tRot, targetOffsetHead, bendNormal);
                }
            }
            else
            {
                BasisIKHelpers.Pass(stream, HandleChest, HandleNeck, HandleHead);
                return;
            }
        }

        public void SolveTwoBoneSpine(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, Vector3 PositionTarget, Quaternion RotationTarget, Quaternion targetOffset, Vector3 bendNormal)
        {
            Vector3 aPos = root.GetPosition(stream);
            Vector3 bPos = mid.GetPosition(stream);
            Vector3 cPos = tip.GetPosition(stream);

            Quaternion tRot = RotationTarget * targetOffset;

            Vector3 ab = bPos - aPos;
            Vector3 bc = cPos - bPos;
            Vector3 ac = cPos - aPos;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;

            float minReach = Mathf.Abs(abLen - bcLen);

            Vector3 at = PositionTarget - aPos;
            float atLen = at.magnitude;
            float margin = Mathf.Max(1e-3f, (abLen + bcLen) * 0.005f);

            if (atLen < minReach + margin)
            {
                Vector3 dir = (atLen > 1e-6f) ? (at / atLen) : (ac.sqrMagnitude > 1e-8f) ? ac.normalized : Vector3.up;
                PositionTarget = aPos + dir * (minReach + margin);
                atLen = minReach + margin;
            }
            Vector3 atVec = PositionTarget - aPos;

            float acLen = ac.magnitude;
            float oldAbcAngle = BasisIKHelpers.TriangleAngle(acLen, abLen, bcLen);
            float newAbcAngle = BasisIKHelpers.TriangleAngle(atLen, abLen, bcLen);

            Vector3 axis = BasisIKHelpers.ComputeIkAxis(bendNormal);

            float halfAngle = 0.5f * (oldAbcAngle - newAbcAngle);
            float s = Mathf.Sin(halfAngle);
            float c = Mathf.Cos(halfAngle);
            Quaternion deltaMid = new Quaternion(axis.x * s, axis.y * s, axis.z * s, c);
            mid.SetRotation(stream, deltaMid * mid.GetRotation(stream));

            cPos = tip.GetPosition(stream);
            ac = cPos - aPos;
            root.SetRotation(stream, QuaternionExt.FromToRotation(ac, atVec) * root.GetRotation(stream));

            tip.SetRotation(stream, tRot);
        }

        unsafe static Vector3 ComputeReachableHipsFromHeadFABRIK_Inline(AnimationStream stream, BasisIKSpine chain, Vector3 headTargetPos, Vector3 hipsTargetPos, int iterations = 6, float eps = 1e-4f)
        {
            int n = chain.Count;
            if (n < 2) return hipsTargetPos;

            // IMPORTANT: this must not exceed 6.
            // If your chain can be longer, clamp or increase these arrays.
            if (n > 6) n = 6;

            float3* p = stackalloc float3[6];
            float* l = stackalloc float[6];

            for (int i = 0; i < n; i++)
            {
                int ci = (n - 1) - i;
                var h = BasisIKHelpers.GetSpineHandle(chain, ci);
                float3 pos = h.GetPosition(stream);
                p[i] = pos;
            }

            p[0] = headTargetPos;

            l[0] = 0f;
            float totalLen = 0f;
            for (int i = 1; i < n; i++)
            {
                float len = math.max(math.length(p[i] - p[i - 1]), eps);
                l[i] = len;
                totalLen += len;
            }

            float3 basePos = p[0];
            float3 target = hipsTargetPos;

            float dist = math.length(target - basePos);

            if (dist >= totalLen - eps)
            {
                float3 dir = BasisIKHelpers.SafeDir(target - basePos, eps, new float3(0f, -1f, 0f));
                for (int i = 1; i < n; i++)
                    p[i] = p[i - 1] + dir * l[i];

                return (Vector3)p[n - 1];
            }

            for (int it = 0; it < iterations; it++)
            {
                p[n - 1] = target;
                for (int i = n - 2; i >= 0; i--)
                {
                    float3 dir = BasisIKHelpers.SafeDir(p[i] - p[i + 1], eps, new float3(0f, -1f, 0f));
                    p[i] = p[i + 1] + dir * l[i + 1];
                }

                p[0] = headTargetPos;
                for (int i = 1; i < n; i++)
                {
                    float3 dir = BasisIKHelpers.SafeDir(p[i] - p[i - 1], eps, new float3(0f, -1f, 0f));
                    p[i] = p[i - 1] + dir * l[i];
                }

                if (math.lengthsq(p[n - 1] - target) <= eps * eps)
                    break;
            }

            return (Vector3)p[n - 1];
        }

        // You already had these elbow circle helpers (kept as-is)
        static Vector3 ProjectElbowToValidCircle(Vector3 A, Vector3 C, float abLen, float bcLen, Vector3 elbowTracker)
        {
            Vector3 AC = C - A;
            float acLen = AC.magnitude;
            if (acLen < 1e-6f) return elbowTracker;

            Vector3 n = AC / acLen;

            float x = (abLen * abLen - bcLen * bcLen + acLen * acLen) / (2f * acLen);
            float r2 = abLen * abLen - x * x;
            float r = Mathf.Sqrt(Mathf.Max(0f, r2));

            Vector3 center = A + n * x;

            Vector3 v = elbowTracker - center;
            v -= n * Vector3.Dot(v, n);

            float vLen = v.magnitude;
            if (vLen < 1e-6f)
            {
                v = Vector3.Cross(n, Vector3.up);
                if (v.sqrMagnitude < 1e-6f)
                    v = Vector3.Cross(n, Vector3.right);

                v.Normalize();
                return center + v * r;
            }

            v /= vLen;
            return center + v * r;
        }

        static Vector3 LerpOnCircleAroundAxis(Vector3 A, Vector3 C, Vector3 Bcurrent, Vector3 Bdesired, float w)
        {
            Vector3 AC = C - A;
            float acLen = AC.magnitude;
            if (acLen < 1e-6f) return Vector3.Lerp(Bcurrent, Bdesired, w);

            Vector3 n = AC / acLen;

            Vector3 v1 = Bcurrent - A; v1 -= n * Vector3.Dot(v1, n);
            Vector3 v2 = Bdesired - A; v2 -= n * Vector3.Dot(v2, n);

            float v1Len = v1.magnitude;
            float v2Len = v2.magnitude;
            if (v1Len < 1e-6f || v2Len < 1e-6f) return Vector3.Lerp(Bcurrent, Bdesired, w);

            v1 /= v1Len;
            v2 /= v2Len;

            float dot = Mathf.Clamp(Vector3.Dot(v1, v2), -1f, 1f);
            float ang = Mathf.Acos(dot);

            Vector3 cross = Vector3.Cross(v1, v2);
            float sign = Mathf.Sign(Vector3.Dot(cross, n));
            float angDeg = ang * sign * Mathf.Rad2Deg;

            Quaternion rot = Quaternion.AngleAxis(angDeg * Mathf.Clamp01(w), n);
            Vector3 v = rot * (Bcurrent - A);

            float r = (Bcurrent - (A + n * Vector3.Dot(Bcurrent - A, n))).magnitude;
            Vector3 vPlane = v - n * Vector3.Dot(v, n);
            float vPlaneLen = vPlane.magnitude;
            if (vPlaneLen > 1e-6f)
            {
                vPlane = vPlane / vPlaneLen * r;
                float along = Vector3.Dot(Bcurrent - A, n);
                return A + n * along + vPlane;
            }

            return Vector3.Lerp(Bcurrent, Bdesired, w);
        }
    }
}
