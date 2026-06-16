namespace UnityEngine.Animations.Rigging
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
        public float HintMaxStepDeg;   // max elbow-swivel change this solve; float.MaxValue = unclamped (offline)
        public bool HintIsTracker;     // hint is a REAL elbow tracker (trust it further before the down-stabilizer overrides); false = lookup-derived
    }

    public struct BasisArmSolveResult
    {
        // Apply through the AnimationStream in this order; identity steps are exact no-ops:
        //   mid.SetRotation(MidDelta * mid.GetRotation), root.SetRotation(RootDelta * ...),
        //   root.SetRotation(HintDelta * ...), tip.SetRotation(TipRotation).
        public Quaternion MidDelta;
        public Quaternion RootDelta;
        public Quaternion HintDelta;
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
    }

    // Stream-free geometry shared by BasisFullIKConstraintJob.SolveTwoBoneIKArms and the
    // offline sweep harness. Change the elbow math HERE so both stay in lock-step.
    public static class BasisArmSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        // Anatomical elbow flexion range, as the angle at the elbow between the upper arm and the forearm.
        // 180 deg = arm straight; small = forearm folded toward the upper arm. A human elbow cannot
        // hyperextend past straight, nor fold the forearm fully into the upper arm (~25-30 deg is the limit).
        public const float MinElbowAngleDeg = 23f;
        public const float MaxElbowAngleDeg = 180f;

        public static void Solve(in BasisArmSolveInput i, out BasisArmSolveResult r)
        {
            r = default;

            Vector3 aPosition = i.Shoulder;
            Vector3 bPosition = i.Elbow;
            Vector3 cPosition = i.Hand;
            Quaternion rootRot = i.RootRotation;
            Quaternion midRot = i.MidRotation;

            Vector3 tPosition = i.TargetPosition;
            Quaternion tRotation = i.TargetRotation * i.TargetOffset;

            // Segment vectors (rest pose)
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

            // Clamp to the anatomical elbow flexion range. The triangle solve already caps extension at
            // straight (180 deg); the lower clamp stops the forearm folding impossibly far into the upper
            // arm for a too-close target -- the joint holds at min flex and the hand falls short (pushed
            // out along the target direction) instead of bending the elbow past human range.
            newAbcAngle = Mathf.Clamp(newAbcAngle, MinElbowAngleDeg * Mathf.Deg2Rad, MaxElbowAngleDeg * Mathf.Deg2Rad);

            // Bend in the ARM plane. Cross(ab,bc) is the shoulder-elbow-hand plane normal, which the
            // triangle solve REQUIRES so deltaR changes |ac| to exactly the target distance. Seeding this
            // axis from the hint (as the leg does) tilts it off the arm plane, so deltaR rotates the hand
            // out of plane and it over/undershoots the target. The hint instead follows via the swivel
            // (hintR) below, which rotates about the shoulder->hand axis and so preserves reach exactly.
            // Stable-down-pole / hint / shoulder->target / player-up are collinear-only fallbacks (arm near-straight).
            byte axisSource = 0;
            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                // Arm dead straight: the bend plane is undefined. Bend toward a STABLE pole (the elbow hangs
                // down, perpendicular to the shoulder->hand axis) FIRST, so a fully-stretched arm settles
                // instead of thrashing between the collinear fallbacks below -- which, reaching BACKWARD, are
                // themselves near-parallel to the backward forearm and so flip the bend plane frame-to-frame.
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

            float a = 0.5f * (oldAbcAngle - newAbcAngle);
            float sin = Mathf.Sin(a);
            float cos = Mathf.Cos(a);
            Quaternion deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);

            // mid.SetRotation(deltaR * midRot): tip rotates about the elbow pivot.
            midRot = deltaR * midRot;
            cPosition = bPosition + deltaR * (cPosition - bPosition);
            ac = cPosition - aPosition;

            // --- rotate root toward the corrected target direction ---
            Quaternion rootDelta = Quaternion.identity;
            if (atCorrectedLen > k_Epsilon)
            {
                rootDelta = QuaternionExt.FromToRotation(ac, atCorrected);
                rootRot = rootDelta * rootRot;
                // Propagate root rotation to its children (mid + tip), pivoting about A.
                bPosition = aPosition + rootDelta * (bPosition - aPosition);
                cPosition = aPosition + rootDelta * (cPosition - aPosition);
                midRot = rootDelta * midRot;
            }

            Quaternion hintR = Quaternion.identity;
            bool hintApplied = false;
            float hintFade = 0f;
            float hintProjMag = 0f;
            float armProjMag = 0f;
            if (i.HintWeight)
            {
                // Original keeps the pre-root |ac|^2 here; rootDelta is a pure rotation so the
                // magnitude is unchanged and acNorm below stays correctly normalized.
                float acSqrMag = ac.sqrMagnitude;
                if (acSqrMag > 0f)
                {
                    ab = bPosition - aPosition;
                    ac = cPosition - aPosition;

                    Vector3 acNorm = ac / Mathf.Sqrt(acSqrMag);
                    Vector3 ah = i.HintPosition - aPosition;
                    Vector3 abProj = ab - acNorm * Vector3.Dot(ab, acNorm);
                    Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);
                    hintProjMag = ahProj.magnitude;
                    armProjMag = abProj.magnitude;

                    // Fade only when the pole genuinely collapses onto the shoulder->hand axis,
                    // keyed on the projection magnitude itself, not raw extension (which discarded
                    // tracker follow on 21% of the workspace).
                    float projNorm = (totalLen > k_Epsilon) ? ahProj.magnitude / totalLen : 0f;
                    // A real elbow tracker sits a physical stand-off (limb radius + strap) OFF the bone, so even
                    // a short swing-plane projection is genuine out-direction signal, not noise. Re-condition it
                    // (floor the effective projection) so the elbow FOLLOWS the tracker instead of fading toward
                    // the rest bend -- the "tracker looks unnatural for some mounts/body sizes" fix. Keyed only
                    // on ahProj (shoulder/hand/hint positions), so unlike a tracker-LOCAL offset it does NOT
                    // swing with forearm pronation. Below a small floor the tracker is essentially on the bone
                    // line (direction is noise) so it still fades. Lookup (no-tracker) path is untouched.
                    if (i.HintIsTracker && projNorm > 0.05f) projNorm = Mathf.Max(projNorm, 0.30f);
                    hintFade = Mathf.Clamp01((projNorm - 0.06f) / 0.12f);
                    if (hintFade > 0f && abProj.sqrMagnitude > (totalLen * totalLen * 0.001f) && ahProj.sqrMagnitude > (totalLen * totalLen * 0.001f))
                    {
                        hintR = QuaternionExt.FromToRotation(abProj, ahProj);
                        // A near-180 deg bend->hint rotation is direction-ambiguous when applied
                        // partially, so the elbow snaps sides on smooth motion (the pole flip). Commit
                        // toward the hint (fade->1) as the bend nears anti-parallel, so the elbow lands
                        // on the smooth hint pole instead of halfway; the ramp keeps it continuous.
                        float effFade = hintFade;
                        if (effFade < 1f)
                        {
                            float denom = Mathf.Sqrt(abProj.sqrMagnitude * ahProj.sqrMagnitude);
                            float cosBA = denom > k_Epsilon ? Mathf.Clamp(Vector3.Dot(abProj, ahProj) / denom, -1f, 1f) : 1f;
                            float flipDeg = Mathf.Acos(cosBA) * Mathf.Rad2Deg;
                            float commit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((flipDeg - 90f) / 80f));
                            // Only commit when the hint is already strong. Committing as it merely emerges
                            // (low fade) snaps the elbow off the stable rest bend -- the folded-arm case.
                            commit *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((hintFade - 0.3f) / 0.25f));
                            effFade = hintFade + (1f - hintFade) * commit;
                        }
                        if (effFade < 1f)
                        {
                            hintR = Quaternion.Slerp(Quaternion.identity, hintR, effFade);
                        }
                        hintR = QuaternionExt.NormalizeSafe(hintR);

                        // Rate-limit the swivel so the elbow eases toward the pole instead of
                        // snapping ~180 deg when the hint crosses to the opposite side of the
                        // current elbow (the long-standing pole flip). Reach is unaffected; this
                        // only bounds the swivel rotation. Offline callers pass MaxValue (no clamp).
                        float hintAngle = 2f * Mathf.Acos(Mathf.Clamp(Mathf.Abs(hintR.w), 0f, 1f)) * Mathf.Rad2Deg;
                        if (hintAngle > i.HintMaxStepDeg && hintAngle > k_Epsilon)
                        {
                            hintR = Quaternion.Slerp(Quaternion.identity, hintR, i.HintMaxStepDeg / hintAngle);
                        }

                        // Hand reach is PRIMARY: the hint must stay a pure swivel about the shoulder->hand
                        // axis so the hand keeps meeting the target. At the anti-parallel singularity
                        // FromToRotation's axis is arbitrary, so a full hint throws the hand off (the pole
                        // flip). Reduce the hint toward identity until the hand returns to its pre-hint reach
                        // (or as close as possible) -- the elbow yields, the destination is always met.
                        float reachTol = (cPosition - tPosition).magnitude + 0.004f * totalLen;
                        Vector3 cFull = aPosition + hintR * (cPosition - aPosition);
                        if ((cFull - tPosition).magnitude > reachTol)
                        {
                            float lo = 0f, hi = 1f;
                            for (int it = 0; it < 12; it++)
                            {
                                float midK = 0.5f * (lo + hi);
                                Vector3 cK = aPosition + Quaternion.Slerp(Quaternion.identity, hintR, midK) * (cPosition - aPosition);
                                if ((cK - tPosition).magnitude <= reachTol) lo = midK; else hi = midK;
                            }
                            hintR = Quaternion.Slerp(Quaternion.identity, hintR, lo);
                        }

                        rootRot = hintR * rootRot;
                        bPosition = aPosition + hintR * (bPosition - aPosition);
                        cPosition = aPosition + hintR * (cPosition - aPosition);
                        midRot = hintR * midRot;
                        hintApplied = true;
                    }
                }
            }

            // Pole-collapse stabilizer (the BACKWARD full-stretch rapid flip). The live arm solve is
            // stateless and unclamped, so when the hint pole goes near-collinear with the shoulder->hand axis
            // -- as the arm stretches out, worst BEHIND the body where the lookup bend itself points backward
            // along the arm -- the swivel is hypersensitive and the elbow flips rapidly on small hand motion.
            // Ease the elbow toward a STABLE pole (world-down projected onto the swing plane, where it
            // naturally hangs) by a reach-preserving swivel about the shoulder->hand axis, weighted by how
            // collapsed the hint pole is (collapse 1 at projNorm<=0.15, off by 0.30 -- the tuning knob; raise
            // it if a backward flip survives, lower it if forward/up reaches drift). Folded into HintDelta so
            // the runtime applies it; the hand stays exactly on target -- only the ill-conditioned swivel DOF
            // is replaced by a stable attractor. Perpendicular hint poles (forward / up reaches) are untouched.
            if (i.HintWeight)
            {
                float poleCond = totalLen > k_Epsilon ? hintProjMag / totalLen : 1f;
                // Same physical-stand-off reasoning as the hintFade floor above: a real tracker's short pole is
                // real out-direction, so re-condition it (positions only -> pronation-safe) and the world-down
                // stabilizer backs off, letting the elbow follow the tracker. Lookup path keeps the wider window
                // (the backward full-stretch flip fix). Below the floor (tracker on the bone line) it still acts.
                if (i.HintIsTracker && poleCond > 0.05f) poleCond = Mathf.Max(poleCond, 0.30f);
                float collapse = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((poleCond - 0.15f) / 0.15f));
                Vector3 acStab = cPosition - aPosition;
                if (collapse > 0f && acStab.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 acStabN = acStab.normalized;
                    Vector3 downPole = -i.PlayerUp - acStabN * Vector3.Dot(-i.PlayerUp, acStabN);
                    Vector3 elbowPole = (bPosition - aPosition) - acStabN * Vector3.Dot(bPosition - aPosition, acStabN);
                    if (downPole.sqrMagnitude > k_SqrEpsilon && elbowPole.sqrMagnitude > k_SqrEpsilon)
                    {
                        Quaternion stab = Quaternion.Slerp(Quaternion.identity, QuaternionExt.FromToRotation(elbowPole, downPole), collapse);
                        // Offline temporal callers pass a per-solve cap; the live stateless rig passes MaxValue
                        // (down is a fixed target, so a full stateless swivel onto it is stable, not a snap).
                        float stabAngle = 2f * Mathf.Acos(Mathf.Clamp(Mathf.Abs(stab.w), 0f, 1f)) * Mathf.Rad2Deg;
                        if (stabAngle > i.HintMaxStepDeg && stabAngle > k_Epsilon)
                        {
                            stab = Quaternion.Slerp(Quaternion.identity, stab, i.HintMaxStepDeg / stabAngle);
                        }
                        stab = QuaternionExt.NormalizeSafe(stab);
                        rootRot = stab * rootRot;
                        bPosition = aPosition + stab * (bPosition - aPosition);
                        cPosition = aPosition + stab * (cPosition - aPosition);
                        midRot = stab * midRot;
                        hintR = stab * hintR; // fold into the hint delta the runtime applies
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
            r.AxisSource = axisSource;
            r.HandError = (cPosition - tPosition).magnitude;
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
