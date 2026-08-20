using Unity.Burst;
using Unity.Mathematics;

namespace Basis.IK
{
    /// <summary>
    /// Tuning for <see cref="BasisNodPivotEstimatorCore"/>. Every field is a gate: with the defaults
    /// the estimator stays silent until the head has actually swept enough of an arc to see the pivot.
    /// </summary>
    public struct BasisNodPivotSettings
    {
        /// <summary>
        /// Tikhonov weight, as a fraction of the window's own excitation (trace(M)/3). A nod excites the
        /// arm's Y and Z but never its X, so the normal matrix is rank deficient by construction; pulling
        /// the unobserved directions back toward the prior is what keeps the solve well posed instead of
        /// needing rank detection.
        /// </summary>
        public float PriorWeight;

        /// <summary>Minimum spread of gaze pitch across the window, degrees. Below this the arc is too short to fit.</summary>
        public float MinPitchRangeDeg;

        /// <summary>Minimum fraction of the window's positional variance the fitted arc has to explain (an R-squared).</summary>
        public float MinFitQuality;

        /// <summary>
        /// Largest RMS wander of the recovered pivot across the window, metres, before <see cref="Scale"/>.
        /// A ratio test cannot catch a squat taken mid-window: the squat inflates the total variance as
        /// much as the residual, so R-squared stays near 1 while the arm absorbs the drop. Measuring the
        /// pivot's own wander is the same test in the space where the nod has already been divided out,
        /// and it needs an absolute bound rather than a ratio.
        /// </summary>
        public float MaxPivotSpreadMeters;

        /// <summary>
        /// Largest vertical excursion the window may contain, metres, before <see cref="Scale"/>. A squat
        /// taken in step with a look-down is genuinely ambiguous -- dropping the body and lengthening the
        /// arm predict the same HMD path -- so the pivot-spread test cannot separate them and the fit
        /// quietly grows the arm instead. Nodding alone cannot move the HMD vertically by more than about
        /// twice the arm, so bounding the excursion is what tells the two apart.
        /// </summary>
        public float MaxVerticalRangeMeters;

        /// <summary>
        /// Half-extent of the plausible arm box, metres, before <see cref="Scale"/>. X is the lateral slack.
        /// Anatomy, not slack: this is the last line of defence when a correlated body movement biases the
        /// fit, so it is sized to the population and not to what the solver might return.
        /// </summary>
        public float3 MaxArm;

        /// <summary>Avatar scale. The arm is a body dimension, so the box scales with the body.</summary>
        public float Scale;
    }

    public struct BasisNodPivotResult
    {
        /// <summary>Eye-from-pivot, head-local. The prior, unchanged, when <see cref="Accepted"/> is false.</summary>
        public float3 Arm;

        public bool Accepted;

        /// <summary>Fraction of the window's positional variance explained by the fitted arc.</summary>
        public float FitQuality;

        public float PitchRangeDeg;

        /// <summary>RMS wander of the recovered pivot across the window, metres.</summary>
        public float PivotSpreadMeters;

        /// <summary>Peak-to-peak vertical excursion of the HMD across the window, metres.</summary>
        public float VerticalRangeMeters;
    }

    /// <summary>
    /// Recovers the point the user's head actually pivots about when they nod.
    /// <para>
    /// The virtual spine reconstructs the head bone as <c>eye + eyeRot * (tposeHead - tposeEye)</c>, so it
    /// treats the AVATAR's authored eye-to-head offset as the nod pivot. A real nod pivots at the base of
    /// the skull, which for most people sits further below and further behind the HMD than any avatar
    /// authors it. The leftover arc, <c>(R - I) * (avatarArm - realArm)</c>, translates the whole
    /// reconstructed body -- pelvis included -- every time the gaze pitches, which is what reads as "the
    /// hips are not under me when I look up".
    /// </para>
    /// <para>
    /// The pivot is observable from the HMD alone: over a window where the user is not walking,
    /// <c>p_i = c + R_i * a</c> for a fixed pivot <c>c</c> and arm <c>a</c>. Differencing against the
    /// window mean removes <c>c</c> and leaves a linear least squares in <c>a</c>.
    /// </para>
    /// </summary>
    [BurstCompile]
    public static class BasisNodPivotEstimatorCore
    {
        private const float k_DetEpsilon = 1e-12f;

        /// <summary>Below this the window carries no positional variance to explain and R-squared is meaningless.</summary>
        private const float k_MinVariance = 1e-8f;

        public static BasisNodPivotSettings Defaults()
        {
            BasisNodPivotSettings s;
            s.PriorWeight = 0.15f;
            s.MinPitchRangeDeg = 18f;
            s.MinFitQuality = 0.70f;
            s.MaxPivotSpreadMeters = 0.02f;
            s.MaxVerticalRangeMeters = 0.20f;
            // The atlanto-occipital joint sits roughly 2-8 cm below and 8-16 cm behind an HMD's centre eye
            // across the population; this is that range with margin, not an open-ended bound.
            s.MaxArm = new float3(0.05f, 0.12f, 0.20f);
            s.Scale = 1f;
            return s;
        }

