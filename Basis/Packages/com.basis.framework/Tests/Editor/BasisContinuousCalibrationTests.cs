using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Regression tests for the continuous calibration refresh math
    /// (<see cref="BasisContinuousCalibrationCore"/>): the yaw-flattened head body frame that
    /// normalizes tracker poses across playspace motion, the standing gates, and the capped
    /// exponential blend that walks a calibration snapshot toward the live tracker pose. Pins the
    /// safety properties the runtime relies on: body-local poses are invariant under playspace
    /// yaw + horizontal translation, a correction can never carry a snapshot past the cap from its
    /// calibrated home, and the drift thresholds keep their gate > flag > cap ordering.
    /// </summary>
    public class BasisContinuousCalibrationTests
    {
        const float Cap = BasisContinuousCalibrationCore.CorrectionCapMeters;

        // ---------- Body frame ----------

        [Test]
        public void BodyFrame_OriginSitsOnTheFloorUnderTheHead()
        {
            Vector3 head = new Vector3(1.25f, 1.68f, -0.4f);
            bool ok = BasisContinuousCalibrationCore.TryComputeBodyFrame(head, Quaternion.Euler(10f, 35f, 0f), out Vector3 origin, out _);
            Assert.That(ok, Is.True);
            Assert.That(origin.x, Is.EqualTo(head.x).Within(1e-5f));
            Assert.That(origin.y, Is.EqualTo(0f).Within(1e-5f), "the frame origin must sit on the playspace floor.");
            Assert.That(origin.z, Is.EqualTo(head.z).Within(1e-5f));
        }

        [Test]
        public void BodyFrame_RelativePose_IsInvariantUnderPlayspaceYawAndTranslation()
        {
            Vector3 headPos = new Vector3(0.3f, 1.65f, 0.1f);
            Quaternion headRot = Quaternion.Euler(12f, 40f, 3f);
            Vector3 trackerPos = new Vector3(0.15f, 0.45f, 0.35f);
            Quaternion trackerRot = Quaternion.Euler(70f, 10f, -20f);

            Assert.That(BasisContinuousCalibrationCore.TryComputeBodyFrame(headPos, headRot, out Vector3 o1, out Quaternion r1), Is.True);
            BasisContinuousCalibrationCore.ToBodyLocal(o1, r1, trackerPos, trackerRot, out Vector3 rel1, out Quaternion relRot1);

            // Rigidly re-stage the whole body elsewhere in the playspace: yaw 137° + horizontal slide.
            Quaternion spin = Quaternion.Euler(0f, 137f, 0f);
            Vector3 slide = new Vector3(-2.4f, 0f, 3.1f);
            Vector3 headPos2 = spin * headPos + slide;
            Quaternion headRot2 = spin * headRot;
            Vector3 trackerPos2 = spin * trackerPos + slide;
            Quaternion trackerRot2 = spin * trackerRot;

            Assert.That(BasisContinuousCalibrationCore.TryComputeBodyFrame(headPos2, headRot2, out Vector3 o2, out Quaternion r2), Is.True);
            BasisContinuousCalibrationCore.ToBodyLocal(o2, r2, trackerPos2, trackerRot2, out Vector3 rel2, out Quaternion relRot2);

            Assert.That((rel1 - rel2).magnitude, Is.LessThan(1e-4f),
                "the body-local position must not depend on where the player stands or faces in the playspace.");
            Assert.That(Quaternion.Angle(relRot1, relRot2), Is.LessThan(0.01f),
                "the body-local rotation must not depend on where the player stands or faces in the playspace.");
        }

        [Test]
        public void BodyFrame_FailsWhenTheHeadPitchesPastTheYawLimit()
        {
            Vector3 head = new Vector3(0f, 1.6f, 0f);
            Assert.That(BasisContinuousCalibrationCore.TryComputeBodyFrame(head, Quaternion.Euler(90f, 0f, 0f), out _, out _), Is.False,
                "looking straight down leaves no usable yaw — the frame must refuse.");
            Assert.That(BasisContinuousCalibrationCore.TryComputeBodyFrame(head, Quaternion.Euler(45f, 20f, 0f), out _, out _), Is.True,
                "a moderate look-down must still produce a frame.");
        }

        [Test]
        public void BodyLocal_RoundTripsThroughTheFrame()
        {
            Assert.That(BasisContinuousCalibrationCore.TryComputeBodyFrame(new Vector3(0.4f, 1.7f, -0.2f), Quaternion.Euler(5f, -60f, 0f), out Vector3 origin, out Quaternion rotation), Is.True);
            Vector3 pos = new Vector3(-0.2f, 0.9f, 0.15f);
            BasisContinuousCalibrationCore.ToBodyLocal(origin, rotation, pos, Quaternion.identity, out Vector3 rel, out _);
            Vector3 back = BasisContinuousCalibrationCore.FromBodyLocalPosition(origin, rotation, rel);
            Assert.That((back - pos).magnitude, Is.LessThan(1e-5f));
        }

        // ---------- Gates ----------

        [Test]
        public void StandingHeight_AcceptsTheBand_AndRejectsSeated()
        {
            const float eye = 1.65f;
            // The gated quantity is the head CONTROL, which rides the avatar's eye-to-head-bone offset
            // below the raw eye. The gap here is deliberately LARGER than the ±6% band: referencing the
            // ritual head snapshot cancels it, where the old eye-referenced gate failed exactly this
            // case (standing perfectly still never passed).
            const float boneGap = 0.12f;
            const float calibratedHead = eye - boneGap;
            float halfBand = eye * BasisContinuousCalibrationCore.HeadHeightBandFraction;

            Assert.That(BasisContinuousCalibrationCore.IsStandingHeight(calibratedHead, calibratedHead, eye), Is.True,
                "standing exactly at the calibrated height must pass regardless of the eye-to-head-bone gap.");
            Assert.That(BasisContinuousCalibrationCore.IsStandingHeight(calibratedHead + halfBand * 0.9f, calibratedHead, eye), Is.True);
            Assert.That(BasisContinuousCalibrationCore.IsStandingHeight(calibratedHead - halfBand * 0.9f, calibratedHead, eye), Is.True);
            Assert.That(BasisContinuousCalibrationCore.IsStandingHeight(calibratedHead + halfBand * 1.1f, calibratedHead, eye), Is.False,
                "outside the band must not count as the calibration stance.");
            Assert.That(BasisContinuousCalibrationCore.IsStandingHeight(eye * 0.8f - boneGap, calibratedHead, eye), Is.False,
                "a seated head must never pass the standing gate.");
            Assert.That(BasisContinuousCalibrationCore.IsStandingHeight(1.2f, 1.2f, 0f), Is.False,
                "a degenerate eye height must never pass.");
        }

        // ---------- Step fraction ----------

        [Test]
        public void StepFraction_IsZeroAtZeroDt_AndApproachesOne()
        {
            const float tau = BasisContinuousCalibrationCore.CorrectionTauSeconds;
            Assert.That(BasisContinuousCalibrationCore.StepFraction(0f, tau), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(BasisContinuousCalibrationCore.StepFraction(tau * 10f, tau), Is.EqualTo(1f).Within(1e-3f));
            Assert.That(BasisContinuousCalibrationCore.StepFraction(0.011f, tau), Is.GreaterThan(BasisContinuousCalibrationCore.StepFraction(0.010f, tau)),
                "a longer frame must close more of the error.");
            Assert.That(BasisContinuousCalibrationCore.StepFraction(0.5f, 0f), Is.EqualTo(1f).Within(1e-5f),
                "a degenerate tau collapses to an immediate step.");
        }

        // ---------- Capped blend ----------

        [Test]
        public void BlendWithCap_StepsTowardTheTarget_AndReportsTheStep()
        {
            Vector3 home = Vector3.zero;
            Vector3 stored = Vector3.zero;
            Vector3 target = new Vector3(0.02f, 0f, 0f);
            float step = BasisContinuousCalibrationCore.BlendWithCap(home, stored, target, 0.25f, Cap, out Vector3 result);
            Assert.That(result.x, Is.EqualTo(0.005f).Within(1e-6f));
            Assert.That(step, Is.EqualTo((result - stored).magnitude).Within(1e-6f));
        }

        [Test]
        public void BlendWithCap_ConvergesToTargetsInsideTheCap()
        {
            Vector3 home = new Vector3(0.1f, 0.4f, 0f);
            Vector3 target = home + new Vector3(0.03f, -0.01f, 0.02f);
            Vector3 stored = home;
            for (int i = 0; i < 400; i++)
            {
                BasisContinuousCalibrationCore.BlendWithCap(home, stored, target, 0.05f, Cap, out stored);
            }
            Assert.That((stored - target).magnitude, Is.LessThan(1e-4f),
                "repeated steps must fully absorb a drift inside the correction budget.");
        }

        [Test]
        public void BlendWithCap_NeverCarriesTheSnapshotPastTheCap()
        {
            Vector3 home = Vector3.zero;
            Vector3 target = new Vector3(0.5f, 0f, 0f);
            Vector3 stored = Vector3.zero;
            for (int i = 0; i < 400; i++)
            {
                BasisContinuousCalibrationCore.BlendWithCap(home, stored, target, 0.2f, Cap, out stored);
                Assert.That((stored - home).magnitude, Is.LessThanOrEqualTo(Cap + 1e-5f),
                    "no sequence of steps may walk the snapshot beyond the correction budget.");
            }
            Assert.That((stored - home).magnitude, Is.EqualTo(Cap).Within(1e-4f),
                "a large drift should saturate at the cap, partially corrected.");
        }

        [Test]
        public void BlendWithCap_PullsAStoredValueBackInsideTheCap()
        {
            Vector3 home = Vector3.zero;
            Vector3 stored = new Vector3(Cap * 2f, 0f, 0f);
            BasisContinuousCalibrationCore.BlendWithCap(home, stored, stored, 0f, Cap, out Vector3 result);
            Assert.That((result - home).magnitude, Is.LessThanOrEqualTo(Cap + 1e-5f),
                "even a zero-fraction step must re-clamp a value that sits outside the budget.");
        }

        // ---------- Threshold ordering ----------

        [Test]
        public void Thresholds_KeepTheGateFlagCapOrdering()
        {
            Assert.That(BasisContinuousCalibrationCore.FlagPositionMeters, Is.GreaterThan(BasisContinuousCalibrationCore.CorrectionCapMeters),
                "the recalibrate flag must fire beyond what the auto-correction is allowed to absorb.");
            Assert.That(BasisContinuousCalibrationCore.PoseGateMeters, Is.GreaterThan(BasisContinuousCalibrationCore.FlagPositionMeters),
                "flagged drift must still be inside the in-pose gate, or it could never be observed.");
            Assert.That(BasisContinuousCalibrationCore.SettledResidualMeters, Is.LessThan(BasisContinuousCalibrationCore.CorrectionCapMeters));
        }
    }
}
