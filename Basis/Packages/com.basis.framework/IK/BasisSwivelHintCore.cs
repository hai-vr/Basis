using Unity.Burst;
using Unity.Mathematics;

namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// The body frame the swivel models are fitted in: a right-handed triad built from BONE POSITIONS.
    ///
    /// From POSITIONS, never from bone rotations. A bone's local axes are a RIG CONVENTION -- CMU's chest bone
    /// is not Unity's -- so a frame taken from rotations is fitted to one skeleton and no other. A shoulder
    /// line and a spine direction are ANATOMY, and anatomy transfers between rigs.
    ///
    /// `Right` is the UN-MIRRORED body right. The mirror (+x OUTWARD for both limbs, so that one model serves
    /// both) is applied per-limb in Features().
    /// </summary>
    public struct BasisSwivelFrame
    {
        public Vector3 Right;
        public Vector3 Up;
        public Vector3 Forward;
        public bool Valid;
    }

    /// <summary>
    /// Builds the exact inputs BasisElbowFieldModel (arm) and BasisLegSwivelModel (leg) were FITTED on, and
    /// turns their prediction into the hint position the two-bone solvers want.
    ///
    /// The two limbs no longer answer the same KIND of question, and that is deliberate. The leg still
    /// predicts a swivel ANGLE; the arm predicts the elbow's POSITION and projects it onto the reachable
    /// circle. An angle must be measured from a reference direction, and every choice of reference vanishes
    /// somewhere on the sphere of tip directions -- the arm's old one vanished when the arm hung straight
    /// down, which is 29.7% of real human frames and the commonest pose in VR. The leg's reference is body
    /// OUTWARD, and a leg does not point sideways out of the hip, so its zero sits outside the reachable set.
    /// The arm had nowhere safe to put it. See BasisElbowFieldModel.
    ///
    /// ================================================================================================
    /// THIS FILE EXISTS SO THE RUNTIME AND THE FIT CANNOT DISAGREE.
    ///
    /// The models are polynomials in three numbers. Get any ONE of handedness, body frame or mirror wrong and
    /// the model does not degrade -- it produces CONFIDENT GARBAGE, which is the only kind of wrong that gets
    /// past a green test suite. That has now happened twice on this project:
    ///
    ///   * a fit done in a separate pipeline scored 3.77 % there and 31 % in the harness, because the two
    ///     disagreed about the mirror. The predicted swivel came out 145 degrees off. Nothing crashed.
    ///
    ///   * an earlier version of THIS FILE fed the models 27 extra features describing the tip's ORIENTATION,
    ///     divided by a T-pose read at job-build time. BasisLocalAvatarDriver exits T-pose BEFORE it builds the
    ///     rig, so that "rest pose" was not reliably a rest pose -- and in a headset the elbows sat up by the
    ///     ears, near-inverted, on almost every frame, while every test in the suite stayed green. The corpus
    ///     had warned about exactly this in writing (NOTICE.md: "a mocap hand is not a controller").
    ///
    /// So the feature construction lives in ONE place, it reads POSITIONS ONLY, and
    /// BasisSwivelHintConformanceTests pins it against the harness's own construction (BasisMocapAccuracy,
    /// which IS the fit pipeline).
    /// ================================================================================================
    /// </summary>
    [BurstCompile]
    public static class BasisSwivelHintCore
    {
        const float k_SqrEpsilon = 1e-10f;
        const float k_Epsilon = 1e-5f;

        /// <summary>
        /// THE ARM NO LONGER HAS ONE, AND THAT IS THE FIX, NOT AN OMISSION.
        ///
        /// This used to be a HARD GATE at 0.20 in BasisFullBodyIK.SolveHand: below it the hint was dropped
        /// ENTIRELY and the elbow fell back to whatever the animation clip happened to be doing. Two
        /// unrelated poles, switched between on a boolean -- which is the definition of a pop, and which
        /// LegHint's own comment below already says in as many words.
        ///
        /// It is gone because BasisElbowFieldModel has nothing to be unconfident ABOUT: it predicts a
        /// POSITION, so its only degeneracy is geometric (the predicted elbow landing on the arm's own
        /// axis), it is measurable, and it is measure-zero -- the model falls back to its rest pole ONLY
        /// on those exact cores. (It used to FADE toward that pole below a 0.10 lever instead, and the
        /// fade's antipodal lerp was itself a hidden gate: it teleported the elbow 28 cm/frame on big
        /// cross-body swings. See the field model's header.)
        /// </summary>

        /// <summary>
        /// A styling nudge on the no-tracker elbow, requested from the headset: "elbow tuck a little bit".
        /// The field model predicts the CORPUS-MEAN elbow; this leans it a few degrees toward the body.
        ///
        /// It is a POLE BLEND, not an angle, for the same reason the model predicts a position: an angle
        /// needs a signed reference and a mirror flips it. The pole lives in the MIRRORED frame (+x OUT for
        /// both limbs), so -x is INWARD for both arms and one constant serves both. Its perpendicular
        /// projection is blended into the bend un-normalised, so the tuck's magnitude is sin(arm-vs-pole)
        /// and it FADES ITSELF to zero as the arm approaches the pole's own direction -- no reference to
        /// collapse, no seam. The weight bounds the rotation at atan(weight * |pole|) ~ 7 deg.
        /// </summary>
        static readonly float3 k_ElbowTuckPole = new float3(-1f, -0.35f, 0f);
        public const float ElbowTuckWeight = 0.12f;

        /// <summary>|(s,c)| at or below which the leg model has no usable opinion and the solve should
        /// fall back to its own anatomical bend pole.</summary>
        public const float LegTrustLo = 0.30f;

        /// <summary>|(s,c)| at or above which the leg model is trusted outright. Above this LegModelTrust
        /// returns exactly 1, so everything inside the fitted domain is bit-for-bit unchanged.</summary>
        public const float LegTrustHi = 0.70f;

        /// <summary>
        /// How far to trust <see cref="BasisLegSwivelModel"/>, read off its own |(s,c)|.
        ///
        /// The magnitude is the model's opinion STRENGTH, and the domain clamp in SwivelRad bounds the
        /// radius but cannot bound the DIRECTION — so leg ABDUCTION (standing with the feet apart) walks
        /// straight out of a fit whose corpus is CMU walk/idle/reach/sit. Measured on a 22° abduction:
        /// confidence 0.98 → 0.28 while the predicted swivel runs 4° → 70°, rotating the knee out of the
        /// sagittal plane and back inward. Out there the answer is not approximate, it is invented.
        ///
        /// Smoothstep so the pole it feeds is C1 and cannot kink.
        /// </summary>
        public static float LegModelTrust(float confidence)
        {
            float t = Mathf.Clamp01((confidence - LegTrustLo) / (LegTrustHi - LegTrustLo));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// `up` runs upFrom -> upTo (chest -> neck for the arm; hips -> chest for the leg).
        /// `right` runs leftAnchor -> rightAnchor (the shoulder line; the hip line), orthogonalised against up.
        /// Returns Valid = false on a degenerate rig rather than a silently wrong frame.
        /// </summary>
        public static BasisSwivelFrame BuildFrame(Vector3 leftAnchor, Vector3 rightAnchor, Vector3 upFrom, Vector3 upTo)
        {
            BasisSwivelFrame f = default;   // Valid = false

            Vector3 up = upTo - upFrom;
            float upSqr = up.sqrMagnitude;
            // Reject-unless-good: NaN fails every ordered comparison, so `!(x > eps)` rejects it where
            // `x < eps` would wave it through. A NaN frame poisons every feature downstream.
            if (!(upSqr > k_SqrEpsilon))
            {
                return f;
            }
            up /= Mathf.Sqrt(upSqr);

            Vector3 right = rightAnchor - leftAnchor;
            right -= up * Vector3.Dot(right, up);
            float rightSqr = right.sqrMagnitude;
            if (!(rightSqr > k_SqrEpsilon))
            {
                return f;
            }
            right /= Mathf.Sqrt(rightSqr);

            f.Right = right;
            f.Up = up;
            f.Forward = Vector3.Cross(right, up);
            f.Valid = true;
            return f;
        }

        /// <summary>
        /// The three numbers the models eat: the tip's position in the mirrored body frame, normalised by limb
        /// length. Nothing else -- no rotation, no T-pose, nothing a rig convention can reach.
        /// </summary>
        public static void Features(in BasisSwivelFrame frameNow, Vector3 rootPos, Vector3 tipPos,
                                    float limbLen, bool isLeft, out float3 tipLocal)
        {
            // +x is OUTWARD for both limbs, so one model serves both. The caller un-mirrors the ANGLE.
            Vector3 bOut = isLeft ? -frameNow.Right : frameNow.Right;

            Vector3 r2t = tipPos - rootPos;
            float inv = 1f / Mathf.Max(limbLen, k_Epsilon);
            tipLocal = new float3(Vector3.Dot(r2t, bOut) * inv,
                                  Vector3.Dot(r2t, frameNow.Up) * inv,
                                  Vector3.Dot(r2t, frameNow.Forward) * inv);
        }

        /// <summary>
        /// Where the ELBOW goes with no elbow tracker. `hintPos` sits half an arm-length off the shoulder along
        /// the predicted bend -- the same convention the old lookup used, so the solver is handed the same shape
        /// of thing it always was, and the pole-collapse stabilizer in BasisArmSolveCore stays dead on this
        /// path (poleCond lands at 0.5, well clear of its 0.15 window).
        ///
        /// There is no swivel angle here and no reference direction to get the sign of. See
        /// BasisElbowFieldModel: an angle needs something to be measured FROM, every such reference vanishes
        /// somewhere on the sphere of hand directions, and the one this used to use vanished when the arm
        /// hung down -- 29.7 % of real human frames.
        ///
        /// Returns false on a degenerate/NaN frame: the caller then leaves `hasHint` false and the two-bone core
        /// falls back to its own internal pole, which is what it did before any of this existed.
        /// </summary>
        public static bool ArmHint(in BasisSwivelFrame frameNow, Vector3 shoulder, Vector3 handPos,
                                   float armLen, bool isLeft, out Vector3 hintPos, out float confidence)
        {
            hintPos = default;
            confidence = 0f;

            if (!frameNow.Valid || !(armLen > k_Epsilon))
            {
                return false;
            }

            Features(frameNow, shoulder, handPos, armLen, isLeft, out float3 tipLocal);

            // One NaN hand target used to walk all the way into BasisArmBendLookup.SampleTrilinear, become
            // (int)NaN == int.MinValue, and abort the process with no managed stack. There is no int cast on
            // this path, but a NaN hint still poisons the solve, so it is stopped at the door.
            if (!IsFinite(tipLocal))
            {
                return false;
            }

            float3 elbowLocal = BasisElbowFieldModel.Elbow(tipLocal);
            float3 bend = BasisElbowFieldModel.BendDirection(tipLocal, elbowLocal, out confidence);

            if (!IsFinite(bend))
            {
                return false;
            }

            // The tuck. Projected against the SAME axis BendDirection used, so the blend of two
            // perpendicular vectors stays perpendicular and the world-map comment below keeps its promise.
            // |bend| = 1 and the tuck term is <= ElbowTuckWeight * |pole|, so the sum cannot degenerate.
            float3 tuckAxis = math.normalizesafe(tipLocal, new float3(0f, -1f, 0f));
            float3 tuckPerp = k_ElbowTuckPole - tuckAxis * math.dot(k_ElbowTuckPole, tuckAxis);
            bend = math.normalizesafe(bend + ElbowTuckWeight * tuckPerp, bend);

            // Back to world through the SAME mirrored triad the features were built in. It is orthonormal,
            // so it carries perpendicularity across unchanged: `bend` is perpendicular to tipLocal in the
            // body frame, therefore bendWorld is perpendicular to shoulder->hand in world, exactly.
            //
            // And nothing is un-mirrored here. A POSITION maps through a mirror; an ANGLE is a pseudo-scalar
            // and flips sign, which is a trap that has silently poisoned this model twice (once for 145
            // degrees). Predicting a position rather than an angle deletes the trap along with the sign.
            Vector3 bOut = isLeft ? -frameNow.Right : frameNow.Right;
            Vector3 bendWorld = bend.x * bOut + bend.y * frameNow.Up + bend.z * frameNow.Forward;

            hintPos = shoulder + 0.5f * armLen * bendWorld;
            return true;
        }

        /// <summary>
        /// Where the KNEE goes with no knee tracker. Same shape as ArmHint, three differences, all measured:
        ///
        ///   * the frame hangs off the PELVIS (hip line, hips->chest), not the chest;
        ///   * the swivel's reference is body OUTWARD and is passed MIRRORED (the arm's is body-down and is not);
        ///   * NO confidence gate. Fading the hint weight toward zero does not avoid a pop, it CREATES one --
        ///     the knee falls back to the solver's BendNormal pole, which points somewhere unrelated, and
        ///     swinging between two unrelated poles over a few frames IS a pop. Measured: the fade took the knee
        ///     from 70 pops to 65. It relocated the discontinuity, it did not remove it.
        /// </summary>
        public static bool LegHint(in BasisSwivelFrame frameNow, Vector3 hip, Vector3 footPos,
                                   float legLen, bool isLeft, out Vector3 hintPos, out float confidence)
        {
            hintPos = default;
            confidence = 0f;

            if (!frameNow.Valid || !(legLen > k_Epsilon))
            {
                return false;
            }

            Features(frameNow, hip, footPos, legLen, isLeft, out float3 tipLocal);

            if (!IsFinite(tipLocal))
            {
                return false;
            }

            float swivel = BasisLegSwivelModel.SwivelRad(tipLocal, out confidence);
            if (isLeft)
            {
                swivel = -swivel;
            }

            Vector3 gOut = isLeft ? -frameNow.Right : frameNow.Right;   // MIRRORED, unlike the arm's reference
            Vector3 h2f = footPos - hip;
            float3 bend = BasisLegSwivelModel.BendDirection(
                new float3(h2f.x, h2f.y, h2f.z),
                new float3(gOut.x, gOut.y, gOut.z),
                swivel);

            if (!IsFinite(bend))
            {
                return false;
            }

            hintPos = hip + 0.5f * legLen * new Vector3(bend.x, bend.y, bend.z);
            return true;
        }

        static bool IsFinite(in float3 v) => math.all(math.isfinite(v));
    }
}
