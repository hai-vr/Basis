using Unity.Burst;
using Unity.Mathematics;

namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// Where the elbow goes for a user with NO elbow tracker. It predicts the elbow's POSITION and
    /// projects it onto the reachable circle. It does not predict an angle, and that is the point.
    ///
    /// ================================================================================================
    /// WHY THE PREVIOUS MODEL (BasisArmSwivelModel) FLIPPED, AND WHY NO REFIT COULD HAVE SAVED IT.
    ///
    /// With the shoulder and hand fixed the elbow lies on a CIRCLE, so its redundancy is ONE SCALAR --
    /// the swivel angle. That reframe was right, and predicting the angle really does kill the snap at
    /// full extension. But AN ANGLE HAS TO BE MEASURED FROM SOMETHING, and that reference direction is
    /// where the whole thing came apart:
    ///
    ///     u = normalize(down - axis * dot(down, axis))
    ///
    /// |u| is the sine of the arm's angle off vertical. IT IS ZERO WHEN THE HAND IS DIRECTLY BELOW THE
    /// SHOULDER -- and measured across the 55,140-frame corpus, |u| < 0.2 on 29.7% OF REAL HUMAN FRAMES,
    /// with a minimum of 0.001. On nearly a third of poses the swivel was being measured against a
    /// direction that barely exists, and `normalizesafe` then hard-snapped it to a fallback.
    ///
    /// Instrumented, on the shipped model: the hand sways 3 cm fore-aft under the shoulder -- standing
    /// still, arms relaxed, the commonest pose in VR -- and the elbow swings 49 degrees, travelling
    /// 19.6 cm. That is the flip.
    ///
    /// AND IT IS NOT A CHOICE OF REFERENCE. The pole is a unit tangent vector on the sphere of hand
    /// directions, so by the HAIRY BALL THEOREM every continuous choice of it vanishes SOMEWHERE. (The
    /// same fact in coordinates: sin and cos fitted as independent affine functions have a common zero
    /// LINE -- two planes meet in a line.) The singularity cannot be deleted. It can only be MOVED.
    /// The shipped model moved it onto the rest pose.
    ///
    /// A POSITION IS NOT A TANGENT VECTOR, so it carries no such obstruction. Predict the elbow's
    /// position, project it onto the circle, and the only degeneracy left is "the predicted elbow lands
    /// ON the shoulder->hand axis" -- which is 0.036% of the reachable workspace and is faded, not
    /// gated. Nothing to snap, no reference frame, no atan2, no confidence cliff.
    /// ================================================================================================
    ///
    /// MEASURED, leave-one-CLIP-out over the corpus, elbow position error as % of arm length, and
    /// elbow travel per unit of HAND travel over the whole reachable workspace (a real elbow tracks its
    /// hand at 0.5-1.5x; above ~5x reads as a flip):
    ///
    ///                                 err     gain p99   >20x       idle-sway step
    ///     BasisArmSwivelModel ....  4.76 %      68 x     1.67 %        1.13 cm
    ///     THIS MODEL ............  4.19 %     5.2 x     0.065 %        0.19 cm
    ///
    /// It is more accurate out of sample with TWELVE coefficients against sixty-four. The old cubic's
    /// coefficients ran to 35 not because a human elbow needs a cubic, but because it was spending its
    /// capacity cancelling the singularity of its own reference frame.
    ///
    /// FITTED ON THE HARNESS'S OWN DUMPED FEATURES (BasisMocapAccuracy -> basis_swivel_train.csv), so
    /// the fit frame and the eval frame cannot disagree. A mismatch in any one of handedness / body
    /// frame / mirror does not degrade this kind of model, it produces CONFIDENT GARBAGE -- that has
    /// happened twice on this project. DO NOT HAND-EDIT THE COEFFICIENTS. Re-fit and re-generate.
    /// </summary>
    [BurstCompile]
    public static class BasisElbowFieldModel
    {
        /// <summary>|pole| below this (as a fraction of arm length) and the predicted elbow is so close to
        /// the shoulder->hand axis that its DIRECTION is noise. Fade, never gate: a hard gate here is what
        /// dropped the hint entirely and handed the elbow back to the animation clip.</summary>
        public const float FadeLo = 0.02f;

        /// <summary>At or above this the model is trusted outright, so everything in the fitted domain is
        /// bit-for-bit the unfaded answer.</summary>
        public const float FadeHi = 0.10f;

        /// <summary>The anatomical rest pole, in the mirrored body frame (+x OUT, +y UP, +z FWD): an elbow
        /// hangs DOWN, a little OUT, and a little BACK. Consulted only where the field above has already
        /// gone to noise. Magnitude is irrelevant; only the direction is used.</summary>
        static readonly float3 k_RestPole = new float3(0.35f, -1.0f, -0.15f);

        /// <summary>
        /// The elbow's position relative to the shoulder, in the mirrored body frame, in arm lengths.
        ///
        /// `tipLocal` is (hand - shoulder) in the same frame and units, with +x OUTWARD for both limbs so
        /// one model serves both. The caller un-mirrors x on the way back out.
        /// </summary>
        public static float3 Elbow(float3 tipLocal)
        {
            float len = math.length(tipLocal);
            float3 t = len > 1f ? tipLocal / len : tipLocal;
            float x = t.x, y = t.y, z = t.z;

            return new float3(
                (+0.25611932f) + (+0.23203308f) * x + (+0.23016090f) * y + (-0.03095514f) * z,
                (-0.16631846f) + (+0.09813791f) * x + (+0.35133371f) * y + (-0.10962090f) * z,
                (-0.03474265f) + (-0.06358632f) * x + (+0.12388336f) * y + (+0.45664834f) * z);
        }

        /// <summary>
        /// The bend direction the two-bone solver wants: a UNIT vector PERPENDICULAR to the shoulder->hand
        /// axis. Perpendicular by construction, so it lies on the elbow's reachable circle and the solver
        /// has nothing to project away and no near-singular projection to guard.
        ///
        /// `elbowLocal` is this model's prediction, `tipLocal` the hand, both in the mirrored body frame.
        /// `conditioning` comes back as the pole's lever arm in arm lengths -- a purely GEOMETRIC number
        /// (how far the predicted elbow stands off the arm's own axis), not a regression's opinion of
        /// itself. It is reported for tests and gizmos; the fade is already applied.
        /// </summary>
        public static float3 BendDirection(float3 tipLocal, float3 elbowLocal, out float conditioning)
        {
            float3 axis = math.normalizesafe(tipLocal, new float3(0f, -1f, 0f));

            float3 perp = elbowLocal - axis * math.dot(elbowLocal, axis);
            conditioning = math.length(perp);

            float3 restPerp = k_RestPole - axis * math.dot(k_RestPole, axis);
            float3 rest = math.normalizesafe(restPerp, new float3(0f, 0f, -1f));

            float w = math.saturate((conditioning - FadeLo) / (FadeHi - FadeLo));
            w = w * w * (3f - 2f * w);

            float3 primary = conditioning > 1e-6f ? perp / conditioning : rest;
            return math.normalizesafe(primary * w + rest * (1f - w), rest);
        }
    }
}
