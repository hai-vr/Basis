using UnityEngine;
namespace Basis.IK
{
    public struct BasisElbowProtectInput
    {
        // Post arm-solve pose (shoulder/elbow/hand), in the same space as the torso points.
        public Vector3 Shoulder;
        public Vector3 Elbow;
        public Vector3 Hand;

        // Torso capsule segments. Hips->Spine and Spine->Chest are only checked when the
        // matching flag is set (mirrors the live job's HandleHips/HandleSpine validity gating).
        public Vector3 HipsPos;
        public Vector3 SpinePos;
        public Vector3 ChestPos;   // chest segment start; also seeds the shoulder-out direction
        public Vector3 NeckPos;    // chest segment end
        public bool HasHips;
        public bool HasSpine;

        // Raw radii inputs; the core derives the segment radii so the live rig and the sweep
        // share the exact same derivation.
        public float ChestRadiusBase;
        public float CollisionSkin;
        public float HandRadius;
        public float HandSkin;

        public Vector3 PlayerUp;

        // Body-lateral (left<->right) unit direction, from shoulder-to-shoulder (RightUpperArm - LeftUpperArm).
        // Orients the torso's ELLIPTICAL cross-section: the segment radius is the WIDE (lateral) half-width and
        // the front-back half-depth is a fraction of it (see k_ChestDepthRatio). Zero (the struct default) falls
        // back to deriving the axis from the shoulder's own offset, so old callers that never set it still work.
        public Vector3 BodyRight;
    }

    public struct BasisElbowProtectResult
    {
        public bool Engaged;          // true when a swivel was actually produced
        public int CollisionState;    // 0 none, 1 cleared the torso, 2 engaged but could not fully clear
        public Vector3 DesiredElbow;  // elbow position to swing to; == input Elbow when not engaged
        public float WorstPenetration;
        public float SideDot;         // dot(currentDir, outDir)
        public float BlendUsed;       // fraction of the current->out swivel arc taken (0..1)
        public float SwingAngleDeg;   // angle the elbow is swung this solve
        public float ElbowRadius;
        public Vector3 ElbowCenter;
        public float ResidualClearance; // signed torso clearance at DesiredElbow (>=0 cleared, <0 still penetrating)
    }

    // Stream-free torso-collision elbow push shared by BasisFullIKConstraintJob.SolveHand and the
    // offline BasisElbowProtectSweep harness. Change the protect-elbow math HERE so both stay in
    // lock-step. Reuses the job's public capsule helpers so the penetration test never drifts.
    //
    // The elbow is constrained to a circle around the shoulder->hand axis (the hand stays on its
    // target), so the only freedom is the swivel. When the natural (arm-solved / elbow-tracker)
    // pose penetrates the torso, swing the elbow toward "out" by the MINIMUM that leaves the torso;
    // if nothing on the out side clears (hand too close to the body to wrap the arm around), hold
    // the least-penetrating swivel. This keeps the elbow as close to natural as the geometry allows
    // instead of pinning it to a fixed direction.
    public static class BasisElbowProtectCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_ClearMargin = 0.003f;   // land a hair (3 mm) outside the torso surface
        const int k_SwivelSteps = 48;

        // =============================================================================================
        // THE TORSO IS AN ELLIPSE, NOT A CIRCLE. A real chest is roughly 1.4x wider left-to-right than it is
        // deep front-to-back. The old model was a round capsule with one radius, so it had to be a compromise:
        // fat enough to cover the SIDES, it stood far too proud of the FRONT, and a cross-body reach (the arm
        // coming across the chest, front-on) got shoved off it -- "the collider is a little big and not well
        // formed". So the per-segment radius is now the WIDE (lateral) half-width, and the front-back half-depth
        // is this fraction of it: the arm can hug the chest FRONT while the SIDES stay covered exactly as before.
        // 0.68 is the anatomical chest depth:width ratio; the sides are unchanged, only the front is drawn in.
        // =============================================================================================
        const float k_ChestDepthRatio = 0.68f;

