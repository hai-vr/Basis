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

        // Elbow-lever fade window, as a fraction of the arm's reach that the elbow stands off the shoulder->hand
        // axis. Start = sqrt(0.001), the exact threshold the old boolean gate cliffed at, so the fade only

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
            float swivelUsedRad = 0f;   // how much of the per-frame swivel budget the hint has already spent
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
                    Vector3 elbowDir = Vector3.Cross(acNorm, rootDelta * axis);
                    elbowDir -= acNorm * Vector3.Dot(elbowDir, acNorm);
                    hintProjMag = ahProj.magnitude;
                    armProjMag = abProj.magnitude;

                    // ==========================================================================================
                    // ⭐ THE POLE IS COMMANDED. IT IS OBEYED. THERE IS NOTHING HERE TO FADE.
                    //
                    // This block used to hold TWO fades, and between them they were the reason a real elbow
                    // tracker inverted:
                    //
                    //   projNorm  faded on |ahProj| -- how far the POLE stands off the shoulder->hand axis.
                    //   bendNorm  faded on |abProj| -- how far the CURRENT ELBOW stands off that same axis.
                    //
                    // BOTH of those collapse to zero as the arm STRAIGHTENS, and a VR user holds their arms
                    // mostly straight: 57% of the corpus sits above 95% extension. Measured on a 0.6 m arm with
                    // a real tracker, the authority the solver granted it was
                    //
                    //      94% extension -> 1.00     98.5% -> 0.62     99.5% -> 0.21     99.9% -> 0.00
                    //
                    // At partial authority the elbow does not land on the tracker and it does not land on the
                    // animation -- it lands BETWEEN them, at a point that depends on the noisy, collapsing
                    // abProj. Cross the threshold and it snaps from one to the other. THAT is "the elbow
                    // inverts with a real tracker attached", and "it is not very human movement": the elbow was
                    // being blended toward an idle animation the user is not performing.
                    //
                    // THE FADES WERE GUARDING THE WRONG QUANTITY. What goes to noise as the limb straightens is
                    // abProj -- the MEASURED direction of the current elbow, whose lever arm is vanishing. The
                    // POLE does not: it is COMMANDED, by a tracker or by the swivel model, and it stays
                    // perfectly well-defined at every extension. And because this swivel rotates the elbow ONTO
                    // the pole, THE ELBOW'S FINAL DIRECTION DOES NOT DEPEND ON WHERE IT STARTED.
                    //
                    // BasisLegSolveCore worked this out and deleted its copy long ago, and its comment says so
                    // in as many words -- "the POLE is commanded, BY A TRACKER OR BY BendNormal". The leg has
                    // shipped without these fades ever since. The arm's survived, and it is what has been
                    // breaking the elbow.
                    //
                    // What the deletion left behind was the SECOND half of the same defect, and it is why the
                    // angle below is measured from elbowDir and not from abProj. Deleting the fade correctly
                    // stopped abProj's collapse from stealing the pole's AUTHORITY -- but the swivel ANGLE was
                    // still being read off abProj's DIRECTION, and that direction is gone at full extension.
                    // The resulting rotation is about acNorm, which at full extension IS the upper arm's own
                    // long axis, so a noisy angle is not a harmless "wrong twist for one frame" -- it is the arm
                    // ROLLING, at full magnitude, on a limb whose elbow has no lever arm left to show it.
                    // Measured on a 0.6 m arm: a 1 mm nudge of the hand target rolled the upper arm and the
                    // forearm 180 deg, with the hand still on target. The median roll at 99.95% extension was
                    // 20 deg. A VR user reaching or pointing lives there -- 57% of the corpus is past 95%.
                    //
                    // elbowDir is that same direction RECONSTRUCTED from the plane the bend just used, via the
                    // identity Cross(ac, Cross(ab, bc)) == |ac|^2 * abProj. The bend axis is perpendicular to ac
                    // by construction, so the cross product is a UNIT vector at every extension: identical to
                    // abProj wherever abProj exists, and still there when it does not. See BasisLegSolveCore,
                    // which carries the same fix and the same identity.
                    //
                    // And note WHAT the surviving epsilon below guards: ahProj, the POLE. It was never a guard on
                    // the ELBOW, which is why the elbow's collapse ran unnoticed for so long.
                    // ==========================================================================================
                    hintFade = 1f;

                    // ==========================================================================================
                    // ⚠ A NUMERICAL EPSILON, NOT A BEHAVIOURAL GATE. This test used to read
                    //
                    //      ahProj.sqrMagnitude > totalLen * totalLen * 0.001f
                    //
                    // which on a 0.6 m arm is |ahProj| > 1.9 CENTIMETRES -- and it is a BOOLEAN. Below it the
                    // hint was not faded, it was DROPPED, in a single frame, leaving the elbow wherever the base
                    // animation happened to have left it. And |ahProj| is the pole's stand-off from the
                    // shoulder->hand axis, which COLLAPSES as the arm straightens -- so an ordinary extended
                    // reach walked straight through the cliff. Measured by BasisArmTrackerHintAuthorityTests:
                    // the elbow travelled 2582x the hand's own distance in one step as it crossed.
                    //
                    // Geometry alone gets you to about 12x near full extension. Hundreds is not geometry -- it
                    // is a pole being switched off between two frames, which is the definition of a pop.
                    //
                    // The pole's DIRECTION is perfectly well-defined at 1.9 cm; it is well-defined at 1.9
                    // millimetres. It only becomes noise at true numerical zero. So the guard belongs where
                    // BasisLegSolveCore has always had it -- at 1e-8, a genuine singularity check -- and nowhere
                    // else. (And below it, SignedAngleRad returns 0 anyway, so the elbow simply is not swivelled:
                    // a straight arm whose circle has collapsed has nowhere to put its elbow regardless.)
                    // ==========================================================================================
                    if (ahProj.sqrMagnitude > k_SqrEpsilon && elbowDir.sqrMagnitude > k_SqrEpsilon)
                    {
                        // A near-180 deg bend->hint swivel is direction-ambiguous when applied PARTIALLY: as
                        // the geometry crosses anti-parallel the signed angle flips +179 -> -179, and at half
                        // weight that is a 180 deg elbow swing (the pole flip). At FULL weight the very same
                        // flip is a 0.2 deg no-op, because rotations are periodic. So commit toward the hint as
                        // the bend nears anti-parallel, where the ambiguity stops being able to hurt.
                        float effFade = hintFade;
                        if (effFade < 1f)
                        {
                            float denom = Mathf.Sqrt(elbowDir.sqrMagnitude * ahProj.sqrMagnitude);
                            float cosBA = denom > k_Epsilon ? Mathf.Clamp(Vector3.Dot(elbowDir, ahProj) / denom, -1f, 1f) : 1f;
                            float flipDeg = Mathf.Acos(cosBA) * Mathf.Rad2Deg;
                            float commit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((flipDeg - 90f) / 80f));
                            // Only commit when the hint is already strong. Committing as it merely emerges
                            // (low fade) snaps the elbow off the stable rest bend -- the folded-arm case.
                            commit *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((hintFade - 0.3f) / 0.25f));
                            effFade = hintFade + (1f - hintFade) * commit;
                        }

                        // Swivel about the shoulder->hand axis BY NAME. The hand LIES on that axis, so a
                        // rotation about it cannot move the hand: reach preservation is structural, holds at
                        // every weight, and the promise made in the bend comment above is finally kept.
                        //
                        // QuaternionExt.FromToRotation(abProj, ahProj) used to build this. It takes its axis
                        // from Cross(from, to), which DOES lie along acNorm in the general case -- but when the
                        // two go anti-parallel it abandons the plane and returns 180 deg about
                        // Cross(from, Vector3.right), an arbitrary WORLD axis, and swinging the arm about that
                        // throws the hand clean off its target. A 12-iteration bisection used to sit right here,
                        // walking the hint back toward identity until the hand came home. Naming the axis
                        // deletes the failure and the search for it together.
                        swivelUsedRad = SignedAngleRad(elbowDir, ahProj, acNorm) * effFade;
                        float swivel = swivelUsedRad;

                        // Rate-limit so the elbow eases toward the pole rather than swinging ~180 deg the frame
                        // the hint crosses sides. Reach is unaffected either way; this only bounds the swivel.
                        // Offline callers pass MaxValue (no clamp).
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
            // ⚠ NOT WHEN THE ELBOW IS TRACKED. This stabilizer eases the elbow toward WORLD-DOWN, and it was
            // written for the old invented lookup pole, which really could point backward along the arm and
            // flip. A TRACKER is the user's actual elbow. Dragging it toward world-down is the solver
            // overruling a measurement with a guess -- and because it engages on |ahProj|, which collapses as
            // the arm straightens, it engaged hardest exactly where the user spends most of their time. It is
            // half of "the elbow is not doing very human movement": the other half was the fades above.
            //
            // The swivel model does not need it either (its pole is perpendicular by construction, so
            // poleCond == 0.5 and collapse == 0 -- this block has been dead code on that path since the model
            // landed). So it now guards only what it was built for, and nothing else.
            if (i.HintWeight && !i.HintIsTracker)
            {
                float poleCond = totalLen > k_Epsilon ? hintProjMag / totalLen : 1f;
                // Same physical-stand-off reasoning as the hintFade floor above: a real tracker's short pole is
                // real out-direction, so re-condition it (positions only -> pronation-safe) and the world-down
                // stabilizer backs off, letting the elbow follow the tracker. Lookup path keeps the wider window
                // (the backward full-stretch flip fix). Below the floor (tracker on the bone line) it still acts.
                // Blended over [0.05, 0.10] like the hintFade floor — a hard gate flipped the stabilizer's
                // collapse weight 1<->0 in a single frame at the same crossing.
                if (i.HintIsTracker)
                {
                    float floorBlend = Mathf.Clamp01((poleCond - 0.05f) / 0.05f);
                    poleCond = Mathf.Lerp(poleCond, Mathf.Max(poleCond, 0.30f), floorBlend);
                }
                float collapse = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((poleCond - 0.15f) / 0.15f));
                Vector3 acStab = cPosition - aPosition;
                if (collapse > 0f && acStab.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 acStabN = acStab.normalized;
                    Vector3 downPole = -i.PlayerUp - acStabN * Vector3.Dot(-i.PlayerUp, acStabN);
                    Vector3 elbowPole = (bPosition - aPosition) - acStabN * Vector3.Dot(bPosition - aPosition, acStabN);
                    if (downPole.sqrMagnitude > k_SqrEpsilon && elbowPole.sqrMagnitude > k_SqrEpsilon)
                    {
                        // Named-axis swivel, exactly as the hint above and for exactly the same reason. This block
                        // advertises itself as "a reach-preserving swivel about the shoulder->hand axis", and with
                        // FromToRotation choosing the axis it was no such thing: an elbow pole opposite world-down
                        // IS the anti-parallel case, and it comes up on precisely the backward full-stretch reaches
                        // this stabilizer exists to rescue. It was throwing the hand off the target it was hired to
                        // protect. Offline temporal callers pass a per-solve cap; the live stateless rig passes
                        // MaxValue (down is a fixed target, so a full stateless swivel onto it settles, not snaps).
                        float stabSwivel = SignedAngleRad(elbowPole, downPole, acStabN) * collapse;

                        // ONE budget, because the elbow swivel is ONE degree of freedom. The hint above and this
                        // stabilizer both spin the elbow about the shoulder->hand axis -- and now that the hint
                        // swivel genuinely preserves reach, the hand does not move between them, so acStabN IS
                        // acNorm and the two angles simply ADD. Giving each its own full HintMaxStepDeg therefore
                        // let the elbow travel at TWICE the rate limit whenever they pulled the same way, which
                        // is a pop by the rate limiter's own definition. Spend what the hint left.
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

            // =============================================================================================
            // ⭐ THE ANATOMY GUARD, AND IT IS THE LAST THING THAT HAPPENS.
            //
            // An elbow cannot point at the sky. Measured on 55,140 frames of real human arm motion: THE ELBOW
            // NEVER RISES ABOVE THE SHOULDER, NOR ABOVE THE HAND, WHICHEVER IS HIGHER -- worst violation in the
            // entire corpus, nine millimetres. You cannot lift your elbow over your shoulder while your hand
            // hangs low; the humerus will not do it. See BasisElbowAnatomyCore.
            //
            // The KNEE has had a guard of exactly this status for a long time (BasisLegSolveCore's anterior
            // half-space: "a knee behind that axis is not unnatural, it is anatomically unrepresentable").
            // THE ARM HAD NONE. Nothing whatsoever stopped this solver placing the elbow anywhere on its
            // circle, and "the arms get into rotations that are not possible, the elbows point at the sky" is
            // what that costs.
            //
            // IT GUARDS THE OUTCOME, NOT THE HINT, and that is the whole point of putting it HERE, after
            // everything else has had its say. A guard on the hint would protect the arm from a bad hint. A
            // guard on the RESULT protects it from a bad hint, a mis-strapped elbow tracker, the pole-collapse
            // stabilizer above, and the animated pose the solve started from -- so there is no path by which
            // the arm can end a frame outside the envelope, because this is the end of the frame.
            //
            // It costs no reach, structurally: the correction is a swivel about the shoulder->hand axis, and
            // the hand LIES on that axis. A rotation about a line cannot move a point on that line.
            // =============================================================================================
            float guardSwivel = BasisElbowAnatomyCore.GuardSwivelRad(aPosition, bPosition, cPosition, i.PlayerUp, totalLen);
            if (guardSwivel != 0f)   // exact 0 inside the envelope: legal poses take the untouched path
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

        // Signed angle from `from` to `to`, measured about `axis` (normalized). Both vectors already lie in the
        // plane perpendicular to `axis`, so this is exact. Written the long way rather than through
        // Vector3.SignedAngle / Quaternion.AngleAxis because this runs inside a Burst job.
        //
        // NaN-safe by shape: `!(denom > k_Epsilon)` takes the reject branch on NaN, where `denom < k_Epsilon`
        // would have waved it through -- NaN fails every ordered comparison, so a guard has to be written as
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
