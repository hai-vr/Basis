namespace UnityEngine.Animations.Rigging
{
    // Stream-free "chicken-wing" flare for the NO-elbow-tracker (lookup) arm pole. Shared by the live rig
    // (BasisFullIKConstraintJob.ComputeArmBendFromLookup) and the offline sweep so both clamp identically --
    // the same Core pattern as BasisArmSolveCore / BasisChestSpringCore.
    //
    // Goal (users without elbow trackers): when the controllers are turned inward -- the "chicken-wing" pose
    // -- push the derived elbow OUT toward the half-T-pose mark, but HARD-CLAMP it there so the elbow never
    // crosses the halfway line between hanging-straight-down (0 deg of swivel) and straight-out-to-the-side /
    // T-pose (90 deg). Past that "won't feel right", and it must never wing UP.
    //
    // The pole is measured as a swivel angle in the swing plane perpendicular to the shoulder->hand (forearm)
    // axis: 0 deg = the elbow hangs straight down, +maxFlareDeg = out toward the side. "Engagement" in [0,1]
    // is how far into the chicken-wing the controller roll is; 0 is an exact no-op so normal reaches are
    // untouched. The runtime derives engagement from the controller roll (RollEngagement01); the offline
    // sweep / unit tests pass it explicitly so the clamp+push are verified without needing hand rotations.
    public static class BasisElbowFlareCore
    {
        // Engagement ramps the hard cap in over the first slice of the roll so engagement=0+ stays continuous
        // with the natural pole, yet a committed chicken-wing is firmly capped.
        const float k_CapEngageEnd = 0.3f;

        // Confidence fade for the roll measurement, keyed on how much of the hand's up-axis survives projection
        // into the swing plane. A LATENT guard -- measured to be a bit-for-bit no-op on the CMU corpus, and NOT
        // the buzz fix (that is k_BasisFade* below). See RollEngagement01 for why it is kept anyway.
        // Same shape as BasisArmSolveCore's hintFade (start 0.06, width 0.12); a projection magnitude IS a sine,
        // so 0.10 -> 0.25 means "no confidence within ~6 deg of the forearm axis, full confidence past ~15 deg".
        const float k_RollProjFadeStart = 0.10f;
        const float k_RollProjFadeFull = 0.25f;

        // Confidence fade for the SWING BASIS itself -- see BuildSwingBasis. A projection magnitude is a sine, so
        // 0.05 -> 0.20 means "no confidence within ~3 deg of vertical, full confidence past ~11.5 deg". Measured
        // on the CMU corpus the buzzy clips sit at a 5th-percentile projection of 0.03-0.045, i.e. inside this
        // window, while the clean clips sit at 0.19-0.28, i.e. outside it. The window is drawn from that data.
        const float k_BasisFadeStart = 0.05f;
        const float k_BasisFadeFull = 0.20f;

        // Re-aims a no-tracker bend direction for the chicken-wing, given an explicit engagement in [0,1].
        // engage=0 returns bend unchanged. engage=1 lands the pole exactly at +maxFlareDeg of swivel (out to
        // the half-T-pose mark). Between, the pole is pushed toward the cap and clamped so it never exceeds it.
        public static Vector3 ApplyFlare(Vector3 bend, Vector3 shoulderToHand, Vector3 outwardDir, Vector3 playerUp,
            float engage01, float maxFlareDeg)
        {
            float r = Mathf.Clamp01(engage01);
            if (r <= 0f) return bend; // neutral: exact no-op, no regression on non-chicken-wing reaches
            if (!BuildSwingBasis(shoulderToHand, outwardDir, playerUp, out Vector3 axis, out Vector3 downPole,
                                 out Vector3 outPole, out float basisConfidence))
                return bend;

            // The swivel is only as trustworthy as the basis it is measured in. Near-vertical forearm => hand back
            // the natural pole rather than a confident angle built on a collapsed reference. This is the fade that
            // stops the elbow buzzing when you reach down.
            r *= basisConfidence;
            if (r <= 0f) return bend;

            float cap = Mathf.Max(0f, maxFlareDeg);

            // Current pole's swivel in the (downPole=0 deg, outPole=+90 deg) plane: + out, - across body, +-180 up.
            Vector3 bendProj = Vector3.ProjectOnPlane(bend, axis);
            float s0 = bendProj.sqrMagnitude < 1e-10f
                ? 0f
                : Mathf.Atan2(Vector3.Dot(bendProj, outPole), Vector3.Dot(bendProj, downPole)) * Mathf.Rad2Deg;

            // Aim at +cap: a tucked elbow is pushed OUT toward the half-T-pose mark, a wider one pulled IN to it.
            float s = Mathf.Lerp(s0, cap, r);
            // Hard cap, ramped in over the first bit of engagement: r=0+ stays continuous with the natural pole,
            // but a committed chicken-wing can never cross the half-T-pose line (nor wing UP past it).
            float capNow = Mathf.Lerp(180f, cap, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / k_CapEngageEnd)));
            s = Mathf.Clamp(s, -capNow, capNow);

            float rad = s * Mathf.Deg2Rad;
            Vector3 pole = downPole * Mathf.Cos(rad) + outPole * Mathf.Sin(rad);
            return pole.sqrMagnitude > 1e-12f ? pole.normalized : bend;
        }

        // Chicken-wing engagement [0,1] derived from the controller roll about the forearm axis. Measures the
        // controller's up-axis roll in the swing plane: 0 = aligned with body-up, + = rolled toward the body's
        // OUTWARD side, which is the inward "chicken-wing" turn for the assumed controller convention. Scaled
        // by inwardGain (signed: set negative to flip if a setup rolls the other way; 0 disables) and the roll
        // angle that counts as fully engaged. Kept separate from ApplyFlare so the engagement and the geometry
        // are each unit-testable.
        public static float RollEngagement01(Quaternion handRot, Vector3 shoulderToHand, Vector3 outwardDir,
            Vector3 playerUp, float inwardGain, float fullRollDeg)
        {
            if (Mathf.Abs(inwardGain) < 1e-6f) return 0f;
            if (!BuildSwingBasis(shoulderToHand, outwardDir, playerUp, out Vector3 axis, out Vector3 downPole,
                                 out Vector3 outPole, out float basisConfidence))
                return 0f;
            if (basisConfidence <= 0f) return 0f;   // the roll angle is measured against downPole; if that is
                                                    // noise, so is the roll.

            // Confidence fade on the roll measurement.
            //
            // `hUp` is the hand's up-axis projected into the swing plane. As that projection collapses (hand's
            // up-axis approaching the forearm axis) the angle it carries stops meaning anything: normalizing a
            // near-zero vector amplifies numerical residue, and atan2 of residue is noise, not a roll. The old
            // guard was `sqrMagnitude < 1e-8f` -- magnitude 1e-4 -- which is numerically small but
            // GEOMETRICALLY MEANINGLESS: a projection of 0.001 (hand-up within 0.06 deg of the forearm axis)
            // sails straight through it and reports a confident, entirely fictional angle.
            //
            // Hard-gating a projection at an epsilon is a defect pattern this codebase has already been bitten
            // by and fixed three times (the arm solve's hintFade, the elbow-tracker cliff, the swivel
            // smoother's near-colinear swim). The fix is always the same shape: scale the measurement by how
            // much of it is REAL rather than trusting it up to an epsilon and then dropping it off a cliff.
            //
            // ⚠ HONESTY NOTE: this is a LATENT guard, not the fix for the elbow buzz. Measured against the CMU
            // corpus it changes nothing -- results are bit-identical, because a mocap hand's up-axis never gets
            // near the forearm axis in that data, so `confidence` is 1.0 on every frame. It is kept because a
            // VR controller's up-axis is a grip convention, not an anatomical one, and CAN line up with the
            // forearm in poses a marker-fitted mocap hand never reaches. It removes a latent noise source and
            // provably cannot regress anything. Do not credit it with the buzz fix.
            Vector3 hUp = Vector3.ProjectOnPlane(handRot * Vector3.up, axis);
            float proj = hUp.magnitude;   // = sin(angle between the hand's up-axis and the forearm axis)
            if (proj < 1e-6f) return 0f;
            hUp /= proj;

            // -downPole is "up" in the swing plane, so a hand-up aligned with body-up reads 0 deg.
            float aDeg = Mathf.Atan2(Vector3.Dot(hUp, outPole), Vector3.Dot(hUp, -downPole)) * Mathf.Rad2Deg;
            float engage = Mathf.Clamp01((aDeg / Mathf.Max(1f, fullRollDeg)) * inwardGain);

            // How much to believe that angle at all. At proj = 0 the roll is undefined; at proj = 1 the hand's
            // up-axis is square to the forearm and the reading is exact.
            float confidence = Mathf.Clamp01((proj - k_RollProjFadeStart) / (k_RollProjFadeFull - k_RollProjFadeStart));
            return engage * confidence * basisConfidence;
        }

        // Runtime convenience: derive engagement from the controller rotation, then flare.
        public static Vector3 ApplyChickenWingFlare(Vector3 bend, Vector3 shoulderToHand, Vector3 outwardDir,
            Vector3 playerUp, Quaternion handRot, float inwardGain, float fullRollDeg, float maxFlareDeg)
        {
            float r = RollEngagement01(handRot, shoulderToHand, outwardDir, playerUp, inwardGain, fullRollDeg);
            return ApplyFlare(bend, shoulderToHand, outwardDir, playerUp, r, maxFlareDeg);
        }

        // Orthonormal swing-plane basis perpendicular to the forearm axis: downPole (swivel 0 = straight down)
        // and outPole (swivel +90 = out to the body's outward side). Returns false when "down" or "out" is
        // undefined in the plane (forearm vertical, or outward parallel to the axis) -- caller leaves the pole.
        //
        // ⭐⭐ THIS IS WHERE THE ELBOW BUZZ WAS BORN. Read this before widening any threshold here.
        //
        // `downPole` is the reference the ENTIRE swivel angle is measured from -- both s0 in ApplyFlare and aDeg
        // in RollEngagement01 are angles *against it*, and the returned pole is literally built out of it. It
        // comes from projecting -playerUp onto the plane perpendicular to the forearm. So its magnitude is
        // sin(angle of the forearm off vertical), and it COLLAPSES whenever the forearm approaches vertical --
        // which is not an edge case, it is reaching down: picking a box off the floor, bending over, washing a
        // window, sitting down, an arm hanging at your side.
        //
        // The old guard was `dp.sqrMagnitude < 1e-8f`, i.e. magnitude 1e-4. Measured on the CMU corpus, the
        // buzzy clips drive this projection down to 0.003 -- the forearm within 0.17 degrees of vertical -- which
        // is THIRTY TIMES the guard and sails straight through it. `dp.normalized` then manufactures a confident
        // direction out of numerical residue, and every angle measured against it is noise.
        //
        // The evidence is a clean inverse correlation across all 20 clips: where the 5th-percentile projection
        // is 0.03-0.045 the engagement scalar's jitter is 0.075-0.085 and the elbow buzzes 0.56-1.36% of arm
        // length; where the projection stays at 0.19-0.28 the engagement jitter is 0.028-0.036 and the elbow is
        // clean. It is also why SmoothElbowSwivel could not rescue it: that One-Euro deliberately OPENS its
        // cutoff on sustained swivel velocity so a real reach does not lag, and this noise arrives precisely
        // when the arm is moving.
        //
        // FADE, DO NOT GATE -- the same fix this codebase has already applied three times to this same defect
        // family (the arm solve's hintFade, the elbow-tracker projNorm cliff, the swivel smoother's
        // near-colinear swim). A projection magnitude is a confidence, so weight the measurement by it rather
        // than trusting it right up to an epsilon and then dropping it off a cliff.
        //
        // Behaviourally this is also the RIGHT answer, not just the numerically safe one: with the forearm
        // vertical, "swivel about the forearm axis" is genuinely degenerate -- the elbow can sit anywhere on a
        // circle and there is no meaningful "down" to measure it against. A chicken-wing flare has nothing to
        // say there, so it should hand back the natural pole and stay quiet.
        static bool BuildSwingBasis(Vector3 shoulderToHand, Vector3 outwardDir, Vector3 playerUp,
            out Vector3 axis, out Vector3 downPole, out Vector3 outPole, out float confidence)
        {
            axis = downPole = outPole = Vector3.zero;
            confidence = 0f;
            if (shoulderToHand.sqrMagnitude < 1e-10f) return false;
            axis = shoulderToHand.normalized;

            Vector3 dp = Vector3.ProjectOnPlane(-playerUp, axis);
            float dpMag = dp.magnitude;                    // = sin(angle of the forearm off vertical)
            if (dpMag < 1e-6f) return false;               // forearm exactly vertical: "down" does not exist
            downPole = dp / dpMag;

            Vector3 op = Vector3.ProjectOnPlane(outwardDir, axis);
            op -= downPole * Vector3.Dot(op, downPole);    // orthonormalise against downPole, keeping the outward sign
            float opMag = op.magnitude;
            if (opMag < 1e-6f) return false;
            outPole = op / opMag;

            // How much this basis is worth. Keyed on the WEAKER of the two projections, because the swivel is
            // only as well-defined as its worst reference axis.
            float weakest = Mathf.Min(dpMag, opMag);
            confidence = Mathf.Clamp01((weakest - k_BasisFadeStart) / (k_BasisFadeFull - k_BasisFadeStart));
            return true;
        }
    }
}
