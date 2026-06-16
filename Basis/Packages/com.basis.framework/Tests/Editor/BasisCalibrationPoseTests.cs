using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Regression tests for pose-tolerant calibration (<see cref="BasisCalibrationPoseCore"/>): the math
    /// that lets the player calibrate without a perfect straight T-pose by reconstructing the limb from a
    /// mid-joint (elbow/knee) tracker. Pins the properties the offline BasisCalibrationPoseSweep gates:
    /// bend-invariant length, a pose-matched offset reference that places the avatar bone exactly while
    /// the T-pose reference drifts with bend, and a quality score that drops when no mid tracker exists.
    /// </summary>
    public class BasisCalibrationPoseTests
    {
        const float AUp = 0.28f, ALo = 0.26f, PUp = 0.30f, PLo = 0.27f;
        static readonly Vector3 ARoot = new Vector3(0.16f, 1.40f, 0f);
        static readonly Vector3 PRoot = new Vector3(0.18f, 1.43f, 0.02f);
        static readonly Vector3 TposeDir = Vector3.right;

        [Test]
        public void BendInvariantLength_EqualsSegmentSum_AtEveryBend()
        {
            for (float bend = 180f; bend >= 30f; bend -= 10f)
            {
                BasisCalibrationPoseCore.Solve(Make(bend, true), out var r);
                Assert.That(r.PlayerLimbLength, Is.EqualTo(PUp + PLo).Within(1e-4f),
                    $"bend {bend}: bend-invariant length {r.PlayerLimbLength:0.000} != segment sum {PUp + PLo:0.000}.");
            }
        }

        [Test]
        public void StraightLineDistance_ShrinksWithBend_TheBugItFixes()
        {
            BasisCalibrationPoseCore.Solve(Make(180f, true), out var straight);
            BasisCalibrationPoseCore.Solve(Make(60f, true), out var bent);
            Assert.That(straight.StraightLineDist, Is.EqualTo(PUp + PLo).Within(1e-3f),
                "a straight arm's wrist-to-shoulder distance should equal the true length.");
            Assert.That(bent.StraightLineDist, Is.LessThan(straight.StraightLineDist - 0.05f),
                $"a bent arm's straight-line distance ({bent.StraightLineDist:0.000}) must be visibly shorter -- this is what corrupts the old span-based scale.");
            Assert.That(bent.StraightDeficit, Is.GreaterThan(0.05f));
        }

        [Test]
        public void BendAngle_RecoversTheCalibrationBend()
        {
            foreach (float bend in new[] { 45f, 90f, 135f, 175f })
            {
                BasisCalibrationPoseCore.Solve(Make(bend, true), out var r);
                Assert.That(r.BendAngleDeg, Is.EqualTo(bend).Within(1.5f),
                    $"recovered bend {r.BendAngleDeg:0.0} != input {bend}.");
            }
        }

        [Test]
        public void AvatarEndMatched_EqualsTpose_WhenStraight()
        {
            var i = Make(180f, true);
            BasisCalibrationPoseCore.Solve(i, out var r);
            Assert.That((r.AvatarEndMatched - i.AvatarEndTpose).magnitude, Is.LessThan(1e-3f),
                "a straight calibration must leave the avatar end at its T-pose position.");
        }

        [Test]
        public void AvatarEndMatched_MirrorsPlayer_AtAvatarLengths_WhenBent()
        {
            var i = Make(70f, true);
            BasisCalibrationPoseCore.Solve(i, out var r);
            // The matched mid bone sits one avatar-upper-length along the player's upper-arm direction.
            Vector3 playerUpperDir = (i.PlayerMid - i.PlayerRoot).normalized;
            Vector3 expectMid = i.AvatarRoot + playerUpperDir * AUp;
            Assert.That((r.AvatarMidMatched - expectMid).magnitude, Is.LessThan(1e-4f));
            Assert.That((r.AvatarMidMatched - i.AvatarRoot).magnitude, Is.EqualTo(AUp).Within(1e-4f),
                "matched upper segment must keep the AVATAR bone length, not the player's.");
            // And the end is bent away from the straight T-pose end.
            Assert.That((r.AvatarEndMatched - i.AvatarEndTpose).magnitude, Is.GreaterThan(0.1f),
                "a bent calibration must move the matched avatar end off the straight T-pose end.");
        }

        [Test]
        public void CompensatedOffset_PlacesBoneExactly_AtCalibrationPose()
        {
            for (float bend = 160f; bend >= 40f; bend -= 20f)
            {
                var i = Make(bend, true);
                BasisCalibrationPoseCore.Solve(i, out var r);
                Vector3 trackerPos = i.PlayerEnd;
                Quaternion trackerRot = SafeLook(i.PlayerEnd - i.PlayerMid);
                BasisCalibrationPoseCore.CaptureEndOffset(true, trackerPos, trackerRot, r, i, out var op, out var orot);
                BasisCalibrationMath.ApplyInverseOffset(trackerPos, trackerRot, op, orot, out var landed, out _);
                Assert.That((landed - r.AvatarEndMatched).magnitude, Is.LessThan(1e-4f),
                    $"bend {bend}: pose-matched offset must land the avatar end on the mirrored pose.");
            }
        }

        [Test]
        public void UncompensatedOffset_DriftsWithBend()
        {
            var straight = Make(180f, true);
            BasisCalibrationPoseCore.Solve(straight, out var rs);
            float errStraight = UncompErrAtCalib(straight, rs);
            Assert.That(errStraight, Is.LessThan(1e-3f), "a straight calibration has no T-pose drift.");

            var bent = Make(60f, true);
            BasisCalibrationPoseCore.Solve(bent, out var rb);
            float errBent = UncompErrAtCalib(bent, rb);
            Assert.That(errBent, Is.GreaterThan(0.1f),
                $"the T-pose reference must misplace the avatar end on a bent calibration ({errBent * 1000f:0}mm); that is the bug.");
        }

        [Test]
        public void Quality_MidTrackerBeatsNoMid_WhenBent()
        {
            BasisCalibrationPoseCore.Solve(Make(60f, true), out var mid);
            BasisCalibrationPoseCore.Solve(Make(60f, false), out var noMid);
            Assert.That(mid.QualityScore, Is.GreaterThan(noMid.QualityScore + 0.2f),
                $"a mid tracker must score a bent calibration higher (mid {mid.QualityScore:0.00} vs no-mid {noMid.QualityScore:0.00}).");
            Assert.That(noMid.QualityScore, Is.LessThan(0.6f), "a bent, un-compensatable calibration must score low.");
        }

        [Test]
        public void Quality_HighForBoth_WhenStraight()
        {
            BasisCalibrationPoseCore.Solve(Make(179f, true), out var mid);
            BasisCalibrationPoseCore.Solve(Make(179f, false), out var noMid);
            Assert.That(mid.QualityScore, Is.GreaterThan(0.95f));
            Assert.That(noMid.QualityScore, Is.GreaterThan(0.9f),
                "a straight calibration is fine even without a mid tracker.");
        }

        [Test]
        public void NoMid_FallsBackToTposeReference()
        {
            var i = Make(60f, false);
            BasisCalibrationPoseCore.Solve(i, out var r);
            Assert.That(r.Apply, Is.False, "no mid tracker -> no compensation produced.");
            Assert.That((r.AvatarEndMatched - i.AvatarEndTpose).magnitude, Is.LessThan(1e-4f),
                "without a mid tracker the reference must fall back to the T-pose bone.");
        }

        [Test]
        public void Degenerate_NoNaN()
        {
            var i = Make(90f, true);
            i.PlayerMid = i.PlayerRoot; // zero upper segment
            BasisCalibrationPoseCore.Solve(i, out var r);
            Assert.That(r.Apply, Is.False);
            Assert.That(float.IsNaN(r.AvatarEndMatched.x), Is.False);
            Assert.That(float.IsNaN(r.QualityScore), Is.False);
        }

        // ----------------------------------------------------------------- helpers

        static float UncompErrAtCalib(in BasisLimbCalibrationInput i, in BasisLimbCalibrationResult r)
        {
            Vector3 trackerPos = i.PlayerEnd;
            Quaternion trackerRot = SafeLook(i.PlayerEnd - i.PlayerMid);
            BasisCalibrationPoseCore.CaptureEndOffset(false, trackerPos, trackerRot, r, i, out var op, out var orot);
            BasisCalibrationMath.ApplyInverseOffset(trackerPos, trackerRot, op, orot, out var landed, out _);
            return (landed - r.AvatarEndMatched).magnitude;
        }

        static BasisLimbCalibrationInput Make(float bendDeg, bool hasMid)
        {
            Vector3 upperDir = TposeDir;
            Vector3 bendAxis = Vector3.Cross(TposeDir, Vector3.forward).normalized;
            Vector3 lowerDir = Quaternion.AngleAxis(180f - bendDeg, bendAxis) * upperDir;
            Vector3 pMid = PRoot + upperDir * PUp;
            Vector3 pEnd = pMid + lowerDir * PLo;
            Vector3 aMidT = ARoot + TposeDir * AUp;
            Vector3 aEndT = aMidT + TposeDir * ALo;

            BasisLimbCalibrationInput i;
            i.PlayerRoot = PRoot; i.PlayerMid = pMid; i.PlayerEnd = pEnd; i.HasMid = hasMid;
            i.AvatarRoot = ARoot; i.AvatarMidTpose = aMidT; i.AvatarEndTpose = aEndT;
            i.AvatarMidRotTpose = SafeLook(TposeDir); i.AvatarEndRotTpose = SafeLook(TposeDir);
            i.AvatarUpperLen = AUp; i.AvatarLowerLen = ALo;
            return i;
        }

        static Quaternion SafeLook(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-8f) return Quaternion.identity;
            Vector3 up = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(dir.normalized, up);
        }
    }
}
