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
        public byte AxisSource;     // 0 plane-normal, 1 hint (straight leg), 2 target (straight leg), 3 bend-normal, 4 pole blended toward bend-normal
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
            // ---------------------------------------------------------------------------------------------
            Quaternion hintR = Quaternion.identity;
            bool hintApplied = false;

            Vector3 acFinal = cPosition - aPosition;
            float acFinalSqr = acFinal.sqrMagnitude;
            if (acFinalSqr > k_SqrEpsilon)
            {
                Vector3 acNorm = acFinal / Mathf.Sqrt(acFinalSqr);
                Vector3 abFinal = bPosition - aPosition;
                Vector3 abProj = abFinal - acNorm * Vector3.Dot(abFinal, acNorm);

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
                }

                pole -= acNorm * Vector3.Dot(pole, acNorm);   // the pole-colinear slerp can tilt it; back into the swing plane

                // No near-extension fade, and no near-extension blend back toward BendNormal. Both used to live
                // here, and both were guarding the WRONG QUANTITY -- the same mistake, one level up, that the
                // snap fix was about.
                //
                // What goes to noise as the leg straightens is abProj: the MEASURED direction of the current
                // knee, whose lever arm is collapsing. The POLE does not -- it is commanded, by a tracker or by
                // BendNormal, and it stays perfectly well-defined at every extension. And because this swivel
                // rotates abProj ONTO the pole, the knee's final direction does not depend on where it started:
                // a noisy abProj buys a noisy swivel ANGLE, but still lands the knee exactly on the pole. Only
                // the leg's twist is affected, and only for the one frame it takes to converge.
                //
                // So fading here bought nothing and cost plenty. It let go of the knee exactly where the knee
                // most needed holding: at 95.7% extension it was surrendering 57% of a real butterfly hint, and
                // at 99.9% it switched the swivel off entirely and left the knee wherever the bend had put it --
                // including bent BACKWARD through the joint, which is the inversion the guard was there to stop.
                //
                // The snap it was originally added for cannot happen in this structure. The knee is always on
                // its commanded pole; as the leg straightens, the CIRCLE it sits on shrinks to nothing on its
                // own, continuously. Nothing is ever released to go and find a different pole.
                float weight = hasHint ? hintWeight : 1f;

                if (weight > 0f && abProj.sqrMagnitude > k_SqrEpsilon && pole.sqrMagnitude > k_SqrEpsilon)
                {
                    float swivel = ScaleSwivel(SignedAngleRad(abProj, pole, acNorm), weight);
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
