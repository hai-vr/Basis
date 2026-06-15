namespace UnityEngine.Animations.Rigging
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
            float chestR = Mathf.Max(0f, chestRBase + skin);
            float spineR = Mathf.Max(0f, chestRBase * 0.8f + skin);
            float hipsR = Mathf.Max(0f, chestRBase * 1.4f + skin);

            float natClear = MinTorsoClearance(i, shoulderPos, elbowPos, upperArmR, chestR, spineR, hipsR);
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
                float c = MinTorsoClearance(i, shoulderPos, elbowCenter + d * elbowRadius, upperArmR, chestR, spineR, hipsR);
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
            Vector3 dir = Quaternion.AngleAxis(thetaOut * chosenT, acDir) * currentDir;

            r.DesiredElbow = elbowCenter + dir * elbowRadius;
            r.SwingAngleDeg = Mathf.Abs(thetaOut * chosenT);
            r.BlendUsed = chosenT;
            r.CollisionState = cleared ? 1 : 2;
            r.ResidualClearance = MinTorsoClearance(i, shoulderPos, r.DesiredElbow, upperArmR, chestR, spineR, hipsR);
            r.Engaged = true;
        }

        // Signed worst-case clearance (gap > 0, penetration < 0) of the upper-arm capsule against the
        // torso segments. Same segment set + validity gating as the live penetration test, so the
        // engage decision is unchanged; sampled once per swivel candidate above.
        static float MinTorsoClearance(in BasisElbowProtectInput i, Vector3 shoulderPos, Vector3 elbowPos,
            float upperArmR, float chestR, float spineR, float hipsR)
        {
            float worst = float.PositiveInfinity;
            if (i.HasHips && i.HasSpine)
            {
                worst = Mathf.Min(worst, SegmentClearance(shoulderPos, elbowPos, upperArmR, i.HipsPos, i.SpinePos, hipsR));
            }
            if (i.HasSpine)
            {
                worst = Mathf.Min(worst, SegmentClearance(shoulderPos, elbowPos, upperArmR, i.SpinePos, i.ChestPos, spineR));
            }
            worst = Mathf.Min(worst, SegmentClearance(shoulderPos, elbowPos, upperArmR, i.ChestPos, i.NeckPos, chestR));
            return worst;
        }

        static float SegmentClearance(Vector3 p1, Vector3 q1, float r1, Vector3 p2, Vector3 q2, float r2)
        {
            BasisFullIKConstraintJob.SegmentSegmentClosestPoints(p1, q1, p2, q2, out _, out _, out Vector3 c1, out Vector3 c2);
            return (c1 - c2).magnitude - (r1 + r2);
        }
    }
}
