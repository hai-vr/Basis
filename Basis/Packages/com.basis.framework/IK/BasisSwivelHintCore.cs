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
    /// Builds the exact inputs BasisArmSwivelModel / BasisLegSwivelModel were FITTED on, and turns their
    /// predicted swivel angle into the hint position the two-bone solvers want.
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
        /// Below this the model is telling you IT DOES NOT KNOW, and atan2 near the origin does not fail -- it
        /// SPINS. Measured across the corpus the arm falls under this on 0.004 % of frames and the leg on none,
        /// so it essentially never fires; it is here because the failure it prevents is a spinning elbow.
        /// </summary>
        public const float MinConfidence = 0.20f;

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
        /// of thing it always was.
        ///
        /// The swivel's zero is body DOWN (an elbow hangs down). Passing +up instead of -up put the elbow ABOVE
        /// the shoulder and cost 34.98 % error, so the sign is load-bearing rather than cosmetic.
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

            // The model clamps its own domain. It has to: the raw controller target routinely exceeds the
            // avatar's reach, and a cubic outside its fit box is a random number generator. See
            // BasisArmSwivelModel -- that omission is what put the elbows up by the ears.
            float swivel = BasisArmSwivelModel.SwivelRad(tipLocal, out confidence);
            if (isLeft)
            {
                swivel = -swivel;   // un-mirror: the model answers in the mirrored frame
            }

            Vector3 s2h = handPos - shoulder;
            float3 bend = BasisArmSwivelModel.BendDirection(
                new float3(s2h.x, s2h.y, s2h.z),
                new float3(-frameNow.Up.x, -frameNow.Up.y, -frameNow.Up.z),   // body DOWN, UN-mirrored
                swivel);

            if (!IsFinite(bend))
            {
                return false;
            }

            hintPos = shoulder + 0.5f * armLen * new Vector3(bend.x, bend.y, bend.z);
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
