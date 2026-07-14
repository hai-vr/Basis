namespace UnityEngine.Animations.Rigging
{
    public struct BasisLegSolveInput
    {
        public Vector3 Root;
        public Vector3 Mid;
        public Vector3 Tip;
        public Quaternion RootRotation;
        public Quaternion MidRotation;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public Vector3 HintPosition;
        public float HintWeight;

        /// <summary>How much to DISTRUST HintPosition's direction, 0..1. Zero (the struct default) trusts it
        /// completely, so every existing caller is unchanged. Eases the pole onto the BendNormal pole, which
        /// is NOT the same as fading HintWeight — that is discontinuous at zero (see the blend in Solve).</summary>
        public float HintDistrust;
        public Quaternion TargetOffset;
        public Vector3 BendNormal;
    }

    public struct BasisLegSolveResult
    {
        public Quaternion MidDelta;
        public Quaternion RootDelta;
        public Quaternion HintDelta;
        public Quaternion TipRotation;
        public bool HintApplied;

        public Vector3 KneeSolved;
        public Vector3 FootSolved;
        public Quaternion RootRotationSolved;
        public Quaternion MidRotationSolved;

        public float UpperLength;
        public float LowerLength;
        public float TargetDistance;
        public float ReachRatio;
        public float KneeAngleDeg;
        public byte AxisSource;     // 0 plane-normal, 1 hint (straight leg), 2 target (straight leg), 3 bend-normal, 4 pole blended toward bend-normal, 5 pole clamped into the anterior half-space
        public float FootError;
    }

    // Three steps, and each one owns exactly one degree of freedom:
    //
    //   BEND   rotate the shin about the knee  -> sets the hip->ankle DISTANCE
    //   AIM    rotate the leg about the hip    -> sets the hip->ankle DIRECTION
    //   SWIVEL spin the leg about hip->ankle   -> sets WHERE ON ITS CIRCLE the knee sits (the pole)
    //
    // Keeping them separate is what makes the foot land on its target. The previous version steered the knee
    // in the BEND step, by bending about a hint-derived axis, and that is a length error dressed up as a pole
    // choice -- see the comment on the bend below.
    public static class BasisLegSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;
        const float k_PoleColinearSin = 0.5f; // sin(30deg): a hint nearer the leg axis than this is unreliable

        public const float MinKneeInteriorDeg = 20f; // max human knee flexion ~160deg; folding past this drives the calf through the thigh

        // ANTERIOR HALF-SPACE: how far the knee may swivel away from "in front of the leg" before the guard bites.
        //
        // A knee is a HINGE. It flexes one way -- heel toward the backside -- so the knee ALWAYS bulges anterior to
        // the hip->ankle axis. A knee behind that axis is not "unnatural", it is anatomically unrepresentable, so
        // the guard is a hard constraint rather than a tuning knob (same status as MinKneeInteriorDeg above).
        //
        // The angles are measured from ANTERIOR, about the hip->ankle axis:
        //   0 deg  = knee dead ahead of the leg      90 deg = knee dead lateral      180 deg = knee INVERTED
        // so `dot(kneeDir, anterior) > 0` is exactly `|swivel| < 90`.
        //
        //   SOFT (85): everything below passes through EXACTLY untouched.
        //   HARD (89.5): the asymptote. Strictly under 90, so dot(kneeDir, anterior) stays strictly POSITIVE -- the
        //                posterior half-space is not merely discouraged, it is unreachable.
        //
        // The soft band sits at 85 and not lower because 90deg is NOT an inversion -- it is a knee pointing dead
        // LATERAL, which is a legal (if extreme) pose, and the codebase already contracts to honour it:
        // BasisLegHintReachTests.KneeHint_StillActuallySwivelsTheKnee parks the hint at exactly +X, 90deg off
        // anterior, and demands the knee follow it to within 15deg. Only PAST 90 does the knee go through the joint.
        // So the guard has to bite as late as it possibly can while still asymptoting under 90: a 90deg lateral hint
        // is nudged just 2.6deg, butterfly splay (60deg) and hip abduction (~45deg) are not touched at all, and an
        // outright inverting hint still cannot reach the far side. Set SOFT lower and the guard starts bending legal
        // poses to fix an illegal one, which is the wrong trade.
        public const float KneeAnteriorSoftDeg = 85f;
        public const float KneeAnteriorHardDeg = 89.5f;

        // Compress a knee swivel (degrees from anterior) into the anterior half-space.
        //
        // Below `softDeg` this is the IDENTITY -- byte for byte, so a legitimate pose is not perturbed by a guard
        // that has no business firing. Past it, the excess is squashed through a rational saturation that has slope
        // exactly 1 where it takes over and asymptotes to `hardDeg`:
        //
        //     out = soft + M * e / (M + e),   e = |x| - soft,   M = hard - soft
        //
        // Slope-1 at the handover means NO derivative step: the knee eases into the limit instead of hitting a wall,
        // which is the difference between a guard and a jerk. The asymptote means the limit is never even reached,
        // let alone crossed, so `dot > 0` holds by construction and not by a tolerance. And it is a rational
        // function -- no transcendentals, safe and cheap inside a Burst job.
        //
        // Shared, not duplicated: this is the ONE implementation, called by the solve (which places the knee) and by
        // BasisSwivelSmootherCore (which can move it afterwards). Two copies of an anatomical constraint is exactly
        // the hand-mirror failure that has bitten this codebase repeatedly -- the sweep and the runtime silently
        // disagreeing about what the rule was.
        public static float ClampKneeSwivelDeg(float swivelDeg, float softDeg, float hardDeg)
        {
            float mag = swivelDeg < 0f ? -swivelDeg : swivelDeg;

            // Reject-unless-good. NaN fails every ordered comparison, so `!(mag >= 0f)` is TRUE for NaN and sends it
            // to the safe anterior default. Written the other way round (`if (float.IsNaN(mag))` aside), a guard of
            // the shape `if (mag < 0f) return ...` would let NaN sail straight through into the bone.
            if (!(mag >= 0f))
            {
                return 0f;
            }

            if (!(mag > softDeg))
            {
                return swivelDeg;   // inside the free band: identity, exactly
            }

            float maxExcess = hardDeg - softDeg;
            if (!(maxExcess > 0f))
            {
                return swivelDeg < 0f ? -softDeg : softDeg;   // degenerate limits: fall back to a hard clamp
            }

            float excess = mag - softDeg;
            float compressed = softDeg + maxExcess * excess / (maxExcess + excess);
            return swivelDeg < 0f ? -compressed : compressed;
        }

        public static void Solve(in BasisLegSolveInput i, out BasisLegSolveResult r)
        {
            r = default;

            Vector3 aPosition = i.Root;
            Vector3 bPosition = i.Mid;
            Vector3 cPosition = i.Tip;
            Quaternion rootRot = i.RootRotation;
            Quaternion midRot = i.MidRotation;

            Vector3 tPosition = i.TargetPosition;
            Quaternion tRotation = i.TargetRotation * i.TargetOffset;

            float hintWeight = i.HintWeight;
            bool hasHint = hintWeight > 0f;

            Vector3 ab = bPosition - aPosition;
            Vector3 bc = cPosition - bPosition;
            Vector3 ac = cPosition - aPosition;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float acLen = ac.magnitude;

            float maxReach = abLen + bcLen;
            float oldAbcAngle = TriangleAngle(acLen, abLen, bcLen);
            Vector3 atCorrected = tPosition - aPosition;
            float atCorrectedLen = atCorrected.magnitude;

            // Anatomical max-flexion clamp: a human knee can't fold past MinKneeInteriorDeg of interior
            // angle (the calf would pass through the thigh). If the target would over-fold the knee, hold
            // the foot at the max-flexion distance along the target direction instead of letting it intersect.
            float minFlexReach = MinFlexionReach(abLen, bcLen);
            if (atCorrectedLen < minFlexReach) atCorrectedLen = minFlexReach;

            float newAbcAngle = TriangleAngle(atCorrectedLen, abLen, bcLen);

            // ---------------------------------------------------------------------------------------------
            // BEND -- rotate the shin about the knee until the hip->ankle distance equals atCorrectedLen.
            //
            // TriangleAngle read that angle off the law of cosines on the hip-knee-ankle triangle, so the
            // rotation that realises it MUST be about that triangle's own normal, Cross(ab, bc). Any other
            // axis tilts the shin OUT of the plane: the interior angle at the knee does not land where the
            // maths assumed, |ac| is not the length we solved for, and the AIM step that follows can only fix
            // the DIRECTION to the target -- it has no length authority whatsoever. The leg used to bend about
            // Cross(hint, bc) instead, then slerp that toward BendNormal twice more, so the axis was never the
            // plane normal and the foot never quite arrived: 145 mm off on a hint swept around the leg axis,
            // 27 mm on real mocap. The arm has the same error and hides it behind a 12-iteration bisection.
            //
            // Steering the knee is the SWIVEL's job, further down. It was being done here, and the length paid.
            //
            // The fallbacks below only run when the leg is ALREADY straight, where the plane is undefined and
            // ANY axis perpendicular to bc is a legal plane normal -- so they stay exact. They only choose
            // which way a straight leg folds.
            // ---------------------------------------------------------------------------------------------
            byte axisSource = 0;
            Vector3 bendAxis = Vector3.Cross(ab, bc);
            if (bendAxis.sqrMagnitude < k_SqrEpsilon)
            {
                if (hasHint)
                {
                    bendAxis = Vector3.Cross(i.HintPosition - aPosition, bc);
                    axisSource = 1;
                }

                if (bendAxis.sqrMagnitude < k_SqrEpsilon)
                {
                    bendAxis = Vector3.Cross(atCorrected, bc);
                    axisSource = 2;
                }

                if (bendAxis.sqrMagnitude < k_SqrEpsilon)
                {
                    // Orthogonalise against bc so BendNormal is a legal plane normal here too.
                    Vector3 bcN = bcLen > k_Epsilon ? bc / bcLen : Vector3.zero;
                    bendAxis = i.BendNormal - bcN * Vector3.Dot(i.BendNormal, bcN);
                    axisSource = 3;

                    if (bendAxis.sqrMagnitude < k_SqrEpsilon)
                    {
                        bendAxis = i.BendNormal;
                    }
                }
            }

            bendAxis = Vector3.Normalize(bendAxis);

            float half = 0.5f * (oldAbcAngle - newAbcAngle);
            float sinHalf = Mathf.Sin(half);
            float cosHalf = Mathf.Cos(half);
            Quaternion deltaR = new Quaternion(bendAxis.x * sinHalf, bendAxis.y * sinHalf, bendAxis.z * sinHalf, cosHalf);

            midRot = deltaR * midRot;
            cPosition = bPosition + deltaR * (cPosition - bPosition);
            ac = cPosition - aPosition;   // |ac| == atCorrectedLen now, exactly

            // ---------------------------------------------------------------------------------------------
            // AIM -- swing the whole leg about the hip so the ankle points at the target. The length already
            // matches, so this puts the foot ON the target. FromToRotation is the right tool here and only
            // here: this is a genuine "rotate A onto B", where any perpendicular axis is a correct answer.
            // ---------------------------------------------------------------------------------------------
            Quaternion rootDelta = Quaternion.identity;
            if (atCorrectedLen > k_Epsilon)
            {
                rootDelta = QuaternionExt.FromToRotation(ac, atCorrected);
                rootRot = rootDelta * rootRot;
                bPosition = aPosition + rootDelta * (bPosition - aPosition);
                cPosition = aPosition + rootDelta * (cPosition - aPosition);
                midRot = rootDelta * midRot;
            }

            // ---------------------------------------------------------------------------------------------
            // SWIVEL -- spin the leg about the hip->ankle axis to bring the knee onto its pole.
            //
            // The axis is NAMED (acNorm), not discovered. A rotation about acNorm cannot move a point lying on
            // acNorm, and the ankle is one -- so reach preservation is structural, true at every weight, and
            // not merely something that happens to hold away from the singularity.
            //
            // QuaternionExt.FromToRotation(abProj, ahProj) used to do this job. It agrees with the named axis
            // in the general case: both inputs are perpendicular to acNorm, so their cross product lies along
            // it. But when the two go ANTI-PARALLEL -- a hint pointing straight across the leg from the knee,
            // which the sweep reaches every single revolution -- Unity's implementation abandons the plane and
            // returns 180 deg about Cross(from, Vector3.right), an arbitrary WORLD axis. Spinning the leg about
            // that carries the ankle straight off its target.
            //
            // The angle is measured from kneeDir: the knee's direction RECONSTRUCTED from the plane the BEND
            // just used, rather than MEASURED off the knee's own lever arm. The two are the same vector --
            //     Cross(ac, Cross(ab, bc)) == |ac|^2 * abProj
            // is an identity (expand the triple product with ac = ab + bc) -- but only one of them survives a
            // straight leg. abProj is the knee's stand-off from the hip->ankle axis, so it VANISHES as the leg
            // extends, and its direction with it. The bend axis is perpendicular to that axis by construction,
            // so their cross product is a UNIT vector at every extension and cannot go to noise.
            //
            // That distinction is the whole bug. The BEND chooses a plane and then flattens the leg into it --
            // destroying the very quantity the SWIVEL then reads back to find that same plane. At full reach
            // TriangleAngle saturates at 180 deg, the leg is commanded dead straight, abProj collapses to float
            // residue, and SignedAngleRad reads a direction that is pure noise. The resulting swivel is applied
            // about acNorm -- which at full extension IS the thigh's own long axis -- so it lands as a PURE ROLL
            // of the thigh and the calf. Measured: a 1 mm nudge of the foot target rolled both bones 180 deg,
            // with the foot still exactly on target and the knee, having no lever arm left, barely moving. Every
            // gate in this repo scores positions, so all of them passed while the leg spun.
            //
            // And standing lives there: footHeightOffset is clamped so the legs fully extend, which parks
            // hip->foot at ~= thigh + shin permanently.
            // ---------------------------------------------------------------------------------------------
            Quaternion hintR = Quaternion.identity;
            bool hintApplied = false;

            Vector3 acFinal = cPosition - aPosition;
            float acFinalSqr = acFinal.sqrMagnitude;
            if (acFinalSqr > k_SqrEpsilon)
            {
                Vector3 acNorm = acFinal / Mathf.Sqrt(acFinalSqr);
                Vector3 kneeDir = Vector3.Cross(acNorm, rootDelta * bendAxis);
                kneeDir -= acNorm * Vector3.Dot(kneeDir, acNorm);

                // The pole BendNormal implies: bending about BendNormal swings the knee THIS way.
                // (Leg: BendNormal = hips-right, acNorm = down, so bendPole = forward. As it should be.)
                Vector3 bendPole = Vector3.Cross(acNorm, i.BendNormal);
                bendPole -= acNorm * Vector3.Dot(bendPole, acNorm);
                bool hasBendPole = bendPole.sqrMagnitude > k_SqrEpsilon;

                Vector3 pole = bendPole;
                if (hasHint)
                {
                    Vector3 ah = i.HintPosition - aPosition;
                    Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);
                    float ahLen = ah.magnitude;

                    if (ahProj.sqrMagnitude > k_SqrEpsilon)
                    {
                        pole = ahProj;
                    }

                    // Pole-vector singularity: as the hint closes on the leg axis its perpendicular component
                    // shrinks and its DIRECTION goes to noise, which used to snap the knee backward. Ease onto
                    // the BendNormal pole rather than trust it. Same intent as the old axis blend -- but done
                    // to the POLE, where it costs nothing, instead of to the BEND AXIS, where it cost reach.
                    float poleSin = ahLen > k_Epsilon ? ahProj.magnitude / ahLen : 0f;
                    if (poleSin < k_PoleColinearSin && hasBendPole && pole.sqrMagnitude > k_SqrEpsilon)
                    {
                        float blend = 1f - poleSin / k_PoleColinearSin;
                        pole = Vector3.Slerp(pole.normalized, bendPole.normalized, blend);
                        axisSource = 4;
                    }

                    // Same easing, second reason: the hint above is untrustworthy GEOMETRICALLY (it has closed
                    // on the leg axis); HintDistrust says it is untrustworthy STATISTICALLY. Applied to the POLE
                    // and never to HintWeight -- the weight is discontinuous at zero, because the solve stops
                    // steering as w->0+ and then jumps onto bendPole at w==0.
                    if (i.HintDistrust > 0f && hasBendPole && pole.sqrMagnitude > k_SqrEpsilon)
                    {
                        pole = Vector3.Slerp(pole.normalized, bendPole.normalized, Mathf.Clamp01(i.HintDistrust));
                        axisSource = 5;
                    }
                }

                pole -= acNorm * Vector3.Dot(pole, acNorm);   // the pole-colinear slerp can tilt it; back into the swing plane

                // -----------------------------------------------------------------------------------------
                // ANTERIOR HALF-SPACE GUARD -- a knee cannot bend backwards, so make that unreachable.
                //
                // This swivel rotates the knee ONTO the pole, so at full weight the knee's final direction IS the
                // pole direction. Guarding the POLE therefore guards the KNEE, structurally: there is no path by
                // which the solve can place the knee somewhere the pole is forbidden to point.
                //
                // `bendPole` is the anterior reference and it is already in the swing plane -- Cross(acNorm,
                // BendNormal) with BendNormal = hips-right and acNorm = hip->ankle, which is "in front of the leg,
                // as seen from the leg's own axis". It is a BODY-frame quantity, so it co-rotates with the player
                // and cannot go stale on a turn (a world-frame reference here would lag every yaw -- the defect
                // BasisSwivelSmootherCore exists to remove).
                //
                // WHY THIS FIRES: the hint is a real tracker, and a real knee is never behind the leg. But push the
                // foot forward and the leg EXTENDS -- the knee's lever arm off the axis collapses toward zero, and
                // at ~1.0 reach a few millimetres of tracker offset is enough to tip the projected pole across the
                // axis and land the knee posterior. The leg inverts. That is the "extension 0.999 inversion" defect,
                // and it is the same pole singularity as the swivel snap; this is its other half. The BEND step
                // cannot save us either, because it saturates at a straight leg rather than refusing -- so the only
                // place the inversion can be stopped is here, at the pole.
                // -----------------------------------------------------------------------------------------
                if (hasBendPole && pole.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 anterior = bendPole.normalized;
                    float poleDeg = SignedAngleRad(anterior, pole, acNorm) * Mathf.Rad2Deg;
                    float guardedDeg = ClampKneeSwivelDeg(poleDeg, KneeAnteriorSoftDeg, KneeAnteriorHardDeg);

                    // Only rebuild when the guard actually bit. ClampKneeSwivelDeg returns its argument unchanged
                    // inside the free band, so this compares equal and every legitimate pose takes the untouched
                    // path -- no normalisation, no float drift, bit-for-bit the pre-guard solver.
                    // A NaN pole angle lands here too: the clamp returns 0, and `0f != NaN` is true, so the knee is
                    // rebuilt pointing dead ahead rather than being fed NaN.
                    if (guardedDeg != poleDeg)
                    {
                        pole = AngleAxisRad(guardedDeg * Mathf.Deg2Rad, acNorm) * anterior;
                        axisSource = 5;
                    }
                }

                // No near-extension fade, and no near-extension blend back toward BendNormal. Both used to live
                // here, and both were guarding the WRONG QUANTITY -- the same mistake, one level up, that the
                // snap fix was about.
                //
                // Fading bought nothing and cost plenty. It let go of the knee exactly where the knee most
                // needed holding: at 95.7% extension it was surrendering 57% of a real butterfly hint, and at
                // 99.9% it switched the swivel off entirely and left the knee wherever the bend had put it --
                // including bent BACKWARD through the joint, which is the inversion the guard was there to stop.
                //
                // Nor is a fade the right answer to the ill-conditioning that IS here. A fade would have had to
                // ramp on |abProj|, and |abProj| is not a confidence -- it is a LEVER ARM. It vanishes on a leg
                // that is merely straight, which is a perfectly ordinary, perfectly well-determined pose, not an
                // uncertain one. Reconstructing kneeDir from the bend plane removes the collapse instead of
                // ramping across it: the pole keeps full authority at every extension, the knee is always on it,
                // and the CIRCLE it sits on shrinks to nothing on its own, continuously. Nothing is released to
                // go and find a different pole, and nothing is left for a tuning constant to get wrong.
                float weight = hasHint ? hintWeight : 1f;

                if (weight > 0f && kneeDir.sqrMagnitude > k_SqrEpsilon && pole.sqrMagnitude > k_SqrEpsilon)
                {
                    float swivel = ScaleSwivel(SignedAngleRad(kneeDir, pole, acNorm), weight);
                    hintR = AngleAxisRad(swivel, acNorm);

                    rootRot = hintR * rootRot;
                    bPosition = aPosition + hintR * (bPosition - aPosition);
                    cPosition = aPosition + hintR * (cPosition - aPosition);
                    midRot = hintR * midRot;
                    hintApplied = hasHint;
                }
            }

            r.MidDelta = deltaR;
            r.RootDelta = rootDelta;
            r.HintDelta = hintR;
            r.TipRotation = tRotation;
            r.HintApplied = hintApplied;

            r.KneeSolved = bPosition;
            r.FootSolved = cPosition;
            r.RootRotationSolved = rootRot;
            r.MidRotationSolved = midRot;

            r.UpperLength = abLen;
            r.LowerLength = bcLen;
            r.TargetDistance = atCorrectedLen;
            r.ReachRatio = (maxReach > k_Epsilon) ? atCorrectedLen / maxReach : 0f;
            r.KneeAngleDeg = AngleDeg(aPosition - bPosition, cPosition - bPosition);
            r.AxisSource = axisSource;
            r.FootError = (cPosition - tPosition).magnitude;
        }

        // Signed angle from `from` to `to` measured about `axis` (normalized). Both vectors are already in the
        // plane perpendicular to `axis`, so this is exact. Written the long way rather than via
        // Vector3.SignedAngle / Quaternion.AngleAxis because this runs inside a Burst job.
        //
        // NaN-safe by shape: `!(denom > k_Epsilon)` takes the reject branch on NaN, where `denom < k_Epsilon`
        // would have let it through -- NaN fails every ordered comparison, so a guard has to be written as
        // "reject unless good", never "reject if bad".
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

        // Apply partial weight to a swivel the way this solver always has.
        //
        // The old code scaled the hint quaternion's VECTOR part by the weight and renormalized. That is not a
        // linear scale of the angle -- it maps t -> 2*atan(w*tan(t/2)), a curve that runs away toward the FULL
        // swivel as t nears 180 deg however small w gets (at 170 deg, w=0.5 still delivers 160 deg, where a
        // linear scale would give 85). Callers are tuned against that curve: scaling the angle linearly instead
        // cost the butterfly knee half its outward swing. So keep the curve exactly, and change only the AXIS,
        // which is the thing that was actually wrong.
        static float ScaleSwivel(float radians, float weight)
        {
            if (weight >= 1f)
            {
                return radians;
            }

            if (!(weight > 0f))   // NaN-safe: NaN fails this and lands here, not in the branch above
            {
                return 0f;
            }

            return 2f * Mathf.Atan(weight * Mathf.Tan(0.5f * radians));
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

        // Chord (root->tip distance) at the max-flexion interior angle -- the closest the foot can get to
        // the hip before the knee over-folds. Law of cosines on the two segments.
        static float MinFlexionReach(float upper, float lower)
        {
            float c = Mathf.Cos(MinKneeInteriorDeg * Mathf.Deg2Rad);
            float d2 = upper * upper + lower * lower - 2f * upper * lower * c;
            return d2 > 0f ? Mathf.Sqrt(d2) : 0f;
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
