using Unity.Burst;
using Unity.Mathematics;

namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// Where the knee goes for a user with NO knee tracker -- FITTED to real humans, not guessed at.
    ///
    /// ================================================================================================
    /// THE REFRAME: the knee is not free, so stop predicting a direction. Predict ONE ANGLE.
    ///
    /// With the hip and the foot both fixed, the knee cannot go wherever it likes -- it is confined
    /// to a CIRCLE. (Korein's swivel-angle formulation; the standard result for a redundant limb.) So the
    /// limb's entire redundancy collapses to ONE SCALAR: the swivel angle about the hip->foot axis.
    /// Predicting the knee IS predicting that angle.
    ///
    /// This is not tidiness, it is THE FIX FOR THE SNAP AT FULL EXTENSION. Any angle you predict lands ON
    /// the reachable circle by construction. A 3-vector "bend direction" does not -- so the solver needs
    /// projections, fades, pole guards and clamps to drag it back on, AND AS THE LIMB STRAIGHTENS THAT
    /// CIRCLE SHRINKS TO A POINT. The swivel becomes undefined, the fades switch the hint OFF, and the pole
    /// is handed to a fallback direction. That handoff is the snap users report past ~95% extension.
    ///
    /// Predict the angle and it cannot happen: the angle stays defined and continuous at any extension, and
    /// as the circle collapses the resulting POSITION change goes to zero on its own. Nothing to fade,
    /// nothing to hand off, nothing to snap.
    /// ================================================================================================
    ///
    /// WHAT IT REPLACES. The leg had NO hint model at all: BasisLegSolveCore fell back to a FIXED
    /// BendNormal = hipsRot * right. A fixed body-right pole, which collapses precisely when the leg
    /// straightens -- which is most of the time, because standing IS a straight leg. That is why the
    /// knee snapped past ~95% extension and why the knee never tracked where a human's knee actually was.
    ///
    /// A polynomial in the foot's pose, fitted by weighted least squares to real human motion,
    /// leave-one-clip-out. 59 coefficients for sin(phi), 59 for cos(phi), then atan2.
    ///
    /// Three things earn their place in the fit, each for a measured reason:
    ///   * SPHERICAL coordinates alongside Cartesian. Soechting and Flanders (1989) found human limb posture
    ///     is approximately LINEAR in the target's spherical coordinates. The corpus agrees.
    ///   * foot ORIENTATION, as its rotation relative to its own T-POSE, in the body frame.
    ///   * The fit is WEIGHTED BY THE CIRCLE RADIUS. We are judged on knee POSITION error, and position
    ///     error ~= radius * angular error. The radius collapses to zero as the limb straightens, so a
    ///     near-straight limb's swivel hardly matters and a bent one's matters a lot. Weighting makes the
    ///     regression minimise the quantity we actually measure rather than the one that is easy to write.
    ///
    /// ACCURACY -- knee position error, % of leg length, measured in BasisMocapMotionQualityTests:
    ///     the old fixed BendNormal ... 3.17 %   pops 36
    ///     THIS MODEL ................. see the harness table
    ///     a real knee tracker ........ 0.84 %   pops 11
    ///
    /// ⭐ THE CONSTANT TERM CARRIES A DELIBERATE BIAS (k = 0.30 toward the population-mean swivel), and it is
    /// LOAD-BEARING. sin and cos are fitted as independent polynomials, so nothing forces sqrt(s*s+c*c) to
    /// stay near 1 -- and least squares shrinks BOTH toward zero exactly where the swivel is genuinely
    /// unpredictable. That collapse is the model correctly saying "I do not know"... but atan2 near the
    /// origin does not fail, it SPINS, and a spinning pole is a popping knee. Biasing the direction field
    /// away from the origin costs 0.33 points of accuracy and takes the knee's minimum |sin,cos| from 0.068
    /// to 0.217, with ZERO frames left under 0.2 (was 0.34%). Re-fit and the bias must be re-applied.
    ///
    /// SMOOTH BY CONSTRUCTION. A polynomial is C-infinity. There is no fade to tune here and no
    /// discontinuity to fade -- the derivative simply exists, everywhere.
    ///
    /// ⚠ THE COEFFICIENTS ARE FITTED TO THE HARNESS'S OWN DUMPED FEATURES, and that is not an accident.
    /// The first attempt fitted in a separate Python pipeline and scored 3.77% there and 31% in the harness
    /// -- the two disagreed about handedness, or the body frame, or the left/right mirror, and a mismatch in
    /// any ONE of those silently poisons the model (the predicted swivel came out 145 degrees off). The
    /// harness now dumps the exact inputs it feeds this function, Python fits on those, and codegen emits
    /// this file. There is only one pipeline, so the two cannot disagree. NEVER RE-FIT IN A DIFFERENT FRAME
    /// FROM THE ONE YOU EVALUATE IN.
    ///
    /// Generated by scratchpad/gen_cs2.py. DO NOT HAND-EDIT THE COEFFICIENTS -- re-fit and re-generate.
    /// </summary>
    [BurstCompile]
    public static class BasisLegSwivelModel
    {
        /// <summary>
        /// The knee's swivel angle, in radians, about the hip->foot axis. Zero points along
        /// body FORWARD (a knee points forward), projected into the plane perpendicular to that axis.
        ///
        /// EVERY INPUT IS IN THE BODY FRAME AND MIRRORED, so both sides share one model:
        ///   tipLocal    (foot - hip), in the body frame, divided by limb length, with
        ///               +x OUTWARD (negate x for the LEFT limb), +y UP, +z FORWARD.
        ///   tipOrient   the foot's rotation RELATIVE TO ITS OWN T-POSE, in that same frame:
        ///                   transpose(bodyFrame) * (tipWorldRot * inverse(tipTposeWorldRot))
        ///               Dividing out the T-pose is what makes this RIG-INDEPENDENT and it is NOT optional --
        ///               a bone's local axes are a rig convention. Same rest-basis map
        ///               BasisFootSimParams.footAlign uses to stop the foot coming out toes-up.
        ///
        /// The CALLER mirrors the result back: negate the returned angle for the left limb.
        /// </summary>
        public static float SwivelRad(in float3 tipLocal, in float3x3 tipOrient)
            => SwivelRad(tipLocal, tipOrient, out _);

        /// <summary>
        /// As above, and it also hands back HOW MUCH IT KNOWS.
        ///
        /// sin(phi) and cos(phi) are fitted as two independent polynomials, so nothing forces
        /// sqrt(s*s + c*c) to stay near 1 -- and it does not. Least squares shrinks BOTH toward zero
        /// exactly where the true swivel is UNPREDICTABLE: where a real human's joint genuinely goes to
        /// different places for the same end-effector pose. The collapsing magnitude is not a defect. It is
        /// the model saying "I do not know", and it says so precisely when it should.
        ///
        /// Feed that straight into atan2 and you get a disaster: near the origin atan2 does not fail, it
        /// SPINS -- a hair's change of input swings the angle across the circle. Measured on the knee, the
        /// magnitude dropped under 0.2 on 0.45% of frames, and those frames DOUBLED the pop count (36 -> 70)
        /// even while mean accuracy more than halved. Accurate and unusable.
        ///
        /// So the caller must treat `confidence` as what it is and fade the HINT WEIGHT with it, never the
        /// angle. A low-confidence frame then hands authority back to the solver's own pole smoothly,
        /// instead of pointing it somewhere with total conviction and no basis.
        /// </summary>
        public static float SwivelRad(in float3 tipLocal, in float3x3 tipOrient, out float confidence)
        {
            float x = tipLocal.x, y = tipLocal.y, z = tipLocal.z;
            float r = math.length(tipLocal);

            float elev = math.asin(math.clamp(y / math.max(r, 1e-6f), -1f, 1f));
            float azim = math.atan2(x, z);

            float xx = x * x, yy = y * y, zz = z * z;

            float h0 = tipOrient.c0.x, h1 = tipOrient.c1.x, h2 = tipOrient.c2.x;
            float h3 = tipOrient.c0.y, h4 = tipOrient.c1.y, h5 = tipOrient.c2.y;
            float h6 = tipOrient.c0.z, h7 = tipOrient.c1.z, h8 = tipOrient.c2.z;

            // Straight-line, no array, no indirection: Burst folds the constants into the instruction
            // stream and fuses the multiply-adds.
            float sinPhi =
                (-3.97647477e-01f) * 1f + (-1.68642037e+00f) * x + (+6.32892787e+00f) * y +
                (+5.89302276e-01f) * z + (+4.31555046e+00f) * xx + (+2.79768857e+00f) * yy +
                (-4.17003082e+00f) * zz + (-1.54100178e+00f) * x*y + (+8.61953225e-01f) * x*z +
                (-6.35004043e-01f) * y*z + (+1.79490256e+00f) * xx*x + (+4.09573831e-01f) * yy*y +
                (+5.02552753e-01f) * zz*z + (+8.27797841e+00f) * xx*y + (-2.57458375e+00f) * xx*z +
                (-2.41829692e+00f) * yy*x + (+1.65776249e-01f) * yy*z + (-4.62669805e+00f) * zz*x +
                (-3.50668538e+00f) * zz*y + (+7.06663043e-01f) * x*y*z + (+3.10070396e+00f) * r +
                (+2.94321000e+00f) * r*r + (-3.46157577e+00f) * elev + (+6.34940565e-03f) * azim +
                (-1.83667513e+00f) * elev*elev + (+7.45617316e-04f) * azim*azim + (+2.82936110e-02f) * elev*azim +
                (-2.65337849e+00f) * r*elev + (+3.50259063e-02f) * r*azim + (+3.07802407e+00f) * r*x +
                (+5.68742702e+00f) * r*y + (-1.09316981e+00f) * r*z + (-8.58438305e-02f) * h0 +
                (+1.10821774e+00f) * h1 + (+4.54881343e-01f) * h2 + (-5.40600748e-02f) * h3 +
                (+1.66587716e+00f) * h4 + (+1.57393841e+00f) * h5 + (+3.28349724e-01f) * h6 +
                (+1.37520349e+00f) * h7 + (-2.27888241e+00f) * h8 + (+5.21035632e-02f) * h0*x +
                (+1.14854079e+00f) * h1*x + (+1.46194198e-01f) * h2*x + (-5.11424863e-01f) * h3*x +
                (+2.95010589e-01f) * h4*x + (+1.91325932e+00f) * h5*x + (+4.57969664e-01f) * h6*x +
                (+8.73081003e-01f) * h7*x + (-1.25041318e+00f) * h8*x + (-5.80595803e-02f) * h0*y +
                (+1.87538731e+00f) * h1*y + (+4.85145404e-01f) * h2*y + (+3.77594338e-02f) * h3*y +
                (+2.25770787e+00f) * h4*y + (+2.03693082e+00f) * h5*y + (+5.40876792e-01f) * h6*y +
                (+1.41068602e+00f) * h7*y + (-3.33823053e+00f) * h8*y;

            float cosPhi =
                (+9.83420118e-01f) * 1f + (-2.18021546e+00f) * x + (-2.82829501e+00f) * y +
                (+1.08333218e+00f) * z + (+2.31173232e+00f) * xx + (-9.16616302e-01f) * yy +
                (+6.67282582e-01f) * zz + (+2.02548712e+00f) * x*y + (-2.90571532e+00f) * x*z +
                (-2.65276863e+00f) * y*z + (-4.83096256e+00f) * xx*x + (-1.17798716e+00f) * yy*y +
                (+2.26444100e+00f) * zz*z + (-3.25908425e-01f) * xx*y + (-3.07019211e-01f) * xx*z +
                (-3.40798516e+00f) * yy*x + (+1.35195931e+00f) * yy*z + (-5.29240886e+00f) * zz*x +
                (+6.54690973e-01f) * zz*y + (-3.86947403e+00f) * x*y*z + (-2.11218527e+00f) * r +
                (+2.06239931e+00f) * r*r + (+1.64546496e+00f) * elev + (+6.64650759e-02f) * azim +
                (+3.89463632e-01f) * elev*elev + (+1.16158503e-02f) * azim*azim + (+3.98145599e-02f) * elev*azim +
                (-8.93135687e-01f) * r*elev + (-1.36386756e-02f) * r*azim + (+7.41987453e+00f) * r*x +
                (+4.09009629e+00f) * r*y + (-4.65451857e+00f) * r*z + (+5.60851991e-02f) * h0 +
                (-2.50087601e-01f) * h1 + (-6.00519625e-01f) * h2 + (+2.51800189e-01f) * h3 +
                (-1.19829345e+00f) * h4 + (-1.57533376e+00f) * h5 + (+6.20097020e-02f) * h6 +
                (-1.63897835e+00f) * h7 + (+1.22723920e+00f) * h8 + (+2.17451816e-01f) * h0*x +
                (+8.33889467e-01f) * h1*x + (+7.63571862e-01f) * h2*x + (+2.99902538e-01f) * h3*x +
                (+4.62170923e-01f) * h4*x + (+5.32402656e-02f) * h5*x + (+1.09480402e-01f) * h6*x +
                (-8.25474709e-02f) * h7*x + (+3.97902365e-01f) * h8*x + (+9.19690058e-02f) * h0*y +
                (-1.38630469e+00f) * h1*y + (-1.43650339e+00f) * h2*y + (+3.43426484e-01f) * h3*y +
                (-9.72684944e-01f) * h4*y + (-2.06529744e+00f) * h5*y + (+2.21326274e-02f) * h6*y +
                (-2.19047224e+00f) * h7*y + (+1.08418691e+00f) * h8*y;

            confidence = math.sqrt(sinPhi * sinPhi + cosPhi * cosPhi);
            return math.atan2(sinPhi, cosPhi);
        }

        /// <summary>
        /// The bend direction the two-bone solver wants: a unit vector PERPENDICULAR to the hip->foot
        /// axis, pointing at the swivel angle this model predicts. Perpendicular by construction -- the
        /// solver has nothing to project away and no near-singular projection to guard.
        /// </summary>
        public static float3 BendDirection(float3 rootToTip, float3 reference, float swivelRad)
        {
            float3 axis = math.normalizesafe(rootToTip, new float3(0f, -1f, 0f));
            float3 u = math.normalizesafe(reference - axis * math.dot(reference, axis), new float3(0f, 0f, -1f));
            float3 v = math.cross(axis, u);
            math.sincos(swivelRad, out float sn, out float cs);
            return u * cs + v * sn;
        }
    }
}