        // =============================================================================================
        // THE PROTECT'S ONLY AUTHORITY IS THE ELBOW'S CIRCLE RADIUS, AND IT MUST FADE OUT WITH IT.
        //
        // This whole file clears the torso by SWIVELLING the elbow about the shoulder->hand axis. That
        // rotation does two things:
        //
        //     it MOVES THE ELBOW ...... by rho * theta      <- the point of the exercise
        //     it ROLLS THE UPPER ARM .. by theta            <- an unavoidable side effect
        //
        // rho is the elbow's stand-off from the shoulder->hand axis, and IT COLLAPSES AS THE ARM
        // STRAIGHTENS. So at full extension the benefit goes to zero and THE COST DOES NOT: the swivel
        // axis IS the arm's own long axis there, and the entire correction lands as ROLL.
        //
        // Measured, on the user's report ("fully extend the arm, then cross it over the body as far as
        // humanly possible -- the elbow does a nasty rotation"): the protect engages (crossing your body
        // is exactly what presses the upper arm into the chest), commands a steady ~47 degree swing, and
        // at extension >= 0.999 that swing is PURE ROLL -- 47 to 51 degrees of upper-arm spin per
        // MILLIMETRE of hand travel. With the protect disabled the same sweep measures 0.0.
        //
        // FADE ON THE REACH RATIO, NOT ON rho. rho = sqrt(upper^2 - (d/2)^2) is SQUARE-ROOT SINGULAR in
        // the hand position -- its own gradient blows up exactly where we need the fade to be gentle, so
        // fading on it just converts a cliff into a steep ramp (measured: 9-10 deg/mm, versus 2-3 for
        // this). The reach ratio is smooth and its derivative is bounded by 1/armLen.
        //
        // Below Start the protect is BIT-IDENTICAL to what it always was. It reaches zero authority at
        // End, where rho is 3 cm and falling and there is nothing left for a swivel to achieve anyway.
        // =============================================================================================
        const float k_AuthorityFadeStart = 0.95f;   // rho = 9.4 cm: the elbow can still travel 18.7 cm
        const float k_AuthorityFadeEnd = 0.995f;    // rho = 3.0 cm: a swivel is mostly roll by here

