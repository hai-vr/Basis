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

        // Swivel blend toward the natural-side angle. Live rig passes SameSideBlend = 0.5 and
        // WrongSideBlend = 1.0; exposed here so the sweep can tune the snap that drives the twitch.
        public float SameSideBlend;
        public float WrongSideBlend;

        // Penetration depth (m) over which the push eases in (smoothstep) from 0 at the surface to
        // full -- a soft engagement gate, so the elbow doesn't pop on/off as contact starts. Beyond
        // this depth the blend is constant (full pin) -> jitter-free. 0 (or less) = legacy binary snap.
        public float DepthScaleRange;

        // Aims the push toward player-down in the swing plane (0 = straight out from the chest, the
        // old chicken-wing direction; 1 = fully down). The elbow stays pinned to a stable in-plane
        // target, so raising this fixes the chicken-wing WITHOUT weakening the push -- weakening it
        // un-pins the elbow and lets hand-tracking noise back in as jitter. Shipped at 0.7.
        public float DownBias;
    }

    public struct BasisElbowProtectResult
    {
        public bool Engaged;          // true when a swing was actually produced
        public int CollisionState;    // 0 none, 1 same-side push, 2 wrong-side flip (the hard snap)
        public Vector3 DesiredElbow;  // elbow position to swing to (== resulting elbow); == input Elbow when not engaged
        public float WorstPenetration;
        public float SideDot;         // dot(currentDir, targetDir); < 0 = wrong side -> WrongSideBlend
        public float BlendUsed;
        public float SwingAngleDeg;   // angle the elbow is swung this solve (the chicken-wing magnitude)
        public float ElbowRadius;
        public Vector3 ElbowCenter;
    }

    // Stream-free torso-collision elbow push shared by BasisFullIKConstraintJob.SolveHand and the
    // offline BasisElbowProtectSweep harness. Change the protect-elbow math HERE so both stay in
    // lock-step. Reuses the job's public capsule helpers so the penetration test never drifts.
    public static class BasisElbowProtectCore
    {
        const float k_Epsilon = 1e-5f;

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

            Vector3 pUp = i.PlayerUp;
            if (pUp.sqrMagnitude < k_Epsilon * k_Epsilon)
            {
                pUp = Vector3.up;
            }

            Vector3 chestPos = i.ChestPos;
            Vector3 neckPos = i.NeckPos;
            float worstPen = 0f;
            if (i.HasHips && i.HasSpine)
            {
                BasisFullIKConstraintJob.AccumulateWorstTorsoSegment(shoulderPos, elbowPos, upperArmR,
                    i.HipsPos, i.SpinePos, hipsR, pUp, ref worstPen);
            }
            if (i.HasSpine)
            {
                BasisFullIKConstraintJob.AccumulateWorstTorsoSegment(shoulderPos, elbowPos, upperArmR,
                    i.SpinePos, chestPos, spineR, pUp, ref worstPen);
            }
            BasisFullIKConstraintJob.AccumulateWorstTorsoSegment(shoulderPos, elbowPos, upperArmR,
                chestPos, neckPos, chestR, pUp, ref worstPen);
            r.WorstPenetration = worstPen;

            if (worstPen <= k_Epsilon)
            {
                return;
            }

            Vector3 shoulderClosest = BasisFullIKConstraintJob.ClosestPointOnSegment(shoulderPos, chestPos, neckPos);
            Vector3 shoulderOutDir = shoulderPos - shoulderClosest;
            Vector3 shoulderPerp = shoulderOutDir - acDir * Vector3.Dot(shoulderOutDir, acDir);
            float shoulderPerpSqr = shoulderPerp.sqrMagnitude;
            if (shoulderPerpSqr <= k_Epsilon * k_Epsilon)
            {
                return;
            }

            Vector3 targetDir = shoulderPerp / Mathf.Sqrt(shoulderPerpSqr);

            // Aim the pushed elbow toward "down" in the swing plane instead of straight out from the
            // chest. The out-radial target is what read as a chicken-wing; biasing it toward
            // player-down lands the elbow in a natural hang. Both vectors are perpendicular to acDir,
            // so this stays in-plane and stable -- and because the elbow is still fully pinned to it,
            // there's no extra hand-noise sensitivity. This (not a weaker push) is the chicken-wing fix.
            if (i.DownBias > 0f)
            {
                Vector3 downInPlane = Vector3.ProjectOnPlane(-pUp, acDir);
                if (downInPlane.sqrMagnitude > k_Epsilon * k_Epsilon)
                {
                    targetDir = Vector3.Lerp(targetDir, downInPlane.normalized, i.DownBias).normalized;
                }
            }

            Vector3 currentDir = (elbowPos - elbowCenter) / elbowRadius;
            float sideDot = Vector3.Dot(currentDir, targetDir);

            // DepthScaleRange > 0 turns the binary snap into a smoothstep engagement gate (eased on/off
            // -> no boundary twitch); past the gate depth the blend is a constant full pin, so the
            // elbow holds a stable target instead of chasing the noisy raw solve (jitter-free). With
            // the shipped Same=Wrong=1 the side term drops out to a clean pin. <=0 = legacy binary snap.
            float blend;
            if (i.DepthScaleRange > 0f)
            {
                float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(worstPen / i.DepthScaleRange));
                float baseBlend = i.SameSideBlend + (i.WrongSideBlend - i.SameSideBlend) * Mathf.Clamp01(0.5f * (1f - sideDot));
                blend = baseBlend * f;
            }
            else
            {
                blend = sideDot < 0f ? i.WrongSideBlend : i.SameSideBlend;
            }
            r.SideDot = sideDot;
            r.BlendUsed = blend;
            r.CollisionState = sideDot < 0f ? 2 : 1;

            Vector3 blendedDir = Vector3.Slerp(currentDir, targetDir, blend);
            r.DesiredElbow = elbowCenter + blendedDir * elbowRadius;
            r.SwingAngleDeg = Vector3.Angle(currentDir, blendedDir);
            r.Engaged = true;
        }
    }
}