        /// <summary>
        /// Fits the eye-from-pivot arm over <paramref name="count"/> samples. Returns the prior untouched
        /// unless the window is both well excited and well explained, so a caller can use the result
        /// unconditionally.
        /// </summary>
        public static void Solve(
            float3[] positions,
            quaternion[] rotations,
            int count,
            float3 priorArm,
            in BasisNodPivotSettings settings,
            out BasisNodPivotResult result)
        {
            result = default;
            result.Arm = priorArm;

            if (positions == null || rotations == null || count < 4) return;
            if (count > positions.Length || count > rotations.Length) return;

            // Excitation: the fit can only see the arm through the arc the head actually swept.
            float minPitch = float.MaxValue;
            float maxPitch = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                float3 fwd = math.mul(rotations[i], new float3(0f, 0f, 1f));
                float horiz = math.sqrt(fwd.x * fwd.x + fwd.z * fwd.z);
                float pitch = math.degrees(math.atan2(fwd.y, horiz));
                minPitch = math.min(minPitch, pitch);
                maxPitch = math.max(maxPitch, pitch);
            }
            result.PitchRangeDeg = maxPitch - minPitch;
            if (!(result.PitchRangeDeg >= settings.MinPitchRangeDeg)) return;

            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                minY = math.min(minY, positions[i].y);
                maxY = math.max(maxY, positions[i].y);
            }
            result.VerticalRangeMeters = maxY - minY;
            if (!(result.VerticalRangeMeters <= settings.MaxVerticalRangeMeters * math.max(1e-3f, settings.Scale))) return;

            float inv = 1f / count;
            float3 meanPos = float3.zero;
            float3x3 meanRot = float3x3.zero;
            for (int i = 0; i < count; i++)
            {
                meanPos += positions[i];
                meanRot += new float3x3(rotations[i]);
            }
            meanPos *= inv;
            meanRot = MulScalar(meanRot, inv);

            // M a = b, with D_i = R_i - meanRot and dp_i = p_i - meanPos.
            float3x3 M = float3x3.zero;
            float3 b = float3.zero;
            float totalVar = 0f;
            for (int i = 0; i < count; i++)
            {
                float3x3 D = new float3x3(rotations[i]) - meanRot;
                float3 dp = positions[i] - meanPos;
                M += math.mul(math.transpose(D), D);
                b += math.mul(math.transpose(D), dp);
                totalVar += math.lengthsq(dp);
            }

            if (!(totalVar > k_MinVariance)) return;

            // A pure nod leaves the arm's lateral component unobserved; regularising toward the prior is
            // what fills those directions in, instead of leaving the solve singular.
            float lambda = math.max(0f, settings.PriorWeight) * ((M.c0.x + M.c1.y + M.c2.z) / 3f);
            if (lambda > 0f)
            {
                M.c0.x += lambda;
                M.c1.y += lambda;
                M.c2.z += lambda;
                b += lambda * priorArm;
            }

            if (!TrySolveSymmetric3(in M, in b, out float3 arm)) return;
            if (!math.all(math.isfinite(arm))) return;

            // How much of the window's motion the fitted arc actually accounts for. A user who walked or
            // leaned mid-window leaves residual the arc cannot explain, and that is exactly the window
            // whose fit must not be trusted.
            float residual = 0f;
            for (int i = 0; i < count; i++)
            {
                float3x3 D = new float3x3(rotations[i]) - meanRot;
                residual += math.lengthsq(math.mul(D, arm) - (positions[i] - meanPos));
            }
            result.FitQuality = 1f - (residual / totalVar);
            if (!(result.FitQuality >= settings.MinFitQuality)) return;

            float scale = math.max(1e-3f, settings.Scale);

            // With the nod divided out, every sample should name the SAME pivot. Anything that moved the
            // user's body rather than just their head -- a squat, a step, a lean -- shows up here as
            // wander that the arm would otherwise have quietly absorbed.
            float3 pivotMean = float3.zero;
            for (int i = 0; i < count; i++) pivotMean += positions[i] - math.mul(rotations[i], arm);
            pivotMean *= inv;

            float pivotVar = 0f;
            for (int i = 0; i < count; i++)
            {
                pivotVar += math.lengthsq((positions[i] - math.mul(rotations[i], arm)) - pivotMean);
            }
            result.PivotSpreadMeters = math.sqrt(pivotVar * inv);
            if (!(result.PivotSpreadMeters <= settings.MaxPivotSpreadMeters * scale)) return;

            float3 box = settings.MaxArm * scale;
            // The pivot is behind and below the eye or it is not a neck; the box is one-sided on Y and Z.
            arm = math.clamp(arm, new float3(-box.x, 0f, 0f), box);

            result.Arm = arm;
            result.Accepted = true;
        }

        private static float3x3 MulScalar(float3x3 m, float s)
        {
            return new float3x3(m.c0 * s, m.c1 * s, m.c2 * s);
        }

        /// <summary>Cofactor inverse of a symmetric 3x3. Returns false when it is too near singular to trust.</summary>
        private static bool TrySolveSymmetric3(in float3x3 M, in float3 b, out float3 x)
        {
            x = default;

            float a00 = M.c0.x, a01 = M.c1.x, a02 = M.c2.x;
            float a11 = M.c1.y, a12 = M.c2.y;
            float a22 = M.c2.z;

            float c00 = a11 * a22 - a12 * a12;
            float c01 = a02 * a12 - a01 * a22;
            float c02 = a01 * a12 - a02 * a11;

            float det = a00 * c00 + a01 * c01 + a02 * c02;
            if (!(math.abs(det) > k_DetEpsilon)) return false;

            float c11 = a00 * a22 - a02 * a02;
            float c12 = a02 * a01 - a00 * a12;
            float c22 = a00 * a11 - a01 * a01;

            float invDet = 1f / det;
            x = new float3(
                (c00 * b.x + c01 * b.y + c02 * b.z) * invDet,
                (c01 * b.x + c11 * b.y + c12 * b.z) * invDet,
                (c02 * b.x + c12 * b.y + c22 * b.z) * invDet);
            return true;
        }
    }
}
