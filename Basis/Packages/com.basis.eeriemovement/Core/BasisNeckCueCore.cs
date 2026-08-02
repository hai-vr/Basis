using UnityEngine;
namespace Basis.IK
{
    // ================================================================================================
    // WHERE THE NECK IS, GIVEN WHERE THE HEAD IS.
    //
    // The whole torso is estimated off one point: re-attach the T-pose head->neck lever to the head
    // target and you have the top of the trunk. Both the FBIK pre-bend (ComputeNeckCue) and the virtual
    // spine's neck bone (the Head->Neck rotational lock) are literally this one line:
    //
    //     neck = headTargetPos + headWorldRot * tposeHeadToNeckLocal
    //
    // THAT LINE ASSUMES A NOD PIVOTS AT THE NECK BONE. Swinging the lever by the HEAD's rotation is
    // only correct if the neck rotated by the same amount -- i.e. if the whole cervical column turned
    // rigidly and the skull rode around on the end of it. When that holds, the estimate is exact and
    // provably gaze-invariant: the head's orbit about the neck and the lever's re-attachment cancel
    // algebraically (the derivation is written out in DistributeSpineBend).
    //
    // ⭐ IT DOES NOT HOLD ON A LOOK-UP, AND THIS FILE IS ALREADY WHERE THAT WAS WRITTEN DOWN. Cervical
    // extension has far less range than flexion, and a look-up is taken largely by the thoracic spine
    // arching rather than by the skull sliding backwards over the shoulders -- so the head barely
    // travels, and BasisHeadPitchSwingCore scales the geometric prediction of that travel down to
    // `DesktopHeadSwingBackward = 0.35` on exactly this side of the sweep. The neck estimate never got
    // the same treatment: it kept swinging the lever by the FULL gaze.
    //
    // The error is (1 - carry) * (R - I) * lever, and it points FORWARD. On a 10 cm lever at a 60 deg
    // look-up that is ~5.6 cm of neck that walked out in front of the body without the player moving,
    // and ~3.3 cm of neck that floated UP. Everything that asks "where is the torso" then reads it as a
    // real forward lean: the spine pre-bend folds the chest, the trunk counterbalance answers the
    // phantom fold by sliding the pelvis back, and -- the big one -- the virtual spine strings the chest
    // and spine bones along the neck->hips chord, so the chest target itself slides forward with it and
    // the chest IK target drags the real chest bone out after it.
    //
    // THE FIX IS THE SAME SHAPE AS THE ONE BasisHeadPitchSwingCore ALREADY MAKES: swing the lever by
    // only the fraction of the extension the neck actually took. Rotating the swung lever back down
    // about the gaze's own horizontal right axis by (1 - carry) * extension is exactly
    // `R_yaw * R_pitch^carry * lever`, so:
    //   * pure yaw is untouched (the correction angle is zero),
    //   * a level gaze is untouched,
    //   * a LOOK-DOWN is untouched, bit for bit -- flexion is the side where the rigid model holds and
    //     the side every existing look-down fix was tuned against,
    //   * damp = 0 is bit-identical to the old code, so the feature has a true off switch,
    //   * the lever KEEPS ITS LENGTH (it is a rotation, not a lerp), which matters because that length
    //     is a link in the spine chain -- shortening it would compress the CCD toward its singularity.
    // ================================================================================================
    public static class BasisNeckCueCore
    {
        const float k_SqrEpsilon = 1e-8f;
        const float k_Epsilon = 1e-5f;

        /// <summary>
        /// How much of a look-UP's lever swing to REMOVE. The neck carries only the remainder; the rest of
        /// the gaze is the skull rotating on the atlas, which does not move the neck at all. 0.65 leaves a
        /// 0.35 carry, matching BasisHeadPitchSwingCore's backward scale -- the same physiology measured from
        /// the other end (how far the eye travels).
        ///
        /// ⚠️ THE PARAMETER IS THE DAMPING AND NOT THE CARRY SO THAT ZERO IS THE OLD BEHAVIOUR. Every job
        /// struct and test fixture that never sets this field gets 0 and is therefore bit-identical to the
        /// rigid re-attachment. A "carry" spelling would have made an unset field mean FULL damping, which is
        /// the silent-behaviour-change trap that has already cost this file a whole corpus re-baseline.
        /// </summary>
        public const float DefaultExtensionDamp = 0.65f;

        /// <summary>
        /// Re-attaches the T-pose head->neck lever to the head target, damping the swing on a look-up.
        /// `playerUp` is the body's up; `extensionDamp` is clamped to [0,1] and 0 is a true no-op.
        /// </summary>
        public static Vector3 Solve(Vector3 headTargetPos, Quaternion headWorldRot, Vector3 tposeHeadToNeckLocal,
            Vector3 playerUp, float extensionDamp)
        {
            Vector3 lever = headWorldRot * tposeHeadToNeckLocal;

            float damp = Mathf.Clamp01(extensionDamp);
            if (damp <= 0f || lever.sqrMagnitude < k_SqrEpsilon)
            {
                return headTargetPos + lever;
            }

            Vector3 up = playerUp.sqrMagnitude < k_SqrEpsilon ? Vector3.up : playerUp.normalized;
            Vector3 gaze = headWorldRot * Vector3.forward;
            float upComp = Vector3.Dot(gaze, up);
            if (upComp <= 0f)
            {
                // Level, or looking DOWN. Flexion is the side the rigid model gets right, so this path is
                // the shipped one, unchanged, to the bit.
                return headTargetPos + lever;
            }

            Vector3 horiz = gaze - up * upComp;
            float horizMag = horiz.magnitude;
            float extensionDeg = Mathf.Atan2(upComp, horizMag) * Mathf.Rad2Deg;

            Vector3 forwardAzimuth;
            if (horizMag > k_Epsilon)
            {
                forwardAzimuth = horiz / horizMag;
            }
            else
            {
                // Gaze is straight up the body axis, so head-forward carries no azimuth. Head-up is
                // orthogonal to it by construction and points BACKWARD there, so negate it -- and stay in
                // the body frame rather than reaching for a world axis.
                Vector3 alt = headWorldRot * Vector3.up;
                Vector3 altH = alt - up * Vector3.Dot(alt, up);
                if (altH.sqrMagnitude < k_SqrEpsilon)
                {
                    return headTargetPos + lever;   // fully degenerate: leave the lever alone
                }
                forwardAzimuth = -altH.normalized;
            }

            Vector3 axis = Vector3.Cross(up, forwardAzimuth);
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                return headTargetPos + lever;
            }
            axis.Normalize();

            // +angle about (up x forward) pitches the lever back DOWN, so this lands it at (1 - damp) * the
            // extension it was swung by. AxisAngle, not Quaternion.AngleAxis: the latter is a native
            // ECall, which Burst has to intrinsify and the standalone harnesses cannot run at all.
            Quaternion undo = BasisSpineAnatomyCore.AxisAngle(damp * extensionDeg, axis);
            return headTargetPos + undo * lever;
        }
    }
}
