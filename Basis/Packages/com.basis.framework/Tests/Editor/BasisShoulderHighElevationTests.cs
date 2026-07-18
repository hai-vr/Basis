using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// The shoulder girdle must keep contributing as the arm goes overhead.
    ///
    /// THE DEFECT (user: "star fish, arms up and out -- the upper arm gets pinched on both sides"). Scapular
    /// contribution is PROGRESSIVE -- the first ~30 deg of humeral elevation is almost purely glenohumeral and
    /// the scapula takes over late -- so a single CoupleRatio cannot express it. The shipped 0.4 was chosen to
    /// stop the elbow reading floaty in the MID range, and left the top starved: measured girdle 5.2 deg at 90,
    /// 7.9 at 135, 10.6 at 180, against an anatomical ~30/45/60 (2:1 rhythm). Whatever the scapula does not
    /// supply the glenohumeral joint must, so the humerus ends up tens of degrees further rotated against a
    /// shoulder that barely moved, which collapses the deltoid. Symmetric, hence "both sides".
    ///
    /// THE FIX is elevation-gated on purpose: below ~90 deg it is the exact identity, so the mid-range tuning
    /// (and the floaty-elbow regression it fixed) is untouched.
    /// </summary>
    public class BasisShoulderHighElevationTests
    {
        const float Upper = 0.28f, Fore = 0.26f;
        static readonly Vector3 Shoulder = new Vector3(-0.17f, 1.40f, 0f);

        static float GirdleDeg(float elevDeg)
        {
            float armLen = Upper + Fore;
            Vector3 dir = new Vector3(-Mathf.Sin(elevDeg * Mathf.Deg2Rad), -Mathf.Cos(elevDeg * Mathf.Deg2Rad), 0f);

            BasisShoulderSolveInput i = default;
            i.ShoulderPos = Shoulder;
            i.HandTargetPos = Shoulder + dir * (armLen * 0.97f);
            i.ElbowPos = Shoulder + dir * Upper;
            i.HasElbow = false;
            i.HasShoulderTracker = false;
            i.ChestRot = Quaternion.identity;
            i.TposeChestRot = Quaternion.identity;
            i.TposeShoulderRot = Quaternion.identity;
            i.TposeArmDirWorld = Vector3.down;
            i.TposeArmLength = armLen;
            i.TposeClavicleLength = 0.05f;
            i.TposeElbowLength = Upper;
            i.ShrugEnabled = false;
            i.ElevationFactor = 0.4f;
            i.ProtractionFactor = 0.3f;
            i.CoupleRatio = 0.4f;
            i.MaxShoulderDeg = 25f;
            i.TrackerFinal = Quaternion.identity;
            i.IsLeft = true;

            BasisShoulderSolveCore.Solve(i, out BasisShoulderSolveResult r);
            r.ShoulderRotation.ToAngleAxis(out float deg, out Vector3 _);
            return deg > 180f ? 360f - deg : deg;
        }

        /// <summary>Below the gate the boost must be the EXACT identity: these are the values the mid-range
        /// tuning produced before it existed, and the floaty-elbow fix depends on them.</summary>
        [Test]
        public void BelowNinetyDegrees_IsUnchanged(
            [Values(0f, 15f, 30f, 45f, 60f, 75f, 90f)] float elevDeg)
        {
            float[] baseline = { 0.00f, 0.00f, 0.28f, 1.03f, 2.27f, 3.82f, 5.24f };
            int idx = (int)(elevDeg / 15f);

            Assert.That(GirdleDeg(elevDeg), Is.EqualTo(baseline[idx]).Within(0.02f),
                $"the high-elevation boost must not touch {elevDeg:F0} deg of elevation");
        }

        [Test]
        public void Overhead_TheGirdleActuallyContributes()
        {
            float at135 = GirdleDeg(135f);
            float at180 = GirdleDeg(180f);

            Assert.That(at135, Is.GreaterThan(15f),
                $"starfish girdle {at135:F1} deg is still starved (was 7.9 before the boost)");
            Assert.That(at180, Is.GreaterThan(15f),
                $"overhead girdle {at180:F1} deg is still starved (was 10.6 before the boost)");
        }

        [Test]
        public void TheGirdleIsMonotoneInElevation()
        {
            float prev = -1f;
            for (float elev = 0f; elev <= 180.01f; elev += 7.5f)
            {
                float g = GirdleDeg(elev);
                Assert.That(g, Is.GreaterThanOrEqualTo(prev - 0.01f),
                    $"girdle went backwards at {elev:F0} deg ({g:F2} after {prev:F2}) -- a fold here is a visible pop");
                prev = g;
            }
        }

        [Test]
        public void TheClampIsStillRespected()
        {
            for (float elev = 0f; elev <= 180.01f; elev += 7.5f)
            {
                Assert.That(GirdleDeg(elev), Is.LessThanOrEqualTo(25f + 0.01f),
                    $"MaxShoulderDeg must still bound the girdle at {elev:F0} deg");
            }
        }
    }
}
