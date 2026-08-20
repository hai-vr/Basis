using NUnit.Framework;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Even arm-twist distribution. A single twist bone (forearm / upper-arm) must absorb a share of the bone's
    /// roll equal to its POSITION along the bone, so the roll spreads as a linear gradient instead of piling up
    /// at one joint -- the "candy-wrapper" the user sees as the arm "fully twisting at points". Exercises
    /// <see cref="BasisTwistSolveCore.SegmentPositionFraction"/> + <see cref="BasisTwistSolveCore.Solve"/>, the
    /// same path <c>BasisFullIKConstraintJob.SolveArmTwist</c> runs live (it sets Fraction = positionFraction *
    /// distributionStrength).
    /// </summary>
    public class BasisArmTwistDistributionTests
    {
        const float BoneLen = 0.26f;
        static readonly Vector3 Axis = Vector3.forward;

        [Test]
        public void SegmentPositionFraction_MatchesBonePlacement()
        {
            for (float p = 0.05f; p <= 0.95f; p += 0.05f)
            {
                float got = BasisTwistSolveCore.SegmentPositionFraction(Vector3.zero, Axis * BoneLen, Axis * (p * BoneLen));
                Assert.That(got, Is.EqualTo(p).Within(1e-4f), $"position fraction wrong for a bone at {p:0.00} along the segment.");
            }
            // A perpendicular stand-off projects onto the segment; positions outside the segment clamp to [0,1].
            Assert.That(BasisTwistSolveCore.SegmentPositionFraction(Vector3.zero, Axis * BoneLen, Axis * (0.5f * BoneLen) + Vector3.up * 0.1f),
                Is.EqualTo(0.5f).Within(1e-3f), "perpendicular offset should not change the along-segment fraction.");
            Assert.That(BasisTwistSolveCore.SegmentPositionFraction(Vector3.zero, Axis * BoneLen, -Axis * 0.5f), Is.EqualTo(0f), "before the parent clamps to 0.");
            Assert.That(BasisTwistSolveCore.SegmentPositionFraction(Vector3.zero, Axis * BoneLen, Axis * (2f * BoneLen)), Is.EqualTo(1f), "past the child clamps to 1.");
        }

        [Test]
        public void TwistDistributesEvenly_AcrossBonePositions()
        {
            // Position-proportional share (distribution strength 1) puts the twist bone exactly on the linear
            // gradient, so the parent->bone and bone->child spans twist at the SAME rate (concentration ~1, no
            // pile-up) for any bone placement -- the fix for "roll piles up at one joint".
            float worstConc = 1f, worstEvenErr = 0f, worstAt = 0f;
            for (float p = 0.1f; p <= 0.9f; p += 0.1f)
            {
                float eff = BasisTwistSolveCore.SegmentPositionFraction(Vector3.zero, Axis * BoneLen, Axis * (p * BoneLen)); // strength 1
                foreach (float roll in new[] { 30f, 75f, 120f, 160f })
                {
                    float bone = SolveBoneRoll(eff, roll);
                    float effMeas = bone / roll;
                    float rate1 = effMeas / p;            // twist rate over parent->bone vs the even ideal (1)
                    float rate2 = (1f - effMeas) / (1f - p); // twist rate over bone->child vs the even ideal (1)
                    float conc = Mathf.Max(rate1, rate2);
                    if (conc > worstConc) { worstConc = conc; worstAt = p; }
                    worstEvenErr = Mathf.Max(worstEvenErr, Mathf.Abs(bone - p * roll));
                }
            }
            Assert.That(worstConc, Is.LessThan(1.25f),
                $"twist piles up (concentration {worstConc:0.00}x the even rate at p={worstAt:0.0}) -- distribution is not position-proportional.");
            Assert.That(worstEvenErr, Is.LessThan(2f),
                $"twist bone sits {worstEvenErr:0.0} deg off the linear (even) gradient.");
        }

        [Test]
        public void FixedFraction_PilesUpAtWristEndBone_WhichIsWhyEvenIsNeeded()
        {
            // Documents the failure the position-proportional share fixes: a FIXED 0.5 fraction on a bone near
            // the wrist (p = 0.9) leaves the last tenth of the bone carrying ~5x the twist rate -- the candy-wrap.
            // If this ever stops over-twisting, the even-distribution guard above is no longer meaningful.
            const float p = 0.9f;
            float bone = SolveBoneRoll(0.5f, 120f);          // old behavior: fixed fraction regardless of position
            float effMeas = bone / 120f;
            float wristSpanRate = (1f - effMeas) / (1f - p); // bone->child twist rate vs the even ideal (1)
            Assert.That(wristSpanRate, Is.GreaterThan(2f),
                $"expected a fixed-fraction wrist-end bone to over-twist its short span (got {wristSpanRate:0.0}x the even rate).");
        }

        // A rig that authors BOTH the helper and the driving child off-axis from the arm bone -- hands posed
        // palm-down under the forearm, helpers exported carrying their own roll. Neither is live twist.
        static readonly Quaternion TwistBind = Quaternion.AngleAxis(35f, Axis);
        static readonly Quaternion ChildBind = Quaternion.AngleAxis(-50f, Axis);

        [Test]
        public void BindPose_LeavesTwistBoneOnItsAuthoredRotation()
        {
            // Nothing has moved off the T-pose, so the helper must land exactly where the rig authored it.
            // Reading raw locals instead put it at parent * roll-share, throwing the authored rotation away
            // -- a constant wrong twist on every frame, worst on the upper arm where it reads as the whole
            // limb being rolled.
            Quaternion parent = Quaternion.Euler(15f, -40f, 70f);
            var input = new BasisTwistSolveInput
            {
                ParentRotation = parent,
                ChildRotation = parent * ChildBind,       // child sitting on its bind
                ParentToChild = parent * (Axis * BoneLen),
                Fraction = 0.5f,
                ChildBindLocal = ChildBind,
                TwistBindLocal = TwistBind,
            };

            BasisTwistSolveCore.Solve(input, out BasisTwistSolveResult r);

            Assert.That(r.Apply, Is.True, "bind pose should still resolve a twist write.");
            Assert.That(r.TwistAngleDeg, Is.EqualTo(0f).Within(0.01f),
                $"authored child roll read as live twist ({r.TwistAngleDeg:0.00} deg with nothing moving).");
            Assert.That(Quaternion.Angle(r.TwistWorldRotation, parent * TwistBind), Is.LessThan(0.01f),
                "twist bone left its authored rotation in a clean T-pose.");
        }

        [Test]
        public void LiveRoll_IsMeasuredFromBind_AndComposesOntoIt()
        {
            // Same off-axis rig, now genuinely rolled: the recovered angle is the departure from bind, and
            // the helper takes its share of that measured from its own authored rotation.
            const float Roll = 60f;
            const float Fraction = 0.5f;
            Quaternion parent = Quaternion.Euler(-25f, 12f, 5f);
            var input = new BasisTwistSolveInput
            {
                ParentRotation = parent,
                ChildRotation = parent * Quaternion.AngleAxis(Roll, Axis) * ChildBind,
                ParentToChild = parent * (Axis * BoneLen),
                Fraction = Fraction,
                ChildBindLocal = ChildBind,
                TwistBindLocal = TwistBind,
            };

            BasisTwistSolveCore.Solve(input, out BasisTwistSolveResult r);

            Assert.That(r.Apply, Is.True);
            Assert.That(r.TwistAngleDeg, Is.EqualTo(Roll).Within(0.01f),
                "recovered twist should be the roll past bind, with the authored roll cancelled out.");
            Assert.That(Quaternion.Angle(r.TwistWorldRotation, parent * TwistBind), Is.EqualTo(Roll * Fraction).Within(0.01f),
                "helper's share should be measured from its authored rotation, not from the parent.");
        }

        [Test]
        public void UnbakedBinds_FallBackToIdentity()
        {
            // default(BasisTwistSolveInput) leaves the bind fields as the zero quaternion, and every call
            // site that predates bind cancellation (sweeps, equivariance) still builds inputs that way.
            // Those must keep the raw behaviour rather than normalising a zero into NaN.
            const float Roll = 40f;
            BasisTwistSolveInput unbaked = default;
            unbaked.ParentRotation = Quaternion.identity;
            unbaked.ChildRotation = Quaternion.AngleAxis(Roll, Axis);
            unbaked.ParentToChild = Axis * BoneLen;
            unbaked.Fraction = 1f;

            BasisTwistSolveCore.Solve(unbaked, out BasisTwistSolveResult r);

            Assert.That(r.Apply, Is.True);
            Assert.That(r.TwistAngleDeg, Is.EqualTo(Roll).Within(0.01f), "zero bind quaternions should read as identity.");
            Assert.That(IsFinite(r.TwistWorldRotation), Is.True, "zero bind quaternion normalised into a non-finite rotation.");
        }

        static bool IsFinite(Quaternion q) =>
            !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
              float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));

        // One twist solve: child rolled by 'roll' about the bone axis; returns the twist bone's roll magnitude.
        static float SolveBoneRoll(float fraction, float roll)
        {
            var input = new BasisTwistSolveInput
            {
                ParentRotation = Quaternion.identity,
                ChildRotation = Quaternion.AngleAxis(roll, Axis),
                ParentToChild = Axis * BoneLen,
                Fraction = fraction,
            };
            BasisTwistSolveCore.Solve(input, out BasisTwistSolveResult r);
            return r.Apply ? Quaternion.Angle(Quaternion.identity, r.TwistWorldRotation) : 0f;
        }
    }
}
