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

        // Re-aims a no-tracker bend direction for the chicken-wing, given an explicit engagement in [0,1].
        // engage=0 returns bend unchanged. engage=1 lands the pole exactly at +maxFlareDeg of swivel (out to
        // the half-T-pose mark). Between, the pole is pushed toward the cap and clamped so it never exceeds it.
        public static Vector3 ApplyFlare(Vector3 bend, Vector3 shoulderToHand, Vector3 outwardDir, Vector3 playerUp,
            float engage01, float maxFlareDeg)
        {
            float r = Mathf.Clamp01(engage01);
            if (r <= 0f) return bend; // neutral: exact no-op, no regression on non-chicken-wing reaches
            if (!BuildSwingBasis(shoulderToHand, outwardDir, playerUp, out Vector3 axis, out Vector3 downPole, out Vector3 outPole))
                return bend;

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
            if (!BuildSwingBasis(shoulderToHand, outwardDir, playerUp, out Vector3 axis, out Vector3 downPole, out Vector3 outPole))
                return 0f;

            Vector3 hUp = Vector3.ProjectOnPlane(handRot * Vector3.up, axis);
            if (hUp.sqrMagnitude < 1e-8f) return 0f;
            hUp.Normalize();

            // -downPole is "up" in the swing plane, so a hand-up aligned with body-up reads 0 deg.
            float aDeg = Mathf.Atan2(Vector3.Dot(hUp, outPole), Vector3.Dot(hUp, -downPole)) * Mathf.Rad2Deg;
            return Mathf.Clamp01((aDeg / Mathf.Max(1f, fullRollDeg)) * inwardGain);
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
        static bool BuildSwingBasis(Vector3 shoulderToHand, Vector3 outwardDir, Vector3 playerUp,
            out Vector3 axis, out Vector3 downPole, out Vector3 outPole)
        {
            axis = downPole = outPole = Vector3.zero;
            if (shoulderToHand.sqrMagnitude < 1e-10f) return false;
            axis = shoulderToHand.normalized;

            Vector3 dp = Vector3.ProjectOnPlane(-playerUp, axis);
            if (dp.sqrMagnitude < 1e-8f) return false; // forearm vertical: "down" undefined in the swing plane
            downPole = dp.normalized;

            Vector3 op = Vector3.ProjectOnPlane(outwardDir, axis);
            op -= downPole * Vector3.Dot(op, downPole); // orthonormalise against downPole, keeping the outward sign
            if (op.sqrMagnitude < 1e-8f) return false;
            outPole = op.normalized;
            return true;
        }
    }
}