        public static void Solve(in BasisElbowProtectInput i, out BasisElbowProtectResult r)
        {
            r = default;
            r.DesiredElbow = i.Elbow;
            r.SideDot = float.NaN;

            Vector3 shoulderPos = i.Shoulder;
            Vector3 elbowPos = i.Elbow;
            Vector3 handPos = i.Hand;

            Vector3 acAxis = handPos - shoulderPos;
            float acSqr = Vector3.Dot(acAxis, acAxis);
            if (acSqr <= k_Epsilon * k_Epsilon)
            {
                return;
            }

            Vector3 acDir = acAxis / Mathf.Sqrt(acSqr);
            Vector3 toElbow = elbowPos - shoulderPos;
            Vector3 elbowCenter = shoulderPos + acDir * Vector3.Dot(toElbow, acDir);
            float elbowRadius = (elbowPos - elbowCenter).magnitude;
            r.ElbowCenter = elbowCenter;
            r.ElbowRadius = elbowRadius;
            if (elbowRadius <= k_Epsilon)
            {
                return;
            }

            float upperArmR = Mathf.Max(0f, (i.HandRadius + i.HandSkin) * 1.2f);
            float chestRBase = i.ChestRadiusBase;
            float skin = i.CollisionSkin;
            // These are the LATERAL (wide, side-to-side) half-widths now; the front-back depth is derived from
            // them in SegmentClearance. The values are unchanged, so the SIDES collide exactly as before.
            float chestR = Mathf.Max(0f, chestRBase + skin);
            float spineR = Mathf.Max(0f, chestRBase * 0.8f + skin);
            float hipsR = Mathf.Max(0f, chestRBase * 1.4f + skin);

            // Body frame for the elliptical cross-section: bodyLat (wide) and bodyFwd (thin), both horizontal.
            Vector3 upN = acSqr > 0f && i.PlayerUp.sqrMagnitude > k_Epsilon ? i.PlayerUp.normalized : Vector3.up;
            Vector3 bodyLat = i.BodyRight - upN * Vector3.Dot(i.BodyRight, upN);
            if (bodyLat.sqrMagnitude <= k_Epsilon * k_Epsilon)
            {
                // Old caller (no shoulder-to-shoulder axis): derive lateral from the shoulder's own offset off
                // the chest axis. Slightly forward-tilted vs the true lateral, but keeps every caller working.
                Vector3 chestClosest = BasisFullIKConstraintJob.ClosestPointOnSegment(shoulderPos, i.ChestPos, i.NeckPos);
                Vector3 off = shoulderPos - chestClosest;
                bodyLat = off - upN * Vector3.Dot(off, upN);
            }
            Vector3 bodyFwd = Vector3.zero;
            float bodyLatLen = bodyLat.magnitude;
            if (bodyLatLen > k_Epsilon)
            {
                bodyLat /= bodyLatLen;
                Vector3 fwd = Vector3.Cross(bodyLat, upN);
                float fLen = fwd.magnitude;
                bodyFwd = fLen > k_Epsilon ? fwd / fLen : Vector3.zero;
            }
            else
            {
                bodyLat = Vector3.zero;   // fully degenerate -> SegmentClearance falls back to a round radius
            }

            float natClear = MinTorsoClearance(i, shoulderPos, elbowPos, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd);
            float worstPen = natClear < 0f ? -natClear : 0f;
            r.WorstPenetration = worstPen;
            if (worstPen <= k_Epsilon)
            {
                return;
            }

            // Anatomical "out" direction (shoulder's own side of the body), in the swing plane.
            Vector3 shoulderClosest = BasisFullIKConstraintJob.ClosestPointOnSegment(shoulderPos, i.ChestPos, i.NeckPos);
            Vector3 shoulderOut = shoulderPos - shoulderClosest;
            Vector3 shoulderPerp = shoulderOut - acDir * Vector3.Dot(shoulderOut, acDir);
            float shoulderPerpSqr = shoulderPerp.sqrMagnitude;
            if (shoulderPerpSqr <= k_Epsilon * k_Epsilon)
            {
                return;
            }
            Vector3 outDir = shoulderPerp / Mathf.Sqrt(shoulderPerpSqr);

            Vector3 currentDir = (elbowPos - elbowCenter) / elbowRadius;
            r.SideDot = Vector3.Dot(currentDir, outDir);

            // Sweep the swivel from the natural pole (t=0) to fully-out (t=1) around the shoulder->hand
            // axis. Take the first sample that clears (smallest swing); else the least-penetrating one.
            float thetaOut = Mathf.Atan2(Vector3.Dot(Vector3.Cross(currentDir, outDir), acDir),
                Vector3.Dot(currentDir, outDir)) * Mathf.Rad2Deg;
            float firstClearT = -1f;
            float bestClear = float.NegativeInfinity;
            float bestClearT = 0f;
            for (int k = 0; k <= k_SwivelSteps; k++)
            {
                float t = (float)k / k_SwivelSteps;
                Vector3 d = Quaternion.AngleAxis(thetaOut * t, acDir) * currentDir;
                float c = MinTorsoClearance(i, shoulderPos, elbowCenter + d * elbowRadius, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd);
                if (c >= k_ClearMargin && firstClearT < 0f)
                {
                    firstClearT = t;
                }
                if (c > bestClear)
                {
                    bestClear = c;
                    bestClearT = t;
                }
            }

            bool cleared = firstClearT >= 0f;
            float chosenT = cleared ? firstClearT : bestClearT;
            // Near anti-parallel (|thetaOut|~180) the partial clear-swing direction is ambiguous and
            // snaps sides on smooth motion (the pole flip). Commit toward fully-out (chosenT->1, the
            // stable max-clear pose == outDir exactly) as the natural pole nears anti-parallel to out, so
            // the elbow lands on outDir instead of a fast-moving halfway swing. The ramp starts EARLY
            // (100 deg, was 120) and runs all the way to 180: that shrinks the partial-swing transition
            // band where the elbow swings through large angles, which is what popped on a tight torso-
            // hugging circle (worst trajectory pop 55 -> ~40 deg, under the gate). It trades a little
            // naturalness (the elbow flares out sooner on cross-body reaches) for smoothness.
            float flipCommit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(thetaOut) - 100f) / 80f));
            chosenT += (1f - chosenT) * flipCommit;

            // Bones do not stretch, so the FK chain gives the arm's true length whatever pose it is in.
            float totalLen = (elbowPos - shoulderPos).magnitude + (handPos - elbowPos).magnitude;
            float reach = totalLen > k_Epsilon ? Mathf.Sqrt(acSqr) / totalLen : 1f;
            float authority = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((reach - k_AuthorityFadeStart) / (k_AuthorityFadeEnd - k_AuthorityFadeStart)));
            chosenT *= authority;

            // At zero authority chosenT is exactly 0, so `dir` is exactly `currentDir` and DesiredElbow is
            // exactly the elbow we were handed. SwingElbowAroundAC then sees v1 == v2 and applies the
            // identity. The no-op is structural, not a tolerance -- there is no residual roll to leak.
            Vector3 dir = Quaternion.AngleAxis(thetaOut * chosenT, acDir) * currentDir;

            r.DesiredElbow = elbowCenter + dir * elbowRadius;
            r.SwingAngleDeg = Mathf.Abs(thetaOut * chosenT);
            r.BlendUsed = chosenT;
            r.CollisionState = cleared ? 1 : 2;
            r.ResidualClearance = MinTorsoClearance(i, shoulderPos, r.DesiredElbow, upperArmR, chestR, spineR, hipsR, bodyLat, bodyFwd);
            r.Engaged = true;
        }

        // Signed worst-case clearance (gap > 0, penetration < 0) of the upper-arm capsule against the
        // torso segments. Same segment set + validity gating as the live penetration test, so the
        // engage decision is unchanged; sampled once per swivel candidate above. The torso radii passed
        // here are the LATERAL half-widths; SegmentClearance draws the front-back depth in from them.
        static float MinTorsoClearance(in BasisElbowProtectInput i, Vector3 shoulderPos, Vector3 elbowPos,
            float upperArmR, float chestLatR, float spineLatR, float hipsLatR, Vector3 bodyLat, Vector3 bodyFwd)
        {
            float worst = float.PositiveInfinity;
            if (i.HasHips && i.HasSpine)
            {
                worst = Mathf.Min(worst, SegmentClearance(shoulderPos, elbowPos, upperArmR, i.HipsPos, i.SpinePos, hipsLatR, bodyLat, bodyFwd));
            }
            if (i.HasSpine)
            {
                worst = Mathf.Min(worst, SegmentClearance(shoulderPos, elbowPos, upperArmR, i.SpinePos, i.ChestPos, spineLatR, bodyLat, bodyFwd));
            }
            worst = Mathf.Min(worst, SegmentClearance(shoulderPos, elbowPos, upperArmR, i.ChestPos, i.NeckPos, chestLatR, bodyLat, bodyFwd));
            return worst;
        }

        // Signed clearance of the arm capsule (p1,q1,r1) against ONE torso segment (p2,q2) modeled as an
        // ELLIPTICAL capsule: `latR` is the wide (lateral) half-width, the front-back half-depth is
        // latR * k_ChestDepthRatio, and the ellipse is oriented by (bodyLat, bodyFwd). The effective torso
        // radius toward the closest-approach direction is the ellipse's radius in that direction, so the
        // arm clears the chest FRONT far sooner than its SIDES -- the whole point. Falls back to a round
        // radius (latR) when the frame is unavailable or the separation is along the segment axis.
        static float SegmentClearance(Vector3 p1, Vector3 q1, float r1, Vector3 p2, Vector3 q2, float latR,
            Vector3 bodyLat, Vector3 bodyFwd)
        {
            BasisFullIKConstraintJob.SegmentSegmentClosestPoints(p1, q1, p2, q2, out _, out _, out Vector3 c1, out Vector3 c2);
            Vector3 sep = c1 - c2;
            float sepLen = sep.magnitude;

            float rEff = latR;
            float apR = latR * k_ChestDepthRatio;
            Vector3 axis = q2 - p2;
            float axisSqr = axis.sqrMagnitude;
            if (apR > k_Epsilon && sepLen > k_Epsilon && axisSqr > k_Epsilon
                && bodyLat.sqrMagnitude > k_Epsilon && bodyFwd.sqrMagnitude > k_Epsilon)
            {
                // Separation direction projected into the segment's cross-section (perpendicular to its axis).
                Vector3 axisN = axis / Mathf.Sqrt(axisSqr);
                Vector3 sepPerp = sep - axisN * Vector3.Dot(sep, axisN);
                float sepPerpLen = sepPerp.magnitude;
                if (sepPerpLen > k_Epsilon)
                {
                    Vector3 sepDir = sepPerp / sepPerpLen;
                    float cu = Vector3.Dot(sepDir, bodyLat);   // component toward the wide (lateral) axis
                    float cw = Vector3.Dot(sepDir, bodyFwd);   // component toward the thin (front-back) axis
                    float denom = (cu * cu) / (latR * latR) + (cw * cw) / (apR * apR);
                    if (denom > k_Epsilon)
                    {
                        rEff = 1f / Mathf.Sqrt(denom);   // ellipse radius toward sepDir
                    }
                }
            }
            return sepLen - (r1 + rEff);
        }
    }
}
