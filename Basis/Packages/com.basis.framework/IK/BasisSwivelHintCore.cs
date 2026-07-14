using Unity.Burst;
using Unity.Mathematics;

namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// The body frame the swivel models are fitted in: a right-handed triad built from BONE POSITIONS.
    ///
    /// From POSITIONS, never from bone rotations. A bone's local axes are a RIG CONVENTION -- CMU's chest bone
    /// is not Unity's -- so a frame taken from rotations does not transfer between rigs and the model would be
    /// fitted to the mocap skeleton and nothing else. A shoulder line and a spine direction are ANATOMY, and
    /// anatomy transfers.
    ///
    /// `Right` is the UN-MIRRORED body right. The mirror (+x OUTWARD for both limbs) is applied per-limb in
    /// Features(), because the T-pose reference must NOT be mirrored -- see the note there.
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
    /// The models are polynomials in 12 numbers. Get any ONE of handedness, body frame, mirror or T-pose
    /// convention wrong and the model does not degrade -- it produces confident garbage. That already happened
    /// once on this project: a fit done in a separate pipeline scored 3.77% there and 31% in the harness,
    /// because the two disagreed about the mirror. The predicted swivel came out 145 degrees off.
    ///
    /// So the feature construction lives in ONE place, and BasisSwivelHintConformanceTests pins it against the
    /// harness's own construction (BasisMocapAccuracy, which IS the fit pipeline) on a synthetic rig.
    /// ================================================================================================
    ///
    /// ⚠ THE T-POSE DIVISION, AND WHY IT NEEDS THE T-POSE *FRAME* AND NOT JUST THE T-POSE ROTATION.
    ///
    /// The orientation feature is "where do the hand's own axes point, relative to the body". In the CMU BVH the
    /// rest pose has IDENTITY rotations on every joint, so the harness can read the hand's axes straight off
    /// `handRot * (right|up|forward)`. A real avatar's hand bone has an arbitrary rest rotation, so the rig
    /// convention has to be divided out: `delta = handRot * inverse(handTposeRot)`.
    ///
    /// That is necessary but NOT sufficient. `delta` is a WORLD-space delta, and the T-pose was captured with the
    /// player standing at some arbitrary world yaw Q0 (BasisLocalAvatarDriver calibrates wherever the user
    /// happens to be facing). Work it through: the live features come out equal to the fitted ones RIGHT-
    /// MULTIPLIED BY inverse(Q0). The position features do not care -- the body frame yaws with the player, so
    /// the yaw cancels -- but the ORIENTATION features are silently rotated by a constant, and the model has
    /// never seen that. A user who calibrated facing east would get a different elbow from one facing north.
    ///
    /// The cure is to feed the T-POSE BODY FRAME back in on the right: applying `delta` to the T-pose frame's
    /// own axes (rather than to world x/y/z) cancels Q0 exactly. In the BVH -- rest rotations identity, rest
    /// frame world-aligned -- it reduces to `handRot * (right|up|forward)`, i.e. bit-for-bit what was fitted.
    /// BasisSwivelHintConformanceTests.Features_AreInvariant_ToTheWorldYawAtCalibration pins that.
    ///
    /// The T-pose frame is the UN-MIRRORED one for BOTH limbs. Mirroring it too would conjugate the orientation
    /// matrix (M X M) instead of merely reflecting it (M X), which is NOT the object the model was fitted on.
    /// </summary>
    [BurstCompile]
    public static class BasisSwivelHintCore
    {
        const float k_SqrEpsilon = 1e-10f;
        const float k_Epsilon = 1e-5f;

        /// <summary>
        /// Below this the model is telling you IT DOES NOT KNOW, and atan2 near the origin does not fail -- it
        /// SPINS. Drawn from the measured distribution across the corpus. See BasisArmSwivelModel.SwivelRad.
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
        /// The 12 numbers the models eat: the tip's position in the mirrored body frame (normalised by limb
        /// length), and the tip's orientation relative to its own T-pose, in that same frame.
        /// </summary>
        public static void Features(in BasisSwivelFrame frameNow, in BasisSwivelFrame frameTpose,
                                    Vector3 rootPos, Vector3 tipPos, Quaternion tipRot, Quaternion tipTposeRot,
                                    float limbLen, bool isLeft,
                                    out float3 tipLocal, out float3x3 tipOrient)
        {
            // +x is OUTWARD for both limbs, so one model serves both. The caller un-mirrors the ANGLE.
            Vector3 bOut = isLeft ? -frameNow.Right : frameNow.Right;
            Vector3 bUp = frameNow.Up;
            Vector3 bFwd = frameNow.Forward;

            Vector3 r2t = tipPos - rootPos;
            float inv = 1f / Mathf.Max(limbLen, k_Epsilon);
            tipLocal = new float3(Vector3.Dot(r2t, bOut) * inv, Vector3.Dot(r2t, bUp) * inv, Vector3.Dot(r2t, bFwd) * inv);

            // The rig convention divided out, and Q0 (the world yaw at calibration) cancelled by applying the
            // delta to the T-POSE FRAME's axes rather than to world x/y/z. See the class comment.
            Quaternion delta = tipRot * Quaternion.Inverse(tipTposeRot);
            Vector3 hX = delta * frameTpose.Right;
            Vector3 hY = delta * frameTpose.Up;
            Vector3 hZ = delta * frameTpose.Forward;

            // COLUMNS are the tip's own axes, written in the body frame.
            tipOrient = new float3x3(
                new float3(Vector3.Dot(hX, bOut), Vector3.Dot(hX, bUp), Vector3.Dot(hX, bFwd)),
                new float3(Vector3.Dot(hY, bOut), Vector3.Dot(hY, bUp), Vector3.Dot(hY, bFwd)),
                new float3(Vector3.Dot(hZ, bOut), Vector3.Dot(hZ, bUp), Vector3.Dot(hZ, bFwd)));
        }

        /// <summary>
        /// Where the ELBOW goes with no elbow tracker. `hintPos` sits half an arm-length off the shoulder along
        /// the predicted bend -- the same convention the old lookup used, so the solver is handed the same shape
        /// of thing it always was.
        ///
        /// The swivel's zero is body DOWN (an elbow hangs down). Passing +up instead of -up put the elbow ABOVE
        /// the shoulder and cost 34.98% error, so the sign is load-bearing, not cosmetic.
        ///
        /// Returns false on a degenerate/NaN frame: the caller then leaves `hasHint` false and the two-bone core
        /// falls back to its own internal pole, which is what it did before any of this existed.
        /// </summary>
        public static bool ArmHint(in BasisSwivelFrame frameNow, in BasisSwivelFrame frameTpose,
                                   Vector3 shoulder, Vector3 handPos, Quaternion handRot, Quaternion handTposeRot,
                                   float armLen, bool isLeft,
                                   out Vector3 hintPos, out float confidence)
        {
            hintPos = default;
            confidence = 0f;

            if (!frameNow.Valid || !frameTpose.Valid || !(armLen > k_Epsilon))
            {
                return false;
            }

            Features(frameNow, frameTpose, shoulder, handPos, handRot, handTposeRot, armLen, isLeft,
                     out float3 tipLocal, out float3x3 tipOrient);

            // One NaN hand target used to walk all the way into BasisArmBendLookup.SampleTrilinear, become
            // (int)NaN == int.MinValue and abort the process. There is no int cast on this path, but a NaN hint
            // still poisons the solve, so it is stopped here.
            if (!IsFinite(tipLocal) || !IsFinite(tipOrient))
            {
                return false;
            }

            float swivel = BasisArmSwivelModel.SwivelRad(tipLocal, tipOrient, out confidence);
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
        ///     from 70 pops to 65. It relocated the discontinuity, it did not remove it. The real cure is
        ///     upstream, in BasisLegSwivelModel's biased constant term, which stops (sin,cos) ever approaching
        ///     the origin so atan2 can never spin. `confidence` is reported for diagnostics only.
        /// </summary>
        public static bool LegHint(in BasisSwivelFrame frameNow, in BasisSwivelFrame frameTpose,
                                   Vector3 hip, Vector3 footPos, Quaternion footRot, Quaternion footTposeRot,
                                   float legLen, bool isLeft,
                                   out Vector3 hintPos, out float confidence)
        {
            hintPos = default;
            confidence = 0f;

            if (!frameNow.Valid || !frameTpose.Valid || !(legLen > k_Epsilon))
            {
                return false;
            }

            Features(frameNow, frameTpose, hip, footPos, footRot, footTposeRot, legLen, isLeft,
                     out float3 tipLocal, out float3x3 tipOrient);

            if (!IsFinite(tipLocal) || !IsFinite(tipOrient))
            {
                return false;
            }

            float swivel = BasisLegSwivelModel.SwivelRad(tipLocal, tipOrient, out confidence);
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

        static bool IsFinite(in float3x3 m) =>
            math.all(math.isfinite(m.c0)) && math.all(math.isfinite(m.c1)) && math.all(math.isfinite(m.c2));
    }
}
