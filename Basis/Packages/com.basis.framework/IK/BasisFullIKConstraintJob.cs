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

        public FloatProperty handRadius, handSkin, chestRadius, collisionSkin, MinHeadSpineHeight, maxBendDeg, minFactor, maxFactor, struggleStart, struggleEnd, MaxChestDeltaDeg;
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
            Vector3 headTargetPos = targetPositionHead.Get(stream);
            Vector3 hipsTargetPos = targetPositionHips.Get(stream);

            SolveHipsAndSpine(stream, headTargetPos, hipsTargetPos, targetRotationHips, offsetRotationHips, enabledSpineIK, HandleHips, HandleChest, HandleNeck, HandleHead, targetPositionHead, targetRotationHead, targetOffsetHead, bendNormalHead);

            ApplyRotation(stream, enabledLeftShoulder, HandleLeftShoulder, TargetRotationLeftShoulder, targetOffsetLeftShoulder);
            ApplyRotation(stream, enabledRightShoulder, HandleRightShoulder, TargetRotationRightShoulder, targetOffsetRightShoulder);

            SolveLegs(stream, enabledLeftLowerLeg, HandleLeftUpperLeg, HandleLeftLowerLeg, HandleLeftFoot, targetPositionLeftLowerLeg, targetRotationLeftLowerLeg, hintPositionLeftLowerLeg, hintRotationLeftLowerLeg, hintWeightLeftLowerLeg, targetOffsetLeftFoot, bendNormalHead);
            SolveLegs(stream, enabledRightLowerLeg, HandleRightUpperLeg, HandleRightLowerLeg, HandleRightFoot, targetPositionRightLowerLeg, targetRotationRightLowerLeg, hintPositionRightLowerLeg, hintRotationRightLowerLeg, hintWeightRightLowerLeg, targetOffsetRightFoot, bendNormalHead);

            SolveHand(stream, enabledLeftHand, HandleLeftUpperArm, HandleLeftLowerArm, HandleLeftHand, targetPositionLeftHand, targetRotationLeftHand, hintPositionLeftHand, hintRotationLeftHand, hintWeightLeftHand, targetOffsetLeftHand, HandleChest, HandleNeck, chestRadius, collisionSkin, collisionsEnabled, handRadius, handSkin, useHandCapsule, protectElbow);
            SolveHand(stream, enabledRightHand, HandleRightUpperArm, HandleRightLowerArm, HandleRightHand, targetPositionRightHand, targetRotationRightHand, hintPositionRightHand, hintRotationRightHand, hintWeightRightHand, targetOffsetRightHand, HandleChest, HandleNeck, chestRadius, collisionSkin, collisionsEnabled, handRadius, handSkin, useHandCapsule, protectElbow);

            ApplyRotation(stream, leftToeEnabled, HandleLeftToe, leftDrivenTargetRot, targetOffsetLeftToe);
            ApplyRotation(stream, RightToeEnabled, HandleRightToe, rightDrivenTargetRot, targetOffsetRightToe);

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
        static Vector3 EnforceSpineBendLimit(Vector3 headPos, Vector3 hipsPos, float maxBendDeg, Vector3 up)
        {
            if (maxBendDeg <= 0f)
            {
                return hipsPos;
            }

            Vector3 diff = hipsPos - headPos;
            float sqrMag = diff.sqrMagnitude;
            if (sqrMag < BasisIKHelpers.k_MinMagnitude)
            {
                return hipsPos;
            }
            float verticalDot = Vector3.Dot(diff, -up);
            Vector3 vertical = -up * verticalDot;
            Vector3 lateral = diff - vertical;

            float lateralLen = lateral.magnitude;
            float absVertical = Mathf.Abs(verticalDot);

            if (lateralLen < BasisIKHelpers.k_MinMagnitude || absVertical < BasisIKHelpers.k_MinMagnitude)
            {
                return hipsPos;
            }

            // Current bend angle from head to hips
            float currentAngle = Mathf.Atan2(lateralLen, absVertical) * Mathf.Rad2Deg;
            if (currentAngle <= maxBendDeg)
            {
                return hipsPos;
            }

            // We want lateral / newVertical = tan(maxBend)
            float maxRatio = Mathf.Tan(maxBendDeg * Mathf.Deg2Rad);
            float newVertical = lateralLen / Mathf.Max(maxRatio, BasisIKHelpers.k_MinMagnitude);

            // Push hips further down in the same direction along -up
            float finalVertical = Mathf.Sign(verticalDot) * Mathf.Max(newVertical, absVertical);
            Vector3 newVerticalVec = -up * finalVertical;

            Vector3 newDiff = newVerticalVec + (lateralLen > BasisIKHelpers.k_MinMagnitude ? lateral.normalized * lateralLen : Vector3.zero);
            return headPos + newDiff;
        }
        public void ApplyRotation(AnimationStream stream, BoolProperty enabledProp, ReadWriteTransformHandle handle, Vector4Property targetRotProp, Quaternion RotationOffset)
        {
            if (!handle.IsValid(stream))
            {
                return;
            }

            if (enabledProp.Get(stream))
            {
                handle.SetRotation(stream, BasisIKHelpers.ConvertToQuaternion(targetRotProp.Get(stream)) * RotationOffset);
            }
            else
            {
                BasisIKHelpers.PassThrough(stream, handle);
            }
        }
        static Vector3 ClampHipsAroundHead(Vector3 HeadTargetPos, Vector3 HipsTargetPos, float MinHeadSpineHeight, float minFactor, float maxFactor)
        {
            Vector3 headToHips = HipsTargetPos - HeadTargetPos;
            float sqrMag = headToHips.sqrMagnitude;
            if (sqrMag >= BasisIKHelpers.k_MinSqrMagnitude)
            {
                // Use the head→hips direction as the "up" axis for the clamp
                Vector3 up = headToHips / Mathf.Sqrt(sqrMag);

                float verticalDot = Vector3.Dot(headToHips, up);
                Vector3 vertical = up * verticalDot;
                Vector3 lateral = headToHips - vertical;

                float absY = Mathf.Abs(verticalDot);
                float minY = MinHeadSpineHeight * minFactor;
                float maxY = MinHeadSpineHeight * maxFactor;
                float clampedY = Mathf.Clamp(absY, minY, maxY) * Mathf.Sign(verticalDot);
                vertical = up * clampedY;

                float lateralLen = lateral.magnitude;
                float maxLateral = MinHeadSpineHeight * BasisIKHelpers.k_MaxSpineHorizontalFactor;

                if (lateralLen > maxLateral && lateralLen > BasisIKHelpers.k_LengthEpsilon)
                {
                    lateral *= maxLateral / lateralLen;
                }

                return HeadTargetPos + vertical + lateral;
            }

            return HeadTargetPos + MinHeadSpineHeight * minFactor * Vector3.down; // could also use previous frame’s axis
        }
        public void SolveTwoBoneIKArms(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, AffineTransform target, AffineTransform hint, bool hintWeight, Quaternion targetOffset)
        {
            Vector3 aPosition = root.GetPosition(stream);
            Vector3 bPosition = mid.GetPosition(stream);
            Vector3 cPosition = tip.GetPosition(stream);

            Vector3 targetPos = target.translation;
            Quaternion targetRot = target.rotation;

            Vector3 tPosition = targetPos;
            Quaternion tRotation = targetRot * targetOffset;

            // Segment vectors
            Vector3 ab = bPosition - aPosition;
            Vector3 bc = cPosition - bPosition;
            Vector3 ac = cPosition - aPosition;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float totalLen = abLen + bcLen;

            // Original target vector
            Vector3 atCorrected = tPosition - aPosition;
            float acLen = ac.magnitude;

            float oldAbcAngle = BasisIKHelpers.TriangleAngle(acLen, abLen, bcLen);
            float atCorrectedLen = atCorrected.magnitude;
            float newAbcAngle = BasisIKHelpers.TriangleAngle(atCorrectedLen, abLen, bcLen);
            // Prefer current bend plane; fallbacks to hint / at if collinear.
            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < BasisIKHelpers.k_MinSqrMagnitude)
            {
                axis = hintWeight ? Vector3.Cross(hint.translation - aPosition, bc) : Vector3.zero;
                if (axis.sqrMagnitude < BasisIKHelpers.k_MinSqrMagnitude)
                {
                    axis = Vector3.Cross(atCorrected, bc); // use corrected
                }

                if (axis.sqrMagnitude < BasisIKHelpers.k_MinSqrMagnitude)
                {
                    axis = Vector3.up;
                }
            }
            axis = axis.normalized;

            float a = 0.5f * (oldAbcAngle - newAbcAngle);
            float sin = Mathf.Sin(a);
            float cos = Mathf.Cos(a);
            Quaternion deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);
            mid.SetRotation(stream, deltaR * mid.GetRotation(stream));

            // Re-evaluate after rotating mid
            cPosition = tip.GetPosition(stream);
            ac = cPosition - aPosition;

            // rotate root towards *corrected* direction, not raw tPosition ---
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

                    // you can also soften this threshold if hinting fights with max reach
                    if (abProj.sqrMagnitude > (totalLen * totalLen * BasisIKHelpers.K_Soften) && ahProj.sqrMagnitude > 0f)
                    {
                        Quaternion hintR = QuaternionExt.FromToRotation(abProj, ahProj);
                        hintR = QuaternionExt.NormalizeSafe(hintR);
                        root.SetRotation(stream, hintR * root.GetRotation(stream));
                    }
                }
            }

            tip.SetRotation(stream, tRotation);
        }
        public static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float abSqr = Vector3.Dot(ab, ab);
            if (abSqr <= BasisIKHelpers.k_MinSqrMagnitude)
            {
                return a;
            }

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

            if (dSqr >= rSum * rSum)
            {
                return Vector3.zero;
            }

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
                {
                    normal = Vector3.Normalize(Vector3.Cross(axis, Vector3.right));
                }

                if (normal.sqrMagnitude < BasisIKHelpers.k_MinMagnitude)
                {
                    normal = Vector3.up;
                }
            }

            float d = Mathf.Sqrt(Mathf.Max(dSqr, 0f));
            float penetration = (rSum - d);
            return normal * penetration;
        }
        public static void SwingElbowAroundAC(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, Vector3 desiredB)
        {
            Vector3 A = root.GetPosition(stream);
            Vector3 C = tip.GetPosition(stream);
            Vector3 B = mid.GetPosition(stream);

            Vector3 AC = C - A;
            float acSqr = Vector3.Dot(AC, AC);
            if (acSqr <= BasisIKHelpers.k_MinSqrMagnitude)
            {
                return;
            }

            Vector3 n = AC / Mathf.Sqrt(acSqr);
            Vector3 v1 = B - A; v1 -= n * Vector3.Dot(v1, n);
            Vector3 v2 = desiredB - A; v2 -= n * Vector3.Dot(v2, n);

            float v1Sqr = Vector3.Dot(v1, v1);
            float v2Sqr = Vector3.Dot(v2, v2);
            if (v1Sqr <= BasisIKHelpers.k_MinSqrMagnitude || v2Sqr <= BasisIKHelpers.k_MinSqrMagnitude)
            {
                return;
            }

            v1 /= Mathf.Sqrt(v1Sqr);
            v2 /= Mathf.Sqrt(v2Sqr);

            float dot = Mathf.Clamp(Vector3.Dot(v1, v2), -1f, 1f);
            float ang = Mathf.Acos(dot);
            Vector3 cross = Vector3.Cross(v1, v2);
            float dir = Mathf.Sign(Vector3.Dot(cross, n));
            Quaternion swing = Quaternion.AngleAxis(ang * dir * Mathf.Rad2Deg, n);

            root.SetRotation(stream, swing * root.GetRotation(stream));
        }
        public static Vector3 PushOutFromCapsule(Vector3 p, Vector3 a, Vector3 b, float radiusWithSkin)
        {
            Vector3 q = ClosestPointOnSegment(p, a, b);
            Vector3 qp = p - q;
            float dSqr = Vector3.Dot(qp, qp);
            if (dSqr >= radiusWithSkin * radiusWithSkin)
            {
                return p;
            }

            float d = Mathf.Sqrt(Mathf.Max(dSqr, BasisIKHelpers.k_MinSqrMagnitude));
            Vector3 n = (d > 0f) ? (qp / d) : Vector3.up;
            return q + n * radiusWithSkin;
        }
        /// <summary>
        /// Evaluates the Two-Bone IK algorithm.
        /// </summary>
        /// <param name="stream">The animation stream to work on.</param>
        /// <param name="root">The transform handle for the root transform.</param>
        /// <param name="mid">The transform handle for the mid transform.</param>
        /// <param name="tip">The transform handle for the tip transform.</param>
        /// <param name="target">The transform handle for the target transform.</param>
        /// <param name="hint">The transform handle for the hint transform.</param>
        /// <param name="HasHint">The weight for which hint transform has an effect on IK calculations. This is a value in between 0 and 1.</param>
        /// <param name="targetOffset">The offset applied to the target transform.</param>
        public void SolveTwoBone(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, AffineTransform target, AffineTransform hint, bool HasHint, Quaternion targetOffset, Vector3 BendNormal)
        {
            Vector3 aPosition = root.GetPosition(stream);
            Vector3 bPosition = mid.GetPosition(stream);
            Vector3 cPosition = tip.GetPosition(stream);

            Vector3 targetPos = target.translation;
            Quaternion targetRot = target.rotation;

            Vector3 tPosition = targetPos;
            Quaternion tRotation = targetRot * targetOffset;

            // Segment vectors
            Vector3 ab = bPosition - aPosition;
            Vector3 bc = cPosition - bPosition;
            Vector3 ac = cPosition - aPosition;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float acLen = ac.magnitude;

            float maxReach = abLen + bcLen;
            float oldAbcAngle = BasisIKHelpers.TriangleAngle(acLen, abLen, bcLen);
            Vector3 atCorrected = tPosition - aPosition;
            // Vector3 atCorrected = correctedTargetPos - aPosition;
            float atCorrectedLen = atCorrected.magnitude;

            float newAbcAngle = BasisIKHelpers.TriangleAngle(atCorrectedLen, abLen, bcLen);

            Vector3 axis;
            if (HasHint)
            {
                axis = Vector3.Cross(hint.translation - aPosition, bc);

                if (axis.sqrMagnitude < BasisIKHelpers.k_MinSqrMagnitude)
                {
                    // use corrected vector, not raw tPosition
                    axis = Vector3.Cross(atCorrected, bc);
                }

                if (axis.sqrMagnitude < BasisIKHelpers.k_MinSqrMagnitude)
                {
                    axis = BendNormal;
                }
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

            // Re-evaluate after rotating mid
            cPosition = tip.GetPosition(stream);
            ac = cPosition - aPosition;

            if (atCorrectedLen > BasisIKHelpers.k_LengthEpsilon)
            {
                // Swing root toward corrected target
                root.SetRotation(stream, QuaternionExt.FromToRotation(ac, atCorrected) * root.GetRotation(stream));
            }

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
        public void SolveHand(AnimationStream stream, BoolProperty enabledProp, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, Vector3Property targetPosProp, Vector4Property targetRotProp, Vector3Property hintPosProp, Vector4Property hintRotProp, BoolProperty hintWeightProp, Quaternion targetOffset, ReadWriteTransformHandle chestStart, ReadWriteTransformHandle chestEnd, FloatProperty chestRadius, FloatProperty collisionSkin, BoolProperty collisionsEnabled, FloatProperty handRadius, FloatProperty handSkin, BoolProperty useHandCapsule, BoolProperty protectElbow)
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

            // Read inputs
            Vector3 tgtPos = targetPosProp.Get(stream);
            Quaternion tgtRot = BasisIKHelpers.ConvertToQuaternion(targetRotProp.Get(stream));
            Vector3 hintPos = hintPosProp.Get(stream);
            Quaternion hintRot = BasisIKHelpers.ConvertToQuaternion(hintRotProp.Get(stream));
            bool doCollisions = collisionsEnabled.Get(stream) && chestStart.IsValid(stream) && chestEnd.IsValid(stream);
            if (doCollisions)
            {
                Vector3 a = chestStart.GetPosition(stream);
                Vector3 b = chestEnd.GetPosition(stream);
                float chestR = Mathf.Max(0f, chestRadius.Get(stream) + collisionSkin.Get(stream));
                if (useHandCapsule.Get(stream))
                {
                    float hRad = Mathf.Max(0f, handRadius.Get(stream) + handSkin.Get(stream));
                    // Use the actual current mid & tip positions as the hand capsule ends
                    Vector3 handA = mid.GetPosition(stream);
                    Vector3 handB = tip.GetPosition(stream);

                    Vector3 correction = CapsuleCapsuleResolve(handA, handB, hRad, a, b, chestR);
                    if (correction.sqrMagnitude > 0f)
                    {
                        // Move the IK target & hint by the same correction
                        tgtPos += correction;
                        hintPos += correction * 0.25f; // steer elbow slightly
                    }
                }
                else
                {
                    tgtPos = PushOutFromCapsule(tgtPos, a, b, chestR);
                    Vector3 nudgedHint = PushOutFromCapsule(hintPos, a, b, chestR * 0.9f);
                    hintPos = Vector3.Lerp(hintPos, nudgedHint, 0.6f);
                }
            }
            var target = new AffineTransform(tgtPos, tgtRot);
            var hint = new AffineTransform(hintPos, hintRot);
            // First solve (arms variant to preserve wrist)
            SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hintWeightProp.Get(stream), targetOffset);
            // Optional elbow protection pass
            if (protectElbow.Get(stream) && doCollisions)
            {
                Vector3 a = chestStart.GetPosition(stream);
                Vector3 b = chestEnd.GetPosition(stream);
                float chestR = Mathf.Max(0f, chestRadius.Get(stream) + collisionSkin.Get(stream));

                Vector3 B = mid.GetPosition(stream);
                Vector3 pushedB = PushOutFromCapsule(B, a, b, chestR);
                if ((pushedB - B).sqrMagnitude > BasisIKHelpers.k_DivisionSafetyEpsilon)
                {
                    SwingElbowAroundAC(stream, root, mid, tip, pushedB);
                    // Re-lock wrist to target after elbow swing
                    SolveTwoBoneIKArms(stream, root, mid, tip, target, hint, hintWeightProp.Get(stream), targetOffset);
                }
            }
        }
        public void SolveHipsAndSpine(AnimationStream stream,Vector3 headTargetPos, Vector3 hipsTargetPos, Vector4Property targetRotationHips, Vector4Property offsetRotationHips, BoolProperty EnableSpineIK, ReadWriteTransformHandle HandleHips, ReadWriteTransformHandle HandleChest, ReadWriteTransformHandle HandleNeck, ReadWriteTransformHandle HandleHead, Vector3Property targetPositionHead, Vector4Property targetRotationHead, Quaternion targetOffsetHead, Vector3Property bendNormalHead)
        {
            hipsTargetPos = EnforceSpineBendLimit(headTargetPos, hipsTargetPos, maxBendDeg.Get(stream), Vector3.up);
            hipsTargetPos = ClampHipsAroundHead(headTargetPos, hipsTargetPos, MinHeadSpineHeight.Get(stream), minFactor.Get(stream), maxFactor.Get(stream));

            if (!EnableSpineIK.Get(stream))
            {
                BasisIKHelpers.Pass(stream, HandleChest, HandleNeck, HandleHead);
                BasisIKHelpers.PassThrough(stream, HandleHips);
                return;
            }
            // Apply hips driver if valid
            if (HandleHips.IsValid(stream))
            {
                Quaternion hipRot = BasisIKHelpers.ConvertToQuaternion(targetRotationHips.Get(stream));
                Quaternion hipOff = BasisIKHelpers.ConvertToQuaternion(offsetRotationHips.Get(stream));

                HandleHips.SetPosition(stream, hipsTargetPos);
                HandleHips.SetRotation(stream, hipRot * hipOff);
            }

            // Validate required upper chain handles (Burst-safe: no params/arrays)
            if (HandleChest.IsValid(stream) & HandleNeck.IsValid(stream) & HandleHead.IsValid(stream))
            {
                // Build target + hint transforms
                Quaternion tRot = BasisIKHelpers.ConvertToQuaternion(targetRotationHead.Get(stream));
                AffineTransform target = new AffineTransform(targetPositionHead.Get(stream), tRot);
                Vector3 bendNormal = bendNormalHead.Get(stream);

                SolveTwoBoneSpine(stream, HandleChest, HandleNeck, HandleHead, target, targetOffsetHead, bendNormal);

                if (hintWeightHead.Get(stream) && HandleChest.IsValid(stream))
                {
                    // Neck rotation produced by your spine IK pass – we keep this
                    Quaternion neckRot = HandleNeck.IsValid(stream) ? HandleNeck.GetRotation(stream) : Quaternion.identity;
                    // Spine as an extra reference if available (nice stabiliser)
                    Quaternion spineRot = HandleSpine.IsValid(stream) ? HandleSpine.GetRotation(stream) : neckRot;
                    // Raw chest from tracker
                    Quaternion trackerChestRot = BasisIKHelpers.ConvertToQuaternion(hintRotationHead.Get(stream)) * targetOffsetChest;

                    float MaxChestDelta = MaxChestDeltaDeg.Get(stream);
                    // Clamp relative to neck and spine
                    Quaternion clampedChestRot = BasisIKHelpers.ClampRotation(trackerChestRot, neckRot, MaxChestDelta);
                    clampedChestRot = BasisIKHelpers.ClampRotation(clampedChestRot, spineRot, MaxChestDelta);

                    HandleChest.SetRotation(stream, clampedChestRot);

                    // Build target + hint transforms
                    tRot = BasisIKHelpers.ConvertToQuaternion(targetRotationHead.Get(stream));
                    target = new AffineTransform(targetPositionHead.Get(stream), tRot);

                    SolveTwoBoneSpine(stream, HandleChest, HandleNeck, HandleHead, target, targetOffsetHead, bendNormal);
                }
            }
            else
            {
                BasisIKHelpers.Pass(stream, HandleChest, HandleNeck, HandleHead);
                return;
            }
        }
        public void SolveTwoBoneSpine(AnimationStream stream, ReadWriteTransformHandle root, ReadWriteTransformHandle mid, ReadWriteTransformHandle tip, AffineTransform target, Quaternion targetOffset, Vector3 bendNormal)
        {
            // Read current joint positions
            Vector3 aPos = root.GetPosition(stream);
            Vector3 bPos = mid.GetPosition(stream);
            Vector3 cPos = tip.GetPosition(stream);

            // Target with offset applied in target space
            Vector3 tPos = target.translation;
            Quaternion tRot = target.rotation * targetOffset;

            // Current bone vectors
            Vector3 ab = bPos - aPos;
            Vector3 bc = cPos - bPos;
            Vector3 ac = cPos - aPos;
            Vector3 at = tPos - aPos;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float acLen = ac.magnitude;
            float atLen = at.magnitude;
            float oldAbcAngle = BasisIKHelpers.TriangleAngle(acLen, abLen, bcLen);
            float newAbcAngle = BasisIKHelpers.TriangleAngle(atLen, abLen, bcLen);

            // Compute rotation axis for mid joint bend
            Vector3 axis = BasisIKHelpers.ComputeIkAxis(bendNormal);

            // Rotate mid joint by half the angle delta (distributes motion)
            float halfAngle = 0.5f * (oldAbcAngle - newAbcAngle);
            float s = Mathf.Sin(halfAngle);
            float c = Mathf.Cos(halfAngle);
            Quaternion deltaMid = new Quaternion(axis.x * s, axis.y * s, axis.z * s, c);
            mid.SetRotation(stream, deltaMid * mid.GetRotation(stream));

            // Re-evaluate and swing root so AC aligns with AT
            cPos = tip.GetPosition(stream);
            ac = cPos - aPos;
            root.SetRotation(stream, QuaternionExt.FromToRotation(ac, at) * root.GetRotation(stream));

            // Set tip rotation to match target orientation (+offset)
            tip.SetRotation(stream, tRot);
        }
    }
}
